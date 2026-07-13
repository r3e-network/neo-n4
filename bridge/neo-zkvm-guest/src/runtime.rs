use alloc::{
    collections::BTreeMap,
    string::{String, ToString},
    vec::Vec,
};

use neo_execution_core::{
    CanonicalStackValue, ContractManifest, ContractParameterType, ContractWitness, ExecutionError,
    ExecutionEvent, ExecutionPayload, ParsedTransaction, PermissionContract, PermissionMethods,
    StateWitness, StorageDelta, StorageOperation, UInt160, VmOutcome, encode_stack_state, hash160,
    normalize_signed_le,
};
use neo_vm_rs::{
    OpCode, StackValue, SyscallProvider, VmState, encode_integer,
    interpret_with_stack_and_syscalls_at, interpret_with_stack_and_syscalls_at_with_initializer,
};

const CALL_FLAGS_READ_STATES: u8 = 0x01;
const CALL_FLAGS_WRITE_STATES: u8 = 0x02;
const CALL_FLAGS_ALLOW_CALL: u8 = 0x04;
const CALL_FLAGS_ALLOW_NOTIFY: u8 = 0x08;
const CALL_FLAGS_ALL: u8 = 0x0f;
const TRIGGER_APPLICATION: i64 = 0x40;
const MAX_INVOCATION_DEPTH: usize = 1024;
const STORAGE_CONTEXT_MAGIC: u64 = 0x4e34_5354_0000_0000;
const STORAGE_CONTEXT_READ_ONLY: u64 = 1 << 32;

pub(crate) fn execute_transaction(
    payload: &ExecutionPayload,
    witness: &StateWitness,
    state: &BTreeMap<Vec<u8>, Vec<u8>>,
    transaction: &ParsedTransaction,
) -> Result<VmOutcome, ExecutionError> {
    let mut provider = ApplicationProvider::new(payload, witness, state, transaction);
    let result =
        interpret_with_stack_and_syscalls_at(&transaction.script, Vec::new(), 0, &mut provider);
    let success = matches!(result, Ok(ref result) if result.state == VmState::Halt);
    if !success {
        return Ok(VmOutcome::fault(provider.gas_consumed));
    }
    let storage_deltas = provider.storage_deltas()?;
    Ok(VmOutcome {
        success: true,
        gas_consumed: provider.gas_consumed,
        storage_deltas,
        events: provider.events,
    })
}

#[derive(Clone)]
struct CallContext {
    script_hash: UInt160,
    calling_script_hash: UInt160,
    contract_id: Option<i32>,
    call_flags: u8,
}

struct ApplicationProvider<'a> {
    payload: &'a ExecutionPayload,
    witness: &'a StateWitness,
    state: &'a BTreeMap<Vec<u8>, Vec<u8>>,
    transaction: &'a ParsedTransaction,
    overlay: BTreeMap<Vec<u8>, Option<Vec<u8>>>,
    contexts: Vec<CallContext>,
    invocation_counter: BTreeMap<UInt160, u32>,
    gas_consumed: i64,
    events: Vec<ExecutionEvent>,
    entry_script_hash: UInt160,
}

impl<'a> ApplicationProvider<'a> {
    fn new(
        payload: &'a ExecutionPayload,
        witness: &'a StateWitness,
        state: &'a BTreeMap<Vec<u8>, Vec<u8>>,
        transaction: &'a ParsedTransaction,
    ) -> Self {
        let entry_script_hash = hash160(&transaction.script);
        Self {
            payload,
            witness,
            state,
            transaction,
            overlay: BTreeMap::new(),
            contexts: alloc::vec![CallContext {
                script_hash: entry_script_hash,
                calling_script_hash: [0u8; 20],
                contract_id: None,
                call_flags: CALL_FLAGS_ALL,
            }],
            invocation_counter: BTreeMap::new(),
            gas_consumed: 0,
            events: Vec::new(),
            entry_script_hash,
        }
    }

    fn charge(&mut self, amount: i64) -> Result<(), String> {
        if amount < 0 {
            return Err("negative gas charge".to_string());
        }
        self.gas_consumed = self
            .gas_consumed
            .checked_add(amount)
            .ok_or_else(|| "gas overflow".to_string())?;
        if self.gas_consumed > self.witness.config.per_tx_gas_limit {
            return Err("insufficient GAS".to_string());
        }
        Ok(())
    }

    fn charge_syscall(&mut self, fixed_price: i64, required_flags: u8) -> Result<(), String> {
        if self.current_context()?.call_flags & required_flags != required_flags {
            return Err("syscall disallowed by call flags".to_string());
        }
        self.charge(
            fixed_price
                .checked_mul(i64::from(self.witness.config.exec_fee_factor))
                .ok_or_else(|| "syscall gas overflow".to_string())?,
        )
    }

