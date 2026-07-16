using Neo.Wallets;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_LocalKeyTransactionSigner
{
    [TestMethod]
    public void FromWif_RoundTripsAccount()
    {
        var key = new KeyPair(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());
        var wif = key.Export();
        key.PrivateKey.AsSpan().Clear();

        using var signer = LocalKeyTransactionSigner.FromWif(wif);
        Assert.AreNotEqual(UInt160.Zero, signer.Account);
    }

    [TestMethod]
    public void FromWif_InvalidPayload_FailsClosed()
    {
        Assert.ThrowsExactly<FormatException>(() => LocalKeyTransactionSigner.FromWif("not-a-wif"));
        Assert.ThrowsExactly<ArgumentException>(() => LocalKeyTransactionSigner.FromWif(""));
    }

    [TestMethod]
    public void FromEnvironmentVariable_Missing_FailsClosed()
    {
        var name = "NEO_N4_TEST_WIF_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(name, null);
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(
                () => LocalKeyTransactionSigner.FromEnvironmentVariable(name));
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }

    [TestMethod]
    public void FromEnvironmentVariable_LoadsWif()
    {
        var name = "NEO_N4_TEST_WIF_" + Guid.NewGuid().ToString("N");
        var key = new KeyPair(Enumerable.Range(3, 32).Select(i => (byte)i).ToArray());
        var wif = key.Export();
        var expected = key.PublicKey;
        key.PrivateKey.AsSpan().Clear();
        Environment.SetEnvironmentVariable(name, wif);
        try
        {
            using var signer = LocalKeyTransactionSigner.FromEnvironmentVariable(name);
            Assert.AreNotEqual(UInt160.Zero, signer.Account);
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, null);
        }
    }
}
