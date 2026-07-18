using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Neo.L2;

namespace Neo.Stack.Cli.Commands;

internal static class InitL2Command
{
    public static int Run(string[] args)
    {
        var rawChainId = ArgUtil.Get(args, "--chain-id", "1001");
        if (!uint.TryParse(rawChainId, out var parsedChainId))
        {
            Console.Error.WriteLine($"--chain-id must be a non-negative integer, got '{rawChainId}'");
            return 1;
        }
        // Match create-chain's iter-123 fix: reject the L1-reserved 0 here too so the
        // operator sees the misconfig at init time, not when the L2 first tries to
        // route a cross-chain message.
        var chainId = Neo.L2.ChainIdValidator.ValidateL2(parsedChainId, "--chain-id");
        var da = ArgUtil.Get(args, "--da", "neofs");
        // Accept either --path or --output; create-chain uses --output, so a script
        // that strings the subcommands together can reuse one flag.
        var outputFlag = ArgUtil.Get(args, "--output", "");
        var path = outputFlag.Length > 0
            ? outputFlag
            : ArgUtil.Get(args, "--path", $"./chain-{chainId}");

        if (!Directory.Exists(path))
        {
            Console.Error.WriteLine($"Chain dir not found: {path}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return 1;
        }
        // Defense-in-depth: chain.config.json is the authoritative input for every
        // downstream step (register-chain, start-*, devnet preview). If it's missing,
        // the operator forgot create-chain (or pointed --output at an unrelated dir).
        // Surface that here with a clear remediation rather than silently creating
        // the working subdirs against a chain that hasn't been configured.
        var configPath = Path.Combine(path, "chain.config.json");
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine($"Missing chain.config.json: {configPath}");
            Console.Error.WriteLine("Run `neo-stack create-chain --chain-id <id>` first.");
            return 2;
        }

        var nodeConfigDestination = Path.Combine(path, "node", "config.json");
        if (!TryLoadReviewedConfig(
                args,
                "--node-config",
                nodeConfigDestination,
                out var nodeConfigBytes,
                out var nodeConfigError))
        {
            Console.Error.WriteLine(nodeConfigError);
            return 3;
        }
        var batcherConfigDestination = Path.Combine(path, "batcher-node", "config.json");
        if (!TryLoadReviewedConfig(
                args,
                "--batcher-node-config",
                batcherConfigDestination,
                out var batcherConfigBytes,
                out var batcherConfigError))
        {
            Console.Error.WriteLine(batcherConfigError);
            return 3;
        }

        Directory.CreateDirectory(Path.Combine(path, "data"));
        Directory.CreateDirectory(Path.Combine(path, "batcher-data"));
        Directory.CreateDirectory(Path.Combine(path, "logs"));
        Directory.CreateDirectory(Path.Combine(path, "Plugins"));
        Directory.CreateDirectory(Path.Combine(path, "node", "Plugins"));
        Directory.CreateDirectory(Path.Combine(path, "batcher-node", "Plugins"));
        Directory.CreateDirectory(Path.Combine(path, "prover", "inbox"));
        Directory.CreateDirectory(Path.Combine(path, "prover", "archive"));
        // Canonical WireProduction RocksDB roots (empty until the host opens them).
        NeoHubDeployReport.EnsureSettlementStoreDirectories(path);
        if (nodeConfigBytes is not null)
        {
            File.WriteAllBytes(nodeConfigDestination, nodeConfigBytes);
        }
        if (batcherConfigBytes is not null)
        {
            File.WriteAllBytes(batcherConfigDestination, batcherConfigBytes);
        }

        // Optional: materialize L1 deploy hashes + plugin configs as soon as the node
        // layout exists, so WireProduction hosts do not wait until register-chain.
        var deployReportPath = ArgUtil.Get(args, "--from-deploy-report", "");
        IReadOnlyList<string>? deployArtifacts = null;
        if (deployReportPath.Length > 0)
        {
            try
            {
                var report = NeoHubDeployReport.Load(deployReportPath);
                if (report.L2ChainId != chainId)
                {
                    Console.Error.WriteLine(
                        $"--chain-id {chainId} differs from deploy report l2ChainId {report.L2ChainId}");
                    return 4;
                }
                deployArtifacts = report.WriteOperatorArtifacts(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"failed to load --from-deploy-report: {ex.Message}");
                return 4;
            }
        }

        Console.WriteLine($"Initialized L2 node at {path}");
        Console.WriteLine($"  data       = {path}/data");
        Console.WriteLine($"  batcher    = {path}/batcher-data");
        Console.WriteLine($"  logs       = {path}/logs");
        Console.WriteLine($"  plugins    = {path}/Plugins");
        Console.WriteLine($"  node root  = {path}/node");
        Console.WriteLine($"  prover     = {path}/prover");
        Console.WriteLine($"  da mode    = {da}");
        if (deployArtifacts is not null)
        {
            Console.WriteLine($"  L1 deploy  = {deployReportPath}");
            foreach (var artifact in deployArtifacts.Distinct(StringComparer.Ordinal))
                Console.WriteLine($"  wrote      = {path}/{artifact}");
        }
        // Operator host composition roots (no Neo.CLI required for local Multisig/Optimistic/Zk).
        Console.WriteLine("  host open  = MultisigLocalHostComposition.Open / OptimisticLocalHostComposition.Open / ZkLocalHostComposition.Open");
        Console.WriteLine("  host ready = LocalHost.IsOperatorReady / GetOperatorStatusAsync (see wireproduction notes)");
        Console.WriteLine("  host batch = LocalHost.BatcherConfiguredChainId / SettlementConfiguredChainId / NextExpectedBlock / NextBatchNumber");
        Console.WriteLine("               PendingSealedBatchNumber / MaxBlocksPerBatch / SettlementConfiguredProofType");
        Console.WriteLine("               RpcChainId / IsChainIdConfigConsistent / IsProofTypeConfigConsistent");
        Console.WriteLine("               RpcDaMode / IsDaModeConfigConsistent");
        Console.WriteLine("               IsNeoHubHashWiringComplete / IsBatcherInboxWiringComplete");
        Console.WriteLine("               IsSecurityLevelProofTypeConsistent / IsSecurityLevelDaModeConsistent");
        Console.WriteLine("               HasExpectedNetwork / HasScannerDeployHeights / IsOfflinePassportComplete");
        Console.WriteLine("               OfflinePassportFailures (empty when complete)");
        Console.WriteLine("               MaxForcedTransactionsPerBatch / MaxL1MessagesPerBatch / OpenBatchForcedInclusionCount");
        Console.WriteLine("               OpenBatchL2ToL2MessageCount / OpenBatchWithdrawalCount / HasBatchDepositSource");
        Console.WriteLine("               HasBatchMessageRouter / HasBatchForcedInclusionSource / HasBatchProver");
        Console.WriteLine("  host rpc   = LocalHost.AddRpcBatch / RecordRpcDeposit / RegisterRpcAsset / MessageOutbox");
        Console.WriteLine("  host msg   = LocalHost.MessageOutboxL2ToL1Root / KnownInboundNonceCount / RegisterInboundMessageNonce");
        Console.WriteLine("  host da/fi = LocalHost.PublishDaAsync / SupportsLocalDaReader / RegisterForcedInclusionNonce / KnownForcedInclusionNonceCount");
        Console.WriteLine("  host gw    = GatewayHost.IsEnabled / MaxAutomaticRetries / IsPublicationConfigured");
        Console.WriteLine("               ProofSystem / AggregationBackendId / ExpectedNetwork / HasL1RpcEndpoint");
        Console.WriteLine("               IsPublicationProfileReady / HasExpectedNetwork / IsOfflinePassportComplete");
        Console.WriteLine("               OfflinePassportFailures (empty when complete)");
        Console.WriteLine("               IsOutboxIdle / IsOutboxPoisoned / IsPublicationHealthy");
        Console.WriteLine("               PublicationHealthFailures (empty when publication healthy)");
        Console.WriteLine("               ReplayDomain / VerificationKeyId / SettlementManagerHash / MessageRouterHash");
        Console.WriteLine("  host ops   = LocalHost.ExportPrometheusMetrics / IsMetricsEnabled / MetricsConfiguredPort / MetricsMaxConcurrentConnections / RegisterBridgeAsset");
        Console.WriteLine("               IsMetricsWiringComplete / HasMetricsReadinessCheck / StartMetricsHttp / StopMetricsHttp");
        Console.WriteLine("  host pipe  = LocalHost.IsDepositPipelineWiringComplete / IsMessagePipelineWiringComplete");
        Console.WriteLine("               IsForcedInclusionPipelineWiringComplete / IsSettlementClientWiringComplete");
        Console.WriteLine("               IsPipelineEnabled / IsSettlementPoisoned / IsSettlementIdle / IsPipelineHealthy");
        Console.WriteLine("               PipelineHealthFailures (empty when pipeline healthy; includes HasPendingSealedBatch)");
        Console.WriteLine("               IsMetricsHttpHealthy / MetricsHttpHealthFailures (N/A empty when metrics disabled)");
        Console.WriteLine("  host bridge= LocalHost.ProcessDeposit / ProcessReadyDeposits / ScanAndProcessReadyDepositsAsync");
        Console.WriteLine("               ConsumedDepositCount / DepositSourceReadyCount / DepositSourceReservedCount / DepositSourceSoftConsumedCount");
        Console.WriteLine("               IsSettlementEnabled / L1FinalityDepth / HasL1RpcEndpoint / ExpectedNetwork");
        Console.WriteLine("               HasSettlementManagerHash / HasForcedInclusionHash / HasSharedBridgeHash / HasMessageRouterHash");
        Console.WriteLine("               HasL2BridgeHash / HasMessageOutbox");
        Console.WriteLine("               ForcedInclusionDeploymentHeight / SharedBridgeDeploymentHeight");
        Console.WriteLine("               StageWithdrawal / ProveAsync / HasOverdueForcedInclusionAsync / L1InboxPendingCount");
        Console.WriteLine("               HasForcedInclusionFinalizer / HasSettlementClient / HasTransactionSender");
        Console.WriteLine("  host settle= LocalHost.GetLatestDurableCheckpointAsync / GetInitialStateRootAsync / LatestCheckpointBatchNumber / InitialStateRoot");
        Console.WriteLine("  host status= LocalHost.WriteOperatorStatusAsync(path) (JSON health dump)");
        Console.WriteLine("  host prom  = LocalHost/GatewayHost WritePrometheusMetricsAsync; GatewayHost.IsPublicationConfigured");
        Console.WriteLine("               (see l1.wireproduction-notes.json when --from-deploy-report was used)");
        return 0;
    }

    private static bool TryLoadReviewedConfig(
        string[] args,
        string option,
        string destinationPath,
        out byte[]? configBytes,
        out string error)
    {
        configBytes = null;
        error = "";
        var configuredSource = ArgUtil.Get(args, option, "");
        if (configuredSource.Length == 0) return true;

        var sourcePath = Path.GetFullPath(configuredSource);
        if (!File.Exists(sourcePath))
        {
            error = $"Neo.CLI config not found: {sourcePath}";
            return false;
        }
        try
        {
            configBytes = File.ReadAllBytes(sourcePath);
            using var _ = JsonDocument.Parse(configBytes);
        }
        catch (Exception exception) when (exception is IOException or JsonException)
        {
            error = $"Invalid Neo.CLI config: {exception.Message}";
            return false;
        }
        if (File.Exists(destinationPath)
            && !File.ReadAllBytes(destinationPath).SequenceEqual(configBytes))
        {
            error = $"Refusing to overwrite existing Neo.CLI config: {destinationPath}";
            return false;
        }
        return true;
    }
}
