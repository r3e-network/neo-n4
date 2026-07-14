using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using Neo.Cryptography;
using Neo.L2.State;

namespace Neo.L2.Batch;

/// <summary>Canonical serializer for <see cref="ExecutionPayloadV1"/>.</summary>
/// <remarks>
/// See doc.md §7.2, §7.4, and §8. All integers and lengths are little-endian; every
/// variable-length section uses a 32-bit unsigned length.
/// </remarks>
public static class ExecutionPayloadSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4EXEC"u8;

    private const ushort Flags = 0;
    private const int FixedSize = 140;
    private const int MessageFixedSize = 61;

    /// <summary>Minimum canonical payload size with zero messages and transactions.</summary>
    public const int MinimumEncodedBytes = FixedSize;

    /// <summary>Maximum accepted encoded execution payload size (64 MiB).</summary>
    public const int MaxEncodedBytes = 64 * 1024 * 1024;

    /// <summary>Maximum accepted transaction size (16 MiB).</summary>
    public const int MaxTransactionBytes = 16 * 1024 * 1024;

    /// <summary>Maximum accepted L1-message payload size (4 MiB).</summary>
    public const int MaxMessagePayloadBytes = 4 * 1024 * 1024;

    /// <summary>Maximum number of messages or transactions in one payload.</summary>
    public const uint MaxItemCount = 1_000_000;

    /// <summary>Encode an execution payload to canonical DA bytes.</summary>
    public static byte[] Encode(ExecutionPayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        Validate(payload);

        var size = CalculateSize(payload);
        var writer = new CanonicalWireWriter(size);
        writer.WriteBytes(Magic);
        writer.WriteUInt16(ExecutionPayloadV1.Version);
        writer.WriteUInt16(Flags);
        writer.WriteUInt32(payload.ChainId);
        writer.WriteUInt64(payload.BatchNumber);
        writer.WriteUInt64(payload.FirstBlock);
        writer.WriteUInt64(payload.LastBlock);
        writer.WriteUInt256(payload.PreStateRoot);
        WriteBlockContext(writer, payload.BlockContext);

        writer.WriteUInt32(checked((uint)payload.L1Messages.Count));
        foreach (var message in payload.L1Messages)
        {
            var encodedLength = checked(MessageFixedSize + message.Payload.Length);
            writer.WriteUInt32(checked((uint)encodedLength));
            writer.WriteUInt32(message.SourceChainId);
            writer.WriteUInt32(message.TargetChainId);
            writer.WriteUInt64(message.Nonce);
            writer.WriteUInt160(message.Sender);
            writer.WriteUInt160(message.Receiver);
            writer.WriteByte((byte)message.MessageType);
            writer.WriteLengthPrefixedBytes(message.Payload.Span);
        }

        writer.WriteUInt32(checked((uint)payload.ForcedInclusions.Count));
        foreach (var proof in payload.ForcedInclusions)
        {
            writer.WriteUInt64(proof.Nonce);
            writer.WriteUInt32(proof.LeafIndex);
            writer.WriteUInt256(proof.TxHash);
            writer.WriteUInt32(checked((uint)proof.Siblings.Count));
            foreach (var sibling in proof.Siblings)
                writer.WriteUInt256(sibling);
        }

        writer.WriteUInt32(checked((uint)payload.Transactions.Count));
        foreach (var transaction in payload.Transactions)
            writer.WriteLengthPrefixedBytes(transaction.Span);

        if (writer.WrittenCount != size)
            throw new InvalidOperationException(
                $"Execution payload length mismatch: wrote {writer.WrittenCount}, expected {size}");
        return writer.ToArray();
    }

    /// <summary>Decode canonical DA bytes, rejecting non-canonical or malformed input.</summary>
    public static ExecutionPayloadV1 Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < FixedSize)
            throw new InvalidDataException($"Execution payload is truncated: {data.Length} < {FixedSize}");
        if (data.Length > MaxEncodedBytes)
            throw new InvalidDataException(
                $"Execution payload exceeds {MaxEncodedBytes} bytes: {data.Length}");

        var reader = new StrictWireReader(data);
        reader.RequireMagic(Magic, "execution payload");
        var version = reader.ReadUInt16("version");
        if (version != ExecutionPayloadV1.Version)
            throw new InvalidDataException($"Unsupported execution payload version {version}");
        var flags = reader.ReadUInt16("flags");
        if (flags != Flags)
            throw new InvalidDataException($"Unknown execution payload flags 0x{flags:x4}");

        var chainId = reader.ReadUInt32("chainId");
        var batchNumber = reader.ReadUInt64("batchNumber");
        var firstBlock = reader.ReadUInt64("firstBlock");
        var lastBlock = reader.ReadUInt64("lastBlock");
        var preStateRoot = reader.ReadUInt256("preStateRoot");
        var blockContext = ReadBlockContext(ref reader);

        var messageCount = reader.ReadBoundedCount(MaxItemCount, "L1 message count");
        if (messageCount > (uint)(reader.Remaining / (4 + MessageFixedSize)))
            throw new InvalidDataException(
                $"L1 message count {messageCount} exceeds the remaining payload capacity");
        var messages = new List<CrossChainMessage>(checked((int)messageCount));
        for (var i = 0U; i < messageCount; i++)
        {
            var messageLength = reader.ReadUInt32($"L1 message {i} length");
            if (messageLength < MessageFixedSize
                || messageLength > MessageFixedSize + MaxMessagePayloadBytes)
                throw new InvalidDataException($"Invalid L1 message {i} length {messageLength}");
            var messageReader = reader.ReadSubReader(messageLength, $"L1 message {i}");
            var sourceChainId = messageReader.ReadUInt32("sourceChainId");
            var targetChainId = messageReader.ReadUInt32("targetChainId");
            var nonce = messageReader.ReadUInt64("nonce");
            var sender = messageReader.ReadUInt160("sender");
            var receiver = messageReader.ReadUInt160("receiver");
            var messageTypeByte = messageReader.ReadByte("messageType");
            if (!Enum.IsDefined((MessageType)messageTypeByte))
                throw new InvalidDataException($"Unknown MessageType byte {messageTypeByte}");
            var messagePayload = messageReader.ReadLengthPrefixedBytes(
                MaxMessagePayloadBytes,
                "message payload");
            messageReader.EnsureEnd($"L1 message {i}");

            if (sourceChainId != 0)
                throw new InvalidDataException(
                    $"L1 message {i} has non-L1 source chain {sourceChainId}");
            if (targetChainId != chainId)
                throw new InvalidDataException(
                    $"L1 message {i} targets chain {targetChainId}, expected {chainId}");

            var messageWithoutHash = new CrossChainMessage
            {
                SourceChainId = sourceChainId,
                TargetChainId = targetChainId,
                Nonce = nonce,
                Sender = sender,
                Receiver = receiver,
                MessageType = (MessageType)messageTypeByte,
                Payload = messagePayload,
                MessageHash = UInt256.Zero,
            };
            messages.Add(messageWithoutHash with
            {
                MessageHash = MessageHasher.HashMessage(messageWithoutHash),
            });
        }

        var forcedNonceCount = reader.ReadBoundedCount(
            MaxItemCount,
            "forced-inclusion nonce count");
        if (forcedNonceCount > (uint)(reader.Remaining / 48))
            throw new InvalidDataException(
                $"Forced-inclusion nonce count {forcedNonceCount} exceeds the remaining payload capacity");
        var forcedInclusions = new List<ForcedInclusionConsumptionProof>(
            checked((int)forcedNonceCount));
        for (var index = 0U; index < forcedNonceCount; index++)
        {
            var nonce = reader.ReadUInt64($"forced-inclusion nonce {index}");
            var leafIndex = reader.ReadUInt32($"forced-inclusion leaf index {index}");
            var txHash = reader.ReadUInt256($"forced-inclusion tx hash {index}");
            var siblingCount = reader.ReadBoundedCount(64, $"forced-inclusion sibling count {index}");
            var siblings = new UInt256[siblingCount];
            for (var siblingIndex = 0U; siblingIndex < siblingCount; siblingIndex++)
                siblings[siblingIndex] = reader.ReadUInt256(
                    $"forced-inclusion sibling {index}:{siblingIndex}");
            forcedInclusions.Add(new ForcedInclusionConsumptionProof
            {
                Nonce = nonce,
                LeafIndex = leafIndex,
                TxHash = txHash,
                Siblings = Array.AsReadOnly(siblings),
            });
        }

        var transactionCount = reader.ReadBoundedCount(MaxItemCount, "transaction count");
        if (transactionCount > (uint)(reader.Remaining / 4))
            throw new InvalidDataException(
                $"Transaction count {transactionCount} exceeds the remaining payload capacity");
        var transactions = new List<ReadOnlyMemory<byte>>(checked((int)transactionCount));
        for (var i = 0U; i < transactionCount; i++)
            transactions.Add(reader.ReadLengthPrefixedBytes(
                MaxTransactionBytes,
                $"transaction {i}"));
        reader.EnsureEnd("execution payload");

        var payload = new ExecutionPayloadV1
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            PreStateRoot = preStateRoot,
            BlockContext = blockContext,
            L1Messages = messages,
            ForcedInclusions = forcedInclusions,
            Transactions = transactions,
        };
        try
        {
            Validate(payload);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Execution payload fields are inconsistent", ex);
        }
        return payload;
    }

    /// <summary>Compute the DA commitment for an execution payload.</summary>
    public static UInt256 ComputeCommitment(ExecutionPayloadV1 payload)
        => ComputeCommitment(Encode(payload));

    /// <summary>Compute the DA commitment for already encoded execution-payload bytes.</summary>
    public static UInt256 ComputeCommitment(ReadOnlySpan<byte> encodedPayload)
    {
        if (encodedPayload.Length > MaxEncodedBytes)
            throw new ArgumentOutOfRangeException(
                nameof(encodedPayload),
                $"Execution payload exceeds {MaxEncodedBytes} bytes");
        return new UInt256(Crypto.Hash256(encodedPayload));
    }

    private static void Validate(ExecutionPayloadV1 payload)
    {
        ArgumentNullException.ThrowIfNull(payload.PreStateRoot);
        ArgumentNullException.ThrowIfNull(payload.BlockContext);
        ArgumentNullException.ThrowIfNull(payload.BlockContext.SequencerCommitteeHash);
        ArgumentNullException.ThrowIfNull(payload.L1Messages);
        ArgumentNullException.ThrowIfNull(payload.ForcedInclusions);
        ArgumentNullException.ThrowIfNull(payload.Transactions);
        if (payload.LastBlock < payload.FirstBlock)
            throw new ArgumentException(
                $"LastBlock {payload.LastBlock} precedes FirstBlock {payload.FirstBlock}",
                nameof(payload));
        if (payload.BlockContext.LastBlockTimestamp < payload.BlockContext.FirstBlockTimestamp)
            throw new ArgumentException(
                "BlockContext.LastBlockTimestamp precedes FirstBlockTimestamp",
                nameof(payload));
        if ((uint)payload.L1Messages.Count > MaxItemCount)
            throw new ArgumentException($"L1 message count exceeds {MaxItemCount}", nameof(payload));
        if ((uint)payload.Transactions.Count > MaxItemCount)
            throw new ArgumentException($"Transaction count exceeds {MaxItemCount}", nameof(payload));
        if ((uint)payload.ForcedInclusions.Count > MaxItemCount)
            throw new ArgumentException(
                $"Forced-inclusion nonce count exceeds {MaxItemCount}", nameof(payload));
        if (payload.ForcedInclusions.Count > payload.Transactions.Count)
            throw new ArgumentException(
                "Forced-inclusion nonce count exceeds transaction count", nameof(payload));
        for (var index = 0; index < payload.ForcedInclusions.Count; index++)
        {
            if (payload.ForcedInclusions[index] is null)
                throw new ArgumentException(
                    $"ForcedInclusions[{index}] is null", nameof(payload));
        }
        if (payload.ForcedInclusions.Select(static proof => proof.Nonce).Distinct().Count()
            != payload.ForcedInclusions.Count)
            throw new ArgumentException(
                "Forced-inclusion nonces must be unique", nameof(payload));

        for (var i = 0; i < payload.L1Messages.Count; i++)
        {
            var message = payload.L1Messages[i]
                ?? throw new ArgumentException($"L1Messages[{i}] is null", nameof(payload));
            ArgumentNullException.ThrowIfNull(message.Sender);
            ArgumentNullException.ThrowIfNull(message.Receiver);
            ArgumentNullException.ThrowIfNull(message.MessageHash);
            if (message.SourceChainId != 0)
                throw new ArgumentException(
                    $"L1Messages[{i}] source chain must be 0", nameof(payload));
            if (message.TargetChainId != payload.ChainId)
                throw new ArgumentException(
                    $"L1Messages[{i}] targets chain {message.TargetChainId}, expected {payload.ChainId}",
                    nameof(payload));
            if (!Enum.IsDefined(message.MessageType))
                throw new ArgumentException(
                    $"L1Messages[{i}] has unknown MessageType {(byte)message.MessageType}",
                    nameof(payload));
            if (message.Payload.Length > MaxMessagePayloadBytes)
                throw new ArgumentException(
                    $"L1Messages[{i}] payload exceeds {MaxMessagePayloadBytes} bytes",
                    nameof(payload));
            var expectedHash = MessageHasher.HashMessage(message);
            if (!expectedHash.Equals(message.MessageHash))
                throw new ArgumentException(
                    $"L1Messages[{i}] MessageHash does not match its canonical fields",
                    nameof(payload));
        }

        for (var i = 0; i < payload.Transactions.Count; i++)
        {
            if (payload.Transactions[i].Length > MaxTransactionBytes)
                throw new ArgumentException(
                    $"Transactions[{i}] exceeds {MaxTransactionBytes} bytes",
                    nameof(payload));
        }
        _ = new SealedBatch(
            payload.ChainId,
            payload.BatchNumber,
            payload.FirstBlock,
            payload.LastBlock,
            payload.PreStateRoot,
            payload.Transactions,
            payload.L1Messages,
            payload.BlockContext,
            payload.ForcedInclusions);
    }

    private static int CalculateSize(ExecutionPayloadV1 payload)
    {
        long size = FixedSize;
        foreach (var message in payload.L1Messages)
            size = checked(size + 4L + MessageFixedSize + message.Payload.Length);
        foreach (var proof in payload.ForcedInclusions)
            size = checked(size + 8L + 4L + 32L + 4L + 32L * proof.Siblings.Count);
        foreach (var transaction in payload.Transactions)
            size = checked(size + 4L + transaction.Length);
        if (size > MaxEncodedBytes)
            throw new ArgumentException(
                $"Encoded execution payload exceeds {MaxEncodedBytes} bytes",
                nameof(payload));
        return checked((int)size);
    }

    private static void WriteBlockContext(CanonicalWireWriter writer, BatchBlockContext context)
    {
        writer.WriteUInt32(context.L1FinalizedHeight);
        writer.WriteUInt64(context.FirstBlockTimestamp);
        writer.WriteUInt64(context.LastBlockTimestamp);
        writer.WriteUInt256(context.SequencerCommitteeHash);
        writer.WriteUInt32(context.Network);
    }

    private static BatchBlockContext ReadBlockContext(ref StrictWireReader reader)
        => new()
        {
            L1FinalizedHeight = reader.ReadUInt32("L1FinalizedHeight"),
            FirstBlockTimestamp = reader.ReadUInt64("FirstBlockTimestamp"),
            LastBlockTimestamp = reader.ReadUInt64("LastBlockTimestamp"),
            SequencerCommitteeHash = reader.ReadUInt256("SequencerCommitteeHash"),
            Network = reader.ReadUInt32("Network"),
        };
}

