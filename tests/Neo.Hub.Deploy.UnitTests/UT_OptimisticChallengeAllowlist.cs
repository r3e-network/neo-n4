using System.IO;
using Neo.Json;

namespace Neo.Hub.Deploy.UnitTests;

/// <summary>
/// Pin the <c>OptimisticChallenge</c> contract's fraud-verifier allowlist ABI
/// surface against the deploy planner's post-deploy wiring. The planner emits
/// <c>OptimisticChallenge.RegisterFraudVerifier(...)</c> as a required step;
/// without this test, a future contract refactor that renames/removes the
/// method would only surface at L1 deploy-execution time when the planner's
/// instruction reverts. Pinning the manifest here moves the failure to the
/// CI contract-compile job, where it's free to fix.
/// </summary>
/// <remarks>
/// Reads <c>contracts/NeoHub.OptimisticChallenge/bin/sc/NeoHub.OptimisticChallenge.manifest.json</c>
/// — produced by the CI <c>contracts</c> job's nccs invocation. Skips with a
/// pass-through when the manifest isn't present (local dev without <c>nccs</c>
/// on <c>PATH</c>) so the suite stays green for code-only changes.
/// </remarks>
[TestClass]
public class UT_OptimisticChallengeAllowlist
{
    private const string ManifestRel =
        "contracts/NeoHub.OptimisticChallenge/bin/sc/NeoHub.OptimisticChallenge.manifest.json";

    private static string? FindManifest()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir != null; i++)
        {
            var candidate = Path.Combine(dir, ManifestRel);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    [TestMethod]
    public void Manifest_Exposes_RegisterFraudVerifier_WithUInt160Param()
    {
        var path = FindManifest();
        if (path is null)
        {
            // nccs not on PATH for this build — let the CI gate catch the gap.
            return;
        }
        var manifest = (JObject)JToken.Parse(File.ReadAllText(path))!;
        var abi = (JObject)manifest["abi"]!;
        var methods = (JArray)abi["methods"]!;
        var registerFraudVerifier = methods.FirstOrDefault(m =>
            ((JObject?)m)?["name"]?.AsString() == "registerFraudVerifier");
        Assert.IsNotNull(registerFraudVerifier,
            "OptimisticChallenge must expose registerFraudVerifier — the deploy " +
            "planner emits 'OptimisticChallenge.RegisterFraudVerifier(...)' as a " +
            "required post-deploy wiring step (see UT_DeployPlanner). A missing " +
            "method here would manifest as a runtime revert at deploy time.");
        var rfvObj = (JObject)registerFraudVerifier!;
        Assert.AreEqual("Void", rfvObj["returntype"]?.AsString(), "registerFraudVerifier must return Void");
        var rfvParams = (JArray)rfvObj["parameters"]!;
        Assert.AreEqual(1, rfvParams.Count, "registerFraudVerifier must take exactly one parameter");
        var p0 = (JObject)rfvParams[0]!;
        Assert.AreEqual("verifier", p0["name"]?.AsString(), "parameter must be named 'verifier'");
        Assert.AreEqual("Hash160", p0["type"]?.AsString(), "parameter must be Hash160 (UInt160)");
    }

    [TestMethod]
    public void Manifest_Exposes_IsApprovedFraudVerifier_AsSafe()
    {
        var path = FindManifest();
        if (path is null) return;
        var manifest = (JObject)JToken.Parse(File.ReadAllText(path))!;
        var abi = (JObject)manifest["abi"]!;
        var methods = (JArray)abi["methods"]!;
        var isApproved = methods.FirstOrDefault(m =>
            ((JObject?)m)?["name"]?.AsString() == "isApprovedFraudVerifier");
        Assert.IsNotNull(isApproved,
            "OptimisticChallenge must expose isApprovedFraudVerifier — front-ends + " +
            "operators rely on the read-only check before submitting a Challenge");
        var iaObj = (JObject)isApproved!;
        Assert.AreEqual("Boolean", iaObj["returntype"]?.AsString());
        Assert.IsTrue(iaObj["safe"]!.AsBoolean(),
            "isApprovedFraudVerifier must be [Safe] so wallets / dashboards can call it without a witness");
    }

    [TestMethod]
    public void Manifest_Exposes_RevokeFraudVerifier_WithUInt160Param()
    {
        var path = FindManifest();
        if (path is null) return;
        var manifest = (JObject)JToken.Parse(File.ReadAllText(path))!;
        var abi = (JObject)manifest["abi"]!;
        var methods = (JArray)abi["methods"]!;
        var revoke = methods.FirstOrDefault(m =>
            ((JObject?)m)?["name"]?.AsString() == "revokeFraudVerifier");
        Assert.IsNotNull(revoke,
            "OptimisticChallenge must expose revokeFraudVerifier so governance can " +
            "remove a compromised verifier from the allowlist");
        var rObj = (JObject)revoke!;
        Assert.AreEqual("Void", rObj["returntype"]?.AsString());
        var rParams = (JArray)rObj["parameters"]!;
        Assert.AreEqual(1, rParams.Count);
        var p0 = (JObject)rParams[0]!;
        Assert.AreEqual("verifier", p0["name"]?.AsString());
        Assert.AreEqual("Hash160", p0["type"]?.AsString());
    }

    [TestMethod]
    public void Manifest_Declares_FraudVerifierApproved_Event()
    {
        var path = FindManifest();
        if (path is null) return;
        var manifest = (JObject)JToken.Parse(File.ReadAllText(path))!;
        var abi = (JObject)manifest["abi"]!;
        var events = (JArray)abi["events"]!;
        var ev = events.FirstOrDefault(e =>
            ((JObject?)e)?["name"]?.AsString() == "FraudVerifierApproved");
        Assert.IsNotNull(ev,
            "OptimisticChallenge must declare FraudVerifierApproved so off-chain " +
            "indexers can detect when a new verifier is allowlisted");
    }
}
