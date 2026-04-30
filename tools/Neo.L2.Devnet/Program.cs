using System;
using System.Numerics;
using System.Threading.Tasks;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;
using Neo.L2.Executor.State;
using Neo.L2.Messaging;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Sequencer;
using Neo.L2.State;
using Neo.Plugins.L2Rpc;

namespace Neo.L2.Devnet;

/// <summary>
/// In-process devnet runner. Boots all the off-chain L2 pieces, walks through a few batches
/// (deposit → execute → prove → verify → withdraw), and prints the resulting state. Real
/// <see cref="KeyedStateStore"/> backs the post-state-root oracle, so each batch's
/// <c>preStateRoot</c> equals the previous batch's <c>postStateRoot</c>.
/// </summary>
internal static class Program
{
    private const uint LocalChainId = 1001;
    private static readonly UInt160 GasL1 = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 GasL2 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 Alice = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Bob = UInt160.Parse("0x" + new string('b', 40));

    public static async Task<int> Main(string[] args)
    {
        var batches = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 3;

        Console.WriteLine("┌─────────────────────────────────────────────┐");
        Console.WriteLine("│  Neo Elastic Network — devnet runner v0.2    │");
        Console.WriteLine($"│  chainId = {LocalChainId}, batches = {batches,2}                      │");
        Console.WriteLine("└─────────────────────────────────────────────┘");
        Console.WriteLine();

        // ---- Wire components ----
        var registry = new AssetRegistry();
        registry.Register(new AssetMapping
        {
            L1Asset = GasL1,
            L2ChainId = LocalChainId,
            L2Asset = GasL2,
            AssetType = AssetType.Gas,
            MintBurn = true,
            LockMint = true,
            Active = true,
        });
        Console.WriteLine($"[wire] asset registry: 1 mapping (GAS L1={Truncate160(GasL1)} → L2={Truncate160(GasL2)})");

        var depositProcessor = new DepositProcessor(LocalChainId, registry);
        var withdrawalProcessor = new WithdrawalProcessor(LocalChainId, registry);

        var validators = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(validators);
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(validators.Select(v => v.pub), threshold: 3);
        Console.WriteLine($"[wire] {validators.Count} validators, attestation threshold = 3");

        var verifierRegistry = new VerifierRegistry();
        verifierRegistry.Register(verifier);

        // Sequencer committee — 3 sequencers, all bonded.
        var committeeProvider = new InMemorySequencerCommitteeProvider(LocalChainId, maxCommitteeSize: 7);
        for (var i = 0; i < 3; i++)
        {
            var (pub, _) = GenKey((byte)(100 + i));
            var addr = new byte[20];
            for (var j = 0; j < 20; j++) addr[j] = (byte)(0x10 * (i + 1));
            committeeProvider.Register(pub, new UInt160(addr));
        }
        var initialCommittee = await committeeProvider.GetActiveCommitteeAsync();
        Console.WriteLine($"[wire] sequencer committee: {initialCommittee.Count} active members");

        // Real state store + oracle. Seed Alice with 0 balance.
        var stateStore = new KeyedStateStore();
        var stateRootOracle = new KeyedStateRootOracle(stateStore);
        Console.WriteLine($"[wire] keyed state store + oracle ({stateStore.Count} initial entries)");

        var rpcStore = new InMemoryL2RpcStore(LocalChainId, SecurityLevel.Optimistic);
        rpcStore.RegisterAsset(GasL1, GasL2);
        var rpc = new L2RpcMethods(rpcStore);

        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            stateRootOracle);
        Console.WriteLine();

