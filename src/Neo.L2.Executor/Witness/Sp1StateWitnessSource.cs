using System.Text;
using Neo.L2.Batch;
using Neo.L2.Persistence;
using Neo.L2.State;
using Neo.SmartContract.Native;

namespace Neo.L2.Executor.ProofWitness;

/// <summary>Immutable complete state snapshot passed to the SP1 execution profile.</summary>
/// <remarks>See doc.md §7.3 and §8.1–§8.4.</remarks>
public sealed record Sp1StateWitnessSnapshot
{
    /// <summary>Merkle root of every encoded state entry.</summary>
    public required UInt256 StateRoot { get; init; }

    /// <summary>Canonical <c>NEO4STW1</c> bytes.</summary>
    public required ReadOnlyMemory<byte> Witness { get; init; }

    /// <inheritdoc />
    public bool Equals(Sp1StateWitnessSnapshot? other) => other is not null
        && StateRoot.Equals(other.StateRoot)
        && Witness.Span.SequenceEqual(other.Witness.Span);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(StateRoot);
        hash.AddBytes(Witness.Span);
        return hash.ToHashCode();
    }
}

/// <summary>Captures authenticated SP1 state witnesses from Neo's durable L2 state store.</summary>
/// <remarks>
/// See doc.md §7.3 and §8. The source never invents contract metadata: it reads
/// <see cref="NativeContract.ContractManagement"/> from the same state snapshot, emits the
/// exact scripts and deterministic manifests, and requires their domain-separated binding
/// leaves to participate in the committed state root.
/// </remarks>
public sealed class Sp1StateWitnessSource : IInitialStateRootProvider
{
    private static readonly byte[] BindingPrefixBytes =
        [0xff, .. "neo-n4/contract-binding/v1/"u8];
    private static ReadOnlySpan<byte> BindingPrefix => BindingPrefixBytes;

    private readonly IL2KeyValueStore _state;
    private readonly UInt256 _initialStateRoot;
    private readonly Lock _gate = new();

