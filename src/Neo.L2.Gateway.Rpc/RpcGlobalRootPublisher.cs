using Neo.L2.Settlement.Rpc;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc;

/// <summary>
/// Production <see cref="IGlobalRootPublisher"/> that calls
/// <c>NeoHub.MessageRouter.publishGlobalRoot</c> on L1 over JSON-RPC, signing the L1 transaction
/// through a caller-supplied delegate. Mirrors <see cref="RpcSettlementClient"/>'s
/// sign-and-send pattern: this library avoids forcing a particular signing dependency on consumers,
/// so the wallet / signer is injected.
/// </summary>
/// <remarks>
/// <para>The on-chain <c>PublishGlobalRoot</c> (Phase-5 proof-gated) takes four arguments —
/// <c>(batchEpoch, globalRoot, verificationKeyId, aggregatedProof)</c>. The signer MUST forward all
/// four as the contract-call arguments, with the global root and verification key id exactly as
/// supplied here (the contract hashes / dispatches on them verbatim). The settlement-manager
/// witness authorizing the publish must be the transaction signer's account.</para>
/// <para>The signature is settled at the gateway plugin layer — <c>L2GatewayPlugin.PublishAggregateAsync</c>
/// produces the <see cref="AggregatedCommitment"/> and forwards it here, where the proof bytes /
/// global root are the proof-gate inputs MessageRouter verifies against the wired Groth16Verifier.</para>
/// </remarks>
public sealed class RpcGlobalRootPublisher : IGlobalRootPublisher, IDisposable
{
    /// <summary>Delegate for signing + sending the <c>PublishGlobalRoot</c> transaction.</summary>
    /// <param name="messageRouterHash">The deployed NeoHub.MessageRouter contract hash.</param>
    /// <param name="batchEpoch">Operator-defined epoch number (forwarded as-is).</param>
    /// <param name="globalRoot">32-byte global message root (the proof's single public input).</param>
    /// <param name="verificationKeyId">32-byte governance-registered Groth16 VK id.</param>
    /// <param name="aggregatedProof">Opaque aggregated-proof bytes the on-chain verifier checks.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>Tx hash returned by <c>sendrawtransaction</c>, or <see cref="UInt256.Zero"/> for a
    /// benign already-published retry (the on-chain publish-once-per-epoch guard surfaced as a
    /// benign result rather than a hard fault).</returns>
    public delegate ValueTask<UInt256> SignAndSendAsync(
        UInt160 messageRouterHash,
        ulong batchEpoch,
        UInt256 globalRoot,
        ReadOnlyMemory<byte> verificationKeyId,
        ReadOnlyMemory<byte> aggregatedProof,
        CancellationToken cancellationToken);

    private readonly UInt160 _messageRouterHash;
    private readonly SignAndSendAsync _signAndSend;
    private bool _disposed;

    /// <summary>Construct. The <paramref name="signAndSend"/> delegate owns the L1 wallet /
    /// signer; this class is transport-agnostic so consumers plug in Neo SDK, a hardware wallet,
    /// an HSM, etc.</summary>
    public RpcGlobalRootPublisher(UInt160 messageRouterHash, SignAndSendAsync signAndSend)
    {
        ArgumentNullException.ThrowIfNull(signAndSend);
        if (messageRouterHash.Equals(UInt160.Zero))
            throw new ArgumentException("messageRouterHash must be a non-zero UInt160", nameof(messageRouterHash));
        _messageRouterHash = messageRouterHash;
        _signAndSend = signAndSend;
    }

    /// <inheritdoc />
    public async ValueTask<UInt256> PublishGlobalRootAsync(
        ulong batchEpoch,
        AggregatedCommitment commitment,
        ReadOnlyMemory<byte> verificationKeyId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commitment);
        if (verificationKeyId.Length != 32)
            throw new ArgumentException("verificationKeyId must be 32 bytes", nameof(verificationKeyId));

        // Surface an empty-proof at the boundary — the on-chain contract rejects it anyway, but
        // raising it here (before the L1 round-trip) gives a clearer error to the operator.
        if (commitment.AggregatedProof.Length == 0)
            throw new ArgumentException(
                "commitment.AggregatedProof is empty — production MessageRouter (verifier wired) rejects empty proofs",
                nameof(commitment));

        // Defensive: a null-returning signAndSend would propagate as a NRE further downstream
        // (e.g. an L1-tracker that dereferences the tx hash). Same iter-171/172/173 callee-contract
        // pattern as RpcSettlementClient.
        var txHash = await _signAndSend(
            _messageRouterHash,
            batchEpoch,
            commitment.GlobalMessageRoot,
            verificationKeyId,
            commitment.AggregatedProof,
            cancellationToken).ConfigureAwait(false);
        return txHash;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // No JsonRpcClient held here — the signAndSend delegate owns transport if it needs one.
        // Dispose is implemented for IGlobalRootPublisher symmetry + future fields that may hold
        // resources.
        GC.SuppressFinalize(this);
    }
}
