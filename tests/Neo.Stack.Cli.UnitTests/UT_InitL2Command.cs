using System;
using System.IO;
using Neo.Stack.Cli.Commands;

namespace Neo.Stack.Cli.UnitTests;

/// <summary>
/// Tests for <see cref="InitL2Command"/> — creates the L2 node working subdirectories
/// (<c>data/</c>, <c>logs/</c>, <c>Plugins/</c>) inside an existing chain dir
/// (output of <c>create-chain</c>). Pins the missing-prerequisite diagnostic + the
/// flag-routing precedence + the exact set of subdirs created.
/// </summary>
[TestClass]
public class UT_InitL2Command
{
    private string _tempDir = null!;

    [TestInitialize]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "neo-n4-init-l2-test-" + Guid.NewGuid().ToString("N"));
    }

    [TestCleanup]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
    }

    private void SeedChainDirWithConfig()
    {
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "chain.config.json"), "{}");
    }

    [TestMethod]
    public void InitL2_HappyPath_CreatesAllThreeSubdirs()
    {
        SeedChainDirWithConfig();
        var rc = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data")), "data/ must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "batcher-data")), "batcher-data/ must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "logs")), "logs/ must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "Plugins")), "Plugins/ must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "node", "Plugins")), "node/Plugins must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "batcher-node", "Plugins")), "batcher-node/Plugins must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "prover", "inbox")), "prover inbox must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "prover", "archive")), "prover archive must be created");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data", "settlement", "proof-witness")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data", "settlement", "forced-inclusion-events")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data", "settlement", "shared-bridge-deposits")));
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data", "settlement", "message-router-events")));
    }

    [TestMethod]
    public void InitL2_BatcherConfig_CopiesIntoDedicatedDeploymentRoot()
    {
        SeedChainDirWithConfig();
        var source = Path.Combine(_tempDir, "reviewed-batcher-config.json");
        File.WriteAllText(source, "{\"ApplicationConfiguration\":{\"Storage\":{\"Path\":\"batcher-data\"}}}");

        var result = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--batcher-node-config", source,
        });

        Assert.AreEqual(0, result);
        Assert.AreEqual(
            File.ReadAllText(source),
            File.ReadAllText(Path.Combine(_tempDir, "batcher-node", "config.json")));
    }

    [TestMethod]
    public void InitL2_NodeConfig_CopiesReviewedConfigWithoutOverwriting()
    {
        SeedChainDirWithConfig();
        var source = Path.Combine(_tempDir, "reviewed-node-config.json");
        File.WriteAllText(source, "{\"ProtocolConfiguration\":{\"Network\":123}}");

        var first = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--node-config", source,
        });

        Assert.AreEqual(0, first);
        var destination = Path.Combine(_tempDir, "node", "config.json");
        Assert.AreEqual(File.ReadAllText(source), File.ReadAllText(destination));

        File.WriteAllText(source, "{\"ProtocolConfiguration\":{\"Network\":456}}");
        var second = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--node-config", source,
        });

        Assert.AreEqual(3, second);
        StringAssert.Contains(File.ReadAllText(destination), "123");
    }

    [TestMethod]
    public void InitL2_MissingChainConfig_ExitsTwo()
    {
        // Defense-in-depth: chain dir exists but chain.config.json doesn't —
        // operator forgot create-chain (or pointed --output at an unrelated dir).
        // Pin exit 2 (distinct from 1=chain dir missing) so a CI script can
        // disambiguate the two missing-prerequisite cases.
        Directory.CreateDirectory(_tempDir);  // chain dir exists, but no chain.config.json
        var (rc, _, stderr) = CaptureBoth(() => InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        }));
        Assert.AreEqual(2, rc);
        StringAssert.Contains(stderr, "Missing chain.config.json");
        StringAssert.Contains(stderr, "neo-stack create-chain");
        // None of the working subdirs should have been created.
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "data")),
            "missing-config rejection must abort before subdirs are created");
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "logs")));
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "Plugins")));
    }

    [TestMethod]
    public void InitL2_NonNumericChainId_ExitsOne()
    {
        var rc = InitL2Command.Run(new[]
        {
            "--chain-id", "abc",
            "--output", _tempDir,
        });
        Assert.AreEqual(1, rc);
    }

    [TestMethod]
    public void InitL2_ChainIdZero_Throws()
    {
        Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            InitL2Command.Run(new[]
            {
                "--chain-id", "0",
                "--output", _tempDir,
            }));
    }

    [TestMethod]
    public void InitL2_ChainDirNotFound_ExitsOne_PointsAtCreateChain()
    {
        // The "Chain dir not found" diagnostic must point operators at create-chain.
        // Without this, an operator who skipped step 1 sees a confusing diagnostic.
        var nonExistent = Path.Combine(_tempDir, "does-not-exist");
        var (rc, _, stderr) = CaptureBoth(() => InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", nonExistent,
        }));
        Assert.AreEqual(1, rc);
        StringAssert.Contains(stderr, "Chain dir not found");
        StringAssert.Contains(stderr, "neo-stack create-chain");
    }

    [TestMethod]
    public void InitL2_AcceptsPathFlag_BackwardsCompat()
    {
        // --path was the original flag; --output came later. Both must continue to
        // work (operator scripts predating --output depend on --path).
        SeedChainDirWithConfig();
        var rc = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--path", _tempDir,
        });
        Assert.AreEqual(0, rc);
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data")));
    }

    [TestMethod]
    public void InitL2_OutputTakesPrecedenceOverPath()
    {
        // --output wins (matches register-chain + start-* + deploy-bridge-adapter
        // precedence — different from create-chain's inverted convention).
        SeedChainDirWithConfig();
        var bogusPath = Path.Combine(_tempDir, "bogus");
        var rc = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
            "--path", bogusPath,
        });
        Assert.AreEqual(0, rc, "--output wins; bogus --path doesn't poison the run");
        Assert.IsTrue(Directory.Exists(Path.Combine(_tempDir, "data")));
        Assert.IsFalse(Directory.Exists(Path.Combine(bogusPath, "data")));
    }

    [TestMethod]
    public void InitL2_DaFlag_PrintedInSummary()
    {
        // --da is informational metadata that surfaces in the operator-facing summary.
        SeedChainDirWithConfig();
        var (rc, output) = CaptureStdout(() => InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--da", "L1",
            "--output", _tempDir,
        }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "da mode    = L1");
    }

    [TestMethod]
    public void InitL2_PrintsLocalHostCompositionOpenTip()
    {
        SeedChainDirWithConfig();
        var (rc, output) = CaptureStdout(() => InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        }));
        Assert.AreEqual(0, rc);
        StringAssert.Contains(output, "MultisigLocalHostComposition.Open");
        StringAssert.Contains(output, "OptimisticLocalHostComposition.Open");
        StringAssert.Contains(output, "ZkLocalHostComposition.Open");
        StringAssert.Contains(output, "IsOperatorReady");
        StringAssert.Contains(output, "GetOperatorStatusAsync");
        StringAssert.Contains(output, "NextExpectedBlock");
        StringAssert.Contains(output, "NextBatchNumber");
        StringAssert.Contains(output, "PendingSealedBatchNumber");
        StringAssert.Contains(output, "MaxBlocksPerBatch");
        StringAssert.Contains(output, "MetricsMaxConcurrentConnections");
        StringAssert.Contains(output, "HasBatchMessageRouter");
        StringAssert.Contains(output, "HasBatchDepositSource");
        StringAssert.Contains(output, "HasBatchProver");
        StringAssert.Contains(output, "OpenBatchForcedInclusionCount");
        StringAssert.Contains(output, "OpenBatchL2ToL2MessageCount");
        StringAssert.Contains(output, "SupportsLocalDaReader");
        StringAssert.Contains(output, "GetLatestDurableCheckpointAsync");
        StringAssert.Contains(output, "LatestCheckpointBatchNumber");
        StringAssert.Contains(output, "InitialStateRoot");
        StringAssert.Contains(output, "MaxForcedTransactionsPerBatch");
        StringAssert.Contains(output, "MessageOutboxL2ToL1Root");
        StringAssert.Contains(output, "KnownInboundNonceCount");
        StringAssert.Contains(output, "KnownForcedInclusionNonceCount");
        StringAssert.Contains(output, "L1FinalityDepth");
        StringAssert.Contains(output, "IsSettlementEnabled");
        StringAssert.Contains(output, "DepositSourceReadyCount");
        StringAssert.Contains(output, "IsMetricsEnabled");
        StringAssert.Contains(output, "MetricsConfiguredPort");
        StringAssert.Contains(output, "DepositSourceSoftConsumedCount");
        StringAssert.Contains(output, "ForcedInclusionDeploymentHeight");
        StringAssert.Contains(output, "GatewayHost.IsEnabled");
        StringAssert.Contains(output, "MaxAutomaticRetries");
        StringAssert.Contains(output, "StopMetricsHttp");
        StringAssert.Contains(output, "ConsumedDepositCount");
        StringAssert.Contains(output, "L1InboxPendingCount");
        StringAssert.Contains(output, "HasForcedInclusionFinalizer");
        StringAssert.Contains(output, "HasSettlementClient");
        StringAssert.Contains(output, "HasTransactionSender");
        StringAssert.Contains(output, "IsPublicationConfigured");
        StringAssert.Contains(output, "AddRpcBatch");
        StringAssert.Contains(output, "MessageOutbox");
        StringAssert.Contains(output, "PublishDaAsync");
        StringAssert.Contains(output, "RegisterForcedInclusionNonce");
        StringAssert.Contains(output, "ExportPrometheusMetrics");
        StringAssert.Contains(output, "RegisterBridgeAsset");
        StringAssert.Contains(output, "ProcessDeposit");
        StringAssert.Contains(output, "ProcessReadyDeposits");
        StringAssert.Contains(output, "ScanAndProcessReadyDepositsAsync");
        StringAssert.Contains(output, "HasOverdueForcedInclusionAsync");
        StringAssert.Contains(output, "StageWithdrawal");
        StringAssert.Contains(output, "ProveAsync");
        StringAssert.Contains(output, "WriteOperatorStatusAsync");
        StringAssert.Contains(output, "WritePrometheusMetricsAsync");
        StringAssert.Contains(output, "l1.wireproduction-notes.json");
    }

    [TestMethod]
    public void InitL2_RerunIsIdempotent()
    {
        // Operator who already ran init-l2 + populated data/ shouldn't lose work
        // on a second run. Directory.CreateDirectory is idempotent — pin the
        // semantics so a refactor doesn't switch to a recursive-delete-then-create
        // pattern.
        SeedChainDirWithConfig();
        var rc1 = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc1);

        // Drop a sentinel file in data/.
        var sentinel = Path.Combine(_tempDir, "data", "preexisting.db");
        File.WriteAllText(sentinel, "operator data");

        // Re-run init-l2.
        var rc2 = InitL2Command.Run(new[]
        {
            "--chain-id", "1099",
            "--output", _tempDir,
        });
        Assert.AreEqual(0, rc2, "second init-l2 run must succeed (idempotent)");

        // Sentinel survived.
        Assert.IsTrue(File.Exists(sentinel),
            "operator's data/ contents must NOT be wiped by a second init-l2 run");
        Assert.AreEqual("operator data", File.ReadAllText(sentinel));
    }

    [TestMethod]
    public void InitL2_FromDeployReport_InstallsPluginConfigsUnderNodeRoots()
    {
        SeedChainDirWithConfig();
        var reportPath = Path.Combine(_tempDir, "deploy-report.json");
        File.WriteAllText(reportPath, """
            {
              "rpc": "https://n3seed1.ngd.network:20332/",
              "network": 894710606,
              "ownerAddress": "NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu",
              "ownerScriptHash": "0x13ef519c362973f9a34648a9eac5b71250b2a80a",
              "l2ChainId": 1099,
              "records": [
                {"category":"deploy","name":"ChainRegistry","status":"deployed","contractHash":"0x65201c5415f6fc13093ad7169c556351c2392d23"},
                {"category":"deploy","name":"VerifierRegistry","status":"deployed","contractHash":"0xe058ea01d6933f38bad1321d0407f514112016b1"},
                {"category":"deploy","name":"SharedBridge","status":"deployed","contractHash":"0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241"},
                {"category":"deploy","name":"MessageRouter","status":"deployed","contractHash":"0x3caf3c6e160b5aec2e07672dc37662b5998afe90"},
                {"category":"deploy","name":"SettlementManager","status":"deployed","contractHash":"0x11448868f1c14422506b9c2360051df34bcbbb51"},
                {"category":"deploy","name":"ForcedInclusion","status":"deployed","contractHash":"0x962829ae28e7f89e5de4b4672b167c8ae2ba55a9"}
              ]
            }
            """);

        var rc = InitL2Command.Run(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--from-deploy-report", reportPath,
        ]);
        Assert.AreEqual(0, rc);
        Assert.IsTrue(File.Exists(Path.Combine(_tempDir, "l1.deployed.json")));
        Assert.IsTrue(File.Exists(Path.Combine(
            _tempDir, "Plugins", "Neo.Plugins.L2Settlement", "config.json")));
        Assert.IsTrue(File.Exists(Path.Combine(
            _tempDir, "node", "Plugins", "Neo.Plugins.L2Settlement", "config.json")));
        Assert.IsTrue(File.Exists(Path.Combine(
            _tempDir, "batcher-node", "Plugins", "Neo.Plugins.L2Batch", "config.json")));
        StringAssert.Contains(
            File.ReadAllText(Path.Combine(
                _tempDir, "node", "Plugins", "Neo.Plugins.L2Settlement", "config.json")),
            "0x11448868f1c14422506b9c2360051df34bcbbb51");
    }

    [TestMethod]
    public void InitL2_FromDeployReport_ChainIdMismatch_FailsClosed()
    {
        SeedChainDirWithConfig();
        var reportPath = Path.Combine(_tempDir, "deploy-report.json");
        File.WriteAllText(reportPath, """
            {
              "rpc": "https://example/",
              "network": 1,
              "ownerAddress": "NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu",
              "ownerScriptHash": "0x13ef519c362973f9a34648a9eac5b71250b2a80a",
              "l2ChainId": 20260716,
              "records": [
                {"category":"deploy","name":"ChainRegistry","status":"deployed","contractHash":"0x65201c5415f6fc13093ad7169c556351c2392d23"},
                {"category":"deploy","name":"VerifierRegistry","status":"deployed","contractHash":"0xe058ea01d6933f38bad1321d0407f514112016b1"},
                {"category":"deploy","name":"SharedBridge","status":"deployed","contractHash":"0xf2f5114b83dd6fed4ddcac0ff9966fd22a77b241"},
                {"category":"deploy","name":"MessageRouter","status":"deployed","contractHash":"0x3caf3c6e160b5aec2e07672dc37662b5998afe90"},
                {"category":"deploy","name":"SettlementManager","status":"deployed","contractHash":"0x11448868f1c14422506b9c2360051df34bcbbb51"},
                {"category":"deploy","name":"ForcedInclusion","status":"deployed","contractHash":"0x962829ae28e7f89e5de4b4672b167c8ae2ba55a9"}
              ]
            }
            """);

        var rc = InitL2Command.Run(
        [
            "--chain-id", "1099",
            "--output", _tempDir,
            "--from-deploy-report", reportPath,
        ]);
        Assert.AreEqual(4, rc);
    }

    // ---- Helpers ----

    private static (int rc, string stdout) CaptureStdout(Func<int> run)
    {
        var origOut = Console.Out;
        try
        {
            var sw = new StringWriter();
            Console.SetOut(sw);
            var rc = run();
            return (rc, sw.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
        }
    }

    private static (int rc, string stdout, string stderr) CaptureBoth(Func<int> run)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        try
        {
            var swOut = new StringWriter();
            var swErr = new StringWriter();
            Console.SetOut(swOut);
            Console.SetError(swErr);
            var rc = run();
            return (rc, swOut.ToString(), swErr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }
}
