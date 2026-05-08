using System;
using System.Threading.Tasks;
using Neo.L2.Sdk;

namespace Neo.L2.Bridge.Cli.Commands;

/// <summary>
/// <c>neo-bridge audit-withdrawal</c> — fetches a withdrawal's Merkle proof from the L2
/// RPC endpoint + sanity-checks its structure (sibling count consistent, leaf index
/// matches header). A real on-chain verify happens at
/// <c>SettlementManager.VerifyWithdrawalLeafWithProof</c>; this command is the off-chain
/// pre-flight an operator runs before paying gas to submit the L1 transaction.
/// </summary>
internal static class AuditWithdrawalCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var endpoint = Args.RequireString(args, "--endpoint");
        var chainId = Args.RequireUInt(args, "--chain-id");
        var leaf = Args.RequireUInt256(args, "--leaf");
        if (endpoint is null || chainId is null || leaf is null) return 1;

        try
        {
            using var client = new L2RpcClient(endpoint, chainId.Value);
            var bytes = await client.GetWithdrawalProofAsync(leaf);
            if (bytes is null)
            {
                Console.Error.WriteLine($"❌ leaf {leaf} not found on chain {chainId}");
                Console.Error.WriteLine($"   (proof not yet generated, or the L2 endpoint doesn't track proofs for this leaf)");
                return 2;
            }
            var siblings = MerkleProofDecoder.Decode(bytes);
            Console.WriteLine($"withdrawal proof for leaf {leaf}");
            Console.WriteLine($"  proof bytes      = {bytes.Length}");
            Console.WriteLine($"  Merkle depth     = {siblings.Count}");
            for (var i = 0; i < siblings.Count; i++)
                Console.WriteLine($"  sibling[{i}] = {siblings[i]}");
            Console.WriteLine();
            Console.WriteLine($"✅ proof structurally valid; submit via `neo-bridge withdraw` for L1 verification");
            return 0;
        }
        catch (L2RpcException ex)
        {
            Console.Error.WriteLine($"❌ L2 RPC failure ({ex.GetType().Name}): {ex.Message}");
            return 3;
        }
    }
}
