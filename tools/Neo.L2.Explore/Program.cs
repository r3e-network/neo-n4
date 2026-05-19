using System;
using System.Net.Http;
using System.Threading.Tasks;
using Neo.L2.Explore.Commands;
using Neo.L2.Sdk;

namespace Neo.L2.Explore;

/// <summary>
/// <c>neo-l2-explore</c> — terminal block explorer for an L2 node.
/// </summary>
/// <remarks>
/// Subcommands:
/// <list type="bullet">
///   <item><c>label</c> — print the §16.2 5-dimension security label.</item>
///   <item><c>batch &lt;n&gt;</c> — print full canonical commitment for one batch.</item>
///   <item><c>tail [N]</c> — walk the last N batches (default 5) printing one-line summaries.</item>
///   <item><c>audit [N]</c> — verify state-root continuity across the last N sealed batches.</item>
/// </list>
/// All subcommands take <c>--endpoint &lt;url&gt;</c> + <c>--chain-id &lt;N&gt;</c>.
/// </remarks>
public static class Program
{
    /// <summary>Process-entry. Returns a process exit code: 0 on success, non-zero
    /// on usage error / RPC failure / continuity violation. See class remarks
    /// for subcommand listing and exit-code semantics.</summary>
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage();
            return 0;
        }

        var sub = args[0];
        var rest = args[1..];
        try
        {
            return sub switch
            {
                "label" => await LabelCommand.RunAsync(rest, NewClient),
                "batch" => await BatchCommand.RunAsync(rest, NewClient),
                "tail" => await TailCommand.RunAsync(rest, NewClient),
                "audit" => await AuditCommand.RunAsync(rest, NewClient),
                _ => UsageError($"unknown subcommand '{sub}'"),
            };
        }
        catch (L2RpcException ex)
        {
            // Each command's caller already gets to react in-process — but anything that
            // bubbles all the way here gets one canonical "RPC failed" surface so an
            // operator's terminal sees a clear cause line + a non-zero exit code.
            Console.Error.WriteLine($"❌ RPC failed: {ex.Message}");
            return 3;
        }
    }

    /// <summary>Default <see cref="L2RpcClient"/> factory; tests substitute their own.</summary>
    public static L2RpcClient NewClient(string endpoint, uint chainId, HttpClient? http) =>
        new L2RpcClient(endpoint, chainId, http);

    internal static int UsageError(string message)
    {
        Console.Error.WriteLine($"❌ {message}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: neo-l2-explore <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  label                          Print the §16.2 security label.");
        Console.WriteLine("  batch <n>                      Print full canonical commitment for batch <n>.");
        Console.WriteLine("  tail [N]                       Walk the last N batches (default 5).");
        Console.WriteLine("  audit [N]                      Verify state-root continuity across the last N batches.");
        Console.WriteLine();
        Console.WriteLine("Common options:");
        Console.WriteLine("  --endpoint <url>               L2 node RPC endpoint (e.g. http://localhost:30332).");
        Console.WriteLine("  --chain-id <N>                 Expected chain id; cross-checked on every response.");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  neo-l2-explore label   --endpoint http://localhost:30332 --chain-id 1099");
        Console.WriteLine("  neo-l2-explore batch 7 --endpoint http://localhost:30332 --chain-id 1099");
        Console.WriteLine("  neo-l2-explore tail 10 --endpoint http://localhost:30332 --chain-id 1099");
        Console.WriteLine("  neo-l2-explore audit   --endpoint http://localhost:30332 --chain-id 1099");
    }
}
