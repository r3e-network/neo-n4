using System.Buffers.Binary;
using Neo.L2.State;

namespace Neo.L2.Batch.UnitTests;

[TestClass]
public class UT_ProofWitnessArtifact
{
    [TestMethod]
    public void ExecutionPayload_RoundTripsCanonicalBytes()
    {
        var payload = SamplePayload();
        var bytes = ExecutionPayloadSerializer.Encode(payload);
        var decoded = ExecutionPayloadSerializer.Decode(bytes);

        Assert.AreEqual(payload, decoded);
        CollectionAssert.AreEqual("NEO4EXEC"u8.ToArray(), bytes[..8]);
        Assert.AreEqual(
            ExecutionPayloadV1.Version,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
        Assert.AreEqual(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(10, 2)));
        Assert.AreEqual(payload.ChainId, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4)));
        Assert.AreEqual(payload.BatchNumber, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16, 8)));
        Assert.AreEqual(payload.FirstBlock, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24, 8)));
        Assert.AreEqual(payload.LastBlock, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(32, 8)));
        CollectionAssert.AreEqual(payload.PreStateRoot.GetSpan().ToArray(), bytes[40..72]);
    }

    [TestMethod]
    public void ExecutionPayload_CommitmentHasStableGoldenValue()
    {
        var commitment = ExecutionPayloadSerializer.ComputeCommitment(SamplePayload());
        Assert.AreEqual(
            "851d973ae3e7b163c664f825f6c43533bfdd5502c46f57178e944b2344be6f31",
            Convert.ToHexString(commitment.GetSpan()).ToLowerInvariant());
    }

    [TestMethod]
    public void ExecutionPayload_ValueSemanticsBindEveryOrderedField()
    {
        var forced = new ForcedInclusionConsumptionProof
        {
            Nonce = 77,
            LeafIndex = 0,
            TxHash = H(0x71),
            Siblings = [H(0x72)],
        };
        var payload = SamplePayload() with { ForcedInclusions = [forced] };
        var equal = payload with
        {
            L1Messages = payload.L1Messages.ToArray(),
            ForcedInclusions = payload.ForcedInclusions.ToArray(),
            Transactions = payload.Transactions.Select(static value =>
                (ReadOnlyMemory<byte>)value.ToArray()).ToArray(),
        };

        Assert.AreEqual(payload, equal);
        Assert.AreEqual(payload.GetHashCode(), equal.GetHashCode());
        Assert.AreNotEqual(payload, null);
        Assert.AreNotEqual(payload, payload with { ChainId = payload.ChainId + 1 });
        Assert.AreNotEqual(payload, payload with { BatchNumber = payload.BatchNumber + 1 });
        Assert.AreNotEqual(payload, payload with { FirstBlock = payload.FirstBlock + 1 });
        Assert.AreNotEqual(payload, payload with { LastBlock = payload.LastBlock + 1 });
        Assert.AreNotEqual(payload, payload with { PreStateRoot = H(0x73) });
        Assert.AreNotEqual(payload, payload with
        {
            BlockContext = payload.BlockContext with
            {
                L1FinalizedHeight = payload.BlockContext.L1FinalizedHeight + 1,
            },
        });
        Assert.AreNotEqual(payload, payload with { L1Messages = [] });
        Assert.AreNotEqual(payload, payload with { ForcedInclusions = [] });
        Assert.AreNotEqual(payload, payload with { Transactions = [] });
        Assert.AreNotEqual(payload, payload with
        {
            L1Messages = [payload.L1Messages[0] with { Nonce = 99 }],
        });
        Assert.AreNotEqual(payload, payload with
        {
            ForcedInclusions = [forced with { Nonce = 78 }],
        });
        Assert.AreNotEqual(payload, payload with
        {
            Transactions = [new byte[] { 0xff }, payload.Transactions[1]],
        });
    }

    [TestMethod]
    public void ExecutionPayload_DecodeRejectsUnknownMagicVersionFlagsAndTrailingBytes()
    {
        var canonical = ExecutionPayloadSerializer.Encode(SamplePayload());
        AssertDecodeFails(Mutate(canonical, 0, 0x00));

        var unknownVersion = canonical.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(unknownVersion.AsSpan(8, 2), 2);
        AssertDecodeFails(unknownVersion);

        var unknownFlags = canonical.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(unknownFlags.AsSpan(10, 2), 1);
        AssertDecodeFails(unknownFlags);

        AssertDecodeFails([.. canonical, 0x00]);
    }

    [TestMethod]
    public void ExecutionPayload_DecodeRejectsTruncationAndLengthAbuse()
    {
        var canonical = ExecutionPayloadSerializer.Encode(SamplePayload());
        AssertDecodeFails(canonical[..^1]);

        var oversizedMessage = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedMessage.AsSpan(132, 4), uint.MaxValue);
        AssertDecodeFails(oversizedMessage);

        var oversizedCount = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedCount.AsSpan(128, 4), uint.MaxValue);
        AssertDecodeFails(oversizedCount);

        var impossibleMessageCount = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            impossibleMessageCount.AsSpan(128, 4),
            ExecutionPayloadSerializer.MaxItemCount);
        AssertDecodeFails(impossibleMessageCount);

        var impossibleTransactionCount = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(
            impossibleTransactionCount.AsSpan(204, 4),
            ExecutionPayloadSerializer.MaxItemCount);
        AssertDecodeFails(impossibleTransactionCount);
    }

    [TestMethod]
    public void ExecutionPayload_DecodeRejectsInvalidMessageRoutingAndType()
    {
        var canonical = ExecutionPayloadSerializer.Encode(SamplePayload());

        var wrongSource = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongSource.AsSpan(136, 4), 9);
        AssertDecodeFails(wrongSource);

        var wrongTarget = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongTarget.AsSpan(140, 4), 9);
        AssertDecodeFails(wrongTarget);

        AssertDecodeFails(Mutate(canonical, 192, 0xff));
    }

    [TestMethod]
    public void ExecutionPayload_EncodeRejectsForgedMessageHash()
    {
        var payload = SamplePayload();
        var forged = payload.L1Messages[0] with { MessageHash = H(0xee) };
        var invalid = payload with { L1Messages = [forged] };
        Assert.ThrowsExactly<ArgumentException>(() => ExecutionPayloadSerializer.Encode(invalid));
    }

    [TestMethod]
    public void ExecutionPayload_PersistsCanonicalForcedInclusionProofs()
    {
        var payload = SamplePayload();
        var txHashes = payload.Transactions
            .Select(transaction => new UInt256(
                Neo.Cryptography.Crypto.Hash256(transaction.Span)))
            .ToArray();
        var merkleProof = new Neo.L2.State.MerkleTree(txHashes).GetProof(0);
        var forced = new ForcedInclusionConsumptionProof
        {
            Nonce = 77,
            LeafIndex = 0,
            TxHash = txHashes[0],
            Siblings = merkleProof.Siblings,
        };
        var withForced = payload with
        {
            ForcedInclusions = new[] { forced },
        };

        var decoded = ExecutionPayloadSerializer.Decode(
            ExecutionPayloadSerializer.Encode(withForced));
        Assert.AreEqual(forced, decoded.ForcedInclusions.Single());

        Assert.ThrowsExactly<ArgumentException>(() =>
            ExecutionPayloadSerializer.Encode(withForced with
            {
                ForcedInclusions = new[] { forced with { LeafIndex = 1 } },
            }));
        Assert.ThrowsExactly<ArgumentException>(() =>
            ExecutionPayloadSerializer.Encode(withForced with
            {
                ForcedInclusions = new[] { forced with { TxHash = H(0xee) } },
            }));
    }

    [TestMethod]
    public void Artifact_RoundTripsAndEmbedsCanonicalPublicInputs()
    {
        var artifact = SampleArtifact();
        var bytes = ProofWitnessArtifactSerializer.Encode(artifact);
        var decoded = ProofWitnessArtifactSerializer.Decode(bytes);

        Assert.AreEqual(artifact, decoded);
        CollectionAssert.AreEqual("NEO4PWIT"u8.ToArray(), bytes[..8]);
        Assert.AreEqual(
            ProofWitnessArtifactV1.Version,
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2)));
        Assert.AreEqual((byte)ProofType.Zk, bytes[12]);
        Assert.AreEqual((byte)WitnessProofSystem.Sp1, bytes[13]);
        CollectionAssert.AreEqual(new byte[2], bytes[14..16]);

        var publicInputsOffset = bytes.Length - UInt256.Length - BatchSerializer.PublicInputsSize;
        CollectionAssert.AreEqual(
            BatchSerializer.EncodePublicInputs(artifact.PublicInputs),
            bytes.AsSpan(publicInputsOffset, BatchSerializer.PublicInputsSize).ToArray());
        CollectionAssert.AreEqual(
            artifact.ContentHash.GetSpan().ToArray(),
            bytes[^UInt256.Length..]);
    }

    [TestMethod]
    public void Artifact_ContentHashHasStableGoldenValue()
    {
        var artifact = SampleArtifact();
        Assert.AreEqual(
            "3e828a96f943f514ad9d29ef2e8e6f209c7bdcfa6e0b108576327cb1574d1553",
            Convert.ToHexString(artifact.ContentHash.GetSpan()).ToLowerInvariant());
    }

    [TestMethod]
    public void Artifact_ValueSemanticsAndHashCodeBindEveryField()
    {
        var artifact = SampleArtifact();
        var equal = ProofWitnessArtifactSerializer.Decode(
            ProofWitnessArtifactSerializer.Encode(artifact));

        Assert.AreEqual(artifact, equal);
        Assert.AreEqual(artifact.GetHashCode(), equal.GetHashCode());
        Assert.AreNotEqual(artifact, null);
        Assert.AreNotEqual(artifact, artifact with { ProofSystem = WitnessProofSystem.Halo2 });
        Assert.AreNotEqual(artifact, artifact with { ProofType = ProofType.Optimistic });
        Assert.AreNotEqual(artifact, artifact with { VerificationKeyId = H(0x81) });
        Assert.AreNotEqual(artifact, artifact with { ExecutionSemanticId = H(0x82) });
        Assert.AreNotEqual(artifact, artifact with { ExecutionWitnessAuthenticated = false });
        Assert.AreNotEqual(artifact, artifact with { ChainId = artifact.ChainId + 1 });
        Assert.AreNotEqual(artifact, artifact with { BatchNumber = artifact.BatchNumber + 1 });
        Assert.AreNotEqual(artifact, artifact with { FirstBlock = artifact.FirstBlock + 1 });
        Assert.AreNotEqual(artifact, artifact with { LastBlock = artifact.LastBlock + 1 });
        Assert.AreNotEqual(artifact, artifact with
        {
            ExecutionPayload = artifact.ExecutionPayload with
            {
                BatchNumber = artifact.ExecutionPayload.BatchNumber + 1,
            },
        });
        Assert.AreNotEqual(artifact, artifact with { StateWitness = new byte[] { 0x83 } });
        Assert.AreNotEqual(artifact, artifact with
        {
            ExecutionResult = artifact.ExecutionResult with
            {
                GasConsumed = artifact.ExecutionResult.GasConsumed + 1,
            },
        });
        Assert.AreNotEqual(artifact, artifact with { Effects = new byte[] { 0x84 } });
        Assert.AreNotEqual(artifact, artifact with
        {
            DAReceipt = artifact.DAReceipt with { Evidence = new byte[] { 0x85 } },
        });
        Assert.AreNotEqual(artifact, artifact with
        {
            PublicInputs = artifact.PublicInputs with { ReceiptRoot = H(0x86) },
        });
        Assert.AreNotEqual(artifact, artifact with { ContentHash = H(0x87) });
    }

    [TestMethod]
    public void Artifact_DecodeRejectsUnknownHeaderFields()
    {
        var canonical = ProofWitnessArtifactSerializer.Encode(SampleArtifact());
        AssertArtifactDecodeFails(Mutate(canonical, 0, 0x00));

        var version = canonical.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(version.AsSpan(8, 2), 2);
        AssertArtifactDecodeFails(version);

        var flags = canonical.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(flags.AsSpan(10, 2), 0x8000);
        AssertArtifactDecodeFails(flags);

        AssertArtifactDecodeFails(Mutate(canonical, 12, 0xff));
        AssertArtifactDecodeFails(Mutate(canonical, 13, 0xff));
        AssertArtifactDecodeFails(Mutate(canonical, 14, 0x01));
    }

    [TestMethod]
    public void Artifact_DecodeRejectsTamperTruncationTrailingAndLengthOverflow()
    {
        var canonical = ProofWitnessArtifactSerializer.Encode(SampleArtifact());
        AssertArtifactDecodeFails(Mutate(canonical, 16, (byte)(canonical[16] ^ 0x80)));
        AssertArtifactDecodeFails(canonical[..^1]);
        AssertArtifactDecodeFails([.. canonical, 0x00]);

        var oversizedPayload = canonical.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(oversizedPayload.AsSpan(108, 4), uint.MaxValue);
        AssertArtifactDecodeFails(oversizedPayload);
    }

    [TestMethod]
    public void Artifact_DecodeRejectsUnknownDaMode()
    {
        var artifact = SampleArtifact();
        var canonical = ProofWitnessArtifactSerializer.Encode(artifact);
        var payloadLength = BinaryPrimitives.ReadUInt32LittleEndian(canonical.AsSpan(108, 4));
        var stateLengthOffset = checked(112 + (int)payloadLength);
        var stateLength = BinaryPrimitives.ReadUInt32LittleEndian(
            canonical.AsSpan(stateLengthOffset, 4));
        var effectsLengthOffset = checked(stateLengthOffset + 4 + (int)stateLength + 200);
        var effectsLength = BinaryPrimitives.ReadUInt32LittleEndian(
            canonical.AsSpan(effectsLengthOffset, 4));
        var daModeOffset = checked(effectsLengthOffset + 4 + (int)effectsLength);
        AssertArtifactDecodeFails(Mutate(canonical, daModeOffset, 0xff));
    }

    [TestMethod]
    public void Artifact_EverySingleByteTamperIsRejected()
    {
        var canonical = ProofWitnessArtifactSerializer.Encode(SampleArtifact());
        for (var index = 0; index < canonical.Length; index++)
        {
            var tampered = canonical.ToArray();
            tampered[index] ^= 0x01;
            Assert.ThrowsExactly<InvalidDataException>(
                () => ProofWitnessArtifactSerializer.Decode(tampered),
                $"tamper at byte {index} must be rejected");
        }
    }

    [TestMethod]
    public void Artifact_EveryTruncatedPrefixIsRejected()
    {
        var canonical = ProofWitnessArtifactSerializer.Encode(SampleArtifact());
        for (var length = 0; length < canonical.Length; length++)
        {
            Assert.ThrowsExactly<InvalidDataException>(
                () => ProofWitnessArtifactSerializer.Decode(canonical.AsSpan(0, length)),
                $"truncation at length {length} must be rejected");
        }
    }

    [TestMethod]
    public void Artifact_CreateRejectsCrossFieldMismatches()
    {
        var artifact = SampleArtifact();

        var wrongIdentity = artifact with { ChainId = artifact.ChainId + 1 };
        Assert.ThrowsExactly<ArgumentException>(
            () => ProofWitnessArtifactSerializer.ComputeContentHash(wrongIdentity));

        var wrongDa = artifact with
        {
            DAReceipt = artifact.DAReceipt with { Commitment = H(0xef) },
        };
        Assert.ThrowsExactly<ArgumentException>(
            () => ProofWitnessArtifactSerializer.ComputeContentHash(wrongDa));

        var wrongInputs = artifact with
        {
            PublicInputs = artifact.PublicInputs with { TxRoot = H(0xee) },
        };
        Assert.ThrowsExactly<ArgumentException>(
            () => ProofWitnessArtifactSerializer.ComputeContentHash(wrongInputs));
    }

    [TestMethod]
    public void Artifact_RejectsL1ContextTimestampAndTransactionTamper()
    {
        var artifact = SampleArtifact();
        var originalMessage = artifact.ExecutionPayload.L1Messages[0];
        var changedMessage = originalMessage with
        {
            Payload = new byte[] { 0xff },
            MessageHash = UInt256.Zero,
        };
        changedMessage = changedMessage with
        {
            MessageHash = MessageHasher.HashMessage(changedMessage),
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            ProofWitnessArtifactSerializer.ComputeContentHash(artifact with
            {
                ExecutionPayload = artifact.ExecutionPayload with
                {
                    L1Messages = [changedMessage],
                },
            }));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ProofWitnessArtifactSerializer.ComputeContentHash(artifact with
            {
                ExecutionPayload = artifact.ExecutionPayload with
                {
                    BlockContext = artifact.ExecutionPayload.BlockContext with
                    {
                        LastBlockTimestamp =
                            artifact.ExecutionPayload.BlockContext.LastBlockTimestamp + 1,
                    },
                },
            }));

        Assert.ThrowsExactly<ArgumentException>(() =>
            ProofWitnessArtifactSerializer.ComputeContentHash(artifact with
            {
                ExecutionPayload = artifact.ExecutionPayload with
                {
                    Transactions = [new byte[] { 0xfe }, .. artifact.ExecutionPayload.Transactions.Skip(1)],
                },
            }));
    }

    [TestMethod]
    public void Artifact_EncodeRejectsStaleContentHash()
    {
        var artifact = SampleArtifact();
        var modified = artifact with { Effects = new byte[] { 0x99 } };
        Assert.ThrowsExactly<InvalidOperationException>(
            () => ProofWitnessArtifactSerializer.Encode(modified));
    }

    [TestMethod]
    public void Artifact_LegacyProfileAllowsEmptyInternalWitnessSections()
    {
        var artifact = SampleArtifact();
        var empty = ProofWitnessArtifactV1.Create(
            ProofType.Multisig,
            WitnessProofSystem.None,
            UInt256.Zero,
            artifact.ExecutionSemanticId,
            false,
            artifact.ChainId,
            artifact.BatchNumber,
            artifact.FirstBlock,
            artifact.LastBlock,
            artifact.ExecutionPayload,
            ReadOnlyMemory<byte>.Empty,
            artifact.ExecutionResult,
            ReadOnlyMemory<byte>.Empty,
            artifact.DAReceipt,
            artifact.PublicInputs);

        Assert.AreEqual(
            empty,
            ProofWitnessArtifactSerializer.Decode(ProofWitnessArtifactSerializer.Encode(empty)));
    }

    [TestMethod]
    public void Artifact_RejectsOversizedDaPointer()
    {
        var artifact = SampleArtifact();
        var oversized = artifact with
        {
            DAReceipt = artifact.DAReceipt with
            {
                Pointer = new byte[ProofWitnessArtifactSerializer.MaxDaPointerBytes + 1],
            },
        };
        Assert.ThrowsExactly<ArgumentException>(
            () => ProofWitnessArtifactSerializer.ComputeContentHash(oversized));
    }

    internal static ProofWitnessArtifactV1 SampleArtifact(ulong batchNumber = 257)
    {
        var payload = SamplePayload(batchNumber);
        var executionResult = new BatchExecutionResult
        {
            PostStateRoot = H(0x31),
            TxRoot = H(0x32),
            ReceiptRoot = H(0x33),
            WithdrawalRoot = H(0x34),
            L2ToL1MessageRoot = H(0x35),
            L2ToL2MessageRoot = H(0x36),
            GasConsumed = 12_345,
        };
        var daReceipt = new DAReceipt
        {
            Layer = DAMode.NeoFS,
            Commitment = ExecutionPayloadSerializer.ComputeCommitment(payload),
            Pointer = new byte[] { 0xa1, 0xa2, 0xa3 },
            Kind = DAReceiptKind.NeoFSObject,
            Evidence = new byte[] { 0xa4, 0xa5 },
        };
        var publicInputs = new PublicInputs
        {
            ChainId = payload.ChainId,
            BatchNumber = payload.BatchNumber,
            PreStateRoot = payload.PreStateRoot,
            PostStateRoot = executionResult.PostStateRoot,
            TxRoot = executionResult.TxRoot,
            ReceiptRoot = executionResult.ReceiptRoot,
            WithdrawalRoot = executionResult.WithdrawalRoot,
            L2ToL1MessageRoot = executionResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = executionResult.L2ToL2MessageRoot,
            L1MessageHash = StateRootCalculator.HashL1Messages(payload.L1Messages),
            DACommitment = daReceipt.Commitment,
            BlockContextHash = StateRootCalculator.HashBlockContext(payload.BlockContext),
        };
        return ProofWitnessArtifactV1.Create(
            ProofType.Zk,
            WitnessProofSystem.Sp1,
            H(0x41),
            H(0x42),
            true,
            payload.ChainId,
            payload.BatchNumber,
            payload.FirstBlock,
            payload.LastBlock,
            payload,
            new byte[] { 0xb1, 0xb2, 0xb3, 0xb4 },
            executionResult,
            new byte[] { 0xc1, 0xc2 },
            daReceipt,
            publicInputs);
    }

    internal static ExecutionPayloadV1 SamplePayload(ulong batchNumber = 257)
    {
        const uint chainId = 0x1020_3040;
        var unhashedMessage = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = chainId,
            Nonce = 9,
            Sender = H160(0x11),
            Receiver = H160(0x22),
            MessageType = MessageType.Deposit,
            Payload = new byte[] { 0xd1, 0xd2, 0xd3 },
            MessageHash = UInt256.Zero,
        };
        var message = unhashedMessage with
        {
            MessageHash = MessageHasher.HashMessage(unhashedMessage),
        };
        return new ExecutionPayloadV1
        {
            ChainId = chainId,
            BatchNumber = batchNumber,
            FirstBlock = 100,
            LastBlock = 102,
            PreStateRoot = H(0x10),
            BlockContext = new BatchBlockContext
            {
                L1FinalizedHeight = 88,
                FirstBlockTimestamp = 1_700_000_000,
                LastBlockTimestamp = 1_700_000_012,
                SequencerCommitteeHash = H(0x20),
                Network = 860_833_102,
            },
            L1Messages = [message],
            Transactions =
            [
                new byte[] { 0x01, 0x02 },
                new byte[] { 0x03, 0x04, 0x05 },
            ],
        };
    }

    internal static UInt256 H(byte value)
        => new(Enumerable.Repeat(value, UInt256.Length).ToArray());

    private static UInt160 H160(byte value)
        => new(Enumerable.Repeat(value, UInt160.Length).ToArray());

    private static byte[] Mutate(byte[] source, int index, byte value)
    {
        var mutated = source.ToArray();
        mutated[index] = value;
        return mutated;
    }

    private static void AssertDecodeFails(byte[] data)
        => Assert.ThrowsExactly<InvalidDataException>(
            () => ExecutionPayloadSerializer.Decode(data));

    private static void AssertArtifactDecodeFails(byte[] data)
        => Assert.ThrowsExactly<InvalidDataException>(
            () => ProofWitnessArtifactSerializer.Decode(data));
}
