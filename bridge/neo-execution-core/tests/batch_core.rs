use neo_execution_core::{
    CANONICAL_RECEIPT_V1_BYTES, CanonicalReceiptV1, ExecutionError, StateEntry,
    encode_proof_witness_artifact, encode_receipt, events_hash, hash256, keyed_state_root,
    merkle_root, parse_proof_witness_artifact, parse_transaction, receipt_hash, storage_delta_hash,
};

const FIXTURE_HEX: &str = include_str!("../../neo-zkvm-guest/tests/fixtures/stateful_batch_v1.hex");

fn fixture_bytes() -> Vec<u8> {
    decode_hex(&FIXTURE_HEX.split_whitespace().collect::<String>())
}

#[test]
fn proof_witness_artifact_v1_round_trips_without_a_second_envelope() {
    let bytes = fixture_bytes();
    let artifact = parse_proof_witness_artifact(&bytes).expect("fixture parse");

    assert_eq!(&bytes[..8], b"NEO4PWIT");
    assert_eq!(&artifact.state_witness_bytes[..8], b"NEO4STW1");
    assert_eq!(&artifact.effects_bytes[..8], b"NEO4EFX1");
    assert_eq!(encode_proof_witness_artifact(&artifact).unwrap(), bytes);
}

#[test]
fn canonical_receipt_v1_is_exactly_105_bytes() {
    let receipt = CanonicalReceiptV1 {
        tx_hash: [0x11; 32],
        success: true,
        gas_consumed: 0x0102_0304_0506_0708,
        storage_delta_hash: [0x22; 32],
        events_hash: [0x33; 32],
    };
    let bytes = encode_receipt(&receipt);

    assert_eq!(CANONICAL_RECEIPT_V1_BYTES, 105);
    assert_eq!(&bytes[..32], &[0x11; 32]);
    assert_eq!(bytes[32], 1);
    assert_eq!(&bytes[33..41], &0x0102_0304_0506_0708i64.to_le_bytes());
    assert_eq!(&bytes[41..73], &[0x22; 32]);
    assert_eq!(&bytes[73..105], &[0x33; 32]);
    assert_eq!(receipt_hash(&receipt), hash256(&bytes));
}

#[test]
fn full_transaction_hash_excludes_witnesses() {
    let artifact = parse_proof_witness_artifact(&fixture_bytes()).unwrap();
    let original = &artifact.execution_payload.transactions[0];
    let parsed = parse_transaction(original).unwrap();
    let mut changed_witness = original.clone();
    *changed_witness.last_mut().unwrap() = 1;
    changed_witness.push(0x40);
    let changed = parse_transaction(&changed_witness).unwrap();

    assert_eq!(parsed.hash, changed.hash);
    assert_eq!(parsed.script, changed.script);
    assert_ne!(parsed.witnesses, changed.witnesses);
}

#[test]
fn transaction_decoder_rejects_noncanonical_varint_and_trailing_bytes() {
    let artifact = parse_proof_witness_artifact(&fixture_bytes()).unwrap();
    let transaction = &artifact.execution_payload.transactions[0];

    let mut trailing = transaction.clone();
    trailing.push(0);
    assert!(matches!(
        parse_transaction(&trailing),
        Err(ExecutionError::Invalid("trailing transaction bytes"))
    ));

    let mut noncanonical = transaction.clone();
    let signer_count_offset = 1 + 4 + 8 + 8 + 4;
    noncanonical.splice(signer_count_offset..signer_count_offset + 1, [0xfd, 1, 0]);
    assert!(matches!(
        parse_transaction(&noncanonical),
        Err(ExecutionError::Invalid("non-canonical Neo varint"))
    ));
}

#[test]
fn keyed_state_root_uses_full_sorted_keys() {
    let first = StateEntry {
        key: b"a\0suffix".to_vec(),
        value: b"one".to_vec(),
    };
    let second = StateEntry {
        key: b"a\0suffix-2".to_vec(),
        value: b"two".to_vec(),
    };

    assert_ne!(
        keyed_state_root(&[first.clone(), second.clone()]),
        keyed_state_root(&[second, first])
    );
}

#[test]
fn empty_effect_collections_use_zero_uint256() {
    assert_eq!(storage_delta_hash(&[]), [0u8; 32]);
    assert_eq!(events_hash(&[]).unwrap(), [0u8; 32]);
    assert_eq!(merkle_root(&[]), [0u8; 32]);
}

#[test]
fn artifact_content_tampering_is_rejected_before_execution() {
    let mut bytes = fixture_bytes();
    bytes[100] ^= 1;
    assert!(matches!(
        parse_proof_witness_artifact(&bytes),
        Err(ExecutionError::Invalid("proof witness content hash"))
    ));
}

fn decode_hex(value: &str) -> Vec<u8> {
    assert_eq!(value.len() % 2, 0);
    value
        .as_bytes()
        .chunks_exact(2)
        .map(|chunk| (nibble(chunk[0]) << 4) | nibble(chunk[1]))
        .collect()
}

fn nibble(value: u8) -> u8 {
    match value {
        b'0'..=b'9' => value - b'0',
        b'a'..=b'f' => value - b'a' + 10,
        b'A'..=b'F' => value - b'A' + 10,
        _ => panic!("invalid fixture hex"),
    }
}
