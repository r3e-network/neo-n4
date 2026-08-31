using System;
using System.Collections.Generic;

namespace Neo.L2.TestInfra;

/// <summary>
/// Golden byte vectors for the four canonical encodings that cross a boundary, expressed as data that
/// neither implementation owns.
/// </summary>
/// <remarks>
/// <para>
/// Audit finding <c>V2</c>: <c>NeoHub.Contracts.VmTests</c> carries <em>zero</em>
/// <c>ProjectReference</c>s (a reference to <c>Neo.L2.Batch</c> would resolve
/// <c>$(NeoCorePath)\Neo\Neo.csproj</c> next to <c>Neo.SmartContract.Testing</c>'s own copy of
/// <c>Neo</c>), so the VM tests cannot call <c>BatchSerializer</c> or <c>L2ChainConfigSerializer</c>.
/// They hand-roll the same buffers from their own offset table instead. Each side of a pairing was
/// already pinned — but only against itself: <c>UT_BatchSerializer</c> checks the encoders against
/// the encoders' own documented offsets, and the contract's offsets were exercised only through
/// hand-rolled buffers that no test compared to encoder output. No test fed an encoder's bytes to a
/// deployed contract. This file is the third reference that makes them agree by construction: the
/// encoder must produce these bytes, the hand-rolled builder must produce these bytes, and the
/// deployed NEF must read these bytes back as the values they encode.
/// </para>
/// <para>
/// <c>tests/Shared/canonical_encoding_vectors.hex</c> exports the same fields for the Rust lane
/// (<c>bridge/neo-execution-core/tests/canonical_encoding_parity.rs</c>), which is the only way those
/// tests can reach a .NET byte layout: the crate has no reference to any .NET project. The export is
/// pinned equal to this file by <c>SharedHexExport_MatchesTheVectors</c>.
/// </para>
/// <para>
/// <b>Every 32-byte field carries a distinct value</b>, so a swapped or skipped field changes the
/// vector rather than leaving it intact. The commitment's <c>withdrawalRoot</c> is the Merkle root of
/// <see cref="WithdrawalLeaves"/>, which binds the header layout to the inclusion-proof fold in
/// <c>SettlementManager.VerifyWithdrawalLeafWithProof</c>; its <c>publicInputHash</c> is
/// <c>Hash256</c> over <see cref="PublicInputs"/>, which binds the two <c>BatchSerializer</c> layouts
/// to each other.
/// </para>
/// <para>
/// <b>Provenance, and its limit.</b> These bytes were produced by a throwaway third implementation in
/// a language neither side uses, not by running the C# encoders, so they are not a snapshot of the
/// code under test. That buys real drift detection and costs nothing else only while the vectors are
/// read as the spec: if a deliberate format change is ever coordinated across every consumer, the
/// vectors are updated in this file alone, and that commit is where a reviewer should look hardest.
/// </para>
/// </remarks>
internal static class CanonicalEncodingVectors
{
    /// <summary>Chain the commitment and config vectors both describe (matches the VM tests' chain id).</summary>
    public const uint ChainId = 1001;

    /// <summary>Batch number of <see cref="CommitmentHeader"/>.</summary>
    public const ulong Batch = 1;

    /// <summary>
    /// First and last block of <see cref="CommitmentHeader"/>. Deliberately <em>different</em> from
    /// <see cref="Batch"/>: every test that hand-rolls a header sets all three to the same number, so
    /// a builder that wrote <c>batchNumber</c> three times would still match those tests and still
    /// match a vector built the same way.
    /// </summary>
    public const ulong FirstBlock = 2;

    /// <summary>Last block of <see cref="CommitmentHeader"/>; see <see cref="FirstBlock"/>.</summary>
    public const ulong LastBlock = 3;

    /// <summary><c>ProofType.Multisig</c> — the discriminant <see cref="CommitmentHeader"/> carries at offset 316.</summary>
    public const byte ProofType = 1;

    // Fill bytes for each 32-byte field of CommitmentHeader / PublicInputs. Distinct on purpose.
    public const byte FillPreStateRoot = 0x10;
    public const byte FillPostStateRoot = 0xA1;
    public const byte FillTxRoot = 0x03;
    public const byte FillReceiptRoot = 0x04;
    public const byte FillL2ToL1MessageRoot = 0x06;
    public const byte FillL2ToL2MessageRoot = 0x07;
    public const byte FillDaCommitment = 0x09;
    public const byte FillL1MessageHash = 0xB1;
    public const byte FillBlockContextHash = 0xC2;

    /// <summary>A 32-byte hash whose every byte is <paramref name="fill"/> — the VM tests' <c>R(fill)</c> convention.</summary>
    public static byte[] Fill(byte fill)
    {
        var bytes = new byte[32];
        Array.Fill(bytes, fill);
        return bytes;
    }

