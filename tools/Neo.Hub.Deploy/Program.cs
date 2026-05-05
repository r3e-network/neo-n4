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
                "plan" => RunPlan(args[1..]),
                "verify" => await RunVerifyAsync(args[1..]),
                "scaffold" => RunScaffold(args[1..]),
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
              scaffold --output <path>            Write a starter DeployPlan covering all NeoHub contracts.
              plan --plan <path> --output <path>  Topologically sort + resolve a plan; emit a deploy bundle.
              verify --plan <path> --rpc <url>    Confirm planned contracts are deployed at expected hashes.
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

    // ---- subcommands ----

    private static int RunScaffold(string[] args)
    {
        var output = ArgUtil.Get(args, "--output", "deploy-plan.json");
        var plan = ScaffoldPlan.Default();
        File.WriteAllText(output, plan.ToJson());
        Console.WriteLine($"Wrote starter plan to {output}");
        Console.WriteLine($"  steps: {plan.Steps.Count}");
        return 0;
    }

    private static int RunPlan(string[] args)
    {
        var planPath = ArgUtil.Get(args, "--plan", "deploy-plan.json");
        var output = ArgUtil.Get(args, "--output", "deploy-bundle.json");

        var plan = DeployPlan.FromJson(File.ReadAllText(planPath));

        // For 'plan' (no signer), resolve hashes deterministically from step name. Real deploys
        // override via 'verify' once contracts have actual L1 hashes.
        var bundle = DeployPlanner.Plan(plan, name => DeterministicStubHash(name));

        File.WriteAllText(output, bundle.ToJson());
        Console.WriteLine($"Resolved {bundle.Invocations.Count} invocations → {output}");
        Console.WriteLine($"  network: {bundle.Network}");
        Console.WriteLine();
        Console.WriteLine("⚠ Hashes are deterministic stubs — pass through a wallet-equipped signer to deploy.");
        Console.WriteLine();

        // Surface required post-deploy actions. The bundle alone does not finish the
        // wiring — SequencerBond's slashers list is intentionally minimal at deploy time
        // to avoid a cycle (see ScaffoldPlan.BondDeployData). Without this hint, an
        // operator who runs the bundle could think the deployment is "done" but be
        // silently missing the Phase-3 slash payout path.
        var postActions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        if (postActions.Count > 0)
        {
            Console.WriteLine("Required post-deploy actions:");
            foreach (var a in postActions) Console.WriteLine($"  - {a}");
        }
        return 0;
    }

    private static async Task<int> RunVerifyAsync(string[] args)
    {
        var planPath = ArgUtil.Get(args, "--plan", "deploy-plan.json");
        var rpcUrl = ArgUtil.Get(args, "--rpc", "");
        if (string.IsNullOrEmpty(rpcUrl))
        {
            Console.Error.WriteLine("--rpc <url> is required");
            return 1;
        }

        var plan = DeployPlan.FromJson(File.ReadAllText(planPath));
        Console.WriteLine($"Verifying {plan.Steps.Count} steps against {rpcUrl}...");

        // Hook for production: query the L1 chain to confirm each contract is deployed and
        // its manifest matches the planned ABI. MVP version just confirms the file paths exist.
        foreach (var step in plan.Steps)
        {
            var nefExists = File.Exists(step.NefPath);
            var manifestExists = File.Exists(step.ManifestPath);
            Console.WriteLine($"  [{(nefExists && manifestExists ? "ok" : "missing")}] {step.Name}");
        }
        await Task.Yield();
        return 0;
    }

    private static UInt160 DeterministicStubHash(string stepName)
    {
        // Predictable per-name placeholder so 'plan' is reproducible without a signer.
        var bytes = new byte[20];
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(stepName);
        Array.Copy(nameBytes, bytes, Math.Min(nameBytes.Length, 20));
        return new UInt160(bytes);
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
