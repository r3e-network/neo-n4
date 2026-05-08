using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Neo.Json;
using Neo.L2;
using Neo.L2.Sdk;
using Neo.Plugins.L2Rpc;

namespace Neo.L2.Explore.UnitTests;

/// <summary>
/// Test-only client factory: wires <see cref="L2RpcClient"/> to a real
/// <see cref="L2RpcMethods"/> dispatcher via an in-memory bridge handler. Each
/// command test uses this to exercise the FULL request/response cycle through
/// the SDK against a configurable in-memory store, no real network.
/// </summary>
internal sealed class StubBackedClient
{
    public InMemoryL2RpcStore Store { get; }
    private readonly L2RpcMethods _methods;
    private readonly HttpClient _http;

    public StubBackedClient(uint chainId, SecurityLevel level = SecurityLevel.Optimistic)
    {
        Store = new InMemoryL2RpcStore(chainId, level)
        {
            DAMode = DAMode.L1,
            GatewayEnabled = false,
            Sequencer = SequencerModel.DbftCommittee,
            Exit = ExitModel.Delayed,
        };
        _methods = new L2RpcMethods(Store);
        _http = new HttpClient(new BridgeHandler(_methods));
    }

    /// <summary>
    /// Factory matching <see cref="Program.NewClient"/>'s shape. Every command's
    /// <c>RunAsync</c> takes this delegate so tests substitute the live HTTP
    /// HttpClient with the in-memory bridge.
    /// </summary>
    public Func<string, uint, HttpClient?, L2RpcClient> Factory =>
        (endpoint, chainId, _) => new L2RpcClient(endpoint, chainId, _http);

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

            JToken? result = null;
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
                error = new JObject { ["code"] = -32603, ["message"] = ex.Message };
            }

            var resp = new JObject { ["jsonrpc"] = "2.0", ["id"] = id };
            if (error is not null) resp["error"] = error;
            else resp["result"] = result;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(resp.ToString(), System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }
}
