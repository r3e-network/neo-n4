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
    public void SubstituteOperatorPlaceholders_ReplacesChainAndFraudDomains()
    {
        var key = new Neo.Wallets.KeyPair(new byte[32] { 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
        var owner = new UInt160(new byte[UInt160.Length]);
        var gas = new UInt160(Enumerable.Repeat((byte)2, UInt160.Length).ToArray());
        var emergencyCouncil = new UInt160(Enumerable.Repeat((byte)3, UInt160.Length).ToArray());

        var substituted = LiveDeployCommand.SubstituteOperatorPlaceholders(
            ScaffoldPlan.Default(), owner, gas, key.PublicKey, emergencyCouncil, 1001,
            FraudReplayDomain);
        var escrow = substituted.Steps.Single(s => s.Name == "ExternalBridgeEscrow");
        var emergency = substituted.Steps.Single(s => s.Name == "EmergencyManager");
        var restricted = substituted.Steps.Single(
            s => s.Name == "RestrictedExecutionFraudVerifier");

        Assert.IsInstanceOfType<Neo.Json.JNumber>(escrow.DeployData[2]);
        Assert.AreEqual(1001d, escrow.DeployData[2]!.AsNumber());
        Assert.AreEqual(emergencyCouncil.ToString(), emergency.DeployData[1]!.AsString());
        Assert.AreEqual(FraudReplayDomain.ToString(), restricted.DeployData[1]!.AsString());

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
                ScaffoldPlan.Default(), owner, gas, key.PublicKey, emergencyCouncil, 0,
                FraudReplayDomain));
        Assert.ThrowsExactly<ArgumentException>(() =>
            LiveDeployCommand.SubstituteOperatorPlaceholders(
                ScaffoldPlan.Default(), owner, gas, key.PublicKey, emergencyCouncil, 1001,
                UInt256.Zero));
    }

    [TestMethod]
    public void SubstituteOperatorPlaceholders_LegacyOnlyOptimisticPlan_IsRejected()
    {
        var key = new Neo.Wallets.KeyPair(new byte[32]
        {
            1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        });
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
                plan, owner, owner, key.PublicKey, owner, 1001, FraudReplayDomain));
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
    public void BuildPostDeployCalls_BindsAndLocksSp1ThenFreezesOuterRegistry()
    {
        var hashes = ContractHashes();
        var programVKey = new UInt256(AsymmetricProgramVKey);
        var actions = LiveDeployCommand.BuildPostDeployCalls(
            hashes,
            hashes["Gas"],
            hashes["Owner"],
            100_000,
            programVKey,
            1001,
            FraudReplayDomain).ToArray();
        Assert.IsTrue(actions.All(static action => action.CompletionCheck is not null),
            "every production post-deploy mutation must be resumable from its exact target state");

        var registerKey = IndexOf(actions, "ContractZkVerifier.RegisterVerificationKey.Sp1");
        var registerTerminal = IndexOf(actions, "ContractZkVerifier.RegisterProofVerifier.Sp1");
        var lockEnvelope = IndexOf(actions, "ContractZkVerifier.DisableEnvelopeOnlyPermanently.Sp1");
        var lockConfiguration = IndexOf(actions, "ContractZkVerifier.LockProofSystemConfiguration.Sp1");
        var registerOuterRoute = IndexOf(actions, "VerifierRegistry.RegisterVerifier.Zk");
        var lockOuterRegistry = IndexOf(actions, "VerifierRegistry.LockGovernance");

        Assert.AreEqual(registerKey + 1, registerTerminal);
        Assert.AreEqual(registerTerminal + 1, lockEnvelope);
        Assert.AreEqual(lockEnvelope + 1, lockConfiguration);
        Assert.AreEqual(lockConfiguration + 1, registerOuterRoute,
            "ProofType.Zk must not become reachable before the inner SP1 verifier is fully bound and locked");
        Assert.AreEqual(registerOuterRoute + 1, lockOuterRegistry,
            "production deployment must freeze the outer route immediately after bootstrap registration");

        var registerPermissionlessV4 = IndexOf(actions,
            "OptimisticChallenge.RegisterPermissionlessFraudProfile.RestrictedExecutionV4");
        Assert.AreEqual(lockOuterRegistry + 1, registerPermissionlessV4,
            "the exact executable v4 profile must be atomically approved immediately after the ZK route is frozen");

        var setGas = IndexOf(actions, "ForcedInclusion.SetGasToken");
        var setRecipient = IndexOf(actions, "ForcedInclusion.SetFeeRecipient");
        var setFee = IndexOf(actions, "ForcedInclusion.SetFee");
        Assert.AreEqual(setGas + 1, setRecipient);
        Assert.AreEqual(setRecipient + 1, setFee,
            "fee must only be enabled after both the token and recipient are configured");

        var action = actions[registerKey];
        using var expected = new ScriptBuilder();
        expected.EmitDynamicCall(
            hashes["ContractZkVerifier"],
            "registerVerificationKey",
            (byte)1,
            programVKey,
            true);
        CollectionAssert.AreEqual(expected.ToArray(), action.Script);
        Assert.IsTrue(action.Script.AsSpan().IndexOf(AsymmetricProgramVKey) >= 0,
            "registerVerificationKey script must push the raw bytes32_raw() bytes");
        Assert.AreEqual(-1, action.Script.AsSpan().IndexOf(AsymmetricProgramVKey.Reverse().ToArray()),
            "registerVerificationKey script must not contain the reversed display-order digest");
    }

    [TestMethod]
    public async Task IsPostDeployActionComplete_ExactStateSkipsMismatchRetries()
    {
        var hashes = ContractHashes();
        var programVKey = new UInt256(AsymmetricProgramVKey);
        var action = LiveDeployCommand.BuildPostDeployCalls(
                hashes,
                hashes["Gas"],
                hashes["Owner"],
                100_000,
                programVKey,
                1001,
                FraudReplayDomain)
            .Single(item => item.Name == "VerifierRegistry.LockGovernance");

        Assert.IsNotNull(action.CompletionCheck);
        Assert.IsTrue(await LiveDeployCommand.IsPostDeployActionCompleteAsync(
            action.CompletionCheck, new StubRpcClient(BooleanResult(true))));
        Assert.IsFalse(await LiveDeployCommand.IsPostDeployActionCompleteAsync(
            action.CompletionCheck, new StubRpcClient(BooleanResult(false))));
    }

    [TestMethod]
    public async Task BuildSmokeChecks_AllSp1PostconditionsPassAndQueryUsesRawVKeyBytes()
    {
        var hashes = ContractHashes();
        var programVKey = new UInt256(AsymmetricProgramVKey);
        var smokes = LiveDeployCommand.BuildSmokeChecks(
            hashes,
            hashes["Owner"],
            hashes["Gas"],
            hashes["Owner"],
            100_000,
            programVKey,
            1001,
            FraudReplayDomain).ToDictionary(check => check.Name, StringComparer.Ordinal);

        var verificationKeyRpc = new StubRpcClient(BooleanResult(true));
        await smokes["ContractZkVerifier.IsVerificationKeyRegistered.Sp1"].RunAsync(verificationKeyRpc);
        Assert.AreEqual("invokescript", verificationKeyRpc.Calls.Single().Method);
        var queryScript = Convert.FromBase64String((string)verificationKeyRpc.Calls.Single().Parameters[0]!);
        using (var expected = new ScriptBuilder())
        {
            expected.EmitDynamicCall(
                hashes["ContractZkVerifier"],
                "isVerificationKeyRegistered",
                CallFlags.ReadOnly,
                (byte)1,
                programVKey);
            CollectionAssert.AreEqual(expected.ToArray(), queryScript);
        }
        Assert.IsTrue(queryScript.AsSpan().IndexOf(AsymmetricProgramVKey) >= 0,
            "isVerificationKeyRegistered query must push the same raw program vkey bytes as registration and payload");
        Assert.AreEqual(-1, queryScript.AsSpan().IndexOf(AsymmetricProgramVKey.Reverse().ToArray()));

        await smokes["ContractZkVerifier.GetProofVerifier.Sp1"]
            .RunAsync(new StubRpcClient(HashResult(hashes["Sp1Groth16Verifier"])));
        await smokes["ContractZkVerifier.IsEnvelopeOnlyLocked.Sp1"]
            .RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["ContractZkVerifier.IsEnvelopeOnlyAllowed.Sp1"]
            .RunAsync(new StubRpcClient(BooleanResult(false)));
        await smokes["ContractZkVerifier.IsProofSystemConfigurationLocked.Sp1"]
            .RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["ContractZkVerifier.GetLockedVerificationKey.Sp1"]
            .RunAsync(new StubRpcClient(Hash256Result(programVKey)));
        await smokes["VerifierRegistry.GetVerifier.Zk"]
            .RunAsync(new StubRpcClient(HashResult(hashes["ContractZkVerifier"])));
        await smokes["VerifierRegistry.GetGovernanceController"]
            .RunAsync(new StubRpcClient(HashResult(hashes["GovernanceController"])));
        await smokes["VerifierRegistry.IsGovernanceLocked"]
            .RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["OptimisticChallenge.IsApprovedFraudVerifier.RestrictedExecutionV4"]
            .RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["OptimisticChallenge.IsPermissionlessFraudProfile.RestrictedExecutionV4"]
            .RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["RestrictedExecutionFraudVerifier.GetSettlementManager"]
            .RunAsync(new StubRpcClient(HashResult(hashes["SettlementManager"])));
        await smokes["RestrictedExecutionFraudVerifier.GetReplayDomain"]
            .RunAsync(new StubRpcClient(Hash256Result(FraudReplayDomain)));
        await smokes["RestrictedExecutionFraudVerifier.GetExecutorSemanticId"]
            .RunAsync(new StubRpcClient(Hash256Result(
                LiveDeployCommand.RestrictedExecutorSemanticId)));
        await smokes["ForcedInclusion.GetGasToken"]
            .RunAsync(new StubRpcClient(HashResult(hashes["Gas"])));
        await smokes["ForcedInclusion.GetFeeRecipient"]
            .RunAsync(new StubRpcClient(HashResult(hashes["Owner"])));
        await smokes["ForcedInclusion.GetFee"]
            .RunAsync(new StubRpcClient(IntegerResult(100_000)));
        await smokes["ForcedInclusion.IsProductionReady"]
            .RunAsync(new StubRpcClient(BooleanResult(true)));
        await smokes["ExternalBridgeEscrow.GetNeoChainId"]
            .RunAsync(new StubRpcClient(IntegerResult(1001)));
    }

    [TestMethod]
    public async Task BuildSmokeChecks_AnyIncorrectSp1Postcondition_FailsClosed()
    {
        var hashes = ContractHashes();
        var smokes = LiveDeployCommand.BuildSmokeChecks(
            hashes,
            hashes["Owner"],
            hashes["Gas"],
            hashes["Owner"],
            100_000,
            new UInt256(AsymmetricProgramVKey),
            1001,
            FraudReplayDomain).ToDictionary(check => check.Name, StringComparer.Ordinal);

        var mismatches = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["ContractZkVerifier.IsVerificationKeyRegistered.Sp1"] = BooleanResult(false),
            ["ContractZkVerifier.GetProofVerifier.Sp1"] = HashResult(UInt160.Zero),
            ["ContractZkVerifier.IsEnvelopeOnlyLocked.Sp1"] = BooleanResult(false),
            ["ContractZkVerifier.IsEnvelopeOnlyAllowed.Sp1"] = BooleanResult(true),
            ["ContractZkVerifier.IsProofSystemConfigurationLocked.Sp1"] = BooleanResult(false),
            ["ContractZkVerifier.GetLockedVerificationKey.Sp1"] = Hash256Result(UInt256.Zero),
            ["VerifierRegistry.GetVerifier.Zk"] = HashResult(UInt160.Zero),
            ["VerifierRegistry.GetGovernanceController"] = HashResult(UInt160.Zero),
            ["VerifierRegistry.IsGovernanceLocked"] = BooleanResult(false),
            ["OptimisticChallenge.IsApprovedFraudVerifier.RestrictedExecutionV4"] = BooleanResult(false),
            ["OptimisticChallenge.IsPermissionlessFraudProfile.RestrictedExecutionV4"] = BooleanResult(false),
            ["RestrictedExecutionFraudVerifier.GetSettlementManager"] = HashResult(UInt160.Zero),
            ["RestrictedExecutionFraudVerifier.GetReplayDomain"] = Hash256Result(UInt256.Zero),
            ["RestrictedExecutionFraudVerifier.GetExecutorSemanticId"] = Hash256Result(UInt256.Zero),
            ["ForcedInclusion.GetGasToken"] = HashResult(UInt160.Zero),
            ["ForcedInclusion.GetFeeRecipient"] = HashResult(UInt160.Zero),
            ["ForcedInclusion.GetFee"] = IntegerResult(0),
            ["ForcedInclusion.IsProductionReady"] = BooleanResult(false),
        };

        foreach (var (name, result) in mismatches)
        {
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
                smokes[name].RunAsync(new StubRpcClient(result)),
                $"{name} mismatch must abort the deployment command");
        }
    }

    private static int IndexOf(IReadOnlyList<LiveDeployCommand.PostDeployCall> actions, string name)
    {
        for (var index = 0; index < actions.Count; index++)
        {
            if (string.Equals(actions[index].Name, name, StringComparison.Ordinal)) return index;
        }
        Assert.Fail($"missing post-deploy action {name}");
        return -1;
    }

    private static IReadOnlyDictionary<string, UInt160> ContractHashes()
    {
        string[] names =
        [
            "Owner", "Gas", "SequencerBond", "OptimisticChallenge", "ForcedInclusion",
            "ChainRegistry", "SharedBridge", "EmergencyManager", "GovernanceController",
            "VerifierRegistry", "ContractZkVerifier", "Sp1Groth16Verifier",
            "RestrictedExecutionFraudVerifier",
            "MpcCommitteeVerifier", "ExternalBridgeRegistry", "ExternalBridgeBond",
            "ExternalBridgeEscrow", "MpcCommitteeFraudVerifier", "SettlementManager",
            "DARegistry", "DAValidator",
        ];

        return names.Select((name, index) => new
        {
            Name = name,
            Hash = new UInt160(Enumerable.Range(0, UInt160.Length)
                .Select(offset => (byte)(index * 17 + offset + 1))
                .ToArray()),
        }).ToDictionary(entry => entry.Name, entry => entry.Hash, StringComparer.Ordinal);
    }

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
