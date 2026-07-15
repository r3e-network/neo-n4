using System.Buffers.Binary;
using System.Security.Cryptography;
using Neo.SmartContract.Manifest;
using Neo.VM;

namespace NeoHub.Sp1Groth16Verifier.UnitTests;

[TestClass]
public class UT_Sp1Groth16Verifier
{
    private const byte Sp1 = 1;
    private const int SelectorSize = 4;
    private const int FieldElementSize = 32;
    private const int HeaderSize = SelectorSize + 3 * FieldElementSize;
    private const int ProofSize = HeaderSize + 256;
    private const int ExitCodeOffset = SelectorSize;
    private const int VkRootOffset = ExitCodeOffset + FieldElementSize;
    private const int NonceOffset = VkRootOffset + FieldElementSize;
    private const int ProofAOffset = HeaderSize;
    private const int ProofBOffset = ProofAOffset + 64;
    private const int ProofCOffset = ProofBOffset + 128;
    private const int CommitmentPublicInputHashOffset = 284;
    private const int CommitmentProofTypeOffset = 316;
    private const int CommitmentProofLengthOffset = 317;
    private const int CommitmentProofOffset = 321;

    private static readonly byte[] Selector = [0x43, 0x88, 0xA2, 0x1C];
    private static readonly byte[] VkRoot =
    [
        0x00, 0x2F, 0x85, 0x0E, 0xE9, 0x98, 0x97, 0x4D,
        0x6C, 0xC0, 0x0E, 0x50, 0xCD, 0x08, 0x14, 0xB0,
        0x98, 0xC0, 0x5B, 0xFA, 0xDE, 0x46, 0x6D, 0x28,
        0x57, 0x32, 0x40, 0xD0, 0x57, 0xF2, 0x53, 0x52,
    ];

