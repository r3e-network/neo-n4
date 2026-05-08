using System;
using System.Net.Http;
using System.Threading.Tasks;
using Neo.L2.Sdk;

namespace Neo.L2.Explore.Commands;

/// <summary>
/// <c>neo-l2-explore batch &lt;n&gt;</c> — prints the full canonical commitment
/// for batch <c>n</c>. Useful for an operator who wants to inspect what was
/// sealed at a specific batch height.
/// </summary>
internal static class BatchCommand
{
    public static async Task<int> RunAsync(string[] args, Func<string, uint, HttpClient?, L2RpcClient> clientFactory)
    {
        var parsed = CommonArgs.Parse(args);
        if (parsed is null) return 1;
        var (endpoint, chainId) = parsed.Value;

        var batchNumber = CommonArgs.ParsePositionalUlong(args, "batch number");
        if (batchNumber is null)
        {
            Console.Error.WriteLine("❌ batch number is required: neo-l2-explore batch <n>");
            return 1;
        }

        using var client = clientFactory(endpoint, chainId, null);
        var batch = await client.GetBatchAsync(batchNumber.Value);
        if (batch is null)
        {
            Console.Error.WriteLine($"❌ batch {batchNumber} not found on chain {chainId}");
            return 2;
        }

        Console.WriteLine($"batch #{batch.BatchNumber} on chain {batch.ChainId}");
        Console.WriteLine($"  blocks           = {batch.FirstBlock}..{batch.LastBlock}");
        Console.WriteLine($"  preStateRoot     = {batch.PreStateRoot}");
        Console.WriteLine($"  postStateRoot    = {batch.PostStateRoot}");
        Console.WriteLine($"  txRoot           = {batch.TxRoot}");
        Console.WriteLine($"  receiptRoot      = {batch.ReceiptRoot}");
        Console.WriteLine($"  withdrawalRoot   = {batch.WithdrawalRoot}");
        Console.WriteLine($"  l2ToL1MsgRoot    = {batch.L2ToL1MessageRoot}");
        Console.WriteLine($"  l2ToL2MsgRoot    = {batch.L2ToL2MessageRoot}");
        Console.WriteLine($"  daCommitment     = {batch.DACommitment}");
        Console.WriteLine($"  publicInputHash  = {batch.PublicInputHash}");
        Console.WriteLine($"  proofType        = {batch.ProofType}");
        Console.WriteLine($"  proof            = {batch.Proof.Length} bytes");
        Console.WriteLine($"  encodedSize      = {batch.EncodedWireFormat.Length} bytes");

        var status = await client.GetBatchStatusAsync(batchNumber.Value);
        Console.WriteLine($"  status           = {status.Status}");
        return 0;
    }
}
