using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Messaging;
using Neo.L2.Persistence;
using Neo.L2.State;
using Neo.Plugins.L2;
using Neo.Plugins.L2Rpc;

namespace Neo.L2.SoftSeal.TestSupport;

public static class SoftSealMultiCycle
{
    /// <summary>Highest soft multi-batch cycle retained by current SoftSeal completeness tests.</summary>
    public const int TargetCycle = 23;

    public const uint ChainId = 20260716u;

    /// <summary>Host surface used by SoftSeal multi-cycle asserts (Multisig/Optimistic/E2E).</summary>
    public sealed class HostSurface
    {
        public required Func<DAPublishRequest, Task<DAReceipt>> PublishDaAsync { get; init; }
        public required Func<DAReceipt, Task<bool>> IsDaAvailableAsync { get; init; }
        public required Func<bool> SupportsLocalDaReader { get; init; }
        public required Func<IDAReader> CreateLocalDaReader { get; init; }
        public required Func<CrossChainMessage, MintInstruction> ProcessDeposit { get; init; }
        public required Func<uint, ulong, bool> HasConsumedDeposit { get; init; }
        public required Func<int> ConsumedDepositCount { get; init; }
        public required Action<DepositStatus> RecordRpcDeposit { get; init; }
        public required Func<uint, ulong, DepositStatus?> GetRpcL1DepositStatus { get; init; }
        public required Func<Task<int>> ScanSharedBridgeDepositsAsync { get; init; }
        public required Func<WithdrawalRequest, UInt256> StageWithdrawal { get; init; }
        public required Func<int> StagedWithdrawalCount { get; init; }
        public required Func<(UInt256 Root, WithdrawalTree Tree)> SealWithdrawalBatch { get; init; }
        public required Func<IReadOnlyList<CrossChainMessage>, Task> EnqueueOutbound { get; init; }
        public required Func<int> MessageOutboxL2ToL1Count { get; init; }
        public required Func<UInt256> MessageOutboxL2ToL1Root { get; init; }
        public required Func<ulong, bool> RegisterForcedInclusionNonce { get; init; }
        public required Func<int> KnownForcedInclusionNonceCount { get; init; }
        public required Func<bool> HasOverdueForcedInclusionCached { get; init; }
        public required Action InvalidateForcedInclusionCache { get; init; }
        public required Func<int> OpenBatchForcedInclusionCount { get; init; }
        public required Func<ulong, bool> RegisterInboundMessageNonce { get; init; }
        public required Func<int> KnownInboundNonceCount { get; init; }
        public required Action InvalidateInboundMessageCache { get; init; }
        public required Func<int> OpenBatchL1MessageCount { get; init; }
        public required Func<int> L1InboxPendingCount { get; init; }
        public required Action<UInt256, byte[]> RecordRpcWithdrawalProof { get; init; }
        public required Func<UInt256, ReadOnlyMemory<byte>?> GetRpcWithdrawalProof { get; init; }
        public required Action<UInt256, byte[]> RecordRpcMessageProof { get; init; }
        public required Func<UInt256, ReadOnlyMemory<byte>?> GetRpcMessageProof { get; init; }
        public required Action<UInt256, ReadOnlyMemory<byte>> RecordMessageRouterFinalizedProof { get; init; }
        public required Func<UInt256, Task<ReadOnlyMemory<byte>?>> GetMessageRouterProofAsync { get; init; }
        public required Func<Task> ReconcileAsync { get; init; }
        public required Func<Task> SubmitNextAsync { get; init; }
        public required Func<ulong, UInt256, Task> RecoverPoisonedBatchAsync { get; init; }
        public required Func<Task<bool>> IsSettlementPoisonedAsync { get; init; }
        public required Func<int> GetPendingCount { get; init; }
        public required Func<LocalHostOperatorStatus> GetOperatorStatus { get; init; }
        public required Func<LocalHostHealthProbeDocument> GetHealthProbe { get; init; }
        public required Func<string> FormatOperatorStatusJson { get; init; }
        public required Func<string> FormatHealthProbeJson { get; init; }
        public required Func<string> ExportPrometheusMetrics { get; init; }
        public required Func<string, Task> WritePrometheusMetricsAsync { get; init; }
        public required Func<string, Task> WriteOperatorStatusAsync { get; init; }
        public required Func<string, Task> WriteHealthProbeAsync { get; init; }
        public required Func<ulong, L2BatchCommitment?> GetRpcBatch { get; init; }
        public required Func<ulong, BatchStatus> GetRpcBatchStatus { get; init; }
        public required Func<ulong, UInt256> GetRpcStateRootAtBatch { get; init; }
        public required Func<UInt256> GetLatestRpcStateRoot { get; init; }
        public required UInt256 ExpectedPostStateRoot { get; init; }
    }

