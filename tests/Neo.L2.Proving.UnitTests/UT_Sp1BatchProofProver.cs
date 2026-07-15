using System.Security.Cryptography;
using System.Text.Json;
using Neo.L2.Batch;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.State;

namespace Neo.L2.Proving.UnitTests;

/// <summary>Security-boundary tests for the production SP1 batch file-queue client.</summary>
[TestClass]
public sealed class UT_Sp1BatchProofProver
{
    [TestMethod]
    public async Task ProveAsync_ValidManifest_ReturnsBoundGroth16Envelope()
    {
        using var queue = new TemporaryDirectory();
        var (artifact, request) = Request();
        var daemon = RespondOnceAsync(queue.Path);
        var prover = CreateProver(queue.Path, artifact.VerificationKeyId);

        var result = await prover.ProveAsync(request);

        await daemon;
        Assert.AreEqual(ProofType.Zk, result.Kind);
        Assert.AreEqual(StateRootCalculator.HashPublicInputs(request.PublicInputs), result.PublicInputHash);
        var payload = RiscVProofPayload.Decode(result.Proof.Span);
        Assert.AreEqual(ProofSystem.Sp1, payload.ProofSystem);
        Assert.AreEqual(artifact.VerificationKeyId, payload.VerificationKeyId);
        Assert.AreEqual(Sp1BatchProofProver.Groth16ProofSize, payload.ProofBytes.Length);
        Assert.IsTrue(payload.ProofBytes.Span.SequenceEqual(ProofBytes()));
    }

    [TestMethod]
    public async Task ProveAsync_TamperedRequestDigest_IsRejected()
        => await AssertRejectedAsync(plan => plan with { RequestSha256 = new string('f', 64) });

    [TestMethod]
    public async Task ProveAsync_TamperedVerificationKey_IsRejected()
        => await AssertRejectedAsync(plan => plan with
        {
            VerificationKey = Enumerable.Repeat((byte)0xB2, 32).ToArray(),
        });

    [TestMethod]
    public async Task ProveAsync_TamperedExecutionSemantic_IsRejected()
        => await AssertRejectedAsync(plan => plan with
        {
            ExecutionSemanticId = Enumerable.Repeat((byte)0xC3, 32).ToArray(),
        });

    [TestMethod]
    public async Task ProveAsync_TamperedPublicValues_IsRejected()
        => await AssertRejectedAsync(plan =>
        {
            var values = plan.PublicValues.ToArray();
            values[^1] ^= 1;
            return plan with { PublicValues = values };
        });

    [TestMethod]
    public async Task ProveAsync_TamperedProofDigest_IsRejected()
        => await AssertRejectedAsync(plan => plan with { ProofSha256 = new string('0', 64) });

    [TestMethod]
    public async Task ProveAsync_TraversalFilename_IsRejected()
        => await AssertRejectedAsync(plan => plan with { ProofFile = "../forged.proof.bin" });

    [TestMethod]
    public async Task ProveAsync_ExistingExactArtifacts_AreIdempotent()
    {
        using var queue = new TemporaryDirectory();
        var (artifact, request) = Request();
        var daemon = RespondOnceAsync(queue.Path);
        var prover = CreateProver(queue.Path, artifact.VerificationKeyId);

        var first = await prover.ProveAsync(request);
        await daemon;
        var requestPath = Directory.GetFiles(queue.Path, "*.batch.bin").Single();
        var requestBytes = await File.ReadAllBytesAsync(requestPath);

        var second = await prover.ProveAsync(request);

        Assert.AreEqual(first, second);
        CollectionAssert.AreEqual(requestBytes, await File.ReadAllBytesAsync(requestPath));
        Assert.AreEqual(1, Directory.GetFiles(queue.Path, "*.batch.bin").Length);
        Assert.AreEqual(0, Directory.GetFiles(queue.Path, "*.tmp-*").Length);
    }

    [TestMethod]
    public async Task ProveAsync_PublicInputsMismatch_FailsBeforePublication()
    {
        using var queue = new TemporaryDirectory();
        var (artifact, request) = Request();
        var prover = CreateProver(queue.Path, artifact.VerificationKeyId);
        var tampered = request with
        {
            PublicInputs = request.PublicInputs with { BatchNumber = request.PublicInputs.BatchNumber + 1 },
        };

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await prover.ProveAsync(tampered));

