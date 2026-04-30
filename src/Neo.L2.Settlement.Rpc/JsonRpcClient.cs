using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using Neo.Json;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// Minimal JSON-RPC 2.0 client over HTTP. Built on <see cref="HttpClient"/> + Neo's
/// <see cref="JToken"/> serializer; no third-party dependency.
/// </summary>
public sealed class JsonRpcClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private long _nextId;
    private bool _ownsHttp;

    /// <summary>Construct against an endpoint URI; allocates a default <see cref="HttpClient"/>.</summary>
    public JsonRpcClient(string endpoint) : this(new Uri(endpoint), httpClient: null)
    { }

    /// <summary>Construct with an explicit endpoint and optional caller-owned <see cref="HttpClient"/>.</summary>
    public JsonRpcClient(Uri endpoint, HttpClient? httpClient)
    {
        _endpoint = endpoint;
        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttp = false;
        }
    }

    /// <summary>The endpoint URI this client targets.</summary>
    public Uri Endpoint => _endpoint;

    /// <summary>
    /// Send a single JSON-RPC 2.0 request. Returns the <c>result</c> token on success;
    /// throws <see cref="JsonRpcException"/> on RPC error.
    /// </summary>
    public async ValueTask<JToken?> CallAsync(string method, JArray @params, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(@params);

        var id = Interlocked.Increment(ref _nextId);
        var envelope = new JObject();
        envelope["jsonrpc"] = "2.0";
        envelope["method"] = method;
        envelope["params"] = @params;
        envelope["id"] = id;

        var requestBody = envelope.ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        var parsed = JToken.Parse(responseBody);
        if (parsed is not JObject obj)
            throw new JsonRpcException(-32600, $"unexpected response: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");

        if (obj["error"] is JObject err)
        {
            var code = err["code"] is JNumber n ? (int)n.AsNumber() : -32603;
            var message = err["message"]?.AsString() ?? "rpc error";
            throw new JsonRpcException(code, message);
        }

        return obj["result"];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
            _ownsHttp = false;
        }
    }
}

/// <summary>RPC-side error returned by a JSON-RPC peer.</summary>
public sealed class JsonRpcException : Exception
{
    /// <summary>RPC error code (per JSON-RPC 2.0 spec).</summary>
    public int Code { get; }

    /// <summary>Construct.</summary>
    public JsonRpcException(int code, string message) : base($"jsonrpc {code}: {message}")
    {
        Code = code;
    }
}
