using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal deployable-verifier surface so the router's <c>Contract.Call(verifier,
/// "verifyZkProof", ...)</c> dispatch can be mocked. ABI mirrors the contract docstring:
/// <c>verifyZkProof(byte proofSystem, byte[] verificationKeyId, byte[] publicInputHash,
/// byte[] proofBytes) : bool</c>.</summary>
public abstract class Mock_ContractZkVerifier_Verifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verifyZkProof")]
    public abstract bool? VerifyZkProof(BigInteger? proofSystem, byte[]? verificationKeyId,
        byte[]? publicInputHash, byte[]? proofBytes);
}

/// <summary>
/// VM-level tests for NeoHub.ContractZkVerifier — the deployable ProofType.Zk verifier router.
/// These execute every governance and verification path in a real NeoVM and pin the security-critical
/// invariants:
///   * deploy rejects an invalid/zero owner;
///   * every mutating governance method is owner-witness-gated (positive AND negative paths);
///   * proofSystem is validated to 1..4 and zero verification-key ids are rejected;
///   * envelope-only mode honors the one-way permanent lock (DisableEnvelopeOnlyPermanently is
///     irreversible — no future owner can re-enable it, the documented anti-foot-gun hardening);
///   * the production configuration lock freezes one exact VK and terminal verifier per proof
///     system, even when stale keys had been registered during bootstrap;
///   * Verify enforces the full commitment/payload envelope validation, requires a governance-
///     registered verification key, dispatches proof math to a registered verifier contract, and only
///     accepts envelope-only (no real proof) when explicitly allowed — never silently.
/// </summary>
[TestClass]
public class UT_ContractZkVerifier_Vm
{
    private const byte Sp1 = 1;     // ProofSystemSp1
    private const byte Halo2 = 3;   // ProofSystemHalo2
    private const byte ProofTypeZk = 3;
    private const byte ZkPayloadVersion = 1;

    // Commitment envelope offsets (mirror the contract constants).
    private const int PublicInputHashOffset = 284;
    private const int ProofTypeOffset = 316;
    private const int ProofLenOffset = 317;
    private const int ProofBytesOffset = 321;

    // ZK payload offsets (mirror the contract constants).
    private const int ZkPayloadVerificationKeyOffset = 2;
    private const int ZkPayloadInnerProofLenOffset = 34;
    private const int ZkPayloadProofBytesOffset = 38;

    private static readonly UInt256 VkId = new(Convert.FromHexString("00" + new string('1', 62)));
    private static readonly UInt256 AlternateVkId = new(Convert.FromHexString("00" + new string('2', 62)));

    /// <summary>Deploy the router. owner defaults to engine.Sender so the owner witness check passes;
    /// pass an explicit non-sender <paramref name="owner"/> to exercise the negative auth paths.</summary>
    private static NeoHubContractZkVerifier Deploy(TestEngine engine, UInt160? owner = null)
    {
        var o = owner ?? engine.Sender;
        return engine.Deploy<NeoHubContractZkVerifier>(
            NeoHubContractZkVerifier.Nef, NeoHubContractZkVerifier.Manifest, o);
    }

    /// <summary>Build the inner ZK payload: version(1) ‖ proofSystem(1) ‖ verificationKeyId(32) ‖
    /// innerProofLen(4 LE) ‖ innerProof(innerProofLen). Caller can corrupt the result to test guards.</summary>
    private static byte[] BuildPayload(byte version, byte proofSystem, UInt256 vkId, byte[] innerProof)
    {
        var buf = new byte[ZkPayloadProofBytesOffset + innerProof.Length];
        buf[0] = version;
        buf[1] = proofSystem;
        vkId.GetSpan().CopyTo(buf.AsSpan(ZkPayloadVerificationKeyOffset, 32));
        BinaryPrimitives.WriteUInt32LittleEndian(
            buf.AsSpan(ZkPayloadInnerProofLenOffset, 4), (uint)innerProof.Length);
        innerProof.CopyTo(buf.AsSpan(ZkPayloadProofBytesOffset, innerProof.Length));
        return buf;
    }

