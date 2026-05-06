using System;
using System.Threading.Tasks;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        var subcommand = args[0];
        var rest = args[1..];
        // Top-level try/catch matches neo-hub-deploy's pattern. Without it, a
        // FormatException from int.Parse, an IOException from Directory.CreateDirectory,
        // or any subcommand-thrown InvalidDataException leaks as a raw stack trace —
        // unhelpful for an operator running from a terminal.
        try
        {
            return subcommand switch
            {
                "create-chain" => CreateChainCommand.Run(rest),
                "init-l2" => InitL2Command.Run(rest),
                "register-chain" => await RegisterChainCommand.RunAsync(rest),
                "deploy-bridge-adapter" => await DeployBridgeAdapterCommand.RunAsync(rest),
                "start-sequencer" => StartSequencerCommand.Run(rest),
                "start-batcher" => StartBatcherCommand.Run(rest),
                "start-prover" => StartProverCommand.Run(rest),
                "submit-batch" => await SubmitBatchCommand.RunAsync(rest),
                "validate" => ValidateChainConfigCommand.Run(rest),
                "--help" or "-h" or "help" => PrintHelp(),
                _ => Unknown(subcommand),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int Unknown(string subcommand)
    {
        Console.Error.WriteLine($"Unknown subcommand: {subcommand}");
        return PrintHelp();
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            neo-stack — launch framework CLI for Neo Elastic Network L2 chains

            Usage:
              neo-stack <subcommand> [options]

            Subcommands:
              create-chain          Generate config + genesis for a new L2 chain
              init-l2               Initialize an L2 node working directory
              register-chain        Register the L2 with NeoHub.ChainRegistry on L1
              deploy-bridge-adapter Deploy the chain's bridge adapter on L1
              start-sequencer       Start the L2 sequencer (dBFT committee)
              start-batcher         Start the batcher
              start-prover          Start the prover
              submit-batch          Submit a sealed batch to NeoHub
              validate              Sanity-check a chain.config.json (enum names, required fields)
              help                  Show this message

            See doc.md §14.2 for the full design.
            """);
        return 0;
    }
}
