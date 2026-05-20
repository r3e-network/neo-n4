using System.IO;
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
/// Reads `contracts/NeoHub.*/bin/sc/*.manifest.json` — produced by the CI
/// <c>contracts</c> job's nccs invocation. Skips with a pass when the manifest
/// isn't present (local dev without <c>nccs</c> on PATH) so the suite stays
/// green for code-only changes.
/// </remarks>
[TestClass]
public class UT_ContractManifestInvariants
{
    private static string? FindManifest(string contractName)
    {
        var rel = $"contracts/{contractName}/bin/sc/{contractName}.manifest.json";
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, rel);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    private static JArray? LoadMethods(string contract)
    {
        var path = FindManifest(contract);
        if (path is null) return null;
        var manifest = (JObject)JToken.Parse(File.ReadAllText(path))!;
        var abi = (JObject)manifest["abi"]!;
        return (JArray)abi["methods"]!;
    }

    private static bool HasMethod(JArray methods, string name) =>
        methods.Any(m => ((JObject?)m)?["name"]?.AsString() == name);

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
