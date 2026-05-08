using System;
using System.IO;
using System.Threading.Tasks;
using Neo;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Explore.Commands;

namespace Neo.L2.Explore.UnitTests;

[TestClass]
public class UT_AuditCommand
{
    private const uint ChainId = 1099;
    private const string Endpoint = "http://x.example";

    [TestMethod]
    public async Task Audit_ContinuousChain_ExitsZero_AndReportsCheckedPairs()
    {
        var stub = new StubBackedClient(ChainId);
        L2BatchCommitment? prev = null;
        for (ulong i = 0; i <= 5; i++)
        {
            var batch = BatchFactory.Continuous(ChainId, i, prev);
            stub.Store.AddBatch(batch, BatchStatus.Finalized);
            prev = batch;
        }

        var (rc, output) = await CaptureAsync(() => AuditCommand.RunAsync(
            new[] { "5", "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));

        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "✅");
        StringAssert.Contains(output, "consecutive pairs continuous");
    }

    [TestMethod]
    public async Task Audit_DiscontinuousPreStateRoot_ExitsFour_NamesOffendingBatch()
    {
        // Build a chain where batch #3 has a preStateRoot that does NOT match
        // batch #2's postStateRoot — exactly the kind of chain a malicious /
        // misbehaving sequencer would emit before settlement caught it.
        var stub = new StubBackedClient(ChainId);
        L2BatchCommitment? prev = null;
        for (ulong i = 0; i <= 4; i++)
        {
            L2BatchCommitment batch;
            if (i == 3)
            {
                // Inject discontinuity: pretend batch #3 thinks state was never #2's post.
                var bogus = UInt256.Parse("0x" + new string('f', 64));
                batch = BatchFactory.WithExplicitPre(ChainId, i, bogus);
            }
            else
            {
                batch = BatchFactory.Continuous(ChainId, i, prev);
            }
            stub.Store.AddBatch(batch, BatchStatus.Finalized);
            prev = batch;
        }

        var (rc, stderr) = await CaptureBothAsync(() => AuditCommand.RunAsync(
            new[] { "5", "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));

        Assert.AreEqual(4, rc, "discontinuity exit code is 4");
        StringAssert.Contains(stderr, "state-root continuity violation");
        StringAssert.Contains(stderr, "batch #3");
    }

    [TestMethod]
    public async Task Audit_CountUnderTwo_ExitsOne()
    {
        var stub = new StubBackedClient(ChainId);
        var (rc, _) = await CaptureBothAsync(() => AuditCommand.RunAsync(
            new[] { "1", "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));
        Assert.AreEqual(1, rc);
    }

    [TestMethod]
    public async Task Audit_DefaultCount_IsTen()
    {
        // Pin the default — a future change to the default value would be a
        // user-facing semantics change, so call it out.
        var stub = new StubBackedClient(ChainId);
        L2BatchCommitment? prev = null;
        for (ulong i = 0; i <= 11; i++)
        {
            var batch = BatchFactory.Continuous(ChainId, i, prev);
            stub.Store.AddBatch(batch, BatchStatus.Finalized);
            prev = batch;
        }

        var (rc, output) = await CaptureAsync(() => AuditCommand.RunAsync(
            new[] { "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));

        Assert.AreEqual(0, rc);
        // The default is 10 — head=11, so audit window 2..11 (10 batches, 9 pairs).
        StringAssert.Contains(output, "batches 2..11");
        StringAssert.Contains(output, "9 consecutive pairs continuous");
    }

    private static async Task<(int rc, string stdout)> CaptureAsync(Func<Task<int>> run)
    {
        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = await run();
            return (rc, sw.ToString());
        }
        finally { Console.SetOut(origOut); }
    }

    private static async Task<(int rc, string stderr)> CaptureBothAsync(Func<Task<int>> run)
    {
        var origErr = Console.Error;
        try
        {
            var sw = new StringWriter();
            Console.SetError(sw);
            var rc = await run();
            return (rc, sw.ToString());
        }
        finally { Console.SetError(origErr); }
    }
}
