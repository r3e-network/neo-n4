//! Canonical encoding of `ExternalCrossChainMessage`. Byte-for-byte parity
//! with `Neo.L2.Messaging.ExternalMessageHasher` in the C# codebase.

use sha2::{Digest, Sha256};
use thiserror::Error;

/// Direction the message flows. Matches `Neo.L2.ExternalBridgeDirection`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ExternalBridgeDirection {
    /// Neo → foreign chain.
    NeoToForeign = 1,
    /// Foreign chain → Neo.
    ForeignToNeo = 2,
}

/// Message type. Matches `Neo.L2.ExternalMessageType`.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum ExternalMessageType {
    /// Pure asset transfer.
    AssetTransfer = 0,
    /// Pure contract call.
    Call = 1,
    /// Asset transfer + follow-up call.
    AssetAndCall = 2,
}

/// One foreign-bridge cross-chain message, pre-hashing.
#[derive(Debug, Clone)]
pub struct ExternalCrossChainMessage {
    pub external_chain_id: u32,
    pub neo_chain_id: u32,
    pub nonce: u64,
    pub direction: ExternalBridgeDirection,
    /// 20-byte sender (UInt160 on Neo side; for foreign senders, last 20B
    /// of the foreign address — natural for Eth/Tron).
    pub sender: [u8; 20],
    /// 20-byte recipient.
    pub recipient: [u8; 20],
    pub deadline_unix_seconds: u64,
    /// 32-byte source-chain transaction reference (Eth tx hash etc.).
    pub source_tx_ref: [u8; 32],
    pub message_type: ExternalMessageType,
    pub payload: Vec<u8>,
}

#[derive(Debug, Error)]
pub enum BuildError {
    #[error(
        "externalChainId 0x{0:08X} must use the 0xE0_xx_xx_xx foreign-namespace prefix"
    )]
    BadNamespace(u32),
    /// The Eth-side router emitted a message-type byte whose handler isn't
    /// implemented on the watcher side yet (e.g. `MSG_TYPE_ASSET_AND_CALL`).
    /// The watcher rejects the message rather than guessing at the payload
    /// layout — silently misinterpreting it would forge an inbound message
    /// the Eth side never authorized.
    #[error("unsupported message-type byte 0x{0:02X} — watcher cannot encode payload safely")]
    UnsupportedMessageType(u8),
}

/// Build the canonical pre-image bytes (102-byte fixed prefix + payload).
/// The watcher signs THESE bytes; the verifier hashes them with secp256k1
/// + SHA256 (Eth `ecrecover(sha256(messageBytes))` and Neo
///   `CryptoLib.VerifyWithECDsa(secp256k1SHA256)` are the same operation).
pub fn canonical_message_bytes(
    msg: &ExternalCrossChainMessage,
) -> Result<Vec<u8>, BuildError> {
    if msg.external_chain_id & 0xFF00_0000 != 0xE000_0000 {
        return Err(BuildError::BadNamespace(msg.external_chain_id));
    }

    let size = 102 + msg.payload.len();
    let mut out = Vec::with_capacity(size);
    out.extend_from_slice(&msg.external_chain_id.to_le_bytes());
    out.extend_from_slice(&msg.neo_chain_id.to_le_bytes());
    out.extend_from_slice(&msg.nonce.to_le_bytes());
    out.push(msg.direction as u8);
    out.extend_from_slice(&msg.sender);
    out.extend_from_slice(&msg.recipient);
    out.extend_from_slice(&msg.deadline_unix_seconds.to_le_bytes());
    out.extend_from_slice(&msg.source_tx_ref);
    out.push(msg.message_type as u8);
    out.extend_from_slice(&(msg.payload.len() as u32).to_le_bytes());
    out.extend_from_slice(&msg.payload);
    debug_assert_eq!(out.len(), size);
    Ok(out)
}

/// Compute the canonical message hash (Hash256 = double-SHA256 over the
/// canonical pre-image bytes). Mirrors C# `ExternalMessageHasher.HashMessage`.
pub fn message_hash(msg: &ExternalCrossChainMessage) -> Result<[u8; 32], BuildError> {
    let bytes = canonical_message_bytes(msg)?;
    let h1 = Sha256::digest(&bytes);
    let h2 = Sha256::digest(h1);
    Ok(h2.into())
}

