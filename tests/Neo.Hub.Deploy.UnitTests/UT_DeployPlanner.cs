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
        // 15 core NeoHub contracts + 1 executable fraud verifier
        // (RestrictedExecution v4) + 4 external-bridge contracts (doc.md
        // §11.3 Phase B: MpcCommitteeVerifier, ExternalBridgeRegistry,
        // ExternalBridgeEscrow, L2PayoutAdapter, ExternalBridgeBond) + 1 Phase-C
        // (MpcCommitteeFraudVerifier) + 1 contract-deployed ZK verifier router
        // + 1 pinned SP1 Groth16 terminal verifier = 24.
        Assert.AreEqual(24, plan.Steps.Count);
        var names = plan.Steps.Select(s => s.Name).ToHashSet();
        Assert.IsFalse(names.Contains("GovernanceFraudVerifier"),
            "structural v1/v2 evidence must not ship in the production deployment bundle");
        Assert.IsTrue(names.Contains("RestrictedExecutionFraudVerifier"));
        Assert.IsTrue(names.Contains("ContractZkVerifier"));
        Assert.IsTrue(names.Contains("Sp1Groth16Verifier"));
        // Phase 0/1/2 contracts
        Assert.IsTrue(names.Contains("ChainRegistry"));
        Assert.IsTrue(names.Contains("SharedBridge"));
        Assert.IsTrue(names.Contains("SettlementManager"));
        Assert.IsTrue(names.Contains("VerifierRegistry"));
        Assert.IsTrue(names.Contains("TokenRegistry"));
        Assert.IsTrue(names.Contains("DARegistry"));
        Assert.IsTrue(names.Contains("DAValidator"));
        Assert.IsTrue(names.Contains("MessageRouter"));
        Assert.IsTrue(names.Contains("L1TxFilter"));
        Assert.IsTrue(names.Contains("EmergencyManager"));
        Assert.IsTrue(names.Contains("GovernanceController"));
        Assert.IsTrue(names.Contains("ForcedInclusion"));
        // Phase 3 contracts (newly added to scaffold)
        Assert.IsTrue(names.Contains("SequencerBond"));
        Assert.IsTrue(names.Contains("SequencerRegistry"));
        Assert.IsTrue(names.Contains("OptimisticChallenge"));
        // External-bridge stack (doc.md §11.3 — cross-foreign-chain bridge to
        // Eth/Tron/Solana). The four pieces deploy independently of the
        // Phase-3 settlement contracts; the verifier is a committee, not a
        // per-batch settlement gate.
        Assert.IsTrue(names.Contains("MpcCommitteeVerifier"));
        Assert.IsTrue(names.Contains("ExternalBridgeRegistry"));
        Assert.IsTrue(names.Contains("ExternalBridgeEscrow"));
        Assert.IsTrue(names.Contains("L2PayoutAdapter"));
        Assert.IsTrue(names.Contains("ExternalBridgeBond"));
        Assert.IsTrue(names.Contains("MpcCommitteeFraudVerifier"));

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
    public void Scaffold_ContractZkVerifierHasOwnerOnlyDeployData()
    {
        var plan = ScaffoldPlan.Default();
        var verifier = plan.Steps.Single(s => s.Name == "ContractZkVerifier");

        Assert.AreEqual(1, verifier.DeployData.Count,
            "ContractZkVerifier deploys as a normal NeoHub L1 contract with an owner; VK/verifier-profile wiring is post-deploy.");
        Assert.AreEqual("OWNER_REPLACE_ME", verifier.DeployData[0]!.AsString());
        Assert.AreEqual(0, verifier.DependsOn.Count,
            "ContractZkVerifier is a registry target; it must not create a deploy-time cycle with VerifierRegistry.");
    }

    [TestMethod]
    public void Scaffold_Sp1Groth16VerifierIsStateless()
    {
        var plan = ScaffoldPlan.Default();
        var verifier = plan.Steps.Single(s => s.Name == "Sp1Groth16Verifier");

        Assert.AreEqual(0, verifier.DeployData.Count,
            "the pinned SP1 verifier has no mutable verifying-key or owner state");
        Assert.AreEqual(0, verifier.DependsOn.Count,
            "the SP1 verifier only uses Neo native BN254 interops");
    }

    [TestMethod]
    public void Scaffold_ExternalBridgeEscrowBindsExplicitL2Domain()
    {
        var plan = ScaffoldPlan.Default();
        var escrow = plan.Steps.Single(s => s.Name == "ExternalBridgeEscrow");

        Assert.AreEqual(3, escrow.DeployData.Count,
            "ExternalBridgeEscrow _deploy requires (owner, registry, neoChainId)");
        Assert.AreEqual("OWNER_REPLACE_ME", escrow.DeployData[0]!.AsString());
        Assert.AreEqual("$step:ExternalBridgeRegistry", escrow.DeployData[1]!.AsString());
        Assert.AreEqual("L2_CHAIN_ID_REPLACE_ME", escrow.DeployData[2]!.AsString());
        CollectionAssert.Contains(escrow.DependsOn.ToArray(), "ExternalBridgeRegistry");
    }

    [TestMethod]
    public void Scaffold_L2PayoutAdapterIsConcreteAndDomainBound()
    {
        var plan = ScaffoldPlan.Default();
        var adapter = plan.Steps.Single(s => s.Name == "L2PayoutAdapter");
        Assert.AreEqual(4, adapter.DeployData.Count,
            "L2PayoutAdapter _deploy requires (owner, escrow, neoChainId, relayAccount)");
        Assert.AreEqual("OWNER_REPLACE_ME", adapter.DeployData[0]!.AsString());
        Assert.AreEqual("$step:ExternalBridgeEscrow", adapter.DeployData[1]!.AsString());
        Assert.AreEqual("L2_CHAIN_ID_REPLACE_ME", adapter.DeployData[2]!.AsString());
        Assert.AreEqual("L2_PAYOUT_RELAY_ACCOUNT_REPLACE_ME", adapter.DeployData[3]!.AsString());
        CollectionAssert.AreEqual(new[] { "ExternalBridgeEscrow" }, adapter.DependsOn.ToArray());

        var bundle = DeployPlanner.Plan(plan, _ => H(0x42));
        var action = ScaffoldPlan.PostDeployActions(bundle)
            .Single(line => line.StartsWith("ExternalBridgeEscrow.SetAssetRoute", StringComparison.Ordinal));
        StringAssert.Contains(action, "L2PayoutAdapter");
        Assert.IsFalse(action.Contains("PAYOUT_ADAPTER_V1", StringComparison.Ordinal),
            "the production plan must name the deployed adapter, not a comment placeholder");
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
    public void Scaffold_PostDeployActionsWireSettlementManagerToOptimisticChallenge()
    {
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, _ => H(0x42));

        var actions = ScaffoldPlan.PostDeployActions(bundle).ToArray();

        Assert.IsTrue(actions.Any(a => a.Contains("SettlementManager.SetOptimisticChallenge(OptimisticChallenge)", StringComparison.Ordinal)),
            "SettlementManager deploys before OptimisticChallenge to avoid a cycle, so the post-deploy wiring must be explicit.");
    }

    [TestMethod]
    public void Scaffold_RestrictedExecutionFraudVerifierBindsV4DeploymentDomain()
    {
        var plan = ScaffoldPlan.Default();
        var v = plan.Steps.Single(s => s.Name == "RestrictedExecutionFraudVerifier");
        Assert.AreEqual(2, v.DeployData.Count,
            "production v4 requires [SettlementManager, replayDomain]");
        Assert.AreEqual("$step:SettlementManager", v.DeployData[0]!.AsString());
        Assert.AreEqual("FRAUD_REPLAY_DOMAIN_REPLACE_ME", v.DeployData[1]!.AsString());
        CollectionAssert.AreEqual(new[] { "SettlementManager" }, v.DependsOn.ToArray());
        ScaffoldPlan.RequireExecutableOptimisticFraudProfile(plan);
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
    public void Scaffold_FraudVerifierDeploymentShapeReflectsSecurityModel()
    {
        var plan = ScaffoldPlan.Default();
        var rex = plan.Steps.Single(s => s.Name == "RestrictedExecutionFraudVerifier");
        Assert.IsFalse(plan.Steps.Any(s => s.Name == "GovernanceFraudVerifier"),
            "the structural governance verifier is advisory-only and must not be production deployed");
        Assert.AreEqual(2, rex.DeployData.Count,
            "restricted executable v4 must not inherit the legacy empty deploy shape");
        CollectionAssert.AreEqual(new[] { "SettlementManager" }, rex.DependsOn.ToArray());
    }

    [TestMethod]
    public void Scaffold_DefaultExcludesStructuralGovernanceFraudVerifier()
    {
        var plan = ScaffoldPlan.Default();
        Assert.IsFalse(plan.Steps.Any(s => s.Name == "GovernanceFraudVerifier"),
            "v1/v2 structural evidence cannot authorize state changes and must remain outside the production bundle");
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
        // 5 original (Phase 0-3) + 8 forced-inclusion enforcement/spam-control hints +
        // 1 emergency-withdrawal wiring hint +
        // 6 production ZK verifier wiring/governance-lock hints +
        // 1 exact atomic v4 profile registration +
        // 1 fraud-verifier security-boundary note +
        // 7 external-bridge governance/route/liquidity/setup hints +
        // 2 Phase-C wiring hints + 8 registry/settlement-governance/DA/optimistic/Gateway/filter wiring hints = 37.
        Assert.AreEqual(37, actions.Count);

        // 1. SequencerBond.RegisterSlasher(OptimisticChallenge) — Phase-3 cycle-break.
        StringAssert.Contains(actions[0], "SequencerBond.RegisterSlasher");
        StringAssert.Contains(actions[0], "OptimisticChallenge");

        // 2-9. Forced-inclusion reports must slash + pause and charge a non-zero spam fee.
        StringAssert.Contains(actions[1], "SequencerBond.RegisterSlasher");
        StringAssert.Contains(actions[1], "ForcedInclusion");
        StringAssert.Contains(actions[2], "ChainRegistry.RegisterPauser");
        StringAssert.Contains(actions[2], "ForcedInclusion");
        StringAssert.Contains(actions[3], "ForcedInclusion.SetChainRegistry");
        StringAssert.Contains(actions[3], "ChainRegistry");
        StringAssert.Contains(actions[4], "ForcedInclusion.SetSequencerBond");
        StringAssert.Contains(actions[4], "SequencerBond");
        StringAssert.Contains(actions[5], "ForcedInclusion.SetCensorshipSlashAmount");
        StringAssert.Contains(actions[5], "1000000");
        StringAssert.Contains(actions[6], "ForcedInclusion.SetGasToken");
        StringAssert.Contains(actions[6], "GAS_CONTRACT_HASH");
        StringAssert.Contains(actions[7], "ForcedInclusion.SetFeeRecipient");
        StringAssert.Contains(actions[7], "FEE_RECIPIENT");
        StringAssert.Contains(actions[8], "ForcedInclusion.SetFee");
        StringAssert.Contains(actions[8], "100000");

        // 10. SharedBridge must know EmergencyManager so paused withdrawals still pay out.
        StringAssert.Contains(actions[9], "SharedBridge.SetEmergencyManager");
        StringAssert.Contains(actions[9], "EmergencyManager");

        // 11. ChainRegistry.SetGovernanceController(GovernanceController) — §16.1.
        StringAssert.Contains(actions[10], "ChainRegistry.SetGovernanceController");
        StringAssert.Contains(actions[10], "GovernanceController");
        StringAssert.Contains(actions[10], "§16.1");

        // 12. VerifierRegistry.SetGovernanceController(GovernanceController) — §16 council-veto.
        StringAssert.Contains(actions[11], "VerifierRegistry.SetGovernanceController");
        StringAssert.Contains(actions[11], "GovernanceController");
        StringAssert.Contains(actions[11], "§16");

        // 13. SettlementManager must bind its emergency rollback to §16 before locking.
        StringAssert.Contains(actions[12], "SettlementManager.SetGovernanceController");
        StringAssert.Contains(actions[12], "GovernanceController");
        StringAssert.Contains(actions[12], "emergency batch rollback");

        // 14-19. ZK settlement must use the pinned SP1 verifier, irreversibly
        // disable envelope-only acceptance, and freeze the exact inner route before
        // routing ProofType.Zk to production.
        StringAssert.Contains(actions[13], "ContractZkVerifier.RegisterVerificationKey");
        StringAssert.Contains(actions[13], "ProofSystem.Sp1=1");
        StringAssert.Contains(actions[13], "PROGRAM_VKEY_REPLACE_ME");
        StringAssert.Contains(actions[14], "ContractZkVerifier.RegisterProofVerifier");
        StringAssert.Contains(actions[14], "Sp1Groth16Verifier");
        StringAssert.Contains(actions[15], "ContractZkVerifier.DisableEnvelopeOnlyPermanently");
        StringAssert.Contains(actions[15], "ProofSystem.Sp1=1");
        StringAssert.Contains(actions[16], "ContractZkVerifier.LockProofSystemConfiguration");
        StringAssert.Contains(actions[16], "ProofSystem.Sp1=1");
        StringAssert.Contains(actions[16], "PROGRAM_VKEY_REPLACE_ME");
        StringAssert.Contains(actions[17], "VerifierRegistry.RegisterVerifier");
        StringAssert.Contains(actions[17], "ProofType.Zk");
        StringAssert.Contains(actions[17], "ContractZkVerifier");
        StringAssert.Contains(actions[18], "VerifierRegistry.LockGovernance");
        StringAssert.Contains(actions[18], "irreversible production gate");

        // 20. Atomically approve and register the exact executable v4 profile.
        StringAssert.Contains(actions[19], "RegisterPermissionlessFraudProfile");
        StringAssert.Contains(actions[19], "RestrictedExecutionFraudVerifier");
        StringAssert.Contains(actions[19], "L2_CHAIN_ID_REPLACE_ME");
        StringAssert.Contains(actions[19], "FRAUD_REPLAY_DOMAIN_REPLACE_ME");

        // 21. Explain the strict executable-v4 security boundary.
        StringAssert.Contains(actions[20], "v1/v2/v3 fraud payloads are advisory only");
        StringAssert.Contains(actions[20], "governance or owner witness");

        // 22. MpcCommitteeVerifier.SetGovernanceController(GovernanceController) — bridge committee gov.
        StringAssert.Contains(actions[21], "MpcCommitteeVerifier.SetGovernanceController");
        StringAssert.Contains(actions[21], "GovernanceController");
        StringAssert.Contains(actions[21], "RegisterCommitteeViaProposal");

        // 23. ExternalBridgeRegistry.SetGovernanceController(GovernanceController) — bridge verifier upgrade gov.
        StringAssert.Contains(actions[22], "ExternalBridgeRegistry.SetGovernanceController");
        StringAssert.Contains(actions[22], "GovernanceController");
        StringAssert.Contains(actions[22], "UpgradeVerifierViaProposal");

        // 24. ExternalBridgeEscrow proposal-governance wiring.
        StringAssert.Contains(actions[23], "ExternalBridgeEscrow.SetGovernanceController");
        StringAssert.Contains(actions[23], "timelocked");

        // 25. Per-foreign-chain committee setup pointer.
        StringAssert.Contains(actions[24], "neo-external-bridge");
        StringAssert.Contains(actions[24], "RegisterVerifier");
        StringAssert.Contains(actions[24], "0xE0000001");

        // 26-28. Inbound payout route, collateral, and irreversible admin lock.
        StringAssert.Contains(actions[25], "ExternalBridgeEscrow.SetAssetRoute");
        StringAssert.Contains(actions[25], "payoutVersion()==1");
        StringAssert.Contains(actions[25], "UpdateCounter==0");
        StringAssert.Contains(actions[25], "non-zero L2_CHAIN_ID_REPLACE_ME");
        StringAssert.Contains(actions[25], "neoChainId=0");
        StringAssert.Contains(actions[26], "ExternalBridgeEscrow.FundLiquidity");
        StringAssert.Contains(actions[26], "Neo L1 direct-release routes only");
        StringAssert.Contains(actions[27], "ExternalBridgeEscrow.LockGovernance");
        StringAssert.Contains(actions[27], "ConfigureAssetRouteViaProposal");

        // 29. Phase-C: ExternalBridgeBond.RegisterSlasher(MpcCommitteeFraudVerifier).
        StringAssert.Contains(actions[28], "ExternalBridgeBond.RegisterSlasher");
        StringAssert.Contains(actions[28], "MpcCommitteeFraudVerifier");

        // 30. Phase-C: per-chain RegisterCommitteeWithMembers pointer.
        StringAssert.Contains(actions[29], "RegisterCommitteeWithMembers");
        StringAssert.Contains(actions[29], "MpcCommitteeFraudVerifier");

        StringAssert.Contains(actions[30], "SettlementManager.SetDARegistry");
        StringAssert.Contains(actions[31], "SettlementManager.SetDAValidator");
        StringAssert.Contains(actions[32], "SettlementManager.SetOptimisticChallenge");
        StringAssert.Contains(actions[33], "SettlementManager.SetMessageRouter");
        StringAssert.Contains(actions[33], "MessageRouter");
        StringAssert.Contains(actions[34], "ChainRegistry.LockGovernance");
        StringAssert.Contains(actions[34], "proposal-bound council approval");
        StringAssert.Contains(actions[35], "SettlementManager.LockGovernance");
        StringAssert.Contains(actions[35], "RevertBatchViaProposal");
        StringAssert.Contains(actions[36], "MessageRouter.SetL1TxFilter");
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
