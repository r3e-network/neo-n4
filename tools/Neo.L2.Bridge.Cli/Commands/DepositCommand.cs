using System;
using Neo;

namespace Neo.L2.Bridge.Cli.Commands;

/// <summary>
/// <c>neo-bridge deposit</c> — emits the canonical <c>SharedBridge.Deposit</c> invocation
/// script as hex. The operator pastes the hex into their wallet (Neo-Express, Neon
/// Wallet, NeoLine, custom HSM tooling) to sign + broadcast against the L1 RPC.
/// </summary>
internal static class DepositCommand
{
    public static int Run(string[] args)
    {
        var bridge = Args.RequireUInt160(args, "--bridge");
        var asset = Args.RequireUInt160(args, "--asset");
        var amount = Args.RequireBigInteger(args, "--amount");
        var targetChain = Args.RequireUInt(args, "--target-chain");
        var recipient = Args.RequireUInt160(args, "--recipient");
        if (bridge is null || asset is null || amount is null || targetChain is null || recipient is null)
            return 1;

        byte[] script;
        try
        {
            script = InvocationBuilder.BuildDeposit(bridge, asset, amount.Value, targetChain.Value, recipient);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"❌ {ex.Message}");
            return 1;
        }

        Console.WriteLine($"# canonical SharedBridge.Deposit invocation");
        Console.WriteLine($"#   bridge      = {bridge}");
        Console.WriteLine($"#   asset       = {asset}");
        Console.WriteLine($"#   amount      = {amount}");
        Console.WriteLine($"#   targetChain = {targetChain}");
        Console.WriteLine($"#   l2Recipient = {recipient}");
        Console.WriteLine($"# script bytes  = {script.Length}");
        Console.WriteLine();
        Console.WriteLine("script (hex, copy-paste into wallet):");
        Console.WriteLine(Convert.ToHexString(script).ToLowerInvariant());
        Console.WriteLine();
        Console.WriteLine("Next steps:");
        Console.WriteLine("  1. Decode this script in your wallet (Neo-Express / Neon Wallet / etc.).");
        Console.WriteLine("  2. The wallet will request approval for the embedded transfer call —");
        Console.WriteLine($"     you must have approved the bridge ({bridge}) to spend {amount} of {asset}.");
        Console.WriteLine("  3. Sign + broadcast on L1.");
        Console.WriteLine("  4. The deposit nonce returned by the contract is what the L2 will use to");
        Console.WriteLine("     match this deposit when scanning the queue (see `neo-bridge query-deposit`).");
        return 0;
    }
}
