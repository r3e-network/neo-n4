namespace Neo.L2;

/// <summary>
/// Operating mode of a Neo 4 node. Drives consensus, batching, settlement, and DA behavior.
/// </summary>
/// <remarks>
/// See doc.md §6.
/// </remarks>
public enum ChainMode : byte
{
    /// <summary>Plain Neo L1. Normal dBFT, normal governance, GAS generation, native contracts.</summary>
    L1Mode = 0,

    /// <summary>App-chain with independent state and validators. May bridge to NeoHub but L1 does not verify state transitions.</summary>
    SidechainMode = 1,

    /// <summary>Rollup L2: local sequencer/dBFT, batches submitted to NeoHub, L1 verifies proof or challenge.</summary>
    L2RollupMode = 2,

    /// <summary>Validium L2: like rollup but transaction data lives off L1 (NeoFS, DAC, external DA).</summary>
    L2ValidiumMode = 3,
}
