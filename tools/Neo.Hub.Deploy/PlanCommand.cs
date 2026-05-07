using System;
using System.IO;
using System.Linq;

namespace Neo.Hub.Deploy;

/// <summary>
/// <c>neo-hub-deploy plan</c> — read a <see cref="DeployPlan"/> JSON, topologically
/// sort + resolve <c>$step:&lt;name&gt;</c> placeholders against deterministic
/// per-name stub hashes, and emit a <see cref="DeployBundle"/> JSON the operator's
/// wallet feeds to <c>ContractManagement.Deploy</c> in order. Also surfaces
/// post-deploy wiring actions (slasher registration, governance-controller wiring,
/// per-fraud-verifier informational notes).
/// </summary>
/// <remarks>
/// Exit codes: 0 on success, 1 on caller error (missing/unparseable plan,
/// unwriteable output, plan resolution failure — cycle, missing dependency,
/// duplicate step name).
/// </remarks>
public static class PlanCommand
{
    /// <summary>Run the plan subcommand. Public so tests can exercise it directly.</summary>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var planPath = ArgUtilLocal.Get(args, "--plan", "deploy-plan.json");
        var output = ArgUtilLocal.Get(args, "--output", "deploy-bundle.json");

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

        DeployBundle bundle;
        try
        {
            bundle = DeployPlanner.Plan(plan, name => DeterministicStubHash(name));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"plan resolution failed: {ex.Message}");
            return 1;
        }

        try
        {
            File.WriteAllText(output, bundle.ToJson());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to write {output}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Resolved {bundle.Invocations.Count} invocations → {output}");
        Console.WriteLine($"  network: {bundle.Network}");
        Console.WriteLine();
        Console.WriteLine("⚠ Hashes are deterministic stubs — pass through a wallet-equipped signer to deploy.");
        Console.WriteLine();

        // Surface required post-deploy actions. The bundle alone does not finish the
        // wiring — SequencerBond's slashers list is intentionally minimal at deploy time
        // to avoid a cycle. Without this hint, an operator who runs the bundle could
        // think the deployment is "done" but be silently missing the Phase-3 slash
        // payout path.
        var postActions = ScaffoldPlan.PostDeployActions(bundle).ToList();
        if (postActions.Count > 0)
        {
            Console.WriteLine("Required post-deploy actions:");
            foreach (var a in postActions) Console.WriteLine($"  - {a}");
        }
        return 0;
    }

    private static UInt160 DeterministicStubHash(string stepName)
    {
        var bytes = new byte[20];
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(stepName);
        Array.Copy(nameBytes, bytes, Math.Min(nameBytes.Length, 20));
        return new UInt160(bytes);
    }

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
