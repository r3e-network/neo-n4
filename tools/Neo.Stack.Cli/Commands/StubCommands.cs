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

internal static class StartSequencerCommand
{
    public static int Run(string[] args)
    {
        Console.WriteLine("Would start L2 sequencer (dBFT committee).");
        Console.WriteLine("In production this spawns neo-cli with the L2 plugins loaded.");
        return 0;
    }
}

internal static class StartBatcherCommand
{
    public static int Run(string[] args)
    {
        Console.WriteLine("Would start batcher (Neo.Plugins.L2Batch host).");
        return 0;
    }
}

internal static class StartProverCommand
{
    public static int Run(string[] args)
    {
        Console.WriteLine("Would start prover (Neo.Plugins.L2Prover host).");
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
        Console.WriteLine($"Would submit {batchFile} to NeoHub.SettlementManager.SubmitBatch.");
        return Task.FromResult(0);
    }
}
