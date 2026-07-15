using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Neo.Extensions;
using Neo.L2.Bridge;
using Neo.L2.Executor.Effects;
using Neo.L2.Executor.State;
using Neo.L2.Persistence;
using Neo.L2.State;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.VM;

namespace Neo.L2.Executor;

/// <summary>Canonical N4-genesis V1 adapter for the restricted L1 inbox transition.</summary>
/// <remarks>See <c>doc.md</c> §8 and <c>SPEC.md</c> “N4 genesis V1”.</remarks>
public sealed class CanonicalL1MessageProcessor : IL1MessageProcessor
{
    private const int L2BridgeId = -104;
    private const int BridgedNep17Id = -109;
    private const int TokenManagementId = -12;
    private const int ContractManagementId = -1;
    private const byte PrefixContract = 0x08;
    private const byte PrefixTokenState = 0x0a;
    private const byte PrefixAccountState = 0x0c;
    private const byte PrefixMapping = 0x01;
    private const byte PrefixDepositConsumed = 0x02;
    private const byte PrefixAuthorizedBridge = 0x03;
    private const byte KeyBridge = 0xfe;
    private static readonly BigInteger MaxMintAmount = BigInteger.One << 128;

    private readonly IL2KeyValueStore _state;

    /// <summary>Create an adapter over the live Neo-compatible state store.</summary>
    public CanonicalL1MessageProcessor(IL2KeyValueStore state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
    }

