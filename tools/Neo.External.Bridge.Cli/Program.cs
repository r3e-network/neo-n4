using System;
using Neo.External.Bridge.Cli.Commands;

namespace Neo.External.Bridge.Cli;

/// <summary>
/// <c>neo-external-bridge</c> — operator CLI for the cross-foreign-chain
/// bridge (the system in <c>doc.md</c> §11.3 and
/// <c>docs/external-bridge-roadmap.md</c>). Three subcommands cover the
/// committee setup + deploy lifecycle:
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><c>genkey</c> — generate a secp256k1 keypair, emit the
///     33-byte compressed pubkey (Neo committee blob format) + 20-byte
///     Eth address (Eth router committee format) for the same identity.</item>
///   <item><c>committee-blob</c> — concatenate N pubkey hexes into the
///     <c>N × 33B</c> blob <c>MpcCommitteeVerifier.RegisterCommittee</c>
///     accepts, and emit the matching Eth-address list for
///     <c>NeoExternalBridgeRouter.setCommittee</c>.</item>
///   <item><c>deploy-bundle</c> — given Neo + Eth deployment addresses
///     and a committee, print the ordered dual-side wire-up calls
///     (<c>RegisterVerifier</c> on Neo, <c>RegisterCommittee</c> on Neo,
///     <c>SetRegistry</c> on the Neo escrow, <c>setCommittee</c> on the
///     Eth router) so an operator can execute the deploy in lock-step.</item>
/// </list>
/// <para>
/// No live RPC: same plan-printer pattern as <c>neo-bridge</c> /
/// <c>neo-l2-faucet</c>. The CLI emits canonical bytes / wallet-ready
/// plans; the operator's wallet of choice signs and submits.
/// </para>
/// </remarks>
public static class Program
{
    public static int Main(string[] args)
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
                "genkey" => GenKeyCommand.Run(rest),
                "committee-blob" => CommitteeBlobCommand.Run(rest),
                "deploy-bundle" => DeployBundleCommand.Run(rest),
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
        Console.WriteLine("""
            Usage: neo-external-bridge <subcommand> [options]

            Subcommands:
              genkey            Generate a secp256k1 keypair (priv32 + pub33 + ethAddr20).
              committee-blob    Concatenate N pubkeys into the MpcCommitteeVerifier
                                blob format + matching Eth address list.
              deploy-bundle     Print the dual-side (Neo + Eth) wire-up call sequence
                                for a freshly-deployed bridge.

            See docs/external-bridge-roadmap.md for the bridge architecture and
            doc.md §11.3 for the canonical wire-format spec.
            """);
    }
}
