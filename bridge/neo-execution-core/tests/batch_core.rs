use neo_execution_core::{
    CANONICAL_RECEIPT_V1_BYTES, CanonicalReceiptV1, ExecutionError, ForcedInclusionProof,
    StateEntry, encode_execution_payload, encode_proof_witness_artifact, encode_receipt,
    events_hash, hash256, keyed_state_root, merkle_root, parse_execution_payload,
    parse_proof_witness_artifact, parse_transaction, receipt_hash, storage_delta_hash,
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
fn execution_payload_round_trips_forced_inclusion_proofs_in_csharp_order() {
    let mut payload = parse_proof_witness_artifact(&fixture_bytes())
        .unwrap()
        .execution_payload;
    payload.forced_inclusions.push(ForcedInclusionProof {
        nonce: 42,
        leaf_index: 7,
        tx_hash: [0x55; 32],
        siblings: vec![[0x66; 32], [0x77; 32]],
    });

    let encoded = encode_execution_payload(&payload).unwrap();

    assert_eq!(parse_execution_payload(&encoded).unwrap(), payload);
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
    // Preserve the witness framing and mutate one signature byte only: the unsigned preimage
    // (and therefore canonical txid) must remain unchanged while the witness contents differ.
    let verification_len = 39usize;
    let verification_start = changed_witness.len() - verification_len;
    let invocation_len = usize::from(changed_witness[verification_start - 1]);
    let invocation_start = verification_start - 1 - invocation_len;
    changed_witness[invocation_start + 1] ^= 0x01;
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

/// A transaction is executable only if `valid_until_block` covers the batch's `last_block`: the
/// payload's flat transaction list carries no per-transaction block assignment, so the executing
/// height may be anywhere up to `last_block`, and Neo expires a transaction once the executing
/// height exceeds `valid_until_block`.
mod validity_window {
    use neo_execution_core::{
        BatchBlockContext, ExecutionPayload, ProtocolConfig, StateWitness, VmOutcome,
        compute_batch_with,
    };

    fn minimal_transaction(valid_until_block: u32) -> Vec<u8> {
        use sha2::Digest;
        // A real standard-account witness: signature verification is fatal per batch, so the
        // expiry checks must carry an authorized transaction to reach the window logic.
        const NETWORK: u32 = 860_833_102;
        let signing_key =
            p256::ecdsa::SigningKey::from_bytes(&[42u8; 32].into()).expect("signing key");
        let pubkey = signing_key
            .verifying_key()
            .to_encoded_point(true)
            .as_bytes()
            .to_vec();
        let mut verification = Vec::with_capacity(41);
        verification.push(0x21);
        verification.extend_from_slice(&pubkey);
        verification.push(0x41);
        verification.extend_from_slice(&[0x56, 0xe7, 0xb3, 0x27]); // CheckSig syscall

        let mut tx = Vec::new();
        tx.push(0); // version
        tx.extend_from_slice(&1u32.to_le_bytes()); // nonce
        tx.extend_from_slice(&1_000_000i64.to_le_bytes()); // system fee
        tx.extend_from_slice(&1_000_000i64.to_le_bytes()); // network fee
        tx.extend_from_slice(&valid_until_block.to_le_bytes());
        tx.push(1); // signer count
        tx.extend_from_slice(&neo_execution_core::hash160(&verification));
        tx.push(0x01); // CalledByEntry scope
        tx.push(0); // attribute count
        tx.push(1); // script length
        tx.push(0x01); // script

        let mut hasher = sha2::Sha256::new();
        hasher.update(NETWORK.to_le_bytes());
        hasher.update(neo_execution_core::inventory_hash(&tx));
        let signature = signing_key
            .sign_prehash_recoverable(&hasher.finalize())
            .expect("sign")
            .0;

        tx.push(1); // witness count
        tx.push(65); // invocation script length
        tx.push(0x40);
        let sig_bytes: [u8; 64] = signature.to_bytes().into();
        tx.extend_from_slice(&sig_bytes);
        tx.push(verification.len() as u8);
        tx.extend_from_slice(&verification);
        tx
    }

    fn payload(last_block: u64, valid_until_block: u32) -> ExecutionPayload {
        ExecutionPayload {
            chain_id: 1,
            batch_number: 1,
            first_block: 1,
            last_block,
            pre_state_root: [0u8; 32],
            block_context: BatchBlockContext {
                l1_finalized_height: 0,
                first_block_timestamp: 1_700_000_000,
                last_block_timestamp: 1_700_000_001,
                sequencer_committee_hash: [0u8; 32],
                network: 860_833_102,
            },
            l1_messages: Vec::new(),
            forced_inclusions: Vec::new(),
            transactions: vec![minimal_transaction(valid_until_block)],
        }
    }

    fn witness() -> StateWitness {
        StateWitness {
            config: ProtocolConfig {
                exec_fee_factor: 1,
                storage_price: 1,
                address_version: 0x35,
                per_tx_gas_limit: 100_000_000,
            },
            entries: Vec::new(),
            contracts: Vec::new(),
        }
    }

    #[test]
    fn expired_transaction_faults_without_executing() {
        let executor_calls = std::cell::Cell::new(0u32);
        let computed = compute_batch_with(
            &payload(10, 9),
            &witness(),
            |_payload, _witness, _state, _tx| {
                executor_calls.set(executor_calls.get() + 1);
                Ok(VmOutcome::fault(0))
            },
        )
        .unwrap();

        assert_eq!(
            executor_calls.get(),
            0,
            "an expired transaction must never reach the executor"
        );
        assert!(!computed.effects.transactions[0].receipt.success);
        assert_eq!(computed.effects.transactions[0].receipt.gas_consumed, 0);
    }

    #[test]
    fn unexpired_transaction_reaches_the_executor() {
        let executor_calls = std::cell::Cell::new(0u32);
        let computed = compute_batch_with(
            &payload(10, 10),
            &witness(),
            |_payload, _witness, _state, tx| {
                executor_calls.set(executor_calls.get() + 1);
                assert_eq!(tx.valid_until_block, 10);
                Ok(VmOutcome::fault(0))
            },
        )
        .unwrap();

        assert_eq!(executor_calls.get(), 1);
        assert!(!computed.effects.transactions[0].receipt.success);
    }
}

fn nibble(value: u8) -> u8 {
    match value {
        b'0'..=b'9' => value - b'0',
        b'a'..=b'f' => value - b'a' + 10,
        b'A'..=b'F' => value - b'A' + 10,
        _ => panic!("invalid fixture hex"),
    }
}
