using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Neo.L2.Telemetry;

/// <summary>
/// Minimal background HTTP server that exposes <see cref="MetricsRequestHandler"/> over a
/// raw <see cref="TcpListener"/>. Speaks HTTP/1.0 well enough to satisfy a Prometheus scrape
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
    private readonly TcpListener _listener;
    private readonly MetricsRequestHandler _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    /// <summary>The concrete IP endpoint the server bound. Useful when constructing with port 0.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>Construct a server that binds to <paramref name="endpoint"/>.</summary>
    /// <param name="endpoint">IP endpoint to bind. Use port 0 for "any free port".</param>
    /// <param name="handler">Request handler to dispatch requests through.</param>
    public MetricsHttpServer(IPEndPoint endpoint, MetricsRequestHandler handler)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(handler);
        _listener = new TcpListener(endpoint);
        _listener.Start();
        Endpoint = (IPEndPoint)_listener.LocalEndpoint;
        _handler = handler;
    }

    /// <summary>Construct a server that binds to <paramref name="address"/> and <paramref name="port"/>.</summary>
    public MetricsHttpServer(IPAddress address, int port, MetricsRequestHandler handler)
        : this(new IPEndPoint(address, port), handler) { }

    /// <summary>Start accepting requests. Runs until <see cref="Dispose"/>.</summary>
    public void Start()
    {
        _loop = Task.Run(AcceptLoopAsync);
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
            catch (SocketException) { return; }
            catch (ObjectDisposedException) { return; }

            _ = Task.Run(() => HandleConnectionAsync(client));
        }
    }

    private async Task HandleConnectionAsync(TcpClient client)
    {
        // Apply a per-connection deadline so a slow client can't pin a worker forever.
        // NetworkStream.ReadTimeout doesn't apply to ReadAsync, so we use a CTS instead.
        using var deadlineCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, deadlineCts.Token);

        try
        {
            using (client)
            using (var stream = client.GetStream())
            {
                var path = await ReadRequestPathAsync(stream, linkedCts.Token).ConfigureAwait(false);
                if (path is null) return;

                var response = _handler.Handle(path);
                await WriteResponseAsync(stream, response, linkedCts.Token).ConfigureAwait(false);
            }
        }
        catch
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
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "OK",
    };

    /// <inheritdoc />
    public void Dispose()
    {
        try { _cts.Cancel(); } catch { /* swallow */ }
        try { _listener.Stop(); } catch { /* swallow */ }
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { /* swallow */ }
        _cts.Dispose();
    }
}
