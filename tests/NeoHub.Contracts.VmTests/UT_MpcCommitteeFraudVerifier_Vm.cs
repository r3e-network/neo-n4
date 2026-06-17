using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// Minimal ExternalBridgeBond surface so the fraud verifier's bond reads + slash payout can be mocked.
/// The verifier calls getBalance(chainId, member) (read-only) then slash(chainId, member, amount,
/// reporter) (CallFlags.All) on this hash.
/// </summary>
public abstract class Mock_MpcCommitteeFraudVerifier_Bond(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("getBalance")]
    public abstract BigInteger? GetBalance(BigInteger? externalChainId, UInt160? member);

    [DisplayName("slash")]
    public abstract bool? Slash(BigInteger? externalChainId, UInt160? member, BigInteger? amount, UInt160? reporter);
}

/// <summary>
/// VM-level tests for NeoHub.MpcCommitteeFraudVerifier — the permissionless equivocation slasher on the
/// cross-foreign-chain bridge. With the MpcCommitteeVerifier and ExternalBridgeBond replaced by mocks
/// (distinct hashes) and a REAL secp256k1 committee key, these execute the slash path in a real NeoVM
/// and pin the security-critical invariants:
///   * deploy validates owner/verifier/bond are non-zero (input validation);
///   * setOwner is witness-gated (positive AND negative auth) and rejects a zero new owner;
///   * a fully valid equivocation proof slashes once and is then replay-protected per
///     (externalChainId, signerIdx) — a second submission faults and isSlashed flips true;
///   * the equivocation invariants are enforced as VM FAULTs: short messages, wrong-length sigs,
///     chainId mismatch, distinct nonces (not equivocation), non-ForeignToNeo direction, and
///     byte-identical messages (honest re-signing) all revert;
///   * signatures that do not verify against committee[signerIdx] revert (the crypto is real);
///   * signerIdx out of committee range reverts;
///   * an unbound signer slot (getSignerMember == zero) refuses to slash so the wrong identity
///     can never be slashed;
///   * a zero-balance equivocator reverts on the "nothing to slash" guard and records NOTHING —
///     the zero-balance assert runs before the replay-record write, so a re-report simply reverts
///     again until the member posts a bond, at which point the slash can proceed;
///   * the CEI ordering: the replay flag is written BEFORE the external bond.slash call.
/// </summary>
[TestClass]
public class UT_MpcCommitteeFraudVerifier_Vm
{
    private const uint ChainId = 0xE0000001;          // an Eth-style foreign chain id
    private const byte SignerIdx = 0;
    private const byte CurveSecp256k1 = 1;
    private const byte DirectionForeignToNeo = 2;
    private const int OffsetDirection = 16;

    // Distinct mock hashes: verifier and bond MUST be different (we cannot mock one hash twice).
    private static readonly UInt160 VerifierHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 BondHash = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 BondMember = UInt160.Parse("0x" + new string('d', 40));

    /// <summary>A deterministic secp256k1 PRIVATE key (32 bytes) whose 33-byte compressed pubkey is
    /// committee slot 0. The contract verifies with the native secp256k1+SHA256 syscall
    /// (VerifyWithECDsa with NamedCurveHash.secp256k1SHA256), which cannot be mocked — so the test must
    /// produce REAL secp256k1 keys/signatures. Neo's KeyPair is secp256r1-only and cannot represent
    /// this key, so we carry the raw private-key bytes and derive the pubkey / sign off the curve directly.</summary>
    private static byte[] CommitteeKey()
    {
        var priv = new byte[32];
        for (var j = 0; j < 32; j++) priv[j] = (byte)(0x11 + j);
        priv[0] &= 0x1F; // keep inside curve order (non-zero, < n)
        return priv;
    }

    /// <summary>Derive the 33-byte compressed secp256k1 pubkey for a private key: G * priv, compressed.</summary>
    private static byte[] DerivePubKey(byte[] priv) =>
        (ECCurve.Secp256k1.G * priv).EncodePoint(true); // 33 bytes

