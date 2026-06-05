using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ContractZkVerifier;

/// <summary>
/// Deployable NeoHub verifier router for <c>ProofType.Zk</c> settlement.
/// </summary>
/// <remarks>
/// The default N4 L1 path is contract-deployed, not a native Neo core change.
/// This router validates the canonical N4 batch-commitment and RISC-V proof
/// payload envelope, checks that the verification key is governance-registered,
/// then dispatches proof-system math to a governance-registered deployable
/// verifier contract when one is configured. Development networks may explicitly
/// enable envelope-only mode per proof system; production networks should register
/// a verifier contract for each proof system they accept, and may call
/// <c>DisableEnvelopeOnlyPermanently</c> to make that commitment irreversible.
///
/// Verifier contract ABI:
/// <c>verifyZkProof(byte proofSystem, byte[] verificationKeyId, byte[] publicInputHash, byte[] proofBytes) : bool</c>.
/// </remarks>
[DisplayName("NeoHub.ContractZkVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Deployable ProofType.Zk verifier router backed by ordinary verifier contracts.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ContractZkVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class ContractZkVerifierContract : SmartContract
{
    private const byte PrefixVerificationKey = 0x02;
    private const byte PrefixProofVerifier = 0x03;
    private const byte PrefixEnvelopeOnly = 0x04;
    private const byte PrefixEnvelopeOnlyLocked = 0x05;
    private const byte KeyOwner = 0xFF;

    private const int PublicInputHashOffset = 284;
    private const int ProofTypeOffset = 316;
    private const int ProofLenOffset = 317;
    private const int ProofBytesOffset = 321;

    private const byte ProofTypeZk = 3;
    private const byte ZkPayloadVersion = 1;
    private const int ZkPayloadVerificationKeyOffset = 2;
    private const int ZkPayloadInnerProofLenOffset = 34;
    private const int ZkPayloadProofBytesOffset = 38;
    private const int MaxProofPayloadBytes = 1 * 1024 * 1024;

    /// <summary>SP1 proof-system tag used by <c>RiscVProofPayload</c>.</summary>
    public const byte ProofSystemSp1 = 1;
    /// <summary>Risc0 proof-system tag used by <c>RiscVProofPayload</c>.</summary>
    public const byte ProofSystemRiscZero = 2;
    /// <summary>Halo2 proof-system tag used by <c>RiscVProofPayload</c>.</summary>
    public const byte ProofSystemHalo2 = 3;
    /// <summary>Axiom proof-system tag used by <c>RiscVProofPayload</c>.</summary>
    public const byte ProofSystemAxiom = 4;

    /// <summary>Emitted when a verification key is allowed or removed.</summary>
    [DisplayName("VerificationKeyRegistered")]
    public static event Action<byte, UInt256, bool> OnVerificationKeyRegistered = default!;

    /// <summary>Emitted when a proof-system verifier contract is allowed or removed.</summary>
    [DisplayName("ProofVerifierRegistered")]
    public static event Action<byte, UInt160, bool> OnProofVerifierRegistered = default!;

    /// <summary>Emitted when envelope-only verification is enabled or disabled.</summary>
    [DisplayName("EnvelopeOnlyModeSet")]
    public static event Action<byte, bool> OnEnvelopeOnlyModeSet = default!;

    /// <summary>Emitted when envelope-only verification is permanently disabled for a proof system.</summary>
    [DisplayName("EnvelopeOnlyPermanentlyDisabled")]
    public static event Action<byte> OnEnvelopeOnlyPermanentlyDisabled = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

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

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>
    /// Allow or remove a verification key for a supported proof system. Owner only.
    /// </summary>
    public static void RegisterVerificationKey(byte proofSystem, UInt256 verificationKeyId, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ValidateProofSystem(proofSystem);
        ExecutionEngine.Assert(!verificationKeyId.Equals(UInt256.Zero), "verification key id must be non-zero");

        var key = VerificationKeyStorageKey(proofSystem, verificationKeyId);
        if (allowed)
        {
            Storage.Put(key, new byte[] { 1 });
        }
        else
        {
            Storage.Delete(key);
        }

        OnVerificationKeyRegistered(proofSystem, verificationKeyId, allowed);
    }

    /// <summary>
    /// Allow or remove the deployable verifier contract for a supported proof system.
    /// </summary>
    public static void RegisterProofVerifier(byte proofSystem, UInt160 verifier, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ValidateProofSystem(proofSystem);
        if (allowed)
        {
            ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid proof verifier");
            Storage.Put(ProofVerifierStorageKey(proofSystem), verifier);
        }
        else
        {
            Storage.Delete(ProofVerifierStorageKey(proofSystem));
        }

        OnProofVerifierRegistered(proofSystem, verifier, allowed);
    }

    /// <summary>Read the deployable verifier contract hash for a proof system, or zero if unset.</summary>
    [Safe]
    public static UInt160 GetProofVerifier(byte proofSystem)
    {
        if (!IsSupportedProofSystem(proofSystem)) return UInt160.Zero;
        var raw = Storage.Get(ProofVerifierStorageKey(proofSystem));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Enable or disable envelope-only acceptance for a supported proof system.
    /// </summary>
    /// <remarks>
    /// Envelope-only mode is intended for private devnets, test fixtures, and staged
    /// integrations before a proof-system verifier contract is deployed. Production
    /// chains should leave this disabled and register proof verifiers instead.
    /// </remarks>
    public static void SetEnvelopeOnlyAllowed(byte proofSystem, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ValidateProofSystem(proofSystem);
        if (allowed)
        {
            ExecutionEngine.Assert(!IsEnvelopeOnlyLocked(proofSystem),
                "envelope-only permanently disabled for this proof system");
            Storage.Put(EnvelopeOnlyStorageKey(proofSystem), new byte[] { 1 });
        }
        else
        {
            Storage.Delete(EnvelopeOnlyStorageKey(proofSystem));
        }

        OnEnvelopeOnlyModeSet(proofSystem, allowed);
    }

    /// <summary>True when envelope-only acceptance is explicitly enabled for a proof system.</summary>
    [Safe]
    public static bool IsEnvelopeOnlyAllowed(byte proofSystem)
    {
        if (!IsSupportedProofSystem(proofSystem)) return false;
        return Storage.Get(EnvelopeOnlyStorageKey(proofSystem)) != null;
    }

    /// <summary>
    /// Permanently forbid envelope-only acceptance for a supported proof system. Owner only.
    /// </summary>
    /// <remarks>
    /// One-way switch. Once a chain commits to real proof verification for a proof system
    /// (for example a rollup that must never accept an unverified batch), it can lock the
    /// door so that no future owner — compromised or otherwise — can re-enable envelope-only
    /// mode. This immediately disables any currently-enabled envelope-only flag for the proof
    /// system and blocks all future <see cref="SetEnvelopeOnlyAllowed"/> enables. There is no
    /// corresponding unlock; the decision is irreversible by design.
    /// </remarks>
    public static void DisableEnvelopeOnlyPermanently(byte proofSystem)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ValidateProofSystem(proofSystem);

        Storage.Delete(EnvelopeOnlyStorageKey(proofSystem));
        Storage.Put(EnvelopeOnlyLockStorageKey(proofSystem), new byte[] { 1 });

        OnEnvelopeOnlyModeSet(proofSystem, false);
        OnEnvelopeOnlyPermanentlyDisabled(proofSystem);
    }

    /// <summary>True when envelope-only acceptance is permanently locked off for a proof system.</summary>
    [Safe]
    public static bool IsEnvelopeOnlyLocked(byte proofSystem)
    {
        if (!IsSupportedProofSystem(proofSystem)) return false;
        return Storage.Get(EnvelopeOnlyLockStorageKey(proofSystem)) != null;
    }

    /// <summary>True when the proof-system/VK pair is governance-allowed.</summary>
    [Safe]
    public static bool IsVerificationKeyRegistered(byte proofSystem, UInt256 verificationKeyId)
    {
        if (!IsSupportedProofSystem(proofSystem)) return false;
        if (verificationKeyId.Equals(UInt256.Zero)) return false;
        return Storage.Get(VerificationKeyStorageKey(proofSystem, verificationKeyId)) != null;
    }

    /// <summary>
    /// Verify a canonical N4 batch commitment with <c>ProofType.Zk</c>.
    /// </summary>
    [Safe]
    public static bool Verify(byte[] commitmentBytes)
    {
        ExecutionEngine.Assert(commitmentBytes.Length >= ProofBytesOffset, "commitment missing proof length");
        ExecutionEngine.Assert(commitmentBytes[ProofTypeOffset] == ProofTypeZk, "commitment proofType is not Zk");

        var payloadLen = ReadUInt32(commitmentBytes, ProofLenOffset);
        ExecutionEngine.Assert(payloadLen > 0, "proof payload empty");
        ExecutionEngine.Assert(payloadLen <= MaxProofPayloadBytes, "proof payload too large");
        var payloadSize = (int)payloadLen;
        ExecutionEngine.Assert(ProofBytesOffset + payloadSize == commitmentBytes.Length,
            "commitment proof length mismatch");

        var publicInputHash = ReadFixedBytes(commitmentBytes, PublicInputHashOffset, 32);
        var payload = ReadFixedBytes(commitmentBytes, ProofBytesOffset, payloadSize);
        ExecutionEngine.Assert(payload.Length >= ZkPayloadProofBytesOffset, "ZK payload too small");
        ExecutionEngine.Assert(payload[0] == ZkPayloadVersion, "unsupported ZK payload version");

        var proofSystem = payload[1];
        ValidateProofSystem(proofSystem);

        var verificationKeyId = ReadUInt256(payload, ZkPayloadVerificationKeyOffset);
        ExecutionEngine.Assert(IsVerificationKeyRegistered(proofSystem, verificationKeyId),
            "verification key not registered");

        var proofLen = ReadUInt32(payload, ZkPayloadInnerProofLenOffset);
        ExecutionEngine.Assert(proofLen > 0, "inner proof empty");
        ExecutionEngine.Assert(proofLen <= MaxProofPayloadBytes, "inner proof too large");
        var proofSize = (int)proofLen;
        ExecutionEngine.Assert(ZkPayloadProofBytesOffset + proofSize == payload.Length,
            "ZK payload proof length mismatch");
        var proofBytes = ReadFixedBytes(payload, ZkPayloadProofBytesOffset, proofSize);

        var verifier = GetProofVerifier(proofSystem);
        if (verifier.IsValid && !verifier.IsZero)
        {
            return (bool)Contract.Call(
                verifier,
                "verifyZkProof",
                CallFlags.ReadOnly,
                new object[] { proofSystem, (byte[])verificationKeyId, publicInputHash, proofBytes });
        }

        ExecutionEngine.Assert(IsEnvelopeOnlyAllowed(proofSystem),
            "proof verifier not configured");
        return true;
    }

    private static void ValidateProofSystem(byte proofSystem)
    {
        ExecutionEngine.Assert(IsSupportedProofSystem(proofSystem),
            "proofSystem must be 1..4 (SP1/Risc0/Halo2/Axiom)");
    }

    private static bool IsSupportedProofSystem(byte proofSystem)
    {
        return proofSystem >= ProofSystemSp1 && proofSystem <= ProofSystemAxiom;
    }

    private static uint ReadUInt32(byte[] data, int offset)
    {
        return (uint)data[offset]
            | ((uint)data[offset + 1] << 8)
            | ((uint)data[offset + 2] << 16)
            | ((uint)data[offset + 3] << 24);
    }

    private static UInt256 ReadUInt256(byte[] data, int offset)
    {
        var slice = ReadFixedBytes(data, offset, 32);
        return (UInt256)slice;
    }

    private static byte[] ReadFixedBytes(byte[] data, int offset, int count)
    {
        var slice = new byte[count];
        for (var i = 0; i < count; i++) slice[i] = data[offset + i];
        return slice;
    }

    private static byte[] VerificationKeyStorageKey(byte proofSystem, UInt256 verificationKeyId)
    {
        var vk = (byte[])verificationKeyId;
        var key = new byte[34];
        key[0] = PrefixVerificationKey;
        key[1] = proofSystem;
        for (var i = 0; i < 32; i++) key[2 + i] = vk[i];
        return key;
    }

    private static byte[] ProofVerifierStorageKey(byte proofSystem)
    {
        return new[] { PrefixProofVerifier, proofSystem };
    }

    private static byte[] EnvelopeOnlyStorageKey(byte proofSystem)
    {
        return new[] { PrefixEnvelopeOnly, proofSystem };
    }

    private static byte[] EnvelopeOnlyLockStorageKey(byte proofSystem)
    {
        return new[] { PrefixEnvelopeOnlyLocked, proofSystem };
    }
}
