using Neo.Json;

namespace Neo.Hub.Deploy.UnitTests;

/// <summary>
/// Manifest-level invariant pins for the security-critical NeoHub contracts.
/// These tests assert what the contract MUST and MUST NOT expose on its ABI
/// surface so a regression that, say, accidentally adds a `clearImmutableFlag`
/// method to GovernanceController surfaces in CI rather than during a
/// post-deployment audit.
/// </summary>
/// <remarks>
/// Manifest is loaded via <see cref="ManifestTestHelper.LoadFreshManifest"/>,
/// which pass-throughs (returns <c>null</c>) for both missing-manifest (local
/// dev without <c>nccs</c>) and stale-manifest (source <c>*.cs</c> newer than
/// the cached <c>bin/sc/*.manifest.json</c>). Stale-case prints a
/// <c>[manifest-stale]</c> hint so the developer knows to re-run nccs.
/// The authoritative gate is the CI <c>contracts</c> job which rebuilds.
/// </remarks>
[TestClass]
public class UT_ContractManifestInvariants
{
    private static JArray? LoadMethods(string contract)
    {
        var manifest = ManifestTestHelper.LoadFreshManifest(contract);
        if (manifest is null) return null;
        var abi = (JObject)manifest["abi"]!;
        return (JArray)abi["methods"]!;
    }

    private static bool HasMethod(JArray methods, string name) =>
        methods.Any(m => ((JObject?)m)?["name"]?.AsString() == name);

    // ─── ContractZkVerifier: contract-deployed proof route ───────────────────

    [TestMethod]
    public void ContractZkVerifier_Exposes_ContractVerifierRoute_AndNoNativeAccelerator()
    {
        var methods = LoadMethods("NeoHub.ContractZkVerifier");
        if (methods is null) return;

        Assert.IsTrue(HasMethod(methods, "registerVerificationKey"),
            "ContractZkVerifier must expose registerVerificationKey");
        Assert.IsTrue(HasMethod(methods, "registerProofVerifier"),
            "ContractZkVerifier must expose registerProofVerifier for deployable verifier contracts");
        Assert.IsTrue(HasMethod(methods, "getProofVerifier"),
            "ContractZkVerifier must expose getProofVerifier for operator/audit introspection");
        Assert.IsTrue(HasMethod(methods, "setEnvelopeOnlyAllowed"),
            "ContractZkVerifier must expose explicit devnet-only envelope mode");
        Assert.IsTrue(HasMethod(methods, "isEnvelopeOnlyAllowed"),
            "ContractZkVerifier must expose isEnvelopeOnlyAllowed");
        Assert.IsTrue(HasMethod(methods, "verify"),
            "ContractZkVerifier must expose verify for VerifierRegistry dispatch");

        Assert.IsFalse(HasMethod(methods, "setNativeAccelerator"),
            "REGRESSION: ContractZkVerifier must not require an L1 native accelerator");
        Assert.IsFalse(HasMethod(methods, "getNativeAccelerator"),
            "REGRESSION: ContractZkVerifier must not expose native-accelerator state");
    }

    // ─── GovernanceController: SetImmutableFlag is permanent ──────────

