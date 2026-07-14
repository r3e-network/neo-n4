namespace Neo.Hub.Deploy.UnitTests;

[TestClass]
public class UT_ProductionGapClosure
{
    [TestMethod]
    public void ProductionScaffold_BindsAndLocksRealSp1Verifier()
    {
        var plan = ScaffoldPlan.Default();
        var names = plan.Steps.Select(step => step.Name).ToHashSet(StringComparer.Ordinal);

        CollectionAssert.Contains(names.ToArray(), "Sp1Groth16Verifier",
            "the production bundle must ship the SP1 Groth16 terminal verifier");

        var bundle = DeployPlanner.Plan(plan, name => H((byte)(name.Length & 0xFF)));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToArray();

        Assert.IsTrue(actions.Any(action => action.Contains(
            "ContractZkVerifier.RegisterProofVerifier(ProofSystem.Sp1=1, Sp1Groth16Verifier",
            StringComparison.Ordinal)),
            "the production wiring must route SP1 proof math to the in-repo terminal verifier");
        Assert.IsTrue(actions.Any(action => action.Contains(
            "ContractZkVerifier.DisableEnvelopeOnlyPermanently(ProofSystem.Sp1=1)",
            StringComparison.Ordinal)),
            "the production wiring must irreversibly close the envelope-only escape hatch");
        Assert.IsTrue(actions.Any(action => action.Contains(
            "ContractZkVerifier.LockProofSystemConfiguration(ProofSystem.Sp1=1, PROGRAM_VKEY_REPLACE_ME)",
            StringComparison.Ordinal)),
            "the production wiring must freeze the exact SP1 vkey and terminal verifier");
        Assert.IsFalse(actions.Any(action => action.Contains(
            "SetEnvelopeOnlyAllowed", StringComparison.Ordinal)),
            "production deployment instructions must never enable envelope-only acceptance");
    }

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
        Assert.IsTrue(actions.Any(a => a.Contains("SettlementManager.SetMessageRouter")
            && a.Contains("MessageRouter")),
            "operator hints must wire MessageRouter into SettlementManager's atomic Gateway path");
        Assert.IsTrue(actions.Any(a => a.Contains("MessageRouter.SetL1TxFilter")
            && a.Contains("L1TxFilter")), "operator hints must explain per-chain L1TxFilter wiring");
    }

    [TestMethod]
    public void PostDeployActions_CloseExternalBridgeInboundPayoutWiring()
    {
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, name => H((byte)(name.Length & 0xFF)));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToArray();

        Assert.IsTrue(actions.Any(action => action.Contains(
            "ExternalBridgeEscrow.SetGovernanceController(GovernanceController)",
            StringComparison.Ordinal)));
        Assert.IsTrue(actions.Any(action => action.Contains("ExternalBridgeEscrow.SetAssetRoute", StringComparison.Ordinal)
            && action.Contains("payoutVersion()==1", StringComparison.Ordinal)
            && action.Contains("UpdateCounter==0", StringComparison.Ordinal)
            && action.Contains("non-zero L2_CHAIN_ID_REPLACE_ME", StringComparison.Ordinal)
            && action.Contains("neoChainId=0", StringComparison.Ordinal)));
        Assert.IsTrue(actions.Any(action => action.Contains(
            "ExternalBridgeEscrow.FundLiquidity", StringComparison.Ordinal)
            && action.Contains("Neo L1 direct-release routes only", StringComparison.Ordinal)));
        Assert.IsTrue(actions.Any(action => action.Contains(
            "ExternalBridgeEscrow.LockGovernance()", StringComparison.Ordinal)
            && action.Contains("ConfigureAssetRouteViaProposal", StringComparison.Ordinal)));
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
    public void Repository_DocumentsContractDeployedZkVerifierBoundary()
    {
        var root = FindRepositoryRoot();
        string[] docs =
        [
            "README.md",
            Path.Combine("docs", "neohub-architecture-and-workflows.md"),
            Path.Combine("docs", "security-model.md"),
            Path.Combine("docs", "zh", "neohub-architecture-and-workflows.md"),
            Path.Combine("docs", "zh", "security-model.md"),
        ];

        foreach (var relativePath in docs)
        {
            var text = File.ReadAllText(Path.Combine(root, relativePath));
            StringAssert.Contains(text, "ContractZkVerifier");
            if (relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Equals("zh", StringComparison.OrdinalIgnoreCase)))
            {
                StringAssert.Contains(text, "可部署验证器合约");
            }
            else
            {
                StringAssert.Contains(text, "deployable verifier contract");
            }
        }
    }

    [TestMethod]
    public void ZksyncComparison_DocumentsNeoNativeReplicaPolicy()
    {
        var root = FindRepositoryRoot();
        var english = File.ReadAllText(Path.Combine(root, "docs", "zksync-comparison.md"));
        var chinese = File.ReadAllText(Path.Combine(root, "docs", "zh", "zksync-comparison.md"));

        string[] englishRequired =
        [
            "Neo-native 1:1 replica policy",
            "component role",
            "security invariant",
            "operator workflow",
            "Bridgehub",
            "Chain Type Manager",
            "Shared Bridge",
            "Gateway",
            "NeoFS",
            "RISC-V",
            "ContractZkVerifier",
            "envelope-only",
            "Direct-copy boundary",
        ];
        foreach (var required in englishRequired)
        {
            StringAssert.Contains(english, required);
        }

        string[] chineseRequired =
        [
            "Neo-native 1:1 复刻策略",
            "组件职责",
            "安全不变量",
            "运维流程",
            "Bridgehub",
            "Chain Type Manager",
            "Shared Bridge",
            "Gateway",
            "NeoFS",
            "RISC-V",
            "ContractZkVerifier",
            "envelope-only",
            "直接复刻边界",
        ];
        foreach (var required in chineseRequired)
        {
            StringAssert.Contains(chinese, required);
        }
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
            .Where(name => name is not "ExternalBridgeStubVerifier" and not "GovernanceFraudVerifier")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.AreEqual(26, neoHubContracts.Length,
            "contracts/NeoHub.* must contain 24 production contracts, one advisory structural verifier, and the test-only ExternalBridgeStubVerifier.");
        Assert.AreEqual(24, productionSteps.Length,
            "The default NeoHub deploy plan must emit only the 24 state-changing production contracts.");
        CollectionAssert.Contains(neoHubContracts, "ExternalBridgeStubVerifier");
        CollectionAssert.DoesNotContain(productionSteps, "ExternalBridgeStubVerifier",
            "ExternalBridgeStubVerifier is a dev/test helper and must not ship in the production NeoHub deploy bundle.");
        CollectionAssert.DoesNotContain(productionSteps, "GovernanceFraudVerifier",
            "GovernanceFraudVerifier is structural audit evidence and must not ship in the production NeoHub deploy bundle.");
        CollectionAssert.AreEqual(expectedProductionContracts, productionSteps,
            "The production deploy bundle must include every production NeoHub contract and exclude advisory/test-only projects.");
    }

    [TestMethod]
    public void CurrentDocumentation_UsesCurrentNeoHubCounts()
    {
        var root = FindRepositoryRoot();
        var neoHubCount = Directory.EnumerateDirectories(
            Path.Combine(root, "contracts"), "NeoHub.*").Count();
        var productionCount = ScaffoldPlan.Default().Steps.Count;
        string[] currentDocs =
        [
            "AGENTS.md",
            "README.md",
            "IMPLEMENTATION_STATUS.md",
            "TECH_STACK.md",
            "WHITEPAPER.md",
            Path.Combine("contracts", "README.md"),
            Path.Combine("docs", "README.md"),
            Path.Combine("docs", "architecture-glossary.md"),
            Path.Combine("docs", "architecture-l1-vs-l2.md"),
            Path.Combine("docs", "architecture-l2-lifecycle.md"),
            Path.Combine("docs", "getting-started.md"),
            Path.Combine("docs", "launching-an-l2.md"),
            Path.Combine("docs", "neohub-architecture-and-workflows.md"),
            Path.Combine("docs", "tech-stack-coverage.md"),
            Path.Combine("docs", "zksync-comparison.md"),
            Path.Combine("docs", "zh", "README.md"),
            Path.Combine("docs", "zh", "WHITEPAPER.md"),
            Path.Combine("docs", "zh", "architecture-glossary.md"),
            Path.Combine("docs", "zh", "architecture-l1-vs-l2.md"),
            Path.Combine("docs", "zh", "architecture-l2-lifecycle.md"),
            Path.Combine("docs", "zh", "contracts", "README.md"),
            Path.Combine("docs", "zh", "launching-an-l2.md"),
            Path.Combine("docs", "zh", "neohub-architecture-and-workflows.md"),
            Path.Combine("docs", "zh", "tech-stack-coverage.md"),
            Path.Combine("docs", "zh", "zksync-comparison.md")
        ];

        string[] obsoleteClaims =
        [
            "23 production",
            "23-step",
            "25 NeoHub",
            "all 25",
            "23 个生产",
            "25 个 NeoHub",
            "25 个项目",
            "23 步"
        ];

        foreach (var relativePath in currentDocs)
        {
            var text = File.ReadAllText(Path.Combine(root, relativePath));
            foreach (var obsoleteClaim in obsoleteClaims)
            {
                Assert.IsFalse(text.Contains(obsoleteClaim, StringComparison.OrdinalIgnoreCase),
                    $"{relativePath} must not carry obsolete inventory text '{obsoleteClaim}'.");
            }
        }

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        StringAssert.Contains(readme, $"{neoHubCount} NeoHub L1 contract projects");
        StringAssert.Contains(readme, $"{productionCount} production");

        var contractsReadme = File.ReadAllText(Path.Combine(root, "contracts", "README.md"));
        StringAssert.Contains(contractsReadme, $"There are {neoHubCount} `NeoHub.*` projects");
        StringAssert.Contains(contractsReadme, $"{productionCount} production contracts");
    }

    [TestMethod]
    public void CurrentDocumentation_UsesSourceDrivenTestAndSdkInventories()
    {
        var root = FindRepositoryRoot();
        var testProjectCount = Directory
            .EnumerateFiles(Path.Combine(root, "tests"), "*.csproj", SearchOption.AllDirectories)
            .Count();
        var foundryTestCount = Directory
            .EnumerateFiles(Path.Combine(root, "external", "foreign-contracts", "eth", "test"),
                "*.sol", SearchOption.AllDirectories)
            .Sum(path => System.Text.RegularExpressions.Regex.Matches(
                File.ReadAllText(path), @"\bfunction\s+test[A-Za-z0-9_]*\s*\(").Count);

        var techStack = File.ReadAllText(Path.Combine(root, "TECH_STACK.md"));
        StringAssert.Contains(techStack, $"| Test projects | {testProjectCount} |");

        string[] foundryDocs =
        [
            "README.md",
            "IMPLEMENTATION_STATUS.md",
            Path.Combine("docs", "tech-stack-coverage.md"),
            Path.Combine("docs", "testing-approach.md")
        ];
        foreach (var relativePath in foundryDocs)
        {
            StringAssert.Contains(
                File.ReadAllText(Path.Combine(root, relativePath)),
                $"{foundryTestCount} Foundry tests");
        }

        string[] sdkPaths =
        [
            Path.Combine("src", "Neo.L2.Sdk"),
            Path.Combine("sdk", "typescript"),
            Path.Combine("sdk", "rust"),
            Path.Combine("sdk", "python")
        ];
        Assert.IsTrue(sdkPaths.All(path => Directory.Exists(Path.Combine(root, path))));
        Assert.IsFalse(Directory.Exists(Path.Combine(root, "sdk", "go")),
            "The documented Go SDK gap must remain source-driven until a Go implementation exists.");

        var sdkReadme = File.ReadAllText(Path.Combine(root, "sdk", "README.md"));
        StringAssert.Contains(sdkReadme, "Four language bindings");
        StringAssert.Contains(sdkReadme, "## Why four typed languages");
        Assert.IsFalse(sdkReadme.Contains("Why three languages", StringComparison.OrdinalIgnoreCase));

        var comparison = File.ReadAllText(Path.Combine(root, "docs", "zksync-comparison.md"));
        StringAssert.Contains(comparison, "### Gap 2 — No Go SDK");
        StringAssert.Contains(comparison, "SDK source implementations are present");
        Assert.IsFalse(comparison.Contains("No Python / Go SDK", StringComparison.OrdinalIgnoreCase));

        var gitmodules = File.ReadAllText(Path.Combine(root, ".gitmodules"));
        var submoduleCount = System.Text.RegularExpressions.Regex.Matches(
            gitmodules, @"(?m)^\[submodule ").Count;
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        StringAssert.Contains(readme, $"| Submodules        | **{submoduleCount}**");
        StringAssert.Contains(readme, "external/neo-vm-rs");

        var l2ProjectCount = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "Neo.L2.*.csproj", SearchOption.AllDirectories)
            .Count();
        var rpcAdapterCount = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "Neo.L2.*.Rpc.csproj", SearchOption.AllDirectories)
            .Count();
        var coreLibraryCount = l2ProjectCount - rpcAdapterCount - 1;
        StringAssert.Contains(techStack, $"| Core off-chain .NET libraries | {coreLibraryCount} |");
        StringAssert.Contains(techStack, $"| RPC adapter libraries | {rpcAdapterCount} |");
        StringAssert.Contains(readme, $"| Core off-chain libraries | **{coreLibraryCount}**");
        StringAssert.Contains(readme, $"| RPC adapter libraries | **{rpcAdapterCount}**");

        var englishDocCount = Directory
            .EnumerateFiles(Path.Combine(root, "docs"), "*.md", SearchOption.TopDirectoryOnly)
            .Count();
        var chineseDocCount = Directory
            .EnumerateFiles(Path.Combine(root, "docs", "zh"), "*.md", SearchOption.TopDirectoryOnly)
            .Count();
        StringAssert.Contains(techStack,
            $"| Top-level documentation pages | {englishDocCount} EN + {chineseDocCount} zh |");

        string[] staleInventoryClaims =
        [
            "393-line",
            "393 lines",
            "~638-line",
            "32 base tests + 55",
            "55 live-RPC integration tests",
            "Submodules        | **4**"
        ];
        foreach (var claim in staleInventoryClaims)
        {
            Assert.IsFalse(readme.Contains(claim, StringComparison.OrdinalIgnoreCase),
                $"README.md must not carry stale inventory text '{claim}'.");
        }

        var changelog = File.ReadAllText(Path.Combine(root, "CHANGELOG.md"));
        StringAssert.Contains(changelog, "targeted regression gates");
        Assert.IsFalse(changelog.Contains("cannot drift silently", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void CurrentDocumentation_SeparatesImplementationFromProductionReadiness()
    {
        var root = FindRepositoryRoot();
        var status = File.ReadAllText(Path.Combine(root, "IMPLEMENTATION_STATUS.md"));
        StringAssert.Contains(status, "| Production-ready |");
        StringAssert.Contains(status, "No phase is production-ready");

        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        StringAssert.Contains(readme, "| Production-ready |");
        StringAssert.Contains(readme, "No row is a production-readiness claim");

        var chinese = File.ReadAllText(Path.Combine(root, "docs", "zh", "IMPLEMENTATION_STATUS.md"));
        StringAssert.Contains(chinese, "| 生产完备 |");
        StringAssert.Contains(chinese, "任何阶段都不能标记为生产完备");
    }

    [TestMethod]
    public void SampleContracts_UseRepositoryMaintainerAttribution()
    {
        var root = FindRepositoryRoot();
        string[] samples =
        [
            Path.Combine("samples", "contracts", "Sample.CrossChainGreeter", "CrossChainGreeter.cs"),
            Path.Combine("samples", "contracts", "Sample.WithdrawalDemo", "WithdrawalDemo.cs")
        ];

        foreach (var relativePath in samples)
        {
            var source = File.ReadAllText(Path.Combine(root, relativePath));
            StringAssert.Contains(source, "ContractAuthor(\"R3E Network — Sample\", \"dev@r3e.network\")");
            Assert.IsFalse(source.Contains("Neo Project", StringComparison.Ordinal));
            Assert.IsFalse(source.Contains("dev@neo.org", StringComparison.Ordinal));
        }
    }

    [TestMethod]
    public void CurrentSecurityPolicy_DoesNotInventARelease()
    {
        var root = FindRepositoryRoot();
        var policy = File.ReadAllText(Path.Combine(root, "SECURITY.md"));

        StringAssert.Contains(policy, "No production release tag has been published yet.");
        StringAssert.Contains(policy, "private vulnerability reporting form");
        StringAssert.Contains(policy, "R3E Network security mailbox");
        StringAssert.Contains(policy, "git tag --verify <published-tag>");
        Assert.IsFalse(policy.Contains("git tag --verify v0.1.0", StringComparison.Ordinal));
        Assert.IsFalse(policy.Contains("all release tags are signed", StringComparison.OrdinalIgnoreCase));

        var model = File.ReadAllText(Path.Combine(root, "docs", "security-model.md"));
        StringAssert.Contains(model, "r3e-network/neo-n4");
        StringAssert.Contains(model, "unmodified upstream Neo behavior");
        StringAssert.Contains(model, "Neo Project");
        Assert.IsFalse(model.Contains("Neo project security mailbox", StringComparison.OrdinalIgnoreCase));
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
            .Where(pair => !pair.Chinese.Any(candidate => File.Exists(Path.Combine(root, candidate))))
            .Select(pair => $"{pair.English} -> {string.Join(" or ", pair.Chinese)}")
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
            .Select(relative => new { English = relative, Chinese = ExpectedChineseFigures(relative) })
            .Where(pair => !pair.Chinese.Any(candidate => File.Exists(Path.Combine(root, candidate))))
            .Select(pair => $"{pair.English} -> {string.Join(" or ", pair.Chinese)}")
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
            .SelectMany(path => ExpectedChineseMarkdown(Relative(root, path)))
            .Where(relative => File.Exists(Path.Combine(root, relative)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(relative =>
            {
                var zhPath = Path.Combine(root, relative);
                return !File.ReadAllText(zhPath).Any(IsChineseCharacter);
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

    [TestMethod]
    public void Repository_KeepsOnlyCuratedAuditEvidenceInDocs()
    {
        var root = FindRepositoryRoot();
        var auditRoot = Path.Combine(root, "docs", "audit");
        var rawAuditOutputs = Directory.Exists(auditRoot)
            ? Directory.EnumerateFiles(auditRoot, "*.log", SearchOption.AllDirectories)
                .Where(path => !IsSkippedPath(root, path))
                .Select(path => Relative(root, path))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        Assert.AreEqual(0, rawAuditOutputs.Length,
            "docs/audit must keep curated Markdown/JSON evidence only; raw command output stays local:\n"
            + string.Join('\n', rawAuditOutputs));

        string[] fullStackReports =
        [
            Path.Combine(root, "docs", "audit", "full-stack-validation-2026-05-20", "README.md"),
            Path.Combine(root, "docs", "zh", "audit", "full-stack-validation-2026-05-20", "README.md")
        ];
        foreach (var fullStackReport in fullStackReports.Where(File.Exists))
        {
            Assert.IsFalse(File.ReadAllText(fullStackReport).Contains(".log", StringComparison.OrdinalIgnoreCase),
                "The full-stack validation report must reference curated evidence, not transient command-output filenames.");
        }
    }

    [TestMethod]
    public void Repository_DoesNotKeepRetiredNativeZkVerifierCurrentEvidence()
    {
        var root = FindRepositoryRoot();
        var auditRoot = Path.Combine(root, "docs", "audit");
        var staleEvidence = Directory.Exists(auditRoot)
            ? Directory.EnumerateFiles(auditRoot, "*.json", SearchOption.AllDirectories)
                .Where(path => !IsSkippedPath(root, path))
                .Where(path =>
                {
                    var relative = Relative(root, path);
                    return relative.Contains("testnet-deployment-2026-05-20", StringComparison.OrdinalIgnoreCase)
                        || relative.Contains("full-stack-validation-2026-05-20", StringComparison.OrdinalIgnoreCase);
                })
                .Where(path => File.ReadAllText(path).Contains("NativeZkVerifier", StringComparison.Ordinal))
                .Select(path => Relative(root, path))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : Array.Empty<string>();

        Assert.AreEqual(0, staleEvidence.Length,
            "Current deployment evidence must use the contract-first ContractZkVerifier route:\n"
            + string.Join('\n', staleEvidence));
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

        // Documentation-consistency applies to the COMMITTED repository only. Skip anything not
        // git-tracked, so gitignored generated output (e.g. outputs/) and untracked work-in-progress
        // — which a clean checkout / CI never sees — don't produce false counterpart failures. Falls
        // back to the path-based rules below when git is unavailable.
        var tracked = GetTrackedPaths(root);
        if (tracked is not null && !tracked.Contains(relative))
            return true;

        var parts = relative.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(part => part is ".git" or "bin" or "obj" or "target" or "book" or "build" or "node_modules" or "outputs"))
            return true;

        return relative.StartsWith("artifacts/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo-devpack-dotnet/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo-riscv-vm/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/neo-zkvm/", StringComparison.OrdinalIgnoreCase)
            || relative.StartsWith("external/foreign-contracts/eth/lib/", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string>? _trackedPaths;
    private static bool _trackedComputed;

    /// <summary>
    /// The set of git-tracked paths (relative, forward-slash), or null if git is unavailable. Cached
    /// for the process. Used so the doc-counterpart checks only consider committed files.
    /// </summary>
    private static HashSet<string>? GetTrackedPaths(string root)
    {
        if (_trackedComputed) return _trackedPaths;
        _trackedComputed = true;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git", $"-C \"{root}\" ls-files")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return _trackedPaths = null;
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            if (process.ExitCode != 0) return _trackedPaths = null;
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                set.Add(line.Trim().Replace('\\', '/'));
            return _trackedPaths = set.Count > 0 ? set : null;
        }
        catch
        {
            return _trackedPaths = null;
        }
    }

    private static bool IsChinesePath(string root, string path)
    {
        var relative = Relative(root, path);
        var fileName = Path.GetFileName(relative);
        return relative.StartsWith("docs/zh/", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(".zh.", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ExpectedChineseMarkdown(string relative)
    {
        var extension = Path.GetExtension(relative);
        var withoutExtension = relative[..^extension.Length];
        var sibling = $"{withoutExtension}.zh{extension}";
        var mirrored = relative.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
            ? "docs/zh/" + relative["docs/".Length..]
            : "docs/zh/" + relative;
        return sibling.Equals(mirrored, StringComparison.OrdinalIgnoreCase)
            ? [sibling]
            : [sibling, mirrored];
    }

    private static string[] ExpectedChineseFigures(string relative)
    {
        var extension = Path.GetExtension(relative);
        var withoutExtension = relative[..^extension.Length];
        var sibling = $"{withoutExtension}.zh{extension}";
        var mirrored = relative.StartsWith("docs/figures/", StringComparison.OrdinalIgnoreCase)
            ? "docs/zh/figures/" + relative["docs/figures/".Length..]
            : "docs/zh/" + relative;
        return sibling.Equals(mirrored, StringComparison.OrdinalIgnoreCase)
            ? [sibling]
            : [sibling, mirrored];
    }

    private static string Relative(string root, string path)
        => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private static bool IsChineseCharacter(char c) => c is >= '\u4e00' and <= '\u9fff';
}
