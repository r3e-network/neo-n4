using System;
using Neo.Extensions.VM;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Settlement.Rpc;

/// <summary>RPC client for the complete optimistic challenge lifecycle.</summary>
/// <remarks>
/// See doc.md §17. Reads the on-chain deadline before finalization, submits the exact fraud-proof
/// bytes through <c>OptimisticChallenge.challenge</c>, and treats an already-consumed window as an
/// idempotent race outcome. No local clock or local proof result is treated as authoritative.
/// </remarks>
public sealed class RpcOptimisticChallengeClient : Neo.L2.IOptimisticChallengeClient
{
    public delegate ValueTask<RpcTransactionReceipt> SendInvocationAsync(
        ReadOnlyMemory<byte> script, CancellationToken cancellationToken);

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _challengeHash;
    private readonly SendInvocationAsync _send;

    public RpcOptimisticChallengeClient(
        JsonRpcClient rpc,
        UInt160 challengeHash,
        SendInvocationAsync send)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(challengeHash);
        ArgumentNullException.ThrowIfNull(send);
        if (challengeHash.Equals(UInt160.Zero))
            throw new ArgumentException("challenge contract hash must be non-zero", nameof(challengeHash));
        _rpc = rpc;
        _challengeHash = challengeHash;
        _send = send;
    }

    public async ValueTask<uint> GetChallengeDeadlineAsync(
        uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
    {
        ValidateBatch(chainId, batchNumber);
        var result = await RpcContractReader.InvokeReadAsync(
            _rpc, _challengeHash, "getDeadline", [chainId, batchNumber], cancellationToken)
            .ConfigureAwait(false);
        return checked((uint)RpcContractReader.ParseUInt64(result));
    }

    public async ValueTask SubmitChallengeAsync(
        uint chainId,
        ulong batchNumber,
        UInt160 challenger,
        ReadOnlyMemory<byte> fraudProofBytes,
        UInt160 fraudVerifier,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(chainId, batchNumber);
        ArgumentNullException.ThrowIfNull(challenger);
        ArgumentNullException.ThrowIfNull(fraudVerifier);
        if (challenger.Equals(UInt160.Zero))
            throw new ArgumentException("challenger must be non-zero", nameof(challenger));
        if (fraudVerifier.Equals(UInt160.Zero))
            throw new ArgumentException("fraud verifier must be non-zero", nameof(fraudVerifier));
        if (fraudProofBytes.IsEmpty)
            throw new ArgumentException("fraud proof must be non-empty", nameof(fraudProofBytes));

        using var builder = new ScriptBuilder();
        builder.EmitDynamicCall(
            _challengeHash,
            "challenge",
            CallFlags.All,
            chainId,
            batchNumber,
            challenger,
            new ContractParameter(ContractParameterType.ByteArray) { Value = fraudProofBytes.ToArray() },
            fraudVerifier);
        var receipt = await _send(builder.ToArray(), cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("transaction sender returned a null receipt");
        if (!string.Equals(receipt.VmState, "HALT", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"challenge transaction {receipt.TransactionHash} faulted: {receipt.Exception ?? "unknown VM fault"}");
    }

    public async ValueTask FinalizeIfPastWindowAsync(
        uint chainId, ulong batchNumber, CancellationToken cancellationToken = default)
    {
        ValidateBatch(chainId, batchNumber);
        var deadline = await GetChallengeDeadlineAsync(chainId, batchNumber, cancellationToken)
            .ConfigureAwait(false);
        if (deadline == 0) return;

        var script = BuildFinalizeScript(chainId, batchNumber);
        try
        {
            var receipt = await _send(script, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("transaction sender returned a null receipt");
            if (!string.Equals(receipt.VmState, "HALT", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"finalization transaction {receipt.TransactionHash} faulted: {receipt.Exception ?? "unknown VM fault"}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A concurrent challenge/finalizer consumes the same window. Re-read the authoritative
            // key: zero means the operation already completed, so retry is idempotently successful.
            if (await GetChallengeDeadlineAsync(chainId, batchNumber, cancellationToken)
                .ConfigureAwait(false) == 0)
                return;
            throw;
        }
    }

    private byte[] BuildFinalizeScript(uint chainId, ulong batchNumber)
    {
        using var builder = new ScriptBuilder();
        builder.EmitDynamicCall(_challengeHash, "finalizeIfPastWindow", CallFlags.All, chainId, batchNumber);
        return builder.ToArray();
    }

    private static void ValidateBatch(uint chainId, ulong batchNumber)
    {
        if (chainId == 0) throw new ArgumentOutOfRangeException(nameof(chainId));
        if (batchNumber == 0) throw new ArgumentOutOfRangeException(nameof(batchNumber));
    }
}
