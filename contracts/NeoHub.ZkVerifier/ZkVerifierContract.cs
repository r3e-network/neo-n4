using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ZkVerifier;

/// <summary>
/// Unified Pillar 3 ZK validity verifier for the Neo Elastic Network.
/// Combines commitment envelope parsing, verification key registry, and
/// SP1 6.2.1 SDK v6.1-compatible Groth16/BN254 pairing verification.
/// See doc.md §8.
/// </summary>
[DisplayName("NeoHub.ZkVerifier")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Unified ZK validity verifier + BN254 Groth16 math for Neo Elastic Network.")]
[ContractVersion("0.2.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ZkVerifier")]
[ContractPermission(
    "0x726cb6e0cd8628a1350a611384688911ab75f51b",
    "sha256",
    "bn254Deserialize",
    "bn254Add",
    "bn254Mul",
    "bn254Pairing",
    "bn254Equal")]
[ContractPermission(Permission.Any, Method.Any)]
public class ZkVerifierContract : SmartContract
{
    private const byte PrefixVerificationKey = 0x02;
    private const byte PrefixProofVerifier = 0x03;
    private const byte PrefixEnvelopeOnly = 0x04;
    private const byte PrefixEnvelopeOnlyLocked = 0x05;
    private const byte PrefixProofSystemConfigurationLocked = 0x06;
    private const byte KeyOwner = 0xFF;

    private const int PublicInputHashOffset = 284;
    private const int ProofTypeOffset = 316;
    private const int ProofLenOffset = 317;
    private const int ProofBytesOffset = 321;

    public const byte ProofTypeZk = 3;
    private const byte ZkPayloadVersion = 1;
    private const int ZkPayloadVerificationKeyOffset = 2;
    private const int ZkPayloadInnerProofLenOffset = 34;
    private const int ZkPayloadProofBytesOffset = 38;
    private const int MaxProofPayloadBytes = 1 * 1024 * 1024;

    public const byte ProofSystemSp1 = 1;
    public const byte ProofSystemRiscZero = 2;
    public const byte ProofSystemHalo2 = 3;

    #region Events

    [DisplayName("VerificationKeyRegistered")]
    public static event Action<byte, UInt256, bool> OnVerificationKeyRegistered = default!;

    [DisplayName("ProofVerifierRegistered")]
    public static event Action<byte, UInt160, bool> OnProofVerifierRegistered = default!;

    [DisplayName("EnvelopeOnlyModeSet")]
    public static event Action<byte, bool> OnEnvelopeOnlyModeSet = default!;

    [DisplayName("EnvelopeOnlyPermanentlyDisabled")]
    public static event Action<byte> OnEnvelopeOnlyPermanentlyDisabled = default!;

    [DisplayName("ProofSystemConfigurationLocked")]
    public static event Action<byte, UInt256, UInt160> OnProofSystemConfigurationLocked = default!;

    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    #endregion

    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var owner = (UInt160)data;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
    }

    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    #region Verification Key & Router Registry

    public static void RegisterVerificationKey(byte proofSystem, UInt256 verificationKeyId, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsProofSystemConfigurationLocked(proofSystem), "configuration locked");
        ExecutionEngine.Assert(proofSystem > 0, "invalid proof system");
        ExecutionEngine.Assert(verificationKeyId.IsValid && !verificationKeyId.IsZero, "invalid verification key");

        var key = VerificationKeyKey(proofSystem, verificationKeyId);
        if (allowed) Storage.Put(key, new byte[] { 1 });
        else Storage.Delete(key);
        OnVerificationKeyRegistered(proofSystem, verificationKeyId, allowed);
    }

    [Safe]
    public static bool IsVerificationKeyRegistered(byte proofSystem, UInt256 verificationKeyId)
    {
        return Storage.Get(VerificationKeyKey(proofSystem, verificationKeyId)) != null;
    }

    public static void RegisterProofVerifier(byte proofSystem, UInt160 verifier, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsProofSystemConfigurationLocked(proofSystem), "configuration locked");
        ExecutionEngine.Assert(proofSystem > 0, "invalid proof system");
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier contract");

        var key = ProofVerifierKey(proofSystem);
        if (allowed) Storage.Put(key, verifier);
        else Storage.Delete(key);
        OnProofVerifierRegistered(proofSystem, verifier, allowed);
    }

    [Safe]
    public static UInt160 GetProofVerifier(byte proofSystem)
    {
        var raw = Storage.Get(ProofVerifierKey(proofSystem));
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetEnvelopeOnlyAllowed(byte proofSystem, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsEnvelopeOnlyLocked(proofSystem), "envelope-only permanently locked");
        var key = EnvelopeOnlyKey(proofSystem);
        if (allowed) Storage.Put(key, new byte[] { 1 });
        else Storage.Delete(key);
        OnEnvelopeOnlyModeSet(proofSystem, allowed);
    }

    [Safe]
    public static bool IsEnvelopeOnlyAllowed(byte proofSystem)
    {
        var raw = Storage.Get(EnvelopeOnlyKey(proofSystem));
        return raw != null && ((byte[])raw)[0] == 1;
    }

    public static void DisableEnvelopeOnlyPermanently(byte proofSystem)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Delete(EnvelopeOnlyKey(proofSystem));
        Storage.Put(EnvelopeOnlyLockedKey(proofSystem), new byte[] { 1 });
        OnEnvelopeOnlyPermanentlyDisabled(proofSystem);
    }

    [Safe]
    public static bool IsEnvelopeOnlyLocked(byte proofSystem)
    {
        var raw = Storage.Get(EnvelopeOnlyLockedKey(proofSystem));
        return raw != null && ((byte[])raw)[0] == 1;
    }

    public static void LockProofSystemConfiguration(byte proofSystem, UInt256 verificationKeyId)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(!IsProofSystemConfigurationLocked(proofSystem), "already locked");
        ExecutionEngine.Assert(IsVerificationKeyRegistered(proofSystem, verificationKeyId), "vk must be registered");

        var verifier = GetProofVerifier(proofSystem);
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero,
            "a trusted external verifier is required before locking proof configuration");
        // Atomic downgrade closure: locking a proof system must not depend on the owner having
        // separately flipped independent switches in the right order. Envelope-only acceptance is
        // disabled and irreversibly locked here, and verification pins to exactly the locked key
        // (VerifyProof rejects every other key once this record exists — including keys
        // registered earlier).
        Storage.Delete(EnvelopeOnlyKey(proofSystem));
        Storage.Put(EnvelopeOnlyLockedKey(proofSystem), new byte[] { 1 });
        Storage.Put(ProofSystemConfigurationLockedKey(proofSystem), (byte[])verificationKeyId);
        OnProofSystemConfigurationLocked(proofSystem, verificationKeyId, GetProofVerifier(proofSystem));
    }

    [Safe]
    public static bool IsProofSystemConfigurationLocked(byte proofSystem)
    {
        return Storage.Get(ProofSystemConfigurationLockedKey(proofSystem)) != null;
    }

    #endregion

    #region Proof Verification

    /// <summary>
    /// Verify a canonical N4 batch commitment's ZK proof (ProofType.Zk envelope).
    /// </summary>
    /// <remarks>
    /// See doc.md §8. This is the settlement entry point RollupHub invokes
    /// (<c>verifyProof(byte[] commitmentBytes) → bool</c>). Once a proof system's configuration is
    /// locked, ONLY the locked verification key is acceptable — keys registered before the lock
    /// stop verifying, so the lock pins the proof semantics to one key no matter what storage
    /// still holds.
    /// </remarks>
    [Safe]
    public static bool VerifyProof(byte[] commitmentBytes)
    {
        if (commitmentBytes == null || commitmentBytes.Length < ProofBytesOffset) return false;
        if (commitmentBytes[ProofTypeOffset] != ProofTypeZk) return false;

        var proofLen = ReadInt32(commitmentBytes, ProofLenOffset);
        if (proofLen <= 0 || proofLen > MaxProofPayloadBytes) return false;
        if (commitmentBytes.Length < ProofBytesOffset + proofLen) return false;

        var payload = ReadBytes(commitmentBytes, ProofBytesOffset, proofLen);
        if (payload.Length < ZkPayloadProofBytesOffset) return false;
        if (payload[0] != ZkPayloadVersion) return false;

        var proofSystem = payload[1];
        var vkId = (UInt256)ReadBytes(payload, ZkPayloadVerificationKeyOffset, 32);

        // A configuration lock supersedes the registry: only the locked key verifies. Otherwise
        // the key must be registered for this proof system.
        var lockedVkRaw = Storage.Get(ProofSystemConfigurationLockedKey(proofSystem));
        if (lockedVkRaw != null)
        {
            if (!vkId.Equals((UInt256)lockedVkRaw)) return false;
        }
        else if (!IsVerificationKeyRegistered(proofSystem, vkId))
        {
            return false;
        }

        var innerProofLen = ReadInt32(payload, ZkPayloadInnerProofLenOffset);
        if (innerProofLen < 0 || payload.Length < ZkPayloadProofBytesOffset + innerProofLen) return false;
        var innerProof = ReadBytes(payload, ZkPayloadProofBytesOffset, innerProofLen);

        var publicInputHash = ReadBytes(commitmentBytes, PublicInputHashOffset, 32);

        // SECURITY CHECK: Production deployments MUST disable envelope-only mode before mainnet launch.
        // This check enforces that only verified proofs are accepted on production networks.
        if (IsEnvelopeOnlyAllowed(proofSystem))
        {
            ExecutionEngine.Assert(false, "envelope-only mode forbidden for production deployment");
        }

        // A released external verifier is the only accepted production backend until the
        // generated SP1 release parameters are checked into this contract. The placeholder
        // in-contract constants are never a verification path.
        var externalVerifier = GetProofVerifier(proofSystem);
        if (externalVerifier.IsValid && !externalVerifier.IsZero)
        {
            return (bool)Contract.Call(
                externalVerifier, "verifyZkProof",
                CallFlags.All,
                new object[] { proofSystem, (byte[])vkId, publicInputHash, innerProof }, 1000000);
        }

        return false;
    }

    [Safe]
    public static byte[] GetVerifierSelector() => new byte[0];

    [Safe]
    public static byte[] GetRecursionVkRoot() => new byte[0];

    [Safe]
    public static bool VerifyZkProof(
        byte proofSystem,
        byte[] verificationKeyId,
        byte[] publicInputHash,
        byte[] proofBytes)
    {
        // No generated SP1 release parameters are checked into this source tree. The former
        // zero/fixture constants are intentionally not a verifier: until a typed external
        // verifier is registered, this legacy direct-call surface is fail-closed.
        return false;
    }

    #endregion

    #region Mathematical Constants & Helpers

    /// <summary>
    /// Storage key for a registered verification key: <c>0x02 ‖ proofSystem ‖ vkId(32)</c>.
    /// </summary>
    /// <remarks>
    /// The proofSystem byte is part of the key: the same 32-byte id under different proof systems
    /// (SP1 / RiscZero / Halo2) must be independent registry entries, never one shared slot.
    /// </remarks>
    private static byte[] VerificationKeyKey(byte ps, UInt256 vk)
    {
        var res = new byte[34];
        res[0] = PrefixVerificationKey;
        res[1] = ps;
        var b = (byte[])vk;
        for (var i = 0; i < 32; i++) res[2 + i] = b[i];
        return res;
    }

    private static byte[] ProofVerifierKey(byte ps) => new byte[] { PrefixProofVerifier, ps };
    private static byte[] EnvelopeOnlyKey(byte ps) => new byte[] { PrefixEnvelopeOnly, ps };
    private static byte[] EnvelopeOnlyLockedKey(byte ps) => new byte[] { PrefixEnvelopeOnlyLocked, ps };
    private static byte[] ProofSystemConfigurationLockedKey(byte ps) => new byte[] { PrefixProofSystemConfigurationLocked, ps };

    private static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    private static byte[] ReadBytes(byte[] data, int offset, int length)
    {
        var res = new byte[length];
        for (var i = 0; i < length; i++) res[i] = data[offset + i];
        return res;
    }

    #endregion
}
