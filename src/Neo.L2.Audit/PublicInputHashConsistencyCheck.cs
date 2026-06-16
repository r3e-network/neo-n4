using Neo.L2.State;

namespace Neo.L2.Audit;

/// <summary>
/// Verifies that each batch's stored <see cref="L2BatchCommitment.PublicInputHash"/>
/// matches the hash of public inputs reconstructed from the commitment's own fields.
/// </summary>
/// <remarks>
/// <para>
/// Scope (default ctor): the reconstruction uses the same commitment fields and the same
/// <see cref="StateRootCalculator.HashPublicInputs"/> function that the settlement plugin's
/// <c>BuildPublicInputs</c> uses to produce <c>PublicInputHash</c>, so this check is the
/// inverse of the producer. It therefore catches only inconsistency / drift between a stored
/// commitment's fields and its stored <c>PublicInputHash</c> (e.g. partial corruption where one
/// was mutated without the other). It does NOT detect deliberate tampering — an attacker who
/// edits a commitment field can recompute a matching hash with the same public function and pass
/// — and it does NOT bind the proof's embedded inputs (wrong-proof binding is
/// <see cref="ProofValidityCheck"/>'s responsibility). To turn this into a tamper/wrong-proof
/// check it must be driven from an independent source of public inputs via the resolver below.
/// </para>
/// <para>
/// The commitment alone doesn't carry every <see cref="PublicInputs"/> field
/// (specifically <c>L1MessageHash</c> and <c>BlockContextHash</c>). The default
/// reconstruction matches the current Phase 0–3 settlement plugin's
/// <c>BuildPublicInputs</c> by zero-filling those fields. When future phases populate
/// them, callers can pass an optional <c>publicInputsResolver</c> to the constructor —
/// same shape as <see cref="ProofValidityCheck"/>'s resolver — to look up the actual
/// values from a side store / replay path, which also makes the check independent of the
/// producer rather than a self-inverse.
/// </para>
/// </remarks>
public sealed class PublicInputHashConsistencyCheck : IAuditCheck
{
    private readonly Func<L2BatchCommitment, PublicInputs>? _resolver;

    /// <summary>Default ctor — assumes <c>L1MessageHash</c> and <c>BlockContextHash</c>
    /// are zero (Phase 0–3 settlement convention).</summary>
    public PublicInputHashConsistencyCheck() : this(resolver: null) { }

    /// <summary>Construct with an explicit public-inputs resolver. Pass when later phases
    /// fill in <c>L1MessageHash</c> / <c>BlockContextHash</c> off-chain.</summary>
    public PublicInputHashConsistencyCheck(Func<L2BatchCommitment, PublicInputs>? resolver)
    {
        _resolver = resolver;
    }

    /// <inheritdoc />
    public string Name => "public_input_hash";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<AuditFinding>> RunAsync(
        IReadOnlyList<L2BatchCommitment> batches,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batches);

        var findings = new List<AuditFinding>();
        var failed = 0;

        foreach (var batch in batches)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var inputs = _resolver is null
                ? DefaultReconstruct(batch)
                : _resolver(batch)
                    ?? throw new InvalidOperationException(
                        $"publicInputsResolver returned null for batch {batch.BatchNumber}");
            var expected = StateRootCalculator.HashPublicInputs(inputs);

            if (!batch.PublicInputHash.Equals(expected))
            {
                findings.Add(new AuditFinding
                {
                    Check = Name,
                    Passed = false,
                    BatchNumber = batch.BatchNumber,
                    Detail = $"batch {batch.BatchNumber} PublicInputHash mismatch: stored {Truncate(batch.PublicInputHash)}, expected {Truncate(expected)}",
                });
                failed++;
            }
        }

        if (failed == 0)
        {
            findings.Add(new AuditFinding
            {
                Check = Name,
                Passed = true,
                BatchNumber = 0,
                Detail = $"all {batches.Count} batches have consistent PublicInputHash",
            });
        }
        return new ValueTask<IReadOnlyList<AuditFinding>>(findings);
    }

    private static PublicInputs DefaultReconstruct(L2BatchCommitment batch) => new()
    {
        ChainId = batch.ChainId,
        BatchNumber = batch.BatchNumber,
        PreStateRoot = batch.PreStateRoot,
        PostStateRoot = batch.PostStateRoot,
        TxRoot = batch.TxRoot,
        ReceiptRoot = batch.ReceiptRoot,
        WithdrawalRoot = batch.WithdrawalRoot,
        L2ToL1MessageRoot = batch.L2ToL1MessageRoot,
        L2ToL2MessageRoot = batch.L2ToL2MessageRoot,
        L1MessageHash = UInt256.Zero,
        DACommitment = batch.DACommitment,
        BlockContextHash = UInt256.Zero,
    };

    private static string Truncate(UInt256 root) => AuditFormatting.Truncate(root);
}