/// <summary>Canonical serializer for <see cref="ProofWitnessArtifactV1"/>.</summary>
/// <remarks>
/// See doc.md §7.5 and §8. Unknown versions, flags, enum discriminants, overflows,
/// truncation, trailing bytes, cross-field mismatches, and content-hash tampering are rejected.
/// </remarks>
public static class ProofWitnessArtifactSerializer
{
    private static ReadOnlySpan<byte> Magic => "NEO4PWIT"u8;
    private static ReadOnlySpan<byte> ContentHashDomain => "neo-n4/proof-witness/v1\0"u8;

    private const ushort AuthenticatedWitnessFlag = 1;
    private const ushort KnownFlags = AuthenticatedWitnessFlag;
    private const int FixedHeaderSize = 108;
    private const int ExecutionResultSize = 200;
    private const int ContentHashSize = 32;
    private const int MinimumEncodedBytes =
        FixedHeaderSize
        + 4 + ExecutionPayloadSerializer.MinimumEncodedBytes
        + 4
        + ExecutionResultSize
        + 4
        + 1 + 1 + 2 + 32 + 4 + 4
        + BatchSerializer.PublicInputsSize
        + ContentHashSize;

    /// <summary>Maximum accepted complete artifact size (256 MiB).</summary>
    public const int MaxEncodedBytes = 256 * 1024 * 1024;

