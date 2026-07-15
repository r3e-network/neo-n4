using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Neo.L2.Bridge;
using Neo.L2.Executor.Effects;
using Neo.L2.Messaging;
using Neo.L2.Persistence;
using Neo.L2.State;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using Array = Neo.VM.Types.Array;

namespace Neo.L2.Executor.UnitTests;

/// <summary>Cross-language tests for the restricted canonical N4 native transition V1.</summary>
/// <remarks>See <c>doc.md</c> §8 and <c>SPEC.md</c> “N4 genesis V1”.</remarks>
[TestClass]
public class UT_CanonicalNativeExecutionAdapter
{
    private const uint ChainId = 1099;
    private const int L2BridgeId = -104;
    private const int BridgedNep17Id = -109;
    private const int TokenManagementId = -12;
    private const int ContractManagementId = -1;

    [TestMethod]
    public void NativeContractHashes_MatchRustAndN4CoreGoldens()
    {
        AssertHash(NativeContract.L2Bridge.Hash, "e44d9687201a211e2ee8809d55b42152442a9a05");
        AssertHash(NativeContract.L2Message.Hash, "789d668525bfeb59a82300bdecebffc864f24295");
        AssertHash(NativeContract.BridgedNep17.Hash, "4fca40807f7bc98fc22f9642cc66302f9a4d3318");
        AssertHash(NativeContract.TokenManagement.Hash, "9f040ea4a8448f015af645659b0fb2ae7dc500ae");
        AssertHash(NativeContract.Governance.Hash, "67ca70350663bf258ca513049467c6059d15e74c");
    }

