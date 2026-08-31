namespace Neo.L2;

/// <summary>
/// Operating mode of a Neo 4 node per doc.md §6 — one of four values, and the set is closed.
/// </summary>
/// <remarks>
/// This is an operator-facing label, not a runtime switch: <c>neo-stack validate</c> reads it to warn
/// about incoherent configs, and nothing else consumes it. It has no byte in the 91-byte
/// <see cref="Neo.L2.L2ChainConfigSerializer"/> registration format, so <c>ChainRegistry</c> never sees
/// it. The execution engine is selected separately — doc.md §14.2's <c>--vm</c> (the
/// <c>neovm2-riscv</c> label in <c>chain.config.json</c>) and the devnet's <c>--executor riscv</c> —
/// so a new VM profile does not need, and must not be given, a member here.
/// </remarks>
public enum ChainMode : byte
{
    /// <summary>Plain Neo L1. Normal dBFT, normal governance, GAS generation, native contracts.</summary>
    L1Mode = 0,

    /// <summary>App-chain with independent state and validators. May bridge to NeoHub but L1 does not verify state transitions.</summary>
    SidechainMode = 1,

    /// <summary>Rollup L2: local sequencer/dBFT, batches submitted to NeoHub, L1 verifies proof or challenge while DA is recorded separately (NeoFS by default).</summary>
    L2RollupMode = 2,

    /// <summary>Validium L2: like rollup but transaction data lives off L1 (NeoFS, DAC, external DA).</summary>
    L2ValidiumMode = 3,
}
