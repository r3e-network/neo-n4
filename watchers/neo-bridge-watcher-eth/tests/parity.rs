//! Cross-language parity tests. These pin byte-for-byte equivalence
//! between this Rust crate's encoders and the C# implementations in
//! `src/Neo.L2.Bridge/External/`. The hex test vectors in
//! `messaging.rs` and these tests are the single source of truth — a
//! change to either side that breaks the constant breaks both test
//! suites simultaneously, forcing both implementations to update
//! together.

use neo_bridge_watcher_eth::messaging::encode_asset_transfer_payload;
use neo_bridge_watcher_eth::proof::{IndexedRsv, PubkeySignature};
use neo_bridge_watcher_eth::{Curve, EthProofBytes, NeoProofBytes};
use neo_bridge_watcher_eth::{
    ExternalBridgeDirection, ExternalCrossChainMessage, ExternalMessageType, NATIVE_ASSET_SENTINEL,
    canonical_message_bytes, message_hash,
};

const ASYMMETRIC_ASSET: [u8; 20] = [
    0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef, 0x10, 0x32, 0x54, 0x76, 0x98, 0xba, 0xdc, 0xfe,
    0x11, 0x22, 0x33, 0x44,
];
const REVERSED_ASSET: [u8; 20] = [
    0x44, 0x33, 0x22, 0x11, 0xfe, 0xdc, 0xba, 0x98, 0x76, 0x54, 0x32, 0x10, 0xef, 0xcd, 0xab, 0x89,
    0x67, 0x45, 0x23, 0x01,
];

fn sample_eth_deposit() -> ExternalCrossChainMessage {
    // 1_000_000 = 0x0F4240, 3-byte minimal LE (matches C# BigInteger.ToByteArray).
    let payload = encode_asset_transfer_payload(ASYMMETRIC_ASSET, &[0x40, 0x42, 0x0F]).unwrap();
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
    let expected = "010000e04b0400000700000000000000021111111111111111111111111111111111111111aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa00b33f7100000000eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee001b0000000123456789abcdef1032547698badcfe112233440300000040420f";
    assert_eq!(hex::encode(&bytes), expected);
}

#[test]
fn asset_payload_matches_csharp_and_solidity_network_order_vector() {
    let payload = encode_asset_transfer_payload(ASYMMETRIC_ASSET, &[0x40, 0x42, 0x0f]).unwrap();
    assert_eq!(
        hex::encode(payload),
        "0123456789abcdef1032547698badcfe112233440300000040420f"
    );
}

#[test]
fn asset_route_lookup_does_not_match_reversed_address() {
    let routes = std::collections::HashMap::from([(ASYMMETRIC_ASSET, "evm-token-route")]);
    assert_eq!(routes.get(&ASYMMETRIC_ASSET), Some(&"evm-token-route"));
    assert_eq!(routes.get(&REVERSED_ASSET), None);
}

#[test]
fn native_asset_sentinel_matches_csharp_and_solidity() {
    assert_eq!(
        hex::encode(NATIVE_ASSET_SENTINEL),
        "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee"
    );
}

/// Pin the raw Hash256 bytes. C# `UInt256.ToString()` displays the same
/// hash byte-reversed as `f69f8169...` — that's a display convention, not
/// a different value. Both representations are the same 32 bytes.
#[test]
fn message_hash_pinned_against_csharp() {
    let msg = sample_eth_deposit();
    assert_eq!(
        hex::encode(message_hash(&msg).unwrap()),
        "473ca6a7e6e63a57952ff7fdd7a9338f3202e3843386f13ec6fee5da69819ff6"
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

// ───────────────── shared cross-language fixture anchor ─────────────────
//
// The in-module vectors above are pasted into both language's tests (Rust here, C# in
// UT_External_RealCrypto) — the pattern works only while nobody edits one copy, and the
// audit found exactly that class of drift surviving for months elsewhere (withdrawal
// leaf). This block reads the SHARED third-implementation fixture that neither language
// produces at test time, so this crate's encoder is checked against bytes produced by
// neither itself nor its C# mirror.

const SHARED_FIXTURE: &str = include_str!("../../../tests/Shared/canonical_encoding_vectors.hex");

fn fixture_field(key: &str) -> String {
    for line in SHARED_FIXTURE.lines() {
        let line = line.trim();
        if let Some((k, v)) = line.split_once('=') {
            if k.trim() == key {
                return v.trim().to_string();
            }
        }
    }
    panic!("shared fixture has no field {key}");
}

fn fixture_bytes(key: &str) -> Vec<u8> {
    let hex = fixture_field(key);
    assert_eq!(hex.len() % 2, 0, "field {key} has odd hex length");
    (0..hex.len() / 2)
        .map(|i| u8::from_str_radix(&hex[2 * i..2 * i + 2], 16).expect("valid hex"))
        .collect()
}

fn fixture_u64(key: &str) -> u64 {
    fixture_field(key).parse().expect("decimal u64")
}

fn fixture_u32(key: &str) -> u32 {
    fixture_field(key).parse().expect("decimal u32")
}

fn fixture_hash(key: &str) -> [u8; 32] {
    fixture_bytes(key).try_into().expect("32 bytes")
}

#[test]
fn canonical_bytes_match_the_shared_third_implementation_vector() {
    // THREE encoders exist for this layout: this crate's `canonical_message_bytes`
    // (the bytes watchers SIGN), C# `ExternalMessageHasher.EncodeCanonical` (the
    // deployment/consumer side), and a hand-rolled copy inside
    // `UT_External_RealCrypto.CanonicalMessageBytes`. Before this test they had never
    // been compared against anything outside themselves. The vector comes from a fourth
    // implementation (Python) in a language none of the three use.
    let expected = fixture_bytes("xmsg_canonical");
    assert_eq!(expected.len(), 128, "the vector is a 102B prefix + 26B payload");

    // Rebuild the payload first: 20B foreignAsset + 4B amountLen + amount LE.
    let payload_vec = fixture_bytes("xmsg_payload");
    assert_eq!(payload_vec.len(), 26);
    let foreign_asset: [u8; 20] = payload_vec[..20].try_into().expect("20 bytes");
    let amount: &[u8] = &payload_vec[24..];
    assert_eq!(&payload_vec[20..24], &u32::try_from(amount.len()).unwrap().to_le_bytes());
    let payload = encode_asset_transfer_payload(foreign_asset, amount).unwrap();
    assert_eq!(payload, payload_vec, "the asset-transfer payload encoding drifted");

    let msg = ExternalCrossChainMessage {
        external_chain_id: fixture_u32("xmsg_external_chain_id"),
        neo_chain_id: fixture_u32("xmsg_neo_chain_id"),
        nonce: fixture_u64("xmsg_nonce"),
        direction: ExternalBridgeDirection::ForeignToNeo, // xmsg_direction = 2
        sender: [0xE1; 20],
        recipient: [0xC2; 20],
        deadline_unix_seconds: fixture_u64("xmsg_deadline"),
        source_tx_ref: [0xD3; 32],
        message_type: ExternalMessageType::AssetTransfer, // xmsg_message_type = 0
        payload,
    };

    let bytes = canonical_message_bytes(&msg).unwrap();
    assert_eq!(
        hex::encode(&bytes),
        hex::encode(&expected),
        "canonical_message_bytes does not reproduce the shared vector — the namespace, \
         direction byte, deadline width, or payload length prefix has drifted"
    );

    let hash = message_hash(&msg).unwrap();
    assert_eq!(
        hash,
        fixture_hash("xmsg_hash"),
        "message_hash is not the double-SHA256 of the shared vector"
    );
}
