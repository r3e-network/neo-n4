using System;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.DAValidator — the L1 data-availability validator that gates batch
/// finalization for validium-style chains. These execute the deploy / committee-registration /
/// attestation / validate paths in a real NeoVM with genuine secp256r1 (P-256) signatures, and pin
/// the security-critical on-chain invariants:
///   * owner-gating of governance (SetOwner) and committee registration (positive AND negative),
///   * committee blob structural validation (33-byte keys, size/threshold bounds),
///   * off-L1 DA modes require an M-of-N committee attestation whose signatures actually verify,
///   * replay protection: a batch attestation can only be persisted once,
///   * attestation rejection on sub-threshold sig count, duplicate signers, out-of-range signer
///     index, and forged/invalid signatures,
///   * the documented ModeL1 trust note: L1 mode passes on any non-zero commitment without proof,
///     while off-L1 modes refuse to validate without a stored attestation.
/// </summary>
[TestClass]
public class UT_DAValidator_Vm
{
    private const uint ChainA = 1001;
    private const byte ModeL1 = 0;
    private const byte ModeNeoFS = 1;
    private const byte ModeExternal = 2;
    private const byte ModeDAC = 3;

    private static readonly UInt160 DARegistryHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt256 Commitment = UInt256.Parse("0x" + new string('1', 64));
    private static readonly UInt256 OtherCommitment = UInt256.Parse("0x" + new string('2', 64));

    /// <summary>Deploy the validator. owner defaults to engine.Sender so owner-gated witness checks
    /// pass; pass an explicit <paramref name="owner"/> to exercise the negative authorization paths.</summary>
    private static NeoHubDAValidator Deploy(TestEngine engine, UInt160? owner = null, UInt160? daRegistry = null)
    {
        var o = owner ?? engine.Sender;
        var dar = daRegistry ?? DARegistryHash;
        return engine.Deploy<NeoHubDAValidator>(
            NeoHubDAValidator.Nef, NeoHubDAValidator.Manifest, new object[] { o, dar });
    }

    // ----- secp256r1 committee-member key material + signing helpers -------------------------------

    /// <summary>One committee signer: a P-256 key pair with the 33-byte compressed public key the
    /// contract stores, and the ability to produce 64-byte IEEE-P1363 (r||s) signatures that
    /// CryptoLib.VerifyWithECDsa(..., secp256r1SHA256) accepts.</summary>
    private sealed class Signer
    {
        private readonly ECDsa _key;
        public byte[] CompressedPubKey { get; }

        public Signer(int seed)
        {
            // Deterministic-ish key per seed: generate then keep. (Generation is fine for tests;
            // we only need a valid P-256 pair whose signatures verify on-chain.)
            _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var p = _key.ExportParameters(false);
            CompressedPubKey = Compress(p.Q.X!, p.Q.Y!);
        }

        public byte[] Sign(byte[] message) =>
            // secp256r1SHA256 => SHA256 the message then ECDSA; IEEE-P1363 yields raw 64-byte r||s.
            _key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        private static byte[] Compress(byte[] x32, byte[] y32)
        {
            var c = new byte[33];
            c[0] = (byte)(((y32[31] & 1) == 0) ? 0x02 : 0x03);
            Array.Copy(x32, 0, c, 1, 32);
            return c;
        }
    }

