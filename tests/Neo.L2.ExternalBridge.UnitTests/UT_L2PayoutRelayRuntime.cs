using Neo.L2.Settlement.Rpc;
using Neo.Wallets;

using static Neo.L2.ExternalBridge.UnitTests.PayoutTestData;

namespace Neo.L2.ExternalBridge.UnitTests;

[TestClass]
public sealed class UT_L2PayoutRelayRuntime
{
    [TestMethod]
    public async Task Create_ValidProductionComposition_OwnsAndDisposesResources()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"neo4-payout-{Guid.NewGuid():N}");
        using var signer = CreateSigner();
        var options = Options(directory, signer.Account);
        try
        {
            var runtime = L2PayoutRelayRuntime.Create(options, signer, signer);
            runtime.Dispose();
            runtime.Dispose();
            await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
                await runtime.RunOnceAsync());
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void Create_RejectsIncompleteOrMismatchedProductionConfiguration()
    {
        using var signer = CreateSigner();
        using var otherSigner = new LocalKeyTransactionSigner(
            new KeyPair(Enumerable.Range(33, 32).Select(value => (byte)value).ToArray()));
        var options = Options("relay.db", signer.Account);

        Assert.ThrowsExactly<ArgumentNullException>(() =>
            L2PayoutRelayRuntime.Create(null!, signer, signer));
        Assert.ThrowsExactly<ArgumentException>(() =>
            L2PayoutRelayRuntime.Create(options with { L1RpcEndpoint = " " }, signer, signer));
        Assert.ThrowsExactly<ArgumentException>(() =>
            L2PayoutRelayRuntime.Create(options with { L2RpcEndpoint = " " }, signer, signer));
        Assert.ThrowsExactly<ArgumentException>(() =>
            L2PayoutRelayRuntime.Create(options with { DataDirectory = " " }, signer, signer));
        Assert.ThrowsExactly<ArgumentException>(() =>
            L2PayoutRelayRuntime.Create(options with { Adapter = UInt160.Zero }, signer, signer));
        Assert.ThrowsExactly<ArgumentException>(() =>
            L2PayoutRelayRuntime.Create(options with { L2NativeBridge = UInt160.Zero }, signer, signer));
        Assert.ThrowsExactly<ArgumentException>(() =>
            L2PayoutRelayRuntime.Create(options with { RelayAccount = UInt160.Zero }, signer, signer));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            L2PayoutRelayRuntime.Create(options with { NeoChainId = 0 }, signer, signer));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            L2PayoutRelayRuntime.Create(options with { MaximumRetries = 0 }, signer, signer));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            L2PayoutRelayRuntime.Create(options, signer, otherSigner));
    }

    private static L2PayoutRelayProductionOptions Options(
        string directory,
        UInt160 relayAccount) => new()
        {
            L1RpcEndpoint = "http://l1.example/",
            L2RpcEndpoint = "http://l2.example/",
            L1Network = 894_710_606,
            L2Network = 1_234_567,
            Adapter = Adapter,
            L2NativeBridge = NativeBridge,
            NeoChainId = NeoChainId,
            RelayAccount = relayAccount,
            L1DeploymentHeight = 100,
            DataDirectory = directory,
        };

    private static LocalKeyTransactionSigner CreateSigner()
    {
        var privateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        return new LocalKeyTransactionSigner(new KeyPair(privateKey));
    }
}
