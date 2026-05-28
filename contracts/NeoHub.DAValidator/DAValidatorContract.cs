using System;
using System.ComponentModel;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.DAValidator;

/// <summary>
/// L1 data-availability validator for validium-style Neo Elastic Network chains.
/// Rollup/L1 DA only requires a non-zero commitment; DAC mode additionally requires
/// M-of-N secp256r1 committee attestations before SettlementManager can finalize.
/// </summary>
[DisplayName("NeoHub.DAValidator")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("L1 data-availability validator for Neo Elastic Network batches.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.DAValidator")]
[ContractPermission(Permission.Any, Method.Any)]
public class DAValidatorContract : SmartContract
{
    private const byte PrefixCommittee = 0x01;
    private const byte PrefixValidated = 0x02;
    private const byte KeyDARegistry = 0xFD;
    private const byte KeyOwner = 0xFF;

    private const byte ModeL1 = 0;
    private const byte ModeNeoFS = 1;
    private const byte ModeExternal = 2;
    private const byte ModeDAC = 3;
    private const int PublicKeyLength = 33;
    private const int SignatureLength = 64;
    private const int MaxCommitteeSize = 64;

    /// <summary>Emitted whenever a DA committee is registered or replaced.</summary>
    [DisplayName("DACommitteeRegistered")]
    public static event Action<uint, byte, byte> OnCommitteeRegistered = default!;

    /// <summary>Emitted when a batch DA commitment has passed validation.</summary>
    [DisplayName("DAValidated")]
    public static event Action<uint, ulong, UInt256, byte> OnDAValidated = default!;

    /// <summary>Set owner and DARegistry wiring.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var daRegistry = (UInt160)arr[1];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(daRegistry.IsValid && !daRegistry.IsZero, "invalid DA registry");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyDARegistry }, daRegistry);
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>DARegistry hash this validator is paired with.</summary>
    [Safe]
    public static UInt160 GetDARegistry()
    {
        var raw = Storage.Get(new byte[] { KeyDARegistry });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>
    /// Register the DAC committee for one L2 chain. <paramref name="committeeBlob"/>
    /// is the ordered concatenation of 33-byte compressed secp256r1 public keys.
    /// </summary>
    public static void RegisterCommittee(uint chainId, byte threshold, byte[] committeeBlob)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(threshold > 0, "threshold must be positive");
        ExecutionEngine.Assert(committeeBlob.Length > 0, "committee blob is empty");
        ExecutionEngine.Assert(committeeBlob.Length % PublicKeyLength == 0,
            "committee blob must contain 33-byte public keys");
        var size = committeeBlob.Length / PublicKeyLength;
        ExecutionEngine.Assert(size <= MaxCommitteeSize, "committee too large");
        ExecutionEngine.Assert(threshold <= size, "threshold exceeds committee size");

        var stored = new byte[2 + committeeBlob.Length];
        stored[0] = threshold;
        stored[1] = (byte)size;
        for (var i = 0; i < committeeBlob.Length; i++) stored[2 + i] = committeeBlob[i];

        Storage.Put(CommitteeKey(chainId), stored);
        OnCommitteeRegistered(chainId, threshold, (byte)size);
    }

    /// <summary>Read the raw committee header + public keys for a chain.</summary>
    [Safe]
    public static byte[] GetCommittee(uint chainId)
    {
        var raw = Storage.Get(CommitteeKey(chainId));
        return raw == null ? new byte[0] : (byte[])raw;
    }

    /// <summary>
    /// Submit and persist an attestation that a DAC batch is available. Proof format:
    /// <c>[2B sigCount LE] + sigCount * ([1B signerIndex] [64B secp256r1 signature])</c>.
    /// The signed message is <c>"N4DA" || chainId || batchNumber || commitment || daMode</c>.
    /// </summary>
    public static bool SubmitAttestation(
        uint chainId,
        ulong batchNumber,
        UInt256 commitment,
        byte daMode,
        byte[] proofBytes)
    {
        ExecutionEngine.Assert(VerifyAttestation(chainId, batchNumber, commitment, daMode, proofBytes),
            "DA attestation rejected");
        ExecutionEngine.Assert(Storage.Get(ValidatedKey(chainId, batchNumber)) == null,
            "attestation already submitted for this batch");
        var value = new byte[33];
        value[0] = daMode;
        var commitmentBytes = (byte[])commitment;
        for (var i = 0; i < 32; i++) value[1 + i] = commitmentBytes[i];
        Storage.Put(ValidatedKey(chainId, batchNumber), value);
        OnDAValidated(chainId, batchNumber, commitment, daMode);
        return true;
    }

