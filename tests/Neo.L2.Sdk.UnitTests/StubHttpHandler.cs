using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Neo.Json;

namespace Neo.L2.Sdk.UnitTests;

/// <summary>
/// Test-only <see cref="HttpMessageHandler"/> that captures requests + returns
/// caller-supplied JSON-RPC responses. Each <see cref="Reply"/> is consumed once
/// per call in FIFO order; out-of-order calls or unexpected methods get
/// <see cref="Replies"/> appended for the test to assert on.
/// </summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    public readonly List<(string Method, JArray Params, long Id)> Captured = new();
    public readonly Queue<Func<long, JObject>> Responders = new();
    public Func<HttpResponseMessage>? RawResponder;

    /// <summary>Queue a successful response builder. The builder receives the request's id.</summary>
    public void EnqueueResult(Func<JToken?> resultBuilder)
    {
        Responders.Enqueue(id =>
        {
            var resp = new JObject();
            resp["jsonrpc"] = "2.0";
            resp["id"] = id;
            resp["result"] = resultBuilder();
            return resp;
        });
    }

    /// <summary>Queue a JSON-RPC error response.</summary>
    public void EnqueueError(int code, string message)
    {
        Responders.Enqueue(id =>
        {
            var resp = new JObject();
            resp["jsonrpc"] = "2.0";
            resp["id"] = id;
            var err = new JObject();
            err["code"] = code;
            err["message"] = message;
            resp["error"] = err;
            return resp;
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (RawResponder is not null)
            return RawResponder();

        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var parsed = (JObject)JToken.Parse(body)!;
        var method = parsed["method"]!.AsString();
        var @params = (JArray)parsed["params"]!;
        var id = (long)((JNumber)parsed["id"]!).AsNumber();
        Captured.Add((method, @params, id));

        if (Responders.Count == 0)
            throw new InvalidOperationException($"StubHttpHandler: unexpected call to '{method}' with no queued responder");
        var responder = Responders.Dequeue();
        var responseObj = responder(id);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseObj.ToString(), System.Text.Encoding.UTF8, "application/json"),
        };
    }
}
