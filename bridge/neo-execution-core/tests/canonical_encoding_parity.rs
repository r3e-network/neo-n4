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
        352,
        "the .NET public-inputs vector is not 352 bytes"
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
    assert_eq!(field_bytes(&fields, "public_inputs").len(), 352);
    assert_eq!(field_bytes(&fields, "withdrawal_proof").len(), 48 + 3 * 32);
    assert_eq!(
        hash256(&field_bytes(&fields, "public_inputs")),
        root(&fields, "public_input_hash"),
        "the fixture's own public_input_hash is not the digest of its public_inputs"
    );
}

#[test]
fn commitment_round_trips_fixture_commitment() {
    let fields = fields();
    let commitment_bytes = field_bytes(&fields, "commitment");
    let commitment =
        neo_execution_core::parse_commitment(&commitment_bytes).expect("parse commitment");
    assert_eq!(commitment.chain_id, field_u32(&fields, "chain_id"));
    assert_eq!(commitment.batch_number, field_u64(&fields, "batch_number"));
    assert_eq!(commitment.first_block, field_u64(&fields, "first_block"));
    assert_eq!(commitment.last_block, field_u64(&fields, "last_block"));
    assert_eq!(commitment.pre_state_root, root(&fields, "pre_state_root"));
    assert_eq!(commitment.post_state_root, root(&fields, "post_state_root"));
    assert_eq!(commitment.withdrawal_root, root(&fields, "withdrawal_root"));
    assert_eq!(
        commitment.public_input_hash,
        root(&fields, "public_input_hash")
    );
    assert_eq!(
        commitment.proof_type as u8,
        field_u32(&fields, "proof_type") as u8
    );
    let re_encoded =
        neo_execution_core::encode_commitment(&commitment).expect("encode commitment");
    assert_eq!(re_encoded, commitment_bytes);
}

#[test]
fn merkle_proof_round_trips_and_verifies_fixture_withdrawal_proof() {
    let fields = fields();
    let proof_bytes = field_bytes(&fields, "withdrawal_proof");
    let proof =
        neo_execution_core::parse_merkle_proof(&proof_bytes).expect("parse withdrawal proof");
    assert_eq!(
        proof.leaf_index,
        field_u32(&fields, "withdrawal_proof_leaf_index")
    );
    assert_eq!(proof.siblings.len(), 3);
    assert_eq!(proof.leaf, root(&fields, "withdrawal_leaf_4"));
    let re_encoded = neo_execution_core::encode_merkle_proof(&proof).expect("encode proof");
    assert_eq!(re_encoded, proof_bytes);
    assert!(
        neo_execution_core::verify_merkle_proof(&proof, &root(&fields, "withdrawal_root")),
        "verify_merkle_proof failed against canonical fixture withdrawal_root"
    );
}

#[test]
fn fuzz_parse_commitment_never_panics_on_random_bytes() {
    let mut state: u64 = 0xCAFE_BABE_DEAD_BEEF;
    for _ in 0..1000 {
        // Simple LCG PRNG for reproducible fuzzing
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let len = (state % 2048) as usize;
        let mut buf = vec![0u8; len];
        for b in buf.iter_mut() {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
            *b = (state >> 32) as u8;
        }
        // Must never panic
        let _ = neo_execution_core::parse_commitment(&buf);
    }
}

#[test]
fn fuzz_parse_chain_config_never_panics_on_random_bytes() {
    let mut state: u64 = 0x1234_5678_9ABC_DEF0;
    for _ in 0..1000 {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let len = (state % 256) as usize;
        let mut buf = vec![0u8; len];
        for b in buf.iter_mut() {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
            *b = (state >> 32) as u8;
        }
        let _ = neo_execution_core::parse_chain_config(&buf);
    }
}

#[test]
fn fuzz_parse_merkle_proof_never_panics_on_random_bytes() {
    let mut state: u64 = 0xF00D_BABE_55AA_1234;
    for _ in 0..1000 {
        state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
        let len = (state % 2048) as usize;
        let mut buf = vec![0u8; len];
        for b in buf.iter_mut() {
            state = state.wrapping_mul(6364136223846793005).wrapping_add(1);
            *b = (state >> 32) as u8;
        }
        let _ = neo_execution_core::parse_merkle_proof(&buf);
    }
}

#[test]
fn fuzz_chain_config_rejects_non_canonical_booleans() {
    let valid_config = neo_execution_core::L2ChainConfig {
        chain_id: 100,
        operator_manager: [1u8; 20],
        verifier: [2u8; 20],
        bridge_adapter: [3u8; 20],
        message_adapter: [4u8; 20],
        security_level: 2,
        da_mode: 1,
        gateway_enabled: true,
        permissionless_exit: false,
        sequencer_model: 1,
        exit_model: 0,
        active: true,
    };
    let encoded = neo_execution_core::encode_chain_config(&valid_config);
    assert_eq!(encoded.len(), 91);

    // Offsets 86 (gateway_enabled), 87 (permissionless_exit), 90 (active)
    for offset in [86, 87, 90] {
        for bad_val in [2u8, 3, 127, 255] {
            let mut corrupted = encoded;
            corrupted[offset] = bad_val;
            assert!(
                neo_execution_core::parse_chain_config(&corrupted).is_err(),
                "parse_chain_config accepted non-canonical boolean {bad_val} at offset {offset}"
            );
        }
    }
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
