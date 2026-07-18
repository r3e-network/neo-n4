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
}
