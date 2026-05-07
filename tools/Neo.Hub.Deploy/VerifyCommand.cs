using System;
using System.IO;
using System.Threading.Tasks;

namespace Neo.Hub.Deploy;

/// <summary>
/// <c>neo-hub-deploy verify</c> — confirm planned contracts are deployed at expected
/// hashes. MVP version verifies that each step's <c>.nef</c> + <c>.manifest.json</c>
/// build artifacts exist on disk; production hook would also query the L1 RPC to
/// confirm each contract is deployed and its manifest matches the planned ABI.
/// </summary>
/// <remarks>
/// <para>
/// Exit codes:
/// </para>
/// <list type="bullet">
///   <item><description><c>0</c> — every step's nef + manifest are present.</description></item>
///   <item><description><c>1</c> — caller error (missing <c>--rpc</c> or unreadable plan).</description></item>
///   <item><description><c>2</c> — at least one step's nef or manifest is missing on disk. Operator can't
///     deploy without that artifact, so a non-zero exit lets a CI script treat it as a hard fail.</description></item>
/// </list>
/// </remarks>
public static class VerifyCommand
{
    /// <summary>Run the verify command. Public so tests can exercise it directly.</summary>
    public static async Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var planPath = ArgUtilLocal.Get(args, "--plan", "deploy-plan.json");
        var rpcUrl = ArgUtilLocal.Get(args, "--rpc", "");
        if (string.IsNullOrEmpty(rpcUrl))
        {
            Console.Error.WriteLine("--rpc <url> is required");
            return 1;
        }

        if (!File.Exists(planPath))
        {
            Console.Error.WriteLine($"plan file not found: {planPath}");
            return 1;
        }

        DeployPlan plan;
        try
        {
            plan = DeployPlan.FromJson(File.ReadAllText(planPath));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to parse {planPath}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Verifying {plan.Steps.Count} steps against {rpcUrl}...");
        var missing = 0;
        foreach (var step in plan.Steps)
        {
            var nefExists = File.Exists(step.NefPath);
            var manifestExists = File.Exists(step.ManifestPath);
            var ok = nefExists && manifestExists;
            if (!ok) missing++;
            Console.WriteLine($"  [{(ok ? "ok" : "missing")}] {step.Name}");
        }
        Console.WriteLine();
        Console.WriteLine($"  {plan.Steps.Count - missing} ok / {missing} missing of {plan.Steps.Count} total");
        await Task.Yield();
        return missing == 0 ? 0 : 2;
    }

    // Local --flag <value> parser. Same shape as the Program.cs ArgUtil helper, kept
    // here so this class doesn't depend on Program.cs's internal ArgUtil. (Public Run
    // method needs to compile without InternalsVisibleTo gymnastics.)
    private static class ArgUtilLocal
    {
        public static string Get(string[] args, string name, string defaultValue)
        {
            for (var i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name) return args[i + 1];
            }
            return defaultValue;
        }
    }
}
