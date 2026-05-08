using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Neo.Json;

namespace Neo.L2.Messaging.UnitTests;

internal sealed class StubRpcHandler : HttpMessageHandler
{
    public delegate JToken? Handler(string method, string contractHash, JArray contractParams);

    private readonly List<Handler> _handlers = new();
    public readonly List<(string Method, string Hash, JArray Params)> Captured = new();

    public void Register(Handler handler) => _handlers.Add(handler);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var body = await request.Content!.ReadAsStringAsync(ct);
        var parsed = (JObject)JToken.Parse(body)!;
        var rpcMethod = parsed["method"]!.AsString();
        var id = (long)((JNumber)parsed["id"]!).AsNumber();
        var rpcParams = (JArray)parsed["params"]!;

        if (rpcMethod != "invokefunction")
            throw new InvalidOperationException($"unexpected RPC method '{rpcMethod}'");
        var contractHash = rpcParams[0]!.AsString();
        var contractMethod = rpcParams[1]!.AsString();
        var contractArgs = (JArray)rpcParams[2]!;
        Captured.Add((contractMethod, contractHash, contractArgs));

        JToken? stackTop = null;
        foreach (var h in _handlers)
        {
            stackTop = h(contractMethod, contractHash, contractArgs);
            if (stackTop is not null) break;
        }
        if (stackTop is null)
            throw new InvalidOperationException($"no stub handler matched '{contractMethod}'");

        var resp = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = new JObject
            {
                ["state"] = "HALT",
                ["stack"] = new JArray(stackTop),
            },
        };
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(resp.ToString(), System.Text.Encoding.UTF8, "application/json"),
        };
    }

    public static JObject Boolean(bool value) => new() { ["type"] = "Boolean", ["value"] = value ? "true" : "false" };
    public static JObject ByteArrayBase64(byte[] bytes) => new() { ["type"] = "ByteString", ["value"] = Convert.ToBase64String(bytes) };
}
