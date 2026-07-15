using System.Buffers.Binary;
using System.Numerics;
using Neo.L2.State;

namespace Neo.L2.Bridge;

/// <summary>
/// On-L1 storage record written by <c>NeoHub.SharedBridge.EncodeDeposit</c> and returned by
/// <c>GetDeposit(chainId, nonce)</c>.
/// </summary>
/// <remarks>
/// Wire layout (little-endian multi-byte fields):
/// <c>asset(20) || recipient(20) || sender(20) || nonce(8) || amountLen(4) || amount(unsigned LE)</c>.
/// This is the L1 escrow audit record. The L2 mint path consumes the narrower
/// <see cref="DepositPayload"/> embedded inside a <see cref="CrossChainMessage"/>.
/// See doc.md §9.1 / §15.2.
/// </remarks>
public sealed record SharedBridgeDepositRecord
{
    /// <summary>Maximum amount encoding size, matching <see cref="DepositPayload.MaxAmountBytes"/>.</summary>
    public const int MaxAmountBytes = DepositPayload.MaxAmountBytes;

    /// <summary>Fixed prefix before the variable-length amount: 20+20+20+8+4.</summary>
    public const int FixedHeaderSize = 20 + 20 + 20 + 8 + 4;

    /// <summary>L1 NEP-17 asset locked in SharedBridge.</summary>
    public required UInt160 Asset { get; init; }

    /// <summary>L2 recipient that should receive the bridged representation.</summary>
    public required UInt160 Recipient { get; init; }

    /// <summary>L1 depositor (SharedBridge caller / transfer source).</summary>
    public required UInt160 Sender { get; init; }

    /// <summary>Per-target-chain SharedBridge deposit nonce.</summary>
    public required ulong Nonce { get; init; }

    /// <summary>Locked amount in L1 smallest units (unsigned).</summary>
    public required BigInteger Amount { get; init; }

    /// <summary>Encode the canonical L1 storage record.</summary>
    public byte[] Encode()
    {
        ArgumentNullException.ThrowIfNull(Asset);
        ArgumentNullException.ThrowIfNull(Recipient);
        ArgumentNullException.ThrowIfNull(Sender);
        if (Amount <= BigInteger.Zero)
            throw new InvalidOperationException("deposit amount must be positive");
        var amountBytes = Amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        if (amountBytes.Length > MaxAmountBytes)
            throw new InvalidOperationException($"deposit amount exceeds {MaxAmountBytes} bytes");

        var buffer = new byte[FixedHeaderSize + amountBytes.Length];
        var span = buffer.AsSpan();
        var pos = 0;
        Asset.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        Recipient.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        Sender.GetSpan().CopyTo(span.Slice(pos, 20)); pos += 20;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), Nonce); pos += 8;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), amountBytes.Length); pos += 4;
        amountBytes.CopyTo(span.Slice(pos));
        return buffer;
    }

    /// <summary>Decode a SharedBridge <c>GetDeposit</c> record.</summary>
    public static SharedBridgeDepositRecord Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedHeaderSize)
            throw new ArgumentException(
                $"SharedBridge deposit record too small ({bytes.Length} < {FixedHeaderSize})",
                nameof(bytes));

        var pos = 0;
        var asset = new UInt160(bytes.Slice(pos, 20)); pos += 20;
        var recipient = new UInt160(bytes.Slice(pos, 20)); pos += 20;
        var sender = new UInt160(bytes.Slice(pos, 20)); pos += 20;
        var nonce = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(pos, 8)); pos += 8;
        var amountLen = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(pos, 4)); pos += 4;
        if (amountLen <= 0 || amountLen > MaxAmountBytes || pos + amountLen != bytes.Length)
            throw new InvalidDataException(
                $"invalid SharedBridge deposit amount length {amountLen} (have {bytes.Length}, fixed {FixedHeaderSize})");
        var amount = new BigInteger(bytes.Slice(pos, amountLen), isUnsigned: true, isBigEndian: false);
        if (amount <= BigInteger.Zero)
            throw new InvalidDataException("SharedBridge deposit amount must be positive");
        if (asset.Equals(UInt160.Zero) || recipient.Equals(UInt160.Zero) || sender.Equals(UInt160.Zero))
            throw new InvalidDataException("SharedBridge deposit asset/recipient/sender must be non-zero");

        return new SharedBridgeDepositRecord
        {
            Asset = asset,
            Recipient = recipient,
            Sender = sender,
            Nonce = nonce,
            Amount = amount,
        };
    }

    /// <summary>Project the L1 audit record into the canonical L2 mint payload.</summary>
    public DepositPayload ToDepositPayload() => new()
    {
        L1Asset = Asset,
        L2Recipient = Recipient,
        Amount = Amount,
    };

    /// <summary>
    /// Build the L1→L2 <see cref="CrossChainMessage"/> the batcher must include so
    /// <c>CanonicalNativeExecutionAdapter</c> / <see cref="DepositProcessor"/> can mint.
    /// </summary>
    /// <param name="targetChainId">Destination L2 chain id.</param>
    /// <param name="l2BridgeHash">
    /// Native L2 bridge script hash used as the message receiver. N4 genesis V1 routes
    /// deposits exclusively through this native contract.
    /// </param>
    public CrossChainMessage ToCrossChainMessage(uint targetChainId, UInt160 l2BridgeHash)
    {
        ArgumentNullException.ThrowIfNull(l2BridgeHash);
        if (targetChainId == 0)
            throw new ArgumentOutOfRangeException(nameof(targetChainId), "target chain id 0 is reserved for L1");
        if (l2BridgeHash.Equals(UInt160.Zero))
            throw new ArgumentException("L2 bridge hash must be non-zero", nameof(l2BridgeHash));

        var payload = ToDepositPayload().Encode();
        var unhashed = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = targetChainId,
            Nonce = Nonce,
            Sender = Sender,
            Receiver = l2BridgeHash,
            MessageType = MessageType.Deposit,
            Payload = payload,
            MessageHash = UInt256.Zero,
        };
        return unhashed with { MessageHash = MessageHasher.HashMessage(unhashed) };
    }
}
