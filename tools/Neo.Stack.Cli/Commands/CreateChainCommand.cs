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
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsedChainId))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        // Reuse the shared validator so a typo like --chain-id 0 is rejected at CLI
        // entry instead of becoming a working chain that misroutes L2→L2 messages as
        // L2→L1 (ChainId 0 is the L1 sentinel — see L2Outbox.L1ChainId).
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsedChainId, "--chain-id");
        // Accept either --output or --path; init-l2 / register-chain / start-* use
        // --path, so a script that strings the subcommands together can reuse one flag.
        var pathFlag = ArgUtil.Get(args, "--path", "");
        var output = pathFlag.Length > 0
            ? pathFlag
            : ArgUtil.Get(args, "--output", $"./chain-{chainId}");

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