    [TestMethod]
    public async Task DepositV1_UsesNativeTokenStateAndMatchesRustStateRoot()
    {
        using var fixture = DepositFixture.Create();
        AssertHash(await StateRoot(fixture.Store), "4900cdb769870ea32d24f925965824a6efcd13ac7a495982b4004c3abb42a1a9");

        var processor = new CanonicalL1MessageProcessor(fixture.Store);
        await processor.ApplyBatchAsync(ChainId, [fixture.Message]);

        AssertHash(await StateRoot(fixture.Store), "bac5b837912c96232e40e471c9b5c2bff886f8b1cdbe8417004ed0ad4df143ac");
        CollectionAssert.AreEqual(new byte[] { 1 }, fixture.Store.Get(fixture.ReplayKey));
        Assert.AreEqual(new BigInteger(12_345_600), ReadAccount(fixture.Store.Get(fixture.AccountKey)!));
        Assert.AreEqual(new BigInteger(12_345_605), ReadToken(fixture.Store.Get(fixture.TokenKey)!).TotalSupply);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await processor.ApplyBatchAsync(ChainId, [fixture.Message]));
    }

    [TestMethod]
    public async Task DepositV1_TamperingAndUnsupportedMessagesFailAtomically()
    {
        await AssertDepositRejected(static fixture => fixture.Message with { SourceChainId = 1 });
        await AssertDepositRejected(static fixture => fixture.Message with { TargetChainId = ChainId + 1 });
        await AssertDepositRejected(static fixture => fixture.Message with { Receiver = Repeat160(0x99) });
        await AssertDepositRejected(static fixture => fixture.Message with { MessageHash = UInt256.Zero });
        await AssertDepositRejected(static fixture => fixture.Message with { MessageType = MessageType.Call });
        await AssertDepositRejected(
            static fixture => Rehash(fixture.Message with
            {
                Payload = new DepositPayload
                {
                    L1Asset = fixture.L1Asset,
                    L2Recipient = fixture.Recipient,
                    Amount = BigInteger.Zero,
                }.Encode(),
            }));
        await AssertDepositRejected(
            static fixture => fixture.Message,
            static fixture => fixture.Store.Delete(fixture.MappingKey));
        await AssertDepositRejected(
            static fixture => fixture.Message,
            static fixture =>
            {
                var mapping = fixture.Store.Get(fixture.MappingKey)!;
                mapping[21] = 19;
                fixture.Store.Put(fixture.MappingKey, mapping);
            });
    }

    [TestMethod]
    public void OutboundV1_MatchesRustRootsAndBindsOrderAndParameters()
    {
        var effects = OutboundEffects();
        var canonical = CanonicalNativeEffectsAdapter.Derive(ChainId, effects);
        var withdrawals = new WithdrawalTree();
        var outbox = new L2Outbox();
        foreach (var withdrawal in canonical.Withdrawals) withdrawals.Add(withdrawal);
        foreach (var message in canonical.Messages) outbox.Add(message);

        AssertHash(withdrawals.Root, "737a25af11ca940d4e9004a43bd0129f3000df624a9d845752439d819ddae593");
        AssertHash(outbox.L2ToL1Root, "00f3a7ccaf825db24ea4a674fd9be0af0a5f7cda0f8ebd28d9ca345b4ef1e8af");
        AssertHash(outbox.L2ToL2Root, "d6aa57f677872096fa2ce57c58d652f0e632179fa3a2fe5d2b6fb1c957c10881");

        var changed = CanonicalNativeEffectsAdapter.Derive(
            ChainId,
            CanonicalExecutionEffects.Create([], [WithdrawalEvent(124), L1MessageEvent(), L2MessageEvent()]));
        var changedTree = new WithdrawalTree();
        changedTree.Add(changed.Withdrawals.Single());
        Assert.AreNotEqual(withdrawals.Root, changedTree.Root);

        var ordered = CanonicalNativeEffectsAdapter.Derive(
            ChainId,
            CanonicalExecutionEffects.Create(
                [],
                [L2MessageEvent(), MessageEvent(2201, 3, 0x67, MessageType.Event, "uvw"u8.ToArray())]));
        var reordered = CanonicalNativeEffectsAdapter.Derive(
            ChainId,
            CanonicalExecutionEffects.Create(
                [],
                [MessageEvent(2201, 3, 0x67, MessageType.Event, "uvw"u8.ToArray()), L2MessageEvent()]));
        var orderedOutbox = new L2Outbox();
        foreach (var message in ordered.Messages) orderedOutbox.Add(message);
        var reorderedOutbox = new L2Outbox();
        foreach (var message in reordered.Messages) reorderedOutbox.Add(message);
        Assert.AreNotEqual(orderedOutbox.L2ToL2Root, reorderedOutbox.L2ToL2Root);
    }

    [TestMethod]
    public void OutboundV1_MalformedReservedEventsFailClosedAndUserNotifyCannotSpoof()
    {
        var malformed = CanonicalExecutionEffects.Create(
            [],
            [new NotifyEventArgs(null, NativeContract.L2Message.Hash, "MessageEmitted", new Array(null))]);
        Assert.ThrowsExactly<InvalidDataException>(() => CanonicalNativeEffectsAdapter.Derive(ChainId, malformed));

        var spoof = CanonicalExecutionEffects.Create(
            [],
            [new NotifyEventArgs(null, Repeat160(0x99), "MessageEmitted", ((NotifyEventArgs)L1MessageEvent()).State)]);
        var derived = CanonicalNativeEffectsAdapter.Derive(ChainId, spoof);
        Assert.AreEqual(0, derived.Withdrawals.Count);
        Assert.AreEqual(0, derived.Messages.Count);
    }

    internal static CanonicalExecutionEffects OutboundEffects()
        => CanonicalExecutionEffects.Create([], [WithdrawalEvent(123), L1MessageEvent(), L2MessageEvent()]);

    private static NotifyEventArgs WithdrawalEvent(int amount)
        => new(
            null,
            NativeContract.L2Bridge.Hash,
            "WithdrawalEmitted",
            new Array(null,
            [
                new ByteString(Enumerable.Repeat((byte)0x44, UInt160.Length).ToArray()),
                new ByteString(Enumerable.Repeat((byte)0x77, UInt160.Length).ToArray()),
                new ByteString(Enumerable.Repeat((byte)0x88, UInt160.Length).ToArray()),
                new Integer(amount),
                new Integer(1),
            ]));

    private static NotifyEventArgs L1MessageEvent()
        => MessageEvent(0, 1, 0x55, MessageType.Call, "abc"u8.ToArray());

    private static NotifyEventArgs L2MessageEvent()
        => MessageEvent(2200, 2, 0x66, MessageType.Event, "xyz"u8.ToArray());

    private static NotifyEventArgs MessageEvent(
        uint targetChainId,
        ulong nonce,
        byte receiver,
        MessageType messageType,
        byte[] payload)
        => new(
            null,
            NativeContract.L2Message.Hash,
            "MessageEmitted",
            new Array(null,
            [
                new Integer(ChainId),
                new Integer(targetChainId),
                new Integer(nonce),
                new ByteString(Enumerable.Repeat((byte)0x44, UInt160.Length).ToArray()),
                new ByteString(Enumerable.Repeat(receiver, UInt160.Length).ToArray()),
                new Integer((byte)messageType),
                new ByteString(payload),
            ]));

    private static async Task AssertDepositRejected(
        Func<DepositFixture, CrossChainMessage> message,
        Action<DepositFixture>? mutateState = null)
    {
        using var fixture = DepositFixture.Create();
        mutateState?.Invoke(fixture);
        var before = await StateRoot(fixture.Store);
        var processor = new CanonicalL1MessageProcessor(fixture.Store);
        Exception? failure = null;
        try
        {
            await processor.ApplyBatchAsync(ChainId, [message(fixture)]);
        }
        catch (Exception exception)
        {
            failure = exception;
        }
        Assert.IsNotNull(failure);
        Assert.AreEqual(before, await StateRoot(fixture.Store));
    }

    private static CrossChainMessage Rehash(CrossChainMessage message)
        => message with { MessageHash = MessageHasher.HashMessage(message) };

    private static TokenState ReadToken(byte[] bytes)
    {
        var token = new TokenState
        {
            Type = TokenType.Fungible,
            Owner = UInt160.Zero,
            Name = string.Empty,
            Symbol = string.Empty,
            Decimals = 0,
        };
        token.FromStackItem(BinarySerializer.Deserialize(bytes, ExecutionEngineLimits.Default));
        return token;
    }

    private static BigInteger ReadAccount(byte[] bytes)
    {
        var account = new AccountState();
        account.FromStackItem(BinarySerializer.Deserialize(bytes, ExecutionEngineLimits.Default));
        return account.Balance;
    }

    private static ValueTask<UInt256> StateRoot(InMemoryKeyValueStore store)
        => new MerkleStatePostStateRootOracle(store).ResolveAsync(UInt256.Zero, UInt256.Zero, Context());

    private static BatchBlockContext Context() => new()
    {
        L1FinalizedHeight = 10,
        FirstBlockTimestamp = 100,
        LastBlockTimestamp = 100,
        SequencerCommitteeHash = new UInt256(Enumerable.Repeat((byte)0x44, UInt256.Length).ToArray()),
        Network = 0x334f_454e,
    };

    private static void AssertHash(UInt160 actual, string expected)
        => CollectionAssert.AreEqual(Convert.FromHexString(expected), actual.GetSpan().ToArray());

    private static void AssertHash(UInt256 actual, string expected)
        => CollectionAssert.AreEqual(Convert.FromHexString(expected), actual.GetSpan().ToArray());

    private static UInt160 Repeat160(byte value)
        => new(Enumerable.Repeat(value, UInt160.Length).ToArray());

    private static byte[] Key(int contractId, byte prefix, params byte[][] parts)
    {
        var key = new byte[sizeof(int) + 1 + parts.Sum(static part => part.Length)];
        BinaryPrimitives.WriteInt32LittleEndian(key, contractId);
        key[sizeof(int)] = prefix;
        var offset = sizeof(int) + 1;
        foreach (var part in parts)
        {
            part.CopyTo(key, offset);
            offset += part.Length;
        }
        return key;
    }

    private sealed class DepositFixture : IDisposable
    {
        public required InMemoryKeyValueStore Store { get; init; }
        public required CrossChainMessage Message { get; init; }
        public required UInt160 L1Asset { get; init; }
        public required UInt160 Recipient { get; init; }
        public required byte[] MappingKey { get; init; }
        public required byte[] TokenKey { get; init; }
        public required byte[] AccountKey { get; init; }
        public required byte[] ReplayKey { get; init; }

        public static DepositFixture Create()
        {
            var store = new InMemoryKeyValueStore();
            var l1Asset = Repeat160(0x22);
            var recipient = Repeat160(0x33);
            var l2Asset = TokenManagement.GetAssetId(NativeContract.BridgedNep17.Hash, "Wrapped GAS");
            foreach (var hash in new[]
                     {
                         NativeContract.L2Bridge.Hash,
                         NativeContract.BridgedNep17.Hash,
                         NativeContract.TokenManagement.Hash,
                     })
            {
                store.Put(Key(ContractManagementId, 0x08, hash.GetSpan().ToArray()), Encoding.ASCII.GetBytes("native"));
            }
            store.Put(Key(BridgedNep17Id, 0xfe), NativeContract.L2Bridge.Hash.GetSpan());
            var mappingKey = Key(L2BridgeId, 0x01, l1Asset.GetSpan().ToArray());
            store.Put(mappingKey, l2Asset.GetSpan().ToArray().Concat(new byte[] { 6, 8 }).ToArray());
            var tokenKey = Key(TokenManagementId, 0x0a, l2Asset.GetSpan().ToArray());
            var token = new TokenState
            {
                Type = TokenType.Fungible,
                Owner = NativeContract.BridgedNep17.Hash,
                Name = "Wrapped GAS",
                Symbol = "WGAS",
                Decimals = 8,
                TotalSupply = 5,
                MaxSupply = 1_000_000_000,
            };
            store.Put(
                tokenKey,
                BinarySerializer.Serialize(token.ToStackItem(null), ExecutionEngineLimits.Default));
            var payload = new DepositPayload
            {
                L1Asset = l1Asset,
                L2Recipient = recipient,
                Amount = 123_456,
            }.Encode();
            var message = Rehash(new CrossChainMessage
            {
                SourceChainId = 0,
                TargetChainId = ChainId,
                Nonce = 7,
                Sender = Repeat160(0x11),
                Receiver = NativeContract.L2Bridge.Hash,
                MessageType = MessageType.Deposit,
                Payload = payload,
                MessageHash = UInt256.Zero,
            });
            return new DepositFixture
            {
                Store = store,
                Message = message,
                L1Asset = l1Asset,
                Recipient = recipient,
                MappingKey = mappingKey,
                TokenKey = tokenKey,
                AccountKey = Key(
                    TokenManagementId,
                    0x0c,
                    recipient.GetSpan().ToArray(),
                    l2Asset.GetSpan().ToArray()),
                ReplayKey = Key(L2BridgeId, 0x02, new byte[4], BitConverter.GetBytes(7UL)),
            };
        }

        public void Dispose() => Store.Dispose();
    }
}
