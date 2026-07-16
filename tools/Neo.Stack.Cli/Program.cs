using System;
using System.Threading.Tasks;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var shutdown = OperatorShutdown.Create();
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
                "bootstrap-genesis" => BootstrapGenesisCommand.Run(rest),
                "register-chain" => await RegisterChainCommand.RunAsync(rest),
                "deploy-bridge-adapter" => await DeployBridgeAdapterCommand.RunAsync(rest),
                "start-sequencer" => await StartSequencerCommand.RunAsync(rest, cancellationToken: shutdown.Token),
                "start-batcher" => await StartBatcherCommand.RunAsync(rest, cancellationToken: shutdown.Token),
                "start-prover" => await StartProverCommand.RunAsync(rest, cancellationToken: shutdown.Token),
                "submit-batch" => await SubmitBatchCommand.RunAsync(rest),
                "validate" => ValidateChainConfigCommand.Run(rest),
                "scaffold-executor" => ScaffoldExecutorCommand.Run(rest),
                "new-l2" => NewL2Command.Run(rest),
                "list-templates" => ListTemplatesCommand.Run(rest),
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
              create-chain          Generate chain.config.json from a template (rollup / zk-rollup / validium / sidechain)
              init-l2               Initialize an L2 node working directory
                                    (optional --from-deploy-report installs L1 plugin configs)
              bootstrap-genesis     Bootstrap NeoVM + SP1 genesis state and write genesis-manifest.json
              register-chain        Validate or sign+broadcast NeoHub.ChainRegistry registration
                                    (supports --from-deploy-report; auto-uses genesis-manifest.json)
              deploy-bridge-adapter Configure canonical asset mappings on L1 and L2
              start-sequencer       Start the L2 sequencer (dBFT committee)
              start-batcher         Start the batcher
              start-prover          Start the prover
              submit-batch          Validate or sign+broadcast a sealed batch to NeoHub
              validate              Sanity-check a chain.config.json (enum names, required fields)
              scaffold-executor     Generate a starter custom-ITransactionExecutor project
              new-l2                Composite: create-chain + init-l2 + scaffold-executor --with-tests
              list-templates        Print the available chain-config templates + use-case descriptions
              help                  Show this message

            Signed L1 execution:
              Add --broadcast --rpc <url> --expected-network <magic> and set
              NEO_N4_OPERATOR_WIF (or select another variable with --wif-env).
              Optional --witness-scope CalledByEntry|Global (default CalledByEntry).
              Use Global for SharedBridge.Deposit and ForcedInclusion fee transfers
              (nested NEP-17). Production adapters use --signer-command with
              --signer-account, --signer-verification-script, and
              --signer-placeholder-invocation-script.
              Bridge mapping uses the corresponding --l1-* and --l2-* options.

            Operator processes:
              init-l2 accepts --node-config and --batcher-node-config and installs
              each reviewed config as config.json in its isolated deployment root.
              start-sequencer/start-batcher require --neo-cli <reviewed Neo.CLI path>
              (or NEO_N4_NEO_CLI); start-prover requires --prover <prove-batch path>
              (or NEO_N4_PROVE_BATCH). Add --dry-run to validate and print the exact
              argument-vector launch without starting a child process. Neo.CLI config
              and storage overrides are rejected; only --verbose may follow `--`.

            See doc.md §14.2 for the full design.
            """);
        return 0;
    }
}
