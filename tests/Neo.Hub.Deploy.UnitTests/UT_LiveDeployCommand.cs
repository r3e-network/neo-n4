using System.Text.Json;
using System.Text.Json.Nodes;
using Neo.Extensions.IO;
using Neo.Extensions.VM;
using Neo.Json;
using Neo.L2.Proving.RiscVZk;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.VM;
using StjSerializer = System.Text.Json.JsonSerializer;

namespace Neo.Hub.Deploy.UnitTests;

[TestClass]
public class UT_LiveDeployCommand
{
    [TestMethod]
    public void NeoGas_UsesEightDecimalDatoshiScale()
    {
        Assert.AreEqual(10_000_000L, Neo.L2.Settlement.Rpc.NeoGas.ParseRpcValue("0.1"));
        Assert.AreEqual(100_000_000L, Neo.L2.Settlement.Rpc.NeoGas.ParseRpcValue("1.00000000"));
        Assert.AreEqual(42L, Neo.L2.Settlement.Rpc.NeoGas.ParseRpcValue("42"));
    }

    private static readonly byte[] AsymmetricProgramVKey = Convert.FromHexString(
        "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f");
    private static readonly UInt256 FraudReplayDomain = new(
        Convert.FromHexString("a50102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1eff"));
    private static readonly UInt256 GatewayProgramVKey = new(
        Convert.FromHexString("00d1d2d3d4d5d6d7d8d9dadbdcdddedfd0d1d2d3d4d5d6d7d8d9dadbdcdddedf"));
    private static readonly UInt256 GatewayReplayDomain = new(
        Convert.FromHexString("b60102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1eff"));

    private static IReadOnlyList<Neo.Cryptography.ECC.ECPoint> GovernanceCouncil()
    {
        return
        [
            new Neo.Wallets.KeyPair(new byte[32]
                { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }).PublicKey,
            new Neo.Wallets.KeyPair(new byte[32]
                { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                  0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }).PublicKey,
        ];
    }

    [TestMethod]
    public void ParseSp1ProgramVKey_AsymmetricRawHex_PreservesCanonicalWireBytes()
    {
        var parsed = LiveDeployCommand.ParseSp1ProgramVKey(
            $"0x{Convert.ToHexString(AsymmetricProgramVKey)}");

        CollectionAssert.AreEqual(AsymmetricProgramVKey, parsed.GetSpan().ToArray());
        CollectionAssert.AreNotEqual(AsymmetricProgramVKey, parsed.GetSpan().ToArray().Reverse().ToArray());

        var payload = new RiscVProofPayload
        {
            ProofSystem = ProofSystem.Sp1,
            VerificationKeyId = parsed,
            ProofBytes = new byte[] { 0xA1, 0xB2, 0xC3 },
        }.Encode();

        CollectionAssert.AreEqual(AsymmetricProgramVKey, payload[2..34],
            "RiscVProofPayload must carry bytes32_raw() without UInt256 display-order reversal");
        CollectionAssert.AreEqual(
            AsymmetricProgramVKey,
            RiscVProofPayload.Decode(payload).VerificationKeyId.GetSpan().ToArray(),
            "payload decode must preserve the exact SP1 program vkey bytes");
    }

