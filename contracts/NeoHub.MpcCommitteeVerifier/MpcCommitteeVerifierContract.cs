using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.MpcCommitteeVerifier;

/// <summary>
/// Phase-B verifier for cross-foreign-chain messages: M-of-N secp256k1
/// committee attestations over the canonical
/// <c>ExternalCrossChainMessage</c> bytes. Implements the
/// <see cref="ExternalBridgeRegistry"/> dispatch ABI (<c>verifyInboundMessage</c>).
/// </summary>
/// <remarks>
/// This is the same shape as <c>Neo.L2.Proving.Attestation.AttestationVerifier</c>,
/// just on-chain, indexed per-foreign-chain, and using secp256k1 (Eth/Tron-native)
/// instead of secp256r1. Solana watchers sign with ed25519 and use the
/// <see cref="CurveSecp256k1"/> = 0 sentinel to fall through to the
/// <see cref="CryptoLib.VerifyWithEd25519"/> path.
///
/// <para>Storage layout per externalChainId:</para>
/// <list type="bullet">
///   <item><description><c>0x01 + externalChainId(4B LE)</c> → committee blob:
///     <c>[1B threshold][1B size][1B curveTag][size × 33B pubkey]</c></description></item>
///   <item><description><c>0x02 + externalChainId(4B LE) + nonce(8B LE)</c> →
///     1B (replay-protection: a finalized inbound message can't be applied twice)</description></item>
/// </list>
///
/// <para>Trust model: the committee can collude (M-of-N). Phase C wraps this in
/// an optimistic challenge window so an honest watcher can fraud-prove an
/// equivocating committee. Phase D replaces the verifier with a ZK light
/// client that doesn't trust a committee at all.</para>
/// </remarks>
[DisplayName("NeoHub.MpcCommitteeVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("M-of-N committee verifier for cross-foreign-chain messages.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MpcCommitteeVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class MpcCommitteeVerifierContract : SmartContract
{
    private const byte PrefixCommittee = 0x01;
    private const byte PrefixConsumedNonce = 0x02;
    private const byte KeyGovernanceController = 0x03;
    private const byte PrefixConsumedProposal = 0x04;
    /// <summary>0x05 + externalChainId(4B LE) + signerIdx(1B) → UInt160 member.
    /// Populated by <see cref="RegisterCommitteeWithMembers"/>; required for
    /// the Phase-C <c>NeoHub.MpcCommitteeFraudVerifier</c> to slash a
    /// specific committee member's bond on equivocation.</summary>
    private const byte PrefixSignerMember = 0x05;
    private const byte KeyOwner = 0xFF;

    /// <summary>secp256k1 + SHA256 hashing — Ethereum / Tron watchers sign this curve.</summary>
    public const byte CurveSecp256k1 = 1;
    /// <summary>ed25519 — Solana watchers sign this curve.</summary>
    public const byte CurveEd25519 = 2;

    /// <summary>Hard cap on committee size (bytes per blob bound). Production
    /// committees run 7-21; 64 is a defensive ceiling.</summary>
    public const int MaxCommitteeSize = 64;

    /// <summary>Emitted when a committee is registered or replaced.</summary>
    [DisplayName("CommitteeRegistered")]
    public static event Action<uint, byte, byte, byte> OnCommitteeRegistered = default!;

    /// <summary>Emitted on every verified inbound message (allows off-chain
    /// indexers to track committee activity without re-decoding tx scripts).</summary>
    [DisplayName("InboundVerified")]
    public static event Action<uint, ulong> OnInboundVerified = default!;

    /// <summary>Set the initial owner.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var owner = (UInt160)data;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Wire the GovernanceController contract hash.</summary>
    public static void SetGovernanceController(UInt160 governanceController)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(governanceController.IsValid && !governanceController.IsZero,
            "invalid governance controller");
        Storage.Put(new byte[] { KeyGovernanceController }, governanceController);
    }

    /// <summary>Read the wired GovernanceController hash, or zero if not set.</summary>
    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { KeyGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Register (or replace) the committee for an external chain. Owner only.
    /// </summary>
    /// <param name="externalChainId">Foreign chain id (must use 0xE0_xx_xx_xx prefix).</param>
    /// <param name="threshold">M (signatures required).</param>
    /// <param name="curveTag">1 = secp256k1 (Eth/Tron), 2 = ed25519 (Solana).</param>
    /// <param name="committeeBlob">Concatenated 33-byte compressed pubkeys
    /// (secp256k1) or 32-byte ed25519 pubkeys, in canonical order.</param>
    public static void RegisterCommittee(
        uint externalChainId, byte threshold, byte curveTag, byte[] committeeBlob)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        WriteCommittee(externalChainId, threshold, curveTag, committeeBlob);
    }

    /// <summary>
    /// Same as <see cref="RegisterCommittee"/> but also binds each signer index
    /// to a Neo-side bond-holder address. Required for the Phase-C
    /// <c>NeoHub.MpcCommitteeFraudVerifier</c>: when proving equivocation, the
    /// fraud verifier needs to know which Neo address holds the equivocator's
    /// bond on <c>NeoHub.ExternalBridgeBond</c>. Without this binding, the
    /// fraud verifier refuses to slash (bond holder is unknown).
    /// </summary>
    /// <param name="memberBlob">Concatenated 20-byte UInt160 member addresses,
    /// in the same order as <paramref name="committeeBlob"/>'s pubkeys
    /// (signerIdx 0 → first pubkey + first member, etc.).</param>
    public static void RegisterCommitteeWithMembers(
        uint externalChainId, byte threshold, byte curveTag,
        byte[] committeeBlob, byte[] memberBlob)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        WriteCommitteeWithMembers(externalChainId, threshold, curveTag, committeeBlob, memberBlob);
    }

    /// <summary>Governance-mediated committee registration with replay protection.</summary>
    public static void RegisterCommitteeViaProposal(
        uint externalChainId, byte threshold, byte curveTag, byte[] committeeBlob, ulong proposalId)
    {
        var gc = GetGovernanceController();
        ExecutionEngine.Assert(gc != UInt160.Zero,
            "governance controller not wired — owner must call SetGovernanceController first");

        var consumedKey = new byte[1 + 8];
        consumedKey[0] = PrefixConsumedProposal;
        consumedKey[1] = (byte)proposalId; consumedKey[2] = (byte)(proposalId >> 8);
        consumedKey[3] = (byte)(proposalId >> 16); consumedKey[4] = (byte)(proposalId >> 24);
        consumedKey[5] = (byte)(proposalId >> 32); consumedKey[6] = (byte)(proposalId >> 40);
        consumedKey[7] = (byte)(proposalId >> 48); consumedKey[8] = (byte)(proposalId >> 56);
        ExecutionEngine.Assert(Storage.Get(consumedKey) == null, "proposal already consumed");

        var ok = (bool)Contract.Call(gc, "isApprovedAndTimelocked",
            CallFlags.ReadOnly, new object[] { proposalId });
        ExecutionEngine.Assert(ok,
            "proposal not approved + timelocked");

        // Bind proposal payload to (externalChainId, threshold, curveTag, committeeBlob).
        // Without this, an approved proposal could install ANY committee on ANY chain
        // — the council vote becomes a one-time blank check at the bridge-committee
        // boundary. Defense matters: a malicious committee rotation here lets attackers
        // attest fraudulent inbound external-chain messages with reporter-rewarded slashes
        // unable to catch them (the attacker IS the committee).
        var expectedAction = BuildRegisterCommitteeAction(externalChainId, threshold, curveTag, committeeBlob);
        var bound = (bool)Contract.Call(gc, "matchesProposalPayload",
            CallFlags.ReadOnly, new object[] { proposalId, expectedAction });
        ExecutionEngine.Assert(bound,
            "proposal payload does not match (externalChainId, threshold, curveTag, committeeBlob) action args (council voted on different bytes)");

        Storage.Put(consumedKey, new byte[] { 1 });
        WriteCommittee(externalChainId, threshold, curveTag, committeeBlob);
    }

    /// <summary>
    /// Canonical encoding for a "register committee" action. Layout:
    /// <c>"neo4-gov:registerCommittee" || externalChainId(4B LE) || threshold(1B) ||
    /// curveTag(1B) || committeeBlob(NB)</c>. Variable-length because the blob carries
    /// the per-signer pubkeys.
    /// </summary>
    [Safe]
    public static byte[] BuildRegisterCommitteeAction(
        uint externalChainId, byte threshold, byte curveTag, byte[] committeeBlob)
    {
        var tag = ActionTagRegisterCommittee;
        var blobLen = committeeBlob.Length;
        var buf = new byte[tag.Length + 4 + 1 + 1 + blobLen];
        for (var i = 0; i < tag.Length; i++) buf[i] = tag[i];
        var pos = tag.Length;
        buf[pos++] = (byte)externalChainId;
        buf[pos++] = (byte)(externalChainId >> 8);
        buf[pos++] = (byte)(externalChainId >> 16);
        buf[pos++] = (byte)(externalChainId >> 24);
        buf[pos++] = threshold;
        buf[pos++] = curveTag;
        for (var i = 0; i < blobLen; i++) buf[pos + i] = committeeBlob[i];
        return buf;
    }

    private static readonly byte[] ActionTagRegisterCommittee = new byte[]
    {
        (byte)'n', (byte)'e', (byte)'o', (byte)'4', (byte)'-',
        (byte)'g', (byte)'o', (byte)'v', (byte)':',
        (byte)'r', (byte)'e', (byte)'g', (byte)'i', (byte)'s', (byte)'t', (byte)'e', (byte)'r',
        (byte)'C', (byte)'o', (byte)'m', (byte)'m', (byte)'i', (byte)'t', (byte)'t', (byte)'e', (byte)'e'
    };

    /// <summary>Read the raw committee blob for a chain (header + concatenated pubkeys).</summary>
    [Safe]
    public static byte[] GetCommittee(uint externalChainId)
    {
        var raw = Storage.Get(CommitteeKey(externalChainId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>Read just the (threshold, size, curveTag) header for a chain.</summary>
    [Safe]
    public static byte[] GetCommitteeHeader(uint externalChainId)
    {
        var raw = Storage.Get(CommitteeKey(externalChainId));
        if (raw == null || raw.Length < 3) return new byte[0];
        var b = (byte[])raw;
        return new byte[] { b[0], b[1], b[2] };
    }

    /// <summary>Read the bond-holder member address bound to a signer slot,
    /// or <see cref="UInt160.Zero"/> if no binding has been registered. The
    /// Phase-C fraud verifier reads this to decide whose bond to slash on
    /// proven equivocation; refuses to slash if zero.</summary>
    [Safe]
    public static UInt160 GetSignerMember(uint externalChainId, byte signerIdx)
    {
        var raw = Storage.Get(SignerMemberKey(externalChainId, signerIdx));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// The dispatch entry point — registry calls this. Verifies the proof
    /// against the committee for <paramref name="externalChainId"/>, enforces
    /// threshold, replay-protects on the message's nonce.
    /// </summary>
    /// <param name="externalChainId">Foreign chain id.</param>
    /// <param name="messageBytes">Canonical ExternalCrossChainMessage bytes
    /// (102B fixed prefix + payload, NO trailing 32B messageHash — the hash
    /// is recomputed here from the pre-image).</param>
    /// <param name="proofBytes">Canonical
    /// <c>[2B sigCount LE]</c> + <c>sigCount × ([keyLen B pubkey][64B sig])</c>
    /// where <c>keyLen</c> is 33 for secp256k1, 32 for ed25519 (per the
    /// committee's curveTag).</param>
    public static bool VerifyInboundMessage(
        uint externalChainId, byte[] messageBytes, byte[] proofBytes)
    {
        ExecutionEngine.Assert(messageBytes.Length >= 102,
            "messageBytes too short for ExternalCrossChainMessage layout");
        ExecutionEngine.Assert(proofBytes.Length >= 2,
            "proofBytes too short");

        // 1. Read committee. Storage.Get returns ByteString in contract context;
        // we cast to byte[] once so the rest of the function can index freely.
        var committeeRaw = Storage.Get(CommitteeKey(externalChainId));
        ExecutionEngine.Assert(committeeRaw != null && committeeRaw.Length >= 3,
            "no committee registered for externalChainId");
        var committee = (byte[])committeeRaw!;
        var threshold = committee[0];
        var size = committee[1];
        var curveTag = committee[2];
        ExecutionEngine.Assert(threshold > 0 && threshold <= size, "invalid threshold");

        var keyLen = curveTag == CurveSecp256k1 ? 33 : 32;
        ExecutionEngine.Assert(committee.Length == 3 + size * keyLen, "committee blob length mismatch");

        // 2. Replay protection: nonce lives at offset 8 in messageBytes.
        var nonce =
            (ulong)messageBytes[8]
            | ((ulong)messageBytes[9] << 8)
            | ((ulong)messageBytes[10] << 16)
            | ((ulong)messageBytes[11] << 24)
            | ((ulong)messageBytes[12] << 32)
            | ((ulong)messageBytes[13] << 40)
            | ((ulong)messageBytes[14] << 48)
            | ((ulong)messageBytes[15] << 56);
        var nonceKey = ConsumedNonceKey(externalChainId, nonce);
        ExecutionEngine.Assert(Storage.Get(nonceKey) == null,
            "message nonce already consumed (replay)");

        // 3. Decode and verify signatures.
        var sigCount = (int)proofBytes[0] | ((int)proofBytes[1] << 8);
        ExecutionEngine.Assert(sigCount >= threshold,
            "signature count below threshold");
        var perSig = keyLen + 64;
        ExecutionEngine.Assert(proofBytes.Length == 2 + sigCount * perSig,
            "proofBytes length inconsistent with declared sigCount");

        // Track which committee indices have signed (dedup) — index into the
        // committee blob is 0..size-1; we use a bitmap so 64 max maps to 8 bytes.
        var seenBitmap = new byte[(MaxCommitteeSize + 7) / 8];

        var validSigs = 0;
        for (var i = 0; i < sigCount; i++)
        {
            var off = 2 + i * perSig;
            var pubkey = new byte[keyLen];
            for (var j = 0; j < keyLen; j++) pubkey[j] = proofBytes[off + j];
            var sig = new byte[64];
            for (var j = 0; j < 64; j++) sig[j] = proofBytes[off + keyLen + j];

            // Find this pubkey in the committee.
            var memberIdx = FindCommitteeIndex(committee, size, keyLen, pubkey);
            ExecutionEngine.Assert(memberIdx >= 0, "signature from non-committee key");

            // Dedup by committee-member index, not by signature bytes. This is the
            // load-bearing defense against ECDSA signature malleability: even if an
            // attacker submits two valid signatures (low-S and high-S variants) from the
            // same signer for the same message, the second one hits this assert and is
            // rejected — they can't count the same signer twice toward the threshold.
            var byteIdx = memberIdx / 8;
            var bitIdx = (byte)(1 << (memberIdx % 8));
            ExecutionEngine.Assert((seenBitmap[byteIdx] & bitIdx) == 0, "duplicate signer");
            seenBitmap[byteIdx] = (byte)(seenBitmap[byteIdx] | bitIdx);

            // Verify signature.
            bool ok;
            if (curveTag == CurveSecp256k1)
            {
                // secp256k1 + SHA256 — Eth-flavored watchers sign over the canonical
                // bytes; CryptoLib hashes internally.
                ok = CryptoLib.VerifyWithECDsa(
                    (ByteString)messageBytes, (ECPoint)pubkey,
                    (ByteString)sig, NamedCurveHash.secp256k1SHA256);
            }
            else
            {
                ExecutionEngine.Assert(curveTag == CurveEd25519, "unknown curveTag");
                ok = CryptoLib.VerifyWithEd25519(
                    (ByteString)messageBytes, (ByteString)pubkey, (ByteString)sig);
            }
            ExecutionEngine.Assert(ok, "signature verification failed");
            validSigs++;
        }

        ExecutionEngine.Assert(validSigs >= threshold,
            "valid signatures below threshold after dedup");

        // 4. Mark the nonce consumed and emit the audit event.
        Storage.Put(nonceKey, new byte[] { 1 });
        OnInboundVerified(externalChainId, nonce);
        return true;
    }

    /// <summary>Required by the registry's dispatch convention.</summary>
    [Safe]
    public static byte BridgeKind() => 1; // 1 = MPC

    private static int FindCommitteeIndex(byte[] committee, byte size, int keyLen, byte[] pubkey)
    {
        for (var i = 0; i < size; i++)
        {
            var off = 3 + i * keyLen;
            var match = true;
            for (var j = 0; j < keyLen; j++)
            {
                if (committee[off + j] != pubkey[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static void WriteCommittee(uint externalChainId, byte threshold, byte curveTag, byte[] committeeBlob)
    {
        ExecutionEngine.Assert(
            (externalChainId & 0xFF000000U) == 0xE0000000U,
            "externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix");
        ExecutionEngine.Assert(threshold > 0, "threshold must be positive");
        ExecutionEngine.Assert(curveTag == CurveSecp256k1 || curveTag == CurveEd25519,
            "curveTag must be 1 (secp256k1) or 2 (ed25519)");

        var keyLen = curveTag == CurveSecp256k1 ? 33 : 32;
        ExecutionEngine.Assert(committeeBlob != null, "committeeBlob is null");
        // Null-checked above; this dereference is safe.
        ExecutionEngine.Assert(committeeBlob!.Length > 0, "committeeBlob is empty");
        ExecutionEngine.Assert(committeeBlob.Length % keyLen == 0,
            "committeeBlob length must be a multiple of pubkey length for the curve");
        var size = committeeBlob.Length / keyLen;
        ExecutionEngine.Assert(size <= MaxCommitteeSize, "committee size exceeds MaxCommitteeSize");
        ExecutionEngine.Assert(threshold <= size, "threshold > committee size");

        var stored = new byte[3 + committeeBlob.Length];
        stored[0] = threshold;
        stored[1] = (byte)size;
        stored[2] = curveTag;
        for (var i = 0; i < committeeBlob.Length; i++) stored[3 + i] = committeeBlob[i];
        Storage.Put(CommitteeKey(externalChainId), stored);
        OnCommitteeRegistered(externalChainId, threshold, (byte)size, curveTag);
    }

    private static void WriteCommitteeWithMembers(
        uint externalChainId, byte threshold, byte curveTag,
        byte[] committeeBlob, byte[] memberBlob)
    {
        // Validate committee blob via the existing path first; size/curveTag
        // checks set the invariants the member-blob validation relies on.
        WriteCommittee(externalChainId, threshold, curveTag, committeeBlob);

        var keyLen = curveTag == CurveSecp256k1 ? 33 : 32;
        var size = committeeBlob.Length / keyLen;
        ExecutionEngine.Assert(memberBlob != null, "memberBlob is null");
        ExecutionEngine.Assert(memberBlob!.Length == size * 20,
            "memberBlob length must be size × 20 (one 20-byte member per signer)");

        // Write per-signer member binding. Slot i ↔ pubkey at committee
        // offset (3 + i*keyLen) ↔ member at memberBlob offset (i*20).
        for (var i = 0; i < size; i++)
        {
            var member = new byte[20];
            for (var j = 0; j < 20; j++) member[j] = memberBlob[i * 20 + j];
            ExecutionEngine.Assert(((UInt160)member).IsValid && !((UInt160)member).IsZero,
                "memberBlob slot is invalid or zero address");
            Storage.Put(SignerMemberKey(externalChainId, (byte)i), member);
        }
    }

    private static byte[] SignerMemberKey(uint externalChainId, byte signerIdx)
    {
        var k = new byte[1 + 4 + 1];
        k[0] = PrefixSignerMember;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        k[5] = signerIdx;
        return k;
    }

    private static byte[] CommitteeKey(uint externalChainId)
    {
        var k = new byte[1 + 4];
        k[0] = PrefixCommittee;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        return k;
    }

    private static byte[] ConsumedNonceKey(uint externalChainId, ulong nonce)
    {
        var k = new byte[1 + 4 + 8];
        k[0] = PrefixConsumedNonce;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        k[5] = (byte)nonce;
        k[6] = (byte)(nonce >> 8);
        k[7] = (byte)(nonce >> 16);
        k[8] = (byte)(nonce >> 24);
        k[9] = (byte)(nonce >> 32);
        k[10] = (byte)(nonce >> 40);
        k[11] = (byte)(nonce >> 48);
        k[12] = (byte)(nonce >> 56);
        return k;
    }
}
