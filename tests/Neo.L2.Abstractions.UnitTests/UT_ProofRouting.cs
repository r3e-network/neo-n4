using Neo.L2.TestInfra;

namespace Neo.L2.UnitTests;

/// <summary>
/// Pins <see cref="ProofRouting"/> — the single off-chain statement of the SecurityLevel ⇒ ProofType
/// rule — against the same reference table the compiled contract is pinned to by
/// <c>UT_SettlementManager_ProofRouting.ProofRoutingTable_MatchesTheOnChainAuthority</c>.
/// </summary>
/// <remarks>
/// Audit finding <c>H18</c>: the contract, the settlement plugin's operator-status heuristic and
/// <c>neo-stack validate</c> each carried their own copy of this rule and they disagreed. Neither
/// implementation may be checked against a copy of itself, so both are checked against
/// <see cref="ProofRoutingExpectations"/>, which is compiled into both test assemblies.
/// </remarks>
[TestClass]
public class UT_ProofRouting
{
    [TestMethod]
    public void AcceptsProofType_MatchesTheReferenceTable()
    {
        foreach (var level in ProofRoutingExpectations.Levels)
        {
            foreach (var proof in ProofRoutingExpectations.Proofs)
            {
                var expected = ProofRoutingExpectations.Accepts(level, proof);
                Assert.AreEqual(expected,
                    ProofRouting.AcceptsProofType((SecurityLevel)level, (ProofType)proof),
                    $"{ProofRoutingExpectations.Name(level, proof)}: the off-chain mirror must "
                    + (expected ? "accept" : "reject") + " this pair, exactly as the contract does");
            }
        }
    }

    [TestMethod]
    public void AcceptedProofTypes_ListsExactlyThePairsTheRuleAccepts()
    {
        // `neo-stack validate` prints this list as the operator's fix hint, so every entry it names has
        // to be a pair AcceptsProofType actually accepts — and it must name all of them.
        foreach (var level in ProofRoutingExpectations.Levels)
        {
            var expected = ProofRoutingExpectations.Accepted
                .Where(pair => pair.Level == level)
                .Select(pair => (ProofType)pair.Proof)
                .ToArray();
            var listed = ProofRouting.AcceptedProofTypes((SecurityLevel)level);

            CollectionAssert.AreEqual(expected, listed,
                $"AcceptedProofTypes({(SecurityLevel)level}) drifted from the reference table");
            foreach (var proof in listed)
            {
                Assert.IsTrue(ProofRouting.AcceptsProofType((SecurityLevel)level, proof),
                    $"AcceptedProofTypes({(SecurityLevel)level}) names {proof}, which AcceptsProofType rejects");
            }
        }
    }

    [TestMethod]
    public void AcceptedProofTypes_NeverNamesNoneAtAnyLevel()
    {
        // None has no verifier route to register (VerifierRegistry.WriteVerifier rejects proofType 0) and
        // no proof artifact to build, yet it read as the honest "a sidechain proves nothing" default.
        foreach (var level in ProofRoutingExpectations.Levels)
        {
            Assert.IsFalse(ProofRouting.AcceptedProofTypes((SecurityLevel)level).Contains(ProofType.None),
                $"AcceptedProofTypes({(SecurityLevel)level}) must not name None");
        }
    }

    [TestMethod]
    public void ProductionVerifierRoutes_IsTheSingleRouteTheDeployerRegisters()
    {
        // Neo.Hub.Deploy writes the Zk route and then calls lockGovernance(), which is one-way. The
        // deployer side is pinned by UT_LiveDeployCommand; if it ever registers Multisig or Optimistic,
        // update this and the warning text in ValidateChainConfigCommand together.
        CollectionAssert.AreEqual(new[] { ProofType.Zk }, ProofRouting.ProductionVerifierRoutes.ToArray());
        Assert.IsTrue(ProofRouting.HasProductionVerifierRoute(ProofType.Zk));
        Assert.IsFalse(ProofRouting.HasProductionVerifierRoute(ProofType.None));
        Assert.IsFalse(ProofRouting.HasProductionVerifierRoute(ProofType.Multisig));
        Assert.IsFalse(ProofRouting.HasProductionVerifierRoute(ProofType.Optimistic));
    }
}
