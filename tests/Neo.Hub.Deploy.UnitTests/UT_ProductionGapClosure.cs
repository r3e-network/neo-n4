namespace Neo.Hub.Deploy.UnitTests;

[TestClass]
public class UT_ProductionGapClosure
{
    [TestMethod]
    public void Scaffold_IncludesDAValidatorAndL1TxFilter()
    {
        var plan = ScaffoldPlan.Default();
        var names = plan.Steps.Select(s => s.Name).ToHashSet();

        Assert.IsTrue(names.Contains("DAValidator"),
            "default NeoHub scaffold must deploy the L1 DA validator production gate");
        Assert.IsTrue(names.Contains("L1TxFilter"),
            "default NeoHub scaffold must deploy the optional L1->L2 transaction filter hook");

        var da = plan.Steps.Single(s => s.Name == "DAValidator");
        CollectionAssert.Contains(da.DependsOn.ToArray(), "DARegistry");
        Assert.AreEqual("OWNER_REPLACE_ME", da.DeployData[0]!.AsString());
        Assert.AreEqual("$step:DARegistry", da.DeployData[1]!.AsString());

        var filter = plan.Steps.Single(s => s.Name == "L1TxFilter");
        Assert.AreEqual(1, filter.DeployData.Count);
        Assert.AreEqual("OWNER_REPLACE_ME", filter.DeployData[0]!.AsString());
        Assert.AreEqual(0, filter.DependsOn.Count);
    }

    [TestMethod]
    public void PostDeployActions_SurfaceDAAndFilterWiring()
    {
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, name => H((byte)(name.Length & 0xFF)));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToList();

        Assert.IsTrue(actions.Any(a => a.Contains("SettlementManager.SetDARegistry")
            && a.Contains("DARegistry")), "operator hints must wire DARegistry into SettlementManager");
        Assert.IsTrue(actions.Any(a => a.Contains("SettlementManager.SetDAValidator")
            && a.Contains("DAValidator")), "operator hints must wire DAValidator into SettlementManager");
        Assert.IsTrue(actions.Any(a => a.Contains("MessageRouter.SetL1TxFilter")
            && a.Contains("L1TxFilter")), "operator hints must explain per-chain L1TxFilter wiring");
    }

    [TestMethod]
    public void Repository_UsesNeoCoreForkForL2NativeContracts()
    {
        var root = FindRepositoryRoot();
        var sln = File.ReadAllText(Path.Combine(root, "Neo.L2.sln"));
        var nativeContractSource = Path.Combine(root, "external", "neo", "src", "Neo", "SmartContract", "Native", "L2NativeContracts.cs");
        var nativeRegistrySource = Path.Combine(root, "external", "neo", "src", "Neo", "SmartContract", "Native", "NativeContract.cs");
        var nativeSourceText = File.ReadAllText(nativeContractSource);
        var nativeRegistryText = File.ReadAllText(nativeRegistrySource);
        string[] nativeContracts =
        [
            "L2SystemConfigContract",
            "L2BatchInfoContract",
            "L2MessageContract",
            "L2BridgeContract",
            "L2FeeContract",
            "L2PaymasterContract",
            "L2NativeExternalBridgeContract",
            "L2AccountAbstraction",
            "BridgedNep17Contract",
            "L2InteropVerifier"
        ];

        Assert.IsTrue(File.Exists(nativeContractSource),
            "N4 L2 system contracts must live in the r3e Neo core fork as native contracts.");
        Assert.IsTrue(File.Exists(nativeRegistrySource),
            "N4 L2 native contracts must be registered by Neo core NativeContract.");
        foreach (var nativeContract in nativeContracts)
        {
            StringAssert.Contains(nativeSourceText, $"public sealed class {nativeContract} : L2NativeContract");
            StringAssert.Contains(nativeRegistryText, $"public static {nativeContract}");
        }
        Assert.IsFalse(Directory.EnumerateDirectories(Path.Combine(root, "contracts"), "L2Native.*").Any(),
            "L2Native DevPack projects must not remain as later-deployed contracts.");
        Assert.IsFalse(sln.Contains("contracts\\L2Native.", StringComparison.OrdinalIgnoreCase),
            "Neo.L2.sln must not include later-deployed L2Native projects.");
    }

    [TestMethod]
    public void Repository_DeployPlanMatchesNeoHubContractInventory()
    {
        var root = FindRepositoryRoot();
        var neoHubContracts = Directory
            .EnumerateDirectories(Path.Combine(root, "contracts"), "NeoHub.*")
            .Select(Path.GetFileName)
            .Select(name => name!["NeoHub.".Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();
        var productionSteps = ScaffoldPlan.Default().Steps
            .Select(s => s.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expectedProductionContracts = neoHubContracts
            .Where(name => name != "ExternalBridgeStubVerifier")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(23, neoHubContracts.Length,
            "contracts/NeoHub.* must contain the 22 production contracts plus the test-only ExternalBridgeStubVerifier.");
        Assert.AreEqual(22, productionSteps.Length,
            "The default NeoHub deploy plan must emit only the 22 production contracts.");
        CollectionAssert.Contains(neoHubContracts, "ExternalBridgeStubVerifier");
        CollectionAssert.DoesNotContain(productionSteps, "ExternalBridgeStubVerifier",
            "ExternalBridgeStubVerifier is a dev/test helper and must not ship in the production NeoHub deploy bundle.");
        CollectionAssert.AreEqual(expectedProductionContracts, productionSteps,
            "The production deploy bundle must include every NeoHub contract except the test-only stub.");
    }

    [TestMethod]
    public void CurrentDocumentation_UsesCurrentNeoHubCounts()
    {
        var root = FindRepositoryRoot();
        string[] currentDocs =
        [
            "README.md",
            Path.Combine("docs", "README.md"),
            Path.Combine("docs", "architecture-l2-lifecycle.md"),
            Path.Combine("docs", "zh", "architecture-l2-lifecycle.md"),
            Path.Combine("contracts", "README.md"),
            "IMPLEMENTATION_STATUS.md"
        ];

        foreach (var relativePath in currentDocs)
        {
            var text = File.ReadAllText(Path.Combine(root, relativePath));
            Assert.IsFalse(text.Contains("20 contracts", StringComparison.OrdinalIgnoreCase),
                $"{relativePath} must not carry the obsolete 20-contract NeoHub count.");
            Assert.IsFalse(text.Contains("20 个合约", StringComparison.Ordinal),
                $"{relativePath} must not carry the obsolete Chinese 20-contract NeoHub count.");
        }

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        StringAssert.Contains(readme, "23 NeoHub L1 deployable contracts");
        StringAssert.Contains(readme, "22 production");
    }

    private static UInt160 H(byte b)
    {
        var bytes = new byte[20];
        for (var i = 0; i < 20; i++) bytes[i] = b;
        return new UInt160(bytes);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Neo.L2.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing Neo.L2.sln");
    }
}