    /// <summary>Create a source locked to an operator-persisted genesis state root.</summary>
    public Sp1StateWitnessSource(IL2KeyValueStore state, UInt256 initialStateRoot)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(initialStateRoot);
        if (initialStateRoot.Equals(UInt256.Zero))
            throw new ArgumentException(
                "SP1 initial state root must be non-zero", nameof(initialStateRoot));
        _state = state;
        _initialStateRoot = new UInt256(initialStateRoot.GetSpan());
    }

    /// <summary>
    /// Idempotently add every current ContractManagement descriptor binding before batch 1,
    /// then return the root operators persist as the chain's immutable initial state root.
    /// </summary>
    public static UInt256 InitializeGenesisContractBindings(IL2KeyValueStore state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var contracts = ReadContracts(state);
        var expectedBindings = contracts.ToDictionary(
            static contract => StateWitnessV1Serializer.ContractBindingKey(contract.Hash),
            static contract => StateWitnessV1Serializer.ContractBindingHash(contract).GetSpan().ToArray(),
            LexicographicByteArrayEqualityComparer.Instance);
        var existingBindings = state.EnumeratePrefix(BindingPrefix)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value,
                LexicographicByteArrayEqualityComparer.Instance);
        foreach (var (key, value) in existingBindings)
        {
            if (!expectedBindings.TryGetValue(key, out var expected)
                || !value.AsSpan().SequenceEqual(expected))
                throw new InvalidDataException(
                    "existing SP1 contract binding is stale or orphaned");
        }
        foreach (var (key, value) in expectedBindings)
        {
            if (!existingBindings.ContainsKey(key)) state.Put(key, value);
        }

        var snapshot = Capture(state, contracts);
        if (snapshot.StateRoot.Equals(UInt256.Zero))
            throw new InvalidDataException("SP1 genesis state root must be non-zero");
        return snapshot.StateRoot;
    }

    /// <summary>Capture the current state and require it to match the sealed pre-state root.</summary>
    public Sp1StateWitnessSnapshot Capture(UInt256 expectedStateRoot)
    {
        ArgumentNullException.ThrowIfNull(expectedStateRoot);
        lock (_gate)
        {
            var snapshot = Capture(_state, ReadContracts(_state));
            if (!snapshot.StateRoot.Equals(expectedStateRoot))
                throw new InvalidDataException(
                    "SP1 state snapshot root differs from the sealed batch pre-state root");
            return snapshot;
        }
    }

    /// <summary>Capture the currently committed state without assuming its root.</summary>
    public Sp1StateWitnessSnapshot CaptureCurrent()
    {
        lock (_gate)
        {
            return Capture(_state, ReadContracts(_state));
        }
    }

    /// <summary>
    /// Validate and atomically commit the complete post-state emitted by the SP1 execution
    /// runtime, returning its authenticated state root.
    /// </summary>
    internal UInt256 CommitTransition(
        UInt256 expectedPreStateRoot,
        UInt256 expectedPostStateRoot,
        ReadOnlyMemory<byte> postStateWitness)
    {
        ArgumentNullException.ThrowIfNull(expectedPreStateRoot);
        ArgumentNullException.ThrowIfNull(expectedPostStateRoot);
        if (_state is not IAtomicL2KeyValueStore atomicState)
            throw new InvalidOperationException(
                "SP1 state transitions require an atomic L2 key/value store");
        lock (_gate)
        {
            var currentContracts = ReadContracts(_state);
            var current = Capture(_state, currentContracts);
            if (!current.StateRoot.Equals(expectedPreStateRoot))
                throw new InvalidDataException(
                    "SP1 state changed before the proven transition could be committed");

            var postState = StateWitnessV1Serializer.Decode(postStateWitness.Span);
            var canonicalPostState = StateWitnessV1Serializer.Encode(postState);
            if (!canonicalPostState.AsSpan().SequenceEqual(postStateWitness.Span))
                throw new InvalidDataException("SP1 post-state witness is not canonical");
            if (!postState.Contracts.SequenceEqual(currentContracts))
                throw new InvalidDataException(
                    "SP1 V1 execution cannot add, remove, or replace deployed contracts");

            using var validationState = new InMemoryKeyValueStore();
            validationState.ReplaceAll(postState.Entries.Select(static entry =>
                (entry.Key, entry.Value)));
            var projected = Capture(validationState, ReadContracts(validationState));
            if (!projected.Witness.Span.SequenceEqual(canonicalPostState))
                throw new InvalidDataException(
                    "SP1 post-state contract metadata differs from ContractManagement state");
            if (!projected.StateRoot.Equals(expectedPostStateRoot))
                throw new InvalidDataException(
                    "SP1 post-state witness differs from the expected transition root");

            var currentState = StateWitnessV1Serializer.Decode(current.Witness.Span);
            if (!atomicState.CompareExchangeAll(
                currentState.Entries.Select(static entry => (entry.Key, entry.Value)),
                postState.Entries.Select(static entry => (entry.Key, entry.Value))))
                throw new InvalidDataException(
                    "SP1 state changed during the atomic transition commit");
            var committed = Capture(_state, ReadContracts(_state));
            if (!committed.StateRoot.Equals(projected.StateRoot)
                || !committed.Witness.Span.SequenceEqual(canonicalPostState))
                throw new InvalidDataException(
                    "atomically committed SP1 state does not match the proven post-state");
            return committed.StateRoot;
        }
    }

    /// <inheritdoc />
    public ValueTask<UInt256> GetInitialStateRootAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new UInt256(_initialStateRoot.GetSpan()));
    }

    private static Sp1StateWitnessSnapshot Capture(
        IL2KeyValueStore state,
        IReadOnlyList<ContractWitnessV1> contracts)
    {
        var entries = state.EnumeratePrefix(ReadOnlySpan<byte>.Empty)
            .Select(static pair => new StateWitnessEntryV1
            {
                Key = pair.Key,
                Value = pair.Value,
            })
            .ToArray();
        var model = new StateWitnessV1
        {
            ProtocolConfig = new StateWitnessProtocolConfigV1(),
            Entries = entries,
            Contracts = contracts,
        };
        var bytes = StateWitnessV1Serializer.Encode(model);
        var root = KeyedStateMerkleTree.ComputeRoot(
            entries.Select(static entry =>
                (entry.Key.ToArray(), entry.Value.ToArray())));
        return new Sp1StateWitnessSnapshot
        {
            StateRoot = root,
            Witness = bytes,
        };
    }

    private static IReadOnlyList<ContractWitnessV1> ReadContracts(IL2KeyValueStore state)
    {
        var snapshot = new L2DataCacheAdapter(state, readOnly: true);
        return NativeContract.ContractManagement.ListContracts(snapshot)
            .Select(static contract => new ContractWitnessV1
            {
                Id = contract.Id,
                Hash = new UInt160(contract.Hash.GetSpan()),
                Script = contract.Script.ToArray(),
                Manifest = Encoding.UTF8.GetBytes(contract.Manifest.ToJson().ToString()),
            })
            .OrderBy(
                static contract => contract.Hash.GetSpan().ToArray(),
                LexicographicByteArrayComparer.Instance)
            .ToArray();
    }

    private sealed class LexicographicByteArrayEqualityComparer
        : IEqualityComparer<byte[]>
    {
        public static LexicographicByteArrayEqualityComparer Instance { get; } = new();

        public bool Equals(byte[]? left, byte[]? right) => ReferenceEquals(left, right)
            || (left is not null && right is not null && left.AsSpan().SequenceEqual(right));

        public int GetHashCode(byte[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            var hash = new HashCode();
            hash.AddBytes(value);
            return hash.ToHashCode();
        }
    }
}
