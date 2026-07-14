using System.Numerics;
using Neo.Cryptography;
using Neo.L2.Bridge.External;
using Neo.L2.ExternalBridge;
using Neo.L2.Messaging;
using Neo.L2.Persistence;

namespace Neo.L2.IntegrationTests;

/// <summary>End-to-end durable payout relay tests over production state transitions.</summary>
[TestClass]
public sealed class UT_E2E_ExternalPayoutRelay
{
    private const uint ExternalChainId = 0xE0000038;
    private const uint NeoChainId = 1099;
    private static readonly UInt160 Adapter = H160(0x11);
    private static readonly UInt160 ForeignSender = H160(0x22);
    private static readonly UInt160 ForeignAsset = H160(0x33);
    private static readonly UInt160 NeoAsset = H160(0x44);
    private static readonly UInt160 Recipient = H160(0x55);
    private static readonly UInt256 SourceTransaction = H256(0x66);

    [TestMethod]
    public async Task Relay_CreditsRecipientOnce_AcknowledgesAndIgnoresReplayAcrossRestart()
    {
        var instruction = Instruction(amount: 250);
        var directory = Path.Combine(
            Path.GetTempPath(), "neo-l2-payout-relay-" + Guid.NewGuid().ToString("N"));
        var target = new FakeCreditClient(throwAfterFirstCredit: true);
        var acknowledgement = new FakeAcknowledgementClient();
        try
        {
            using (var store = new RocksDbKeyValueStore(directory))
            using (var outbox = new PersistentL2PayoutOutbox(store))
            {
                Assert.IsTrue(outbox.Enqueue(instruction));
                Assert.IsFalse(outbox.Enqueue(instruction),
                    "finalized block replay must be idempotent");
                using var firstRun = new L2PayoutRelay(outbox, target, acknowledgement);
                Assert.AreEqual(0, await firstRun.ProcessAsync(),
                    "ambiguous post-credit broadcast failure must not be acknowledged");
                Assert.AreEqual(new BigInteger(250), target.BalanceOf(Recipient, NeoAsset));
                Assert.AreEqual(1, target.CreditCount);
                Assert.AreEqual(0, acknowledgement.AcknowledgementCount);
                Assert.AreEqual(L2PayoutRelayState.CreditPrepared,
                    outbox.LoadPending().Single().State,
                    "prepared transaction and retry state must be durable before restart");
            }

            using (var reopenedStore = new RocksDbKeyValueStore(directory))
            using (var reopenedOutbox = new PersistentL2PayoutOutbox(reopenedStore))
            {
                Assert.IsFalse(reopenedOutbox.Enqueue(instruction),
                    "source event replay stays idempotent after process restart");
                using var restarted = new L2PayoutRelay(
                    reopenedOutbox, target, acknowledgement);
                Assert.AreEqual(1, await restarted.ProcessAsync());
                Assert.AreEqual(0, reopenedOutbox.LoadPending().Count);
            }

            Assert.AreEqual(new BigInteger(250), target.BalanceOf(Recipient, NeoAsset));
            Assert.AreEqual(1, target.CreditCount,
                "one canonical message must mint exactly once across ambiguous broadcast recovery");
            Assert.AreEqual(1, acknowledgement.AcknowledgementCount);
        }
        finally
        {
            if (Directory.Exists(directory))
                try { Directory.Delete(directory, recursive: true); } catch { }
        }
    }

    [TestMethod]
    public void Instruction_TamperedCanonicalFieldOrHash_IsRejected()
    {
        var instruction = Instruction(amount: 250);
        var tampered = instruction.CanonicalMessageBytes.ToArray();
        tampered[37] ^= 0x01;

        Assert.ThrowsExactly<InvalidDataException>(() => L2PayoutInstruction.Decode(
            instruction.Sequence,
            instruction.Adapter,
            instruction.NeoAsset,
            instruction.Message.MessageHash,
            tampered,
            NeoChainId));
        Assert.ThrowsExactly<InvalidDataException>(() => L2PayoutInstruction.Decode(
            instruction.Sequence,
            instruction.Adapter,
            instruction.NeoAsset,
            H256(0x99),
            instruction.CanonicalMessageBytes,
            NeoChainId));
    }

    private static L2PayoutInstruction Instruction(BigInteger amount)
    {
        var payload = new ExternalAssetTransferPayload
        {
            ForeignAsset = ForeignAsset,
            Amount = amount,
        }.Encode();
        var message = ExternalMessageBuilder.Build(
            ExternalChainId,
            NeoChainId,
            nonce: 7,
            ExternalBridgeDirection.ForeignToNeo,
            ForeignSender,
            Recipient,
            deadlineUnixSeconds: 1_900_000_000,
            SourceTransaction,
            ExternalMessageType.AssetTransfer,
            payload);
        var canonical = ExternalMessageHasher.EncodeCanonical(message);
        return L2PayoutInstruction.Decode(
            sequence: 1,
            Adapter,
            NeoAsset,
            message.MessageHash,
            canonical,
            NeoChainId);
    }

