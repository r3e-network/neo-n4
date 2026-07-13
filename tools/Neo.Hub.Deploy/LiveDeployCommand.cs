using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Neo.Hub.Deploy.UnitTests")]

namespace Neo.Hub.Deploy;

/// <summary>
/// Live Neo N3 testnet deployment runner for the canonical NeoHub bundle.
/// </summary>
public static class LiveDeployCommand
{
    private const string DefaultRpc = "https://testnet1.neo.coz.io:443";
    private const string DefaultWifEnv = "NEO_N4_TESTNET_WIF";
    private const string DefaultGasHash = "0xd2a4cff31913016155e38e474a2c06d08be276cf";
    private const long DefaultForcedInclusionFee = 100_000L; // 0.001 GAS.
    private const byte ProofSystemSp1 = 1;
    private const byte ProofTypeZk = 3;
    private const long MinimumNetworkFeeFallback = 10_00000000L / 10; // 0.1 GAS.
    private const uint ValidUntilDelta = 100;

    /// <summary>Run the live deployment command.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var rpcEndpoint = ParseAndValidateRpcEndpoint(ArgUtil.Get(args, "--rpc", DefaultRpc));
        var rpcUrl = rpcEndpoint.AbsoluteUri;
        var reportedRpc = RedactRpcEndpoint(rpcEndpoint);
        var planPath = ArgUtil.Get(args, "--plan", "");
        var wifEnv = ArgUtil.Get(args, "--wif-env", DefaultWifEnv);
        var output = ArgUtil.Get(args, "--output", DefaultReportPath());
        var dryRun = ArgUtil.HasFlag(args, "--dry-run");
        var skipExisting = !ArgUtil.HasFlag(args, "--no-skip-existing");
        var runPostDeploy = !ArgUtil.HasFlag(args, "--no-postdeploy");
        var runSmoke = !ArgUtil.HasFlag(args, "--no-smoke");
        var maxSteps = ParseOptionalInt(ArgUtil.Get(args, "--max-steps", ""));
        var sp1ProgramVKey = ParseSp1ProgramVKey(ArgUtil.Get(args, "--sp1-program-vkey", ""));
        var l2ChainId = ParseRequiredL2ChainId(ArgUtil.Get(args, "--l2-chain-id", ""));
        var expectedNetwork = ParseRequiredNetwork(ArgUtil.Get(args, "--expected-network", ""));
        var forcedInclusionFee = ParsePositiveForcedInclusionFee(
            ArgUtil.Get(args, "--forced-inclusion-fee", DefaultForcedInclusionFee.ToString(CultureInfo.InvariantCulture)));

        ValidateProductionSafetyOptions(dryRun, runPostDeploy, runSmoke, maxSteps);

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
        if (network != expectedNetwork)
            throw new InvalidOperationException(
                $"RPC network mismatch: expected {expectedNetwork}, endpoint reports {network}");

        var key = ImportWif(wif);
        var sender = Contract.CreateSignatureRedeemScript(key.PublicKey).ToScriptHash();
        var emergencyCouncil = ParseRequiredAccount(
            ArgUtil.Get(args, "--emergency-council", ""), addressVersion, "--emergency-council");
        var forcedInclusionFeeRecipient = ParseRequiredAccount(
            ArgUtil.Get(args, "--forced-inclusion-fee-recipient", sender.ToString()),
            addressVersion,
            "--forced-inclusion-fee-recipient");
        var ownerAddress = sender.ToAddress(addressVersion);
        var gasHash = UInt160.Parse(ArgUtil.Get(args, "--gas-hash", DefaultGasHash));
        var baseDirectory = string.IsNullOrWhiteSpace(planPath)
            ? Directory.GetCurrentDirectory()
            : Path.GetDirectoryName(Path.GetFullPath(planPath)) ?? Directory.GetCurrentDirectory();
        var plan = LoadPlan(planPath);
        plan = SubstituteOperatorPlaceholders(
            plan, sender, gasHash, key.PublicKey, emergencyCouncil, l2ChainId);

        var predicted = PredictContractHashes(plan, sender, baseDirectory);
        var bundle = DeployPlanner.Plan(plan, name => predicted[name]);
        var records = new List<Dictionary<string, object?>>();
        WriteReport(output, reportedRpc, network, ownerAddress, sender, sp1ProgramVKey, dryRun, records);

