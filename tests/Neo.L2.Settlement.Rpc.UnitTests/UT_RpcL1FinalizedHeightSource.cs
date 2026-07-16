using System.Net;
using System.Text;
using Neo.Json;

namespace Neo.L2.Settlement.Rpc.UnitTests;

[TestClass]
public class UT_RpcL1FinalizedHeightSource
{
    [TestMethod]
    public async Task GetFinalizedHeight_SubtractsFinalityDepth()
    {
        var handler = new BlockCountHandler(100);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://localhost:10332"), http);
        var source = new RpcL1FinalizedHeightSource(rpc, finalityDepth: 1);

        var height = await source.GetFinalizedHeightAsync();

        Assert.AreEqual(98u, height); // 100 - 1 - 1
        CollectionAssert.Contains(handler.Methods, "getblockcount");
    }

    [TestMethod]
    public async Task GetFinalizedHeight_ShallowChain_ReturnsZero()
    {
        var handler = new BlockCountHandler(1);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://localhost:10332"), http);
        var source = new RpcL1FinalizedHeightSource(rpc, finalityDepth: 5);

        Assert.AreEqual(0u, await source.GetFinalizedHeightAsync());
    }

    [TestMethod]
    public void CreateSyncProvider_ReturnsSameHeight()
    {
        var handler = new BlockCountHandler(50);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://localhost:10332"), http);
        var source = new RpcL1FinalizedHeightSource(rpc, finalityDepth: 2);
        var provider = source.CreateSyncProvider();

        Assert.AreEqual(47u, provider()); // 50 - 1 - 2
    }

    [TestMethod]
    public void Constructor_RejectsNullRpc()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new RpcL1FinalizedHeightSource(null!));
    }

    private sealed class BlockCountHandler(uint blockCount) : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var envelope = (JObject)JToken.Parse(body)!;
            Methods.Add(envelope["method"]!.AsString());
            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = envelope["id"],
                ["result"] = blockCount,
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json"),
            };
        }
    }
}
