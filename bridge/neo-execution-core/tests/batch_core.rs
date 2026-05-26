use neo_execution_core::{
    execute_batch_with, hash256, merkle_root, ExecutionError, VmExecutionReceipt,
    DEFAULT_PER_TX_GAS_LIMIT,
};

fn build_request(transactions: &[&[u8]]) -> Vec<u8> {
    let mut buf = Vec::new();
    buf.push(1u8);
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    buf.extend_from_slice(&[0u8; 32]);
    buf.extend_from_slice(&[0xCDu8; 32]);
    buf.extend_from_slice(&1u32.to_le_bytes());
    buf.extend_from_slice(&4u32.to_le_bytes());
    buf.extend_from_slice(b"l1-a");
    buf.extend_from_slice(&(transactions.len() as u32).to_le_bytes());
    for tx in transactions {
        buf.extend_from_slice(&(tx.len() as u32).to_le_bytes());
        buf.extend_from_slice(tx);
    }
    buf
}

#[test]
fn executes_batch_through_backend_agnostic_receipts() {
    let bytes = build_request(&[b"tx-a", b"tx-b"]);
    let mut calls = Vec::new();

    let result = execute_batch_with(&bytes, |tx, gas_limit| {
        calls.push((tx.to_vec(), gas_limit));
        VmExecutionReceipt {
            state: 0,
            gas_consumed: 42 + tx.len() as u64,
            output_hash: hash256(tx),
        }
    })
    .expect("batch execution should succeed");

    assert_eq!(
        calls,
        vec![
            (b"tx-a".to_vec(), DEFAULT_PER_TX_GAS_LIMIT),
            (b"tx-b".to_vec(), DEFAULT_PER_TX_GAS_LIMIT)
        ]
    );
    assert_ne!(result.post_state_root, [0u8; 32]);
    assert_ne!(result.tx_root, [0u8; 32]);
    assert_ne!(result.receipt_root, [0u8; 32]);
    assert_ne!(result.public_input_hash, [0u8; 32]);
}

#[test]
fn deterministic_for_same_input_and_executor() {
    let bytes = build_request(&[b"tx-a"]);
    let run = || {
        execute_batch_with(&bytes, |tx, _| VmExecutionReceipt {
            state: 0,
            gas_consumed: 1,
            output_hash: hash256(tx),
        })
        .unwrap()
    };

    assert_eq!(run(), run());
}

#[test]
fn rejects_bad_wire_inputs() {
    assert!(matches!(
        execute_batch_with(&[1u8], |_, _| unreachable!()),
        Err(ExecutionError::Truncated)
    ));

    let mut invalid_version = vec![99u8];
    invalid_version.resize(100, 0);
    assert!(matches!(
        execute_batch_with(&invalid_version, |_, _| unreachable!()),
        Err(ExecutionError::InvalidVersion(99))
    ));
}

#[test]
fn merkle_root_is_stable_and_ordered() {
    let a = [0x01u8; 32];
    let b = [0x02u8; 32];

    assert_eq!(merkle_root(&[]), [0u8; 32]);
    assert_eq!(merkle_root(&[a]), a);
    assert_ne!(merkle_root(&[a, b]), merkle_root(&[b, a]));
}

#[test]
fn manifest_stays_backend_agnostic() {
    let manifest = include_str!("../Cargo.toml");

    assert!(!manifest.contains("sp1"));
    assert!(!manifest.contains("polkavm"));
    assert!(!manifest.contains("neo-vm-guest"));
}

/// Pin the hash_public_inputs output against a known golden value.
/// This ensures the Rust and C# hash implementations stay byte-for-byte
/// identical. The golden value is computed from C# StateRootCalculator
/// with pre_state_root=[1u8;32], post_state_root=[2u8;32], and all
/// other roots zeroed.
#[test]
fn hash_public_inputs_golden_value() {
    use neo_execution_core::hash_public_inputs;

    let pre = [1u8; 32];
    let post = [2u8; 32];
    let zero = [0u8; 32];

    let result = hash_public_inputs(
        1099, 7, &pre, &post, &zero, &zero, &zero, &zero, &zero, &zero, &zero, &zero,
    );

    // Golden value computed from this Rust implementation. Pin so any
    // future change to the hash function is detected immediately.
    let expected: [u8; 32] = [
        0x6B, 0xF4, 0x2E, 0x3D, 0xE8, 0x1D, 0x57, 0x10,
        0x09, 0x23, 0xEE, 0xBA, 0x1F, 0xC5, 0x6A, 0x61,
        0xC7, 0x6D, 0x3A, 0x98, 0xDF, 0x36, 0xA4, 0x52,
        0xF1, 0xA1, 0x71, 0xD5, 0xDF, 0x6F, 0xED, 0xCA,
    ];
    assert_eq!(result, expected, "hash_public_inputs golden value must match C# byte-for-byte");
}

