namespace Sample.CounterChainExecutor;

/// <summary>
/// Minimal state seam for the <see cref="CounterChainExecutor"/> — keeps the executor
/// decoupled from any concrete state store. In tests + the in-process devnet a
/// <see cref="InMemoryCounterChainState"/> is enough; in production an operator wires
/// <c>Neo.L2.Executor.State.KeyedStateStore</c> behind this interface so per-key reads
/// and writes participate in the canonical Merkle root.
/// </summary>
/// <remarks>
/// The interface intentionally stays at the byte-array level: the framework's
/// <c>KeyedStateStore.HashEntry</c> + <c>SettlementManager.VerifyStateLeafWithProof</c>
/// hash entries by <c>Hash256(int32LE(keyLen)||key||int32LE(valueLen)||value)</c>, so a
/// custom executor that stores typed values must serialize them to a deterministic byte
/// representation before calling <see cref="Put"/>.
/// </remarks>
public interface ICounterChainState
{
    /// <summary>Try to read the value at <paramref name="key"/>; returns false if absent.</summary>
    bool TryGet(byte[] key, out byte[] value);

    /// <summary>Write (or overwrite) the value at <paramref name="key"/>.</summary>
    void Put(byte[] key, byte[] value);
}

/// <summary>
/// Trivial in-memory state for tests + in-process devnet. Keys are matched by content,
/// not by reference, so two independently-built byte arrays with identical content
/// resolve to the same entry.
/// </summary>
public sealed class InMemoryCounterChainState : ICounterChainState
{
    private readonly Dictionary<string, byte[]> _store = new();

    /// <inheritdoc />
    public bool TryGet(byte[] key, out byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_store.TryGetValue(Convert.ToHexString(key), out var v))
        {
            value = v;
            return true;
        }
        value = Array.Empty<byte>();
        return false;
    }

    /// <inheritdoc />
    public void Put(byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        _store[Convert.ToHexString(key)] = value;
    }
}
