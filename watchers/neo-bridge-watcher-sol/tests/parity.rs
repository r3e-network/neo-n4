//! Cross-curve parity tests: prove the Eth watcher's WatcherCore
//! orchestrator runs unchanged with an ed25519 signer (curve_tag=2).
//! If these pass, a Solana daemon built on this crate produces proof
//! bytes Neo's `MpcCommitteeVerifier` accepts via the
//! `CryptoLib.VerifyWithEd25519` dispatch path.

use neo_bridge_watcher_sol::eth::messaging::encode_asset_transfer_payload;
use neo_bridge_watcher_sol::eth::*;
use neo_bridge_watcher_sol::{Ed25519FileSigner, SOLANA_MAINNET_CHAIN_ID};

fn build_solana_deposit(nonce: u64) -> ExternalCrossChainMessage {
    let payload = encode_asset_transfer_payload([0xee; 20], &[0x40, 0x42, 0x0F]).unwrap();
    ExternalCrossChainMessage {
        external_chain_id: SOLANA_MAINNET_CHAIN_ID,
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
fn watcher_core_drives_through_with_ed25519_signer() {
    // Construct a WatcherCore parameterized over Ed25519FileSigner. If the
    // trait abstraction was right, this just compiles and runs. Then drive
    // one event end-to-end and assert the produced proofBytes have the
    // ed25519-flavored layout (32B pubkey + 64B sig per signer, total
    // 2 + 32 + 64 = 98 bytes).
    let signer = Ed25519FileSigner::from_bytes(&[0x42; 32]).unwrap();
    let mut amount = [0u8; 32];
    amount[29..32].copy_from_slice(&[0x0F, 0x42, 0x40]);
    let event = LockedEvent {
        external_chain_id: SOLANA_MAINNET_CHAIN_ID,
        neo_chain_id: 1099,
        nonce: 7,
        sender: [0x11; 20],
        neo_recipient: [0xaa; 20],
        asset: [0xee; 20],
        amount,
        payload: Vec::new(),
        deadline: 1_900_000_000,
        source_tx_hash: [0xee; 32],
        block_number: 1234,
    };
    let mut core = WatcherCore::new(
        SOLANA_MAINNET_CHAIN_ID,
        signer,
        MockEventSource::new(),
        MockSubmitter::new(),
        InMemoryJournal::new(),
    );
    let _ = core.process_event(event).expect("ed25519 round-trip");

    let subs = core.submitter.submissions();
    assert_eq!(subs.len(), 1);
    // Proof bytes for ed25519 single-signer: 2B header + (32B pubkey + 64B sig) = 98.
    assert_eq!(subs[0].proof_bytes.len(), 98,
        "ed25519 single-signer Neo proof = 2 + 32 + 64 = 98 bytes (was 99 for secp256k1's 33B pubkey)");
    // Header is sigCount LE.
    assert_eq!(
        u16::from_le_bytes([subs[0].proof_bytes[0], subs[0].proof_bytes[1]]),
        1
    );
    // First sig: pubkey at offset 2 (32B), signature at offset 34 (64B).
    let pubkey_in_proof = &subs[0].proof_bytes[2..2 + 32];
    let signer = Ed25519FileSigner::from_bytes(&[0x42; 32]).unwrap();
    assert_eq!(pubkey_in_proof, &signer.public_key_bytes()[..]);
}

#[test]
fn solana_canonical_bytes_use_solana_chain_id() {
    let msg = build_solana_deposit(7);
    let bytes = canonical_message_bytes(&msg).unwrap();
    let chain_id_le = u32::from_le_bytes([bytes[0], bytes[1], bytes[2], bytes[3]]);
    assert_eq!(chain_id_le, SOLANA_MAINNET_CHAIN_ID);
}

#[test]
fn solana_message_hash_distinct_from_eth_and_tron() {
    // Same fields, three different chain ids → three distinct hashes.
    // Pins the cross-chain replay-protection invariant that already
    // applies to Eth+Tron, also extends to Solana.
    let sol_msg = build_solana_deposit(7);
    let eth_msg = ExternalCrossChainMessage {
        external_chain_id: 0xE000_0001,
        ..sol_msg.clone()
    };
    let tron_msg = ExternalCrossChainMessage {
        external_chain_id: 0xE000_0010,
        ..sol_msg.clone()
    };
    let sol_hash = message_hash(&sol_msg).unwrap();
    let eth_hash = message_hash(&eth_msg).unwrap();
    let tron_hash = message_hash(&tron_msg).unwrap();
    assert_ne!(sol_hash, eth_hash);
    assert_ne!(sol_hash, tron_hash);
    assert_ne!(eth_hash, tron_hash);
}