        Console.WriteLine($"NeoHub live deployment");
        Console.WriteLine($"  rpc: {reportedRpc}");
        Console.WriteLine($"  network: {network}");
        Console.WriteLine($"  signer: {ownerAddress} ({sender})");
        Console.WriteLine($"  external bridge L2 domain: {l2ChainId}");
        Console.WriteLine($"  forced inclusion fee: {forcedInclusionFee} ({forcedInclusionFeeRecipient})");
        Console.WriteLine($"  dryRun: {dryRun}");
        Console.WriteLine($"  steps: {bundle.Invocations.Count}");
        Console.WriteLine($"  SP1 program vkey (raw): 0x{Convert.ToHexString(sp1ProgramVKey.GetSpan()).ToLowerInvariant()}");
        Console.WriteLine();

        var deployed = 0;
        foreach (var invocation in bundle.Invocations)
        {
            if (maxSteps is not null && deployed >= maxSteps.Value) break;
            deployed++;

            var hash = predicted[invocation.Name];
            var nefPath = ResolvePath(baseDirectory, invocation.NefPath);
            var manifestPath = ResolvePath(baseDirectory, invocation.ManifestPath);
            var nefBytes = await File.ReadAllBytesAsync(nefPath);
            var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
            var manifestJson = Encoding.UTF8.GetString(manifestBytes);
            var existingState = await TryGetContractStateAsync(rpc, hash);

            if (skipExisting && existingState is not null)
            {
                ValidateRemoteContractState(invocation.Name, hash, nefBytes, manifestJson, existingState.Value);
                Console.WriteLine($"[{deployed}/{bundle.Invocations.Count}] reuse {invocation.Name} {hash}");
                records.Add(Record("deploy", invocation.Name, "reused", hash, txHash: null, null, null, null));
                WriteReport(output, reportedRpc, network, ownerAddress, sender, sp1ProgramVKey, dryRun, records);
                continue;
            }

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
                var deployedState = await TryGetContractStateAsync(rpc, hash);
                if (deployedState is null)
                    throw new InvalidOperationException($"{invocation.Name} tx halted but contract state not found at {hash}");
                ValidateRemoteContractState(invocation.Name, hash, nefBytes, manifestJson, deployedState.Value);
            }

