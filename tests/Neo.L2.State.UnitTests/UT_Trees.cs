using System.Numerics;

namespace Neo.L2.State.UnitTests;

[TestClass]
public class UT_Trees
{
    private static WithdrawalRequest Withdrawal(ulong nonce) => new()
    {
        EmittingContract = UInt160.Parse("0x" + new string('a', 40)),
        L2Sender = UInt160.Parse("0x" + new string('b', 40)),
        L1Recipient = UInt160.Parse("0x" + new string('c', 40)),
        L2Asset = UInt160.Parse("0x" + new string('d', 40)),
        Amount = new BigInteger(1_000_000) * (long)(nonce + 1),
        Nonce = nonce,
    };

    private static CrossChainMessage Message(uint targetChainId, ulong nonce) => new()
    {
        SourceChainId = 1001,
        TargetChainId = targetChainId,
        Nonce = nonce,
        Sender = UInt160.Parse("0x" + new string('e', 40)),
        Receiver = UInt160.Parse("0x" + new string('f', 40)),
        MessageType = MessageType.Call,
        Payload = new byte[] { 1, 2, 3 },
        MessageHash = UInt256.Parse("0x" + new string('1', 64).Replace('1', (char)('0' + (int)(nonce % 10)))),
    };

    [TestMethod]
    public void WithdrawalTree_CountsAndHasRoot()
    {
        var tree = new WithdrawalTree();
        for (ulong i = 0; i < 5; i++) tree.Add(Withdrawal(i));
        Assert.AreEqual(5, tree.Count);
        Assert.AreNotEqual(UInt256.Zero, tree.Root);
    }

    [TestMethod]
    public void WithdrawalTree_ProofVerifies()
    {
        var tree = new WithdrawalTree();
        for (ulong i = 0; i < 7; i++) tree.Add(Withdrawal(i));
        for (var i = 0; i < tree.Count; i++)
        {
            var proof = tree.GetProof(i);
            Assert.IsTrue(MerkleTree.Verify(proof, tree.Root), $"index {i} proof failed");
        }
    }

    [TestMethod]
    public void MessageTree_LookupAndProof()
    {
        var tree = new MessageTree();
        var m1 = Message(0, 1);
        var m2 = Message(2002, 2);
        tree.Add(m1);
        tree.Add(m2);
        Assert.IsTrue(tree.TryGetIndex(m2.MessageHash, out var idx));
        Assert.AreEqual(1, idx);
        var proof = tree.GetProof(idx);
        Assert.IsTrue(MerkleTree.Verify(proof, tree.Root));
    }

    [TestMethod]
    public void StateRootCalculator_HashL1Messages_DeterministicOrder()
    {
        var msgs = new[] { Message(1001, 1), Message(1001, 2), Message(1001, 3) };
        var a = StateRootCalculator.HashL1Messages(msgs);
        var b = StateRootCalculator.HashL1Messages(msgs);
        Assert.AreEqual(a, b);

        var reordered = new[] { msgs[2], msgs[1], msgs[0] };
        var c = StateRootCalculator.HashL1Messages(reordered);
        Assert.AreNotEqual(a, c);
    }

    [TestMethod]
    public void StateRootCalculator_PublicInputHash_StableAcrossCalls()
    {
        var inputs = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 7,
            PreStateRoot = UInt256.Parse("0x" + new string('1', 64)),
            PostStateRoot = UInt256.Parse("0x" + new string('2', 64)),
            TxRoot = UInt256.Parse("0x" + new string('3', 64)),
            ReceiptRoot = UInt256.Parse("0x" + new string('4', 64)),
            WithdrawalRoot = UInt256.Parse("0x" + new string('5', 64)),
            L2ToL1MessageRoot = UInt256.Parse("0x" + new string('6', 64)),
            L2ToL2MessageRoot = UInt256.Parse("0x" + new string('7', 64)),
            L1MessageHash = UInt256.Parse("0x" + new string('8', 64)),
            DACommitment = UInt256.Parse("0x" + new string('9', 64)),
            BlockContextHash = UInt256.Parse("0x" + new string('a', 64)),
        };
        var a = StateRootCalculator.HashPublicInputs(inputs);
        var b = StateRootCalculator.HashPublicInputs(inputs);
        Assert.AreEqual(a, b);
        Assert.AreNotEqual(UInt256.Zero, a);
    }

    [TestMethod]
    public void MessageTree_Add_RejectsNullMessageHash()
    {
        // Regression for iter 185: MessageHash is UInt256 (reference type) and `required`
        // doesn't prevent null. Without this guard, _byHash[null] threw with a generic
        // "key" message, and _leaves picked up a null entry that the iter-179
        // MerkleTree.ComputeRoot guard would catch — but only later. Surface at the
        // source.
        var tree = new MessageTree();
        var bad = new CrossChainMessage
        {
            SourceChainId = 1, TargetChainId = 2, Nonce = 1,
            Sender = UInt160.Zero, Receiver = UInt160.Zero,
            MessageType = MessageType.Call,
            Payload = ReadOnlyMemory<byte>.Empty,
            MessageHash = null!,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => tree.Add(bad));
    }

    [TestMethod]
    public void MessageTree_GetMessage_RejectsOutOfRangeIndex()
    {
        // Regression for iter 194: List<T>'s indexer threw a generic
        // "Index was out of range" message; now we surface the actual range.
        var tree = new MessageTree();
        tree.Add(Message(2002, 1));
        var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetMessage(-1));
        StringAssert.Contains(ex.Message, "[0, 1)");
        var ex2 = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetMessage(5));
        StringAssert.Contains(ex2.Message, "[0, 1)");
    }

    [TestMethod]
    public void WithdrawalTree_GetWithdrawal_RejectsOutOfRangeIndex()
    {
        var tree = new WithdrawalTree();
        tree.Add(Withdrawal(0));
        tree.Add(Withdrawal(1));
        var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetWithdrawal(-1));
        StringAssert.Contains(ex.Message, "[0, 2)");
        var ex2 = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => tree.GetWithdrawal(2));
        StringAssert.Contains(ex2.Message, "[0, 2)");
    }

    [TestMethod]
    public void MessageTree_TryGetIndex_RejectsNullHash()
    {
        var tree = new MessageTree();
        Assert.ThrowsExactly<ArgumentNullException>(() => tree.TryGetIndex(null!, out _));
    }

    [TestMethod]
    public void StateRootCalculator_HashL1Messages_RejectsNullMessages()
    {
        // Pin StateRootCalculator.cs:26. Without it the foreach loop NREs.
        Assert.ThrowsExactly<ArgumentNullException>(
            () => StateRootCalculator.HashL1Messages(null!));
    }

    [TestMethod]
    public void StateRootCalculator_HashBlockContext_RejectsNullSequencerCommitteeHash()
    {
        // Pin StateRootCalculator.cs:57. UInt256 reference-typed; null would NRE on
        // GetSpan() inside the buffer-write loop.
        var bad = new BatchBlockContext
        {
            L1FinalizedHeight = 1,
            FirstBlockTimestamp = 1, LastBlockTimestamp = 2,
            SequencerCommitteeHash = null!,
            Network = 0,
        };
        Assert.ThrowsExactly<ArgumentNullException>(
            () => StateRootCalculator.HashBlockContext(bad));
    }

    [TestMethod]
    public void StateRootCalculator_HashPublicInputs_RejectsNullInputs()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => StateRootCalculator.HashPublicInputs(null!));
}
