using Neo.L2.Executor.State;

namespace Sample.CounterChainExecutor;

/// <summary>
/// Production-ready bridge between <see cref="ICounterChainState"/> and the framework's
/// canonical <see cref="KeyedStateStore"/>. With this adapter wired, the executor's writes
/// flow into the same store the post-state-root oracle hashes — so the
/// <c>BatchExecutionResult.PostStateRoot</c> reflects the executor's actual state
/// mutations, not just a synthetic XOR of the receipt root.
/// </summary>
/// <remarks>
/// <para>
/// Why this matters: the four canonical-encoding leaf-hash invariants
/// (<see cref="KeyedStateStore.HashEntry"/> = <c>Hash256(int32LE(keyLen)||key||
/// int32LE(valueLen)||value)</c>) hold whether the writes come from the
/// <c>ReferenceTransactionExecutor</c> or a custom executor — what matters is that the
/// executor's writes land in the SAME store that <see cref="KeyedStateRootOracle"/>
/// will hash. This adapter provides the bridge with no extra serialization layer.
/// </para>
/// <para>
/// This is what an integration test wires when proving "custom executor + framework
/// pipeline = consistent post-state root."
/// </para>
/// </remarks>
public sealed class KeyedStateStoreAdapter : ICounterChainState
{
    private readonly KeyedStateStore _store;

    /// <summary>Construct over the same <see cref="KeyedStateStore"/> the post-state-root oracle hashes.</summary>
    public KeyedStateStoreAdapter(KeyedStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <inheritdoc />
    public bool TryGet(byte[] key, out byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        var raw = _store.Get(key);
        if (raw.Length == 0)
        {
            value = Array.Empty<byte>();
            return false;
        }
        value = raw.ToArray();
        return true;
    }

    /// <inheritdoc />
    public void Put(byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _store.Put(key, value);
    }
}
