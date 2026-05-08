using System;
using System.Net.Http;
using System.Threading.Tasks;
using Neo.L2.Sdk;

namespace Neo.L2.Explore.Commands;

/// <summary>
/// <c>neo-l2-explore audit [N]</c> — verifies state-root continuity across
/// the last <c>N</c> sealed batches (default 10). For each consecutive pair
/// <c>(prev, curr)</c>, asserts <c>curr.PreStateRoot == prev.PostStateRoot</c>.
/// </summary>
/// <remarks>
/// <para>
/// State-root continuity is a fundamental L2 invariant — the
/// <see cref="L2BatchView.PreStateRoot"/> of every batch MUST equal the
/// <see cref="L2BatchView.PostStateRoot"/> of the previous batch, otherwise
/// the chain has a state gap (lost transitions, equivocation, replay).
/// On-chain settlement enforces this for finalized batches, but a misbehaving
/// or pre-finalization sequencer can ship batches that violate it; this audit
/// catches those before settlement.
/// </para>
/// <para>
/// Exits 0 when the chain is continuous; 4 when a violation is found (the
/// diagnostic names the offending batch + prints both state roots).
/// </para>
/// </remarks>
internal static class AuditCommand
{
    public static async Task<int> RunAsync(string[] args, Func<string, uint, HttpClient?, L2RpcClient> clientFactory)
    {
        var parsed = CommonArgs.Parse(args);
        if (parsed is null) return 1;
        var (endpoint, chainId) = parsed.Value;

        var requested = CommonArgs.ParsePositionalUlong(args, "audit count") ?? 10UL;
        if (requested < 2)
        {
            Console.Error.WriteLine("❌ audit count must be at least 2 (need consecutive batches to compare)");
            return 1;
        }

        using var client = clientFactory(endpoint, chainId, null);

        var head = await TailCommand_DiscoverHead.DiscoverAsync(client, requested);
        if (head is null)
        {
            Console.Error.WriteLine("❌ no sealed batches found on this chain");
            return 2;
        }

        var startBatch = head.Value >= requested - 1 ? head.Value - (requested - 1) : 0UL;
        Console.WriteLine($"audit chain {chainId} @ {endpoint}: batches {startBatch}..{head} ({head.Value - startBatch + 1} batches)");

        L2BatchView? prev = null;
        ulong checkedPairs = 0;
        for (var i = startBatch; i <= head.Value; i++)
        {
            var batch = await client.GetBatchAsync(i);
            if (batch is null)
            {
                Console.Error.WriteLine($"❌ gap detected: batch {i} missing within audit range");
                return 4;
            }
            if (prev is not null)
            {
                if (batch.PreStateRoot != prev.PostStateRoot)
                {
                    Console.Error.WriteLine(
                        $"❌ state-root continuity violation at batch #{batch.BatchNumber}");
                    Console.Error.WriteLine($"   batch #{prev.BatchNumber}.postStateRoot = {prev.PostStateRoot}");
                    Console.Error.WriteLine($"   batch #{batch.BatchNumber}.preStateRoot = {batch.PreStateRoot}");
                    return 4;
                }
                checkedPairs++;
            }
            prev = batch;
        }
        Console.WriteLine($"✅ {checkedPairs} consecutive pairs continuous; final postStateRoot = {prev!.PostStateRoot}");
        return 0;
    }
}

/// <summary>
/// Internal head-discovery helper; lives next to <see cref="AuditCommand"/> + reused
/// by <c>TailCommand</c> via a shared internal type rather than a method on each.
/// </summary>
internal static class TailCommand_DiscoverHead
{
    public static async Task<ulong?> DiscoverAsync(L2RpcClient client, ulong cap)
    {
        ulong probe = 1;
        ulong best = 0;
        ulong absoluteCap = Math.Max(cap * 4, 16);
        while (probe <= absoluteCap)
        {
            var b = await client.GetBatchAsync(probe);
            if (b is null) break;
            best = probe;
            probe *= 2;
        }
        if (best == 0)
        {
            var zero = await client.GetBatchAsync(0);
            return zero is null ? null : 0;
        }
        ulong cursor = best + 1;
        while (true)
        {
            var b = await client.GetBatchAsync(cursor);
            if (b is null) break;
            best = cursor;
            cursor++;
        }
        return best;
    }
}
