using System;
using System.IO;
using System.Security.Cryptography;
using Neo.Cryptography;
using Neo.Wallets;
// `ECCurve` lives in two namespaces (Neo.Cryptography.ECC + System.Security.Cryptography);
// alias to avoid the ambiguity. KeyPair takes Neo's curve.
using NeoECCurve = Neo.Cryptography.ECC.ECCurve;

namespace Neo.External.Bridge.Cli.Commands;

/// <summary>
/// <c>genkey</c> — generate a secp256k1 keypair and emit the canonical
/// identity bytes for both bridge sides.
/// </summary>
/// <remarks>
/// <para>Output:</para>
/// <list type="bullet">
///   <item><c>priv32</c> — 32-byte raw secp256k1 private key.</item>
///   <item><c>pub33</c> — 33-byte compressed pubkey. Goes into Neo's
///     <c>MpcCommitteeVerifier.RegisterCommittee</c> committee blob.</item>
///   <item><c>ethAddr</c> — 20-byte Eth address derived as
///     <c>keccak256(uncompressed_pubkey[1..])[12..32]</c>. Goes into
///     <c>NeoExternalBridgeRouter.setCommittee</c>.</item>
/// </list>
/// <para>The single keypair represents one watcher's identity on BOTH sides.
/// Both representations are the same secp256k1 public key — Neo stores the
/// compressed pubkey because <c>CryptoLib.VerifyWithECDsa</c> takes it
/// directly; Eth stores the address because <c>ecrecover</c>'s output is an
/// address.</para>
/// <para>By default writes priv key to a file (so it never appears on the
/// console / shell history) and prints pub + ethAddr to stdout. Pass
/// <c>--out priv.bin</c> to control the file location; <c>--print-priv</c>
/// to additionally echo the priv hex (NEVER do this in production —
/// command shells log).</para>
/// </remarks>
internal static class GenKeyCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] is "--help" or "-h")
        {
            PrintUsage();
            return 0;
        }

        var outPath = Args.Get(args, "--out") ?? "watcher.priv";
        var printPriv = HasFlag(args, "--print-priv");

        // Random 32-byte private key. KeyPair will reject if it lands above
        // the secp256k1 group order (1-in-2^128 chance — guard with a retry
        // loop just in case).
        byte[] priv;
        KeyPair key;
        while (true)
        {
            priv = RandomNumberGenerator.GetBytes(32);
            try
            {
                key = new KeyPair(priv, NeoECCurve.Secp256k1);
                break;
            }
            catch
            {
                // Reroll; vanishingly rare.
            }
        }

        var pub33 = key.PublicKey.EncodePoint(true);
        // Uncompressed = 0x04 || X(32) || Y(32). Strip the 0x04 prefix to get
        // the raw 64 bytes that Eth's address derivation hashes.
        var pubUncompressed = key.PublicKey.EncodePoint(false);
        var ethAddrFull = pubUncompressed.AsSpan(1).ToArray().Keccak256();
        var ethAddr = ethAddrFull.AsSpan(12, 20).ToArray();

        if (File.Exists(outPath))
        {
            Console.Error.WriteLine($"❌ refusing to overwrite existing file: {outPath}");
            Console.Error.WriteLine($"   delete it first or pass --out <new-path>");
            return 1;
        }
        if (!TryWritePrivateKey(outPath, priv))
            return 1;
        // Best-effort: tighten file permissions so a fresh-install umask (often
        // 022) doesn't leave the priv key world-readable. Skip on Windows where
        // File.SetUnixFileMode isn't supported (and POSIX modes don't apply).
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(outPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { /* best-effort */ }
        }

        Console.WriteLine($"# wrote private key to {outPath} (0600 if POSIX)");
        Console.WriteLine($"pub33   = 0x{HexLower(pub33)}");
        Console.WriteLine($"ethAddr = 0x{HexLower(ethAddr)}");
        if (printPriv)
        {
            Console.WriteLine($"priv32  = 0x{HexLower(priv)}");
            Console.Error.WriteLine("⚠ --print-priv echoed the private key. Make sure your shell history");
            Console.Error.WriteLine("  is not logged anywhere persistent (~/.bash_history, etc).");
        }
        Console.WriteLine();
        Console.WriteLine("Next: register this watcher in the committee. Use `committee-blob`");
        Console.WriteLine("with all watcher pub33s to build the Neo blob, and the ethAddr list");
        Console.WriteLine("for `NeoExternalBridgeRouter.setCommittee` on the Eth side.");
        return 0;
    }

    internal static string HexLower(byte[] b) => Convert.ToHexStringLower(b);

    private static bool TryWritePrivateKey(string outPath, byte[] priv)
    {
        try
        {
            using var output = new FileStream(outPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            output.Write(priv);
            return true;
        }
        catch (IOException) when (File.Exists(outPath))
        {
            Console.Error.WriteLine($"refusing to overwrite existing file: {outPath}");
            Console.Error.WriteLine("delete it first or pass --out <new-path>");
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Console.Error.WriteLine($"failed to create private key file '{outPath}': {ex.Message}");
            return false;
        }
    }

    private static bool HasFlag(string[] args, string flag)
    {
        for (var i = 0; i < args.Length; i++) if (args[i] == flag) return true;
        return false;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            Usage: neo-external-bridge genkey [--out path] [--print-priv]

            Generate a fresh secp256k1 keypair. Writes the 32-byte raw private
            key to --out (default: watcher.priv) and prints the 33-byte
            compressed pubkey + 20-byte Eth address to stdout.

            --out path      File to write the private key to (default: watcher.priv).
                            Refuses to overwrite an existing file.
            --print-priv    Also print the private key hex to stdout. NEVER do
                            this in production — your shell logs the line.
            """);
    }
}
