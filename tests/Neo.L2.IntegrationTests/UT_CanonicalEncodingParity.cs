using System.Globalization;
using System.Security.Cryptography;
using Neo.L2.Batch;
using Neo.L2.State;
using Neo.L2.TestInfra;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Audit finding <c>V2</c>, first two legs of the lock: the canonical encoders must produce
/// <see cref="CanonicalEncodingVectors"/>'s bytes, and the hex export the Rust lane reads must be
/// those same bytes.
/// </summary>
/// <remarks>
/// <para>
/// Each side of every pairing already had a self-pin, and a self-pin is blind to drift <em>across</em>
/// the pairing: <c>UT_BatchSerializer.Commitment_ByteLayout_MatchesDocumentedOffsets</c> checks the
/// encoder against the encoder's own documented offsets, so an edit that moves both the write and the
/// documentation stays green, and a round-trip test encodes and decodes with the <em>same</em>
/// constants, so moving a field moves both halves of its assertion. What no test did was feed one
/// side's bytes to the other side — in particular to a deployed contract, which parses these buffers
/// by hardcoded offset. <c>NeoHub.Contracts.VmTests</c>, the only suite that runs those contracts,
/// carries zero <c>ProjectReference</c>s and therefore cannot call these encoders at all; it rebuilds
/// the same buffers from its own copy of the offsets.
/// </para>
/// <para>
/// So this class compares the encoders against data neither implementation owns;
/// <c>UT_CanonicalEncodingParity_Vm</c> compares the hand-rolled builders and the compiled contracts
/// against the same data; and <see cref="SharedHexExport_MatchesTheVectors"/> pins the file
/// <c>bridge/neo-execution-core/tests/canonical_encoding_parity.rs</c> reads to it, which is what makes
/// that Rust test a check on the .NET encoders rather than on a snapshot. A field that moves in one
/// place and not the others fails at the exact byte offset.
/// </para>
/// </remarks>
[TestClass]
public class UT_CanonicalEncodingParity
{
    private static byte[] Hash256(byte[] x) => SHA256.HashData(SHA256.HashData(x));

    private static UInt256 Root(byte fill) => new(CanonicalEncodingVectors.Fill(fill));

    private static UInt160 Hash160(byte fill)
    {
        var bytes = new byte[20];
        Array.Fill(bytes, fill);
        return new UInt160(bytes);
    }

    private static UInt256[] ToUInt256s(IReadOnlyList<byte[]> hashes) =>
        [.. hashes.Select(b => new UInt256(b))];

