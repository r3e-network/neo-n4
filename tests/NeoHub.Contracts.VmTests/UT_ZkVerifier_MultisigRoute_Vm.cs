using Neo;
using Neo.SmartContract;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;
using System.Numerics;
using System.Security.Cryptography;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// End-to-end regression tests for the MULTISIG attestation route on
/// <c>NeoHub.ZkVerifier</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Defect 1 — the reserved proof-system byte.</b> <c>VerifyMultisigProof</c> dispatched
/// on proof-system byte 0, while <c>RegisterProofVerifier</c> rejects byte 0
/// (<c>proofSystem &gt; 0</c>). The two halves could therefore never meet:
/// <c>GetProofVerifier(0)</c> always returned the zero address, the dispatch branch was
/// never taken, and every multisig commitment verified as <c>false</c>. No committee-attested
/// batch could ever settle, and no operator action could repair it — the registration call
/// rejected the only byte the router looked up.
/// </para>
/// <para>
/// <b>Not a defect — the call flags.</b> The dispatch passes <c>CallFlags.ReadOnly</c> rather
/// than <c>CallFlags.All</c>, because the callee is <c>[Safe]</c> and only reads state. A
/// negative test confirmed this is a PRECISION improvement, not a behavioural fix: the VM
/// masks the write flags off for a safe callee
/// (<c>ApplicationEngine.CallContractInternal</c>: <c>if (method.Safe) flags &amp;=
/// ~(CallFlags.WriteStates | CallFlags.AllowNotify);</c>), so <c>CallFlags.All</c> behaves
/// identically here. The reserved proof-system byte above was the actual cause.
/// </para>
/// <para>
/// <b>Why a real contract.</b> A real-contract test proves the whole chain end to end —
/// dispatch to the registered verifier, the verifier's sequencer-registry check, and the
/// ECDSA signature verification — so a <c>true</c> result can only come from every link
/// actually working. (An earlier revision of this comment claimed a mock registered via
/// <c>FromHash(checkExistence: false)</c> was unresolvable by <c>Contract.Call</c>; that
/// claim was tested and DISPROVED — see
/// <see cref="StubVerifierInDedicatedSlot_IsCallableThroughTheRoute"/>. A stub verifier is
/// a valid pre-production pattern; these tests use the real contract because it proves
/// more, not because the stub cannot work.)
/// </para>
/// </remarks>
[TestClass]
public sealed class UT_ZkVerifier_MultisigRoute_Vm
{
    private const byte ProofTypeMultisig = 1;
    private const byte ProofSystemSp1 = 1;
    private const byte ProofTypeZk = 3;
    private const int OffPublicInputHash = 284;
    private const int OffProofType = 316;
    private const int OffProofLen = 317;
    private const int ProofBytesOffset = 321;

    // Multisig payload layout (NeoHub.MultisigVerifier).
    private const byte MultisigPayloadVersion = 1;
    private const int MultisigPayloadSignerCountOffset = 1;
    private const int MultisigPayloadSignaturesOffset = 3;
    private const int MultisigPublicKeySize = 33;
    private const int MultisigSignatureSize = 64;

    private const uint ChainId = 1001;

    private static byte[] R(byte fill) { var b = new byte[32]; for (var i = 0; i < 32; i++) b[i] = fill; return b; }

    private static (TestEngine engine, NeoHubZkVerifier verifier, UInt160 owner) DeployZkVerifier()
    {
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var owner = engine.Sender;
        var verifier = engine.Deploy<NeoHubZkVerifier>(NeoHubZkVerifier.Nef, NeoHubZkVerifier.Manifest, owner);
        return (engine, verifier, owner);
    }

