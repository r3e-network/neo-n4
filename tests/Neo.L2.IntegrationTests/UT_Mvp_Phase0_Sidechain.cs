using System.Numerics;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Batch;
using Neo.L2.Bridge;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;
using Neo.L2.Messaging;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.State;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Phase 0 MVP end-to-end test (doc.md §20). Walks the full L2 batch lifecycle in-process —
/// no Neo node, no L1 RPC — to lock in that the off-chain pieces compose:
///   1. Register a GAS asset mapping.
///   2. Receive an L1 → L2 deposit message; mint via DepositProcessor.
///   3. Build an L2 batch that includes the deposit + a withdrawal.
///   4. Run ReferenceBatchExecutor → BatchExecutionResult.
///   5. Pack into L2BatchCommitment with multisig attestation proof.
///   6. Verify the proof end-to-end via VerifierRegistry.
///   7. Generate a withdrawal Merkle proof and verify against the finalized root.
/// </summary>
[TestClass]
public class UT_Mvp_Phase0_Sidechain
{
    private const uint LocalChainId = 1001;
    private static readonly UInt160 GasL1 = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 GasL2 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 Alice = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Bob = UInt160.Parse("0x" + new string('b', 40));

    private static (ECPoint pub, byte[] priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }

    private static AssetRegistry RegistryWithGas()
    {
        var r = new AssetRegistry();
        r.Register(new AssetMapping
        {
            L1Asset = GasL1,
            L2ChainId = LocalChainId,
            L2Asset = GasL2,
            L1Decimals = 8,
            L2Decimals = 8,
            AssetType = AssetType.Gas,
            MintBurn = true,
            LockMint = true,
            Active = true,
        });
        return r;
    }

    [TestMethod]
    public async Task FullLifecycle_Deposit_Batch_Prove_Verify_Withdraw()
    {
        // ---- 1. Asset registry ----
        var registry = RegistryWithGas();

        // ---- 2. Inbound deposit message from L1 (synthetic) ----
        var deposit = new DepositPayload { L1Asset = GasL1, L2Recipient = Alice, Amount = new BigInteger(1_000_000) };
        var depositMsg = MessageBuilder.Build(
            sourceChainId: 0,
            targetChainId: LocalChainId,
            nonce: 1,
            sender: UInt160.Zero,
            receiver: Alice,
            messageType: MessageType.Deposit,
            payload: deposit.Encode());

        var depositProcessor = new DepositProcessor(LocalChainId, registry);
        var mint = depositProcessor.Process(depositMsg);
        Assert.AreEqual(GasL2, mint.L2Asset);
        Assert.AreEqual(Alice, mint.Recipient);
        Assert.AreEqual(deposit.Amount, mint.Amount);

        // ---- 3. Stage a withdrawal request (Alice → Bob on L1) ----
        var withdrawalProcessor = new WithdrawalProcessor(LocalChainId, registry);
        var withdrawal = new WithdrawalRequest
        {
            EmittingContract = UInt160.Zero,
            L2Sender = Alice,
            L1Recipient = Bob,
            L2Asset = GasL2,
            Amount = new BigInteger(500_000),
            Nonce = 1,
        };
        var withdrawalLeaf = withdrawalProcessor.Stage(withdrawal);

        // ---- 4. Build a batch + run the executor ----
        var blockContext = new BatchBlockContext
        {
            L1FinalizedHeight = 1234,
            FirstBlockTimestamp = 1_700_000_000_000,
            LastBlockTimestamp = 1_700_000_005_000,
            SequencerCommitteeHash = UInt256.Parse("0x" + new string('c', 64)),
            Network = 0x4F454E,
        };

        // Build a synthetic transaction whose effects include the staged withdrawal.
        var txBytes = new ReadOnlyMemory<byte>(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var txHash = new UInt256(Crypto.Hash256(txBytes.Span));

        var effects = new MapEffectsCollector(new Dictionary<UInt256, BatchEffects>
        {
            [txHash] = new BatchEffects
            {
                Withdrawals = new[] { withdrawal },
                Messages = Array.Empty<CrossChainMessage>(),
            },
        });

        var executor = new ReferenceBatchExecutor(
            new ReferenceTransactionExecutor(effects),
            new DerivedPostStateRootOracle(),
            new ApplyDepositL1Processor(depositProcessor));

        var execRequest = new BatchExecutionRequest
        {
            ChainId = LocalChainId,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            Transactions = new[] { txBytes },
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(), // already processed above
            BlockContext = blockContext,
        };
        var execResult = await executor.ApplyBatchAsync(execRequest);

        // ---- 5. Build commitment + sign with multisig prover ----
        var validators = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(validators);
        var prover = new AttestationProver(signers);

        var publicInputs = new PublicInputs
        {
            ChainId = LocalChainId,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = execResult.PostStateRoot,
            TxRoot = execResult.TxRoot,
            ReceiptRoot = execResult.ReceiptRoot,
            WithdrawalRoot = execResult.WithdrawalRoot,
            L2ToL1MessageRoot = execResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = execResult.L2ToL2MessageRoot,
            L1MessageHash = StateRootCalculator.HashL1Messages(execRequest.L1MessagesConsumed),
            DACommitment = UInt256.Zero,
            BlockContextHash = StateRootCalculator.HashBlockContext(blockContext),
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
            BatchNumber = 1,
            FirstBlock = 100,
            LastBlock = 100,
            PreStateRoot = UInt256.Zero,
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

        // ---- 6. Verify via VerifierRegistry ----
        var registry2 = new VerifierRegistry();
        registry2.Register(new AttestationVerifier(validators.Select(v => v.pub), threshold: 3));
        var verify = await registry2.VerifyAsync(commitment, publicInputs);
        Assert.IsTrue(verify.Valid, verify.FailureReason);

        // ---- 7. Withdrawal Merkle proof against the finalized withdrawalRoot ----
        var (root, tree) = withdrawalProcessor.SealBatch();
        Assert.AreEqual(execResult.WithdrawalRoot, root, "executor and processor agree on withdrawal root");
        var proof = tree.GetProof(0);
        Assert.AreEqual(MessageHasher.HashWithdrawal(withdrawal), proof.Leaf);
        Assert.IsTrue(State.MerkleTree.Verify(proof, root));
    }

    /// <summary>Test-only effects collector backed by a dictionary.</summary>
    private sealed class MapEffectsCollector : IBatchEffectsCollector
    {
        private readonly Dictionary<UInt256, BatchEffects> _map;
        public MapEffectsCollector(IDictionary<UInt256, BatchEffects> map) => _map = new(map);
        public BatchEffects GetEffects(UInt256 txHash) => _map.TryGetValue(txHash, out var e) ? e : BatchEffects.Empty;
    }

    /// <summary>Test-only L1 message processor that pipes deposits into a real DepositProcessor.</summary>
    private sealed class ApplyDepositL1Processor : IL1MessageProcessor
    {
        private readonly DepositProcessor _depositProcessor;
        public ApplyDepositL1Processor(DepositProcessor depositProcessor) => _depositProcessor = depositProcessor;
        public ValueTask ApplyAsync(CrossChainMessage message, CancellationToken cancellationToken = default)
        {
            if (message.MessageType == MessageType.Deposit)
                _depositProcessor.Process(message);
            return ValueTask.CompletedTask;
        }
    }
}