    [TestMethod]
    public void Artifact_PinsSourceCompiledNefAndLeastPrivilegeManifest()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "contracts",
            "NeoHub.Sp1Groth16Verifier",
            "Sp1Groth16VerifierContract.cs");
        var sourceDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
        Assert.AreEqual(Sp1Groth16VerifierArtifact.SourceSha256, sourceDigest,
            "Regenerate the embedded NEF whenever the verifier source changes.");

        var digest = Convert.ToHexString(SHA256.HashData(Sp1Groth16VerifierArtifact.Nef)).ToLowerInvariant();
        Assert.AreEqual(Sp1Groth16VerifierArtifact.NefSha256, digest);

        var manifest = ContractManifest.Parse(Sp1Groth16VerifierArtifact.ManifestJson);
        Assert.HasCount(1, manifest.Permissions);
        var permission = manifest.Permissions[0].ToJson().ToString();
        StringAssert.Contains(permission, "0x726cb6e0cd8628a1350a611384688911ab75f51b");
        foreach (var method in new[]
        {
            "bn254Add", "bn254Deserialize", "bn254Equal", "bn254Mul", "bn254Pairing", "sha256",
        })
        {
            StringAssert.Contains(permission, method);
        }
        StringAssert.DoesNotMatch(permission, new System.Text.RegularExpressions.Regex("\\\"methods\\\":\\\"\\*\\\""));
    }

    [TestMethod]
    public void RouterArtifact_PinsSourceCompiledNefAndDynamicVerifierPermission()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "contracts",
            "NeoHub.ContractZkVerifier",
            "ContractZkVerifierContract.cs");
        var sourceDigest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourcePath))).ToLowerInvariant();
        Assert.AreEqual(ContractZkVerifierArtifact.SourceSha256, sourceDigest,
            "Regenerate the embedded router NEF whenever the router source changes.");

        var nefDigest = Convert.ToHexString(SHA256.HashData(ContractZkVerifierArtifact.Nef)).ToLowerInvariant();
        Assert.AreEqual(ContractZkVerifierArtifact.NefSha256, nefDigest);

        var manifest = ContractManifest.Parse(ContractZkVerifierArtifact.ManifestJson);
        Assert.HasCount(1, manifest.Permissions);
        var permission = manifest.Permissions[0].ToJson().ToString();
        StringAssert.Contains(permission, "\"contract\":\"*\"");
        StringAssert.Contains(permission, "\"methods\":\"*\"");
    }

    [TestMethod]
    public void Constants_MatchPinnedSp1Verifier()
    {
        using var harness = new NeoContractHarness();

        var selector = harness.InvokeBytes("getVerifierSelector");
        var vkRoot = harness.InvokeBytes("getRecursionVkRoot");

        Assert.AreEqual(VMState.HALT, selector.State, selector.Fault);
        Assert.AreEqual(VMState.HALT, vkRoot.State, vkRoot.Fault);
        CollectionAssert.AreEqual(Selector, selector.Result!);
        CollectionAssert.AreEqual(VkRoot, vkRoot.Result!);
    }

    [TestMethod]
    public void VerifyZkProof_WellFormedButInvalidPairing_ReturnsFalseWithinFeeLimit()
    {
        using var harness = new NeoContractHarness();

        var result = harness.InvokeBoolean(
            "verifyZkProof",
            Sp1,
            CanonicalProgramVKey(),
            new byte[FieldElementSize],
            StructurallyValidButFalseProof());

        Assert.AreEqual(VMState.HALT, result.State, result.Fault);
        Assert.IsFalse(result.Result);
        Assert.IsGreaterThan(0, result.FeeConsumed);
        Assert.IsLessThanOrEqualTo(100_00000000L, result.FeeConsumed);
    }

    [TestMethod]
    public void RustGeneratedPositiveVector_VerifiesThroughTerminalAndRouterWithinFeeLimit()
    {
        AssertVectorIntegrity();
        using var harness = new NeoContractHarness();

        var terminal = harness.InvokeBoolean(
            "verifyZkProof",
            Sp1,
            Sp1Groth16PositiveVector.ProgramVKey,
            Sp1Groth16PositiveVector.PublicInputHash,
            Sp1Groth16PositiveVector.Proof);

        Assert.AreEqual(VMState.HALT, terminal.State, terminal.Fault);
        Assert.IsTrue(terminal.Result, "the Rust-produced SP1 proof must satisfy the NeoVM pairing equation");
        Assert.IsGreaterThan(0, terminal.FeeConsumed);
        Assert.IsLessThanOrEqualTo(100_00000000L, terminal.FeeConsumed);

        harness.ConfigureRouterForSp1(Sp1Groth16PositiveVector.ProgramVKey);
        var routed = harness.InvokeRouterBoolean("verify", BuildCommitment(
            Sp1Groth16PositiveVector.ProgramVKey,
            Sp1Groth16PositiveVector.PublicInputHash,
            Sp1Groth16PositiveVector.Proof));

        Assert.AreEqual(VMState.HALT, routed.State, routed.Fault);
        Assert.IsTrue(routed.Result, "the production router must preserve the terminal verifier result");
        Assert.IsLessThanOrEqualTo(100_00000000L, routed.FeeConsumed);
    }

    [TestMethod]
    public void RustGeneratedPositiveVector_EveryBoundFieldAndProofPointFailsClosedWhenTampered()
    {
        AssertVectorIntegrity();
        using var harness = new NeoContractHarness();

        var wrongVKey = Sp1Groth16PositiveVector.ProgramVKey;
        wrongVKey[^1] ^= 0x01;
        AssertRejected(harness.InvokeBoolean(
            "verifyZkProof", Sp1, wrongVKey,
            Sp1Groth16PositiveVector.PublicInputHash, Sp1Groth16PositiveVector.Proof));

        var wrongPublicInput = Sp1Groth16PositiveVector.PublicInputHash;
        wrongPublicInput[^1] ^= 0x01;
        AssertRejected(harness.InvokeBoolean(
            "verifyZkProof", Sp1, Sp1Groth16PositiveVector.ProgramVKey,
            wrongPublicInput, Sp1Groth16PositiveVector.Proof));

        foreach (var offset in new[]
        {
            0,
            VkRootOffset,
            NonceOffset + FieldElementSize - 1,
            ProofAOffset + FieldElementSize - 1,
            ProofBOffset + FieldElementSize - 1,
            ProofCOffset + FieldElementSize - 1,
        })
        {
            var tampered = Sp1Groth16PositiveVector.Proof;
            tampered[offset] ^= 0x01;
            AssertRejected(harness.InvokeBoolean(
                "verifyZkProof", Sp1, Sp1Groth16PositiveVector.ProgramVKey,
                Sp1Groth16PositiveVector.PublicInputHash, tampered));
        }
    }

    [TestMethod]
    public void RustGeneratedPositiveVector_RouterRejectsAlternateKeyAndTamperedPublicInput()
    {
        using var harness = new NeoContractHarness();
        harness.ConfigureRouterForSp1(Sp1Groth16PositiveVector.ProgramVKey);

        var alternateVKey = Sp1Groth16PositiveVector.ProgramVKey;
        alternateVKey[^1] ^= 0x01;
        AssertRejected(harness.InvokeRouterBoolean("verify", BuildCommitment(
            alternateVKey,
            Sp1Groth16PositiveVector.PublicInputHash,
            Sp1Groth16PositiveVector.Proof)));

        var tamperedPublicInput = Sp1Groth16PositiveVector.PublicInputHash;
        tamperedPublicInput[^1] ^= 0x01;
        AssertRejected(harness.InvokeRouterBoolean("verify", BuildCommitment(
            Sp1Groth16PositiveVector.ProgramVKey,
            tamperedPublicInput,
            Sp1Groth16PositiveVector.Proof)));
    }

    [TestMethod]
    public void VerifyZkProof_RejectsWrongSystemAndLengths()
    {
        using var harness = new NeoContractHarness();
        var proof = StructurallyValidButFalseProof();

        AssertFault(harness.InvokeBoolean(
            "verifyZkProof", 2, CanonicalProgramVKey(), new byte[FieldElementSize], proof));
        AssertFault(harness.InvokeBoolean(
            "verifyZkProof", Sp1, new byte[31], new byte[FieldElementSize], proof));
        AssertFault(harness.InvokeBoolean(
            "verifyZkProof", Sp1, CanonicalProgramVKey(), new byte[31], proof));
        AssertFault(harness.InvokeBoolean(
            "verifyZkProof", Sp1, CanonicalProgramVKey(), new byte[FieldElementSize], new byte[ProofSize - 1]));
    }

    [TestMethod]
    public void VerifyZkProof_RejectsWrongSelectorExitCodeAndVkRoot()
    {
        using var harness = new NeoContractHarness();

        var wrongSelector = StructurallyValidButFalseProof();
        wrongSelector[0] ^= 0x01;
        AssertFault(Invoke(harness, wrongSelector));

        var nonZeroExit = StructurallyValidButFalseProof();
        nonZeroExit[ExitCodeOffset + FieldElementSize - 1] = 1;
        AssertFault(Invoke(harness, nonZeroExit));

        var wrongVkRoot = StructurallyValidButFalseProof();
        wrongVkRoot[VkRootOffset] ^= 0x01;
        AssertFault(Invoke(harness, wrongVkRoot));
    }

    [TestMethod]
    public void VerifyZkProof_RejectsNonCanonicalScalarAndMalformedPoint()
    {
        using var harness = new NeoContractHarness();
        var proof = StructurallyValidButFalseProof();

        AssertFault(harness.InvokeBoolean(
            "verifyZkProof",
            Sp1,
            Enumerable.Repeat((byte)0xFF, FieldElementSize).ToArray(),
            new byte[FieldElementSize],
            proof));

        var nonCanonicalNonce = StructurallyValidButFalseProof();
        Array.Fill(nonCanonicalNonce, (byte)0xFF, NonceOffset, FieldElementSize);
        AssertFault(Invoke(harness, nonCanonicalNonce));

        var malformedPoint = StructurallyValidButFalseProof();
        malformedPoint[ProofAOffset + FieldElementSize - 1] = 1;
        AssertFault(Invoke(harness, malformedPoint));
    }

    private static VmInvocation<bool> Invoke(NeoContractHarness harness, byte[] proof) =>
        harness.InvokeBoolean(
            "verifyZkProof",
            Sp1,
            CanonicalProgramVKey(),
            new byte[FieldElementSize],
            proof);

    private static void AssertFault(VmInvocation<bool> invocation) =>
        Assert.AreEqual(VMState.FAULT, invocation.State, invocation.Fault);

    private static void AssertRejected(VmInvocation<bool> invocation)
    {
        if (invocation.State == VMState.HALT)
        {
            Assert.IsFalse(invocation.Result, "a tampered proof must never return true");
            return;
        }

        Assert.AreEqual(VMState.FAULT, invocation.State, invocation.Fault);
    }

    private static void AssertVectorIntegrity()
    {
        Assert.AreEqual(1, Sp1Groth16PositiveVector.SchemaVersion);
        Assert.AreEqual("6.2.1", Sp1Groth16PositiveVector.Sp1Version);
        Assert.AreEqual(ProofSize, Sp1Groth16PositiveVector.Proof.Length);
        Assert.AreEqual(FieldElementSize, Sp1Groth16PositiveVector.ProgramVKey.Length);
        Assert.AreEqual(1 + FieldElementSize, Sp1Groth16PositiveVector.PublicValues.Length);
        Assert.AreEqual(FieldElementSize, Sp1Groth16PositiveVector.PublicInputHash.Length);
        Assert.AreEqual(0, Sp1Groth16PositiveVector.PublicValues[0]);
        CollectionAssert.AreEqual(
            Sp1Groth16PositiveVector.PublicInputHash,
            Sp1Groth16PositiveVector.PublicValues[1..]);
        Assert.AreEqual(
            Sp1Groth16PositiveVector.ProofSha256,
            Convert.ToHexString(SHA256.HashData(Sp1Groth16PositiveVector.Proof)).ToLowerInvariant());
        Assert.AreEqual(
            Sp1Groth16PositiveVector.ProgramVKeySha256,
            Convert.ToHexString(SHA256.HashData(Sp1Groth16PositiveVector.ProgramVKey)).ToLowerInvariant());
        Assert.AreEqual(
            Sp1Groth16PositiveVector.PublicValuesSha256,
            Convert.ToHexString(SHA256.HashData(Sp1Groth16PositiveVector.PublicValues)).ToLowerInvariant());
        Assert.AreEqual(
            Sp1Groth16PositiveVector.PublicInputHashSha256,
            Convert.ToHexString(SHA256.HashData(Sp1Groth16PositiveVector.PublicInputHash)).ToLowerInvariant());
    }

    private static byte[] BuildCommitment(byte[] programVKey, byte[] publicInputHash, byte[] proof)
    {
        var payload = new byte[38 + proof.Length];
        payload[0] = 1;
        payload[1] = Sp1;
        programVKey.CopyTo(payload, 2);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(34, 4), (uint)proof.Length);
        proof.CopyTo(payload, 38);

        var commitment = new byte[CommitmentProofOffset + payload.Length];
        publicInputHash.CopyTo(commitment, CommitmentPublicInputHashOffset);
        commitment[CommitmentProofTypeOffset] = 3;
        BinaryPrimitives.WriteUInt32LittleEndian(
            commitment.AsSpan(CommitmentProofLengthOffset, 4), (uint)payload.Length);
        payload.CopyTo(commitment, CommitmentProofOffset);
        return commitment;
    }

    private static byte[] StructurallyValidButFalseProof()
    {
        var proof = new byte[ProofSize];
        Selector.CopyTo(proof, 0);
        VkRoot.CopyTo(proof, VkRootOffset);
        return proof;
    }

    private static byte[] CanonicalProgramVKey()
    {
        var vkey = new byte[FieldElementSize];
        vkey[^1] = 1;
        return vkey;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Neo.L2.sln"))) return current.FullName;
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the neo-n4 repository root.");
    }
}
