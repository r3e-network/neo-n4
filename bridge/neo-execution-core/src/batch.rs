use alloc::{
    collections::{BTreeMap, BTreeSet},
    vec::Vec,
};

use crate::{
    hashing::{
        events_hash, hash_block_context, hash_l1_messages, hash_public_inputs, hash256,
        keyed_state_root_from_map, merkle_root, receipt_hash, storage_delta_hash,
    },
    native::{apply_l1_inbox_v1, derive_outbound_roots_v1},
    transaction::parse_transaction,
    types::{
        BatchEffects, BatchExecutionResult, BatchResult, CanonicalReceiptV1, ComputedBatch,
        ComputedBatchTransition, ExecutionError, ExecutionPayload, ParsedTransaction,
        ProofWitnessArtifact, PublicInputs, StateEntry, StateWitness, StorageDelta,
        StorageOperation, TransactionEffects, UInt160, VmOutcome,
    },
    wire::{
        encode_batch_effects, encode_execution_payload, encode_state_witness,
        parse_proof_witness_artifact,
    },
};

struct ComputedBatchState {
    batch: ComputedBatch,
    state: BTreeMap<Vec<u8>, Vec<u8>>,
}

pub fn compute_batch_with<F>(
    payload: &ExecutionPayload,
    witness: &StateWitness,
    execute_transaction: F,
) -> Result<ComputedBatch, ExecutionError>
where
    F: FnMut(
        &ExecutionPayload,
        &StateWitness,
        &alloc::collections::BTreeMap<Vec<u8>, Vec<u8>>,
        &ParsedTransaction,
    ) -> Result<VmOutcome, ExecutionError>,
{
    Ok(compute_batch_state_with(payload, witness, execute_transaction)?.batch)
}

pub fn compute_batch_transition_with<F>(
    payload: &ExecutionPayload,
    witness: &StateWitness,
    execute_transaction: F,
) -> Result<ComputedBatchTransition, ExecutionError>
where
    F: FnMut(
        &ExecutionPayload,
        &StateWitness,
        &BTreeMap<Vec<u8>, Vec<u8>>,
        &ParsedTransaction,
    ) -> Result<VmOutcome, ExecutionError>,
{
    let ComputedBatchState { batch, state } =
        compute_batch_state_with(payload, witness, execute_transaction)?;
    let post_state_witness = StateWitness {
        config: witness.config.clone(),
        entries: state
            .into_iter()
            .map(|(key, value)| StateEntry { key, value })
            .collect(),
        contracts: witness.contracts.clone(),
    };
    let post_state_witness_bytes = encode_state_witness(&post_state_witness)?;
    Ok(ComputedBatchTransition {
        batch,
        post_state_witness_bytes,
    })
}

fn compute_batch_state_with<F>(
    payload: &ExecutionPayload,
    witness: &StateWitness,
    mut execute_transaction: F,
) -> Result<ComputedBatchState, ExecutionError>
where
    F: FnMut(
        &ExecutionPayload,
        &StateWitness,
        &BTreeMap<Vec<u8>, Vec<u8>>,
        &ParsedTransaction,
    ) -> Result<VmOutcome, ExecutionError>,
{
    let mut state = witness.state_map();
    if keyed_state_root_from_map(&state) != payload.pre_state_root {
        return Err(ExecutionError::Invalid("pre-state root"));
    }
    apply_l1_inbox_v1(payload, &mut state)?;

    let transactions = payload
        .transactions
        .iter()
        .map(|bytes| parse_transaction(bytes))
        .collect::<Result<Vec<_>, _>>()?;
    let mut nonce_keys = BTreeSet::<(UInt160, u32)>::new();
    let mut transaction_hashes = Vec::with_capacity(transactions.len());
    let mut receipt_hashes = Vec::with_capacity(transactions.len());
    let mut transaction_effects = Vec::with_capacity(transactions.len());
    let mut total_gas = 0i64;

    for transaction in &transactions {
        let nonce_key = (transaction.signers[0].account, transaction.nonce);
        let outcome = if nonce_keys.insert(nonce_key) {
            execute_transaction(payload, witness, &state, transaction)?
        } else {
            VmOutcome::fault(0)
        };
        validate_vm_outcome(&state, &outcome)?;
        total_gas = total_gas
            .checked_add(outcome.gas_consumed)
            .ok_or(ExecutionError::Invalid("batch gas overflow"))?;

        let (deltas, events) = if outcome.success {
            apply_deltas(&mut state, &outcome.storage_deltas)?;
            (outcome.storage_deltas, outcome.events)
        } else {
            (Vec::new(), Vec::new())
        };
        let receipt = CanonicalReceiptV1 {
            tx_hash: transaction.hash,
            success: outcome.success,
            gas_consumed: outcome.gas_consumed,
            storage_delta_hash: storage_delta_hash(&deltas),
            events_hash: events_hash(&events)?,
        };
        transaction_hashes.push(transaction.hash);
        receipt_hashes.push(receipt_hash(&receipt));
        transaction_effects.push(TransactionEffects {
            receipt,
            storage_deltas: deltas,
            events,
        });
    }

    let effects = BatchEffects {
        transactions: transaction_effects,
    };
    let (withdrawal_root, l2_to_l1_message_root, l2_to_l2_message_root) =
        derive_outbound_roots_v1(payload.chain_id, &effects)?;
    let execution_result = BatchExecutionResult {
        post_state_root: keyed_state_root_from_map(&state),
        tx_root: merkle_root(&transaction_hashes),
        receipt_root: merkle_root(&receipt_hashes),
        withdrawal_root,
        l2_to_l1_message_root,
        l2_to_l2_message_root,
        gas_consumed: total_gas,
    };
    let effects_bytes = encode_batch_effects(&effects)?;
    let da_commitment = hash256(&encode_execution_payload(payload)?);
    let public_inputs = PublicInputs {
        chain_id: payload.chain_id,
        batch_number: payload.batch_number,
        pre_state_root: payload.pre_state_root,
        post_state_root: execution_result.post_state_root,
        tx_root: execution_result.tx_root,
        receipt_root: execution_result.receipt_root,
        withdrawal_root: execution_result.withdrawal_root,
        l2_to_l1_message_root: execution_result.l2_to_l1_message_root,
        l2_to_l2_message_root: execution_result.l2_to_l2_message_root,
        l1_message_hash: hash_l1_messages(&payload.l1_messages),
        da_commitment,
        block_context_hash: hash_block_context(&payload.block_context),
    };
    let public_input_hash = hash_public_inputs(
        public_inputs.chain_id,
        public_inputs.batch_number,
        &public_inputs.pre_state_root,
        &public_inputs.post_state_root,
        &public_inputs.tx_root,
        &public_inputs.receipt_root,
        &public_inputs.withdrawal_root,
        &public_inputs.l2_to_l1_message_root,
        &public_inputs.l2_to_l2_message_root,
        &public_inputs.l1_message_hash,
        &public_inputs.da_commitment,
        &public_inputs.block_context_hash,
    );
    Ok(ComputedBatchState {
        batch: ComputedBatch {
            public_input_hash,
            execution_result,
            effects,
            effects_bytes,
            public_inputs,
        },
        state,
    })
}

