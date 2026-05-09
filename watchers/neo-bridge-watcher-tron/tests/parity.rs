//! Cross-chain parity tests: prove the Eth watcher's messaging core works
//! unchanged for Tron. If these tests pass, a Tron daemon built on this
//! crate produces bytes the Neo `MpcCommitteeVerifier` accepts identically
//! to bytes from an Eth daemon — only the `externalChainId` field differs.

use neo_bridge_watcher_tron::eth::*;
use neo_bridge_watcher_tron::TRON_MAINNET_CHAIN_ID;
use neo_bridge_watcher_tron::eth::messaging::encode_asset_transfer_payload;

fn build_tron_deposit(nonce: u64) -> ExternalCrossChainMessage {
    // 1_000_000 = 0x0F4240, 3-byte minimal LE (matches C#
    // BigInteger.ToByteArray for unsigned values).
    let payload = encode_asset_transfer_payload([0xee; 20], &[0x40, 0x42, 0x0F]);
    ExternalCrossChainMessage {
        external_chain_id: TRON_MAINNET_CHAIN_ID,
        neo_chain_id: 1099,
        nonce,
        direction: ExternalBridgeDirection::ForeignToNeo,
        sender: [0x11; 20],
        recipient: [0xaa; 20],
        deadline_unix_seconds: 1_900_000_000,
        source_tx_ref: [0xee; 32],
        message_type: ExternalMessageType::AssetTransfer,
        payload,
    }
}

#[test]
fn canonical_bytes_emit_tron_chain_id_at_offset_zero() {
    // The on-chain MpcCommitteeVerifier reads externalChainId at offset 0
    // (4B LE). Pin that the Tron-flavored builder writes 0x10000000E0 there.
    let msg = build_tron_deposit(7);
    let bytes = canonical_message_bytes(&msg).unwrap();
    let chain_id_le = u32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]);
    assert_eq!(chain_id_le, TRON_MAINNET_CHAIN_ID);
}

#[test]
fn canonical_bytes_diverge_from_eth_only_at_chain_id_position() {
    // Same fields except the chain id. Only bytes 0..4 should differ —
    // pin that the Tron + Eth canonical formats are otherwise byte-identical.
    let tron = build_tron_deposit(7);
    let eth = ExternalCrossChainMessage {
        external_chain_id: 0xE000_0001,
        ..tron.clone()
    };
    let tron_bytes = canonical_message_bytes(&tron).unwrap();
    let eth_bytes = canonical_message_bytes(&eth).unwrap();
    assert_eq!(tron_bytes.len(), eth_bytes.len());
    // Bytes 0..4 differ.
    assert_ne!(&tron_bytes[..4], &eth_bytes[..4]);
    // Bytes 4.. are byte-identical.
    assert_eq!(&tron_bytes[4..], &eth_bytes[4..]);
}

#[test]
fn message_hash_differs_from_eth_for_same_other_fields() {
    // Pinning the safety property: a watcher signing a Tron message can NOT
    // be tricked into producing a signature that's also valid for an Eth
    // message with otherwise-identical fields. The chain id flows into the
    // hash.
    let tron = build_tron_deposit(7);
    let eth = ExternalCrossChainMessage {
        external_chain_id: 0xE000_0001,
        ..tron.clone()
    };
    let tron_hash = message_hash(&tron).unwrap();
    let eth_hash = message_hash(&eth).unwrap();
    assert_ne!(tron_hash, eth_hash);
}

#[test]
fn fixed_prefix_still_102_bytes() {
    // Layout invariant the contract parser depends on. Tron messages use
    // exactly the same layout — pin that this crate didn't accidentally
    // shift the structure.
    let mut msg = build_tron_deposit(0);
    msg.payload.clear();
    let bytes = canonical_message_bytes(&msg).unwrap();
    assert_eq!(bytes.len(), 102);
}
