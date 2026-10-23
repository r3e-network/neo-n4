using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.L2.Proving.Attestation;

namespace Neo.L2.Proving.UnitTests;

[TestClass]
public class UT_AttestationMessage
{
    private static UInt256 H(byte fill) => new(Enumerable.Repeat(fill, 32).ToArray());

    [TestMethod]
    public void Build_LaysOutDomainTagThenPublicInputHash()
    {
        var publicInputHash = H(0xA1);
        var message = AttestationMessage.Build(publicInputHash);

        Assert.AreEqual(AttestationMessage.Size, message.Length);
        CollectionAssert.AreEqual("N4ATTST1"u8.ToArray(), message.AsSpan(0, AttestationMessage.DomainSize).ToArray());
        CollectionAssert.AreEqual(
            publicInputHash.ToArray(),
            message.AsSpan(AttestationMessage.DomainSize).ToArray());
    }

    [TestMethod]
    public void Domain_IsThePinnedAsciiTag()
    {
        CollectionAssert.AreEqual("N4ATTST1"u8.ToArray(), AttestationMessage.Domain.ToArray());
    }

    [TestMethod]
    public void Build_ForPublicInputs_HashesTheCanonicalPreimage()
    {
        var inputs = new PublicInputs
        {
            ChainId = 1099,
            BatchNumber = 1,
            FirstBlock = 1,
            LastBlock = 1,
            PreStateRoot = H(1),
            PostStateRoot = H(2),
            TxRoot = H(3),
            ReceiptRoot = H(4),
            WithdrawalRoot = H(5),
            L2ToL1MessageRoot = H(6),
            L2ToL2MessageRoot = H(7),
            L1MessageHash = H(8),
            DACommitment = H(9),
            BlockContextHash = H(10),
        };
        var expected = AttestationMessage.Build(
            Neo.L2.State.StateRootCalculator.HashPublicInputs(inputs));
        CollectionAssert.AreEqual(expected, AttestationMessage.Build(inputs));
    }

    [TestMethod]
    public void ComparePublicKeys_OrdersByCompressedByteEncoding()
    {
        // Deterministic keys: small scalars give distinct compressed encodings.
        var low = new byte[32]; low[31] = 2;
        var high = new byte[32]; high[31] = 3;
        var lowKey = Neo.Cryptography.ECC.ECCurve.Secp256r1.G * low;
        var highKey = Neo.Cryptography.ECC.ECCurve.Secp256r1.G * high;

        Assert.AreEqual(0, AttestationMessage.ComparePublicKeys(lowKey, lowKey));
        var ordering = Math.Sign(AttestationMessage.ComparePublicKeys(lowKey, highKey));
        Assert.AreEqual(
            ordering,
            -Math.Sign(AttestationMessage.ComparePublicKeys(highKey, lowKey)),
            "the comparison must be antisymmetric");
        Assert.AreNotEqual(0, ordering, "distinct keys must compare non-equal");
    }

    [TestMethod]
    public void Build_RejectsNullHash()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => AttestationMessage.Build(null!));
    }
}
