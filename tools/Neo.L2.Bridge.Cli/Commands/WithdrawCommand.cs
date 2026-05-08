using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Neo;
using Neo.L2.Sdk;

namespace Neo.L2.Bridge.Cli.Commands;

/// <summary>
/// <c>neo-bridge withdraw</c> — emits the canonical
/// <c>SharedBridge.FinalizeWithdrawalWithProof</c> invocation script, automatically
/// fetching the per-leaf Merkle proof from the L2 RPC endpoint.
/// </summary>
internal static class WithdrawCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        var bridge = Args.RequireUInt160(args, "--bridge");
        var chainId = Args.RequireUInt(args, "--chain-id");
        var batch = Args.RequireULong(args, "--batch");
        var leaf = Args.RequireUInt256(args, "--leaf");
        var leafIndex = Args.RequireULong(args, "--leaf-index");
        var asset = Args.RequireUInt160(args, "--asset");
        var recipient = Args.RequireUInt160(args, "--recipient");
        var amount = Args.RequireBigInteger(args, "--amount");
        var proofEndpoint = Args.RequireString(args, "--proof-endpoint");
        if (bridge is null || chainId is null || batch is null || leaf is null
            || leafIndex is null || asset is null || recipient is null || amount is null
            || proofEndpoint is null) return 1;

        // Fetch the canonical proof bytes from the L2 RPC endpoint.
        IReadOnlyList<UInt256> siblings;
        try
        {
            using var sdk = new L2RpcClient(proofEndpoint, chainId.Value);
            var proofBytes = await sdk.GetWithdrawalProofAsync(leaf);
            if (proofBytes is null)
            {
                Console.Error.WriteLine($"❌ no withdrawal proof returned by L2 RPC for leaf {leaf}");
                Console.Error.WriteLine($"   (the leaf may not be in a finalized batch yet, or the L2 endpoint isn't tracking proofs)");
                return 2;
            }
            siblings = MerkleProofDecoder.Decode(proofBytes);
        }
        catch (L2RpcException ex)
        {
            Console.Error.WriteLine($"❌ L2 RPC failure ({ex.GetType().Name}): {ex.Message}");
            return 3;
        }

        byte[] script;
        try
        {
            script = InvocationBuilder.BuildFinalizeWithdrawalWithProof(
                bridge, chainId.Value, batch.Value, leaf, siblings, leafIndex.Value,
                asset, recipient, amount.Value);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"❌ {ex.Message}");
            return 1;
        }

        Console.WriteLine($"# canonical SharedBridge.FinalizeWithdrawalWithProof invocation");
        Console.WriteLine($"#   bridge      = {bridge}");
        Console.WriteLine($"#   chainId     = {chainId}");
        Console.WriteLine($"#   batch       = {batch}");
        Console.WriteLine($"#   leaf        = {leaf}");
        Console.WriteLine($"#   leafIndex   = {leafIndex}");
        Console.WriteLine($"#   asset       = {asset}");
        Console.WriteLine($"#   recipient   = {recipient}");
        Console.WriteLine($"#   amount      = {amount}");
        Console.WriteLine($"#   siblings    = {siblings.Count} levels (depth {siblings.Count})");
        Console.WriteLine($"# script bytes  = {script.Length}");
        Console.WriteLine();
        Console.WriteLine("script (hex, copy-paste into wallet):");
        Console.WriteLine(Convert.ToHexString(script).ToLowerInvariant());
        return 0;
    }
}
