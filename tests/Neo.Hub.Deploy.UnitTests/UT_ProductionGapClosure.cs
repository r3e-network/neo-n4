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
    public void Repository_DocumentsSeparateL1AndL2NeoCoreBranches()
    {
        var root = FindRepositoryRoot();
        var gitmodules = File.ReadAllText(Path.Combine(root, ".gitmodules"));
        var policy = File.ReadAllText(Path.Combine(root, "docs", "core-fork-policy.md"));
        var zhPolicy = File.ReadAllText(Path.Combine(root, "docs", "zh", "core-fork-policy.md"));

        StringAssert.Contains(gitmodules, "branch = r3e/neo-n4-core",
            "external/neo must keep tracking the L2 core branch by default.");
        Assert.IsFalse(gitmodules.Contains("r3e/neo-n3-core", StringComparison.Ordinal),
            "The L1 core branch must not replace the default external/neo L2 submodule pointer.");

        foreach (var doc in new[] { policy, zhPolicy })
        {
            StringAssert.Contains(doc, "master-n3");
            StringAssert.Contains(doc, "r3e/neo-n3-core");
            StringAssert.Contains(doc, "master");
            StringAssert.Contains(doc, "r3e/neo-n4-core");
        }
    }

    [TestMethod]
    public void Repository_DocumentsNeoHubAsDeployableContractsNotL1NativeContracts()
    {
        var root = FindRepositoryRoot();
        var l2NativeRegistry = Path.Combine(root, "external", "neo", "src", "Neo", "SmartContract", "Native", "NativeContract.cs");
        var l2NativeNeoHubPath = Path.Combine(root, "external", "neo", "src", "Neo", "SmartContract", "Native", "NeoHub");
        var policy = File.ReadAllText(Path.Combine(root, "docs", "core-fork-policy.md"));
        var neohub = File.ReadAllText(Path.Combine(root, "docs", "neohub-architecture-and-workflows.md"));
        var zhPolicy = File.ReadAllText(Path.Combine(root, "docs", "zh", "core-fork-policy.md"));
        var zhNeoHub = File.ReadAllText(Path.Combine(root, "docs", "zh", "neohub-architecture-and-workflows.md"));
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));

        Assert.IsFalse(File.ReadAllText(l2NativeRegistry).Contains("NeoHub", StringComparison.Ordinal),
            "The vendored L2 core NativeContract registry must not register NeoHub business contracts.");
        Assert.IsFalse(Directory.Exists(l2NativeNeoHubPath),
            "NeoHub must not be reintroduced under the Neo core Native/NeoHub folder.");

        StringAssert.Contains(policy, "NeoHub L1 contracts are a different boundary: they are deployed L1 contracts");
        StringAssert.Contains(policy, "must not register NeoHub business contracts under");
        StringAssert.Contains(neohub, "NeoHub is not an L1 native-contract set");
        StringAssert.Contains(readme, "NeoHub is deployed");

        StringAssert.Contains(zhPolicy, "不应在 `NativeContract` 中注册 NeoHub 业务合约");
        StringAssert.Contains(zhNeoHub, "NeoHub 不是");
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

    [TestMethod]
    public void CurrentDocumentation_EveryEnglishMarkdownHasChineseCounterpart()
    {
        var root = FindRepositoryRoot();
        var missing = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => IsMarkdown(path) && !IsSkippedPath(root, path) && !IsChinesePath(root, path))
            .Select(path => Relative(root, path))
            .Select(relative => new { English = relative, Chinese = ExpectedChineseMarkdown(relative) })
            .Where(pair => !File.Exists(Path.Combine(root, pair.Chinese)))
            .Select(pair => $"{pair.English} -> {pair.Chinese}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(0, missing.Length,
            "Every English Markdown document must have a Chinese counterpart:\n" + string.Join('\n', missing));
    }

    [TestMethod]
    public void CurrentDocumentation_EveryEnglishFigureHasChineseCounterpart()
    {
        var root = FindRepositoryRoot();
        var missing = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => IsFigure(path) && !IsSkippedPath(root, path) && !IsChinesePath(root, path))
            .Select(path => Relative(root, path))
            .Select(relative => new { English = relative, Chinese = ExpectedChineseFigure(relative) })
            .Where(pair => !File.Exists(Path.Combine(root, pair.Chinese)))
            .Select(pair => $"{pair.English} -> {pair.Chinese}")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(0, missing.Length,
            "Every English figure/diagram asset must have a Chinese counterpart:\n" + string.Join('\n', missing));
    }

    [TestMethod]
    public void CurrentDocumentation_ChineseMarkdownCounterpartsContainChineseText()
    {
        var root = FindRepositoryRoot();
        var missingChineseText = Directory
            .EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path => IsMarkdown(path) && !IsSkippedPath(root, path) && !IsChinesePath(root, path))
            .Select(path => ExpectedChineseMarkdown(Relative(root, path)))
            .Where(relative =>
            {
                var zhPath = Path.Combine(root, relative);
                return File.Exists(zhPath) && !File.ReadAllText(zhPath).Any(IsChineseCharacter);
            })
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(0, missingChineseText.Length,
            "Chinese Markdown counterparts must contain localized Chinese text:\n" + string.Join('\n', missingChineseText));
    }

    [TestMethod]
    public void CurrentDocumentation_ChineseSvgFiguresContainChineseText()
    {
        var root = FindRepositoryRoot();
        var missingChineseText = Directory
            .EnumerateFiles(Path.Combine(root, "docs", "zh", "figures"), "*.svg", SearchOption.AllDirectories)
            .Where(path => !File.ReadAllText(path).Any(IsChineseCharacter))
            .Select(path => Relative(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(0, missingChineseText.Length,
            "Chinese SVG counterparts must contain localized Chinese text:\n" + string.Join('\n', missingChineseText));
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

    private static bool IsMarkdown(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mdx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFigure(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".svg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mermaid", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".mmd", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".drawio", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".puml", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".dot", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSkippedPath(string root, string path)
    {
        var relative = Relative(root, path);
        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is ".git" or "bin" or "obj" or "target" or "book" or "node_modules"))
            return true;

        return relative.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo-devpack-dotnet/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo-riscv-vm/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo-zkvm/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/foreign-contracts/eth/lib/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChinesePath(string root, string path)
        => Relative(root, path).StartsWith("docs/zh/", StringComparison.OrdinalIgnoreCase);

    private static string ExpectedChineseMarkdown(string relative)
        => relative.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
            ? "docs/zh/" + relative["docs/".Length..]
            : "docs/zh/" + relative;

    private static string ExpectedChineseFigure(string relative)
        => relative.StartsWith("docs/figures/", StringComparison.OrdinalIgnoreCase)
            ? "docs/zh/figures/" + relative["docs/figures/".Length..]
            : "docs/zh/" + relative;

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsChineseCharacter(char c) => c is >= '\u4e00' and <= '\u9fff';
}
