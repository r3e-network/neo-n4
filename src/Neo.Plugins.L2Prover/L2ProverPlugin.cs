using Microsoft.Extensions.Configuration;
using Neo.L2;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Proving.RiscVZk;

namespace Neo.Plugins.L2;

/// <summary>
/// Hosts the <see cref="IL2Prover"/> implementation that <c>L2SettlementPlugin</c>
/// pulls proofs from. Selection is driven by chain config; multisig (Stage 0) is the default.
/// See doc.md §7.5 (ProverAdapter stages).
/// </summary>
public sealed class L2ProverPlugin : Plugin
{
    private ProofType _kind = ProofType.Multisig;
    private IL2Prover? _prover;

    /// <inheritdoc />
    public override string Name => "L2ProverPlugin";

    /// <inheritdoc />
    public override string Description => "Generates batch proofs for the configured ProofType (Stage 0/1/2).";

    /// <summary>The active prover (null until <see cref="Wire"/> has been called).</summary>
    public IL2Prover? Prover => _prover;

    /// <summary>The proof type this prover produces.</summary>
    public ProofType Kind => _kind;

    /// <summary>
    /// Wire the plugin with stage-specific dependencies. Multisig wants an <see cref="ISignerSet"/>;
    /// Optimistic wants a sequencer key + bond reference (handled in L2SettlementPlugin); Zk
    /// wants a RISC-V backend (currently <see cref="MockRiscVProver"/> only).
    /// </summary>
    public void Wire(ISignerSet? signerSet = null, MockRiscVProver? riscVProver = null)
    {
        _prover = _kind switch
        {
            ProofType.Multisig => new AttestationProver(signerSet ?? throw new InvalidOperationException("ProofType.Multisig requires a signer set")),
            ProofType.Zk => riscVProver ?? throw new InvalidOperationException("ProofType.Zk requires a RiscV prover"),
            ProofType.Optimistic => throw new NotSupportedException("Optimistic prover lives in L2SettlementPlugin (it just signs)."),
            _ => throw new NotSupportedException($"Unknown ProofType {_kind}"),
        };
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        _kind = (ProofType)section.GetValue<byte>("ProofType", (byte)ProofType.Multisig);
    }
}