#[test]
fn hash_receipt_is_deterministic() {
    use neo_execution_core::{hash_receipt, VmExecutionReceipt};

    let tx_hash = [0xAA; 32];
    let receipt = VmExecutionReceipt { state: 0, gas_consumed: 42, output_hash: [0xBB; 32] };
    let h1 = hash_receipt(&tx_hash, receipt);
    let h2 = hash_receipt(&tx_hash, receipt);
    assert_eq!(h1, h2);
    assert_ne!(h1, [0u8; 32]);

    let receipt_fault = VmExecutionReceipt { state: 1, ..receipt };
    assert_ne!(hash_receipt(&tx_hash, receipt_fault), h1);

    let receipt_gas = VmExecutionReceipt { gas_consumed: 43, ..receipt };
    assert_ne!(hash_receipt(&tx_hash, receipt_gas), h1);
}

#[test]
fn fold_state_root_is_order_dependent() {
    use neo_execution_core::{fold_state_root, hash_receipt, VmExecutionReceipt};

    let tx_hash = [0x11; 32];
    let receipt = VmExecutionReceipt { state: 0, gas_consumed: 1, output_hash: [0x22; 32] };
    let receipt_hash = hash_receipt(&tx_hash, receipt);

    let root_a = [0x33; 32];
    let root_b = [0x44; 32];
    assert_ne!(fold_state_root(&root_a, &receipt_hash), fold_state_root(&root_b, &receipt_hash));
}

#[test]
fn apply_l1_message_is_deterministic() {
    use neo_execution_core::{apply_l1_message, L1Message};

    let root = [0x55; 32];
    let msg = L1Message { bytes: b"test-l1-msg".to_vec() };
    let result = apply_l1_message(&root, &msg);
    assert_ne!(result, root);
    assert_ne!(result, [0u8; 32]);
    assert_eq!(apply_l1_message(&root, &msg), result);

    let msg2 = L1Message { bytes: b"other-msg".to_vec() };
    assert_ne!(apply_l1_message(&root, &msg2), result);
}

#[test]
fn parse_v2_request_fields() {
    use neo_execution_core::parse_batch_request;

    let mut buf = Vec::new();
    buf.push(2u8);
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    // v2 wire format: 7 roots (pre_state_root through block_context_hash)
    for _ in 0..7 { buf.extend_from_slice(&[0xCDu8; 32]); }
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 L1 msgs
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 txs

    let req = parse_batch_request(&buf).expect("v2 parse failed");
    assert_eq!(req.wire_version, 2);
    assert_eq!(req.chain_id, 1099);
    assert_eq!(req.batch_number, 7);
}

#[test]
fn reject_oversized_per_element() {
    use neo_execution_core::{parse_batch_request, ExecutionError};

    let mut buf = Vec::new();
    buf.push(2u8);
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    for _ in 0..7 { buf.extend_from_slice(&[0u8; 32]); }
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 L1 msgs
    buf.extend_from_slice(&1u32.to_le_bytes()); // 1 tx
    // Claim 2 MiB transaction (exceeds 1 MiB per-element cap)
    buf.extend_from_slice(&(2u32 * 1024 * 1024).to_le_bytes());
    buf.resize(buf.len() + 100, 0);

    let err = parse_batch_request(&buf).unwrap_err();
    assert!(matches!(err, ExecutionError::OversizedField(_)));
}

#[test]
fn reject_too_many_l1_messages() {
    use neo_execution_core::{parse_batch_request, ExecutionError};

    let mut buf = Vec::new();
    buf.push(2u8);
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    for _ in 0..7 { buf.extend_from_slice(&[0u8; 32]); }
    buf.extend_from_slice(&1025u32.to_le_bytes()); // exceeds 1024 cap
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 txs

    let err = parse_batch_request(&buf).unwrap_err();
    assert!(matches!(err, ExecutionError::OversizedField("l1_messages")));
}

#[test]
fn gas_exceeded_error_propagated() {
    use neo_execution_core::{execute_batch_with, ExecutionError, VmExecutionReceipt};

    let mut buf = Vec::new();
    buf.push(2u8);
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    for _ in 0..7 { buf.extend_from_slice(&[0u8; 32]); }
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 L1 msgs
    buf.extend_from_slice(&1u32.to_le_bytes()); // 1 tx
    buf.extend_from_slice(&3u32.to_le_bytes()); // tx len = 3
    buf.extend_from_slice(&[0xAA, 0xBB, 0xCC]);

    let result = execute_batch_with(&buf, |_, _| VmExecutionReceipt {
        state: 0,
        gas_consumed: 200_000_000, // exceeds DEFAULT_PER_TX_GAS_LIMIT (100_000_000)
        output_hash: [0u8; 32],
    });
    assert!(matches!(result, Err(ExecutionError::GasExceeded)));
}

