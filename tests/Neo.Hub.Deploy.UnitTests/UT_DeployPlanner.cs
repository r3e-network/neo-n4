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
        // 15 core NeoHub contracts + 2 fraud verifiers (Governance v1/v2 +
        // RestrictedExecution v3) + 4 external-bridge contracts (doc.md
        // §11.3 Phase B: MpcCommitteeVerifier, ExternalBridgeRegistry,
        // ExternalBridgeEscrow, ExternalBridgeBond) + 1 Phase-C
        // (MpcCommitteeFraudVerifier) + 1 contract-deployed ZK verifier router
        // + 1 pinned SP1 Groth16 terminal verifier = 24.
        Assert.AreEqual(24, plan.Steps.Count);
        var names = plan.Steps.Select(s => s.Name).ToHashSet();
        Assert.IsTrue(names.Contains("GovernanceFraudVerifier"));
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
    public void Scaffold_GovernanceControllerHasCouncilDeployData()
    {
        var plan = ScaffoldPlan.Default();
        var governance = plan.Steps.Single(s => s.Name == "GovernanceController");

        Assert.AreEqual(4, governance.DeployData.Count,
            "GovernanceController _deploy needs (owner, councilMembers[], threshold, timelockSeconds).");
        Assert.AreEqual("OWNER_REPLACE_ME", governance.DeployData[0]!.AsString());
        var council = governance.DeployData[1] as Neo.Json.JArray;
        Assert.IsNotNull(council, "2nd arg must be a council public-key array");
        Assert.AreEqual("GOVERNANCE_COUNCIL_MEMBER_REPLACE_ME", council[0]!.AsString());
        Assert.AreEqual(1, (int)governance.DeployData[2]!.AsNumber());
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
    public void Scaffold_RestrictedExecutionFraudVerifierHasEmptyDeployData()
    {
        // Same shape pin as Scaffold_GovernanceFraudVerifierHasEmptyDeployData.
        // The on-chain v3 verifier is also stateless — re-derives state roots from
        // wire-format storage proofs. No owner / config / wired deps. Pin so a future
        // refactor that adds OwnerOnly()/OwnerAndDep() to one verifier without
        // updating the contract's _deploy handler would fail loud here, not at
        // deploy time on L1 with a confusing ContractManagement.Deploy cast error.
        var plan = ScaffoldPlan.Default();
        var v = plan.Steps.Single(s => s.Name == "RestrictedExecutionFraudVerifier");
        Assert.AreEqual(0, v.DeployData.Count,
            "RestrictedExecutionFraudVerifier is stateless — no deploy args");
        Assert.AreEqual(0, v.DependsOn.Count,
            "RestrictedExecutionFraudVerifier has no deploy-time dependencies — verifies a static wire format");
    }

    [TestMethod]
    public void Scaffold_BothFraudVerifiers_ParallelShape()
    {
        // Pin that the two fraud verifiers (v1/v2 governance + v3 trustless) have
        // identical deploy-shape: no args, no deps. They're peers — operators pick
        // one (or deploy both) based on whether they're running governance-
        // arbitration or trustless v3. A regression that adds asymmetry between
        // them would mean the planner can't be a pure superset.
        var plan = ScaffoldPlan.Default();
        var gov = plan.Steps.Single(s => s.Name == "GovernanceFraudVerifier");
        var rex = plan.Steps.Single(s => s.Name == "RestrictedExecutionFraudVerifier");
        Assert.AreEqual(gov.DeployData.Count, rex.DeployData.Count);
        Assert.AreEqual(gov.DependsOn.Count, rex.DependsOn.Count);
    }

    [TestMethod]
    public void Scaffold_GovernanceFraudVerifierHasEmptyDeployData()
    {
        // Pin the GovernanceFraudVerifier step's empty-args deploy shape. The contract
        // is stateless (no _deploy handler, no Storage.Put) — the structural verifier
        // doesn't need owner / config / wired-deps. A regression that adds an
        // OwnerOnly() or OwnerAndDep() call would break ContractManagement.Deploy
        // with a confusing cast error at deploy time. Pin the empty JArray shape so
        // the no-args contract on-chain stays in lockstep with the planner.
        var plan = ScaffoldPlan.Default();
        var v = plan.Steps.Single(s => s.Name == "GovernanceFraudVerifier");
        Assert.AreEqual(0, v.DeployData.Count,
            "GovernanceFraudVerifier is stateless — no deploy args");
        Assert.AreEqual(0, v.DependsOn.Count,
            "GovernanceFraudVerifier has no deploy-time dependencies — verifies a static wire format");
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
        // governance never gets wired; or miss the informational notes about which
        // fraud-verifier contract hash to pass as fraudVerifier when challenging
        // (one note per deployed verifier — v1/v2 + v3).
        var plan = ScaffoldPlan.Default();
        var bundle = DeployPlanner.Plan(plan, name => H((byte)(name.Length & 0xFF)));
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        // 5 original (Phase 0-3) + 8 forced-inclusion enforcement/spam-control hints +
        // 1 emergency-withdrawal wiring hint +
        // 6 production ZK verifier wiring/governance-lock hints +
        // 2 RegisterFraudVerifier wiring + 2 fraud-verifier informational notes +
        // 3 external-bridge gov/setup hints +
        // 2 Phase-C wiring hints + 4 DA/optimistic/filter production wiring hints = 31.
        Assert.AreEqual(31, actions.Count);

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

        // 10-15. ZK settlement must use the pinned SP1 verifier, irreversibly
        // disable envelope-only acceptance, and freeze the exact inner route before
        // routing ProofType.Zk to production.
        StringAssert.Contains(actions[12], "ContractZkVerifier.RegisterVerificationKey");
        StringAssert.Contains(actions[12], "ProofSystem.Sp1=1");
        StringAssert.Contains(actions[12], "PROGRAM_VKEY_REPLACE_ME");
        StringAssert.Contains(actions[13], "ContractZkVerifier.RegisterProofVerifier");
        StringAssert.Contains(actions[13], "Sp1Groth16Verifier");
        StringAssert.Contains(actions[14], "ContractZkVerifier.DisableEnvelopeOnlyPermanently");
        StringAssert.Contains(actions[14], "ProofSystem.Sp1=1");
        StringAssert.Contains(actions[15], "ContractZkVerifier.LockProofSystemConfiguration");
        StringAssert.Contains(actions[15], "ProofSystem.Sp1=1");
        StringAssert.Contains(actions[15], "PROGRAM_VKEY_REPLACE_ME");
        StringAssert.Contains(actions[16], "VerifierRegistry.RegisterVerifier");
        StringAssert.Contains(actions[16], "ProofType.Zk");
        StringAssert.Contains(actions[16], "ContractZkVerifier");
        StringAssert.Contains(actions[17], "VerifierRegistry.LockGovernance");
        StringAssert.Contains(actions[17], "irreversible production gate");

        // 16. OptimisticChallenge.RegisterFraudVerifier(GovernanceFraudVerifier) — allowlist gate.
        StringAssert.Contains(actions[18], "OptimisticChallenge.RegisterFraudVerifier");
        StringAssert.Contains(actions[18], "GovernanceFraudVerifier");

        // 17. Informational: pass GovernanceFraudVerifier hash to OptimisticChallenge.Challenge (v1/v2).
        StringAssert.Contains(actions[19], "GovernanceFraudVerifier");
        StringAssert.Contains(actions[19], "fraudVerifier");
        StringAssert.Contains(actions[19], "OptimisticChallenge.Challenge");
        StringAssert.Contains(actions[19], "v1/v2");

        // 18. OptimisticChallenge.RegisterFraudVerifier(RestrictedExecutionFraudVerifier) — allowlist gate.
        StringAssert.Contains(actions[20], "OptimisticChallenge.RegisterFraudVerifier");
        StringAssert.Contains(actions[20], "RestrictedExecutionFraudVerifier");

        // 19. Informational: pass RestrictedExecutionFraudVerifier hash to OptimisticChallenge.Challenge (v3 trustless).
        StringAssert.Contains(actions[21], "RestrictedExecutionFraudVerifier");
        StringAssert.Contains(actions[21], "fraudVerifier");
        StringAssert.Contains(actions[21], "OptimisticChallenge.Challenge");
        StringAssert.Contains(actions[21], "v3");

        // 20. MpcCommitteeVerifier.SetGovernanceController(GovernanceController) — bridge committee gov.
        StringAssert.Contains(actions[22], "MpcCommitteeVerifier.SetGovernanceController");
        StringAssert.Contains(actions[22], "GovernanceController");
        StringAssert.Contains(actions[22], "RegisterCommitteeViaProposal");

        // 21. ExternalBridgeRegistry.SetGovernanceController(GovernanceController) — bridge verifier upgrade gov.
        StringAssert.Contains(actions[23], "ExternalBridgeRegistry.SetGovernanceController");
        StringAssert.Contains(actions[23], "GovernanceController");
        StringAssert.Contains(actions[23], "UpgradeVerifierViaProposal");

        // 22. Per-foreign-chain committee setup pointer.
        StringAssert.Contains(actions[24], "neo-external-bridge");
        StringAssert.Contains(actions[24], "RegisterVerifier");
        StringAssert.Contains(actions[24], "0xE0000001");

        // 23. Phase-C: ExternalBridgeBond.RegisterSlasher(MpcCommitteeFraudVerifier).
        StringAssert.Contains(actions[25], "ExternalBridgeBond.RegisterSlasher");
        StringAssert.Contains(actions[25], "MpcCommitteeFraudVerifier");

        // 24. Phase-C: per-chain RegisterCommitteeWithMembers pointer.
        StringAssert.Contains(actions[26], "RegisterCommitteeWithMembers");
        StringAssert.Contains(actions[26], "MpcCommitteeFraudVerifier");
    }

    [TestMethod]
    public void PostDeployActions_OnlyV3FraudVerifier_EmitsOnlyV3Note()
    {
        // Asymmetric pin: an operator who only deploys RestrictedExecutionFraudVerifier
        // (e.g. a chain that wants trustless v3 only, no governance-arbitration fallback)
        // gets ONLY the v3 note — not the v1/v2 GovernanceFraudVerifier note. This
        // verifies the two verifier-note paths are independent.
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
        var actions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        // 2 actions: allowlist register + informational note. Both reference the v3 verifier;
        // neither references the v1/v2 GovernanceFraudVerifier (which is not in this plan).
        Assert.AreEqual(2, actions.Count);
        StringAssert.Contains(actions[0], "OptimisticChallenge.RegisterFraudVerifier");
        StringAssert.Contains(actions[0], "RestrictedExecutionFraudVerifier");
        StringAssert.Contains(actions[1], "RestrictedExecutionFraudVerifier");
        StringAssert.Contains(actions[1], "v3");
        // v1/v2 note must NOT be present in either action.
        foreach (var action in actions)
        {
            Assert.IsFalse(action.Contains("GovernanceFraudVerifier"),
                "v1/v2 verifier must not appear when only v3 verifier is in the bundle");
        }
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
    public void PostDeployActions_OnlyChainRegistryAndGovernance_EmitsOneHint()
    {
        // Asymmetry test: an operator who only deploys ChainRegistry (without
        // VerifierRegistry — e.g. governance-controlled chain admission only) gets
        // exactly one hint.
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
        Assert.AreEqual(1, actions.Count);
        StringAssert.Contains(actions[0], "ChainRegistry.SetGovernanceController");
    }

    [TestMethod]
    public void PostDeployActions_RejectsNullBundle()
    {
        Assert.ThrowsExactly<ArgumentNullException>(
            () => ScaffoldPlan.PostDeployActions(null!).ToList());
    }
}
