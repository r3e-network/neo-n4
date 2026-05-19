using System;
using System.IO;
using System.Threading.Tasks;
using Neo.Json;

namespace Neo.Hub.Deploy;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0) return PrintHelp();

        try
        {
            return args[0] switch
            {
                "plan" => PlanCommand.Run(args[1..]),
                "verify" => await VerifyCommand.RunAsync(args[1..]),
                "scaffold" => ScaffoldCommand.Run(args[1..]),
                "help" or "--help" or "-h" => PrintHelp(),
                _ => Unknown(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static int PrintHelp()
    {
        Console.WriteLine("""
            neo-hub-deploy — declarative NeoHub L1 deployment tool

            Usage:
              neo-hub-deploy <subcommand> [options]

            Subcommands:
              scaffold --output <path>            Write a starter DeployPlan covering the 23-step NeoHub production bundle.
              plan --plan <path> --output <path>  Topologically sort + resolve a plan; emit a deploy bundle.
              verify --plan <path> --rpc <url>    Confirm each plan step's nef + manifest exist on disk (exit 2 on any missing).
              help                                Show this message.

            See doc.md §3.2 (NeoHub) for the contract suite layout.
            """);
        return 0;
    }

    private static int Unknown(string sub)
    {
        Console.Error.WriteLine($"Unknown subcommand: {sub}");
        return PrintHelp();
    }

}

internal static class ArgUtil
{
    public static string Get(string[] args, string name, string defaultValue)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return defaultValue;
    }
}