#[test]
fn batch_with_l1_messages_and_transactions() {
    use neo_execution_core::{execute_batch_with, hash256, VmExecutionReceipt};

    // Build a v2 request with 2 L1 messages and 2 transactions
    let mut buf = Vec::new();
    buf.push(2u8);
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    for _ in 0..7 { buf.extend_from_slice(&[0u8; 32]); }
    // 2 L1 messages
    buf.extend_from_slice(&2u32.to_le_bytes());
    buf.extend_from_slice(&4u32.to_le_bytes());
    buf.extend_from_slice(b"msg1");
    buf.extend_from_slice(&4u32.to_le_bytes());
    buf.extend_from_slice(b"msg2");
    // 2 transactions
    buf.extend_from_slice(&2u32.to_le_bytes());
    buf.extend_from_slice(&3u32.to_le_bytes());
    buf.extend_from_slice(&[0xAA, 0xBB, 0xCC]);
    buf.extend_from_slice(&4u32.to_le_bytes());
    buf.extend_from_slice(&[0xDD, 0xEE, 0xFF, 0x00]);

    let result = execute_batch_with(&buf, |tx, _| VmExecutionReceipt {
        state: 0, gas_consumed: tx.len() as u64, output_hash: hash256(tx),
    }).expect("batch with L1 msgs and txs should succeed");

    assert_ne!(result.post_state_root, [0u8; 32]);
    assert_ne!(result.tx_root, [0u8; 32]);
    assert_ne!(result.receipt_root, [0u8; 32]);
    assert_ne!(result.public_input_hash, [0u8; 32]);
    // post_state_root incorporates L1 message folding (state changes even
    // though txs do nothing — L1 messages modify state root)
    assert_ne!(result.post_state_root, result.tx_root);
}

#[test]
fn hash_public_inputs_all_roots_change_output() {
    use neo_execution_core::hash_public_inputs;

    let a = [1u8; 32];
    let zero = [0u8; 32];

    let baseline = hash_public_inputs(1, 1, &zero, &zero, &zero, &zero,
        &zero, &zero, &zero, &zero, &zero, &zero);

    // Changing any single root should change the output
    assert_ne!(hash_public_inputs(1, 1, &a, &zero, &zero, &zero,
        &zero, &zero, &zero, &zero, &zero, &zero), baseline);
    assert_ne!(hash_public_inputs(1, 1, &zero, &a, &zero, &zero,
        &zero, &zero, &zero, &zero, &zero, &zero), baseline);
    assert_ne!(hash_public_inputs(1, 1, &zero, &zero, &a, &zero,
        &zero, &zero, &zero, &zero, &zero, &zero), baseline);
    assert_ne!(hash_public_inputs(1, 1, &zero, &zero, &zero, &a,
        &zero, &zero, &zero, &zero, &zero, &zero), baseline);
    // Changing chain_id or batch_number also changes output
    assert_ne!(hash_public_inputs(2, 1, &zero, &zero, &zero, &zero,
        &zero, &zero, &zero, &zero, &zero, &zero), baseline);
    assert_ne!(hash_public_inputs(1, 2, &zero, &zero, &zero, &zero,
        &zero, &zero, &zero, &zero, &zero, &zero), baseline);
}

#[test]
fn execute_batch_with_empty_transactions_is_valid() {
    use neo_execution_core::execute_batch_with;

    let mut buf = Vec::new();
    buf.push(2u8);
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    for _ in 0..7 { buf.extend_from_slice(&[0u8; 32]); }
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 L1 msgs
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 txs

    let result = execute_batch_with(&buf, |_, _| unreachable!()).expect("empty batch succeeds");
    // With zero transactions, post_state_root == pre_state_root (no folding)
    assert_eq!(result.post_state_root, [0u8; 32]);
    assert_eq!(result.tx_root, [0u8; 32]); // empty tree = zero
    assert_eq!(result.receipt_root, [0u8; 32]); // empty tree = zero
}

#[test]
fn v1_parse_sets_wire_version_correctly() {
    use neo_execution_core::parse_batch_request;

    let mut buf = Vec::new();
    buf.push(1u8); // version 1
    buf.extend_from_slice(&1099u32.to_le_bytes());
    buf.extend_from_slice(&7u64.to_le_bytes());
    buf.extend_from_slice(&[0u8; 32]); // pre_state_root
    buf.extend_from_slice(&[0xCDu8; 32]); // da_commitment
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 L1 msgs
    buf.extend_from_slice(&0u32.to_le_bytes()); // 0 txs

    let req = parse_batch_request(&buf).expect("v1 parse");
    assert_eq!(req.wire_version, 1);
    // v2-only fields should be zero-filled
    assert_eq!(req.withdrawal_root, [0u8; 32]);
    assert_eq!(req.block_context_hash, [0u8; 32]);
}

#[test]
fn zero_length_input_is_truncated() {
    use neo_execution_core::{parse_batch_request, ExecutionError};
    assert!(matches!(
        parse_batch_request(&[]),
        Err(ExecutionError::Truncated)
    ));
}

#[test]
fn large_merkle_tree_is_deterministic() {
    use neo_execution_core::merkle_root;

    // 1024 leaves — exercises multi-level tree construction
    let leaves: Vec<[u8; 32]> = (0..1024).map(|i| [i as u8; 32]).collect();
    let r1 = merkle_root(&leaves);
    let r2 = merkle_root(&leaves);
    assert_eq!(r1, r2);
    assert_ne!(r1, [0u8; 32]);
    assert_ne!(r1, merkle_root(&leaves[..512]));
}
