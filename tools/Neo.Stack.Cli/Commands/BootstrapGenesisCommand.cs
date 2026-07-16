using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Neo.L2.Executor;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>bootstrap-genesis</c> — run Neo native-contract genesis + SP1 contract-binding
/// initialization against a durable (or ephemeral) L2 state store and persist the
/// authenticated non-zero initial state root for <c>register-chain</c>.
/// </summary>
/// <remarks>
/// See doc.md §8 / §14.2 and <c>docs/launching-an-l2.md</c>. The produced root is the
/// batch-1 trust anchor written into <c>ChainRegistry.registerChain</c>.
/// </remarks>
internal static class BootstrapGenesisCommand
{
    public const string ManifestFileName = "genesis-manifest.json";
    public const string RelativeStateDir = "data/state";

    public static int Run(string[] args)
    {
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsedChainId))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsedChainId, "--chain-id");
        var outputFlag = ArgUtil.Get(args, "--output", "");
        var path = outputFlag.Length > 0
            ? outputFlag
            : ArgUtil.Get(args, "--path", $"./chain-{chainId}");
        var force = ArgUtil.HasFlag(args, "--force");
        var ephemeral = ArgUtil.HasFlag(args, "--ephemeral");

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Chain dir not found: {path}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return 1;
        }
        var configPath = Path.Combine(path, "chain.config.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Missing chain.config.json: {configPath}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return 2;
        }

        var manifestPath = Path.Combine(path, ManifestFileName);
        if (File.Exists(manifestPath) && !force)
        {
            Console.Error.WriteLine($"Genesis manifest already exists: {manifestPath}");
            Console.Error.WriteLine("Pass --force to re-bootstrap (refuses to clobber a durable state dir).");
            return 3;
        }

        UInt256 initialRoot;
        string statePath;
        string storeKind;
        if (ephemeral)
        {
            statePath = "";
            storeKind = "InMemoryKeyValueStore";
            using var memory = new InMemoryKeyValueStore();
            initialRoot = Bootstrap(memory);
        }
        else
        {
            statePath = Path.GetFullPath(Path.Combine(path, RelativeStateDir));
            if (Directory.Exists(statePath)
                && Directory.EnumerateFileSystemEntries(statePath).Any()
                && !force)
            {
                Console.Error.WriteLine($"State directory is non-empty: {statePath}");
                Console.Error.WriteLine("Pass --force only after reviewing whether the prior genesis may be discarded.");
                return 3;
            }
            Directory.CreateDirectory(statePath);
            storeKind = "RocksDbKeyValueStore";
            using var rocks = new RocksDbKeyValueStore(statePath);
            initialRoot = Bootstrap(rocks);
        }

        if (initialRoot.Equals(UInt256.Zero))
        {
            Console.Error.WriteLine("bootstrap produced a zero genesis state root");
            return 4;
        }

        var manifest = new GenesisManifest(
            SchemaVersion: 1,
            ChainId: chainId,
            InitialStateRoot: initialRoot.ToString(),
            Profile: "NeoVMGenesisBootstrap+Sp1StateWitnessSource.InitializeGenesisContractBindings",
            StoreKind: storeKind,
            StateDirectory: string.IsNullOrEmpty(statePath) ? null : statePath,
            CreatedAtUtc: DateTime.UtcNow.ToString("o"));
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(
                manifest,
                new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                })
                + Environment.NewLine);

        Console.WriteLine($"Bootstrapped genesis for chain {chainId}");
        Console.WriteLine($"  store            = {storeKind}");
        if (!string.IsNullOrEmpty(statePath))
            Console.WriteLine($"  state directory  = {statePath}");
        Console.WriteLine($"  initialStateRoot = {initialRoot}");
        Console.WriteLine($"  manifest         = {manifestPath}");
        Console.WriteLine();
        Console.WriteLine("Next:");
        Console.WriteLine(
            $"  neo-stack register-chain --chain-id {chainId} --output {path} \\");
        Console.WriteLine(
            $"    --from-deploy-report <neo-hub-deploy evidence JSON> \\");
        Console.WriteLine(
            $"    --genesis-manifest {manifestPath}");
        return 0;
    }

    /// <summary>Read a previously written genesis-manifest.json and return the root.</summary>
    public static UInt256 ReadInitialStateRoot(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException("genesis manifest not found", manifestPath);
        using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if ((!doc.RootElement.TryGetProperty("initialStateRoot", out var rootEl)
                && !doc.RootElement.TryGetProperty("InitialStateRoot", out rootEl))
            || rootEl.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("genesis manifest missing initialStateRoot");
        var text = rootEl.GetString()
            ?? throw new InvalidDataException("genesis manifest initialStateRoot is null");
        if (string.IsNullOrWhiteSpace(text)
            || !UInt256.TryParse(text, out var root)
            || root is null
            || root.Equals(UInt256.Zero))
            throw new InvalidDataException(
                "genesis manifest initialStateRoot must be a non-zero UInt256");
        return root;
    }

    internal static UInt256 Bootstrap(IL2KeyValueStore state)
    {
        ArgumentNullException.ThrowIfNull(state);
        NeoVMGenesisBootstrap.Run(state, NeoVMGenesisBootstrap.DefaultBootstrapSettings);
        return Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
    }

    private sealed record GenesisManifest(
        int SchemaVersion,
        uint ChainId,
        string InitialStateRoot,
        string Profile,
        string StoreKind,
        string? StateDirectory,
        string CreatedAtUtc);
}
