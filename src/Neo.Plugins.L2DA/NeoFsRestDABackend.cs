using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Neo.Cryptography;
using Neo.L2;

namespace Neo.Plugins.L2;

/// <summary>
/// Production NeoFS writer backed by the official NeoFS REST Gateway object API.
/// </summary>
/// <remarks>
/// See doc.md §7.4, §12, and §17. Upload uses
/// <c>POST /v1/objects/{containerId}</c>; the returned container/object address is
/// independently retrieved through the supplied <see cref="IProductionDAReader"/> before
/// publication succeeds. The injected <see cref="HttpClient"/> and authenticator are owned
/// by the composition root and may use separate gateway nodes, TLS policy, and key custody.
/// Production handlers must disable automatic redirects so delegated credentials and object
/// payloads cannot be replayed to another origin.
/// </remarks>
public sealed class NeoFsRestDAWriter : IProductionDAWriter
{
    /// <summary>Default maximum accepted NeoFS object size.</summary>
    public const int DefaultMaxObjectBytes = 64 * 1024 * 1024;

    private readonly HttpClient _httpClient;
    private readonly string _containerId;
    private readonly INeoFsRestRequestAuthenticator _authenticator;
    private readonly IProductionDAReader _verificationReader;
    private readonly int _maxObjectBytes;

    /// <summary>Construct a production writer with an independently configured reader.</summary>
    public NeoFsRestDAWriter(
        HttpClient httpClient,
        string containerId,
        INeoFsRestRequestAuthenticator authenticator,
        IProductionDAReader verificationReader,
        int maxObjectBytes = DefaultMaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(verificationReader);
        NeoFsRestProtocol.ValidateHttpClient(httpClient);
        if (verificationReader.Mode != DAMode.NeoFS
            || verificationReader.ReceiptKind != DAReceiptKind.NeoFSObject)
        {
            throw new ArgumentException(
                "NeoFS writer verification reader must be a production NeoFS object reader",
                nameof(verificationReader));
        }
        if (maxObjectBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxObjectBytes));

        _httpClient = httpClient;
        _containerId = NeoFsObjectLocator.ValidateContainerId(containerId);
        _authenticator = authenticator;
        _verificationReader = verificationReader;
        _maxObjectBytes = maxObjectBytes;
    }

    /// <inheritdoc />
    public DAMode Mode => DAMode.NeoFS;

    /// <inheritdoc />
    public DAReceiptKind ReceiptKind => DAReceiptKind.NeoFSObject;

    /// <inheritdoc />
    public async ValueTask<DAReceipt> PublishAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Payload.Length > _maxObjectBytes)
            throw new InvalidOperationException(
                $"NeoFS payload length {request.Payload.Length} exceeds configured maximum {_maxObjectBytes}");

        var payload = request.Payload.ToArray();
        var commitment = new UInt256(Crypto.Hash256(payload));
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1/objects/{Uri.EscapeDataString(_containerId)}")
        {
            Content = new ReadOnlyMemoryContent(payload),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        message.Headers.Add(
            NeoFsRestProtocol.AttributesHeader,
            NeoFsRestProtocol.EncodeAttributes(request, commitment));
        await _authenticator.AuthenticateAsync(message, cancellationToken).ConfigureAwait(false);

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        NeoFsRestProtocol.RequireStatus(response, HttpStatusCode.OK, "upload");

        var responseBytes = await NeoFsRestProtocol.ReadBoundedAsync(
            response.Content,
            NeoFsRestProtocol.MaxUploadResponseBytes,
            cancellationToken).ConfigureAwait(false);
        var uploaded = JsonSerializer.Deserialize<UploadResponse>(responseBytes)
            ?? throw new InvalidDataException("NeoFS REST upload returned an empty JSON response");
        if (!string.Equals(uploaded.ContainerId, _containerId, StringComparison.Ordinal))
            throw new InvalidDataException("NeoFS REST upload returned a different container identifier");

        NeoFsObjectLocator locator;
        try
        {
            locator = new NeoFsObjectLocator(uploaded.ContainerId, uploaded.ObjectId);
        }
        catch (ArgumentException error)
        {
            throw new InvalidDataException("NeoFS REST upload returned an invalid object address", error);
        }

        var receipt = new DAReceipt
        {
            Commitment = commitment,
            Pointer = locator.ToPointer(),
            Evidence = NeoFsRestProtocol.Evidence.ToArray(),
            Kind = ReceiptKind,
            Layer = Mode,
        };

        var retrieved = await _verificationReader.ReadAsync(receipt, cancellationToken).ConfigureAwait(false);
        if (retrieved is null || !retrieved.Value.Span.SequenceEqual(payload))
            throw new InvalidOperationException(
                $"NeoFS object {locator.ProtocolAddress} failed independent read-after-write verification");
        return receipt;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Commitment);
        if (!NeoFsRestProtocol.IsReceipt(receipt)) return false;
        return await _verificationReader.ReadAsync(receipt, cancellationToken).ConfigureAwait(false)
            is not null;
    }

    private sealed record UploadResponse
    {
        [JsonPropertyName("container_id")]
        public required string ContainerId { get; init; }

        [JsonPropertyName("object_id")]
        public required string ObjectId { get; init; }
    }
}

