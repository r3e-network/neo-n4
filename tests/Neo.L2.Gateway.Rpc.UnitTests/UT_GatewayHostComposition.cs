using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
using Neo.L2.Telemetry;
using Neo.Network.P2P.Payloads;
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
            var statusPath = Path.Combine(dir, "gateway-status.json");
            host.WriteOperatorStatusAsync(statusPath).AsTask().GetAwaiter().GetResult();
            Assert.IsTrue(File.Exists(statusPath));
            var statusJson = File.ReadAllText(statusPath);
            StringAssert.Contains(statusJson, "\"hasPendingPublication\": false");
            StringAssert.Contains(statusJson, "\"aggregatorPendingCount\": 0");
            StringAssert.Contains(statusJson, "\"hasDurableOutbox\": true");
            StringAssert.Contains(statusJson, "\"isPublicationConfigured\": true");
            StringAssert.Contains(statusJson, "\"isPublicationProfileReady\": true");
            StringAssert.Contains(statusJson, "\"hasExpectedNetwork\": true");
            StringAssert.Contains(statusJson, "\"isOfflinePassportComplete\": true");
            StringAssert.Contains(statusJson, "\"offlinePassportFailures\":");
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
