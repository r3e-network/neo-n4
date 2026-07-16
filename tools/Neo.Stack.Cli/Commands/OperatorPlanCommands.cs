using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Neo.Extensions.VM;
using Neo.L2;
using Neo.L2.Settlement.Rpc;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// neo-stack operator subcommands. Each validates inputs and prints a deterministic
/// dry-run plan; transaction-producing commands also support explicit signed broadcast
/// through the shared wallet/KMS boundary. Per IMPLEMENTATION_STATUS Phase 6,
/// all 12 neo-stack subcommands are functional. This file hosts register-chain,
/// deploy-bridge-adapter, submit-batch, and start-{sequencer,batcher,prover};
/// create-chain, init-l2, validate, scaffold-executor, new-l2, and list-templates
/// have their own files.
/// </summary>
internal static class RegisterChainCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var l1 = ArgUtil.Get(args, "--l1", "neo-n3-testnet");
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsedChainId))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return Task.FromResult(1);
        }
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsedChainId, "--chain-id");
        // Accept both --output (matches the Quick-path walkthrough in
        // launching-an-l2.md + create-chain primary flag) and --path (kept for
        // backwards compat). --output takes precedence when both supplied, mirroring
        // InitL2Command + StartCommandPreflight. Without this, the documented
        // 5-command path `neo-stack register-chain --chain-id N --output ./my-l2`
        // silently fell back to ./chain-N and either ran with the wrong dir or
        // exited with "Missing config".
        var outputFlag = ArgUtil.Get(args, "--output", "");
        var chainDir = outputFlag.Length > 0
            ? outputFlag
            : ArgUtil.Get(args, "--path", $"./chain-{chainId}");

        var configPath = System.IO.Path.Combine(chainDir, "chain.config.json");
        if (!System.IO.File.Exists(configPath))
        {
            Console.Error.WriteLine($"Missing config: {configPath}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return Task.FromResult(2);
        }

        // Read + display the config so the operator knows exactly what would land on L1.
        string configJson;
        try
        {
            configJson = System.IO.File.ReadAllText(configPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to read {configPath}: {ex.Message}");
            return Task.FromResult(3);
        }

        Console.WriteLine($"Registration plan for chain {chainId} on L1 '{l1}':");
        Console.WriteLine();
        Console.WriteLine($"  Target contract : NeoHub.ChainRegistry");
        Console.WriteLine($"  Method          : registerChain");
        Console.WriteLine($"  Config source   : {configPath}");
        Console.WriteLine();

        // Resolve L1 hashes either from explicit flags or from a neo-hub-deploy evidence
        // report (--from-deploy-report). Report path also materializes l1.deployed.json +
        // settlement config snippet under the chain directory for WireProduction hosts.
        var deployReportPath = ArgUtil.Get(args, "--from-deploy-report", "");
        NeoHubDeployReport? deployReport = null;
        if (deployReportPath.Length > 0)
        {
            try
            {
                deployReport = NeoHubDeployReport.Load(deployReportPath);
                if (deployReport.L2ChainId != chainId)
                {
                    Console.Error.WriteLine(
                        $"--chain-id {chainId} differs from deploy report l2ChainId {deployReport.L2ChainId}");
                    return Task.FromResult(4);
                }
                var written = deployReport.WriteOperatorArtifacts(chainDir);
                Console.WriteLine($"  Deploy report   : {deployReportPath}");
                Console.WriteLine($"  L1 network      : {deployReport.Network} @ {deployReport.Rpc}");
                foreach (var artifact in written.Distinct(StringComparer.Ordinal))
                    Console.WriteLine($"  Wrote           : {Path.Combine(chainDir, artifact)}");
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to load --from-deploy-report: {ex.Message}");
                return Task.FromResult(4);
            }
        }

        var operatorHash = FirstNonEmpty(
            ArgUtil.Get(args, "--operator", ""),
            deployReport?.DefaultOperatorManager.ToString() ?? "");
        var verifierHash = FirstNonEmpty(
            ArgUtil.Get(args, "--verifier", ""),
            deployReport?.VerifierRegistry.ToString() ?? "");
        var bridgeHash = FirstNonEmpty(
            ArgUtil.Get(args, "--bridge", ""),
            deployReport?.SharedBridge.ToString() ?? "");
        var messageHash = FirstNonEmpty(
            ArgUtil.Get(args, "--message", ""),
            deployReport?.MessageRouter.ToString() ?? "");

        if (operatorHash.Length > 0 && verifierHash.Length > 0
            && bridgeHash.Length > 0 && messageHash.Length > 0)
        {
            try
            {
                UInt256 genesisStateRoot;
                var genesisManifest = ArgUtil.Get(args, "--genesis-manifest", "");
                // Default to chain-dir genesis-manifest.json when present so
                // bootstrap-genesis → register-chain needs no re-typed path.
                if (genesisManifest.Length == 0)
                {
                    var defaultManifest = Path.Combine(chainDir, BootstrapGenesisCommand.ManifestFileName);
                    if (File.Exists(defaultManifest))
                        genesisManifest = defaultManifest;
                }
                var genesisStateRootValue = ArgUtil.Get(args, "--genesis-state-root", "");
                if (genesisManifest.Length > 0)
                {
                    try
                    {
                        genesisStateRoot = BootstrapGenesisCommand.ReadInitialStateRoot(genesisManifest);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"failed to read --genesis-manifest: {ex.Message}");
                        return Task.FromResult(4);
                    }
                    if (genesisStateRootValue.Length > 0
                        && UInt256.TryParse(genesisStateRootValue, out var explicitRoot)
                        && explicitRoot is not null
                        && !explicitRoot.Equals(genesisStateRoot))
                    {
                        Console.Error.WriteLine(
                            "--genesis-state-root differs from --genesis-manifest initialStateRoot");
                        return Task.FromResult(4);
                    }
                }
                else if (!UInt256.TryParse(genesisStateRootValue, out var parsedRoot)
                    || parsedRoot is null
                    || parsedRoot.Equals(UInt256.Zero))
                {
                    Console.Error.WriteLine(
                        "--genesis-state-root <non-zero UInt256> or --genesis-manifest <path> is required "
                        + "with the four L1 contract hashes"
                        + (deployReport is not null
                            ? " (or after --from-deploy-report)"
                            : ""));
                    return Task.FromResult(4);
                }
                else
                {
                    genesisStateRoot = parsedRoot;
                }
                var config = L2ChainConfigJsonReader.FromJson(chainId, configJson,
                    operatorHash, verifierHash, bridgeHash, messageHash);
                var bytes = L2ChainConfigSerializer.Encode(config);
                Console.WriteLine($"  Operator        : {operatorHash}");
                Console.WriteLine($"  Verifier        : {verifierHash}");
                Console.WriteLine($"  Bridge          : {bridgeHash}");
                Console.WriteLine($"  Message         : {messageHash}");
                Console.WriteLine(
                    $"  Args            : (chainId={chainId}, configBytes=<{bytes.Length} bytes>, genesisStateRoot={genesisStateRoot})");
                Console.WriteLine();
                Console.WriteLine($"  configBytes (hex, copy-pasteable):");
                Console.WriteLine($"  0x{Convert.ToHexString(bytes).ToLowerInvariant()}");
                if (ArgUtil.HasFlag(args, "--broadcast"))
                {
                    var chainRegistryValue = FirstNonEmpty(
                        ArgUtil.Get(args, "--chain-registry", ""),
                        deployReport?.ChainRegistry.ToString() ?? "");
                    if (!UInt160.TryParse(chainRegistryValue, out var chainRegistry)
                        || chainRegistry == UInt160.Zero)
                    {
                        Console.Error.WriteLine(
                            "--chain-registry <UInt160> is required with --broadcast"
                            + " (or supply --from-deploy-report that includes ChainRegistry)");
                        return Task.FromResult(5);
                    }
                    // Prefer deploy-report network/rpc when flags omitted so a single
                    // --from-deploy-report path can broadcast without re-typing endpoints.
                    if (deployReport is not null)
                    {
                        if (string.IsNullOrWhiteSpace(ArgUtil.Get(args, "--rpc", "")))
                            args = AppendFlag(args, "--rpc", deployReport.Rpc);
                        if (string.IsNullOrWhiteSpace(ArgUtil.Get(args, "--expected-network", "")))
                            args = AppendFlag(
                                args, "--expected-network", deployReport.Network.ToString());
                    }
                    using var scriptBuilder = new ScriptBuilder();
                    scriptBuilder.EmitDynamicCall(
                        chainRegistry,
                        "registerChain",
                        chainId,
                        bytes,
                        genesisStateRoot);
                    return BroadcastAndVerifyRegistrationAsync(
                        args,
                        scriptBuilder.ToArray(),
                        chainId,
                        chainRegistry,
                        genesisStateRoot);
                }
                Console.WriteLine();
                Console.WriteLine($"Next steps:");
                Console.WriteLine($"  1. Add --broadcast --rpc <url> --expected-network <magic> --chain-registry <hash>");
                if (deployReport is not null)
                {
                    Console.WriteLine(
                        $"     (with --from-deploy-report, --rpc/--expected-network/--chain-registry default from the report)");
                }
                Console.WriteLine(
                    $"     to sign + submit registerChain({chainId}, 0x{Convert.ToHexString(bytes).ToLowerInvariant()}, {genesisStateRoot})");
                Console.WriteLine($"     Sign with NEO_N4_OPERATOR_WIF or --signer-command (docs/operator-signer-command-protocol.md).");
                Console.WriteLine($"  2. Verify on L1 by calling ChainRegistry.isActive({chainId})");
                return Task.FromResult(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to build configBytes: {ex.Message}");
                return Task.FromResult(4);
            }
        }

        Console.WriteLine($"  Args            : (chainId={chainId}, configBytes=<encoded>, genesisStateRoot=<required>)");
        Console.WriteLine($"  Config bytes    : {configJson.Length} chars (will be NEO-serialized to bytes)");
        Console.WriteLine();
        Console.WriteLine($"--- chain config preview ---");
        Console.WriteLine(configJson.Length > 600 ? configJson[..600] + "…" : configJson);
        Console.WriteLine($"--- end ---");
        Console.WriteLine();
        Console.WriteLine($"Next steps for production registration:");
        Console.WriteLine($"  1. Run `neo-hub-deploy deploy-testnet` (or scaffold+plan+wallet) for L1 NeoHub.");
        Console.WriteLine($"  2. Re-run register-chain with either:");
        Console.WriteLine($"       --from-deploy-report docs/audit/testnet-deployment-<date>-live.json \\");
        Console.WriteLine($"         --genesis-manifest ./my-l2/genesis-manifest.json");
        Console.WriteLine($"     (run `neo-stack bootstrap-genesis` first to produce the manifest)");
        Console.WriteLine($"     or the four explicit hashes + --genesis-state-root <UInt256>");
        Console.WriteLine($"     to emit the canonical 91-byte configBytes hex (ready for wallet-side submission).");
        Console.WriteLine($"  3. Sign + submit registerChain({chainId}, <configBytes>, <genesisStateRoot>) via --broadcast or wallet.");
        Console.WriteLine($"  4. Verify on L1 by calling ChainRegistry.isActive({chainId}).");
        Console.WriteLine();
        Console.WriteLine($"(Wallet integration is operator-specific — wire your signer via Neo.L2.Settlement.Rpc.RpcSettlementClient.)");
        return Task.FromResult(0);
    }

    private static string FirstNonEmpty(string preferred, string fallback)
        => preferred.Length > 0 ? preferred : fallback;

    private static string[] AppendFlag(string[] args, string flag, string value)
    {
        var next = new string[args.Length + 2];
        args.CopyTo(next, 0);
        next[^2] = flag;
        next[^1] = value;
        return next;
    }

    /// <summary>
    /// Broadcast registerChain then fail closed unless ChainRegistry.isActive(chainId)
    /// and getGenesisStateRoot(chainId) match the just-submitted trust anchor.
    /// </summary>
    private static async Task<int> BroadcastAndVerifyRegistrationAsync(
        string[] args,
        byte[] script,
        uint chainId,
        UInt160 chainRegistry,
        UInt256 expectedGenesisRoot)
    {
        var broadcastRc = await OperatorTransactionBroadcaster.BroadcastAsync(
            args,
            script,
            $"chain {chainId} registration").ConfigureAwait(false);
        if (broadcastRc != 0)
            return broadcastRc;

        var rpcValue = ArgUtil.Get(args, "--rpc", "");
        if (!Uri.TryCreate(rpcValue, UriKind.Absolute, out var rpcEndpoint)
            || rpcEndpoint.Scheme is not ("http" or "https"))
        {
            Console.Error.WriteLine(
                "post-registration verify skipped: --rpc is missing after broadcast");
            return 14;
        }

        try
        {
            using var rpc = new JsonRpcClient(rpcEndpoint.AbsoluteUri);
            var activeItem = await RpcContractReader.InvokeReadAsync(
                rpc,
                chainRegistry,
                "isActive",
                new object[] { chainId },
                default).ConfigureAwait(false);
            if (!RpcContractReader.ParseBoolean(activeItem))
            {
                Console.Error.WriteLine(
                    $"post-registration verify failed: ChainRegistry.isActive({chainId}) is false");
                return 15;
            }

            var rootItem = await RpcContractReader.InvokeReadAsync(
                rpc,
                chainRegistry,
                "getGenesisStateRoot",
                new object[] { chainId },
                default).ConfigureAwait(false);
            var onChainRoot = RpcContractReader.ParseUInt256(rootItem);
            if (!onChainRoot.Equals(expectedGenesisRoot))
            {
                Console.Error.WriteLine(
                    $"post-registration verify failed: getGenesisStateRoot({chainId})={onChainRoot} "
                    + $"differs from submitted genesis {expectedGenesisRoot}");
                return 16;
            }

            Console.WriteLine();
            Console.WriteLine($"post-registration verify ok:");
            Console.WriteLine($"  isActive({chainId})          : true");
            Console.WriteLine($"  getGenesisStateRoot({chainId}): {onChainRoot}");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(
                $"post-registration verify failed: {exception.Message}");
            return 17;
        }
    }

}