            records.Add(Record("deploy", invocation.Name, dryRun ? "dry-run" : "deployed", hash, txHash, tx.SystemFee, tx.NetworkFee, "HALT"));
            WriteReport(output, reportedRpc, network, ownerAddress, sender, sp1ProgramVKey, dryRun, records);
        }

        if (!dryRun && runPostDeploy)
        {
            foreach (var action in BuildPostDeployCalls(
                predicted,
                gasHash,
                forcedInclusionFeeRecipient,
                forcedInclusionFee,
                sp1ProgramVKey))
            {
                if (action.CompletionCheck is not null &&
                    await IsPostDeployActionCompleteAsync(action.CompletionCheck, rpc))
                {
                    Console.WriteLine($"postdeploy {action.Name}: already satisfied");
                    records.Add(Record("postdeploy", action.Name, "reused", action.Contract,
                        txHash: null, null, null, "HALT"));
                    WriteReport(output, reportedRpc, network, ownerAddress, sender, sp1ProgramVKey, dryRun, records);
                    continue;
                }

                var tx = await BuildSignedTransactionAsync(rpc, action.Script, sender, key, network, "postdeploy", action.Name);
                Console.WriteLine($"postdeploy {action.Name}");
                Console.WriteLine($"  tx: {tx.Hash}");
                await SendTransactionAsync(rpc, tx);
                var execution = await WaitForApplicationLogAsync(rpc, tx.Hash);
                if (execution.VmState != "HALT")
                    throw new InvalidOperationException($"{action.Name} failed: {execution.Exception}");
                records.Add(Record("postdeploy", action.Name, "executed", action.Contract, tx.Hash.ToString(), tx.SystemFee, tx.NetworkFee, execution.VmState));
                WriteReport(output, reportedRpc, network, ownerAddress, sender, sp1ProgramVKey, dryRun, records);
            }
        }

        if (!dryRun && runSmoke)
        {
            foreach (var smoke in BuildSmokeChecks(
                predicted,
                sender,
                gasHash,
                forcedInclusionFeeRecipient,
                forcedInclusionFee,
                sp1ProgramVKey,
                l2ChainId))
            {
                await smoke.RunAsync(rpc);
                Console.WriteLine($"smoke {smoke.Name}: ok");
                records.Add(Record("smoke", smoke.Name, "ok", smoke.Contract, txHash: null, null, null, "HALT"));
                WriteReport(output, reportedRpc, network, ownerAddress, sender, sp1ProgramVKey, dryRun, records);
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Deployment evidence written to {output}");
        Console.WriteLine("Skipped operator-specific actions: external committee registration and per-chain L1TxFilter wiring.");
        return 0;
    }

    internal static UInt256 ParseSp1ProgramVKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "--sp1-program-vkey is required and must be an exact 32-byte prover .proof.vk file or 64 hexadecimal raw bytes.",
                nameof(value));

        byte[] raw;
        if (File.Exists(value))
        {
            raw = File.ReadAllBytes(value);
        }
        else
        {
            var hex = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
            if (hex.Length != UInt256.Length * 2 || !hex.All(Uri.IsHexDigit))
                throw new FormatException(
                    "--sp1-program-vkey must be an existing 32-byte prover .proof.vk file or exactly 64 hexadecimal raw bytes (optional 0x prefix).");
            raw = Convert.FromHexString(hex);
        }

        if (raw.Length != UInt256.Length)
            throw new FormatException(
                $"--sp1-program-vkey must contain exactly {UInt256.Length} raw bytes; got {raw.Length}.");
        if (raw.All(static value => value == 0))
            throw new FormatException("--sp1-program-vkey must not be the all-zero verification key.");
        if (raw[0] != 0)
            throw new FormatException(
                "--sp1-program-vkey must be the canonical SP1 bytes32_raw() encoding with a zero leading byte.");

        // SP1 bytes32_raw() is already the canonical big-endian 32-byte digest.
        // UInt256 is used only as the Neo ABI's fixed-width byte container here;
        // constructing it from the raw span preserves the wire bytes. UInt256.Parse
        // would reverse display-order hexadecimal and bind a different program key.
        return new UInt256(raw);
    }

    internal static uint ParseRequiredL2ChainId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "--l2-chain-id is required and must identify the non-zero Neo L2 domain bound to ExternalBridgeEscrow.",
                nameof(value));
        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var chainId) || chainId == 0)
            throw new FormatException("--l2-chain-id must be an unsigned 32-bit integer greater than zero.");
        return chainId;
    }

    internal static long ParsePositiveForcedInclusionFee(string value)
    {
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var fee) || fee <= 0)
            throw new FormatException(
                "--forced-inclusion-fee must be a positive integer in smallest GAS units.");
        return fee;
    }

    internal static uint ParseRequiredNetwork(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException(
                "--expected-network is required and must equal the target RPC network magic.",
                nameof(value));
        if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var network))
            throw new FormatException("--expected-network must be an unsigned 32-bit integer.");
        return network;
    }

    internal static Uri ParseAndValidateRpcEndpoint(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var endpoint))
            throw new FormatException("--rpc must be an absolute HTTP(S) endpoint.");
        if (endpoint.Scheme == Uri.UriSchemeHttps) return endpoint;
        var loopback = endpoint.Scheme == Uri.UriSchemeHttp
            && (string.Equals(endpoint.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(endpoint.Host, out var address) && IPAddress.IsLoopback(address)));
        if (!loopback)
            throw new InvalidOperationException(
                "--rpc must use HTTPS; plaintext HTTP is allowed only for a loopback node.");
        return endpoint;
    }

    internal static string RedactRpcEndpoint(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        var authority = endpoint.IsDefaultPort
            ? endpoint.Host
            : $"{endpoint.Host}:{endpoint.Port}";
        return $"{endpoint.Scheme}://{authority}/";
    }

    internal static UInt160 ParseRequiredAccount(string value, byte addressVersion, string optionName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{optionName} is required.", nameof(value));
        UInt160 account;
        if (!UInt160.TryParse(value, out account!))
        {
            try
            {
                account = value.ToScriptHash(addressVersion);
            }
            catch (FormatException ex)
            {
                throw new FormatException(
                    $"{optionName} must be a valid UInt160 or Neo address for address version {addressVersion}.",
                    ex);
            }
        }
        if (account == UInt160.Zero)
            throw new FormatException($"{optionName} must not be the zero account.");
        return account;
    }

    internal static void ValidateProductionSafetyOptions(
        bool dryRun,
        bool runPostDeploy,
        bool runSmoke,
        int? maxSteps = null)
    {
        if (!dryRun && !runPostDeploy)
            throw new InvalidOperationException(
                "--no-postdeploy is only allowed with --dry-run; a live deployment must bind and lock the SP1 verifier.");
        if (!dryRun && !runSmoke)
            throw new InvalidOperationException(
                "--no-smoke is only allowed with --dry-run; a live deployment must verify every SP1 binding postcondition.");
        if (!dryRun && maxSteps is not null)
            throw new InvalidOperationException(
                "--max-steps is only allowed with --dry-run; a live deployment must execute the complete deployment plan.");
    }

    private static DeployPlan LoadPlan(string planPath)
    {
        if (string.IsNullOrWhiteSpace(planPath)) return ScaffoldPlan.Default();
        if (!File.Exists(planPath)) throw new FileNotFoundException("plan file not found", planPath);
        return DeployPlan.FromJson(File.ReadAllText(planPath));
    }

    internal static DeployPlan SubstituteOperatorPlaceholders(
        DeployPlan plan,
        UInt160 owner,
        UInt160 gasHash,
        NeoECPoint publicKey,
        UInt160 emergencyCouncil,
        uint l2ChainId)
    {
        if (l2ChainId == 0) throw new ArgumentOutOfRangeException(nameof(l2ChainId));
        return new DeployPlan
        {
            Version = plan.Version,
            Network = plan.Network,
            Steps = plan.Steps.Select(s => s with
            {
                DeployData = (JArray)SubstituteToken(
                    s.DeployData, owner, gasHash, publicKey, emergencyCouncil, l2ChainId)!,
            }).ToArray(),
        };
    }

    private static JToken? SubstituteToken(
        JToken? token,
        UInt160 owner,
        UInt160 gasHash,
        NeoECPoint publicKey,
        UInt160 emergencyCouncil,
        uint l2ChainId)
    {
        if (token is null) return null;
        if (token is JString str)
        {
            return str.AsString() switch
            {
                "OWNER_REPLACE_ME" => new JString(owner.ToString()),
                "BOND_ASSET_REPLACE_ME" => new JString(gasHash.ToString()),
                "GOVERNANCE_COUNCIL_MEMBER_REPLACE_ME" => new JString(publicKey.ToString()),
                "EMERGENCY_COUNCIL_REPLACE_ME" => new JString(emergencyCouncil.ToString()),
                "L2_CHAIN_ID_REPLACE_ME" => new JNumber(l2ChainId),
                _ => new JString(str.AsString()),
            };
        }
        if (token is JArray arr)
        {
            var copy = new JArray();
            foreach (var child in arr)
                copy.Add(SubstituteToken(child, owner, gasHash, publicKey, emergencyCouncil, l2ChainId));
            return copy;
        }
        if (token is JObject obj)
        {
            var copy = new JObject();
            foreach (var (k, v) in obj.Properties)
                copy[k] = SubstituteToken(v, owner, gasHash, publicKey, emergencyCouncil, l2ChainId);
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
        ILiveRpcClient rpc,
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
                    Scopes = WitnessScope.CalledByEntry,
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

    private static async Task<long> CalculateNetworkFeeAsync(ILiveRpcClient rpc, Transaction tx)
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

    private static async Task SendTransactionAsync(ILiveRpcClient rpc, Transaction tx)
    {
        var result = await rpc.CallAsync("sendrawtransaction", Convert.ToBase64String(tx.ToArray()));
        if (result.ValueKind == JsonValueKind.False)
            throw new InvalidOperationException($"sendrawtransaction returned false for {tx.Hash}");
    }

    private static async Task<JsonElement?> TryGetContractStateAsync(ILiveRpcClient rpc, UInt160 hash)
    {
        try
        {
            return await rpc.CallAsync("getcontractstate", hash.ToString());
        }
        catch (LiveRpcException ex) when (
            ex.Message.Contains("Unknown contract", StringComparison.OrdinalIgnoreCase)
            || ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || ex.Code == -100)
        {
            return null;
        }
    }

    internal static void ValidateRemoteContractState(
        string name,
        UInt160 expectedHash,
        byte[] expectedNefBytes,
        string expectedManifestJson,
        JsonElement remoteState)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(expectedHash);
        ArgumentNullException.ThrowIfNull(expectedNefBytes);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedManifestJson);

        var remoteHash = UInt160.Parse(remoteState.GetProperty("hash").GetString()
            ?? throw new InvalidOperationException($"{name}: remote contract hash is missing"));
        if (remoteHash != expectedHash)
            throw new InvalidOperationException($"{name}: expected hash {expectedHash}, got {remoteHash}");

        var updateCounter = remoteState.GetProperty("updatecounter").GetInt32();
        if (updateCounter != 0)
            throw new InvalidOperationException(
                $"{name}: refusing updated contract state (updatecounter={updateCounter}); deploy a newly audited immutable artifact");

        var expectedNef = NefFile.Parse(expectedNefBytes).ToJson().ToString();
        AssertEquivalentJson(name, "NEF", expectedNef, remoteState.GetProperty("nef").GetRawText());

        var expectedManifest = ContractManifest.Parse(expectedManifestJson).ToJson().ToString();
        AssertEquivalentJson(name, "manifest", expectedManifest, remoteState.GetProperty("manifest").GetRawText());
    }

    private static void AssertEquivalentJson(string name, string artifact, string expected, string actual)
    {
        var expectedNode = JsonNode.Parse(expected)
            ?? throw new InvalidOperationException($"{name}: expected {artifact} JSON is empty");
        var actualNode = JsonNode.Parse(actual)
            ?? throw new InvalidOperationException($"{name}: remote {artifact} JSON is empty");
        if (!JsonNode.DeepEquals(expectedNode, actualNode))
            throw new InvalidOperationException(
                $"{name}: remote {artifact} differs from the locally audited deployment artifact");
    }

    private static async Task<ExecutionResult> WaitForApplicationLogAsync(ILiveRpcClient rpc, UInt256 txHash)
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

    internal static IReadOnlyList<PostDeployCall> BuildPostDeployCalls(
        IReadOnlyDictionary<string, UInt160> h,
        UInt160 gasHash,
        UInt160 forcedInclusionFeeRecipient,
        long forcedInclusionFee,
        UInt256 sp1ProgramVKey)
    {
        ArgumentNullException.ThrowIfNull(h);
        ArgumentNullException.ThrowIfNull(sp1ProgramVKey);
        if (gasHash == UInt160.Zero) throw new ArgumentException("GAS hash must not be zero.", nameof(gasHash));
        if (forcedInclusionFeeRecipient == UInt160.Zero)
            throw new ArgumentException("Forced-inclusion fee recipient must not be zero.", nameof(forcedInclusionFeeRecipient));
        if (forcedInclusionFee <= 0)
            throw new ArgumentOutOfRangeException(nameof(forcedInclusionFee), "Forced-inclusion fee must be positive.");

        return
        [
            CheckedCall("SequencerBond.RegisterSlasher", h["SequencerBond"], "registerSlasher",
                BoolCheck("SequencerBond.IsSlasher", h["SequencerBond"], "isSlasher", true, h["OptimisticChallenge"]), h["OptimisticChallenge"]),
            CheckedCall("SequencerBond.RegisterSlasher.ForcedInclusion", h["SequencerBond"], "registerSlasher",
                BoolCheck("SequencerBond.IsSlasher.ForcedInclusion", h["SequencerBond"], "isSlasher", true, h["ForcedInclusion"]), h["ForcedInclusion"]),
            CheckedCall("ChainRegistry.RegisterPauser.ForcedInclusion", h["ChainRegistry"], "registerPauser",
                BoolCheck("ChainRegistry.IsPauser.ForcedInclusion", h["ChainRegistry"], "isPauser", true, h["ForcedInclusion"]), h["ForcedInclusion"]),
            CheckedCall("ForcedInclusion.SetChainRegistry", h["ForcedInclusion"], "setChainRegistry",
                HashCheck("ForcedInclusion.GetChainRegistry", h["ForcedInclusion"], "getChainRegistry", h["ChainRegistry"]), h["ChainRegistry"]),
            CheckedCall("ForcedInclusion.SetSequencerBond", h["ForcedInclusion"], "setSequencerBond",
                HashCheck("ForcedInclusion.GetSequencerBond", h["ForcedInclusion"], "getSequencerBond", h["SequencerBond"]), h["SequencerBond"]),
            CheckedCall("ForcedInclusion.SetCensorshipSlashAmount", h["ForcedInclusion"], "setCensorshipSlashAmount",
                IntegerCheck("ForcedInclusion.GetCensorshipSlashAmount", h["ForcedInclusion"], "getCensorshipSlashAmount", 1_000_000), 1_000_000L),
            CheckedCall("ForcedInclusion.SetGasToken", h["ForcedInclusion"], "setGasToken",
                HashCheck("ForcedInclusion.GetGasToken", h["ForcedInclusion"], "getGasToken", gasHash), gasHash),
            CheckedCall("ForcedInclusion.SetFeeRecipient", h["ForcedInclusion"], "setFeeRecipient",
                HashCheck("ForcedInclusion.GetFeeRecipient", h["ForcedInclusion"], "getFeeRecipient", forcedInclusionFeeRecipient), forcedInclusionFeeRecipient),
            CheckedCall("ForcedInclusion.SetFee", h["ForcedInclusion"], "setFee",
                IntegerCheck("ForcedInclusion.GetFee", h["ForcedInclusion"], "getFee", forcedInclusionFee), forcedInclusionFee),
            CheckedCall("SharedBridge.SetEmergencyManager", h["SharedBridge"], "setEmergencyManager",
                HashCheck("SharedBridge.GetEmergencyManager", h["SharedBridge"], "getEmergencyManager", h["EmergencyManager"]), h["EmergencyManager"]),
            CheckedCall("ChainRegistry.SetGovernanceController", h["ChainRegistry"], "setGovernanceController",
                HashCheck("ChainRegistry.GetGovernanceController", h["ChainRegistry"], "getGovernanceController", h["GovernanceController"]), h["GovernanceController"]),
            CheckedCall("VerifierRegistry.SetGovernanceController", h["VerifierRegistry"], "setGovernanceController",
                HashCheck("VerifierRegistry.GetGovernanceController", h["VerifierRegistry"], "getGovernanceController", h["GovernanceController"]), h["GovernanceController"]),
            CheckedCall("ContractZkVerifier.RegisterVerificationKey.Sp1", h["ContractZkVerifier"], "registerVerificationKey",
                BoolCheck("ContractZkVerifier.IsVerificationKeyRegistered.Sp1", h["ContractZkVerifier"], "isVerificationKeyRegistered", true, ProofSystemSp1, sp1ProgramVKey), ProofSystemSp1, sp1ProgramVKey, true),
            CheckedCall("ContractZkVerifier.RegisterProofVerifier.Sp1", h["ContractZkVerifier"], "registerProofVerifier",
                HashCheck("ContractZkVerifier.GetProofVerifier.Sp1", h["ContractZkVerifier"], "getProofVerifier", h["Sp1Groth16Verifier"], ProofSystemSp1), ProofSystemSp1, h["Sp1Groth16Verifier"], true),
            CheckedCall("ContractZkVerifier.DisableEnvelopeOnlyPermanently.Sp1", h["ContractZkVerifier"], "disableEnvelopeOnlyPermanently",
                BoolCheck("ContractZkVerifier.IsEnvelopeOnlyLocked.Sp1", h["ContractZkVerifier"], "isEnvelopeOnlyLocked", true, ProofSystemSp1), ProofSystemSp1),
            CheckedCall("ContractZkVerifier.LockProofSystemConfiguration.Sp1", h["ContractZkVerifier"], "lockProofSystemConfiguration",
                Hash256Check("ContractZkVerifier.GetLockedVerificationKey.Sp1", h["ContractZkVerifier"], "getLockedVerificationKey", sp1ProgramVKey, ProofSystemSp1), ProofSystemSp1, sp1ProgramVKey),
            CheckedCall("VerifierRegistry.RegisterVerifier.Zk", h["VerifierRegistry"], "registerVerifier",
                HashCheck("VerifierRegistry.GetVerifier.Zk", h["VerifierRegistry"], "getVerifier", h["ContractZkVerifier"], ProofTypeZk), ProofTypeZk, h["ContractZkVerifier"]),
            CheckedCall("VerifierRegistry.LockGovernance", h["VerifierRegistry"], "lockGovernance",
                BoolCheck("VerifierRegistry.IsGovernanceLocked", h["VerifierRegistry"], "isGovernanceLocked", true)),
            CheckedCall("OptimisticChallenge.RegisterFraudVerifier.Governance", h["OptimisticChallenge"], "registerFraudVerifier",
                BoolCheck("OptimisticChallenge.IsApprovedFraudVerifier.Governance", h["OptimisticChallenge"], "isApprovedFraudVerifier", true, h["GovernanceFraudVerifier"]), h["GovernanceFraudVerifier"]),
            CheckedCall("OptimisticChallenge.RegisterFraudVerifier.RestrictedExecution", h["OptimisticChallenge"], "registerFraudVerifier",
                BoolCheck("OptimisticChallenge.IsApprovedFraudVerifier.RestrictedExecution", h["OptimisticChallenge"], "isApprovedFraudVerifier", true, h["RestrictedExecutionFraudVerifier"]), h["RestrictedExecutionFraudVerifier"]),
            CheckedCall("MpcCommitteeVerifier.SetGovernanceController", h["MpcCommitteeVerifier"], "setGovernanceController",
                HashCheck("MpcCommitteeVerifier.GetGovernanceController", h["MpcCommitteeVerifier"], "getGovernanceController", h["GovernanceController"]), h["GovernanceController"]),
            CheckedCall("ExternalBridgeRegistry.SetGovernanceController", h["ExternalBridgeRegistry"], "setGovernanceController",
                HashCheck("ExternalBridgeRegistry.GetGovernanceController", h["ExternalBridgeRegistry"], "getGovernanceController", h["GovernanceController"]), h["GovernanceController"]),
            CheckedCall("ExternalBridgeBond.RegisterSlasher", h["ExternalBridgeBond"], "registerSlasher",
                BoolCheck("ExternalBridgeBond.IsSlasher", h["ExternalBridgeBond"], "isSlasher", true, h["MpcCommitteeFraudVerifier"]), h["MpcCommitteeFraudVerifier"]),
            CheckedCall("SettlementManager.SetDARegistry", h["SettlementManager"], "setDARegistry",
                HashCheck("SettlementManager.GetDARegistry", h["SettlementManager"], "getDARegistry", h["DARegistry"]), h["DARegistry"]),
            CheckedCall("SettlementManager.SetDAValidator", h["SettlementManager"], "setDAValidator",
                HashCheck("SettlementManager.GetDAValidator", h["SettlementManager"], "getDAValidator", h["DAValidator"]), h["DAValidator"]),
            CheckedCall("SettlementManager.SetOptimisticChallenge", h["SettlementManager"], "setOptimisticChallenge",
                HashCheck("SettlementManager.GetOptimisticChallenge", h["SettlementManager"], "getOptimisticChallenge", h["OptimisticChallenge"]), h["OptimisticChallenge"]),
        ];
    }

    private static PostDeployCall CheckedCall(
        string name,
        UInt160 contract,
        string method,
        SmokeCheck completionCheck,
        params object[] args)
    {
        using var sb = new ScriptBuilder();
        sb.EmitDynamicCall(contract, method, args);
        return new PostDeployCall(name, contract, sb.ToArray(), completionCheck);
    }

    internal static async Task<bool> IsPostDeployActionCompleteAsync(
        SmokeCheck completionCheck,
        ILiveRpcClient rpc)
    {
        try
        {
            await completionCheck.RunAsync(rpc);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    internal static IReadOnlyList<SmokeCheck> BuildSmokeChecks(
        IReadOnlyDictionary<string, UInt160> h,
        UInt160 owner,
        UInt160 gasHash,
        UInt160 forcedInclusionFeeRecipient,
        long forcedInclusionFee,
        UInt256 sp1ProgramVKey,
        uint l2ChainId)
    {
        ArgumentNullException.ThrowIfNull(h);
        ArgumentNullException.ThrowIfNull(sp1ProgramVKey);

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
            HashCheck("ForcedInclusion.GetGasToken", h["ForcedInclusion"], "getGasToken", gasHash),
            HashCheck("ForcedInclusion.GetFeeRecipient", h["ForcedInclusion"], "getFeeRecipient", forcedInclusionFeeRecipient),
            IntegerCheck("ForcedInclusion.GetFee", h["ForcedInclusion"], "getFee", forcedInclusionFee),
            BoolCheck("ForcedInclusion.IsProductionReady", h["ForcedInclusion"], "isProductionReady", true),
            HashCheck("SharedBridge.GetEmergencyManager", h["SharedBridge"], "getEmergencyManager", h["EmergencyManager"]),
            HashCheck("ChainRegistry.GetGovernanceController", h["ChainRegistry"], "getGovernanceController", h["GovernanceController"]),
            BoolCheck("ContractZkVerifier.IsVerificationKeyRegistered.Sp1", h["ContractZkVerifier"], "isVerificationKeyRegistered", true, ProofSystemSp1, sp1ProgramVKey),
            HashCheck("ContractZkVerifier.GetProofVerifier.Sp1", h["ContractZkVerifier"], "getProofVerifier", h["Sp1Groth16Verifier"], ProofSystemSp1),
            BoolCheck("ContractZkVerifier.IsEnvelopeOnlyLocked.Sp1", h["ContractZkVerifier"], "isEnvelopeOnlyLocked", true, ProofSystemSp1),
            BoolCheck("ContractZkVerifier.IsEnvelopeOnlyAllowed.Sp1", h["ContractZkVerifier"], "isEnvelopeOnlyAllowed", false, ProofSystemSp1),
            BoolCheck("ContractZkVerifier.IsProofSystemConfigurationLocked.Sp1", h["ContractZkVerifier"], "isProofSystemConfigurationLocked", true, ProofSystemSp1),
            Hash256Check("ContractZkVerifier.GetLockedVerificationKey.Sp1", h["ContractZkVerifier"], "getLockedVerificationKey", sp1ProgramVKey, ProofSystemSp1),
            HashCheck("VerifierRegistry.GetVerifier.Zk", h["VerifierRegistry"], "getVerifier", h["ContractZkVerifier"], ProofTypeZk),
            HashCheck("VerifierRegistry.GetGovernanceController", h["VerifierRegistry"], "getGovernanceController", h["GovernanceController"]),
            BoolCheck("VerifierRegistry.IsGovernanceLocked", h["VerifierRegistry"], "isGovernanceLocked", true),
            BoolCheck("OptimisticChallenge.IsApprovedFraudVerifier.Governance", h["OptimisticChallenge"], "isApprovedFraudVerifier", true, h["GovernanceFraudVerifier"]),
            BoolCheck("OptimisticChallenge.IsApprovedFraudVerifier.RestrictedExecution", h["OptimisticChallenge"], "isApprovedFraudVerifier", true, h["RestrictedExecutionFraudVerifier"]),
            HashCheck("MpcCommitteeVerifier.GetGovernanceController", h["MpcCommitteeVerifier"], "getGovernanceController", h["GovernanceController"]),
            HashCheck("ExternalBridgeRegistry.GetGovernanceController", h["ExternalBridgeRegistry"], "getGovernanceController", h["GovernanceController"]),
            BoolCheck("SequencerBond.IsSlasher", h["SequencerBond"], "isSlasher", true, h["OptimisticChallenge"]),
            BoolCheck("SequencerBond.IsSlasher.ForcedInclusion", h["SequencerBond"], "isSlasher", true, h["ForcedInclusion"]),
            BoolCheck("ChainRegistry.IsPauser.ForcedInclusion", h["ChainRegistry"], "isPauser", true, h["ForcedInclusion"]),
            BoolCheck("ExternalBridgeBond.IsSlasher", h["ExternalBridgeBond"], "isSlasher", true, h["MpcCommitteeFraudVerifier"]),
            IntegerCheck("ExternalBridgeEscrow.GetNeoChainId", h["ExternalBridgeEscrow"], "getNeoChainId", l2ChainId),
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

    private static SmokeCheck Hash256Check(string name, UInt160 contract, string method, UInt256 expected, params object[] args)
    {
        return new SmokeCheck(name, contract, async rpc =>
        {
            var result = await InvokeReadAsync(rpc, contract, method, args);
            var actual = StackValue(result);
            var expectedRaw = Convert.ToHexString(expected.GetSpan()).ToLowerInvariant();
            if (!string.Equals(actual, expectedRaw, StringComparison.Ordinal))
                throw new InvalidOperationException($"{name}: expected raw 0x{expectedRaw}, got {actual}");
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

    private static async Task<JsonElement> InvokeReadAsync(ILiveRpcClient rpc, UInt160 contract, string method, object[] args)
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
                ["scopes"] = "CalledByEntry",
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
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parsed = int.Parse(value, CultureInfo.InvariantCulture);
        if (parsed <= 0) throw new ArgumentOutOfRangeException(nameof(value), "--max-steps must be positive");
        return parsed;
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
        UInt256 sp1ProgramVKey,
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
            ["sp1ProgramVKeyRaw"] = $"0x{Convert.ToHexString(sp1ProgramVKey.GetSpan()).ToLowerInvariant()}",
            ["dryRun"] = dryRun,
            ["records"] = records,
        };
        var json = StjSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        var temporary = $"{output}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, output, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private sealed record ExecutionResult(string VmState, string? Exception);

    internal sealed record PostDeployCall(
        string Name,
        UInt160 Contract,
        byte[] Script,
        SmokeCheck? CompletionCheck);

    internal sealed record SmokeCheck(string Name, UInt160 Contract, Func<ILiveRpcClient, Task> RunAsync);

    internal interface ILiveRpcClient
    {
        Task<JsonElement> CallAsync(string method, params object?[] parameters);
    }

    private sealed class LiveRpcClient : ILiveRpcClient, IDisposable
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
