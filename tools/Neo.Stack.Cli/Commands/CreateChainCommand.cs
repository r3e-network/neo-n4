using System;
using System.IO;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>create-chain</c> — generate a working directory with config files for a new L2 chain.
/// The <c>--template</c> flag picks a starting point (rollup / zk-rollup / validium /
/// sidechain) that drives chainMode + daMode + proofType + security label defaults
/// (doc.md §6 + §16.2). Operators can edit <c>chain.config.json</c> after creation to
/// tweak any field.
/// </summary>
internal static class CreateChainCommand
{
    /// <summary>Per-template starting defaults (doc.md §6 + §16.2).</summary>
    private readonly record struct Template(
        string ChainMode, string DaMode, string ProofType, string SecurityLevel,
        string SequencerModel, string ExitModel,
        bool GatewayEnabled, bool PermissionlessExit);

    private static Template Resolve(string templateName) => templateName switch
    {
        "zk-rollup" => new Template(
            ChainMode: "L2RollupMode", DaMode: "L1", ProofType: "Zk",
            SecurityLevel: "Validity", SequencerModel: "DbftCommittee",
            ExitModel: "Permissionless", GatewayEnabled: true, PermissionlessExit: true),
        "validium" => new Template(
            ChainMode: "L2ValidiumMode", DaMode: "NeoFS", ProofType: "Zk",
            SecurityLevel: "Validium", SequencerModel: "DbftCommittee",
            ExitModel: "Delayed", GatewayEnabled: true, PermissionlessExit: false),
        "sidechain" => new Template(
            ChainMode: "SidechainMode", DaMode: "External", ProofType: "None",
            SecurityLevel: "Sidechain", SequencerModel: "DbftCommittee",
            ExitModel: "Permissionless", GatewayEnabled: false, PermissionlessExit: true),
        // "rollup" (default): optimistic challenge window, L1 DA, dBFT committee, delayed exit
        _ => new Template(
            ChainMode: "L2RollupMode", DaMode: "L1", ProofType: "Optimistic",
            SecurityLevel: "Optimistic", SequencerModel: "DbftCommittee",
            ExitModel: "Delayed", GatewayEnabled: false, PermissionlessExit: true),
    };

    public static int Run(string[] args)
    {
        var templateName = ArgUtil.Get(args, "--template", "rollup");
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

        var t = Resolve(templateName);
        Directory.CreateDirectory(output);
        var configPath = Path.Combine(output, "chain.config.json");
        File.WriteAllText(configPath, $$"""
            {
              "chainId": {{chainId}},
              "template": "{{templateName}}",
              "vm": "{{vm}}",
              "chainMode": "{{t.ChainMode}}",
              "daMode": "{{t.DaMode}}",
              "proofType": "{{t.ProofType}}",
              "securityLevel": "{{t.SecurityLevel}}",
              "sequencerModel": "{{t.SequencerModel}}",
              "exitModel": "{{t.ExitModel}}",
              "gatewayEnabled": {{(t.GatewayEnabled ? "true" : "false")}},
              "permissionlessExit": {{(t.PermissionlessExit ? "true" : "false")}},
              "milestonePerBlockMs": 5000,
              "validators": []
            }
            """);

        Console.WriteLine($"Created {output}");
        Console.WriteLine($"  template       = {templateName}");
        Console.WriteLine($"  vm             = {vm}");
        Console.WriteLine($"  chainId        = {chainId}");
        Console.WriteLine($"  chainMode      = {t.ChainMode}");
        Console.WriteLine($"  daMode         = {t.DaMode}");
        Console.WriteLine($"  proofType      = {t.ProofType}");
        Console.WriteLine($"  securityLevel  = {t.SecurityLevel}");
        Console.WriteLine($"  sequencerModel = {t.SequencerModel}");
        Console.WriteLine($"  exitModel      = {t.ExitModel}");
        Console.WriteLine($"  gateway        = {(t.GatewayEnabled ? "enabled" : "disabled")}");
        Console.WriteLine($"  exit policy    = {(t.PermissionlessExit ? "permissionless" : "operator-gated")}");
        Console.WriteLine($"  config file    = {configPath}");
        Console.WriteLine();
        Console.WriteLine($"Templates: rollup (default), zk-rollup, validium, sidechain");
        Console.WriteLine($"Next: `neo-stack init-l2 --chain-id {chainId}`");
        return 0;
    }
}