    fn current_context(&self) -> Result<&CallContext, String> {
        self.contexts
            .last()
            .ok_or_else(|| "missing execution context".to_string())
    }

    fn current_contract(&self) -> Result<&ContractWitness, String> {
        let id = self
            .current_context()?
            .contract_id
            .ok_or_else(|| "deployed contract context required".to_string())?;
        self.witness
            .contract_by_id(id)
            .ok_or_else(|| "contract witness not found".to_string())
    }

    fn storage_deltas(&self) -> Result<Vec<StorageDelta>, ExecutionError> {
        let mut deltas = Vec::new();
        for (key, new_value) in &self.overlay {
            let old_value = self.state.get(key).cloned();
            if old_value == *new_value {
                continue;
            }
            let operation = match (old_value.is_some(), new_value.is_some()) {
                (false, true) => StorageOperation::Add,
                (true, true) => StorageOperation::Update,
                (true, false) => StorageOperation::Delete,
                (false, false) => continue,
            };
            deltas.push(StorageDelta {
                key: key.clone(),
                operation,
                old_value,
                new_value: new_value.clone(),
            });
        }
        Ok(deltas)
    }

    fn storage_value(&self, key: &[u8]) -> Option<&[u8]> {
        match self.overlay.get(key) {
            Some(Some(value)) => Some(value),
            Some(None) => None,
            None => self.state.get(key).map(Vec::as_slice),
        }
    }

    fn full_storage_key(id: i32, key: &[u8]) -> Result<Vec<u8>, String> {
        if key.len() > neo_execution_core::MAX_STORAGE_KEY_BYTES {
            return Err("storage key exceeds 64 bytes".to_string());
        }
        let mut full_key = Vec::with_capacity(4 + key.len());
        full_key.extend_from_slice(&id.to_le_bytes());
        full_key.extend_from_slice(key);
        Ok(full_key)
    }

    fn put_storage(
        &mut self,
        id: i32,
        read_only: bool,
        key: Vec<u8>,
        value: Vec<u8>,
    ) -> Result<(), String> {
        if read_only {
            return Err("storage context is read-only".to_string());
        }
        if value.len() > neo_execution_core::MAX_STORAGE_VALUE_BYTES {
            return Err("storage value exceeds 65535 bytes".to_string());
        }
        let full_key = Self::full_storage_key(id, &key)?;
        if full_key.starts_with(neo_execution_core::CONTRACT_BINDING_KEY_PREFIX) {
            return Err("contract binding state is immutable".to_string());
        }
        let old_value = self.storage_value(&full_key).map(<[u8]>::to_vec);
        let new_data_size = match old_value.as_deref() {
            None => key
                .len()
                .checked_add(value.len())
                .ok_or_else(|| "storage fee overflow".to_string())?,
            Some(_) if value.is_empty() => 0,
            Some(old) if value.len() <= old.len() => (value.len() - 1) / 4 + 1,
            Some([]) => value.len(),
            Some(old) => (old.len() - 1) / 4 + 1 + value.len() - old.len(),
        };
        self.overlay.insert(full_key, Some(value));
        self.charge(
            i64::try_from(new_data_size)
                .ok()
                .and_then(|size| size.checked_mul(i64::from(self.witness.config.storage_price)))
                .ok_or_else(|| "storage fee overflow".to_string())?,
        )
    }

    fn delete_storage(&mut self, id: i32, read_only: bool, key: Vec<u8>) -> Result<(), String> {
        if read_only {
            return Err("storage context is read-only".to_string());
        }
        let full_key = Self::full_storage_key(id, &key)?;
        if full_key.starts_with(neo_execution_core::CONTRACT_BINDING_KEY_PREFIX) {
            return Err("contract binding state is immutable".to_string());
        }
        if self.storage_value(&full_key).is_some() {
            self.overlay.insert(full_key, None);
        }
        Ok(())
    }

