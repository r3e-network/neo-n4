using System;
using System.Buffers.Binary;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sample.CounterChainExecutor.UnitTests;

/// <summary>
/// Tests for <see cref="CounterChainExecutor"/>. Walks the executor through each
/// opcode (happy path + edge cases) so a future operator forking this sample can copy
/// the coverage shape into their own custom executor.
/// </summary>
[TestClass]
public class UT_CounterChainExecutor
{
    private const uint SampleChainId = 1100;

    private static UInt160 Addr(byte b)
    {
        var bytes = new byte[20];
        bytes[0] = b;
        return new UInt160(bytes);
    }

    private static BatchBlockContext Context() => new()
    {
        L1FinalizedHeight = 0,
        FirstBlockTimestamp = 1_700_000_000_000UL,
        LastBlockTimestamp = 1_700_000_000_500UL,
        SequencerCommitteeHash = UInt256.Zero,
        Network = 0x4E454F00,  // "NEO\0"
    };

    private static (CounterChainExecutor exec, InMemoryCounterChainState state) NewExecutor()
    {
        var state = new InMemoryCounterChainState();
        var exec = new CounterChainExecutor(SampleChainId, state, Addr(0x42));
        return (exec, state);
    }

    [TestMethod]
    public async Task Execute_IncrementCounter_HappyPath_StoresAndAccumulates()
    {
        var (exec, state) = NewExecutor();
        var sender = Addr(0xAA);

        // First increment: 0 + 100 = 100.
        var tx1 = CounterTxBuilder.IncrementCounter(sender, 100);
        var r1 = await exec.ExecuteAsync(tx1, Context());
        Assert.IsTrue(r1.Receipt.Success);
        Assert.AreEqual(CounterChainExecutor.GasIncrementCounter, r1.Receipt.GasConsumed);
        Assert.AreEqual(0, r1.Withdrawals.Count);
        Assert.AreEqual(0, r1.Messages.Count);

        // Verify state was actually mutated (the executor really wrote, not no-op'd).
        var key = BuildCounterKey(sender);
        Assert.IsTrue(state.TryGet(key, out var v1));
        Assert.AreEqual(100UL, BinaryPrimitives.ReadUInt64LittleEndian(v1));

        // Second increment: 100 + 50 = 150 (exercises the read-then-add path).
        var tx2 = CounterTxBuilder.IncrementCounter(sender, 50);
        var r2 = await exec.ExecuteAsync(tx2, Context());
        Assert.IsTrue(r2.Receipt.Success);
        Assert.IsTrue(state.TryGet(key, out var v2));
        Assert.AreEqual(150UL, BinaryPrimitives.ReadUInt64LittleEndian(v2));
    }

    [TestMethod]
    public async Task Execute_IncrementCounter_PerSenderIsolation()
    {
        var (exec, state) = NewExecutor();
        var alice = Addr(0xAA);
        var bob = Addr(0xBB);
        await exec.ExecuteAsync(CounterTxBuilder.IncrementCounter(alice, 10), Context());
        await exec.ExecuteAsync(CounterTxBuilder.IncrementCounter(bob, 20), Context());

        Assert.IsTrue(state.TryGet(BuildCounterKey(alice), out var aliceVal));
        Assert.AreEqual(10UL, BinaryPrimitives.ReadUInt64LittleEndian(aliceVal));
        Assert.IsTrue(state.TryGet(BuildCounterKey(bob), out var bobVal));
        Assert.AreEqual(20UL, BinaryPrimitives.ReadUInt64LittleEndian(bobVal));
    }

    [TestMethod]
    public async Task Execute_IncrementCounter_OverflowWraps()
    {
        // Pin the wraparound semantics: a custom chain that wants checked arithmetic
        // should override this. The default sample matches Neo's NEP-17 wraparound.
        var (exec, state) = NewExecutor();
        var sender = Addr(0xCC);

        // First write: ulong.MaxValue - 5
        var key = BuildCounterKey(sender);
        var seed = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(seed, ulong.MaxValue - 5);
        state.Put(key, seed);

        // Add 10 → wraparound to 4.
        var tx = CounterTxBuilder.IncrementCounter(sender, 10);
        var r = await exec.ExecuteAsync(tx, Context());
        Assert.IsTrue(r.Receipt.Success);

        Assert.IsTrue(state.TryGet(key, out var v));
        Assert.AreEqual(4UL, BinaryPrimitives.ReadUInt64LittleEndian(v),
            "ulong overflow wraps mod 2^64 (sample uses unchecked arithmetic intentionally)");
    }