    [TestMethod]
    public void ParseSp1ProgramVKey_ExactBinaryFile_PreservesRawBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"neo-n4-sp1-vkey-{Guid.NewGuid():N}.proof.vk");
        try
        {
            File.WriteAllBytes(path, AsymmetricProgramVKey);
            var parsed = LiveDeployCommand.ParseSp1ProgramVKey(path);
            CollectionAssert.AreEqual(AsymmetricProgramVKey, parsed.GetSpan().ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestMethod]
    public void ParseSp1ProgramVKey_MissingMalformedWrongLengthOrZero_FailsClosed()
    {
        Assert.ThrowsExactly<ArgumentException>(() => LiveDeployCommand.ParseSp1ProgramVKey(""));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseSp1ProgramVKey("0x1234"));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseSp1ProgramVKey(new string('g', 64)));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseSp1ProgramVKey(new string('0', 64)));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseSp1ProgramVKey("01" + new string('0', 62)));

        foreach (var length in new[] { 31, 33 })
        {
            var path = Path.Combine(Path.GetTempPath(), $"neo-n4-sp1-vkey-{Guid.NewGuid():N}.proof.vk");
            try
            {
                File.WriteAllBytes(path, new byte[length]);
                Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseSp1ProgramVKey(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    [TestMethod]
    public void ParseRequiredL2ChainId_RequiresCanonicalNonZeroUInt32()
    {
        Assert.AreEqual(1001u, LiveDeployCommand.ParseRequiredL2ChainId("1001"));
        Assert.AreEqual(uint.MaxValue, LiveDeployCommand.ParseRequiredL2ChainId(uint.MaxValue.ToString()));
        Assert.ThrowsExactly<ArgumentException>(() => LiveDeployCommand.ParseRequiredL2ChainId(""));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseRequiredL2ChainId("0"));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseRequiredL2ChainId("-1"));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseRequiredL2ChainId("4294967296"));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseRequiredL2ChainId(" 1001"));
    }

    [TestMethod]
    public void ParseRequiredFraudReplayDomain_RequiresExactNonZeroRawBytes()
    {
        var raw = FraudReplayDomain.GetSpan().ToArray();
        var parsed = LiveDeployCommand.ParseRequiredFraudReplayDomain(
            $"0x{Convert.ToHexString(raw)}");

        CollectionAssert.AreEqual(raw, parsed.GetSpan().ToArray());
        Assert.ThrowsExactly<ArgumentException>(() =>
            LiveDeployCommand.ParseRequiredFraudReplayDomain(""));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseRequiredFraudReplayDomain("0x1234"));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseRequiredFraudReplayDomain(new string('g', 64)));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseRequiredFraudReplayDomain(new string('0', 64)));
    }

    [TestMethod]
    public void ParseGatewayProgramVKey_KeepsCanonicalWireBytesAndNamesItsOwnSwitch()
    {
        var raw = GatewayProgramVKey.GetSpan().ToArray();
        var parsed = LiveDeployCommand.ParseGatewayProgramVKey($"0x{Convert.ToHexString(raw)}");
        CollectionAssert.AreEqual(raw, parsed.GetSpan().ToArray());

        Assert.ThrowsExactly<ArgumentException>(() => LiveDeployCommand.ParseGatewayProgramVKey(""));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseGatewayProgramVKey("ff" + new string('0', 62)));

        var message = Assert.ThrowsExactly<ArgumentException>(
            () => LiveDeployCommand.ParseGatewayProgramVKey(string.Empty)).Message;
        StringAssert.Contains(message, "--gateway-program-vkey",
            "the shared vkey parser must fault with the Gateway switch name, not the SP1 one");
    }

    [TestMethod]
    public void ParseRequiredGatewayReplayDomain_RequiresExactNonZeroBytesAndNamesItsOwnSwitch()
    {
        var raw = GatewayReplayDomain.GetSpan().ToArray();
        var parsed = LiveDeployCommand.ParseRequiredGatewayReplayDomain($"0x{Convert.ToHexString(raw)}");
        CollectionAssert.AreEqual(raw, parsed.GetSpan().ToArray());
        Assert.AreNotEqual(FraudReplayDomain, parsed,
            "the Gateway and fraud replay domains are independent locks and must not share a fixture value");

        var message = Assert.ThrowsExactly<FormatException>(
            () => LiveDeployCommand.ParseRequiredGatewayReplayDomain(new string('0', 64))).Message;
        StringAssert.Contains(message, "--gateway-replay-domain",
            "the shared replay-domain parser must fault with the Gateway switch name, not the fraud one");
    }

    [TestMethod]
    public void ParsePositiveForcedInclusionFee_RejectsDisabledOrMalformedProductionFee()
    {
        Assert.AreEqual(100_000L, LiveDeployCommand.ParsePositiveForcedInclusionFee("100000"));
        Assert.AreEqual(long.MaxValue, LiveDeployCommand.ParsePositiveForcedInclusionFee(long.MaxValue.ToString()));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParsePositiveForcedInclusionFee(""));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParsePositiveForcedInclusionFee("0"));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParsePositiveForcedInclusionFee("-1"));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParsePositiveForcedInclusionFee(" 100000"));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParsePositiveForcedInclusionFee("1.0"));
    }

    [TestMethod]
    public void ParseGovernanceCouncilAndThreshold_RequireDistinctThresholdMultisig()
    {
        var council = GovernanceCouncil();
        var encoded = string.Join(',', council.Select(member => member.ToString()));
        var parsed = LiveDeployCommand.ParseGovernanceCouncil(encoded);

        CollectionAssert.AreEqual(
            council.Select(member => member.ToString()).ToArray(),
            parsed.Select(member => member.ToString()).ToArray());
        Assert.AreEqual(2u, LiveDeployCommand.ParseGovernanceThreshold("2", parsed.Count));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LiveDeployCommand.ParseGovernanceCouncil(""));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseGovernanceCouncil(council[0].ToString()));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseGovernanceCouncil(
                $"{council[0]},{council[0]}"));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseGovernanceThreshold("1", parsed.Count));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseGovernanceThreshold("3", parsed.Count));
    }

    [TestMethod]
    public void SubstituteOperatorPlaceholders_ReplacesChainAndFraudDomains()
    {
        var owner = new UInt160(new byte[UInt160.Length]);
        var gas = new UInt160(Enumerable.Repeat((byte)2, UInt160.Length).ToArray());
        var emergencyCouncil = new UInt160(Enumerable.Repeat((byte)3, UInt160.Length).ToArray());
        var payoutRelay = new UInt160(Enumerable.Repeat((byte)4, UInt160.Length).ToArray());

        var plan = new DeployPlan
        {
            Version = 1,
            Network = "neo-n3-testnet",
            Steps = new[]
            {
                new DeployStep
                {
                    Name = "SettlementManager",
                    NefPath = "test.nef",
                    ManifestPath = "test.manifest.json",
                    DeployData = new JArray(),
                    DependsOn = []
                },
                new DeployStep
                {
                    Name = "GovernanceController",
                    NefPath = "test.nef",
                    ManifestPath = "test.manifest.json",
                    DeployData = new JArray { "OWNER_REPLACE_ME", new JArray { "GOVERNANCE_COUNCIL_REPLACE_ME" }, "GOVERNANCE_THRESHOLD_REPLACE_ME", 3600 },
                    DependsOn = []
                },
                new DeployStep
                {
                    Name = "EmergencyManager",
                    NefPath = "test.nef",
                    ManifestPath = "test.manifest.json",
                    DeployData = new JArray { "OWNER_REPLACE_ME", "EMERGENCY_COUNCIL_REPLACE_ME" },
                    DependsOn = []
                },
                new DeployStep
                {
                    Name = "ExternalBridgeEscrow",
                    NefPath = "test.nef",
                    ManifestPath = "test.manifest.json",
                    DeployData = new JArray { "OWNER_REPLACE_ME", "$step:SettlementManager", "L2_CHAIN_ID_REPLACE_ME" },
                    DependsOn = ["SettlementManager"]
                },
                new DeployStep
                {
                    Name = "L2PayoutAdapter",
                    NefPath = "test.nef",
                    ManifestPath = "test.manifest.json",
                    DeployData = new JArray { "OWNER_REPLACE_ME", "$step:SettlementManager", "L2_CHAIN_ID_REPLACE_ME", "L2_PAYOUT_RELAY_ACCOUNT_REPLACE_ME" },
                    DependsOn = ["SettlementManager"]
                },
                new DeployStep
                {
                    Name = "OptimisticChallenge",
                    NefPath = "test.nef",
                    ManifestPath = "test.manifest.json",
                    DeployData = new JArray { "OWNER_REPLACE_ME", "$step:SettlementManager" },
                    DependsOn = []
                },
                new DeployStep
                {
                    Name = "RestrictedExecutionFraudVerifier",
                    NefPath = "test.nef",
                    ManifestPath = "test.manifest.json",
                    DeployData = new JArray { "$step:SettlementManager", "FRAUD_REPLAY_DOMAIN_REPLACE_ME" },
                    DependsOn = new[] { "SettlementManager" }
                }
            }
        };

        var substituted = LiveDeployCommand.SubstituteOperatorPlaceholders(
            plan, owner, gas, GovernanceCouncil(), 2, emergencyCouncil, 1001,
            FraudReplayDomain, payoutRelay);
        var escrow = substituted.Steps.Single(s => s.Name == "ExternalBridgeEscrow");
        var payoutAdapter = substituted.Steps.Single(s => s.Name == "L2PayoutAdapter");
        var emergency = substituted.Steps.Single(s => s.Name == "EmergencyManager");
        var restricted = substituted.Steps.Single(
            s => s.Name == "RestrictedExecutionFraudVerifier");
        var governance = substituted.Steps.Single(s => s.Name == "GovernanceController");

        Assert.IsInstanceOfType<Neo.Json.JNumber>(escrow.DeployData[2]);
        Assert.AreEqual(1001d, escrow.DeployData[2]!.AsNumber());
        Assert.AreEqual(emergencyCouncil.ToString(), emergency.DeployData[1]!.AsString());
        Assert.AreEqual(FraudReplayDomain.ToString(), restricted.DeployData[1]!.AsString());
        Assert.AreEqual(payoutRelay.ToString(), payoutAdapter.DeployData[3]!.AsString());
        Assert.AreEqual(2, ((JArray)governance.DeployData[1]!).Count);
        Assert.AreEqual(2d, governance.DeployData[2]!.AsNumber());

        var resolvedRestricted = DeployPlanner.Plan(substituted, _ => owner).Invocations
            .Single(invocation => invocation.Name == "RestrictedExecutionFraudVerifier");
        var deployParameter = LiveDeployCommand.BuildDeployData(
            resolvedRestricted.ResolvedDeployData);
        Assert.AreEqual(ContractParameterType.Array, deployParameter.Type);
        var deployValues = (IReadOnlyList<ContractParameter>)deployParameter.Value!;
        Assert.AreEqual(ContractParameterType.Hash160, deployValues[0].Type);
        Assert.AreEqual(ContractParameterType.Hash256, deployValues[1].Type);
        Assert.AreEqual(FraudReplayDomain, deployValues[1].Value);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            LiveDeployCommand.SubstituteOperatorPlaceholders(
                ScaffoldPlan.Default(), owner, gas, GovernanceCouncil(), 2, emergencyCouncil, 0,
                FraudReplayDomain, payoutRelay));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LiveDeployCommand.SubstituteOperatorPlaceholders(
                ScaffoldPlan.Default(), owner, gas, GovernanceCouncil(), 2, emergencyCouncil, 1001,
                UInt256.Zero, payoutRelay));
    }

    [TestMethod]
    public void SubstituteOperatorPlaceholders_LegacyOnlyOptimisticPlan_IsRejected()
    {
        var owner = new UInt160(new byte[UInt160.Length]);
        var plan = new DeployPlan
        {
            Version = 1,
            Network = "test",
            Steps =
            [
                new DeployStep
                {
                    Name = "OptimisticChallenge",
                    NefPath = "optimistic.nef",
                    ManifestPath = "optimistic.manifest.json",
                    DeployData = new JArray { "OWNER_REPLACE_ME" },
                    DependsOn = [],
                },
                new DeployStep
                {
                    Name = "GovernanceFraudVerifier",
                    NefPath = "governance.nef",
                    ManifestPath = "governance.manifest.json",
                    DeployData = new JArray(),
                    DependsOn = [],
                },
            ],
        };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.SubstituteOperatorPlaceholders(
                plan, owner, owner, GovernanceCouncil(), 2, owner, 1001,
                FraudReplayDomain, owner));
        StringAssert.Contains(ex.Message, "unsupported optimistic deployment");
        StringAssert.Contains(ex.Message, "v1/v2/v3");
    }

    [TestMethod]
    public void ProductionEndpointAndNetworkValidation_FailsClosed()
    {
        Assert.AreEqual(894710606u, LiveDeployCommand.ParseRequiredNetwork("894710606"));
        Assert.ThrowsExactly<ArgumentException>(() => LiveDeployCommand.ParseRequiredNetwork(""));
        Assert.ThrowsExactly<FormatException>(() => LiveDeployCommand.ParseRequiredNetwork("-1"));

        var secure = LiveDeployCommand.ParseAndValidateRpcEndpoint(
            "https://user:secret@example.com:8443/rpc?api-key=secret");
        Assert.AreEqual("https://example.com:8443/", LiveDeployCommand.RedactRpcEndpoint(secure));
        _ = LiveDeployCommand.ParseAndValidateRpcEndpoint("http://127.0.0.1:10332");
        _ = LiveDeployCommand.ParseAndValidateRpcEndpoint("http://localhost:10332");
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.ParseAndValidateRpcEndpoint("http://example.com:10332"));
        Assert.ThrowsExactly<FormatException>(() =>
            LiveDeployCommand.ParseAndValidateRpcEndpoint("not-a-uri"));
    }

    [TestMethod]
    public void ProductionSafetyOptions_LiveModeCannotSkipBindingOrPostconditions()
    {
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.ValidateProductionSafetyOptions(dryRun: false, runPostDeploy: false, runSmoke: true));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.ValidateProductionSafetyOptions(dryRun: false, runPostDeploy: true, runSmoke: false));
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.ValidateProductionSafetyOptions(
                dryRun: false,
                runPostDeploy: true,
                runSmoke: true,
                maxSteps: 1));

        LiveDeployCommand.ValidateProductionSafetyOptions(dryRun: false, runPostDeploy: true, runSmoke: true);
        LiveDeployCommand.ValidateProductionSafetyOptions(
            dryRun: true,
            runPostDeploy: false,
            runSmoke: false,
            maxSteps: 1);
    }

    [TestMethod]
    public void ValidateRemoteContractState_RequiresExactImmutableNefAndManifest()
    {
        var hash = ContractHashes()["Sp1Groth16Verifier"];
        var nef = new NefFile
        {
            Compiler = "nccs-test",
            Source = "",
            Tokens = [],
            Script = new byte[] { (byte)OpCode.RET },
            CheckSum = 0,
        };
        nef.CheckSum = NefFile.ComputeChecksum(nef);
        var nefBytes = nef.ToArray();
        const string manifestJson =
            "{\"name\":\"ExactVerifier\",\"groups\":[],\"features\":{},\"supportedstandards\":[]," +
            "\"abi\":{\"methods\":[{\"name\":\"verify\",\"parameters\":[],\"returntype\":\"Boolean\"," +
            "\"offset\":0,\"safe\":true}],\"events\":[]},\"permissions\":[],\"trusts\":[],\"extra\":null}";
        var manifest = ContractManifest.Parse(manifestJson);

        var expectedNefJson = JsonNode.Parse(nef.ToJson().ToString())!;
        var expectedManifestJson = JsonNode.Parse(manifest.ToJson().ToString())!;
        var exact = ContractStateResult(hash, updateCounter: 0, expectedNefJson, expectedManifestJson);
        LiveDeployCommand.ValidateRemoteContractState(
            "Sp1Groth16Verifier", hash, nefBytes, manifestJson, exact);

        var updated = ContractStateResult(
            hash,
            updateCounter: 1,
            expectedNefJson.DeepClone(),
            expectedManifestJson.DeepClone());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.ValidateRemoteContractState(
                "Sp1Groth16Verifier", hash, nefBytes, manifestJson, updated));

        var alteredNef = expectedNefJson.DeepClone().AsObject();
        alteredNef["script"] = Convert.ToBase64String([(byte)OpCode.NOP, (byte)OpCode.RET]);
        var wrongCode = ContractStateResult(
            hash,
            updateCounter: 0,
            alteredNef,
            expectedManifestJson.DeepClone());
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.ValidateRemoteContractState(
                "Sp1Groth16Verifier", hash, nefBytes, manifestJson, wrongCode));

        var alteredManifest = expectedManifestJson.DeepClone().AsObject();
        alteredManifest["name"] = "DifferentVerifier";
        var wrongManifest = ContractStateResult(
            hash,
            updateCounter: 0,
            expectedNefJson.DeepClone(),
            alteredManifest);
        Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.ValidateRemoteContractState(
                "Sp1Groth16Verifier", hash, nefBytes, manifestJson, wrongManifest));
    }

    [TestMethod]
    public void BuildPostDeployCalls_WiresLeanPillarsAndLocksSp1()
    {
        var hashes = LeanContractHashes();
        var programVKey = new UInt256(AsymmetricProgramVKey);
        var actions = LiveDeployCommand.BuildPostDeployCalls(
            hashes,
            hashes["Gas"],
            hashes["Owner"],
            100_000,
            programVKey,
            1001,
            FraudReplayDomain,
            GatewayProgramVKey,
            GatewayReplayDomain).ToArray();
        Assert.IsTrue(actions.All(static action => action.CompletionCheck is not null),
            "every production post-deploy mutation must be resumable from its exact target state");

        Assert.AreEqual(8, actions.Length);
        Assert.AreEqual("RollupHub.SetGovernanceController", actions[0].Name);
        Assert.AreEqual("SharedBridge.SetSettlementManager", actions[1].Name);
        Assert.AreEqual("SharedBridge.SetEmergencyManager", actions[2].Name);
        Assert.AreEqual("RollupHub.SetSharedBridge", actions[3].Name);
        Assert.AreEqual("ZkVerifier.RegisterVerificationKey.Sp1", actions[4].Name);
        Assert.AreEqual("ZkVerifier.RegisterProofVerifier.Sp1", actions[5].Name);
        Assert.AreEqual("ZkVerifier.DisableEnvelopeOnlyPermanently.Sp1", actions[6].Name);
        Assert.AreEqual("ZkVerifier.LockProofSystemConfiguration.Sp1", actions[7].Name);

        using (var expected = new ScriptBuilder())
        {
            expected.EmitDynamicCall(
                hashes["SharedBridge"],
                "setSettlementManager",
                hashes["RollupHub"]);
            CollectionAssert.AreEqual(expected.ToArray(), actions[1].Script);
        }
        using (var expected = new ScriptBuilder())
        {
            expected.EmitDynamicCall(
                hashes["RollupHub"],
                "setSharedBridge",
                hashes["SharedBridge"]);
            CollectionAssert.AreEqual(expected.ToArray(), actions[3].Script);
        }
        using (var expected = new ScriptBuilder())
        {
            expected.EmitDynamicCall(
                hashes["ZkVerifier"],
                "registerVerificationKey",
                (byte)1,
                programVKey,
                true);
            CollectionAssert.AreEqual(expected.ToArray(), actions[4].Script);
        }
        Assert.IsTrue(actions[4].Script.AsSpan().IndexOf(programVKey.GetSpan()) >= 0);
    }

    [TestMethod]
    public void BuildPostDeployCalls_MissingLeanHash_FailsClosed()
    {
        var hashes = LeanContractHashes().ToDictionary(static kv => kv.Key, static kv => kv.Value);
        hashes.Remove("RollupHub");
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            LiveDeployCommand.BuildPostDeployCalls(
                hashes,
                hashes["Gas"],
                hashes["Owner"],
                100_000,
                new UInt256(AsymmetricProgramVKey),
                1001,
                FraudReplayDomain,
                GatewayProgramVKey,
                GatewayReplayDomain));
        StringAssert.Contains(ex.Message, "RollupHub");
    }

    [TestMethod]
    public void BuildPostDeployCalls_DoesNotIndexDeletedMicroContracts()
    {
        var hashes = LeanContractHashes();
        var actions = LiveDeployCommand.BuildPostDeployCalls(
            hashes,
            hashes["Gas"],
            hashes["Owner"],
            100_000,
            new UInt256(AsymmetricProgramVKey),
            1001,
            FraudReplayDomain,
            GatewayProgramVKey,
            GatewayReplayDomain);
        Assert.IsFalse(actions.Any(a => a.Name.Contains("MessageRouter", StringComparison.Ordinal)));
        Assert.IsFalse(actions.Any(a => a.Name.Contains("ChainRegistry", StringComparison.Ordinal)));
        Assert.IsFalse(actions.Any(a => a.Name.Contains("ForcedInclusion", StringComparison.Ordinal)));
        Assert.IsFalse(actions.Any(a => a.Name.StartsWith("SettlementManager.", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void ValidateNativeGasHash_RejectsNonNativeFeeToken()
    {
        LiveDeployCommand.ValidateNativeGasHash(LiveDeployCommand.NativeGasHash);
        Assert.ThrowsExactly<ArgumentException>(() =>
            LiveDeployCommand.ValidateNativeGasHash(
                UInt160.Parse("0x" + new string('6', UInt160.Length * 2))));
    }

    [TestMethod]
    public async Task IsPostDeployActionComplete_ExactStateSkipsMismatchRetries()
    {
        var hashes = LeanContractHashes();
        var action = LiveDeployCommand.BuildPostDeployCalls(
                hashes,
                hashes["Gas"],
                hashes["Owner"],
                100_000,
                new UInt256(AsymmetricProgramVKey),
                1001,
                FraudReplayDomain,
                GatewayProgramVKey,
                GatewayReplayDomain)
            .Single(item => item.Name == "ZkVerifier.DisableEnvelopeOnlyPermanently.Sp1");

        Assert.IsNotNull(action.CompletionCheck);
        Assert.IsTrue(await LiveDeployCommand.IsPostDeployActionCompleteAsync(
            action.CompletionCheck, new StubRpcClient(BooleanResult(true))));
        Assert.IsFalse(await LiveDeployCommand.IsPostDeployActionCompleteAsync(
            action.CompletionCheck, new StubRpcClient(BooleanResult(false))));
    }

    [TestMethod]
    public async Task BuildSmokeChecks_AllLeanPostconditionsPassAndQueryUsesRawVKeyBytes()
    {
        var hashes = LeanContractHashes();
        var programVKey = new UInt256(AsymmetricProgramVKey);
        var smokes = LiveDeployCommand.BuildSmokeChecks(
            hashes,
            hashes["Owner"],
            hashes["Gas"],
            hashes["Owner"],
            100_000,
            programVKey,
            1001,
            FraudReplayDomain,
            GatewayProgramVKey,
            GatewayReplayDomain,
            2,
            2).ToDictionary(check => check.Name, StringComparer.Ordinal);

        var verificationKeyRpc = new StubRpcClient(BooleanResult(true));
        await smokes["ZkVerifier.IsVerificationKeyRegistered.Sp1"].RunAsync(verificationKeyRpc);
        Assert.AreEqual("invokescript", verificationKeyRpc.Calls.Single().Method);
        var queryScript = Convert.FromBase64String((string)verificationKeyRpc.Calls.Single().Parameters[0]!);
        using (var expected = new ScriptBuilder())
        {
            expected.EmitDynamicCall(
                hashes["ZkVerifier"],
                "isVerificationKeyRegistered",
                CallFlags.ReadOnly,
                (byte)1,
                programVKey);
            CollectionAssert.AreEqual(expected.ToArray(), queryScript);
        }

        await smokes["RollupHub.GetOwner"].RunAsync(new StubRpcClient(HashResult(hashes["Owner"])));
        await smokes["RollupHub.GetGovernanceController"].RunAsync(new StubRpcClient(HashResult(hashes["GovernanceController"])));
        await smokes["RollupHub.GetSharedBridge"].RunAsync(new StubRpcClient(HashResult(hashes["SharedBridge"])));
        await smokes["SharedBridge.GetSettlementManager"].RunAsync(new StubRpcClient(HashResult(hashes["RollupHub"])));
        await smokes["SharedBridge.GetEmergencyManager"].RunAsync(new StubRpcClient(HashResult(hashes["GovernanceController"])));
        await smokes["ZkVerifier.GetProofVerifier.Sp1"].RunAsync(new StubRpcClient(HashResult(hashes["Sp1Groth16Verifier"])));
        await smokes["ZkVerifier.IsEnvelopeOnlyLocked.Sp1"].RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["ZkVerifier.IsEnvelopeOnlyAllowed.Sp1"].RunAsync(new StubRpcClient(BooleanResult(false)));
        await smokes["ZkVerifier.IsProofSystemConfigurationLocked.Sp1"].RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["ZkVerifier.GetLockedVerificationKey.Sp1"].RunAsync(new StubRpcClient(Hash256Result(programVKey)));
    }

    [TestMethod]
    public async Task BuildSmokeChecks_MismatchAborts()
    {
        var hashes = LeanContractHashes();
        var programVKey = new UInt256(AsymmetricProgramVKey);
        var smokes = LiveDeployCommand.BuildSmokeChecks(
            hashes,
            hashes["Owner"],
            hashes["Gas"],
            hashes["Owner"],
            100_000,
            programVKey,
            1001,
            FraudReplayDomain,
            GatewayProgramVKey,
            GatewayReplayDomain,
            2,
            2).ToDictionary(check => check.Name, StringComparer.Ordinal);

        var mismatches = new (string Name, System.Text.Json.JsonElement Result)[]
        {
            ("RollupHub.GetOwner", HashResult(UInt160.Zero)),
            ("ZkVerifier.IsVerificationKeyRegistered.Sp1", BooleanResult(false)),
            ("ZkVerifier.IsEnvelopeOnlyLocked.Sp1", BooleanResult(false)),
            ("ZkVerifier.IsEnvelopeOnlyAllowed.Sp1", BooleanResult(true)),
            ("ZkVerifier.IsProofSystemConfigurationLocked.Sp1", BooleanResult(false)),
            ("SharedBridge.GetSettlementManager", HashResult(UInt160.Zero)),
            ("RollupHub.GetSharedBridge", HashResult(UInt160.Zero)),
            ("RollupHub.GetGovernanceController", HashResult(UInt160.Zero)),
        };

        foreach (var (name, result) in mismatches)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                smokes[name].RunAsync(new StubRpcClient(result)),
                $"{name} mismatch must abort the deployment command");
        }
    }

    private static IReadOnlyDictionary<string, UInt160> LeanContractHashes()
    {
        string[] names =
        [
            "Owner", "Gas", "Sp1Groth16Verifier", "ZkVerifier",
            "GovernanceController", "RollupHub", "SharedBridge",
        ];

        var hashes = names.Select((name, index) => new
        {
            Name = name,
            Hash = new UInt160(Enumerable.Range(0, UInt160.Length)
                .Select(offset => (byte)(index * 17 + offset + 1))
                .ToArray()),
        }).ToDictionary(entry => entry.Name, entry => entry.Hash, StringComparer.Ordinal);
        hashes["Gas"] = LiveDeployCommand.NativeGasHash;
        return hashes;
    }

    private static IReadOnlyDictionary<string, UInt160> ContractHashes() => LeanContractHashes();

    private static JsonElement BooleanResult(bool value) => StjSerializer.SerializeToElement(new
    {
        state = "HALT",
        stack = new[] { new { type = "Boolean", value } },
    });

    private static JsonElement IntegerResult(long value) => StjSerializer.SerializeToElement(new
    {
        state = "HALT",
        stack = new[] { new { type = "Integer", value = value.ToString() } },
    });

    private static JsonElement HashResult(UInt160 value) => StjSerializer.SerializeToElement(new
    {
        state = "HALT",
        stack = new[]
        {
            new
            {
                type = "ByteString",
                value = Convert.ToBase64String(value.GetSpan()),
            },
        },
    });

    private static JsonElement Hash256Result(UInt256 value) => StjSerializer.SerializeToElement(new
    {
        state = "HALT",
        stack = new[]
        {
            new
            {
                type = "ByteString",
                value = Convert.ToBase64String(value.GetSpan()),
            },
        },
    });

    private static JsonElement ContractStateResult(
        UInt160 hash,
        int updateCounter,
        JsonNode nef,
        JsonNode manifest) => StjSerializer.SerializeToElement(new JsonObject
        {
            ["id"] = 1,
            ["updatecounter"] = updateCounter,
            ["hash"] = hash.ToString(),
            ["nef"] = nef,
            ["manifest"] = manifest,
        });

    private sealed class StubRpcClient(params JsonElement[] responses) : LiveDeployCommand.ILiveRpcClient
    {
        private readonly Queue<JsonElement> _responses = new(responses);

        public List<RpcCall> Calls { get; } = [];

        public Task<JsonElement> CallAsync(string method, params object?[] parameters)
        {
            Calls.Add(new RpcCall(method, parameters));
            if (_responses.Count == 0) throw new InvalidOperationException("No stub RPC response configured.");
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed record RpcCall(string Method, object?[] Parameters);
}
