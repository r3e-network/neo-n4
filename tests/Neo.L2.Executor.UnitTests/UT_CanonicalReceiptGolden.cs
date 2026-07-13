using Neo.L2.Executor.Receipts;

namespace Neo.L2.Executor.UnitTests;

/// <summary>Cross-language conformance tests for the 105-byte N4 genesis receipt.</summary>
[TestClass]
public sealed class UT_CanonicalReceiptGolden
{
    [TestMethod]
    public void CanonicalReceiptV1_MatchesRustGenesisGoldenByteForByte()
    {
        var effects = UT_CanonicalExecutionEffects.GoldenEffects();
        var receipt = new Receipt
        {
            TxHash = new UInt256(Enumerable.Range(0, UInt256.Length).Select(static value => (byte)value).ToArray()),
            Success = true,
            GasConsumed = 0x0102_0304_0506_0708,
            StorageDeltaHash = effects.StorageHash,
            EventsHash = effects.EventsHash,
        };

        Assert.AreEqual(105, Receipt.ReceiptHashSize);
        CollectionAssert.AreEqual(
            Convert.FromHexString(CanonicalEffectsGolden.Receipt),
            receipt.EncodeHashData());
        CollectionAssert.AreEqual(
            Convert.FromHexString(CanonicalEffectsGolden.ReceiptHash),
            receipt.Hash().GetSpan().ToArray());
    }
}
