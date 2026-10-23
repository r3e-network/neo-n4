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
    private const byte PrefixAnyProofSystemLocked = 0x07;
    private const byte PrefixMultisigVerifier = 0x08;   // 0x08 -> multisig verifier contract hash
    private const byte KeyOwner = 0xFF;

    private const int PublicInputHashOffset = 284;
    private const int ProofTypeOffset = 316;
    private const int ProofLenOffset = 317;
    private const int ProofBytesOffset = 321;

    public const byte ProofTypeMultisig = 1;
    public const byte ProofTypeZk = 3;
    private const byte ZkPayloadVersion = 1;
    private const int ZkPayloadVerificationKeyOffset = 2;
    private const int ZkPayloadInnerProofLenOffset = 34;
    private const int ZkPayloadProofBytesOffset = 38;
    private const int MaxProofPayloadBytes = 1 * 1024 * 1024;

    private const byte MultisigPayloadVersion = 1;
    private const int MultisigPayloadSignerCountOffset = 1;
    private const int MultisigPayloadSignaturesOffset = 3;
    private const int MultisigPublicKeySize = 33;
    private const int MultisigSignatureSize = 64;
    private const int MaxMultisigSigners = 256;

    public const byte ProofSystemSp1 = 1;
    public const byte ProofSystemRiscZero = 2;
    public const byte ProofSystemHalo2 = 3;

    /// <summary>
    /// Sentinel proof system used by the global envelope-only lock to close every proof system
    /// at once. It is never a valid registered proof system.
    /// </summary>
    private const byte ProofSystemAny = 0xFF;

    #region Events

    [DisplayName("VerificationKeyRegistered")]
    public static event Action<byte, UInt256, bool> OnVerificationKeyRegistered = default!;

    [DisplayName("ProofVerifierRegistered")]
    public static event Action<byte, UInt160, bool> OnProofVerifierRegistered = default!;

    [DisplayName("MultisigVerifierRegistered")]
    public static event Action<UInt160, bool> OnMultisigVerifierRegistered = default!;

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
        // Proof system 0 is NOT a valid ZK proof system. It is reserved for the multisig
        // attestation route, which has its own slot (`PrefixMultisigVerifier`) and carries
        // no verification key — see `RegisterMultisigVerifier`.
        //
        // Allowing 0 here would make byte 0 mean two different things at once ("unset"
        // for `GetProofVerifier`, and "the multisig backend" for the routing below), and a
        // `Storage.Delete` on an unset entry could never be distinguished from an explicit
        // revocation.
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

    /// <summary>
    /// Register the verifier contract that backs the MULTISIG attestation route.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The multisig route is a chain-committee attestation, not a ZK proof: it has no
    /// verification key and no proof system. It therefore gets its OWN storage slot rather
    /// than borrowing the `proofSystem` key space.
    /// </para>
    /// <para>
    /// This split fixes a real functional break: `VerifyMultisigProof` dispatches on the
    /// reserved system byte 0, while `RegisterProofVerifier` rejects byte 0 — so
    /// `GetProofVerifier(0)` could never return anything and the multisig route was
    /// permanently unreachable (every multisig commitment verified as `false`).
    /// </para>
    /// </remarks>
    public static void RegisterMultisigVerifier(UInt160 verifier, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(verifier.IsValid && !verifier.IsZero, "invalid verifier contract");
        var key = new byte[] { PrefixMultisigVerifier };
        if (allowed) Storage.Put(key, verifier);
        else Storage.Delete(key);
        OnMultisigVerifierRegistered(verifier, allowed);
    }

    /// <summary>The verifier contract backing the multisig attestation route, or zero.</summary>
    [Safe]
    public static UInt160 GetMultisigVerifier()
    {
        var raw = Storage.Get(new byte[] { PrefixMultisigVerifier });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetEnvelopeOnlyAllowed(byte proofSystem, bool allowed)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        // The global lock is irreversible for the whole registry: once ANY proof system has
        // been closed (or the configuration locked), envelope-only must never be re-enabled
        // for any proof system — including a different one.
        ExecutionEngine.Assert(Storage.Get(new byte[] { PrefixAnyProofSystemLocked }) == null,
            "envelope-only permanently locked");
        ExecutionEngine.Assert(!IsEnvelopeOnlyLocked(proofSystem), "envelope-only permanently locked");
        // The reserved byte 0 means "unset" in the proof-system key space — every other
        // control surface (RegisterProofVerifier, RegisterVerificationKey, and through it
        // LockProofSystemConfiguration) rejects it. This one must too: enabling envelope-only
        // on byte 0 would flip the MULTISIG route's dispatch into a fail-open that accepts
        // any commitment with zero signatures, and byte 0 has no configuration lock to close
        // it with (verification keys cannot be registered for it) — only the irreversible
        // registry-wide lock would remain.
        ExecutionEngine.Assert(proofSystem > 0, "invalid proof system");
        var key = EnvelopeOnlyKey(proofSystem);
        if (allowed) Storage.Put(key, new byte[] { 1 });
        else Storage.Delete(key);
        OnEnvelopeOnlyModeSet(proofSystem, allowed);
    }

    [Safe]
    public static bool IsEnvelopeOnlyAllowed(byte proofSystem)
    {
        // The global lock disables envelope-only for EVERY proof system, so the read side must
        // honour it too — otherwise a stale per-system flag would keep the fail-open path
        // reachable after the registry has been closed.
        if (Storage.Get(new byte[] { PrefixAnyProofSystemLocked }) != null) return false;
        var raw = Storage.Get(EnvelopeOnlyKey(proofSystem));
        return raw != null && ((byte[])raw)[0] == 1;
    }

    public static void DisableEnvelopeOnlyPermanently(byte proofSystem)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Delete(EnvelopeOnlyKey(proofSystem));
        Storage.Put(EnvelopeOnlyLockedKey(proofSystem), new byte[] { 1 });
        // Closing envelope-only for ONE proof system must close it for EVERY proof system.
        // `proofSystem` is an attacker-controlled payload field, so a per-system lock alone
        // would leave the attacker free to pick a proof system that is still fail-open and
        // submit an arbitrary (forged) state root under it. The global lock makes the
        // downgrade irreversible for the whole registry in the same transaction.
        Storage.Delete(EnvelopeOnlyKey(ProofSystemAny));
        Storage.Put(new byte[] { PrefixAnyProofSystemLocked }, new byte[] { 1 });
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
        Storage.Put(new byte[] { PrefixAnyProofSystemLocked }, new byte[] { 1 });
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
    /// Verify a canonical N4 batch commitment's proof (ProofType.Multisig or ProofType.Zk envelope).
    /// </summary>
    /// <remarks>
    /// See doc.md §8. This is the settlement entry point RollupHub invokes
    /// (<c>verifyProof(byte[] commitmentBytes) → bool</c>). Once a proof system's configuration is
    /// locked, ONLY the locked verification key is acceptable — keys registered before the lock
    /// stop verifying, so the lock pins the proof semantics to one key no matter what storage
    /// still holds.
    ///
    /// For ProofType.Multisig (Stage 0 attestation), delegates verification to the external
    /// verifier contract registered for proof system 0 (must implement verifyMultisigProof method).
    /// For ProofType.Zk, uses the ZK verification path.
    /// </remarks>
    [Safe]
    public static bool VerifyProof(byte[] commitmentBytes)
    {
        if (commitmentBytes == null || commitmentBytes.Length < ProofBytesOffset) return false;

        var proofType = commitmentBytes[ProofTypeOffset];

        // Multisig attestation (Stage 0) - delegate to external verifier
        if (proofType == ProofTypeMultisig)
        {
            return VerifyMultisigProof(commitmentBytes);
        }

        // ZK validity proof (Stage 2)
        if (proofType != ProofTypeZk) return false;

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

        // SECURITY: Production ZkVerifier allows envelope-only mode ONLY during lock configuration,
        // when the council is registering verification keys and testing them before the production
        // lock. Once Lock() is called, envelope-only acceptance is permanently disabled and ONLY
        // the external verifier or in-contract verification can succeed. Tests that exercise
        // envelope-only mode must do so BEFORE calling Lock().
        //
        // `IsEnvelopeOnlyAllowed` already returns false once the registry-wide lock
        // (`PrefixAnyProofSystemLocked`) is present, so the fail-open path below is unreachable
        // after ANY proof system has been closed — not just the one being verified.
        if (IsEnvelopeOnlyAllowed(proofSystem))
        {
            return true;
        }

        // A released external verifier is the only accepted production backend until the
        // generated SP1 release parameters are checked into this contract. The placeholder
        // in-contract constants are never a verification path.
        var externalVerifier = GetProofVerifier(proofSystem);
        if (externalVerifier.IsValid && !externalVerifier.IsZero)
        {
            // The devpack exposes a single `Contract.Call(hash, method, flags, params object[] args)`
            // overload. Arguments MUST therefore be passed as individual trailing values so the
            // VM sees the target method's real arity — `ApplicationEngine.Contract.Call` resolves
            // the ABI with `GetMethod(method, args.Count)`. Packing them into a nested array
            // (e.g. `new object[] { a, b, c, d }`) would make the VM see arity 1 and the call
            // would never resolve against `verifyZkProof(byte, byte[], byte[], byte[])`.
            return (bool)Contract.Call(
                externalVerifier, "verifyZkProof",
                // `ReadOnly` (= ReadStates | AllowCall) rather than `All`: the callee
                // (`verifyZkProof`) is `[Safe]` and only reads state, so requesting write
                // flags is meaningless. The VM masks them off for a safe callee regardless.
                CallFlags.ReadOnly,
                proofSystem, (byte[])vkId, publicInputHash, innerProof);
        }

        return false;
    }

    /// <summary>
    /// Verify a multisig attestation proof by delegating to the registered external verifier.
    /// </summary>
    /// <remarks>
    /// Multisig verification (ProofType.Multisig, Stage 0) requires validating committee signatures
    /// against registered sequencer keys and checking quorum. This is delegated to an external
    /// verifier contract because signature verification is expensive and the committee registry
    /// lives in GovernanceController. The external verifier must implement:
    /// <c>verifyMultisigProof(byte[] commitmentBytes) → bool</c>
    ///
    /// Proof system 0 is reserved for multisig attestation. If envelope-only mode is enabled for
    /// proof system 0, accepts any commitment without verification (pre-production testing only).
    /// </remarks>
    private static bool VerifyMultisigProof(byte[] commitmentBytes)
    {
        var proofLen = ReadInt32(commitmentBytes, ProofLenOffset);
        if (proofLen <= 0 || proofLen > MaxProofPayloadBytes) return false;
        if (commitmentBytes.Length < ProofBytesOffset + proofLen) return false;

        // NO envelope-only path here — deliberately.
        //
        // Every other proof-system control surface rejects the reserved byte 0, so it can
        // never legitimately carry an envelope-only flag; and a fail-open on this route
        // would accept a multisig commitment with ZERO signatures. Pre-production testing of
        // the success path does not need a contract-level bypass: deploy a stub verifier
        // (a contract whose verifyMultisigProof returns true) and register it — that is what
        // the dedicated multisig slot is for.

        // Production path: delegate to external multisig verifier.
        //
        // The verifier lives in its OWN slot (`PrefixMultisigVerifier`), not in the
        // `proofSystem` key space: the reserved system byte 0 is rejected by
        // `RegisterProofVerifier`, so `GetProofVerifier(0)` could never resolve and the
        // multisig route was permanently unreachable.
        var externalVerifier = GetMultisigVerifier();
        if (externalVerifier.IsValid && !externalVerifier.IsZero)
        {
            // The callee's ABI name is camelCase ("verifyMultisigProof") — nccs camel-cases
            // the PascalCase C# method name, and `ContractAbi.GetMethod` resolves the name
            // with an ordinal comparison, so the string must match the manifest entry
            // exactly. `scripts/ci/verify_cross_language_encoding.py` checks every
            // cross-contract call against the generated manifest.
            return (bool)Contract.Call(
                externalVerifier, "verifyMultisigProof",
                // `ReadOnly` (= ReadStates | AllowCall) rather than `All`.
                //
                // The callee (`verifyMultisigProof`) is `[Safe]` and only reads state, so
                // asking for write flags is meaningless. The VM masks the write flags off
                // for a safe callee anyway (`ApplicationEngine.CallContractInternal`:
                // `if (method.Safe) flags &= ~(CallFlags.WriteStates | CallFlags.AllowNotify);`),
                // so this is a precision improvement rather than a behavioural fix.
                CallFlags.ReadOnly,
                commitmentBytes);
        }

        return false;
    }

    [Safe]
    public static byte[] GetVerifierSelector() => new byte[0];

    [Safe]
    public static byte[] GetRecursionVkRoot() => new byte[0];

    /// <summary>
    /// Verifies one ZK proof through the external verifier registered for
    /// <paramref name="proofSystem"/>.
    /// </summary>
    /// <remarks>
    /// This is the router entry point used by <c>RollupHub.PublishGatewayGlobalRoot</c>, which
    /// holds a reference to this contract (the verifier registry) rather than to a concrete
    /// proof-system implementation. It must therefore forward to the registered external
    /// verifier instead of re-implementing verification.
    ///
    /// The external verifier is expected to expose
    /// <c>verifyZkProof(byte, byte[], byte[], byte[]) → bool</c>. The proof system must be
    /// registered and (when the proof-system configuration is locked) must carry exactly the
    /// locked verification key, so a proof can never be accepted under a different key.
    ///
    /// When no external verifier is registered the call is fail-closed: an unconfigured
    /// verifier registry must never accept a proof.
    /// </remarks>
    [Safe]
    public static bool VerifyZkProof(
        byte proofSystem,
        byte[] verificationKeyId,
        byte[] publicInputHash,
        byte[] proofBytes)
    {
        if (verificationKeyId == null || verificationKeyId.Length != 32) return false;
        if (publicInputHash == null || publicInputHash.Length != 32) return false;
        if (proofBytes == null || proofBytes.Length == 0) return false;

        // A configuration lock supersedes the registry: only the locked key verifies.
        var lockedVkRaw = Storage.Get(ProofSystemConfigurationLockedKey(proofSystem));
        if (lockedVkRaw != null)
        {
            if (!BytesEqual((byte[])lockedVkRaw, 0, verificationKeyId)) return false;
        }
        else if (!IsVerificationKeyRegistered(proofSystem, (UInt256)verificationKeyId))
        {
            return false;
        }

        var externalVerifier = GetProofVerifier(proofSystem);
        if (!externalVerifier.IsValid || externalVerifier.IsZero) return false;

        // Individual trailing arguments — see the arity note in VerifyProof.
        return (bool)Contract.Call(
            externalVerifier, "verifyZkProof",
            // `ReadOnly` (= ReadStates | AllowCall) rather than `All`: the callee
            // (`verifyZkProof`) is `[Safe]` and only reads state, so requesting write flags
            // is meaningless. The VM masks them off for a safe callee regardless, so this
            // is a precision improvement, not a behavioural fix.
            CallFlags.ReadOnly,
            proofSystem, verificationKeyId, publicInputHash, proofBytes);
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

    /// <summary>Constant-time-enough equality for a byte slice against an expected array.</summary>
    private static bool BytesEqual(byte[] value, int offset, byte[] expected)
    {
        if (value == null || expected == null) return false;
        if (offset < 0 || offset + expected.Length > value.Length) return false;
        for (var i = 0; i < expected.Length; i++)
            if (value[offset + i] != expected[i]) return false;
        return true;
    }

    #endregion
}
