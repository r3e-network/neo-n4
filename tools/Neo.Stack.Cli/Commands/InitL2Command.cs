using System;
using System.IO;

namespace Neo.Stack.Cli.Commands;

internal static class InitL2Command
{
    public static int Run(string[] args)
    {
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsedChainId))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        // Match create-chain's iter-123 fix: reject the L1-reserved 0 here too so the
        // operator sees the misconfig at init time, not when the L2 first tries to
        // route a cross-chain message.
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsedChainId, "--chain-id");
        var da = ArgUtil.Get(args, "--da", "neofs");
        // Accept either --path or --output; create-chain uses --output, so a script
        // that strings the subcommands together can reuse one flag.
        var outputFlag = ArgUtil.Get(args, "--output", "");
        var path = outputFlag.Length > 0
            ? outputFlag
            : ArgUtil.Get(args, "--path", $"./chain-{chainId}");

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Chain dir not found: {path}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return 1;
        }

        Directory.CreateDirectory(Path.Combine(path, "data"));
        Directory.CreateDirectory(Path.Combine(path, "logs"));
        Directory.CreateDirectory(Path.Combine(path, "Plugins"));

        Console.WriteLine($"Initialized L2 node at {path}");
        Console.WriteLine($"  data       = {path}/data");
        Console.WriteLine($"  logs       = {path}/logs");
        Console.WriteLine($"  plugins    = {path}/Plugins");
        Console.WriteLine($"  da mode    = {da}");
        return 0;
    }
}
