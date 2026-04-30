using Microsoft.Extensions.Configuration;

namespace Neo.Plugins.L2;

/// <summary>Configuration for <see cref="L2SettlementPlugin"/>.</summary>
public sealed class L2SettlementSettings
{
    /// <summary>Local L2 chain identifier.</summary>
    public uint ChainId { get; init; }

    /// <summary>L1 RPC endpoint for submitting batches and reading bridge state.</summary>
    public string L1RpcEndpoint { get; init; } = "http://localhost:10332";

    /// <summary>Encoded NeoHub.SettlementManager contract hash.</summary>
    public string SettlementManagerHash { get; init; } = "";

    /// <summary>Proof type used at this stage: 0=None, 1=Multisig, 2=Optimistic, 3=Zk.</summary>
    public byte ProofType { get; init; } = 1;

    /// <summary>Master kill switch.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Build settings from <c>PluginConfiguration</c>.</summary>
    public static L2SettlementSettings From(IConfigurationSection s)
    {
        return new L2SettlementSettings
        {
            ChainId = s.GetValue<uint>("ChainId"),
            L1RpcEndpoint = s.GetValue("L1RpcEndpoint", "http://localhost:10332")!,
            SettlementManagerHash = s.GetValue("SettlementManagerHash", "")!,
            ProofType = s.GetValue<byte>("ProofType", 1),
            Enabled = s.GetValue("Enabled", true),
        };
    }
}
