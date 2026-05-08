using System;
using System.Collections.Generic;
using System.IO;
using Neo.Cryptography;
using Neo.Cryptography.ECC;

namespace Neo.External.Bridge.Cli.Commands;

/// <summary>
/// <c>committee-blob</c> — given N watcher pubkey hexes, emit:
/// <list type="bullet">
///   <item>The <c>N × 33B</c> committee blob the Neo
///     <c>MpcCommitteeVerifier.RegisterCommittee</c> accepts.</item>
///   <item>The matching list of 20-byte Eth addresses
///     <c>NeoExternalBridgeRouter.setCommittee</c> accepts.</item>
/// </list>
/// </summary>
/// <remarks>
/// <para>The two outputs are different encodings of THE SAME identities —
/// the Neo verifier stores compressed pubkeys (because
/// <c>CryptoLib.VerifyWithECDsa</c> takes them directly), while the Eth
/// router stores addresses (because <c>ecrecover</c>'s output is an
/// address). Operators must register both in lock-step or the bridge
/// rejects valid signatures on whichever side is misconfigured.</para>
/// <para>Pubkey hexes can come from <c>genkey</c>'s <c>pub33</c>
/// output. They go in canonical (registration) order — the Eth router
/// indexes into the array; the Neo verifier's bitmap dedup also
/// implicitly assumes a stable order.</para>
/// </remarks>
internal static class CommitteeBlobCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var pubsRaw = Args.Get(args, "--pubs");
        var pubsFile = Args.Get(args, "--pubs-file");
        if (pubsRaw is null && pubsFile is null)
        {
            Console.Error.WriteLine("❌ pass --pubs <hex,hex,...> or --pubs-file <path>");
            return 1;
        }
        if (pubsRaw is not null && pubsFile is not null)
        {
            Console.Error.WriteLine("❌ pass exactly one of --pubs / --pubs-file");
            return 1;
        }

        IEnumerable<string> pubHexes = pubsRaw is not null
            ? pubsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : File.ReadAllLines(pubsFile!);

        var pubs = new List<byte[]>();
        var ethAddrs = new List<byte[]>();
        var idx = 0;
        foreach (var raw in pubHexes)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) continue;
            var hex = trimmed.StartsWith("0x") ? trimmed.Substring(2) : trimmed;
            if (hex.Length != 66)
            {
                Console.Error.WriteLine(
                    $"❌ pubkey at index {idx} has hex length {hex.Length}, expected 66 (33B compressed secp256k1)");
                return 2;
            }
            var bytes = HexDecode(hex);
            if (bytes is null) return 2;

            // Verify the bytes are a valid secp256k1 point — catches typos
            // before they hit the committee blob and brick the verifier.
            ECPoint pubkey;
            try
            {
                pubkey = ECPoint.DecodePoint(bytes, ECCurve.Secp256k1);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ pubkey at index {idx} is not a valid secp256k1 point: {ex.Message}");
                return 2;
            }

            // Reject duplicates — two committee slots with the same identity
            // is at best a misconfig, at worst an attempt to inflate a
            // single signer's voting power.
            for (var j = 0; j < pubs.Count; j++)
            {
                if (BytesEqual(pubs[j], bytes))
                {
                    Console.Error.WriteLine(
                        $"❌ pubkey at index {idx} is a duplicate of index {j} — committee members must be distinct");
                    return 2;
                }
            }

            pubs.Add(bytes);
            // Derive Eth address.
            var pubUncompressed = pubkey.EncodePoint(false);
            var hash = pubUncompressed.AsSpan(1).ToArray().Keccak256();
            ethAddrs.Add(hash.AsSpan(12, 20).ToArray());
            idx++;
        }

        if (pubs.Count == 0)
        {
            Console.Error.WriteLine("❌ no pubkeys provided (empty input)");
            return 1;
        }
        if (pubs.Count > 64)
        {
            Console.Error.WriteLine($"❌ committee size {pubs.Count} exceeds MaxCommitteeSize 64");
            return 1;
        }

        // Concatenate into the canonical committeeBlob.
        var blob = new byte[pubs.Count * 33];
        for (var i = 0; i < pubs.Count; i++)
        {
            Array.Copy(pubs[i], 0, blob, i * 33, 33);
        }

        Console.WriteLine($"# committee size = {pubs.Count}");
        Console.WriteLine();
        Console.WriteLine("# Neo side — pass to MpcCommitteeVerifier.RegisterCommittee");
        Console.WriteLine($"#   threshold     = <set this to your M-of-N quorum>");
        Console.WriteLine($"#   curveTag      = 1   # 1 = secp256k1, 2 = ed25519");
        Console.WriteLine($"#   committeeBlob = (next line, hex):");
        Console.WriteLine($"0x{GenKeyCommand.HexLower(blob)}");
        Console.WriteLine();
        Console.WriteLine("# Eth side — pass to NeoExternalBridgeRouter.setCommittee");
        Console.WriteLine("#   address[]:");
        for (var i = 0; i < ethAddrs.Count; i++)
        {
            Console.WriteLine($"#     [{i}] = 0x{GenKeyCommand.HexLower(ethAddrs[i])}");
        }
        return 0;
    }

    private static byte[]? HexDecode(string s)
    {
        if (s.Length % 2 != 0) { Console.Error.WriteLine("❌ odd-length hex"); return null; }
        var bytes = new byte[s.Length / 2];
        for (var i = 0; i < bytes.Length; i++)
        {
            if (!byte.TryParse(s.AsSpan(2 * i, 2), System.Globalization.NumberStyles.HexNumber, null, out var v))
            {
                Console.Error.WriteLine($"❌ invalid hex byte at offset {2 * i}");
                return null;
            }
            bytes[i] = v;
        }
        return bytes;
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: neo-external-bridge committee-blob (--pubs <hex,hex,...> | --pubs-file <path>)

            Concatenate N watcher pubkeys into the committee blob format both
            sides accept. Outputs the Neo-side blob (hex) for
            MpcCommitteeVerifier.RegisterCommittee and the matching Eth-address
            list for NeoExternalBridgeRouter.setCommittee.

            --pubs       Comma-separated list of pub33 hex values (33B
                         compressed secp256k1 pubkey, with or without 0x).
            --pubs-file  Path to a file with one pub hex per line.

            Validates: each pubkey is a valid secp256k1 point, no duplicates,
            committee size ≤ 64 (MpcCommitteeVerifier.MaxCommitteeSize).
            """);
    }
}
