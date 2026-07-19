using Microsoft.VisualStudio.TestTools.UnitTesting;
using Neo.L2.Gateway.Rpc;

namespace Neo.L2.Gateway.Rpc.UnitTests;

/// <summary>
/// Pure unit tests for <see cref="GatewayHostOperatorStatus"/> health helpers
/// (no RPC / Neo.CLI; L1 confirmation remains a funded gate).
/// </summary>
[TestClass]
public sealed class UT_GatewayHostOperatorStatusHelpers
{
    [TestMethod]
    public void IsOutboxRuntimeIdle_TrueWhenEmptyAndNotPoisoned()
    {
        Assert.IsTrue(GatewayHostOperatorStatus.IsOutboxRuntimeIdle(
            hasPendingPublication: false,
            aggregatorPendingCount: 0,
            outboxQueueDepth: 0,
            outboxLastError: null,
            isOutboxPoisoned: false));
    }

    [TestMethod]
    public void IsOutboxRuntimeIdle_FalseWhenAnyWorkOrPoison()
    {
        Assert.IsFalse(GatewayHostOperatorStatus.IsOutboxRuntimeIdle(
            hasPendingPublication: true,
            aggregatorPendingCount: 0,
            outboxQueueDepth: 0,
            outboxLastError: null,
            isOutboxPoisoned: false));
        Assert.IsFalse(GatewayHostOperatorStatus.IsOutboxRuntimeIdle(
            hasPendingPublication: false,
            aggregatorPendingCount: 1,
            outboxQueueDepth: 0,
            outboxLastError: null,
            isOutboxPoisoned: false));
        Assert.IsFalse(GatewayHostOperatorStatus.IsOutboxRuntimeIdle(
            hasPendingPublication: false,
            aggregatorPendingCount: 0,
            outboxQueueDepth: 2,
            outboxLastError: null,
            isOutboxPoisoned: false));
        Assert.IsFalse(GatewayHostOperatorStatus.IsOutboxRuntimeIdle(
            hasPendingPublication: false,
            aggregatorPendingCount: 0,
            outboxQueueDepth: 0,
            outboxLastError: "lag",
            isOutboxPoisoned: false));
        Assert.IsFalse(GatewayHostOperatorStatus.IsOutboxRuntimeIdle(
            hasPendingPublication: false,
            aggregatorPendingCount: 0,
            outboxQueueDepth: 0,
            outboxLastError: null,
            isOutboxPoisoned: true));
    }

