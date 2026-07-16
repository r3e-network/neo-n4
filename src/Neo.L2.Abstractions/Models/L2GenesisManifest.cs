using System.Text.Json;

namespace Neo.L2;

/// <summary>
/// Reader for <c>genesis-manifest.json</c> written by <c>neo-stack bootstrap-genesis</c>.
/// </summary>
/// <remarks>
/// See doc.md §8 / §14.2. The <c>initialStateRoot</c> is the batch-1 trust anchor used by
/// <c>register-chain</c> and by <c>ProofWitnessPipelineProfile</c> / SP1 execution stacks.
/// </remarks>
public static class L2GenesisManifest
{
    /// <summary>Default relative path under a chain working directory.</summary>
    public const string RelativePath = "genesis-manifest.json";

    /// <summary>
    /// Read <c>initialStateRoot</c> (camelCase or PascalCase) from a genesis manifest file.
    /// Fails closed on missing file, missing field, zero root, or invalid UInt256.
    /// </summary>
    public static UInt256 ReadInitialStateRoot(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        var fullPath = Path.GetFullPath(manifestPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("genesis manifest not found", fullPath);

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if ((!document.RootElement.TryGetProperty("initialStateRoot", out var rootEl)
                && !document.RootElement.TryGetProperty("InitialStateRoot", out rootEl))
            || rootEl.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("genesis manifest missing initialStateRoot");

        var text = rootEl.GetString()
            ?? throw new InvalidDataException("genesis manifest initialStateRoot is null");
        if (string.IsNullOrWhiteSpace(text)
            || !UInt256.TryParse(text, out var root)
            || root is null
            || root.Equals(UInt256.Zero))
            throw new InvalidDataException(
                "genesis manifest initialStateRoot must be a non-zero UInt256");
        return root;
    }

    /// <summary>
    /// Read <c>initialStateRoot</c> from <c>{chainDirectory}/genesis-manifest.json</c>.
    /// </summary>
    public static UInt256 ReadInitialStateRootFromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        return ReadInitialStateRoot(
            Path.Combine(Path.GetFullPath(chainDirectory), RelativePath));
    }
}