/// <summary>
/// Independently configured production reader for NeoFS REST Gateway object addresses.
/// </summary>
/// <remarks>
/// See doc.md §7.4, §12, and §17. Retrieval uses
/// <c>GET /v1/objects/{containerId}/by_id/{objectId}</c>, requires the official locator
/// response headers, bounds the payload, and verifies the canonical Hash256 commitment.
/// Production handlers must disable automatic redirects.
/// </remarks>
public sealed class NeoFsRestDAReader : IProductionDAReader
{
    private readonly HttpClient _httpClient;
    private readonly INeoFsRestRequestAuthenticator _authenticator;
    private readonly int _maxObjectBytes;

    /// <summary>Construct an independent NeoFS reader.</summary>
    public NeoFsRestDAReader(
        HttpClient httpClient,
        INeoFsRestRequestAuthenticator authenticator,
        int maxObjectBytes = NeoFsRestDAWriter.DefaultMaxObjectBytes)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(authenticator);
        NeoFsRestProtocol.ValidateHttpClient(httpClient);
        if (maxObjectBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxObjectBytes));

        _httpClient = httpClient;
        _authenticator = authenticator;
        _maxObjectBytes = maxObjectBytes;
    }

    /// <inheritdoc />
    public DAMode Mode => DAMode.NeoFS;

    /// <inheritdoc />
    public DAReceiptKind ReceiptKind => DAReceiptKind.NeoFSObject;

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(receipt.Commitment);
        if (!NeoFsRestProtocol.IsReceipt(receipt)
            || !NeoFsObjectLocator.TryParsePointer(receipt.Pointer.Span, out var locator))
        {
            return null;
        }

        using var message = new HttpRequestMessage(
            HttpMethod.Get,
            $"v1/objects/{Uri.EscapeDataString(locator!.ContainerId)}/by_id/{Uri.EscapeDataString(locator.ObjectId)}");
        await _authenticator.AuthenticateAsync(message, cancellationToken).ConfigureAwait(false);

        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        NeoFsRestProtocol.RequireStatus(response, HttpStatusCode.OK, "download");
        NeoFsRestProtocol.RequireLocatorHeaders(response, locator);

        var payload = await NeoFsRestProtocol.ReadBoundedAsync(
            response.Content,
            _maxObjectBytes,
            cancellationToken).ConfigureAwait(false);
        if (!Crypto.Hash256(payload).AsSpan().SequenceEqual(receipt.Commitment.GetSpan()))
            return null;
        return payload;
    }
}

internal static class NeoFsRestProtocol
{
    internal const string AttributesHeader = "X-Attributes-Base64";
    internal const int MaxUploadResponseBytes = 16 * 1024;
    internal static ReadOnlySpan<byte> Evidence => "neo-n4:neofs-rest-gateway:v1"u8;

