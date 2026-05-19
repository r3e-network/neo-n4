using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.NativeZkVerifier;

/// <summary>
/// Deployable NeoHub verifier adapter for <c>ProofType.Zk</c> settlement.
/// </summary>
/// <remarks>
/// This contract intentionally does not implement a SNARK/STARK verifier in ordinary
/// NeoVM bytecode. It validates the canonical N4 batch-commitment and RISC-V proof
/// payload envelope, checks that the verification key is governance-registered, then
/// delegates the heavy proof-system math to a native accelerator contract exposed by
/// the target L1 core. NeoHub stays deployable and minimally invasive, while ZK
/// verification remains efficient and explicit.
///
/// Native accelerator ABI:
/// <c>verifyZkProof(byte proofSystem, byte[] verificationKeyId, byte[] publicInputHash, byte[] proofBytes) : bool</c>.
/// </remarks>
[DisplayName("NeoHub.NativeZkVerifier")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Deployable ProofType.Zk verifier adapter backed by a native ZK accelerator.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.NativeZkVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class NativeZkVerifierContract : SmartContract
{
    private const byte KeyNativeAccelerator = 0x01;
    private const byte PrefixVerificationKey = 0x02;
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

    /// <summary>Emitted when the native accelerator hash changes.</summary>
    [DisplayName("NativeAcceleratorSet")]
    public static event Action<UInt160> OnNativeAcceleratorSet = default!;

    /// <summary>Emitted when a verification key is allowed or removed.</summary>
    [DisplayName("VerificationKeyRegistered")]
    public static event Action<byte, UInt256, bool> OnVerificationKeyRegistered = default!;

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

    /// <summary>
    /// Wire the L1 native accelerator. Owner only.
    /// </summary>
    public static void SetNativeAccelerator(UInt160 accelerator)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(accelerator.IsValid && !accelerator.IsZero, "invalid native accelerator");
        Storage.Put(new byte[] { KeyNativeAccelerator }, accelerator);
        OnNativeAcceleratorSet(accelerator);
    }

    /// <summary>Read the configured native accelerator hash, or zero if unset.</summary>
    [Safe]
    public static UInt160 GetNativeAccelerator()
    {
        var raw = Storage.Get(new byte[] { KeyNativeAccelerator });
        return raw == null ? UInt160.Zero : (UInt160)raw;
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

        var accelerator = GetNativeAccelerator();
        ExecutionEngine.Assert(accelerator.IsValid && !accelerator.IsZero,
            "native accelerator not wired");

        return (bool)Contract.Call(
            accelerator,
            "verifyZkProof",
            CallFlags.ReadOnly,
            new object[] { proofSystem, (byte[])verificationKeyId, publicInputHash, proofBytes });
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
}
