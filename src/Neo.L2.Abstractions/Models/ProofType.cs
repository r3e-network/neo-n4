namespace Neo.L2;

/// <summary>
/// Discriminator for the <c>proof</c> bytes inside <see cref="L2BatchCommitment"/>.
/// <c>NeoHub.VerifierRegistry</c> dispatches on this when verifying.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (SettlementManager) and §7.5 (ProverAdapter stages).
/// </remarks>
public enum ProofType : byte
{
    /// <summary>No proof (used only for genesis or operator-trusted internal flows).</summary>
    None = 0,

    /// <summary>Stage 0 — validator multisig attestation. The "proof" is a set of signatures.</summary>
    Multisig = 1,

    /// <summary>Stage 1 — optimistic. The batch is provisional until the challenge window closes.</summary>
    Optimistic = 2,

    /// <summary>Stage 2 — ZK validity proof (NeoVM2 / RISC-V).</summary>
    Zk = 3,
}

/// <summary>Validation helpers for <see cref="ProofType"/>.</summary>
public static class ProofTypeExtensions
{
    /// <summary>
    /// Validate a config-supplied byte against the defined <see cref="ProofType"/> range.
    /// Throws <see cref="System.IO.InvalidDataException"/> on an unknown value so misconfiguration
    /// surfaces at plugin-load time instead of at first proof generation.
    /// </summary>
    public static ProofType Resolve(byte raw)
    {
        var pt = (ProofType)raw;
        if (pt is not (ProofType.None or ProofType.Multisig or ProofType.Optimistic or ProofType.Zk))
            throw new System.IO.InvalidDataException(
                $"ProofType {raw} is not one of None(0), Multisig(1), Optimistic(2), Zk(3) — fix config");
        return pt;
    }
}
