#![no_std]

extern crate alloc;

mod runtime;

pub use neo_execution_core::{BatchResult, ExecutionError};

pub fn execute_batch(input: &[u8]) -> Result<BatchResult, ExecutionError> {
    neo_execution_core::verify_artifact_with(input, runtime::execute_transaction)
}

pub fn compute_batch(
    payload: &neo_execution_core::ExecutionPayload,
    witness: &neo_execution_core::StateWitness,
) -> Result<neo_execution_core::ComputedBatch, ExecutionError> {
    neo_execution_core::compute_batch_with(payload, witness, runtime::execute_transaction)
}

pub fn compute_batch_transition(
    payload: &neo_execution_core::ExecutionPayload,
    witness: &neo_execution_core::StateWitness,
) -> Result<neo_execution_core::ComputedBatchTransition, ExecutionError> {
    neo_execution_core::compute_batch_transition_with(
        payload,
        witness,
        runtime::execute_transaction,
    )
}