    /// <summary>Maximum state-witness section size (128 MiB).</summary>
    public const int MaxStateWitnessBytes = 128 * 1024 * 1024;

    /// <summary>Maximum canonical effects section size (64 MiB).</summary>
    public const int MaxEffectsBytes = 64 * 1024 * 1024;

    /// <summary>Maximum DA pointer size (1 MiB).</summary>
    public const int MaxDaPointerBytes = 1024 * 1024;

    /// <summary>Maximum DA receipt evidence size (16 MiB).</summary>
    public const int MaxDaEvidenceBytes = 16 * 1024 * 1024;

    /// <summary>Encode a canonical artifact including its verified content hash.</summary>
    public static byte[] Encode(ProofWitnessArtifactV1 artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        var body = EncodeWithoutContentHash(artifact);
        var expectedHash = ComputeContentHash(body);
        if (!expectedHash.Equals(artifact.ContentHash))
            throw new InvalidOperationException(
                "Proof witness ContentHash does not match the canonical artifact bytes");

        var encoded = new byte[checked(body.Length + ContentHashSize)];
        body.CopyTo(encoded, 0);
        artifact.ContentHash.GetSpan().CopyTo(encoded.AsSpan(body.Length));
        return encoded;
    }

    /// <summary>Decode and fully validate a canonical artifact.</summary>
    public static ProofWitnessArtifactV1 Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinimumEncodedBytes)
            throw new InvalidDataException($"Proof witness artifact is truncated: {data.Length}");
        if (data.Length > MaxEncodedBytes)
            throw new InvalidDataException(
                $"Proof witness artifact exceeds {MaxEncodedBytes} bytes: {data.Length}");

        var reader = new StrictWireReader(data);
        reader.RequireMagic(Magic, "proof witness artifact");
        var version = reader.ReadUInt16("version");
        if (version != ProofWitnessArtifactV1.Version)
            throw new InvalidDataException($"Unsupported proof witness version {version}");
        var flags = reader.ReadUInt16("flags");
        if ((flags & ~KnownFlags) != 0)
            throw new InvalidDataException($"Unknown proof witness flags 0x{flags:x4}");

        var proofTypeByte = reader.ReadByte("proofType");
        if (!Enum.IsDefined((ProofType)proofTypeByte))
            throw new InvalidDataException($"Unknown proof type byte {proofTypeByte}");
        var proofSystemByte = reader.ReadByte("proofSystem");
        if (!Enum.IsDefined((WitnessProofSystem)proofSystemByte))
            throw new InvalidDataException($"Unknown proof system byte {proofSystemByte}");
        var reserved = reader.ReadBytes(2, "reserved bytes");
        if (reserved[0] != 0 || reserved[1] != 0)
            throw new InvalidDataException("Proof witness reserved bytes must be zero");

        var verificationKeyId = reader.ReadUInt256("verificationKeyId");
        var executionSemanticId = reader.ReadUInt256("executionSemanticId");
        var chainId = reader.ReadUInt32("chainId");
        var batchNumber = reader.ReadUInt64("batchNumber");
        var firstBlock = reader.ReadUInt64("firstBlock");
        var lastBlock = reader.ReadUInt64("lastBlock");
        var payloadBytes = reader.ReadLengthPrefixedBytes(
            ExecutionPayloadSerializer.MaxEncodedBytes,
            "execution payload");
        var executionPayload = ExecutionPayloadSerializer.Decode(payloadBytes);
        var stateWitness = reader.ReadLengthPrefixedBytes(
            MaxStateWitnessBytes,
            "state witness");

        var executionResult = new BatchExecutionResult
        {
            PostStateRoot = reader.ReadUInt256("postStateRoot"),
            TxRoot = reader.ReadUInt256("txRoot"),
            ReceiptRoot = reader.ReadUInt256("receiptRoot"),
            WithdrawalRoot = reader.ReadUInt256("withdrawalRoot"),
            L2ToL1MessageRoot = reader.ReadUInt256("l2ToL1MessageRoot"),
            L2ToL2MessageRoot = reader.ReadUInt256("l2ToL2MessageRoot"),
            GasConsumed = reader.ReadInt64("gasConsumed"),
        };
        var effects = reader.ReadLengthPrefixedBytes(MaxEffectsBytes, "effects");
        var daModeByte = reader.ReadByte("daMode");
        if (!Enum.IsDefined((DAMode)daModeByte))
            throw new InvalidDataException($"Unknown DA mode byte {daModeByte}");
        var daKindByte = reader.ReadByte("daReceiptKind");
        if (!Enum.IsDefined((DAReceiptKind)daKindByte))
            throw new InvalidDataException($"Unknown DA receipt kind byte {daKindByte}");
        var daReserved = reader.ReadBytes(2, "DA reserved bytes");
        if (daReserved[0] != 0 || daReserved[1] != 0)
            throw new InvalidDataException("DA receipt reserved bytes must be zero");
        var daReceipt = new DAReceipt
        {
            Layer = (DAMode)daModeByte,
            Kind = (DAReceiptKind)daKindByte,
            Commitment = reader.ReadUInt256("daCommitment"),
            Pointer = reader.ReadLengthPrefixedBytes(MaxDaPointerBytes, "DA pointer"),
            Evidence = reader.ReadLengthPrefixedBytes(MaxDaEvidenceBytes, "DA evidence"),
        };
        var publicInputsBytes = reader.ReadBytes(
            BatchSerializer.PublicInputsSize,
            "public inputs");
        var publicInputs = BatchSerializer.DecodePublicInputs(publicInputsBytes);
        var contentHash = reader.ReadUInt256("contentHash");
        reader.EnsureEnd("proof witness artifact");

        var expectedContentHash = ComputeContentHash(data[..^ContentHashSize]);
        if (!expectedContentHash.Equals(contentHash))
            throw new InvalidDataException("Proof witness content hash mismatch");

        var artifact = new ProofWitnessArtifactV1
        {
            ProofType = (ProofType)proofTypeByte,
            ProofSystem = (WitnessProofSystem)proofSystemByte,
            VerificationKeyId = verificationKeyId,
            ExecutionSemanticId = executionSemanticId,
            ExecutionWitnessAuthenticated = (flags & AuthenticatedWitnessFlag) != 0,
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = firstBlock,
            LastBlock = lastBlock,
            ExecutionPayload = executionPayload,
            StateWitness = stateWitness,
            ExecutionResult = executionResult,
            Effects = effects,
            DAReceipt = daReceipt,
            PublicInputs = publicInputs,
            ContentHash = contentHash,
        };
        try
        {
            Validate(artifact, payloadBytes);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException("Proof witness artifact fields are inconsistent", ex);
        }
        return artifact;
    }

    /// <summary>Compute the canonical content hash for an artifact model.</summary>
    public static UInt256 ComputeContentHash(ProofWitnessArtifactV1 artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        return ComputeContentHash(EncodeWithoutContentHash(artifact));
    }

    private static byte[] EncodeWithoutContentHash(ProofWitnessArtifactV1 artifact)
    {
        var payloadBytes = ExecutionPayloadSerializer.Encode(artifact.ExecutionPayload);
        Validate(artifact, payloadBytes);
        var publicInputsBytes = BatchSerializer.EncodePublicInputs(artifact.PublicInputs);

        var size = checked(
            FixedHeaderSize
            + 4 + payloadBytes.Length
            + 4 + artifact.StateWitness.Length
            + ExecutionResultSize
            + 4 + artifact.Effects.Length
            + 1 + 1 + 2 + 32
            + 4 + artifact.DAReceipt.Pointer.Length
            + 4 + artifact.DAReceipt.Evidence.Length
            + publicInputsBytes.Length);
        if (size + ContentHashSize > MaxEncodedBytes)
            throw new ArgumentException(
                $"Encoded proof witness exceeds {MaxEncodedBytes} bytes",
                nameof(artifact));

        var writer = new CanonicalWireWriter(size);
        writer.WriteBytes(Magic);
        writer.WriteUInt16(ProofWitnessArtifactV1.Version);
        writer.WriteUInt16(
            artifact.ExecutionWitnessAuthenticated ? AuthenticatedWitnessFlag : (ushort)0);
        writer.WriteByte((byte)artifact.ProofType);
        writer.WriteByte((byte)artifact.ProofSystem);
        writer.WriteBytes(stackalloc byte[2]);
        writer.WriteUInt256(artifact.VerificationKeyId);
        writer.WriteUInt256(artifact.ExecutionSemanticId);
        writer.WriteUInt32(artifact.ChainId);
        writer.WriteUInt64(artifact.BatchNumber);
        writer.WriteUInt64(artifact.FirstBlock);
        writer.WriteUInt64(artifact.LastBlock);
        writer.WriteLengthPrefixedBytes(payloadBytes);
        writer.WriteLengthPrefixedBytes(artifact.StateWitness.Span);
        writer.WriteUInt256(artifact.ExecutionResult.PostStateRoot);
        writer.WriteUInt256(artifact.ExecutionResult.TxRoot);
        writer.WriteUInt256(artifact.ExecutionResult.ReceiptRoot);
        writer.WriteUInt256(artifact.ExecutionResult.WithdrawalRoot);
        writer.WriteUInt256(artifact.ExecutionResult.L2ToL1MessageRoot);
        writer.WriteUInt256(artifact.ExecutionResult.L2ToL2MessageRoot);
        writer.WriteInt64(artifact.ExecutionResult.GasConsumed);
        writer.WriteLengthPrefixedBytes(artifact.Effects.Span);
        writer.WriteByte((byte)artifact.DAReceipt.Layer);
        writer.WriteByte((byte)artifact.DAReceipt.Kind);
        writer.WriteBytes(stackalloc byte[2]);
        writer.WriteUInt256(artifact.DAReceipt.Commitment);
        writer.WriteLengthPrefixedBytes(artifact.DAReceipt.Pointer.Span);
        writer.WriteLengthPrefixedBytes(artifact.DAReceipt.Evidence.Span);
        writer.WriteBytes(publicInputsBytes);

        if (writer.WrittenCount != size)
            throw new InvalidOperationException(
                $"Proof witness length mismatch: wrote {writer.WrittenCount}, expected {size}");
        return writer.ToArray();
    }

    private static UInt256 ComputeContentHash(ReadOnlySpan<byte> body)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        sha256.AppendData(ContentHashDomain);
        sha256.AppendData(body);
        var firstHash = sha256.GetHashAndReset();
        return new UInt256(SHA256.HashData(firstHash));
    }

    private static void Validate(ProofWitnessArtifactV1 artifact, ReadOnlySpan<byte> payloadBytes)
    {
        if (!Enum.IsDefined(artifact.ProofType) || artifact.ProofType == ProofType.None)
            throw new ArgumentException(
                $"Unsupported proof type byte {(byte)artifact.ProofType}", nameof(artifact));
        if (!Enum.IsDefined(artifact.ProofSystem))
            throw new ArgumentException(
                $"Unknown proof system byte {(byte)artifact.ProofSystem}", nameof(artifact));
        ArgumentNullException.ThrowIfNull(artifact.VerificationKeyId);
        ArgumentNullException.ThrowIfNull(artifact.ExecutionSemanticId);
        if (artifact.ExecutionSemanticId.Equals(UInt256.Zero))
            throw new ArgumentException("ExecutionSemanticId must be non-zero", nameof(artifact));
        if (artifact.ProofType == ProofType.Zk)
        {
            if (artifact.ProofSystem == WitnessProofSystem.None)
                throw new ArgumentException(
                    "ZK artifacts require a concrete proof system", nameof(artifact));
            if (artifact.VerificationKeyId.Equals(UInt256.Zero))
                throw new ArgumentException(
                    "ZK artifacts require a non-zero VerificationKeyId", nameof(artifact));
            if (!artifact.ExecutionWitnessAuthenticated || artifact.StateWitness.IsEmpty)
                throw new ArgumentException(
                    "ZK artifacts require a non-empty authenticated execution witness",
                    nameof(artifact));
        }
        else
        {
            if (artifact.ProofType is not (ProofType.Multisig or ProofType.Optimistic))
                throw new ArgumentException(
                    "Only explicit multisig/optimistic legacy profiles are supported",
                    nameof(artifact));
            if (artifact.ProofSystem != WitnessProofSystem.None
                || !artifact.VerificationKeyId.Equals(UInt256.Zero)
                || artifact.ExecutionWitnessAuthenticated)
                throw new ArgumentException(
                    "Legacy artifacts must use ProofSystem.None, zero VK, and unauthenticated witness",
                    nameof(artifact));
        }
        ArgumentNullException.ThrowIfNull(artifact.ExecutionPayload);
        ArgumentNullException.ThrowIfNull(artifact.ExecutionResult);
        ArgumentNullException.ThrowIfNull(artifact.DAReceipt);
        ArgumentNullException.ThrowIfNull(artifact.DAReceipt.Commitment);
        ArgumentNullException.ThrowIfNull(artifact.PublicInputs);
        ArgumentNullException.ThrowIfNull(artifact.ContentHash);
        ValidateExecutionResult(artifact.ExecutionResult);

        if (artifact.LastBlock < artifact.FirstBlock)
            throw new ArgumentException("LastBlock precedes FirstBlock", nameof(artifact));
        if (artifact.ChainId != artifact.ExecutionPayload.ChainId
            || artifact.BatchNumber != artifact.ExecutionPayload.BatchNumber
            || artifact.FirstBlock != artifact.ExecutionPayload.FirstBlock
            || artifact.LastBlock != artifact.ExecutionPayload.LastBlock)
            throw new ArgumentException(
                "Artifact identity does not match the embedded execution payload",
                nameof(artifact));
        if (artifact.StateWitness.Length > MaxStateWitnessBytes)
            throw new ArgumentException(
                $"StateWitness exceeds {MaxStateWitnessBytes} bytes", nameof(artifact));
        if (artifact.Effects.Length > MaxEffectsBytes)
            throw new ArgumentException(
                $"Effects exceeds {MaxEffectsBytes} bytes", nameof(artifact));
        if (artifact.DAReceipt.Pointer.Length > MaxDaPointerBytes)
            throw new ArgumentException(
                $"DA pointer exceeds {MaxDaPointerBytes} bytes", nameof(artifact));
        if (artifact.DAReceipt.Evidence.Length > MaxDaEvidenceBytes)
            throw new ArgumentException(
                $"DA evidence exceeds {MaxDaEvidenceBytes} bytes", nameof(artifact));
        if (!Enum.IsDefined(artifact.DAReceipt.Layer))
            throw new ArgumentException(
                $"Unknown DA mode byte {(byte)artifact.DAReceipt.Layer}", nameof(artifact));
        if (!Enum.IsDefined(artifact.DAReceipt.Kind)
            || artifact.DAReceipt.Kind == DAReceiptKind.Unspecified
            || artifact.DAReceipt.Pointer.IsEmpty
            || artifact.DAReceipt.Evidence.IsEmpty)
            throw new ArgumentException(
                "DA receipt must carry a declared kind, locator, and evidence",
                nameof(artifact));

        var expectedDaCommitment = ExecutionPayloadSerializer.ComputeCommitment(payloadBytes);
        if (!expectedDaCommitment.Equals(artifact.DAReceipt.Commitment))
            throw new ArgumentException(
                "DA commitment does not equal Hash256 of the encoded execution payload",
                nameof(artifact));

        var inputs = artifact.PublicInputs;
        ArgumentNullException.ThrowIfNull(inputs.PreStateRoot);
        ArgumentNullException.ThrowIfNull(inputs.PostStateRoot);
        ArgumentNullException.ThrowIfNull(inputs.TxRoot);
        ArgumentNullException.ThrowIfNull(inputs.ReceiptRoot);
        ArgumentNullException.ThrowIfNull(inputs.WithdrawalRoot);
        ArgumentNullException.ThrowIfNull(inputs.L2ToL1MessageRoot);
        ArgumentNullException.ThrowIfNull(inputs.L2ToL2MessageRoot);
        ArgumentNullException.ThrowIfNull(inputs.L1MessageHash);
        ArgumentNullException.ThrowIfNull(inputs.DACommitment);
        ArgumentNullException.ThrowIfNull(inputs.BlockContextHash);
        if (inputs.ChainId != artifact.ChainId || inputs.BatchNumber != artifact.BatchNumber)
            throw new ArgumentException(
                "PublicInputs identity does not match the artifact", nameof(artifact));
        if (!inputs.PreStateRoot.Equals(artifact.ExecutionPayload.PreStateRoot)
            || !inputs.PostStateRoot.Equals(artifact.ExecutionResult.PostStateRoot)
            || !inputs.TxRoot.Equals(artifact.ExecutionResult.TxRoot)
            || !inputs.ReceiptRoot.Equals(artifact.ExecutionResult.ReceiptRoot)
            || !inputs.WithdrawalRoot.Equals(artifact.ExecutionResult.WithdrawalRoot)
            || !inputs.L2ToL1MessageRoot.Equals(artifact.ExecutionResult.L2ToL1MessageRoot)
            || !inputs.L2ToL2MessageRoot.Equals(artifact.ExecutionResult.L2ToL2MessageRoot)
            || !inputs.DACommitment.Equals(artifact.DAReceipt.Commitment))
            throw new ArgumentException(
                "PublicInputs roots do not match the payload, execution result, and DA receipt",
                nameof(artifact));

        var expectedL1MessageHash = StateRootCalculator.HashL1Messages(
            artifact.ExecutionPayload.L1Messages);
        if (!inputs.L1MessageHash.Equals(expectedL1MessageHash))
            throw new ArgumentException(
                "PublicInputs.L1MessageHash does not match the execution payload",
                nameof(artifact));
        var expectedBlockContextHash = StateRootCalculator.HashBlockContext(
            artifact.ExecutionPayload.BlockContext);
        if (!inputs.BlockContextHash.Equals(expectedBlockContextHash))
            throw new ArgumentException(
                "PublicInputs.BlockContextHash does not match the execution payload",
                nameof(artifact));

        _ = BatchSerializer.EncodePublicInputs(inputs);
    }

    private static void ValidateExecutionResult(BatchExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(result.PostStateRoot);
        ArgumentNullException.ThrowIfNull(result.TxRoot);
        ArgumentNullException.ThrowIfNull(result.ReceiptRoot);
        ArgumentNullException.ThrowIfNull(result.WithdrawalRoot);
        ArgumentNullException.ThrowIfNull(result.L2ToL1MessageRoot);
        ArgumentNullException.ThrowIfNull(result.L2ToL2MessageRoot);
        if (result.GasConsumed < 0)
            throw new ArgumentException("ExecutionResult.GasConsumed must be non-negative");
    }
}

