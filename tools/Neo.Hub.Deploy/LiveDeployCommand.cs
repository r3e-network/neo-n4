using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions.IO;
using Neo.Extensions.VM;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.Wallets;
using NeoECPoint = Neo.Cryptography.ECC.ECPoint;
using StjSerializer = System.Text.Json.JsonSerializer;

namespace Neo.Hub.Deploy;

/// <summary>
/// Live Neo N3 testnet deployment runner for the canonical NeoHub bundle.
/// </summary>
public static class LiveDeployCommand
{
    private const string DefaultRpc = "https://testnet1.neo.coz.io:443";
    private const string DefaultWifEnv = "NEO_N4_TESTNET_WIF";
    private const string DefaultGasHash = "0xd2a4cff31913016155e38e474a2c06d08be276cf";
    private const long MinimumNetworkFeeFallback = 10_00000000L / 10; // 0.1 GAS.
    private const uint ValidUntilDelta = 5750;

    /// <summary>Run the live deployment command.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var rpcUrl = ArgUtil.Get(args, "--rpc", DefaultRpc);
        var planPath = ArgUtil.Get(args, "--plan", "");
        var wifEnv = ArgUtil.Get(args, "--wif-env", DefaultWifEnv);
        var output = ArgUtil.Get(args, "--output", DefaultReportPath());
        var dryRun = ArgUtil.HasFlag(args, "--dry-run");
        var skipExisting = !ArgUtil.HasFlag(args, "--no-skip-existing");
        var runPostDeploy = !ArgUtil.HasFlag(args, "--no-postdeploy");
        var runSmoke = !ArgUtil.HasFlag(args, "--no-smoke");
        var maxSteps = ParseOptionalInt(ArgUtil.Get(args, "--max-steps", ""));

        var wif = Environment.GetEnvironmentVariable(wifEnv);
        if (string.IsNullOrWhiteSpace(wif))
        {
            Console.Error.WriteLine($"{wifEnv} is required; pass --wif-env <name> to use another environment variable.");
            return 1;
        }

        using var rpc = new LiveRpcClient(rpcUrl);
        var version = await rpc.CallAsync("getversion");
        var protocol = version.GetProperty("protocol");
        var network = protocol.GetProperty("network").GetUInt32();
        var addressVersion = protocol.GetProperty("addressversion").GetByte();

        var key = ImportWif(wif);
        var sender = Contract.CreateSignatureRedeemScript(key.PublicKey).ToScriptHash();
        var ownerAddress = sender.ToAddress(addressVersion);
        var gasHash = UInt160.Parse(ArgUtil.Get(args, "--gas-hash", DefaultGasHash));
        var baseDirectory = string.IsNullOrWhiteSpace(planPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(planPath)) ?? Directory.GetCurrentDirectory();
        var plan = LoadPlan(planPath);
        plan = SubstituteOperatorPlaceholders(plan, sender, gasHash, key.PublicKey);

        var predicted = PredictContractHashes(plan, sender, baseDirectory);
        var bundle = DeployPlanner.Plan(plan, name => predicted[name]);
        var records = new List<Dictionary<string, object?>>();
        WriteReport(output, rpcUrl, network, ownerAddress, sender, dryRun, records);

        Console.WriteLine($"NeoHub live deployment");
        Console.WriteLine($"  rpc: {rpcUrl}");
        Console.WriteLine($"  network: {network}");
        Console.WriteLine($"  signer: {ownerAddress} ({sender})");
        Console.WriteLine($"  dryRun: {dryRun}");
        Console.WriteLine($"  steps: {bundle.Invocations.Count}");
        Console.WriteLine();

