using System;
using System.IO;
using System.Threading.Tasks;
using Neo.L2;
using Neo.L2.Explore.Commands;
using Neo.L2.Sdk;

namespace Neo.L2.Explore.UnitTests;

[TestClass]
public class UT_LabelCommand
{
    [TestMethod]
    public async Task Label_PrintsAllFiveDimensions()
    {
        var stub = new StubBackedClient(chainId: 1099, level: SecurityLevel.Validity);
        stub.Store.GetType(); // (init-only properties already configured by ctor)

        var (rc, output) = await CaptureAsync(() => LabelCommand.RunAsync(
            new[] { "--endpoint", "http://x.example", "--chain-id", "1099" },
            stub.Factory));

        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "chain 1099");
        StringAssert.Contains(output, "securityLevel  = Validity");
        StringAssert.Contains(output, "daMode         = L1");
        StringAssert.Contains(output, "gatewayEnabled = False");
        StringAssert.Contains(output, "sequencer      = DbftCommittee");
        StringAssert.Contains(output, "exit           = Delayed");
    }

    [TestMethod]
    public async Task Label_MissingEndpoint_ExitsOne()
    {
        var stub = new StubBackedClient(chainId: 1099);
        var (rc, _) = await CaptureBothAsync(() => LabelCommand.RunAsync(
            new[] { "--chain-id", "1099" },
            stub.Factory));
        Assert.AreEqual(1, rc);
    }

    [TestMethod]
    public async Task Label_MissingChainId_ExitsOne()
    {
        var stub = new StubBackedClient(chainId: 1099);
        var (rc, _) = await CaptureBothAsync(() => LabelCommand.RunAsync(
            new[] { "--endpoint", "http://x.example" },
            stub.Factory));
        Assert.AreEqual(1, rc);
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
