using System;
using System.Net.Http;
using System.Threading.Tasks;
using Neo.L2.Sdk;

namespace Neo.L2.Explore.Commands;

/// <summary>
/// <c>neo-l2-explore label</c> — fetches and prints the full §16.2 5-dimension
/// security label for the L2 the operator points at.
/// </summary>
internal static class LabelCommand
{
    public static async Task<int> RunAsync(string[] args, Func<string, uint, HttpClient?, L2RpcClient> clientFactory)
    {
        var parsed = CommonArgs.Parse(args);
        if (parsed is null) return 1;
        var (endpoint, chainId) = parsed.Value;

        using var client = clientFactory(endpoint, chainId, null);
        var label = await client.GetSecurityLabelAsync();

        Console.WriteLine($"chain {label.ChainId} @ {endpoint}");
        Console.WriteLine($"  securityLevel  = {label.SecurityLevel}");
        Console.WriteLine($"  daMode         = {label.DAMode}");
        Console.WriteLine($"  gatewayEnabled = {label.GatewayEnabled}");
        Console.WriteLine($"  sequencer      = {label.Sequencer}");
        Console.WriteLine($"  exit           = {label.Exit}");
        return 0;
    }
}