    [TestMethod]
    public void GovernanceController_HasSetImmutableFlag_ButNoClear()
    {
        var methods = LoadMethods("NeoHub.GovernanceController");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "setImmutableFlag"),
            "GovernanceController must expose setImmutableFlag (owner-gated)");
        Assert.IsTrue(HasMethod(methods, "setImmutableFlagViaProposal"),
            "GovernanceController must expose setImmutableFlagViaProposal (council-veto path)");
        Assert.IsTrue(HasMethod(methods, "isImmutable"),
            "GovernanceController must expose isImmutable (read-only check)");
        // Critical: there must be NO clear-side method. Once set, a permanent
        // restriction MUST be permanent (ZKsync PermanentRestriction equivalent).
        // A regression that adds a clearImmutableFlag would silently break the
        // security model — pinning the absence here catches it at CI time.
        Assert.IsFalse(HasMethod(methods, "clearImmutableFlag"),
            "REGRESSION: GovernanceController must NOT expose clearImmutableFlag — " +
            "permanent restrictions are permanent by design");
        Assert.IsFalse(HasMethod(methods, "removeImmutableFlag"),
            "REGRESSION: GovernanceController must NOT expose removeImmutableFlag");
        Assert.IsFalse(HasMethod(methods, "unsetImmutableFlag"),
            "REGRESSION: GovernanceController must NOT expose unsetImmutableFlag");
    }

    // ─── MessageRouter: PublishGlobalRoot exists ──────────────────────

    [TestMethod]
    public void MessageRouter_Exposes_PublishGlobalRoot_AndGlobalRootGetter()
    {
        var methods = LoadMethods("NeoHub.MessageRouter");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "publishGlobalRoot"),
            "MessageRouter must expose publishGlobalRoot — gateway-published " +
            "aggregated message roots are the per-epoch L1 commitment that " +
            "Phase-5 cross-L2 inclusion proofs verify against");
        Assert.IsTrue(HasMethod(methods, "getGlobalRoot"),
            "MessageRouter must expose getGlobalRoot for off-chain consumers");
    }

    // ─── ExternalBridgeEscrow: Receive (inbound dispatch) ─────────────

    [TestMethod]
    public void ExternalBridgeEscrow_Exposes_Send_And_Receive()
    {
        var methods = LoadMethods("NeoHub.ExternalBridgeEscrow");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "send"),
            "ExternalBridgeEscrow must expose send (Neo → foreign outbound)");
        Assert.IsTrue(HasMethod(methods, "receive"),
            "ExternalBridgeEscrow must expose receive (foreign → Neo inbound)");
        // The NEP-17 hook MUST be present so unsolicited transfers can be
        // rejected loudly (rather than silently accepted as Send records).
        Assert.IsTrue(HasMethod(methods, "onNEP17Payment"),
            "ExternalBridgeEscrow must expose onNEP17Payment so a fat-fingered " +
            "direct transfer can be rejected at the bridge boundary");
    }

    // ─── SettlementManager: status enum surface ───────────────────────

    [TestMethod]
    public void SettlementManager_Exposes_VerifyWithdrawalLeafAt_And_Status()
    {
        var methods = LoadMethods("NeoHub.SettlementManager");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "verifyWithdrawalLeafAt"),
            "SettlementManager must expose verifyWithdrawalLeafAt — " +
            "SharedBridge calls this to gate withdrawal finalization on " +
            "the per-chain, per-batch Finalized state");
        Assert.IsTrue(HasMethod(methods, "verifyWithdrawalLeafWithProof"),
            "SettlementManager must expose verifyWithdrawalLeafWithProof — " +
            "the production withdrawal flow does inclusion-proof verification " +
            "against the canonical withdrawal root");
        Assert.IsTrue(HasMethod(methods, "getBatchStatus"),
            "SettlementManager must expose getBatchStatus so off-chain " +
            "consumers can distinguish Pending / Challengeable / Finalized");
    }

    // ─── EmergencyManager: escape hatch is replay-protected ───────────

    [TestMethod]
    public void EmergencyManager_Exposes_EscapeHatch_Methods()
    {
        var methods = LoadMethods("NeoHub.EmergencyManager");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "escapeHatchExit"),
            "EmergencyManager must expose escapeHatchExit (single-leaf fast path)");
        Assert.IsTrue(HasMethod(methods, "escapeHatchExitWithProof"),
            "EmergencyManager must expose escapeHatchExitWithProof (production " +
            "Merkle-proof path — the standard withdrawal flow under pause)");
        Assert.IsTrue(HasMethod(methods, "pause"),
            "EmergencyManager must expose pause (council multisig only)");
        Assert.IsTrue(HasMethod(methods, "resume"),
            "EmergencyManager must expose resume (owner / governance only)");
        Assert.IsTrue(HasMethod(methods, "isPaused"),
            "EmergencyManager must expose isPaused (read-only)");
    }

    // ─── MpcCommitteeVerifier: per-signer dedup live on the verifyInbound path ──

    [TestMethod]
    public void MpcCommitteeVerifier_Exposes_VerifyInboundMessage()
    {
        var methods = LoadMethods("NeoHub.MpcCommitteeVerifier");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "verifyInboundMessage"),
            "MpcCommitteeVerifier must expose verifyInboundMessage — the dispatch " +
            "entry-point ExternalBridgeRegistry calls. Without it, the duplicate-" +
            "signer dedup (load-bearing ECDSA malleability defense) is unreachable.");
        Assert.IsTrue(HasMethod(methods, "registerCommittee"),
            "MpcCommitteeVerifier must expose registerCommittee");
        Assert.IsTrue(HasMethod(methods, "registerCommitteeWithMembers"),
            "MpcCommitteeVerifier must expose registerCommitteeWithMembers — " +
            "Phase-C equivocation slashing requires the per-signer bond-holder binding");
    }

    // ─── ForcedInclusion: censorship-resistance surface ───────────────

    [TestMethod]
    public void ForcedInclusion_Exposes_EnqueueAndConsume()
    {
        var methods = LoadMethods("NeoHub.ForcedInclusion");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "enqueueForcedTransaction"),
            "ForcedInclusion must expose enqueueForcedTransaction (user-side queue entry)");
        Assert.IsTrue(HasMethod(methods, "markConsumed"),
            "ForcedInclusion must expose markConsumed (sequencer dequeues post-inclusion)");
        Assert.IsTrue(HasMethod(methods, "reportCensorship"),
            "ForcedInclusion must expose reportCensorship (post-deadline escalation)");
    }
}
