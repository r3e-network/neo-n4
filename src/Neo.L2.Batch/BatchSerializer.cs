using System.Buffers.Binary;

namespace Neo.L2.Batch;

/// <summary>
/// Canonical, deterministic encoding for <see cref="L2BatchCommitment"/> and <see cref="PublicInputs"/>.
/// </summary>
/// <remarks>
/// All multi-byte integers are little-endian (matches Neo on-chain conventions). UInt160 / UInt256
/// are encoded as their 20- / 32-byte payloads. Proof bytes are length-prefixed (32-bit LE length).
/// <para>
/// This is the byte format that NeoHub's settlement contract reads, so any change here is a
/// breaking on-chain change.
/// </para>
/// <para>
/// <b>L2BatchCommitment layout (321 + proofLen bytes):</b>
/// <code>
/// offset  size       field
/// 0       4          chainId (uint32)
/// 4       8          batchNumber (uint64)
/// 12      8          firstBlock (uint64)
/// 20      8          lastBlock (uint64)
/// 28      32         preStateRoot
/// 60      32         postStateRoot
/// 92      32         txRoot
/// 124     32         receiptRoot
/// 156     32         withdrawalRoot
/// 188     32         l2ToL1MessageRoot
/// 220     32         l2ToL2MessageRoot
/// 252     32         daCommitment
/// 284     32         publicInputHash
/// 316     1          proofType (byte)
/// 317     4          proofLen (int32)
/// 321     proofLen   proof bytes
/// </code>
/// </para>
/// <para>
/// <b>PublicInputs layout (332 bytes, fixed):</b>
/// <code>
/// offset  size  field
/// 0       4     chainId (uint32)
/// 4       8     batchNumber (uint64)
/// 12      32    preStateRoot
/// 44      32    postStateRoot
/// 76      32    txRoot
/// 108     32    receiptRoot
/// 140     32    withdrawalRoot
/// 172     32    l2ToL1MessageRoot
/// 204     32    l2ToL2MessageRoot
/// 236     32    l1MessageHash
/// 268     32    daCommitment
/// 300     32    blockContextHash
/// </code>
/// </para>
/// </remarks>
public static class BatchSerializer
{
    private const int ProofMaxBytes = 1 * 1024 * 1024; // 1 MiB cap matches NeoHub's defensive limit.

    /// <summary>Fixed-size portion of an <see cref="L2BatchCommitment"/> encoding (excludes proof varbytes).</summary>
    public const int CommitmentFixedSize =
        4 +              // ChainId
        8 + 8 + 8 +      // BatchNumber, FirstBlock, LastBlock
        9 * 32 +         // 9× UInt256 roots/hashes
        1 +              // ProofType
        4;               // proof length prefix

    /// <summary>Total size of <see cref="PublicInputs"/> encoding (no varbytes).</summary>
    public const int PublicInputsSize =
        4 +              // ChainId
        8 +              // BatchNumber
        10 * 32;         // 10× UInt256 roots/hashes

    /// <summary>Encode <paramref name="commitment"/> to its canonical byte form.</summary>
    public static byte[] Encode(L2BatchCommitment commitment)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        if (commitment.Proof.Length > ProofMaxBytes)
            throw new ArgumentException($"Proof bytes exceed maximum {ProofMaxBytes}", nameof(commitment));