    /// <inheritdoc />
    public ValueTask ApplyBatchAsync(
        uint chainId,
        IReadOnlyList<CrossChainMessage> messages,
        CancellationToken cancellationToken = default)
    {
        if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId));
        ArgumentNullException.ThrowIfNull(messages);
        cancellationToken.ThrowIfCancellationRequested();
        if (messages.Count == 0) return ValueTask.CompletedTask;

        using var transaction = new ExecutionStateTransaction(_state);
        try
        {
            RequireNativeContract(transaction, NativeContract.L2Bridge.Hash);
            RequireNativeContract(transaction, NativeContract.BridgedNep17.Hash);
            RequireNativeContract(transaction, NativeContract.TokenManagement.Hash);
            RequireBridgeAuthorization(transaction);
            for (var index = 0; index < messages.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var message = messages[index]
                    ?? throw new ArgumentException($"messages[{index}] is null", nameof(messages));
                ApplyDeposit(chainId, transaction, message);
            }
            transaction.Commit();
            return ValueTask.CompletedTask;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static void ApplyDeposit(
        uint chainId,
        ExecutionStateTransaction state,
        CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message.Sender);
        ArgumentNullException.ThrowIfNull(message.Receiver);
        ArgumentNullException.ThrowIfNull(message.MessageHash);
        if (message.MessageType != MessageType.Deposit)
            throw new NotSupportedException($"L1 inbox MessageType {message.MessageType} is not supported by N4 genesis V1");
        if (message.SourceChainId != 0
            || message.TargetChainId != chainId
            || message.Sender == UInt160.Zero
            || message.Receiver != NativeContract.L2Bridge.Hash)
            throw new InvalidDataException("Invalid L1 deposit routing");
        if (MessageHasher.HashMessage(message) != message.MessageHash)
            throw new InvalidDataException("L1 deposit MessageHash mismatch");

        var deposit = DepositPayload.Decode(message.Payload.Span);
        if (!deposit.Encode().AsSpan().SequenceEqual(message.Payload.Span)
            || deposit.L1Asset == UInt160.Zero
            || deposit.L2Recipient == UInt160.Zero
            || deposit.Amount <= BigInteger.Zero)
            throw new InvalidDataException("Non-canonical deposit payload");

        var replayKey = Key(
            L2BridgeId,
            PrefixDepositConsumed,
            UInt32(message.SourceChainId),
            UInt64(message.Nonce));
        if (state.Contains(replayKey)) throw new InvalidDataException("Deposit replayed");

        var mappingBytes = state.Get(Key(L2BridgeId, PrefixMapping, deposit.L1Asset.GetSpan().ToArray()))
            ?? throw new InvalidDataException("Missing L2 bridge asset mapping");
        if (mappingBytes.Length != 22)
            throw new InvalidDataException("N4 genesis V1 requires a 22-byte decimal-aware bridge mapping");
        var l2Asset = new UInt160(mappingBytes.AsSpan(0, UInt160.Length));
        var l1Decimals = mappingBytes[20];
        var l2Decimals = mappingBytes[21];
        if (l2Asset == UInt160.Zero || l1Decimals > 18 || l2Decimals > 18)
            throw new InvalidDataException("Invalid L2 bridge asset mapping");
        var l2Amount = ScaleAmount(deposit.Amount, l1Decimals, l2Decimals);
        if (l2Amount <= BigInteger.Zero || l2Amount > MaxMintAmount)
            throw new InvalidDataException("Deposit amount exceeds TokenManagement mint bounds");

        if (state.Contains(Key(ContractManagementId, PrefixContract, deposit.L2Recipient.GetSpan().ToArray())))
            throw new NotSupportedException("N4 genesis V1 deposit recipients must not be contracts");

        var tokenKey = Key(TokenManagementId, PrefixTokenState, l2Asset.GetSpan().ToArray());
        var tokenBytes = state.Get(tokenKey)
            ?? throw new InvalidDataException("Missing TokenManagement token state");
        var token = DecodeTokenState(tokenBytes);
        if (token.Type != TokenType.Fungible
            || token.Owner != NativeContract.BridgedNep17.Hash
            || token.Decimals != l2Decimals
            || TokenManagement.GetAssetId(token.Owner, token.Name) != l2Asset
            || token.TotalSupply < BigInteger.Zero
            || token.MaxSupply < BigInteger.MinusOne)
            throw new InvalidDataException("Bridged token metadata mismatch");
        token.TotalSupply += l2Amount;
        if (token.MaxSupply >= BigInteger.Zero && token.TotalSupply > token.MaxSupply)
            throw new InvalidDataException("Bridged token maximum supply exceeded");

        var accountKey = Key(
            TokenManagementId,
            PrefixAccountState,
            deposit.L2Recipient.GetSpan().ToArray(),
            l2Asset.GetSpan().ToArray());
        var accountBytes = state.Get(accountKey);
        var account = accountBytes is null
            ? new AccountState()
            : DecodeAccountState(accountBytes);
        if (account.Balance < BigInteger.Zero)
            throw new InvalidDataException("Negative TokenManagement account balance");
        account.Balance += l2Amount;

        state.Put(replayKey, [1]);
        state.Put(tokenKey, BinarySerializer.Serialize(token.ToStackItem(null), ExecutionEngineLimits.Default));
        state.Put(accountKey, BinarySerializer.Serialize(account.ToStackItem(null), ExecutionEngineLimits.Default));
    }

    private static TokenState DecodeTokenState(byte[] bytes)
    {
        var token = new TokenState
        {
            Type = TokenType.Fungible,
            Owner = UInt160.Zero,
            Name = string.Empty,
            Symbol = string.Empty,
            Decimals = 0,
        };
        try
        {
            token.FromStackItem(BinarySerializer.Deserialize(bytes, ExecutionEngineLimits.Default));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("Invalid TokenManagement token state", exception);
        }
        var canonical = BinarySerializer.Serialize(token.ToStackItem(null), ExecutionEngineLimits.Default);
        if (!canonical.AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Non-canonical TokenManagement token state");
        return token;
    }

    private static AccountState DecodeAccountState(byte[] bytes)
    {
        var account = new AccountState();
        try
        {
            account.FromStackItem(BinarySerializer.Deserialize(bytes, ExecutionEngineLimits.Default));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidDataException("Invalid TokenManagement account state", exception);
        }
        var canonical = BinarySerializer.Serialize(account.ToStackItem(null), ExecutionEngineLimits.Default);
        if (!canonical.AsSpan().SequenceEqual(bytes))
            throw new InvalidDataException("Non-canonical TokenManagement account state");
        return account;
    }

    private static BigInteger ScaleAmount(BigInteger amount, byte from, byte to)
    {
        if (to >= from) return amount * BigInteger.Pow(10, to - from);
        var divisor = BigInteger.Pow(10, from - to);
        var quotient = BigInteger.DivRem(amount, divisor, out var remainder);
        if (!remainder.IsZero) throw new InvalidDataException("Inexact bridge decimal scaling");
        return quotient;
    }

    private static void RequireNativeContract(ExecutionStateTransaction state, UInt160 hash)
    {
        var value = state.Get(Key(ContractManagementId, PrefixContract, hash.GetSpan().ToArray()));
        if (value is null || value.Length == 0)
            throw new InvalidDataException($"Missing native contract state witness {hash}");
    }

    private static void RequireBridgeAuthorization(ExecutionStateTransaction state)
    {
        var configured = state.Get(Key(BridgedNep17Id, KeyBridge));
        if (configured is not null && configured.AsSpan().SequenceEqual(NativeContract.L2Bridge.Hash.GetSpan()))
            return;
        if (state.Contains(Key(
            BridgedNep17Id,
            PrefixAuthorizedBridge,
            NativeContract.L2Bridge.Hash.GetSpan().ToArray())))
            return;
        throw new InvalidDataException("BridgedNep17 does not authorize L2Bridge");
    }

    private static byte[] UInt32(uint value)
    {
        var bytes = new byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] UInt64(ulong value)
    {
        var bytes = new byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Key(int contractId, byte prefix, params byte[][] parts)
    {
        var length = sizeof(int) + 1 + parts.Sum(static part => part.Length);
        var key = new byte[length];
        BinaryPrimitives.WriteInt32LittleEndian(key, contractId);
        key[sizeof(int)] = prefix;
        var offset = sizeof(int) + 1;
        foreach (var part in parts)
        {
            part.AsSpan().CopyTo(key.AsSpan(offset));
            offset += part.Length;
        }
        return key;
    }
}

/// <summary>Derives batch outbox records only from canonical N4 native notifications.</summary>
/// <remarks>See <c>doc.md</c> §8 and <c>SPEC.md</c> “N4 genesis V1”.</remarks>
public static class CanonicalNativeEffectsAdapter
{
    /// <summary>Decode trusted withdrawals and messages in execution order.</summary>
    public static (
        IReadOnlyList<WithdrawalRequest> Withdrawals,
        IReadOnlyList<CrossChainMessage> Messages) Derive(
        uint chainId,
        CanonicalExecutionEffects effects)
    {
        if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId));
        ArgumentNullException.ThrowIfNull(effects);
        var withdrawals = new List<WithdrawalRequest>();
        var messages = new List<CrossChainMessage>();
        var withdrawalNonces = new HashSet<(UInt160 Sender, ulong Nonce)>();
        var messageNonces = new HashSet<(UInt160 Sender, ulong Nonce)>();

        foreach (var executionEvent in effects.Events)
        {
            if (executionEvent.ScriptHash == NativeContract.L2Bridge.Hash
                && executionEvent.EventName == "WithdrawalEmitted")
            {
                var reader = CanonicalNativeEventReader.Create(executionEvent.State.Span, 5);
                var sender = reader.ReadHash160("withdrawal sender");
                var recipient = reader.ReadHash160("withdrawal recipient");
                var asset = reader.ReadHash160("withdrawal asset");
                var amount = reader.ReadPositiveInteger("withdrawal amount");
                var nonce = reader.ReadUInt64("withdrawal nonce");
                reader.EnsureEnd();
                if (sender == UInt160.Zero || recipient == UInt160.Zero || asset == UInt160.Zero || nonce == 0)
                    throw new InvalidDataException("Invalid WithdrawalEmitted event");
                if (amount.ToByteArray(isUnsigned: true, isBigEndian: false).Length > 64)
                    throw new InvalidDataException("Withdrawal amount exceeds 64 bytes");
                if (!withdrawalNonces.Add((sender, nonce)))
                    throw new InvalidDataException("Duplicate WithdrawalEmitted nonce");
                withdrawals.Add(new WithdrawalRequest
                {
                    ChainId = chainId,
                    EmittingContract = NativeContract.L2Bridge.Hash,
                    L2Sender = sender,
                    L1Recipient = recipient,
                    L2Asset = asset,
                    Amount = amount,
                    Nonce = nonce,
                });
            }
            else if (executionEvent.ScriptHash == NativeContract.L2Message.Hash
                && executionEvent.EventName == "MessageEmitted")
            {
                var reader = CanonicalNativeEventReader.Create(executionEvent.State.Span, 7);
                var sourceChainId = reader.ReadUInt32("message sourceChainId");
                var targetChainId = reader.ReadUInt32("message targetChainId");
                var nonce = reader.ReadUInt64("message nonce");
                var sender = reader.ReadHash160("message sender");
                var receiver = reader.ReadHash160("message receiver");
                var messageTypeByte = reader.ReadByteInteger("message type");
                var payload = reader.ReadByteString("message payload", 4 * 1024 * 1024);
                reader.EnsureEnd();
                if (sourceChainId != chainId
                    || targetChainId == sourceChainId
                    || nonce == 0
                    || sender == UInt160.Zero
                    || receiver == UInt160.Zero
                    || !Enum.IsDefined((MessageType)messageTypeByte))
                    throw new InvalidDataException("Invalid MessageEmitted event");
                if (!messageNonces.Add((sender, nonce)))
                    throw new InvalidDataException("Duplicate MessageEmitted nonce");
                var withoutHash = new CrossChainMessage
                {
                    SourceChainId = sourceChainId,
                    TargetChainId = targetChainId,
                    Nonce = nonce,
                    Sender = sender,
                    Receiver = receiver,
                    MessageType = (MessageType)messageTypeByte,
                    Payload = payload,
                    MessageHash = UInt256.Zero,
                };
                messages.Add(withoutHash with { MessageHash = MessageHasher.HashMessage(withoutHash) });
            }
        }
        return (withdrawals, messages);
    }

    private ref struct CanonicalNativeEventReader
    {
        private static ReadOnlySpan<byte> Magic => "NEO4STK1"u8;
        private ReadOnlySpan<byte> _bytes;
        private int _offset;

        public static CanonicalNativeEventReader Create(ReadOnlySpan<byte> bytes, uint fieldCount)
        {
            var reader = new CanonicalNativeEventReader { _bytes = bytes, _offset = 0 };
            reader.Require(Magic, "canonical stack magic");
            if (reader.ReadUInt16("canonical stack version") != 1
                || reader.ReadUInt16("canonical stack flags") != 0
                || reader.ReadByte("event array tag") != 0x40
                || reader.ReadUInt32Raw("event field count") != fieldCount)
                throw new InvalidDataException("Invalid canonical native event state");
            return reader;
        }

        public UInt160 ReadHash160(string field)
        {
            var bytes = ReadByteString(field, UInt160.Length);
            if (bytes.Length != UInt160.Length) throw new InvalidDataException($"Invalid {field}");
            return new UInt160(bytes.Span);
        }

        public uint ReadUInt32(string field)
            => checked((uint)ReadUnsignedInteger(field, uint.MaxValue));

        public ulong ReadUInt64(string field)
            => checked((ulong)ReadUnsignedInteger(field, ulong.MaxValue));

        public byte ReadByteInteger(string field)
            => checked((byte)ReadUnsignedInteger(field, byte.MaxValue));

        public BigInteger ReadPositiveInteger(string field)
        {
            var value = ReadInteger(field);
            if (value <= BigInteger.Zero) throw new InvalidDataException($"Invalid {field}");
            return value;
        }

        public ReadOnlyMemory<byte> ReadByteString(string field, int maxLength)
        {
            if (ReadByte($"{field} tag") != 0x28) throw new InvalidDataException($"Invalid {field}");
            var length = ReadUInt32Raw($"{field} length");
            if (length > maxLength) throw new InvalidDataException($"{field} exceeds {maxLength} bytes");
            return ReadBytes(checked((int)length), field).ToArray();
        }

        public void EnsureEnd()
        {
            if (_offset != _bytes.Length) throw new InvalidDataException("Trailing canonical event bytes");
        }

        private BigInteger ReadUnsignedInteger(string field, BigInteger maximum)
        {
            var value = ReadInteger(field);
            if (value < BigInteger.Zero || value > maximum) throw new InvalidDataException($"Invalid {field}");
            return value;
        }

        private BigInteger ReadInteger(string field)
        {
            if (ReadByte($"{field} tag") != 0x21) throw new InvalidDataException($"Invalid {field}");
            var length = ReadUInt32Raw($"{field} length");
            if (length > 64) throw new InvalidDataException($"{field} exceeds 64 bytes");
            var bytes = ReadBytes(checked((int)length), field);
            var value = new BigInteger(bytes, isUnsigned: false, isBigEndian: false);
            if (!value.ToByteArrayStandard().AsSpan().SequenceEqual(bytes))
                throw new InvalidDataException($"Non-canonical {field}");
            return value;
        }

        private ushort ReadUInt16(string field)
            => BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(sizeof(ushort), field));

        private uint ReadUInt32Raw(string field)
            => BinaryPrimitives.ReadUInt32LittleEndian(ReadBytes(sizeof(uint), field));

        private byte ReadByte(string field)
        {
            var bytes = ReadBytes(1, field);
            return bytes[0];
        }

        private void Require(ReadOnlySpan<byte> expected, string field)
        {
            if (!ReadBytes(expected.Length, field).SequenceEqual(expected))
                throw new InvalidDataException($"Invalid {field}");
        }

        private ReadOnlySpan<byte> ReadBytes(int length, string field)
        {
            if (length < 0 || _offset > _bytes.Length - length)
                throw new InvalidDataException($"Truncated {field}");
            var value = _bytes.Slice(_offset, length);
            _offset += length;
            return value;
        }
    }
}