internal sealed class CanonicalWireWriter
{
    private readonly ArrayBufferWriter<byte> _writer;

    public CanonicalWireWriter(int initialCapacity)
        => _writer = new ArrayBufferWriter<byte>(initialCapacity);

    public int WrittenCount => _writer.WrittenCount;

    public void WriteByte(byte value)
    {
        var span = _writer.GetSpan(1);
        span[0] = value;
        _writer.Advance(1);
    }

    public void WriteUInt16(ushort value)
    {
        var span = _writer.GetSpan(2);
        BinaryPrimitives.WriteUInt16LittleEndian(span, value);
        _writer.Advance(2);
    }

    public void WriteUInt32(uint value)
    {
        var span = _writer.GetSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _writer.Advance(4);
    }

    public void WriteInt32(int value)
    {
        var span = _writer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(span, value);
        _writer.Advance(4);
    }

    public void WriteUInt64(ulong value)
    {
        var span = _writer.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _writer.Advance(8);
    }

    public void WriteInt64(long value)
    {
        var span = _writer.GetSpan(8);
        BinaryPrimitives.WriteInt64LittleEndian(span, value);
        _writer.Advance(8);
    }

    public void WriteUInt160(UInt160 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteBytes(value.GetSpan());
    }

    public void WriteUInt256(UInt256 value)
    {
        ArgumentNullException.ThrowIfNull(value);
        WriteBytes(value.GetSpan());
    }

