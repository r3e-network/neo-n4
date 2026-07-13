using System.Security.Cryptography;
using System.Text.Json;
using Neo.L2;
using Neo.Plugins.L2Gateway;

namespace Neo.Plugins.L2Gateway.UnitTests;

/// <summary>Security-boundary tests for the production Gateway SP1 file-queue client.</summary>
[TestClass]
public sealed class UT_Sp1GatewayProofProver
{
    private static readonly UInt160 MessageRouter =
        UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt256 ReplayDomain = H(0xD1);
    private static readonly UInt256 GatewayVerificationKey = H(0xA1);

    [TestMethod]
    public async Task ProveAsync_ValidManifest_ReturnsCanonicalGroth16Proof()
    {
        using var queue = new TemporaryDirectory();
        var (binding, aggregate) = Statement();
        var daemon = RespondOnceAsync(queue.Path, binding);
        var prover = CreateProver(queue.Path);

        var proof = await prover.ProveAsync(binding, aggregate);

        await daemon;
        Assert.AreEqual(Sp1GatewayProofProver.Groth16ProofSize, proof.Length);
        Assert.IsTrue(proof.Span.SequenceEqual(ProofBytes()));
    }

    [TestMethod]
    public async Task ProveAsync_BindingHashOnlyProof_IsRejected()
    {
        using var queue = new TemporaryDirectory();
        var (binding, aggregate) = Statement();
        var daemon = RespondOnceAsync(
            queue.Path,
            binding,
            state => state.Plan with
            {
                Proof = GatewayProofBindingSerializer.ComputeHash(binding).GetSpan().ToArray(),
            });
        var prover = CreateProver(queue.Path);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await prover.ProveAsync(binding, aggregate));

