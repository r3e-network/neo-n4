using Neo.Cryptography.ECC;

namespace Neo.L2.Sequencer;

/// <summary>
/// L2-side view of <c>NeoHub.SequencerRegistry</c>. The L2 node's dBFT consensus consults this
/// before each block to know who is currently allowed to produce / sign. See doc.md §7.1.
/// </summary>
/// <remarks>
/// Production wires this to an L1-RPC-backed implementation that polls
/// <c>SequencerRegistry.GetActiveCount</c> + <c>GetStatus</c> + <c>GetSequencerAddress</c> per
/// chain. The in-memory variant lives here for tests and devnet boot.
/// </remarks>
public interface ISequencerCommitteeProvider
{
    /// <summary>L2 chain identifier this provider watches.</summary>
    uint ChainId { get; }

    /// <summary>Snapshot the current committee. Excludes any members past their exit window.</summary>
    ValueTask<IReadOnlyList<CommitteeMember>> GetActiveCommitteeAsync(CancellationToken cancellationToken = default);

    /// <summary>Maximum committee size advertised by NeoHub.</summary>
    ValueTask<int> GetMaxCommitteeSizeAsync(CancellationToken cancellationToken = default);

    /// <summary>True if a key is currently registered (any status, including Exiting).</summary>
    ValueTask<bool> IsRegisteredAsync(ECPoint sequencerKey, CancellationToken cancellationToken = default);
}

/// <summary>One sequencer entry in the committee.</summary>
public sealed record CommitteeMember
{
    /// <summary>The sequencer's secp256r1 pubkey (matches the on-chain registration).</summary>
    public required ECPoint PublicKey { get; init; }

    /// <summary>L1 address tied to the pubkey (for slashing payouts and audit).</summary>
    public required UInt160 L1Address { get; init; }

    /// <summary>Status byte from <c>SequencerRegistry</c> — 1=Active, 2=Exiting.</summary>
    public required byte Status { get; init; }

    /// <summary>Unix timestamp at which an exiting sequencer's window completes (0 when Active).</summary>
    public required uint ExitsAtUnixSeconds { get; init; }
}