internal static class DeployBridgeAdapterCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsed))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsed, "--chain-id");

        // Accept --output / --path for consistency with the rest of the CLI (the
        // documented Quick path passes --output to every command). When supplied,
        // do the same chain.config.json existence preflight as register-chain so
        // an operator who forgot create-chain gets a clear diagnostic instead of
        // a plan that doesn't apply to anything. Without --output / --path, the
        // command keeps its previous chain-id-only behavior — a generic plan.
        var outputFlag = ArgUtil.Get(args, "--output", "");
        var pathFlag = ArgUtil.Get(args, "--path", "");
        var chainDir = outputFlag.Length > 0 ? outputFlag : pathFlag;
        if (chainDir.Length > 0)
        {
            var configPath = System.IO.Path.Combine(chainDir, "chain.config.json");
            if (!System.IO.File.Exists(configPath))
            {
                Console.Error.WriteLine($"Missing config: {configPath}");
                Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
                return 2;
            }
        }

        Console.WriteLine($"Bridge adapter deployment plan for chain {chainId}:");
        Console.WriteLine();
        Console.WriteLine("  L2-side native contract   : L2BridgeContract (built into r3e-network/neo branch r3e/neo-n4-core)");
        Console.WriteLine("  L1-side anchor contract   : NeoHub.SharedBridge");
        Console.WriteLine();
        Console.WriteLine("  Required asset mappings   :");
        Console.WriteLine("    L2BridgeContract.RegisterMapping(<L1 NEO>, <L2 NEO>, l1Decimals=0, l2Decimals=8)");
        Console.WriteLine("    L2BridgeContract.RegisterMapping(<L1 GAS>, <L2 GAS>, l1Decimals=8, l2Decimals=8)");
        Console.WriteLine("    L2BridgeContract.RegisterMapping(<L1 USDT>, <L2 USDT>, l1Decimals=6, l2Decimals=6)");
        Console.WriteLine("    L2BridgeContract.RegisterMapping(<L1 USDC>, <L2 USDC>, l1Decimals=6, l2Decimals=6)");
        Console.WriteLine("    L2BridgeContract.RegisterMapping(<L1 BTC>,  <L2 BTC>,  l1Decimals=8, l2Decimals=8)");
        Console.WriteLine("    NeoHub.TokenRegistry.RegisterMapping(<encoded L1+chainId+L2 mapping with decimals>)");
        Console.WriteLine();
        Console.WriteLine($"Next steps for production deploy:");
        Console.WriteLine($"  1. Start chain {chainId} from the r3e Neo core fork so L2BridgeContract exists at genesis as a native contract");
        Console.WriteLine($"  2. Configure native L2BridgeContract owner/system account through the L2 governance signer");
        Console.WriteLine($"  3. Register the L1-L2 NEO, GAS, USDT, USDC, and BTC mappings on both sides (asymmetric - L1 calls TokenRegistry, L2 calls L2BridgeContract)");
        Console.WriteLine($"  4. Verify lookup + decimals: TokenRegistry.GetL2Asset/GetL1Decimals/GetL2Decimals and L2BridgeContract.GetL2Asset/GetL1Decimals/GetL2Decimals");
        Console.WriteLine();
        Console.WriteLine($"(No L2Native contract is deployed after genesis; L1 setup still needs the contract owner and operator-specific signing.)");
        if (!ArgUtil.HasFlag(args, "--broadcast")) return 0;

        if (!TryBuildMapping(args, chainId, out var mapping)) return 3;
        var mappingBytes = AssetMappingSerializer.Encode(mapping);
        var side = ArgUtil.Get(args, "--side", "both").ToLowerInvariant();
        if (side is not ("l1" or "l2" or "both"))
        {
            Console.Error.WriteLine("--side must be l1, l2, or both");
            return 4;
        }

        Console.WriteLine();
        Console.WriteLine($"Canonical TokenRegistry mapping: 0x{Convert.ToHexString(mappingBytes).ToLowerInvariant()}");

        if (side is "l1" or "both")
        {
            var tokenRegistryValue = ArgUtil.Get(args, "--token-registry", "");
            if (!UInt160.TryParse(tokenRegistryValue, out var tokenRegistry)
                || tokenRegistry == UInt160.Zero)
            {
                Console.Error.WriteLine("--token-registry <UInt160> is required for L1 mapping registration");
                return 5;
            }
            using var l1Script = new ScriptBuilder();
            l1Script.EmitDynamicCall(tokenRegistry, "registerMapping", mappingBytes);
            var l1Result = await OperatorTransactionBroadcaster.BroadcastAsync(
                args,
                l1Script.ToArray(),
                $"L1 asset mapping for chain {chainId}",
                optionPrefix: "l1");
            if (l1Result != 0) return l1Result;
        }

        if (side is "l2" or "both")
        {
            using var l2Script = new ScriptBuilder();
            var ownerValue = ArgUtil.Get(args, "--l2-owner", "");
            var systemAccountValue = ArgUtil.Get(args, "--l2-system-account", "");
            if (ownerValue.Length > 0 || systemAccountValue.Length > 0)
            {
                if (!TryParseNonZeroHash(ownerValue, "--l2-owner", out var owner)
                    || !TryParseNonZeroHash(systemAccountValue, "--l2-system-account", out var systemAccount))
                {
                    return 6;
                }
                l2Script.EmitDynamicCall(NativeContract.L2Bridge.Hash, "configure", owner, systemAccount);
            }
            l2Script.EmitDynamicCall(
                NativeContract.L2Bridge.Hash,
                "registerMapping",
                mapping.L1Asset,
                mapping.L2Asset,
                mapping.L1Decimals,
                mapping.L2Decimals);
            var l2Result = await OperatorTransactionBroadcaster.BroadcastAsync(
                args,
                l2Script.ToArray(),
                $"L2 native bridge mapping for chain {chainId}",
                optionPrefix: "l2");
            if (l2Result != 0) return l2Result;
        }

        return 0;
    }

    private static bool TryBuildMapping(string[] args, uint chainId, out AssetMapping mapping)
    {
        mapping = null!;
        if (!TryParseNonZeroHash(ArgUtil.Get(args, "--l1-asset", ""), "--l1-asset", out var l1Asset)
            || !TryParseNonZeroHash(ArgUtil.Get(args, "--l2-asset", ""), "--l2-asset", out var l2Asset))
        {
            return false;
        }
        var assetTypeValue = ArgUtil.Get(args, "--asset-type", "");
        if (!Enum.TryParse<AssetType>(assetTypeValue, ignoreCase: true, out var assetType)
            || !Enum.IsDefined(assetType))
        {
            Console.Error.WriteLine("--asset-type must be a named AssetType value");
            return false;
        }
        if (!byte.TryParse(ArgUtil.Get(args, "--l1-decimals", ""), out var l1Decimals)
            || l1Decimals > AssetAmount.MaxDecimals)
        {
            Console.Error.WriteLine($"--l1-decimals must be between 0 and {AssetAmount.MaxDecimals}");
            return false;
        }
        if (!byte.TryParse(ArgUtil.Get(args, "--l2-decimals", ""), out var l2Decimals)
            || l2Decimals > AssetAmount.MaxDecimals)
        {
            Console.Error.WriteLine($"--l2-decimals must be between 0 and {AssetAmount.MaxDecimals}");
            return false;
        }
        mapping = new AssetMapping
        {
            L1Asset = l1Asset,
            L2ChainId = chainId,
            L2Asset = l2Asset,
            AssetType = assetType,
            MintBurn = true,
            LockMint = true,
            L1Decimals = l1Decimals,
            L2Decimals = l2Decimals,
            Active = true,
        };
        return true;
    }

    private static bool TryParseNonZeroHash(string value, string option, out UInt160 hash)
    {
        if (UInt160.TryParse(value, out var parsed) && parsed is not null && parsed != UInt160.Zero)
        {
            hash = parsed;
            return true;
        }
        Console.Error.WriteLine($"{option} <non-zero UInt160> is required with --broadcast");
        hash = UInt160.Zero;
        return false;
    }
}