        var deployed = 0;
        foreach (var invocation in bundle.Invocations)
        {
            if (maxSteps is not null && deployed >= maxSteps.Value) break;
            deployed++;

            var hash = predicted[invocation.Name];
            var nefPath = ResolvePath(baseDirectory, invocation.NefPath);
            var manifestPath = ResolvePath(baseDirectory, invocation.ManifestPath);

            if (skipExisting && await ContractExistsAsync(rpc, hash))
            {
                Console.WriteLine($"[{deployed}/{bundle.Invocations.Count}] reuse {invocation.Name} {hash}");
                records.Add(Record("deploy", invocation.Name, "reused", hash, txHash: null, null, null, null));
                WriteReport(output, rpcUrl, network, ownerAddress, sender, dryRun, records);
                continue;
            }

            var nefBytes = await File.ReadAllBytesAsync(nefPath);
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
            var deployData = BuildDeployData(invocation.ResolvedDeployData);
            using var sb = new ScriptBuilder();
            sb.EmitDynamicCall(NativeContract.ContractManagement.Hash, "deploy", nefBytes, manifestBytes, deployData);
            var script = sb.ToArray();

            var tx = await BuildSignedTransactionAsync(rpc, script, sender, key, network, "deploy", invocation.Name);
            var txHash = tx.Hash.ToString();
            Console.WriteLine($"[{deployed}/{bundle.Invocations.Count}] deploy {invocation.Name} {hash}");
            Console.WriteLine($"  tx: {txHash}");

            if (!dryRun)
            {
                await SendTransactionAsync(rpc, tx);
                var execution = await WaitForApplicationLogAsync(rpc, tx.Hash);
                if (execution.VmState != "HALT")
                    throw new InvalidOperationException($"{invocation.Name} deploy failed: {execution.Exception}");
                if (!await ContractExistsAsync(rpc, hash))
                    throw new InvalidOperationException($"{invocation.Name} tx halted but contract state not found at {hash}");
            }

            records.Add(Record("deploy", invocation.Name, dryRun ? "dry-run" : "deployed", hash, txHash, tx.SystemFee, tx.NetworkFee, "HALT"));
            WriteReport(output, rpcUrl, network, ownerAddress, sender, dryRun, records);
        }

        if (!dryRun && runPostDeploy)
        {
            foreach (var action in BuildPostDeployCalls(predicted))
            {
                var tx = await BuildSignedTransactionAsync(rpc, action.Script, sender, key, network, "postdeploy", action.Name);
                Console.WriteLine($"postdeploy {action.Name}");
                Console.WriteLine($"  tx: {tx.Hash}");
                await SendTransactionAsync(rpc, tx);
                var execution = await WaitForApplicationLogAsync(rpc, tx.Hash);
                if (execution.VmState != "HALT")
                    throw new InvalidOperationException($"{action.Name} failed: {execution.Exception}");
                records.Add(Record("postdeploy", action.Name, "executed", action.Contract, tx.Hash.ToString(), tx.SystemFee, tx.NetworkFee, execution.VmState));
                WriteReport(output, rpcUrl, network, ownerAddress, sender, dryRun, records);
            }
        }

