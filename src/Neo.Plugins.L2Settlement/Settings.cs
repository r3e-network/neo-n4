using Microsoft.Extensions.Configuration;

namespace Neo.Plugins.L2;

/// <summary>Configuration for <see cref="L2SettlementPlugin"/>.</summary>
/// <remarks>See doc.md §3.2, §7.5, §14.2, and §15.4.</remarks>
public sealed class L2SettlementSettings
{
    /// <summary>Local L2 chain identifier.</summary>
    public uint ChainId { get; init; }

    /// <summary>L1 RPC endpoint for submitting batches and reading bridge state.</summary>
    public string L1RpcEndpoint { get; init; } = "";

    /// <summary>Expected L1 network magic. Production wiring requires an explicit value.</summary>
    public uint? ExpectedNetwork { get; init; }

    /// <summary>Encoded NeoHub.SettlementManager contract hash.</summary>
    public string SettlementManagerHash { get; init; } = "";

    /// <summary>Encoded NeoHub.ForcedInclusion contract hash.</summary>
    public string ForcedInclusionHash { get; init; } = "";

    /// <summary>Proof type used at this stage: 0=None, 1=Multisig, 2=Optimistic, 3=Zk.</summary>
    public byte ProofType { get; init; } = 1;

    /// <summary>Master kill switch.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Build settings from <c>PluginConfiguration</c>.</summary>
    public static L2SettlementSettings From(IConfigurationSection s)
    {
        ArgumentNullException.ThrowIfNull(s);
        RejectPrivateKeyConfiguration(s);
        var rawProofType = s.GetValue<byte>("ProofType", 1);
        // Validate at parse time so a misconfigured ProofType byte fails at plugin
        // load, not later at SubmitNextAsync when the first batch is sealed.
        Neo.L2.ProofTypeExtensions.Resolve(rawProofType);
        // ChainId: distinguish "key missing" (test mode, no config supplied) from
        // "explicitly set to 0" (operator misconfig). Nullable read returns null for
        // the former and 0 for the latter; we only validate the second.
        var rawChainId = s.GetValue<uint?>("ChainId");
        return new L2SettlementSettings
        {
            ChainId = rawChainId is null ? 0u : Neo.L2.ChainIdValidator.ValidateL2(rawChainId.Value),
            L1RpcEndpoint = s.GetValue<string>("L1RpcEndpoint") ?? "",
            ExpectedNetwork = s.GetValue<uint?>("ExpectedNetwork"),
            SettlementManagerHash = s.GetValue<string>("SettlementManagerHash") ?? "",
            ForcedInclusionHash = s.GetValue<string>("ForcedInclusionHash") ?? "",
            ProofType = rawProofType,
            Enabled = s.GetValue("Enabled", true),
        };
    }

    internal L2SettlementProductionConfiguration ValidateProduction()
    {
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(ChainId);
        if (string.IsNullOrWhiteSpace(L1RpcEndpoint))
            throw new InvalidDataException(
                "L1RpcEndpoint is required for production settlement wiring");
        if (!Uri.TryCreate(L1RpcEndpoint, UriKind.Absolute, out var endpoint)
            || endpoint.Scheme is not ("http" or "https"))
            throw new InvalidDataException(
                "L1RpcEndpoint must be an absolute HTTP or HTTPS URI");
        if (ExpectedNetwork is null)
            throw new InvalidDataException(
                "ExpectedNetwork is required for production settlement wiring");

        var settlementManagerHash = ParseNonZeroHash(
            SettlementManagerHash, nameof(SettlementManagerHash));
        var forcedInclusionHash = ParseNonZeroHash(
            ForcedInclusionHash, nameof(ForcedInclusionHash));
        if (settlementManagerHash.Equals(forcedInclusionHash))
            throw new InvalidDataException(
                "SettlementManagerHash and ForcedInclusionHash must identify different contracts");

        return new L2SettlementProductionConfiguration(
            chainId,
            endpoint,
            ExpectedNetwork.Value,
            settlementManagerHash,
            forcedInclusionHash);
    }

    private static UInt160 ParseNonZeroHash(string? raw, string settingName)
    {
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidDataException(
                $"{settingName} is required for production settlement wiring");

        UInt160 hash;
        try
        {
            hash = UInt160.Parse(raw);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidDataException(
                $"{settingName} must be a valid UInt160 contract hash", exception);
        }

        if (hash.Equals(UInt160.Zero))
            throw new InvalidDataException($"{settingName} must not be zero");
        return hash;
    }

    private static void RejectPrivateKeyConfiguration(IConfigurationSection section)
    {
        foreach (var child in section.GetChildren())
        {
            if (child.Key.Equals("Wif", StringComparison.OrdinalIgnoreCase)
                || child.Key.Equals("SignerWif", StringComparison.OrdinalIgnoreCase)
                || child.Key.Equals("OperatorWif", StringComparison.OrdinalIgnoreCase)
                || child.Key.Equals("PrivateKey", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Private signing material must not be stored in L2Settlement config; " +
                    "pass an INeoTransactionSigner to WireProduction");
            }
        }
    }
}

internal sealed record L2SettlementProductionConfiguration(
    uint ChainId,
    Uri RpcEndpoint,
    uint ExpectedNetwork,
    UInt160 SettlementManagerHash,
    UInt160 ForcedInclusionHash);
