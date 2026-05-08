using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Neo;
using Neo.Json;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Sdk;
using Neo.Plugins.L2Rpc;

namespace Neo.L2.Sdk.UnitTests;

/// <summary>
/// Integration-style pin: wires the SDK's HTTP request to a real
/// <see cref="L2RpcMethods"/> dispatcher (through an in-process bridge handler
/// that decodes the SDK's JSON-RPC envelope, calls the method, and re-encodes
/// the result). If the SDK and the server-side method ever drift in their
/// param shape or response decoding, these tests catch it.
/// </summary>
/// <remarks>
/// This complements the canned-response tests in <c>UT_L2RpcClient_HappyPath</c>
/// by exercising the actual server logic (not a hand-written response). The
/// bridge handler is HTTP-free — it's an in-memory shortcut, not a real server —
/// so the tests stay fast.
/// </remarks>
[TestClass]
public class UT_L2RpcClient_ContractWithServer
{
    private const uint TestChainId = 4242;
    private const string Endpoint = "http://node.example:30332";

    private sealed class BridgeHandler : HttpMessageHandler
    {
        private readonly L2RpcMethods _methods;
        public BridgeHandler(L2RpcMethods methods) { _methods = methods; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = await request.Content!.ReadAsStringAsync(ct);
            var envelope = (JObject)JToken.Parse(body)!;
            var method = envelope["method"]!.AsString();
            var @params = (JArray)envelope["params"]!;
            var id = (long)((JNumber)envelope["id"]!).AsNumber();

            JToken? result;
            JObject? error = null;
            try
            {
                result = method switch
                {
                    "getl2batch" => _methods.GetL2Batch(@params),
                    "getl2batchstatus" => _methods.GetL2BatchStatus(@params),
                    "getl2stateroot" => _methods.GetL2StateRoot(@params),
                    "getl2withdrawalproof" => _methods.GetL2WithdrawalProof(@params),
                    "getl2messageproof" => _methods.GetL2MessageProof(@params),
                    "getl1depositstatus" => _methods.GetL1DepositStatus(@params),
                    "getcanonicalasset" => _methods.GetCanonicalAsset(@params),
                    "getbridgedasset" => _methods.GetBridgedAsset(@params),
                    "getsecuritylevel" => _methods.GetSecurityLevel(@params),
                    "getsecuritylabel" => _methods.GetSecurityLabel(@params),
                    _ => throw new ArgumentException($"unknown method '{method}'"),
                };
            }
            catch (Exception ex)
            {
                result = null;
                error = new JObject { ["code"] = -32603, ["message"] = ex.Message };
            }

            var resp = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
            };
            if (error is not null) resp["error"] = error;
            else resp["result"] = result;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(resp.ToString(), System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (L2RpcClient client, InMemoryL2RpcStore store) NewWiredClient()
    {
        var store = new InMemoryL2RpcStore(TestChainId, SecurityLevel.Optimistic)
        {
            DAMode = DAMode.L1,
            GatewayEnabled = false,
            Sequencer = SequencerModel.DbftCommittee,
            Exit = ExitModel.Delayed,
        };
        var methods = new L2RpcMethods(store);
        var http = new HttpClient(new BridgeHandler(methods));
        var client = new L2RpcClient(Endpoint, TestChainId, http);
        return (client, store);
    }

    [TestMethod]
    public async Task SecurityLabel_RoundTripsThroughActualServer()
    {
        var (client, _) = NewWiredClient();
        var got = await client.GetSecurityLabelAsync();
        Assert.AreEqual(TestChainId, got.ChainId);
        Assert.AreEqual(SecurityLevel.Optimistic, got.SecurityLevel);
        Assert.AreEqual(DAMode.L1, got.DAMode);
        Assert.IsFalse(got.GatewayEnabled);
        Assert.AreEqual(SequencerModel.DbftCommittee, got.Sequencer);
        Assert.AreEqual(ExitModel.Delayed, got.Exit);
    }

    [TestMethod]
    public async Task SecurityLevel_RoundTripsThroughActualServer()
    {
        var (client, _) = NewWiredClient();
        var got = await client.GetSecurityLevelAsync();
        Assert.AreEqual(TestChainId, got.ChainId);
        Assert.AreEqual(SecurityLevel.Optimistic, got.Level);
    }

    [TestMethod]
    public async Task GetBatch_UnknownNumber_ReturnsNull()
    {
        var (client, _) = NewWiredClient();
        var got = await client.GetBatchAsync(99999);
        Assert.IsNull(got);
    }

    [TestMethod]
    public async Task CanonicalAsset_RoundTripsThroughActualServer()
    {
        // Pin the asset-mapping wire path: register on the server, fetch via SDK,
        // assert both directions resolve correctly. Catches SDK ↔ server drift in
        // UInt160 string formatting.
        var (client, store) = NewWiredClient();
        var l1 = UInt160.Parse("0x" + new string('a', 40));
        var l2 = UInt160.Parse("0x" + new string('b', 40));
        store.RegisterAsset(l1, l2);

        var canonical = await client.GetCanonicalAssetAsync(l2);
        var bridged = await client.GetBridgedAssetAsync(l1);

        Assert.AreEqual(l1, canonical, "L2 → L1 canonical lookup");
        Assert.AreEqual(l2, bridged, "L1 → L2 bridged lookup");
    }

    [TestMethod]
    public async Task WithdrawalProof_RoundTripsThroughActualServer()
    {
        // Pin the hex-encoded byte path between SDK and server. Catches a regression
        // where one side encodes upper / lower / mixed case and the other can't decode.
        var (client, store) = NewWiredClient();
        var leaf = UInt256.Parse("0x" + new string('e', 64));
        var proofBytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0xCA, 0xFE };
        store.RecordWithdrawalProof(leaf, proofBytes);

        var got = await client.GetWithdrawalProofAsync(leaf);
        CollectionAssert.AreEqual(proofBytes, got);
    }

    [TestMethod]
    public async Task ServerSideMismatchedChainId_SurfacesAsServerError()
    {
        // Server's L2RpcMethods rejects chainId mismatch via ArgumentException;
        // the bridge handler wraps it as a -32603 server error, and the SDK surfaces
        // it as L2RpcServerException. End-to-end pin that operator-supplied wrong
        // chainId at SDK construction never gets through silently.
        var (_, store) = NewWiredClient();
        var http = new HttpClient(new BridgeHandler(new L2RpcMethods(store)));
        using var wrongClient = new L2RpcClient(Endpoint, chainId: 1, http);  // server runs 4242

        var ex = await Assert.ThrowsExactlyAsync<L2RpcServerException>(
            () => wrongClient.GetSecurityLevelAsync());
        StringAssert.Contains(ex.Message, "differs from local");
    }
}
