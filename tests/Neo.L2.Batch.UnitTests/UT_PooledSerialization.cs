namespace Neo.L2.Batch.UnitTests;

[TestClass]
public class UT_PooledSerialization
{
    private static UInt256 H(char c) => UInt256.Parse("0x" + new string(c, 64));

    private static L2BatchCommitment MakeCommitment(int seed) => new()
    {
        ChainId = 1099,
        BatchNumber = 7,
        FirstBlock = 100,
        LastBlock = 200,
        PreStateRoot = H('a'),
        PostStateRoot = H('b'),
        TxRoot = H('c'),
        ReceiptRoot = H('d'),
        WithdrawalRoot = H('e'),
        L2ToL1MessageRoot = H('f'),
        L2ToL2MessageRoot = H('1'),
        DACommitment = H('2'),
        PublicInputHash = H('3'),
        ProofType = ProofType.Zk,
        Proof = new byte[1 + (seed % 64)],
    };

    [TestMethod]
    public void PooledSerialize_MatchesBatchSerializer_ByteForByte()
    {
        // The pooled path is a pure allocation optimization: its output must be
        // byte-identical to the canonical (non-pooled) encoder.
        var commitment = MakeCommitment(3);
        var canonical = BatchSerializer.Encode(commitment);
        var pooled = PooledBatchSerializer.Serialize(commitment);
        CollectionAssert.AreEqual(canonical, pooled);
    }

    [TestMethod]
    public void PooledSerialize_PublicInputs_MatchesCanonicalEncoder()
    {
        var inputs = new PublicInputs
        {
            ChainId = 1099,
            BatchNumber = 7,
            FirstBlock = 1,
            LastBlock = 2,
            PreStateRoot = H('a'),
            PostStateRoot = H('b'),
            TxRoot = H('c'),
            ReceiptRoot = H('d'),
            WithdrawalRoot = H('e'),
            L2ToL1MessageRoot = H('f'),
            L2ToL2MessageRoot = H('1'),
            L1MessageHash = H('2'),
            DACommitment = H('3'),
            BlockContextHash = H('4'),
        };
        var canonical = BatchSerializer.EncodePublicInputs(inputs);
        var pooled = PooledBatchSerializer.Serialize(inputs);
        CollectionAssert.AreEqual(canonical, pooled);
    }

    [TestMethod]
    public void CopyToPooledBuffer_ConcatenatesInOrder()
    {
        var first = new ReadOnlyMemory<byte>(new byte[] { 1, 2, 3 });
        var second = new ReadOnlyMemory<byte>(new byte[] { 4, 5 });
        using var owner = PooledTransactionArrays.CopyToPooledBuffer(new[] { first, second });
        CollectionAssert.AreEqual(
            new byte[] { 1, 2, 3, 4, 5 },
            owner.Memory.ToArray());
    }

    [TestMethod]
    public void CopyToPooledBuffer_EmptyListProducesEmptyBuffer()
    {
        using var owner = PooledTransactionArrays.CopyToPooledBuffer(
            Array.Empty<ReadOnlyMemory<byte>>());
        Assert.AreEqual(0, owner.Memory.Length);
    }

    [TestMethod]
    public void WrapTransactionInPool_CopiesBytes()
    {
        var transaction = new byte[] { 9, 8, 7, 6 };
        using var owner = PooledTransactionArrays.WrapTransactionInPool(transaction);
        CollectionAssert.AreEqual(transaction, owner.Memory.ToArray());
        // Mutating the source after wrapping must not affect the pooled copy.
        transaction[0] = 0xFF;
        Assert.AreEqual(9, owner.Memory.Span[0]);
    }
}