        if (!dryRun && runSmoke)
        {
            foreach (var smoke in BuildSmokeChecks(predicted, sender, gasHash))
            {
                await smoke.RunAsync(rpc);
                Console.WriteLine($"smoke {smoke.Name}: ok");
                records.Add(Record("smoke", smoke.Name, "ok", smoke.Contract, txHash: null, null, null, "HALT"));
                WriteReport(output, rpcUrl, network, ownerAddress, sender, dryRun, records);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Deployment evidence written to {output}");
        Console.WriteLine("Skipped operator-specific actions: ContractZkVerifier.RegisterVerificationKey/RegisterProofVerifier, external committee registration, and per-chain L1TxFilter wiring.");
        return 0;
    }

    private static DeployPlan LoadPlan(string planPath)
    {
        if (string.IsNullOrWhiteSpace(planPath)) return ScaffoldPlan.Default();
        if (!File.Exists(planPath)) throw new FileNotFoundException("plan file not found", planPath);
        return DeployPlan.FromJson(File.ReadAllText(planPath));
    }

    private static DeployPlan SubstituteOperatorPlaceholders(DeployPlan plan, UInt160 owner, UInt160 gasHash, NeoECPoint publicKey)
    {
        return new DeployPlan
        {
            Version = plan.Version,
            Network = plan.Network,
            Steps = plan.Steps.Select(s => s with
            {
                DeployData = (JArray)SubstituteToken(s.DeployData, owner, gasHash, publicKey)!,
            }).ToArray(),
        };
    }

    private static JToken? SubstituteToken(JToken? token, UInt160 owner, UInt160 gasHash, NeoECPoint publicKey)
    {
        if (token is null) return null;
        if (token is JString str)
        {
            return str.AsString() switch
            {
                "OWNER_REPLACE_ME" => new JString(owner.ToString()),
                "BOND_ASSET_REPLACE_ME" => new JString(gasHash.ToString()),
                "GOVERNANCE_COUNCIL_MEMBER_REPLACE_ME" => new JString(publicKey.ToString()),
                _ => new JString(str.AsString()),
            };
        }
        if (token is JArray arr)
        {
            var copy = new JArray();
            foreach (var child in arr) copy.Add(SubstituteToken(child, owner, gasHash, publicKey));
            return copy;
        }
        if (token is JObject obj)
        {
            var copy = new JObject();
            foreach (var (k, v) in obj.Properties) copy[k] = SubstituteToken(v, owner, gasHash, publicKey);
            return copy;
        }
        return token;
    }

    private static IReadOnlyDictionary<string, UInt160> PredictContractHashes(DeployPlan plan, UInt160 sender, string baseDirectory)
    {
        var hashes = new Dictionary<string, UInt160>(StringComparer.Ordinal);
        foreach (var step in plan.Steps)
        {
            var nefPath = ResolvePath(baseDirectory, step.NefPath);
            var manifestPath = ResolvePath(baseDirectory, step.ManifestPath);
            var nef = NefFile.Parse(File.ReadAllBytes(nefPath));
            var manifest = ContractManifest.Parse(File.ReadAllText(manifestPath));
            hashes[step.Name] = Neo.SmartContract.Helper.GetContractHash(sender, nef.CheckSum, manifest.Name);
        }
        return hashes;
    }

    private static ContractParameter BuildDeployData(JArray data)
    {
        return data.Count switch
        {
            0 => new ContractParameter(ContractParameterType.Any) { Value = null },
            1 => ParameterFromToken(data[0]),
            _ => ParameterFromArray(data),
        };
    }

    private static ContractParameter ParameterFromToken(JToken? token)
    {
        if (token is null) return new ContractParameter(ContractParameterType.Any) { Value = null };
        if (token is JString str) return ParameterFromString(str.AsString());
        if (token is JNumber number)
            return new ContractParameter(ContractParameterType.Integer)
            {
                Value = new System.Numerics.BigInteger(number.AsNumber()),
            };
        if (token is JBoolean boolean)
            return new ContractParameter(ContractParameterType.Boolean) { Value = boolean.AsBoolean() };
        if (token is JArray arr) return ParameterFromArray(arr);
        throw new ArgumentException($"unsupported deploy-data token: {token.GetType().Name}");
    }

    private static ContractParameter ParameterFromArray(JArray arr)
    {
        return new ContractParameter(ContractParameterType.Array)
        {
            Value = arr.Select(ParameterFromToken).ToList(),
        };
    }

    private static ContractParameter ParameterFromString(string value)
    {
        if (UInt160.TryParse(value, out var hash))
            return new ContractParameter(ContractParameterType.Hash160) { Value = hash };
        if (LooksLikeCompressedPublicKey(value))
            return new ContractParameter(ContractParameterType.PublicKey)
            {
                Value = NeoECPoint.Parse(value, ECCurve.Secp256r1),
            };
        return new ContractParameter(ContractParameterType.String) { Value = value };
    }

    private static bool LooksLikeCompressedPublicKey(string value)
    {
        if (value.Length != 66 || value[0] != '0' || value[1] is not ('2' or '3')) return false;
        return value.All(Uri.IsHexDigit);
    }

    private static async Task<Transaction> BuildSignedTransactionAsync(
        LiveRpcClient rpc,
        byte[] script,
        UInt160 sender,
        KeyPair key,
        uint network,
        string category,
        string name)
    {
        var invoke = await rpc.CallAsync("invokescript", Convert.ToBase64String(script), SignerJson(sender));
        var state = invoke.GetProperty("state").GetString();
        if (state != "HALT")
        {
            var exception = invoke.TryGetProperty("exception", out var ex) ? ex.GetString() : "unknown";
            throw new InvalidOperationException($"{category} {name} test invoke failed: {exception}");
        }

        var blockCount = (uint)(await rpc.CallAsync("getblockcount")).GetInt32();
        var tx = new Transaction
        {
            Version = 0,
            Nonce = RandomNonce(),
            SystemFee = AddGasMargin(ParseGas(invoke.GetProperty("gasconsumed").GetString()!)),
            NetworkFee = 0,
            ValidUntilBlock = blockCount + ValidUntilDelta,
            Signers =
            [
                new Signer
                {
                    Account = sender,
                    Scopes = WitnessScope.Global,
                },
            ],
            Attributes = [],
            Script = script,
            Witnesses =
            [
                SignatureWitness(new byte[64], key),
            ],
        };

        tx.NetworkFee = await CalculateNetworkFeeAsync(rpc, tx);
        tx.Witnesses = [SignatureWitness(tx.Sign(key, network), key)];
        return tx;
    }

    private static async Task<long> CalculateNetworkFeeAsync(LiveRpcClient rpc, Transaction tx)
    {
        try
        {
            var result = await rpc.CallAsync("calculatenetworkfee", Convert.ToBase64String(tx.ToArray()));
            var fee = ParseGas(result.GetProperty("networkfee").GetString()!);
            return AddGasMargin(fee);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"warning: calculatenetworkfee failed, using fallback 0.1 GAS: {ex.Message}");
            return MinimumNetworkFeeFallback;
        }
    }

