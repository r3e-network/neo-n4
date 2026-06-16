using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.MpcCommitteeFraudVerifier;

/// <summary>
/// Phase-C optimistic-challenge fraud verifier for the cross-foreign-chain
/// bridge. Anyone can submit cryptographic proof that a committee member
/// equivocated (signed two byte-distinct messages with the same
/// <c>(externalChainId, nonce)</c> pair); the contract verifies the proof
/// and slashes the equivocator's full bond, paying the reporter.
/// </summary>
/// <remarks>
/// <para>This is the slasher seam <c>NeoHub.ExternalBridgeBond</c> hooks
/// into. Until this contract deploys, the bond's <c>Slash</c> is owner-only
/// (devnet path). After deploy + <c>RegisterSlasher</c>, slashing becomes
/// permissionless: any honest party who observes equivocation can submit
/// the two signed messages + earn the slashed bond as their reward.</para>
///
/// <para>Wiring required at deploy time:</para>
/// <list type="number">
///   <item><description>Deploy this contract.</description></item>
///   <item><description>Owner calls
///     <c>NeoHub.ExternalBridgeBond.RegisterSlasher(this contract's hash)</c>
///     so the bond contract accepts <c>Slash</c> calls from here.</description></item>
///   <item><description>Per supported foreign chain: owner calls
///     <c>NeoHub.MpcCommitteeVerifier.RegisterCommitteeWithMembers(...)</c>
///     instead of <c>RegisterCommittee(...)</c> so the per-signer bond-holder
///     binding gets set. Without the binding, this contract refuses to slash
///     (no way to identify whose bond to take).</description></item>
/// </list>
///
/// <para>Equivocation proof shape: caller passes two <c>(messageBytes,
/// signature)</c> pairs alleging that the same committee member signed
/// both. The contract:</para>
/// <list type="number">
///   <item><description>Reads the committee from
///     <see cref="NeoHub.MpcCommitteeVerifier.MpcCommitteeVerifierContract"/>;
///     extracts the pubkey at <paramref name="signerIdx"/>.</description></item>
///   <item><description>Asserts both messages parse as valid
///     <c>ExternalCrossChainMessage</c> bytes with the SAME
///     <c>(externalChainId, nonce)</c> at offsets 0+8.</description></item>
///   <item><description>Asserts the messages are NOT byte-identical
///     (otherwise both signatures could be honest signings of the
///     same message — no equivocation).</description></item>
///   <item><description>Verifies both signatures against the same
///     committee pubkey using the curve the committee was registered with
///     (<c>secp256k1+SHA256</c> or <c>ed25519</c>).</description></item>
///   <item><description>Looks up the bond-holder member via
///     <see cref="NeoHub.MpcCommitteeVerifier.MpcCommitteeVerifierContract.GetSignerMember"/>;
///     refuses to slash if no binding is registered.</description></item>
///   <item><description>Reads the equivocator's bond balance from
///     <c>NeoHub.ExternalBridgeBond.GetBalance</c>; calls
///     <c>Slash(externalChainId, member, fullBalance, msg.sender)</c>
///     so the equivocator loses their entire bond + the reporter gets
///     paid. Replay-protected per
///     <c>(externalChainId, signerIdx)</c>: a single equivocation can
///     only be slashed once.</description></item>
/// </list>
/// </remarks>
[DisplayName("NeoHub.MpcCommitteeFraudVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Slashes equivocating committee members on the cross-foreign-chain bridge.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MpcCommitteeFraudVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class MpcCommitteeFraudVerifierContract : SmartContract
{
    private const byte KeyVerifier = 0x01;
    private const byte KeyBond = 0x02;
    /// <summary>0x03 + externalChainId(4B LE) + signerIdx(1B) → 1 (slashed).
    /// A single equivocation can be slashed once; second attempt reverts.</summary>
    private const byte PrefixSlashed = 0x03;
    private const byte KeyOwner = 0xFF;

    /// <summary>secp256k1 curveTag — must match what
    /// <c>MpcCommitteeVerifier.RegisterCommittee</c> recorded.</summary>
    private const byte CurveSecp256k1 = 1;
    /// <summary>ed25519 curveTag.</summary>
    private const byte CurveEd25519 = 2;

    /// <summary>Offset of the direction byte in an ExternalCrossChainMessage (matches
    /// MpcCommitteeVerifier.OffsetDirection).</summary>
    private const int OffsetDirection = 16;
    /// <summary>The only direction the committee legitimately attests (ForeignToNeo).</summary>
    private const byte DirectionForeignToNeo = 2;

    /// <summary>Emitted on a successful slash. <c>amount</c> is the slashed
    /// portion paid to <c>reporter</c>; <c>signerIdx</c> identifies which
    /// committee slot equivocated for off-chain monitoring.</summary>
    [DisplayName("CommitteeMemberSlashed")]
    public static event Action<uint, byte, UInt160, BigInteger, UInt160> OnCommitteeMemberSlashed = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Wire owner + verifier + bond on deploy.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var verifier = (UInt160)arr[1];
        var bond = (UInt160)arr[2];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier");
        ExecutionEngine.Assert(bond.IsValid && !bond.IsZero, "invalid bond");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyVerifier }, verifier);
        Storage.Put(new byte[] { KeyBond }, bond);
    }

    /// <summary>The governance owner — typically the <c>GovernanceController</c>
    /// contract hash. Returns <c>UInt160.Zero</c> if the contract is not yet
    /// configured (<see cref="_deploy"/> hasn't run).</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>The wired MpcCommitteeVerifier contract hash.</summary>
    [Safe]
    public static UInt160 GetVerifier()
    {
        var raw = Storage.Get(new byte[] { KeyVerifier });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>The wired ExternalBridgeBond contract hash.</summary>
    [Safe]
    public static UInt160 GetBond()
    {
        var raw = Storage.Get(new byte[] { KeyBond });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Has the equivocation slot already been slashed?</summary>
    [Safe]
    public static bool IsSlashed(uint externalChainId, byte signerIdx)
    {
        return Storage.Get(SlashedKey(externalChainId, signerIdx)) != null;
    }

    /// <summary>
    /// Submit cryptographic proof of equivocation by committee member
    /// <paramref name="signerIdx"/> on chain <paramref name="externalChainId"/>.
    /// On valid proof, slashes the member's full bond + pays the caller as
    /// reporter.
    /// </summary>
    public static void Slash(
        uint externalChainId,
        byte signerIdx,
        byte[] message1Bytes, byte[] signature1,
        byte[] message2Bytes, byte[] signature2)
    {
        // Replay-protect per (chainId, signerIdx). A single equivocation can
        // be slashed once; a second attempt reverts (preserves any remaining
        // bond for governance to process if multiple slashable offenses
        // happen in close succession).
        var slashedKey = SlashedKey(externalChainId, signerIdx);
        ExecutionEngine.Assert(Storage.Get(slashedKey) == null,
            "already slashed for (externalChainId, signerIdx)");

        // Sanity-check both messages parse as ExternalCrossChainMessage and
        // claim the same (chainId, nonce) — the equivocation invariant.
        ExecutionEngine.Assert(message1Bytes != null && message1Bytes.Length >= 102,
            "message1Bytes too short for ExternalCrossChainMessage layout");
        ExecutionEngine.Assert(message2Bytes != null && message2Bytes.Length >= 102,
            "message2Bytes too short for ExternalCrossChainMessage layout");
        ExecutionEngine.Assert(signature1 != null && signature1!.Length == 64, "signature1 must be 64 bytes");
        ExecutionEngine.Assert(signature2 != null && signature2!.Length == 64, "signature2 must be 64 bytes");

        var msg1ChainId = ReadUInt32LE(message1Bytes!, 0);
        var msg2ChainId = ReadUInt32LE(message2Bytes!, 0);
        ExecutionEngine.Assert(msg1ChainId == externalChainId, "message1 chainId != externalChainId");
        ExecutionEngine.Assert(msg2ChainId == externalChainId, "message2 chainId != externalChainId");
        var nonce1 = ReadUInt64LE(message1Bytes!, 8);
        var nonce2 = ReadUInt64LE(message2Bytes!, 8);
        ExecutionEngine.Assert(nonce1 == nonce2,
            "messages have different nonces — not an equivocation (a member is allowed to sign distinct nonces)");

        // Both messages must be ForeignToNeo — the only direction the committee legitimately
        // attests (see MpcCommitteeVerifier). Nonce namespaces are disjoint across directions, so
        // without this an honest ForeignToNeo signature paired with an (un-attested) NeoToForeign
        // message that happens to share (chainId, nonce) would be treated as equivocation and
        // slash an innocent member.
        ExecutionEngine.Assert(message1Bytes![OffsetDirection] == DirectionForeignToNeo,
            "message1 direction must be ForeignToNeo(2)");
        ExecutionEngine.Assert(message2Bytes![OffsetDirection] == DirectionForeignToNeo,
            "message2 direction must be ForeignToNeo(2)");

        // Messages must NOT be byte-identical. Two identical messages with
        // two valid signatures is just two honest signings — ECDSA permits
        // distinct (r,s) for the same digest. Without this check a member
        // could be slashed for re-signing the same canonical message.
        ExecutionEngine.Assert(!BytesEqual(message1Bytes!, message2Bytes!),
            "messages are byte-identical — not an equivocation");

        // Read committee + the equivocator's pubkey.
        var verifier = GetVerifier();
        ExecutionEngine.Assert(verifier != UInt160.Zero, "verifier not wired");
        var committeeRaw = (byte[])Contract.Call(verifier, "getCommittee",
            CallFlags.ReadOnly, new object[] { externalChainId });
        ExecutionEngine.Assert(committeeRaw.Length >= 3, "no committee registered for externalChainId");
        var size = committeeRaw[1];
        var curveTag = committeeRaw[2];
        ExecutionEngine.Assert(signerIdx < size, "signerIdx >= committee size");

        var keyLen = curveTag == CurveSecp256k1 ? 33 : 32;
        ExecutionEngine.Assert(committeeRaw.Length == 3 + size * keyLen, "committee blob length mismatch");
        var pubkey = new byte[keyLen];
        for (var i = 0; i < keyLen; i++) pubkey[i] = committeeRaw[3 + signerIdx * keyLen + i];

        // Verify both signatures against the same pubkey.
        bool ok1, ok2;
        if (curveTag == CurveSecp256k1)
        {
            ok1 = CryptoLib.VerifyWithECDsa(
                (ByteString)message1Bytes!, (ECPoint)pubkey,
                (ByteString)signature1!, NamedCurveHash.secp256k1SHA256);
            ok2 = CryptoLib.VerifyWithECDsa(
                (ByteString)message2Bytes!, (ECPoint)pubkey,
                (ByteString)signature2!, NamedCurveHash.secp256k1SHA256);
        }
        else
        {
            ExecutionEngine.Assert(curveTag == CurveEd25519, "unknown curveTag");
            ok1 = CryptoLib.VerifyWithEd25519(
                (ByteString)message1Bytes!, (ByteString)pubkey, (ByteString)signature1!);
            ok2 = CryptoLib.VerifyWithEd25519(
                (ByteString)message2Bytes!, (ByteString)pubkey, (ByteString)signature2!);
        }
        ExecutionEngine.Assert(ok1, "signature1 does not verify against committee[signerIdx]");
        ExecutionEngine.Assert(ok2, "signature2 does not verify against committee[signerIdx]");

        // Look up bond holder. Must have been bound via
        // RegisterCommitteeWithMembers — refuse to slash otherwise so we
        // don't accidentally slash the wrong identity.
        var member = (UInt160)Contract.Call(verifier, "getSignerMember",
            CallFlags.ReadOnly, new object[] { externalChainId, signerIdx });
        ExecutionEngine.Assert(member != UInt160.Zero,
            "no bond-holder bound to this signer slot — operator must call RegisterCommitteeWithMembers before slashing can succeed");

        // Read full bond balance + slash everything to the reporter.
        var bond = GetBond();
        ExecutionEngine.Assert(bond != UInt160.Zero, "bond contract not wired");
        var balance = (BigInteger)Contract.Call(bond, "getBalance",
            CallFlags.ReadOnly, new object[] { externalChainId, member });
        ExecutionEngine.Assert(balance > 0,
            "equivocator has zero bond balance — nothing to slash (still record the slash to prevent replay)");

        var reporter = (UInt160)Runtime.CallingScriptHash;

        // CEI: record the replay flag BEFORE the external bond.slash call. The current
        // ExternalBridgeBond.Slash is benign (just decrements storage + transfers), but if
        // the bond contract were ever replaced with one that re-entered Slash on this
        // contract — e.g. via a hostile NEP-17 hook called during payout — the line-156
        // replay guard wouldn't yet be set. Writing first closes the door before any
        // external code runs.
        Storage.Put(slashedKey, new byte[] { 1 });

        Contract.Call(bond, "slash", CallFlags.All,
            new object[] { externalChainId, member, balance, reporter });

        OnCommitteeMemberSlashed(externalChainId, signerIdx, member, balance, reporter);
    }

    private static byte[] SlashedKey(uint externalChainId, byte signerIdx)
    {
        var k = new byte[1 + 4 + 1];
        k[0] = PrefixSlashed;
        k[1] = (byte)externalChainId;
        k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16);
        k[4] = (byte)(externalChainId >> 24);
        k[5] = signerIdx;
        return k;
    }

    private static uint ReadUInt32LE(byte[] data, int offset)
    {
        return ((uint)data[offset])
             | ((uint)data[offset + 1] << 8)
             | ((uint)data[offset + 2] << 16)
             | ((uint)data[offset + 3] << 24);
    }

    private static ulong ReadUInt64LE(byte[] data, int offset)
    {
        return ((ulong)data[offset])
             | ((ulong)data[offset + 1] << 8)
             | ((ulong)data[offset + 2] << 16)
             | ((ulong)data[offset + 3] << 24)
             | ((ulong)data[offset + 4] << 32)
             | ((ulong)data[offset + 5] << 40)
             | ((ulong)data[offset + 6] << 48)
             | ((ulong)data[offset + 7] << 56);
    }

    private static bool BytesEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}
