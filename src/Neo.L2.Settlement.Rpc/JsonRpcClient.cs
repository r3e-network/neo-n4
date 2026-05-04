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
        ArgumentNullException.ThrowIfNull(endpoint);
        // Reject non-HTTP schemes at construction so a misconfigured endpoint
        // (file://, mailto:, ftp://, etc.) doesn't surface as an obscure HttpClient
        // SendAsync error at first request. JsonRpcClient is HTTP/HTTPS only.
        if (endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException(
                $"endpoint scheme '{endpoint.Scheme}' must be http or https",
                nameof(endpoint));
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

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Caller-driven cancellation: propagate so they get the OperationCanceledException
            // they expected.
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // Timeout — caller didn't cancel, but the SendAsync gave up (HttpClient.Timeout).
            throw new JsonRpcException(-32603, $"RPC timeout: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            // Network-level failure (connection refused, DNS, TLS).
            throw new JsonRpcException(-32603, $"HTTP send failed: {ex.Message}");
        }
        using var _response = response;
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // Symmetric with the parse-error wrapping below: a non-2xx status (proxy 502,
            // server 500, etc.) gets a JsonRpcException so callers handle one type.
            // -32603 is the JSON-RPC 2.0 spec code for "Internal error".
            var snippet = responseBody.Length <= 200 ? responseBody : responseBody[..200];
            throw new JsonRpcException(-32603, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}");
        }

        JToken? parsed;
        try { parsed = JToken.Parse(responseBody); }
        catch (Exception ex)
        {
            // -32700 is the JSON-RPC 2.0 spec code for "Parse error". Wrapping here gives
            // callers a uniform exception type to handle (JsonRpcException) regardless of
            // whether the failure originated server-side or in the response body.
            throw new JsonRpcException(-32700, $"failed to parse RPC response: {ex.Message}");
        }
        if (parsed is not JObject obj)
            throw new JsonRpcException(-32600, $"unexpected response: {responseBody.Substring(0, Math.Min(200, responseBody.Length))}");

        // Validate the response's id matches the request's. JSON-RPC 2.0 spec §5
        // mandates this for response correlation. Although we rely on HTTP one-request-
        // at-a-time correlation here (so a mismatch can't actually misroute a response),
        // accepting a mismatched id silently masks server bugs / proxy misconfiguration
        // / a confused upstream that's interleaving streams. Reject loudly.
        var responseIdToken = obj["id"];
        var responseId = responseIdToken is JNumber rn ? (long)rn.AsNumber()
            : responseIdToken is JString rs && long.TryParse(rs.AsString(), out var rsi) ? rsi
            : -1L;
        if (responseId != id)
            throw new JsonRpcException(-32603,
                $"response id {responseId} does not match request id {id}");

        if (obj["error"] is JObject err)
        {
            // Bound-check the server-supplied code before casting double → int. A
            // pathological server response with code = 1e100 or 2^32 would wrap to
            // int.MinValue (or undefined behavior) — the JsonRpcException would carry
            // the wrong code and operators chasing the value in dashboards see a
            // confusing -2147483648. Clamp to the JSON-RPC 2.0 spec error-code range.
            var code = -32603;
            if (err["code"] is JNumber n)
            {
                var d = n.AsNumber();
                if (d >= int.MinValue && d <= int.MaxValue) code = (int)d;
                // Out-of-range: keep the -32603 internal-error sentinel as a stand-in.
            }
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
