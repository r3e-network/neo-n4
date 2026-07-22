using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;
using Neo.Network.P2P.Payloads;
using Neo.Plugins.L2;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc.UnitTests;

/// <summary>Host composition tests for <see cref="GatewayHostComposition"/>.</summary>
[TestClass]
public sealed class UT_GatewayHostComposition
{
    private static readonly UInt160 MessageRouter =
        UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 SettlementManager =
        UInt160.Parse("0x" + new string('b', 40));

    [TestMethod]
    public void OpenMerkle_ConfiguresPublicationFromDeployedLayout()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-merkle-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var prover = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MerklePathRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(
                    new byte[] { 0x01 }));
            var metrics = new InMemoryMetrics();
            using var host = GatewayHostComposition.OpenMerkle(
                dir,
                prover,
                new StubSigner(),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)),
                metrics: metrics);

            Assert.AreEqual(Path.GetFullPath(dir), host.ChainDirectory);
            Assert.IsFalse(host.OwnsProofProver);
            Assert.AreSame(prover, host.ProofProver);
            Assert.IsInstanceOfType(host.Gateway.Aggregator, typeof(BinaryTreeAggregator));
            Assert.AreSame(host.Gateway.Aggregator, host.Aggregator);
            Assert.AreEqual(
                MerklePathRoundProver.ConstBackendId,
                ((BinaryTreeAggregator)host.Aggregator).RoundProver.BackendId);
            Assert.IsFalse(host.Gateway.HasPendingPublication);
            // Host-level ops surface (no dig into Gateway plugin).
            Assert.IsFalse(host.HasPendingPublication);
            Assert.IsNull(host.PendingPublicationEpoch);
            Assert.AreEqual(0, host.AggregatorPendingCount);
            Assert.AreEqual(host.Aggregator.PendingCount, host.AggregatorPendingCount);
            Assert.IsTrue(host.HasDurableOutbox);
            Assert.IsTrue(host.IsPublicationConfigured);
            Assert.IsTrue(host.IsEnabled);
            Assert.IsTrue(host.MaxAutomaticRetries >= 1);
            Assert.IsNotNull(host.OutboxStatus);
            var gwStatus = host.GetOperatorStatus();
            Assert.IsFalse(gwStatus.HasPendingPublication);
            Assert.IsNull(gwStatus.PendingPublicationEpoch);
            Assert.AreEqual(0, gwStatus.AggregatorPendingCount);
            Assert.IsTrue(gwStatus.HasDurableOutbox);
            Assert.IsTrue(gwStatus.IsPublicationConfigured);
            Assert.IsTrue(gwStatus.IsEnabled);
            Assert.AreEqual(host.MaxAutomaticRetries, gwStatus.MaxAutomaticRetries);
            Assert.AreEqual(0, gwStatus.OutboxQueueDepth);
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, gwStatus.AggregationBackendId);
            Assert.AreEqual(1, gwStatus.ProofSystem);
            Assert.AreEqual(1, host.ProofSystem);
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, host.AggregationBackendId);
            Assert.AreEqual(host.AggregationBackendId, gwStatus.AggregationBackendId);
            Assert.IsTrue(host.HasL1RpcEndpoint);
            Assert.IsTrue(gwStatus.HasL1RpcEndpoint);
            Assert.IsTrue(host.IsPublicationProfileReady);
            Assert.IsTrue(gwStatus.IsPublicationProfileReady);
            Assert.IsTrue(host.HasExpectedNetwork);
            Assert.IsTrue(gwStatus.HasExpectedNetwork);
            Assert.IsTrue(host.IsOfflinePassportComplete);
            Assert.IsTrue(gwStatus.IsOfflinePassportComplete);
            Assert.AreEqual(0, host.OfflinePassportFailures.Count);
            Assert.AreEqual(0, gwStatus.OfflinePassportFailures.Count);
            Assert.IsFalse(host.IsOutboxPoisoned);
            Assert.IsTrue(host.IsOutboxIdle);
            Assert.IsTrue(host.IsPublicationHealthy);
            Assert.AreEqual(0, host.PublicationHealthFailures.Count);
            Assert.IsTrue(host.IsGatewayHostHealthy);
            Assert.AreEqual(0, host.GatewayHostHealthFailures.Count);
            Assert.IsFalse(gwStatus.IsOutboxPoisoned);
            Assert.IsTrue(gwStatus.IsOutboxIdle);
            Assert.IsTrue(gwStatus.IsPublicationHealthy);
            Assert.AreEqual(0, gwStatus.PublicationHealthFailures.Count);
            Assert.IsTrue(gwStatus.IsGatewayHostHealthy);
            Assert.AreEqual(0, gwStatus.GatewayHostHealthFailures.Count);
            Assert.AreEqual(894710606u, host.ExpectedNetwork);
            Assert.AreEqual(894710606u, gwStatus.ExpectedNetwork);
            Assert.AreEqual(UInt256.Parse("0x" + new string('d', 64)), host.ReplayDomain);
            Assert.AreEqual(UInt256.Parse("0x" + new string('e', 64)), host.VerificationKeyId);
            Assert.AreEqual(host.ReplayDomain, gwStatus.ReplayDomain);
            Assert.AreEqual(host.VerificationKeyId, gwStatus.VerificationKeyId);
            Assert.AreEqual(SettlementManager, host.SettlementManagerHash);
            Assert.AreEqual(MessageRouter, host.MessageRouterHash);
            Assert.AreEqual(host.SettlementManagerHash, gwStatus.SettlementManagerHash);
            Assert.AreEqual(host.MessageRouterHash, gwStatus.MessageRouterHash);
            Assert.AreEqual(host.Publisher.SettlementManagerHash, host.SettlementManagerHash);
            Assert.IsFalse(gwStatus.OwnsProofProver);
            Assert.IsTrue(gwStatus.HasMetrics);
            Assert.IsTrue(gwStatus.MetricsEntryCount >= 0);
            Assert.IsFalse(host.HasMetricsPlugin);
            Assert.IsFalse(gwStatus.HasMetricsPlugin);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            Assert.IsTrue(gwStatus.IsMetricsHttpHealthy);
            Assert.AreEqual(0, host.MetricsHttpHealthFailures.Count);
            Assert.AreEqual(0, gwStatus.MetricsConfiguredPort);
            Assert.AreEqual(string.Empty, gwStatus.MetricsBindAddress);
            Assert.AreEqual(0, gwStatus.MetricsMaxConcurrentConnections);
            Assert.ThrowsExactly<InvalidOperationException>(() => host.StartMetricsHttp());
            var formattedStatus = host.FormatOperatorStatusJson();
            StringAssert.Contains(formattedStatus, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(formattedStatus, "\"hasPendingPublication\": false");
            StringAssert.Contains(formattedStatus, "\"metricsConfiguredPort\": 0");
            StringAssert.Contains(formattedStatus, "\"metricsBindAddress\":");
            StringAssert.Contains(formattedStatus, "\"metricsMaxConcurrentConnections\": 0");
            Assert.IsTrue(formattedStatus.EndsWith('\n') || formattedStatus.EndsWith(Environment.NewLine));
            var statusPath = Path.Combine(dir, "gateway-status.json");
            host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            var statusJson = File.ReadAllText(statusPath);
            Assert.AreEqual(formattedStatus, statusJson);
            var probe = host.GetHealthProbe();
            Assert.IsTrue(probe.IsOfflinePassportComplete);
            Assert.AreEqual(host.ChainDirectory, probe.ChainDirectory);
            Assert.IsTrue(probe.IsEnabled);
            Assert.IsTrue(probe.IsPublicationConfigured);
            Assert.IsTrue(probe.HasDurableOutbox);
            Assert.IsTrue(probe.HasL1RpcEndpoint);
            Assert.IsTrue(probe.HasExpectedNetwork);
            Assert.AreEqual(894710606u, probe.ExpectedNetwork);
            Assert.IsTrue(probe.IsPublicationProfileReady);
            Assert.IsTrue(probe.MaxAutomaticRetries >= 1);
            Assert.AreEqual(1, probe.ProofSystem);
            Assert.AreEqual(MerklePathRoundProver.ConstBackendId, probe.AggregationBackendId);
            Assert.AreEqual(host.ReplayDomain.ToString(), probe.ReplayDomain);
            Assert.AreEqual(host.VerificationKeyId.ToString(), probe.VerificationKeyId);
            Assert.AreEqual(host.SettlementManagerHash.ToString(), probe.SettlementManagerHash);
            Assert.AreEqual(host.MessageRouterHash.ToString(), probe.MessageRouterHash);
            Assert.IsFalse(probe.OwnsProofProver);
            Assert.IsTrue(probe.MetricsEntryCount >= 0);
            Assert.IsTrue(probe.HasMetrics);
            Assert.IsFalse(probe.HasMetricsPlugin);
            Assert.AreEqual(0, probe.MetricsConfiguredPort);
            Assert.AreEqual(string.Empty, probe.MetricsBindAddress);
            Assert.AreEqual(0, probe.MetricsMaxConcurrentConnections);
            Assert.IsFalse(probe.HasPendingPublication);
            Assert.IsNull(probe.PendingPublicationEpoch);
            Assert.AreEqual(0, probe.AggregatorPendingCount);
            Assert.AreEqual(0, probe.OutboxQueueDepth);
            Assert.AreEqual(0, probe.OutboxRetryCount);
            Assert.IsTrue(string.IsNullOrEmpty(probe.OutboxLastError));
            Assert.IsTrue(probe.IsPublicationHealthy);
            Assert.IsTrue(probe.IsOutboxIdle);
            Assert.IsFalse(probe.IsOutboxPoisoned);
            Assert.AreEqual(0, probe.PublicationHealthFailures.Count);
            var formatted = host.FormatHealthProbeJson();
            StringAssert.Contains(formatted, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(formatted, "\"isPublicationHealthy\": true");
            StringAssert.Contains(formatted, "\"hasPendingPublication\": false");
            StringAssert.Contains(formatted, "\"isPublicationConfigured\": true");
            StringAssert.Contains(formatted, "\"hasDurableOutbox\": true");
            StringAssert.Contains(formatted, "\"isPublicationProfileReady\": true");
            StringAssert.Contains(formatted, "\"hasExpectedNetwork\": true");
            StringAssert.Contains(formatted, "\"hasMetrics\": true");
            StringAssert.Contains(formatted, "\"chainDirectory\":");
            StringAssert.Contains(formatted, "\"proofSystem\": 1");
            StringAssert.Contains(formatted, "\"aggregationBackendId\":");
            StringAssert.Contains(formatted, "\"settlementManagerHash\":");
            StringAssert.Contains(formatted, "\"messageRouterHash\":");
            StringAssert.Contains(formatted, "\"replayDomain\":");
            StringAssert.Contains(formatted, "\"verificationKeyId\":");
            StringAssert.Contains(formatted, "\"ownsProofProver\": false");
            StringAssert.Contains(formatted, "\"metricsEntryCount\":");
            StringAssert.Contains(formatted, "\"metricsConfiguredPort\": 0");
            StringAssert.Contains(formatted, "\"metricsBindAddress\":");
            Assert.IsTrue(formatted.EndsWith('\n') || formatted.EndsWith(Environment.NewLine));
            var probePath = Path.Combine(dir, "gateway-health-probe.json");
            host.WriteHealthProbeAsync(probePath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(probePath));
            var probeJson = File.ReadAllText(probePath);
            Assert.AreEqual(formatted, probeJson);
            StringAssert.Contains(probeJson, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(probeJson, "\"hasPendingPublication\": false");
            StringAssert.Contains(probeJson, "\"aggregatorPendingCount\": 0");
            StringAssert.Contains(probeJson, "\"outboxQueueDepth\": 0");
            StringAssert.Contains(probeJson, "\"proofSystem\": 1");
            StringAssert.Contains(probeJson, "\"settlementManagerHash\":");
            StringAssert.Contains(probeJson, "\"outboxRetryCount\": 0");
            StringAssert.Contains(probeJson, "\"isPublicationHealthy\": true");
            StringAssert.Contains(probeJson, "\"isOutboxIdle\": true");
            StringAssert.Contains(probeJson, "\"isOutboxPoisoned\": false");
            // Compact probe now includes binding identity for OfflinePassportFailures interpretability.
            StringAssert.Contains(probeJson, "\"verificationKeyId\":");
            StringAssert.Contains(probeJson, "\"messageRouterHash\":");
            StringAssert.Contains(statusJson, "\"hasPendingPublication\": false");
            StringAssert.Contains(statusJson, "\"aggregatorPendingCount\": 0");
            StringAssert.Contains(statusJson, "\"hasDurableOutbox\": true");
            StringAssert.Contains(statusJson, "\"isPublicationConfigured\": true");
            StringAssert.Contains(statusJson, "\"isPublicationProfileReady\": true");
            StringAssert.Contains(statusJson, "\"hasExpectedNetwork\": true");
            StringAssert.Contains(statusJson, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(statusJson, "\"offlinePassportFailures\":");
            StringAssert.Contains(statusJson, "\"isOutboxIdle\": true");
            StringAssert.Contains(statusJson, "\"isOutboxPoisoned\": false");
            StringAssert.Contains(statusJson, "\"isPublicationHealthy\": true");
            StringAssert.Contains(statusJson, "\"outboxQueueDepth\": 0");
            StringAssert.Contains(statusJson, "\"hasMetrics\": true");
            StringAssert.Contains(statusJson, "\"hasL1RpcEndpoint\": true");
            StringAssert.Contains(statusJson, "\"expectedNetwork\": 894710606");
            StringAssert.Contains(statusJson, "\"proofSystem\": 1");
            StringAssert.Contains(statusJson, "\"replayDomain\":");
            StringAssert.Contains(statusJson, "\"verificationKeyId\":");
            StringAssert.Contains(statusJson, "\"settlementManagerHash\":");
            StringAssert.Contains(statusJson, "\"messageRouterHash\":");
            Assert.IsNotNull(host.Publisher);
            // Durable outbox host compositions fail closed on direct PullAggregate.
            Assert.ThrowsExactly<InvalidOperationException>(() => host.PullAggregate());
            // Soft ReceiveBatch into durable aggregator (no L1 PublishAggregate).
            var softZ = UInt256.Zero;
            var softRoot = new UInt256(Enumerable.Repeat((byte)0xAB, 32).ToArray());
            var softBatch = new L2BatchCommitment
            {
                ChainId = 1001,
                BatchNumber = 1,
                FirstBlock = 1,
                LastBlock = 1,
                PreStateRoot = softZ,
                PostStateRoot = softRoot,
                TxRoot = softZ,
                ReceiptRoot = softZ,
                WithdrawalRoot = softZ,
                L2ToL1MessageRoot = softZ,
                L2ToL2MessageRoot = softRoot,
                DACommitment = softZ,
                PublicInputHash = softZ,
                ProofType = ProofType.Multisig,
                Proof = new byte[] { 0x11 },
            };
            host.ReceiveBatch(softBatch);
            host.ReceiveBatch(softBatch with
            {
                ChainId = 1002,
                L2ToL2MessageRoot = new UInt256(Enumerable.Repeat((byte)0xAC, 32).ToArray()),
                Proof = new byte[] { 0x22 },
            });
            Assert.IsTrue(host.AggregatorPendingCount >= 1);
            Assert.AreEqual(host.Aggregator.PendingCount, host.AggregatorPendingCount);
            var statusAfterReceive = host.GetOperatorStatus();
            Assert.AreEqual(host.AggregatorPendingCount, statusAfterReceive.AggregatorPendingCount);
            // Metrics sink is retained for outbox/aggregator emission + export.
            Assert.AreSame(metrics, host.Metrics);
            Assert.IsTrue(host.CaptureMetricsSnapshot().TotalEntries >= 0);
            Assert.IsFalse(string.IsNullOrWhiteSpace(host.ExportPrometheusMetrics()));
            var promPath = Path.Combine(dir, "gateway-metrics.prom");
            host.WritePrometheusMetricsAsync(promPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(promPath));
            Assert.IsFalse(string.IsNullOrWhiteSpace(File.ReadAllText(promPath)));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenMerkle_WrongBackend_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-bad-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var prover = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MultisigRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(
                    new byte[] { 0x01 }));
            Assert.ThrowsExactly<ArgumentException>(() =>
                GatewayHostComposition.OpenMerkle(
                    dir,
                    prover,
                    new StubSigner(),
                    UInt256.Parse("0x" + new string('1', 64)),
                    UInt256.Parse("0x" + new string('2', 64))));
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenMultisig_ConfiguresPublicationWithMultisigBackend()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-msig-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var keys = Enumerable.Range(1, 2).Select(i =>
            {
                var priv = new byte[32];
                for (var j = 0; j < 32; j++) priv[j] = (byte)(i + j);
                return (ECCurve.Secp256r1.G * priv, priv);
            }).ToList();
            var signers = new InMemorySignerSet(keys);
            var prover = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MultisigRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(
                    new byte[] { 0x02 }));

            using var host = GatewayHostComposition.OpenMultisig(
                dir,
                signers,
                threshold: 2,
                prover,
                new StubSigner(),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)));

            Assert.IsFalse(host.OwnsProofProver);
            Assert.AreEqual(
                MultisigRoundProver.ConstBackendId,
                ((BinaryTreeAggregator)host.Gateway.Aggregator).RoundProver.BackendId);
            Assert.AreEqual(2, ((MultisigRoundProver)((BinaryTreeAggregator)host.Gateway.Aggregator).RoundProver).Threshold);
            Assert.IsFalse(host.Gateway.HasPendingPublication);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// OpenMultisig rejects a non-Multisig aggregation backend (mode-only Open gate).
    /// </summary>
    [TestMethod]
    public void OpenMultisig_WrongBackend_FailsClosed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-msig-bad-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var keys = Enumerable.Range(1, 2).Select(i =>
            {
                var priv = new byte[32];
                for (var j = 0; j < 32; j++) priv[j] = (byte)(i + j);
                return (ECCurve.Secp256r1.G * priv, priv);
            }).ToList();
            var signers = new InMemorySignerSet(keys);
            // Merkle backend is wrong for OpenMultisig (requires MultisigRoundProver.ConstBackendId).
            var prover = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MerklePathRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(
                    new byte[] { 0x01 }));
            var ex = Assert.ThrowsExactly<ArgumentException>(() =>
                GatewayHostComposition.OpenMultisig(
                    dir,
                    signers,
                    threshold: 2,
                    prover,
                    new StubSigner(),
                    UInt256.Parse("0x" + new string('1', 64)),
                    UInt256.Parse("0x" + new string('2', 64))));
            StringAssert.Contains(ex.Message, "OpenMultisig");
            StringAssert.Contains(ex.Message, "MultisigRoundProver");
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public async Task OpenMerkle_StartMetricsHttp_WiresReadyzHealthprobeAndOperatorStatus()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-metrics-" + Guid.NewGuid().ToString("N"));
        var gwConfigDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        var metricsConfigDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Metrics");
        Directory.CreateDirectory(gwConfigDir);
        Directory.CreateDirectory(metricsConfigDir);
        try
        {
            File.WriteAllText(Path.Combine(gwConfigDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(metricsConfigDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "BindAddress": "127.0.0.1",
                    "Port": 0,
                    "MaxConcurrentConnections": 8
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var prover = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MerklePathRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(
                    new byte[] { 0x01 }));
            using var metricsPlugin = L2MetricsPlugin.CreateFromChainDirectory(dir);
            using var host = GatewayHostComposition.OpenMerkle(
                dir,
                prover,
                new StubSigner(),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)),
                metricsPlugin: metricsPlugin);

            Assert.IsTrue(host.HasMetricsPlugin);
            Assert.IsTrue(host.IsMetricsEnabled);
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.IsFalse(host.IsMetricsHttpHealthy);
            Assert.AreEqual(0, host.MetricsConfiguredPort); // config Port: 0
            Assert.AreEqual("127.0.0.1", host.MetricsBindAddress);
            Assert.AreEqual(8, host.MetricsMaxConcurrentConnections);
            CollectionAssert.Contains(
                host.MetricsHttpHealthFailures.ToArray(),
                nameof(GatewayHostOperatorStatus.IsMetricsHttpListening));
            Assert.IsFalse(host.IsGatewayHostHealthy);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.HasMetricsReadinessCheck);
            Assert.IsTrue(host.HasMetricsHealthProbe);
            Assert.IsTrue(host.HasMetricsOperatorStatus);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            Assert.AreEqual(0, host.MetricsHttpHealthFailures.Count);
            Assert.IsTrue(host.IsGatewayHostHealthy);
            Assert.AreEqual(0, host.GatewayHostHealthFailures.Count);

            var status = host.GetOperatorStatus();
            Assert.IsTrue(status.HasMetricsPlugin);
            Assert.IsTrue(status.IsMetricsHttpListening);
            Assert.IsTrue(status.HasMetricsOperatorStatus);
            Assert.IsTrue(status.IsMetricsHttpHealthy);
            Assert.IsTrue(status.IsGatewayHostHealthy);
            Assert.AreEqual(host.ChainDirectory, status.ChainDirectory);
            Assert.AreEqual(host.MetricsConfiguredPort, status.MetricsConfiguredPort);
            Assert.AreEqual(host.MetricsBindAddress, status.MetricsBindAddress);
            Assert.AreEqual(host.MetricsMaxConcurrentConnections, status.MetricsMaxConcurrentConnections);
            Assert.AreEqual(0, status.MetricsConfiguredPort);
            Assert.AreEqual("127.0.0.1", status.MetricsBindAddress);
            Assert.AreEqual(8, status.MetricsMaxConcurrentConnections);
            var statusJsonAfterStart = host.FormatOperatorStatusJson();
            StringAssert.Contains(statusJsonAfterStart, "\"metricsBindAddress\": \"127.0.0.1\"");
            StringAssert.Contains(statusJsonAfterStart, "\"metricsMaxConcurrentConnections\": 8");
            StringAssert.Contains(statusJsonAfterStart, "\"metricsConfiguredPort\": 0");

            var probe = host.GetHealthProbe();
            Assert.AreEqual(host.ChainDirectory, probe.ChainDirectory);
            Assert.IsTrue(probe.HasMetricsPlugin);
            Assert.IsTrue(probe.IsMetricsHttpListening);
            Assert.IsTrue(probe.HasMetricsHealthProbe);
            Assert.IsTrue(probe.HasMetricsOperatorStatus);
            Assert.IsTrue(probe.IsMetricsHttpHealthy);
            Assert.AreEqual(host.MetricsConfiguredPort, probe.MetricsConfiguredPort);
            Assert.AreEqual(host.MetricsBindAddress, probe.MetricsBindAddress);
            Assert.AreEqual(host.MetricsMaxConcurrentConnections, probe.MetricsMaxConcurrentConnections);

            using var client = new HttpClient();
            var ready = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/readyz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, ready.StatusCode);
            var healthprobe = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthprobe");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, healthprobe.StatusCode);
            var probeBody = await healthprobe.Content.ReadAsStringAsync();
            StringAssert.Contains(probeBody, "isOfflinePassportComplete");
            StringAssert.Contains(probeBody, "hasMetricsOperatorStatus");
            StringAssert.Contains(probeBody, "chainDirectory");
            StringAssert.Contains(probeBody, "metricsBindAddress");
            var operatorstatus = await client.GetAsync(
                $"http://127.0.0.1:{host.MetricsBoundPort}/operatorstatus");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, operatorstatus.StatusCode);
            var statusBody = await operatorstatus.Content.ReadAsStringAsync();
            StringAssert.Contains(statusBody, "isPublicationHealthy");
            StringAssert.Contains(statusBody, "hasMetricsPlugin");

            host.StopMetricsHttp();
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// OpenMultisig + metricsPlugin: StartMetricsHttp wires /readyz + /healthprobe
    /// (ops parity with OpenMerkle; no funded L1 publish).
    /// </summary>
    [TestMethod]
    public async Task OpenMultisig_StartMetricsHttp_WiresReadyzAndHealthprobe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-msig-metrics-" + Guid.NewGuid().ToString("N"));
        var gwConfigDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        var metricsConfigDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Metrics");
        Directory.CreateDirectory(gwConfigDir);
        Directory.CreateDirectory(metricsConfigDir);
        try
        {
            File.WriteAllText(Path.Combine(gwConfigDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(metricsConfigDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "BindAddress": "127.0.0.1",
                    "Port": 0,
                    "MaxConcurrentConnections": 8
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var keys = Enumerable.Range(1, 2).Select(i =>
            {
                var priv = new byte[32];
                for (var j = 0; j < 32; j++) priv[j] = (byte)(i + j);
                return (ECCurve.Secp256r1.G * priv, priv);
            }).ToList();
            var signers = new InMemorySignerSet(keys);
            var prover = new DelegatingGatewayProofProver(
                proofSystem: 1,
                aggregationBackendId: MultisigRoundProver.ConstBackendId,
                proofFactory: static (_, _, _) => ValueTask.FromResult<ReadOnlyMemory<byte>>(
                    new byte[] { 0x02 }));

            using var metricsPlugin = L2MetricsPlugin.CreateFromChainDirectory(dir);
            using var host = GatewayHostComposition.OpenMultisig(
                dir,
                signers,
                threshold: 2,
                prover,
                new StubSigner(),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)),
                metricsPlugin: metricsPlugin);

            Assert.IsTrue(host.HasMetricsPlugin);
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(
                MultisigRoundProver.ConstBackendId,
                ((BinaryTreeAggregator)host.Gateway.Aggregator).RoundProver.BackendId);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            Assert.IsTrue(host.IsGatewayHostHealthy);
            Assert.IsTrue(host.IsOfflinePassportComplete);

            using var client = new HttpClient();
            var ready = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/readyz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, ready.StatusCode);
            var healthprobe = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthprobe");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, healthprobe.StatusCode);
            var probeBody = await healthprobe.Content.ReadAsStringAsync();
            StringAssert.Contains(probeBody, "isOfflinePassportComplete");
            StringAssert.Contains(probeBody, "hasMetricsOperatorStatus");

            var status = host.GetOperatorStatus();
            Assert.IsTrue(status.HasMetricsPlugin);
            Assert.IsTrue(status.IsMetricsHttpHealthy);
            Assert.IsTrue(status.IsGatewayHostHealthy);

            host.StopMetricsHttp();
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    [TestMethod]
    public void OpenSp1_CreatesQueueAndPublicationProfile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-sp1-" + Guid.NewGuid().ToString("N"));
        var configDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        Directory.CreateDirectory(configDir);
        try
        {
            File.WriteAllText(Path.Combine(configDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var vk = UInt256.Parse("0x" + new string('a', 64));
            using var host = GatewayHostComposition.OpenSp1(
                dir,
                vk,
                new StubSigner(),
                UInt256.Parse("0x" + new string('d', 64)),
                vk,
                resultTimeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(10));

            Assert.IsTrue(host.OwnsProofProver);
            Assert.IsInstanceOfType(host.ProofProver, typeof(Sp1GatewayProofProver));
            var sp1 = (Sp1GatewayProofProver)host.ProofProver;
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(dir, NeoHubDeployReport.RelativeGatewayProverQueueDir)),
                sp1.QueueDirectory);
            Assert.AreEqual(
                Sp1GatewayProofProver.RecursiveAggregationBackendId,
                ((BinaryTreeAggregator)host.Gateway.Aggregator).RoundProver.BackendId);
            Assert.IsFalse(host.Gateway.HasPendingPublication);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// OpenSp1 + metricsPlugin: StartMetricsHttp wires /readyz + /healthprobe
    /// (ops parity with OpenMerkle/OpenMultisig; no funded L1 publish / SP1 prove).
    /// </summary>
    [TestMethod]
    public async Task OpenSp1_StartMetricsHttp_WiresReadyzAndHealthprobe()
    {
        var dir = Path.Combine(Path.GetTempPath(), "neo-n4-gw-host-sp1-metrics-" + Guid.NewGuid().ToString("N"));
        var gwConfigDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Gateway");
        var metricsConfigDir = Path.Combine(dir, "Plugins", "Neo.Plugins.L2Metrics");
        Directory.CreateDirectory(gwConfigDir);
        Directory.CreateDirectory(metricsConfigDir);
        try
        {
            File.WriteAllText(Path.Combine(gwConfigDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "MaxAutomaticRetries": 3
                  }
                }
                """);
            File.WriteAllText(Path.Combine(metricsConfigDir, "config.json"), """
                {
                  "PluginConfiguration": {
                    "Enabled": true,
                    "BindAddress": "127.0.0.1",
                    "Port": 0,
                    "MaxConcurrentConnections": 8
                  }
                }
                """);
            File.WriteAllText(Path.Combine(dir, "l1.deployed.json"), $$"""
                {
                  "rpc": "https://l1.example/",
                  "network": 894710606,
                  "settlementManager": "{{SettlementManager}}",
                  "messageRouter": "{{MessageRouter}}"
                }
                """);

            var vk = UInt256.Parse("0x" + new string('a', 64));
            using var metricsPlugin = L2MetricsPlugin.CreateFromChainDirectory(dir);
            using var host = GatewayHostComposition.OpenSp1(
                dir,
                vk,
                new StubSigner(),
                UInt256.Parse("0x" + new string('d', 64)),
                vk,
                resultTimeout: TimeSpan.FromSeconds(5),
                pollInterval: TimeSpan.FromMilliseconds(10),
                metricsPlugin: metricsPlugin);

            Assert.IsTrue(host.HasMetricsPlugin);
            Assert.IsTrue(host.OwnsProofProver);
            Assert.IsInstanceOfType(host.ProofProver, typeof(Sp1GatewayProofProver));
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(
                Sp1GatewayProofProver.RecursiveAggregationBackendId,
                ((BinaryTreeAggregator)host.Gateway.Aggregator).RoundProver.BackendId);

            host.StartMetricsHttp(portOverride: 0);
            Assert.IsTrue(host.IsMetricsHttpListening);
            Assert.IsTrue(host.MetricsBoundPort > 0);
            Assert.IsTrue(host.IsMetricsHttpHealthy);
            Assert.IsTrue(host.IsGatewayHostHealthy);
            Assert.IsTrue(host.IsOfflinePassportComplete);

            using var client = new HttpClient();
            var ready = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/readyz");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, ready.StatusCode);
            var healthprobe = await client.GetAsync($"http://127.0.0.1:{host.MetricsBoundPort}/healthprobe");
            Assert.AreEqual(System.Net.HttpStatusCode.OK, healthprobe.StatusCode);
            var probeBody = await healthprobe.Content.ReadAsStringAsync();
            StringAssert.Contains(probeBody, "isOfflinePassportComplete");
            StringAssert.Contains(probeBody, "hasMetricsOperatorStatus");
            StringAssert.Contains(probeBody, "chainDirectory");

            var status = host.GetOperatorStatus();
            Assert.IsTrue(status.HasMetricsPlugin);
            Assert.IsTrue(status.IsMetricsHttpHealthy);
            Assert.IsTrue(status.IsGatewayHostHealthy);
            Assert.AreEqual(host.ChainDirectory, status.ChainDirectory);

            host.StopMetricsHttp();
            Assert.IsFalse(host.IsMetricsHttpListening);
            Assert.AreEqual(0, host.MetricsBoundPort);
        }
        finally
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class StubSigner : INeoTransactionSigner
    {
        public UInt160 Account { get; } = UInt160.Parse("0x" + new string('c', 40));
        public WitnessScope Scope => WitnessScope.CalledByEntry;

        public Witness CreatePlaceholderWitness()
            => new()
            {
                InvocationScript = Array.Empty<byte>(),
                VerificationScript = Array.Empty<byte>(),
            };

        public ValueTask<Witness> SignAsync(
            Transaction tx, uint network, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
