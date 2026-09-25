using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.MultisigVerifier;

/// <summary>
/// Stage 0 multisig attestation verifier for Neo Elastic Network.
/// Validates committee signatures against registered sequencer keys from GovernanceController.
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 0 Attestation proof). This verifier is called by ZkVerifier
/// for ProofType.Multisig commitments. It decodes the multisig proof payload, verifies
/// each signature against the canonical public-input hash, and checks that signers are
/// registered sequencers with sufficient quorum.
///
/// Proof payload format (little-endian):
/// [1B version=1] [2B signerCount] (per signer: [33B compressed-secp256r1-pubkey] [64B sig])
/// </remarks>
[DisplayName("NeoHub.MultisigVerifier")]
[ContractAuthor("R3E Network", "dev@r3e.network")]
[ContractDescription("Multisig attestation verifier for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MultisigVerifier")]
[ContractPermission(Permission.Any, Method.Any)]
public class MultisigVerifierContract : SmartContract
{
    private const byte KeyOwner = 0xFF;
    private const byte KeyGovernanceController = 0x01;
    private const byte KeyMinQuorum = 0x02;

    private const int PublicInputHashOffset = 284;
    private const int ProofTypeOffset = 316;
    private const int ProofLenOffset = 317;
    private const int ProofBytesOffset = 321;

    private const byte MultisigPayloadVersion = 1;
    private const int MultisigPayloadSignerCountOffset = 1;
    private const int MultisigPayloadSignaturesOffset = 3;
    private const int MultisigPublicKeySize = 33;
    private const int MultisigSignatureSize = 64;
    private const int MaxMultisigSigners = 256;

    public const int DefaultMinQuorum = 1;  // Minimum signatures required

    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    [DisplayName("GovernanceControllerSet")]
    public static event Action<UInt160> OnGovernanceControllerSet = default!;

    [DisplayName("MinQuorumSet")]
    public static event Action<uint> OnMinQuorumSet = default!;

    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var owner = (UInt160)data;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyMinQuorum }, (BigInteger)DefaultMinQuorum);
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

    [Safe]
    public static UInt160 GetGovernanceController()
    {
        var raw = Storage.Get(new byte[] { KeyGovernanceController });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    public static void SetGovernanceController(UInt160 controller)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(controller.IsValid && !controller.IsZero, "invalid governance controller");
        Storage.Put(new byte[] { KeyGovernanceController }, controller);
        OnGovernanceControllerSet(controller);
    }

    [Safe]
    public static uint GetMinQuorum()
    {
        var raw = Storage.Get(new byte[] { KeyMinQuorum });
        return raw == null ? DefaultMinQuorum : (uint)(BigInteger)raw;
    }

    public static void SetMinQuorum(uint minQuorum)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(minQuorum > 0, "quorum must be positive");
        ExecutionEngine.Assert(minQuorum <= MaxMultisigSigners, "quorum exceeds max signers");
        Storage.Put(new byte[] { KeyMinQuorum }, (BigInteger)minQuorum);
        OnMinQuorumSet(minQuorum);
    }

    /// <summary>
    /// Verify a multisig attestation proof for a batch commitment.
    /// </summary>
    /// <remarks>
    /// Called by ZkVerifier for ProofType.Multisig commitments. Extracts the proof payload,
    /// decodes signatures, verifies each signature against the public input hash using
    /// CryptoLib.VerifyWithECDsa, and checks that all signers are registered sequencers
    /// via GovernanceController.IsSequencerRegistered. Returns true only if the number of
    /// valid signatures meets the minimum quorum.
    /// </remarks>
    [Safe]
    public static bool VerifyMultisigProof(byte[] commitmentBytes)
    {
        if (commitmentBytes == null || commitmentBytes.Length < ProofBytesOffset) return false;

        var governance = GetGovernanceController();
        if (governance == UInt160.Zero) return false;

        var proofLen = ReadInt32(commitmentBytes, ProofLenOffset);
        if (proofLen <= 0 || proofLen > 1024 * 1024) return false;
        if (commitmentBytes.Length < ProofBytesOffset + proofLen) return false;

        var payload = ReadBytes(commitmentBytes, ProofBytesOffset, proofLen);
        if (payload.Length < MultisigPayloadSignaturesOffset) return false;
        if (payload[0] != MultisigPayloadVersion) return false;

        var signerCount = ReadUInt16(payload, MultisigPayloadSignerCountOffset);
        if (signerCount == 0 || signerCount > MaxMultisigSigners) return false;

        var expectedSize = MultisigPayloadSignaturesOffset + signerCount * (MultisigPublicKeySize + MultisigSignatureSize);
        if (payload.Length != expectedSize) return false;

        var publicInputHash = ReadBytes(commitmentBytes, PublicInputHashOffset, 32);
        var chainId = ReadUInt32(commitmentBytes, 0);

        var validSignatures = 0;
        var pos = MultisigPayloadSignaturesOffset;

        for (var i = 0; i < signerCount; i++)
        {
            var pubKeyBytes = ReadBytes(payload, pos, MultisigPublicKeySize);
            pos += MultisigPublicKeySize;
            var signature = ReadBytes(payload, pos, MultisigSignatureSize);
            pos += MultisigSignatureSize;

            // Decode public key
            var pubKey = (ECPoint)pubKeyBytes;

            // Check if signer is registered sequencer for this chain
            var isRegistered = (bool)Contract.Call(
                governance, "isSequencerRegistered",
                CallFlags.ReadOnly,
                chainId, pubKey);
            if (!isRegistered) continue;

            // Verify signature
            var isValid = CryptoLib.VerifyWithECDsa(
                (ByteString)publicInputHash,
                pubKey,
                (ByteString)signature,
                NamedCurveHash.secp256r1SHA256);

            if (isValid) validSignatures++;
        }

        var minQuorum = GetMinQuorum();
        return validSignatures >= minQuorum;
    }

    private static int ReadInt32(byte[] data, int offset) =>
        data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24);

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadUInt32(byte[] data, int offset) =>
        (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));

    private static byte[] ReadBytes(byte[] data, int offset, int length)
    {
        var res = new byte[length];
        for (var i = 0; i < length; i++) res[i] = data[offset + i];
        return res;
    }
}