    [TestMethod]
    public async Task Execute_IncrementCounter_TruncatedTx_FailsDeterministically()
    {
        var (exec, _) = NewExecutor();
        var truncated = new byte[5];  // way below 1+20+8
        truncated[0] = (byte)CounterChainExecutor.Opcode.IncrementCounter;
        var r = await exec.ExecuteAsync(truncated, Context());
        Assert.IsFalse(r.Receipt.Success);
        // Failed txs still charge their declared gas — same as Neo Native's HALT-vs-FAULT.
        Assert.AreEqual(CounterChainExecutor.GasIncrementCounter, r.Receipt.GasConsumed);
    }

    [TestMethod]
    public async Task Execute_EmitWithdrawal_HappyPath_ProducesValidWithdrawal()
    {
        var (exec, _) = NewExecutor();
        var recipient = Addr(0xEE);
        var token = Addr(0xFF);
        var tx = CounterTxBuilder.EmitWithdrawal(recipient, token, 1234);
        var r = await exec.ExecuteAsync(tx, Context());

        Assert.IsTrue(r.Receipt.Success);
        Assert.AreEqual(1, r.Withdrawals.Count);
        var w = r.Withdrawals[0];
        Assert.AreEqual(recipient, w.L1Recipient);
        Assert.AreEqual(recipient, w.L2Sender);
        Assert.AreEqual(token, w.L2Asset);
        Assert.AreEqual(new BigInteger(1234), w.Amount);
        Assert.AreNotEqual(0UL, w.Nonce, "nonce must be derived from txHash, not zero");
    }

    [TestMethod]
    public async Task Execute_EmitWithdrawal_ZeroAmount_Failed()
    {
        var (exec, _) = NewExecutor();
        var tx = CounterTxBuilder.EmitWithdrawal(Addr(0xEE), Addr(0xFF), 0);
        var r = await exec.ExecuteAsync(tx, Context());
        Assert.IsFalse(r.Receipt.Success);
        Assert.AreEqual(0, r.Withdrawals.Count);
    }

    [TestMethod]
    public async Task Execute_EmitMessage_HappyPath_ProducesRoutableMessage()
    {
        var (exec, _) = NewExecutor();
        var body = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var tx = CounterTxBuilder.EmitMessage(destChainId: 1200, body);
        var r = await exec.ExecuteAsync(tx, Context());

        Assert.IsTrue(r.Receipt.Success);
        Assert.AreEqual(1, r.Messages.Count);
        var m = r.Messages[0];
        Assert.AreEqual(SampleChainId, m.SourceChainId);
        Assert.AreEqual(1200u, m.TargetChainId);
        Assert.AreEqual(MessageType.Call, m.MessageType);
        CollectionAssert.AreEqual(body, m.Payload.ToArray());
        Assert.AreNotEqual(UInt256.Zero, m.MessageHash, "MessageBuilder must compute a non-zero hash");
    }

    [TestMethod]
    public async Task Execute_EmitMessage_SelfRouted_Failed()
    {
        // doc.md §10 (Neo Connect): self-routed messages have no transport.
        // The executor must fail at execution time rather than emit a doomed message.
        var (exec, _) = NewExecutor();
        var tx = CounterTxBuilder.EmitMessage(destChainId: SampleChainId, ReadOnlySpan<byte>.Empty);
        var r = await exec.ExecuteAsync(tx, Context());
        Assert.IsFalse(r.Receipt.Success);
        Assert.AreEqual(0, r.Messages.Count);
    }

    [TestMethod]
    public async Task Execute_EmitMessage_OversizedBody_BuilderRejects()
    {
        // The builder caps inline message bodies at MaxMessageBytes; oversize
        // throws at build time. Pin so a future operator forking the sample
        // can't accidentally raise the cap without realizing the implications
        // for DA tier sizing.
        Assert.ThrowsExactly<ArgumentException>(() =>
            CounterTxBuilder.EmitMessage(1200,
                new byte[CounterChainExecutor.MaxMessageBytes + 1]));
    }

