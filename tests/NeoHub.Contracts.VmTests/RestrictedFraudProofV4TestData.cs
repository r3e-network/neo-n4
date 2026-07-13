using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Neo;

namespace NeoHub.Contracts.VmTests;

internal static class RestrictedFraudProofV4TestData
{
    internal const uint ChainId = 1001;
    internal const ulong BatchNumber = 42;
    internal const int ReplayDomainOffset = 1;
    internal const int ExecutorSemanticIdOffset = 33;
    internal const int ClaimIdOffset = 65;
    internal const int TranscriptHashOffset = 97;
    internal const int WitnessHashOffset = 129;
    internal const int CommittedHeaderHashOffset = 161;
    internal const int ChainIdOffset = 193;
    internal const int BatchNumberOffset = 197;
    internal const int DisputedTxIndexOffset = 205;
    internal const int TransactionCountOffset = 209;
    internal const int LowerBoundOffset = 213;
    internal const int UpperBoundOffset = 217;
    internal const int PreStateRootOffset = 221;
    internal const int CommittedPostStateRootOffset = 253;
    internal const int ExpectedPostStateRootOffset = 285;
    internal const int TxRootOffset = 317;
    internal const int TxLengthOffset = 349;
    internal const int FixedHeaderSize = 353;

    private const int CommitmentHeaderSize = 321;
    private const int HeaderPreStateRootOffset = 28;
    private const int HeaderPostStateRootOffset = 60;
    private const int HeaderTxRootOffset = 92;
    private const int HeaderProofTypeOffset = 316;
    private static readonly byte[] TranscriptTag = Encoding.ASCII.GetBytes("neo4-fraud-bisection-transcript:v1");
    private static readonly byte[] WitnessTag = Encoding.ASCII.GetBytes("neo4-fraud-witness:v1");
    private static readonly byte[] ClaimTag = Encoding.ASCII.GetBytes("neo4-fraud-claim:v4");
    private static readonly byte[] SemanticTag = Encoding.ASCII.GetBytes("neo4-executor:counter-increment-existing-key:v1");
    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('a', 40));

    internal static readonly UInt256 ReplayDomain = new(Hash256(Encoding.ASCII.GetBytes("vm-test-replay-domain")));
    internal static readonly UInt256 ExecutorSemanticId = new(Hash256(SemanticTag));

    internal sealed record Fixture(
        byte[] CanonicalHeader,
        byte[] Payload,
        UInt256 ClaimId,
        UInt256 ExpectedPostStateRoot,
        UInt256 CommittedPostStateRoot);

    internal static Fixture Build(UInt160 settlementManager, UInt160 fraudVerifier, bool committedFraud)
    {
        var transaction = new byte[29];
        transaction[0] = 0x01;
        Sender.GetSpan().CopyTo(transaction.AsSpan(1, 20));
        BinaryPrimitives.WriteUInt64LittleEndian(transaction.AsSpan(21, 8), 9);

        var stateKey = new byte[28];
        Encoding.ASCII.GetBytes("counter:").CopyTo(stateKey, 0);
        Sender.GetSpan().CopyTo(stateKey.AsSpan(8, 20));
        var preValue = new byte[8];
        var expectedPostValue = new byte[8];
        var committedPostValue = new byte[8];
        BinaryPrimitives.WriteUInt64LittleEndian(preValue, 11);
        BinaryPrimitives.WriteUInt64LittleEndian(expectedPostValue, 20);
        BinaryPrimitives.WriteUInt64LittleEndian(committedPostValue, committedFraud ? 21UL : 20UL);

        var siblingLeaf = HashEntry(new byte[] { 0xFF }, new byte[] { 0x44 });
        var preStateRoot = HashPair(HashEntry(stateKey, preValue), siblingLeaf);
        var expectedPostStateRoot = HashPair(HashEntry(stateKey, expectedPostValue), siblingLeaf);
        var committedPostStateRoot = HashPair(HashEntry(stateKey, committedPostValue), siblingLeaf);
        var transactionHash = Hash256(transaction);

        var canonicalHeader = new byte[CommitmentHeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(canonicalHeader.AsSpan(0, 4), ChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(canonicalHeader.AsSpan(4, 8), BatchNumber);
        BinaryPrimitives.WriteUInt64LittleEndian(canonicalHeader.AsSpan(12, 8), 100);
        BinaryPrimitives.WriteUInt64LittleEndian(canonicalHeader.AsSpan(20, 8), 100);
        preStateRoot.CopyTo(canonicalHeader, HeaderPreStateRootOffset);
        committedPostStateRoot.CopyTo(canonicalHeader, HeaderPostStateRootOffset);
        transactionHash.CopyTo(canonicalHeader, HeaderTxRootOffset);
        canonicalHeader[HeaderProofTypeOffset] = 2;

        var transactionProof = new byte[48];
        transactionHash.CopyTo(transactionProof, 0);
        var stateProof = EncodeStateProof(stateKey, preValue, committedPostValue, siblingLeaf);
        var payload = new byte[FixedHeaderSize + transaction.Length + 4 + transactionProof.Length + 4 + stateProof.Length];
        payload[0] = 4;
        ReplayDomain.GetSpan().CopyTo(payload.AsSpan(ReplayDomainOffset, 32));
        ExecutorSemanticId.GetSpan().CopyTo(payload.AsSpan(ExecutorSemanticIdOffset, 32));
        Hash256(canonicalHeader).CopyTo(payload, CommittedHeaderHashOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(ChainIdOffset, 4), ChainId);
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(BatchNumberOffset, 8), BatchNumber);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(DisputedTxIndexOffset, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(TransactionCountOffset, 4), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(LowerBoundOffset, 4), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(UpperBoundOffset, 4), 1);
        preStateRoot.CopyTo(payload, PreStateRootOffset);
        committedPostStateRoot.CopyTo(payload, CommittedPostStateRootOffset);
        expectedPostStateRoot.CopyTo(payload, ExpectedPostStateRootOffset);
        transactionHash.CopyTo(payload, TxRootOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(TxLengthOffset, 4), (uint)transaction.Length);

        var position = FixedHeaderSize;
        transaction.CopyTo(payload, position);
        position += transaction.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(position, 4), (uint)transactionProof.Length);
        position += 4;
        transactionProof.CopyTo(payload, position);
        position += transactionProof.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(position, 4), (uint)stateProof.Length);
        position += 4;
        stateProof.CopyTo(payload, position);

        var transcriptHash = ComputeTranscriptHash(payload);
        transcriptHash.CopyTo(payload, TranscriptHashOffset);
        var witnessHash = ComputeWitnessHash(payload);
        witnessHash.CopyTo(payload, WitnessHashOffset);
        var claimId = ComputeClaimId(payload, settlementManager, fraudVerifier, transcriptHash, witnessHash);
        claimId.CopyTo(payload, ClaimIdOffset);

        return new Fixture(
            canonicalHeader,
            payload,
            new UInt256(claimId),
            new UInt256(expectedPostStateRoot),
            new UInt256(committedPostStateRoot));
    }

    internal static byte[] BuildProfileProof(UInt256 replayDomain, UInt256 executorSemanticId, UInt256 claimId)
    {
        var payload = new byte[ClaimIdOffset + 32];
        payload[0] = 4;
        replayDomain.GetSpan().CopyTo(payload.AsSpan(ReplayDomainOffset, 32));
        executorSemanticId.GetSpan().CopyTo(payload.AsSpan(ExecutorSemanticIdOffset, 32));
        claimId.GetSpan().CopyTo(payload.AsSpan(ClaimIdOffset, 32));
        return payload;
    }

    internal static void Rebind(byte[] payload, UInt160 settlementManager, UInt160 fraudVerifier)
    {
        var transcriptHash = ComputeTranscriptHash(payload);
        transcriptHash.CopyTo(payload, TranscriptHashOffset);
        var witnessHash = ComputeWitnessHash(payload);
        witnessHash.CopyTo(payload, WitnessHashOffset);
        ComputeClaimId(payload, settlementManager, fraudVerifier, transcriptHash, witnessHash)
            .CopyTo(payload, ClaimIdOffset);
    }

    private static byte[] EncodeStateProof(byte[] key, byte[] preValue, byte[] postValue, byte[] sibling)
    {
        var bytes = new byte[2 + key.Length + 4 + preValue.Length + 4 + postValue.Length + 8 + 1 + 32 + 1 + 32];
        var position = 0;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(position, 2), (ushort)key.Length);
        position += 2;
        key.CopyTo(bytes, position);
        position += key.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position, 4), (uint)preValue.Length);
        position += 4;
        preValue.CopyTo(bytes, position);
        position += preValue.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position, 4), (uint)postValue.Length);
        position += 4;
        postValue.CopyTo(bytes, position);
        position += postValue.Length;
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(position, 8), 0);
        position += 8;
        bytes[position++] = 1;
        sibling.CopyTo(bytes, position);
        position += 32;
        bytes[position++] = 1;
        sibling.CopyTo(bytes, position);
        return bytes;
    }

    private static byte[] ComputeTranscriptHash(byte[] payload)
    {
        using var stream = new MemoryStream();
        stream.Write(TranscriptTag);
        WriteSlice(stream, payload, ReplayDomainOffset, 32);
        WriteSlice(stream, payload, ExecutorSemanticIdOffset, 32);
        WriteSlice(stream, payload, CommittedHeaderHashOffset, 32);
        WriteSlice(stream, payload, ChainIdOffset, 4);
        WriteSlice(stream, payload, BatchNumberOffset, 8);
        WriteSlice(stream, payload, DisputedTxIndexOffset, 4);
        WriteSlice(stream, payload, TransactionCountOffset, 4);
        WriteSlice(stream, payload, LowerBoundOffset, 4);
        WriteSlice(stream, payload, UpperBoundOffset, 4);
        WriteSlice(stream, payload, PreStateRootOffset, 32);
        WriteSlice(stream, payload, CommittedPostStateRootOffset, 32);
        WriteSlice(stream, payload, ExpectedPostStateRootOffset, 32);
        WriteSlice(stream, payload, TxRootOffset, 32);
        return Hash256(stream.ToArray());
    }

    private static byte[] ComputeWitnessHash(byte[] payload)
    {
        var preimage = new byte[WitnessTag.Length + payload.Length - TxLengthOffset];
        WitnessTag.CopyTo(preimage, 0);
        payload.AsSpan(TxLengthOffset).CopyTo(preimage.AsSpan(WitnessTag.Length));
        return Hash256(preimage);
    }

    private static byte[] ComputeClaimId(
        byte[] payload,
        UInt160 settlementManager,
        UInt160 fraudVerifier,
        byte[] transcriptHash,
        byte[] witnessHash)
    {
        using var stream = new MemoryStream();
        stream.Write(ClaimTag);
        stream.Write(settlementManager.GetSpan());
        stream.Write(fraudVerifier.GetSpan());
        WriteSlice(stream, payload, ReplayDomainOffset, 32);
        WriteSlice(stream, payload, ExecutorSemanticIdOffset, 32);
        WriteSlice(stream, payload, CommittedHeaderHashOffset, 32);
        WriteSlice(stream, payload, ChainIdOffset, 4);
        WriteSlice(stream, payload, BatchNumberOffset, 8);
        WriteSlice(stream, payload, DisputedTxIndexOffset, 4);
        stream.Write(transcriptHash);
        stream.Write(witnessHash);
        return Hash256(stream.ToArray());
    }

    private static byte[] HashEntry(byte[] key, byte[] value)
    {
        var bytes = new byte[4 + key.Length + 4 + value.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(0, 4), key.Length);
        key.CopyTo(bytes, 4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4 + key.Length, 4), value.Length);
        value.CopyTo(bytes, 8 + key.Length);
        return Hash256(bytes);
    }

    private static byte[] HashPair(byte[] left, byte[] right)
    {
        var bytes = new byte[64];
        left.CopyTo(bytes, 0);
        right.CopyTo(bytes, 32);
        return Hash256(bytes);
    }

    private static byte[] Hash256(byte[] bytes) => SHA256.HashData(SHA256.HashData(bytes));

    private static void WriteSlice(Stream stream, byte[] bytes, int offset, int count) =>
        stream.Write(bytes, offset, count);
}