    /// <summary>Compares byte-for-byte and names the first offset that disagrees.</summary>
    private static void AssertBytesEqual(string what, byte[] expected, byte[] actual)
    {
        Assert.AreEqual(expected.Length, actual.Length, $"{what}: length differs");
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.AreEqual(expected[i], actual[i],
                $"{what}: byte {i} is 0x{actual[i]:X2}, the vector says 0x{expected[i]:X2}");
        }
    }

    private static L2BatchCommitment GoldenCommitment() => new()
    {
        ChainId = CanonicalEncodingVectors.ChainId,
        BatchNumber = CanonicalEncodingVectors.Batch,
        FirstBlock = CanonicalEncodingVectors.FirstBlock,
        LastBlock = CanonicalEncodingVectors.LastBlock,
        PreStateRoot = Root(CanonicalEncodingVectors.FillPreStateRoot),
        PostStateRoot = Root(CanonicalEncodingVectors.FillPostStateRoot),
        TxRoot = Root(CanonicalEncodingVectors.FillTxRoot),
        ReceiptRoot = Root(CanonicalEncodingVectors.FillReceiptRoot),
        WithdrawalRoot = new UInt256(CanonicalEncodingVectors.WithdrawalRoot()),
        L2ToL1MessageRoot = Root(CanonicalEncodingVectors.FillL2ToL1MessageRoot),
        L2ToL2MessageRoot = Root(CanonicalEncodingVectors.FillL2ToL2MessageRoot),
        DACommitment = Root(CanonicalEncodingVectors.FillDaCommitment),
        PublicInputHash = new UInt256(Hash256(CanonicalEncodingVectors.PublicInputs())),
        ProofType = (ProofType)CanonicalEncodingVectors.ProofType,
        Proof = ReadOnlyMemory<byte>.Empty,
    };

    private static L2ChainConfig GoldenChainConfig() => new()
    {
        ChainId = CanonicalEncodingVectors.ChainId,
        OperatorManager = Hash160(0x11),
        Verifier = Hash160(0x22),
        BridgeAdapter = Hash160(0x33),
        MessageAdapter = Hash160(0x44),
        SecurityLevel = SecurityLevel.Sidechain,
        DAMode = DAMode.DAC,
        GatewayEnabled = true,
        PermissionlessExit = false,
        Sequencer = SequencerModel.DbftCommittee,
        Exit = ExitModel.OperatorAssisted,
        Active = true,
    };

    [TestMethod]
    public void BatchSerializer_Commitment_MatchesGoldenVector()
    {
        AssertBytesEqual(
            "BatchSerializer.Encode",
            CanonicalEncodingVectors.CommitmentHeader(),
            BatchSerializer.Encode(GoldenCommitment()));
    }

    [TestMethod]
    public void BatchSerializer_DecodeOfGoldenVector_KeepsEveryField()
    {
        // The encoder leg above proves "these fields produce these bytes"; this one proves the
        // reverse reading, which is what every off-chain consumer (batcher, prover, auditor,
        // relayer) uses when it pulls a commitment back off L1 calldata.
        var decoded = BatchSerializer.Decode(CanonicalEncodingVectors.CommitmentHeader());

        Assert.AreEqual(CanonicalEncodingVectors.ChainId, decoded.ChainId);
        Assert.AreEqual(CanonicalEncodingVectors.Batch, decoded.BatchNumber);
        Assert.AreEqual(CanonicalEncodingVectors.FirstBlock, decoded.FirstBlock);
        Assert.AreEqual(CanonicalEncodingVectors.LastBlock, decoded.LastBlock);
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillPreStateRoot), decoded.PreStateRoot);
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillPostStateRoot), decoded.PostStateRoot);
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillTxRoot), decoded.TxRoot);
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillReceiptRoot), decoded.ReceiptRoot);
        Assert.AreEqual(new UInt256(CanonicalEncodingVectors.WithdrawalRoot()), decoded.WithdrawalRoot);
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillL2ToL1MessageRoot), decoded.L2ToL1MessageRoot);
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillL2ToL2MessageRoot), decoded.L2ToL2MessageRoot);
        Assert.AreEqual(Root(CanonicalEncodingVectors.FillDaCommitment), decoded.DACommitment);
        Assert.AreEqual(new UInt256(Hash256(CanonicalEncodingVectors.PublicInputs())), decoded.PublicInputHash);
        Assert.AreEqual((ProofType)CanonicalEncodingVectors.ProofType, decoded.ProofType);
        Assert.AreEqual(0, decoded.Proof.Length);

        AssertBytesEqual("re-encode of decoded vector", CanonicalEncodingVectors.CommitmentHeader(),
            BatchSerializer.Encode(decoded));
    }

    [TestMethod]
    public void BatchSerializer_PublicInputs_MatchesGoldenVector()
    {
        var inputs = new PublicInputs
        {
            ChainId = CanonicalEncodingVectors.ChainId,
            BatchNumber = CanonicalEncodingVectors.Batch,
            FirstBlock = CanonicalEncodingVectors.FirstBlock,
            LastBlock = CanonicalEncodingVectors.LastBlock,
            PreStateRoot = Root(CanonicalEncodingVectors.FillPreStateRoot),
            PostStateRoot = Root(CanonicalEncodingVectors.FillPostStateRoot),
            TxRoot = Root(CanonicalEncodingVectors.FillTxRoot),
            ReceiptRoot = Root(CanonicalEncodingVectors.FillReceiptRoot),
            WithdrawalRoot = new UInt256(CanonicalEncodingVectors.WithdrawalRoot()),
            L2ToL1MessageRoot = Root(CanonicalEncodingVectors.FillL2ToL1MessageRoot),
            L2ToL2MessageRoot = Root(CanonicalEncodingVectors.FillL2ToL2MessageRoot),
            L1MessageHash = Root(CanonicalEncodingVectors.FillL1MessageHash),
            DACommitment = Root(CanonicalEncodingVectors.FillDaCommitment),
            BlockContextHash = Root(CanonicalEncodingVectors.FillBlockContextHash),
        };

        AssertBytesEqual(
            "BatchSerializer.EncodePublicInputs",
            CanonicalEncodingVectors.PublicInputs(),
            BatchSerializer.EncodePublicInputs(inputs));

        var decoded = BatchSerializer.DecodePublicInputs(CanonicalEncodingVectors.PublicInputs());
        Assert.AreEqual(inputs, decoded);
    }

    [TestMethod]
    public void CommitmentPublicInputHash_IsDigestOfPublicInputsVector()
    {
        // The two BatchSerializer layouts are bound to each other by this digest, and
        // SettlementManager.ComputePublicInputHash re-derives exactly it before accepting a batch —
        // so a drift in either layout, or in the field order of either, faults every submit.
        var header = CanonicalEncodingVectors.CommitmentHeader();
        AssertBytesEqual(
            "commitment.publicInputHash vs Hash256(publicInputs)",
            Hash256(CanonicalEncodingVectors.PublicInputs()),
            header[284..316]);
    }

    [TestMethod]
    public void L2ChainConfigSerializer_Encode_MatchesGoldenVector()
    {
        AssertBytesEqual(
            "L2ChainConfigSerializer.Encode",
            CanonicalEncodingVectors.ChainConfig(),
            L2ChainConfigSerializer.Encode(GoldenChainConfig()));
    }

    [TestMethod]
    public void L2ChainConfigSerializer_Decode_RoundTripsGoldenVector()
    {
        var decoded = L2ChainConfigSerializer.Decode(CanonicalEncodingVectors.ChainConfig());

        Assert.AreEqual(GoldenChainConfig(), decoded);
        AssertBytesEqual("re-encode of decoded vector", CanonicalEncodingVectors.ChainConfig(),
            L2ChainConfigSerializer.Encode(decoded));
    }

    [TestMethod]
    public void L2ChainConfigSerializer_EachSingleByteField_WritesOnlyItsOwnOffset()
    {
        // The four UInt160 slots and the chainId are distinct values, so the vector alone catches a
        // swap between any two of them. The seven trailing bytes are bools and small enums: they
        // cannot all be pairwise distinct, so a vector comparison cannot see an encoder that writes
        // SecurityLevel into offset 86 and GatewayEnabled into 84. Varying one model field at a time
        // does see it — exactly one byte may move, and it must be that field's own byte.
        var baseline = GoldenChainConfig();
        var expected = CanonicalEncodingVectors.ChainConfig();
        var cases = new (string Field, int Offset, L2ChainConfig Mutated)[]
        {
            (nameof(L2ChainConfig.SecurityLevel), 84, baseline with { SecurityLevel = SecurityLevel.Settled }),
            (nameof(L2ChainConfig.DAMode), 85, baseline with { DAMode = DAMode.NeoFS }),
            (nameof(L2ChainConfig.GatewayEnabled), 86, baseline with { GatewayEnabled = false }),
            (nameof(L2ChainConfig.PermissionlessExit), 87, baseline with { PermissionlessExit = true }),
            (nameof(L2ChainConfig.Sequencer), 88, baseline with { Sequencer = SequencerModel.Decentralized }),
            (nameof(L2ChainConfig.Exit), 89, baseline with { Exit = ExitModel.Permissionless }),
            (nameof(L2ChainConfig.Active), 90, baseline with { Active = false }),
        };

        foreach (var (field, offset, mutated) in cases)
        {
            var actual = L2ChainConfigSerializer.Encode(mutated);
            Assert.AreEqual(expected.Length, actual.Length, $"{field}: the mutation changed the length");

            for (var i = 0; i < expected.Length; i++)
            {
                if (i == offset)
                {
                    Assert.AreNotEqual(expected[i], actual[i],
                        $"{field} was mutated but byte {offset} still holds the baseline value");
                }
                else
                {
                    Assert.AreEqual(expected[i], actual[i],
                        $"{field} mutated byte {i}, which is not its own offset");
                }
            }
        }
    }

    [TestMethod]
    public void MerkleTree_WithdrawalFixture_MatchesGoldenVector()
    {
        var tree = new MerkleTree(ToUInt256s(CanonicalEncodingVectors.WithdrawalLeaves()));

        Assert.AreEqual(CanonicalEncodingVectors.WithdrawalLeafCount, tree.LeafCount);
        Assert.AreEqual(3, tree.Depth, "5 leaves promote 5 -> 3 -> 2 -> 1");
        Assert.AreEqual(new UInt256(CanonicalEncodingVectors.WithdrawalRoot()), tree.Root);

        // The vector's siblings are stated in the on-chain convention (per-level sibling, leaf level
        // first, position driven by the leaf index bits). MerkleTree.GetProof walks the same tree
        // through its own path bitmap, so agreement here is what makes the vectors safe to hand to
        // VerifyWithdrawalLeafWithProof in the VM leg.
        for (var i = 0; i < tree.LeafCount; i++)
        {
            var proof = tree.GetProof(i);
            var golden = CanonicalEncodingVectors.WithdrawalSiblings(i);

            Assert.AreEqual(golden.Count, proof.Siblings.Count, $"leaf {i}: sibling count");
            for (var level = 0; level < golden.Count; level++)
            {
                Assert.AreEqual(new UInt256(golden[level]), proof.Siblings[level],
                    $"leaf {i}: sibling at level {level}");
            }

            Assert.IsTrue(proof.Verify(tree.Root), $"leaf {i} must verify against the golden root");
        }
    }

    [TestMethod]
    public void MerkleProofSerializer_GoldenFraming_RoundTrips()
    {
        var framing = CanonicalEncodingVectors.WithdrawalProofFraming();
        var tree = new MerkleTree(ToUInt256s(CanonicalEncodingVectors.WithdrawalLeaves()));

        AssertBytesEqual("MerkleProofSerializer.Encode", framing,
            MerkleProofSerializer.Encode(tree.GetProof(CanonicalEncodingVectors.WithdrawalProofLeafIndex)));

        var decoded = MerkleProofSerializer.Decode(framing);
        Assert.AreEqual(CanonicalEncodingVectors.WithdrawalProofLeafIndex, decoded.LeafIndex);
        var golden = CanonicalEncodingVectors.WithdrawalSiblings(CanonicalEncodingVectors.WithdrawalProofLeafIndex);
        for (var level = 0; level < golden.Count; level++)
        {
            Assert.AreEqual(new UInt256(golden[level]), decoded.Siblings[level],
                $"sibling at level {level}");
        }
        // Bit 2 set, bits 0 and 1 clear: leaf 4's level-0 and level-1 siblings join on the right (the
        // level-1 one is the leaf's own duplicate), and only the level-2 sibling is a left child.
        Assert.AreEqual(0b100UL, decoded.PathBitmap);
        Assert.IsTrue(decoded.Verify(new UInt256(CanonicalEncodingVectors.WithdrawalRoot())));
    }

    /// <summary>
    /// The Rust lane cannot reference a .NET project, so it reads
    /// <c>tests/Shared/canonical_encoding_vectors.hex</c> through <c>include_str!</c>. That file is
    /// only evidence about <see cref="CanonicalEncodingVectors"/> while the two agree field for
    /// field, so this pins the export: every value, and every key in both directions.
    /// </summary>
    [TestMethod]
    public void SharedHexExport_MatchesTheVectors()
    {
        var path = Path.Combine(RepoRoot.Directory, "tests", "Shared", "canonical_encoding_vectors.hex");
        var export = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;
            var separator = trimmed.IndexOf('=');
            Assert.IsTrue(separator > 0, $"malformed export line: {line}");
            export[trimmed[..separator]] = trimmed[(separator + 1)..].Trim();
        }

        var read = new HashSet<string>(StringComparer.Ordinal);
        string Field(string key)
        {
            Assert.IsTrue(export.TryGetValue(key, out var value), $"the export has no {key} field");
            read.Add(key);
            return value;
        }

        byte[] Bytes(string key) => Convert.FromHexString(Field(key));
        long Number(string key) => Convert.ToInt64(Field(key), CultureInfo.InvariantCulture);

        AssertBytesEqual("export.commitment", CanonicalEncodingVectors.CommitmentHeader(),
            Bytes("commitment"));
        AssertBytesEqual("export.public_inputs", CanonicalEncodingVectors.PublicInputs(),
            Bytes("public_inputs"));
        AssertBytesEqual("export.withdrawal_root", CanonicalEncodingVectors.WithdrawalRoot(),
            Bytes("withdrawal_root"));
        AssertBytesEqual("export.withdrawal_proof", CanonicalEncodingVectors.WithdrawalProofFraming(),
            Bytes("withdrawal_proof"));
        AssertBytesEqual("export.public_input_hash", Hash256(CanonicalEncodingVectors.PublicInputs()),
            Bytes("public_input_hash"));

        var fills = new (string Key, byte Fill)[]
        {
            ("pre_state_root", CanonicalEncodingVectors.FillPreStateRoot),
            ("post_state_root", CanonicalEncodingVectors.FillPostStateRoot),
            ("tx_root", CanonicalEncodingVectors.FillTxRoot),
            ("receipt_root", CanonicalEncodingVectors.FillReceiptRoot),
            ("l2_to_l1_message_root", CanonicalEncodingVectors.FillL2ToL1MessageRoot),
            ("l2_to_l2_message_root", CanonicalEncodingVectors.FillL2ToL2MessageRoot),
            ("l1_message_hash", CanonicalEncodingVectors.FillL1MessageHash),
            ("da_commitment", CanonicalEncodingVectors.FillDaCommitment),
            ("block_context_hash", CanonicalEncodingVectors.FillBlockContextHash),
        };
        foreach (var (key, fill) in fills)
        {
            AssertBytesEqual($"export.{key}", CanonicalEncodingVectors.Fill(fill), Bytes(key));
        }

        var leaves = CanonicalEncodingVectors.WithdrawalLeaves();
        Assert.AreEqual((long)leaves.Count, Number("withdrawal_leaf_count"), "export.withdrawal_leaf_count");
        for (var i = 0; i < leaves.Count; i++)
        {
            AssertBytesEqual($"export.withdrawal_leaf_{i}", leaves[i], Bytes($"withdrawal_leaf_{i}"));
        }

        Assert.AreEqual((long)CanonicalEncodingVectors.ChainId, Number("chain_id"), "export.chain_id");
        Assert.AreEqual((long)CanonicalEncodingVectors.Batch, Number("batch_number"), "export.batch_number");
        Assert.AreEqual((long)CanonicalEncodingVectors.FirstBlock, Number("first_block"), "export.first_block");
        Assert.AreEqual((long)CanonicalEncodingVectors.LastBlock, Number("last_block"), "export.last_block");
        Assert.AreEqual((long)CanonicalEncodingVectors.ProofType, Number("proof_type"), "export.proof_type");
        Assert.AreEqual((long)CanonicalEncodingVectors.WithdrawalProofLeafIndex,
            Number("withdrawal_proof_leaf_index"), "export.withdrawal_proof_leaf_index");

        // The other direction: a field added to the export that neither lane asserts is a field
        // nobody checks, which is how a "shared" vector starts lying about one language.
        var unread = export.Keys.Except(read, StringComparer.Ordinal).ToArray();
        Assert.AreEqual(0, unread.Length,
            $"the export declares fields no assertion reads: {string.Join(", ", unread)}");
    }

    [TestMethod]
    public void Vectors_HaveTheSizesTheEncodersDeclare()
    {
        // If a size constant moves on either side, this fails before the byte comparisons do, and
        // says which pairing broke rather than reporting a mismatch at byte 0.
        Assert.AreEqual(BatchSerializer.CommitmentFixedSize, CanonicalEncodingVectors.CommitmentHeader().Length);
        Assert.AreEqual(BatchSerializer.PublicInputsSize, CanonicalEncodingVectors.PublicInputs().Length);
        Assert.AreEqual(L2ChainConfigSerializer.ConfigSize, CanonicalEncodingVectors.ChainConfig().Length);
        Assert.AreEqual(
            MerkleProofSerializer.HeaderSize + 32 * CanonicalEncodingVectors.WithdrawalSiblings(0).Count,
            CanonicalEncodingVectors.WithdrawalProofFraming().Length);
    }

    [TestMethod]
    public void Vectors_GiveEveryHashFieldADistinctValue()
    {
        // The fixture's drift detection rests on no two fields sharing a value: a fill reused twice
        // turns a swapped buffer back into a passing one. Guarded here so a future edit to
        // CanonicalEncodingVectors cannot quietly drop that property.
        var fills = new[]
        {
            CanonicalEncodingVectors.FillPreStateRoot,
            CanonicalEncodingVectors.FillPostStateRoot,
            CanonicalEncodingVectors.FillTxRoot,
            CanonicalEncodingVectors.FillReceiptRoot,
            CanonicalEncodingVectors.FillL2ToL1MessageRoot,
            CanonicalEncodingVectors.FillL2ToL2MessageRoot,
            CanonicalEncodingVectors.FillDaCommitment,
            CanonicalEncodingVectors.FillL1MessageHash,
            CanonicalEncodingVectors.FillBlockContextHash,
        };
        Assert.AreEqual(fills.Length, fills.Distinct().Count(), "two 32-byte fields share a fill byte");

        var leafFills = Enumerable.Range(0, CanonicalEncodingVectors.WithdrawalLeafCount)
            .Select(i => CanonicalEncodingVectors.WithdrawalLeaves()[i][0])
            .ToArray();
        Assert.AreEqual(leafFills.Length, leafFills.Distinct().Count(), "two withdrawal leaves share a fill");
        foreach (var leafFill in leafFills)
        {
            CollectionAssert.DoesNotContain(fills, leafFill,
                "a withdrawal leaf reuses a commitment root fill");
        }
    }
}
