using Neo.Extensions.VM;
using Neo.L2.ForcedInclusion;
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
        RpcForcedInclusionSource forcedInclusionSource)
    {
        Configuration = configuration;
        Rpc = rpc;
        TransactionSender = transactionSender;
        SettlementClient = settlementClient;
        ForcedInclusionFinalizer = forcedInclusionFinalizer;
        ForcedInclusionSource = forcedInclusionSource;
    }

    internal L2SettlementProductionConfiguration Configuration { get; }

    internal JsonRpcClient Rpc { get; }

    internal RpcTransactionSender TransactionSender { get; }

    internal RpcSettlementClient SettlementClient { get; }

    internal RpcForcedInclusionFinalizationClient ForcedInclusionFinalizer { get; }

    internal RpcForcedInclusionSource ForcedInclusionSource { get; }

    internal bool IsDisposed => _disposed;

    internal static L2SettlementProductionComposition Create(
        L2SettlementSettings settings,
        INeoTransactionSigner signer,
        IEnumerable<ulong> knownForcedInclusionNonces)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(knownForcedInclusionNonces);
        var signerAccount = signer.Account
            ?? throw new InvalidDataException("production settlement signer returned a null account");
        if (signerAccount.Equals(UInt160.Zero))
            throw new InvalidDataException(
                "production settlement signer account must not be zero");

        var configuration = settings.ValidateProduction();
        JsonRpcClient? rpc = null;
        RpcSettlementClient? settlementClient = null;
        RpcForcedInclusionSource? forcedInclusionSource = null;
        try
        {
            rpc = new JsonRpcClient(configuration.RpcEndpoint, httpClient: null);
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
            forcedInclusionSource = new RpcForcedInclusionSource(
                rpc,
                configuration.ForcedInclusionHash,
                configuration.ChainId,
                knownForcedInclusionNonces,
                ownsRpc: false);
            return new L2SettlementProductionComposition(
                configuration,
                rpc,
                transactionSender,
                settlementClient,
                forcedInclusionFinalizer,
                forcedInclusionSource);
        }
        catch
        {
            forcedInclusionSource?.Dispose();
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