    [TestMethod]
    public async Task Execute_UnknownOpcode_Failed()
    {
        var (exec, _) = NewExecutor();
        var unknown = new byte[] { 0xFF, 1, 2, 3 };  // 0xFF isn't a valid opcode
        var r = await exec.ExecuteAsync(unknown, Context());
        Assert.IsFalse(r.Receipt.Success);
        Assert.AreEqual(0L, r.Receipt.GasConsumed,
            "unknown opcodes charge zero gas — chain operator decides whether to charge a base fee");
    }

    [TestMethod]
    public async Task Execute_EmptyTx_Failed()
    {
        var (exec, _) = NewExecutor();
        var r = await exec.ExecuteAsync(Array.Empty<byte>(), Context());
        Assert.IsFalse(r.Receipt.Success);
        Assert.AreEqual(0L, r.Receipt.GasConsumed);
    }

    [TestMethod]
    public async Task Execute_Determinism_SameInputSameOutput()
    {
        // SPEC.md determinism contract: two independently-constructed executors with
        // identical state must produce identical receipts for the same tx. Pin so
        // anything non-deterministic creeping in (clock reads, env vars, RNG) breaks loud.
        var (e1, s1) = NewExecutor();
        var (e2, s2) = NewExecutor();
        var sender = Addr(0xAB);
        var tx = CounterTxBuilder.IncrementCounter(sender, 7);
        var r1 = await e1.ExecuteAsync(tx, Context());
        var r2 = await e2.ExecuteAsync(tx, Context());
        Assert.AreEqual(r1.Receipt.TxHash, r2.Receipt.TxHash);
        Assert.AreEqual(r1.Receipt.GasConsumed, r2.Receipt.GasConsumed);
        Assert.AreEqual(r1.Receipt.StorageDeltaHash, r2.Receipt.StorageDeltaHash);
        Assert.AreEqual(r1.Receipt.Success, r2.Receipt.Success);

        // Even the post-state must match.
        var key = BuildCounterKey(sender);
        s1.TryGet(key, out var v1);
        s2.TryGet(key, out var v2);
        CollectionAssert.AreEqual(v1, v2);
    }

    [TestMethod]
    public async Task Execute_BatchOfMixedOpcodes_AllSucceed()
    {
        // Smoke test: walk a small batch of mixed opcodes through one executor +
        // shared state. Mirrors the per-batch driver shape ReferenceBatchExecutor uses.
        var (exec, state) = NewExecutor();
        var sender = Addr(0xAA);
        var recipient = Addr(0xEE);
        var token = Addr(0xFF);

        var batch = new byte[][]
        {
            CounterTxBuilder.IncrementCounter(sender, 5),
            CounterTxBuilder.IncrementCounter(sender, 3),
            CounterTxBuilder.EmitWithdrawal(recipient, token, 100),
            CounterTxBuilder.EmitMessage(2200, new byte[] { 0x01 }),
        };

        var totalGas = 0L;
        var totalWithdrawals = 0;
        var totalMessages = 0;
        foreach (var tx in batch)
        {
            var r = await exec.ExecuteAsync(tx, Context());
            Assert.IsTrue(r.Receipt.Success);
            totalGas += r.Receipt.GasConsumed;
            totalWithdrawals += r.Withdrawals.Count;
            totalMessages += r.Messages.Count;
        }

        Assert.AreEqual(
            CounterChainExecutor.GasIncrementCounter * 2
            + CounterChainExecutor.GasEmitWithdrawal
            + CounterChainExecutor.GasEmitMessage,
            totalGas);
        Assert.AreEqual(1, totalWithdrawals);
        Assert.AreEqual(1, totalMessages);

        Assert.IsTrue(state.TryGet(BuildCounterKey(sender), out var v));
        Assert.AreEqual(8UL, BinaryPrimitives.ReadUInt64LittleEndian(v));
    }

    private static byte[] BuildCounterKey(UInt160 sender)
    {
        var senderBytes = sender.GetSpan().ToArray();
        return CounterChainExecutor.CounterKeyPrefix
            .Concat(senderBytes)
            .ToArray();
    }

}
