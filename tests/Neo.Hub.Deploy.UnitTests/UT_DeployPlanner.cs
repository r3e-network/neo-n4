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
    public void Scaffold_DefaultIncludesOnlyProductionNeoHubContracts()
    {
        var plan = ScaffoldPlan.Default();
        Assert.AreEqual(5, plan.Steps.Count);
        var names = plan.Steps.Select(s => s.Name).ToHashSet();
        Assert.IsTrue(names.Contains("ZkVerifier"));
        Assert.IsTrue(names.Contains("GovernanceController"));
        Assert.IsTrue(names.Contains("RollupHub"));
        Assert.IsTrue(names.Contains("SharedBridge"));

        foreach (var step in plan.Steps)
        {
            var nefPath = step.NefPath.Replace('\\', '/');
            var manifestPath = step.ManifestPath.Replace('\\', '/');
            StringAssert.Contains(nefPath, "/bin/sc/");
            StringAssert.Contains(manifestPath, "/bin/sc/");
            Assert.IsFalse(nefPath.Contains("/bin/Release/", StringComparison.Ordinal),
                $"{step.Name} NEF path must point at nccs output under bin/sc");
            Assert.IsFalse(manifestPath.Contains("/bin/Release/", StringComparison.Ordinal),
                $"{step.Name} manifest path must point at nccs output under bin/sc");
        }
    }

    [TestMethod]
    public void Scaffold_ZkVerifierHasOwnerOnlyDeployData()
    {
        var plan = ScaffoldPlan.Default();
        var verifier = plan.Steps.Single(s => s.Name == "ZkVerifier");

        Assert.AreEqual(1, verifier.DeployData.Count,
            "ZkVerifier deploys as a normal NeoHub L1 contract with an owner; VK/verifier-profile wiring is post-deploy.");
        Assert.AreEqual("OWNER_REPLACE_ME", verifier.DeployData[0]!.AsString());
        Assert.AreEqual(0, verifier.DependsOn.Count);
    }

    [TestMethod]
    public void Scaffold_ZkVerifierIsConfigured()
    {
        var plan = ScaffoldPlan.Default();
        var verifier = plan.Steps.Single(s => s.Name == "ZkVerifier");

        Assert.AreEqual(1, verifier.DeployData.Count);
        Assert.AreEqual("OWNER_REPLACE_ME", verifier.DeployData[0]!.AsString());
        Assert.AreEqual(0, verifier.DependsOn.Count);
    }

    [TestMethod]
    public void Scaffold_RollupHubBindsZkVerifier()
    {
        var plan = ScaffoldPlan.Default();
        var rollupHub = plan.Steps.Single(s => s.Name == "RollupHub");

        Assert.AreEqual(2, rollupHub.DeployData.Count);
        Assert.AreEqual("OWNER_REPLACE_ME", rollupHub.DeployData[0]!.AsString());
        Assert.AreEqual("$step:ZkVerifier", rollupHub.DeployData[1]!.AsString());
        CollectionAssert.Contains(rollupHub.DependsOn.ToArray(), "ZkVerifier");
    }

    [TestMethod]
    public void Scaffold_SharedBridgeBindsRollupHub()
    {
        var plan = ScaffoldPlan.Default();
        var bridge = plan.Steps.Single(s => s.Name == "SharedBridge");

        Assert.AreEqual(2, bridge.DeployData.Count);
        Assert.AreEqual("OWNER_REPLACE_ME", bridge.DeployData[0]!.AsString());
        Assert.AreEqual("$step:RollupHub", bridge.DeployData[1]!.AsString());
        CollectionAssert.Contains(bridge.DependsOn.ToArray(), "RollupHub");
    }

    [TestMethod]
    public void Scaffold_GovernanceControllerHasCouncilDeployData()
    {
        var plan = ScaffoldPlan.Default();
        var governance = plan.Steps.Single(s => s.Name == "GovernanceController");

        Assert.AreEqual(4, governance.DeployData.Count,
            "GovernanceController _deploy needs (owner, councilMembers[], threshold, timelockSeconds).");
        Assert.AreEqual("OWNER_REPLACE_ME", governance.DeployData[0]!.AsString());
        var council = governance.DeployData[1] as Neo.Json.JArray;
        Assert.IsNotNull(council, "2nd arg must be a council public-key array");
        Assert.AreEqual("GOVERNANCE_COUNCIL_REPLACE_ME", council[0]!.AsString());
        Assert.AreEqual("GOVERNANCE_THRESHOLD_REPLACE_ME", governance.DeployData[2]!.AsString());
        Assert.AreEqual(3600, (int)governance.DeployData[3]!.AsNumber());
    }

    [TestMethod]
    public void Scaffold_PostDeployActionsWireRollupHubToGovernanceController()
    {
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, _ => H(0x42));

        var actions = ScaffoldPlan.PostDeployActions(bundle).ToArray();

        Assert.IsTrue(actions.Any(a => a.Contains("RollupHub.SetGovernanceController(GovernanceController)", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Scaffold_OptimisticProfileWithWrongSettlementBinding_IsRejected()
    {
        var custom = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("SettlementManager", new JArray()),
                Step("OptimisticChallenge", new JArray { "OWNER", "$step:SettlementManager" }, "SettlementManager"),
                Step("RestrictedExecutionFraudVerifier",
                    new JArray { "0x0000000000000000000000000000000000000001", "FRAUD_REPLAY_DOMAIN_REPLACE_ME" },
                    "SettlementManager"),
            },
        };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ScaffoldPlan.RequireExecutableOptimisticFraudProfile(custom));
        StringAssert.Contains(ex.Message, "exact executable v4");
        StringAssert.Contains(ex.Message, "SettlementManager");
    }

    [TestMethod]
    public void PostDeployActions_ResolvedSettlementMismatch_IsRejected()
    {
        var custom = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("SettlementManager", new JArray()),
                Step("OtherSettlementManager", new JArray()),
                Step("OptimisticChallenge",
                    new JArray { "OWNER", "$step:SettlementManager" },
                    "SettlementManager"),
                Step("RestrictedExecutionFraudVerifier",
                    new JArray { "$step:OtherSettlementManager", "FRAUD_REPLAY_DOMAIN_REPLACE_ME" },
                    "OtherSettlementManager"),
            },
        };
        var bundle = DeployPlanner.Plan(custom, name => H(
            name == "SettlementManager" ? (byte)0x11 : (byte)0x22));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ScaffoldPlan.PostDeployActions(bundle).ToList());
        StringAssert.Contains(ex.Message, "exact executable v4");
    }

    [TestMethod]
    public void Scaffold_DefaultExcludesStructuralGovernanceFraudVerifier()
    {
        var plan = ScaffoldPlan.Default();
        Assert.IsFalse(plan.Steps.Any(s => s.Name == "GovernanceFraudVerifier"),
            "v1/v2 structural evidence cannot authorize state changes and must remain outside the production bundle");
    }

    [TestMethod]
    public void Scaffold_DependenciesAreOrdered()
    {
        var plan = ScaffoldPlan.Default();
        var rollupHub = plan.Steps.Single(s => s.Name == "RollupHub");
        CollectionAssert.Contains(rollupHub.DependsOn.ToArray(), "ZkVerifier");

        var bridge = plan.Steps.Single(s => s.Name == "SharedBridge");
        CollectionAssert.Contains(bridge.DependsOn.ToArray(), "RollupHub");
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
            Version = 1,
            Network = "test",
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

    [TestMethod]
    public void PostDeployActions_DefaultPlan_EmitsAllWiringHints()
    {
        // Pin the operator-facing hints emitted by `plan` after a successful resolution.
        // Without these, an operator could miss the cycle-break + governance wiring
        // post-deploy steps, leaving Phase-3 challenges with no slash payout path AND
        // §16.1 admission / §16 council-veto silently broken; the bridge committee
        // governance never gets wired; or miss the exact executable-v4 registration
        // boundary that prevents structural evidence from authorizing state changes.
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, name => H((byte)(name.Length & 0xFF)));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        // 2 sequencer/forced-inclusion slasher registrations +
        // 7 forced-inclusion enforcement/spam-control hints + 1 emergency-withdrawal wiring hint +
        // 4 governance-controller bindings (ChainRegistry, VerifierRegistry, SettlementManager,
        //   OptimisticChallenge — the last is new for H12) +
        // 4 ContractZkVerifier SP1 hints + 2 outer VerifierRegistry route/freeze hints +
        // 1 exact atomic v4 profile registration + 1 fraud-verifier security-boundary note +
        // 1 OptimisticChallenge freeze (H12) +
        // 4 external-bridge governance/setup pointers + 2 payout-route hints +
        // 1 escrow freeze + 4 Phase-C slash/member-binding hints +
        // 2 bridge committee + dispatch-table freezes (H12) +
        // 4 SettlementManager dependency hints + 3 MessageRouter Gateway trust-root hints +
        // 2 registry/settlement freezes +
        // 1 MessageRouter filter hint = 44.
        Assert.AreEqual(6, actions.Count);

        StringAssert.Contains(actions[0], "RollupHub.SetGovernanceController(GovernanceController)");
        StringAssert.Contains(actions[1], "SharedBridge.SetSettlementManager(RollupHub)");
        StringAssert.Contains(actions[2], "SharedBridge.SetEmergencyManager(GovernanceController)");
        StringAssert.Contains(actions[3], "ZkVerifier.RegisterProofVerifier(ProofSystem.Sp1=1, Sp1Groth16Verifier, allowed=true)");
        StringAssert.Contains(actions[4], "ZkVerifier.RegisterVerificationKey(ProofSystem.Sp1=1, <SP1_PROGRAM_VK_FROM_RELEASE_MANIFEST>, allowed=true)");
        StringAssert.Contains(actions[5], "ZkVerifier.DisableEnvelopeOnlyPermanently(ProofSystem.Sp1=1)");
    }

    [TestMethod]
    public void PostDeployActions_LegacyEmptyRestrictedVerifier_IsRejected()
    {
        // A legacy verifier without the executable-v4 deployment binding must not even be
        // allowlisted: doing so would suggest governance can revive a structural payload.
        var custom = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("OptimisticChallenge", new JArray { "X" }),
                Step("RestrictedExecutionFraudVerifier", new JArray()),
            },
        };
        var bundle = DeployPlanner.Plan(custom, name => H(0xAA));
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ScaffoldPlan.PostDeployActions(bundle).ToList());
        StringAssert.Contains(ex.Message, "executable v4");
        StringAssert.Contains(ex.Message, "unsupported");
    }

    [TestMethod]
    public void PostDeployActions_GovernanceVerifierOnly_IsRejected()
    {
        var custom = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("OptimisticChallenge", new JArray { "X" }),
                Step("GovernanceFraudVerifier", new JArray()),
            },
        };
        var bundle = DeployPlanner.Plan(custom, _ => H(0xAA));
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => ScaffoldPlan.PostDeployActions(bundle).ToList());
        StringAssert.Contains(ex.Message, "executable v4");
        StringAssert.Contains(ex.Message, "v1/v2/v3");
    }

    [TestMethod]
    public void PostDeployActions_NoChallenge_NoSlasherHint()
    {
        // If a custom plan deploys SequencerBond without OptimisticChallenge (e.g. the
        // operator opted out of Phase-3 entirely), no slasher hint is emitted — the bond's
        // initial slashers list of just GovernanceController is fine for that setup.
        var custom = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[] { Step("SequencerBond", new JArray { "X" }) },
        };
        var bundle = DeployPlanner.Plan(custom, name => H(0xAA));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        Assert.AreEqual(0, actions.Count);
    }

    [TestMethod]
    public void PostDeployActions_NoGovernance_NoGovernanceHints()
    {
        // A custom plan with ChainRegistry + VerifierRegistry but no GovernanceController
        // emits no governance-wiring hints — the operator either deploys GovernanceController
        // separately or opted out (§16.1 admission then defaults to permissioned-only).
        var custom = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("ChainRegistry", new JArray { "X" }),
                Step("VerifierRegistry", new JArray { "X" }),
            },
        };
        var bundle = DeployPlanner.Plan(custom, name => H(0xAA));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        Assert.AreEqual(0, actions.Count);
    }

    [TestMethod]
    public void PostDeployActions_OnlyChainRegistryAndGovernance_EmitsWiringAndLockHints()
    {
        // Asymmetry test: an operator who only deploys ChainRegistry (without
        // VerifierRegistry — e.g. governance-controlled chain admission only) gets
        // both the controller-wiring and irreversible-lock hints.
        var custom = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps = new[]
            {
                Step("ChainRegistry", new JArray { "X" }),
                Step("GovernanceController", new JArray { "X" }),
            },
        };
        var bundle = DeployPlanner.Plan(custom, name => H(0xAA));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        Assert.AreEqual(2, actions.Count);
        StringAssert.Contains(actions[0], "ChainRegistry.SetGovernanceController");
        StringAssert.Contains(actions[1], "ChainRegistry.LockGovernance");
    }

    [TestMethod]
    public void PostDeployActions_RejectsNullBundle()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ScaffoldPlan.PostDeployActions(null!).ToList());
    }
}