    fn call_contract(&mut self, stack: &mut Vec<StackValue>) -> Result<(), String> {
        let contract_hash = pop_fixed_bytes::<20>(stack, "contract hash")?;
        let method_name = pop_string(stack, "contract method")?;
        if method_name.starts_with('_') {
            return Err("dynamic contract method cannot start with underscore".to_string());
        }
        let requested_flags = pop_u8(stack, "contract call flags")?;
        if requested_flags & !CALL_FLAGS_ALL != 0 {
            return Err("invalid contract call flags".to_string());
        }
        let arguments = pop_array(stack, "contract arguments")?;
        let contract = self
            .witness
            .contract_by_hash(&contract_hash)
            .ok_or_else(|| "called contract does not exist in witness".to_string())?;
        let method = contract
            .manifest
            .methods
            .iter()
            .find(|candidate| {
                candidate.name == method_name && candidate.parameter_types.len() == arguments.len()
            })
            .cloned()
            .ok_or_else(|| "called contract method is absent from manifest".to_string())?;
        if !method.safe {
            if let Some(caller_id) = self.current_context()?.contract_id {
                let caller = self
                    .witness
                    .contract_by_id(caller_id)
                    .ok_or_else(|| "caller contract witness not found".to_string())?;
                if !manifest_allows_call(&caller.manifest, contract, &method_name) {
                    return Err("contract manifest permission denied".to_string());
                }
            }
        }
        if self.contexts.len() >= MAX_INVOCATION_DEPTH {
            return Err("maximum contract invocation depth reached".to_string());
        }

        let parent = self.current_context()?.clone();
        let mut child_flags = requested_flags & parent.call_flags;
        if method.safe {
            child_flags &= !(CALL_FLAGS_WRITE_STATES | CALL_FLAGS_ALLOW_NOTIFY);
        }
        *self.invocation_counter.entry(contract.hash).or_insert(0) += 1;
        let contract_id = contract.id;
        let script = contract.script.clone();
        let initializer = contract
            .manifest
            .methods
            .iter()
            .find(|candidate| {
                candidate.name == "_initialize" && candidate.parameter_types.is_empty()
            })
            .map(|candidate| candidate.offset);
        let overlay_checkpoint = self.overlay.clone();
        let event_checkpoint = self.events.len();
        self.contexts.push(CallContext {
            script_hash: contract.hash,
            calling_script_hash: parent.script_hash,
            contract_id: Some(contract_id),
            call_flags: child_flags,
        });
        let initial_stack = arguments.into_iter().rev().collect::<Vec<_>>();
        let result = if let Some(initializer) = initializer {
            interpret_with_stack_and_syscalls_at_with_initializer(
                &script,
                initial_stack,
                method.offset,
                initializer,
                self,
            )
        } else {
            interpret_with_stack_and_syscalls_at(&script, initial_stack, method.offset, self)
        };
        self.contexts.pop();

        let result = match result {
            Ok(result) if result.state == VmState::Halt => result,
            Ok(result) => {
                self.overlay = overlay_checkpoint;
                self.events.truncate(event_checkpoint);
                return Err(result
                    .fault_message
                    .unwrap_or_else(|| "called contract FAULT".to_string()));
            }
            Err(error) => {
                self.overlay = overlay_checkpoint;
                self.events.truncate(event_checkpoint);
                return Err(error);
            }
        };
        let returns_value = method.return_type != ContractParameterType::Void;
        if returns_value {
            if result.stack.len() != 1 {
                self.overlay = overlay_checkpoint;
                self.events.truncate(event_checkpoint);
                return Err("called contract return stack mismatch".to_string());
            }
            stack.push(result.stack[0].clone());
        } else if !result.stack.is_empty() {
            self.overlay = overlay_checkpoint;
            self.events.truncate(event_checkpoint);
            return Err("void contract returned a value".to_string());
        }
        Ok(())
    }

    fn notify(&mut self, stack: &mut Vec<StackValue>) -> Result<(), String> {
        let name = pop_string(stack, "event name")?;
        if name.is_empty() || name.len() > 32 {
            return Err("invalid event name".to_string());
        }
        let state = pop_array(stack, "event state")?;
        if self.events.len() >= 512 {
            return Err("maximum notification count reached".to_string());
        }
        let contract = self.current_contract()?;
        let event = contract
            .manifest
            .events
            .iter()
            .find(|event| event.name == name)
            .ok_or_else(|| "event absent from contract manifest".to_string())?;
        if event.parameter_types.len() != state.len() {
            return Err("event argument count mismatch".to_string());
        }
        for (value, parameter_type) in state.iter().zip(&event.parameter_types) {
            if !stack_value_matches_type(value, parameter_type) {
                return Err("event argument type mismatch".to_string());
            }
        }
        let canonical_state = CanonicalStackValue::Array(
            state
                .iter()
                .map(canonical_stack_value)
                .collect::<Result<Vec<_>, _>>()?,
        );
        encode_stack_state(&canonical_state).map_err(|error| error.to_string())?;
        self.events.push(ExecutionEvent {
            script_hash: contract.hash,
            name,
            state: canonical_state,
        });
        Ok(())
    }

