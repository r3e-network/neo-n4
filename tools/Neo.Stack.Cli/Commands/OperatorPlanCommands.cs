using System;
using System.Threading.Tasks;
using Neo.L2;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// neo-stack subcommands that issue a structured operator plan rather than executing
/// against L1/L2 directly. Each one validates inputs, reads the chain config it would
/// act on, and prints the deterministic plan (target contract + args + numbered next
/// steps) so an operator can audit before signing. Production wiring (signed L1
/// transactions, running plugin processes) is operator-supplied; these subcommands
/// are the deterministic, auditable preflight side. Per IMPLEMENTATION_STATUS Phase 6,
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
        // Real L1 submission needs a wallet-equipped signer; this command outputs what
        // would be submitted so an operator can audit before sending.
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

        // If the operator supplied the four L1 contract hashes (discovered from the
        // neo-hub-deploy bundle output), encode the L2ChainConfig into the canonical
        // 91-byte wire format and print it as hex — directly pasteable into a wallet's
        // contract-call args. Without addresses, fall back to the legacy plan-only
        // output (JSON preview + numbered next steps).
        var operatorHash = ArgUtil.Get(args, "--operator", "");
        var verifierHash = ArgUtil.Get(args, "--verifier", "");
        var bridgeHash = ArgUtil.Get(args, "--bridge", "");
        var messageHash = ArgUtil.Get(args, "--message", "");

        if (operatorHash.Length > 0 && verifierHash.Length > 0
            && bridgeHash.Length > 0 && messageHash.Length > 0)
        {
            try
            {
                var config = L2ChainConfigJsonReader.FromJson(chainId, configJson,
                    operatorHash, verifierHash, bridgeHash, messageHash);
                var bytes = L2ChainConfigSerializer.Encode(config);
                Console.WriteLine($"  Args            : (chainId={chainId}, configBytes=<{bytes.Length} bytes>)");
                Console.WriteLine();
                Console.WriteLine($"  configBytes (hex, copy-pasteable):");
                Console.WriteLine($"  0x{Convert.ToHexString(bytes).ToLowerInvariant()}");
                Console.WriteLine();
                Console.WriteLine($"Next steps:");
                Console.WriteLine($"  1. Sign + submit registerChain({chainId}, 0x{Convert.ToHexString(bytes).ToLowerInvariant()}) via your wallet RPC");
                Console.WriteLine($"  2. Verify on L1 by calling ChainRegistry.isActive({chainId})");
                return Task.FromResult(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to build configBytes: {ex.Message}");
                return Task.FromResult(4);
            }
        }

        Console.WriteLine($"  Args            : (chainId={chainId}, configBytes=<encoded>)");
        Console.WriteLine($"  Config bytes    : {configJson.Length} chars (will be NEO-serialized to bytes)");
        Console.WriteLine();
        Console.WriteLine($"--- chain config preview ---");
        Console.WriteLine(configJson.Length > 600 ? configJson[..600] + "…" : configJson);
        Console.WriteLine($"--- end ---");
        Console.WriteLine();
        Console.WriteLine($"Next steps for production registration:");
        Console.WriteLine($"  1. Run `neo-hub-deploy scaffold` + `plan` to get the deploy bundle.");
        Console.WriteLine($"     Feed the bundle to your wallet — each ContractManagement.Deploy call");
        Console.WriteLine($"     returns the REAL on-chain contract hash (the bundle's own hashes are");
        Console.WriteLine($"     deterministic stubs, only valid for plan reproducibility).");
        Console.WriteLine($"  2. Re-run register-chain with the four wallet-returned hashes:");
        Console.WriteLine($"       neo-stack register-chain --chain-id {chainId} \\");
        Console.WriteLine($"         --operator <real hash> --verifier <real hash> \\");
        Console.WriteLine($"         --bridge <real hash> --message <real hash>");
        Console.WriteLine($"     to emit the canonical 91-byte configBytes hex (ready for wallet-side submission).");
        Console.WriteLine($"  3. Sign + submit registerChain({chainId}, <configBytes>) via your wallet RPC.");
        Console.WriteLine($"  4. Verify on L1 by calling ChainRegistry.isActive({chainId}).");
        Console.WriteLine();
        Console.WriteLine($"(Wallet integration is operator-specific — wire your signer via Neo.L2.Settlement.Rpc.RpcSettlementClient.)");
        return Task.FromResult(0);
    }

}

