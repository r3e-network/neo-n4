using System.Net;
using System.Numerics;
using System.Text;
using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.State;
using Neo.Network.P2P.Payloads;
using Neo.Plugins.L2Rpc;
using Neo.Wallets;

namespace Neo.Plugins.L2Settlement.UnitTests;

/// <summary>Host composition tests for <see cref="MultisigLocalHostComposition"/>.</summary>
[TestClass]
public sealed class UT_MultisigLocalHostComposition
{
    [TestMethod]
    public async Task Open_FromDeployReport_WiresMultisigLocalStack()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeMultisigChain(chainDir, reportPath);
            using var http = CanonicalRootHttpClient();
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                SampleSigners(),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);

            Assert.AreEqual(Path.GetFullPath(chainDir), host.ChainDirectory);
            Assert.IsNotNull(host.Batch);
            Assert.IsNotNull(host.Settlement);
            Assert.IsNotNull(host.Layout.ProofWitness);
            Assert.AreEqual(ProofType.Multisig, host.Prover.Kind);
            Assert.IsInstanceOfType(host.Prover.Prover, typeof(AttestationProver));
            Assert.AreEqual(DAMode.Local, host.DaWriter.Mode);
            Assert.AreEqual(20260716u, host.ForcedInclusion.ChainId);
            Assert.IsNotNull(host.Settlement.ProductionDepositSource);
            Assert.IsNotNull(host.Settlement.ProductionMessageRouter);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionSource);
            Assert.IsNotNull(host.Settlement.ProductionForcedInclusionFinalizer);
            Assert.IsNotNull(host.Settlement.ProductionSettlementClient);
            Assert.AreSame(host.ForcedInclusion, host.Settlement.ProductionForcedInclusionSource);
            // Host-level production surfaces (no dig into Settlement internals).
            Assert.AreSame(host.Settlement.ProductionDepositSource, host.DepositSource);
            Assert.AreSame(host.Settlement.ProductionMessageRouter, host.MessageRouter);
            Assert.AreSame(
                host.Settlement.ProductionForcedInclusionFinalizer,
                host.ForcedInclusionFinalizer);
            Assert.AreSame(host.Settlement.ProductionSettlementClient, host.SettlementClient);
            Assert.AreSame(host.Settlement.ProductionTransactionSender, host.TransactionSender);
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
            Assert.AreEqual(20260716u, host.Bridge.ChainId);
            Assert.AreSame(
                host.Settlement.ProductionDepositSource,
                host.Bridge.DepositSource);
            // WireProduction installs the L1 inbox + sealed-batch sink on the batcher.
            Assert.AreSame(host.Settlement.ProductionDepositSource, host.Batch.DepositSource);
            Assert.AreSame(host.Settlement.ProductionMessageRouter, host.Batch.MessageRouter);
            Assert.AreSame(host.ForcedInclusion, host.Batch.ForcedInclusionSource);
            Assert.IsTrue(host.Batch.HasSealedBatchSink);
            Assert.IsTrue(host.HasSealedBatchSink);
            Assert.AreEqual(1UL, host.NextExpectedBlock);
            Assert.AreEqual(host.Batch.NextExpectedBlock, host.NextExpectedBlock);
            Assert.AreEqual(0UL, host.LastAcknowledgedBatchNumber);
            Assert.AreEqual(0UL, host.LastAcknowledgedBlock);
            Assert.AreEqual(1UL, host.NextBatchNumber);
            Assert.IsFalse(host.HasPendingSealedBatch);
            Assert.IsNull(host.PendingSealedBatch);
            Assert.IsNull(host.PendingSealedBatchNumber);
            Assert.IsTrue(host.IsBatcherEnabled);
            Assert.IsTrue(host.MaxBlocksPerBatch > 0);
            Assert.IsTrue(host.MaxTransactionsPerBatch > 0);
            Assert.IsTrue(host.MaxBatchAgeMillis > 0);
            Assert.IsFalse(host.Batch.HasPendingSealedBatch);
            Assert.IsFalse(host.HasOpenBatch);
            Assert.AreEqual(0, host.InProgressTxCount);
            Assert.IsNull(host.OpenBatchFirstBlock);
            Assert.IsNull(host.OpenBatchLastBlock);
            Assert.AreEqual(0, host.OpenBatchBlockCount);
            Assert.AreEqual(0, host.OpenBatchL1MessageCount);
            Assert.AreEqual(0, host.OpenBatchL2ToL1MessageCount);
            Assert.AreEqual(0, host.ConsumedDepositCount);
            Assert.IsFalse(host.TryRetryPendingSealedBatch());
            Assert.IsTrue(host.RegisterInboundMessageNonce(7));
            host.InvalidateInboundMessageCache();
            // ProcessCommittedBlock is public on L2BatchPlugin/LocalHost; first-block drain hits
            // L1 FI/deposit scanners (funded). Full hand-off covered by L2BatchPlugin unit tests.
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsTrue(host.IsProductionWired);
            Assert.IsTrue(host.IsOperatorReady);
            Assert.AreEqual(20260716u, host.ChainId);
            Assert.AreEqual(ProofType.Multisig, host.ProofType);
            Assert.AreEqual(DAMode.Local, host.DaMode);
            Assert.AreEqual(0, host.PeekSharedBridgeDeposits(8).Count);
            var status = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(20260716u, status.ChainId);
            Assert.AreEqual(ProofType.Multisig, status.ProofType);
            Assert.AreEqual(DAMode.Local, status.DaMode);
            Assert.AreEqual(host.RpcStore.SecurityLevel, status.SecurityLevel);
            Assert.IsTrue(status.IsOperatorReady);
            Assert.IsTrue(status.IsProductionWired);
            Assert.IsTrue(status.HasSealedBatchSink);
            Assert.AreEqual(1UL, status.NextExpectedBlock);
            Assert.IsFalse(status.HasPendingSealedBatch);
            Assert.IsFalse(status.HasOpenBatch);
            Assert.AreEqual(0, status.InProgressTxCount);
            Assert.IsNull(status.OpenBatchFirstBlock);
            Assert.AreEqual(0, status.OpenBatchBlockCount);
            Assert.IsTrue(status.HasDepositSource);
            Assert.IsTrue(status.HasMessageRouter);
            Assert.IsFalse(status.IsMetricsHttpListening);
            Assert.AreEqual(0, status.PendingSettlementCount);
            Assert.AreEqual(0, status.ReadyDepositCount);
            Assert.AreEqual(0, status.TrackedForcedInclusionNonceCount);
            Assert.AreEqual(0, status.Recovery.PendingCount);
            Assert.AreEqual(host.GetLatestRpcStateRoot(), status.LatestRpcStateRoot);
            // Host RPC store helpers (no Neo.CLI).
            var root = new UInt256(Enumerable.Repeat((byte)0xAB, 32).ToArray());
            var z = UInt256.Zero;
            var batch = new L2BatchCommitment
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                FirstBlock = 1,
                LastBlock = 1,
                PreStateRoot = z,
                PostStateRoot = root,
                TxRoot = z,
                ReceiptRoot = z,
                WithdrawalRoot = z,
                L2ToL1MessageRoot = z,
                L2ToL2MessageRoot = z,
                DACommitment = z,
                PublicInputHash = z,
                ProofType = ProofType.Multisig,
                Proof = ReadOnlyMemory<byte>.Empty,
            };
            host.AddRpcBatch(batch, BatchStatus.Pending);
            Assert.AreEqual(BatchStatus.Pending, host.GetRpcBatchStatus(1));
            Assert.IsNotNull(host.GetRpcBatch(1));
            host.FinalizeRpcBatch(1);
            Assert.AreEqual(BatchStatus.Finalized, host.GetRpcBatchStatus(1));
            Assert.AreEqual(root, host.GetLatestRpcStateRoot());
            Assert.AreEqual(root, host.GetRpcStateRootAtBatch(1));
            host.RecordRpcDeposit(new DepositStatus(20260716u, 1, ConsumedOnL2: false, IncludedInBatch: null));
            Assert.IsNotNull(host.GetRpcL1DepositStatus(20260716u, 1));
            var l1Asset = UInt160.Parse("0x" + new string('a', 40));
            var l2Asset = UInt160.Parse("0x" + new string('b', 40));
            host.RegisterRpcAsset(l1Asset, l2Asset);
            Assert.AreEqual(l2Asset, host.GetRpcBridgedAsset(l1Asset));
            Assert.AreEqual(l1Asset, host.GetRpcCanonicalAsset(l2Asset));
            var leaf = new UInt256(Enumerable.Repeat((byte)0xCD, 32).ToArray());
            host.RecordRpcWithdrawalProof(leaf, [0x01, 0x02]);
            Assert.IsTrue(host.GetRpcWithdrawalProof(leaf) is { Length: 2 });
            var msgHash = new UInt256(Enumerable.Repeat((byte)0xEF, 32).ToArray());
            host.RecordRpcMessageProof(msgHash, [0x03]);
            Assert.IsTrue(host.GetRpcMessageProof(msgHash) is { Length: 1 });
            Assert.IsNotNull(host.MessageOutbox);
            Assert.AreEqual(0, host.MessageOutbox!.L2ToL1Count);
            host.RecordMessageRouterFinalizedProof(msgHash, new byte[] { 0x04 });
            var routerProof = host.GetMessageRouterProofAsync(msgHash).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(routerProof is { Length: 1 });
            Assert.IsTrue(status.GatewayEnabled);
            Assert.IsTrue(host.RegisterForcedInclusionNonce(42));
            Assert.IsFalse(host.RegisterForcedInclusionNonce(42)); // already known
            host.InvalidateForcedInclusionCache();
            var daReq = new DAPublishRequest
            {
                ChainId = 20260716u,
                BatchNumber = 1,
                Payload = new byte[] { 0x10, 0x20, 0x30 },
            };
            var receipt = host.PublishDaAsync(daReq).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(DAMode.Local, receipt.Layer);
            Assert.IsTrue(host.IsDaAvailableAsync(receipt).AsTask().GetAwaiter().GetResult());
            var daReader = host.CreateLocalDaReader();
            Assert.IsNotNull(daReader);
            var payload = daReader.ReadAsync(receipt).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(payload is { Length: 3 });
            Assert.IsNotNull(host.Metrics.Metrics);
            Assert.AreEqual(0, host.BridgeAssetCount);
            host.RegisterBridgeAsset(new AssetMapping
            {
                L1Asset = l1Asset,
                L2Asset = l2Asset,
                L2ChainId = 20260716u,
                L1Decimals = 8,
                L2Decimals = 8,
                AssetType = AssetType.Gas,
                MintBurn = true,
                LockMint = true,
                Active = true,
            });
            Assert.AreEqual(1, host.BridgeAssetCount);
            Assert.AreEqual(1, host.SnapshotBridgeAssets().Count);
            var prom = host.ExportPrometheusMetrics();
            Assert.IsFalse(string.IsNullOrWhiteSpace(prom));
            var snap = host.CaptureMetricsSnapshot();
            Assert.IsTrue(snap.TotalEntries >= 0);
            var status2 = host.GetOperatorStatusAsync().AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(1, status2.BridgeAssetCount);
            Assert.AreEqual(0, status2.MessageOutboxL2ToL1Count);
            Assert.AreEqual(0, status2.StagedWithdrawalCount);
            // Offline deposit mint path + Multisig prove (no funded L1).
            var depositPayload = new DepositPayload
            {
                L1Asset = l1Asset,
                L2Recipient = Account(0x55),
                Amount = new BigInteger(1_000),
            };
            var depositMsg = new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = 20260716u,
                Nonce = 1,
                Sender = Account(0x66),
                Receiver = Account(0x55),
                MessageType = MessageType.Deposit,
                Payload = depositPayload.Encode(),
                MessageHash = UInt256.Zero,
            };
            var mint = host.ProcessDeposit(depositMsg);
            Assert.AreEqual(l2Asset, mint.L2Asset);
            Assert.IsTrue(host.HasConsumedDeposit(0, 1));
            Assert.AreEqual(1, host.ConsumedDepositCount);
            // Ready-deposit drain is peek-only; empty ready set yields empty without L1 scan.
            Assert.AreEqual(0, host.ProcessReadyDeposits().Count);
            Assert.AreEqual(0, host.ProcessReadyDeposits(0).Count);
            Assert.IsNotNull(host.DepositSource);
            Assert.IsNotNull(host.ForcedInclusion);
            // Scan / overdue check hit L1 applicationlog scanners (funded RPC gate);
            // weak mock only covers settle-style getblockcount probes.
            await Assert.ThrowsExactlyAsync<Neo.L2.Settlement.Rpc.JsonRpcException>(
                async () => await host.ScanSharedBridgeDepositsAsync());
            await Assert.ThrowsExactlyAsync<Neo.L2.Settlement.Rpc.JsonRpcException>(
                async () => await host.HasOverdueForcedInclusionAsync(0));
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            var proof = host.ProveAsync(new ProofRequest
            {
                PublicInputs = new PublicInputs
                {
                    ChainId = 20260716u,
                    BatchNumber = 1,
                    PreStateRoot = z,
                    PostStateRoot = root,
                    TxRoot = z,
                    ReceiptRoot = z,
                    WithdrawalRoot = z,
                    L2ToL1MessageRoot = z,
                    L2ToL2MessageRoot = z,
                    L1MessageHash = z,
                    DACommitment = z,
                    BlockContextHash = z,
                },
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            }).AsTask().GetAwaiter().GetResult();
            Assert.AreEqual(ProofType.Multisig, proof.Kind);
            Assert.IsFalse(proof.Proof.IsEmpty);
            Assert.IsNotNull(host.BatchProver);
            // Withdrawal staging + outbound message outbox + status JSON dump.
            var sender = Account(0x77);
            var wdLeaf = host.StageWithdrawal(new WithdrawalRequest
            {
                ChainId = 20260716u,
                EmittingContract = sender,
                L2Sender = sender,
                L1Recipient = sender,
                L2Asset = l2Asset,
                Amount = new BigInteger(50),
                Nonce = 1,
            });
            Assert.AreNotEqual(UInt256.Zero, wdLeaf);
            Assert.AreEqual(1, host.StagedWithdrawalCount);
            var sealedWd = host.SealWithdrawalBatch();
            Assert.AreNotEqual(UInt256.Zero, sealedWd.Root);
            Assert.AreEqual(0, host.StagedWithdrawalCount);
            var outboundDraft = new CrossChainMessage
            {
                SourceChainId = 20260716u,
                TargetChainId = 0,
                Nonce = 9,
                Sender = sender,
                Receiver = sender,
                MessageType = MessageType.Event,
                Payload = new byte[] { 0x01 },
                MessageHash = UInt256.Zero,
            };
            var outbound = outboundDraft with { MessageHash = MessageHasher.HashMessage(outboundDraft) };
            await host.EnqueueOutboundMessagesAsync([outbound]);
            Assert.AreEqual(1, host.MessageOutbox!.L2ToL1Count);
            Assert.AreNotEqual(UInt256.Zero, outbound.MessageHash);
            Assert.AreNotEqual(UInt256.Zero, host.MessageOutboxL2ToL1Root);
            Assert.AreEqual(host.MessageOutbox.L2ToL1Root, host.MessageOutboxL2ToL1Root);
            var statusPath = Path.Combine(chainDir, "operator-status.json");
            await host.WriteOperatorStatusAsync(statusPath);
            Assert.IsTrue(File.Exists(statusPath));
            var statusJson = await File.ReadAllTextAsync(statusPath);
            StringAssert.Contains(statusJson, "\"chainId\": 20260716");
            StringAssert.Contains(statusJson, "\"isOperatorReady\": true");
            StringAssert.Contains(statusJson, "\"nextExpectedBlock\": 1");
            StringAssert.Contains(statusJson, "\"hasPendingSealedBatch\": false");
            StringAssert.Contains(statusJson, "\"isBatcherEnabled\": true");
            StringAssert.Contains(statusJson, "\"maxBlocksPerBatch\":");
            StringAssert.Contains(statusJson, "\"maxTransactionsPerBatch\":");
            StringAssert.Contains(statusJson, "\"maxBatchAgeMillis\":");
            StringAssert.Contains(statusJson, "\"hasOpenBatch\": false");
            StringAssert.Contains(statusJson, "\"inProgressTxCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchBlockCount\": 0");
            StringAssert.Contains(statusJson, "\"openBatchL1MessageCount\": 0");
            StringAssert.Contains(statusJson, "\"consumedDepositCount\": 1");
            StringAssert.Contains(statusJson, "\"lastAcknowledgedBatchNumber\": 0");
            StringAssert.Contains(statusJson, "\"nextBatchNumber\": 1");
            StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Count\": 1");
            StringAssert.Contains(statusJson, "\"messageOutboxL2ToL1Root\":");
            StringAssert.Contains(statusJson, "\"stagedWithdrawalCount\": 0");
            var promPath = Path.Combine(chainDir, "metrics.prom");
            await host.WritePrometheusMetricsAsync(promPath);
            Assert.IsTrue(File.Exists(promPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(await File.ReadAllTextAsync(promPath)));
            Assert.AreEqual(0, host.Metrics.BoundPort); // HTTP not started by default
            Assert.AreEqual(20260716u, host.RpcStore.ChainId);
            Assert.AreEqual(DAMode.Local, host.RpcStore.DAMode);
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public async Task Open_StartMetricsHttp_ReadyzOk_AndSettleHelpersWork()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeMultisigChain(chainDir, reportPath);
            using var http = CanonicalRootHttpClient();
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                SampleSigners(),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http,
                startMetricsHttp: true,
                metricsPortOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.AreEqual(host.Metrics.BoundPort, host.MetricsBoundPort);
            Assert.IsTrue(host.Batch.HasSealedBatchSink);
            Assert.IsTrue(host.Settlement.IsProductionWired);
            Assert.IsNotNull(host.TransactionSender);
            Assert.AreSame(host.Settlement.ProductionTransactionSender, host.TransactionSender);

            using var client = new HttpClient();
            var ready = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/readyz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, ready.StatusCode);
            var health = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, health.StatusCode);

            Assert.AreEqual(0, await host.GetPendingCountAsync());
            await host.ReconcileAsync();
            await host.SubmitNextAsync();
            Assert.IsTrue(host.IsProductionWired);

            // Recovery helpers that read only durable local stores (no extra L1 RPC).
            var recovery = await host.GetRecoveryStatusAsync();
            Assert.AreEqual(0, recovery.PendingCount);
            Assert.IsNull(recovery.State);
            var tracked = await host.GetTrackedForcedInclusionNoncesAsync(20260716u);
            Assert.AreEqual(0, tracked.Count);

            var metricsBody = await client.GetStringAsync(
                $"http://127.0.0.1:{host.MetricsBoundPort}/metrics");
            Assert.IsFalse(string.IsNullOrWhiteSpace(metricsBody));
            // Prometheus exposition always includes TYPE/HELP or at least scrapeable text.
            StringAssert.Contains(metricsBody, "#");

            host.StopMetricsHttp();
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
            // Can restart after stop without disposing the host metrics sink.
            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void Open_DeferredStartMetricsHttp_And_CreateRpcPlugin()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-deferred-metrics-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            MaterializeMultisigChain(chainDir, reportPath);
            using var http = CanonicalRootHttpClient();
            using var host = MultisigLocalHostComposition.Open(
                chainDir,
                new StubExecutor(),
                SampleSigners(),
                new StubSigner(Account(0x44)),
                rpcHttpClient: http);
            Assert.AreEqual(0, host.MetricsBoundPort);
            Assert.IsFalse(host.IsMetricsHttpListening);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.IsProductionWired);

            var rpcPlugin = host.CreateRpcPlugin();
            Assert.IsNotNull(rpcPlugin);
            Assert.IsFalse(rpcPlugin.IsRegistered(894710606)); // no NeoSystem registration yet
            // Metrics already configured on the plugin; second WithMetrics would be ok only if
            // no networks registered — CreateRpcPlugin owns that one-shot wiring.
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    [TestMethod]
    public void Open_ZkSettlementConfig_FailsClosed()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-msig-host-zk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), """
                {
                  "chainId": 20260716,
                  "proofType": "Zk",
                  "securityLevel": "Validity",
                  "daMode": "L1",
                  "sequencerModel": "DbftCommittee",
                  "exitModel": "Permissionless",
                  "gatewayEnabled": true,
                  "permissionlessExit": true,
                  "validators": []
                }
                """);
            NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
            File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
                { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
                """);

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
                MultisigLocalHostComposition.Open(
                    chainDir,
                    new StubExecutor(),
                    SampleSigners(),
                    new StubSigner(Account(0x55))));
            StringAssert.Contains(ex.Message, "Multisig");
            StringAssert.Contains(ex.Message, "Zk");
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    private static void MaterializeMultisigChain(string chainDir, string reportPath)
    {
        var validatorA = new KeyPair(Enumerable.Range(3, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var validatorB = new KeyPair(Enumerable.Range(4, 32).Select(i => (byte)i).ToArray()).PublicKey;
        var hexA = Convert.ToHexString(validatorA.EncodePoint(true)).ToLowerInvariant();
        var hexB = Convert.ToHexString(validatorB.EncodePoint(true)).ToLowerInvariant();
        File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), $$"""
            {
              "chainId": 20260716,
              "proofType": "Multisig",
              "securityLevel": "Optimistic",
              "daMode": "Local",
              "sequencerModel": "DbftCommittee",
              "exitModel": "Permissionless",
              "gatewayEnabled": true,
              "permissionlessExit": true,
              "validators": [ "{{hexA}}", "{{hexB}}" ]
            }
            """);
        NeoHubDeployReport.Load(reportPath).WriteOperatorArtifacts(chainDir);
        RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Multisig);
        RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Multisig);
        File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
            { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
            """);
    }

    private static void RewriteProofType(string chainDir, string pluginFolder, byte proofType)
    {
        var path = Path.Combine(chainDir, "Plugins", pluginFolder, "config.json");
        var text = File.ReadAllText(path);
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            text,
            "\"ProofType\"\\s*:\\s*\\d+",
            $"\"ProofType\": {proofType}");
        File.WriteAllText(path, rewritten);
    }

    private static InMemorySignerSet SampleSigners()
    {
        var keys = Enumerable.Range(1, 2).Select(i =>
        {
            var priv = new byte[32];
            for (var j = 0; j < 32; j++) priv[j] = (byte)(i + j);
            return (ECCurve.Secp256r1.G * priv, priv);
        }).ToList();
        return new InMemorySignerSet(keys);
    }

    private static UInt160 Account(byte fill) =>
        UInt160.Parse("0x" + Convert.ToHexString(Enumerable.Repeat(fill, 20).ToArray()).ToLowerInvariant());

    private static UInt256 Root(byte fill) =>
        new(Enumerable.Repeat(fill, 32).ToArray());

    private static HttpClient CanonicalRootHttpClient()
    {
        return new HttpClient(new MockHandler(async (request, _) =>
        {
            var body = await request.Content!.ReadAsStringAsync();
            if (body.Contains("getversion", StringComparison.Ordinal))
            {
                return Json(
                    """{"jsonrpc":"2.0","id":1,"result":{"protocol":{"network":894710606,"addressversion":53}}}""");
            }
            if (body.Contains("getblockcount", StringComparison.Ordinal))
            {
                return Json("""{"jsonrpc":"2.0","id":1,"result":100}""");
            }
            // Canonical state root for committee / profile paths.
            if (body.Contains("invokefunction", StringComparison.Ordinal)
                || body.Contains("invokescript", StringComparison.Ordinal))
            {
                var root = Convert.ToBase64String(Root(0x11).GetSpan().ToArray());
                return Json(
                    "{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"state\":\"HALT\",\"gasconsumed\":\"0\","
                    + "\"stack\":[{\"type\":\"ByteString\",\"value\":\"" + root + "\"}]}}");
            }
            return Json("""{"jsonrpc":"2.0","id":1,"result":null}""");
        }));
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class MockHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private sealed class StubExecutor : IProofWitnessBatchExecutor
    {
        public ValueTask<BatchExecutionResult> ApplyBatchAsync(
            BatchExecutionRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub executor does not execute batches");

        public ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
            SealedBatch batch,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub executor does not execute batches");
    }

    private sealed class StubSigner(UInt160 account) : INeoTransactionSigner
    {
        public UInt160 Account { get; } = account;
        public WitnessScope Scope => WitnessScope.CalledByEntry;

        public Witness CreatePlaceholderWitness()
            => new()
            {
                InvocationScript = Array.Empty<byte>(),
                VerificationScript = Array.Empty<byte>(),
            };

        public ValueTask<Witness> SignAsync(
            Transaction tx, uint network, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("stub signer does not sign");
    }
}
