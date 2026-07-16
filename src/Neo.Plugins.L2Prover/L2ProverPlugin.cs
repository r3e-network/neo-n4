using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Neo.L2;
using Neo.L2.Proving;
using Neo.L2.Proving.Attestation;
using Neo.L2.Proving.Optimistic;
using Neo.L2.Proving.RiscVZk;

namespace Neo.Plugins.L2;

/// <summary>
/// Hosts the <see cref="IL2Prover"/> implementation that <c>L2SettlementPlugin</c>
/// pulls proofs from. Selection is driven by chain config; multisig (Stage 0) is the default.
/// See doc.md §7.5 (ProverAdapter stages).
/// </summary>
public sealed class L2ProverPlugin : Plugin
{
    /// <summary>
    /// Relative path of the prover plugin config under a chain working directory
    /// (written by <c>init-l2 --from-deploy-report</c> / deploy-report materialization).
    /// </summary>
    public const string RelativePluginConfigPath =
        "Plugins/Neo.Plugins.L2Prover/config.json";

    private ProofType _kind = ProofType.Multisig;
    private IL2Prover? _prover;

    /// <inheritdoc />
    public override string Name => "L2ProverPlugin";

    /// <inheritdoc />
    public override string Description => "Generates batch proofs for the configured ProofType (Stage 0/1/2).";

    /// <summary>The active prover (null until <see cref="Wire"/> has been called).</summary>
    public IL2Prover? Prover => _prover;

    /// <summary>The proof type this prover produces. Set by <see cref="Configure"/> from the
    /// plugin config; can be overridden in test code that bypasses Configure.</summary>
    public ProofType Kind
    {
        get => _kind;
        set => _kind = value;
    }

    /// <summary>
    /// Host composition factory: preload <see cref="Kind"/> from a chain working directory
    /// without the Neo plugin config loader.
    /// </summary>
    /// <remarks>
    /// Reads <c>Plugins/Neo.Plugins.L2Prover/config.json</c> (or node/batcher-node variants)
    /// written by deploy-report materialization. Call <see cref="Wire"/> afterward with the
    /// stage-specific dependency (signer set / OptimisticProver / Sp1BatchProofProver).
    /// Multisig hosts may use <see cref="CreateMultisigWiredFromChainDirectory"/> to bind
    /// <see cref="AttestationProver"/> in one call.
    /// </remarks>
    public static L2ProverPlugin CreateFromChainDirectory(string chainDirectory)
    {
        var kind = ReadProofTypeFromChainDirectory(chainDirectory);
        return new L2ProverPlugin { Kind = kind };
    }

    /// <summary>
    /// Host composition: load Multisig proof type from the chain directory and wire
    /// <see cref="AttestationProver"/> over the operator-supplied <see cref="ISignerSet"/>.
    /// </summary>
    /// <remarks>
    /// Fails closed when the chain directory configures a non-Multisig <c>ProofType</c>
    /// (Optimistic/Zk need their own stage dependency). Production uses HSM/KMS
    /// <see cref="ISignerSet"/>; <see cref="InMemorySignerSet"/> is tests/devnet only.
    /// Pass <see cref="Prover"/> into <c>WireProductionFromLayout</c> after this call.
    /// </remarks>
    public static L2ProverPlugin CreateMultisigWiredFromChainDirectory(
        string chainDirectory,
        ISignerSet signers)
    {
        ArgumentNullException.ThrowIfNull(signers);
        var plugin = CreateFromChainDirectory(chainDirectory);
        if (plugin.Kind != ProofType.Multisig)
        {
            throw new InvalidOperationException(
                $"CreateMultisigWiredFromChainDirectory requires ProofType.Multisig; "
                + $"chain directory configures {plugin.Kind}");
        }
        plugin.Wire(signerSet: signers);
        return plugin;
    }

    /// <summary>
    /// Host composition: load Zk proof type from the chain directory and wire
    /// <see cref="Sp1BatchProofProver.OpenFromChainDirectory"/> for the batch SP1 daemon queue.
    /// </summary>
    /// <remarks>
    /// Fails closed when the chain directory configures a non-Zk <c>ProofType</c>.
    /// The reviewed <c>prove-batch</c> daemon must watch <c>prover/inbox</c>; the
    /// verification key remains host-supplied (funded release pin). Pass
    /// <see cref="Prover"/> into <c>WireProductionFromLayout</c> or use
    /// <c>Sp1SettlementExecutionStack</c> for the full executor+prover+profile bind.
    /// </remarks>
    public static L2ProverPlugin CreateZkWiredFromChainDirectory(
        string chainDirectory,
        UInt256 verificationKeyId,
        TimeSpan? resultTimeout = null,
        TimeSpan? pollInterval = null)
    {
        ArgumentNullException.ThrowIfNull(verificationKeyId);
        var plugin = CreateFromChainDirectory(chainDirectory);
        if (plugin.Kind != ProofType.Zk)
        {
            throw new InvalidOperationException(
                $"CreateZkWiredFromChainDirectory requires ProofType.Zk; "
                + $"chain directory configures {plugin.Kind}");
        }
        var zkProver = Sp1BatchProofProver.OpenFromChainDirectory(
            chainDirectory,
            verificationKeyId,
            resultTimeout,
            pollInterval);
        plugin.Wire(zkProver: zkProver);
        return plugin;
    }

