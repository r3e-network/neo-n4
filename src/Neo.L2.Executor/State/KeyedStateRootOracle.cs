namespace Neo.L2.Executor.State;

/// <summary>
/// Real <see cref="IPostStateRootOracle"/> backed by a <see cref="KeyedStateStore"/>. After the
/// batch executor has applied all transactions (which mutate the store), this oracle simply
/// returns <see cref="KeyedStateStore.ComputeRoot"/> as the post-state root.
/// </summary>
/// <remarks>
/// This replaces <see cref="DerivedPostStateRootOracle"/> for tests and devnet flows that
/// want a true state-root pipeline. Production swaps in an MPT-backed oracle.
/// <para>
/// The transaction executor is responsible for keeping the store consistent — typically by
/// passing the store into <see cref="ITransactionExecutor"/> implementations.
/// </para>
/// </remarks>
public sealed class KeyedStateRootOracle : IPostStateRootOracle
{
    private readonly KeyedStateStore _store;

    /// <summary>Construct against the store the executor mutates.</summary>
    public KeyedStateRootOracle(KeyedStateStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>The store this oracle reads its root from.</summary>
    public KeyedStateStore Store => _store;

    /// <inheritdoc />
    public ValueTask<UInt256> ResolveAsync(
        UInt256 preStateRoot,
        UInt256 receiptRoot,
        BatchBlockContext blockContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new ValueTask<UInt256>(_store.ComputeRoot());
    }
}
