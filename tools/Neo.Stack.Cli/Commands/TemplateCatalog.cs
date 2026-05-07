using System.Collections.Generic;
using System.Linq;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// Single source of truth for the four chain-config templates (rollup / zk-rollup /
/// validium / sidechain) consumed by <c>create-chain</c>, <c>new-l2</c>, and
/// <c>list-templates</c>. Keeps per-template defaults from drifting across commands.
/// </summary>
internal static class TemplateCatalog
{
    /// <summary>Per-template defaults (doc.md §6 chain modes + §16.2 security label).</summary>
    public readonly record struct Template(
        string Name,
        string ChainMode,
        string DaMode,
        string ProofType,
        string SecurityLevel,
        string SequencerModel,
        string ExitModel,
        bool GatewayEnabled,
        bool PermissionlessExit,
        string TagLine,
        string UseCase);

    /// <summary>All known templates in display order (default first).</summary>
    public static readonly Template[] All = new[]
    {
        new Template(
            Name: "rollup",
            ChainMode: "L2RollupMode", DaMode: "L1", ProofType: "Optimistic",
            SecurityLevel: "Optimistic", SequencerModel: "DbftCommittee",
            ExitModel: "Delayed", GatewayEnabled: false, PermissionlessExit: true,
            TagLine: "Optimistic L1 DA + dBFT committee + delayed exit (the safe default).",
            UseCase: "General-purpose Neo L2 — DeFi, dApp hosting. Inherits the §17 mitigation #2 optimistic challenge window so a faulty proof is contestable. L1 DA matches the strongest data-availability tier; everyone can independently re-derive the state by replaying batches from L1. Pick this unless one of the others specifically applies."),
        new Template(
            Name: "zk-rollup",
            ChainMode: "L2RollupMode", DaMode: "L1", ProofType: "Zk",
            SecurityLevel: "Validity", SequencerModel: "DbftCommittee",
            ExitModel: "Permissionless", GatewayEnabled: true, PermissionlessExit: true,
            TagLine: "ZK validity + L1 DA + permissionless exit. Strongest trust assumption.",
            UseCase: "Validity-proof rollup. No challenge window — finalization is the proof. L1 DA + permissionless exit gives users the strongest exit guarantee. Gateway-enabled so the chain participates in Phase-5 cross-L2 messaging. Use when the chain warrants ZK proving cost (high TVL, regulatory rigor)."),
        new Template(
            Name: "validium",
            ChainMode: "L2ValidiumMode", DaMode: "NeoFS", ProofType: "Zk",
            SecurityLevel: "Validium", SequencerModel: "DbftCommittee",
            ExitModel: "Delayed", GatewayEnabled: true, PermissionlessExit: false,
            TagLine: "ZK validity + NeoFS off-chain DA. DEX / orderbook / matching engine.",
            UseCase: "Validity-proof + off-chain DA. Cheaper than L1 DA + still retrievable via NeoFS. Delayed exit lets the operator drain orderbook on shutdown without users front-running. Gateway-enabled so DEX users can move assets between this and other Elastic Network L2s without round-tripping L1."),
        new Template(
            Name: "sidechain",
            ChainMode: "SidechainMode", DaMode: "External", ProofType: "None",
            SecurityLevel: "Sidechain", SequencerModel: "DbftCommittee",
            ExitModel: "Permissionless", GatewayEnabled: false, PermissionlessExit: true,
            TagLine: "No L1 settlement, attestation only. Permissioned consortia, enterprise.",
            UseCase: "Lightest-touch variant. SidechainMode + ProofType=None + permissionlessExit. Useful for permissioned consortia or enterprise networks where the L1 anchor isn't a trust anchor — it's just a discovery + asset-bridge endpoint. No prover plugin needed; settlement happens via attestation alone."),
    };

    /// <summary>Resolve a template by name (case-sensitive). Falls back to <c>"rollup"</c> on unknown name.</summary>
    public static Template Resolve(string name) =>
        All.FirstOrDefault(t => t.Name == name, defaultValue: All[0]);

    /// <summary>Returns true if <paramref name="name"/> matches a known template.</summary>
    public static bool IsKnown(string name) => All.Any(t => t.Name == name);

    /// <summary>Comma-separated list of valid template names — used in error messages.</summary>
    public static string ValidNames => string.Join(", ", All.Select(t => t.Name));
}
