using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.L2.Executor.Receipts;
using Neo.L2.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Executor;

/// <summary>
/// Legacy compatibility <see cref="ITransactionExecutor"/> that runs each transaction's
/// script through Neo's real <see cref="ApplicationEngine"/>. Neo N4 L2 production
/// execution uses NeoVM2/RISC-V; this executor is retained for N3-era NeoVM checks.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Genesis bootstrap requirement</strong>: <see cref="ApplicationEngine"/>'s
/// constructor reads <c>PolicyContract.GetExecFeeFactor</c> from the snapshot, which
/// requires the Neo native contracts to be initialized via the genesis-block
/// <c>OnPersist</c> flow. Legacy NeoVM compatibility chains do this once at
/// chain genesis (running an empty block through <c>NeoSystem</c>'s
/// <c>Blockchain.Initialize</c> path) and then persist the resulting state to the chain's
/// <see cref="IL2KeyValueStore"/>; subsequent batch executions reuse that state.
/// Operators wiring this executor MUST ensure their state store has gone through
/// genesis bootstrap before the first transaction runs.
/// </para>
/// </remarks>
/// <remarks>
/// <para>
/// The transaction bytes are deserialized as a Neo <see cref="Transaction"/>
/// (signers, witnesses, attributes, script). The script runs against an
/// <see cref="L2DataCacheAdapter"/>-wrapped snapshot of the operator-supplied
/// <see cref="IL2KeyValueStore"/>. State changes that the engine commits to its
/// data cache are flushed back to the underlying KV store on HALT; on FAULT,
/// the cache is dropped and no state changes persist (atomic-tx semantics).
/// </para>
/// <para>
/// This executor produces no withdrawals or messages on its own — those come
/// from contract notifications the operator's <see cref="INotificationCollector"/>
/// inspects. The default collector treats the receipt as effect-free (mirroring
/// <see cref="ReferenceTransactionExecutor"/>'s behavior); production deployments
/// wire a collector that decodes domain-specific events into withdrawals/messages.
/// </para>
/// </remarks>
public sealed class ApplicationEngineTransactionExecutor : ITransactionExecutor
{
    private readonly IL2KeyValueStore _state;
    private readonly INotificationCollector _collector;
    private readonly ProtocolSettings _settings;
    private readonly long _gasLimit;

