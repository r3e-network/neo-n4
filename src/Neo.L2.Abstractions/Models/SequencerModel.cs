namespace Neo.L2;

/// <summary>
/// Sequencer model published in <see cref="L2ChainConfig"/> as part of the spec §16.2
/// security label set. Tells users + bridges who is responsible for ordering L2 transactions.
/// </summary>
/// <remarks>
/// See doc.md §16.2 (Security Labels — "Sequencer: centralized / dBFT committee / decentralized").
/// Higher numbers mean less centralized; pinned bytes 0..2 so a future add doesn't shift the
/// existing wire format.
/// </remarks>
public enum SequencerModel : byte
{
    /// <summary>Single operator orders transactions; censorship resistance comes from forced
    /// inclusion + slashing only.</summary>
    Centralized = 0,

    /// <summary>dBFT committee (the Neo-native default) — multiple validators reach consensus
    /// on tx order each block. Inherits Neo's one-block finality property.</summary>
    DbftCommittee = 1,

    /// <summary>Permissionless decentralized sequencer set — anyone can propose ordering
    /// (typically with a leader-election / VRF mechanism).</summary>
    Decentralized = 2,
}
