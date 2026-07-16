using Neo.L2.Bridge;

namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_L1MessageDrain
{
    private static CrossChainMessage Msg(uint source, ulong nonce, byte tag = 1) => new()
    {
        SourceChainId = source,
        TargetChainId = 1001,
        Nonce = nonce,
        Sender = UInt160.Parse("0x" + new string('1', 40)),
        Receiver = UInt160.Parse("0x" + new string('2', 40)),
        MessageType = MessageType.Deposit,
        Payload = new byte[] { tag },
        MessageHash = UInt256.Zero,
    };

    [TestMethod]
    public void Combine_MergesSortsAndCaps()
    {
        var drain = L1MessageDrain.Combine(
            _ => new[] { Msg(0, 3), Msg(0, 1) },
            _ => new[] { Msg(0, 2) });

        var result = drain(2);
        Assert.AreEqual(2, result.Count);
        Assert.AreEqual(1UL, result[0].Nonce);
        Assert.AreEqual(2UL, result[1].Nonce);
    }

    [TestMethod]
    public void Combine_RejectsNullDrainResult()
    {
        var drain = L1MessageDrain.Combine(_ => null!);
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => drain(1));
        StringAssert.Contains(ex.Message, "returned null");
    }

    [TestMethod]
    public void Combine_RejectsDuplicateSourceNonceAcrossDrains()
    {
        var drain = L1MessageDrain.Combine(
            _ => new[] { Msg(0, 7, tag: 1) },
            _ => new[] { Msg(0, 7, tag: 2) });
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => drain(10));
        StringAssert.Contains(ex.Message, "duplicate L1 message key");
    }

    [TestMethod]
    public void Combine_RejectsEmptyAndNullEntries()
    {
        Assert.ThrowsExactly<ArgumentException>(() => L1MessageDrain.Combine());
        Assert.ThrowsExactly<ArgumentNullException>(() => L1MessageDrain.Combine(null!));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => L1MessageDrain.Combine(max => Array.Empty<CrossChainMessage>(), null!));
    }

    [TestMethod]
    public void FromDeposits_ScansThenDrains()
    {
        // Seal-path composition must discover finalized deposits before reserving them.
        var source = new ScanGatedDepositSource(1001);
        source.Stage(Msg(0, 9, tag: 0xAB));
        Assert.AreEqual(0, source.Peek(10).Count, "staged deposits are invisible until Scan");

        var drain = L1MessageDrain.FromDeposits(source);
        var drained = drain(10);

        Assert.AreEqual(1, source.ScanCount);
        Assert.AreEqual(1, drained.Count);
        Assert.AreEqual(9UL, drained[0].Nonce);
        Assert.AreEqual(0, source.Peek(10).Count, "drain reserves after scan");
    }

    [TestMethod]
    public void FromDeposits_RejectsNullSource()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => L1MessageDrain.FromDeposits(null!));
    }

    /// <summary>
    /// Deposit source that only materializes staged messages on <see cref="ScanAsync"/>,
    /// matching production <c>RpcSharedBridgeDepositSource</c> discovery semantics.
    /// </summary>
    private sealed class ScanGatedDepositSource(uint chainId) : ISharedBridgeDepositSource
    {
        private readonly List<CrossChainMessage> _staged = new();
        private readonly List<CrossChainMessage> _ready = new();
        private readonly List<CrossChainMessage> _reserved = new();

        public uint ChainId { get; } = chainId;
        public int ScanCount { get; private set; }

        public void Stage(CrossChainMessage message) => _staged.Add(message);

        public ValueTask<int> ScanAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ScanCount++;
            _ready.AddRange(_staged);
            var count = _staged.Count;
            _staged.Clear();
            return ValueTask.FromResult(count);
        }

        public IReadOnlyList<CrossChainMessage> Peek(int maxMessages)
            => _ready.Take(maxMessages).ToArray();

        public IReadOnlyList<CrossChainMessage> Drain(int maxMessages)
        {
            var take = _ready.Take(maxMessages).ToArray();
            _ready.RemoveRange(0, take.Length);
            _reserved.AddRange(take);
            return take;
        }

        public void ConfirmConsumed(ulong nonce) { }

        public void ReleaseReservations(IEnumerable<ulong> nonces) { }
    }
}