    /// <summary>
    /// Load <c>ProofType</c> from a prover plugin config under a chain directory.
    /// </summary>
    public static ProofType ReadProofTypeFromChainDirectory(string chainDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chainDirectory);
        var root = System.IO.Path.GetFullPath(chainDirectory);
        var candidates = new[]
        {
            System.IO.Path.Combine(root, "Plugins", "Neo.Plugins.L2Prover", "config.json"),
            System.IO.Path.Combine(root, "node", "Plugins", "Neo.Plugins.L2Prover", "config.json"),
            System.IO.Path.Combine(root, "batcher-node", "Plugins", "Neo.Plugins.L2Prover", "config.json"),
        };
        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return ReadProofTypeFromPluginConfigFile(candidate);
        }
        throw new FileNotFoundException(
            "prover plugin config not found under chain directory "
            + $"(expected {RelativePluginConfigPath} or node/batcher-node variants)",
            System.IO.Path.Combine(root, RelativePluginConfigPath));
    }

    /// <summary>Load <c>ProofType</c> from a prover plugin <c>config.json</c> file.</summary>
    public static ProofType ReadProofTypeFromPluginConfigFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("prover plugin config not found", fullPath);

        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        if (!document.RootElement.TryGetProperty("PluginConfiguration", out var plugin)
            || plugin.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException(
                $"{fullPath} is missing a PluginConfiguration object");
        if (!plugin.TryGetProperty("ProofType", out var proofTypeEl))
            throw new InvalidDataException($"{fullPath} is missing PluginConfiguration.ProofType");

        byte raw = proofTypeEl.ValueKind switch
        {
            JsonValueKind.Number when proofTypeEl.TryGetByte(out var b) => b,
            JsonValueKind.String when byte.TryParse(proofTypeEl.GetString(), out var parsed) => parsed,
            _ => throw new InvalidDataException(
                $"{fullPath} ProofType must be a byte (0..3)"),
        };
        return ProofTypeExtensions.Resolve(raw);
    }

    /// <summary>
    /// Wire the plugin with stage-specific dependencies. Multisig wants an <see cref="ISignerSet"/>;
    /// Optimistic wants an <see cref="OptimisticProver"/> (sequencer key + bond reference);
    /// Zk wants an explicit <see cref="IZkExecutionProver"/> implementation.
    /// </summary>
    /// <remarks>
    /// Production Stage-2 chains inject <see cref="Sp1BatchProofProver"/>, which exchanges
    /// immutable content-addressed artifacts with the isolated <c>prove-batch daemon</c> at
    /// <c>bridge/neo-zkvm-host/</c>. Tests may inject <see cref="MockRiscVProver"/>; the
    /// settlement pipeline rejects its non-cryptographic metadata in production ZK profiles.
    /// Optimistic hosts construct <see cref="OptimisticProver"/> with the sequencer key and
    /// L1 bond tx reference, then pass it here or directly to
    /// <c>L2SettlementPlugin.WireProduction</c> / <c>WireProductionFromLayout</c>.
    /// </remarks>
    public void Wire(
        ISignerSet? signerSet = null,
        IZkExecutionProver? zkProver = null,
        OptimisticProver? optimisticProver = null)
    {
        _prover = _kind switch
        {
            ProofType.Multisig => new AttestationProver(signerSet ?? throw new InvalidOperationException("ProofType.Multisig requires a signer set")),
            ProofType.Zk => zkProver ?? throw new InvalidOperationException(
                "ProofType.Zk requires an IZkExecutionProver; use Sp1BatchProofProver with the isolated `prove-batch daemon` in production."),
            ProofType.Optimistic => optimisticProver ?? throw new InvalidOperationException(
                "ProofType.Optimistic requires an OptimisticProver (sequencer key + non-zero bondContract/bondTxHash)"),
            // ProofType.None is legal in the wire format (genesis / operator-trusted flows)
            // but the prover plugin can't produce a proof for it. Without this explicit case
            // the operator sees "Unknown ProofType None" — misleading, since None is defined.
            ProofType.None => throw new NotSupportedException(
                "ProofType.None has no prover (used only for genesis or operator-trusted flows; configure Multisig/Optimistic/Zk to enable settlement)"),
            _ => throw new NotSupportedException($"Unknown ProofType {_kind}"),
        };
    }

    /// <inheritdoc />
    protected override void Configure()
    {
        var section = GetConfiguration();
        if (section is null) return;
        _kind = ProofTypeExtensions.Resolve(section.GetValue<byte>("ProofType", (byte)ProofType.Multisig));
    }
}
