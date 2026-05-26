/// Current canonical batch request wire-format version.
///
/// Version 1 (deprecated): 4-root layout (pre_state_root, da_commitment, l1_messages, txs).
/// Version 2 (current): 10-root layout matching C# PublicInputs (withdrawal_root,
/// l2_to_l1_message_root, l2_to_l2_message_root, l1_message_hash, block_context_hash added).
pub const BATCH_WIRE_VERSION: u8 = 2;

/// Default per-tx gas limit when the wire format does not specify one.
///
/// 100,000,000 datoshi = 1 GAS, matching Neo N3's typical tx-level cap.
pub const DEFAULT_PER_TX_GAS_LIMIT: u64 = 100_000_000;
