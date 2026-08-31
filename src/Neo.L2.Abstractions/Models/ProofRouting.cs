namespace Neo.L2;

/// <summary>
/// The single off-chain statement of which <see cref="ProofType"/> commitments a chain may submit for
/// a given <see cref="SecurityLevel"/>, and which verifier routes the shipped production bundle freezes.
/// </summary>
/// <remarks>
/// See doc.md §3.2 (VerifierRegistry) and §16.2 (Security Labels). <see cref="AcceptsProofType"/> mirrors
/// <c>NeoHub.SettlementManager.IsProofTypeCompatible</c>; the VM test
/// <c>UT_SettlementManager_ProofRouting.ProofRoutingTable_MatchesTheOnChainAuthority</c> enumerates every
/// (SecurityLevel, ProofType) pair through both sides so they cannot drift apart again. Before this type
/// existed three copies disagreed: the contract accepted <c>Optimistic ⇒ Zk</c> while the settlement
/// plugin heuristic and <c>neo-stack validate</c> accepted <c>Optimistic ⇒ Multisig</c> and rejected Zk.
/// </remarks>
public static class ProofRouting
{
    /// <summary>
    /// Proof routes <c>Neo.Hub.Deploy</c> registers in <c>NeoHub.VerifierRegistry</c> before it calls
    /// <c>lockGovernance()</c>. The registry is keyed by proof type and shared by every chain on the hub,
    /// so a commitment whose type is absent faults in <c>submitBatch</c> with "no verifier for proof
    /// type", and no route can be added afterwards because the lock is one-way.
    /// </summary>
    /// <remarks>
    /// Multisig and Optimistic are spec'd routes — doc.md §3.2 lists <c>MultisigVerifier</c> and
    /// <c>OptimisticVerifier</c>, and Phase 3 names the optimistic verifier as its first deliverable —
    /// and both have off-chain provers in <c>Neo.L2.Proving</c>, but no L1 verifier contract implements
    /// <c>verify(commitmentBytes)</c> for them, so neither has a route on a hub deployed by this bundle.
    /// </remarks>
    public static IReadOnlySet<ProofType> ProductionVerifierRoutes { get; } =
        new HashSet<ProofType> { ProofType.Zk };

    /// <summary>
    /// The proof types whose commitments a chain advertising <paramref name="securityLevel"/> may
    /// settle. This is the one table; <see cref="AcceptsProofType"/> tests it. A higher label is a
    /// stronger promise, so a chain may over-deliver (an <c>Optimistic</c> chain submitting <c>Zk</c> is
    /// legal) but never under-deliver. <c>None</c> appears nowhere: it cannot be registered as a verifier
    /// route and no proof artifact can be built for it.
    /// </summary>
    public static ProofType[] AcceptedProofTypes(SecurityLevel securityLevel) => securityLevel switch
    {
        SecurityLevel.Sidechain or SecurityLevel.Settled =>
            [ProofType.Multisig, ProofType.Optimistic, ProofType.Zk],
        SecurityLevel.Optimistic => [ProofType.Optimistic, ProofType.Zk],
        SecurityLevel.Validity or SecurityLevel.Validium => [ProofType.Zk],
        _ => [],
    };

    /// <summary>Mirror of <c>SettlementManager.IsProofTypeCompatible</c> for <paramref name="securityLevel"/>.</summary>
    public static bool AcceptsProofType(SecurityLevel securityLevel, ProofType proofType) =>
        Array.IndexOf(AcceptedProofTypes(securityLevel), proofType) >= 0;

    /// <summary>True when a hub deployed by the shipped bundle can verify <paramref name="proofType"/>.</summary>
    public static bool HasProductionVerifierRoute(ProofType proofType) =>
        ProductionVerifierRoutes.Contains(proofType);
}
