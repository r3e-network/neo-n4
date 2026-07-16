using Neo.Cryptography.ECC;
using Neo.L2;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Proving.Attestation;
using Neo.L2.Proving.RiscVZk;
using Neo.L2.Settlement.Rpc;
using Neo.Plugins.L2;
using Neo.Plugins.L2Gateway;
using Neo.Plugins.L2Rpc;
using Neo.Wallets;

namespace Neo.L2.IntegrationTests;

/// <summary>
/// Local host-composition smoke: materialize a Multisig L2 layout from the public testnet
/// deploy report and open every chain-directory factory without funded L1/daemon traffic.
/// </summary>
/// <remarks>
/// Pins the operator path documented in IMPLEMENTATION_STATUS (create-chain / init-l2
/// artifacts + Multisig WireProduction / Gateway / inbox factories). Real RPC publication
/// and prove-batch / gateway host daemons remain funded gates.
/// </remarks>
[TestClass]
public sealed class UT_E2E_HostComposition_FromDeployReport
{
    [TestMethod]
    public void MultisigHostComposition_OpensAllChainDirectoryFactories()
    {
        var reportPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "docs", "audit", "testnet-deployment-20260716-live.json"));
        if (!File.Exists(reportPath))
            Assert.Inconclusive($"repo evidence file not found at {reportPath}");

        var chainDir = Path.Combine(
            Path.GetTempPath(),
            "neo-n4-host-comp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(chainDir);
        try
        {
            var validatorA = new KeyPair(Enumerable.Range(5, 32).Select(i => (byte)i).ToArray()).PublicKey;
            var validatorB = new KeyPair(Enumerable.Range(6, 32).Select(i => (byte)i).ToArray()).PublicKey;
            var hexA = Convert.ToHexString(validatorA.EncodePoint(true)).ToLowerInvariant();
            var hexB = Convert.ToHexString(validatorB.EncodePoint(true)).ToLowerInvariant();
            File.WriteAllText(Path.Combine(chainDir, "chain.config.json"), $$"""
                {
                  "chainId": 20260716,
                  "proofType": "Multisig",
                  "securityLevel": "Optimistic",
                  "daMode": "Local",
                  "sequencerModel": "DbftCommittee",
                  "exitModel": "Permissionless",
                  "gatewayEnabled": true,
                  "permissionlessExit": true,
                  "validators": [ "{{hexA}}", "{{hexB}}" ]
                }
                """);

            var report = NeoHubDeployReport.Load(reportPath);
            report.WriteOperatorArtifacts(chainDir);

            // Deploy-report templates default Zk; rewrite Multisig for this host path.
            RewriteProofType(chainDir, "Neo.Plugins.L2Settlement", (byte)ProofType.Multisig);
            RewriteProofType(chainDir, "Neo.Plugins.L2Prover", (byte)ProofType.Multisig);
            File.WriteAllText(Path.Combine(chainDir, "genesis-manifest.json"), """
                { "chainId": 20260716, "initialStateRoot": "0x1111111111111111111111111111111111111111111111111111111111111111" }
                """);

            using var batch = L2BatchPlugin.CreateFromChainDirectory(chainDir);
            using var settlement = L2SettlementPlugin.CreateFromChainDirectory(chainDir);
            using var bridge = L2BridgePlugin.CreateFromChainDirectory(chainDir);
            using var metrics = L2MetricsPlugin.CreateFromChainDirectory(chainDir);
            using var da = L2DAPlugin.CreateLocalFromChainDirectory(chainDir);
            using var rpcStore = InMemoryL2RpcStore.OpenFromChainDirectory(chainDir);
            using var gateway = L2GatewayPlugin.CreateMerkleDurableFromChainDirectory(chainDir);

            var signerKeys = new[]
            {
                GenKey(0x10),
                GenKey(0x20),
            };
            var signers = new InMemorySignerSet(signerKeys);
            using var prover = L2ProverPlugin.CreateMultisigWiredFromChainDirectory(chainDir, signers);

            var vk = UInt256.Parse("0x" + new string('a', 64));
            var batchSp1 = Sp1BatchProofProver.OpenFromChainDirectory(chainDir, vk);
            var gatewaySp1 = Sp1GatewayProofProver.OpenFromChainDirectory(chainDir, vk);

            using var publisher = ProofBoundRpcGlobalRootPublisher.OpenFromChainDirectory(
                chainDir,
                (_, _, _, _, _, _, _, _, _, _, _, _) =>
                    ValueTask.FromResult(UInt256.Parse("0x" + new string('f', 64))));

            // Layout owns settlement RocksDB dirs; dispose before L1Inbox opens the same paths.
            using (var layout = L2SettlementStoreLayout.Open(chainDir))
            {
                Assert.IsNotNull(layout.ProofWitness);
                Assert.IsNotNull(layout.ForcedInclusionEvents);
                Assert.IsNotNull(layout.SharedBridgeDeposits);
                Assert.IsNotNull(layout.MessageRouterEvents);
            }

            // openFinalizedProofStore=false: InMemoryL2RpcStore already holds data/rpc/proofs.
            // Production hosts pick one owner (RPC store vs MessageRouter) for that path.
            using var inbox = L1InboxFromChainDirectory.Open(chainDir, openFinalizedProofStore: false);
            inbox.WireBatch(batch);

            Assert.AreEqual(20260716u, bridge.ChainId);
            Assert.AreEqual(ProofType.Multisig, prover.Kind);
            Assert.IsInstanceOfType(prover.Prover, typeof(AttestationProver));
            Assert.IsInstanceOfType(gateway.Aggregator, typeof(BinaryTreeAggregator));
            var binary = (BinaryTreeAggregator)gateway.Aggregator;
            Assert.IsInstanceOfType(binary.RoundProver, typeof(MerklePathRoundProver));
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(chainDir, NeoHubDeployReport.RelativeProverInboxDir)),
                batchSp1.QueueDirectory);
            Assert.AreEqual(
                Path.GetFullPath(Path.Combine(chainDir, NeoHubDeployReport.RelativeGatewayProverQueueDir)),
                gatewaySp1.QueueDirectory);
            Assert.IsNotNull(inbox.ForcedInclusion);
            Assert.IsNotNull(inbox.Deposits);
            Assert.IsNotNull(inbox.MessageRouter);
            Assert.IsNotNull(batch);
            Assert.IsNotNull(settlement);
            Assert.IsNotNull(da);
            Assert.IsNotNull(metrics);
            Assert.IsNotNull(rpcStore);
            Assert.IsNotNull(publisher);
            var settlementSettings = L2SettlementSettings.FromChainDirectory(chainDir);
            Assert.AreEqual(report.SettlementManager.ToString(), settlementSettings.SettlementManagerHash);
            Assert.AreEqual(20260716u, settlementSettings.ChainId);
            Assert.AreEqual((byte)ProofType.Multisig, settlementSettings.ProofType);
        }
        finally
        {
            if (Directory.Exists(chainDir))
                Directory.Delete(chainDir, recursive: true);
        }
    }

    private static void RewriteProofType(string chainDir, string pluginFolder, byte proofType)
    {
        var path = Path.Combine(chainDir, "Plugins", pluginFolder, "config.json");
        if (!File.Exists(path))
            Assert.Fail($"expected materialised plugin config at {path}");
        var text = File.ReadAllText(path);
        // Materialized configs use "ProofType": <byte> under PluginConfiguration.
        var rewritten = System.Text.RegularExpressions.Regex.Replace(
            text,
            "\"ProofType\"\\s*:\\s*\\d+",
            $"\"ProofType\": {proofType}");
        File.WriteAllText(path, rewritten);
    }

    private static (ECPoint Pub, byte[] Priv) GenKey(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return (ECCurve.Secp256r1.G * priv, priv);
    }
}