    /// <summary>Mirror DAValidatorContract.BuildAttestationMessage:
    /// "N4DA" || chainId(4 LE) || batchNumber(8 LE) || commitment(32) || daMode(1).</summary>
    private static byte[] BuildMessage(uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
    {
        var bytes = new byte[4 + 4 + 8 + 32 + 1];
        var pos = 0;
        bytes[pos++] = 0x4E; bytes[pos++] = 0x34; bytes[pos++] = 0x44; bytes[pos++] = 0x41; // N4DA
        bytes[pos++] = (byte)chainId;
        bytes[pos++] = (byte)(chainId >> 8);
        bytes[pos++] = (byte)(chainId >> 16);
        bytes[pos++] = (byte)(chainId >> 24);
        bytes[pos++] = (byte)batchNumber;
        bytes[pos++] = (byte)(batchNumber >> 8);
        bytes[pos++] = (byte)(batchNumber >> 16);
        bytes[pos++] = (byte)(batchNumber >> 24);
        bytes[pos++] = (byte)(batchNumber >> 32);
        bytes[pos++] = (byte)(batchNumber >> 40);
        bytes[pos++] = (byte)(batchNumber >> 48);
        bytes[pos++] = (byte)(batchNumber >> 56);
        var cb = commitment.GetSpan();
        for (var i = 0; i < 32; i++) bytes[pos + i] = cb[i];
        pos += 32;
        bytes[pos] = daMode;
        return bytes;
    }

    /// <summary>Concatenate ordered 33-byte compressed pubkeys into the committee blob.</summary>
    private static byte[] CommitteeBlob(params Signer[] signers)
    {
        var blob = new byte[33 * signers.Length];
        for (var i = 0; i < signers.Length; i++)
            Array.Copy(signers[i].CompressedPubKey, 0, blob, i * 33, 33);
        return blob;
    }

    /// <summary>Build a proof: [2B sigCount LE] + sigCount * ([1B signerIndex] [64B signature]).</summary>
    private static byte[] BuildProof((byte index, byte[] sig)[] entries)
    {
        var proof = new byte[2 + entries.Length * (1 + 64)];
        proof[0] = (byte)(entries.Length & 0xFF);
        proof[1] = (byte)((entries.Length >> 8) & 0xFF);
        var pos = 2;
        foreach (var (index, sig) in entries)
        {
            proof[pos++] = index;
            Array.Copy(sig, 0, proof, pos, 64);
            pos += 64;
        }
        return proof;
    }

    // ----- deploy / governance --------------------------------------------------------------------

    [TestMethod]
    public void Deploy_StoresOwnerAndDARegistry()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.AreEqual(engine.Sender, c.Owner, "deploy must persist the owner");
        Assert.AreEqual(DARegistryHash, c.DARegistry, "deploy must persist the DA registry wiring");
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwnerAndZeroDARegistry()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubDAValidator>(
            NeoHubDAValidator.Nef, NeoHubDAValidator.Manifest, new object[] { UInt160.Zero, DARegistryHash }),
            "zero owner must be rejected at deploy");

