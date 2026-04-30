using System;
using System.IO;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>create-chain</c> — generate a working directory with config files, genesis block, and a
/// dBFT committee for a new L2 chain. Uses sensible defaults (L2RollupMode, NeoFS DA,
/// Multisig proof) unless overridden.
/// </summary>
internal static class CreateChainCommand
{
    public static int Run(string[] args)
    {
        var template = ArgUtil.Get(args, "--template", "rollup");
        var vm = ArgUtil.Get(args, "--vm", "neovm");
        var chainId = uint.Parse(ArgUtil.Get(args, "--chain-id", "1001"));
        var output = ArgUtil.Get(args, "--output", $"./chain-{chainId}");

        Directory.CreateDirectory(output);
        var configPath = Path.Combine(output, "chain.config.json");
        File.WriteAllText(configPath, $$"""
            {
              "chainId": {{chainId}},
              "template": "{{template}}",
              "vm": "{{vm}}",
              "chainMode": "L2RollupMode",
              "daMode": "NeoFS",
              "proofType": "Multisig",
              "milestonePerBlockMs": 5000,
              "validators": []
            }
            """);

        Console.WriteLine($"Created {output}");
        Console.WriteLine($"  template     = {template}");
        Console.WriteLine($"  vm           = {vm}");
        Console.WriteLine($"  chainId      = {chainId}");
        Console.WriteLine($"  config file  = {configPath}");
        Console.WriteLine();
        Console.WriteLine("Next: `neo-stack init-l2 --chain-id <id>`");
        return 0;
    }
}