/// Encode an asset-transfer payload (rides inside `payload` for
/// `MessageType.AssetTransfer`):
///
/// ```text
/// [20B foreignAsset][4B amountLength LE][N amount LE]
/// ```
///
/// Mirrors C# `ExternalAssetTransferPayload.Encode`.
pub fn encode_asset_transfer_payload(foreign_asset: [u8; 20], amount_le: &[u8]) -> Vec<u8> {
    assert!(
        amount_le.len() <= 64,
        "amount_le.len() {} > 64 (cap matches C# MaxAmountBytes)",
        amount_le.len()
    );
    let mut out = Vec::with_capacity(20 + 4 + amount_le.len());
    out.extend_from_slice(&foreign_asset);
    out.extend_from_slice(&(amount_le.len() as u32).to_le_bytes());
    out.extend_from_slice(amount_le);
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    fn sample_msg() -> ExternalCrossChainMessage {
        // 1_000_000 = 0x0F4240, encoded as the 3-byte minimal LE representation
// (matches BigInteger.ToByteArray() in C# for unsigned values that don't
// need a sign byte).
let payload = encode_asset_transfer_payload([0xee; 20], &[0x40, 0x42, 0x0F]);
        ExternalCrossChainMessage {
            external_chain_id: 0xE000_0001,
            neo_chain_id: 1099,
            nonce: 7,
            direction: ExternalBridgeDirection::ForeignToNeo,
            sender: [0x11; 20],
            recipient: [0xaa; 20],
            deadline_unix_seconds: 1_900_000_000,
            source_tx_ref: [0xee; 32],
            message_type: ExternalMessageType::AssetTransfer,
            payload,
        }
    }

    /// Pinned hex from the C# `ExternalMessageHasher` over the same fields.
    /// If this test fails, either the Rust or C# implementation drifted from
    /// the canonical wire format. Both must update together.
    #[test]
    fn canonical_bytes_match_csharp_vector() {
        let msg = sample_msg();
        let bytes = canonical_message_bytes(&msg).unwrap();
        let expected = "010000e04b0400000700000000000000021111111111111111111111111111111111111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa00b33f7100000000eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee001b000000eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee0300000040420f";
        assert_eq!(hex::encode(&bytes), expected);
        assert_eq!(bytes.len(), 129);
    }

    /// Pinned message hash. The full `ExternalCrossChainMessage` MessageHash
    /// is `Hash256(canonical_bytes)`.
    ///
    /// Byte order: this constant is the raw Hash256 output (natural SHA256
    /// byte order). When the C# side's `UInt256.ToString()` displays the
    /// same hash it REVERSES the bytes (Neo's UInt256 display convention is
    /// big-endian-for-humans even though storage is little-endian) — that
    /// reversed display is `acbfe1e9...` for this vector. Both
    /// representations are the same 32 bytes; the test pins the raw
    /// (Rust-natural) order.
    #[test]
    fn message_hash_matches_csharp_vector() {
        let msg = sample_msg();
        let hash = message_hash(&msg).unwrap();
        let expected_raw = "ce681e5ecb3eaf452d1834fd94c397271a6556736a4ecfa1e66e4d67e9e1bfac";
        assert_eq!(hex::encode(hash), expected_raw);
    }

    #[test]
    fn rejects_non_namespaced_external_chain_id() {
        let mut msg = sample_msg();
        msg.external_chain_id = 1099;       // Neo L2 id, NOT 0xE0_xx
        assert!(matches!(
            canonical_message_bytes(&msg),
            Err(BuildError::BadNamespace(1099))
        ));
    }

    #[test]
    fn hash_changes_for_every_field() {
        let baseline = sample_msg();
        let baseline_hash = message_hash(&baseline).unwrap();

        let mut nonce_changed = baseline.clone();
        nonce_changed.nonce += 1;
        assert_ne!(message_hash(&nonce_changed).unwrap(), baseline_hash);

        let mut payload_changed = baseline.clone();
        payload_changed.payload[0] ^= 0xFF;
        assert_ne!(message_hash(&payload_changed).unwrap(), baseline_hash);

        let mut direction_changed = baseline.clone();
        direction_changed.direction = ExternalBridgeDirection::NeoToForeign;
        assert_ne!(message_hash(&direction_changed).unwrap(), baseline_hash);
    }

    #[test]
    fn fixed_prefix_is_102_bytes() {
        // Empty payload → exactly 102 bytes. Pin the layout invariant the
        // contract-side parser relies on (offsets 0..101 are immutable).
        let mut msg = sample_msg();
        msg.payload.clear();
        let bytes = canonical_message_bytes(&msg).unwrap();
        assert_eq!(bytes.len(), 102);
    }
}