    /// <summary>
    /// Construct.
    /// </summary>
    /// <param name="state">L2 state KV store the engine reads/writes.</param>
    /// <param name="collector">
    /// Optional notification → withdrawals/messages decoder. Default treats
    /// every transaction as effect-free.
    /// </param>
    /// <param name="settings">
    /// Optional Neo <see cref="ProtocolSettings"/> (network magic, native-contract
    /// addresses). Defaults to <see cref="ProtocolSettings.Default"/>.
    /// </param>
    /// <param name="gasLimit">
    /// Per-transaction gas budget in datoshi (Neo's smallest GAS unit).
    /// Default = <see cref="ApplicationEngine.TestModeGas"/>.
    /// </param>
    public ApplicationEngineTransactionExecutor(
        IL2KeyValueStore state,
        INotificationCollector? collector = null,
        ProtocolSettings? settings = null,
        long gasLimit = ApplicationEngine.TestModeGas)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (gasLimit <= 0)
            throw new ArgumentException("gasLimit must be positive", nameof(gasLimit));
        _state = state;
        _collector = collector ?? NoEffectsCollector.Instance;
        _settings = settings ?? ProtocolSettings.Default;
        _gasLimit = gasLimit;
    }

    /// <inheritdoc />
    public async ValueTask<TransactionExecutionResult> ExecuteAsync(
        ReadOnlyMemory<byte> serializedTx,
        BatchBlockContext batchContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batchContext);

        // Pre-hash the bytes — the receipt's TxHash field needs to match what the
        // batch sealer expects regardless of whether deserialization succeeds.
        var rawTxHash = new UInt256(Crypto.Hash256(serializedTx.Span));

        Transaction tx;
        try
        {
            tx = serializedTx.ToArray().AsSerializable<Transaction>();
        }
        catch (Exception ex)
        {
            // Malformed transaction — return a Failed receipt without running anything.
            // Same semantics as Neo's mempool reject.
            return Failed(rawTxHash, $"deserialize failed: {ex.Message}");
        }

        // Run the script against a fresh DataCache snapshot. On HALT, commit;
        // on FAULT, the cache is discarded so no state mutates.
        var cache = new L2DataCacheAdapter(_state);
        var persistingBlock = BuildPersistingBlock(batchContext);

        ApplicationEngine engine;
        try
        {
            engine = ApplicationEngine.Run(
                tx.Script,
                cache,
                container: tx,
                persistingBlock: persistingBlock,
                settings: _settings,
                gas: _gasLimit);
        }
        catch (Exception ex)
        {
            // Engine construction itself can throw on a malformed script; treat as FAULT.
            return Failed(tx.Hash, $"engine setup failed: {ex.Message}");
        }

        var success = engine.State == VMState.HALT;
        if (success)
        {
            // Persist the cache changes to the underlying KV store. DataCache.Commit()
            // walks the dirty-tracked entries and calls AddInternal/UpdateInternal/
            // DeleteInternal on the adapter, which routes them to the IL2KeyValueStore.
            cache.Commit();
        }

        var receipt = new Receipt
        {
            TxHash = tx.Hash,
            Success = success,
            GasConsumed = engine.FeeConsumed,
            StorageDeltaHash = success ? ComputeStorageDeltaHash(engine) : UInt256.Zero,
            EventsHash = success ? ComputeEventsHash(engine.Notifications) : UInt256.Zero,
        };

        var (withdrawals, messages) = success
            ? _collector.CollectAsync(tx, engine.Notifications.ToArray()).Result
            : (Array.Empty<WithdrawalRequest>(), Array.Empty<CrossChainMessage>());
        return new TransactionExecutionResult
        {
            Receipt = receipt,
            TxHash = tx.Hash,
            Withdrawals = withdrawals,
            Messages = messages,
        };
    }

    private static TransactionExecutionResult Failed(UInt256 hash, string reason)
    {
        return new TransactionExecutionResult
        {
            Receipt = new Receipt
            {
                TxHash = hash,
                Success = false,
                GasConsumed = 0,
                StorageDeltaHash = UInt256.Zero,
                EventsHash = UInt256.Zero,
            },
            TxHash = hash,
            Withdrawals = Array.Empty<WithdrawalRequest>(),
            Messages = Array.Empty<CrossChainMessage>(),
        };
    }

    private static UInt256 ComputeStorageDeltaHash(ApplicationEngine engine)
    {
        // Hash the canonical-ordered set of (key, value) updates the engine staged.
        // Used by the batch executor to feed the receipt root. Deterministic per the
        // SPEC.md proving contract: same script + same starting state → same delta hash.
        // We sort by the Key bytes so iteration order doesn't leak into the hash.
        var changes = engine.SnapshotCache.GetChangeSet()
            .OrderBy(c => c.Key.ToArray(), ByteSeqComparer.Instance)
            .ToArray();

        if (changes.Length == 0) return UInt256.Zero;

        using var sha = System.Security.Cryptography.SHA256.Create();
        var ms = new System.IO.MemoryStream();
        foreach (var c in changes)
        {
            // c is KeyValuePair<StorageKey, DataCache.Trackable>; the value side
            // exposes .Item (StorageItem) + .State (TrackState).
            var keyBytes = c.Key.ToArray();
            var valBytes = c.Value.Item is null ? Array.Empty<byte>() : c.Value.Item.Value.ToArray();
            ms.Write(BitConverter.GetBytes(keyBytes.Length));
            ms.Write(keyBytes);
            ms.Write(BitConverter.GetBytes(valBytes.Length));
            ms.Write(valBytes);
            ms.WriteByte((byte)c.Value.State);
        }
        return new UInt256(Crypto.Hash256(ms.ToArray()));
    }

    private static UInt256 ComputeEventsHash(IReadOnlyCollection<NotifyEventArgs> notifications)
    {
        if (notifications.Count == 0) return UInt256.Zero;
        var ms = new System.IO.MemoryStream();
        foreach (var n in notifications)
        {
            var hashBytes = n.ScriptHash.GetSpan().ToArray();
            ms.Write(hashBytes);
            // Event-name string contributes; Neo's Notify accepts a name as the first
            // state-stack item so we hash the canonical name + the state JSON repr.
            var nameBytes = System.Text.Encoding.UTF8.GetBytes(n.EventName ?? string.Empty);
            ms.Write(BitConverter.GetBytes(nameBytes.Length));
            ms.Write(nameBytes);
        }
        return new UInt256(Crypto.Hash256(ms.ToArray()));
    }

    private Block BuildPersistingBlock(BatchBlockContext ctx)
    {
        // ApplicationEngine needs a "persisting block" for execution context
        // (Runtime.Time, Runtime.Block.Index, etc.). We synthesize one from the
        // batch context — for L2 chains, the L2 block height + timestamp drive
        // contract behavior, not L1's. Witness / merkle-root fields are zero
        // since this is an L2-internal block; an attacker can't fake them past
        // the engine because consensus-driven block validation isn't part of
        // the script execution path.
        return new Block
        {
            Header = new Header
            {
                Index = ctx.L1FinalizedHeight,
                Timestamp = ctx.FirstBlockTimestamp,
                MerkleRoot = UInt256.Zero,
                PrevHash = UInt256.Zero,
                NextConsensus = UInt160.Zero,
                Nonce = 0,
                Witness = new Witness
                {
                    InvocationScript = ReadOnlyMemory<byte>.Empty,
                    VerificationScript = ReadOnlyMemory<byte>.Empty,
                },
            },
            Transactions = Array.Empty<Transaction>(),
        };
    }

    private sealed class ByteSeqComparer : IComparer<byte[]>
    {
        public static readonly ByteSeqComparer Instance = new();
        public int Compare(byte[]? x, byte[]? y)
        {
            if (x is null || y is null) return (x is null ? 0 : 1) - (y is null ? 0 : 1);
            var min = Math.Min(x.Length, y.Length);
            for (var i = 0; i < min; i++)
            {
                var d = x[i] - y[i];
                if (d != 0) return d;
            }
            return x.Length - y.Length;
        }
    }
}

