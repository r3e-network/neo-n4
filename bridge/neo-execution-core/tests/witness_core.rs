//! Witness authorization tests: every witness must be verified against its signer before any
//! execution. Only standard Neo N3 account shapes are acceptable — single-signature and
//! single-byte-count m-of-n multi-signature — signed over `SHA256(network ‖ txHash)`. An
//! unverifiable witness is a fatal protocol error, never a "trusted signer".

use neo_execution_core::{
    BatchBlockContext, ExecutionPayload, ExecutionError, ProtocolConfig, StateWitness,
    VmOutcome, compute_batch_with, inventory_hash,
};
use p256::ecdsa::SigningKey;
use sha2::{Digest, Sha256};

const NETWORK: u32 = 860_833_102;

/// Deterministic test key (scalar 42) and its 33-byte compressed public key.
fn signing_key(seed: u8) -> SigningKey {
    SigningKey::from_bytes(&[seed; 32].into()).expect("valid signing key")
}

fn compressed_pubkey(key: &SigningKey) -> Vec<u8> {
    key.verifying_key().to_encoded_point(true).as_bytes().to_vec()
}

fn single_sig_verification_script(pubkey: &[u8]) -> Vec<u8> {
    let mut script = Vec::with_capacity(41);
    script.push(0x21);
    script.extend_from_slice(pubkey);
    script.push(0x41); // Neo3 OpCode.SYSCALL
    script.extend_from_slice(&[0x56, 0xe7, 0xb3, 0x27]); // System.Crypto.CheckSig
    script
}

fn multisig_verification_script(keys: &[SigningKey], m: usize) -> Vec<u8> {
    let mut script = Vec::new();
    script.push(0x50 + m as u8);
    for key in keys {
        script.push(0x21);
        script.extend_from_slice(&compressed_pubkey(key));
    }
    script.push(0x50 + keys.len() as u8);
    script.push(0x41); // Neo3 OpCode.SYSCALL
    script.extend_from_slice(&[0x9e, 0xd0, 0xdc, 0x3a]); // System.Crypto.CheckMultisig
    script
}

/// Sign `SHA256(network ‖ txHash)` — the Neo sign data for the transaction's unsigned preimage.
fn sign_message(key: &SigningKey, unsigned_tx_hash: &[u8; 32]) -> [u8; 64] {
    let mut hasher = Sha256::new();
    hasher.update(NETWORK.to_le_bytes());
    hasher.update(unsigned_tx_hash);
    let digest = hasher.finalize();
    let (signature, _) = key.sign_prehash_recoverable(&digest).expect("sign");
    let bytes: [u8; 64] = signature.to_bytes().into();
    bytes
}

fn unsigned_preimage(nonce: u32, signer_account: [u8; 20]) -> Vec<u8> {
    let mut unsigned = Vec::new();
    unsigned.push(0); // version
    unsigned.extend_from_slice(&nonce.to_le_bytes());
    unsigned.extend_from_slice(&1_000_000i64.to_le_bytes()); // system fee
    unsigned.extend_from_slice(&1_000_000i64.to_le_bytes()); // network fee
    unsigned.extend_from_slice(&10_000u32.to_le_bytes()); // valid until block
    unsigned.push(1); // signer count
    unsigned.extend_from_slice(&signer_account);
    unsigned.push(0x01); // CalledByEntry scope
    unsigned.push(0); // attribute count
    unsigned.push(1); // script length
    unsigned.push(0x01); // script
    unsigned
}

fn unsigned_hash(nonce: u32, signer_account: [u8; 20]) -> [u8; 32] {
    inventory_hash(&unsigned_preimage(nonce, signer_account)).into()
}

fn build_transaction(
    nonce: u32,
    signer_account: [u8; 20],
    verification_script: &[u8],
    signatures: &[&[u8]],
) -> Vec<u8> {
    let unsigned = unsigned_preimage(nonce, signer_account);

    let mut invocation = Vec::new();
    for signature in signatures {
        invocation.push(0x40);
        invocation.extend_from_slice(signature);
    }

    let mut tx = unsigned;
    tx.push(1); // witness count
    tx.push(invocation.len() as u8);
    tx.extend_from_slice(&invocation);
    tx.push(verification_script.len() as u8);
    tx.extend_from_slice(verification_script);
    tx
}

fn transaction_with_single_sig(key: &SigningKey) -> Vec<u8> {
    let verification = single_sig_verification_script(&compressed_pubkey(&key));
    let account: [u8; 20] = neo_execution_core::hash160(&verification).into();
    let signature = sign_message(key, &unsigned_hash(1, account));
    build_transaction(1, account, &verification, &[&signature])
}

