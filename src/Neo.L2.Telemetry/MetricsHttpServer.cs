using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Neo.L2.Telemetry;

/// <summary>
/// Minimal background HTTP server that exposes <see cref="MetricsRequestHandler"/> over a
/// raw <see cref="Socket"/>. Speaks HTTP/1.0 well enough to satisfy a Prometheus scrape
/// (request line + headers + body, <c>Connection: close</c>). Avoids the host-side
/// <c>HttpListener</c> stack so behavior is consistent across Linux / macOS / Windows.
/// </summary>
/// <remarks>
/// For production deployments that already run an HTTP framework (ASP.NET, Kestrel, RpcServer
/// plugin), prefer routing GET <c>/metrics</c> directly into <see cref="MetricsRequestHandler.Handle"/>
/// rather than starting another listener. Bind to localhost unless you front the endpoint
/// with a reverse proxy.
/// </remarks>
public sealed class MetricsHttpServer : IDisposable
{
    private readonly Socket _listener;
    private readonly MetricsRequestHandler _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>The concrete IP endpoint the server bound. Useful when constructing with port 0.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>True when <see cref="Start"/> has been called and <see cref="Dispose"/> has not. Useful for diagnostics + integration tests.</summary>
    public bool IsRunning => _loop is not null && !_cts.IsCancellationRequested;

    /// <summary>Construct a server that binds to <paramref name="endpoint"/>.</summary>
    /// <param name="endpoint">IP endpoint to bind. Use port 0 for "any free port".</param>
    /// <param name="handler">Request handler to dispatch requests through.</param>
    public MetricsHttpServer(IPEndPoint endpoint, MetricsRequestHandler handler)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(handler);
        _listener = new Socket(endpoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(endpoint);
        Endpoint = (IPEndPoint)_listener.LocalEndPoint!;
        _handler = handler;
    }

    /// <summary>Construct a server that binds to <paramref name="address"/> and <paramref name="port"/>.</summary>
    public MetricsHttpServer(IPAddress address, int port, MetricsRequestHandler handler)
        : this(MakeValidatedEndpoint(address, port), handler) { }

    private static IPEndPoint MakeValidatedEndpoint(IPAddress address, int port)
    {
        // Surface a clear "port out of range" via PortValidator instead of IPEndPoint's
        // generic "port" ArgumentOutOfRangeException. IPAddress null-guard is delegated
        // to IPEndPoint (which throws ArgumentNullException with arg name "address" —
        // already clear).
        Neo.L2.Telemetry.PortValidator.Validate(port, "MetricsHttpServer port");
        return new IPEndPoint(address, port);
    }

    private readonly Lock _startGate = new();

    /// <summary>Start accepting requests. Runs until <see cref="Dispose"/>. Idempotent — extra calls are no-ops.</summary>
    public void Start()
    {
        lock (_startGate)
        {
            if (_loop is not null) return;
            _listener.Listen();
            _loop = Task.Run(AcceptLoopAsync);
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            Socket client;
            try { client = await _listener.AcceptAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = Task.Run(() => HandleConnectionAsync(client));
        }
    }

    private async Task HandleConnectionAsync(Socket client)
    {
        // Apply a per-connection deadline so a slow client can't pin a worker forever.
        // NetworkStream.ReadTimeout doesn't apply to ReadAsync, so we use a CTS instead.
        using var deadlineCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, deadlineCts.Token);

        try
        {
            using (client)
            using (var stream = new NetworkStream(client, ownsSocket: false))
            {
                var path = await ReadRequestPathAsync(stream, linkedCts.Token).ConfigureAwait(false);
                if (path is null) return;

                var response = _handler.Handle(path);
                await WriteResponseAsync(stream, response, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // best-effort — never let a bad client take down the server
        }
    }

    private static async Task<string?> ReadRequestPathAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Read the request line + minimal headers up to the blank line. We only care about path.
        var buf = new byte[4096];
        var total = 0;
        while (total < buf.Length)
        {
            var n = await stream.ReadAsync(buf.AsMemory(total, buf.Length - total), cancellationToken).ConfigureAwait(false);
            if (n <= 0) break;
            total += n;
            // crlf-crlf or lf-lf indicates end of headers
            var s = Encoding.ASCII.GetString(buf, 0, total);
            if (s.Contains("\r\n\r\n", StringComparison.Ordinal) || s.Contains("\n\n", StringComparison.Ordinal))
            {
                var firstLineEnd = s.IndexOf('\n');
                if (firstLineEnd < 0) return null;
                var line = s[..firstLineEnd].TrimEnd('\r');
                // METHOD PATH HTTP/x.y
                var parts = line.Split(' ');
                return parts.Length >= 2 ? parts[1] : null;
            }
        }
        return null;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, MetricsHttpResponse response, CancellationToken cancellationToken)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(response.Body);
        var headers =
            $"HTTP/1.0 {response.StatusCode} {StatusText(response.StatusCode)}\r\n" +
            $"Content-Type: {response.ContentType}\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(headers);
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, cancellationToken).ConfigureAwait(false);
    }

    private static string StatusText(int code) => code switch
    {
        200 => "OK",
        // 503 is the readiness-probe failure response (MetricsRequestHandler.HandleReady).
        // Without this case the response wire-form was "HTTP/1.0 503 OK" — clients would
        // see a 503 status with the OK reason phrase, a strict-parser confusion vector.
        503 => "Service Unavailable",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "OK",
    };

    /// <inheritdoc />
    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* swallow */ }
        try { _listener.Dispose(); } catch { /* swallow */ }
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* swallow */ }
        _cts.Dispose();
    }
}