    internal static void ValidateHttpClient(HttpClient httpClient)
    {
        var endpoint = httpClient.BaseAddress
            ?? throw new ArgumentException(
                "NeoFS REST HttpClient must have an absolute HTTPS BaseAddress",
                nameof(httpClient));
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException(
                "NeoFS REST HttpClient BaseAddress must use HTTPS",
                nameof(httpClient));
        if (!string.IsNullOrEmpty(endpoint.UserInfo)
            || !string.IsNullOrEmpty(endpoint.Query)
            || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException(
                "NeoFS REST HttpClient BaseAddress cannot contain credentials, query, or fragment",
                nameof(httpClient));
        }
        if (!endpoint.AbsolutePath.EndsWith("/", StringComparison.Ordinal))
            throw new ArgumentException(
                "NeoFS REST HttpClient BaseAddress path must end with '/'",
                nameof(httpClient));
    }

    internal static bool IsReceipt(DAReceipt receipt)
        => receipt.HasRequiredMetadata(DAMode.NeoFS, DAReceiptKind.NeoFSObject)
            && receipt.Evidence.Span.SequenceEqual(Evidence);

    internal static string EncodeAttributes(DAPublishRequest request, UInt256 commitment)
    {
        var attributes = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["NeoN4BatchNumber"] = request.BatchNumber.ToString(CultureInfo.InvariantCulture),
            ["NeoN4ChainId"] = request.ChainId.ToString(CultureInfo.InvariantCulture),
            ["NeoN4PayloadHash256"] = Convert.ToHexString(commitment.GetSpan()),
        };
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(attributes));
    }

    internal static void RequireStatus(
        HttpResponseMessage response,
        HttpStatusCode expected,
        string operation)
    {
        if (response.StatusCode == expected) return;
        if (!response.IsSuccessStatusCode) response.EnsureSuccessStatusCode();
        throw new InvalidDataException(
            $"NeoFS REST {operation} returned unexpected success status {(int)response.StatusCode}");
    }

    internal static void RequireLocatorHeaders(
        HttpResponseMessage response,
        NeoFsObjectLocator locator)
    {
        var containerId = GetRequiredSingleHeader(response, "X-Container-Id");
        var objectId = GetRequiredSingleHeader(response, "X-Object-Id");
        if (!string.Equals(containerId, locator.ContainerId, StringComparison.Ordinal)
            || !string.Equals(objectId, locator.ObjectId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "NeoFS REST download response headers do not match the requested object address");
        }
    }

    internal static async ValueTask<byte[]> ReadBoundedAsync(
        HttpContent content,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Headers.ContentLength is long declaredLength
            && declaredLength > maximumBytes)
        {
            throw new InvalidDataException(
                $"NeoFS REST response length {declaredLength} exceeds configured maximum {maximumBytes}");
        }

        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream(
            content.Headers.ContentLength is > 0 and <= int.MaxValue
                ? (int)content.Headers.ContentLength.Value
                : 0);
        var buffer = ArrayPool<byte>.Shared.Rent(81_920);
        try
        {
            while (true)
            {
                var read = await input.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                if (output.Length + read > maximumBytes)
                    throw new InvalidDataException(
                        $"NeoFS REST response exceeds configured maximum {maximumBytes}");
                output.Write(buffer, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
        return output.ToArray();
    }

    private static string GetRequiredSingleHeader(HttpResponseMessage response, string name)
    {
        IEnumerable<string> values;
        if (!response.Headers.TryGetValues(name, out values!)
            && !response.Content.Headers.TryGetValues(name, out values!))
        {
            throw new InvalidDataException($"NeoFS REST response is missing required {name} header");
        }

        var array = values.ToArray();
        if (array.Length != 1 || string.IsNullOrWhiteSpace(array[0]))
            throw new InvalidDataException($"NeoFS REST response has an invalid {name} header");
        return array[0];
    }
}
