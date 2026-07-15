using System.Text;

namespace NeoHub.Sp1Groth16Verifier.UnitTests;

[TestClass]
public sealed class UT_FreshContractArtifacts
{
    private const string RequireFreshArtifactsVariable = "NEO_N4_REQUIRE_FRESH_MANIFESTS";

    [TestMethod]
    public void Sp1Groth16Verifier_FreshArtifactsMatchPinnedAudit()
    {
        if (!FreshArtifactsAreRequired()) return;

        AssertFreshArtifact(
            "NeoHub.Sp1Groth16Verifier",
            Sp1Groth16VerifierArtifact.Nef,
            Sp1Groth16VerifierArtifact.ManifestJson);
    }

    [TestMethod]
    public void ContractZkVerifier_FreshArtifactsMatchPinnedAudit()
    {
        if (!FreshArtifactsAreRequired()) return;

        AssertFreshArtifact(
            "NeoHub.ContractZkVerifier",
            ContractZkVerifierArtifact.Nef,
            ContractZkVerifierArtifact.ManifestJson);
    }

    private static bool FreshArtifactsAreRequired()
    {
        var value = Environment.GetEnvironmentVariable(RequireFreshArtifactsVariable);
        if (string.IsNullOrEmpty(value)) return false;

        Assert.AreEqual(
            "1",
            value,
            $"{RequireFreshArtifactsVariable} must be unset or exactly '1'; refusing to bypass the fresh-artifact gate.");
        return true;
    }

    private static void AssertFreshArtifact(
        string contractName,
        byte[] auditedNef,
        string auditedManifest)
    {
        var artifactDirectory = Path.Combine(
            FindRepositoryRoot(),
            "contracts",
            contractName,
            "bin",
            "sc");
        var nefPath = Path.Combine(artifactDirectory, $"{contractName}.nef");
        var manifestPath = Path.Combine(artifactDirectory, $"{contractName}.manifest.json");

        Assert.IsTrue(File.Exists(nefPath), $"Fresh NEF is required but missing: {nefPath}");
        Assert.IsTrue(File.Exists(manifestPath), $"Fresh manifest is required but missing: {manifestPath}");

        CollectionAssert.AreEqual(
            auditedNef,
            File.ReadAllBytes(nefPath),
            $"Fresh {contractName} NEF differs from the embedded audited artifact. " +
            "Use the pinned nccs version and audit any intentional compiler output change before updating the fixture.");

        CollectionAssert.AreEqual(
            Encoding.UTF8.GetBytes(auditedManifest.Trim()),
            File.ReadAllBytes(manifestPath),
            $"Fresh {contractName} manifest differs byte-for-byte from the embedded audited manifest. " +
            "Audit every manifest change before updating the fixture.");
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Neo.L2.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the neo-n4 repository root.");
    }
}
