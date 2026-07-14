using Neo.L2.Batch;
using Neo.L2.Executor.ProofWitness;
using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.L2.Executor.UnitTests;

[TestClass]
public class UT_Sp1StateWitnessSource
{
    [TestMethod]
    public async Task GenesisBindings_AreIdempotentAndCaptureCompleteNeoState()
    {
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);

        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var repeatedRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        var snapshot = source.Capture(initialRoot);
        var witness = StateWitnessV1Serializer.Decode(snapshot.Witness.Span);

        Assert.AreEqual(initialRoot, repeatedRoot);
        Assert.AreEqual(initialRoot, snapshot.StateRoot);
        Assert.AreEqual(initialRoot, await source.GetInitialStateRootAsync());
        Assert.IsGreaterThan(0, witness.Contracts.Count);
        Assert.IsGreaterThan(witness.Contracts.Count, witness.Entries.Count);
        foreach (var contract in witness.Contracts)
        {
            var bindingKey = StateWitnessV1Serializer.ContractBindingKey(contract.Hash);
            var binding = witness.Entries.Single(entry =>
                entry.Key.Span.SequenceEqual(bindingKey));
            CollectionAssert.AreEqual(
                StateWitnessV1Serializer.ContractBindingHash(contract).GetSpan().ToArray(),
                binding.Value.ToArray());
        }
    }

    [TestMethod]
    public void Capture_RejectsStateRootDrift()
    {
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);
        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        state.Put("application-key"u8, "changed"u8);

        Assert.ThrowsExactly<InvalidDataException>(() => source.Capture(initialRoot));
    }

    [TestMethod]
    public void GenesisBindings_RejectStaleOrOrphanedBinding()
    {
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);
        Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var binding = state.EnumeratePrefix(
            [0xff, .. "neo-n4/contract-binding/v1/"u8]).First();
        state.Put(binding.Key, new byte[UInt256.Length]);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            Sp1StateWitnessSource.InitializeGenesisContractBindings(state));
    }

    [TestMethod]
    public void Constructor_RejectsZeroInitialStateRoot()
    {
        using var state = new InMemoryKeyValueStore();
        Assert.ThrowsExactly<ArgumentException>(() =>
            new Sp1StateWitnessSource(state, UInt256.Zero));
    }

    [TestMethod]
    public void CommitTransition_AtomicallyInstallsValidatedPostState()
    {
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);
        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        var preState = StateWitnessV1Serializer.Decode(source.Capture(initialRoot).Witness.Span);
        var postStateBytes = AddEntry(
            preState, "application-key"u8.ToArray(), "value"u8.ToArray());
        var expectedPostRoot = ComputeRoot(postStateBytes);

        var postRoot = source.CommitTransition(
            initialRoot, expectedPostRoot, postStateBytes);

        CollectionAssert.AreEqual("value"u8.ToArray(), state.Get("application-key"u8));
        Assert.AreEqual(postRoot, source.Capture(postRoot).StateRoot);
        Assert.AreNotEqual(initialRoot, postRoot);
    }

    [TestMethod]
    public void CommitTransition_RejectsPreStateDriftWithoutMutation()
    {
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);
        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        var preState = StateWitnessV1Serializer.Decode(source.Capture(initialRoot).Witness.Span);
        var postStateBytes = AddEntry(
            preState, "application-key"u8.ToArray(), "value"u8.ToArray());
        var expectedPostRoot = ComputeRoot(postStateBytes);
        state.Put("concurrent-change"u8, "value"u8);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            source.CommitTransition(initialRoot, expectedPostRoot, postStateBytes));

        Assert.IsNull(state.Get("application-key"u8));
        CollectionAssert.AreEqual("value"u8.ToArray(), state.Get("concurrent-change"u8));
    }

    [TestMethod]
    public void CommitTransition_RejectsChangeRacingWithAtomicReplacement()
    {
        using var state = new InterleavingAtomicStore();
        NeoVMGenesisBootstrap.Run(state);
        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        var preState = StateWitnessV1Serializer.Decode(source.Capture(initialRoot).Witness.Span);
        var postStateBytes = AddEntry(
            preState, "application-key"u8.ToArray(), "value"u8.ToArray());
        var expectedPostRoot = ComputeRoot(postStateBytes);
        state.BeforeCompareExchangeAll = () =>
            state.Put("concurrent-change"u8, "value"u8);

        Assert.ThrowsExactly<InvalidDataException>(() =>
            source.CommitTransition(initialRoot, expectedPostRoot, postStateBytes));

        Assert.IsNull(state.Get("application-key"u8));
        CollectionAssert.AreEqual("value"u8.ToArray(), state.Get("concurrent-change"u8));
    }

    [TestMethod]
    public void CommitTransition_RejectsPostStateRootMismatchWithoutMutation()
    {
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);
        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        var preState = StateWitnessV1Serializer.Decode(source.Capture(initialRoot).Witness.Span);
        var postStateBytes = AddEntry(
            preState, "application-key"u8.ToArray(), "value"u8.ToArray());

        Assert.ThrowsExactly<InvalidDataException>(() =>
            source.CommitTransition(initialRoot, initialRoot, postStateBytes));

        Assert.IsNull(state.Get("application-key"u8));
        Assert.AreEqual(initialRoot, source.Capture(initialRoot).StateRoot);
    }

    [TestMethod]
    public void CommitTransition_RejectsContractMetadataMismatchWithoutMutation()
    {
        using var state = new InMemoryKeyValueStore();
        NeoVMGenesisBootstrap.Run(state);
        var initialRoot = Sp1StateWitnessSource.InitializeGenesisContractBindings(state);
        var source = new Sp1StateWitnessSource(state, initialRoot);
        var preState = StateWitnessV1Serializer.Decode(source.Capture(initialRoot).Witness.Span);
        var changedContract = preState.Contracts[0] with
        {
            Script = preState.Contracts[0].Script.ToArray().Append((byte)0x40).ToArray(),
        };
        var bindingKey = StateWitnessV1Serializer.ContractBindingKey(changedContract.Hash);
        var entries = preState.Entries.Select(entry =>
            entry.Key.Span.SequenceEqual(bindingKey)
                ? entry with
                {
                    Value = StateWitnessV1Serializer.ContractBindingHash(changedContract)
                        .GetSpan().ToArray(),
                }
                : entry).ToArray();
        var changedContracts = preState.Contracts.ToArray();
        changedContracts[0] = changedContract;
        var invalidPostState = StateWitnessV1Serializer.Encode(preState with
        {
            Entries = entries,
            Contracts = changedContracts,
        });

        Assert.ThrowsExactly<InvalidDataException>(() =>
            source.CommitTransition(
                initialRoot, ComputeRoot(invalidPostState), invalidPostState));

        Assert.AreEqual(initialRoot, source.Capture(initialRoot).StateRoot);
    }

    private static byte[] AddEntry(
        StateWitnessV1 preState,
        ReadOnlyMemory<byte> key,
        ReadOnlyMemory<byte> value)
    {
        var entries = preState.Entries
            .Append(new StateWitnessEntryV1 { Key = key, Value = value })
            .OrderBy(static entry => entry.Key.ToArray(), LexicographicByteArrayComparer.Instance)
            .ToArray();
        return StateWitnessV1Serializer.Encode(preState with { Entries = entries });
    }

    private static UInt256 ComputeRoot(ReadOnlySpan<byte> stateWitness)
    {
        var state = StateWitnessV1Serializer.Decode(stateWitness);
        return KeyedStateMerkleTree.ComputeRoot(state.Entries.Select(
            static entry => (entry.Key.ToArray(), entry.Value.ToArray())));
    }

    private sealed class InterleavingAtomicStore : IAtomicL2KeyValueStore
    {
        private readonly InMemoryKeyValueStore _inner = new();

        public Action? BeforeCompareExchangeAll { get; set; }

        public long Count => _inner.Count;

        public void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) =>
            _inner.Put(key, value);

        public bool TryPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value) =>
            _inner.TryPut(key, value);

        public bool CompareExchange(
            ReadOnlySpan<byte> key,
            ReadOnlySpan<byte> expectedValue,
            ReadOnlySpan<byte> newValue) =>
            _inner.CompareExchange(key, expectedValue, newValue);

        public byte[]? Get(ReadOnlySpan<byte> key) => _inner.Get(key);

        public bool Delete(ReadOnlySpan<byte> key) => _inner.Delete(key);

        public bool Contains(ReadOnlySpan<byte> key) => _inner.Contains(key);

        public IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(
            ReadOnlySpan<byte> prefix) => _inner.EnumeratePrefix(prefix);

        public void ReplaceAll(
            IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries) =>
            _inner.ReplaceAll(entries);

        public bool CompareExchangeAll(
            IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> expectedEntries,
            IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> replacementEntries)
        {
            var interleave = BeforeCompareExchangeAll;
            BeforeCompareExchangeAll = null;
            interleave?.Invoke();
            return _inner.CompareExchangeAll(expectedEntries, replacementEntries);
        }

        public void Dispose() => _inner.Dispose();
    }
}