    /// <summary>
    /// Build a commitment: a 321-byte header (chainId, public-input hash, proof type,
    /// proof length) followed by the proof payload.
    /// </summary>
    /// <remarks>
    /// <c>proofLen</c> is the length of the payload that follows the header. The ZK route
    /// reads a 38-byte payload header (inner-proof length at offset 34) before the proof
    /// bytes; the MULTISIG route reads the payload directly, so its length is the payload's
    /// own length.
    /// </remarks>
    private static byte[] BuildCommitment(byte proofType, byte[] payload, byte[] publicInputHash, uint chainId)
    {
        var proofLen = payload.Length;
        var c = new byte[ProofBytesOffset + proofLen];
        // The commitment layout starts with the chainId (4B LE) — the multisig verifier
        // reads it from offset 0 to select the sequencer registry it checks against.
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(0, 4), chainId);
        R(0x20).CopyTo(c.AsSpan(32, 32));
        publicInputHash.CopyTo(c.AsSpan(OffPublicInputHash, 32));
        c[OffProofType] = proofType;
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(OffProofLen, 4), proofLen);
        payload.CopyTo(c.AsSpan(ProofBytesOffset, payload.Length));
        return c;
    }

    /// <summary>Build a multisig payload carrying one (pubkey, signature) pair.</summary>
    private static byte[] BuildMultisigPayload(params (byte[] PublicKey, byte[] Signature)[] signers)
    {
        var payload = new byte[
            MultisigPayloadSignaturesOffset + signers.Length * (MultisigPublicKeySize + MultisigSignatureSize)];
        payload[0] = MultisigPayloadVersion;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(
            payload.AsSpan(MultisigPayloadSignerCountOffset, 2), (ushort)signers.Length);
        var pos = MultisigPayloadSignaturesOffset;
        foreach (var (publicKey, signature) in signers)
        {
            publicKey.CopyTo(payload, pos);
            signature.CopyTo(payload, pos + MultisigPublicKeySize);
            pos += MultisigPublicKeySize + MultisigSignatureSize;
        }
        return payload;
    }

    [TestMethod]
    public void MultisigVerifier_IsNotRegisteredInitially_AndReadsBackAsZero()
    {
        var (_, verifier, _) = DeployZkVerifier();

        // Nothing is registered out of the box: the route must fail closed, not
        // silently accept.
        Assert.AreEqual(UInt160.Zero, verifier.MultisigVerifier);
    }

    [TestMethod]
    public void RegisterMultisigVerifier_StoresAndRevokes()
    {
        var (_, verifier, _) = DeployZkVerifier();
        var external = UInt160.Parse("0x" + new string('a', 40));

        verifier.RegisterMultisigVerifier(external, true);
        Assert.AreEqual(external, verifier.MultisigVerifier);

        // Revocation clears the slot, so the route fails closed again.
        verifier.RegisterMultisigVerifier(external, false);
        Assert.AreEqual(UInt160.Zero, verifier.MultisigVerifier);
    }

    [TestMethod]
    public void RegisterMultisigVerifier_NonOwner_Faults()
    {
        // Deploy with an owner that is NOT the default sender, so the default sender is
        // a genuine non-owner for this call.
        var engine = new TestEngine(true);
        engine.Fee = 100_000_000_000L;
        var strangerOwner = UInt160.Parse("0x" + new string('9', 40));
        var verifier = engine.Deploy<NeoHubZkVerifier>(
            NeoHubZkVerifier.Nef, NeoHubZkVerifier.Manifest, strangerOwner);

        Assert.ThrowsExactly<TestException>(() =>
            verifier.RegisterMultisigVerifier(UInt160.Parse("0x" + new string('a', 40)), true));
        Assert.AreEqual(UInt160.Zero, verifier.MultisigVerifier);
    }

    [TestMethod]
    public void RegisterMultisigVerifier_RejectsZeroAddress()
    {
        var (_, verifier, _) = DeployZkVerifier();

        Assert.ThrowsExactly<TestException>(() =>
            verifier.RegisterMultisigVerifier(UInt160.Zero, true));
        Assert.AreEqual(UInt160.Zero, verifier.MultisigVerifier);
    }

    [TestMethod]
    public void MultisigCommitment_WithoutRegisteredVerifier_VerifiesFalse()
    {
        var (_, verifier, _) = DeployZkVerifier();
        var commitment = BuildCommitment(
            ProofTypeMultisig, new byte[] { 0x01, 0x02, 0x03, 0x04 }, R(0xA1), ChainId);

        // Fail closed: with no verifier registered the route must not approve anything.
        Assert.IsFalse(verifier.VerifyProof(commitment));
    }

    [TestMethod]
    public void MultisigCommitment_WithRegisteredVerifier_VerifiesThroughTheRealChain()
    {
        // THE key test: deploy the REAL verifier, register a REAL sequencer, sign with a
        // REAL key. A `true` result can only come from the route dispatching to the
        // verifier, the verifier checking the sequencer registry, and the signature
        // verifying — i.e. the whole route working.
        var (engine, verifier, owner) = DeployZkVerifier();

        // 1. GovernanceController with a one-member council.
        var council = new Neo.Cryptography.ECC.ECPoint[] { GenKey(1).PublicPoint() };
        var gc = engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { owner, council, (BigInteger)1, (BigInteger)100 });

        // 2. The real multisig verifier, bound to the GovernanceController.
        var multisig = engine.Deploy<NeoHubMultisigVerifier>(
            NeoHubMultisigVerifier.Nef, NeoHubMultisigVerifier.Manifest, owner);
        multisig.GovernanceController = gc.Hash;
        multisig.MinQuorum = 1;

        // 3. A sequencer key registered in the GovernanceController.
        var sequencerKey = GenKey(2);
        gc.RegisterSequencer(ChainId, sequencerKey.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));

        // 4. Register the verifier on the ZkVerifier's DEDICATED multisig slot.
        verifier.RegisterMultisigVerifier(multisig.Hash, true);
        Assert.AreEqual(multisig.Hash, verifier.MultisigVerifier);

        // 5. Sign the public-input hash with the sequencer key.
        var publicInputHash = R(0xA1);
        var signature = Sign(sequencerKey, publicInputHash);
        var payload = BuildMultisigPayload((sequencerKey.CompressedPublicKey, signature));

        // 6. The commitment must now verify.
        var commitment = BuildCommitment(ProofTypeMultisig, payload, publicInputHash, ChainId);
        Assert.IsTrue(verifier.VerifyProof(commitment),
            "the multisig route must reach the registered verifier and accept a valid quorum");
    }

    [TestMethod]
    public void MultisigCommitment_TwoDistinctSigners_MeetQuorumOfTwo_VerifiesTrue()
    {
        // The quorum-counting path with MORE THAN ONE signer. Every other test in this
        // file uses a single-signer payload, which exercises `validSignatures >= minQuorum`
        // only at the degenerate point where counting is indistinguishable from a boolean
        // "any valid signature". M-of-N semantics need at least two distinct signers and
        // a raised quorum.
        var (engine, verifier, owner) = DeployZkVerifier();
        var gc = engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { owner, new Neo.Cryptography.ECC.ECPoint[] { GenKey(1).PublicPoint() }, (BigInteger)1, (BigInteger)100 });
        var multisig = engine.Deploy<NeoHubMultisigVerifier>(
            NeoHubMultisigVerifier.Nef, NeoHubMultisigVerifier.Manifest, owner);
        multisig.GovernanceController = gc.Hash;
        multisig.MinQuorum = 2;

        var seqA = GenKey(2);
        var seqB = GenKey(3);
        gc.RegisterSequencer(ChainId, seqA.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));
        gc.RegisterSequencer(ChainId, seqB.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));
        verifier.RegisterMultisigVerifier(multisig.Hash, true);

        var publicInputHash = R(0xA1);
        var payload = BuildMultisigPayload(
            (seqA.CompressedPublicKey, Sign(seqA, publicInputHash)),
            (seqB.CompressedPublicKey, Sign(seqB, publicInputHash)));
        var commitment = BuildCommitment(ProofTypeMultisig, payload, publicInputHash, ChainId);

        Assert.IsTrue(verifier.VerifyProof(commitment),
            "two distinct registered signers must satisfy a quorum of two");
    }

    [TestMethod]
    public void MultisigCommitment_RepeatedSignerCannotStackVotes_QuorumOfTwoFails()
    {
        // THE 1-of-N prevention: the same (pubkey, signature) pair repeated signerCount
        // times must count as ONE vote. Without the distinct-signer dedup, a single
        // registered sequencer could satisfy any quorum by repeating itself, turning an
        // M-of-N committee gate into 1-of-N. The dedup was documented in the contract
        // but never exercised by a VM test (all other tests use one signer).
        var (engine, verifier, owner) = DeployZkVerifier();
        var gc = engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { owner, new Neo.Cryptography.ECC.ECPoint[] { GenKey(1).PublicPoint() }, (BigInteger)1, (BigInteger)100 });
        var multisig = engine.Deploy<NeoHubMultisigVerifier>(
            NeoHubMultisigVerifier.Nef, NeoHubMultisigVerifier.Manifest, owner);
        multisig.GovernanceController = gc.Hash;
        multisig.MinQuorum = 2;

        var lone = GenKey(2);
        gc.RegisterSequencer(ChainId, lone.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));
        verifier.RegisterMultisigVerifier(multisig.Hash, true);

        var publicInputHash = R(0xA1);
        var signature = Sign(lone, publicInputHash);
        // The SAME key and SAME signature, twice. signerCount=2, but only one committee
        // member is actually behind it.
        var payload = BuildMultisigPayload(
            (lone.CompressedPublicKey, signature),
            (lone.CompressedPublicKey, signature));
        var commitment = BuildCommitment(ProofTypeMultisig, payload, publicInputHash, ChainId);

        Assert.IsFalse(verifier.VerifyProof(commitment),
            "one committee member repeating its own signature must not reach a quorum of two");
    }

    [TestMethod]
    public void MultisigCommitment_OneValidOfQuorumTwo_VerifiesFalse()
    {
        // Quorum boundary from below: a single valid signature when two are required.
        // Complements TwoDistinctSigners (boundary from above).
        var (engine, verifier, owner) = DeployZkVerifier();
        var gc = engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { owner, new Neo.Cryptography.ECC.ECPoint[] { GenKey(1).PublicPoint() }, (BigInteger)1, (BigInteger)100 });
        var multisig = engine.Deploy<NeoHubMultisigVerifier>(
            NeoHubMultisigVerifier.Nef, NeoHubMultisigVerifier.Manifest, owner);
        multisig.GovernanceController = gc.Hash;
        multisig.MinQuorum = 2;

        var seqA = GenKey(2);
        gc.RegisterSequencer(ChainId, seqA.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));
        verifier.RegisterMultisigVerifier(multisig.Hash, true);

        var publicInputHash = R(0xA1);
        var payload = BuildMultisigPayload(
            (seqA.CompressedPublicKey, Sign(seqA, publicInputHash)));
        var commitment = BuildCommitment(ProofTypeMultisig, payload, publicInputHash, ChainId);

        Assert.IsFalse(verifier.VerifyProof(commitment),
            "one valid signature must not satisfy a quorum of two");
    }

    [TestMethod]
    public void MultisigCommitment_InvalidSignaturesDoNotCount_MixedEntriesReachQuorum()
    {
        // Mixed entries: two valid signatures plus one structurally-valid-but-wrong
        // signature. Only the valid ones may count, and an invalid entry must not abort
        // the loop or poison the count. The garbage signature is over a DIFFERENT
        // message, so the ECDSA math is well-formed but does not match publicInputHash.
        var (engine, verifier, owner) = DeployZkVerifier();
        var gc = engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { owner, new Neo.Cryptography.ECC.ECPoint[] { GenKey(1).PublicPoint() }, (BigInteger)1, (BigInteger)100 });
        var multisig = engine.Deploy<NeoHubMultisigVerifier>(
            NeoHubMultisigVerifier.Nef, NeoHubMultisigVerifier.Manifest, owner);
        multisig.GovernanceController = gc.Hash;
        multisig.MinQuorum = 2;

        var seqA = GenKey(2);
        var seqB = GenKey(3);
        var seqC = GenKey(4);
        gc.RegisterSequencer(ChainId, seqA.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));
        gc.RegisterSequencer(ChainId, seqB.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));
        gc.RegisterSequencer(ChainId, seqC.PublicPoint(), UInt160.Parse("0x" + new string('e', 40)));
        verifier.RegisterMultisigVerifier(multisig.Hash, true);

        var publicInputHash = R(0xA1);
        var payload = BuildMultisigPayload(
            (seqA.CompressedPublicKey, Sign(seqA, publicInputHash)),
            // seqC signed a DIFFERENT message: the signature is a real secp256r1 signature,
            // so it exercises "crypto-valid but wrong message" rather than garbage bytes.
            (seqC.CompressedPublicKey, Sign(seqC, R(0xB2))),
            (seqB.CompressedPublicKey, Sign(seqB, publicInputHash)));
        var commitment = BuildCommitment(ProofTypeMultisig, payload, publicInputHash, ChainId);

        Assert.IsTrue(verifier.VerifyProof(commitment),
            "two valid signatures among three entries must satisfy a quorum of two");
    }

    [TestMethod]
    public void MultisigCommitment_WithUnregisteredSigner_VerifiesFalse()
    {
        // The route reaches the verifier, but the verifier rejects the signer — proving
        // the result is produced by the real chain, not by a short-circuit.
        var (engine, verifier, owner) = DeployZkVerifier();

        var council = new Neo.Cryptography.ECC.ECPoint[] { GenKey(1).PublicPoint() };
        var gc = engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { owner, council, (BigInteger)1, (BigInteger)100 });

        var multisig = engine.Deploy<NeoHubMultisigVerifier>(
            NeoHubMultisigVerifier.Nef, NeoHubMultisigVerifier.Manifest, owner);
        multisig.GovernanceController = gc.Hash;
        multisig.MinQuorum = 1;

        // NOTE: the signer key is deliberately NOT registered as a sequencer.
        var signerKey = GenKey(3);
        verifier.RegisterMultisigVerifier(multisig.Hash, true);

        var publicInputHash = R(0xA1);
        var payload = BuildMultisigPayload(
            (signerKey.CompressedPublicKey, Sign(signerKey, publicInputHash)));
        var commitment = BuildCommitment(ProofTypeMultisig, payload, publicInputHash, ChainId);

        Assert.IsFalse(verifier.VerifyProof(commitment),
            "an unregistered signer must not satisfy the quorum");
    }

    [TestMethod]
    public void RegisterProofVerifier_SystemZero_IsRejected()
    {
        var (_, verifier, _) = DeployZkVerifier();
        var external = UInt160.Parse("0x" + new string('b', 40));

        // Proof system 0 is reserved for the (now separately slotted) multisig route.
        // Rejecting it here is what makes byte 0 unambiguously mean "unset" in the
        // proof-system key space.
        Assert.ThrowsExactly<TestException>(() =>
            verifier.RegisterProofVerifier(0, external, true));

        // And it must not have created an entry.
        Assert.AreEqual(UInt160.Zero, verifier.GetProofVerifier(0));
    }

    [TestMethod]
    public void ProofSystemOne_StillRegisters_NormalisingThePreviouslyAmbiguousSlot()
    {
        var (_, verifier, _) = DeployZkVerifier();
        var external = UInt160.Parse("0x" + new string('c', 40));

        // The ZK route is unaffected by the multisig split: system 1 registers normally
        // and reads back through the proof-system key space.
        verifier.RegisterProofVerifier(1, external, true);
        Assert.AreEqual(external, verifier.GetProofVerifier(1));

        // The multisig slot is a DIFFERENT slot — registering system 1 must not make the
        // multisig route resolvable.
        Assert.AreEqual(UInt160.Zero, verifier.MultisigVerifier);
    }

    [TestMethod]
    public void ZkCommitment_IsUnaffectedByMultisigRegistration()
    {
        var (engine, verifier, owner) = DeployZkVerifier();

        var council = new Neo.Cryptography.ECC.ECPoint[] { GenKey(1).PublicPoint() };
        var gc = engine.Deploy<NeoHubGovernanceController>(
            NeoHubGovernanceController.Nef, NeoHubGovernanceController.Manifest,
            new object[] { owner, council, (BigInteger)1, (BigInteger)100 });
        var multisig = engine.Deploy<NeoHubMultisigVerifier>(
            NeoHubMultisigVerifier.Nef, NeoHubMultisigVerifier.Manifest, owner);
        multisig.GovernanceController = gc.Hash;
        multisig.MinQuorum = 1;
        verifier.RegisterMultisigVerifier(multisig.Hash, true);

        // A ZK commitment must still fail closed with no SP1 verifier registered —
        // the multisig slot must not accidentally satisfy the ZK dispatch.
        var zkCommitment = BuildCommitment(
            ProofTypeZk, new byte[] { 0x01, 0x02, 0x03, 0x04 }, R(0xA1), ChainId);
        Assert.IsFalse(verifier.VerifyProof(zkCommitment));
    }

    // ─────────────────────────── key helpers ───────────────────────────

    /// <summary>
    /// A secp256r1 key: the .NET signer plus the compressed public point in the 33-byte
    /// form the multisig payload carries.
    /// </summary>
    private sealed record TestKey(ECDsa Signer, byte[] CompressedPublicKey)
    {
        /// <summary>The public point in the form the GovernanceController's ABI expects.</summary>
        public Neo.Cryptography.ECC.ECPoint PublicPoint() =>
            Neo.Cryptography.ECC.ECPoint.DecodePoint(
                CompressedPublicKey, Neo.Cryptography.ECC.ECCurve.Secp256r1);
    }

    private static TestKey GenKey(int scalar)
    {
        var privateKey = new byte[32];
        privateKey[31] = (byte)scalar;
        // Neo's own curve math produces the authoritative public point.
        var neoPoint = Neo.Cryptography.ECC.ECCurve.Secp256r1.G * privateKey;
        var compressed = neoPoint.EncodePoint(true);

        // The .NET signer must use the SAME private scalar, so both sides agree on the key.
        var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = System.Security.Cryptography.ECCurve.NamedCurves.nistP256,
            D = privateKey,
        });
        return new TestKey(ecdsa, compressed);
    }

    /// <summary>Sign a 32-byte message with secp256r1 + SHA-256, as the verifier expects.</summary>
    private static byte[] Sign(TestKey key, byte[] message)
    {
        // `SignData` with the default `DSASignatureFormat` produces the IEEE P1363 form:
        // a 64-byte raw r || s with each component in LITTLE-endian order.
        //
        // That is exactly what Neo's `Crypto.VerifySignature` consumes on this platform:
        // its non-OSX branch calls `ecdsa.VerifyData(message, signature, SHA256)`, and
        // .NET's `VerifyData` accepts the IEEE P1363 encoding (it rejects DER).
        var signature = key.Signer.SignData(message, HashAlgorithmName.SHA256);
        if (signature.Length != 64)
        {
            throw new InvalidOperationException(
                $"expected a 64-byte raw signature, got {signature.Length} bytes");
        }
        return signature;
    }


    [TestMethod]
    public void SetEnvelopeOnlyAllowed_RejectsTheReservedByte()
    {
        // The reserved byte 0 means "unset" in the proof-system key space. RegisterProofVerifier
        // and RegisterVerificationKey both reject it; SetEnvelopeOnlyAllowed must too, because
        // the multisig route's fail-open check reads exactly that byte. Before this guard, an
        // owner call `SetEnvelopeOnlyAllowed(0, true)` made the deployed contract accept a
        // multisig commitment with ZERO signatures (verified by an injected-fault run that
        // returned `VerifyProof=True` for a garbage payload), and byte 0 has no configuration
        // lock to close the hole with — verification keys cannot be registered for it.
        var (_, verifier, _) = DeployZkVerifier();

        Assert.ThrowsExactly<TestException>(() =>
            verifier.SetEnvelopeOnlyAllowed(0, true));
        Assert.ThrowsExactly<TestException>(() =>
            verifier.SetEnvelopeOnlyAllowed(0, false));

        // The flag must remain unset for the reserved byte.
        Assert.IsFalse(verifier.IsEnvelopeOnlyAllowed(0));

        // Legitimate proof systems are unaffected.
        verifier.SetEnvelopeOnlyAllowed(ProofSystemSp1, true);
        Assert.IsTrue(verifier.IsEnvelopeOnlyAllowed(ProofSystemSp1));
        verifier.SetEnvelopeOnlyAllowed(ProofSystemSp1, false);
        Assert.IsFalse(verifier.IsEnvelopeOnlyAllowed(ProofSystemSp1));
    }

    [TestMethod]
    public void MultisigRoute_HasNoEnvelopeOnlyBypass_AnyGarbageCommitmentFails()
    {
        // Even with every envelope-only flag set that CAN be set, the multisig route must not
        // develop a bypass: it delegates to the external verifier or returns false. The
        // zero-signature payload below would have verified as TRUE before the fix.
        var (_, verifier, _) = DeployZkVerifier();
        verifier.SetEnvelopeOnlyAllowed(1, true);
        verifier.SetEnvelopeOnlyAllowed(2, true);

        var payload = BuildMultisigPayload((GenKey(9).CompressedPublicKey, new byte[64]));
        var commitment = BuildCommitment(ProofTypeMultisig, payload, R(0xA1), ChainId);

        Assert.IsFalse(verifier.VerifyProof(commitment),
            "the multisig route must have no envelope-only bypass — a zero-signature "
            + "commitment must fail closed regardless of any flag");
    }

    /// <summary>A stub multisig verifier: accepts everything. Lives only in tests.</summary>
    public abstract class StubMultisigVerifier(SmartContractInitialize initialize) : SmartContract(initialize)
    {
        [System.ComponentModel.DisplayName("verifyMultisigProof")]
        public abstract bool VerifyMultisigProof(byte[]? commitmentBytes);
    }

    [TestMethod]
    public void StubVerifierInDedicatedSlot_IsCallableThroughTheRoute()
    {
        // The recommended pre-production pattern for exercising the multisig route's success
        // path: register a stub verifier (verifyMultisigProof returns true) in the dedicated
        // slot. This test pins that the pattern WORKS — `FromHash(checkExistence: false)`
        // registers a mock that `Contract.Call` can resolve and invoke.
        //
        // (An earlier audit note claimed this mock path was unresolvable by Contract.Call.
        // That claim was tested and disproved: a mock in the dedicated slot is callable, and
        // the round-2 route failure was caused by the empty proof-system slot — the P2-3
        // defect itself — not by the mock. Real-contract tests remain the default because
        // they prove the whole chain end to end; this test proves the stub pattern is a
        // valid complement, not a dead end.)
        var (engine, verifier, owner) = DeployZkVerifier();

        var stubHash = UInt160.Parse("0x" + new string('f', 40));
        engine.FromHash<StubMultisigVerifier>(stubHash, m =>
            m.Setup(v => v.VerifyMultisigProof(Moq.It.IsAny<byte[]>())).Returns(true),
            checkExistence: false);

        verifier.RegisterMultisigVerifier(stubHash, true);

        var payload = BuildMultisigPayload((GenKey(9).CompressedPublicKey, new byte[64]));
        var commitment = BuildCommitment(ProofTypeMultisig, payload, R(0xA1), ChainId);

        Assert.IsTrue(verifier.VerifyProof(commitment),
            "a stub verifier in the dedicated slot must be callable through the route — "
            + "this is the supported pre-production testing pattern");
    }

}