        // checked: a Proof.Length near int.MaxValue (caught later by ProofMaxBytes, but
        // not here at allocation time) wraps. Surface as OverflowException at the sum
        // site so the cause is visible.
        var bufferSize = checked(CommitmentFixedSize + commitment.Proof.Length);
        var buffer = new byte[bufferSize];
        var span = buffer.AsSpan();
        var pos = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), commitment.ChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), commitment.BatchNumber); pos += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), commitment.FirstBlock); pos += 8;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), commitment.LastBlock); pos += 8;

        WriteUInt256(span, ref pos, commitment.PreStateRoot);
        WriteUInt256(span, ref pos, commitment.PostStateRoot);
        WriteUInt256(span, ref pos, commitment.TxRoot);
        WriteUInt256(span, ref pos, commitment.ReceiptRoot);
        WriteUInt256(span, ref pos, commitment.WithdrawalRoot);
        WriteUInt256(span, ref pos, commitment.L2ToL1MessageRoot);
        WriteUInt256(span, ref pos, commitment.L2ToL2MessageRoot);
        WriteUInt256(span, ref pos, commitment.DACommitment);
        WriteUInt256(span, ref pos, commitment.PublicInputHash);

        span[pos++] = (byte)commitment.ProofType;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(pos, 4), commitment.Proof.Length); pos += 4;

        commitment.Proof.Span.CopyTo(span.Slice(pos));
        pos += commitment.Proof.Length;

        if (pos != buffer.Length)
            throw new InvalidOperationException($"BatchSerializer.Encode internal length mismatch: pos={pos}, buf={buffer.Length}");

        return buffer;
    }

    /// <summary>Decode a canonical byte form back to <see cref="L2BatchCommitment"/>.</summary>
    public static L2BatchCommitment Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < CommitmentFixedSize)
            throw new ArgumentException($"Buffer too small: {data.Length} < {CommitmentFixedSize}", nameof(data));

        var pos = 0;
        var chainId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos, 4)); pos += 4;
        var batchNumber = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(pos, 8)); pos += 8;
        var firstBlock = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(pos, 8)); pos += 8;
        var lastBlock = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(pos, 8)); pos += 8;

        var preStateRoot = ReadUInt256(data, ref pos);
        var postStateRoot = ReadUInt256(data, ref pos);
        var txRoot = ReadUInt256(data, ref pos);
        var receiptRoot = ReadUInt256(data, ref pos);
        var withdrawalRoot = ReadUInt256(data, ref pos);
        var l2ToL1MessageRoot = ReadUInt256(data, ref pos);
        var l2ToL2MessageRoot = ReadUInt256(data, ref pos);
        var daCommitment = ReadUInt256(data, ref pos);
        var publicInputHash = ReadUInt256(data, ref pos);

        // Validate ProofType byte is within the defined enum range. An untrusted decoder
        // input (corrupted L1 calldata, replayed older batch from a different version)
        // could carry an unknown discriminant — without this check it would propagate as
        // an undefined ProofType, and downstream `==` comparisons would silently treat it
        // as "not the expected one" rather than rejecting the batch outright.
        var proofTypeByte = data[pos++];
        if (proofTypeByte > (byte)ProofType.Zk)
            throw new InvalidDataException($"Unknown ProofType byte {proofTypeByte} (max {(byte)ProofType.Zk})");
        var proofType = (ProofType)proofTypeByte;

        var proofLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(pos, 4)); pos += 4;

        if (proofLen < 0 || proofLen > ProofMaxBytes)
            throw new InvalidDataException($"Invalid proof length {proofLen}");
        // Strict length match: trailing bytes after the proof would be silently ignored,
        // creating a malleability surface where the same logical commitment yields
        // different on-chain hashes if the L1 contract hashes the full calldata while
        // the L2 decoder strips trailing bytes. Same defensive pattern as
        // OptimisticProofPayload.Decode and DepositPayload.Decode.
        if (pos + proofLen != data.Length)
            throw new InvalidDataException(
                $"Buffer length mismatch: expected {pos + proofLen}, have {data.Length}");

        var proof = data.Slice(pos, proofLen).ToArray();

        return new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            PreStateRoot = preStateRoot,
            PostStateRoot = postStateRoot,
            TxRoot = txRoot,
            ReceiptRoot = receiptRoot,
            WithdrawalRoot = withdrawalRoot,
            L2ToL1MessageRoot = l2ToL1MessageRoot,
            L2ToL2MessageRoot = l2ToL2MessageRoot,
            DACommitment = daCommitment,
            PublicInputHash = publicInputHash,
            ProofType = proofType,
            Proof = proof,
        };
    }

    /// <summary>Encode <paramref name="inputs"/> to its canonical byte form.</summary>
    public static byte[] EncodePublicInputs(PublicInputs inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var buffer = new byte[PublicInputsSize];
        var span = buffer.AsSpan();
        var pos = 0;

        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(pos, 4), inputs.ChainId); pos += 4;
        BinaryPrimitives.WriteUInt64LittleEndian(span.Slice(pos, 8), inputs.BatchNumber); pos += 8;

        WriteUInt256(span, ref pos, inputs.PreStateRoot);
        WriteUInt256(span, ref pos, inputs.PostStateRoot);
        WriteUInt256(span, ref pos, inputs.TxRoot);
        WriteUInt256(span, ref pos, inputs.ReceiptRoot);
        WriteUInt256(span, ref pos, inputs.WithdrawalRoot);
        WriteUInt256(span, ref pos, inputs.L2ToL1MessageRoot);
        WriteUInt256(span, ref pos, inputs.L2ToL2MessageRoot);
        WriteUInt256(span, ref pos, inputs.L1MessageHash);
        WriteUInt256(span, ref pos, inputs.DACommitment);
        WriteUInt256(span, ref pos, inputs.BlockContextHash);

        if (pos != buffer.Length)
            throw new InvalidOperationException($"EncodePublicInputs internal length mismatch: pos={pos}, buf={buffer.Length}");

        return buffer;
    }

    /// <summary>Decode a canonical <see cref="PublicInputs"/> byte form.</summary>
    public static PublicInputs DecodePublicInputs(ReadOnlySpan<byte> data)
    {
        if (data.Length != PublicInputsSize)
            throw new ArgumentException($"Buffer size {data.Length} != expected {PublicInputsSize}", nameof(data));

        var pos = 0;
        var chainId = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(pos, 4)); pos += 4;
        var batchNumber = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(pos, 8)); pos += 8;

        var preStateRoot = ReadUInt256(data, ref pos);
        var postStateRoot = ReadUInt256(data, ref pos);
        var txRoot = ReadUInt256(data, ref pos);
        var receiptRoot = ReadUInt256(data, ref pos);
        var withdrawalRoot = ReadUInt256(data, ref pos);
        var l2ToL1MessageRoot = ReadUInt256(data, ref pos);
        var l2ToL2MessageRoot = ReadUInt256(data, ref pos);
        var l1MessageHash = ReadUInt256(data, ref pos);
        var daCommitment = ReadUInt256(data, ref pos);
        var blockContextHash = ReadUInt256(data, ref pos);

        return new PublicInputs
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            PreStateRoot = preStateRoot,
            PostStateRoot = postStateRoot,
            TxRoot = txRoot,
            ReceiptRoot = receiptRoot,
            WithdrawalRoot = withdrawalRoot,
            L2ToL1MessageRoot = l2ToL1MessageRoot,
            L2ToL2MessageRoot = l2ToL2MessageRoot,
            L1MessageHash = l1MessageHash,
            DACommitment = daCommitment,
            BlockContextHash = blockContextHash,
        };
    }

    private static void WriteUInt256(Span<byte> span, ref int pos, UInt256 value)
    {
        value.GetSpan().CopyTo(span.Slice(pos, 32));
        pos += 32;
    }

    private static UInt256 ReadUInt256(ReadOnlySpan<byte> span, ref int pos)
    {
        var slice = span.Slice(pos, 32);
        pos += 32;
        return new UInt256(slice);
    }
}
