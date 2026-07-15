using Neo.Extensions.VM;
using Neo.L2.ForcedInclusion;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.Plugins.L2;

internal sealed class L2SettlementProductionComposition : IDisposable
{
    private bool _disposed;

    private L2SettlementProductionComposition(
        L2SettlementProductionConfiguration configuration,
        JsonRpcClient rpc,
        RpcTransactionSender transactionSender,
        RpcSettlementClient settlementClient,
        RpcForcedInclusionFinalizationClient forcedInclusionFinalizer,
        RpcForcedInclusionEventScanner forcedInclusionEventScanner,
        RpcForcedInclusionSource forcedInclusionSource)
    {
        Configuration = configuration;
        Rpc = rpc;
        TransactionSender = transactionSender;
        SettlementClient = settlementClient;
        ForcedInclusionFinalizer = forcedInclusionFinalizer;
        ForcedInclusionEventScanner = forcedInclusionEventScanner;
        ForcedInclusionSource = forcedInclusionSource;
    }

    internal L2SettlementProductionConfiguration Configuration { get; }

    internal JsonRpcClient Rpc { get; }

    internal RpcTransactionSender TransactionSender { get; }

    internal RpcSettlementClient SettlementClient { get; }

    internal RpcForcedInclusionFinalizationClient ForcedInclusionFinalizer { get; }

    internal RpcForcedInclusionEventScanner ForcedInclusionEventScanner { get; }

    internal RpcForcedInclusionSource ForcedInclusionSource { get; }

    internal bool IsDisposed => _disposed;

    internal static L2SettlementProductionComposition Create(
        L2SettlementSettings settings,
        INeoTransactionSigner signer,
        IL2KeyValueStore forcedInclusionEventStore,
        uint forcedInclusionDeploymentHeight,
        uint forcedInclusionFinalityDepth = 1,
        int forcedInclusionMaximumBlocksPerScan = 256,
        IEnumerable<ulong>? knownForcedInclusionNonces = null,
        HttpClient? rpcHttpClient = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(forcedInclusionEventStore);
        var signerAccount = signer.Account
            ?? throw new InvalidDataException("production settlement signer returned a null account");
        if (signerAccount.Equals(UInt160.Zero))
            throw new InvalidDataException(
                "production settlement signer account must not be zero");

        var configuration = settings.ValidateProduction();
        JsonRpcClient? rpc = null;
        RpcSettlementClient? settlementClient = null;
        RpcForcedInclusionEventScanner? forcedInclusionEventScanner = null;
        RpcForcedInclusionSource? forcedInclusionSource = null;
        try
        {
            rpc = new JsonRpcClient(configuration.RpcEndpoint, rpcHttpClient);
            var transactionSender = new RpcTransactionSender(
                rpc,
                signer,
                new RpcTransactionSenderOptions
                {
                    ExpectedNetwork = configuration.ExpectedNetwork,
                });
            settlementClient = new RpcSettlementClient(
                rpc,
                configuration.SettlementManagerHash,
                CreateSettlementSubmitter(
                    transactionSender, configuration.SettlementManagerHash));
            var forcedInclusionFinalizer = new RpcForcedInclusionFinalizationClient(
                rpc,
                transactionSender,
                configuration.SettlementManagerHash,
                configuration.ForcedInclusionHash);
            forcedInclusionEventScanner = new RpcForcedInclusionEventScanner(
                rpc,
                configuration.ForcedInclusionHash,
                configuration.ChainId,
                forcedInclusionEventStore,
                forcedInclusionDeploymentHeight,
                forcedInclusionFinalityDepth,
                forcedInclusionMaximumBlocksPerScan);
            forcedInclusionSource = new RpcForcedInclusionSource(
                rpc,
                configuration.ForcedInclusionHash,
                configuration.ChainId,
                knownForcedInclusionNonces ?? Array.Empty<ulong>(),
                ownsRpc: false,
                eventScanner: forcedInclusionEventScanner);
            return new L2SettlementProductionComposition(
                configuration,
                rpc,
                transactionSender,
                settlementClient,
                forcedInclusionFinalizer,
                forcedInclusionEventScanner,
                forcedInclusionSource);
        }
        catch
        {
            if (forcedInclusionSource is not null)
                forcedInclusionSource.Dispose();
            else
                forcedInclusionEventScanner?.Dispose();
            if (settlementClient is not null)
                settlementClient.Dispose();
            else
                rpc?.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ForcedInclusionSource.Dispose();
        SettlementClient.Dispose();
    }

    private static RpcSettlementClient.SignAndSendAsync CreateSettlementSubmitter(
        RpcTransactionSender transactionSender,
        UInt160 expectedSettlementManagerHash)
        => async (
            settlementManagerHash,
            commitmentBytes,
            l1MessageHash,
            blockContextHash,
            cancellationToken) =>
        {
            if (!settlementManagerHash.Equals(expectedSettlementManagerHash))
                throw new InvalidOperationException(
                    "settlement submission target differs from the configured SettlementManager");
            ArgumentNullException.ThrowIfNull(commitmentBytes);
            ArgumentNullException.ThrowIfNull(l1MessageHash);
            ArgumentNullException.ThrowIfNull(blockContextHash);
            if (commitmentBytes.Length == 0)
                throw new InvalidDataException("settlement commitment bytes must not be empty");
            if (l1MessageHash.Length != UInt256.Length)
                throw new InvalidDataException("l1MessageHash must be 32 bytes");
            if (blockContextHash.Length != UInt256.Length)
                throw new InvalidDataException("blockContextHash must be 32 bytes");

            using var scriptBuilder = new ScriptBuilder();
            scriptBuilder.EmitDynamicCall(
                settlementManagerHash,
                "submitBatch",
                CallFlags.All,
                commitmentBytes,
                l1MessageHash,
                blockContextHash);
            var receipt = await transactionSender.SendInvocationAsync(
                scriptBuilder.ToArray(), cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(
                    "transaction sender returned a null settlement receipt");
            if (!string.Equals(receipt.VmState, "HALT", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"settlement transaction completed with VM state {receipt.VmState}");
            if (receipt.TransactionHash is null
                || receipt.TransactionHash.Equals(UInt256.Zero))
                throw new InvalidOperationException(
                    "settlement transaction returned a zero transaction hash");
            return receipt.TransactionHash;
        };
}
