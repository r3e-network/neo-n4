//! Cross-language parity for the batch public-inputs preimage and the withdrawal Merkle fold.
//!
//! Both claims below are about tables this crate keeps implicitly: the order `hash_public_inputs`
//! concatenates its fourteen parameters in, and the order `merkle_root` pairs leaves. Nothing outside
//! Rust compared either one to the .NET side before this file. The vectors come from
//! `tests/Shared/canonical_encoding_vectors.hex`, which
//! `tests/Neo.L2.IntegrationTests/UT_CanonicalEncodingParity.cs` asserts are the bytes
//! `BatchSerializer.EncodePublicInputs` emits and the root `MerkleTree` produces — so a reordering in
//! either language now fails a test in both, and the shared file means neither test re-states the
//! other's expected digest.
//!
//! See `docs/audit/subsystem-verification-audit-2026-08-30.md` V2.

use std::collections::BTreeMap;

use neo_execution_core::{UInt256, hash_public_inputs, hash256, merkle_root};

const FIXTURE: &str = include_str!("../../../tests/Shared/canonical_encoding_vectors.hex");

#[test]
fn hash_public_inputs_assembles_the_bytes_the_dotnet_encoder_writes() {
    let fields = fields();
    let dotnet = field_bytes(&fields, "public_inputs");
    assert_eq!(
        dotnet.len(),
        348,
        "the .NET public-inputs vector is not 348 bytes"
    );

    let rust = hash_public_inputs(
        field_u32(&fields, "chain_id"),
        field_u64(&fields, "batch_number"),
        field_u64(&fields, "first_block"),
        field_u64(&fields, "last_block"),
        &root(&fields, "pre_state_root"),
        &root(&fields, "post_state_root"),
        &root(&fields, "tx_root"),
        &root(&fields, "receipt_root"),
        &root(&fields, "withdrawal_root"),
        &root(&fields, "l2_to_l1_message_root"),
        &root(&fields, "l2_to_l2_message_root"),
        &root(&fields, "l1_message_hash"),
        &root(&fields, "da_commitment"),
        &root(&fields, "block_context_hash"),
    );

    assert_eq!(
        rust,
        hash256(&dotnet),
        "hash_public_inputs concatenates its arguments in an order that is not the order \
         BatchSerializer.EncodePublicInputs writes them"
    );
    assert_eq!(rust, root(&fields, "public_input_hash"));
}

#[test]
fn merkle_root_folds_the_withdrawal_leaves_the_dotnet_tree_folds() {
    let fields = fields();
    let count = usize::try_from(field_u64(&fields, "withdrawal_leaf_count"))
        .expect("withdrawal leaf count");
    let leaves: Vec<UInt256> = (0..count)
        .map(|leaf| root(&fields, &format!("withdrawal_leaf_{leaf}")))
        .collect();

    assert_eq!(
        merkle_root(&leaves),
        root(&fields, "withdrawal_root"),
        "merkle_root and Neo.L2.State.MerkleTree disagree on the withdrawal fold \
         (leaf ordering, odd-leaf duplication, or the join order)"
    );
}

#[test]
fn fixture_carries_the_sizes_the_encoders_declare() {
    let fields = fields();
    assert_eq!(field_bytes(&fields, "commitment").len(), 321);
    assert_eq!(field_bytes(&fields, "public_inputs").len(), 348);
    // 48-byte header + 3 siblings. No Rust code parses this framing today — nothing in the workspace
    // reads a path bitmap — so the length is checked here only to keep the vector from silently
    // losing a sibling.
    assert_eq!(field_bytes(&fields, "withdrawal_proof").len(), 48 + 3 * 32);
    assert_eq!(
        hash256(&field_bytes(&fields, "public_inputs")),
        root(&fields, "public_input_hash"),
        "the fixture's own public_input_hash is not the digest of its public_inputs"
    );
}

fn fields() -> BTreeMap<&'static str, &'static str> {
    FIXTURE
        .lines()
        .filter_map(|line| {
            let line = line.trim();
            if line.is_empty() || line.starts_with('#') {
                None
            } else {
                line.split_once('=')
            }
        })
        .map(|(key, value)| (key, value.trim()))
        .collect()
}

fn value<'a>(fields: &BTreeMap<&'static str, &'a str>, key: &str) -> &'a str {
    let Some(value) = fields.get(key) else {
        panic!("fixture has no {key} field");
    };
    value
}

fn field_u32(fields: &BTreeMap<&'static str, &'static str>, key: &str) -> u32 {
    let Ok(value) = value(fields, key).parse() else {
        panic!("fixture field {key} is not a decimal u32");
    };
    value
}

fn field_u64(fields: &BTreeMap<&'static str, &'static str>, key: &str) -> u64 {
    let Ok(value) = value(fields, key).parse() else {
        panic!("fixture field {key} is not a decimal u64");
    };
    value
}

fn root(fields: &BTreeMap<&'static str, &'static str>, key: &str) -> UInt256 {
    let bytes = field_bytes(fields, key);
    let Ok(root) = bytes.try_into() else {
        panic!("fixture field {key} is not 32 bytes");
    };
    root
}

fn field_bytes(fields: &BTreeMap<&'static str, &'static str>, key: &str) -> Vec<u8> {
    let hex = value(fields, key);
    assert_eq!(
        hex.len() % 2,
        0,
        "fixture field {key} has an odd hex length"
    );
    hex.as_bytes()
        .chunks_exact(2)
        .map(|pair| {
            let text = std::str::from_utf8(pair).expect("fixture hex is ascii");
            let Ok(byte) = u8::from_str_radix(text, 16) else {
                panic!("invalid hex byte in fixture field {key}");
            };
            byte
        })
        .collect()
}
