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
            "NeoHub.ChainRegistry",
            "NeoHub.ContractZkVerifier",
            "NeoHub.DARegistry",
            "NeoHub.DAValidator",
            "NeoHub.EmergencyManager",
            "NeoHub.ExternalBridgeBond",
            "NeoHub.ExternalBridgeEscrow",
            "NeoHub.ExternalBridgeRegistry",
            "NeoHub.ExternalBridgeStubVerifier",
            "NeoHub.ForcedInclusion",
            "NeoHub.GovernanceController",
            "NeoHub.GovernanceFraudVerifier",
            "NeoHub.L1TxFilter",
            "NeoHub.MessageRouter",
            "NeoHub.MpcCommitteeFraudVerifier",
            "NeoHub.MpcCommitteeVerifier",
            "NeoHub.OptimisticChallenge",
            "NeoHub.RestrictedExecutionFraudVerifier",
            "NeoHub.SequencerBond",
            "NeoHub.SequencerRegistry",
            "NeoHub.SettlementManager",
            "NeoHub.SharedBridge",
            "NeoHub.Sp1Groth16Verifier",
            "NeoHub.TokenRegistry",
            "NeoHub.VerifierRegistry"
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
            "NeoHub.ChainRegistry",
            "NeoHub.ContractZkVerifier",
            "NeoHub.DARegistry",
            "NeoHub.DAValidator",
            "NeoHub.EmergencyManager",
            "NeoHub.ExternalBridgeBond",
            "NeoHub.ExternalBridgeEscrow",
            "NeoHub.ExternalBridgeRegistry",
            "NeoHub.ForcedInclusion",
            "NeoHub.GovernanceController",
            "NeoHub.L1TxFilter",
            "NeoHub.MessageRouter",
            "NeoHub.MpcCommitteeFraudVerifier",
            "NeoHub.MpcCommitteeVerifier",
            "NeoHub.OptimisticChallenge",
            "NeoHub.SequencerBond",
            "NeoHub.SequencerRegistry",
            "NeoHub.SettlementManager",
            "NeoHub.SharedBridge",
            "NeoHub.TokenRegistry",
            "NeoHub.VerifierRegistry"
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
        Assert.IsTrue(HasMethod(methods, "disableEnvelopeOnlyPermanently"),
            "ContractZkVerifier must expose disableEnvelopeOnlyPermanently — the one-way " +
            "switch a rollup uses to make 'never accept an unverified batch' irreversible");
        Assert.IsTrue(HasMethod(methods, "isEnvelopeOnlyLocked"),
            "ContractZkVerifier must expose isEnvelopeOnlyLocked (read-only audit of the lock)");
        Assert.IsTrue(HasMethod(methods, "verify"),
            "ContractZkVerifier must expose verify for VerifierRegistry dispatch");

        Assert.IsFalse(HasMethod(methods, "setNativeAccelerator"),
            "REGRESSION: ContractZkVerifier must not require an L1 native accelerator");
        Assert.IsFalse(HasMethod(methods, "getNativeAccelerator"),
            "REGRESSION: ContractZkVerifier must not expose native-accelerator state");

        // Critical: the envelope-only disable is one-way. There must be NO method
        // that unlocks or clears it — a regression adding one would let a future
        // (possibly compromised) owner re-enable "accept any proof with no crypto
        // check" on a chain that had permanently locked the door. Mirrors the
        // GovernanceController.SetImmutableFlag no-clear-side invariant below.
        Assert.IsFalse(HasMethod(methods, "unlockEnvelopeOnly"),
            "REGRESSION: ContractZkVerifier must NOT expose unlockEnvelopeOnly — " +
            "the permanent envelope-only disable is irreversible by design");
        Assert.IsFalse(HasMethod(methods, "clearEnvelopeOnlyLock"),
            "REGRESSION: ContractZkVerifier must NOT expose clearEnvelopeOnlyLock");
        Assert.IsFalse(HasMethod(methods, "removeEnvelopeOnlyLock"),
            "REGRESSION: ContractZkVerifier must NOT expose removeEnvelopeOnlyLock");
        Assert.IsFalse(HasMethod(methods, "enableEnvelopeOnlyPermanently"),
            "REGRESSION: ContractZkVerifier must NOT expose enableEnvelopeOnlyPermanently");
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

    [TestMethod]
    public void TokenIngressContracts_Expose_Nep17PaymentHooks()
    {
        var contracts = new[]
        {
            "NeoHub.SharedBridge",
            "NeoHub.SequencerBond",
            "NeoHub.ExternalBridgeBond",
            "NeoHub.ExternalBridgeEscrow"
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
        Assert.IsTrue(HasMethod(methods, "publishGatewayGlobalRoot"),
            "SettlementManager must atomically validate Gateway constituent finality before routing a root");
        Assert.IsTrue(HasMethod(methods, "getGatewayFinalizedThrough"),
            "SettlementManager must expose the non-revertible per-chain Gateway watermark");
        Assert.IsTrue(HasMethod(methods, "setMessageRouter") && HasMethod(methods, "getMessageRouter"),
            "SettlementManager must expose auditable MessageRouter deployment wiring");
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

    [TestMethod]
    public void ChainRegistry_Exposes_AuthorizedPauserSurface()
    {
        var methods = LoadMethods("NeoHub.ChainRegistry");
        if (methods is null) return;

        Assert.IsTrue(HasMethod(methods, "registerPauser"),
            "ChainRegistry must let governance authorize fault contracts to pause a censored L2");
        Assert.IsTrue(HasMethod(methods, "revokePauser"),
            "ChainRegistry must let governance revoke compromised chain pausers");
        Assert.IsTrue(HasMethod(methods, "isPauser"),
            "ChainRegistry must expose isPauser for deployment smoke checks");
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
        Assert.IsFalse(HasMethod(methods, "consumeNonce"),
            "MpcCommitteeVerifier must not expose public nonce consumption — replay state " +
            "belongs to ExternalBridgeEscrow finalization so callers cannot pre-consume nonces");
        Assert.IsTrue(HasMethod(methods, "registerCommittee"),
            "MpcCommitteeVerifier must expose registerCommittee");
        Assert.IsTrue(HasMethod(methods, "registerCommitteeWithMembers"),
            "MpcCommitteeVerifier must expose registerCommitteeWithMembers — " +
            "Phase-C equivocation slashing requires the per-signer bond-holder binding");
    }

    [TestMethod]
    public void ExternalBridgeStubVerifier_IsNotProductionBridgeKind()
    {
        var methods = LoadMethods("NeoHub.ExternalBridgeStubVerifier");
        if (methods is null) return;

        Assert.IsTrue(HasMethod(methods, "bridgeKind"),
            "ExternalBridgeStubVerifier must expose bridgeKind so registry kind matching rejects it as production");
    }

    // ─── ForcedInclusion: censorship-resistance surface ───────────────

    [TestMethod]
    public void ForcedInclusion_Exposes_EnqueueAndConsume()
    {
        var methods = LoadMethods("NeoHub.ForcedInclusion");
        if (methods is null) return;
        Assert.IsTrue(HasMethod(methods, "enqueueForcedTransaction"),
            "ForcedInclusion must expose enqueueForcedTransaction (user-side queue entry)");
        Assert.IsTrue(HasMethod(methods, "consume"),
            "ForcedInclusion must verify finalized transaction inclusion before consumption");
        Assert.IsFalse(HasMethod(methods, "markConsumed"),
            "ForcedInclusion must not expose a witness-only arbitrary-nonce consumption bypass");
        Assert.IsTrue(HasMethod(methods, "reportCensorship"),
            "ForcedInclusion must expose reportCensorship (post-deadline escalation)");
        Assert.IsTrue(HasMethod(methods, "isCensorshipReported"),
            "ForcedInclusion must make censorship reports at-most-once per queue entry");
        Assert.IsTrue(HasMethod(methods, "setSequencerBond"),
            "ForcedInclusion must wire SequencerBond so censorship reports slash bonds");
        Assert.IsTrue(HasMethod(methods, "setChainRegistry"),
            "ForcedInclusion must wire ChainRegistry so censorship reports pause finalization");
        Assert.IsTrue(HasMethod(methods, "setCensorshipSlashAmount"),
            "ForcedInclusion must expose the per-report slash policy");
        Assert.IsTrue(HasMethod(methods, "isProductionReady"),
            "ForcedInclusion must expose a fail-closed production configuration gate");
    }
}
