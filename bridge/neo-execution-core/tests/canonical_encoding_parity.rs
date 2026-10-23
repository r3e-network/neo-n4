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

use neo_execution_core::{UInt160, UInt256, hash_public_inputs, hash256, merkle_root};

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
fn hash_public_inputs_with_forced_binds_the_forced_inclusion_count() {
    // The 14-parameter `hash_public_inputs` is a thin wrapper over
    // `hash_public_inputs_with_forced(..., 0)`, so the parity test above only pins the
    // ZERO case. The forced-inclusion count is the field the 2026-10-21 audit found
    // diverging between the Gateway guest and the authoritative implementation (the guest
    // rebuilt a 348-byte preimage), and it is what makes the domain a fixed 352 bytes —
    // so the NON-ZERO case needs its own cross-language check.
    //
    // The bytes come verbatim from the shared .NET-exported fixture, so this asserts the
    // Rust encoder against the .NET encoder rather than against a Rust-side mirror.
    let fields = fields();
    let dotnet = field_bytes(&fields, "public_inputs_with_forced");
    assert_eq!(
        dotnet.len(),
        352,
        "the .NET public-inputs vector is not 352 bytes"
    );

    // The vector's trailing u32 LE is the forced-inclusion count.
    let forced = u32::from_le_bytes(dotnet[348..352].try_into().expect("4 bytes"));
    assert_eq!(
        forced, 3,
        "the exported non-zero vector must carry a non-zero forced-inclusion count"
    );

    let with_forced = neo_execution_core::hash_public_inputs_with_forced(
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
        forced,
    );

    assert_eq!(
        with_forced,
        hash256(&dotnet),
        "hash_public_inputs_with_forced does not reproduce the .NET-encoded preimage for a \
         non-zero forced-inclusion count"
    );
}

#[test]
fn hash_public_inputs_with_forced_changes_the_digest_when_the_count_changes() {
    // The count must be INSIDE the signed preimage: a batch that consumed a different
    // number of forced inclusions must not produce the same public-input hash.
    let fields = fields();
    let base = neo_execution_core::hash_public_inputs_with_forced(
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
        0,
    );
    let bumped = neo_execution_core::hash_public_inputs_with_forced(
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
        1,
    );
    assert_ne!(
        base, bumped,
        "the forced-inclusion count is not bound into the public-input hash"
    );
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
fn withdrawal_leaf_preimage_matches_the_dotnet_export() {
    // Four implementations encode this leaf:
    //   1. NeoHub.SharedBridge.ComputeWithdrawalLeafHash   (on-chain, authoritative)
    //   2. Neo.L2.State.MessageHasher.EncodeWithdrawal     (off-chain .NET)
    //   3. neo_execution_core::native::withdrawal_leaf_preimage  ← this test
    //   4. the hand-rolled builder in UT_SharedBridge_Vm
    //
    // The 2026-10-21 audit found this leaf had ALREADY forked once (off-chain 98B Layout A
    // against the on-chain 118B Layout C) and it survived because every implementation was
    // validated only against itself. Until now (3) and (4) had no cross-implementation
    // check at all.
    let fields = fields();
    let dotnet = field_bytes(&fields, "withdrawal_leaf_preimage");
    assert_eq!(
        dotnet.len(),
        120,
        "the .NET withdrawal-leaf vector is not 120 bytes (116-byte header + 4-byte amount)"
    );
    assert_eq!(
        u32::from_le_bytes(dotnet[112..116].try_into().expect("4 bytes")),
        4,
        "the vector's amountLen must be 4 (100_000_000 fits in 4 LE bytes)"
    );

    // Read the 20-byte fields straight out of the vector, so the test cannot invent a
    // value the vector does not carry.
    let field20 =
        |offset: usize| -> UInt160 { dotnet[offset..offset + 20].try_into().expect("20 bytes") };
    let amount = {
        let bytes = &dotnet[116..];
        assert_eq!(bytes.len(), 4);
        num_bigint::BigUint::from_bytes_le(bytes)
    };

    let rust = neo_execution_core::withdrawal_leaf_preimage(
        field_u32(&fields, "withdrawal_chain_id"),
        &field20(4),  // emittingContract
        &field20(24), // l2Sender
        &field20(92), // l1Recipient
        &field20(44), // l2Asset
        &field20(72), // l1Asset
        &amount,
        field_u64(&fields, "withdrawal_nonce"),
    );

    assert_eq!(
        rust, dotnet,
        "withdrawal_leaf_preimage does not reproduce the .NET-encoded preimage — the \
         l1Asset position, the nonce width, or the amount length prefix has drifted"
    );
    assert_eq!(
        hash256(&rust),
        root(&fields, "withdrawal_leaf_hash"),
        "the withdrawal leaf hash is not the double-SHA256 of the preimage"
    );
}

#[test]
fn chain_config_encoding_matches_the_dotnet_export() {
    // `RollupHub.registerChain` parses these 91 bytes. Rust both encodes and parses them, and
    // its own round-trip test cannot detect a layout change that both directions share — so
    // this pins the bytes against the .NET-exported vector instead.
    let fields = fields();
    let dotnet = field_bytes(&fields, "chain_config");
    assert_eq!(
        dotnet.len(),
        91,
        "the .NET chain-config vector is not 91 bytes"
    );

    let field20 =
        |offset: usize| -> UInt160 { dotnet[offset..offset + 20].try_into().expect("20 bytes") };
    let config = neo_execution_core::L2ChainConfig {
        chain_id: u32::from_le_bytes(dotnet[0..4].try_into().expect("4 bytes")),
        operator_manager: field20(4),
        verifier: field20(24),
        bridge_adapter: field20(44),
        message_adapter: field20(64),
        security_level: dotnet[84],
        da_mode: dotnet[85],
        gateway_enabled: dotnet[86] != 0,
        permissionless_exit: dotnet[87] != 0,
        sequencer_model: dotnet[88],
        exit_model: dotnet[89],
        active: dotnet[90] != 0,
    };

    assert_eq!(
        neo_execution_core::encode_chain_config(&config).to_vec(),
        dotnet,
        "encode_chain_config does not reproduce the .NET-encoded chain config — a field \
         order, address slot, or boolean position has drifted"
    );

    // Parse must accept the same bytes and yield the same values.
    let parsed = neo_execution_core::parse_chain_config(&dotnet).expect("parse chain config");
    assert_eq!(
        parsed, config,
        "parse_chain_config disagrees with the vector"
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
    let re_encoded = neo_execution_core::encode_commitment(&commitment).expect("encode commitment");
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