fn payload(transactions: Vec<Vec<u8>>) -> ExecutionPayload {
    ExecutionPayload {
        chain_id: 1,
        batch_number: 1,
        first_block: 1,
        last_block: 10_000,
        pre_state_root: [0u8; 32],
        block_context: BatchBlockContext {
            l1_finalized_height: 0,
            first_block_timestamp: 1_700_000_000,
            last_block_timestamp: 1_700_000_001,
            sequencer_committee_hash: [0u8; 32],
            network: NETWORK,
        },
        l1_messages: Vec::new(),
        forced_inclusions: Vec::new(),
        transactions,
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
fn single_signature_witness_authorizes_execution() {
    let key = signing_key(42);
    let executor_calls = std::cell::Cell::new(0u32);
    let computed = compute_batch_with(
        &payload(vec![transaction_with_single_sig(&key)]),
        &witness(),
        |_payload, _witness, _state, _tx| {
            executor_calls.set(executor_calls.get() + 1);
            Ok(VmOutcome::fault(0))
        },
    )
    .expect("witness-authorized batch computes");

    assert_eq!(executor_calls.get(), 1, "authorized transaction executes");
    assert_eq!(computed.effects.transactions.len(), 1);
}

#[test]
fn tampered_signature_is_fatal() {
    let key = signing_key(42);
    let mut transaction = transaction_with_single_sig(&key);
    let signature_start = transaction.len() - 41 - 65 + 1;
    transaction[signature_start] ^= 0x01;

    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        panic!("unauthorized transaction must never execute")
    });
    assert!(matches!(
        result,
        Err(ExecutionError::Invalid("transaction witness"))
    ));
}

#[test]
fn account_binding_mismatch_is_fatal() {
    let key = signing_key(42);
    let verification = single_sig_verification_script(&compressed_pubkey(&key));
    let signature = sign_message(&key, &[0x11; 32]);
    let wrong_account = [0x99u8; 20];
    let transaction = build_transaction(1, wrong_account, &verification, &[&signature]);

    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        panic!("unauthorized transaction must never execute")
    });
    assert!(matches!(
        result,
        Err(ExecutionError::Invalid("transaction witness account"))
    ));
}

#[test]
fn non_standard_witness_script_is_unsupported() {
    // The account binding passes (the account is derived from the script), so the rejection
    // comes from the script-shape gate, not the binding.
    let script = [0x61u8];
    let account: [u8; 20] = neo_execution_core::hash160(&script).into();
    let transaction = build_transaction(1, account, &script, &[]);
    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        panic!("unauthorized transaction must never execute")
    });
    assert!(matches!(
        result,
        Err(ExecutionError::Unsupported("transaction witness script shape"))
    ));
}

#[test]
fn multisig_witness_authorizes_execution() {
    let keys = [signing_key(1), signing_key(2)];
    let verification = multisig_verification_script(&keys, 2);
    let account: [u8; 20] = neo_execution_core::hash160(&verification).into();
    let digest_input = unsigned_hash(2, account);
    let signatures: Vec<Vec<u8>> = keys
        .iter()
        .map(|key| sign_message(key, &digest_input).to_vec())
        .collect();
    let signature_refs: Vec<&[u8]> = signatures.iter().map(|s| s.as_slice()).collect();
    let transaction = build_transaction(2, account, &verification, &signature_refs);

    let executor_calls = std::cell::Cell::new(0u32);
    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        executor_calls.set(executor_calls.get() + 1);
        Ok(VmOutcome::fault(0))
    });
    assert!(result.is_ok(), "valid 2-of-2 witness authorizes execution");
    assert_eq!(executor_calls.get(), 1);
}

#[test]
fn multisig_one_of_two_authorizes_with_first_key() {
    let keys = [signing_key(1), signing_key(2)];
    let verification = multisig_verification_script(&keys, 1);
    let account: [u8; 20] = neo_execution_core::hash160(&verification).into();
    let digest_input = unsigned_hash(4, account);
    let s1 = sign_message(&keys[0], &digest_input);
    let transaction = build_transaction(4, account, &verification, &[&s1]);

    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        Ok(VmOutcome::fault(0))
    });
    assert!(result.is_ok(), "valid 1-of-2 witness authorizes execution");
}

#[test]
fn multisig_two_of_three_authorizes() {
    let keys = [signing_key(1), signing_key(2), signing_key(3)];
    let verification = multisig_verification_script(&keys, 2);
    let account: [u8; 20] = neo_execution_core::hash160(&verification).into();
    let digest_input = unsigned_hash(5, account);
    let s1 = sign_message(&keys[0], &digest_input).to_vec();
    let s3 = sign_message(&keys[2], &digest_input).to_vec();
    let transaction = build_transaction(5, account, &verification, &[&s1, &s3]);

    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        Ok(VmOutcome::fault(0))
    });
    assert!(result.is_ok(), "valid 2-of-3 witness authorizes execution");
}

#[test]
fn wrong_signer_signature_is_fatal() {
    let keys = [signing_key(1), signing_key(2)];
    let outsider = signing_key(9);
    let verification = multisig_verification_script(&keys, 2);
    let account: [u8; 20] = neo_execution_core::hash160(&verification).into();
    let unsigned_hash: [u8; 32] = unsigned_hash(3, account);
    // One valid participant signature + one from outside the published key set: the greedy
    // match can never reach the threshold.
    let s1 = sign_message(&keys[0], &unsigned_hash).to_vec();
    let s_bad = sign_message(&outsider, &unsigned_hash).to_vec();
    let transaction = build_transaction(3, account, &verification, &[&s1, &s_bad]);

    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        panic!("unauthorized transaction must never execute")
    });
    assert!(matches!(
        result,
        Err(ExecutionError::Invalid("transaction witness"))
    ));
}