pub fn verify_artifact_with<F>(
    input: &[u8],
    execute_transaction: F,
) -> Result<BatchResult, ExecutionError>
where
    F: FnMut(
        &ExecutionPayload,
        &StateWitness,
        &alloc::collections::BTreeMap<Vec<u8>, Vec<u8>>,
        &ParsedTransaction,
    ) -> Result<VmOutcome, ExecutionError>,
{
    let artifact = parse_proof_witness_artifact(input)?;
    verify_decoded_artifact_with(&artifact, execute_transaction)
}

pub fn verify_decoded_artifact_with<F>(
    artifact: &ProofWitnessArtifact,
    execute_transaction: F,
) -> Result<BatchResult, ExecutionError>
where
    F: FnMut(
        &ExecutionPayload,
        &StateWitness,
        &alloc::collections::BTreeMap<Vec<u8>, Vec<u8>>,
        &ParsedTransaction,
    ) -> Result<VmOutcome, ExecutionError>,
{
    if artifact.proof_type != 3
        || artifact.proof_system != 1
        || !artifact.execution_witness_authenticated
        || artifact.execution_semantic_id != crate::SP1_STATEFUL_NEO_VM_V1_EXECUTION_SEMANTIC_ID
    {
        return Err(ExecutionError::Unsupported("non-SP1 proof witness"));
    }
    let computed = compute_batch_with(
        &artifact.execution_payload,
        &artifact.state_witness,
        execute_transaction,
    )?;
    if computed.execution_result != artifact.execution_result {
        return Err(ExecutionError::ClaimMismatch("execution result"));
    }
    if computed.effects != artifact.effects || computed.effects_bytes != artifact.effects_bytes {
        return Err(ExecutionError::ClaimMismatch("canonical effects"));
    }
    if computed.public_inputs != artifact.public_inputs {
        return Err(ExecutionError::ClaimMismatch("public inputs"));
    }

    Ok(BatchResult {
        post_state_root: computed.execution_result.post_state_root,
        tx_root: computed.execution_result.tx_root,
        receipt_root: computed.execution_result.receipt_root,
        gas_consumed: computed.execution_result.gas_consumed,
        public_input_hash: computed.public_input_hash,
    })
}

fn validate_vm_outcome(
    state: &alloc::collections::BTreeMap<Vec<u8>, Vec<u8>>,
    outcome: &VmOutcome,
) -> Result<(), ExecutionError> {
    if outcome.gas_consumed < 0 {
        return Err(ExecutionError::Invalid("VM gas consumption"));
    }
    if !outcome.success && (!outcome.storage_deltas.is_empty() || !outcome.events.is_empty()) {
        return Err(ExecutionError::Invalid("FAULT transaction effects"));
    }
    let mut previous_key: Option<&[u8]> = None;
    for delta in &outcome.storage_deltas {
        if delta.key.is_empty()
            || previous_key.is_some_and(|previous| previous >= delta.key.as_slice())
            || state.get(&delta.key).map(Vec::as_slice) != delta.old_value.as_deref()
            || !valid_delta_transition(delta)
        {
            return Err(ExecutionError::Invalid("VM storage delta"));
        }
        previous_key = Some(&delta.key);
    }
    Ok(())
}

fn apply_deltas(
    state: &mut alloc::collections::BTreeMap<Vec<u8>, Vec<u8>>,
    deltas: &[StorageDelta],
) -> Result<(), ExecutionError> {
    for delta in deltas {
        match &delta.new_value {
            Some(value) => {
                state.insert(delta.key.clone(), value.clone());
            }
            None => {
                if state.remove(&delta.key).is_none() {
                    return Err(ExecutionError::Invalid("storage delete target"));
                }
            }
        }
    }
    Ok(())
}

fn valid_delta_transition(delta: &StorageDelta) -> bool {
    matches!(
        (
            delta.operation,
            delta.old_value.is_some(),
            delta.new_value.is_some()
        ),
        (StorageOperation::Add, false, true)
            | (StorageOperation::Update, true, true)
            | (StorageOperation::Delete, true, false)
    )
}