    fn check_witness(&self, hash: &UInt160) -> Result<bool, String> {
        if self.transaction.has_oracle_response {
            return Err("oracle response signer adapter is unavailable".to_string());
        }
        if *hash == self.current_context()?.calling_script_hash {
            return Ok(true);
        }
        let Some(signer) = self
            .transaction
            .signers
            .iter()
            .find(|signer| signer.account == *hash)
        else {
            return Ok(false);
        };
        if signer.scopes == 0x80 {
            return Ok(true);
        }
        if signer.scopes & 0x01 != 0 && self.called_by_entry() {
            return Ok(true);
        }
        if signer.scopes & 0x10 != 0
            && signer
                .allowed_contracts
                .contains(&self.current_context()?.script_hash)
        {
            return Ok(true);
        }
        if signer.scopes & 0x20 != 0
            && signer
                .allowed_groups
                .iter()
                .any(|group| self.current_contract_has_group(group))
        {
            return Ok(true);
        }
        for rule in &signer.rules {
            if self.matches_condition(&rule.condition)? {
                return Ok(rule.action == neo_execution_core::WitnessRuleAction::Allow);
            }
        }
        Ok(false)
    }

    fn matches_condition(
        &self,
        condition: &neo_execution_core::WitnessCondition,
    ) -> Result<bool, String> {
        use neo_execution_core::WitnessCondition;
        match condition {
            WitnessCondition::Boolean(value) => Ok(*value),
            WitnessCondition::Not(condition) => Ok(!self.matches_condition(condition)?),
            WitnessCondition::And(conditions) => {
                for condition in conditions {
                    if !self.matches_condition(condition)? {
                        return Ok(false);
                    }
                }
                Ok(true)
            }
            WitnessCondition::Or(conditions) => {
                for condition in conditions {
                    if self.matches_condition(condition)? {
                        return Ok(true);
                    }
                }
                Ok(false)
            }
            WitnessCondition::ScriptHash(hash) => Ok(*hash == self.current_context()?.script_hash),
            WitnessCondition::Group(group) => Ok(self.current_contract_has_group(group)),
            WitnessCondition::CalledByEntry => Ok(self.called_by_entry()),
            WitnessCondition::CalledByContract(hash) => {
                Ok(*hash == self.current_context()?.calling_script_hash)
            }
            WitnessCondition::CalledByGroup(group) => Ok(self.calling_contract_has_group(group)),
        }
    }

    fn called_by_entry(&self) -> bool {
        self.contexts.len() <= 2
    }

    fn current_contract_has_group(&self, group: &[u8; 33]) -> bool {
        self.current_context()
            .ok()
            .and_then(|context| context.contract_id)
            .and_then(|id| self.witness.contract_by_id(id))
            .is_some_and(|contract| contract.manifest.groups.contains(group))
    }

    fn calling_contract_has_group(&self, group: &[u8; 33]) -> bool {
        self.contexts
            .get(self.contexts.len().saturating_sub(2))
            .and_then(|context| context.contract_id)
            .and_then(|id| self.witness.contract_by_id(id))
            .is_some_and(|contract| contract.manifest.groups.contains(group))
    }

    fn invocation_count(&mut self) -> Result<u32, String> {
        let hash = self.current_context()?.script_hash;
        Ok(*self.invocation_counter.entry(hash).or_insert(1))
    }
}