        Assert.AreEqual(0, Directory.GetFiles(queue.Path).Length);
    }

    [TestMethod]
    public async Task AcknowledgeSettlementAsync_PublishesPrivateContentBoundMarker()
    {
        using var queue = new TemporaryDirectory();
        var artifactHash = Hash(0xA5);
        var prover = CreateProver(queue.Path, Hash(0x44));

        await prover.AcknowledgeSettlementAsync(artifactHash);

        var path = Path.Combine(
            queue.Path,
            Hex(artifactHash.GetSpan())
                + Sp1BatchFileQueueProtocol.SettlementAcknowledgementSuffix);
        CollectionAssert.AreEqual(artifactHash.GetSpan().ToArray(), await File.ReadAllBytesAsync(path));
        if (!OperatingSystem.IsWindows())
            Assert.AreEqual(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path));
    }

    private static async Task AssertRejectedAsync(Func<ResponsePlan, ResponsePlan> mutate)
    {
        using var queue = new TemporaryDirectory();
        var (artifact, request) = Request();
        var daemon = RespondOnceAsync(queue.Path, mutate);
        var prover = CreateProver(queue.Path, artifact.VerificationKeyId);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await prover.ProveAsync(request));

        await daemon;
    }

    private static Sp1BatchProofProver CreateProver(string queuePath, UInt256 verificationKey) =>
        new(
            queuePath,
            verificationKey,
            resultTimeout: TimeSpan.FromSeconds(5),
            pollInterval: TimeSpan.FromMilliseconds(10));

    private static (ProofWitnessArtifactV1 Artifact, ProofRequest Request) Request()
    {
        var bytes = FixtureBytes();
        var artifact = ProofWitnessArtifactSerializer.Decode(bytes);
        return (artifact, new ProofRequest
        {
            PublicInputs = artifact.PublicInputs,
            Witness = bytes,
            Kind = ProofType.Zk,
        });
    }

    private static async Task RespondOnceAsync(
        string queuePath,
        Func<ResponsePlan, ResponsePlan>? mutate = null)
    {
        var requestPath = await WaitForSingleFileAsync(queuePath, "*.batch.bin");
        var requestBytes = await File.ReadAllBytesAsync(requestPath);
        var artifact = ProofWitnessArtifactSerializer.Decode(requestBytes);
        var requestId = AtomicFileQueueTransport.Hex(artifact.ContentHash.GetSpan());
        var publicInputHash = StateRootCalculator.HashPublicInputs(artifact.PublicInputs);
        var publicValues = new byte[Sp1BatchProofProver.CommittedPublicValuesSize];
        publicInputHash.GetSpan().CopyTo(publicValues.AsSpan(1));
        var plan = new ResponsePlan
        {
            Proof = ProofBytes(),
            VerificationKey = artifact.VerificationKeyId.GetSpan().ToArray(),
            PublicValues = publicValues,
            RequestSha256 = Hex(SHA256.HashData(requestBytes)),
            ExecutionSemanticId = artifact.ExecutionSemanticId.GetSpan().ToArray(),
            ProofFile = requestId + Sp1BatchFileQueueProtocol.ProofSuffix,
        };
        if (mutate is not null) plan = mutate(plan);

        var proofFile = requestId + Sp1BatchFileQueueProtocol.ProofSuffix;
        var verificationKeyFile = requestId + Sp1BatchFileQueueProtocol.VerificationKeySuffix;
        var publicValuesFile = requestId + Sp1BatchFileQueueProtocol.PublicValuesSuffix;
        await WritePrivateAsync(Path.Combine(queuePath, proofFile), plan.Proof);
        await WritePrivateAsync(
            Path.Combine(queuePath, verificationKeyFile), plan.VerificationKey);
        await WritePrivateAsync(
            Path.Combine(queuePath, publicValuesFile), plan.PublicValues);

        var manifest = new Sp1BatchProofResultManifest
        {
            SchemaVersion = Sp1BatchFileQueueProtocol.SchemaVersion,
            Status = Sp1BatchFileQueueProtocol.SucceededStatus,
            RequestId = requestId,
            RequestSha256 = plan.RequestSha256,
            ArtifactContentHash = requestId,
            PublicInputHash = Hex(publicInputHash.GetSpan()),
            ProofSystem = (byte)WitnessProofSystem.Sp1,
            ExecutionSemanticId = Hex(plan.ExecutionSemanticId),
            VerificationKey = Hex(plan.VerificationKey),
            RequestFile = Path.GetFileName(requestPath),
            ProofFile = plan.ProofFile,
            VerificationKeyFile = verificationKeyFile,
            PublicValuesFile = publicValuesFile,
            ProofSha256 = plan.ProofSha256 ?? Hex(SHA256.HashData(plan.Proof)),
            VerificationKeySha256 = Hex(SHA256.HashData(plan.VerificationKey)),
            PublicValuesSha256 = Hex(SHA256.HashData(plan.PublicValues)),
        };
        var resultPath = Path.Combine(
            queuePath, requestId + Sp1BatchFileQueueProtocol.ResultManifestSuffix);
        await WriteAtomicallyAsync(
            resultPath, JsonSerializer.SerializeToUtf8Bytes(manifest));
    }

    private static async Task WriteAtomicallyAsync(string path, byte[] bytes)
    {
        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        await WritePrivateAsync(temporaryPath, bytes);
        File.Move(temporaryPath, path);
    }

    private static async Task WritePrivateAsync(string path, ReadOnlyMemory<byte> bytes)
    {
        await File.WriteAllBytesAsync(path, bytes);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static async Task<string> WaitForSingleFileAsync(string directory, string pattern)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            timeout.Token.ThrowIfCancellationRequested();
            var files = Directory.GetFiles(directory, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length == 1) return files[0];
            if (files.Length > 1)
                throw new InvalidOperationException(
                    $"expected one {pattern} file, found {files.Length}");
            await Task.Delay(10, timeout.Token);
        }
    }

    private static byte[] FixtureBytes()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "stateful_batch_v1.hex");
        return Convert.FromHexString(string.Concat(File.ReadAllLines(path)));
    }

    private static byte[] ProofBytes() =>
        Enumerable.Repeat((byte)0x5A, Sp1BatchProofProver.Groth16ProofSize).ToArray();

    private static string Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    private static UInt256 Hash(byte value)
    {
        var bytes = new byte[UInt256.Length];
        bytes[0] = value;
        return new UInt256(bytes);
    }

    private sealed record ResponsePlan
    {
        public required byte[] Proof { get; init; }
        public required byte[] VerificationKey { get; init; }
        public required byte[] PublicValues { get; init; }
        public required string RequestSha256 { get; init; }
        public required byte[] ExecutionSemanticId { get; init; }
        public required string ProofFile { get; init; }
        public string? ProofSha256 { get; init; }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "neo-n4-batch-prover-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
