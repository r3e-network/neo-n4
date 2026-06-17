using System.ComponentModel;
using System.Numerics;
using Moq;
using Neo;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>Minimal GovernanceController surface so the council-veto proposal path can be mocked.
/// Exposes BOTH read-only checks the verifier consults on the same hash for committee-rotation
/// proposals: <c>isApprovedAndTimelocked</c> (council multisig + timelock) and
/// <c>matchesProposalPayload</c> (binds the approved proposal payload to the exact action bytes so
/// the council vote is not a one-time blank check on which committee installs on which chain).</summary>
public abstract class Mock_MpcCommitteeVerifier_GovernanceController(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedAction);
}

/// <summary>
/// VM-level tests for NeoHub.MpcCommitteeVerifier — the on-chain M-of-N secp256k1/ed25519 committee
/// verifier for inbound cross-foreign-chain messages, executed in a real NeoVM. Pins the
/// security-critical invariants:
///  - ownership / governance wiring: owner witness gating (positive AND negative), zero-address
///    rejection, ownership transfer actually moves the effective authority.
///  - RegisterCommittee*: owner-gated, the 0xE0_xx_xx_xx foreign-namespace prefix guard, threshold
///    range (positive, ≤ size), curveTag whitelist, and blob-length / member-blob shape validation.
///  - Governance-mediated rotation: GovernanceController must be wired, the proposal must be
///    approved + timelocked AND its payload must bind to the exact action bytes, and a consumed
///    proposalId is permanently replay-protected (one council vote = one rotation).
///  - VerifyInboundMessage dispatch guards: message/proof length floors, signed-domain binding
///    (signed externalChainId == argument), direction == ForeignToNeo, deadline expiry, committee
///    presence, sigCount ≥ threshold, and proof-length consistency with declared sigCount. These are
///    the input-validation defenses that gate the M-of-N attestation surface before any crypto runs.
///  - GetSignerMember binding written by the *WithMembers path (required so the Phase-C fraud
///    verifier can attribute equivocation to a slashable bond holder).
/// </summary>
[TestClass]
public class UT_MpcCommitteeVerifier_Vm
{
    // Foreign chain ids MUST carry the 0xE0_xx_xx_xx namespace prefix.
    private const uint ForeignChain = 0xE0000001U;
    private const uint OtherForeignChain = 0xE0000002U;
    private const uint NonForeignChain = 0x00000001U; // missing the 0xE0 prefix

    private const byte CurveSecp256k1 = 1;
    private const byte CurveEd25519 = 2;

    private static readonly UInt160 GcHash = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 GcHashB = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 OtherOwner = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 MemberA = UInt160.Parse("0x" + new string('c', 40));
    private static readonly UInt160 MemberB = UInt160.Parse("0x" + new string('d', 40));

    /// <summary>Deploy with owner defaulting to engine.Sender so the owner witness check passes; pass
    /// an explicit <paramref name="owner"/> to exercise the negative authorization paths.</summary>
    private static NeoHubMpcCommitteeVerifier Deploy(TestEngine engine, UInt160? owner = null)
    {
        var o = owner ?? engine.Sender;
        return engine.Deploy<NeoHubMpcCommitteeVerifier>(
            NeoHubMpcCommitteeVerifier.Nef, NeoHubMpcCommitteeVerifier.Manifest, o);
    }

    /// <summary>A well-formed secp256k1 committee blob: <paramref name="size"/> distinct 33-byte
    /// compressed pubkeys. The bytes need not be valid curve points — the registration path only
    /// validates length/shape; curve validity is exercised only by the crypto path which these
    /// guard-focused tests deliberately fault before.</summary>
    private static byte[] Secp256k1Blob(int size)
    {
        var blob = new byte[size * 33];
        for (var i = 0; i < blob.Length; i++) blob[i] = (byte)(i + 1);
        // Make the leading byte of each pubkey distinct so FindCommitteeIndex can disambiguate.
        for (var i = 0; i < size; i++) blob[i * 33] = (byte)(0x02 + i);
        return blob;
    }

