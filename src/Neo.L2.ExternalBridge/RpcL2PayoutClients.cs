using Neo.Extensions;
using Neo.Extensions.IO;
using Neo.Extensions.VM;
using Neo.IO;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.ExternalBridge;

/// <summary>RPC-backed authenticated target-L2 native credit client.</summary>
/// <remarks>See <c>doc.md</c> §11.3 and §14.2.</remarks>
public sealed class RpcL2PayoutCreditClient : IL2PayoutCreditClient
{
    private readonly JsonRpcClient _rpc;
    private readonly RpcTransactionSender _sender;
    private readonly UInt160 _nativeBridge;
    private readonly uint _neoChainId;
    private readonly UInt160 _relayAccount;

    /// <summary>Construct against one target Neo L2 domain.</summary>
    public RpcL2PayoutCreditClient(
        JsonRpcClient rpc,
        RpcTransactionSender sender,
        UInt160 nativeBridge,
        uint neoChainId,
        UInt160 relayAccount)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(nativeBridge);
        ArgumentNullException.ThrowIfNull(relayAccount);
        if (nativeBridge == UInt160.Zero)
            throw new ArgumentException("Native bridge hash must not be zero.", nameof(nativeBridge));
        if (neoChainId == 0) throw new ArgumentOutOfRangeException(nameof(neoChainId));
        if (relayAccount == UInt160.Zero)
            throw new ArgumentException("Relay account must not be zero.", nameof(relayAccount));
        _rpc = rpc;
        _sender = sender;
        _nativeBridge = nativeBridge;
        _neoChainId = neoChainId;
        _relayAccount = relayAccount;
    }

    /// <summary>Fail closed unless the native endpoint is bound to this domain and signer.</summary>
    public async ValueTask ValidateConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var chainId = RpcContractReader.ParseUInt64(await RpcContractReader.InvokeReadAsync(
            _rpc, _nativeBridge, "getChainId", [], cancellationToken).ConfigureAwait(false));
        if (chainId != _neoChainId)
            throw new InvalidOperationException(
                $"L2 native bridge chain id {chainId} does not match configured {_neoChainId}.");
        var systemAccount = RpcContractReader.ParseUInt160(
            await RpcContractReader.InvokeReadAsync(
                _rpc, _nativeBridge, "getSystemAccount", [], cancellationToken)
                .ConfigureAwait(false));
        if (systemAccount != _relayAccount)
            throw new InvalidOperationException(
                $"L2 native bridge system account {systemAccount} does not match relay {_relayAccount}.");
    }

    /// <inheritdoc />
    public async ValueTask<L2PayoutCreditObservation> ObserveAsync(
        L2PayoutInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        ValidateInstruction(instruction);
        var messageHash = RpcContractReader.ParseUInt256(
            await RpcContractReader.InvokeReadAsync(
                _rpc,
                _nativeBridge,
                "getInboundMessageHash",
                [instruction.Message.ExternalChainId, instruction.Message.Nonce],
                cancellationToken).ConfigureAwait(false));
        if (messageHash == UInt256.Zero) return L2PayoutCreditObservation.Missing;
        var transactionHash = RpcContractReader.ParseUInt256(
            await RpcContractReader.InvokeReadAsync(
                _rpc,
                _nativeBridge,
                "getInboundTransactionHash",
                [instruction.Message.ExternalChainId, instruction.Message.Nonce],
                cancellationToken).ConfigureAwait(false));
        return new L2PayoutCreditObservation(messageHash, transactionHash);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> PrepareAsync(
        L2PayoutInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        ValidateInstruction(instruction);
        using var builder = new ScriptBuilder();
        builder.EmitDynamicCall(
            _nativeBridge,
            "applyPayout",
            CallFlags.All,
            instruction.Message.ExternalChainId,
            instruction.Message.NeoChainId,
            instruction.Message.Nonce,
            instruction.ForeignAsset,
            instruction.NeoAsset,
            instruction.Message.Recipient,
            instruction.Amount,
            instruction.Message.DeadlineUnixSeconds,
            instruction.Message.SourceTxRef,
            instruction.Message.MessageHash,
            instruction.CanonicalMessageBytes.ToArray());
        var transaction = await _sender.BuildSignedInvocationAsync(
            builder.ToArray(), cancellationToken).ConfigureAwait(false);
        return transaction.ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> BroadcastAsync(
        ReadOnlyMemory<byte> signedTransaction,
        CancellationToken cancellationToken = default)
    {
        if (signedTransaction.IsEmpty)
            throw new ArgumentException("Signed transaction must not be empty.", nameof(signedTransaction));
        var transaction = signedTransaction.ToArray().AsSerializable<Transaction>();
        var receipt = await _sender.BroadcastAndWaitAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
        return receipt.TransactionHash;
    }

    private void ValidateInstruction(L2PayoutInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.Message.NeoChainId != _neoChainId)
            throw new InvalidOperationException("Payout instruction targets another Neo L2 domain.");
    }
}

/// <summary>RPC-backed authenticated L1 adapter acknowledgement client.</summary>
/// <remarks>See <c>doc.md</c> §11.3 and §14.2.</remarks>
public sealed class RpcL1PayoutAcknowledgementClient : IL1PayoutAcknowledgementClient
{
    private const byte StatusEnqueued = 1;
    private const byte StatusAcknowledged = 2;
    private readonly JsonRpcClient _rpc;
    private readonly RpcTransactionSender _sender;
    private readonly UInt160 _adapter;
    private readonly uint _neoChainId;
    private readonly UInt160 _relayAccount;

