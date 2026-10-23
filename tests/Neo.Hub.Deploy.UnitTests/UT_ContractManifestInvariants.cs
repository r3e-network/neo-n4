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

    [TestMethod]
    public void NeoHubContracts_UseR3EMaintainerAttribution()
    {
        var contracts = new[]
        {
            "NeoHub.RollupHub",
            "NeoHub.SharedBridge",
            "NeoHub.ZkVerifier",
            "NeoHub.GovernanceController"
        };

        foreach (var contract in contracts)
        {
            var manifest = ManifestTestHelper.LoadFreshManifest(contract);
            if (manifest is null) continue;

            var extra = (JObject)manifest["extra"]!;
            Assert.AreEqual(
                "R3E Network",
                extra["Author"]?.AsString(),
                $"{contract} must identify its actual maintainer in release artifacts");
        }
    }

    [TestMethod]
    public void OwnerManagedContracts_ExposeOwnershipTransfer()
    {
        var contracts = new[]
        {
            "NeoHub.RollupHub",
            "NeoHub.SharedBridge",
            "NeoHub.ZkVerifier",
            "NeoHub.GovernanceController"
        };

        foreach (var contract in contracts)
        {
            var methods = LoadMethods(contract);
            if (methods is null) continue;

            Assert.IsTrue(HasMethod(methods, "getOwner"),
                $"{contract} must expose getOwner for operator/auditor introspection");
            Assert.IsTrue(HasMethod(methods, "setOwner"),
                $"{contract} must expose setOwner so governance can rotate compromised or deprecated owner keys");
        }
    }

    // ─── ContractZkVerifier: contract-deployed proof route ───────────────────

    [TestMethod]
    public void ZkVerifier_Exposes_ContractVerifierRoute_AndNoNativeAccelerator()
    {
        // Retargeted from the pre-consolidation NeoHub.ContractZkVerifier: the 4-pillar
        // consolidation folded the deployable-verifier route into NeoHub.ZkVerifier.
        var methods = LoadMethods("NeoHub.ZkVerifier");
        if (methods is null) return;

        Assert.IsTrue(HasMethod(methods, "registerVerificationKey"),
            "ZkVerifier must expose registerVerificationKey");
        Assert.IsTrue(HasMethod(methods, "registerProofVerifier"),
            "ZkVerifier must expose registerProofVerifier for deployable verifier contracts");
        Assert.IsTrue(HasMethod(methods, "getProofVerifier"),
            "ZkVerifier must expose getProofVerifier for operator/audit introspection");
        Assert.IsTrue(HasMethod(methods, "setEnvelopeOnlyAllowed"),
            "ZkVerifier must expose explicit devnet-only envelope mode");
        Assert.IsTrue(HasMethod(methods, "isEnvelopeOnlyAllowed"),
            "ZkVerifier must expose isEnvelopeOnlyAllowed");
        Assert.IsTrue(HasMethod(methods, "disableEnvelopeOnlyPermanently"),
            "ZkVerifier must expose disableEnvelopeOnlyPermanently — the one-way " +
            "switch a rollup uses to make 'never accept an unverified batch' irreversible");
        Assert.IsTrue(HasMethod(methods, "isEnvelopeOnlyLocked"),
            "ZkVerifier must expose isEnvelopeOnlyLocked (read-only audit of the lock)");
        Assert.IsTrue(HasMethod(methods, "verifyProof"),
            "ZkVerifier must expose verifyProof for VerifierRegistry dispatch");

        Assert.IsFalse(HasMethod(methods, "setNativeAccelerator"),
            "REGRESSION: ZkVerifier must not require an L1 native accelerator");
        Assert.IsFalse(HasMethod(methods, "getNativeAccelerator"),
            "REGRESSION: ZkVerifier must not expose native-accelerator state");

        // Critical: the envelope-only disable is one-way. There must be NO method
        // that unlocks or clears it — a regression adding one would let a future
        // (possibly compromised) owner re-enable "accept any proof with no crypto
        // check" on a chain that had permanently locked the door.
        Assert.IsFalse(HasMethod(methods, "unlockEnvelopeOnly"),
            "REGRESSION: ZkVerifier must NOT expose unlockEnvelopeOnly — " +
            "the permanent envelope-only disable is irreversible by design");
        Assert.IsFalse(HasMethod(methods, "clearEnvelopeOnlyLock"),
            "REGRESSION: ZkVerifier must NOT expose clearEnvelopeOnlyLock");
        Assert.IsFalse(HasMethod(methods, "removeEnvelopeOnlyLock"),
            "REGRESSION: ZkVerifier must NOT expose removeEnvelopeOnlyLock");
        Assert.IsFalse(HasMethod(methods, "enableEnvelopeOnlyPermanently"),
            "REGRESSION: ZkVerifier must NOT expose enableEnvelopeOnlyPermanently");
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

    // ─── Global root publication: folded into RollupHub by the 4-pillar
    // consolidation (the pre-consolidation MessageRouter owned it) ────

    [TestMethod]
    public void RollupHub_Exposes_GatewayGlobalRootPublication_AndGetter()
    {
        var methods = LoadMethods("NeoHub.RollupHub");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "publishGatewayGlobalRoot"),
            "RollupHub must expose publishGatewayGlobalRoot — gateway-published " +
            "aggregated message roots are the per-epoch L1 commitment that " +
            "Phase-5 cross-L2 inclusion proofs verify against");
        Assert.IsTrue(HasMethod(methods, "getGlobalRoot"),
            "RollupHub must expose getGlobalRoot for off-chain consumers");
        Assert.IsTrue(HasMethod(methods, "getGatewayFinalizedThrough"),
            "RollupHub must expose the non-revertible per-chain Gateway watermark");
    }

    // ─── ExternalBridgeEscrow: Receive (inbound dispatch) ─────────────

    // NOTE: the pre-consolidation ExternalBridgeEscrow / MpcCommitteeVerifier /
    // ExternalBridgeStubVerifier manifest invariants were removed with the 4-pillar
    // lean consolidation — those contracts are not part of the deployable suite yet.
    // When the external-bridge stack lands, restore invariants here AND anchor its
    // canonical encodings to tests/Shared/canonical_encoding_vectors.hex (see
    // docs/audit/systematic-audit-2026-10-21.md A.11 — the watcher already signs the
    // external-message layout; the on-chain consumer must match it byte for byte).

    [TestMethod]
    public void TokenIngressContracts_Expose_Nep17PaymentHooks()
    {
        var contracts = new[]
        {
            "NeoHub.SharedBridge"
        };

        foreach (var contract in contracts)
        {
            var methods = LoadMethods(contract);
            if (methods is null) continue;

            Assert.IsTrue(HasMethod(methods, "onNEP17Payment"),
                $"{contract} must reject unsolicited NEP-17 transfers instead of silently orphaning funds");
        }
    }

    [TestMethod]
    public void SharedBridge_Exposes_PausedEmergencyWithdrawalPayout()
    {
        var methods = LoadMethods("NeoHub.SharedBridge");
        if (methods is null) return;

        Assert.IsTrue(HasMethod(methods, "finalizeWithdrawalWithProof"),
            "SharedBridge must expose the normal multi-leaf withdrawal payout path");
        Assert.IsTrue(HasMethod(methods, "emergencyFinalizeWithdrawalWithProof"),
            "SharedBridge must expose a paused-only emergency withdrawal path that pays assets");
    }

    // ─── Settlement surface: folded into RollupHub by the consolidation ──

    [TestMethod]
    public void RollupHub_Exposes_WithdrawalLeafVerification_And_Status()
    {
        var methods = LoadMethods("NeoHub.RollupHub");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "verifyWithdrawalLeafAt"),
            "RollupHub must expose verifyWithdrawalLeafAt — " +
            "SharedBridge calls this to gate withdrawal finalization on " +
            "the per-chain, per-batch Finalized state");
        Assert.IsTrue(HasMethod(methods, "verifyWithdrawalLeafWithProof"),
            "RollupHub must expose verifyWithdrawalLeafWithProof — " +
            "the production withdrawal flow does inclusion-proof verification " +
            "against the canonical withdrawal root");
        Assert.IsTrue(HasMethod(methods, "getBatchStatus"),
            "RollupHub must expose getBatchStatus so off-chain " +
            "consumers can distinguish Pending / Challengeable / Finalized");
        Assert.IsTrue(HasMethod(methods, "publishGatewayGlobalRoot"),
            "RollupHub must atomically validate Gateway constituent finality before routing a root");
        Assert.IsTrue(HasMethod(methods, "getGatewayFinalizedThrough"),
            "RollupHub must expose the non-revertible per-chain Gateway watermark");
        // The pre-consolidation setMessageRouter/getMessageRouter wiring became
        // setSharedBridge/getSharedBridge: the global-root fan-out target.
        Assert.IsTrue(HasMethod(methods, "setSharedBridge") && HasMethod(methods, "getSharedBridge"),
            "RollupHub must expose auditable SharedBridge deployment wiring");
    }

    // ─── Emergency surface: escape hatch on SharedBridge, pause on
    // GovernanceController (the consolidation folded EmergencyManager) ──

    [TestMethod]
    public void EmergencySurface_IsSplitBetweenSharedBridgeAndGovernance()
    {
        var bridge = LoadMethods("NeoHub.SharedBridge");
        if (bridge is null) return;
        Assert.IsTrue(HasMethod(bridge, "emergencyFinalizeWithdrawalWithProof"),
            "SharedBridge must expose a paused-only emergency withdrawal path that pays assets");
        Assert.IsTrue(HasMethod(bridge, "getEmergencyManager") && HasMethod(bridge, "setEmergencyManager"),
            "SharedBridge must expose auditable emergency-manager wiring");

        var governance = LoadMethods("NeoHub.GovernanceController");
        if (governance is null) return;
        Assert.IsTrue(HasMethod(governance, "pause"),
            "GovernanceController must expose pause (council multisig only)");
        Assert.IsTrue(HasMethod(governance, "unpause"),
            "GovernanceController must expose unpause (owner / governance only)");
        Assert.IsTrue(HasMethod(governance, "isPaused"),
            "GovernanceController must expose isPaused (read-only)");
    }

    [TestMethod]
    public void PauserSurface_IsGovernanceControlled()
    {
        // Retargeted from the pre-consolidation ChainRegistry registerPauser surface:
        // chain pause authority now lives on GovernanceController (pauseChain per chain)
        // and RollupHub relays per-chain pause/resume for censorship escalation.
        var governance = LoadMethods("NeoHub.GovernanceController");
        if (governance is null) return;
        Assert.IsTrue(HasMethod(governance, "pauseChain"),
            "GovernanceController must let governance pause a censored L2");
        Assert.IsTrue(HasMethod(governance, "unpauseChain"),
            "GovernanceController must let governance resume a paused L2");
        Assert.IsTrue(HasMethod(governance, "isChainPaused"),
            "GovernanceController must expose isChainPaused for deployment smoke checks");

        var rollup = LoadMethods("NeoHub.RollupHub");
        if (rollup is null) return;
        Assert.IsTrue(HasMethod(rollup, "pauseChain") && HasMethod(rollup, "resumeChain"),
            "RollupHub must relay per-chain pause/resume for censorship escalation");
        Assert.IsTrue(HasMethod(rollup, "reportCensorship"),
            "RollupHub must expose reportCensorship (post-deadline escalation)");
        Assert.IsTrue(HasMethod(rollup, "isCensorshipReported"),
            "RollupHub must make censorship reports at-most-once per queue entry");
    }

    // NOTE: MpcCommitteeVerifier / ExternalBridgeStubVerifier invariants removed —
    // see the note above the TokenIngress tests (external-bridge stack not landed).

    // ─── Forced inclusion: folded into RollupHub by the consolidation ──

    [TestMethod]
    public void RollupHub_Exposes_ForcedInclusionEnqueueAndConsume()
    {
        var methods = LoadMethods("NeoHub.RollupHub");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "enqueueForcedTransaction"),
            "RollupHub must expose enqueueForcedTransaction (user-side queue entry)");
        Assert.IsTrue(HasMethod(methods, "consume"),
            "RollupHub must verify finalized transaction inclusion before consumption");
        Assert.IsFalse(HasMethod(methods, "markConsumed"),
            "RollupHub must not expose a witness-only arbitrary-nonce consumption bypass");
        // The pre-consolidation setSequencerBond / setChainRegistry /
        // setCensorshipSlashAmount / isProductionReady surface was absorbed by
        // GovernanceController's sequencer bond registry (depositBond / getSequencerBond /
        // slashSequencer / hasMinBond) and RollupHub's pauseChain escalation.
        Assert.IsTrue(HasMethod(methods, "getNextForcedNonce"),
            "RollupHub must expose getNextForcedNonce (FIFO queue introspection)");
        Assert.IsTrue(HasMethod(methods, "getPendingForcedCount"),
            "RollupHub must expose getPendingForcedCount (queue depth observability)");
    }
}
