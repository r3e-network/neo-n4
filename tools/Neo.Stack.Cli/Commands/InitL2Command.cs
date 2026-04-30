using System;
using System.IO;

namespace Neo.Stack.Cli.Commands;

internal static class InitL2Command
{
    public static int Run(string[] args)
    {
        var chainId = uint.Parse(ArgUtil.Get(args, "--chain-id", "1001"));
        var da = ArgUtil.Get(args, "--da", "neofs");
        var path = ArgUtil.Get(args, "--path", $"./chain-{chainId}");

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
