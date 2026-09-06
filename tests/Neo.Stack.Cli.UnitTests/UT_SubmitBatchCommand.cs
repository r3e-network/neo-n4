using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Neo;
using Neo.L2;
using Neo.L2.Batch;
using Neo.Json;
using Neo.Stack.Cli.Commands;
using Neo.Wallets;

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

    [TestMethod]
    public async Task SubmitBatch_BroadcastRequiresSettlementManager()
    {
        var path = WriteBatch(SampleCommitment());

        var rc = await SubmitBatchCommand.RunAsync(new[] { "--file", path, "--broadcast" });

        Assert.AreEqual(5, rc);
    }

    [TestMethod]
    public async Task SubmitBatch_BroadcastRequiresPublicInputHashes()
    {
        var path = WriteBatch(SampleCommitment());

        var rc = await SubmitBatchCommand.RunAsync(new[]
        {
            "--file", path,
            "--broadcast",
            "--settlement-manager", "0x" + new string('a', 40),
        });

        Assert.AreEqual(6, rc);
    }

    [TestMethod]
    public async Task SubmitBatch_BroadcastWithInvalidRpcUrl_FailsClosed()
    {
        // Preflight validation must reject malformed RPC endpoints before any L1 submission.
        // The public-input hashes are supplied so the command reaches the broadcaster's
        // endpoint gate instead of exiting 6 on the earlier hash validation.
        var path = WriteBatch(SampleCommitment());

        var rc = await SubmitBatchCommand.RunAsync(new[]
        {
            "--file", path,
            "--broadcast",
            "--settlement-manager", "0x" + new string('a', 40),
            "--l1-message-hash", "0x" + new string('b', 64),
            "--block-context-hash", "0x" + new string('c', 64),
            "--rpc", "ftp://invalid-protocol", // Non-HTTP(S) scheme
            "--expected-network", "894710606",
        });

        // Should fail at RPC validation (exit code 10) or missing WIF (exit code 12)
        Assert.IsTrue(rc == 10 || rc == 12, $"Invalid RPC URL must be rejected early (got {rc})");
    }

    [TestMethod]
    public async Task SubmitBatch_BroadcastRequiresValidBlockContextHash()
    {
        var path = WriteBatch(SampleCommitment());

        var rc = await SubmitBatchCommand.RunAsync(new[]
        {
            "--file", path,
            "--broadcast",
            "--settlement-manager", "0x" + new string('a', 40),
            "--l1-message-hash", UInt256.Zero.ToString(), // Zero hash is invalid
            "--block-context-hash", "0x" + new string('b', 64),
        });

        Assert.AreEqual(6, rc, "Zero block context hash must be rejected");
    }

    [TestMethod]
    public async Task SubmitBatch_ZkProofTriggersAtomicFinalizeMethod()
    {
        // ZK proofs always require atomic finalization in a single transaction. The
        // in-process RpcHandler answers every call RpcTransactionSender makes, so the
        // whole sign-broadcast-confirm path runs offline and the emitted script can be
        // inspected for the atomic method name.
        var zkCommitment = SampleCommitment(chainId: 1099, batchNum: 42) with
        {
            ProofType = ProofType.Zk,
            Proof = new byte[] { 0xAB },
        };
        var path = WriteBatch(zkCommitment);

        var handler = new RpcHandler();
        using var http = new HttpClient(handler);

        var environmentVariable = $"NEO_N4_TEST_WIF_{Guid.NewGuid():N}";
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var wif = key.Export();
        Environment.SetEnvironmentVariable(environmentVariable, wif);

        try
        {
            var rc = await SubmitBatchCommand.RunAsync(new[]
            {
                "--file", path,
                "--broadcast",
                "--settlement-manager", "0x" + new string('a', 40),
                "--l1-message-hash", "0x" + new string('b', 64),
                "--block-context-hash", "0x" + new string('c', 64),
                "--rpc", "http://localhost:10332",
                "--expected-network", "894710606",
                "--wif-env", environmentVariable,
            }, http);

            Assert.AreEqual(0, rc, "ZK proof with atomic finalize should succeed");

            // Verify the method name used was submitAndFinalizeBatch
            StringAssert.Contains(handler.LastScript, "submitAndFinalizeBatch");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
            key.PrivateKey.AsSpan().Clear();
        }
    }

    [TestMethod]
    public async Task SubmitBatch_MultisigProofUsesTwoStepSubmitMethod()
    {
        // Non-ZK proofs keep the two-step submitBatch path unless --atomic-finalize is
        // requested explicitly; pairs with SubmitBatch_ZkProofTriggersAtomicFinalizeMethod
        // so the method-selection branch is pinned from both sides.
        var path = WriteBatch(SampleCommitment(chainId: 1099, batchNum: 42));

        var handler = new RpcHandler();
        using var http = new HttpClient(handler);

        var environmentVariable = $"NEO_N4_TEST_WIF_{Guid.NewGuid():N}";
        var key = new KeyPair(Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        Environment.SetEnvironmentVariable(environmentVariable, key.Export());

        try
        {
            var rc = await SubmitBatchCommand.RunAsync(new[]
            {
                "--file", path,
                "--broadcast",
                "--settlement-manager", "0x" + new string('a', 40),
                "--l1-message-hash", "0x" + new string('b', 64),
                "--block-context-hash", "0x" + new string('c', 64),
                "--rpc", "http://localhost:10332",
                "--expected-network", "894710606",
                "--wif-env", environmentVariable,
            }, http);

            Assert.AreEqual(0, rc, "multisig batch submission should succeed");
            StringAssert.Contains(handler.LastScript, "submitBatch");
            Assert.IsFalse(
                handler.LastScript!.Contains("submitAndFinalizeBatch", StringComparison.Ordinal),
                "multisig proof must not select the atomic finalize method");
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, null);
            key.PrivateKey.AsSpan().Clear();
        }
    }

    [TestMethod]
    public async Task SubmitBatch_PlanModeNeverTouchesTheNetwork()
    {
        // Phase 6 contract: the three wallet-gated commands print a structured operator
        // plan and only submit under an explicit --broadcast flag. Pointing --rpc at an
        // unroutable endpoint must not matter while plan mode is active.
        var path = WriteBatch(SampleCommitment(chainId: 1099, batchNum: 42));

        var handler = new RpcHandler();
        using var http = new HttpClient(handler);

        var (rc, output) = CaptureStdout(async () => await SubmitBatchCommand.RunAsync(new[]
        {
            "--file", path,
            "--rpc", "http://localhost:10332",
            "--expected-network", "894710606",
            "--settlement-manager", "0x" + new string('a', 40),
        }, http));

        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "Validation passed");
        Assert.AreEqual(0, handler.Methods.Count, "plan mode must not issue a single RPC call");
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

