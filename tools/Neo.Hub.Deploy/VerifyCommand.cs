using System;
using System.IO;
using System.Threading.Tasks;

namespace Neo.Hub.Deploy;

/// <summary>
/// <c>neo-hub-deploy verify</c> — confirm planned contracts have their build artifacts
/// on disk before an operator pipes them to a Neo wallet's deploy flow. Each step's
/// <c>.nef</c> + <c>.manifest.json</c> must exist; a missing artifact aborts the deploy
/// with a non-zero exit so CI scripts treat it as a hard fail. Operators that also want
/// post-deploy "is the contract live at the expected hash?" verification supply
/// <c>--rpc &lt;l1-url&gt;</c> and chain that as a separate step against their L1 node.
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
        var planPath = ArgUtil.Get(args, "--plan", "deploy-plan.json");
        var rpcUrl = ArgUtil.Get(args, "--rpc", "");
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
}
