using System;
using System.IO;
using System.Threading.Tasks;
using Neo.L2;
using Neo.L2.Batch;
using Neo.L2.Explore.Commands;

namespace Neo.L2.Explore.UnitTests;

[TestClass]
public class UT_BatchCommand
{
    private const uint ChainId = 1099;
    private const string Endpoint = "http://x.example";

    [TestMethod]
    public async Task Batch_PrintsFullCommitment_AndStatus()
    {
        var stub = new StubBackedClient(ChainId);
        var batch = BatchFactory.Continuous(ChainId, batchNumber: 7);
        stub.Store.AddBatch(batch, BatchStatus.Finalized);

        var (rc, output) = await CaptureAsync(() => BatchCommand.RunAsync(
            new[] { "7", "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));

        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "batch #7");
        StringAssert.Contains(output, $"blocks           = {batch.FirstBlock}..{batch.LastBlock}");
        StringAssert.Contains(output, $"preStateRoot     = {batch.PreStateRoot}");
        StringAssert.Contains(output, $"postStateRoot    = {batch.PostStateRoot}");
        StringAssert.Contains(output, "proofType        = Multisig");
        StringAssert.Contains(output, "status           = Finalized");
    }

    [TestMethod]
    public async Task Batch_MissingNumber_ExitsOne()
    {
        var stub = new StubBackedClient(ChainId);
        var (rc, _) = await CaptureBothAsync(() => BatchCommand.RunAsync(
            new[] { "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));
        Assert.AreEqual(1, rc);
    }

    [TestMethod]
    public async Task Batch_NotFound_ExitsTwo()
    {
        var stub = new StubBackedClient(ChainId);
        var (rc, _) = await CaptureBothAsync(() => BatchCommand.RunAsync(
            new[] { "999", "--endpoint", Endpoint, "--chain-id", ChainId.ToString() },
            stub.Factory));
        Assert.AreEqual(2, rc);
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