    /// <summary>Canonical ExternalCrossChainMessage prefix: 102 fixed bytes. externalChainId at
    /// offset 0 (4B LE), direction byte at offset 16, deadlineUnixSeconds at offset 57 (8B LE).</summary>
    private static byte[] Message(uint externalChainId, byte direction, ulong deadlineUnixSeconds)
    {
        var m = new byte[102];
        m[0] = (byte)externalChainId;
        m[1] = (byte)(externalChainId >> 8);
        m[2] = (byte)(externalChainId >> 16);
        m[3] = (byte)(externalChainId >> 24);
        m[16] = direction;
        m[57] = (byte)deadlineUnixSeconds;
        m[58] = (byte)(deadlineUnixSeconds >> 8);
        m[59] = (byte)(deadlineUnixSeconds >> 16);
        m[60] = (byte)(deadlineUnixSeconds >> 24);
        m[61] = (byte)(deadlineUnixSeconds >> 32);
        m[62] = (byte)(deadlineUnixSeconds >> 40);
        m[63] = (byte)(deadlineUnixSeconds >> 48);
        m[64] = (byte)(deadlineUnixSeconds >> 56);
        return m;
    }

    /// <summary>A proof carrying <paramref name="sigCount"/> entries of (33B pubkey + 64B sig) for
    /// secp256k1, with a leading 2B little-endian sigCount header. Lengths are self-consistent.</summary>
    private static byte[] Secp256k1Proof(int sigCount)
    {
        const int perSig = 33 + 64;
        var p = new byte[2 + sigCount * perSig];
        p[0] = (byte)sigCount;
        p[1] = (byte)(sigCount >> 8);
        return p;
    }