impl SyscallProvider for ApplicationProvider<'_> {
    fn on_instruction(&mut self, opcode: u8) -> Result<(), String> {
        OpCode::try_from(opcode).map_err(|_| "unknown NeoVM opcode".to_string())?;
        self.charge(
            OPCODE_PRICES[usize::from(opcode)]
                .checked_mul(i64::from(self.witness.config.exec_fee_factor))
                .ok_or_else(|| "opcode gas overflow".to_string())?,
        )
    }

    fn syscall(&mut self, api: u32, _ip: usize, stack: &mut Vec<StackValue>) -> Result<(), String> {
        let (fixed_price, required_flags) = syscall_descriptor(api)
            .ok_or_else(|| "unknown or unavailable consensus syscall".to_string())?;
        self.charge_syscall(fixed_price, required_flags)?;
        match api {
            0x525b_7d62 => self.call_contract(stack),
            0x813a_da95 => {
                stack.push(StackValue::Integer(i64::from(
                    self.current_context()?.call_flags,
                )));
                Ok(())
            }
            0xf6fc_79b2 => {
                stack.push(StackValue::ByteString(b"NEO".to_vec()));
                Ok(())
            }
            0xe0a0_fbc5 => {
                stack.push(StackValue::Integer(i64::from(
                    self.payload.block_context.network,
                )));
                Ok(())
            }
            0xdc92_494c => {
                stack.push(StackValue::Integer(i64::from(
                    self.witness.config.address_version,
                )));
                Ok(())
            }
            0xa038_7de9 => {
                stack.push(StackValue::Integer(TRIGGER_APPLICATION));
                Ok(())
            }
            0x0388_c3b7 => {
                stack.push(integer_from_u64(
                    self.payload.block_context.first_block_timestamp,
                ));
                Ok(())
            }
            0x74a8_fedb => {
                stack.push(StackValue::ByteString(
                    self.current_context()?.script_hash.to_vec(),
                ));
                Ok(())
            }
            0x3c6e_5339 => {
                stack.push(StackValue::ByteString(
                    self.current_context()?.calling_script_hash.to_vec(),
                ));
                Ok(())
            }
            0x38e2_b4f9 => {
                stack.push(StackValue::ByteString(self.entry_script_hash.to_vec()));
                Ok(())
            }
            0x8cec_27f8 => {
                let hash = pop_fixed_bytes::<20>(stack, "witness hash")?;
                stack.push(StackValue::Boolean(self.check_witness(&hash)?));
                Ok(())
            }
            0x4311_2784 => {
                let count = self.invocation_count()?;
                stack.push(StackValue::Integer(i64::from(count)));
                Ok(())
            }
            0x9647_e7cf => {
                let message = pop_bytes(stack, "runtime log")?;
                if message.len() > 1024 || core::str::from_utf8(&message).is_err() {
                    return Err("invalid runtime log".to_string());
                }
                Ok(())
            }
            0x616f_0195 => self.notify(stack),
            0xced8_8814 => {
                stack.push(StackValue::Integer(
                    self.witness.config.per_tx_gas_limit - self.gas_consumed,
                ));
                Ok(())
            }
            0xbc8c_5ac3 => {
                let amount = pop_i64(stack, "burn gas")?;
                if amount <= 0 {
                    return Err("burn gas amount must be positive".to_string());
                }
                self.charge(amount)
            }
            0xce67_f69b | 0xe26b_b4f6 => {
                let id = self.current_contract()?.id;
                let read_only = api == 0xe26b_b4f6;
                stack.push(StackValue::Interop(encode_storage_context(id, read_only)));
                Ok(())
            }
            0xe9bf_4c76 => {
                let (id, _) = pop_storage_context(stack)?;
                stack.push(StackValue::Interop(encode_storage_context(id, true)));
                Ok(())
            }
            0xe85e_8dd5 => {
                let key = pop_bytes(stack, "storage key")?;
                let id = self.current_contract()?.id;
                let full_key = Self::full_storage_key(id, &key)?;
                push_storage_value(stack, self.storage_value(&full_key));
                Ok(())
            }
            0x0ae3_0c39 => {
                let key = pop_bytes(stack, "storage key")?;
                let value = pop_bytes(stack, "storage value")?;
                let id = self.current_contract()?.id;
                self.put_storage(id, false, key, value)
            }
            0x94f5_5475 => {
                let key = pop_bytes(stack, "storage key")?;
                let id = self.current_contract()?.id;
                self.delete_storage(id, false, key)
            }
            0x31e8_5d92 => {
                let context = pop_storage_context(stack)?;
                let key = pop_bytes(stack, "storage key")?;
                let full_key = Self::full_storage_key(context.0, &key)?;
                push_storage_value(stack, self.storage_value(&full_key));
                Ok(())
            }
            0x8418_3fe6 => {
                let context = pop_storage_context(stack)?;
                let key = pop_bytes(stack, "storage key")?;
                let value = pop_bytes(stack, "storage value")?;
                self.put_storage(context.0, context.1, key, value)
            }
            0xedc5_582f => {
                let context = pop_storage_context(stack)?;
                let key = pop_bytes(stack, "storage key")?;
                self.delete_storage(context.0, context.1, key)
            }
            _ => Err("consensus syscall is fail-closed in state witness V1".to_string()),
        }
    }
}

fn manifest_allows_call(caller: &ContractManifest, target: &ContractWitness, method: &str) -> bool {
    caller.permissions.iter().any(|permission| {
        let contract_matches = match &permission.contract {
            PermissionContract::Wildcard => true,
            PermissionContract::Hash(hash) => hash == &target.hash,
            PermissionContract::Group(group) => target.manifest.groups.contains(group),
        };
        let method_matches = match &permission.methods {
            PermissionMethods::Wildcard => true,
            PermissionMethods::Named(methods) => methods.iter().any(|name| name == method),
        };
        contract_matches && method_matches
    })
}