internal static class DeployBridgeAdapterCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsed))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return Task.FromResult(1);
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
                return Task.FromResult(2);
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
        return Task.FromResult(0);
    }
}

/// <summary>
/// Shared pre-flight checks for the start-* commands. Without these, an operator who
/// runs <c>start-sequencer</c> without first running <c>create-chain</c>/<c>init-l2</c>
/// would get a confusing error from the underlying plugin host. Surface the misconfig
/// here with a clear remediation instead.
/// </summary>
internal static class StartCommandPreflight
{
    public static int Verify(string[] args, string roleName, out string chainDir, out string configPath)
    {
        chainDir = ""; configPath = "";
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsed))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsed, "--chain-id");
        // Accept both --output (the create-chain default flag, matches the Quick-path
        // walkthrough in launching-an-l2.md) and --path (the original flag — kept for
        // backwards compatibility). --output takes precedence when both are supplied,
        // mirroring InitL2Command's pattern. Without this, the documented 5-command
        // path `neo-stack start-sequencer --chain-id N --output ./my-l2` would silently
        // fall back to ./chain-N and fail with "Chain dir not found".
        var outputFlag = ArgUtil.Get(args, "--output", "");
        chainDir = outputFlag.Length > 0
            ? outputFlag
            : ArgUtil.Get(args, "--path", $"./chain-{chainId}");
        if (!System.IO.Directory.Exists(chainDir))
        {
            Console.Error.WriteLine($"Chain dir not found: {chainDir}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` followed by `neo-stack init-l2 --chain-id <id>` first.");
            return 2;
        }
        configPath = System.IO.Path.Combine(chainDir, "chain.config.json");
        if (!System.IO.File.Exists(configPath))
        {
            Console.Error.WriteLine($"Missing config: {configPath}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return 3;
        }
        Console.WriteLine($"Pre-flight OK for {roleName}:");
        Console.WriteLine($"  chainDir   = {chainDir}");
        Console.WriteLine($"  configPath = {configPath}");
        return 0;
    }
}

internal static class StartSequencerCommand
{
    public static int Run(string[] args)
    {
        var rc = StartCommandPreflight.Verify(args, "sequencer", out _, out _);
        if (rc != 0) return rc;
        Console.WriteLine();
        Console.WriteLine("Compose with neo-cli:");
        Console.WriteLine("  1. Copy Neo.Plugins.L2Batch + Neo.Plugins.L2Prover + Neo.Plugins.L2Settlement into <chainDir>/Plugins");
        Console.WriteLine("  2. Set DBFTPlugin's validator selector to your ISequencerCommitteeProvider");
        Console.WriteLine("  3. neo-cli --datadir <chainDir>/data --plugins-dir <chainDir>/Plugins");
        return 0;
    }
}

internal static class StartBatcherCommand
{
    public static int Run(string[] args)
    {
        var rc = StartCommandPreflight.Verify(args, "batcher", out _, out _);
        if (rc != 0) return rc;
        Console.WriteLine();
        Console.WriteLine("The batcher is hosted by Neo.Plugins.L2Batch — load it via neo-cli on a Neo 4 sequencer node.");
        Console.WriteLine("It hooks Blockchain.Committed and seals batches via BatchSealer (configurable triggers: blocks, txs, age).");
        return 0;
    }
}

internal static class StartProverCommand
{
    public static int Run(string[] args)
    {
        var rc = StartCommandPreflight.Verify(args, "prover", out _, out _);
        if (rc != 0) return rc;
        Console.WriteLine();
        Console.WriteLine("The prover is hosted by Neo.Plugins.L2Prover — load it via neo-cli on a sequencer or dedicated prover node.");
        Console.WriteLine("ProofType is configured per chain: Multisig (Phase 0/1), Optimistic (Phase 3), Zk (Phase 4).");
        return 0;
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
        Console.WriteLine();
        Console.WriteLine($"Validation passed. Would submit to NeoHub.SettlementManager.SubmitBatch on the configured L1.");
        Console.WriteLine($"(L1 wallet integration is operator-specific — wire RpcSettlementClient + ISigner via Neo.L2.Settlement.Rpc.)");
        return Task.FromResult(0);
    }
}
