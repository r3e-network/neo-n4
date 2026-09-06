using System.Buffers.Binary;
using System.ComponentModel;
using System.Numerics;
using Neo;
using Neo.Extensions;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.ZkVerifier (Pillar 3). These pin the fail-closed registry and lock
/// semantics against the compiled contract: verification keys are scoped per proof system, a
/// configuration lock supersedes the registry (every key except the locked one stops verifying),
/// and locking atomically disables envelope-only acceptance.
/// </summary>
[TestClass]
public class UT_ZkVerifier_Vm
{
    private const byte ProofSystemSp1 = 1;
    private const byte ProofSystemRiscZero = 2;

    private const int OffPublicInputHash = 284, OffProofType = 316, OffProofLen = 317, ProofBytesOffset = 321;

    private static UInt256 Vk(byte fill) => new(R(fill));

    private static byte[] R(byte fill) { var b = new byte[32]; for (var i = 0; i < 32; i++) b[i] = fill; return b; }

    private static (TestEngine engine, NeoHubZkVerifier verifier, UInt160 owner) Deploy()
    {
        var engine = new TestEngine(true);
        var owner = engine.Sender;
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Neo.L2.sln")))
            root = root.Parent;
        Assert.IsNotNull(root, "repository root");
        var sc = Path.Combine(root!.FullName, "contracts", "NeoHub.ZkVerifier", "bin", "sc");
        var nef = File.ReadAllBytes(Path.Combine(sc, "NeoHub.ZkVerifier.nef"))
            .AsSerializable<Neo.SmartContract.NefFile>();
        var manifest = Neo.SmartContract.Manifest.ContractManifest.Parse(
            File.ReadAllText(Path.Combine(sc, "NeoHub.ZkVerifier.manifest.json")));
        var verifier = engine.Deploy<NeoHubZkVerifier>(nef, manifest, owner);
        return (engine, verifier, owner);
    }

    private static byte[] BuildCommitmentBytes(byte proofSystem, UInt256 vkId, byte proofType = 3)
    {
        var innerProof = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var proofLen = 38 + innerProof.Length;
        var c = new byte[ProofBytesOffset + proofLen];
        R(0x10).CopyTo(c.AsSpan(0, 32));
        R(0x20).CopyTo(c.AsSpan(32, 32));
        R(0xA1).CopyTo(c.AsSpan(OffPublicInputHash, 32));
        c[OffProofType] = proofType;
        BinaryPrimitives.WriteInt32LittleEndian(c.AsSpan(OffProofLen, 4), proofLen);

        c[ProofBytesOffset] = 1; // ZkPayloadVersion
        c[ProofBytesOffset + 1] = proofSystem;
        vkId.GetSpan().ToArray().CopyTo(c.AsSpan(ProofBytesOffset + 2, 32));
        BinaryPrimitives.WriteInt32LittleEndian(
            c.AsSpan(ProofBytesOffset + 34, 4), innerProof.Length);
        innerProof.CopyTo(c.AsSpan(ProofBytesOffset + 38, innerProof.Length));
        return c;
    }

    [TestMethod]
    public void Deploy_InitializesOwner()
    {
        var (_, verifier, owner) = Deploy();
        Assert.AreEqual(owner, verifier.Owner);
    }

    [TestMethod]
    public void VerificationKeyRegistry_IsScopedPerProofSystem()
    {
        // The storage key must encode the proof system: the same 32-byte id under SP1 and
        // RiscZero is two independent registry entries, never one shared slot.
        var (_, verifier, _) = Deploy();
        var vk = Vk(0x71);

        verifier.RegisterVerificationKey(ProofSystemSp1, vk, true);
        Assert.IsTrue(verifier.IsVerificationKeyRegistered(ProofSystemSp1, vk)!.Value);
        Assert.IsFalse(verifier.IsVerificationKeyRegistered(ProofSystemRiscZero, vk)!.Value);

        verifier.RegisterVerificationKey(ProofSystemRiscZero, vk, true);
        verifier.RegisterVerificationKey(ProofSystemSp1, vk, false);
        Assert.IsFalse(verifier.IsVerificationKeyRegistered(ProofSystemSp1, vk)!.Value);
        Assert.IsTrue(verifier.IsVerificationKeyRegistered(ProofSystemRiscZero, vk)!.Value);
    }

