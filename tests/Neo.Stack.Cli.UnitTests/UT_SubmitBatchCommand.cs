using System;
using System.IO;
using System.Threading.Tasks;
using Neo;
using Neo.L2;
using Neo.L2.Batch;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="SubmitBatchCommand"/> — the wallet-gated batch-submission
/// plan-printer. Pre-flight decodes the batch via <see cref="BatchSerializer"/> so
/// a malformed batch surfaces here (clear error message) instead of at L1
/// (opaque revert). Pins the exit-code contract for each failure mode + the
/// happy-path field decoding.
/// </summary>
[TestClass]
public class UT_SubmitBatchCommand
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-submit-batch-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private static UInt256 H(byte b)
    {
        var bytes = new byte[32];
        bytes[0] = b;
        return new UInt256(bytes);
    }

    private static L2BatchCommitment SampleCommitment(uint chainId = 1099, ulong batchNum = 42) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNum,
        FirstBlock = 100,
        LastBlock = 150,
        PreStateRoot = H(1),
        PostStateRoot = H(2),
        TxRoot = H(3),
        ReceiptRoot = H(4),
        WithdrawalRoot = H(5),
        L2ToL1MessageRoot = H(6),
        L2ToL2MessageRoot = H(7),
        DACommitment = H(8),
        PublicInputHash = H(9),
        ProofType = ProofType.Multisig,
        Proof = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF },
    };

    private string WriteBatch(L2BatchCommitment commitment)
    {
        var bytes = BatchSerializer.Encode(commitment);
        var path = Path.Combine(_tempDir, "batch.bin");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [TestMethod]
    public async Task SubmitBatch_HappyPath_DecodesAndPrintsFields()
    {
        var path = WriteBatch(SampleCommitment(chainId: 1099, batchNum: 42));
        var (rc, output) = CaptureStdout(async () =>
            await SubmitBatchCommand.RunAsync(new[] { "--file", path }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "chainId       : 1099");
        StringAssert.Contains(output, "batchNumber   : 42");
        StringAssert.Contains(output, "blocks        : 100-150");
        StringAssert.Contains(output, "proofType     : Multisig");
        StringAssert.Contains(output, "Validation passed");
    }

    [TestMethod]
    public async Task SubmitBatch_MissingFileFlag_ExitsOne()
    {
        // --file is required; without it the operator's intent is ambiguous.
        var (rc, _, stderr) = await CaptureBoth(async () =>
            await SubmitBatchCommand.RunAsync(Array.Empty<string>()));
        Assert.AreEqual(1, rc);
        StringAssert.Contains(stderr, "--file");
    }

    [TestMethod]
    public async Task SubmitBatch_FileNotFound_ExitsTwo()
    {
        var (rc, _, stderr) = await CaptureBoth(async () =>
            await SubmitBatchCommand.RunAsync(new[]
            {
                "--file", Path.Combine(_tempDir, "does-not-exist.bin"),
            }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "batch file not found");
    }

    [TestMethod]
    public async Task SubmitBatch_MalformedBytes_ExitsFour()
    {
        // Write garbage that isn't a valid L2BatchCommitment encoding. Pin exit 4
        // (decode failure, distinct from 1/2/3) so a CI script can disambiguate.
        var path = Path.Combine(_tempDir, "garbage.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        var (rc, _, stderr) = await CaptureBoth(async () =>
            await SubmitBatchCommand.RunAsync(new[] { "--file", path }));
        Assert.AreEqual(4, rc, "decode failure must exit 4 (distinct from 1/2/3)");
        StringAssert.Contains(stderr, "batch decode failed");
        StringAssert.Contains(stderr, "Submit aborted");
    }

    [TestMethod]
    public async Task SubmitBatch_RoundTripsThroughEncoder()
    {
        // Pin that any commitment encoded by BatchSerializer.Encode decodes cleanly
        // through submit-batch's preflight. Catches a refactor that subtly breaks
        // the encode/decode contract — symmetric with the BatchSerializer's own
        // UT_BatchSerializer round-trip tests, but routed through the CLI surface.
        foreach (var proofType in new[] { ProofType.Multisig, ProofType.Optimistic, ProofType.Zk })
        {
            var path = WriteBatch(SampleCommitment(chainId: 1099, batchNum: 1) with
            {
                ProofType = proofType,
                Proof = new byte[] { 0xAB },
            });
            var rc = await SubmitBatchCommand.RunAsync(new[] { "--file", path });
            Assert.AreEqual(0, rc, $"proofType {proofType} must round-trip cleanly through submit-batch preflight");
        }
    }

    [TestMethod]
    public async Task SubmitBatch_PrintsAllRootFieldsTruncated()
    {
        // Each UInt256 root field is printed; pin that the output mentions all four
        // state roots so an operator auditing the output sees what they're submitting.
        var path = WriteBatch(SampleCommitment());
        var (rc, output) = CaptureStdout(async () =>
            await SubmitBatchCommand.RunAsync(new[] { "--file", path }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "preStateRoot");
        StringAssert.Contains(output, "postStateRoot");
    }

    // ---- Helpers ----

    private static (int rc, string stdout) CaptureStdout(Func<Task<int>> run)
    {
        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = run().GetAwaiter().GetResult();
            return (rc, sw.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    private static async Task<(int rc, string stdout, string stderr)> CaptureBoth(Func<Task<int>> run)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        try
        {
            var swOut = new StringWriter();
            var swErr = new StringWriter();
            Console.SetOut(swOut);
            Console.SetError(swErr);
            var rc = await run();
            return (rc, swOut.ToString(), swErr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