    /// <summary>
    /// Run soft multi-batch cycles <paramref name="fromN"/>..Target (inclusive):
    /// for each n: DA+deposit n → outbound/FI n → poison→recover at n.
    /// </summary>
    public static void RunCycles(HostSurface host, string chainDir, int fromN, int toN = TargetCycle)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrEmpty(chainDir);
        if (fromN < 2)
            throw new ArgumentOutOfRangeException(nameof(fromN), fromN, "multi-cycle SoftSeal starts at n=2");
        if (toN < fromN)
            throw new ArgumentOutOfRangeException(nameof(toN), toN, "toN must be >= fromN");

        for (var n = fromN; n <= toN; n++)
        {
            AfterRecoverDaAndDeposit(host, n, chainDir);
            var (leaf, msgHash) = OutboundAndFi(host, n, chainDir);
            PoisonRecoverRetention(host, n, leaf, msgHash, chainDir);
        }
    }

    /// <summary>After poison-(n-1) recover: re-publish local DA for batches 1+2 and process deposit n.</summary>
    public static void AfterRecoverDaAndDeposit(HostSurface host, int n, string chainDir)
    {
        Assert.IsTrue(n >= 2);
        Assert.IsTrue(host.SupportsLocalDaReader());

        var da1Payload = new byte[] { 0xDA, 0xA1, 0x01 };
        var da1 = host.PublishDaAsync(new DAPublishRequest
        {
            ChainId = ChainId,
            BatchNumber = 1,
            Payload = da1Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da1.Layer);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, da1.Kind);
        Assert.IsTrue(host.IsDaAvailableAsync(da1).GetAwaiter().GetResult());
        var da1Read = host.CreateLocalDaReader().ReadAsync(da1).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da1Read is { Length: 3 });
        CollectionAssert.AreEqual(da1Payload, da1Read!.Value.ToArray());

        var da2Payload = new byte[] { 0xDA, 0xA2, 0x02 };
        var da2 = host.PublishDaAsync(new DAPublishRequest
        {
            ChainId = ChainId,
            BatchNumber = 2,
            Payload = da2Payload,
        }).GetAwaiter().GetResult();
        Assert.AreEqual(DAMode.Local, da2.Layer);
        Assert.IsTrue(host.IsDaAvailableAsync(da2).GetAwaiter().GetResult());
        var da2Read = host.CreateLocalDaReader().ReadAsync(da2).AsTask().GetAwaiter().GetResult();
        Assert.IsTrue(da2Read is { Length: 3 });
        CollectionAssert.AreEqual(da2Payload, da2Read!.Value.ToArray());

        var softL1Asset = UInt160.Parse("0x" + new string('1', 40));
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softDepositMsg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = ChainId,
            Nonce = (ulong)n,
            Sender = Account(0x66),
            Receiver = Account(0x55),
            MessageType = MessageType.Deposit,
            Payload = new DepositPayload
            {
                L1Asset = softL1Asset,
                L2Recipient = Account(0x55),
                Amount = new BigInteger(n * 1_000),
            }.Encode(),
            MessageHash = UInt256.Zero,
        };
        var softMint = host.ProcessDeposit(softDepositMsg);
        Assert.AreEqual(softL2Asset, softMint.L2Asset);

        for (ulong nonce = 1; nonce <= (ulong)n; nonce++)
            Assert.IsTrue(host.HasConsumedDeposit(0, nonce), $"expected consumed deposit nonce {nonce}");
        Assert.AreEqual(n, host.ConsumedDepositCount());

        host.RecordRpcDeposit(new DepositStatus(0, (ulong)n, ConsumedOnL2: true, IncludedInBatch: 2));
        Assert.IsTrue(host.GetRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(host.GetRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.IsTrue(host.GetRpcL1DepositStatus(0, (ulong)n) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(0, host.ScanSharedBridgeDepositsAsync().GetAwaiter().GetResult());

        var prior = n - 1;
        var status = host.GetOperatorStatus();
        Assert.AreEqual(n, status.ConsumedDepositCount);
        Assert.AreEqual(prior, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(prior, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(prior, status.KnownInboundNonceCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));

        var probe = host.GetHealthProbe();
        Assert.AreEqual(n, probe.ConsumedDepositCount);
        Assert.AreEqual(prior, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);

        var statusJson = host.FormatOperatorStatusJson();
        StringAssert.Contains(statusJson, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(statusJson, $"\"messageOutboxL2ToL1Count\": {prior}");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");

        var prom = host.ExportPrometheusMetrics();
        Assert.IsFalse(string.IsNullOrWhiteSpace(prom));
        var promPath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-host.prom");
        host.WritePrometheusMetricsAsync(promPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(promPath));
        Assert.AreEqual(prom, File.ReadAllText(promPath));

        var statusPath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-status.json");
        host.WriteOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        StringAssert.Contains(File.ReadAllText(statusPath), $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(File.ReadAllText(statusPath), "\"isSettlementRetrying\": true");

        var probePath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-probe.json");
        host.WriteHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), $"\"consumedDepositCount\": {n}");

        var durablePath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-da-deposit.json");
        File.WriteAllText(durablePath, $$"""
            {
              "cycle": {{n}},
              "daBatch1Layer": "{{da1.Layer}}",
              "daBatch2Layer": "{{da2.Layer}}",
              "daBatch1Available": true,
              "daBatch2Available": true,
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "depositNonceIncludedInBatch": 2,
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durable = File.ReadAllText(durablePath);
        StringAssert.Contains(durable, "\"daBatch1Layer\": \"Local\"");
        StringAssert.Contains(durable, "\"daBatch2Layer\": \"Local\"");
        StringAssert.Contains(durable, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(durable, $"\"messageOutboxL2ToL1Count\": {prior}");
        StringAssert.Contains(durable, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durable, "\"isOfflinePassportComplete\": true");
    }

    /// <summary>Offline withdrawal seal + L2→L1 outbox + FI/inbound for cycle n; returns leaf/hash for poison pin.</summary>
    public static (UInt256 WithdrawalLeaf, UInt256 OutboundMessageHash) OutboundAndFi(
        HostSurface host, int n, string chainDir)
    {
        Assert.IsTrue(n >= 2);
        var softL2Asset = UInt160.Parse("0x" + new string('2', 40));
        var softSender = Account((byte)(0xB0 + (n & 0x0F)));
        var wdLeaf = host.StageWithdrawal(new WithdrawalRequest
        {
            ChainId = ChainId,
            EmittingContract = softSender,
            L2Sender = softSender,
            L1Recipient = softSender,
            L2Asset = softL2Asset,
            Amount = new BigInteger(100),
            Nonce = (ulong)n,
        });
        Assert.AreNotEqual(UInt256.Zero, wdLeaf);
        Assert.IsTrue(host.StagedWithdrawalCount() >= 1);
        var sealedWd = host.SealWithdrawalBatch();
        Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
        Assert.AreEqual(0, host.StagedWithdrawalCount());
        Assert.IsTrue(sealedWd.Tree.Count >= 1);
        var merkleProof = sealedWd.Tree.GetProof(sealedWd.Tree.Count - 1);
        Assert.AreEqual(wdLeaf, merkleProof.Leaf);
        var proofBytes = MerkleProofSerializer.Encode(merkleProof);
        Assert.IsTrue(proofBytes.Length >= MerkleProofSerializer.HeaderSize);
        host.RecordRpcWithdrawalProof(wdLeaf, proofBytes);
        CollectionAssert.AreEqual(proofBytes, host.GetRpcWithdrawalProof(wdLeaf)!.Value.ToArray());

        var outboundNonce = (ulong)(2 * n);
        var outboundDraft = new CrossChainMessage
        {
            SourceChainId = ChainId,
            TargetChainId = 0,
            Nonce = outboundNonce,
            Sender = softSender,
            Receiver = softSender,
            MessageType = MessageType.Event,
            Payload = new byte[] { (byte)n },
            MessageHash = UInt256.Zero,
        };
        var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
        host.EnqueueOutbound([outbound]).GetAwaiter().GetResult();
        Assert.AreEqual(n, host.MessageOutboxL2ToL1Count());
        Assert.AreNotEqual(UInt256.Zero, host.MessageOutboxL2ToL1Root());

        var messageProofBytes = outbound.MessageHash.GetSpan().ToArray();
        host.RecordRpcMessageProof(outbound.MessageHash, messageProofBytes);
        CollectionAssert.AreEqual(messageProofBytes, host.GetRpcMessageProof(outbound.MessageHash)!.Value.ToArray());
        host.RecordMessageRouterFinalizedProof(outbound.MessageHash, messageProofBytes);
        CollectionAssert.AreEqual(
            messageProofBytes,
            host.GetMessageRouterProofAsync(outbound.MessageHash).GetAwaiter().GetResult()!.Value.ToArray());

        var fiNonce = (ulong)(2 * n + 2);
        Assert.IsTrue(host.RegisterForcedInclusionNonce(fiNonce));
        Assert.IsFalse(host.RegisterForcedInclusionNonce(fiNonce));
        Assert.AreEqual(n, host.KnownForcedInclusionNonceCount());
        Assert.AreEqual(0, host.OpenBatchForcedInclusionCount());
        Assert.IsFalse(host.HasOverdueForcedInclusionCached());
        host.InvalidateForcedInclusionCache();
        Assert.AreEqual(n, host.KnownForcedInclusionNonceCount());

        Assert.IsTrue(host.RegisterInboundMessageNonce(fiNonce));
        Assert.IsFalse(host.RegisterInboundMessageNonce(fiNonce));
        Assert.AreEqual(n, host.KnownInboundNonceCount());
        Assert.AreEqual(0, host.OpenBatchL1MessageCount());
        Assert.AreEqual(0, host.L1InboxPendingCount());
        host.InvalidateInboundMessageCache();
        Assert.AreEqual(n, host.KnownInboundNonceCount());

        var status = host.GetOperatorStatus();
        Assert.AreEqual(n, status.ConsumedDepositCount);
        Assert.AreEqual(n, status.MessageOutboxL2ToL1Count);
        Assert.AreEqual(0, status.StagedWithdrawalCount);
        Assert.AreEqual(n, status.KnownForcedInclusionNonceCount);
        Assert.AreEqual(n, status.KnownInboundNonceCount);
        Assert.IsFalse(status.HasOverdueForcedInclusion);
        Assert.AreEqual(0, status.OpenBatchForcedInclusionCount);
        Assert.AreEqual(0, status.OpenBatchL1MessageCount);
        Assert.AreEqual(2UL, status.LatestCheckpointBatchNumber);
        Assert.IsTrue(status.PendingSettlementCount >= 2);
        Assert.IsTrue(status.IsSettlementRetrying);
        Assert.IsFalse(status.IsSettlementPoisoned);
        Assert.IsFalse(status.IsSettlementIdle);
        Assert.IsTrue(status.IsOfflinePassportComplete);
        Assert.IsTrue(status.IsOperatorReady);
        Assert.IsTrue(status.IsBatcherCheckpointAligned);
        Assert.IsFalse(status.IsPipelineHealthy);
        CollectionAssert.Contains(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.IsSettlementRetrying));
        CollectionAssert.DoesNotContain(
            status.PipelineHealthFailures.ToArray(),
            nameof(status.HasOverdueForcedInclusion));

        var probe = host.GetHealthProbe();
        Assert.AreEqual(n, probe.ConsumedDepositCount);
        Assert.AreEqual(n, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(n, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(n, probe.KnownInboundNonceCount);
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);

        var statusJson = host.FormatOperatorStatusJson();
        StringAssert.Contains(statusJson, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(statusJson, $"\"messageOutboxL2ToL1Count\": {n}");
        StringAssert.Contains(statusJson, $"\"knownForcedInclusionNonceCount\": {n}");
        StringAssert.Contains(statusJson, $"\"knownInboundNonceCount\": {n}");
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");

        var probeJson = host.FormatHealthProbeJson();
        StringAssert.Contains(probeJson, $"\"messageOutboxL2ToL1Count\": {n}");
        StringAssert.Contains(probeJson, $"\"knownForcedInclusionNonceCount\": {n}");
        StringAssert.Contains(probeJson, $"\"knownInboundNonceCount\": {n}");
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");

        var statusPath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-outbound-status.json");
        host.WriteOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, $"\"messageOutboxL2ToL1Count\": {n}");
        StringAssert.Contains(statusFile, $"\"knownForcedInclusionNonceCount\": {n}");
        StringAssert.Contains(statusFile, $"\"knownInboundNonceCount\": {n}");
        StringAssert.Contains(statusFile, $"\"consumedDepositCount\": {n}");

        var probePath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-outbound-probe.json");
        host.WriteHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), $"\"messageOutboxL2ToL1Count\": {n}");

        var durablePath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-outbound.json");
        File.WriteAllText(durablePath, $$"""
            {
              "cycle": {{n}},
              "withdrawalNonce": {{n}},
              "outboundNonce": {{outboundNonce}},
              "consumedDepositCount": {{status.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{status.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{status.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{status.KnownInboundNonceCount}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "withdrawalRoot": "{{sealedWd.Root}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "messageProofBytes": {{messageProofBytes.Length}},
              "latestCheckpointBatchNumber": {{status.LatestCheckpointBatchNumber}},
              "pendingSettlementCount": {{status.PendingSettlementCount}},
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durable = File.ReadAllText(durablePath);
        StringAssert.Contains(durable, $"\"messageOutboxL2ToL1Count\": {n}");
        StringAssert.Contains(durable, $"\"knownForcedInclusionNonceCount\": {n}");
        StringAssert.Contains(durable, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(durable, "\"isSettlementRetrying\": true");

        var rpcPath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-outbound-rpc.json");
        File.WriteAllText(rpcPath, $$"""
            {
              "cycle": {{n}},
              "withdrawalLeaf": "{{wdLeaf}}",
              "outboundMessageHash": "{{outbound.MessageHash}}",
              "withdrawalProofBytes": {{proofBytes.Length}},
              "messageProofBytes": {{messageProofBytes.Length}},
              "messageOutboxL2ToL1Count": {{n}},
              "knownForcedInclusionNonceCount": {{n}},
              "knownInboundNonceCount": {{n}},
              "consumedDepositCount": {{n}},
              "isSettlementRetrying": true
            }
            """);
        Assert.IsTrue(File.Exists(rpcPath));
        var rpc = File.ReadAllText(rpcPath);
        StringAssert.Contains(rpc, "\"withdrawalLeaf\": \"" + wdLeaf + "\"");
        StringAssert.Contains(rpc, "\"outboundMessageHash\": \"" + outbound.MessageHash + "\"");
        StringAssert.Contains(rpc, $"\"messageOutboxL2ToL1Count\": {n}");
        return (wdLeaf, outbound.MessageHash);
    }

    /// <summary>Re-escalate mock L1 until Poisoned then Recover; soft multi-state at count n must remain.</summary>
    public static void PoisonRecoverRetention(
        HostSurface host,
        int n,
        UInt256 withdrawalLeaf,
        UInt256 outboundMessageHash,
        string chainDir)
    {
        Assert.IsTrue(n >= 2);
        var before = host.GetOperatorStatus();
        Assert.IsTrue(before.IsSettlementRetrying);
        Assert.IsFalse(before.IsSettlementPoisoned);
        Assert.IsTrue(before.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, before.LatestCheckpointBatchNumber);
        Assert.AreEqual(n, before.ConsumedDepositCount);
        Assert.AreEqual(n, before.MessageOutboxL2ToL1Count);
        Assert.AreEqual(n, before.KnownForcedInclusionNonceCount);
        Assert.AreEqual(n, before.KnownInboundNonceCount);
        Assert.IsTrue(host.GetPendingCount() >= 2);

        LocalHostOperatorStatus afterPoison = before;
        for (var attempt = 0; attempt < 16; attempt++)
        {
            try
            {
                host.ReconcileAsync().GetAwaiter().GetResult();
            }
            catch (OverflowException)
            {
            }
            catch (Exception)
            {
            }

            host.SubmitNextAsync().GetAwaiter().GetResult();
            afterPoison = host.GetOperatorStatus();
            if (afterPoison.IsSettlementPoisoned)
                break;
        }

        Assert.IsTrue(afterPoison.IsSettlementPoisoned);
        Assert.IsFalse(afterPoison.IsSettlementRetrying);
        CollectionAssert.Contains(
            afterPoison.PipelineHealthFailures.ToArray(),
            nameof(afterPoison.IsSettlementPoisoned));
        Assert.IsNotNull(afterPoison.Recovery.BlockedBatchNumber);
        Assert.IsNotNull(afterPoison.Recovery.ArtifactContentHash);
        var blockedBatch = afterPoison.Recovery.BlockedBatchNumber!.Value;
        var contentHash = afterPoison.Recovery.ArtifactContentHash!;
        Assert.IsTrue(afterPoison.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterPoison.LatestCheckpointBatchNumber);
        Assert.AreEqual(n, afterPoison.ConsumedDepositCount);
        Assert.AreEqual(n, afterPoison.MessageOutboxL2ToL1Count);
        Assert.AreEqual(n, afterPoison.KnownForcedInclusionNonceCount);
        Assert.AreEqual(n, afterPoison.KnownInboundNonceCount);

        Assert.ThrowsExactly<InvalidOperationException>(
            () => host.RecoverPoisonedBatchAsync(blockedBatch, UInt256.Zero).GetAwaiter().GetResult());
        Assert.IsTrue(host.IsSettlementPoisonedAsync().GetAwaiter().GetResult());
        host.RecoverPoisonedBatchAsync(blockedBatch, contentHash).GetAwaiter().GetResult();

        var afterRecover = host.GetOperatorStatus();
        Assert.IsFalse(afterRecover.IsSettlementPoisoned);
        Assert.IsTrue(afterRecover.IsSettlementRetrying);
        Assert.AreEqual(SettlementRecoveryState.Retrying, afterRecover.Recovery.State);
        Assert.AreEqual(0, afterRecover.Recovery.RetryCount);
        Assert.IsTrue(host.GetPendingCount() >= 2);
        Assert.IsTrue(afterRecover.PendingSettlementCount >= 2);
        Assert.AreEqual(2UL, afterRecover.LatestCheckpointBatchNumber);
        Assert.AreEqual(host.ExpectedPostStateRoot, afterRecover.LatestCheckpointPostStateRoot);
        Assert.AreEqual(host.ExpectedPostStateRoot, afterRecover.LatestRpcStateRoot);
        Assert.AreEqual(n, afterRecover.ConsumedDepositCount);
        Assert.AreEqual(n, afterRecover.MessageOutboxL2ToL1Count);
        Assert.AreEqual(n, afterRecover.KnownForcedInclusionNonceCount);
        Assert.AreEqual(n, afterRecover.KnownInboundNonceCount);
        Assert.IsTrue(afterRecover.IsOfflinePassportComplete);
        Assert.IsTrue(afterRecover.IsOperatorReady);
        Assert.IsTrue(afterRecover.IsBatcherCheckpointAligned);
        Assert.IsFalse(afterRecover.IsPipelineHealthy);
        CollectionAssert.Contains(
            afterRecover.PipelineHealthFailures.ToArray(),
            nameof(afterRecover.IsSettlementRetrying));

        for (ulong nonce = 1; nonce <= (ulong)n; nonce++)
            Assert.IsTrue(host.HasConsumedDeposit(0, nonce));
        Assert.IsTrue(host.GetRpcL1DepositStatus(0, 1) is { ConsumedOnL2: true, IncludedInBatch: 1UL });
        Assert.IsTrue(host.GetRpcL1DepositStatus(0, 2) is { ConsumedOnL2: true, IncludedInBatch: 2UL });
        Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
        Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(2));
        Assert.IsNotNull(host.GetRpcBatch(1));
        Assert.IsNotNull(host.GetRpcBatch(2));
        Assert.AreEqual(host.ExpectedPostStateRoot, host.GetRpcStateRootAtBatch(1));
        Assert.AreEqual(host.ExpectedPostStateRoot, host.GetRpcStateRootAtBatch(2));
        Assert.AreEqual(host.ExpectedPostStateRoot, host.GetLatestRpcStateRoot());
        Assert.IsTrue(host.GetRpcWithdrawalProof(withdrawalLeaf) is { Length: > 0 });
        Assert.IsTrue(host.GetRpcMessageProof(outboundMessageHash) is { Length: > 0 });

        var probe = host.GetHealthProbe();
        Assert.IsTrue(probe.IsSettlementRetrying);
        Assert.IsFalse(probe.IsSettlementPoisoned);
        Assert.AreEqual(2UL, probe.LatestCheckpointBatchNumber);
        Assert.IsTrue(probe.PendingSettlementCount >= 2);
        Assert.AreEqual(n, probe.ConsumedDepositCount);
        Assert.AreEqual(n, probe.MessageOutboxL2ToL1Count);
        Assert.AreEqual(n, probe.KnownForcedInclusionNonceCount);
        Assert.AreEqual(n, probe.KnownInboundNonceCount);

        var statusJson = host.FormatOperatorStatusJson();
        StringAssert.Contains(statusJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusJson, "\"isSettlementPoisoned\": false");
        StringAssert.Contains(statusJson, "\"latestCheckpointBatchNumber\": 2");
        StringAssert.Contains(statusJson, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(statusJson, $"\"messageOutboxL2ToL1Count\": {n}");
        StringAssert.Contains(statusJson, $"\"knownForcedInclusionNonceCount\": {n}");
        StringAssert.Contains(statusJson, $"\"knownInboundNonceCount\": {n}");

        var probeJson = host.FormatHealthProbeJson();
        StringAssert.Contains(probeJson, "\"isSettlementRetrying\": true");
        StringAssert.Contains(probeJson, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(probeJson, $"\"messageOutboxL2ToL1Count\": {n}");

        var statusPath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-poison-recover-status.json");
        host.WriteOperatorStatusAsync(statusPath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(statusPath));
        var statusFile = File.ReadAllText(statusPath);
        StringAssert.Contains(statusFile, "\"isSettlementRetrying\": true");
        StringAssert.Contains(statusFile, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(statusFile, $"\"messageOutboxL2ToL1Count\": {n}");
        StringAssert.Contains(statusFile, "\"latestCheckpointBatchNumber\": 2");

        var probePath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-poison-recover-probe.json");
        host.WriteHealthProbeAsync(probePath).GetAwaiter().GetResult();
        Assert.IsTrue(File.Exists(probePath));
        StringAssert.Contains(File.ReadAllText(probePath), $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(File.ReadAllText(probePath), "\"isSettlementRetrying\": true");

        var durablePath = Path.Combine(chainDir, $"soft-seal-cycle-{n}-poison-recover.json");
        File.WriteAllText(durablePath, $$"""
            {
              "cycle": {{n}},
              "poisonBlockedBatch": {{blockedBatch}},
              "pendingSettlementCount": {{afterRecover.PendingSettlementCount}},
              "latestCheckpointBatchNumber": {{afterRecover.LatestCheckpointBatchNumber}},
              "consumedDepositCount": {{afterRecover.ConsumedDepositCount}},
              "messageOutboxL2ToL1Count": {{afterRecover.MessageOutboxL2ToL1Count}},
              "knownForcedInclusionNonceCount": {{afterRecover.KnownForcedInclusionNonceCount}},
              "knownInboundNonceCount": {{afterRecover.KnownInboundNonceCount}},
              "rpcBatch1Status": "{{host.GetRpcBatchStatus(1)}}",
              "rpcBatch2Status": "{{host.GetRpcBatchStatus(2)}}",
              "withdrawalProofPresent": true,
              "messageProofPresent": true,
              "isSettlementRetrying": true,
              "isSettlementPoisoned": false,
              "isOfflinePassportComplete": true
            }
            """);
        Assert.IsTrue(File.Exists(durablePath));
        var durable = File.ReadAllText(durablePath);
        StringAssert.Contains(durable, "\"rpcBatch1Status\": \"Finalized\"");
        StringAssert.Contains(durable, "\"rpcBatch2Status\": \"Finalized\"");
        StringAssert.Contains(durable, $"\"consumedDepositCount\": {n}");
        StringAssert.Contains(durable, $"\"messageOutboxL2ToL1Count\": {n}");
        StringAssert.Contains(durable, $"\"knownForcedInclusionNonceCount\": {n}");
        StringAssert.Contains(durable, "\"isSettlementRetrying\": true");
        StringAssert.Contains(durable, "\"isSettlementPoisoned\": false");
    }

    private static UInt160 Account(byte fill)
    {
        var bytes = new byte[20];
        Array.Fill(bytes, fill);
        return new UInt160(bytes);
    }
}
