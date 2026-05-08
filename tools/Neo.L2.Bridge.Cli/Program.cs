using System;
using System.Threading.Tasks;
using Neo.L2.Bridge.Cli.Commands;

namespace Neo.L2.Bridge.Cli;

/// <summary>
/// <c>neo-bridge</c> — production bridge tool for Neo Elastic Network operators.
/// </summary>
/// <remarks>
/// Subcommands:
/// <list type="bullet">
///   <item><c>deposit</c> — emit canonical <c>SharedBridge.Deposit</c> invocation script (hex).</item>
///   <item><c>withdraw</c> — emit canonical <c>SharedBridge.FinalizeWithdrawalWithProof</c>
///         invocation script, fetching the per-leaf Merkle proof from the L2 RPC endpoint.</item>
///   <item><c>query-deposit</c> — query L2-side deposit consumption status via the SDK.</item>
///   <item><c>audit-withdrawal</c> — fetch + off-chain-verify a withdrawal Merkle proof
///         against a batch's withdrawal root.</item>
/// </list>
/// <para>
/// No built-in signer: production deployments use HSMs or cold wallets, the CLI emits
/// the canonical invocation hex the operator's wallet of choice signs and submits. This
/// matches the existing <c>neo-stack register-chain</c> pattern.
/// </para>
/// </remarks>
public static class Program
{
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
                "deposit" => DepositCommand.Run(rest),
                "withdraw" => await WithdrawCommand.RunAsync(rest),
                "query-deposit" => await QueryDepositCommand.RunAsync(rest),
                "audit-withdrawal" => await AuditWithdrawalCommand.RunAsync(rest),
                _ => UsageError($"unknown subcommand '{sub}'"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"❌ {ex.GetType().Name}: {ex.Message}");
            return 3;
        }
    }

    internal static int UsageError(string message)
    {
        Console.Error.WriteLine($"❌ {message}");
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: neo-bridge <subcommand> [options]");
        Console.WriteLine();
        Console.WriteLine("Subcommands:");
        Console.WriteLine("  deposit               Emit canonical SharedBridge.Deposit invocation hex.");
        Console.WriteLine("  withdraw              Emit canonical FinalizeWithdrawalWithProof invocation hex");
        Console.WriteLine("                        (fetches the Merkle proof from --proof-endpoint).");
        Console.WriteLine("  query-deposit         Query L2 deposit consumption status.");
        Console.WriteLine("  audit-withdrawal      Off-chain Merkle-verify a withdrawal leaf is in a batch.");
        Console.WriteLine();
        Console.WriteLine("Common deposit options:");
        Console.WriteLine("  --bridge <hash>           SharedBridge contract hash (UInt160).");
        Console.WriteLine("  --asset <hash>            Asset to deposit (UInt160; NEP-17 token).");
        Console.WriteLine("  --amount <N>              Quantity to lock in escrow (BigInteger; raw units).");
        Console.WriteLine("  --target-chain <id>       Destination L2 chainId (uint > 0).");
        Console.WriteLine("  --recipient <addr>        L2-side recipient address (UInt160).");
        Console.WriteLine();
        Console.WriteLine("Common withdraw options (in addition to --bridge):");
        Console.WriteLine("  --chain-id <id>           Source L2 chainId.");
        Console.WriteLine("  --batch <N>               Finalized batch number containing the withdrawal.");
        Console.WriteLine("  --leaf <hex>              Withdrawal leaf hash (UInt256).");
        Console.WriteLine("  --leaf-index <N>          Leaf's 0-based index in the batch's withdrawal Merkle tree.");
        Console.WriteLine("  --asset <hash>            L1-side asset (UInt160).");
        Console.WriteLine("  --recipient <addr>        L1-side recipient (UInt160).");
        Console.WriteLine("  --amount <N>              Amount (BigInteger).");
        Console.WriteLine("  --proof-endpoint <url>    L2 RPC endpoint to fetch the Merkle proof from.");
        Console.WriteLine();
        Console.WriteLine("Query / audit options:");
        Console.WriteLine("  --endpoint <url>          L2 RPC endpoint.");
        Console.WriteLine("  --chain-id <N>            L2 chainId served by the endpoint.");
        Console.WriteLine("  --source-chain <N>        L1 deposit's source chainId (for query-deposit).");
        Console.WriteLine("  --nonce <N>               Deposit nonce (for query-deposit).");
        Console.WriteLine("  --leaf <hex>              Withdrawal leaf hash (for audit-withdrawal).");
    }
}