        // ---- Walk through N batches ----
        var preStateRoot = stateStore.ComputeRoot(); // start from empty store root = Zero
        for (var batchNum = 1; batchNum <= batches; batchNum++)
        {
            Console.WriteLine($"────── batch #{batchNum} ──────");

            // 1. Deposit message from L1 → mint balance to Alice in the state store.
            var depositAmount = new BigInteger(1_000_000 * batchNum);
            var deposit = new DepositPayload { L1Asset = GasL1, L2Recipient = Alice, Amount = depositAmount };
            var depositMsg = MessageBuilder.Build(0, LocalChainId, (ulong)batchNum, UInt160.Zero, Alice, MessageType.Deposit, deposit.Encode());
            var mint = depositProcessor.Process(depositMsg);
            // Apply to the store (this is what a real L2BridgeContract.mint would do).
            ApplyMint(stateStore, mint.L2Asset, mint.Recipient, mint.Amount);
            Console.WriteLine($"  [deposit] minted {mint.Amount} → Alice (nonce={mint.SourceNonce})");

            // 2. Stage a withdrawal — Alice → Bob on L1.
            var withdrawalAmount = new BigInteger(10_000 * batchNum);
            var withdrawal = new WithdrawalRequest
            {
                EmittingContract = UInt160.Zero,
                L2Sender = Alice,
                L1Recipient = Bob,
                L2Asset = GasL2,
                Amount = withdrawalAmount,
                Nonce = (ulong)batchNum,
            };
            withdrawalProcessor.Stage(withdrawal);
            ApplyBurn(stateStore, withdrawal.L2Asset, withdrawal.L2Sender, withdrawal.Amount);
            Console.WriteLine($"  [withdraw] staged {withdrawal.Amount} from Alice → Bob (nonce={withdrawal.Nonce})");

            // 3. Build batch + run executor.
            var txBytes = BitConverter.GetBytes((long)batchNum * 17);
            var ctx = new BatchBlockContext
            {
                L1FinalizedHeight = (uint)(1000 + batchNum),
                FirstBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000),
                LastBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000 + 5_000),
                SequencerCommitteeHash = HashCommittee(initialCommittee),
                Network = 0x4F454E,
            };
            var execReq = new BatchExecutionRequest
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                Transactions = new ReadOnlyMemory<byte>[] { txBytes },
                L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
                BlockContext = ctx,
            };
            var execResult = await executor.ApplyBatchAsync(execReq);

            // Replace executor's WithdrawalRoot with the processor's view (matches what NeoHub commits to).
            var (wRoot, _) = withdrawalProcessor.SealBatch();
            execResult = execResult with { WithdrawalRoot = wRoot };

            // 4. Sign + verify.
            var publicInputs = new PublicInputs
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                PreStateRoot = preStateRoot,
                PostStateRoot = execResult.PostStateRoot,
                TxRoot = execResult.TxRoot,
                ReceiptRoot = execResult.ReceiptRoot,
                WithdrawalRoot = execResult.WithdrawalRoot,
                L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
                L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
                L1MessageHash = StateRootCalculator.HashL1Messages(execReq.L1MessagesConsumed),
                DACommitment = UInt256.Zero,
                BlockContextHash = StateRootCalculator.HashBlockContext(ctx),
            };
            var proofResult = await prover.ProveAsync(new ProofRequest
            {
                PublicInputs = publicInputs,
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            });

            var commitment = new L2BatchCommitment
            {
                ChainId = LocalChainId,
                BatchNumber = (ulong)batchNum,
                FirstBlock = (ulong)(100 * batchNum),
                LastBlock = (ulong)(100 * batchNum + 50),
                PreStateRoot = preStateRoot,
                PostStateRoot = execResult.PostStateRoot,
                TxRoot = execResult.TxRoot,
                ReceiptRoot = execResult.ReceiptRoot,
                WithdrawalRoot = execResult.WithdrawalRoot,
                L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
                L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
                DACommitment = UInt256.Zero,
                PublicInputHash = proofResult.PublicInputHash,
                ProofType = ProofType.Multisig,
                Proof = proofResult.Proof,
            };

            var verify = await verifierRegistry.VerifyAsync(commitment, publicInputs);
            Console.WriteLine($"  [seal] preRoot={Truncate(preStateRoot)} postRoot={Truncate(commitment.PostStateRoot)} verify={verify.Valid}");
            if (!verify.Valid)
            {
                Console.Error.WriteLine($"  ❌ verification failed: {verify.FailureReason}");
                return 1;
            }

            // 5. Finalize in RPC store.
            rpcStore.AddBatch(commitment, BatchStatus.Pending);
            rpcStore.Finalize((ulong)batchNum);
            rpcStore.RecordDeposit(new DepositStatus(0, (ulong)batchNum, ConsumedOnL2: true, IncludedInBatch: (ulong)batchNum));

            // Continuity: next batch's pre = this batch's post.
            preStateRoot = commitment.PostStateRoot;
        }

        Console.WriteLine();
        Console.WriteLine("───── post-run RPC snapshot ─────");
        var latest = rpc.GetL2StateRoot(new Json.JArray { LocalChainId });
        Console.WriteLine($"  getl2stateroot:   {latest!.AsString()}");
        Console.WriteLine($"  state entries:    {stateStore.Count}");
        Console.WriteLine($"  committee active: {(await committeeProvider.GetActiveCommitteeAsync()).Count}");

        // Show Alice's net position: deposits - withdrawals over N batches.
        var aliceBalance = ReadBalance(stateStore, GasL2, Alice);
        var expected = BigInteger.Zero;
        for (var i = 1; i <= batches; i++) expected += new BigInteger(1_000_000 * i) - new BigInteger(10_000 * i);
        Console.WriteLine($"  alice balance:    {aliceBalance} (expected {expected})");
        if (aliceBalance != expected)
        {
            Console.Error.WriteLine("❌ Alice's balance disagrees with expected; state-store wiring broken.");
            return 1;
        }

        Console.WriteLine();
        Console.WriteLine("✅ devnet run complete.");
        return 0;
    }

    // ---- Helpers ----

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static string Truncate(UInt256 root)
    {
        var s = root.ToString();
        return s.Length <= 18 ? s : s[..10] + "…" + s[^6..];
    }

    private static string Truncate160(UInt160 h)
    {
        var s = h.ToString();
        return s.Length <= 14 ? s : s[..10] + "…" + s[^4..];
    }

    private static UInt256 HashCommittee(IReadOnlyList<CommitteeMember> members)
    {
        if (members.Count == 0) return UInt256.Zero;
        var bytes = new List<byte>();
        foreach (var m in members.OrderBy(x => x.PublicKey)) bytes.AddRange(m.PublicKey.EncodePoint(true));
        return new UInt256(Crypto.Hash256(bytes.ToArray()));
    }

    private static byte[] BalanceKey(UInt160 asset, UInt160 holder)
    {
        var k = new byte[1 + 20 + 20];
        k[0] = 0x01; // domain prefix for "balance"
        asset.GetSpan().CopyTo(k.AsSpan(1, 20));
        holder.GetSpan().CopyTo(k.AsSpan(21, 20));
        return k;
    }

    private static void ApplyMint(KeyedStateStore store, UInt160 asset, UInt160 holder, BigInteger amount)
    {
        var key = BalanceKey(asset, holder);
        var current = ReadBalance(store, asset, holder);
        store.Put(key, (current + amount).ToByteArray(isUnsigned: true, isBigEndian: false));
    }

    private static void ApplyBurn(KeyedStateStore store, UInt160 asset, UInt160 holder, BigInteger amount)
    {
        var key = BalanceKey(asset, holder);
        var current = ReadBalance(store, asset, holder);
        var next = current - amount;
        if (next < 0) throw new InvalidOperationException("insufficient balance");
        store.Put(key, next.ToByteArray(isUnsigned: true, isBigEndian: false));
    }

    private static BigInteger ReadBalance(KeyedStateStore store, UInt160 asset, UInt160 holder)
    {
        var bytes = store.Get(BalanceKey(asset, holder));
        if (bytes.Length == 0) return BigInteger.Zero;
        return new BigInteger(bytes.Span, isUnsigned: true, isBigEndian: false);
    }
}