    [TestMethod]
    public void BuildOfflinePassportFailures_EmptyWhenAllReady()
    {
        var failures = GatewayHostOperatorStatus.BuildOfflinePassportFailures(
            isEnabled: true,
            isPublicationConfigured: true,
            hasDurableOutbox: true,
            hasL1RpcEndpoint: true,
            hasNonZeroReplayDomain: true,
            hasNonZeroVerificationKeyId: true,
            hasNonZeroSettlementManagerHash: true,
            hasNonZeroMessageRouterHash: true,
            hasExpectedNetwork: true,
            hasPositiveMaxAutomaticRetries: true);

        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void BuildOfflinePassportFailures_NamesEachMissingFlag()
    {
        var failures = GatewayHostOperatorStatus.BuildOfflinePassportFailures(
            isEnabled: false,
            isPublicationConfigured: false,
            hasDurableOutbox: false,
            hasL1RpcEndpoint: false,
            hasNonZeroReplayDomain: false,
            hasNonZeroVerificationKeyId: false,
            hasNonZeroSettlementManagerHash: false,
            hasNonZeroMessageRouterHash: false,
            hasExpectedNetwork: false,
            hasPositiveMaxAutomaticRetries: false);

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(GatewayHostOperatorStatus.IsEnabled),
                nameof(GatewayHostOperatorStatus.IsPublicationConfigured),
                nameof(GatewayHostOperatorStatus.HasDurableOutbox),
                nameof(GatewayHostOperatorStatus.HasL1RpcEndpoint),
                nameof(GatewayHostOperatorStatus.ReplayDomain),
                nameof(GatewayHostOperatorStatus.VerificationKeyId),
                nameof(GatewayHostOperatorStatus.SettlementManagerHash),
                nameof(GatewayHostOperatorStatus.MessageRouterHash),
                nameof(GatewayHostOperatorStatus.HasExpectedNetwork),
                nameof(GatewayHostOperatorStatus.MaxAutomaticRetries),
            },
            failures.ToArray());
    }

    [TestMethod]
    public void BuildPublicationHealthFailures_EmptyWhenPassportAndIdle()
    {
        var failures = GatewayHostOperatorStatus.BuildPublicationHealthFailures(
            offlinePassportComplete: true,
            isOutboxPoisoned: false,
            hasPendingPublication: false,
            aggregatorPendingCount: 0,
            outboxQueueDepth: 0,
            outboxLastError: null);

        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void BuildPublicationHealthFailures_NamesPassportPoisonPendingAggregatorQueueError()
    {
        var failures = GatewayHostOperatorStatus.BuildPublicationHealthFailures(
            offlinePassportComplete: false,
            isOutboxPoisoned: true,
            hasPendingPublication: true,
            aggregatorPendingCount: 2,
            outboxQueueDepth: 3,
            outboxLastError: "retry exhausted");

        CollectionAssert.AreEqual(
            new[]
            {
                nameof(GatewayHostOperatorStatus.IsOfflinePassportComplete),
                nameof(GatewayHostOperatorStatus.IsOutboxPoisoned),
                nameof(GatewayHostOperatorStatus.HasPendingPublication),
                nameof(GatewayHostOperatorStatus.AggregatorPendingCount),
                nameof(GatewayHostOperatorStatus.OutboxQueueDepth),
                nameof(GatewayHostOperatorStatus.OutboxLastError),
            },
            failures.ToArray());
    }

    [TestMethod]
    public void BuildPublicationHealthFailures_NamesNonEmptyLastError()
    {
        var failures = GatewayHostOperatorStatus.BuildPublicationHealthFailures(
            offlinePassportComplete: true,
            isOutboxPoisoned: false,
            hasPendingPublication: false,
            aggregatorPendingCount: 0,
            outboxQueueDepth: 0,
            outboxLastError: "timeout");

        CollectionAssert.AreEqual(
            new[] { nameof(GatewayHostOperatorStatus.OutboxLastError) },
            failures.ToArray());
    }

    [TestMethod]
    public void BuildMetricsHttpHealthFailures_EmptyWhenMetricsDisabled()
    {
        var failures = GatewayHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: false,
            metricsWiringComplete: false,
            metricsHttpListening: false,
            hasMetricsReadinessCheck: false,
            hasMetricsHealthProbe: false,
            hasMetricsOperatorStatus: false);
        Assert.AreEqual(0, failures.Count);
    }

    [TestMethod]
    public void BuildMetricsHttpHealthFailures_NamesWiringListeningAndProvidersWhenEnabled()
    {
        var failures = GatewayHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: true,
            metricsWiringComplete: false,
            metricsHttpListening: false,
            hasMetricsReadinessCheck: false,
            hasMetricsHealthProbe: false,
            hasMetricsOperatorStatus: false);
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(GatewayHostOperatorStatus.HasMetricsPlugin),
                nameof(GatewayHostOperatorStatus.IsMetricsHttpListening),
                nameof(GatewayHostOperatorStatus.HasMetricsReadinessCheck),
                nameof(GatewayHostOperatorStatus.HasMetricsHealthProbe),
                nameof(GatewayHostOperatorStatus.HasMetricsOperatorStatus),
            },
            failures.ToArray());

        var ok = GatewayHostOperatorStatus.BuildMetricsHttpHealthFailures(
            metricsEnabled: true,
            metricsWiringComplete: true,
            metricsHttpListening: true,
            hasMetricsReadinessCheck: true,
            hasMetricsHealthProbe: true,
            hasMetricsOperatorStatus: true);
        Assert.AreEqual(0, ok.Count);
    }

    [TestMethod]
    public void BuildGatewayHostHealthFailures_UnionsPublicationAndMetrics()
    {
        var empty = GatewayHostOperatorStatus.BuildGatewayHostHealthFailures(
            Array.Empty<string>(), Array.Empty<string>());
        Assert.AreEqual(0, empty.Count);

        var pubOnly = GatewayHostOperatorStatus.BuildGatewayHostHealthFailures(
            new[] { nameof(GatewayHostOperatorStatus.IsOutboxPoisoned) },
            Array.Empty<string>());
        CollectionAssert.AreEqual(
            new[] { nameof(GatewayHostOperatorStatus.IsOutboxPoisoned) },
            pubOnly.ToArray());

        var merged = GatewayHostOperatorStatus.BuildGatewayHostHealthFailures(
            new[] { nameof(GatewayHostOperatorStatus.IsOfflinePassportComplete) },
            new[] { nameof(GatewayHostOperatorStatus.IsMetricsHttpListening) });
        CollectionAssert.AreEqual(
            new[]
            {
                nameof(GatewayHostOperatorStatus.IsOfflinePassportComplete),
                nameof(GatewayHostOperatorStatus.IsMetricsHttpListening),
            },
            merged.ToArray());
    }
}
