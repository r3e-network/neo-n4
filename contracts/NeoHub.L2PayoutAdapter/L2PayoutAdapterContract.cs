using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.L2PayoutAdapter;

/// <summary>
/// Immutable L1 adapter that durably enqueues verified external-bridge payouts for one Neo L2.
/// </summary>
/// <remarks>
/// See <c>doc.md</c> §11.3. The escrow call and queue write are one NeoVM transaction. Target-L2
/// credit is asynchronous: an authenticated relay submits the exact signed bytes to the L2 native
/// bridge, confirms the one-time credit, then acknowledges this queue entry.
/// </remarks>
[DisplayName("NeoHub.L2PayoutAdapter")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Authenticated durable L1-to-L2 external-bridge payout queue.")]
[ContractVersion("1.0.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.L2PayoutAdapter")]
public class L2PayoutAdapterContract : SmartContract
{
    private const byte PrefixPayout = 0x01;
    private const byte PrefixSequenceByMessageHash = 0x02;
    private const byte KeyLastSequence = 0xFA;
    private const byte KeyRelayAccount = 0xFB;
    private const byte KeyNeoChainId = 0xFC;
    private const byte KeyEscrow = 0xFD;
    private const byte KeyOwner = 0xFF;
    private const byte StatusEnqueued = 1;
    private const byte StatusAcknowledged = 2;
    private const byte PayoutAbiVersion = 1;
    private const byte DirectionForeignToNeo = 2;
    private const byte MessageTypeAssetTransfer = 0;
    private const int FixedMessagePrefixSize = 102;
    private const int MaxPayloadLength = 64 * 1024;
    private const int MaxAmountBytes = 32;
    private const int RecordNeoAssetOffset = 1;
    private const int RecordMessageHashOffset = RecordNeoAssetOffset + 20;
    private const int RecordL2TransactionHashOffset = RecordMessageHashOffset + 32;
    private const int RecordMessageOffset = RecordL2TransactionHashOffset + 32;

    /// <summary>Emitted only after the exact canonical payout instruction is durably stored.</summary>
    [DisplayName("PayoutEnqueued")]
    public static event Action<ulong, UInt256, uint, uint, ulong, UInt160, UInt160, UInt160,
        BigInteger, ulong, UInt256, byte[]> OnPayoutEnqueued = default!;

    /// <summary>Emitted after the authenticated relay confirms target-L2 credit.</summary>
    [DisplayName("PayoutAcknowledged")]
    public static event Action<ulong, UInt256, UInt256> OnPayoutAcknowledged = default!;

    /// <summary>Deploy data is <c>[owner, escrow, neoChainId, relayAccount]</c>.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var values = (object[])data;
        var owner = (UInt160)values[0];
        var escrow = (UInt160)values[1];
        var neoChainIdValue = (BigInteger)values[2];
        var relayAccount = (UInt160)values[3];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(escrow.IsValid && !escrow.IsZero, "invalid escrow");
        ExecutionEngine.Assert(neoChainIdValue > 0 && neoChainIdValue <= uint.MaxValue,
            "invalid Neo L2 chain id");
        ExecutionEngine.Assert(relayAccount.IsValid && !relayAccount.IsZero,
            "invalid relay account");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyEscrow }, escrow);
        Storage.Put(new byte[] { KeyNeoChainId }, neoChainIdValue);
        Storage.Put(new byte[] { KeyRelayAccount }, relayAccount);
    }

    /// <summary>Version pinned by <c>ExternalBridgeEscrow</c> before route activation.</summary>
    [Safe]
    [DisplayName("payoutVersion")]
    public static byte PayoutVersion() => PayoutAbiVersion;

    /// <summary>Immutable escrow permitted to enqueue payouts.</summary>
    [Safe]
    public static UInt160 GetEscrow() => ReadUInt160(KeyEscrow);

    /// <summary>Immutable target Neo L2 chain id.</summary>
    [Safe]
    public static uint GetNeoChainId() => (uint)ReadInteger(KeyNeoChainId);

    /// <summary>Authenticated hot account permitted to acknowledge target credit.</summary>
    [Safe]
    public static UInt160 GetRelayAccount() => ReadUInt160(KeyRelayAccount);

    /// <summary>Last monotonically allocated queue sequence.</summary>
    [Safe]
    public static ulong GetLastSequence() => (ulong)ReadInteger(KeyLastSequence);

    /// <summary>
    /// Validate every escrow argument against the exact signed canonical bytes, persist one queue
    /// entry, and return success only after storage has been written.
    /// </summary>
    public static bool Payout(
        uint externalChainId,
        uint neoChainId,
        ulong nonce,
        UInt160 foreignAsset,
        UInt160 neoAsset,
        UInt160 recipient,
        BigInteger amount,
        ulong deadlineUnixSeconds,
        UInt256 sourceTxRef,
        byte[] messageBytes)
    {
        ExecutionEngine.Assert(Runtime.CallingScriptHash == GetEscrow(),
            "payout caller is not the pinned escrow");
        ExecutionEngine.Assert(neoAsset.IsValid && !neoAsset.IsZero, "invalid mapped Neo asset");
        var messageHash = ValidateCanonicalMessage(
            externalChainId, neoChainId, nonce, foreignAsset, recipient, amount,
            deadlineUnixSeconds, sourceTxRef, messageBytes);

        var existingSequenceRaw = Storage.Get(MessageHashKey(messageHash));
        if (existingSequenceRaw != null)
        {
            var existingSequence = (ulong)(BigInteger)existingSequenceRaw;
            var existingRecord = ReadRecord(existingSequence);
            ExecutionEngine.Assert(ReadUInt160(existingRecord, RecordNeoAssetOffset) == neoAsset,
                "message hash already queued for another mapped asset");
            ExecutionEngine.Assert(BytesEqual(
                Slice(existingRecord, RecordMessageOffset, existingRecord.Length - RecordMessageOffset),
                messageBytes), "message hash queue record mismatch");
            return true;
        }

        var lastSequence = GetLastSequence();
        ExecutionEngine.Assert(lastSequence < ulong.MaxValue, "payout queue sequence overflow");
        var sequence = lastSequence + 1;
        var record = new byte[RecordMessageOffset + messageBytes.Length];
        record[0] = StatusEnqueued;
        WriteUInt160(record, RecordNeoAssetOffset, neoAsset);
        WriteUInt256(record, RecordMessageHashOffset, messageHash);
        WriteUInt256(record, RecordL2TransactionHashOffset, UInt256.Zero);
        Copy(messageBytes, 0, record, RecordMessageOffset, messageBytes.Length);

        Storage.Put(PayoutKey(sequence), record);
        Storage.Put(MessageHashKey(messageHash), (BigInteger)sequence);
        Storage.Put(new byte[] { KeyLastSequence }, (BigInteger)sequence);
        OnPayoutEnqueued(sequence, messageHash, externalChainId, neoChainId, nonce,
            foreignAsset, neoAsset, recipient, amount, deadlineUnixSeconds, sourceTxRef,
            messageBytes);
        return true;
    }

    /// <summary>
    /// Mark a queue entry acknowledged only after the configured relay has confirmed the exact
    /// message hash at the target native bridge.
    /// </summary>
    public static bool Acknowledge(
        ulong sequence,
        UInt256 messageHash,
        UInt256 l2TransactionHash)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetRelayAccount()), "not relay account");
        ExecutionEngine.Assert(messageHash.IsValid && !messageHash.IsZero, "invalid message hash");
        ExecutionEngine.Assert(l2TransactionHash.IsValid && !l2TransactionHash.IsZero,
            "invalid L2 transaction hash");
        var record = ReadRecord(sequence);
        ExecutionEngine.Assert(ReadUInt256(record, RecordMessageHashOffset) == messageHash,
            "acknowledgement message hash mismatch");
        if (record[0] == StatusAcknowledged)
        {
            ExecutionEngine.Assert(
                ReadUInt256(record, RecordL2TransactionHashOffset) == l2TransactionHash,
                "payout already acknowledged by another L2 transaction");
            return true;
        }
        ExecutionEngine.Assert(record[0] == StatusEnqueued, "invalid payout queue state");
        record[0] = StatusAcknowledged;
        WriteUInt256(record, RecordL2TransactionHashOffset, l2TransactionHash);
        Storage.Put(PayoutKey(sequence), record);
        OnPayoutAcknowledged(sequence, messageHash, l2TransactionHash);
        return true;
    }

    /// <summary>Queue status: 0 missing, 1 enqueued, 2 acknowledged.</summary>
    [Safe]
    public static byte GetPayoutStatus(ulong sequence)
    {
        var raw = Storage.Get(PayoutKey(sequence));
        return raw == null ? (byte)0 : ((byte[])raw)[0];
    }

    /// <summary>Exact canonical signed message bytes for a queue entry.</summary>
    [Safe]
    public static byte[] GetPayoutMessage(ulong sequence)
    {
        var record = ReadRecord(sequence);
        return Slice(record, RecordMessageOffset, record.Length - RecordMessageOffset);
    }

    /// <summary>Canonical <c>Hash256</c> of the signed message bytes.</summary>
    [Safe]
    public static UInt256 GetPayoutMessageHash(ulong sequence) =>
        ReadUInt256(ReadRecord(sequence), RecordMessageHashOffset);

    /// <summary>Mapped Neo L2 asset bound by the escrow route.</summary>
    [Safe]
    public static UInt160 GetPayoutNeoAsset(ulong sequence) =>
        ReadUInt160(ReadRecord(sequence), RecordNeoAssetOffset);

    /// <summary>Confirmed target-L2 transaction hash, or zero while pending.</summary>
    [Safe]
    public static UInt256 GetPayoutL2TransactionHash(ulong sequence) =>
        ReadUInt256(ReadRecord(sequence), RecordL2TransactionHashOffset);

    /// <summary>Queue sequence for an exact message hash, or zero when absent.</summary>
    [Safe]
    public static ulong GetSequenceForMessageHash(UInt256 messageHash)
    {
        var raw = Storage.Get(MessageHashKey(messageHash));
        return raw == null ? 0 : (ulong)(BigInteger)raw;
    }

    private static UInt256 ValidateCanonicalMessage(
        uint externalChainId,
        uint neoChainId,
        ulong nonce,
        UInt160 foreignAsset,
        UInt160 recipient,
        BigInteger amount,
        ulong deadlineUnixSeconds,
        UInt256 sourceTxRef,
        byte[] messageBytes)
    {
        ExecutionEngine.Assert((externalChainId & 0xFF000000U) == 0xE0000000U,
            "invalid external chain id");
        ExecutionEngine.Assert(neoChainId == GetNeoChainId(), "wrong target Neo L2 chain");
        ExecutionEngine.Assert(foreignAsset.IsValid && !foreignAsset.IsZero,
            "invalid foreign asset");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(sourceTxRef.IsValid && !sourceTxRef.IsZero,
            "invalid source transaction");
        ExecutionEngine.Assert(messageBytes.Length >= FixedMessagePrefixSize + 25,
            "canonical message too short");
        ExecutionEngine.Assert(ReadUInt32(messageBytes, 0) == externalChainId,
            "signed external chain mismatch");
        ExecutionEngine.Assert(ReadUInt32(messageBytes, 4) == neoChainId,
            "signed Neo chain mismatch");
        ExecutionEngine.Assert(ReadUInt64(messageBytes, 8) == nonce, "signed nonce mismatch");
        ExecutionEngine.Assert(messageBytes[16] == DirectionForeignToNeo,
            "signed direction must be foreign-to-Neo");
        ExecutionEngine.Assert(ReadUInt160(messageBytes, 37) == recipient,
            "signed recipient mismatch");
        ExecutionEngine.Assert(ReadUInt64(messageBytes, 57) == deadlineUnixSeconds,
            "signed deadline mismatch");
        ExecutionEngine.Assert(ReadUInt256(messageBytes, 65) == sourceTxRef,
            "signed source transaction mismatch");
        ExecutionEngine.Assert(messageBytes[97] == MessageTypeAssetTransfer,
            "adapter supports asset-transfer messages only");
        var payloadLength = ReadUInt32(messageBytes, 98);
        ExecutionEngine.Assert(payloadLength <= MaxPayloadLength,
            "canonical message payload too large");
        ExecutionEngine.Assert(messageBytes.Length == FixedMessagePrefixSize + (int)payloadLength,
            "canonical message length mismatch");
        ExecutionEngine.Assert(ReadUInt160(messageBytes, 102) == foreignAsset,
            "signed foreign asset mismatch");
        var amountLength = ReadUInt32(messageBytes, 122);
        ExecutionEngine.Assert(amountLength > 0 && amountLength <= MaxAmountBytes,
            "signed amount length invalid");
        ExecutionEngine.Assert(payloadLength == 24 + amountLength,
            "asset-transfer payload length mismatch");
        var amountOffset = 126;
        ExecutionEngine.Assert(messageBytes[amountOffset + (int)amountLength - 1] != 0,
            "signed amount is not minimally encoded");
        var signedAmount = BigInteger.Zero;
        for (var index = (int)amountLength - 1; index >= 0; index--)
            signedAmount = signedAmount * 256 + messageBytes[amountOffset + index];
        ExecutionEngine.Assert(signedAmount == amount, "signed amount mismatch");
        return Hash256(messageBytes);
    }

    private static UInt256 Hash256(byte[] bytes)
    {
        var first = CryptoLib.Sha256((ByteString)bytes);
        return (UInt256)(byte[])CryptoLib.Sha256(first);
    }

    private static byte[] ReadRecord(ulong sequence)
    {
        var raw = Storage.Get(PayoutKey(sequence));
        ExecutionEngine.Assert(raw != null, "payout queue entry not found");
        var record = (byte[])raw!;
        ExecutionEngine.Assert(record.Length >= RecordMessageOffset + FixedMessagePrefixSize,
            "payout queue record corrupt");
        ExecutionEngine.Assert(record[0] == StatusEnqueued || record[0] == StatusAcknowledged,
            "payout queue status corrupt");
        return record;
    }

    private static UInt160 ReadUInt160(byte key)
    {
        var raw = Storage.Get(new byte[] { key });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    private static BigInteger ReadInteger(byte key)
    {
        var raw = Storage.Get(new byte[] { key });
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    private static byte[] PayoutKey(ulong sequence)
    {
        var key = new byte[9];
        key[0] = PrefixPayout;
        WriteUInt64(key, 1, sequence);
        return key;
    }

    private static byte[] MessageHashKey(UInt256 messageHash)
    {
        var key = new byte[33];
        key[0] = PrefixSequenceByMessageHash;
        WriteUInt256(key, 1, messageHash);
        return key;
    }

    private static uint ReadUInt32(byte[] bytes, int offset) =>
        (uint)bytes[offset]
        | ((uint)bytes[offset + 1] << 8)
        | ((uint)bytes[offset + 2] << 16)
        | ((uint)bytes[offset + 3] << 24);

    private static ulong ReadUInt64(byte[] bytes, int offset) =>
        (ulong)bytes[offset]
        | ((ulong)bytes[offset + 1] << 8)
        | ((ulong)bytes[offset + 2] << 16)
        | ((ulong)bytes[offset + 3] << 24)
        | ((ulong)bytes[offset + 4] << 32)
        | ((ulong)bytes[offset + 5] << 40)
        | ((ulong)bytes[offset + 6] << 48)
        | ((ulong)bytes[offset + 7] << 56);

    private static UInt160 ReadUInt160(byte[] bytes, int offset)
    {
        var value = new byte[20];
        Copy(bytes, offset, value, 0, value.Length);
        return (UInt160)value;
    }

    private static UInt256 ReadUInt256(byte[] bytes, int offset)
    {
        var value = new byte[32];
        Copy(bytes, offset, value, 0, value.Length);
        return (UInt256)value;
    }

    private static void WriteUInt64(byte[] bytes, int offset, ulong value)
    {
        for (var index = 0; index < 8; index++)
            bytes[offset + index] = (byte)(value >> (index * 8));
    }

    private static void WriteUInt160(byte[] bytes, int offset, UInt160 value) =>
        Copy((byte[])value, 0, bytes, offset, 20);

    private static void WriteUInt256(byte[] bytes, int offset, UInt256 value) =>
        Copy((byte[])value, 0, bytes, offset, 32);

    private static byte[] Slice(byte[] source, int offset, int length)
    {
        var result = new byte[length];
        Copy(source, offset, result, 0, length);
        return result;
    }

    private static bool BytesEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        for (var index = 0; index < left.Length; index++)
            if (left[index] != right[index]) return false;
        return true;
    }

    private static void Copy(byte[] source, int sourceOffset, byte[] target, int targetOffset, int length)
    {
        for (var index = 0; index < length; index++)
            target[targetOffset + index] = source[sourceOffset + index];
    }
}