    /// <summary>
    /// The 321 fixed bytes <c>Neo.L2.Batch.BatchSerializer.Encode</c> emits for
    /// (<see cref="ChainId"/>, <see cref="Batch"/>, <see cref="FirstBlock"/>, <see cref="LastBlock"/>),
    /// the <c>Fill*</c> roots below, <see cref="ProofType"/> and a zero-length proof — i.e. exactly the
    /// <c>commitmentBytes</c> argument <c>NeoHub.SettlementManager.submitBatch</c> parses.
    /// </summary>
    public static byte[] CommitmentHeader() => FromHex(
        "e9030000" + //   0  chainId           = 1001
        "0100000000000000" + //   4  batchNumber       = 1
        "0200000000000000" + //  12  firstBlock        = 2
        "0300000000000000" + //  20  lastBlock         = 3
        "1010101010101010101010101010101010101010101010101010101010101010" + //  28  preStateRoot
        "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1" + //  60  postStateRoot
        "0303030303030303030303030303030303030303030303030303030303030303" + //  92  txRoot
        "0404040404040404040404040404040404040404040404040404040404040404" + // 124  receiptRoot
        WithdrawalRootHex + //                     156  withdrawalRoot  (Merkle root of WithdrawalLeaves)
        "0606060606060606060606060606060606060606060606060606060606060606" + // 188  l2ToL1MessageRoot
        "0707070707070707070707070707070707070707070707070707070707070707" + // 220  l2ToL2MessageRoot
        "0909090909090909090909090909090909090909090909090909090909090909" + // 252  daCommitment
        "a56a616d15b7b5b4f7a2abf997f94be264c1bad1095a3b97992ff7e6af62e4e3" + // 284  publicInputHash
        "01" + //                               316  proofType         = Multisig
        "00000000"); //                          317  proofLen          = 0

    /// <summary>
    /// The 348 bytes <c>BatchSerializer.EncodePublicInputs</c> emits for the same batch. Never
    /// transmitted to L1 — the contract sees only its digest at commitment offset 284 — but it is the
    /// preimage the attestation is signed over, the digest in every durable witness artifact, and the
    /// buffer the Rust side rebuilds byte-for-byte.
    /// </summary>
    public static byte[] PublicInputs() => FromHex(
        "e9030000" + //   0  chainId           = 1001
        "0100000000000000" + //   4  batchNumber       = 1
        "0200000000000000" + //  12  firstBlock        = 2
        "0300000000000000" + //  20  lastBlock         = 3
        "1010101010101010101010101010101010101010101010101010101010101010" + //  28  preStateRoot
        "a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1a1" + //  60  postStateRoot
        "0303030303030303030303030303030303030303030303030303030303030303" + //  92  txRoot
        "0404040404040404040404040404040404040404040404040404040404040404" + // 124  receiptRoot
        WithdrawalRootHex + //                     156  withdrawalRoot  (Merkle root of WithdrawalLeaves)
        "0606060606060606060606060606060606060606060606060606060606060606" + // 188  l2ToL1MessageRoot
        "0707070707070707070707070707070707070707070707070707070707070707" + // 220  l2ToL2MessageRoot
        "b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1" + // 252  l1MessageHash
        "0909090909090909090909090909090909090909090909090909090909090909" + // 284  daCommitment
        "c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2c2"); // 316  blockContextHash

    /// <summary>
    /// The 91 bytes <c>L2ChainConfigSerializer.Encode</c> emits for the config
    /// <c>NeoHub.ChainRegistry.registerChain</c> consumes: chain <see cref="ChainId"/> (the contract
    /// asserts the embedded id equals its argument), four 20-byte hash fills, then
    /// SecurityLevel.Sidechain + DAMode.DAC, gateway on, permissionless exit off,
    /// SequencerModel.DbftCommittee + ExitModel.OperatorAssisted, active.
    /// <para>
    /// SecurityLevel is <c>Sidechain</c> rather than the <c>Optimistic</c> a doc example would pick,
    /// because this vector is fed to the same chain that <see cref="CommitmentHeader"/> is submitted
    /// to: <c>SettlementManager.IsProofTypeCompatible</c> accepts <c>Multisig</c> only under
    /// Sidechain or Settled, so an Optimistic label here would make the end-to-end VM journey fault.
    /// No two adjacent single-byte fields share a value, so a field that is dropped or written twice
    /// shifts a visible value rather than leaving the tail intact.
    /// </para>
    /// </summary>
    public static byte[] ChainConfig() => FromHex(
        "e9030000" + //   0  chainId            = 1001
        "1111111111111111111111111111111111111111" + //   4  operatorManager
        "2222222222222222222222222222222222222222" + //  24  verifier
        "3333333333333333333333333333333333333333" + //  44  bridgeAdapter
        "4444444444444444444444444444444444444444" + //  64  messageAdapter
        "00" + //                                 84  securityLevel     = Sidechain
        "03" + //                                 85  daMode            = DAC
        "01" + //                                 86  gatewayEnabled    = true
        "00" + //                                 87  permissionlessExit = false
        "01" + //                                 88  sequencerModel    = DbftCommittee
        "02" + //                                 89  exitModel         = OperatorAssisted
        "01"); //                                 90  active            = true

