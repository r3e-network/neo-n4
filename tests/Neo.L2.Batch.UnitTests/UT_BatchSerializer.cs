namespace Neo.L2.Batch.UnitTests;

[TestClass]
public class UT_BatchSerializer
{
    private static UInt256 H(char c) => UInt256.Parse("0x" + new string(c, 64));

    private static L2BatchCommitment Sample(byte[]? proof = null) => new()
    {
        ChainId = 0xCAFEBABE,
        BatchNumber = 0xDEAD_BEEF_F00D_BABEUL,
        FirstBlock = 100,
        LastBlock = 200,
        PreStateRoot = H('1'),
        PostStateRoot = H('2'),
        TxRoot = H('3'),
        ReceiptRoot = H('4'),
        WithdrawalRoot = H('5'),
        L2ToL1MessageRoot = H('6'),
        L2ToL2MessageRoot = H('7'),
        DACommitment = H('8'),
        PublicInputHash = H('9'),
        ProofType = ProofType.Zk,
        Proof = proof ?? new byte[] { 0xAA, 0xBB, 0xCC },
    };

    [TestMethod]
    public void Commitment_RoundTrips()
    {
        var original = Sample();
        var bytes = BatchSerializer.Encode(original);
        var decoded = BatchSerializer.Decode(bytes);
        Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void Commitment_RoundTrips_EmptyProof()
    {
        var original = Sample(Array.Empty<byte>());
        var bytes = BatchSerializer.Encode(original);
        var decoded = BatchSerializer.Decode(bytes);
        Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void Commitment_DeterministicOutput()
    {
        var a = BatchSerializer.Encode(Sample());
        var b = BatchSerializer.Encode(Sample());
        CollectionAssert.AreEqual(a, b);
    }

    [TestMethod]
    public void Commitment_FixedSizeMatchesConstant()
    {
        var bytes = BatchSerializer.Encode(Sample(Array.Empty<byte>()));
        Assert.AreEqual(BatchSerializer.CommitmentFixedSize, bytes.Length);
    }

    [TestMethod]
    public void Commitment_RejectsOversizedProof()
    {
        var huge = new byte[2 * 1024 * 1024];
        var c = Sample(huge);
        Assert.ThrowsExactly<ArgumentException>(() => BatchSerializer.Encode(c));
    }

    [TestMethod]
    public void Commitment_Encode_RejectsNullCommitment()
        => Assert.ThrowsExactly<ArgumentNullException>(() => BatchSerializer.Encode(null!));

    [TestMethod]
    public void Commitment_Encode_RejectsNullRootField()
    {
        // Pin one of the 9 per-field null-guards at BatchSerializer.cs:81-89. Pattern is
        // uniform — all 9 are `ArgumentNullException.ThrowIfNull` against UInt256 root
        // fields. Without these, a null root would NRE inside WriteUInt256's GetSpan with
        // no link back to which field was null. Same iter-154/155/156 hashing-primitive
        // null-guard pattern that this test pins for the Encode side.
        var bad = Sample() with { PreStateRoot = null! };
        Assert.ThrowsExactly<ArgumentNullException>(() => BatchSerializer.Encode(bad));
    }

    [TestMethod]
    public void Commitment_Encode_RejectsOutOfRangeProofType()
    {
        // iter-159 Encode/Decode symmetry: Decode rejects ProofType > Zk; Encode must
        // refuse to produce bytes Decode would later reject. Without this guard, a
        // malformed commitment (ProofType = (ProofType)99) would round-trip Encode →
        // bytes → Decode where Decode finally rejects, with a misleading "decoder bug"
        // suspicion rather than the actual cause (encoder accepted bad input).
        var bad = Sample() with { ProofType = (ProofType)99 };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => BatchSerializer.Encode(bad));
        StringAssert.Contains(ex.Message, "ProofType");
        StringAssert.Contains(ex.Message, "99");
    }

    [TestMethod]
    public void Commitment_DetectsTruncation()
    {
        var bytes = BatchSerializer.Encode(Sample());
        Assert.ThrowsExactly<InvalidDataException>(() => BatchSerializer.Decode(bytes.AsSpan(0, bytes.Length - 1)));
    }

    [TestMethod]
    public void PublicInputs_RoundTrips()
    {
        var original = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 42,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = H('a'),
            PostStateRoot = H('b'),
            TxRoot = H('c'),
            ReceiptRoot = H('d'),
            WithdrawalRoot = H('e'),
            L2ToL1MessageRoot = H('f'),
            L2ToL2MessageRoot = H('1'),
            L1MessageHash = H('2'),
            DACommitment = H('3'),
            BlockContextHash = H('4'),
        };
        var bytes = BatchSerializer.EncodePublicInputs(original);
        Assert.AreEqual(BatchSerializer.PublicInputsSize, bytes.Length);

        var decoded = BatchSerializer.DecodePublicInputs(bytes);
        Assert.AreEqual(original, decoded);
    }