fn stack_value_matches_type(value: &StackValue, parameter_type: &ContractParameterType) -> bool {
    if matches!(value, StackValue::Pointer(_)) {
        return false;
    }
    match parameter_type {
        ContractParameterType::Any => true,
        ContractParameterType::Boolean => matches!(value, StackValue::Boolean(_)),
        ContractParameterType::Integer => {
            matches!(value, StackValue::Integer(_) | StackValue::BigInteger(_))
        }
        ContractParameterType::ByteArray => matches!(
            value,
            StackValue::Null | StackValue::ByteString(_) | StackValue::Buffer(_)
        ),
        ContractParameterType::String => matches!(
            value,
            StackValue::ByteString(bytes) | StackValue::Buffer(bytes)
                if core::str::from_utf8(bytes).is_ok()
        ),
        ContractParameterType::Hash160 => null_or_byte_sequence_len(value, 20),
        ContractParameterType::Hash256 => null_or_byte_sequence_len(value, 32),
        ContractParameterType::PublicKey => null_or_byte_sequence_len(value, 33),
        ContractParameterType::Signature => null_or_byte_sequence_len(value, 64),
        ContractParameterType::Array => matches!(
            value,
            StackValue::Null | StackValue::Array(_) | StackValue::Struct(_)
        ),
        ContractParameterType::Map => matches!(value, StackValue::Null | StackValue::Map(_)),
        ContractParameterType::InteropInterface => matches!(
            value,
            StackValue::Null | StackValue::Interop(_) | StackValue::Iterator(_)
        ),
        ContractParameterType::Void => false,
    }
}

fn null_or_byte_sequence_len(value: &StackValue, expected: usize) -> bool {
    match value {
        StackValue::Null => true,
        StackValue::ByteString(bytes) | StackValue::Buffer(bytes) => bytes.len() == expected,
        _ => false,
    }
}

fn canonical_stack_value(value: &StackValue) -> Result<CanonicalStackValue, String> {
    match value {
        StackValue::Null => Ok(CanonicalStackValue::Null),
        StackValue::Boolean(value) => Ok(CanonicalStackValue::Boolean(*value)),
        StackValue::Integer(value) => Ok(CanonicalStackValue::Integer(encode_integer(*value))),
        StackValue::BigInteger(value) => {
            Ok(CanonicalStackValue::Integer(normalize_signed_le(value)))
        }
        StackValue::ByteString(value) => Ok(CanonicalStackValue::ByteString(value.clone())),
        StackValue::Buffer(value) => Ok(CanonicalStackValue::Buffer(value.clone())),
        StackValue::Array(items) => Ok(CanonicalStackValue::Array(
            items
                .iter()
                .map(canonical_stack_value)
                .collect::<Result<Vec<_>, _>>()?,
        )),
        StackValue::Struct(items) => Ok(CanonicalStackValue::Struct(
            items
                .iter()
                .map(canonical_stack_value)
                .collect::<Result<Vec<_>, _>>()?,
        )),
        StackValue::Map(entries) => Ok(CanonicalStackValue::Map(
            entries
                .iter()
                .map(|(key, value)| {
                    Ok((canonical_stack_value(key)?, canonical_stack_value(value)?))
                })
                .collect::<Result<Vec<_>, String>>()?,
        )),
        StackValue::Interop(_) | StackValue::Iterator(_) | StackValue::Pointer(_) => {
            Err("non-serializable event stack value".to_string())
        }
    }
}

fn pop_value(stack: &mut Vec<StackValue>, field: &str) -> Result<StackValue, String> {
    stack.pop().ok_or_else(|| alloc::format!("missing {field}"))
}

fn pop_bytes(stack: &mut Vec<StackValue>, field: &str) -> Result<Vec<u8>, String> {
    pop_value(stack, field)?
        .to_byte_string_bytes()
        .ok_or_else(|| alloc::format!("invalid {field}"))
}

fn pop_fixed_bytes<const N: usize>(
    stack: &mut Vec<StackValue>,
    field: &str,
) -> Result<[u8; N], String> {
    pop_bytes(stack, field)?
        .try_into()
        .map_err(|_| alloc::format!("invalid {field} length"))
}

fn pop_string(stack: &mut Vec<StackValue>, field: &str) -> Result<String, String> {
    let bytes = pop_bytes(stack, field)?;
    core::str::from_utf8(&bytes)
        .map(ToString::to_string)
        .map_err(|_| alloc::format!("invalid {field} UTF-8"))
}

fn pop_i64(stack: &mut Vec<StackValue>, field: &str) -> Result<i64, String> {
    let value = pop_value(stack, field)?
        .to_i128()
        .ok_or_else(|| alloc::format!("invalid {field}"))?;
    i64::try_from(value).map_err(|_| alloc::format!("{field} out of range"))
}

fn pop_u8(stack: &mut Vec<StackValue>, field: &str) -> Result<u8, String> {
    let value = pop_i64(stack, field)?;
    u8::try_from(value).map_err(|_| alloc::format!("{field} out of range"))
}

