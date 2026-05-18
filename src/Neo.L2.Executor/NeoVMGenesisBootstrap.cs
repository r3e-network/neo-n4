using System;
using System.Linq;
using Neo.L2.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Executor;

/// <summary>
/// Genesis bootstrap for legacy NeoVM compatibility chains using
/// <see cref="ApplicationEngineTransactionExecutor"/>.
/// Runs the canonical Neo native-contract <c>OnPersist</c> + <c>PostPersist</c> flow once
/// against the L2's <see cref="IL2KeyValueStore"/> so subsequent transaction execution
/// has the native-contract state (PolicyContract, Ledger, etc.) it requires.
/// </summary>
/// <remarks>
/// <para>
/// This replicates the relevant slice of <c>NeoSystem.Blockchain.Initialize</c> without
/// the heavyweight Akka actor machinery — purely the script execution that populates
/// native state. Operators run this once at chain genesis, persist the resulting state
/// to their KV store, and never call it again.
/// </para>
/// <para>
/// Idempotent in the natural sense: re-running on an already-initialized store would
/// either no-op (native-contract state already present) or be caught by
/// <c>NativeContract.Ledger.Initialized</c>'s guard. The helper also exposes
/// <see cref="IsInitialized"/> so callers can pre-check.
/// </para>
/// </remarks>
public static class NeoVMGenesisBootstrap
{
    /// <summary>
    /// Minimal single-validator settings for genesis bootstrap in tests / devnets.
    /// Legacy NeoVM compatibility chains supply their own <see cref="ProtocolSettings"/>.
    /// </summary>
    public static readonly ProtocolSettings DefaultBootstrapSettings = ProtocolSettings.Default with
    {
        Network = 0x4F454E_4Cu,
        StandbyCommittee = new[]
        {
            Neo.Cryptography.ECC.ECPoint.Parse(
                "0278ed78c917797b637a7ed6e7a9d94e8c408444c41ee4c0a0f310a256b9271eda",
                Neo.Cryptography.ECC.ECCurve.Secp256r1),
        },
        ValidatorsCount = 1,
    };

    /// <summary>
    /// Run the genesis OnPersist + PostPersist flow against <paramref name="state"/>.
    /// After this returns, <see cref="ApplicationEngineTransactionExecutor"/> can run
    /// transaction scripts against the same KV store without FAULT'ing on missing
    /// native-contract state.
    /// </summary>
    /// <param name="state">L2 state KV store to bootstrap.</param>
    /// <param name="settings">
    /// Optional <see cref="ProtocolSettings"/>. Defaults to <see cref="ProtocolSettings.Default"/>.
    /// </param>
    public static void Run(IL2KeyValueStore state, ProtocolSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        settings ??= DefaultBootstrapSettings;
        if (settings.StandbyCommittee.Count == 0)
            throw new ArgumentException(
                "ProtocolSettings.StandbyCommittee must contain at least one validator. " +
                "Production deployments supply their L2's own ProtocolSettings; tests/devnets " +
                "can pass NeoVMGenesisBootstrap.DefaultBootstrapSettings for a minimal config.",
                nameof(settings));
        if (IsInitialized(state, settings)) return;

        // Build the genesis block (matches NeoSystem.CreateGenesisBlock semantics).
        var genesisBlock = BuildGenesisBlock(settings);
        var cache = new L2DataCacheAdapter(state);

        RunOnCache(cache, settings);

        cache.Commit();
    }

    /// <summary>
    /// Run the OnPersist + PostPersist scripts against an existing
    /// <see cref="DataCache"/>. Caller is responsible for committing the cache
    /// to the underlying store. Used internally by <see cref="Run"/> and
    /// directly by tests that need to inspect the cache state.
    /// </summary>
    public static void RunOnCache(DataCache cache, ProtocolSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        settings ??= DefaultBootstrapSettings;
        if (settings.StandbyCommittee.Count == 0)
            throw new ArgumentException(
                "ProtocolSettings.StandbyCommittee must contain at least one validator", nameof(settings));
        var genesisBlock = BuildGenesisBlock(settings);
        RunPersistTrigger(TriggerType.OnPersist, BuildOnPersistScript(), cache, genesisBlock, settings);
        RunPersistTrigger(TriggerType.PostPersist, BuildPostPersistScript(), cache, genesisBlock, settings);
    }

    /// <summary>
    /// True if <paramref name="state"/> has already been initialized via a prior
    /// <see cref="Run"/> call (or via a NeoSystem genesis-init that wrote to the
    /// same store).
    /// </summary>
    public static bool IsInitialized(IL2KeyValueStore state, ProtocolSettings? settings = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        // Direct probe of PolicyContract's ExecFeeFactor storage key:
        // [4B Id LE = 0xFFFFFFF9 (-7)][1B prefix 0x12]. If this key is in the
        // store, native-contract bootstrap has already run.
        // (We don't go through ApplicationEngine because gas=0 short-circuits
        // its constructor before reading PolicyContract — would yield false
        // positives on empty stores.)
        return state.Contains(stackalloc byte[] { 0xF9, 0xFF, 0xFF, 0xFF, 0x12 });
    }

    private static void RunPersistTrigger(TriggerType trigger, byte[] script, DataCache cache, Block block, ProtocolSettings settings)
    {
        // OnPersist + PostPersist are special: ApplicationEngine.Create with their
        // trigger type wires the native-contract-init code path. The first call
        // populates state; the second finalizes (Policy fee schedules, etc.).
        using var engine = ApplicationEngine.Create(trigger, container: null, snapshot: cache, persistingBlock: block, settings: settings, gas: 0);
        engine.LoadScript(script);
        if (engine.Execute() != VMState.HALT)
        {
            throw new InvalidOperationException(
                $"genesis bootstrap {trigger} faulted: {engine.FaultException?.Message ?? "unknown"}");
        }
    }

    private static byte[] BuildOnPersistScript()
    {
        using var sb = new ScriptBuilder();
        sb.EmitSysCall(ApplicationEngine.System_Contract_NativeOnPersist);
        return sb.ToArray();
    }

    private static byte[] BuildPostPersistScript()
    {
        using var sb = new ScriptBuilder();
        sb.EmitSysCall(ApplicationEngine.System_Contract_NativePostPersist);
        return sb.ToArray();
    }

    private static Block BuildGenesisBlock(ProtocolSettings settings)
    {
        // Mirrors NeoSystem.CreateGenesisBlock. The key field for native-contract
        // bootstrap is Index=0 + Timestamp + NextConsensus (used by Policy + Roles
        // native contracts to seed their initial state).
        return new Block
        {
            Header = new Header
            {
                PrevHash = UInt256.Zero,
                MerkleRoot = UInt256.Zero,
                Timestamp = GenesisTimestampMillis(),
                Nonce = 2083236893UL,
                Index = 0,
                PrimaryIndex = 0,
                NextConsensus = Contract.GetBFTAddress(settings.StandbyValidators),
                Witness = new Witness
                {
                    InvocationScript = ReadOnlyMemory<byte>.Empty,
                    VerificationScript = new[] { (byte)OpCode.PUSH1 },
                },
            },
            Transactions = Array.Empty<Transaction>(),
        };
    }

    private static ulong GenesisTimestampMillis()
    {
        // Match NeoSystem.CreateGenesisBlock literally: 2016-07-15 15:08:21 UTC in ms.
        var dt = new DateTime(2016, 7, 15, 15, 8, 21, DateTimeKind.Utc);
        var unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return (ulong)(dt - unixEpoch).TotalMilliseconds;
    }
}