    [TestMethod]
    public void PublicInputs_RejectsWrongSize()
    {
        Assert.ThrowsExactly<ArgumentException>(() =>
            BatchSerializer.DecodePublicInputs(new byte[BatchSerializer.PublicInputsSize - 1]));
    }

    [TestMethod]
    public void PublicInputs_Encode_RejectsNullInputs()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => BatchSerializer.EncodePublicInputs(null!));

    [TestMethod]
    public void PublicInputs_Encode_RejectsNullMember()
    {
        // Pin one of the 10 per-field null-guards at BatchSerializer.cs:207-216. Pattern
        // is uniform — same Encode-side defense-in-depth pattern as the L2BatchCommitment
        // guards pinned in iter 219. PreStateRoot is the representative.
        var bad = new PublicInputs
        {
            ChainId = 1001,
            BatchNumber = 1,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = null!,
            PostStateRoot = H('1'),
            TxRoot = H('2'),
            ReceiptRoot = H('3'),
            WithdrawalRoot = H('4'),
            L2ToL1MessageRoot = H('5'),
            L2ToL2MessageRoot = H('6'),
            L1MessageHash = H('7'),
            DACommitment = H('8'),
            BlockContextHash = H('9'),
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => BatchSerializer.EncodePublicInputs(bad));
    }

    [TestMethod]
    public void Commitment_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the layout claimed in BatchSerializer's XML docs. NeoHub.SettlementManager
        // depends on these offsets; a future encoder reorder must fail this test.
        var c = Sample(new byte[] { 0xAA, 0xBB, 0xCC });
        var bytes = BatchSerializer.Encode(c);

        Assert.AreEqual(BatchSerializer.CommitmentFixedSize + c.Proof.Length, bytes.Length);

        Assert.AreEqual(0xCAFEBABEu, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.AreEqual(0xDEAD_BEEF_F00D_BABEUL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4, 8)));
        Assert.AreEqual(100UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12, 8)));
        Assert.AreEqual(200UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20, 8)));

        CollectionAssert.AreEqual(c.PreStateRoot.GetSpan().ToArray(), bytes[28..60]);
        CollectionAssert.AreEqual(c.PostStateRoot.GetSpan().ToArray(), bytes[60..92]);
        CollectionAssert.AreEqual(c.TxRoot.GetSpan().ToArray(), bytes[92..124]);
        CollectionAssert.AreEqual(c.ReceiptRoot.GetSpan().ToArray(), bytes[124..156]);
        CollectionAssert.AreEqual(c.WithdrawalRoot.GetSpan().ToArray(), bytes[156..188]);
        CollectionAssert.AreEqual(c.L2ToL1MessageRoot.GetSpan().ToArray(), bytes[188..220]);
        CollectionAssert.AreEqual(c.L2ToL2MessageRoot.GetSpan().ToArray(), bytes[220..252]);
        CollectionAssert.AreEqual(c.DACommitment.GetSpan().ToArray(), bytes[252..284]);
        CollectionAssert.AreEqual(c.PublicInputHash.GetSpan().ToArray(), bytes[284..316]);

        Assert.AreEqual((byte)ProofType.Zk, bytes[316]);
        Assert.AreEqual(c.Proof.Length, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(317, 4)));
        CollectionAssert.AreEqual(c.Proof.ToArray(), bytes[321..]);
    }

    [TestMethod]
    public void Commitment_Encode_RejectsOversizedProof()
    {
        // Proof exceeding 1 MiB throws — defensive limit matching NeoHub.
        var oversized = new byte[1024 * 1024 + 1];
        var c = Sample(oversized);
        Assert.ThrowsExactly<ArgumentException>(() => BatchSerializer.Encode(c));
    }

    [TestMethod]
    public void Commitment_Encode_AcceptsExactlyMaxProof()
    {
        // Boundary case: exactly 1 MiB must succeed. Off-by-one in the limit check would
        // either reject this (too strict) or accept 1 MiB+1 (too loose) — pin both directions.
        var atLimit = new byte[1024 * 1024];
        var c = Sample(atLimit);
        var bytes = BatchSerializer.Encode(c);
        var decoded = BatchSerializer.Decode(bytes);
        Assert.AreEqual(atLimit.Length, decoded.Proof.Length);
    }

    [TestMethod]
    public void Commitment_Decode_RejectsHeaderClaimingOversizedProof()
    {
        // Craft a header that claims proof length > ProofMaxBytes; decoder must reject before
        // attempting to allocate the array.
        var c = Sample(new byte[] { 0xAA });
        var bytes = BatchSerializer.Encode(c);
        // Overwrite the proof-length prefix at offset 317 with a huge value.
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(317, 4), 1024 * 1024 + 1);
        Assert.ThrowsExactly<InvalidDataException>(() => BatchSerializer.Decode(bytes));
    }

    [TestMethod]
    public void Commitment_Decode_RejectsTrailingBytes()
    {
        // Regression: previously the length check was `data.Length < pos + proofLen`
        // (allows trailing). Trailing bytes after the proof would be silently ignored,
        // creating a malleability surface — the same logical commitment yields different
        // on-chain hashes if the L1 contract hashes the full calldata while the L2
        // decoder strips trailing bytes.
        var c = Sample(new byte[] { 0x01, 0x02 });
        var bytes = BatchSerializer.Encode(c).ToList();
        bytes.AddRange(new byte[] { 0xFF, 0xFF, 0xFF });  // trailing padding
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => BatchSerializer.Decode(bytes.ToArray()));
        StringAssert.Contains(ex.Message, "length mismatch");
    }

    [TestMethod]
    public void Commitment_Decode_RejectsUnknownProofType()
    {
        // Regression: previously the ProofType byte was cast (ProofType)data[pos++] without
        // bounds-checking. A corrupted or replayed-from-future payload with a discriminant
        // > 3 silently produced an undefined-name enum value that downstream `==` checks
        // would treat as "not the expected one" — silent verification skip.
        var c = Sample(new byte[] { 0xAA });
        var bytes = BatchSerializer.Encode(c);
        // ProofType byte is at offset 316; max valid is 3 (Zk). Overwrite with 99.
        bytes[316] = 99;
        var ex = Assert.ThrowsExactly<InvalidDataException>(() => BatchSerializer.Decode(bytes));
        StringAssert.Contains(ex.Message, "Unknown ProofType");
    }

    [TestMethod]
    public void Commitment_Decode_AcceptsAllValidProofTypes()
    {
        // Boundary partner of the rejection test: every valid enum byte (0..3) decodes.
        foreach (ProofType pt in Enum.GetValues<ProofType>())
        {
            var c = Sample(new byte[] { 0xAA }) with { ProofType = pt };
            var bytes = BatchSerializer.Encode(c);
            var decoded = BatchSerializer.Decode(bytes);
            Assert.AreEqual(pt, decoded.ProofType);
        }
    }

    [TestMethod]
    public void PublicInputs_ByteLayout_MatchesDocumentedOffsets()
    {
        var inputs = new PublicInputs
        {
            ChainId = 0xCAFEBABE,
            BatchNumber = 0xDEAD_BEEFUL,
            FirstBlock = 0x1111_1111_1111_1111UL,
            LastBlock = 0x2222_2222_2222_2222UL,
            PreStateRoot = H('a'),
            PostStateRoot = H('b'),
            TxRoot = H('c'),
            ReceiptRoot = H('d'),
            WithdrawalRoot = H('e'),
            L2ToL1MessageRoot = H('f'),
            L2ToL2MessageRoot = H('1'),
            L1MessageHash = H('2'),
            DACommitment = H('3'),
            BlockContextHash = H('4'),
        };
        var bytes = BatchSerializer.EncodePublicInputs(inputs);

        Assert.AreEqual(352, bytes.Length);
        CollectionAssert.AreEqual(inputs.BlockContextHash.GetSpan().ToArray(), bytes[316..348]);
        Assert.AreEqual(inputs.ForcedInclusionCount, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(348, 4)));
        Assert.AreEqual(0xCAFEBABEu, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4)));
        Assert.AreEqual(0xDEAD_BEEFUL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(4, 8)));
        Assert.AreEqual(0x1111_1111_1111_1111UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(12, 8)));
        Assert.AreEqual(0x2222_2222_2222_2222UL, System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(20, 8)));
        CollectionAssert.AreEqual(inputs.PreStateRoot.GetSpan().ToArray(), bytes[28..60]);
        CollectionAssert.AreEqual(inputs.BlockContextHash.GetSpan().ToArray(), bytes[316..348]);
    }

    [TestMethod]
    public void Encode_RejectsOutOfRangeProofType()
    {
        // Regression for iter 187: BatchSerializer.Encode previously cast ProofType to
        // byte without bounds-checking. The Decode side rejects unknown bytes (iter 103),
        // so an out-of-range ProofType could produce bytes Decode would refuse — masking
        // the producer-side bug at the consumer. Now Encode/Decode are symmetric.
        var bad = Sample() with { ProofType = (ProofType)99 };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => BatchSerializer.Encode(bad));
        StringAssert.Contains(ex.Message, "ProofType");
        StringAssert.Contains(ex.Message, "99");
    }

    [TestMethod]
    [DataRow(0x1337BEEFu)]
    [DataRow(0xCAFED00Du)]
    [DataRow(0xDEADBEEFu)]
    [DataRow(0x00FF00FFu)]
    public void Commitment_Decode_NeverCrashes_OnFuzzedBytes(uint seed)
    {
        var rng = new Random((int)(seed ^ 0xBAAD_F00Du));
        for (var i = 0; i < 500; i++)
        {
            var len = rng.Next(0, 2048);
            var buf = new byte[len];
            rng.NextBytes(buf);
            try
            {
                _ = BatchSerializer.Decode(buf);
            }
            catch (ArgumentException) { /* expected on malformed wire */ }
            catch (System.IO.InvalidDataException) { /* expected on malformed wire */ }
            catch (Exception ex) when (
                ex is not OutOfMemoryException &&
                ex is not StackOverflowException &&
                ex is not OperationCanceledException)
            {
                Assert.Fail($"seed 0x{seed:X8} iter {i}: BatchSerializer.Decode threw " +
                    $"{ex.GetType().Name} on random {len}-byte input: {ex.Message}");
            }
        }
    }

    [TestMethod]
    [DataRow(0x55AA55AAu)]
    [DataRow(0xAA55AA55u)]
    public void PublicInputs_Decode_NeverCrashes_OnFuzzedBytes(uint seed)
    {
        var rng = new Random((int)(seed ^ 0x1234_9876u));
        for (var i = 0; i < 500; i++)
        {
            var len = rng.Next(0, 1024);
            var buf = new byte[len];
            rng.NextBytes(buf);
            try
            {
                _ = BatchSerializer.DecodePublicInputs(buf);
            }
            catch (ArgumentException) { /* expected on malformed wire */ }
            catch (System.IO.InvalidDataException) { /* expected on malformed wire */ }
            catch (Exception ex) when (
                ex is not OutOfMemoryException &&
                ex is not StackOverflowException &&
                ex is not OperationCanceledException)
            {
                Assert.Fail($"seed 0x{seed:X8} iter {i}: BatchSerializer.DecodePublicInputs threw " +
                    $"{ex.GetType().Name} on random {len}-byte input: {ex.Message}");
            }
        }
    }

    [TestMethod]
    [DataRow(0x99887766u)]
    [DataRow(0x55443322u)]
    public void Commitment_Truncation_Fuzz(uint seed)
    {
        var rng = new Random((int)(seed ^ 0x6677_8899u));
        for (var i = 0; i < 50; i++)
        {
            var proofLen = rng.Next(0, 256);
            var proof = new byte[proofLen];
            rng.NextBytes(proof);
            var firstBlock = (ulong)rng.Next(1, 1000);
            var lastBlock = firstBlock + (ulong)rng.Next(0, 100);

            var commitment = Sample(proof) with
            {
                ChainId = (uint)rng.Next(1, int.MaxValue),
                BatchNumber = (ulong)rng.Next(1, int.MaxValue),
                FirstBlock = firstBlock,
                LastBlock = lastBlock,
                ProofType = (ProofType)rng.Next(0, 3),
            };

            var encoded = BatchSerializer.Encode(commitment);
            var lop = rng.Next(1, encoded.Length);
            var truncated = encoded[..^lop];

            var caught = false;
            try
            {
                _ = BatchSerializer.Decode(truncated);
            }
            catch (ArgumentException) { caught = true; }
            catch (System.IO.InvalidDataException) { caught = true; }

            Assert.IsTrue(caught,
                $"seed 0x{seed:X8} iter {i}: truncated commitment ({lop}B removed) accepted without exception");
        }
    }

    [TestMethod]
    [DataRow(0x11223344u)]
    [DataRow(0x55667788u)]
    public void Commitment_RoundTrip_IsIdentity_AcrossFuzzedInputs(uint seed)
    {
        var rng = new Random((int)(seed ^ 0xA1B2_C3D4u));
        for (var i = 0; i < 100; i++)
        {
            var proofLen = rng.Next(0, 1024);
            var proof = new byte[proofLen];
            rng.NextBytes(proof);
            var firstBlock = (ulong)rng.Next(1, 10000);
            var lastBlock = firstBlock + (ulong)rng.Next(0, 500);

            var original = new L2BatchCommitment
            {
                ChainId = (uint)rng.Next(1, int.MaxValue),
                BatchNumber = (ulong)rng.Next(1, int.MaxValue),
                FirstBlock = firstBlock,
                LastBlock = lastBlock,
                PreStateRoot = RandomRoot(rng),
                PostStateRoot = RandomRoot(rng),
                TxRoot = RandomRoot(rng),
                ReceiptRoot = RandomRoot(rng),
                WithdrawalRoot = RandomRoot(rng),
                L2ToL1MessageRoot = RandomRoot(rng),
                L2ToL2MessageRoot = RandomRoot(rng),
                DACommitment = RandomRoot(rng),
                PublicInputHash = RandomRoot(rng),
                ProofType = (ProofType)rng.Next(0, 3),
                Proof = proof,
            };

            var encoded = BatchSerializer.Encode(original);
            var decoded = BatchSerializer.Decode(encoded);
            Assert.AreEqual(original, decoded);

            var reEncoded = BatchSerializer.Encode(decoded);
            CollectionAssert.AreEqual(encoded, reEncoded);
        }
    }

    private static UInt256 RandomRoot(Random rng)
    {
        var b = new byte[32];
        rng.NextBytes(b);
        return new UInt256(b);
    }

    // === PROPERTY-BASED FUZZ TESTS FOR PUBLICINPUTS ===

    [TestMethod]
    public void EncodePublicInputs_RandomInputs_ProducesValidWireFormat()
    {
        var rng = new Random(unchecked((int)0xDEADBEEFUL));
        
        for (int i = 0; i < 100; i++)
        {
            var inputs = GenerateRandomPublicInputs(rng);
            var encoded = BatchSerializer.EncodePublicInputs(inputs);
            Assert.AreEqual(BatchSerializer.PublicInputsSize, encoded.Length, $"Wrong size at iteration {i}");
            
            var decoded = BatchSerializer.DecodePublicInputs(encoded);
            
            Assert.AreEqual(inputs.ChainId, decoded.ChainId, $"ChainId mismatch at iteration {i}");
            Assert.AreEqual(inputs.BatchNumber, decoded.BatchNumber, $"BatchNumber mismatch at iteration {i}");
            Assert.AreEqual(inputs.FirstBlock, decoded.FirstBlock, $"FirstBlock mismatch at iteration {i}");
            Assert.AreEqual(inputs.LastBlock, decoded.LastBlock, $"LastBlock mismatch at iteration {i}");
            Assert.AreEqual(inputs.PreStateRoot, decoded.PreStateRoot, $"PreStateRoot mismatch at iteration {i}");
            Assert.AreEqual(inputs.PostStateRoot, decoded.PostStateRoot, $"PostStateRoot mismatch at iteration {i}");
            Assert.AreEqual(inputs.TxRoot, decoded.TxRoot, $"TxRoot mismatch at iteration {i}");
            Assert.AreEqual(inputs.ReceiptRoot, decoded.ReceiptRoot, $"ReceiptRoot mismatch at iteration {i}");
            Assert.AreEqual(inputs.WithdrawalRoot, decoded.WithdrawalRoot, $"WithdrawalRoot mismatch at iteration {i}");
            Assert.AreEqual(inputs.L2ToL1MessageRoot, decoded.L2ToL1MessageRoot, $"L2ToL1MessageRoot mismatch at iteration {i}");
            Assert.AreEqual(inputs.L2ToL2MessageRoot, decoded.L2ToL2MessageRoot, $"L2ToL2MessageRoot mismatch at iteration {i}");
            Assert.AreEqual(inputs.L1MessageHash, decoded.L1MessageHash, $"L1MessageHash mismatch at iteration {i}");
            Assert.AreEqual(inputs.DACommitment, decoded.DACommitment, $"DACommitment mismatch at iteration {i}");
            Assert.AreEqual(inputs.BlockContextHash, decoded.BlockContextHash, $"BlockContextHash mismatch at iteration {i}");
        }
    }

    [TestMethod]
    public void EncodePublicInputs_ZeroValues_Succeeds()
    {
        var inputs = new PublicInputs
        {
            ChainId = 0,
            BatchNumber = 0,
            FirstBlock = 0,
            LastBlock = 0,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            L1MessageHash = UInt256.Zero,
            DACommitment = UInt256.Zero,
            BlockContextHash = UInt256.Zero
        };

        var encoded = BatchSerializer.EncodePublicInputs(inputs);
        Assert.AreEqual(BatchSerializer.PublicInputsSize, encoded.Length);
        
        var decoded = BatchSerializer.DecodePublicInputs(encoded);
        Assert.AreEqual(inputs.ChainId, decoded.ChainId);
        Assert.AreEqual(inputs.BatchNumber, decoded.BatchNumber);
        Assert.AreEqual(inputs.FirstBlock, decoded.FirstBlock);
    }

    [TestMethod]
    public void EncodePublicInputs_MaxValues_WireFormatValid()
    {
        var inputs = new PublicInputs
        {
            ChainId = uint.MaxValue,
            BatchNumber = ulong.MaxValue,
            FirstBlock = ulong.MaxValue,
            LastBlock = ulong.MaxValue,
            PreStateRoot = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            PostStateRoot = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            TxRoot = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            ReceiptRoot = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            WithdrawalRoot = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            L2ToL1MessageRoot = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            L2ToL2MessageRoot = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            L1MessageHash = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            DACommitment = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            BlockContextHash = UInt256.Parse("0xFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF")
        };

        var encoded = BatchSerializer.EncodePublicInputs(inputs);
        Assert.AreEqual(BatchSerializer.PublicInputsSize, encoded.Length);
        
        var decoded = BatchSerializer.DecodePublicInputs(encoded);
        Assert.AreEqual(inputs.ChainId, decoded.ChainId);
        Assert.AreEqual(inputs.BatchNumber, decoded.BatchNumber);
    }

    [TestMethod]
    public void EncodePublicInputs_DeterministicOutput_IdenticalEncoding()
    {
        var rng = new Random(unchecked((int)0xCAFEBABEUL));
        
        for (int i = 0; i < 50; i++)
        {
            var a = GenerateRandomPublicInputs(rng);
            var b = GenerateRandomPublicInputs(rng);
            
            // Use identical inputs twice
            var encoded1 = BatchSerializer.EncodePublicInputs(a);
            var encoded2 = BatchSerializer.EncodePublicInputs(a);
            
            CollectionAssert.AreEqual(encoded1, encoded2, $"Non-deterministic encoding at iteration {i}");
        }
    }

    [TestMethod]
    public void Fuzz_PublicInputs_100Iterations_ComprehensivePropertyCheck()
    {
        var rng = new Random(unchecked((int)0x98765432UL));
        
        for (int i = 0; i < 100; i++)
        {
            var original = GenerateRandomPublicInputs(rng);
            var encoded = BatchSerializer.EncodePublicInputs(original);
            var decoded = BatchSerializer.DecodePublicInputs(encoded);
            
            // Verify all fields match after round-trip
            Assert.AreEqual(original.ChainId, decoded.ChainId, $"Iteration {i}: ChainId mismatch");
            Assert.AreEqual(original.BatchNumber, decoded.BatchNumber, $"Iteration {i}: BatchNumber mismatch");
            Assert.AreEqual(original.FirstBlock, decoded.FirstBlock, $"Iteration {i}: FirstBlock mismatch");
            Assert.AreEqual(original.LastBlock, decoded.LastBlock, $"Iteration {i}: LastBlock mismatch");
            Assert.AreEqual(original.PreStateRoot, decoded.PreStateRoot, $"Iteration {i}: PreStateRoot mismatch");
            Assert.AreEqual(original.PostStateRoot, decoded.PostStateRoot, $"Iteration {i}: PostStateRoot mismatch");
            Assert.AreEqual(original.TxRoot, decoded.TxRoot, $"Iteration {i}: TxRoot mismatch");
            Assert.AreEqual(original.ReceiptRoot, decoded.ReceiptRoot, $"Iteration {i}: ReceiptRoot mismatch");
            Assert.AreEqual(original.WithdrawalRoot, decoded.WithdrawalRoot, $"Iteration {i}: WithdrawalRoot mismatch");
            Assert.AreEqual(original.L2ToL1MessageRoot, decoded.L2ToL1MessageRoot, $"Iteration {i}: L2ToL1MessageRoot mismatch");
            Assert.AreEqual(original.L2ToL2MessageRoot, decoded.L2ToL2MessageRoot, $"Iteration {i}: L2ToL2MessageRoot mismatch");
            Assert.AreEqual(original.L1MessageHash, decoded.L1MessageHash, $"Iteration {i}: L1MessageHash mismatch");
            Assert.AreEqual(original.DACommitment, decoded.DACommitment, $"Iteration {i}: DACommitment mismatch");
            Assert.AreEqual(original.BlockContextHash, decoded.BlockContextHash, $"Iteration {i}: BlockContextHash mismatch");
        }
    }

    private static PublicInputs GenerateRandomPublicInputs(Random rng)
    {
        return new PublicInputs
        {
            ChainId = (uint)rng.Next(),
            BatchNumber = (ulong)rng.Next(1, int.MaxValue),
            FirstBlock = (ulong)rng.Next(1, 100000),
            LastBlock = (ulong)rng.Next(100000, 200000),
            PreStateRoot = RandomRoot(rng),
            PostStateRoot = RandomRoot(rng),
            TxRoot = RandomRoot(rng),
            ReceiptRoot = RandomRoot(rng),
            WithdrawalRoot = RandomRoot(rng),
            L2ToL1MessageRoot = RandomRoot(rng),
            L2ToL2MessageRoot = RandomRoot(rng),
            L1MessageHash = RandomRoot(rng),
            DACommitment = RandomRoot(rng),
            BlockContextHash = RandomRoot(rng)
        };
    }
}
