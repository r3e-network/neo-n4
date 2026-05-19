using Neo.L2.Audit;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.State;
using Neo.L2.Telemetry;
using Neo.Plugins.L2;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// End-to-end integration test for the full audit pipeline. Devnet wires 6 audit checks
/// (continuity, no-zero-proof, proof-validity, batch-range, public-input-hash,
/// DA-availability); per-check unit tests pin each in isolation, but a refactor that
/// breaks the auditor's metric emission OR drops a check from the pipeline could slip
/// past those.
///
/// This test:
///   1. Constructs a healthy 3-batch chain with real proofs and DA receipts
///   2. Runs the full audit pipeline (all 6 checks) via ChainAuditor
///   3. Asserts the report passes AND the auditor emitted l2.audit.runs (1) and
///      l2.audit.failures (0) — the contract relied on by ops dashboards
/// </summary>
[TestClass]
public class UT_E2E_AuditPipeline
{
    [TestMethod]
    public async Task FullAuditPipeline_HealthyChain_AllChecksPass_MetricsEmitted()
    {
        const uint chainId = 1001;
        var metrics = new InMemoryMetrics();
        var verifierRegistry = new VerifierRegistry();
        var (signers, verifier) = BuildAttestationSet();
        verifierRegistry.Register(verifier);

        // 3-batch chain with continuity. Each batch has a real DA receipt.
        var daWriter = L2DAPlugin.BuildDefaultWriter(DAMode.External, dataDir: null);
        var prover = new AttestationProver(signers);
        var preStateRoot = UInt256.Zero;
        var commitments = new List<L2BatchCommitment>();
        var publicInputsByBatch = new Dictionary<ulong, PublicInputs>();

        for (var i = 1UL; i <= 3UL; i++)
        {
            var post = HashWithSeed(i);
            var receipt = await daWriter.PublishAsync(new DAPublishRequest
            {
                ChainId = chainId,
                BatchNumber = i,
                Payload = new byte[] { (byte)i, 0xAA, 0xBB, 0xCC },
            });
            var inputs = new PublicInputs
            {
                ChainId = chainId,
                BatchNumber = i,
                PreStateRoot = preStateRoot,
                PostStateRoot = post,
                TxRoot = HashWithSeed(i + 100),
                ReceiptRoot = HashWithSeed(i + 200),
                WithdrawalRoot = HashWithSeed(i + 300),
                L2ToL1MessageRoot = HashWithSeed(i + 400),
                L2ToL2MessageRoot = HashWithSeed(i + 500),
                L1MessageHash = UInt256.Zero,
                DACommitment = receipt.Commitment,
                BlockContextHash = UInt256.Zero,
            };
            var proof = await prover.ProveAsync(new ProofRequest
            {
                PublicInputs = inputs,
                Witness = ReadOnlyMemory<byte>.Empty,
                Kind = ProofType.Multisig,
            });
            var commitment = new L2BatchCommitment
            {
                ChainId = chainId,
                BatchNumber = i,
                FirstBlock = (i - 1) * 10,
                LastBlock = (i - 1) * 10 + 9,
                PreStateRoot = preStateRoot,
                PostStateRoot = post,
                TxRoot = inputs.TxRoot,
                ReceiptRoot = inputs.ReceiptRoot,
                WithdrawalRoot = inputs.WithdrawalRoot,
                L2ToL1MessageRoot = inputs.L2ToL1MessageRoot,
                L2ToL2MessageRoot = inputs.L2ToL2MessageRoot,
                DACommitment = receipt.Commitment,
                PublicInputHash = proof.PublicInputHash,
                ProofType = ProofType.Multisig,
                Proof = proof.Proof,
            };
            commitments.Add(commitment);
            publicInputsByBatch[i] = inputs;
            preStateRoot = post;
        }

        // Wire the full pipeline (6 checks) — same shape as the devnet runner.
        var auditor = new ChainAuditor(metrics)
            .Register(new ContinuityCheck())
            .Register(new NoZeroProofCheck())
            .Register(new BatchRangeCheck())
            .Register(new ProofValidityCheck(verifierRegistry, c => publicInputsByBatch[c.BatchNumber]))
            .Register(new PublicInputHashConsistencyCheck())
            .Register(new DAAvailabilityCheck(daWriter));

        var report = await auditor.AuditAsync(commitments);

        Assert.IsTrue(report.Passed, $"audit failed: {report.Summarize()}");
        // All findings must be passing — exact count varies because some checks emit
        // intrinsic ChainAuditor findings beyond the per-check summaries (e.g. the
        // ChainAuditor itself emits a top-level summary). Just assert all pass.
        Assert.IsTrue(report.Findings.All(f => f.Passed),
            $"some findings failed: {string.Join("; ", report.Findings.Where(f => !f.Passed).Select(f => f.Detail))}");
        Assert.IsTrue(report.Findings.Count >= 6, $"expected at least one finding per check, got {report.Findings.Count}");
        // All 6 registered checks must contribute distinct findings. The exact check
        // name strings come from each IAuditCheck.Name property; pinning ≥6 distinct
        // names guards against a refactor that accidentally drops a check from the
        // pipeline.
        var checkNames = report.Findings.Select(f => f.Check).Distinct().ToList();
        Assert.IsTrue(checkNames.Count >= 6,
            $"expected ≥6 distinct check names from 6 registered checks, got: {string.Join(",", checkNames)}");

        // Metric contract: a successful audit increments l2.audit.runs (1) and
        // l2.audit.failures (0). Operators dashboard against these.
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.AuditsRun));
        Assert.AreEqual(0, metrics.GetCounter(MetricNames.AuditFailures));
    }

    [TestMethod]
    public async Task FullAuditPipeline_DaDropped_DAAvailabilityCatchesIt()
    {
        // Specifically pin the DA-dropped failure mode end-to-end: publish a payload
        // to a DA writer, then audit against a SECOND writer that never saw the
        // commitment. The check should flag the missing payload by name + commitment.
        const uint chainId = 1001;
        var publisher = L2DAPlugin.BuildDefaultWriter(DAMode.External, dataDir: null);
        var auditWriter = L2DAPlugin.BuildDefaultWriter(DAMode.External, dataDir: null);

        var receipt = await publisher.PublishAsync(new DAPublishRequest
        {
            ChainId = chainId,
            BatchNumber = 1,
            Payload = new byte[] { 0x42 },
        });

        var batch = new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = 1,
            FirstBlock = 0,
            LastBlock = 9,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = HashWithSeed(1),
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = receipt.Commitment, // the OTHER writer's payload
            PublicInputHash = UInt256.Zero,
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0xAA },
        };

        var auditor = new ChainAuditor()
            .Register(new DAAvailabilityCheck(auditWriter));
        var report = await auditor.AuditAsync(new[] { batch });

        Assert.IsFalse(report.Passed);
        var failed = report.Findings.Single(f => !f.Passed);
        Assert.AreEqual("da_availability", failed.Check);
        StringAssert.Contains(failed.Detail, "no longer available");
    }

    [TestMethod]
    public async Task FullAuditPipeline_BrokenBatchRange_ReportsFail_MetricsCount()
    {
        // The complementary failure-detection test: pin that the pipeline catches a
        // bad batch AND emits l2.audit.failures > 0. Without this, a refactor that
        // accidentally short-circuits the audit on first finding (or never increments
        // the failure counter) would slip past the happy-path test only.
        const uint chainId = 1001;
        var metrics = new InMemoryMetrics();

        // One batch with inverted block range — deliberately bad input.
        var bad = new L2BatchCommitment
        {
            ChainId = chainId,
            BatchNumber = 1,
            FirstBlock = 100,
            LastBlock = 50, // inverted!
            PreStateRoot = UInt256.Zero,
            PostStateRoot = HashWithSeed(1),
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            DACommitment = UInt256.Zero, // skipped by DAAvailabilityCheck
            PublicInputHash = HashWithSeed(99),
            ProofType = ProofType.Multisig,
            Proof = new byte[] { 0xAA },
        };

        var auditor = new ChainAuditor(metrics)
            .Register(new BatchRangeCheck());

        var report = await auditor.AuditAsync(new[] { bad });

        Assert.IsFalse(report.Passed, "broken batch range should fail audit");
        Assert.IsTrue(report.Findings.Any(f => !f.Passed && f.Detail.Contains("inverted")));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.AuditsRun));
        Assert.AreEqual(1, metrics.GetCounter(MetricNames.AuditFailures),
            "one failed finding → AuditFailures += 1");
    }

    private static (ISignerSet signers, AttestationVerifier verifier) BuildAttestationSet()
    {
        // 3-of-4 multisig, deterministic keys via simple seeds.
        var keys = Enumerable.Range(1, 4).Select(i => GenKey((byte)i)).ToList();
        var signers = new InMemorySignerSet(keys);
        var verifier = new AttestationVerifier(keys.Select(k => k.PubKey), threshold: 3);
        return (signers, verifier);
    }

    private static (Neo.Cryptography.ECC.ECPoint PubKey, byte[] PrivateKey) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        var pub = Neo.Cryptography.ECC.ECCurve.Secp256r1.G * priv;
        return (pub, priv);
    }

    private static UInt256 HashWithSeed(ulong seed)
    {
        var bytes = new byte[32];
        for (var i = 0; i < 8; i++) bytes[i] = (byte)((seed >> (i * 8)) & 0xFF);
        return new UInt256(bytes);
    }
}
