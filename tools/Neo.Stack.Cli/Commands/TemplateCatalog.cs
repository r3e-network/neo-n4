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
            // proofType=Zk, not Optimistic: VerifierRegistry is keyed by proof type and the production
            // bundle registers only the Zk route before locking the registry one-way, so an Optimistic
            // commitment faults in submitBatch with "no verifier for proof type" (the OptimisticVerifier
            // doc.md §3.2 lists is unimplemented). Zk under an Optimistic label over-delivers, which is
            // exactly what SettlementManager.IsProofTypeCompatible accepts.
            ChainMode: "L2RollupMode", DaMode: "NeoFS", ProofType: "Zk",
            SecurityLevel: "Optimistic", SequencerModel: "DbftCommittee",
            ExitModel: "Delayed", GatewayEnabled: false, PermissionlessExit: true,
            TagLine: "ZK validity settlement + NeoFS DA + dBFT committee (the safe default).",
            UseCase: "General-purpose Neo L2 — DeFi, dApp hosting. Settlement is an SP1 validity proof, so a batch is final when the proof verifies: no honest-challenger assumption, no window to wait out. NeoFS is the canonical N4 DA layer: batches remain Neo-native, content-addressed, and retrievable without forcing every byte into L1 calldata. securityLevel stays Optimistic because that is the floor the chain advertises (doc.md §16.2) while proofType=Zk over-delivers on it. Pick this unless one of the others specifically applies; pick zk-rollup instead when batch data must also land on L1."),
        new Template(
            Name: "zk-rollup",
            // ChainRegistry asserts SecurityLevel.Validity ⇒ DAMode.L1 (doc.md §12 / §16.2).
            // Off-chain DA + ZK is the validium template, not zk-rollup.
            ChainMode: "L2RollupMode", DaMode: "L1", ProofType: "Zk",
            SecurityLevel: "Validity", SequencerModel: "DbftCommittee",
            ExitModel: "Permissionless", GatewayEnabled: true, PermissionlessExit: true,
            TagLine: "ZK validity + L1 DA + permissionless exit. Strongest DA + proof guarantees.",
            UseCase: "Validity-proof rollup with L1 data availability. No challenge window — finalization is the proof. Batch data lands on L1 so users can reconstruct state without an off-chain DA committee. Gateway-enabled so the chain participates in Phase-5 cross-L2 messaging. Use when the chain warrants ZK proving cost and L1 DA cost (high TVL, regulatory rigor). Prefer the validium template when NeoFS/off-chain DA is acceptable."),
        new Template(
            Name: "validium",
            ChainMode: "L2ValidiumMode", DaMode: "NeoFS", ProofType: "Zk",
            SecurityLevel: "Validium", SequencerModel: "DbftCommittee",
            ExitModel: "Delayed", GatewayEnabled: true, PermissionlessExit: false,
            TagLine: "ZK validity + NeoFS off-chain DA. DEX / orderbook / matching engine.",
            UseCase: "Validity-proof + off-chain DA. Cheaper than L1 DA + still retrievable via NeoFS. Delayed exit lets the operator drain orderbook on shutdown without users front-running. Gateway-enabled so DEX users can move assets between this and other Elastic Network L2s without round-tripping L1."),
        new Template(
            Name: "sidechain",
            // proofType=Multisig, not None: no layer accepts None. SettlementManager
            // .IsProofTypeCompatible has no None row, VerifierRegistry.WriteVerifier rejects proofType 0,
            // and Neo.L2.Batch.ProofWitnessSerializers refuses to build a None artifact — so a None
            // config cannot produce a batch anywhere. Multisig is the committee-attestation route the
            // contract accepts for Sidechain and the one the Phase-0 sidechain scenario uses.
            ChainMode: "SidechainMode", DaMode: "NeoFS", ProofType: "Multisig",
            SecurityLevel: "Sidechain", SequencerModel: "DbftCommittee",
            ExitModel: "Permissionless", GatewayEnabled: false, PermissionlessExit: true,
            TagLine: "No L1 settlement, NeoFS DA, committee attestation. Permissioned consortia, enterprise.",
            UseCase: "Lightest-touch variant. SidechainMode + ProofType=Multisig + permissionlessExit. Useful for permissioned consortia or enterprise networks where the L1 anchor isn't a trust anchor — it's just a discovery + asset-bridge endpoint. NeoFS remains the canonical data-availability store even when the proof model is committee attestation. Note: the shipped production bundle freezes VerifierRegistry with only the Zk route, so a sidechain that must settle batches on a hub deployed by Neo.Hub.Deploy needs an operator-supplied Multisig route registered before the lock."),
    };

    /// <summary>Resolve a template by name (case-sensitive). Falls back to <c>"rollup"</c> on unknown name.</summary>
    public static Template Resolve(string name) =>
        All.FirstOrDefault(t => t.Name == name, defaultValue: All[0]);

    /// <summary>Returns true if <paramref name="name"/> matches a known template.</summary>
    public static bool IsKnown(string name) => All.Any(t => t.Name == name);

    /// <summary>Comma-separated list of valid template names — used in error messages.</summary>
    public static string ValidNames => string.Join(", ", All.Select(t => t.Name));

    /// <summary>Project the (exitModel, permissionlessExit) pair into the operator-facing
    /// exit-policy line, naming the challenge window whenever one applies.</summary>
    /// <remarks>
    /// doc.md §16.2: <c>ExitModel.Delayed</c> means user exit is permissionless but subject
    /// to a fixed challenge window — the window is the substance of the mode, so a line that
    /// prints only "permissionless" under-communicates it (audit §6, permissionlessExit item).
    /// create-chain and list-templates share this projection so the two cannot drift.
    /// </remarks>
    public static string DescribeExitPolicy(string exitModel, bool permissionlessExit) =>
        exitModel switch
        {
            "Permissionless" when permissionlessExit => "permissionless (no challenge window)",
            "Permissionless" => "operator-gated — contradicts exitModel=Permissionless (validate warns)",
            "Delayed" when permissionlessExit => "permissionless initiation; exits finalize only after the Delayed challenge window",
            "Delayed" => "operator-gated; exits finalize only after the Delayed challenge window",
            _ when permissionlessExit => "requires operator co-sign (exitModel=OperatorAssisted) — contradicts permissionlessExit=true (validate warns)",
            _ => "operator-gated (requires operator co-sign)",
        };
}