    /// <summary>True if this batch has already received a matching valid DAC attestation.</summary>
    [Safe]
    public static bool IsValidated(uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
    {
        var raw = Storage.Get(ValidatedKey(chainId, batchNumber));
        if (raw == null) return false;
        var bytes = (byte[])raw;
        if (bytes.Length != 33 || bytes[0] != daMode) return false;
        var commitmentBytes = (byte[])commitment;
        for (var i = 0; i < 32; i++)
        {
            if (bytes[1 + i] != commitmentBytes[i]) return false;
        }

        return true;
    }

    /// <summary>
    /// SettlementManager calls this during finalization. Rollup/NeoFS/external modes
    /// require a non-zero commitment; DAC additionally requires a previously submitted
    /// committee attestation matching this batch and commitment.
    /// </summary>
    [Safe]
    public static bool Validate(uint chainId, ulong batchNumber, UInt256 commitment, byte daMode)
    {
        if (chainId == 0 || daMode > ModeDAC || commitment.Equals(UInt256.Zero)) return false;
        if (daMode == ModeL1 || daMode == ModeNeoFS || daMode == ModeExternal) return true;
        return IsValidated(chainId, batchNumber, commitment, daMode);
    }

    /// <summary>Verify a DAC proof without persisting it.</summary>
    [Safe]
    public static bool VerifyAttestation(
        uint chainId,
        ulong batchNumber,
        UInt256 commitment,
        byte daMode,
        byte[] proofBytes)
    {
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(daMode == ModeDAC, "attestations are only required for DAC mode");
        ExecutionEngine.Assert(!commitment.Equals(UInt256.Zero), "commitment must be non-zero");
        ExecutionEngine.Assert(proofBytes.Length >= 2, "proof too short");

        var committeeRaw = Storage.Get(CommitteeKey(chainId));
        ExecutionEngine.Assert(committeeRaw != null, "no DA committee for chain");
        var committee = (byte[])committeeRaw!;
        ExecutionEngine.Assert(committee.Length >= 2, "committee malformed");
        var threshold = committee[0];
        var size = committee[1];
        ExecutionEngine.Assert(size <= MaxCommitteeSize, "committee too large for verification");
        ExecutionEngine.Assert(threshold > 0, "threshold must be positive");
        ExecutionEngine.Assert(threshold <= size, "threshold exceeds committee size");
        ExecutionEngine.Assert(committee.Length == 2 + size * PublicKeyLength, "committee length mismatch");

        var sigCount = (int)proofBytes[0] | ((int)proofBytes[1] << 8);
        ExecutionEngine.Assert(sigCount >= threshold, "signature count below threshold");
        ExecutionEngine.Assert(proofBytes.Length == 2 + sigCount * (1 + SignatureLength),
            "proof length mismatch");

        var message = BuildAttestationMessage(chainId, batchNumber, commitment, daMode);
        var seen = new byte[(MaxCommitteeSize + 7) / 8];
        var valid = 0;
        for (var i = 0; i < sigCount; i++)
        {
            var off = 2 + i * (1 + SignatureLength);
            var signerIdx = proofBytes[off];
            ExecutionEngine.Assert(signerIdx < size, "signer index outside committee");
            var byteIdx = signerIdx / 8;
            var bit = (byte)(1 << (signerIdx % 8));
            ExecutionEngine.Assert((seen[byteIdx] & bit) == 0, "duplicate signer");
            seen[byteIdx] = (byte)(seen[byteIdx] | bit);

            var pubkey = new byte[PublicKeyLength];
            var pkOff = 2 + signerIdx * PublicKeyLength;
            for (var j = 0; j < PublicKeyLength; j++) pubkey[j] = committee[pkOff + j];

            var sig = new byte[SignatureLength];
            for (var j = 0; j < SignatureLength; j++) sig[j] = proofBytes[off + 1 + j];

            var ok = CryptoLib.VerifyWithECDsa(
                (ByteString)message,
                (ECPoint)pubkey,
                (ByteString)sig,
                NamedCurveHash.secp256r1SHA256);
            ExecutionEngine.Assert(ok, "signature verification failed");
            valid++;
        }

        return valid >= threshold;
    }

    private static byte[] BuildAttestationMessage(
        uint chainId,
        ulong batchNumber,
        UInt256 commitment,
        byte daMode)
    {
        var bytes = new byte[4 + 4 + 8 + 32 + 1];
        var pos = 0;
        bytes[pos++] = 0x4E; // N
        bytes[pos++] = 0x34; // 4
        bytes[pos++] = 0x44; // D
        bytes[pos++] = 0x41; // A
        bytes[pos++] = (byte)chainId;
        bytes[pos++] = (byte)(chainId >> 8);
        bytes[pos++] = (byte)(chainId >> 16);
        bytes[pos++] = (byte)(chainId >> 24);
        bytes[pos++] = (byte)batchNumber;
        bytes[pos++] = (byte)(batchNumber >> 8);
        bytes[pos++] = (byte)(batchNumber >> 16);
        bytes[pos++] = (byte)(batchNumber >> 24);
        bytes[pos++] = (byte)(batchNumber >> 32);
        bytes[pos++] = (byte)(batchNumber >> 40);
        bytes[pos++] = (byte)(batchNumber >> 48);
        bytes[pos++] = (byte)(batchNumber >> 56);
        var commitmentBytes = (byte[])commitment;
        for (var i = 0; i < 32; i++) bytes[pos + i] = commitmentBytes[i];
        pos += 32;
        bytes[pos] = daMode;
        return bytes;
    }

    private static byte[] CommitteeKey(uint chainId)
    {
        var k = new byte[5];
        k[0] = PrefixCommittee;
        k[1] = (byte)chainId;
        k[2] = (byte)(chainId >> 8);
        k[3] = (byte)(chainId >> 16);
        k[4] = (byte)(chainId >> 24);
        return k;
    }

    private static byte[] ValidatedKey(uint chainId, ulong batchNumber)
    {
        var k = new byte[13];
        k[0] = PrefixValidated;
        k[1] = (byte)chainId;
        k[2] = (byte)(chainId >> 8);
        k[3] = (byte)(chainId >> 16);
        k[4] = (byte)(chainId >> 24);
        k[5] = (byte)batchNumber;
        k[6] = (byte)(batchNumber >> 8);
        k[7] = (byte)(batchNumber >> 16);
        k[8] = (byte)(batchNumber >> 24);
        k[9] = (byte)(batchNumber >> 32);
        k[10] = (byte)(batchNumber >> 40);
        k[11] = (byte)(batchNumber >> 48);
        k[12] = (byte)(batchNumber >> 56);
        return k;
    }
}
