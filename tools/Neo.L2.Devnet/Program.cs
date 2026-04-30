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
using Neo.L2.Messaging;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.State;
using Neo.Plugins.L2Rpc;

namespace Neo.L2.Devnet;

/// <summary>
/// In-process devnet runner. Boots all the off-chain L2 pieces, walks through a few batches
/// (deposit → execute → prove → verify → withdraw), and prints the resulting state. Useful as
/// a sanity check after major refactors and as a "what does an L2 actually do?" demo.
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
        Console.WriteLine("│  Neo Elastic Network — devnet runner v0.1    │");
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
        Console.WriteLine($"[wire] asset registry: 1 mapping (GAS L1={GasL1} → L2={GasL2})");

        var depositProcessor = new DepositProcessor(LocalChainId, registry);
        var withdrawalProcessor = new WithdrawalProcessor(LocalChainId, registry);

        var validators = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(validators);
        var prover = new AttestationProver(signers);
        var verifier = new AttestationVerifier(validators.Select(v => v.pub), threshold: 3);
        Console.WriteLine($"[wire] {validators.Count} validators, attestation threshold = 3");

        var verifierRegistry = new VerifierRegistry();
        verifierRegistry.Register(verifier);

        var rpcStore = new InMemoryL2RpcStore(LocalChainId, SecurityLevel.Optimistic);
        rpcStore.RegisterAsset(GasL1, GasL2);
        var rpc = new L2RpcMethods(rpcStore);

        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(),
            new DerivedPostStateRootOracle());
        Console.WriteLine();

        // ---- Walk through N batches ----
        var preStateRoot = UInt256.Zero;
        for (var batchNum = 1; batchNum <= batches; batchNum++)
        {
            Console.WriteLine($"────── batch #{batchNum} ──────");

            // 1. Deposit message from L1.
            var deposit = new DepositPayload { L1Asset = GasL1, L2Recipient = Alice, Amount = new BigInteger(1_000_000 * batchNum) };
            var depositMsg = MessageBuilder.Build(0, LocalChainId, (ulong)batchNum, UInt160.Zero, Alice, MessageType.Deposit, deposit.Encode());
            var mint = depositProcessor.Process(depositMsg);
            Console.WriteLine($"  [deposit] minted {mint.Amount} → {mint.Recipient} (nonce={mint.SourceNonce})");

            // 2. Stage a withdrawal.
            var withdrawal = new WithdrawalRequest
            {
                EmittingContract = UInt160.Zero,
                L2Sender = Alice,
                L1Recipient = Bob,
                L2Asset = GasL2,
                Amount = new BigInteger(10_000 * batchNum),
                Nonce = (ulong)batchNum,
            };
            withdrawalProcessor.Stage(withdrawal);
            Console.WriteLine($"  [withdraw] staged {withdrawal.Amount} from {Alice} → {Bob} (nonce={withdrawal.Nonce})");

            // 3. Build a batch with one synthetic tx whose effects = [withdrawal].
            var txBytes = BitConverter.GetBytes((long)batchNum * 17);
            var txHash = new UInt256(Crypto.Hash256(txBytes));
            var ctx = new BatchBlockContext
            {
                L1FinalizedHeight = (uint)(1000 + batchNum),
                FirstBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000),
                LastBlockTimestamp = (ulong)(1_700_000_000_000 + batchNum * 10_000 + 5_000),
                SequencerCommitteeHash = UInt256.Parse("0x" + new string('c', 64)),
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

            // For the demo, the executor's returned WithdrawalRoot is whatever its bound
            // state oracle produces. We override here with the processor's snapshot so the
            // public-input bundle and the user-side proof both anchor on the same root.
            var (withdrawalRoot, withdrawalTree) = withdrawalProcessor.SealBatch();
            execResult = execResult with { WithdrawalRoot = withdrawalRoot };

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
            Console.WriteLine($"  [seal] postStateRoot={Truncate(commitment.PostStateRoot)} verify={verify.Valid}");
            if (!verify.Valid)
            {
                Console.Error.WriteLine($"  ❌ verification failed: {verify.FailureReason}");
                return 1;
            }

            // 5. RPC store gets the batch + finalizes immediately (Phase 0 mode).
            rpcStore.AddBatch(commitment, BatchStatus.Pending);
            rpcStore.Finalize((ulong)batchNum);
            rpcStore.RecordDeposit(new DepositStatus(0, (ulong)batchNum, ConsumedOnL2: true, IncludedInBatch: (ulong)batchNum));
            var withdrawalLeaf = MessageHasher.HashWithdrawal(withdrawal);
            rpcStore.RecordWithdrawalProof(withdrawalLeaf, EncodeProof(withdrawalTree.GetProof(0)));

            preStateRoot = commitment.PostStateRoot;
        }

        Console.WriteLine();
        Console.WriteLine("───── post-run RPC snapshot ─────");
        var latest = rpc.GetL2StateRoot(new Json.JArray { LocalChainId });
        Console.WriteLine($"  getl2stateroot:  {latest!.AsString()}");
        var sec = rpc.GetSecurityLevel(new Json.JArray { LocalChainId });
        Console.WriteLine($"  getsecuritylevel: {sec}");

        Console.WriteLine();
        Console.WriteLine("✅ devnet run complete.");
        return 0;
    }

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

    private static byte[] EncodeProof(MerkleProof proof)
    {
        // Tiny canonical proof envelope: 4B leafIndex + 8B path + 1B siblingCount + 32B*siblings.
        var size = 4 + 8 + 1 + 32 * proof.Siblings.Count;
        var buf = new byte[size];
        BitConverter.TryWriteBytes(buf.AsSpan(0, 4), proof.LeafIndex);
        BitConverter.TryWriteBytes(buf.AsSpan(4, 8), proof.PathBitmap);
        buf[12] = (byte)proof.Siblings.Count;
        for (var i = 0; i < proof.Siblings.Count; i++)
            proof.Siblings[i].GetSpan().CopyTo(buf.AsSpan(13 + 32 * i, 32));
        return buf;
    }
}
