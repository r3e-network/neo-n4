using System;

namespace Neo.L2.Explore.Commands;

/// <summary>
/// Shared --endpoint + --chain-id parsing for every subcommand. Returns
/// <see langword="null"/> on missing/invalid args (caller has already printed
/// the diagnostic via <see cref="Console.Error"/>).
/// </summary>
internal static class CommonArgs
{
    public static (string endpoint, uint chainId)? Parse(string[] args)
    {
        var endpoint = ArgGet(args, "--endpoint");
        var rawChainId = ArgGet(args, "--chain-id");
        if (endpoint is null)
        {
            Console.Error.WriteLine("❌ missing --endpoint <url>");
            return null;
        }
        if (rawChainId is null)
        {
            Console.Error.WriteLine("❌ missing --chain-id <N>");
            return null;
        }
        if (!uint.TryParse(rawChainId, out var chainId))
        {
            Console.Error.WriteLine($"❌ --chain-id must be a non-negative integer, got '{rawChainId}'");
            return null;
        }
        return (endpoint, chainId);
    }

    public static string? ArgGet(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }

    public static ulong? ParsePositionalUlong(string[] args, string description)
    {
        // First positional arg that doesn't start with "--" — pre-flag tokens are
        // the subcommand args. After --endpoint / --chain-id are consumed, the
        // remaining positional is the count or batch number.
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith("--"))
            {
                i++; // skip its value
                continue;
            }
            if (!ulong.TryParse(a, out var n))
            {
                Console.Error.WriteLine($"❌ {description} must be a non-negative integer, got '{a}'");
                return null;
            }
            return n;
        }
        return null;
    }
}
