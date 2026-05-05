using Neo.Json;

namespace Neo.Hub.Deploy.UnitTests;

[TestClass]
public class UT_DeployPlanner
{
    private static UInt160 H(byte b)
    {
        var bytes = new byte[20];
        for (var i = 0; i < 20; i++) bytes[i] = b;
        return new UInt160(bytes);
    }

    private static DeployStep Step(string name, JArray data, params string[] deps) => new()
    {
        Name = name,
        NefPath = $"{name}.nef",
        ManifestPath = $"{name}.manifest.json",
        DeployData = data,
        DependsOn = deps,
    };

    [TestMethod]
    public void Plan_RespectsTopologicalOrder()
    {
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("C", new JArray(), "A", "B"),
                Step("A", new JArray()),
                Step("B", new JArray(), "A"),
            },
        };

        var bundle = DeployPlanner.Plan(plan, n => H(0x01));

        Assert.AreEqual(3, bundle.Invocations.Count);
        Assert.AreEqual("A", bundle.Invocations[0].Name);
        Assert.AreEqual("B", bundle.Invocations[1].Name);
        Assert.AreEqual("C", bundle.Invocations[2].Name);
    }

    [TestMethod]
    public void Plan_RejectsDuplicateStepNames_WithClearMessage()
    {
        // Regression: previously ToDictionary surfaced "An item with the same key has
        // already been added. Key: <name>" — generic. Now: clear "duplicate deploy step
        // name '<name>'" so the operator can find their typo.
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("A", new JArray()),
                Step("A", new JArray()),
            },
        };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DeployPlanner.Plan(plan, _ => UInt160.Zero));
        StringAssert.Contains(ex.Message, "duplicate deploy step name 'A'");
    }

    [TestMethod]
    public void Plan_RejectsEmptyStepName()
    {
        // Without this check, an empty-name step would slip into byName as the empty key.
        // A subsequent step that depends on "" would resolve, masking a typo in the JSON.
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[] { Step("", new JArray()) },
        };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DeployPlanner.Plan(plan, _ => UInt160.Zero));
        StringAssert.Contains(ex.Message, "must not be empty");
    }

    [TestMethod]
    public void Plan_DetectsSelfCycle()
    {
        // Step A depends on itself — degenerate cycle of length 1. Existing 2-step cycle
        // test covered A→B→A; this pins the trivial degenerate case so a future refactor
        // that special-cases the recursion-path check can't regress.
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[] { Step("A", new JArray(), "A") },
        };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DeployPlanner.Plan(plan, _ => UInt160.Zero));
        StringAssert.Contains(ex.Message, "cycle");
    }

    [TestMethod]
    public void Plan_DetectsCycles()
    {
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("A", new JArray(), "B"),
                Step("B", new JArray(), "A"),
            },
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => DeployPlanner.Plan(plan, _ => H(0x01)));
    }

    [TestMethod]
    public void Plan_DetectsUnknownDependency()
    {
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[] { Step("A", new JArray(), "Ghost") },
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => DeployPlanner.Plan(plan, _ => H(0x01)));
    }

    [TestMethod]
    public void Plan_ResolvesPlaceholders()
    {
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("A", new JArray()),
                Step("B", new JArray { "$step:A" }, "A"),
            },
        };

        var aHash = UInt160.Parse("0x" + new string('a', 40));
        var bundle = DeployPlanner.Plan(plan, name => name == "A" ? aHash : UInt160.Zero);

        Assert.AreEqual(2, bundle.Invocations.Count);
        var bData = bundle.Invocations[1].ResolvedDeployData;
        Assert.AreEqual(aHash.ToString(), bData[0]!.AsString());
    }

    [TestMethod]
    public void Plan_LeavesNonPlaceholderStringsUnchanged()
    {
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[] { Step("A", new JArray { "OWNER_REPLACE_ME", 42 }) },
        };

        var bundle = DeployPlanner.Plan(plan, _ => H(0x01));
        var data = bundle.Invocations[0].ResolvedDeployData;
        Assert.AreEqual("OWNER_REPLACE_ME", data[0]!.AsString());
        Assert.AreEqual(42, (int)data[1]!.AsNumber());
    }

    [TestMethod]
    public void FromJson_RejectsUnsupportedVersion()
    {
        // A future version=2 plan with a different schema would silently parse with the
        // v1 reader and produce garbage. Now we reject explicitly so a future contract
        // author has to migrate the reader together with the bumped version.
        var json = """{"version":99,"network":"testnet","steps":[]}""";
        Assert.ThrowsExactly<InvalidDataException>(() => DeployPlan.FromJson(json));
    }

    [TestMethod]
    public void DeployPlan_RoundTripsJson()
    {
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "neo-n3-testnet",
            Steps = new[] { Step("X", new JArray { 1, "two" }, "Y"), Step("Y", new JArray()) },
        };

        var json = plan.ToJson();
        var roundtrip = DeployPlan.FromJson(json);

        Assert.AreEqual(plan.Version, roundtrip.Version);
        Assert.AreEqual(plan.Network, roundtrip.Network);
        Assert.AreEqual(plan.Steps.Count, roundtrip.Steps.Count);
        Assert.AreEqual("X", roundtrip.Steps[0].Name);
    }

    [TestMethod]
    public void Scaffold_DefaultIncludesAllNeoHubContracts()
    {
        var plan = ScaffoldPlan.Default();
        Assert.AreEqual(13, plan.Steps.Count);
        var names = plan.Steps.Select(s => s.Name).ToHashSet();
        // Phase 0/1/2 contracts
        Assert.IsTrue(names.Contains("ChainRegistry"));
        Assert.IsTrue(names.Contains("SharedBridge"));
        Assert.IsTrue(names.Contains("SettlementManager"));
        Assert.IsTrue(names.Contains("VerifierRegistry"));
        Assert.IsTrue(names.Contains("TokenRegistry"));
        Assert.IsTrue(names.Contains("DARegistry"));
        Assert.IsTrue(names.Contains("MessageRouter"));
        Assert.IsTrue(names.Contains("EmergencyManager"));
        Assert.IsTrue(names.Contains("GovernanceController"));
        Assert.IsTrue(names.Contains("ForcedInclusion"));
        // Phase 3 contracts (newly added to scaffold)
        Assert.IsTrue(names.Contains("SequencerBond"));
        Assert.IsTrue(names.Contains("SequencerRegistry"));
        Assert.IsTrue(names.Contains("OptimisticChallenge"));
    }

    [TestMethod]
    public void Scaffold_SequencerBondSlashersIsArrayWithGovernanceController()
    {
        // Pin the unusual 3rd deploy arg shape — SequencerBond takes
        // (owner, bondAsset, slashers[]) where slashers is a JArray, not a scalar.
        // A regression that flattens this into "$step:GovernanceController" alone
        // would break the contract's _deploy at runtime with a confusing cast error.
        var plan = ScaffoldPlan.Default();
        var bond = plan.Steps.Single(s => s.Name == "SequencerBond");
        Assert.AreEqual(3, bond.DeployData.Count, "SequencerBond needs (owner, bondAsset, slashers[])");
        var slashers = bond.DeployData[2] as Neo.Json.JArray;
        Assert.IsNotNull(slashers, "3rd arg must be a JArray (slashers list)");
        Assert.AreEqual(1, slashers.Count, "Initial slashers list = [GovernanceController]");
    }

    [TestMethod]
    public void Scaffold_OptimisticChallengeDependsOnSettlementAndBond()
    {
        // Pin the dep edges so a refactor that breaks the topo ordering surfaces
        // here, not at L1 deploy time when arr[1] / arr[2] become invalid hashes.
        var plan = ScaffoldPlan.Default();
        var oc = plan.Steps.Single(s => s.Name == "OptimisticChallenge");
        CollectionAssert.Contains(oc.DependsOn.ToArray(), "SettlementManager");
        CollectionAssert.Contains(oc.DependsOn.ToArray(), "SequencerBond");
    }

    [TestMethod]
    public void Scaffold_NoDeployCycle_BondBeforeChallenge()
    {
        // Pin the cycle-break: SequencerBond does NOT depend on OptimisticChallenge,
        // so the topo sort can put bond first and challenge later. Without this
        // assertion, somebody re-adding OptimisticChallenge to BondDeployData would
        // re-introduce the cycle and the planner would fail with a confusing
        // "dependency cycle detected through step 'SequencerBond'" error.
        var plan = ScaffoldPlan.Default();
        var bond = plan.Steps.Single(s => s.Name == "SequencerBond");
        CollectionAssert.DoesNotContain(bond.DependsOn.ToArray(), "OptimisticChallenge");
    }

    [TestMethod]
    public void Scaffold_PlanIsValid()
    {
        var plan = ScaffoldPlan.Default();
        // Planner should accept the canonical layout.
        var bundle = DeployPlanner.Plan(plan, _ => H(0x42));
        Assert.AreEqual(plan.Steps.Count, bundle.Invocations.Count);
    }

    [TestMethod]
    public void Plan_BuggyResolverReturnsNull_SurfacesContractViolation()
    {
        // Regression for iter 201: a HashResolver returning null UInt160 would NRE on
        // .ToString() in ResolveToken. Now surfaces as InvalidOperationException naming
        // the step. Same iter-171/172 callee-contract pattern.
        var plan = new DeployPlan
        {
            Version = 1, Network = "test",
            Steps = new[]
            {
                Step("A", new JArray()),
                Step("B", new JArray { new JString("$step:A") }, "A"),
            },
        };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => DeployPlanner.Plan(plan, _ => null!));
        StringAssert.Contains(ex.Message, "HashResolver");
        StringAssert.Contains(ex.Message, "'A'");
    }
}
