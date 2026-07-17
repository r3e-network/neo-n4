using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Proving.Attestation;
using Neo.L2.Settlement.Rpc;
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
            using var host = GatewayHostComposition.OpenMerkle(
                dir,
                prover,
                new StubSigner(),
                UInt256.Parse("0x" + new string('d', 64)),
                UInt256.Parse("0x" + new string('e', 64)));

            Assert.AreEqual(Path.GetFullPath(dir), host.ChainDirectory);
            Assert.IsFalse(host.OwnsProofProver);
            Assert.AreSame(prover, host.ProofProver);
            Assert.IsInstanceOfType(host.Gateway.Aggregator, typeof(BinaryTreeAggregator));
            Assert.AreEqual(
                MerklePathRoundProver.ConstBackendId,
                ((BinaryTreeAggregator)host.Gateway.Aggregator).RoundProver.BackendId);
            Assert.IsFalse(host.Gateway.HasPendingPublication);
            Assert.IsNotNull(host.Publisher);
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
