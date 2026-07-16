using System.Text.Json;
using Neo.Cryptography.ECC;
using NeoECPoint = Neo.Cryptography.ECC.ECPoint;

namespace Neo.L2.Sequencer;

/// <summary>
/// Reads sequencer public keys from <c>chain.config.json</c> and builds seal-time
/// <c>sequencerCommitteeHash</c> providers for <c>WireProduction</c> / batch sealing.
/// </summary>
/// <remarks>
/// See doc.md §7.1 / §14.2. Production hosts that track live L1 registry membership should
/// prefer <see cref="SequencerCommitteeHasher.CreateSyncProvider"/> over
/// <see cref="RpcSequencerCommitteeProvider"/>. This helper covers the static genesis path
/// when <c>validators</c> in chain config is the trust anchor for seal block context.
/// </remarks>
public static class SequencerCommitteeConfig
{
    /// <summary>Default relative path of the chain config under a chain working directory.</summary>
    public const string RelativeChainConfigPath = "chain.config.json";

    /// <summary>
    /// Parse the <c>validators</c> array of compressed secp256r1 public keys from a
    /// <c>chain.config.json</c> file.
    /// </summary>
    public static IReadOnlyList<NeoECPoint> ReadValidators(string chainConfigPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainConfigPath);
        var fullPath = Path.GetFullPath(chainConfigPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("chain config not found", fullPath);

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("validators", out var validators)
            || validators.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException(
                $"{fullPath} is missing a validators array");

        var keys = new List<NeoECPoint>();
        var index = 0;
        foreach (var element in validators.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(element.GetString()))
                throw new InvalidDataException(
                    $"{fullPath} validators[{index}] must be a non-empty compressed public-key hex string");
            try
            {
                var key = NeoECPoint.Parse(element.GetString()!, ECCurve.Secp256r1);
                if (key.IsInfinity)
                    throw new InvalidDataException(
                        $"{fullPath} validators[{index}] is the point at infinity");
                keys.Add(key);
            }
            catch (Exception ex) when (ex is not InvalidDataException)
            {
                throw new InvalidDataException(
                    $"{fullPath} validators[{index}] is not a valid secp256r1 public key", ex);
            }
            index++;
        }

        if (keys.Count != keys.Distinct().Count())
            throw new InvalidDataException(
                $"{fullPath} validators contains duplicate public keys");
        return keys;
    }

    /// <summary>
    /// Read <c>validators</c> from <c>{chainDirectory}/chain.config.json</c>.
    /// </summary>
    public static IReadOnlyList<NeoECPoint> ReadValidatorsFromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        return ReadValidators(Path.Combine(Path.GetFullPath(chainDirectory), RelativeChainConfigPath));
    }

    /// <summary>
    /// Build a seal-time <c>Func&lt;UInt256&gt;</c> that always returns the
    /// <see cref="SequencerCommitteeHasher"/> hash over the configured validators.
    /// Fails closed when the validators array is empty.
    /// </summary>
    public static Func<UInt256> CreateStaticHashProvider(string chainConfigPath)
    {
        var keys = ReadValidators(chainConfigPath);
        if (keys.Count == 0)
            throw new InvalidDataException(
                $"{Path.GetFullPath(chainConfigPath)} validators is empty — "
                + "add genesis committee public keys before WireProduction inbox wiring, "
                + "or supply SequencerCommitteeHasher.CreateSyncProvider over a live "
                + "ISequencerCommitteeProvider");
        var hash = SequencerCommitteeHasher.Compute(keys);
        if (hash.Equals(UInt256.Zero))
            throw new InvalidDataException(
                "computed sequencer committee hash is zero — validators must be non-empty");
        return () => hash;
    }

    /// <summary>
    /// <see cref="CreateStaticHashProvider"/> for <c>{chainDirectory}/chain.config.json</c>.
    /// </summary>
    public static Func<UInt256> CreateStaticHashProviderFromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        return CreateStaticHashProvider(
            Path.Combine(Path.GetFullPath(chainDirectory), RelativeChainConfigPath));
    }
}
