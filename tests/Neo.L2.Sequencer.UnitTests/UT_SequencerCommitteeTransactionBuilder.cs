using Neo.Cryptography.ECC;

namespace Neo.L2.Sequencer.UnitTests;

[TestClass]
public class UT_SequencerCommitteeTransactionBuilder
{
    [TestMethod]
    public void Normalize_SortsKeysAndRejectsDuplicates()
    {
        var first = Key(1);
        var second = Key(2);

        var canonical = SequencerCommitteeTransactionBuilder.Normalize([second, first], 2);

        CollectionAssert.AreEqual(new[] { first, second }.OrderBy(static key => key).ToArray(), canonical.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() =>
            SequencerCommitteeTransactionBuilder.Normalize([first, first], 2));
    }

    [TestMethod]
    public void BuildSetValidatorsScript_IsCanonicalAcrossInputOrder()
    {
        var first = Key(1);
        var second = Key(2);

        var forward = SequencerCommitteeTransactionBuilder.BuildSetValidatorsScript([first, second], 2);
        var reverse = SequencerCommitteeTransactionBuilder.BuildSetValidatorsScript([second, first], 2);

        CollectionAssert.AreEqual(forward, reverse);
        Assert.IsGreaterThan(0, forward.Length);
    }

    [TestMethod]
    public void Normalize_RejectsCountMismatchAndInfinity()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            SequencerCommitteeTransactionBuilder.Normalize([Key(1)], 2));
        Assert.ThrowsExactly<ArgumentException>(() =>
            SequencerCommitteeTransactionBuilder.Normalize([new ECPoint()], 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            SequencerCommitteeTransactionBuilder.Normalize([Key(1)], 0));
    }

    private static ECPoint Key(byte seed)
    {
        var privateKey = Enumerable.Range(0, 32).Select(index => (byte)(seed + index)).ToArray();
        return ECCurve.Secp256r1.G * privateKey;
    }
}