        var engine2 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => engine2.Deploy<NeoHubDAValidator>(
            NeoHubDAValidator.Nef, NeoHubDAValidator.Manifest, new object[] { engine2.Sender, UInt160.Zero }),
            "zero DA registry must be rejected at deploy");
    }

    [TestMethod]
    public void SetOwner_OwnerGated_TransfersOwnership()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender, witnessed
        var newOwner = UInt160.Parse("0x" + new string('7', 40));

        c.Owner = newOwner;
        Assert.AreEqual(newOwner, c.Owner, "owner must be updated after authorized setOwner");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner gate must reject.
        var c = Deploy(engine, owner: UInt160.Parse("0x" + new string('9', 40)));

        Assert.ThrowsExactly<TestException>(() => c.Owner = UInt160.Parse("0x" + new string('7', 40)),
            "setOwner is owner-gated");
    }

    [TestMethod]
    public void SetOwner_RejectsZeroNewOwner()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.Owner = UInt160.Zero,
            "zero new owner must be rejected");
    }

    // ----- committee registration -----------------------------------------------------------------

    [TestMethod]
    public void RegisterCommittee_OwnerGated_StoresHeaderAndKeys()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        var blob = CommitteeBlob(s0, s1);

        Assert.AreEqual(0, c.GetCommittee(ChainA)!.Length, "no committee registered yet");
        c.RegisterCommittee(ChainA, 1, blob);

        var stored = c.GetCommittee(ChainA)!;
        // Stored layout: [threshold][size][keys...]
        Assert.AreEqual(2 + blob.Length, stored.Length, "stored committee = 2 header bytes + keys");
        Assert.AreEqual((byte)1, stored[0], "header byte 0 is the threshold");
        Assert.AreEqual((byte)2, stored[1], "header byte 1 is the committee size");
    }

    [TestMethod]
    public void RegisterCommittee_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: UInt160.Parse("0x" + new string('9', 40)));
        var blob = CommitteeBlob(new Signer(0));

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ChainA, 1, blob),
            "registerCommittee is owner-gated");
        Assert.AreEqual(0, c.GetCommittee(ChainA)!.Length, "rejected registration must not persist state");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsReservedChainZero()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var blob = CommitteeBlob(new Signer(0));

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(0, 1, blob),
            "chainId 0 is reserved for L1");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsZeroThreshold()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var blob = CommitteeBlob(new Signer(0));

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ChainA, 0, blob),
            "threshold must be positive");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsEmptyBlob()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ChainA, 1, new byte[0]),
            "empty committee blob must be rejected");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsMisalignedBlob()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // 40 bytes is not a multiple of the 33-byte pubkey length.
        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ChainA, 1, new byte[40]),
            "blob length must be a multiple of 33");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsThresholdExceedingSize()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var blob = CommitteeBlob(new Signer(0), new Signer(1)); // size 2

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ChainA, 3, blob),
            "threshold cannot exceed committee size");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsOversizedCommittee()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // 65 valid 33-byte keys: blob length is a multiple of 33, chainId > 0, and threshold is in
        // [1, size], so every earlier guard passes and the only one that can reject is the
        // MaxCommitteeSize (64) cap. This pins the committee-size ceiling that bounds the per-batch
        // signature-verification work (and the seen-bitmap width) in VerifyAttestation.
        var signers = new Signer[65];
        for (var i = 0; i < signers.Length; i++) signers[i] = new Signer(i);
        var oversized = CommitteeBlob(signers); // 65 × 33 bytes

        var ex = Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ChainA, 1, oversized),
            "a committee larger than MaxCommitteeSize (64) must be rejected");
        StringAssert.Contains(ex.Message, "committee too large");
        Assert.AreEqual(0, c.GetCommittee(ChainA)!.Length, "rejected oversized registration must not persist");
    }

    // ----- attestation: happy path + persistence --------------------------------------------------

    [TestMethod]
    public void SubmitAttestation_ValidQuorum_Persists_AndValidateSucceeds()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        var s2 = new Signer(2);
        c.RegisterCommittee(ChainA, 2, CommitteeBlob(s0, s1, s2)); // 2-of-3

        ulong batch = 7;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeDAC);
        var proof = BuildProof(new (byte, byte[])[]
        {
            (0, s0.Sign(msg)),
            (1, s1.Sign(msg)),
        });

        // Off-L1 mode without an attestation cannot validate yet.
        Assert.IsFalse(c.Validate(ChainA, batch, Commitment, ModeDAC)!,
            "off-L1 mode must not validate before an attestation is stored");
        Assert.IsFalse(c.IsValidated(ChainA, batch, Commitment, ModeDAC)!);

        Assert.IsTrue(c.SubmitAttestation(ChainA, batch, Commitment, ModeDAC, proof)!,
            "a valid 2-of-3 quorum must be accepted");

        Assert.IsTrue(c.IsValidated(ChainA, batch, Commitment, ModeDAC)!,
            "batch must be marked validated after a successful attestation");
        Assert.IsTrue(c.Validate(ChainA, batch, Commitment, ModeDAC)!,
            "Validate must now pass for the attested batch/commitment/mode");

        // A different commitment for the same batch was NOT attested -> still not validated.
        Assert.IsFalse(c.IsValidated(ChainA, batch, OtherCommitment, ModeDAC)!,
            "a different commitment must not be considered validated");
        // A different mode for the same batch must not match the stored attestation either.
        Assert.IsFalse(c.IsValidated(ChainA, batch, Commitment, ModeNeoFS)!,
            "mode mismatch must not be considered validated");
    }

    [TestMethod]
    public void SubmitAttestation_ReplayProtected_CannotResubmitSameBatch()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0, s1)); // 1-of-2

        ulong batch = 42;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeExternal);
        var proof = BuildProof(new (byte, byte[])[] { (0, s0.Sign(msg)) });

        Assert.IsTrue(c.SubmitAttestation(ChainA, batch, Commitment, ModeExternal, proof)!,
            "first attestation for the batch must be accepted");
        // Replaying the very same valid proof must fault on the once-only guard.
        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeExternal, proof),
            "a batch attestation can only be persisted once (replay protection)");
    }

    // ----- attestation: rejection paths -----------------------------------------------------------

    [TestMethod]
    public void SubmitAttestation_SubThresholdSignatureCount_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 2, CommitteeBlob(s0, s1)); // require 2

        ulong batch = 1;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeDAC);
        var proof = BuildProof(new (byte, byte[])[] { (0, s0.Sign(msg)) }); // only 1 sig

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeDAC, proof),
            "sig count below threshold must be rejected");
        Assert.IsFalse(c.IsValidated(ChainA, batch, Commitment, ModeDAC)!,
            "rejected attestation must not persist validated state");
    }

    [TestMethod]
    public void SubmitAttestation_DuplicateSigner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 2, CommitteeBlob(s0, s1)); // 2-of-2

        ulong batch = 1;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeDAC);
        var sig0 = s0.Sign(msg);
        // Two entries, both signer index 0 -> distinct-signer guard must fire even though both
        // signatures are individually valid (prevents one signer satisfying the threshold twice).
        var proof = BuildProof(new (byte, byte[])[] { (0, sig0), (0, sig0) });

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeDAC, proof),
            "duplicate signer index must be rejected");
    }

    [TestMethod]
    public void SubmitAttestation_SignerIndexOutOfRange_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0, s1)); // size 2 -> valid indices 0,1

        ulong batch = 1;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeDAC);
        var proof = BuildProof(new (byte, byte[])[] { (5, s0.Sign(msg)) }); // index 5 >= size

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeDAC, proof),
            "signer index outside the committee must be rejected");
    }

    [TestMethod]
    public void SubmitAttestation_ForgedSignature_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0, s1));

        ulong batch = 1;
        // A 64-byte all-zero "signature" claimed for signer 0 must not verify against s0's pubkey.
        var proof = BuildProof(new (byte, byte[])[] { (0, new byte[64]) });

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeDAC, proof),
            "an invalid/forged signature must fail ECDSA verification");
        Assert.IsFalse(c.IsValidated(ChainA, batch, Commitment, ModeDAC)!);
    }

    [TestMethod]
    public void SubmitAttestation_SignatureOverWrongMessage_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0, s1));

        ulong batch = 1;
        // Sign a DIFFERENT batch number than the one submitted -> message binding must reject it.
        var wrongMsg = BuildMessage(ChainA, batch + 1, Commitment, ModeDAC);
        var proof = BuildProof(new (byte, byte[])[] { (0, s0.Sign(wrongMsg)) });

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeDAC, proof),
            "a signature bound to a different batch must not validate this batch");
    }

    [TestMethod]
    public void SubmitAttestation_NoCommitteeForChain_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);

        ulong batch = 1;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeDAC);
        var proof = BuildProof(new (byte, byte[])[] { (0, s0.Sign(msg)) });

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeDAC, proof),
            "attestation for a chain with no registered committee must be rejected");
    }

    [TestMethod]
    public void SubmitAttestation_ZeroCommitment_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0));

        ulong batch = 1;
        var msg = BuildMessage(ChainA, batch, UInt256.Zero, ModeDAC);
        var proof = BuildProof(new (byte, byte[])[] { (0, s0.Sign(msg)) });

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, UInt256.Zero, ModeDAC, proof),
            "zero commitment must be rejected");
    }

    [TestMethod]
    public void SubmitAttestation_L1Mode_Faults_AttestationsAreOffL1Only()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0));

        ulong batch = 1;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeL1);
        var proof = BuildProof(new (byte, byte[])[] { (0, s0.Sign(msg)) });

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, batch, Commitment, ModeL1, proof),
            "attestations are only meaningful for off-L1 modes; L1 mode must be rejected here");
    }

    [TestMethod]
    public void SubmitAttestation_ProofTooShort_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0));

        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, 1, Commitment, ModeDAC, new byte[1]),
            "a proof shorter than the 2-byte header must be rejected");
    }

    [TestMethod]
    public void SubmitAttestation_ProofLengthMismatch_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0, s1));

        // Header claims 1 signature but the body is not 2 + 1*(1+64) bytes long.
        var malformed = new byte[2 + 10];
        malformed[0] = 1; // sigCount = 1
        Assert.ThrowsExactly<TestException>(
            () => c.SubmitAttestation(ChainA, 1, Commitment, ModeDAC, malformed),
            "declared sig count must match the proof body length");
    }

    // ----- Validate: L1-mode trust note + input validation ----------------------------------------

    [TestMethod]
    public void Validate_L1Mode_PassesOnAnyNonZeroCommitment_NoProof()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // Documented trust note: ModeL1 passes on a non-zero commitment WITHOUT any stored
        // attestation. This pins that (weaker) behavior so a regression that silently strengthens
        // or weakens it is caught.
        Assert.IsTrue(c.Validate(ChainA, 1, Commitment, ModeL1)!,
            "L1 mode validates any non-zero commitment without an attestation");
        Assert.IsFalse(c.Validate(ChainA, 1, UInt256.Zero, ModeL1)!,
            "even L1 mode rejects a zero commitment");
    }

    [TestMethod]
    public void Validate_RejectsReservedChainZero_BadMode_ZeroCommitment()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.IsFalse(c.Validate(0, 1, Commitment, ModeL1)!,
            "chainId 0 is reserved and must never validate");
        Assert.IsFalse(c.Validate(ChainA, 1, Commitment, 4)!,
            "daMode > ModeDAC (3) must never validate");
        Assert.IsFalse(c.Validate(ChainA, 1, UInt256.Zero, ModeDAC)!,
            "zero commitment must never validate");
    }

    [TestMethod]
    public void Validate_OffL1Mode_RequiresStoredAttestation()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // No attestation submitted -> off-L1 modes must not validate. This is the core gate that
        // forces a verifiable committee attestation before SettlementManager can finalize.
        Assert.IsFalse(c.Validate(ChainA, 1, Commitment, ModeNeoFS)!,
            "NeoFS mode requires a stored attestation");
        Assert.IsFalse(c.Validate(ChainA, 1, Commitment, ModeExternal)!,
            "External mode requires a stored attestation");
        Assert.IsFalse(c.Validate(ChainA, 1, Commitment, ModeDAC)!,
            "DAC mode requires a stored attestation");
    }

    [TestMethod]
    public void VerifyAttestation_DoesNotPersist_ValidateStillFails()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var s0 = new Signer(0);
        var s1 = new Signer(1);
        c.RegisterCommittee(ChainA, 1, CommitteeBlob(s0, s1));

        ulong batch = 9;
        var msg = BuildMessage(ChainA, batch, Commitment, ModeDAC);
        var proof = BuildProof(new (byte, byte[])[] { (0, s0.Sign(msg)) });

        // verifyAttestation is a [Safe] read-only check: it must verify true but NOT persist.
        Assert.IsTrue(c.VerifyAttestation(ChainA, batch, Commitment, ModeDAC, proof)!,
            "a valid quorum must verify");
        Assert.IsFalse(c.IsValidated(ChainA, batch, Commitment, ModeDAC)!,
            "verifyAttestation must not persist validated state");
        Assert.IsFalse(c.Validate(ChainA, batch, Commitment, ModeDAC)!,
            "Validate must still fail because nothing was persisted");
    }
}
