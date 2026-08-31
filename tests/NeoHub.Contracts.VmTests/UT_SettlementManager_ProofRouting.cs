using Neo;
using Neo.L2.TestInfra;
using Neo.SmartContract.Testing;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// Executes <c>NeoHub.SettlementManager.IsProofTypeCompatible</c> — the on-chain authority for which
/// proof types may settle under a security label — across every <c>(securityLevel, proofType)</c> pair
/// and pins it to the independent table in <see cref="ProofRoutingExpectations"/>.
/// </summary>
/// <remarks>
/// Audit finding <c>H18</c>: one rule was implemented three times (this contract, the settlement plugin's
/// operator-status heuristic, and <c>neo-stack validate</c>) and the copies disagreed, which is how the
/// default rollup template shipped a proof type the deployer registers no verifier for. The rule now lives
/// once per side, and both sides are pinned to the same third reference: <c>UT_ProofRouting</c> walks the
/// identical pair set through <c>Neo.L2.ProofRouting</c>. SubmitBatch and FinalizeBatch call this same
/// function, so the published read cannot drift away from the path that faults a batch.
/// </remarks>
[TestClass]
public class UT_SettlementManager_ProofRouting
{
    [TestMethod]
    public void ProofRoutingTable_MatchesTheOnChainAuthority()
    {
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        // owner + the two registry hashes _deploy insists on; this read touches no storage and no
        // cross-contract call, so no mocks are needed.
        var sm = engine.Deploy<NeoHubSettlementManager>(
            NeoHubSettlementManager.Nef,
            NeoHubSettlementManager.Manifest,
            new object[]
            {
                engine.Sender,
                UInt160.Parse("0x" + new string('1', 40)),
                UInt160.Parse("0x" + new string('2', 40)),
            });

        foreach (var level in ProofRoutingExpectations.Levels)
        {
            foreach (var proof in ProofRoutingExpectations.Proofs)
            {
                var expected = ProofRoutingExpectations.Accepts(level, proof);
                Assert.AreEqual(expected, sm.IsProofTypeCompatible(level, proof),
                    $"{ProofRoutingExpectations.Name(level, proof)}: the contract must "
                    + (expected ? "accept" : "reject") + " this pair");
            }
        }
    }
}
