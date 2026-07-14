using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.ExternalBridge;

/// <summary>Production RPC, finality, authentication, and RocksDB relay configuration.</summary>
/// <remarks>See <c>doc.md</c> §11.3, §14.2, and §17.</remarks>
public sealed record L2PayoutRelayProductionOptions
{
    /// <summary>Neo L1 RPC endpoint containing the immutable payout adapter.</summary>
    public required string L1RpcEndpoint { get; init; }

    /// <summary>Target Neo L2 RPC endpoint containing the native bridge.</summary>
    public required string L2RpcEndpoint { get; init; }

    /// <summary>Expected Neo L1 network magic.</summary>
    public required uint L1Network { get; init; }

    /// <summary>Expected target Neo L2 network magic.</summary>
    public required uint L2Network { get; init; }

    /// <summary>Immutable deployed L1 payout adapter.</summary>
    public required UInt160 Adapter { get; init; }

    /// <summary>Target L2 native external bridge script hash.</summary>
    public required UInt160 L2NativeBridge { get; init; }

    /// <summary>Destination Neo L2 domain bound in both endpoints.</summary>
    public required uint NeoChainId { get; init; }

    /// <summary>Witness account configured in both endpoints.</summary>
    public required UInt160 RelayAccount { get; init; }

    /// <summary>First L1 block that can contain this adapter's events.</summary>
    public required uint L1DeploymentHeight { get; init; }

    /// <summary>Number of L1 confirmations required before a payout enters the relay.</summary>
    public uint L1FinalityDepth { get; init; } = 1;

    /// <summary>Dedicated RocksDB directory for cursor, queue, and write-ahead-log state.</summary>
    public required string DataDirectory { get; init; }

    /// <summary>Maximum automatic failures before an item is poisoned.</summary>
    public int MaximumRetries { get; init; } = 10;
}

/// <summary>Owned production runtime for scan, credit, reconcile, and acknowledgement.</summary>
/// <remarks>See <c>doc.md</c> §11.3 and §17.</remarks>
public sealed class L2PayoutRelayRuntime : IDisposable
{
    private readonly JsonRpcClient _l1Rpc;
    private readonly JsonRpcClient _l2Rpc;
    private readonly PersistentL2PayoutOutbox _outbox;
    private readonly RpcL1PayoutQueueScanner _scanner;
    private readonly RpcL2PayoutCreditClient _creditClient;
    private readonly RpcL1PayoutAcknowledgementClient _acknowledgementClient;
    private readonly L2PayoutRelay _relay;
    private int _configurationValidated;
    private int _disposed;

    private L2PayoutRelayRuntime(
        JsonRpcClient l1Rpc,
        JsonRpcClient l2Rpc,
        PersistentL2PayoutOutbox outbox,
        RpcL1PayoutQueueScanner scanner,
        RpcL2PayoutCreditClient creditClient,
        RpcL1PayoutAcknowledgementClient acknowledgementClient,
        L2PayoutRelay relay)
    {
        _l1Rpc = l1Rpc;
        _l2Rpc = l2Rpc;
        _outbox = outbox;
        _scanner = scanner;
        _creditClient = creditClient;
        _acknowledgementClient = acknowledgementClient;
        _relay = relay;
    }

    /// <summary>Create a fully wired production runtime with caller-supplied signers.</summary>
    public static L2PayoutRelayRuntime Create(
        L2PayoutRelayProductionOptions options,
        INeoTransactionSigner l1Signer,
        INeoTransactionSigner l2Signer)
    {
        ValidateOptions(options, l1Signer, l2Signer);
        var l1Rpc = new JsonRpcClient(options.L1RpcEndpoint);
        var l2Rpc = new JsonRpcClient(options.L2RpcEndpoint);
        PersistentL2PayoutOutbox? outbox = null;
        RpcL1PayoutQueueScanner? scanner = null;
        L2PayoutRelay? relay = null;
        try
        {
            var store = new RocksDbKeyValueStore(options.DataDirectory);
            outbox = new PersistentL2PayoutOutbox(store, ownsStore: true);
            var l1Sender = new RpcTransactionSender(
                l1Rpc, l1Signer, new RpcTransactionSenderOptions { ExpectedNetwork = options.L1Network });
            var l2Sender = new RpcTransactionSender(
                l2Rpc, l2Signer, new RpcTransactionSenderOptions { ExpectedNetwork = options.L2Network });
            var creditClient = new RpcL2PayoutCreditClient(
                l2Rpc, l2Sender, options.L2NativeBridge, options.NeoChainId,
                options.RelayAccount);
            var acknowledgementClient = new RpcL1PayoutAcknowledgementClient(
                l1Rpc, l1Sender, options.Adapter, options.NeoChainId, options.RelayAccount);
            scanner = new RpcL1PayoutQueueScanner(
                l1Rpc,
                options.Adapter,
                options.NeoChainId,
                store,
                outbox,
                options.L1DeploymentHeight,
                options.L1FinalityDepth);
            relay = new L2PayoutRelay(
                outbox, creditClient, acknowledgementClient, options.MaximumRetries);
            return new L2PayoutRelayRuntime(
                l1Rpc, l2Rpc, outbox, scanner, creditClient, acknowledgementClient, relay);
        }
        catch
        {
            relay?.Dispose();
            scanner?.Dispose();
            outbox?.Dispose();
            l1Rpc.Dispose();
            l2Rpc.Dispose();
            throw;
        }
    }

    /// <summary>Scan finalized L1 events and process durable payouts once.</summary>
    public async ValueTask<int> RunOnceAsync(
        int maximumItems = 100,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (Volatile.Read(ref _configurationValidated) == 0)
        {
            await _acknowledgementClient.ValidateConfigurationAsync(cancellationToken)
                .ConfigureAwait(false);
            await _creditClient.ValidateConfigurationAsync(cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _configurationValidated, 1);
        }
        await _scanner.ScanAsync(cancellationToken).ConfigureAwait(false);
        return await _relay.ProcessAsync(maximumItems, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _relay.Dispose();
        _scanner.Dispose();
        _outbox.Dispose();
        _l1Rpc.Dispose();
        _l2Rpc.Dispose();
    }

    private static void ValidateOptions(
        L2PayoutRelayProductionOptions options,
        INeoTransactionSigner l1Signer,
        INeoTransactionSigner l2Signer)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(l1Signer);
        ArgumentNullException.ThrowIfNull(l2Signer);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.L1RpcEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.L2RpcEndpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DataDirectory);
        if (options.Adapter is null || options.Adapter == UInt160.Zero)
            throw new ArgumentException("Adapter must not be zero.", nameof(options));
        if (options.L2NativeBridge is null || options.L2NativeBridge == UInt160.Zero)
            throw new ArgumentException("L2 native bridge must not be zero.", nameof(options));
        if (options.RelayAccount is null || options.RelayAccount == UInt160.Zero)
            throw new ArgumentException("Relay account must not be zero.", nameof(options));
        if (options.NeoChainId == 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Neo chain id must be non-zero.");
        if (options.MaximumRetries <= 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum retries must be positive.");
        if (l1Signer.Account != options.RelayAccount || l2Signer.Account != options.RelayAccount)
            throw new InvalidOperationException(
                "Both transaction signers must match the relay account pinned on L1 and L2.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }
}
