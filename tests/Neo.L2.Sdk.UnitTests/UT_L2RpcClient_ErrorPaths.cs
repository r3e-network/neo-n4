using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Neo;
using Neo.Json;
using Neo.L2;
using Neo.L2.Sdk;

namespace Neo.L2.Sdk.UnitTests;

/// <summary>
/// Error-path tests for <see cref="L2RpcClient"/>. Pins which exception type each
/// failure mode surfaces so callers can write targeted catch clauses (e.g.
/// retry on transport but not protocol).
/// </summary>
[TestClass]
public class UT_L2RpcClient_ErrorPaths
{
    private const uint TestChainId = 1099;
    private const string Endpoint = "http://node.example:30332";

    [TestMethod]
    public void Ctor_InvalidEndpoint_RejectedAtConstruction()
    {
        // Pin that misconfiguration surfaces at ctor time, not on first call.
        Assert.ThrowsExactly<ArgumentException>(() => new L2RpcClient("ftp://x.example", TestChainId));
        Assert.ThrowsExactly<UriFormatException>(() => new L2RpcClient("not-a-url", TestChainId));
        // Empty / null endpoint
        Assert.ThrowsExactly<ArgumentException>(() => new L2RpcClient("", TestChainId));
    }

    [TestMethod]
    public void Ctor_ZeroChainId_RejectedByValidator()
    {
        // ChainIdValidator.ValidateL2 rejects 0 (= L1 chainId, reserved). Pin the rejection
        // so a chainId of 0 leaking through (uninitialized field, default int) doesn't
        // produce a client that silently talks to the wrong chain. The exception type
        // (InvalidDataException) is the validator's contract — we just verify it surfaces.
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() => new L2RpcClient(Endpoint, 0));
    }

    [TestMethod]
    public async Task ServerError_SurfacesAsServerException_WithCode()
    {
        var stub = new StubHttpHandler();
        using var http = new HttpClient(stub);
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        stub.EnqueueError(-32000, "node not synced");

        var ex = await Assert.ThrowsExactlyAsync<L2RpcServerException>(
            () => client.GetLatestStateRootAsync());
        Assert.AreEqual("getl2stateroot", ex.Method);
        Assert.AreEqual(-32000, ex.Code);
        StringAssert.Contains(ex.Message, "node not synced");
    }

    [TestMethod]
    public async Task TransportError_HttpFailure_SurfacesAsTransportException()
    {
        var stub = new StubHttpHandler
        {
            RawResponder = () => new HttpResponseMessage(HttpStatusCode.BadGateway)
            {
                Content = new StringContent("upstream node unavailable"),
            },
        };
        using var http = new HttpClient(stub);
        using var client = new L2RpcClient(Endpoint, TestChainId, http);

        var ex = await Assert.ThrowsExactlyAsync<L2RpcTransportException>(
            () => client.GetLatestStateRootAsync());
        Assert.AreEqual("getl2stateroot", ex.Method);
        StringAssert.Contains(ex.Message, "502");
    }

    [TestMethod]
    public async Task ProtocolError_MalformedJson_SurfacesAsProtocolException()
    {
        var stub = new StubHttpHandler
        {
            RawResponder = () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{not valid json"),
            },
        };
        using var http = new HttpClient(stub);
        using var client = new L2RpcClient(Endpoint, TestChainId, http);

        var ex = await Assert.ThrowsExactlyAsync<L2RpcProtocolException>(
            () => client.GetLatestStateRootAsync());
        Assert.AreEqual("getl2stateroot", ex.Method);
        StringAssert.Contains(ex.Message, "parse error");
    }

    [TestMethod]
    public async Task MismatchedChainId_SurfacesAsMismatchedException_NotSilentlyConsumed()
    {
        // Server returned chainId=2099 but client is bound to 1099 — likely a
        // misconfigured endpoint pointing at the wrong L2. Surface loudly so a
        // caller doesn't silently consume cross-chain data thinking it's their L2's.
        var stub = new StubHttpHandler();
        using var http = new HttpClient(stub);
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        stub.EnqueueResult(() =>
        {
            var obj = new JObject();
            obj["chainId"] = 2099u;  // wrong!
            obj["level"] = (byte)SecurityLevel.Optimistic;
            obj["levelName"] = SecurityLevel.Optimistic.ToString();
            return obj;
        });

        var ex = await Assert.ThrowsExactlyAsync<L2RpcMismatchedChainIdException>(
            () => client.GetSecurityLevelAsync());
        Assert.AreEqual(TestChainId, ex.Expected);
        Assert.AreEqual(2099u, ex.Got);
    }

    [TestMethod]
    public async Task RequestId_MismatchedFromServer_SurfacesAsProtocolException()
    {
        // JSON-RPC §5 mandates response.id == request.id. A server that returns the
        // wrong id is either confused or interleaving streams; surface as a protocol
        // failure rather than silently consuming the wrong response.
        var stub = new StubHttpHandler
        {
            RawResponder = () =>
            {
                var resp = new JObject();
                resp["jsonrpc"] = "2.0";
                resp["id"] = 99999;  // not what was sent
                resp["result"] = "0x" + new string('a', 64);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(resp.ToString(), System.Text.Encoding.UTF8, "application/json"),
                };
            },
        };
        using var http = new HttpClient(stub);
        using var client = new L2RpcClient(Endpoint, TestChainId, http);

        var ex = await Assert.ThrowsExactlyAsync<L2RpcProtocolException>(
            () => client.GetLatestStateRootAsync());
        StringAssert.Contains(ex.Message, "does not match request id");
    }
}