    // ---------------------------------------------------------------------------------------------
    // Deployment & ownership
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deploy_SetsOwner_AndBridgeKindIsMpc()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.AreEqual(engine.Sender, c.Owner, "deploy must record the owner");
        Assert.AreEqual((BigInteger)1, c.BridgeKind!, "BridgeKind must be 1 (MPC) per the registry dispatch convention");
        Assert.AreEqual(UInt160.Zero, c.GovernanceController, "no governance controller wired at deploy");
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => Deploy(engine, owner: UInt160.Zero),
            "deploy must reject a zero/invalid owner");
    }

    [TestMethod]
    public void SetOwner_OwnerGated_TransfersAuthority()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender

        // New owner must be a real address.
        Assert.ThrowsExactly<TestException>(() => c.Owner = UInt160.Zero, "zero new owner rejected");

        c.Owner = OtherOwner;
        Assert.AreEqual(OtherOwner, c.Owner, "ownership transfer must move the recorded owner");

        // The previous signer (engine.Sender) is no longer the owner: a further owner-gated call faults.
        Assert.ThrowsExactly<TestException>(() => c.Owner = engine.Sender,
            "the old owner must lose authority after transfer");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner); // owner is NOT the signer

        Assert.ThrowsExactly<TestException>(() => c.Owner = engine.Sender,
            "SetOwner is owner-witness-gated");
    }

    [TestMethod]
    public void SetGovernanceController_OwnerGated_RejectsZero_StoresHash()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.GovernanceController = UInt160.Zero,
            "zero governance controller rejected");

        c.GovernanceController = GcHash;
        Assert.AreEqual(GcHash, c.GovernanceController, "governance controller hash must be stored");
    }

    [TestMethod]
    public void SetGovernanceController_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner);

        Assert.ThrowsExactly<TestException>(() => c.GovernanceController = GcHash,
            "SetGovernanceController is owner-witness-gated");
    }

    // ---------------------------------------------------------------------------------------------
    // RegisterCommittee — owner-gated direct registration
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void RegisterCommittee_OwnerGated_StoresHeaderAndBlob()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var blob = Secp256k1Blob(3);

        c.RegisterCommittee(ForeignChain, 2, CurveSecp256k1, blob);

        var header = c.GetCommitteeHeader(ForeignChain)!;
        Assert.AreEqual(3, header.Length, "header is [threshold,size,curveTag]");
        Assert.AreEqual(2, header[0], "threshold");
        Assert.AreEqual(3, header[1], "size");
        Assert.AreEqual(CurveSecp256k1, header[2], "curveTag");

        var stored = c.GetCommittee(ForeignChain)!;
        Assert.AreEqual(3 + blob.Length, stored.Length, "stored blob is 3-byte header + pubkeys");
    }

    [TestMethod]
    public void RegisterCommittee_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner);

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ForeignChain, 2, CurveSecp256k1, Secp256k1Blob(3)),
            "RegisterCommittee is owner-witness-gated");
        Assert.AreEqual(0, c.GetCommittee(ForeignChain)!.Length, "rejected registration must not set state");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsNonForeignNamespacePrefix()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // A chain id that does not carry the 0xE0_xx_xx_xx foreign-namespace prefix must be rejected,
        // so a foreign committee can never be installed on a Neo-native / unallocated id.
        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(NonForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1)),
            "externalChainId must use the 0xE0 foreign-namespace prefix");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsZeroThreshold_AndThresholdAboveSize()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ForeignChain, 0, CurveSecp256k1, Secp256k1Blob(3)),
            "threshold must be positive");
        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ForeignChain, 4, CurveSecp256k1, Secp256k1Blob(3)),
            "threshold cannot exceed committee size");
    }

    [TestMethod]
    public void RegisterCommittee_RejectsUnknownCurve_EmptyBlob_AndBadBlobLength()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ForeignChain, 1, 3, Secp256k1Blob(1)),
            "curveTag must be 1 (secp256k1) or 2 (ed25519)");
        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, new byte[0]),
            "empty committee blob rejected");
        // 33-byte pubkey curve but blob length not a multiple of 33.
        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, new byte[40]),
            "blob length must be a whole number of pubkeys for the curve");
    }

    [TestMethod]
    public void RegisterCommittee_Ed25519_UsesThirtyTwoBytePubkeys()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // ed25519 keys are 32 bytes: a 64-byte blob is exactly 2 keys.
        c.RegisterCommittee(ForeignChain, 1, CurveEd25519, new byte[64]);
        var header = c.GetCommitteeHeader(ForeignChain)!;
        Assert.AreEqual(2, header[1], "two 32-byte ed25519 keys");
        Assert.AreEqual(CurveEd25519, header[2], "curveTag ed25519");

        // 40 bytes is not a multiple of 32 -> rejected for ed25519.
        Assert.ThrowsExactly<TestException>(() => c.RegisterCommittee(OtherForeignChain, 1, CurveEd25519, new byte[40]),
            "ed25519 blob must be a multiple of 32");
    }

    [TestMethod]
    public void RegisterCommittee_ReplaceClearsStaleSignerMemberBindings()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // First register WITH members so signer bindings exist.
        var blob = Secp256k1Blob(2);
        var members = new byte[40];
        MemberA.GetSpan().CopyTo(members.AsSpan(0, 20));
        MemberB.GetSpan().CopyTo(members.AsSpan(20, 20));
        c.RegisterCommitteeWithMembers(ForeignChain, 1, CurveSecp256k1, blob, members);
        Assert.AreEqual(MemberA, c.GetSignerMember(ForeignChain, 0), "member binding written");

        // Re-register the SAME chain via the memberless path: stale bindings must be cleared so a
        // rotated committee can't inherit the previous committee's slashable bond holders.
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(2));
        Assert.AreEqual(UInt160.Zero, c.GetSignerMember(ForeignChain, 0),
            "rotation must clear stale signer→member bindings");
    }

    // ---------------------------------------------------------------------------------------------
    // RegisterCommitteeWithMembers — signer→bond-holder binding
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void RegisterCommitteeWithMembers_BindsEachSigner_OwnerGated()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        var blob = Secp256k1Blob(2);
        var members = new byte[40];
        MemberA.GetSpan().CopyTo(members.AsSpan(0, 20));
        MemberB.GetSpan().CopyTo(members.AsSpan(20, 20));

        c.RegisterCommitteeWithMembers(ForeignChain, 2, CurveSecp256k1, blob, members);
        Assert.AreEqual(MemberA, c.GetSignerMember(ForeignChain, 0), "signer 0 → member A");
        Assert.AreEqual(MemberB, c.GetSignerMember(ForeignChain, 1), "signer 1 → member B");
    }

    [TestMethod]
    public void RegisterCommitteeWithMembers_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner);

        var members = new byte[20];
        MemberA.GetSpan().CopyTo(members.AsSpan(0, 20));
        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeWithMembers(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1), members),
            "RegisterCommitteeWithMembers is owner-witness-gated");
    }

    [TestMethod]
    public void RegisterCommitteeWithMembers_RejectsMemberBlobShapeAndZeroMember()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var blob = Secp256k1Blob(2);

        // memberBlob must be size×20: a 20-byte blob for a 2-signer committee is wrong.
        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeWithMembers(ForeignChain, 1, CurveSecp256k1, blob, new byte[20]),
            "memberBlob length must be size × 20");

        // A zero-address member slot is rejected (an unslashable bond holder is useless to the fraud verifier).
        var membersWithZero = new byte[40];
        MemberA.GetSpan().CopyTo(membersWithZero.AsSpan(0, 20)); // slot 1 left all-zero
        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeWithMembers(ForeignChain, 1, CurveSecp256k1, blob, membersWithZero),
            "zero-address member slot rejected");
    }

    // ---------------------------------------------------------------------------------------------
    // Governance-mediated rotation (RegisterCommitteeViaProposal / *WithMembersViaProposal)
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void RegisterCommitteeViaProposal_RequiresGovernanceControllerWired()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // GovernanceController NOT wired

        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeViaProposal(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1), 7),
            "proposal path needs the GovernanceController wired first");
    }

    [TestMethod]
    public void RegisterCommitteeViaProposal_NotApproved_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        engine.FromHash<Mock_MpcCommitteeVerifier_GovernanceController>(GcHash, m =>
        {
            m.Setup(x => x.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(false);
            m.Setup(x => x.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true);
        }, checkExistence: false);
        c.GovernanceController = GcHash;

        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeViaProposal(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1), 7),
            "an un-approved / un-timelocked proposal must not install a committee");
        Assert.AreEqual(0, c.GetCommittee(ForeignChain)!.Length, "rejected proposal must not set state");
    }

    [TestMethod]
    public void RegisterCommitteeViaProposal_PayloadMismatch_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // Approved + timelocked but the proposal payload does NOT bind to the action args. This pins
        // the "council voted on different bytes" / blank-check defense: an approved vote must not let
        // an attacker install an ARBITRARY committee on an arbitrary chain.
        engine.FromHash<Mock_MpcCommitteeVerifier_GovernanceController>(GcHash, m =>
        {
            m.Setup(x => x.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(x => x.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(false);
        }, checkExistence: false);
        c.GovernanceController = GcHash;

        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeViaProposal(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1), 7),
            "a proposal whose payload does not match the action args must be rejected");
        Assert.AreEqual(0, c.GetCommittee(ForeignChain)!.Length);
    }

    [TestMethod]
    public void RegisterCommitteeViaProposal_Approved_Bound_Installs_ThenReplayProtected()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        engine.FromHash<Mock_MpcCommitteeVerifier_GovernanceController>(GcHash, m =>
        {
            m.Setup(x => x.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(x => x.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true);
        }, checkExistence: false);
        c.GovernanceController = GcHash;

        c.RegisterCommitteeViaProposal(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(2), 42);
        Assert.IsTrue(c.GetCommittee(ForeignChain)!.Length > 0, "approved+bound proposal installs the committee");

        // Replay protection: the SAME proposalId can never be applied twice, even though the GC mock
        // would still approve it — one council vote installs exactly one committee.
        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeViaProposal(OtherForeignChain, 1, CurveSecp256k1, Secp256k1Blob(2), 42),
            "a consumed proposalId must not be applied again");
        Assert.AreEqual(0, c.GetCommittee(OtherForeignChain)!.Length, "replayed proposal must not set state");

        // A fresh proposalId is independent and still applicable.
        c.RegisterCommitteeViaProposal(OtherForeignChain, 1, CurveSecp256k1, Secp256k1Blob(2), 43);
        Assert.IsTrue(c.GetCommittee(OtherForeignChain)!.Length > 0, "a new proposalId may install");
    }

    [TestMethod]
    public void RegisterCommitteeWithMembersViaProposal_Approved_Bound_Installs_WithBindings_ThenReplayProtected()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // Use a DISTINCT hash from the other governance tests to keep mock setups isolated; cannot
        // mock the same hash twice across behaviors in a single method, but distinct methods are fine.
        engine.FromHash<Mock_MpcCommitteeVerifier_GovernanceController>(GcHashB, m =>
        {
            m.Setup(x => x.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(x => x.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>())).Returns(true);
        }, checkExistence: false);
        c.GovernanceController = GcHashB;

        var blob = Secp256k1Blob(2);
        var members = new byte[40];
        MemberA.GetSpan().CopyTo(members.AsSpan(0, 20));
        MemberB.GetSpan().CopyTo(members.AsSpan(20, 20));

        c.RegisterCommitteeWithMembersViaProposal(ForeignChain, 2, CurveSecp256k1, blob, members, 100);
        Assert.AreEqual(MemberA, c.GetSignerMember(ForeignChain, 0), "proposal path must also write signer→member bindings");
        Assert.AreEqual(MemberB, c.GetSignerMember(ForeignChain, 1));

        // Same proposalId is replay-protected.
        Assert.ThrowsExactly<TestException>(
            () => c.RegisterCommitteeWithMembersViaProposal(OtherForeignChain, 2, CurveSecp256k1, blob, members, 100),
            "a consumed proposalId must not be applied again");
    }

    [TestMethod]
    public void BuildRegisterCommitteeAction_IsDeterministicAndBindsArgs()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        var blob = Secp256k1Blob(2);

        var a1 = c.BuildRegisterCommitteeAction(ForeignChain, 2, CurveSecp256k1, blob)!;
        var a2 = c.BuildRegisterCommitteeAction(ForeignChain, 2, CurveSecp256k1, blob)!;
        CollectionAssert.AreEqual(a1, a2, "action encoding must be deterministic for identical args");

        // Changing any bound field changes the action bytes — that's what makes payload-binding meaningful.
        var aDiffThreshold = c.BuildRegisterCommitteeAction(ForeignChain, 1, CurveSecp256k1, blob)!;
        CollectionAssert.AreNotEqual(a1, aDiffThreshold, "threshold is part of the bound payload");
        var aDiffChain = c.BuildRegisterCommitteeAction(OtherForeignChain, 2, CurveSecp256k1, blob)!;
        CollectionAssert.AreNotEqual(a1, aDiffChain, "externalChainId is part of the bound payload");
    }

    // ---------------------------------------------------------------------------------------------
    // VerifyInboundMessage — dispatch input-validation guards (executed before crypto)
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void VerifyInboundMessage_RejectsShortMessageAndShortProof()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1));

        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, new byte[101], Secp256k1Proof(1)),
            "messageBytes shorter than the 102-byte layout must be rejected");
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, Message(ForeignChain, 2, 0), new byte[1]),
            "proofBytes shorter than 2 bytes must be rejected");
    }

    [TestMethod]
    public void VerifyInboundMessage_RejectsSignedDomainMismatch()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1));

        // Message is signed for OtherForeignChain but dispatched under ForeignChain: the argument must
        // match the chain id embedded in the signed bytes, else a watcher attestation for chain X could
        // be replayed as an attestation for chain Y.
        var msg = Message(OtherForeignChain, 2, 0);
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, Secp256k1Proof(1)),
            "externalChainId argument must match the signed message domain");
    }

    [TestMethod]
    public void VerifyInboundMessage_RejectsWrongDirection()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1));

        // direction byte 1 (NeoToForeign) instead of 2 (ForeignToNeo) — inbound verifier only accepts
        // ForeignToNeo, so an outbound message can't be replayed as an inbound credit.
        var msg = Message(ForeignChain, 1, 0);
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, Secp256k1Proof(1)),
            "direction must be 2 (ForeignToNeo)");
    }

    [TestMethod]
    public void VerifyInboundMessage_RejectsExpiredDeadline()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1));

        // A deadline of 1 (unix second) is far in the past relative to the test chain clock, so the
        // expiry guard must reject it before any signature work.
        var msg = Message(ForeignChain, 2, deadlineUnixSeconds: 1);
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, Secp256k1Proof(1)),
            "an expired external bridge message must be rejected");
    }

    [TestMethod]
    public void VerifyInboundMessage_RejectsWhenNoCommitteeRegistered()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // no committee for ForeignChain

        var msg = Message(ForeignChain, 2, 0);
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, Secp256k1Proof(1)),
            "no committee registered for the chain must be rejected");
    }

    [TestMethod]
    public void VerifyInboundMessage_RejectsSigCountBelowThreshold()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        // 3-of-3 committee.
        c.RegisterCommittee(ForeignChain, 3, CurveSecp256k1, Secp256k1Blob(3));

        // Only 1 signature offered but threshold is 3 -> reject before crypto.
        var msg = Message(ForeignChain, 2, 0);
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, Secp256k1Proof(1)),
            "signature count below threshold must be rejected");
    }

    [TestMethod]
    public void VerifyInboundMessage_RejectsProofLengthInconsistentWithSigCount()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(2));

        // Header declares sigCount=2 but the buffer only carries room for fewer entries: the explicit
        // length-consistency assert must fire (prevents over/under-reading the proof buffer).
        var proof = new byte[2 + (33 + 64)]; // room for 1 entry, but declare 2
        proof[0] = 2;
        var msg = Message(ForeignChain, 2, 0);
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, proof),
            "proofBytes length must be consistent with the declared sigCount");
    }

    [TestMethod]
    public void VerifyInboundMessage_RejectsSignatureFromNonCommitteeKey()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        // 1-of-1 committee with a known pubkey blob; our proof carries an all-zero pubkey that is not a
        // committee member, so FindCommitteeIndex returns -1 and the "non-committee key" guard fires
        // before any curve verification.
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1));

        var msg = Message(ForeignChain, 2, 0);
        var proof = Secp256k1Proof(1); // pubkey region is all-zero -> not in committee
        Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, proof),
            "a signature whose pubkey is not in the committee must be rejected");
    }

    [TestMethod]
    public void GetSignerMember_UnboundSlot_ReturnsZero()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, Secp256k1Blob(1)); // no member binding

        Assert.AreEqual(UInt160.Zero, c.GetSignerMember(ForeignChain, 0),
            "an unbound signer slot reads back as zero (fraud verifier then refuses to slash)");
    }

    // ---------------------------------------------------------------------------------------------
    // VerifyInboundMessage — duplicate-signer dedup (the load-bearing malleability defense)
    // ---------------------------------------------------------------------------------------------

    /// <summary>A deterministic secp256k1 PRIVATE key (32 bytes), kept inside the curve order. The
    /// on-chain verify uses the native secp256k1+SHA256 syscall, which cannot be mocked — so this test
    /// produces REAL secp256k1 keys/signatures (Neo's KeyPair is secp256r1-only and cannot represent
    /// this key, so we carry raw private-key bytes and derive the pubkey / sign off the curve directly).</summary>
    private static byte[] Secp256k1PrivKey()
    {
        var priv = new byte[32];
        for (var j = 0; j < 32; j++) priv[j] = (byte)(0x11 + j);
        priv[0] &= 0x1F; // keep inside the curve order (non-zero, < n)
        return priv;
    }

    /// <summary>Derive the 33-byte compressed secp256k1 pubkey for a private key: G * priv, compressed.</summary>
    private static byte[] DeriveSecp256k1PubKey(byte[] priv) =>
        (ECCurve.Secp256k1.G * priv).EncodePoint(true); // 33 bytes

    /// <summary>Sign with secp256k1 + SHA256 prehash so the on-chain VerifyWithECDsa(secp256k1SHA256)
    /// accepts it. Crypto.Sign SHA256-prehashes internally and returns the raw 64-byte (r‖s) signature.</summary>
    private static byte[] Secp256k1Sign(byte[] priv, byte[] message) =>
        Crypto.Sign(message, priv, ECCurve.Secp256k1, HashAlgorithm.SHA256);

    [TestMethod]
    public void VerifyInboundMessage_DuplicateSigner_DedupRejectsSecondSignature()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        // Register a 1-of-1 secp256k1 committee whose single member is a REAL key (so its signature
        // actually verifies). committee blob = the 33-byte compressed pubkey at slot 0.
        var priv = Secp256k1PrivKey();
        var pub = DeriveSecp256k1PubKey(priv); // 33 bytes
        c.RegisterCommittee(ForeignChain, 1, CurveSecp256k1, pub);

        // Canonical inbound message: signed-domain chainId == ForeignChain, direction ForeignToNeo(2),
        // no deadline (0 == never expires) so the expiry guard passes regardless of the test clock.
        var msg = Message(ForeignChain, direction: 2, deadlineUnixSeconds: 0);
        var sig = Secp256k1Sign(priv, msg);

        // Sanity: a SINGLE valid attestation from this member verifies (so we know the crypto path is
        // genuinely reached — the dedup fault below is not a masked earlier failure).
        var singleProof = Secp256k1ProofWith(new[] { (pub, sig) });
        Assert.IsTrue(c.VerifyInboundMessage(ForeignChain, msg, singleProof)!,
            "a single valid committee signature must verify (the crypto path is reached)");

        // Two entries, BOTH the same committee member (same pubkey) with valid signatures. The dedup is
        // keyed on the resolved committee index, not the signature bytes, so the second entry resolves
        // to the SAME memberIdx whose bitmap bit is already set -> "duplicate signer" before the second
        // signature is even verified. This is the malleability defense: one signer cannot count twice
        // toward the threshold even with two individually-valid signatures.
        var dupProof = Secp256k1ProofWith(new[] { (pub, sig), (pub, sig) });
        var ex = Assert.ThrowsExactly<TestException>(
            () => c.VerifyInboundMessage(ForeignChain, msg, dupProof),
            "the same committee member counted twice must be rejected by the dedup guard");
        StringAssert.Contains(ex.Message, "duplicate signer");
    }

    /// <summary>Build a secp256k1 proof: [2B sigCount LE] + sigCount × (33B pubkey ‖ 64B sig).</summary>
    private static byte[] Secp256k1ProofWith((byte[] pubkey, byte[] sig)[] entries)
    {
        const int perSig = 33 + 64;
        var p = new byte[2 + entries.Length * perSig];
        p[0] = (byte)(entries.Length & 0xFF);
        p[1] = (byte)((entries.Length >> 8) & 0xFF);
        var pos = 2;
        foreach (var (pubkey, sig) in entries)
        {
            for (var j = 0; j < 33; j++) p[pos + j] = pubkey[j];
            for (var j = 0; j < 64; j++) p[pos + 33 + j] = sig[j];
            pos += perSig;
        }
        return p;
    }
}