    public void WriteLengthPrefixedBytes(ReadOnlySpan<byte> value)
    {
        WriteUInt32(checked((uint)value.Length));
        WriteBytes(value);
    }

    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        value.CopyTo(_writer.GetSpan(value.Length));
        _writer.Advance(value.Length);
    }

    public byte[] ToArray() => _writer.WrittenSpan.ToArray();
}

internal ref struct StrictWireReader
{
    private readonly ReadOnlySpan<byte> _data;
    private int _position;

    public StrictWireReader(ReadOnlySpan<byte> data)
    {
        _data = data;
        _position = 0;
    }

    public readonly int Remaining => _data.Length - _position;

    public byte ReadByte(string field)
    {
        EnsureAvailable(1, field);
        return _data[_position++];
    }

    public ushort ReadUInt16(string field)
    {
        var bytes = ReadBytes(2, field);
        return BinaryPrimitives.ReadUInt16LittleEndian(bytes);
    }

    public uint ReadUInt32(string field)
    {
        var bytes = ReadBytes(4, field);
        return BinaryPrimitives.ReadUInt32LittleEndian(bytes);
    }

    public int ReadInt32(string field)
    {
        var bytes = ReadBytes(4, field);
        return BinaryPrimitives.ReadInt32LittleEndian(bytes);
    }

    public ulong ReadUInt64(string field)
    {
        var bytes = ReadBytes(8, field);
        return BinaryPrimitives.ReadUInt64LittleEndian(bytes);
    }

    public long ReadInt64(string field)
    {
        var bytes = ReadBytes(8, field);
        return BinaryPrimitives.ReadInt64LittleEndian(bytes);
    }

    public UInt160 ReadUInt160(string field) => new(ReadBytes(20, field));

    public UInt256 ReadUInt256(string field) => new(ReadBytes(32, field));

    public uint ReadBoundedCount(uint maximum, string field)
    {
        var count = ReadUInt32(field);
        if (count > maximum)
            throw new InvalidDataException($"{field} {count} exceeds maximum {maximum}");
        return count;
    }

    public byte[] ReadLengthPrefixedBytes(int maximum, string field)
    {
        var length = ReadUInt32($"{field} length");
        if (length > maximum)
            throw new InvalidDataException($"{field} length {length} exceeds maximum {maximum}");
        return ReadBytes(checked((int)length), field).ToArray();
    }

    public StrictWireReader ReadSubReader(uint length, string field)
        => new(ReadBytes(checked((int)length), field));

    public ReadOnlySpan<byte> ReadBytes(int length, string field)
    {
        if (length < 0)
            throw new InvalidDataException($"{field} has a negative length");
        EnsureAvailable(length, field);
        var result = _data.Slice(_position, length);
        _position += length;
        return result;
    }

    public void RequireMagic(ReadOnlySpan<byte> expected, string format)
    {
        if (!ReadBytes(expected.Length, $"{format} magic").SequenceEqual(expected))
            throw new InvalidDataException($"Invalid {format} magic");
    }

    public void EnsureEnd(string format)
    {
        if (_position != _data.Length)
            throw new InvalidDataException(
                $"{format} has {_data.Length - _position} trailing bytes");
    }

    private void EnsureAvailable(int length, string field)
    {
        if (length > _data.Length - _position)
            throw new InvalidDataException(
                $"{field} is truncated: need {length} bytes, have {_data.Length - _position}");
    }
}
