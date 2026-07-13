using System.Net.Http.Headers;

namespace Neo.Plugins.L2;

/// <summary>
/// Applies operator-supplied NeoFS REST Gateway authentication to one request.
/// </summary>
/// <remarks>
/// See doc.md §7.4, §12, and §17. Implementations may attach a v2 session token,
/// NeoFS bearer token and signature headers, or an HSM/KMS-backed delegated credential.
/// </remarks>
public interface INeoFsRestRequestAuthenticator
{
    /// <summary>Authenticate a request immediately before it is sent.</summary>
    ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Applies a rotating NeoFS REST Gateway v2 session token through HTTP Bearer authentication.
/// </summary>
/// <remarks>See doc.md §7.4, §12, and §17.</remarks>
public sealed class NeoFsRestSessionTokenAuthenticator : INeoFsRestRequestAuthenticator
{
    private readonly Func<CancellationToken, ValueTask<string>> _tokenProvider;

    /// <summary>Construct with a request-scoped token provider suitable for wallet, HSM, or KMS custody.</summary>
    public NeoFsRestSessionTokenAuthenticator(
        Func<CancellationToken, ValueTask<string>> tokenProvider)
    {
        ArgumentNullException.ThrowIfNull(tokenProvider);
        _tokenProvider = tokenProvider;
    }

    /// <inheritdoc />
    public async ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var token = await _tokenProvider(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token)
            || token.AsSpan().IndexOfAny('\r', '\n') >= 0)
        {
            throw new InvalidOperationException(
                "NeoFS REST session token provider returned an empty or invalid token");
        }

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }
}

/// <summary>
/// Explicit anonymous authentication for containers whose NeoFS EACL permits the operation.
/// </summary>
/// <remarks>
/// See doc.md §7.4, §12, and §17. Selecting this implementation is an operator decision;
/// the production adapter never silently falls back to anonymous requests.
/// </remarks>
public sealed class NeoFsRestAnonymousAuthenticator : INeoFsRestRequestAuthenticator
{
    private NeoFsRestAnonymousAuthenticator()
    {
    }

    /// <summary>Shared stateless instance.</summary>
    public static NeoFsRestAnonymousAuthenticator Instance { get; } = new();

    /// <inheritdoc />
    public ValueTask AuthenticateAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }
}