    private static UInt160 H160(byte value) =>
        new(Enumerable.Repeat(value, UInt160.Length).ToArray());

    private static UInt256 H256(byte value) =>
        new(Enumerable.Repeat(value, UInt256.Length).ToArray());

    private sealed class FakeCreditClient : IL2PayoutCreditClient
    {
        private readonly Dictionary<(uint Chain, ulong Nonce), L2PayoutCreditObservation> _applied = [];
        private readonly Dictionary<string, L2PayoutInstruction> _prepared = [];
        private readonly Dictionary<(UInt160 Recipient, UInt160 Asset), BigInteger> _balances = [];
        private bool _throwAfterFirstCredit;

        public FakeCreditClient(bool throwAfterFirstCredit = false)
        {
            _throwAfterFirstCredit = throwAfterFirstCredit;
        }

        public int CreditCount { get; private set; }

        public BigInteger BalanceOf(UInt160 recipient, UInt160 asset) =>
            _balances.GetValueOrDefault((recipient, asset));

        public ValueTask<L2PayoutCreditObservation> ObserveAsync(
            L2PayoutInstruction instruction,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_applied.GetValueOrDefault(
                (instruction.Message.ExternalChainId, instruction.Message.Nonce),
                L2PayoutCreditObservation.Missing));
        }

        public ValueTask<ReadOnlyMemory<byte>> PrepareAsync(
            L2PayoutInstruction instruction,
            CancellationToken cancellationToken = default)
        {
            var bytes = instruction.Message.MessageHash.GetSpan().ToArray();
            _prepared[Convert.ToHexString(bytes)] = instruction;
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        public ValueTask<UInt256> BroadcastAsync(
            ReadOnlyMemory<byte> signedTransaction,
            CancellationToken cancellationToken = default)
        {
            var key = Convert.ToHexString(signedTransaction.Span);
            var instruction = _prepared[key];
            var identity = (instruction.Message.ExternalChainId, instruction.Message.Nonce);
            var transactionHash = new UInt256(Crypto.Hash256(signedTransaction.Span));
            if (_applied.TryGetValue(identity, out var existing))
            {
                if (existing.MessageHash != instruction.Message.MessageHash)
                    throw new InvalidDataException("nonce already consumed by another message");
                return ValueTask.FromResult(existing.TransactionHash);
            }

            var balanceKey = (instruction.Message.Recipient, instruction.NeoAsset);
            _balances[balanceKey] = _balances.GetValueOrDefault(balanceKey) + instruction.Amount;
            _applied[identity] = new L2PayoutCreditObservation(
                instruction.Message.MessageHash, transactionHash);
            CreditCount++;
            if (_throwAfterFirstCredit)
            {
                _throwAfterFirstCredit = false;
                throw new IOException("simulated ambiguous response after target credit");
            }
            return ValueTask.FromResult(transactionHash);
        }
    }

    private sealed class FakeAcknowledgementClient : IL1PayoutAcknowledgementClient
    {
        private readonly Dictionary<ulong, UInt256> _acknowledged = [];
        private readonly Dictionary<string, (L2PayoutInstruction Instruction, UInt256 L2TransactionHash)> _prepared = [];

        public int AcknowledgementCount { get; private set; }

        public ValueTask<L1PayoutAcknowledgementObservation> ObserveAsync(
            L2PayoutInstruction instruction,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(_acknowledged.TryGetValue(instruction.Sequence, out var hash)
                ? new L1PayoutAcknowledgementObservation(true, hash)
                : L1PayoutAcknowledgementObservation.Missing);
        }

        public ValueTask<ReadOnlyMemory<byte>> PrepareAsync(
            L2PayoutInstruction instruction,
            UInt256 l2TransactionHash,
            CancellationToken cancellationToken = default)
        {
            var bytes = new byte[sizeof(ulong) + UInt256.Length];
            BitConverter.GetBytes(instruction.Sequence).CopyTo(bytes, 0);
            l2TransactionHash.GetSpan().CopyTo(bytes.AsSpan(sizeof(ulong)));
            _prepared[Convert.ToHexString(bytes)] = (instruction, l2TransactionHash);
            return ValueTask.FromResult<ReadOnlyMemory<byte>>(bytes);
        }

        public ValueTask<UInt256> BroadcastAsync(
            ReadOnlyMemory<byte> signedTransaction,
            CancellationToken cancellationToken = default)
        {
            var prepared = _prepared[Convert.ToHexString(signedTransaction.Span)];
            if (_acknowledged.TryGetValue(prepared.Instruction.Sequence, out var existing))
            {
                if (existing != prepared.L2TransactionHash)
                    throw new InvalidDataException("sequence acknowledged by another L2 transaction");
            }
            else
            {
                _acknowledged[prepared.Instruction.Sequence] = prepared.L2TransactionHash;
                AcknowledgementCount++;
            }
            return ValueTask.FromResult(new UInt256(Crypto.Hash256(signedTransaction.Span)));
        }
    }
}
