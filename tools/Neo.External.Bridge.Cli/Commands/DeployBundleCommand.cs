using System;
using System.Collections.Generic;

namespace Neo.External.Bridge.Cli.Commands;

/// <summary>
/// <c>deploy-bundle</c> — given Neo + Eth deployment hashes and the
/// committee identity, print the ordered dual-side wire-up call sequence
/// the operator's wallet executes to make the bridge live.
/// </summary>
/// <remarks>
/// <para>The order matters. If the operator runs the calls out of order
/// they get "no committee registered for externalChainId" or "no verifier
/// registered" errors at first use. Specifically:</para>
/// <list type="number">
///   <item>Deploy <c>MpcCommitteeVerifier</c>, <c>ExternalBridgeRegistry</c>,
///     <c>ExternalBridgeEscrow</c> on Neo. Deploy
///     <c>NeoExternalBridgeRouter</c> on Eth. (This CLI doesn't run those
///     deploys — operator uses <c>neo-hub-deploy</c> / Foundry.)</item>
///   <item><c>MpcCommitteeVerifier.RegisterCommittee</c> with the
///     committee blob from <c>committee-blob</c>.</item>
///   <item><c>ExternalBridgeRegistry.RegisterVerifier</c> binding
///     <c>externalChainId</c> → <c>MpcCommitteeVerifier</c> hash.</item>
///   <item><c>ExternalBridgeEscrow.SetRegistry</c> pointing at the
///     registry contract.</item>
///   <item><c>NeoExternalBridgeRouter.setCommittee</c> with the matching
///     Eth-address list.</item>
/// </list>
/// <para>The bundle is printed as a checklist, not as wallet-ready hex,
/// because the wallet integration is operator-specific (NeoLine vs Neon
/// vs hardware wallet vs HSM signer). Each line names the contract,
/// method, and args; the operator's wallet does the canonical encoding.</para>
/// </remarks>
internal static class DeployBundleCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var rawChainId = Args.RequireString(args, "--external-chain-id");
        if (rawChainId is null) return 1;
        if (!TryParseHexU32(rawChainId, out var externalChainId))
        {
            Console.Error.WriteLine($"❌ --external-chain-id must be a 4-byte hex value (e.g. 0xE0000001), got '{rawChainId}'");
            return 1;
        }
        if ((externalChainId & 0xFF00_0000U) != 0xE000_0000U)
        {
            Console.Error.WriteLine($"❌ --external-chain-id 0x{externalChainId:X8} must use the 0xE0_xx_xx_xx foreign-namespace prefix");
            return 1;
        }

        var verifier = Args.RequireUInt160(args, "--verifier");
        var registry = Args.RequireUInt160(args, "--registry");
        var escrow = Args.RequireUInt160(args, "--escrow");
        var ethRouter = Args.RequireString(args, "--eth-router");
        var threshold = Args.RequireString(args, "--threshold");
        var blobHex = Args.RequireString(args, "--committee-blob");
        var ethAddrsRaw = Args.RequireString(args, "--eth-addresses");

        if (verifier is null || registry is null || escrow is null
            || ethRouter is null || threshold is null
            || blobHex is null || ethAddrsRaw is null) return 1;

        if (!TryDecodeHexBytes("--eth-router", ethRouter, "20-byte hex address", expectedBytes: 20, out var ethRouterBytes))
            return 1;
        var normalizedEthRouter = "0x" + GenKeyCommand.HexLower(ethRouterBytes);

        if (!byte.TryParse(threshold, out var thresholdByte) || thresholdByte == 0)
        {
            Console.Error.WriteLine($"❌ --threshold must be a positive byte (1..255), got '{threshold}'");
            return 1;
        }

        var ethAddrs = ethAddrsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries
                                              | StringSplitOptions.TrimEntries);
        if (ethAddrs.Length == 0)
        {
            Console.Error.WriteLine("❌ --eth-addresses must list at least one 20-byte hex address");
            return 1;
        }

        var normalizedEthAddrs = new List<string>(ethAddrs.Length);
        for (var i = 0; i < ethAddrs.Length; i++)
        {
            if (!TryDecodeHexBytes($"--eth-addresses[{i}]", ethAddrs[i], "20-byte hex address", expectedBytes: 20, out var addrBytes))
                return 1;
            normalizedEthAddrs.Add("0x" + GenKeyCommand.HexLower(addrBytes));
        }

        // Cross-check the committee blob length matches the Eth-address count.
        if (!TryDecodeHexBytes("--committee-blob", blobHex, "hex-encoded committee blob", expectedBytes: null, out var blobBytes))
            return 1;
        var normalizedBlob = GenKeyCommand.HexLower(blobBytes);
        var blob = blobHex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? blobHex.Substring(2) : blobHex;
        if (blob.Length % 66 != 0)
        {
            Console.Error.WriteLine($"❌ committee-blob hex length {blob.Length} is not a multiple of 66 (33B per pubkey × 2 hex chars)");
            return 1;
        }
        var blobCount = blob.Length / 66;
        if (blobCount != ethAddrs.Length)
        {
            Console.Error.WriteLine($"❌ committee-blob has {blobCount} pubkeys but --eth-addresses lists {ethAddrs.Length} — they must agree");
            return 1;
        }
        if (thresholdByte > blobCount)
        {
            Console.Error.WriteLine($"❌ --threshold {thresholdByte} > committee size {blobCount}");
            return 1;
        }

        Console.WriteLine($"# Bridge deploy bundle for externalChainId 0x{externalChainId:X8}");
        Console.WriteLine($"#   committee size = {blobCount}, threshold = {thresholdByte}");
        Console.WriteLine($"#   Neo verifier  = {verifier}");
        Console.WriteLine($"#   Neo registry  = {registry}");
        Console.WriteLine($"#   Neo escrow    = {escrow}");
        Console.WriteLine($"#   Eth router    = {normalizedEthRouter}");
        Console.WriteLine();
        Console.WriteLine("Pre-flight: contracts already deployed at the addresses above? Yes [ ]");
        Console.WriteLine("            (use neo-hub-deploy for Neo, forge create for Eth)");
        Console.WriteLine();
        Console.WriteLine("Step 1 — register the committee on the Neo verifier:");
        Console.WriteLine($"  contract: {verifier}");
        Console.WriteLine($"  method:   RegisterCommittee");
        Console.WriteLine($"  args:");
        Console.WriteLine($"    externalChainId = 0x{externalChainId:X8}");
        Console.WriteLine($"    threshold       = {thresholdByte}");
        Console.WriteLine($"    curveTag        = 1   # secp256k1");
        Console.WriteLine($"    committeeBlob   = 0x{normalizedBlob}");
        Console.WriteLine();
        Console.WriteLine("Step 2 — register the verifier on the Neo registry:");
        Console.WriteLine($"  contract: {registry}");
        Console.WriteLine($"  method:   RegisterVerifier");
        Console.WriteLine($"  args:");
        Console.WriteLine($"    externalChainId = 0x{externalChainId:X8}");
        Console.WriteLine($"    verifier        = {verifier}");
        Console.WriteLine($"    bridgeKind      = 1   # MPC");
        Console.WriteLine();
        Console.WriteLine("Step 3 — wire the registry into the escrow:");
        Console.WriteLine($"  contract: {escrow}");
        Console.WriteLine($"  method:   SetRegistry");
        Console.WriteLine($"  args:");
        Console.WriteLine($"    registry = {registry}");
        Console.WriteLine();
        Console.WriteLine("Step 4 — register the committee on the Eth router:");
        Console.WriteLine($"  contract: {normalizedEthRouter}");
        Console.WriteLine($"  method:   setCommittee");
        Console.WriteLine($"  args:");
        Console.WriteLine($"    members[] = [");
        for (var i = 0; i < normalizedEthAddrs.Count; i++)
        {
            var sep = i + 1 < normalizedEthAddrs.Count ? "," : "";
            Console.WriteLine($"        {normalizedEthAddrs[i]}{sep}");
        }
        Console.WriteLine("    ]");
        Console.WriteLine($"    threshold = {thresholdByte}");
        Console.WriteLine();
        Console.WriteLine("After step 4, the bridge is live. To verify, lock 1 wei from a test");
        Console.WriteLine("Eth account via NeoExternalBridgeRouter.lockETHAndSend and watch for");
        Console.WriteLine("CrossChainInboundFinalized on the Neo escrow within ~1 minute.");
        return 0;
    }

    private static bool TryParseHexU32(string s, out uint v)
    {
        var hex = s.StartsWith("0x") || s.StartsWith("0X") ? s.Substring(2) : s;
        if (uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out v)) return true;
        v = 0;
        return false;
    }

    private static bool TryDecodeHexBytes(string name, string raw, string valueDescription, int? expectedBytes, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        var hex = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? raw.Substring(2) : raw;
        if (hex.Length == 0)
        {
            Console.Error.WriteLine($"❌ {name} must be a non-empty {valueDescription}");
            return false;
        }
        if (hex.Length % 2 != 0)
        {
            Console.Error.WriteLine($"❌ {name} must have even-length hex");
            return false;
        }
        if (expectedBytes is not null && hex.Length != expectedBytes.Value * 2)
        {
            Console.Error.WriteLine($"❌ {name} must be a {valueDescription}, got {hex.Length / 2} bytes");
            return false;
        }

        bytes = new byte[hex.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(hex.AsSpan(2 * i, 2), System.Globalization.NumberStyles.HexNumber, null, out var v))
            {
                Console.Error.WriteLine($"❌ {name} invalid hex byte at offset {2 * i}");
                return false;
            }
            bytes[i] = v;
        }
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: neo-external-bridge deploy-bundle
                --external-chain-id 0xE0000002         # Sepolia (or 0xE0000001 mainnet, etc.)
                --verifier <UInt160>                    # NeoHub.MpcCommitteeVerifier hash
                --registry <UInt160>                    # NeoHub.ExternalBridgeRegistry hash
                --escrow <UInt160>                      # NeoHub.ExternalBridgeEscrow hash
                --eth-router 0xabc...                   # NeoExternalBridgeRouter on Eth
                --threshold 3
                --committee-blob 0x...                  # output of `committee-blob` Neo line
                --eth-addresses 0xaaa...,0xbbb...,...   # output of `committee-blob` Eth list

            Prints the ordered dual-side wire-up call sequence the operator's
            wallet executes to make the bridge live. Cross-checks that
            committee-blob and eth-addresses agree on size + threshold ≤ size.

            The bundle is printed as a checklist, not wallet-ready hex —
            wallet integration is operator-specific.
            """);
    }
}