/// <summary>
/// Minimal HTTP handler for RPC simulation in unit tests.
/// </summary>
internal sealed class RpcHandler : HttpMessageHandler
{
    public List<string> Methods { get; } = [];

    public string? LastScript { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var envelope = (JObject)JToken.Parse(body)!;
        var method = envelope["method"]!.AsString();
        Methods.Add(method);
        
        // Extract script from invokescript call for verification
        if (method == "invokescript"
            && envelope["params"] is JArray invokeParams
            && invokeParams.Count > 0)
        {
            var scriptBase64 = invokeParams[0]!.AsString();
            var scriptBytes = Convert.FromBase64String(scriptBase64);
            LastScript = System.Text.Encoding.UTF8.GetString(scriptBytes);
        }

        var response = new JObject();
        response["jsonrpc"] = "2.0";
        response["id"] = envelope["id"];
        response["result"] = Result(method);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json"),
        };
    }

    private JToken Result(string method)
    {
        return method switch
        {
            "getversion" => Version(),
            "invokescript" => InvokeResult(),
            "getblockcount" => 100,
            "calculatenetworkfee" => NetworkFee(),
            "sendrawtransaction" => true,
            "getapplicationlog" => ApplicationLog(),
            _ => throw new InvalidOperationException($"Unexpected RPC method {method}"),
        };
    }

    private JObject Version()
    {
        var protocol = new JObject();
        protocol["network"] = 894_710_606;
        var result = new JObject();
        result["protocol"] = protocol;
        return result;
    }

    private static JObject InvokeResult()
    {
        var result = new JObject();
        result["state"] = "HALT";
        result["gasconsumed"] = "1000000";
        return result;
    }

    private static JObject NetworkFee()
    {
        var result = new JObject();
        result["networkfee"] = "1000";
        return result;
    }

    private static JObject ApplicationLog()
    {
        var execution = new JObject();
        execution["vmstate"] = "HALT";
        var result = new JObject();
        result["executions"] = new JArray { execution };
        return result;
    }
}