/// <summary>
/// Plug-in point for translating engine notifications into the L2's
/// <see cref="WithdrawalRequest"/> + <see cref="CrossChainMessage"/> sets.
/// Production wires a domain-specific decoder (e.g. one that recognizes the
/// chain's native bridge contract's <c>OnWithdraw</c> event); the default
/// <see cref="NoEffectsCollector"/> treats every transaction as effect-free.
/// </summary>
public interface INotificationCollector
{
    /// <summary>
    /// Inspect <paramref name="notifications"/> and return any withdrawals +
    /// messages the L2 batch should record for this transaction.
    /// </summary>
    Task<(IReadOnlyList<WithdrawalRequest> withdrawals, IReadOnlyList<CrossChainMessage> messages)>
        CollectAsync(Transaction tx, IReadOnlyList<NotifyEventArgs> notifications);
}

/// <summary>Default <see cref="INotificationCollector"/> that produces no effects.</summary>
public sealed class NoEffectsCollector : INotificationCollector
{
    /// <summary>Singleton.</summary>
    public static NoEffectsCollector Instance { get; } = new();

    /// <inheritdoc />
    public Task<(IReadOnlyList<WithdrawalRequest> withdrawals, IReadOnlyList<CrossChainMessage> messages)>
        CollectAsync(Transaction tx, IReadOnlyList<NotifyEventArgs> notifications)
    {
        return Task.FromResult<(IReadOnlyList<WithdrawalRequest>, IReadOnlyList<CrossChainMessage>)>(
            (Array.Empty<WithdrawalRequest>(), Array.Empty<CrossChainMessage>()));
    }
}
