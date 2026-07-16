using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.State;
using Neo.SmartContract;
using Neo.Wallets;

namespace Neo.L2.Proving.Optimistic;

/// <summary>
/// Stage 1 prover — signs the canonical public-input encoding with the sequencer key and
/// packages an <see cref="OptimisticProofPayload"/> (bond reference + claim signature).
/// </summary>
/// <remarks>
/// See doc.md §7.5 (Stage 1). Challenge-window enforcement remains on
/// <c>NeoHub.SettlementManager</c>; this prover only produces a well-formed optimistic claim.
/// Production hosts should keep the sequencer private key in HSM/KMS and pass a short-lived
/// <see cref="KeyPair"/> copy only when proving. Pair with <see cref="OptimisticVerifier"/>.
/// </remarks>
public sealed class OptimisticProver : IL2Prover
{
    private readonly KeyPair _sequencerKey;
    private readonly UInt160 _sequencerAccount;
    private readonly UInt160 _bondContract;
    private readonly UInt256 _bondTxHash;
    private readonly Func<ulong> _submittedAtUnixMs;

    /// <inheritdoc />
    public ProofType Kind => ProofType.Optimistic;

    /// <summary>
    /// Construct with the sequencer key and the L1 bond that backs optimistic claims.
    /// </summary>
    /// <param name="sequencerKey">Sequencer secp256r1 key (copied into an owned <see cref="KeyPair"/>).</param>
    /// <param name="bondContract">L1 bond contract hash recorded in the proof payload.</param>
    /// <param name="bondTxHash">L1 transaction that posted the bond (non-zero).</param>
    /// <param name="submittedAtUnixMs">
    /// Optional wall-clock source (milliseconds). Defaults to UTC now so challenge windows
    /// advance without host clocks injected at prove time.
    /// </param>
    public OptimisticProver(
        KeyPair sequencerKey,
        UInt160 bondContract,
        UInt256 bondTxHash,
        Func<ulong>? submittedAtUnixMs = null)
    {
        ArgumentNullException.ThrowIfNull(sequencerKey);
        ArgumentNullException.ThrowIfNull(bondContract);
        ArgumentNullException.ThrowIfNull(bondTxHash);
        if (bondContract.Equals(UInt160.Zero))
            throw new ArgumentException("bondContract must be non-zero", nameof(bondContract));
        if (bondTxHash.Equals(UInt256.Zero))
            throw new ArgumentException("bondTxHash must be non-zero", nameof(bondTxHash));

        // Own a copy so callers can clear temporary material after construction.
        _sequencerKey = new KeyPair(sequencerKey.PrivateKey.ToArray());
        _sequencerAccount = Contract.CreateSignatureRedeemScript(_sequencerKey.PublicKey).ToScriptHash();
        if (_sequencerAccount.Equals(UInt160.Zero))
            throw new ArgumentException("sequencer key derives a zero account", nameof(sequencerKey));
        _bondContract = bondContract;
        _bondTxHash = bondTxHash;
        _submittedAtUnixMs = submittedAtUnixMs
            ?? (() => (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
    }

    /// <summary>Sequencer script hash embedded in every proof (slash target).</summary>
    public UInt160 SequencerAccount => _sequencerAccount;

    /// <inheritdoc />
    public ValueTask<ProofResult> ProveAsync(
        ProofRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(request);
        if (request.Kind != ProofType.Optimistic)
            throw new ArgumentException(
                $"OptimisticProver expects ProofType.Optimistic, got {request.Kind}",
                nameof(request));
        ArgumentNullException.ThrowIfNull(request.PublicInputs);

        var canonicalBytes = BatchSerializer.EncodePublicInputs(request.PublicInputs);
        var publicInputHash = StateRootCalculator.HashPublicInputs(request.PublicInputs);
        var signature = Crypto.Sign(canonicalBytes, _sequencerKey);
        if (signature is null || signature.Length != 64)
            throw new InvalidOperationException(
                $"sequencer signature length {signature?.Length ?? 0} != 64");

        var payload = new OptimisticProofPayload
        {
            BondContract = _bondContract,
            BondTxHash = _bondTxHash,
            SubmittedAt = _submittedAtUnixMs(),
            Sequencer = _sequencerAccount,
            SequencerSignature = signature,
        };

        return new ValueTask<ProofResult>(new ProofResult
        {
            Proof = payload.Encode(),
            Kind = ProofType.Optimistic,
            PublicInputHash = publicInputHash,
        });
    }
}