    private static async Task SendTransactionAsync(LiveRpcClient rpc, Transaction tx)
    {
        var result = await rpc.CallAsync("sendrawtransaction", Convert.ToBase64String(tx.ToArray()));
        if (result.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException($"sendrawtransaction returned false for {tx.Hash}");
    }

    private static async Task<bool> ContractExistsAsync(LiveRpcClient rpc, UInt160 hash)
    {
        try
        {
            _ = await rpc.CallAsync("getcontractstate", hash.ToString());
            return true;
        }
        catch (LiveRpcException ex) when (
            ex.Message.Contains("Unknown contract", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || ex.Code == -100)
        {
            return false;
        }
    }

    private static async Task<ExecutionResult> WaitForApplicationLogAsync(LiveRpcClient rpc, UInt256 txHash)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            try
            {
                var log = await rpc.CallAsync("getapplicationlog", txHash.ToString());
                var execution = log.GetProperty("executions")[0];
                return new ExecutionResult(
                    execution.GetProperty("vmstate").GetString() ?? "",
                    execution.TryGetProperty("exception", out var ex) ? ex.GetString() : null);
            }
            catch (LiveRpcException ex) when (
                ex.Message.Contains("Unknown transaction", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
                || ex.Code == -100
                || ex.Code == -105)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
        }
        throw new TimeoutException($"timed out waiting for application log for {txHash}");
    }

    private static IReadOnlyList<PostDeployCall> BuildPostDeployCalls(IReadOnlyDictionary<string, UInt160> h)
    {
        return
        [
            Call("SequencerBond.RegisterSlasher", h["SequencerBond"], "registerSlasher", h["OptimisticChallenge"]),
            Call("SequencerBond.RegisterSlasher.ForcedInclusion", h["SequencerBond"], "registerSlasher", h["ForcedInclusion"]),
            Call("ChainRegistry.RegisterPauser.ForcedInclusion", h["ChainRegistry"], "registerPauser", h["ForcedInclusion"]),
            Call("ForcedInclusion.SetChainRegistry", h["ForcedInclusion"], "setChainRegistry", h["ChainRegistry"]),
            Call("ForcedInclusion.SetSequencerBond", h["ForcedInclusion"], "setSequencerBond", h["SequencerBond"]),
            Call("ForcedInclusion.SetCensorshipSlashAmount", h["ForcedInclusion"], "setCensorshipSlashAmount", 1_000_000L),
            Call("SharedBridge.SetEmergencyManager", h["SharedBridge"], "setEmergencyManager", h["EmergencyManager"]),
            Call("ChainRegistry.SetGovernanceController", h["ChainRegistry"], "setGovernanceController", h["GovernanceController"]),
            Call("VerifierRegistry.SetGovernanceController", h["VerifierRegistry"], "setGovernanceController", h["GovernanceController"]),
            Call("VerifierRegistry.RegisterVerifier.Zk", h["VerifierRegistry"], "registerVerifier", (byte)3, h["ContractZkVerifier"]),
            Call("OptimisticChallenge.RegisterFraudVerifier.Governance", h["OptimisticChallenge"], "registerFraudVerifier", h["GovernanceFraudVerifier"]),
            Call("OptimisticChallenge.RegisterFraudVerifier.RestrictedExecution", h["OptimisticChallenge"], "registerFraudVerifier", h["RestrictedExecutionFraudVerifier"]),
            Call("MpcCommitteeVerifier.SetGovernanceController", h["MpcCommitteeVerifier"], "setGovernanceController", h["GovernanceController"]),
            Call("ExternalBridgeRegistry.SetGovernanceController", h["ExternalBridgeRegistry"], "setGovernanceController", h["GovernanceController"]),
            Call("ExternalBridgeBond.RegisterSlasher", h["ExternalBridgeBond"], "registerSlasher", h["MpcCommitteeFraudVerifier"]),
            Call("SettlementManager.SetDARegistry", h["SettlementManager"], "setDARegistry", h["DARegistry"]),
            Call("SettlementManager.SetDAValidator", h["SettlementManager"], "setDAValidator", h["DAValidator"]),
            Call("SettlementManager.SetOptimisticChallenge", h["SettlementManager"], "setOptimisticChallenge", h["OptimisticChallenge"]),
        ];
    }

    private static PostDeployCall Call(string name, UInt160 contract, string method, params object[] args)
    {
        using var sb = new ScriptBuilder();
        sb.EmitDynamicCall(contract, method, args);
        return new PostDeployCall(name, contract, sb.ToArray());
    }

    private static IReadOnlyList<SmokeCheck> BuildSmokeChecks(IReadOnlyDictionary<string, UInt160> h, UInt160 owner, UInt160 gasHash)
    {
        return
        [
            HashCheck("ChainRegistry.GetOwner", h["ChainRegistry"], "getOwner", owner),
            IntegerCheck("GovernanceController.GetCouncilCount", h["GovernanceController"], "getCouncilCount", 1),
            IntegerCheck("GovernanceController.GetThreshold", h["GovernanceController"], "getThreshold", 1),
            HashCheck("SequencerBond.GetBondAsset", h["SequencerBond"], "getBondAsset", gasHash),
            HashCheck("ExternalBridgeBond.GetBondAsset", h["ExternalBridgeBond"], "getBondAsset", gasHash),
            HashCheck("SettlementManager.GetDARegistry", h["SettlementManager"], "getDARegistry", h["DARegistry"]),
            HashCheck("SettlementManager.GetDAValidator", h["SettlementManager"], "getDAValidator", h["DAValidator"]),
            HashCheck("SettlementManager.GetOptimisticChallenge", h["SettlementManager"], "getOptimisticChallenge", h["OptimisticChallenge"]),
            HashCheck("ForcedInclusion.GetChainRegistry", h["ForcedInclusion"], "getChainRegistry", h["ChainRegistry"]),
            HashCheck("ForcedInclusion.GetSequencerBond", h["ForcedInclusion"], "getSequencerBond", h["SequencerBond"]),
            IntegerCheck("ForcedInclusion.GetCensorshipSlashAmount", h["ForcedInclusion"], "getCensorshipSlashAmount", 1_000_000),
            HashCheck("SharedBridge.GetEmergencyManager", h["SharedBridge"], "getEmergencyManager", h["EmergencyManager"]),
            HashCheck("VerifierRegistry.GetVerifier.Zk", h["VerifierRegistry"], "getVerifier", h["ContractZkVerifier"], (byte)3),
            BoolCheck("SequencerBond.IsSlasher", h["SequencerBond"], "isSlasher", true, h["OptimisticChallenge"]),
            BoolCheck("SequencerBond.IsSlasher.ForcedInclusion", h["SequencerBond"], "isSlasher", true, h["ForcedInclusion"]),
            BoolCheck("ChainRegistry.IsPauser.ForcedInclusion", h["ChainRegistry"], "isPauser", true, h["ForcedInclusion"]),
            BoolCheck("ExternalBridgeBond.IsSlasher", h["ExternalBridgeBond"], "isSlasher", true, h["MpcCommitteeFraudVerifier"]),
        ];
    }

    private static SmokeCheck HashCheck(string name, UInt160 contract, string method, UInt160 expected, params object[] args)
    {
        return new SmokeCheck(name, contract, async rpc =>
        {
            var result = await InvokeReadAsync(rpc, contract, method, args);
            var actual = StackValue(result);
            if (!string.Equals(actual, expected.ToString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
        });
    }

    private static SmokeCheck BoolCheck(string name, UInt160 contract, string method, bool expected, params object[] args)
    {
        return new SmokeCheck(name, contract, async rpc =>
        {
            var result = await InvokeReadAsync(rpc, contract, method, args);
            var actual = bool.Parse(StackValue(result));
            if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
        });
    }

    private static SmokeCheck IntegerCheck(string name, UInt160 contract, string method, long expected, params object[] args)
    {
        return new SmokeCheck(name, contract, async rpc =>
        {
            var result = await InvokeReadAsync(rpc, contract, method, args);
            var actual = long.Parse(StackValue(result), CultureInfo.InvariantCulture);
            if (actual != expected) throw new InvalidOperationException($"{name}: expected {expected}, got {actual}");
        });
    }

    private static async Task<JsonElement> InvokeReadAsync(LiveRpcClient rpc, UInt160 contract, string method, object[] args)
    {
        using var sb = new ScriptBuilder();
        sb.EmitDynamicCall(contract, method, CallFlags.ReadOnly, args);
        var result = await rpc.CallAsync("invokescript", Convert.ToBase64String(sb.ToArray()));
        if (result.GetProperty("state").GetString() != "HALT")
        {
            var exception = result.TryGetProperty("exception", out var ex) ? ex.GetString() : "unknown";
            throw new InvalidOperationException($"{method} read failed: {exception}");
        }
        return result;
    }

    private static string StackValue(JsonElement invokeResult)
    {
        var item = invokeResult.GetProperty("stack")[0];
        var type = item.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        if (item.TryGetProperty("value", out var v))
        {
            if (type == "ByteString" && v.ValueKind == JsonValueKind.String)
            {
                var bytes = Convert.FromBase64String(v.GetString() ?? "");
                if (bytes.Length == UInt160.Length) return new UInt160(bytes).ToString();
                return Convert.ToHexString(bytes).ToLowerInvariant();
            }
            if (type == "Boolean" && v.ValueKind is JsonValueKind.True or JsonValueKind.False)
                return v.GetBoolean().ToString(CultureInfo.InvariantCulture);
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            return v.GetRawText().Trim('"');
        }
        return item.GetRawText();
    }

    private static object[] SignerJson(UInt160 sender)
    {
        return
        [
            new Dictionary<string, object?>
            {
                ["account"] = sender.ToString(),
                ["scopes"] = "Global",
            },
        ];
    }

    private static Witness SignatureWitness(byte[] signature, KeyPair key)
    {
        using var sb = new ScriptBuilder();
        sb.EmitPush(signature);
        return new Witness
        {
            InvocationScript = sb.ToArray(),
            VerificationScript = Contract.CreateSignatureRedeemScript(key.PublicKey),
        };
    }

    private static KeyPair ImportWif(string wif)
    {
        var payload = wif.Base58CheckDecode();
        try
        {
            if (payload.Length != 34 || payload[0] != 0x80 || payload[33] != 0x01)
                throw new FormatException("WIF payload is not a compressed Neo private key.");
            return new KeyPair(payload[1..33]);
        }
        finally
        {
            Array.Clear(payload);
        }
    }

    private static long ParseGas(string value)
    {
        if (value.Contains('.', StringComparison.Ordinal))
        {
            return checked((long)(decimal.Parse(value, CultureInfo.InvariantCulture) * 100_00000000m));
        }
        return long.Parse(value, CultureInfo.InvariantCulture);
    }

    private static long AddGasMargin(long value)
    {
        return checked(value + Math.Max(value / 5, 100_000L));
    }

    private static uint RandomNonce()
    {
        Span<byte> bytes = stackalloc byte[4];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt32(bytes);
    }

    private static string ResolvePath(string baseDirectory, string path)
    {
        return Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(baseDirectory, path));
    }

    private static int? ParseOptionalInt(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : int.Parse(value, CultureInfo.InvariantCulture);
    }

    private static string DefaultReportPath()
    {
        return Path.Combine("docs", "audit", $"testnet-deployment-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.json");
    }

    private static Dictionary<string, object?> Record(
        string category,
        string name,
        string status,
        UInt160 contractHash,
        string? txHash,
        long? systemFee,
        long? networkFee,
        string? vmState)
    {
        return new Dictionary<string, object?>
        {
            ["category"] = category,
            ["name"] = name,
            ["status"] = status,
            ["contractHash"] = contractHash.ToString(),
            ["txHash"] = txHash,
            ["systemFeeDatoshi"] = systemFee,
            ["networkFeeDatoshi"] = networkFee,
            ["vmState"] = vmState,
            ["utc"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
        };
    }

    private static void WriteReport(
        string output,
        string rpcUrl,
        uint network,
        string ownerAddress,
        UInt160 ownerScriptHash,
        bool dryRun,
        IReadOnlyList<Dictionary<string, object?>> records)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(output));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var report = new Dictionary<string, object?>
        {
            ["rpc"] = rpcUrl,
            ["network"] = network,
            ["ownerAddress"] = ownerAddress,
            ["ownerScriptHash"] = ownerScriptHash.ToString(),
            ["dryRun"] = dryRun,
            ["records"] = records,
        };
        File.WriteAllText(output, StjSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    }

    private sealed record ExecutionResult(string VmState, string? Exception);

    private sealed record PostDeployCall(string Name, UInt160 Contract, byte[] Script);

    private sealed record SmokeCheck(string Name, UInt160 Contract, Func<LiveRpcClient, Task> RunAsync);

    private sealed class LiveRpcClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly string _endpoint;
        private long _nextId;

        public LiveRpcClient(string endpoint)
        {
            _endpoint = endpoint;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        }

        public async Task<JsonElement> CallAsync(string method, params object?[] parameters)
        {
            var id = Interlocked.Increment(ref _nextId);
            var envelope = StjSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                method,
                @params = parameters,
                id,
            });
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = new StringContent(envelope, Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            using var response = await _http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new LiveRpcException(-32603, $"HTTP {(int)response.StatusCode}: {body}");

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("error", out var error))
            {
                var code = error.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var c)
                    ? c
                    : -32603;
                var message = error.TryGetProperty("message", out var messageElement)
                    ? messageElement.GetString() ?? error.GetRawText()
                    : error.GetRawText();
                throw new LiveRpcException(code, message);
            }
            return root.GetProperty("result").Clone();
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }

    private sealed class LiveRpcException : Exception
    {
        public int Code { get; }

        public LiveRpcException(int code, string message) : base($"jsonrpc {code}: {message}")
        {
            Code = code;
        }
    }
}