fn pop_array(stack: &mut Vec<StackValue>, field: &str) -> Result<Vec<StackValue>, String> {
    match pop_value(stack, field)? {
        StackValue::Array(items) | StackValue::Struct(items) => Ok(items),
        _ => Err(alloc::format!("invalid {field}")),
    }
}

fn encode_storage_context(id: i32, read_only: bool) -> u64 {
    STORAGE_CONTEXT_MAGIC
        | u64::from(id as u32)
        | if read_only {
            STORAGE_CONTEXT_READ_ONLY
        } else {
            0
        }
}

fn pop_storage_context(stack: &mut Vec<StackValue>) -> Result<(i32, bool), String> {
    let StackValue::Interop(handle) = pop_value(stack, "storage context")? else {
        return Err("invalid storage context".to_string());
    };
    if handle & 0xffff_fffe_0000_0000 != STORAGE_CONTEXT_MAGIC {
        return Err("invalid storage context handle".to_string());
    }
    Ok((
        handle as u32 as i32,
        handle & STORAGE_CONTEXT_READ_ONLY != 0,
    ))
}

fn push_storage_value(stack: &mut Vec<StackValue>, value: Option<&[u8]>) {
    stack.push(match value {
        Some(value) => StackValue::ByteString(value.to_vec()),
        None => StackValue::Null,
    });
}

fn integer_from_u64(value: u64) -> StackValue {
    if let Ok(value) = i64::try_from(value) {
        StackValue::Integer(value)
    } else {
        let mut bytes = value.to_le_bytes().to_vec();
        if bytes.last().is_some_and(|last| last & 0x80 != 0) {
            bytes.push(0);
        }
        StackValue::BigInteger(bytes)
    }
}

fn syscall_descriptor(api: u32) -> Option<(i64, u8)> {
    match api {
        0x525b_7d62 => Some((1 << 15, CALL_FLAGS_READ_STATES | CALL_FLAGS_ALLOW_CALL)),
        0x677b_f71a => Some((0, 0)),
        0x813a_da95 => Some((1 << 10, 0)),
        0x0287_99cf | 0x09e9_336a => Some((0, 0)),
        0x93bc_db2e | 0x165d_a144 => Some((0, CALL_FLAGS_READ_STATES | CALL_FLAGS_WRITE_STATES)),
        0xf6fc_79b2 | 0xe0a0_fbc5 | 0xdc92_494c | 0xa038_7de9 | 0x0388_c3b7 | 0x3008_512d => {
            Some((1 << 3, 0))
        }
        0x74a8_fedb | 0x3c6e_5339 | 0x38e2_b4f9 | 0x4311_2784 | 0xced8_8814 | 0xbc8c_5ac3
        | 0x8b18_f1ac => Some((1 << 4, 0)),
        0x8f80_0cb3 => Some((1 << 15, CALL_FLAGS_ALLOW_CALL)),
        0x8cec_27f8 => Some((1 << 10, 0)),
        0x28a9_de6b => Some((0, 0)),
        0x9647_e7cf | 0x616f_0195 => Some((1 << 15, CALL_FLAGS_ALLOW_NOTIFY)),
        0xf135_4327 => Some((1 << 12, 0)),
        0xce67_f69b | 0xe26b_b4f6 | 0xe9bf_4c76 => Some((1 << 4, CALL_FLAGS_READ_STATES)),
        0x31e8_5d92 | 0xe85e_8dd5 | 0x9ab8_30df | 0xf352_7607 => {
            Some((1 << 15, CALL_FLAGS_READ_STATES))
        }
        0x8418_3fe6 | 0xedc5_582f | 0x0ae3_0c39 | 0x94f5_5475 => {
            Some((1 << 15, CALL_FLAGS_WRITE_STATES))
        }
        0x27b3_e756 => Some((1 << 15, 0)),
        0x3adc_d09e => Some((0, 0)),
        0x9ced_089c => Some((1 << 15, 0)),
        0x1dbf_54f3 => Some((1 << 4, 0)),
        _ => None,
    }
}

