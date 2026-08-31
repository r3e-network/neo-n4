namespace Neo.L2.TestInfra;

/// <summary>
/// The <c>SecurityLevel ⇒ ProofType</c> accept table as data, kept independent of both implementations
/// so neither can be "verified" against a copy of itself.
/// </summary>
/// <remarks>
/// <para>
/// Audit finding <c>H18</c>: three copies of this rule existed (<c>SettlementManagerContract</c>, the
/// settlement plugin's operator-status heuristic, and <c>neo-stack validate</c>) and they disagreed —
/// the rollup template shipped a <c>proofType</c> the deployer registers no verifier for, and
/// <c>validate</c> called that pair consistent. The rule now lives once on each side
/// (<c>NeoHub.SettlementManager.IsProofTypeCompatible</c> and <c>Neo.L2.ProofRouting</c>), and this table
/// is the third reference that pins both: the VM test executes the compiled NEF for every pair, the
/// Abstractions test walks the same pairs through the off-chain mirror.
/// </para>
/// <para>
/// Values are the wire bytes, not the enums: this file is compiled into every test assembly by
/// <c>tests/Directory.Build.props</c>, and <c>NeoHub.Contracts.VmTests</c> deliberately carries no project
/// reference to <c>Neo.L2.Abstractions</c>. SecurityLevel 0..4 / ProofType 0..3 per doc.md §16.2.
/// </para>
/// </remarks>
internal static class ProofRoutingExpectations
{
    // Neo.L2.SecurityLevel
    public const byte LevelSidechain = 0,
        LevelSettled = 1,
        LevelOptimistic = 2,
        LevelValidity = 3,
        LevelValidium = 4;

    // Neo.L2.ProofType
    public const byte ProofNone = 0,
        ProofMultisig = 1,
        ProofOptimistic = 2,
        ProofZk = 3;

    /// <summary>Every (securityLevel, proofType) pair the rule accepts, including out-of-range probes.</summary>
    public static readonly byte[] Levels = [LevelSidechain, LevelSettled, LevelOptimistic, LevelValidity, LevelValidium, 5];

    /// <summary>Proof bytes walked against every level; 4 and 255 pin the reject-everything default.</summary>
    public static readonly byte[] Proofs = [ProofNone, ProofMultisig, ProofOptimistic, ProofZk, 4, 255];

    /// <summary>
    /// Accepted pairs, enumerated rather than derived: a higher label is a stronger promise, so a chain
    /// may over-deliver (Sidechain + Zk) but never under-deliver (Optimistic + Multisig). ProofType.None
    /// and unknown proof bytes appear nowhere, and no level accepts them.
    /// </summary>
    public static readonly (byte Level, byte Proof)[] Accepted =
    [
        (LevelSidechain, ProofMultisig), (LevelSidechain, ProofOptimistic), (LevelSidechain, ProofZk),
        (LevelSettled, ProofMultisig), (LevelSettled, ProofOptimistic), (LevelSettled, ProofZk),
        (LevelOptimistic, ProofOptimistic), (LevelOptimistic, ProofZk),
        (LevelValidity, ProofZk),
        (LevelValidium, ProofZk),
    ];

    /// <summary>Whether <paramref name="level"/> was recorded as accepting <paramref name="proof"/>.</summary>
    public static bool Accepts(byte level, byte proof) => Array.IndexOf(Accepted, (level, proof)) >= 0;

    /// <summary>"Sidechain+Multisig" style label for assertion messages.</summary>
    public static string Name(byte level, byte proof) => $"{LevelName(level)}+{ProofName(proof)}";

    private static string LevelName(byte level) => level switch
    {
        LevelSidechain => "Sidechain",
        LevelSettled => "Settled",
        LevelOptimistic => "Optimistic",
        LevelValidity => "Validity",
        LevelValidium => "Validium",
        _ => $"level({level})",
    };

    private static string ProofName(byte proof) => proof switch
    {
        ProofNone => "None",
        ProofMultisig => "Multisig",
        ProofOptimistic => "Optimistic",
        ProofZk => "Zk",
        _ => $"proof({proof})",
    };
}
