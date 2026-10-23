use alloc::{
    collections::{BTreeMap, BTreeSet},
    vec::Vec,
};

use crate::{
    hashing::{
        events_hash, hash_block_context, hash_l1_messages, hash_public_inputs_with_forced, hash256,
        keyed_state_root_from_map, merkle_root, receipt_hash, storage_delta_hash,
    },
    native::{apply_l1_inbox_v1, derive_outbound_roots_v1},
    transaction::parse_transaction,
    types::{
        BatchEffects, BatchExecutionResult, BatchResult, CanonicalReceiptV1, ComputedBatch,
        ComputedBatchTransition, ExecutionError, ExecutionPayload, ParsedTransaction, ProofType,
        ProofWitnessArtifact, PublicInputs, StateEntry, StateWitness, StorageDelta,
        StorageOperation, TransactionEffects, UInt160, UInt256, VmOutcome,
    },
    wire::constants::MAX_BATCH_GAS,
    wire::{
        encode_batch_effects, encode_execution_payload, encode_state_witness,
        parse_proof_witness_artifact,
    },
    witness::verify_transaction_witnesses,
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

/// Validate the batch's forced-inclusion proofs.
///
/// Mirrors the reference C# producer (`Neo.L2.Batch.SealedBatch`): a forced-inclusion proof
/// must name the batch's LEADING transaction, must carry that transaction's canonical hash,
/// and must be a canonical Merkle proof for the batch's transaction tree. The nonces must be
/// unique within the batch.
///
/// Without this the batch could declare `forced_inclusion_count = N` without actually
/// including the forced transactions. The chain advances the forced queue head by N purely
/// on that count, so the forced transactions would be permanently skipped — a censorship
/// attack that the SP1 proof itself would happily certify.
fn validate_forced_inclusions(
    payload: &ExecutionPayload,
    transactions: &[ParsedTransaction],
) -> Result<(), ExecutionError> {
    let proofs = &payload.forced_inclusions;
    if proofs.is_empty() {
        return Ok(());
    }
    if proofs.len() > transactions.len() {
        return Err(ExecutionError::Invalid(
            "forced-inclusion count exceeds the batch transaction count",
        ));
    }
    let transaction_hashes: Vec<UInt256> = transactions.iter().map(|tx| tx.hash).collect();
    let mut nonces = BTreeSet::new();
    for (index, proof) in proofs.iter().enumerate() {
        if proof.leaf_index as usize != index {
            return Err(ExecutionError::Invalid(
                "forced-inclusion transactions must occupy the leading transaction positions",
            ));
        }
        if !nonces.insert(proof.nonce) {
            return Err(ExecutionError::Invalid(
                "forced-inclusion nonces must be unique within a batch",
            ));
        }
        if proof.tx_hash != transaction_hashes[index] {
            return Err(ExecutionError::Invalid(
                "forced-inclusion tx hash does not match the encoded transaction",
            ));
        }
        // Recompute the canonical sibling path for this leaf and require the proof to match
        // it exactly. A tampered or non-canonical path cannot verify against the root.
        let canonical = canonical_siblings(&transaction_hashes, index);
        if canonical != proof.siblings {
            return Err(ExecutionError::Invalid(
                "forced-inclusion proof is not canonical for the batch transaction root",
            ));
        }
    }
    Ok(())
}

/// Compute the canonical Merkle sibling path for `index` over `leaves`.
///
/// The tree duplicates the last node when a level has an odd number of leaves — the same
/// convention the C# `Neo.L2.State.MerkleTree` uses.
fn canonical_siblings(leaves: &[UInt256], index: usize) -> Vec<UInt256> {
    let mut level: Vec<UInt256> = leaves.to_vec();
    let mut position = index;
    let mut siblings = Vec::new();
    while level.len() > 1 {
        let parent = position / 2;
        let sibling_index = if position % 2 == 0 {
            position + 1
        } else {
            position - 1
        };
        let sibling = level.get(sibling_index).copied().unwrap_or(level[position]);
        siblings.push(sibling);
        let mut parents = Vec::with_capacity(level.len().div_ceil(2));
        for pair in level.chunks(2) {
            let left = pair[0];
            let right = pair.get(1).copied().unwrap_or(left);
            let mut bytes = [0u8; 64];
            bytes[..32].copy_from_slice(&left);
            bytes[32..].copy_from_slice(&right);
            parents.push(hash256(&bytes));
        }
        level = parents;
        position = parent;
    }
    siblings
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
    // Authorization is a proved obligation: every witness is verified against its signer before
    // any execution, so the batch's effects can only follow from authorized transactions. An
    // unverifiable witness is a fatal protocol error, not a transaction FAULT.
    for transaction in &transactions {
        verify_transaction_witnesses(transaction, payload.block_context.network)?;
    }
    // Forced-inclusion proofs must actually prove inclusion of the batch's LEADING
    // transactions. Without this check the batch could claim `forced_inclusion_count = N`
    // while containing none of the forced transactions — the chain advances the forced
    // queue head by N purely on the count, so the forced transactions are permanently
    // skipped (a censorship attack) and the proof itself is not verifiable.
    //
    // The C# producer (`SealedBatch`) enforces the same rules, so a batch produced by the
    // reference implementation passes; a non-conforming producer is rejected here rather
    // than being believed.
    validate_forced_inclusions(payload, &transactions)?;
    let mut nonce_keys = BTreeSet::<(UInt160, u32)>::new();
    let mut transaction_hashes = Vec::with_capacity(transactions.len());
    let mut receipt_hashes = Vec::with_capacity(transactions.len());
    let mut transaction_effects = Vec::with_capacity(transactions.len());
    let mut total_gas = 0i64;

    for transaction in &transactions {
        // Validity-window enforcement: the payload's flat transaction list carries no
        // per-transaction block assignment, so a transaction is executable only if it is
        // unexpired across the whole batch block range — Neo rejects a transaction once the
        // executing height exceeds `valid_until_block`, and last_block is the latest height any
        // transaction here can execute at. An expired transaction faults without executing and
        // without consuming its (sender, nonce) slot.
        let expired = u64::from(transaction.valid_until_block) < payload.last_block;
        let nonce_key = (transaction.signers[0].account, transaction.nonce);
        let outcome = if expired {
            VmOutcome::fault(0)
        } else if nonce_keys.insert(nonce_key) {
            execute_transaction(payload, witness, &state, transaction)?
        } else {
            VmOutcome::fault(0)
        };
        validate_vm_outcome(&state, &outcome)?;
        total_gas = total_gas
            .checked_add(outcome.gas_consumed)
            .ok_or(ExecutionError::Invalid("batch gas overflow"))?;
        // Aggregate batch-GAS ceiling. Each transaction has its own per-transaction limit,
        // so without an aggregate ceiling a batch could carry up to `MAX_PAYLOAD_ITEMS`
        // transactions and demand an unbounded amount of proving time. Rejecting here
        // means both the guest and the host refuse the batch — the limit is part of the
        // execution semantics, not a local policy.
        if u64::try_from(total_gas).unwrap_or(u64::MAX) > MAX_BATCH_GAS {
            return Err(ExecutionError::Invalid("batch total gas exceeds limit"));
        }

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
        first_block: payload.first_block,
        last_block: payload.last_block,
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
        forced_inclusion_count: u32::try_from(payload.forced_inclusions.len())
            .map_err(|_| ExecutionError::Invalid("forced-inclusion count exceeds u32"))?,
    };
    let public_input_hash = hash_public_inputs_with_forced(
        public_inputs.chain_id,
        public_inputs.batch_number,
        public_inputs.first_block,
        public_inputs.last_block,
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
        public_inputs.forced_inclusion_count,
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
    if artifact.proof_type != ProofType::Zk as u8
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