    /// <summary>Construct against one immutable L1 adapter.</summary>
    public RpcL1PayoutAcknowledgementClient(
        JsonRpcClient rpc,
        RpcTransactionSender sender,
        UInt160 adapter,
        uint neoChainId,
        UInt160 relayAccount)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(relayAccount);
        if (adapter == UInt160.Zero)
            throw new ArgumentException("Adapter hash must not be zero.", nameof(adapter));
        if (neoChainId == 0) throw new ArgumentOutOfRangeException(nameof(neoChainId));
        if (relayAccount == UInt160.Zero)
            throw new ArgumentException("Relay account must not be zero.", nameof(relayAccount));
        _rpc = rpc;
        _sender = sender;
        _adapter = adapter;
        _neoChainId = neoChainId;
        _relayAccount = relayAccount;
    }

    /// <summary>Fail closed unless adapter ABI, domain, and relay witness are exactly pinned.</summary>
    public async ValueTask ValidateConfigurationAsync(
        CancellationToken cancellationToken = default)
    {
        var version = RpcContractReader.ParseUInt64(await RpcContractReader.InvokeReadAsync(
            _rpc, _adapter, "payoutVersion", [], cancellationToken).ConfigureAwait(false));
        if (version != 1)
            throw new InvalidOperationException($"Unsupported L1 payout adapter version {version}.");
        var chainId = RpcContractReader.ParseUInt64(await RpcContractReader.InvokeReadAsync(
            _rpc, _adapter, "getNeoChainId", [], cancellationToken).ConfigureAwait(false));
        if (chainId != _neoChainId)
            throw new InvalidOperationException(
                $"L1 payout adapter chain id {chainId} does not match configured {_neoChainId}.");
        var relayAccount = RpcContractReader.ParseUInt160(
            await RpcContractReader.InvokeReadAsync(
                _rpc, _adapter, "getRelayAccount", [], cancellationToken).ConfigureAwait(false));
        if (relayAccount != _relayAccount)
            throw new InvalidOperationException(
                $"L1 payout adapter relay {relayAccount} does not match configured {_relayAccount}.");
    }

    /// <inheritdoc />
    public async ValueTask<L1PayoutAcknowledgementObservation> ObserveAsync(
        L2PayoutInstruction instruction,
        CancellationToken cancellationToken = default)
    {
        ValidateInstruction(instruction);
        var statusValue = RpcContractReader.ParseUInt64(
            await RpcContractReader.InvokeReadAsync(
                _rpc, _adapter, "getPayoutStatus", [instruction.Sequence], cancellationToken)
                .ConfigureAwait(false));
        if (statusValue == 0)
            throw new InvalidDataException("Finalized L1 payout queue entry disappeared.");
        if (statusValue == StatusEnqueued) return L1PayoutAcknowledgementObservation.Missing;
        if (statusValue != StatusAcknowledged)
            throw new InvalidDataException($"L1 payout adapter returned unknown status {statusValue}.");

        var storedMessageHash = RpcContractReader.ParseUInt256(
            await RpcContractReader.InvokeReadAsync(
                _rpc, _adapter, "getPayoutMessageHash", [instruction.Sequence], cancellationToken)
                .ConfigureAwait(false));
        if (storedMessageHash != instruction.Message.MessageHash)
            throw new InvalidDataException("L1 payout sequence is bound to another message hash.");
        var l2TransactionHash = RpcContractReader.ParseUInt256(
            await RpcContractReader.InvokeReadAsync(
                _rpc,
                _adapter,
                "getPayoutL2TransactionHash",
                [instruction.Sequence],
                cancellationToken).ConfigureAwait(false));
        return new L1PayoutAcknowledgementObservation(true, l2TransactionHash);
    }

    /// <inheritdoc />
    public async ValueTask<ReadOnlyMemory<byte>> PrepareAsync(
        L2PayoutInstruction instruction,
        UInt256 l2TransactionHash,
        CancellationToken cancellationToken = default)
    {
        ValidateInstruction(instruction);
        ArgumentNullException.ThrowIfNull(l2TransactionHash);
        if (l2TransactionHash == UInt256.Zero)
            throw new ArgumentException("L2 transaction hash must not be zero.", nameof(l2TransactionHash));
        using var builder = new ScriptBuilder();
        builder.EmitDynamicCall(
            _adapter,
            "acknowledge",
            CallFlags.All,
            instruction.Sequence,
            instruction.Message.MessageHash,
            l2TransactionHash);
        var transaction = await _sender.BuildSignedInvocationAsync(
            builder.ToArray(), cancellationToken).ConfigureAwait(false);
        return transaction.ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> BroadcastAsync(
        ReadOnlyMemory<byte> signedTransaction,
        CancellationToken cancellationToken = default)
    {
        if (signedTransaction.IsEmpty)
            throw new ArgumentException("Signed transaction must not be empty.", nameof(signedTransaction));
        var transaction = signedTransaction.ToArray().AsSerializable<Transaction>();
        var receipt = await _sender.BroadcastAndWaitAsync(transaction, cancellationToken)
            .ConfigureAwait(false);
        return receipt.TransactionHash;
    }

    private void ValidateInstruction(L2PayoutInstruction instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        if (instruction.Adapter != _adapter)
            throw new InvalidOperationException("Payout instruction came from another L1 adapter.");
        if (instruction.Message.NeoChainId != _neoChainId)
            throw new InvalidOperationException("Payout instruction targets another Neo L2 domain.");
    }
}
