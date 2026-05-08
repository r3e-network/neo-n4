using System;
using System.IO;
using System.Threading.Tasks;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Explore.Commands;

namespace Neo.L2.Explore.UnitTests;

[TestClass]
public class UT_TailCommand
{
    private const uint ChainId = 1099;
    private const string Endpoint = "http://x.example";

    [TestMethod]
    public async Task Tail_DefaultCount_PrintsLastFiveBatches()
    {
        var stub = new StubBackedClient(ChainId);
        // Seed batches 0..6 (7 total) — tail 5 should print 6, 5, 4, 3, 2.
        L2BatchCommitment? prev = null;
        for (ulong i = 0; i <= 6; i++)
        {
            var batch = BatchFactory.Continuous(ChainId, i, prev);
            stub.Store.AddBatch(batch, BatchStatus.Finalized);
            prev = batch;
        }

        var (rc, output) = await CaptureAsync(() => TailCommand.RunAsync(
            new[] { "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));

        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "last 5 batches");
        // Should descend 6 → 5 → 4 → 3 → 2.
        StringAssert.Contains(output, "#6");
        StringAssert.Contains(output, "#5");
        StringAssert.Contains(output, "#4");
        StringAssert.Contains(output, "#3");
        StringAssert.Contains(output, "#2");
        Assert.IsFalse(output.Contains("#1 "), "tail 5 should NOT print batch 1 — it's beyond the window");
    }

    [TestMethod]
    public async Task Tail_ExplicitCount_PrintsRequestedDepth()
    {
        var stub = new StubBackedClient(ChainId);
        L2BatchCommitment? prev = null;
        for (ulong i = 0; i <= 4; i++)
        {
            var batch = BatchFactory.Continuous(ChainId, i, prev);
            stub.Store.AddBatch(batch, BatchStatus.Finalized);
            prev = batch;
        }

        var (rc, output) = await CaptureAsync(() => TailCommand.RunAsync(
            new[] { "3", "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));

        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "last 3 batches");
        StringAssert.Contains(output, "#4");
        StringAssert.Contains(output, "#3");
        StringAssert.Contains(output, "#2");
    }

    [TestMethod]
    public async Task Tail_NoSealedBatches_ExitsTwo()
    {
        var stub = new StubBackedClient(ChainId);
        // No batches seeded.
        var (rc, _) = await CaptureBothAsync(() => TailCommand.RunAsync(
            new[] { "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));
        Assert.AreEqual(2, rc);
    }

    [TestMethod]
    public async Task Tail_OnlyGenesis_PrintsBatchZero()
    {
        var stub = new StubBackedClient(ChainId);
        stub.Store.AddBatch(BatchFactory.Continuous(ChainId, 0), BatchStatus.Finalized);

        var (rc, output) = await CaptureAsync(() => TailCommand.RunAsync(
            new[] { "5", "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));

        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "#0");
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
