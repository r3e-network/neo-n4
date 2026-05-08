using System;
using System.Threading.Tasks;
using Neo.L2.Sdk;

namespace Neo.L2.Bridge.Cli.Commands;

/// <summary>
/// <c>neo-bridge query-deposit</c> — looks up an L1 deposit's consumption status on the
/// L2 via <see cref="L2RpcClient.GetDepositStatusAsync(uint, ulong, System.Threading.CancellationToken)"/>.
/// </summary>
internal static class QueryDepositCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var endpoint = Args.RequireString(args, "--endpoint");
        var chainId = Args.RequireUInt(args, "--chain-id");
        var sourceChain = Args.RequireUInt(args, "--source-chain");
        var nonce = Args.RequireULong(args, "--nonce");
        if (endpoint is null || chainId is null || sourceChain is null || nonce is null) return 1;

        try
        {
            using var client = new L2RpcClient(endpoint, chainId.Value);
            var status = await client.GetDepositStatusAsync(sourceChain.Value, nonce.Value);
            if (status is null)
            {
                Console.WriteLine($"deposit (sourceChain={sourceChain}, nonce={nonce}) not tracked by L2 chain {chainId}");
                return 2;
            }
            Console.WriteLine($"deposit (sourceChain={status.SourceChainId}, nonce={status.Nonce})");
            Console.WriteLine($"  consumedOnL2    = {status.ConsumedOnL2}");
            Console.WriteLine($"  includedInBatch = {status.IncludedInBatch?.ToString() ?? "(pending)"}");
            return 0;
        }
        catch (L2RpcException ex)
        {
            Console.Error.WriteLine($"❌ L2 RPC failure ({ex.GetType().Name}): {ex.Message}");
            return 3;
        }
    }
}
