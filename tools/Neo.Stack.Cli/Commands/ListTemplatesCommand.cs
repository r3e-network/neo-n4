using System;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>list-templates</c> — print the four chain-config templates with their §16.2 security
/// label dimensions + use-case descriptions. Discoverability for operators evaluating
/// which template to pass to <c>create-chain</c> / <c>new-l2</c> without having to read
/// source or run create-chain repeatedly.
/// </summary>
/// <remarks>
/// Without arguments, prints all templates as a table + per-template detail blocks.
/// With <c>--template &lt;name&gt;</c>, prints just the named template's full details
/// (use-case + dimensions + sample command). Unknown names exit 1 with the valid
/// list so a typo gets caught.
/// </remarks>
internal static class ListTemplatesCommand
{
    public static int Run(string[] args)
    {
        var nameFilter = ArgUtil.Get(args, "--template", "");
        if (nameFilter.Length > 0)
        {
            if (!TemplateCatalog.IsKnown(nameFilter))
            {
                Console.Error.WriteLine(
                    $"--template '{nameFilter}' not recognized. Valid names: {TemplateCatalog.ValidNames}.");
                return 1;
            }
            PrintTemplate(TemplateCatalog.Resolve(nameFilter));
            return 0;
        }

        // No filter: print the summary table + per-template blocks.
        Console.WriteLine("neo-stack chain templates (doc.md §6 + §16.2)");
        Console.WriteLine();
        Console.WriteLine($"  {"Name",-12} | Security    | DA       | Proof      | Exit            | Tagline");
        Console.WriteLine($"  {new string('-', 12)} + {new string('-', 11)} + {new string('-', 8)} + {new string('-', 10)} + {new string('-', 15)} + {new string('-', 60)}");
        foreach (var t in TemplateCatalog.All)
        {
            Console.WriteLine($"  {t.Name,-12} | {t.SecurityLevel,-11} | {t.DaMode,-8} | {t.ProofType,-10} | {t.ExitModel,-15} | {t.TagLine}");
        }
        Console.WriteLine();
        Console.WriteLine("Run `neo-stack list-templates --template <name>` for full per-template details.");
        Console.WriteLine($"Default template (used when --template is omitted): {TemplateCatalog.All[0].Name}");
        return 0;
    }

    private static void PrintTemplate(TemplateCatalog.Template t)
    {
        Console.WriteLine($"Template: {t.Name}");
        Console.WriteLine($"  chainMode      = {t.ChainMode}");
        Console.WriteLine($"  daMode         = {t.DaMode}");
        Console.WriteLine($"  proofType      = {t.ProofType}");
        Console.WriteLine($"  securityLevel  = {t.SecurityLevel}");
        Console.WriteLine($"  sequencerModel = {t.SequencerModel}");
        Console.WriteLine($"  exitModel      = {t.ExitModel}");
        Console.WriteLine($"  gateway        = {(t.GatewayEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"  exit policy    = {(t.PermissionlessExit ? "permissionless" : "operator-gated")}");
        Console.WriteLine();
        Console.WriteLine($"  Use case:");
        Console.WriteLine($"    {t.UseCase}");
        Console.WriteLine();
        Console.WriteLine($"  Try:");
        Console.WriteLine($"    neo-stack new-l2 --name MyChain --chain-id 1099 --template {t.Name}");
    }
}
