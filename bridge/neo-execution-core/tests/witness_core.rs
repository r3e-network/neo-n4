//! Witness authorization tests: every witness must be verified against its signer before any
//! execution. Only standard fork-VM account shapes are acceptable — single-signature and
//! constant-push m-of-n multi-signature, all using the fork's PUSHDATA1 encoding — signed
//! over `SHA256(network ‖ txHash)`. An unverifiable witness is a fatal protocol error,
//! never a "trusted signer".

use neo_execution_core::{
    BatchBlockContext, ExecutionError, ExecutionPayload, ProtocolConfig, StateWitness, VmOutcome,
    compute_batch_with, inventory_hash,
};
use p256::ecdsa::SigningKey;
use sha2::{Digest, Sha256};

const NETWORK: u32 = 860_833_102;

/// Deterministic test key (scalar 42) and its 33-byte compressed public key.
fn signing_key(seed: u8) -> SigningKey {
    SigningKey::from_bytes(&[seed; 32].into()).expect("valid signing key")
}

fn compressed_pubkey(key: &SigningKey) -> Vec<u8> {
    key.verifying_key()
        .to_encoded_point(true)
        .as_bytes()
        .to_vec()
}

/// The FORK-VM dialect: PUSHBYTES1..75 was replaced by Bitcoin-style PUSHDATA1/2/4, so
/// `Contract.CreateSignatureRedeemScript` emits `PUSHDATA1 0x21 <pubkey> SYSCALL <hash>`
/// (40 bytes). The stock-N3 shape (`0x21 <pubkey> ...`, 39 bytes) is NOT a CheckSig script
/// in this VM: 0x21 is NOP here.
fn single_sig_verification_script(pubkey: &[u8]) -> Vec<u8> {
    let mut script = Vec::with_capacity(40);
    script.push(0x0C); // fork OpCode.PUSHDATA1
    script.push(0x21); // length: 33
    script.extend_from_slice(pubkey);
    script.push(0x41); // OpCode.SYSCALL
    script.extend_from_slice(&[0x56, 0xe7, 0xb3, 0x27]); // System.Crypto.CheckSig
    script
}

/// Fork dialect multisig: `PUSHm | n×[PUSHDATA1 <pubkey>] | PUSHn | SYSCALL <hash>` where
/// PUSH1..PUSH16 are 0x11..0x20 (the stock-N3 0x51..0x60 bytes mean different instructions
/// in the fork's opcode table).
fn multisig_verification_script(keys: &[SigningKey], m: usize) -> Vec<u8> {
    let mut script = Vec::new();
    script.push(0x10 + m as u8); // fork PUSH1..PUSH16
    for key in keys {
        script.push(0x0C); // PUSHDATA1
        script.push(0x21); // length: 33
        script.extend_from_slice(&compressed_pubkey(key));
    }
    script.push(0x10 + keys.len() as u8); // fork PUSHn
    script.push(0x41); // OpCode.SYSCALL
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
    inventory_hash(&unsigned_preimage(nonce, signer_account))
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
        invocation.push(0x0C); // fork OpCode.PUSHDATA1
        invocation.push(0x40); // length: 64
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
    let verification = single_sig_verification_script(&compressed_pubkey(key));
    let account: [u8; 20] = neo_execution_core::hash160(&verification);
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
    let account: [u8; 20] = neo_execution_core::hash160(&script);
    let transaction = build_transaction(1, account, &script, &[]);
    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        panic!("unauthorized transaction must never execute")
    });
    assert!(matches!(
        result,
        Err(ExecutionError::Unsupported(
            "transaction witness script shape"
        ))
    ));
}

#[test]
fn multisig_witness_authorizes_execution() {
    let keys = [signing_key(1), signing_key(2)];
    let verification = multisig_verification_script(&keys, 2);
    let account: [u8; 20] = neo_execution_core::hash160(&verification);
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
    let account: [u8; 20] = neo_execution_core::hash160(&verification);
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
    let account: [u8; 20] = neo_execution_core::hash160(&verification);
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
    let account: [u8; 20] = neo_execution_core::hash160(&verification);
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

#[test]
fn csharp_redeem_script_bytes_parse_in_the_fork_dialect() {
    // Cross-language dialect anchor: these are the EXACT 40 bytes produced by the C# core's
    // `Contract.CreateSignatureRedeemScript` for the key with private scalar 7 (dumped from
    // external/neo's ScriptBuilder on 2026-10-23). The zkVM witness parser must accept the
    // C# ScriptBuilder's actual output byte for byte — the batch it verifies is built by the
    // C# side, and the fork's opcode table (PUSHDATA1 = 0x0C, PUSHBYTES1..75 removed) makes
    // the stock-N3 shape meaningless here (0x21 is NOP in this VM).
    let verification: [u8; 40] = [
        0x0C, 0x21, // PUSHDATA1, length 33
        0x02, 0x8E, 0x53, 0x3B, 0x6F, 0xA0, 0xBF, 0x7B, 0x46, 0x25, 0xBB, 0x30, 0x66, 0x7C, 0x01,
        0xFB, 0x60, 0x7E, 0xF9, 0xF8, 0xB8, 0xA8, 0x0F, 0xEF, 0x5B, 0x30, 0x06, 0x28, 0x70, 0x31,
        0x87, 0xB2, 0xA3, // compressed secp256r1 public key
        0x41, 0x56, 0xE7, 0xB3, 0x27, // SYSCALL System.Crypto.CheckSig
    ];
    let mut scalar = [0u8; 32];
    scalar[31] = 7; // the same private scalar the C# dump used
    let key = SigningKey::from_bytes(&scalar.into()).expect("valid signing key");
    assert_eq!(
        key.verifying_key().to_encoded_point(true).as_bytes(),
        &verification[2..35],
        "the pinned script must carry the public key of scalar 7"
    );
    let account: [u8; 20] = neo_execution_core::hash160(&verification);
    let signature = sign_message(&key, &unsigned_hash(1, account));
    let transaction = build_transaction(1, account, &verification, &[&signature]);
    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        Ok(VmOutcome::fault(0))
    });
    assert!(
        result.is_ok(),
        "the C# ScriptBuilder's actual redeem-script bytes must verify: {result:?}"
    );
}

#[test]
fn stock_n3_witness_shape_is_rejected_in_the_fork_dialect() {
    // The pre-fork Neo N3 encoding (`0x21 <pubkey> 0x41 <hash>`, 39 bytes) is NOT a
    // CheckSig script in the fork's VM — 0x21 is NOP there — so accepting it would let a
    // batch carry witnesses the actual VM cannot execute. The parser must reject the
    // stock shape outright (fail-closed on dialect, not just on validity).
    let mut script = Vec::with_capacity(39);
    let key = signing_key(42);
    script.push(0x21); // stock PUSHBYTES33 — NOP in the fork
    script.extend_from_slice(&compressed_pubkey(&key));
    script.push(0x41);
    script.extend_from_slice(&[0x56, 0xe7, 0xb3, 0x27]);
    let account: [u8; 20] = neo_execution_core::hash160(&script);
    let signature = sign_message(&key, &unsigned_hash(1, account));
    let transaction = build_transaction(1, account, &script, &[&signature]);
    let result = compute_batch_with(&payload(vec![transaction]), &witness(), |_, _, _, _| {
        panic!("stock-N3 witness shape must never execute")
    });
    assert!(matches!(
        result,
        Err(ExecutionError::Unsupported(
            "transaction witness script shape"
        ))
    ));
}