internal static class SubmitBatchCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var batchFile = ArgUtil.Get(args, "--file", "");
        if (string.IsNullOrEmpty(batchFile))
        {
            Console.Error.WriteLine("--file <path> is required");
            return Task.FromResult(1);
        }
        if (!System.IO.File.Exists(batchFile))
        {
            Console.Error.WriteLine($"batch file not found: {batchFile}");
            return Task.FromResult(2);
        }

        // Read + decode the batch via BatchSerializer. Pre-flight validation surfaces a
        // bad encoding here (clear error message) instead of at L1 (opaque revert).
        byte[] bytes;
        try
        {
            bytes = System.IO.File.ReadAllBytes(batchFile);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to read {batchFile}: {ex.Message}");
            return Task.FromResult(3);
        }

        Neo.L2.L2BatchCommitment commitment;
        try
        {
            commitment = Neo.L2.Batch.BatchSerializer.Decode(bytes);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"batch decode failed: {ex.Message}");
            Console.Error.WriteLine("Submit aborted — fix the encoding before re-running.");
            return Task.FromResult(4);
        }

        Console.WriteLine($"Decoded batch from {batchFile} ({bytes.Length} bytes):");
        Console.WriteLine($"  chainId       : {commitment.ChainId}");
        Console.WriteLine($"  batchNumber   : {commitment.BatchNumber}");
        Console.WriteLine($"  blocks        : {commitment.FirstBlock}-{commitment.LastBlock}");
        Console.WriteLine($"  preStateRoot  : {commitment.PreStateRoot}");
        Console.WriteLine($"  postStateRoot : {commitment.PostStateRoot}");
        Console.WriteLine($"  proofType     : {commitment.ProofType} ({commitment.Proof.Length} bytes)");
        if (ArgUtil.HasFlag(args, "--broadcast"))
        {
            var settlementManagerValue = ArgUtil.Get(args, "--settlement-manager", "");
            if (!UInt160.TryParse(settlementManagerValue, out var settlementManager)
                || settlementManager == UInt160.Zero)
            {
                Console.Error.WriteLine("--settlement-manager <UInt160> is required with --broadcast");
                return Task.FromResult(5);
            }

            if (!TryParseHash256(args, "--l1-message-hash", out var l1MessageHash)
                || !TryParseHash256(args, "--block-context-hash", out var blockContextHash))
            {
                return Task.FromResult(6);
            }

            using var scriptBuilder = new ScriptBuilder();
            scriptBuilder.EmitDynamicCall(
                settlementManager,
                "submitBatch",
                CallFlags.All,
                bytes,
                l1MessageHash.GetSpan().ToArray(),
                blockContextHash.GetSpan().ToArray());
            return OperatorTransactionBroadcaster.BroadcastAsync(
                args,
                scriptBuilder.ToArray(),
                $"batch {commitment.ChainId}/{commitment.BatchNumber} submission");
        }
        Console.WriteLine();
        Console.WriteLine($"Validation passed. Add --broadcast plus RPC, network, contract, public-input hashes, and a signer to submit on L1.");
        return Task.FromResult(0);
    }

    private static bool TryParseHash256(string[] args, string option, out UInt256 hash)
    {
        var value = ArgUtil.Get(args, option, "");
        if (UInt256.TryParse(value, out var parsed) && parsed is not null && parsed != UInt256.Zero)
        {
            hash = parsed;
            return true;
        }
        Console.Error.WriteLine($"{option} <non-zero UInt256> is required with --broadcast");
        hash = UInt256.Zero;
        return false;
    }
}