    [TestMethod]
    public void VerifyProof_AcceptsEnvelopeOnlyForRegisteredKey()
    {
        var (_, verifier, _) = Deploy();
        var vk = Vk(0x72);
        verifier.RegisterVerificationKey(ProofSystemSp1, vk, true);

        var commitment = BuildCommitmentBytes(ProofSystemSp1, vk);
        Assert.IsFalse(verifier.VerifyProof(commitment)!.Value,
            "without envelope-only or real verification parameters, fail closed");

        verifier.SetEnvelopeOnlyAllowed(ProofSystemSp1, true);
        Assert.IsTrue(verifier.VerifyProof(commitment)!.Value);
    }

    [TestMethod]
    public void VerifyProof_RejectsUnregisteredKeyEvenInEnvelopeOnly()
    {
        var (_, verifier, _) = Deploy();
        verifier.SetEnvelopeOnlyAllowed(ProofSystemSp1, true);

        var commitment = BuildCommitmentBytes(ProofSystemSp1, Vk(0x73));
        Assert.IsFalse(verifier.VerifyProof(commitment)!.Value);
    }

    [TestMethod]
    public void VerifyProof_RejectsWrongProofTypeAndWrongProofSystemScope()
    {
        var (_, verifier, _) = Deploy();
        var vk = Vk(0x74);
        verifier.RegisterVerificationKey(ProofSystemSp1, vk, true);
        verifier.SetEnvelopeOnlyAllowed(ProofSystemSp1, true);

        Assert.IsFalse(verifier.VerifyProof(BuildCommitmentBytes(ProofSystemSp1, vk, proofType: 2))!.Value,
            "non-ZK commitments are not this contract's input");

        // Registered under SP1 only: the RiscZero envelope must not verify.
        var riscEnvelope = BuildCommitmentBytes(ProofSystemRiscZero, vk);
        Assert.IsFalse(verifier.VerifyProof(riscEnvelope)!.Value);
    }

    [TestMethod]
    public void Lock_PinsVerificationToTheLockedKey()
    {
        var (_, verifier, _) = Deploy();
        var lockedVk = Vk(0x75);
        var strayVk = Vk(0x76);
        verifier.RegisterVerificationKey(ProofSystemSp1, lockedVk, true);
        verifier.RegisterVerificationKey(ProofSystemSp1, strayVk, true);
        verifier.SetEnvelopeOnlyAllowed(ProofSystemSp1, true);

        Assert.ThrowsExactly<TestException>(
            () => verifier.LockProofSystemConfiguration(ProofSystemSp1, lockedVk));
        Assert.IsFalse(verifier.IsProofSystemConfigurationLocked(ProofSystemSp1)!.Value);
        Assert.IsTrue(verifier.IsEnvelopeOnlyAllowed(ProofSystemSp1)!.Value);
    }

    [TestMethod]
    public void Lock_IsIrreversible()
    {
        var (engine, verifier, _) = Deploy();
        var external = UInt160.Parse("0x" + new string('6', 40));
        engine.FromHash<ExternalZkVerifier>(external, checkExistence: false);
        verifier.RegisterProofVerifier(ProofSystemSp1, external, true);
        var vk = Vk(0x78);
        verifier.RegisterVerificationKey(ProofSystemSp1, vk, true);
        verifier.LockProofSystemConfiguration(ProofSystemSp1, vk);

        Assert.IsTrue(verifier.IsProofSystemConfigurationLocked(ProofSystemSp1)!.Value);
        Assert.IsFalse(verifier.IsEnvelopeOnlyAllowed(ProofSystemSp1)!.Value);
        Assert.IsTrue(verifier.IsEnvelopeOnlyLocked(ProofSystemSp1)!.Value);
        Assert.ThrowsExactly<TestException>(
            () => verifier.LockProofSystemConfiguration(ProofSystemSp1, vk));
        Assert.ThrowsExactly<TestException>(
            () => verifier.SetEnvelopeOnlyAllowed(ProofSystemSp1, true));
    }

    [TestMethod]
    public void SetOwner_RejectsZeroAddress()
    {
        var (_, verifier, _) = Deploy();
        Assert.ThrowsExactly<TestException>(() => verifier.Owner = UInt160.Zero);
    }
}

public abstract class ExternalZkVerifier(SmartContractInitialize initialize) : SmartContract(initialize)
{
    [DisplayName("verifyZkProof")]
    public abstract bool VerifyZkProof(BigInteger? proofSystem, byte[]? verificationKeyId, byte[]? publicInputHash, byte[]? proofBytes);
}