    /// <summary>Security level byte <see cref="ChainConfig"/> encodes at offset 84.</summary>
    public const byte ChainConfigSecurityLevel = 0;

    /// <summary>DA mode byte <see cref="ChainConfig"/> encodes at offset 85.</summary>
    public const byte ChainConfigDAMode = 3;

    /// <summary>
    /// Five withdrawal leaves — a deliberately odd count, so the fold exercises the duplication path
    /// at two levels. <c>withdrawalRoot</c> in both encodings above is the Merkle root of this list.
    /// </summary>
    public static IReadOnlyList<byte[]> WithdrawalLeaves() =>
    [
        Fill(0xD0), Fill(0xD1), Fill(0xD2), Fill(0xD3), Fill(0xD4),
    ];

    /// <summary>Merkle root of <see cref="WithdrawalLeaves"/> under <c>Hash256(left || right)</c>.</summary>
    public static byte[] WithdrawalRoot() => FromHex(WithdrawalRootHex);

    /// <summary>
    /// Per-level sibling hashes for leaf <paramref name="leafIndex"/>, leaf level first. The
    /// on-chain verifier pairs them with <c>leafIndex</c>'s bits, so a level-<c>i</c> sibling joins on
    /// the right when bit <c>i</c> is clear and on the left when it is set.
    /// </summary>
    public static IReadOnlyList<byte[]> WithdrawalSiblings(int leafIndex) => leafIndex switch
    {
        0 => Siblings(0xD1, "17271494ebe080ab205c32e4774a41de158b819a0e4e62134f6cb4caead58580",
            "5a848a013a894eeb760049804e3526d05f1cc6c1ab4fe4948c1661b5e791787c"),
        1 => Siblings(0xD0, "17271494ebe080ab205c32e4774a41de158b819a0e4e62134f6cb4caead58580",
            "5a848a013a894eeb760049804e3526d05f1cc6c1ab4fe4948c1661b5e791787c"),
        2 => Siblings(0xD3, "e95fa19b27c4313ec1d7a4194c99337313f85a053ac6b49b8eca5b32e8c01ca4",
            "5a848a013a894eeb760049804e3526d05f1cc6c1ab4fe4948c1661b5e791787c"),
        3 => Siblings(0xD2, "e95fa19b27c4313ec1d7a4194c99337313f85a053ac6b49b8eca5b32e8c01ca4",
            "5a848a013a894eeb760049804e3526d05f1cc6c1ab4fe4948c1661b5e791787c"),
        // The odd last leaf is paired with itself at level 0 and promotes twice.
        4 => Siblings(0xD4, "e5387d434339965c1a0cd9749644cdc5668e7476e530a7e2bd4ac785312409ed",
            "4fadf5defc6a0768919c825f3e6c0e818fc4472e377f42838f72cf548a3c1720"),
        _ => throw new ArgumentOutOfRangeException(nameof(leafIndex)),
    };

    /// <summary>Leaf index whose proof <see cref="WithdrawalProofFraming"/> encodes.</summary>
    public const int WithdrawalProofLeafIndex = 4;

    /// <summary>
    /// The 144 bytes <c>MerkleProofSerializer.Encode</c> emits for leaf
    /// <see cref="WithdrawalProofLeafIndex"/>: a 48-byte header plus three siblings in leaf-to-root
    /// order. Chosen leaf because it is the odd last leaf, so its level-0 sibling is itself and the
    /// bitmap has exactly one bit set.
    /// </summary>
    public static byte[] WithdrawalProofFraming() => FromHex(
        "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4" + //   0  leaf
        "04000000" + //                        32  leafIndex     = 4
        "0400000000000000" + //                36  pathBitmap    = sibling is left at level 2
        "03000000" + //                        44  siblingCount  = 3
        "d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4d4" + //  48  sibling 0 (itself)
        "e5387d434339965c1a0cd9749644cdc5668e7476e530a7e2bd4ac785312409ed" + //  80  sibling 1
        "4fadf5defc6a0768919c825f3e6c0e818fc4472e377f42838f72cf548a3c1720"); // 112  sibling 2

    /// <summary>Number of leaves in the withdrawal fixture.</summary>
    public const int WithdrawalLeafCount = 5;

    private const string WithdrawalRootHex =
        "cd6983836343f9205e1d9c2c90fd891e827ee366762018cd13c31920bde7469e";

    private static IReadOnlyList<byte[]> Siblings(byte level0Fill, string level1, string level2) =>
    [
        Fill(level0Fill), FromHex(level1), FromHex(level2),
    ];

    private static byte[] FromHex(string hex) => Convert.FromHexString(hex);
}
