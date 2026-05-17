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
    let manifest = std::fs::read_to_string(
        std::path::Path::new(env!("CARGO_MANIFEST_DIR")).join("Cargo.toml"),
    )
    .expect("read Cargo.toml");

    assert!(!manifest.contains("sp1"));
    assert!(!manifest.contains("polkavm"));
    assert!(!manifest.contains("neo-vm-guest"));
}