        await daemon;
    }

    [TestMethod]
    public async Task ProveAsync_TamperedRequestHash_IsRejected()
    {
        await AssertRejectedAsync(
            (plan, _) => plan with { RequestHash = new string('0', 64) });
    }

    [TestMethod]
    public async Task ProveAsync_TamperedProofSystem_IsRejected()
    {
        await AssertRejectedAsync((plan, _) => plan with { ProofSystem = 2 });
    }

    [TestMethod]
    public async Task ProveAsync_TamperedBackend_IsRejected()
    {
        await AssertRejectedAsync((plan, _) => plan with { AggregationBackendId = 0xC1 });
    }

    [TestMethod]
    public async Task ProveAsync_TamperedVerificationKey_IsRejected()
    {
        await AssertRejectedAsync(
            (plan, _) => plan with { VerificationKey = Enumerable.Repeat((byte)0xB2, 32).ToArray() });
    }

    [TestMethod]
    public async Task ProveAsync_TamperedPublicValues_IsRejected()
    {
        await AssertRejectedAsync(
            (plan, _) =>
            {
                var values = plan.PublicValues.ToArray();
                values[^1] ^= 0x01;
                return plan with { PublicValues = values };
            });
    }

    [TestMethod]
    public async Task ProveAsync_TamperedProofDigest_IsRejected()
    {
        await AssertRejectedAsync(
            (plan, _) => plan with { ProofSha256 = new string('f', 64) });
    }

    [TestMethod]
    public async Task ProveAsync_UnexpectedArtifactFilename_IsRejected()
    {
        await AssertRejectedAsync(
            (plan, _) => plan with { ProofFile = "../forged.gateway-proof.bin" });
    }

    [TestMethod]
    public async Task ProveAsync_ExistingExactManifest_IsIdempotent()
    {
        using var queue = new TemporaryDirectory();
        var (binding, aggregate) = Statement();
        var daemon = RespondOnceAsync(queue.Path, binding);
        var prover = CreateProver(queue.Path);

        var first = await prover.ProveAsync(binding, aggregate);
        await daemon;
        var requestManifest = Directory.GetFiles(
            queue.Path,
            "*.gateway-request.json",
            SearchOption.TopDirectoryOnly).Single();
        var requestPayload = Path.ChangeExtension(requestManifest, null)!;
        requestPayload = requestPayload[..^".gateway-request".Length] + ".gateway-request.bin";
        var manifestBytes = await File.ReadAllBytesAsync(requestManifest);
        var payloadBytes = await File.ReadAllBytesAsync(requestPayload);

        var second = await prover.ProveAsync(binding, aggregate);

        Assert.IsTrue(first.Span.SequenceEqual(second.Span));
        CollectionAssert.AreEqual(manifestBytes, await File.ReadAllBytesAsync(requestManifest));
        CollectionAssert.AreEqual(payloadBytes, await File.ReadAllBytesAsync(requestPayload));
        Assert.AreEqual(1, Directory.GetFiles(queue.Path, "*.gateway-request.json").Length);
        Assert.AreEqual(1, Directory.GetFiles(queue.Path, "*.gateway-request.bin").Length);
        Assert.AreEqual(0, Directory.GetFiles(queue.Path, "*.tmp-*", SearchOption.TopDirectoryOnly).Length);
    }

    [TestMethod]
    public async Task ProveAsync_RequestUsesCanonicalBindingAndBatchEncodings()
    {
        using var queue = new TemporaryDirectory();
        var (binding, aggregate) = Statement();
        var daemon = RespondOnceAsync(queue.Path, binding);
        var prover = CreateProver(queue.Path);

        await prover.ProveAsync(binding, aggregate);
        await daemon;

        var requestPath = Directory.GetFiles(queue.Path, "*.gateway-request.bin").Single();
        var bytes = await File.ReadAllBytesAsync(requestPath);
        Assert.IsTrue(bytes.AsSpan(0, 8).SequenceEqual("NEO4GWP1"u8));
        Assert.AreEqual(GatewayProofBindingSerializer.EncodedSize, 170);
        var decodedBinding = GatewayProofBindingSerializer.Decode(bytes.AsSpan(8, 170));
        Assert.AreEqual(
            GatewayProofBindingSerializer.ComputeHash(binding),
            GatewayProofBindingSerializer.ComputeHash(decodedBinding));
        Assert.AreEqual(1U, System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(
            bytes.AsSpan(178, 4)));
    }

    [TestMethod]
    public async Task ProveAsync_MismatchedBindingVerificationKey_FailsBeforeQueuePublication()
    {
        using var queue = new TemporaryDirectory();
        var (binding, aggregate) = Statement();
        var prover = CreateProver(queue.Path);
        var tampered = binding with { VerificationKeyId = H(0xB2) };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await prover.ProveAsync(tampered, aggregate));

        Assert.AreEqual(0, Directory.GetFiles(queue.Path).Length);
    }

    [TestMethod]
    public async Task ProveAsync_MismatchedGlobalRoot_FailsBeforeQueuePublication()
    {
        using var queue = new TemporaryDirectory();
        var (binding, aggregate) = Statement();
        var prover = CreateProver(queue.Path);
        var tampered = binding with { GlobalMessageRoot = H(0xB2) };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await prover.ProveAsync(tampered, aggregate));

        Assert.AreEqual(0, Directory.GetFiles(queue.Path).Length);
    }

    private static async Task AssertRejectedAsync(
        Func<DaemonResponsePlan, GatewaySp1ProofRequestManifest, DaemonResponsePlan> mutate)
    {
        using var queue = new TemporaryDirectory();
        var (binding, aggregate) = Statement();
        var daemon = RespondOnceAsync(queue.Path, binding, plan => mutate(plan.Plan, plan.Request));
        var prover = CreateProver(queue.Path);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(
            async () => await prover.ProveAsync(binding, aggregate));

        await daemon;
    }

    private static Sp1GatewayProofProver CreateProver(string queuePath) => new(
        queuePath,
        GatewayVerificationKey,
        resultTimeout: TimeSpan.FromSeconds(5),
        pollInterval: TimeSpan.FromMilliseconds(10));

    private static (GatewayProofBinding Binding, AggregatedCommitment Aggregate) Statement()
    {
        var constituent = Batch();
        var constituents = new[] { constituent };
        var aggregate = new AggregatedCommitment
        {
            Constituents = constituents,
            GlobalMessageRoot = constituent.L2ToL2MessageRoot,
            ConstituentCommitmentsRoot =
                GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(constituents),
            AggregatedProof = ReadOnlyMemory<byte>.Empty,
            BackendId = Sp1GatewayProofProver.RecursiveAggregationBackendId,
        };
        var binding = GatewayProofBindingSerializer.Create(
            MessageRouter,
            ReplayDomain,
            batchEpoch: 77,
            aggregate,
            Sp1GatewayProofProver.Sp1ProofSystem,
            GatewayVerificationKey);
        return (binding, aggregate);
    }

    private static L2BatchCommitment Batch() => new()
    {
        ChainId = 1001,
        BatchNumber = 42,
        FirstBlock = 1,
        LastBlock = 2,
        PreStateRoot = H(0x01),
        PostStateRoot = H(0x02),
        TxRoot = H(0x03),
        ReceiptRoot = H(0x04),
        WithdrawalRoot = H(0x05),
        L2ToL1MessageRoot = H(0x06),
        L2ToL2MessageRoot = H(0x07),
        DACommitment = H(0x08),
        PublicInputHash = H(0x09),
        ProofType = ProofType.Zk,
        Proof = Enumerable.Repeat((byte)0xAB, Sp1GatewayProofProver.Groth16ProofSize).ToArray(),
    };

    private static async Task RespondOnceAsync(
        string queuePath,
        GatewayProofBinding binding,
        Func<(DaemonResponsePlan Plan, GatewaySp1ProofRequestManifest Request), DaemonResponsePlan>? mutate = null)
    {
        var requestManifestPath = await WaitForSingleFileAsync(
            queuePath,
            "*.gateway-request.json");
        var request = JsonSerializer.Deserialize<GatewaySp1ProofRequestManifest>(
            await File.ReadAllBytesAsync(requestManifestPath))
            ?? throw new InvalidOperationException("request manifest deserialized to null");
        var publicValues = new byte[Sp1GatewayProofProver.CommittedPublicValuesSize];
        GatewayProofBindingSerializer.ComputeHash(binding).GetSpan().CopyTo(publicValues.AsSpan(1));
        var plan = new DaemonResponsePlan
        {
            Proof = ProofBytes(),
            VerificationKey = GatewayVerificationKey.GetSpan().ToArray(),
            PublicValues = publicValues,
            RequestHash = request.RequestHash,
            BindingHash = request.BindingHash,
            ProofSystem = request.ProofSystem,
            AggregationBackendId = request.AggregationBackendId,
        };
        if (mutate is not null) plan = mutate((plan, request));

        var proofPath = Path.Combine(queuePath, request.RequestId + ".gateway-proof.bin");
        var verificationKeyPath =
            Path.Combine(queuePath, request.RequestId + ".gateway-verification-key.bin");
        var publicValuesPath =
            Path.Combine(queuePath, request.RequestId + ".gateway-public-values.bin");
        await File.WriteAllBytesAsync(proofPath, plan.Proof);
        await File.WriteAllBytesAsync(verificationKeyPath, plan.VerificationKey);
        await File.WriteAllBytesAsync(publicValuesPath, plan.PublicValues);

        var result = new GatewaySp1ProofResultManifest
        {
            SchemaVersion = GatewaySp1FileQueueProtocol.SchemaVersion,
            Status = GatewaySp1FileQueueProtocol.SucceededStatus,
            RequestId = request.RequestId,
            RequestHash = plan.RequestHash,
            BindingHash = plan.BindingHash,
            ProofSystem = plan.ProofSystem,
            AggregationBackendId = plan.AggregationBackendId,
            VerificationKey = Hex(plan.VerificationKey),
            RequestFile = request.RequestFile,
            ProofFile = plan.ProofFile ?? Path.GetFileName(proofPath),
            VerificationKeyFile = Path.GetFileName(verificationKeyPath),
            PublicValuesFile = Path.GetFileName(publicValuesPath),
            ProofSha256 = plan.ProofSha256 ?? Sha256(plan.Proof),
            VerificationKeySha256 = Sha256(plan.VerificationKey),
            PublicValuesSha256 = Sha256(plan.PublicValues),
        };
        var resultPath = Path.Combine(queuePath, request.RequestId + ".gateway-result.json");
        await File.WriteAllBytesAsync(resultPath, JsonSerializer.SerializeToUtf8Bytes(result));
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
                throw new InvalidOperationException($"expected one {pattern} file, found {files.Length}");
            await Task.Delay(10, timeout.Token);
        }
    }

    private static byte[] ProofBytes() =>
        Enumerable.Repeat((byte)0x5A, Sp1GatewayProofProver.Groth16ProofSize).ToArray();

    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private static string Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    private static string Sha256(ReadOnlySpan<byte> bytes) =>
        Hex(SHA256.HashData(bytes));

    private sealed record DaemonResponsePlan
    {
        public required byte[] Proof { get; init; }
        public required byte[] VerificationKey { get; init; }
        public required byte[] PublicValues { get; init; }
        public required string RequestHash { get; init; }
        public required string BindingHash { get; init; }
        public required byte ProofSystem { get; init; }
        public required byte AggregationBackendId { get; init; }
        public string? ProofSha256 { get; init; }
        public string? ProofFile { get; init; }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "neo-n4-gateway-prover-tests",
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
