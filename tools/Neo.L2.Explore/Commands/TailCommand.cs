using System;
using System.Net.Http;
using System.Threading.Tasks;
using Neo.L2.Sdk;

namespace Neo.L2.Explore.Commands;

/// <summary>
/// <c>neo-l2-explore tail [N]</c> — walks the last <c>N</c> batches (default 5)
/// in descending order, printing a one-line summary per batch.
/// </summary>
/// <remarks>
/// "Last N" is approximated by walking down from the highest batch number whose
/// <see cref="L2RpcClient.GetBatchAsync"/> response is non-null. The first probe
/// reads the latest state root via <see cref="L2RpcClient.GetLatestStateRootAsync"/>
/// to confirm there's any state at all, then GetBatchAsync calls each prior batch
/// number until the requested count is reached or batch 0 is hit.
/// </remarks>
internal static class TailCommand
{
    public static async Task<int> RunAsync(string[] args, Func<string, uint, HttpClient?, L2RpcClient> clientFactory)
    {
        var parsed = CommonArgs.Parse(args);
        if (parsed is null) return 1;
        var (endpoint, chainId) = parsed.Value;

        var requested = CommonArgs.ParsePositionalUlong(args, "tail count") ?? 5UL;
        if (requested == 0)
        {
            Console.Error.WriteLine("❌ tail count must be at least 1");
            return 1;
        }

        using var client = clientFactory(endpoint, chainId, null);

        // Walk down from a probe point: start at a high number and decrement until
        // we hit a sealed batch (server returns non-null). Without a `getlatestbatch`
        // RPC, this is the cheapest way to discover the head batch — bounded by the
        // requested count (so a stale or empty chain doesn't loop forever).
        var head = await TailCommand_DiscoverHead.DiscoverAsync(client, requested);
        if (head is null)
        {
            Console.Error.WriteLine("❌ no sealed batches found on this chain");
            return 2;
        }

        Console.WriteLine($"chain {chainId} @ {endpoint} — last {requested} batches");
        ulong remaining = requested;
        ulong cursor = head.Value;
        while (remaining > 0)
        {
            var batch = await client.GetBatchAsync(cursor);
            if (batch is null) break;
            Console.WriteLine($"  #{batch.BatchNumber,-6} blocks={batch.FirstBlock}..{batch.LastBlock,-6}  postStateRoot={batch.PostStateRoot}");
            if (cursor == 0) break;
            cursor--;
            remaining--;
        }
        return 0;
    }
}