    /// <summary>Wrap a payload in a canonical N4 batch commitment: a zero-filled header of
    /// length ProofBytesOffset with proofType=Zk at ProofTypeOffset, the payload length at
    /// ProofLenOffset (4 LE), a 32-byte public-input hash at PublicInputHashOffset, then the payload.</summary>
    private static byte[] BuildCommitment(byte proofType, byte[] publicInputHash, byte[] payload)
    {
        var buf = new byte[ProofBytesOffset + payload.Length];
        buf[ProofTypeOffset] = proofType;
        BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(ProofLenOffset, 4), (uint)payload.Length);
        publicInputHash.CopyTo(buf.AsSpan(PublicInputHashOffset, 32));
        payload.CopyTo(buf.AsSpan(ProofBytesOffset, payload.Length));
        return buf;
    }

    /// <summary>A well-formed commitment for proofSystem=SP1, VkId, with a non-empty inner proof.</summary>
    private static byte[] ValidCommitment(byte proofSystem = Sp1, UInt256? vkId = null)
    {
        var payload = BuildPayload(ZkPayloadVersion, proofSystem, vkId ?? VkId, new byte[] { 0xAA, 0xBB, 0xCC });
        return BuildCommitment(ProofTypeZk, new byte[32], payload);
    }

    // ---------------------------------------------------------------------
    // Deployment validation
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Deploy_SetsOwner_AndRejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        Assert.AreEqual(engine.Sender, c.Owner, "deploy must persist the owner");

        // _deploy asserts owner.IsValid && !owner.IsZero.
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubContractZkVerifier>(
            NeoHubContractZkVerifier.Nef, NeoHubContractZkVerifier.Manifest, UInt160.Zero),
            "deploy with the zero owner must fault");
    }

    // ---------------------------------------------------------------------
    // Ownership transfer — auth gating (positive + negative)
    // ---------------------------------------------------------------------

    [TestMethod]
    public void SetOwner_OwnerOnly_TransfersOwnership()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var newOwner = UInt160.Parse("0x" + new string('7', 40));

        c.Owner = newOwner;
        Assert.AreEqual(newOwner, c.Owner, "ownership must transfer to the new owner");

        // The old owner (engine.Sender) is no longer authorized — its witness no longer matches GetOwner().
        Assert.ThrowsExactly<TestException>(() => { c.Owner = engine.Sender; },
            "after handing off ownership the previous owner can no longer set the owner");
    }

    [TestMethod]
    public void SetOwner_RejectsZeroNewOwner()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        Assert.ThrowsExactly<TestException>(() => { c.Owner = UInt160.Zero; },
            "transferring ownership to the zero address must fault");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> the owner gate must reject.
        var c = Deploy(engine, owner: UInt160.Parse("0x" + new string('9', 40)));
        Assert.ThrowsExactly<TestException>(() => { c.Owner = engine.Sender; },
            "SetOwner is owner-witness-gated");
    }

    // ---------------------------------------------------------------------
    // RegisterVerificationKey — auth, proof-system + zero-id validation, state
    // ---------------------------------------------------------------------

    [TestMethod]
    public void RegisterVerificationKey_OwnerOnly_RegistersAndRemoves()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.IsFalse(c.IsVerificationKeyRegistered(Sp1, VkId)!.Value, "unset key is not registered");
        c.RegisterVerificationKey(Sp1, VkId, true);
        Assert.IsTrue(c.IsVerificationKeyRegistered(Sp1, VkId)!.Value, "key must register");
        // Proof-system scoping: the same id under a different proof system is independent.
        Assert.IsFalse(c.IsVerificationKeyRegistered(Halo2, VkId)!.Value,
            "registration is scoped per proof system");

        c.RegisterVerificationKey(Sp1, VkId, false);
        Assert.IsFalse(c.IsVerificationKeyRegistered(Sp1, VkId)!.Value, "removal must clear the key");
    }

    [TestMethod]
    public void RegisterVerificationKey_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: UInt160.Parse("0x" + new string('9', 40)));
        Assert.ThrowsExactly<TestException>(() => c.RegisterVerificationKey(Sp1, VkId, true),
            "RegisterVerificationKey is owner-gated");
    }

    [TestMethod]
    public void RegisterVerificationKey_RejectsBadProofSystem_AndZeroId()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.RegisterVerificationKey(0, VkId, true),
            "proofSystem 0 is out of the supported 1..4 range");
        Assert.ThrowsExactly<TestException>(() => c.RegisterVerificationKey(5, VkId, true),
            "proofSystem 5 is out of the supported 1..4 range");
        Assert.ThrowsExactly<TestException>(() => c.RegisterVerificationKey(Sp1, UInt256.Zero, true),
            "the zero verification-key id must be rejected");
    }

    [TestMethod]
    public void RegisterVerificationKey_Sp1RejectsNonCanonicalBytes32RawShape()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var nonCanonical = new UInt256(Convert.FromHexString("01" + new string('0', 62)));

        Assert.ThrowsExactly<TestException>(() => c.RegisterVerificationKey(Sp1, nonCanonical, true),
            "SP1 bytes32_raw() always has a zero leading byte");
        Assert.IsFalse(c.IsVerificationKeyRegistered(Sp1, nonCanonical)!.Value,
            "a rejected SP1 verification key must not become registered");

        c.RegisterVerificationKey(Halo2, nonCanonical, true);
        Assert.IsTrue(c.IsVerificationKeyRegistered(Halo2, nonCanonical)!.Value,
            "the SP1-specific wire invariant must not constrain other proof systems");
    }

    // ---------------------------------------------------------------------
    // RegisterProofVerifier — auth, zero-verifier validation, state
    // ---------------------------------------------------------------------

    [TestMethod]
    public void RegisterProofVerifier_OwnerOnly_RegistersAndRemoves()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var verifier = UInt160.Parse("0x" + new string('a', 40));

        Assert.AreEqual(UInt160.Zero, c.GetProofVerifier(Sp1), "no verifier configured by default");
        c.RegisterProofVerifier(Sp1, verifier, true);
        Assert.AreEqual(verifier, c.GetProofVerifier(Sp1), "verifier must register");
        c.RegisterProofVerifier(Sp1, verifier, false);
        Assert.AreEqual(UInt160.Zero, c.GetProofVerifier(Sp1), "removal must clear the verifier");
    }

    [TestMethod]
    public void RegisterProofVerifier_RejectsZeroVerifier_AndBadProofSystem_AndNonOwner()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var verifier = UInt160.Parse("0x" + new string('a', 40));

        Assert.ThrowsExactly<TestException>(() => c.RegisterProofVerifier(Sp1, UInt160.Zero, true),
            "registering the zero address as a verifier must fault");
        Assert.ThrowsExactly<TestException>(() => c.RegisterProofVerifier(9, verifier, true),
            "out-of-range proofSystem must fault");

        // Non-owner path: switch the active signer away from the owner so the owner-witness gate
        // rejects. (Deploying a second instance from the same deployer would collide on contract hash.)
        engine.SetTransactionSigners(UInt160.Parse("0x" + new string('9', 40)));
        Assert.ThrowsExactly<TestException>(() => c.RegisterProofVerifier(Sp1, verifier, true),
            "RegisterProofVerifier is owner-gated");
    }

    // ---------------------------------------------------------------------
    // Envelope-only mode + the one-way permanent lock (key hardening)
    // ---------------------------------------------------------------------

    [TestMethod]
    public void EnvelopeOnly_OwnerOnly_EnableAndDisable()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.IsFalse(c.IsEnvelopeOnlyAllowed(Sp1)!.Value, "envelope-only disabled by default");
        c.SetEnvelopeOnlyAllowed(Sp1, true);
        Assert.IsTrue(c.IsEnvelopeOnlyAllowed(Sp1)!.Value, "owner can enable envelope-only");
        c.SetEnvelopeOnlyAllowed(Sp1, false);
        Assert.IsFalse(c.IsEnvelopeOnlyAllowed(Sp1)!.Value, "owner can disable envelope-only");
    }

    [TestMethod]
    public void SetEnvelopeOnlyAllowed_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: UInt160.Parse("0x" + new string('9', 40)));
        Assert.ThrowsExactly<TestException>(() => c.SetEnvelopeOnlyAllowed(Sp1, true),
            "SetEnvelopeOnlyAllowed is owner-gated");
    }

    [TestMethod]
    public void DisableEnvelopeOnlyPermanently_IsIrreversible_AndDisablesCurrentFlag()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // Turn envelope-only ON, then permanently lock it OFF.
        c.SetEnvelopeOnlyAllowed(Sp1, true);
        Assert.IsTrue(c.IsEnvelopeOnlyAllowed(Sp1)!.Value);

        c.DisableEnvelopeOnlyPermanently(Sp1);
        Assert.IsTrue(c.IsEnvelopeOnlyLocked(Sp1)!.Value, "proof system must report as permanently locked");
        Assert.IsFalse(c.IsEnvelopeOnlyAllowed(Sp1)!.Value,
            "locking must immediately disable the currently-enabled envelope-only flag");

        // The lock is one-way: no future owner (or a compromised one) can re-enable envelope-only.
        Assert.ThrowsExactly<TestException>(() => c.SetEnvelopeOnlyAllowed(Sp1, true),
            "re-enabling envelope-only after a permanent disable must fault");
        Assert.IsFalse(c.IsEnvelopeOnlyAllowed(Sp1)!.Value, "rejected enable must not change state");

        // The lock is scoped per proof system: a different proof system is unaffected.
        Assert.IsFalse(c.IsEnvelopeOnlyLocked(Halo2)!.Value);
        c.SetEnvelopeOnlyAllowed(Halo2, true);
        Assert.IsTrue(c.IsEnvelopeOnlyAllowed(Halo2)!.Value,
            "an unlocked proof system can still enable envelope-only");
    }

    [TestMethod]
    public void DisableEnvelopeOnlyPermanently_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: UInt160.Parse("0x" + new string('9', 40)));
        Assert.ThrowsExactly<TestException>(() => c.DisableEnvelopeOnlyPermanently(Sp1),
            "DisableEnvelopeOnlyPermanently is owner-gated");
    }

    // ---------------------------------------------------------------------
    // Production proof-system configuration lock
    // ---------------------------------------------------------------------

    [TestMethod]
    public void LockProofSystemConfiguration_RequiresCompleteProductionBootstrap()
    {
        var verifier = UInt160.Parse("0x" + new string('a', 40));

        var missingKeyEngine = new TestEngine(true);
        var missingKey = Deploy(missingKeyEngine);
        missingKey.RegisterProofVerifier(Sp1, verifier, true);
        missingKey.DisableEnvelopeOnlyPermanently(Sp1);
        Assert.ThrowsExactly<TestException>(() => missingKey.LockProofSystemConfiguration(Sp1, VkId),
            "the exact VK must be registered before the configuration can be frozen");

        var missingVerifierEngine = new TestEngine(true);
        var missingVerifier = Deploy(missingVerifierEngine);
        missingVerifier.RegisterVerificationKey(Sp1, VkId, true);
        missingVerifier.DisableEnvelopeOnlyPermanently(Sp1);
        Assert.ThrowsExactly<TestException>(() => missingVerifier.LockProofSystemConfiguration(Sp1, VkId),
            "a terminal verifier must be configured before the configuration can be frozen");

        var mutableEnvelopeEngine = new TestEngine(true);
        var mutableEnvelope = Deploy(mutableEnvelopeEngine);
        mutableEnvelope.RegisterVerificationKey(Sp1, VkId, true);
        mutableEnvelope.RegisterProofVerifier(Sp1, verifier, true);
        Assert.ThrowsExactly<TestException>(() => mutableEnvelope.LockProofSystemConfiguration(Sp1, VkId),
            "envelope-only must be permanently disabled before the configuration can be frozen");

        Assert.IsFalse(mutableEnvelope.IsProofSystemConfigurationLocked(Sp1)!.Value);
        Assert.AreEqual(UInt256.Zero, mutableEnvelope.GetLockedVerificationKey(Sp1));
    }

    [TestMethod]
    public void LockProofSystemConfiguration_FreezesExactKeyTerminalAndEnvelopeState()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var verifier = UInt160.Parse("0x" + new string('a', 40));
        var replacement = UInt160.Parse("0x" + new string('b', 40));

        c.RegisterVerificationKey(Sp1, VkId, true);
        c.RegisterVerificationKey(Sp1, AlternateVkId, true);
        c.RegisterProofVerifier(Sp1, verifier, true);
        c.DisableEnvelopeOnlyPermanently(Sp1);
        c.LockProofSystemConfiguration(Sp1, VkId);

        Assert.IsTrue(c.IsProofSystemConfigurationLocked(Sp1)!.Value);
        Assert.AreEqual(VkId, c.GetLockedVerificationKey(Sp1));
        Assert.IsTrue(c.IsVerificationKeyRegistered(Sp1, VkId)!.Value,
            "the exact locked VK remains accepted");
        Assert.IsFalse(c.IsVerificationKeyRegistered(Sp1, AlternateVkId)!.Value,
            "a stale bootstrap VK becomes unusable once a different exact VK is locked");
        Assert.AreEqual(verifier, c.GetProofVerifier(Sp1));
        Assert.IsTrue(c.IsEnvelopeOnlyLocked(Sp1)!.Value);
        Assert.IsFalse(c.IsEnvelopeOnlyAllowed(Sp1)!.Value);

        Assert.ThrowsExactly<TestException>(() => c.RegisterVerificationKey(Sp1, VkId, false));
        Assert.ThrowsExactly<TestException>(() => c.RegisterVerificationKey(Sp1, AlternateVkId, true));
        Assert.ThrowsExactly<TestException>(() => c.RegisterProofVerifier(Sp1, replacement, true));
        Assert.ThrowsExactly<TestException>(() => c.RegisterProofVerifier(Sp1, verifier, false));
        Assert.ThrowsExactly<TestException>(() => c.SetEnvelopeOnlyAllowed(Sp1, false));
        Assert.ThrowsExactly<TestException>(() => c.SetEnvelopeOnlyAllowed(Sp1, true));
        Assert.ThrowsExactly<TestException>(() => c.DisableEnvelopeOnlyPermanently(Sp1));
        Assert.ThrowsExactly<TestException>(() => c.LockProofSystemConfiguration(Sp1, VkId));

        Assert.AreEqual(VkId, c.GetLockedVerificationKey(Sp1),
            "rejected mutations must not change the exact locked VK");
        Assert.AreEqual(verifier, c.GetProofVerifier(Sp1),
            "rejected mutations must not change the terminal verifier");
        Assert.IsFalse(c.IsEnvelopeOnlyAllowed(Sp1)!.Value,
            "rejected mutations must not reopen envelope-only acceptance");

        Assert.IsFalse(c.IsProofSystemConfigurationLocked(Halo2)!.Value,
            "the lock is scoped per proof system");
        c.RegisterVerificationKey(Halo2, VkId, true);
        Assert.IsTrue(c.IsVerificationKeyRegistered(Halo2, VkId)!.Value,
            "other proof systems remain independently configurable");
    }

    [TestMethod]
    public void LockProofSystemConfiguration_NonOwner_FaultsWithoutChangingState()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var verifier = UInt160.Parse("0x" + new string('a', 40));
        c.RegisterVerificationKey(Sp1, VkId, true);
        c.RegisterProofVerifier(Sp1, verifier, true);
        c.DisableEnvelopeOnlyPermanently(Sp1);

        engine.SetTransactionSigners(UInt160.Parse("0x" + new string('9', 40)));
        Assert.ThrowsExactly<TestException>(() => c.LockProofSystemConfiguration(Sp1, VkId),
            "the irreversible configuration lock is owner-witness-gated");
        Assert.IsFalse(c.IsProofSystemConfigurationLocked(Sp1)!.Value);
        Assert.AreEqual(UInt256.Zero, c.GetLockedVerificationKey(Sp1));
    }

    [TestMethod]
    public void Verify_ConfigurationLockRejectsPreviouslyRegisteredAlternateKey()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var acceptVerifier = UInt160.Parse("0x" + new string('b', 40));
        engine.FromHash<Mock_ContractZkVerifier_Verifier>(acceptVerifier, mock =>
            mock.Setup(x => x.VerifyZkProof(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(), It.IsAny<byte[]?>())).Returns(true), checkExistence: false);

        c.RegisterVerificationKey(Sp1, VkId, true);
        c.RegisterVerificationKey(Sp1, AlternateVkId, true);
        c.RegisterProofVerifier(Sp1, acceptVerifier, true);
        c.DisableEnvelopeOnlyPermanently(Sp1);
        c.LockProofSystemConfiguration(Sp1, VkId);

        Assert.IsTrue(c.Verify(ValidCommitment(Sp1, VkId))!.Value,
            "the exact production VK still reaches the accepting terminal verifier");
        Assert.ThrowsExactly<TestException>(() => c.Verify(ValidCommitment(Sp1, AlternateVkId)),
            "a stale bootstrap VK must not remain accepted after the exact production VK is locked");
    }

    // ---------------------------------------------------------------------
    // Verify — envelope validation, VK-registration requirement, dispatch
    // ---------------------------------------------------------------------

    [TestMethod]
    public void Verify_RejectsUnregisteredVerificationKey()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        // No key registered, no verifier, no envelope-only: must reject at the VK-registration gate.
        Assert.ThrowsExactly<TestException>(() => c.Verify(ValidCommitment()),
            "an unregistered verification key must be rejected");
    }

    [TestMethod]
    public void Verify_RejectsNonZkProofType()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterVerificationKey(Sp1, VkId, true);

        var payload = BuildPayload(ZkPayloadVersion, Sp1, VkId, new byte[] { 0x01 });
        var commitment = BuildCommitment(/*proofType*/ 1, new byte[32], payload); // not ProofType.Zk
        Assert.ThrowsExactly<TestException>(() => c.Verify(commitment),
            "a commitment whose proofType is not Zk must be rejected");
    }

    [TestMethod]
    public void Verify_RejectsTruncatedCommitment_AndLengthMismatch()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // Shorter than ProofBytesOffset -> cannot even read the proof length.
        Assert.ThrowsExactly<TestException>(() => c.Verify(new byte[ProofBytesOffset - 1]),
            "a commitment shorter than the header must fault");

        // Header claims a payload length that doesn't match the actual trailing bytes.
        var payload = BuildPayload(ZkPayloadVersion, Sp1, VkId, new byte[] { 0x01, 0x02 });
        var commitment = BuildCommitment(ProofTypeZk, new byte[32], payload);
        var tampered = new byte[commitment.Length + 1]; // one extra trailing byte
        commitment.CopyTo(tampered.AsSpan(0, commitment.Length));
        // Restore the proofType/length header that BuildCommitment set (CopyTo already did).
        Assert.ThrowsExactly<TestException>(() => c.Verify(tampered),
            "a commitment whose declared proof length doesn't match its actual size must fault");
    }

    [TestMethod]
    public void Verify_RejectsEmptyProofPayload()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        // proofLen at ProofLenOffset == 0 (header is all zeros) and total length == ProofBytesOffset.
        var commitment = new byte[ProofBytesOffset];
        commitment[ProofTypeOffset] = ProofTypeZk;
        Assert.ThrowsExactly<TestException>(() => c.Verify(commitment),
            "an empty proof payload must be rejected");
    }

    [TestMethod]
    public void Verify_RejectsUnsupportedPayloadVersion()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterVerificationKey(Sp1, VkId, true);

        var payload = BuildPayload(/*version*/ 2, Sp1, VkId, new byte[] { 0x01 });
        var commitment = BuildCommitment(ProofTypeZk, new byte[32], payload);
        Assert.ThrowsExactly<TestException>(() => c.Verify(commitment),
            "an unsupported ZK payload version must be rejected");
    }

    [TestMethod]
    public void Verify_RejectsBadProofSystemInPayload()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        var payload = BuildPayload(ZkPayloadVersion, /*proofSystem*/ 7, VkId, new byte[] { 0x01 });
        var commitment = BuildCommitment(ProofTypeZk, new byte[32], payload);
        Assert.ThrowsExactly<TestException>(() => c.Verify(commitment),
            "an out-of-range proofSystem in the payload must be rejected");
    }

    [TestMethod]
    public void Verify_RejectsInnerProofLengthMismatch()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterVerificationKey(Sp1, VkId, true);

        // Build a well-formed payload (3-byte inner proof so the inner-proof-len field reads 3 and the
        // payload length is ZkPayloadProofBytesOffset + 3), then CORRUPT the inner-proof-length field at
        // ZkPayloadInnerProofLenOffset to claim a different size. The VK is registered and the corrupted
        // length is still > 0, so the only guard that can reject is the inner length-match
        // (ZkPayloadProofBytesOffset + proofSize == payload.Length).
        var payload = BuildPayload(ZkPayloadVersion, Sp1, VkId, new byte[] { 0xAA, 0xBB, 0xCC });
        // Claim 5 inner-proof bytes while only 3 are present -> 38 + 5 != 41.
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(ZkPayloadInnerProofLenOffset, 4), 5);
        var commitment = BuildCommitment(ProofTypeZk, new byte[32], payload);

        var ex = Assert.ThrowsExactly<TestException>(() => c.Verify(commitment),
            "an inner-proof-length field that disagrees with the actual inner-proof byte count must fault");
        StringAssert.Contains(ex.Message, "ZK payload proof length mismatch");
    }

    [TestMethod]
    public void Verify_RejectsEmptyInnerProof()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterVerificationKey(Sp1, VkId, true);

        // A payload whose inner-proof-length field is 0: total payload length is exactly
        // ZkPayloadProofBytesOffset (the header with no inner-proof trailer), valid version + proof
        // system + registered VK. The `proofLen > 0` guard (which runs before the length match) is the
        // decider — an empty inner proof can never be a real ZK proof.
        var payload = BuildPayload(ZkPayloadVersion, Sp1, VkId, Array.Empty<byte>());
        Assert.AreEqual(ZkPayloadProofBytesOffset, payload.Length,
            "an empty inner proof yields a header-only payload of exactly ZkPayloadProofBytesOffset bytes");
        var commitment = BuildCommitment(ProofTypeZk, new byte[32], payload);

        var ex = Assert.ThrowsExactly<TestException>(() => c.Verify(commitment),
            "an empty inner proof must be rejected");
        StringAssert.Contains(ex.Message, "inner proof empty");
    }

    [TestMethod]
    public void Verify_EnvelopeOnly_OnlyAcceptedWhenExplicitlyAllowed()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterVerificationKey(Sp1, VkId, true);

        // Registered key, but no verifier contract and envelope-only NOT allowed -> must reject.
        Assert.ThrowsExactly<TestException>(() => c.Verify(ValidCommitment()),
            "with no verifier configured and envelope-only disabled, Verify must fault");

        // Enable envelope-only for SP1 -> the same well-formed commitment now passes.
        c.SetEnvelopeOnlyAllowed(Sp1, true);
        Assert.IsTrue(c.Verify(ValidCommitment())!.Value,
            "with envelope-only explicitly allowed, a well-formed registered-key commitment is accepted");
    }

    [TestMethod]
    public void Verify_DispatchesToRegisteredVerifier_ReturnIsHonored()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterVerificationKey(Sp1, VkId, true);

        // A configured verifier takes precedence over envelope-only and its boolean result is returned.
        var acceptVerifier = UInt160.Parse("0x" + new string('b', 40));
        var rejectVerifier = UInt160.Parse("0x" + new string('c', 40));
        engine.FromHash<Mock_ContractZkVerifier_Verifier>(acceptVerifier, m =>
            m.Setup(x => x.VerifyZkProof(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(), It.IsAny<byte[]?>())).Returns(true), checkExistence: false);
        engine.FromHash<Mock_ContractZkVerifier_Verifier>(rejectVerifier, m =>
            m.Setup(x => x.VerifyZkProof(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(), It.IsAny<byte[]?>())).Returns(false), checkExistence: false);

        c.RegisterProofVerifier(Sp1, acceptVerifier, true);
        Assert.IsTrue(c.Verify(ValidCommitment())!.Value,
            "an accepting verifier contract makes Verify return true");

        // Re-point to a rejecting verifier -> Verify must return the verifier's false result, not true.
        c.RegisterProofVerifier(Sp1, rejectVerifier, true);
        Assert.IsFalse(c.Verify(ValidCommitment())!.Value,
            "a rejecting verifier contract makes Verify return false (no silent accept)");
    }

    [TestMethod]
    public void Verify_RegisteredVerifier_OverridesEnvelopeOnly()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterVerificationKey(Sp1, VkId, true);

        // Even with envelope-only enabled, a configured verifier is consulted and can reject.
        var rejectVerifier = UInt160.Parse("0x" + new string('d', 40));
        engine.FromHash<Mock_ContractZkVerifier_Verifier>(rejectVerifier, m =>
            m.Setup(x => x.VerifyZkProof(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>(),
                It.IsAny<byte[]?>(), It.IsAny<byte[]?>())).Returns(false), checkExistence: false);

        c.SetEnvelopeOnlyAllowed(Sp1, true);
        c.RegisterProofVerifier(Sp1, rejectVerifier, true);
        Assert.IsFalse(c.Verify(ValidCommitment())!.Value,
            "a configured verifier must take precedence over envelope-only mode");
    }

    [TestMethod]
    public void Verify_RejectsWhenKeyRegisteredForDifferentProofSystem()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        // Register the key under Halo2 but submit a commitment for SP1 -> not registered for SP1.
        c.RegisterVerificationKey(Halo2, VkId, true);
        c.SetEnvelopeOnlyAllowed(Sp1, true);

        Assert.ThrowsExactly<TestException>(() => c.Verify(ValidCommitment(Sp1, VkId)),
            "a key registered only for another proof system must not satisfy SP1 verification");
    }
}
