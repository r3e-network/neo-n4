using System.Text.Json;
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

    /// <summary>
    /// Encoded NeoHub.SharedBridge contract hash. When set, production wiring constructs a
    /// durable <c>RpcSharedBridgeDepositSource</c> (unless the caller supplies one).
    /// </summary>
    public string SharedBridgeHash { get; init; } = "";

    /// <summary>
    /// Encoded native L2 bridge script hash used as CrossChainMessage.Receiver for deposits.
    /// Empty means use <c>NativeContract.L2Bridge.Hash</c> for N4 L2s.
    /// </summary>
    public string L2BridgeHash { get; init; } = "";

    /// <summary>
    /// Encoded NeoHub.MessageRouter contract hash. When set, production wiring constructs an
    /// owned <c>RpcMessageRouter</c> with a durable <c>L1ToL2Enqueued</c> event scanner
    /// (unless the caller supplies a message router).
    /// </summary>
    public string MessageRouterHash { get; init; } = "";

    /// <summary>
    /// L1 block index where ForcedInclusion was deployed. Used by production scanners when
    /// <c>WireProduction</c> is not given an explicit non-zero height argument.
    /// </summary>
    public uint ForcedInclusionDeploymentHeight { get; init; }

    /// <summary>
    /// L1 block index where SharedBridge was deployed. Required when
    /// <see cref="SharedBridgeHash"/> is set and the caller does not pass a non-zero
    /// <c>sharedBridgeDeploymentHeight</c> to <c>WireProduction</c>.
    /// </summary>
    public uint SharedBridgeDeploymentHeight { get; init; }

    /// <summary>
    /// L1 block index where MessageRouter was deployed. Required when
    /// <see cref="MessageRouterHash"/> is set and the caller does not pass a non-zero
    /// <c>messageRouterDeploymentHeight</c> to <c>WireProduction</c>.
    /// </summary>
    public uint MessageRouterDeploymentHeight { get; init; }

    /// <summary>
    /// L1 confirmation lag for scanners and for <c>RpcL1FinalizedHeightSource</c>.
    /// <c>WireProduction</c> uses this for ForcedInclusion / SharedBridge / MessageRouter
    /// finality when the corresponding method argument is omitted (<see langword="null"/>).
    /// Default 1 matches scanner defaults and production notes.
    /// </summary>
    public uint L1FinalityDepth { get; init; } = 1;

    /// <summary>Proof type used at this stage: 0=None, 1=Multisig, 2=Optimistic, 3=Zk.</summary>
    public byte ProofType { get; init; } = 1;

    /// <summary>Master kill switch.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Relative path of the settlement plugin config under a chain working directory
    /// (written by <c>init-l2 --from-deploy-report</c> / <c>register-chain --from-deploy-report</c>).
    /// </summary>
    public const string RelativePluginConfigPath =
        "Plugins/Neo.Plugins.L2Settlement/config.json";

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
            SharedBridgeHash = s.GetValue<string>("SharedBridgeHash") ?? "",
            L2BridgeHash = s.GetValue<string>("L2BridgeHash") ?? "",
            MessageRouterHash = s.GetValue<string>("MessageRouterHash") ?? "",
            ForcedInclusionDeploymentHeight = s.GetValue<uint>("ForcedInclusionDeploymentHeight", 0u),
            SharedBridgeDeploymentHeight = s.GetValue<uint>("SharedBridgeDeploymentHeight", 0u),
            MessageRouterDeploymentHeight = s.GetValue<uint>("MessageRouterDeploymentHeight", 0u),
            L1FinalityDepth = s.GetValue<uint>("L1FinalityDepth", 1u),
            ProofType = rawProofType,
            Enabled = s.GetValue("Enabled", true),
        };
    }

    /// <summary>
    /// Load settings from a settlement plugin <c>config.json</c> file
    /// (<c>{ "PluginConfiguration": { ... } }</c>).
    /// </summary>
    public static L2SettlementSettings FromPluginConfigFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(
                "settlement plugin config not found", fullPath);

        // Parse via System.Text.Json then project into IConfiguration so From() and
        // RejectPrivateKeyConfiguration stay the single validation path (no second JSON schema).
        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("PluginConfiguration", out var plugin)
            || plugin.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"{fullPath} is missing a PluginConfiguration object");

        var pairs = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in plugin.EnumerateObject())
            pairs["PluginConfiguration:" + property.Name] = JsonValueToConfigString(property.Value);

        var root = new ConfigurationBuilder()
            .AddInMemoryCollection(pairs)
            .Build();
        return From(root.GetSection("PluginConfiguration"));
    }

    /// <summary>
    /// Load settings from a chain working directory written by <c>init-l2</c> /
    /// <c>--from-deploy-report</c>. Tries top-level, <c>node/</c>, then <c>batcher-node/</c>
    /// plugin config paths.
    /// </summary>
    public static L2SettlementSettings FromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = Path.GetFullPath(chainDirectory);
        var candidates = new[]
        {
            Path.Combine(root, "Plugins", "Neo.Plugins.L2Settlement", "config.json"),
            Path.Combine(root, "node", "Plugins", "Neo.Plugins.L2Settlement", "config.json"),
            Path.Combine(root, "batcher-node", "Plugins", "Neo.Plugins.L2Settlement", "config.json"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return FromPluginConfigFile(candidate);
        }
        throw new FileNotFoundException(
            "settlement plugin config not found under chain directory "
            + $"(expected {RelativePluginConfigPath} or node/batcher-node variants)",
            Path.Combine(root, RelativePluginConfigPath));
    }

    private static string? JsonValueToConfigString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => null,
        _ => value.GetRawText(),
    };

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

        UInt160? sharedBridgeHash = null;
        UInt160? l2BridgeHash = null;
        if (!string.IsNullOrWhiteSpace(SharedBridgeHash))
        {
            sharedBridgeHash = ParseNonZeroHash(SharedBridgeHash, nameof(SharedBridgeHash));
            if (sharedBridgeHash.Equals(settlementManagerHash)
                || sharedBridgeHash.Equals(forcedInclusionHash))
                throw new InvalidDataException(
                    "SharedBridgeHash must identify a distinct NeoHub contract");

            l2BridgeHash = string.IsNullOrWhiteSpace(L2BridgeHash)
                ? Neo.SmartContract.Native.NativeContract.L2Bridge.Hash
                : ParseNonZeroHash(L2BridgeHash, nameof(L2BridgeHash));
            if (l2BridgeHash.Equals(UInt160.Zero))
                throw new InvalidDataException("L2BridgeHash must not be zero");
        }
        else if (!string.IsNullOrWhiteSpace(L2BridgeHash))
        {
            throw new InvalidDataException(
                "L2BridgeHash requires SharedBridgeHash for production deposit wiring");
        }

        UInt160? messageRouterHash = null;
        if (!string.IsNullOrWhiteSpace(MessageRouterHash))
        {
            messageRouterHash = ParseNonZeroHash(MessageRouterHash, nameof(MessageRouterHash));
            if (messageRouterHash.Equals(settlementManagerHash)
                || messageRouterHash.Equals(forcedInclusionHash)
                || (sharedBridgeHash is not null && messageRouterHash.Equals(sharedBridgeHash)))
                throw new InvalidDataException(
                    "MessageRouterHash must identify a distinct NeoHub contract");
        }

        return new L2SettlementProductionConfiguration(
            chainId,
            endpoint,
            ExpectedNetwork.Value,
            settlementManagerHash,
            forcedInclusionHash,
            sharedBridgeHash,
            l2BridgeHash,
            messageRouterHash);
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
    UInt160 ForcedInclusionHash,
    UInt160? SharedBridgeHash,
    UInt160? L2BridgeHash,
    UInt160? MessageRouterHash);
