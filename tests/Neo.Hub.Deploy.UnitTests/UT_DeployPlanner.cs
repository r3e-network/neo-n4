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
        Assert.AreEqual(10, plan.Steps.Count);
        var names = plan.Steps.Select(s => s.Name).ToHashSet();
        Assert.IsTrue(names.Contains("ChainRegistry"));
        Assert.IsTrue(names.Contains("SharedBridge"));
        Assert.IsTrue(names.Contains("SettlementManager"));
        Assert.IsTrue(names.Contains("ForcedInclusion"));
    }

    [TestMethod]
    public void Scaffold_PlanIsValid()
    {
        var plan = ScaffoldPlan.Default();
        // Planner should accept the canonical layout.
        var bundle = DeployPlanner.Plan(plan, _ => H(0x42));
        Assert.AreEqual(plan.Steps.Count, bundle.Invocations.Count);
    }
}
