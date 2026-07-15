using Neo.Cryptography;
using Neo.Extensions.VM;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// Completes permissionless forced-inclusion consumption after a batch becomes final on L1.
/// </summary>
/// <remarks>
/// See doc.md §15.4. The client verifies every persisted transaction proof against
/// <c>SettlementManager.getFinalizedTxRoot</c> before broadcasting, submits the canonical
/// <c>ForcedInclusion.consume</c> invocation, and confirms success through
/// <c>ForcedInclusion.isConsumed</c>. This makes retries safe after partial completion or an
/// ambiguous broadcast result.
/// </remarks>
public sealed class RpcForcedInclusionFinalizationClient
    : IForcedInclusionFinalizationClient
{
    private const int MaxTransactionProofDepth = 64;

    /// <summary>Transaction-submission seam used by wallet, HSM, and test integrations.</summary>
    public delegate ValueTask<RpcTransactionReceipt> SendInvocationAsync(
        ReadOnlyMemory<byte> script,
        CancellationToken cancellationToken);

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _settlementManagerHash;
    private readonly UInt160 _forcedInclusionHash;
    private readonly SendInvocationAsync _sendInvocation;

    /// <summary>Constructs a client backed by the canonical signed transaction sender.</summary>
    public RpcForcedInclusionFinalizationClient(
        JsonRpcClient rpc,
        RpcTransactionSender transactionSender,
        UInt160 settlementManagerHash,
        UInt160 forcedInclusionHash)
        : this(
            rpc,
            settlementManagerHash,
            forcedInclusionHash,
            transactionSender is null
                ? throw new ArgumentNullException(nameof(transactionSender))
                : new SendInvocationAsync(transactionSender.SendInvocationAsync))
    {
    }

    /// <summary>Constructs a client with an operator-supplied transaction submission boundary.</summary>
    public RpcForcedInclusionFinalizationClient(
        JsonRpcClient rpc,
        UInt160 settlementManagerHash,
        UInt160 forcedInclusionHash,
        SendInvocationAsync sendInvocation)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(settlementManagerHash);
        ArgumentNullException.ThrowIfNull(forcedInclusionHash);
        ArgumentNullException.ThrowIfNull(sendInvocation);
        if (settlementManagerHash.Equals(UInt160.Zero))
            throw new ArgumentException("SettlementManager hash must not be zero.", nameof(settlementManagerHash));
        if (forcedInclusionHash.Equals(UInt160.Zero))
            throw new ArgumentException("ForcedInclusion hash must not be zero.", nameof(forcedInclusionHash));

        _rpc = rpc;
        _settlementManagerHash = settlementManagerHash;
        _forcedInclusionHash = forcedInclusionHash;
        _sendInvocation = sendInvocation;
    }

    /// <inheritdoc />
    public async ValueTask ConsumeAndConfirmAsync(
        uint chainId,
        ulong batchNumber,
        IReadOnlyList<ForcedInclusionConsumptionProof> proofs,
        CancellationToken cancellationToken = default)
    {
        if (chainId == 0)
            throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
        if (batchNumber == 0)
            throw new ArgumentOutOfRangeException(nameof(batchNumber), "Batch number must be positive.");
        ArgumentNullException.ThrowIfNull(proofs);

        ValidateProofs(proofs);
        if (proofs.Count == 0) return;

        var finalizedRoot = await ReadFinalizedTransactionRootAsync(
            chainId,
            batchNumber,
            cancellationToken).ConfigureAwait(false);
        if (finalizedRoot.Equals(UInt256.Zero))
        {
            throw new InvalidOperationException(
                $"batch {chainId}/{batchNumber} is not finalized or has an empty transaction root");
        }

        foreach (var proof in proofs)
        {
            if (!ComputeRoot(proof).Equals(finalizedRoot))
            {
                throw new InvalidOperationException(
                    $"forced-inclusion proof for nonce {proof.Nonce} does not match finalized " +
                    $"transaction root {finalizedRoot}");
            }
        }

        foreach (var proof in proofs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsConsumedAsync(chainId, proof.Nonce, cancellationToken).ConfigureAwait(false))
                continue;

            var script = BuildConsumeScript(chainId, batchNumber, proof);
            try
            {
                var receipt = await _sendInvocation(script, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("transaction sender returned a null receipt");
                if (!string.Equals(receipt.VmState, "HALT", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"forced-inclusion consumption transaction {receipt.TransactionHash} " +
                        $"completed with VM state {receipt.VmState}");
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (await IsConsumedAsync(chainId, proof.Nonce, cancellationToken).ConfigureAwait(false))
                    continue;
                throw;
            }

            if (!await IsConsumedAsync(chainId, proof.Nonce, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException(
                    $"forced-inclusion nonce {proof.Nonce} was not consumed after confirmed transaction");
            }
        }
    }

    private async ValueTask<UInt256> ReadFinalizedTransactionRootAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken)
    {
        var result = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _settlementManagerHash,
            "getFinalizedTxRoot",
            [chainId, batchNumber],
            cancellationToken).ConfigureAwait(false);
        return RpcContractReader.ParseUInt256(result);
    }

    private async ValueTask<bool> IsConsumedAsync(
        uint chainId,
        ulong nonce,
        CancellationToken cancellationToken)
    {
        var result = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _forcedInclusionHash,
            "isConsumed",
            [chainId, nonce],
            cancellationToken).ConfigureAwait(false);
        return RpcContractReader.ParseBoolean(result);
    }

    private byte[] BuildConsumeScript(
        uint chainId,
        ulong batchNumber,
        ForcedInclusionConsumptionProof proof)
    {
        var siblingParameters = new List<ContractParameter>(proof.Siblings.Count);
        foreach (var sibling in proof.Siblings)
        {
            siblingParameters.Add(new ContractParameter(ContractParameterType.ByteArray)
            {
                Value = sibling.GetSpan().ToArray(),
            });
        }

        var siblings = new ContractParameter(ContractParameterType.Array)
        {
            Value = siblingParameters,
        };
        using var scriptBuilder = new ScriptBuilder();
        scriptBuilder.EmitDynamicCall(
            _forcedInclusionHash,
            "consume",
            CallFlags.All,
            chainId,
            batchNumber,
            proof.Nonce,
            siblings,
            (ulong)proof.LeafIndex);
        return scriptBuilder.ToArray();
    }

    private static UInt256 ComputeRoot(ForcedInclusionConsumptionProof proof)
    {
        var current = proof.TxHash;
        var index = (ulong)proof.LeafIndex;
        var pair = new byte[UInt256.Length * 2];
        foreach (var sibling in proof.Siblings)
        {
            if ((index & 1UL) == 0)
            {
                current.GetSpan().CopyTo(pair.AsSpan(0, UInt256.Length));
                sibling.GetSpan().CopyTo(pair.AsSpan(UInt256.Length, UInt256.Length));
            }
            else
            {
                sibling.GetSpan().CopyTo(pair.AsSpan(0, UInt256.Length));
                current.GetSpan().CopyTo(pair.AsSpan(UInt256.Length, UInt256.Length));
            }

            current = new UInt256(Crypto.Hash256(pair));
            index >>= 1;
        }

        if (index != 0)
            throw new ArgumentException("Leaf index is not canonical for the proof depth.", nameof(proof));
        return current;
    }

    private static void ValidateProofs(IReadOnlyList<ForcedInclusionConsumptionProof> proofs)
    {
        var nonces = new HashSet<ulong>();
        for (var index = 0; index < proofs.Count; index++)
        {
            var proof = proofs[index]
                ?? throw new ArgumentException($"proofs[{index}] is null", nameof(proofs));
            if (proof.Nonce == 0)
                throw new ArgumentException($"proofs[{index}].Nonce must be positive", nameof(proofs));
            if (!nonces.Add(proof.Nonce))
                throw new ArgumentException($"duplicate forced-inclusion nonce {proof.Nonce}", nameof(proofs));
            if (proof.TxHash is null || proof.TxHash.Equals(UInt256.Zero))
                throw new ArgumentException($"proofs[{index}].TxHash must not be zero", nameof(proofs));
            if (proof.Siblings is null)
                throw new ArgumentException($"proofs[{index}].Siblings is null", nameof(proofs));
            if (proof.Siblings.Count > MaxTransactionProofDepth)
            {
                throw new ArgumentException(
                    $"proofs[{index}] exceeds maximum depth {MaxTransactionProofDepth}",
                    nameof(proofs));
            }

            for (var siblingIndex = 0; siblingIndex < proof.Siblings.Count; siblingIndex++)
            {
                if (proof.Siblings[siblingIndex] is null)
                {
                    throw new ArgumentException(
                        $"proofs[{index}].Siblings[{siblingIndex}] is null",
                        nameof(proofs));
                }
            }

            _ = ComputeRoot(proof);
        }
    }
}
