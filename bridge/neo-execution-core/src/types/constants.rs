/// Current canonical batch request wire-format version.
pub const BATCH_WIRE_VERSION: u8 = 1;

/// Default per-tx gas limit when the wire format does not specify one.
///
/// 100,000,000 datoshi = 1 GAS, matching Neo N3's typical tx-level cap.
pub const DEFAULT_PER_TX_GAS_LIMIT: u64 = 100_000_000;
