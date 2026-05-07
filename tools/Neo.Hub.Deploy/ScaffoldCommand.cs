using System;
using System.IO;

namespace Neo.Hub.Deploy;

/// <summary>
/// <c>neo-hub-deploy scaffold</c> — write a starter <see cref="DeployPlan"/> JSON
/// covering all 15 NeoHub contracts (13 core + GovernanceFraudVerifier v1/v2 +
/// RestrictedExecutionFraudVerifier v3). The output is operator-editable JSON;
/// operators replace the placeholder bond owner / asset hashes before piping it
/// through <c>plan</c>.
/// </summary>
/// <remarks>
/// Exit codes: 0 on success, 1 on caller error (unwriteable output path).
/// </remarks>
public static class ScaffoldCommand
{
    /// <summary>Run the scaffold subcommand. Public so tests can exercise it directly.</summary>
    public static int Run(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var output = ArgUtilLocal.Get(args, "--output", "deploy-plan.json");
        DeployPlan plan;
        try
        {
            plan = ScaffoldPlan.Default();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to build default plan: {ex.Message}");
            return 1;
        }

        try
        {
            File.WriteAllText(output, plan.ToJson());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"failed to write {output}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"Wrote starter plan to {output}");
        Console.WriteLine($"  steps: {plan.Steps.Count}");
        return 0;
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
