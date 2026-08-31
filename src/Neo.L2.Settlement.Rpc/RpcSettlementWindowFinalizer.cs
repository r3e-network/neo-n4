using System.Numerics;
using Neo.Extensions.VM;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// L1 client that drives the permissionless <c>OptimisticChallenge.FinalizeIfPastWindow</c>
/// path for batches whose challenge window has expired.
/// </summary>
/// <remarks>
/// See doc.md §7.5 and §17. Expiry is decided from the on-chain deadline
/// (<c>OptimisticChallenge.getDeadline</c>, zero when no window is open) against the local
/// UTC clock; the contract re-asserts expiry on-chain, so clock skew at worst sends one
/// doomed invocation (caught in preflight by the sender) or delays finalization by the
/// skew. A window that disappears between the expiry read and the send means a concurrent
/// finalizer won the race; that outcome is reported as success and the caller re-reads the
/// batch status to learn the result.
/// </remarks>
public sealed class RpcSettlementWindowFinalizer : ISettlementWindowFinalizer
{
    /// <summary>Transaction-submission seam used by wallet, HSM, and test integrations.</summary>
    public delegate ValueTask<RpcTransactionReceipt> SendInvocationAsync(
        ReadOnlyMemory<byte> script,
        CancellationToken cancellationToken);

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _optimisticChallengeHash;
    private readonly SendInvocationAsync _sendInvocation;

    /// <summary>Constructs a client backed by the canonical signed transaction sender.</summary>
    public RpcSettlementWindowFinalizer(
        JsonRpcClient rpc,
        RpcTransactionSender transactionSender,
        UInt160 optimisticChallengeHash)
        : this(
            rpc,
            optimisticChallengeHash,
            transactionSender is null
                ? throw new ArgumentNullException(nameof(transactionSender))
                : new SendInvocationAsync(transactionSender.SendInvocationAsync))
    {
    }

    /// <summary>Constructs a client with an operator-supplied transaction submission boundary.</summary>
    public RpcSettlementWindowFinalizer(
        JsonRpcClient rpc,
        UInt160 optimisticChallengeHash,
        SendInvocationAsync sendInvocation)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(optimisticChallengeHash);
        ArgumentNullException.ThrowIfNull(sendInvocation);
        if (optimisticChallengeHash.Equals(UInt160.Zero))
            throw new ArgumentException(
                "OptimisticChallenge hash must not be zero.", nameof(optimisticChallengeHash));

        _rpc = rpc;
        _optimisticChallengeHash = optimisticChallengeHash;
        _sendInvocation = sendInvocation;
    }

    /// <inheritdoc />
    public async ValueTask<bool> IsWindowExpiredAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(chainId, batchNumber);
        var deadline = await ReadDeadlineAsync(chainId, batchNumber, cancellationToken)
            .ConfigureAwait(false);
        return deadline != 0 && CurrentUnixSeconds() > deadline;
    }

    /// <inheritdoc />
    public async ValueTask FinalizeIfPastWindowAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken = default)
    {
        ValidateBatch(chainId, batchNumber);
        var deadline = await ReadDeadlineAsync(chainId, batchNumber, cancellationToken)
            .ConfigureAwait(false);
        if (deadline == 0)
            return;
        if (CurrentUnixSeconds() <= deadline)
        {
            throw new InvalidOperationException(
                $"challenge window for batch {chainId}/{batchNumber} is still open; " +
                "FinalizeIfPastWindow would fault on-chain");
        }

        using var scriptBuilder = new ScriptBuilder();
        scriptBuilder.EmitDynamicCall(
            _optimisticChallengeHash,
            "finalizeIfPastWindow",
            CallFlags.All,
            chainId,
            batchNumber);
        try
        {
            var receipt = await _sendInvocation(scriptBuilder.ToArray(), cancellationToken)
                .ConfigureAwait(false)
                ?? throw new InvalidOperationException("transaction sender returned a null receipt");
            if (!string.Equals(receipt.VmState, "HALT", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"FinalizeIfPastWindow transaction {receipt.TransactionHash} " +
                    $"completed with VM state {receipt.VmState}");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (await ReadDeadlineAsync(chainId, batchNumber, cancellationToken)
                .ConfigureAwait(false) == 0)
            {
                // The window was consumed between the expiry read and the send: a
                // concurrent finalizer (or an accepted challenge) won the race. The
                // reconcile pass re-reads the batch status to observe the outcome.
                return;
            }
            throw;
        }
    }

    private async ValueTask<uint> ReadDeadlineAsync(
        uint chainId,
        ulong batchNumber,
        CancellationToken cancellationToken)
    {
        var result = await RpcContractReader.InvokeReadAsync(
            _rpc,
            _optimisticChallengeHash,
            "getDeadline",
            [chainId, batchNumber],
            cancellationToken).ConfigureAwait(false);
        var deadline = RpcContractReader.ParseBigInteger(result);
        if (deadline < BigInteger.Zero || deadline > uint.MaxValue)
        {
            throw new InvalidOperationException(
                $"OptimisticChallenge.getDeadline returned an out-of-range value {deadline}");
        }
        return (uint)deadline;
    }

    private static long CurrentUnixSeconds()
        => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static void ValidateBatch(uint chainId, ulong batchNumber)
    {
        if (chainId == 0)
            throw new ArgumentOutOfRangeException(nameof(chainId), "chainId 0 is reserved for L1.");
        if (batchNumber == 0)
            throw new ArgumentOutOfRangeException(nameof(batchNumber), "Batch number must be positive.");
    }
}
