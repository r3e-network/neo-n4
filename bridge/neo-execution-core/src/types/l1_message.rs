use alloc::vec::Vec;

/// L1→L2 cross-chain message parsed from the wire format.
///
/// The `bytes` field is public for ease of construction in tests and host code.
/// **Invariant**: `bytes.len()` must fit in `u32` — the wire format uses a
/// 32-bit LE length prefix. This is enforced at parse time by
/// [`read_var_bytes`](crate::wire::read_var_bytes) and the per-element cap.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct L1Message {
    pub bytes: Vec<u8>,
}
