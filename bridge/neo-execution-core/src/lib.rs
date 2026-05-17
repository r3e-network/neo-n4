#![no_std]
//! Backend-agnostic Neo L2 batch execution primitives.
//!
//! This crate is intentionally smaller than either execution backend:
//! it knows how to parse canonical batch bytes, fold L1 messages, compute
//! transaction/receipt Merkle roots, and build the public-input commitment.
//! It does not execute NeoVM bytecode itself and depends on neither SP1 nor
//! PolkaVM. Callers provide a small per-transaction execution receipt from
//! their backend of choice.

extern crate alloc;

mod batch;
mod hashing;
mod types;
mod wire;

pub use batch::execute_batch_with;
pub use hashing::{
    apply_l1_message, fold_state_root, hash256, hash_public_inputs, hash_receipt, merkle_root,
};
pub use types::{
    BatchRequest, BatchResult, ExecutionError, L1Message, VmExecutionReceipt, BATCH_WIRE_VERSION,
    DEFAULT_PER_TX_GAS_LIMIT,
};
pub use wire::parse_batch_request;
