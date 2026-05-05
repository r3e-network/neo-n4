using System;
using System.Threading.Tasks;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// Subcommand placeholders. Each prints what it would do; future iterations wire them to
/// real RPC clients, plugin host processes, and signed L1 transactions.
/// </summary>
internal static class RegisterChainCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var l1 = ArgUtil.Get(args, "--l1", "neo-n3-testnet");
        var chainId = ArgUtil.Get(args, "--chain-id", "1001");
        Console.WriteLine($"Would register chain {chainId} with NeoHub on {l1}.");
        Console.WriteLine("(full L1 RPC submission lands in a future iteration; see doc.md §14.2.)");
        return Task.FromResult(0);
    }
}

internal static class DeployBridgeAdapterCommand
{
    public static Task<int> RunAsync(string[] args)
    {
        var chainId = ArgUtil.Get(args, "--chain-id", "1001");
        Console.WriteLine($"Would deploy bridge adapter for chain {chainId}.");
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
        chainDir = ArgUtil.Get(args, "--path", $"./chain-{chainId}");
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
        Console.WriteLine($"  blocks        : {commitment.FirstBlock}–{commitment.LastBlock}");
        Console.WriteLine($"  preStateRoot  : {commitment.PreStateRoot}");
        Console.WriteLine($"  postStateRoot : {commitment.PostStateRoot}");
        Console.WriteLine($"  proofType     : {commitment.ProofType} ({commitment.Proof.Length} bytes)");
        Console.WriteLine();
        Console.WriteLine($"Validation passed. Would submit to NeoHub.SettlementManager.SubmitBatch on the configured L1.");
        Console.WriteLine($"(L1 wallet integration is operator-specific — wire RpcSettlementClient + ISigner via Neo.L2.Settlement.Rpc.)");
        return Task.FromResult(0);
    }
}
