using System;
using System.IO;

namespace Neo.Hub.Deploy;

/// <summary>
/// <c>neo-hub-deploy scaffold</c> — write a starter <see cref="DeployPlan"/> JSON
/// covering the 24-step NeoHub production bundle (15 core, ContractZkVerifier,
/// its immutable SP1 terminal verifier, 2 fraud verifiers, and 5 external-bridge contracts).
/// The output is operator-editable JSON;
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
        var output = ArgUtil.Get(args, "--output", "deploy-plan.json");
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
}