    /// <summary>committee blob = [threshold, size, curveTag] ‖ size × 33B compressed pubkey.
    /// The contract only reads blob[1]=size, blob[2]=curveTag and the pubkey at slot signerIdx.</summary>
    private static byte[] CommitteeBlob(byte[] priv, byte size = 1, byte threshold = 1, byte curveTag = CurveSecp256k1)
    {
        var pub = DerivePubKey(priv); // 33 bytes
        var blob = new byte[3 + size * 33];
        blob[0] = threshold;
        blob[1] = size;
        blob[2] = curveTag;
        // Put the real pubkey in slot 0; remaining slots (if any) stay zero (unused by these tests).
        pub.CopyTo(blob.AsSpan(3, 33));
        return blob;
    }

    /// <summary>Build a 102-byte (minimum) ExternalCrossChainMessage-shaped buffer: chainId@0 (4 LE),
    /// nonce@8 (8 LE), direction@16. A trailing marker byte makes two otherwise-identical messages
    /// byte-distinct (an equivocation) without disturbing the parsed fields.</summary>
    private static byte[] Message(uint chainId, ulong nonce, byte direction, byte marker)
    {
        var buf = new byte[102];
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(0, 4), chainId);
        BinaryPrimitives.WriteUInt64LittleEndian(buf.AsSpan(8, 8), nonce);
        buf[OffsetDirection] = direction;
        buf[101] = marker; // differentiator
        return buf;
    }

    /// <summary>Sign with secp256k1 + SHA256 prehash so the on-chain VerifyWithECDsa(secp256k1SHA256)
    /// accepts it. Crypto.Sign SHA256-prehashes internally and returns the raw 64-byte (r‖s) signature.</summary>
    private static byte[] Sign(byte[] priv, byte[] message) =>
        Crypto.Sign(message, priv, ECCurve.Secp256k1, HashAlgorithm.SHA256); // matches secp256k1SHA256 on-chain

    /// <summary>Deploy with owner defaulting to engine.Sender (so SetOwner witness passes) and the
    /// verifier/bond pointed at our mock hashes. The mocks themselves are wired by the caller via
    /// <see cref="WireMocks"/>.</summary>
    private static NeoHubMpcCommitteeFraudVerifier Deploy(TestEngine engine, UInt160? owner = null) =>
        engine.Deploy<NeoHubMpcCommitteeFraudVerifier>(
            NeoHubMpcCommitteeFraudVerifier.Nef, NeoHubMpcCommitteeFraudVerifier.Manifest,
            new object[] { owner ?? engine.Sender, VerifierHash, BondHash });

    /// <summary>Wire the verifier (getCommittee + getSignerMember) and bond (getBalance + slash) mocks.
    /// Allows overriding the committee blob, the bound member, and the bond balance to exercise the
    /// unbound-slot and zero-balance guards.</summary>
    private static void WireMocks(TestEngine engine, byte[] committeeBlob, UInt160 member, BigInteger balance)
    {
        engine.FromHash<NeoHubMpcCommitteeVerifier>(VerifierHash, m =>
        {
            m.Setup(c => c.GetCommittee(It.IsAny<BigInteger?>())).Returns(committeeBlob);
            m.Setup(c => c.GetSignerMember(It.IsAny<BigInteger?>(), It.IsAny<BigInteger?>())).Returns(member);
        }, checkExistence: false);
        engine.FromHash<Mock_MpcCommitteeFraudVerifier_Bond>(BondHash, m =>
        {
            m.Setup(c => c.GetBalance(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>())).Returns(balance);
            m.Setup(c => c.Slash(It.IsAny<BigInteger?>(), It.IsAny<UInt160?>(), It.IsAny<BigInteger?>(), It.IsAny<UInt160?>())).Returns(true);
        }, checkExistence: false);
    }

    // ---------------------------------------------------------------------------------------------
    // Deploy / configuration
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deploy_WiresOwnerVerifierBond_AndRejectsZeroArgs()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.AreEqual(engine.Sender, c.Owner);
        Assert.AreEqual(VerifierHash, c.Verifier);
        Assert.AreEqual(BondHash, c.Bond);

        // A zero verifier (or owner, or bond) must be rejected by _deploy's input validation.
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubMpcCommitteeFraudVerifier>(
            NeoHubMpcCommitteeFraudVerifier.Nef, NeoHubMpcCommitteeFraudVerifier.Manifest,
            new object[] { engine.Sender, UInt160.Zero, BondHash }),
            "zero verifier must be rejected at deploy");
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubMpcCommitteeFraudVerifier>(
            NeoHubMpcCommitteeFraudVerifier.Nef, NeoHubMpcCommitteeFraudVerifier.Manifest,
            new object[] { UInt160.Zero, VerifierHash, BondHash }),
            "zero owner must be rejected at deploy");
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubMpcCommitteeFraudVerifier>(
            NeoHubMpcCommitteeFraudVerifier.Nef, NeoHubMpcCommitteeFraudVerifier.Manifest,
            new object[] { engine.Sender, VerifierHash, UInt160.Zero }),
            "zero bond must be rejected at deploy");
    }

    [TestMethod]
    public void SetOwner_OwnerOnly_PositiveAndNegativeAuth()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender

        // Positive: the witnessed owner can rotate ownership.
        c.Owner = Stranger;
        Assert.AreEqual(Stranger, c.Owner);

        // Negative: engine.Sender is no longer owner, so a further rotation must fault (CheckWitness).
        Assert.ThrowsExactly<TestException>(() => c.Owner = engine.Sender,
            "setOwner is witness-gated to the current owner");
        Assert.AreEqual(Stranger, c.Owner, "rejected rotation must not change owner");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different principal than the test signer, so the very first rotation must fault.
        var c = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => c.Owner = engine.Sender,
            "non-owner cannot call setOwner");
    }

    [TestMethod]
    public void SetOwner_ZeroNewOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (witness present)

        Assert.ThrowsExactly<TestException>(() => c.Owner = UInt160.Zero,
            "zero new owner must be rejected even by the authorized owner");
    }

    // ---------------------------------------------------------------------------------------------
    // Slash — happy path + replay protection
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Slash_ValidEquivocation_SlashesOnce_ThenReplayProtected()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        // Two byte-distinct messages sharing (chainId, nonce), both ForeignToNeo, signed by slot 0.
        var m1 = Message(ChainId, nonce: 7, DirectionForeignToNeo, marker: 0x01);
        var m2 = Message(ChainId, nonce: 7, DirectionForeignToNeo, marker: 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        Assert.IsFalse(c.IsSlashed(ChainId, SignerIdx)!, "not slashed before proof");

        c.Slash(ChainId, SignerIdx, m1, s1, m2, s2);
        Assert.IsTrue(c.IsSlashed(ChainId, SignerIdx)!, "valid equivocation proof must record the slash");

        // Replay: the SAME (chainId, signerIdx) cannot be slashed twice.
        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, m2, s2),
            "a single equivocation can be slashed only once");
        Assert.IsTrue(c.IsSlashed(ChainId, SignerIdx)!, "replay attempt leaves the slash record intact");
    }

    // ---------------------------------------------------------------------------------------------
    // Slash — equivocation invariants (input validation faults)
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Slash_ShortMessage_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        var m1 = Message(ChainId, 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        var shortMsg = new byte[101]; // one byte below the 102-byte minimum layout
        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, shortMsg, s1, m2, s2),
            "message1 shorter than the ExternalCrossChainMessage layout must fault");
        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, shortMsg, s2),
            "message2 shorter than the layout must fault");
        Assert.IsFalse(c.IsSlashed(ChainId, SignerIdx)!, "rejected proof must not record a slash");
    }

    [TestMethod]
    public void Slash_WrongSignatureLength_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        var m1 = Message(ChainId, 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);
        var s2 = Sign(key, m2);

        var badSig = new byte[63]; // not 64 bytes
        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, badSig, m2, s2),
            "signature1 must be exactly 64 bytes");
    }

    [TestMethod]
    public void Slash_ChainIdMismatch_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        // message1 encodes a DIFFERENT chainId than the externalChainId argument.
        var m1 = Message(ChainId + 1, 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, m2, s2),
            "message1 chainId must equal externalChainId");
    }

    [TestMethod]
    public void Slash_DifferentNonces_NotEquivocation_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        // Distinct nonces => a member is allowed to sign distinct nonces; this is NOT equivocation.
        var m1 = Message(ChainId, nonce: 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, nonce: 8, DirectionForeignToNeo, 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, m2, s2),
            "messages with different nonces are not an equivocation");
        Assert.IsFalse(c.IsSlashed(ChainId, SignerIdx)!);
    }

    [TestMethod]
    public void Slash_WrongDirection_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        // Direction 1 (NeoToForeign) is not legitimately attested by the committee. Pairing it as an
        // equivocation would let an innocent member be slashed for a disjoint nonce namespace.
        var m1 = Message(ChainId, 7, direction: 1, marker: 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, m2, s2),
            "message1 direction must be ForeignToNeo(2)");
    }

    [TestMethod]
    public void Slash_ByteIdenticalMessages_NotEquivocation_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        // Identical messages with two valid signatures is just honest re-signing — not equivocation.
        var m = Message(ChainId, 7, DirectionForeignToNeo, marker: 0x01);
        var s1 = Sign(key, m);
        var s2 = Sign(key, m); // ECDSA may produce a distinct (r,s); both are valid for the same digest

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m, s1, m, s2),
            "byte-identical messages must not be treated as an equivocation");
        Assert.IsFalse(c.IsSlashed(ChainId, SignerIdx)!);
    }

    // ---------------------------------------------------------------------------------------------
    // Slash — crypto + committee binding guards
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Slash_SignatureDoesNotVerify_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 1000);

        var m1 = Message(ChainId, 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);

        // A signature from a DIFFERENT key cannot verify against committee[0]'s pubkey.
        var wrongPriv = new byte[32];
        for (var j = 0; j < 32; j++) wrongPriv[j] = (byte)(0x40 + j);
        wrongPriv[0] &= 0x1F; // keep inside curve order
        var s1 = Sign(wrongPriv, m1); // forged: not by the committee member
        var s2 = Sign(key, m2);

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, m2, s2),
            "a signature that does not verify against committee[signerIdx] must fault");
        Assert.IsFalse(c.IsSlashed(ChainId, SignerIdx)!, "a failed crypto proof must not record a slash");
    }

    [TestMethod]
    public void Slash_SignerIdxOutOfRange_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        // Committee size 1 => only slot 0 exists.
        WireMocks(engine, CommitteeBlob(key, size: 1), BondMember, balance: 1000);

        var m1 = Message(ChainId, 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, signerIdx: 1, m1, s1, m2, s2),
            "signerIdx >= committee size must fault");
    }

    [TestMethod]
    public void Slash_UnboundSignerSlot_RefusesToSlash()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        // getSignerMember returns Zero => no bond-holder bound to this slot.
        WireMocks(engine, CommitteeBlob(key), UInt160.Zero, balance: 1000);

        var m1 = Message(ChainId, 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, m2, s2),
            "an unbound signer slot must refuse to slash (wrong identity protection)");
        Assert.IsFalse(c.IsSlashed(ChainId, SignerIdx)!,
            "refusing on the unbound-slot guard must not record a slash");
    }

    [TestMethod]
    public void Slash_ZeroBondBalance_Faults()
    {
        var engine = new TestEngine(true);
        var key = CommitteeKey();
        var c = Deploy(engine);
        // Bound member, but zero bond balance => nothing to slash.
        WireMocks(engine, CommitteeBlob(key), BondMember, balance: 0);

        var m1 = Message(ChainId, 7, DirectionForeignToNeo, 0x01);
        var m2 = Message(ChainId, 7, DirectionForeignToNeo, 0x02);
        var s1 = Sign(key, m1);
        var s2 = Sign(key, m2);

        Assert.ThrowsExactly<TestException>(() => c.Slash(ChainId, SignerIdx, m1, s1, m2, s2),
            "a zero-balance equivocator yields the 'nothing to slash' fault");
        // The zero-balance assert runs BEFORE the replay-record write, so a reverted zero-balance
        // slash records nothing — a later re-report (after a bond is posted) can still proceed.
        Assert.IsFalse(c.IsSlashed(ChainId, SignerIdx)!,
            "a reverted zero-balance slash must NOT record the slash (no replay record written)");
    }
}