const fn opcode_prices() -> [i64; 256] {
    let mut prices = [0i64; 256];
    prices[0x00] = 1 << 0;
    prices[0x01] = 1 << 0;
    prices[0x02] = 1 << 0;
    prices[0x03] = 1 << 0;
    prices[0x04] = 1 << 2;
    prices[0x05] = 1 << 2;
    prices[0x08] = 1 << 0;
    prices[0x09] = 1 << 0;
    prices[0x0a] = 1 << 2;
    prices[0x0b] = 1 << 0;
    prices[0x0c] = 1 << 3;
    prices[0x0d] = 1 << 9;
    prices[0x0e] = 1 << 12;
    let mut opcode = 0x0f;
    while opcode <= 0x21 {
        prices[opcode] = 1 << 0;
        opcode += 1;
    }
    opcode = 0x22;
    while opcode <= 0x33 {
        prices[opcode] = 1 << 1;
        opcode += 1;
    }
    prices[0x34] = 1 << 9;
    prices[0x35] = 1 << 9;
    prices[0x36] = 1 << 9;
    prices[0x37] = 1 << 15;
    prices[0x39] = 1 << 0;
    prices[0x3a] = 1 << 9;
    opcode = 0x3b;
    while opcode <= 0x3f {
        prices[opcode] = 1 << 2;
        opcode += 1;
    }
    prices[0x43] = 1 << 1;
    prices[0x45] = 1 << 1;
    prices[0x46] = 1 << 1;
    prices[0x48] = 1 << 4;
    prices[0x49] = 1 << 4;
    prices[0x4a] = 1 << 1;
    prices[0x4b] = 1 << 1;
    prices[0x4d] = 1 << 1;
    prices[0x4e] = 1 << 1;
    prices[0x50] = 1 << 1;
    prices[0x51] = 1 << 1;
    prices[0x52] = 1 << 4;
    prices[0x53] = 1 << 1;
    prices[0x54] = 1 << 1;
    prices[0x55] = 1 << 4;
    prices[0x56] = 1 << 4;
    prices[0x57] = 1 << 6;
    opcode = 0x58;
    while opcode <= 0x87 {
        prices[opcode] = 1 << 1;
        opcode += 1;
    }
    prices[0x88] = 1 << 8;
    prices[0x89] = 1 << 11;
    opcode = 0x8b;
    while opcode <= 0x8e {
        prices[opcode] = 1 << 11;
        opcode += 1;
    }
    prices[0x90] = 1 << 2;
    prices[0x91] = 1 << 3;
    prices[0x92] = 1 << 3;
    prices[0x93] = 1 << 3;
    prices[0x97] = 1 << 5;
    prices[0x98] = 1 << 5;
    opcode = 0x99;
    while opcode <= 0x9d {
        prices[opcode] = 1 << 2;
        opcode += 1;
    }
    opcode = 0x9e;
    while opcode <= 0xa2 {
        prices[opcode] = 1 << 3;
        opcode += 1;
    }
    prices[0xa3] = 1 << 6;
    prices[0xa4] = 1 << 6;
    prices[0xa5] = 1 << 5;
    prices[0xa6] = 1 << 11;
    prices[0xa8] = 1 << 3;
    prices[0xa9] = 1 << 3;
    prices[0xaa] = 1 << 2;
    prices[0xab] = 1 << 3;
    prices[0xac] = 1 << 3;
    prices[0xb1] = 1 << 2;
    opcode = 0xb3;
    while opcode <= 0xbb {
        prices[opcode] = 1 << 3;
        opcode += 1;
    }
    opcode = 0xbe;
    while opcode <= 0xc1 {
        prices[opcode] = 1 << 11;
        opcode += 1;
    }
    prices[0xc2] = 1 << 4;
    prices[0xc3] = 1 << 9;
    prices[0xc4] = 1 << 9;
    prices[0xc5] = 1 << 4;
    prices[0xc6] = 1 << 9;
    prices[0xc8] = 1 << 3;
    prices[0xca] = 1 << 2;
    prices[0xcb] = 1 << 6;
    prices[0xcc] = 1 << 4;
    prices[0xcd] = 1 << 13;
    prices[0xce] = 1 << 6;
    prices[0xcf] = 1 << 13;
    prices[0xd0] = 1 << 13;
    prices[0xd1] = 1 << 13;
    prices[0xd2] = 1 << 4;
    prices[0xd3] = 1 << 4;
    prices[0xd4] = 1 << 4;
    prices[0xd8] = 1 << 1;
    prices[0xd9] = 1 << 1;
    prices[0xdb] = 1 << 13;
    prices[0xe1] = 1 << 0;
    prices
}

const OPCODE_PRICES: [i64; 256] = opcode_prices();

#[cfg(test)]
mod tests {
    use super::OPCODE_PRICES;

    #[test]
    fn opcode_prices_pin_production_samples() {
        assert_eq!(OPCODE_PRICES[0x0c], 1 << 3);
        assert_eq!(OPCODE_PRICES[0x37], 1 << 15);
        assert_eq!(OPCODE_PRICES[0xcd], 1 << 13);
        assert_eq!(OPCODE_PRICES[0xe1], 1);
    }
}
