//! Cross-language parity tests. These pin byte-for-byte equivalence
//! between this Rust crate's encoders and the C# implementations in
//! `src/Neo.L2.Bridge/External/`. The hex test vectors in
//! `messaging.rs` and these tests are the single source of truth — a
//! change to either side that breaks the constant breaks both test
//! suites simultaneously, forcing both implementations to update
//! together.

use neo_bridge_watcher_eth::messaging::encode_asset_transfer_payload;
use neo_bridge_watcher_eth::proof::{IndexedRsv, PubkeySignature};
use neo_bridge_watcher_eth::{
    canonical_message_bytes, message_hash, ExternalBridgeDirection, ExternalCrossChainMessage,
    ExternalMessageType,
};
use neo_bridge_watcher_eth::{Curve, EthProofBytes, NeoProofBytes};

fn sample_eth_deposit() -> ExternalCrossChainMessage {
    // 1_000_000 = 0x0F4240, 3-byte minimal LE (matches C# BigInteger.ToByteArray).
    let payload = encode_asset_transfer_payload([0xee; 20], &[0x40, 0x42, 0x0F]).unwrap();
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

#[test]
fn canonical_bytes_pinned_against_csharp() {
    // Same vector as the in-module test, restated here so a refactor
    // that drops the in-module test still keeps the parity invariant.
    let msg = sample_eth_deposit();
    let bytes = canonical_message_bytes(&msg).unwrap();
    let expected = "010000e04b0400000700000000000000021111111111111111111111111111111111111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa00b33f7100000000eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee001b000000eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee0300000040420f";
    assert_eq!(hex::encode(&bytes), expected);
}

/// Pin the raw Hash256 bytes. C# `UInt256.ToString()` displays the same
/// hash byte-reversed as `acbfe1e9...` — that's a display convention, not
/// a different value. Both representations are the same 32 bytes.
#[test]
fn message_hash_pinned_against_csharp() {
    let msg = sample_eth_deposit();
    assert_eq!(
        hex::encode(message_hash(&msg).unwrap()),
        "ce681e5ecb3eaf452d1834fd94c397271a6556736a4ecfa1e66e4d67e9e1bfac"
    );
}

#[test]
fn neo_proof_bytes_layout_matches_csharp() {
    // The C# UT_External_MpcCommitteePayload tests pin the same shape;
    // here we compute the byte length we'd send to Neo's
    // MpcCommitteeVerifier and confirm it matches the contract's
    // expected (header + sigCount × (keyLen + 64)).
    let sigs = vec![
        PubkeySignature {
            pubkey: vec![0x01; 33],
            signature: [0xA1; 64],
        },
        PubkeySignature {
            pubkey: vec![0x02; 33],
            signature: [0xA2; 64],
        },
        PubkeySignature {
            pubkey: vec![0x03; 33],
            signature: [0xA3; 64],
        },
    ];
    let neo_bytes = NeoProofBytes::encode(Curve::Secp256k1, &sigs).unwrap();
    assert_eq!(neo_bytes.len(), 2 + 3 * (33 + 64));
    assert_eq!(u16::from_le_bytes([neo_bytes[0], neo_bytes[1]]), 3);
}

#[test]
fn eth_proof_bytes_layout_matches_solidity() {
    // The Solidity tests in external/foreign-contracts/eth/test/ build
    // the same layout; here we confirm we produce 2 + N × 66 with the
    // right interleaving of (idx, r, s, v).
    let sigs = vec![
        IndexedRsv {
            signer_idx: 0,
            r: [0x11; 32],
            s: [0x22; 32],
            v: 27,
        },
        IndexedRsv {
            signer_idx: 2,
            r: [0x33; 32],
            s: [0x44; 32],
            v: 28,
        },
    ];
    let eth_bytes = EthProofBytes::encode(&sigs).unwrap();
    assert_eq!(eth_bytes.len(), 2 + 2 * 66);
    assert_eq!(u16::from_le_bytes([eth_bytes[0], eth_bytes[1]]), 2);
    // First sig at offset 2. Layout: idx(1) + r(32) + s(32) + v(1).
    assert_eq!(eth_bytes[2], 0);
    assert_eq!(&eth_bytes[3..35], &[0x11; 32]);
    assert_eq!(&eth_bytes[35..67], &[0x22; 32]);
    assert_eq!(eth_bytes[67], 27);
    // Second sig at offset 68.
    assert_eq!(eth_bytes[68], 2);
    assert_eq!(eth_bytes[133], 28);
}
