/// Output of signing a canonical message.
///
/// `signature` is always 64 bytes regardless of curve:
/// - secp256k1 → `r ‖ s` (raw 32 + 32)
/// - ed25519 → `R ‖ s` (raw 32 + 32)
///
/// `recovery_id` is meaningful only for secp256k1 (`27` or `28`,
/// Ethereum's `ecrecover` convention). ed25519 signers return `0`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct SignerOutput {
    pub signature: [u8; 64],
    pub recovery_id: u8,
}
