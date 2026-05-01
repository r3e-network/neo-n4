using Neo.L2.Telemetry;

namespace Neo.L2.Bridge;

/// <summary>
/// Consumes inbound L1 deposit messages and translates them to L2-side mint operations.
/// </summary>
/// <remarks>
/// See doc.md §15.2 (deposit flow). The actual mint happens in the on-L2 native bridge contract;
/// this class is the orchestration shim that decodes the payload, validates the canonical asset,
/// and hands off to the contract.
/// </remarks>
public sealed class DepositProcessor
{
    private readonly AssetRegistry _registry;
    private IL2Metrics _metrics;
    private readonly HashSet<(uint, ulong)> _consumed = new();
    private readonly Lock _gate = new();

    /// <summary>Identifier of the L2 chain this processor runs on.</summary>
    public uint LocalChainId { get; }

    /// <summary>Construct with the chain identifier and shared asset registry.</summary>
    public DepositProcessor(uint localChainId, AssetRegistry registry, IL2Metrics? metrics = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        LocalChainId = localChainId;
        _registry = registry;
        _metrics = metrics ?? NoOpMetrics.Instance;
    }

    /// <summary>Swap the metrics sink in-place. Preserves consumed-nonce state, unlike re-constructing.</summary>
    public void WithMetrics(IL2Metrics metrics)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        _metrics = metrics;
    }

    /// <summary>
    /// Validate <paramref name="message"/> and produce a <see cref="MintInstruction"/> ready
    /// for the L2 bridge contract to execute. Throws on replay or unknown asset.
    /// </summary>
    public MintInstruction Process(CrossChainMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            if (message.MessageType != MessageType.Deposit)
                throw new ArgumentException($"Expected MessageType.Deposit, got {message.MessageType}", nameof(message));
            if (message.TargetChainId != LocalChainId)
                throw new ArgumentException($"Message targets chain {message.TargetChainId}, but local is {LocalChainId}", nameof(message));

            // Decode + validate BEFORE marking the nonce consumed. Otherwise a transient
            // validation failure (e.g. asset not yet registered, decode error) permanently
            // locks the (source, nonce) pair — when the asset is later registered, a retry
            // would fail with "already processed" instead of succeeding. Replay protection
            // only needs to cover the success path.
            var payload = DepositPayload.Decode(message.Payload.Span);

            if (!_registry.TryGetByL1(payload.L1Asset, LocalChainId, out var mapping) || mapping is null)
                throw new InvalidOperationException($"No L2 asset mapping for L1 asset {payload.L1Asset}");
            if (!mapping.Active)
                throw new InvalidOperationException($"Asset mapping for {payload.L1Asset} is inactive");

            // Atomic claim: if another thread already processed this nonce while we were
            // validating, lose the race and throw. The successful thread still emits the
            // mint instruction; the loser sees "already processed" and gives up.
            lock (_gate)
            {
                if (!_consumed.Add((message.SourceChainId, message.Nonce)))
                    throw new InvalidOperationException(
                        $"Deposit ({message.SourceChainId},{message.Nonce}) was already processed");
            }

            var instr = new MintInstruction
            {
                L2Asset = mapping.L2Asset,
                Recipient = payload.L2Recipient,
                Amount = payload.Amount,
                SourceChainId = message.SourceChainId,
                SourceNonce = message.Nonce,
            };
            _metrics.IncrementCounter(MetricNames.DepositsProcessed);
            return instr;
        }
        catch
        {
            _metrics.IncrementCounter(MetricNames.DepositsRejected);
            throw;
        }
    }

    /// <summary>True if a prior call to <see cref="Process"/> consumed this (source, nonce).</summary>
    public bool HasConsumed(uint sourceChainId, ulong nonce)
    {
        lock (_gate)
            return _consumed.Contains((sourceChainId, nonce));
    }
}

/// <summary>
/// What the bridge contract should do as a result of a single deposit message.
/// </summary>
public sealed record MintInstruction
{
    /// <summary>L2 asset to mint.</summary>
    public required UInt160 L2Asset { get; init; }

    /// <summary>L2 address that should hold the newly minted amount.</summary>
    public required UInt160 Recipient { get; init; }

    /// <summary>Smallest-unit amount to mint.</summary>
    public required System.Numerics.BigInteger Amount { get; init; }

    /// <summary>Source chain identifier (used for audit + replay protection on the contract side).</summary>
    public required uint SourceChainId { get; init; }

    /// <summary>Per-source nonce (used for audit + replay protection).</summary>
    public required ulong SourceNonce { get; init; }
}
