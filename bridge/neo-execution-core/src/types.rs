use alloc::{boxed::Box, collections::BTreeMap, string::String, vec::Vec};

pub type UInt160 = [u8; 20];
pub type UInt256 = [u8; 32];

pub const CANONICAL_RECEIPT_V1_BYTES: usize = 105;
pub const DEFAULT_EXEC_FEE_FACTOR: u32 = 30;
pub const DEFAULT_STORAGE_PRICE: u32 = 100_000;
pub const DEFAULT_ADDRESS_VERSION: u8 = 0x35;
pub const DEFAULT_PER_TX_GAS_LIMIT: i64 = 2_000_000_000;
pub const MAX_STORAGE_KEY_BYTES: usize = 64;
pub const MAX_STORAGE_VALUE_BYTES: usize = u16::MAX as usize;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchBlockContext {
    pub l1_finalized_height: u32,
    pub first_block_timestamp: u64,
    pub last_block_timestamp: u64,
    pub sequencer_committee_hash: UInt256,
    pub network: u32,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct L1Message {
    pub source_chain_id: u32,
    pub target_chain_id: u32,
    pub nonce: u64,
    pub sender: UInt160,
    pub receiver: UInt160,
    pub message_type: u8,
    pub payload: Vec<u8>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ExecutionPayload {
    pub chain_id: u32,
    pub batch_number: u64,
    pub first_block: u64,
    pub last_block: u64,
    pub pre_state_root: UInt256,
    pub block_context: BatchBlockContext,
    pub l1_messages: Vec<L1Message>,
    pub transactions: Vec<Vec<u8>>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ProtocolConfig {
    pub exec_fee_factor: u32,
    pub storage_price: u32,
    pub address_version: u8,
    pub per_tx_gas_limit: i64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StateEntry {
    pub key: Vec<u8>,
    pub value: Vec<u8>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum ContractParameterType {
    Any,
    Boolean,
    Integer,
    ByteArray,
    String,
    Hash160,
    Hash256,
    PublicKey,
    Signature,
    Array,
    Map,
    InteropInterface,
    Void,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ManifestMethod {
    pub name: String,
    pub parameter_types: Vec<ContractParameterType>,
    pub return_type: ContractParameterType,
    pub offset: usize,
    pub safe: bool,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ManifestEvent {
    pub name: String,
    pub parameter_types: Vec<ContractParameterType>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum PermissionContract {
    Wildcard,
    Hash(UInt160),
    Group([u8; 33]),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum PermissionMethods {
    Wildcard,
    Named(Vec<String>),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ManifestPermission {
    pub contract: PermissionContract,
    pub methods: PermissionMethods,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ContractManifest {
    pub name: String,
    pub groups: Vec<[u8; 33]>,
    pub methods: Vec<ManifestMethod>,
    pub events: Vec<ManifestEvent>,
    pub permissions: Vec<ManifestPermission>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ContractWitness {
    pub id: i32,
    pub hash: UInt160,
    pub script: Vec<u8>,
    pub manifest_bytes: Vec<u8>,
    pub manifest: ContractManifest,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StateWitness {
    pub config: ProtocolConfig,
    pub entries: Vec<StateEntry>,
    pub contracts: Vec<ContractWitness>,
}

impl StateWitness {
    #[must_use]
    pub fn state_map(&self) -> BTreeMap<Vec<u8>, Vec<u8>> {
        self.entries
            .iter()
            .map(|entry| (entry.key.clone(), entry.value.clone()))
            .collect()
    }

    #[must_use]
    pub fn contract_by_hash(&self, hash: &UInt160) -> Option<&ContractWitness> {
        self.contracts
            .iter()
            .find(|contract| &contract.hash == hash)
    }

    #[must_use]
    pub fn contract_by_id(&self, id: i32) -> Option<&ContractWitness> {
        self.contracts.iter().find(|contract| contract.id == id)
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct PublicInputs {
    pub chain_id: u32,
    pub batch_number: u64,
    pub pre_state_root: UInt256,
    pub post_state_root: UInt256,
    pub tx_root: UInt256,
    pub receipt_root: UInt256,
    pub withdrawal_root: UInt256,
    pub l2_to_l1_message_root: UInt256,
    pub l2_to_l2_message_root: UInt256,
    pub l1_message_hash: UInt256,
    pub da_commitment: UInt256,
    pub block_context_hash: UInt256,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchExecutionResult {
    pub post_state_root: UInt256,
    pub tx_root: UInt256,
    pub receipt_root: UInt256,
    pub withdrawal_root: UInt256,
    pub l2_to_l1_message_root: UInt256,
    pub l2_to_l2_message_root: UInt256,
    pub gas_consumed: i64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ProofWitnessArtifact {
    pub proof_system: u8,
    pub verification_key_id: UInt256,
    pub chain_id: u32,
    pub batch_number: u64,
    pub first_block: u64,
    pub last_block: u64,
    pub payload_bytes: Vec<u8>,
    pub execution_payload: ExecutionPayload,
    pub state_witness_bytes: Vec<u8>,
    pub state_witness: StateWitness,
    pub execution_result: BatchExecutionResult,
    pub effects_bytes: Vec<u8>,
    pub effects: BatchEffects,
    pub da_mode: u8,
    pub da_commitment: UInt256,
    pub da_pointer: Vec<u8>,
    pub public_inputs: PublicInputs,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum WitnessCondition {
    Boolean(bool),
    Not(Box<WitnessCondition>),
    And(Vec<WitnessCondition>),
    Or(Vec<WitnessCondition>),
    ScriptHash(UInt160),
    Group([u8; 33]),
    CalledByEntry,
    CalledByContract(UInt160),
    CalledByGroup([u8; 33]),
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum WitnessRuleAction {
    Deny,
    Allow,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct WitnessRule {
    pub action: WitnessRuleAction,
    pub condition: WitnessCondition,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct Signer {
    pub account: UInt160,
    pub scopes: u8,
    pub allowed_contracts: Vec<UInt160>,
    pub allowed_groups: Vec<[u8; 33]>,
    pub rules: Vec<WitnessRule>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TransactionWitness {
    pub invocation_script: Vec<u8>,
    pub verification_script: Vec<u8>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ParsedTransaction {
    pub hash: UInt256,
    pub nonce: u32,
    pub system_fee: i64,
    pub network_fee: i64,
    pub valid_until_block: u32,
    pub has_oracle_response: bool,
    pub signers: Vec<Signer>,
    pub script: Vec<u8>,
    pub witnesses: Vec<TransactionWitness>,
}

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[repr(u8)]
pub enum StorageOperation {
    Add = 1,
    Update = 2,
    Delete = 3,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StorageDelta {
    pub key: Vec<u8>,
    pub operation: StorageOperation,
    pub old_value: Option<Vec<u8>>,
    pub new_value: Option<Vec<u8>>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum CanonicalStackValue {
    Null,
    Boolean(bool),
    Integer(Vec<u8>),
    ByteString(Vec<u8>),
    Buffer(Vec<u8>),
    Array(Vec<CanonicalStackValue>),
    Struct(Vec<CanonicalStackValue>),
    Map(Vec<(CanonicalStackValue, CanonicalStackValue)>),
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ExecutionEvent {
    pub script_hash: UInt160,
    pub name: String,
    pub state: CanonicalStackValue,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct CanonicalReceiptV1 {
    pub tx_hash: UInt256,
    pub success: bool,
    pub gas_consumed: i64,
    pub storage_delta_hash: UInt256,
    pub events_hash: UInt256,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct TransactionEffects {
    pub receipt: CanonicalReceiptV1,
    pub storage_deltas: Vec<StorageDelta>,
    pub events: Vec<ExecutionEvent>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchEffects {
    pub transactions: Vec<TransactionEffects>,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct VmOutcome {
    pub success: bool,
    pub gas_consumed: i64,
    pub storage_deltas: Vec<StorageDelta>,
    pub events: Vec<ExecutionEvent>,
}

impl VmOutcome {
    #[must_use]
    pub fn fault(gas_consumed: i64) -> Self {
        Self {
            success: false,
            gas_consumed,
            storage_deltas: Vec::new(),
            events: Vec::new(),
        }
    }
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct ComputedBatch {
    pub execution_result: BatchExecutionResult,
    pub effects: BatchEffects,
    pub effects_bytes: Vec<u8>,
    pub public_inputs: PublicInputs,
    pub public_input_hash: UInt256,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct BatchResult {
    pub post_state_root: UInt256,
    pub tx_root: UInt256,
    pub receipt_root: UInt256,
    pub gas_consumed: i64,
    pub public_input_hash: UInt256,
}

#[derive(Debug, Clone, PartialEq, Eq, thiserror::Error)]
pub enum ExecutionError {
    #[error("input truncated")]
    Truncated,
    #[error("invalid {0}")]
    Invalid(&'static str),
    #[error("unsupported {0}")]
    Unsupported(&'static str),
    #[error("field {0} exceeds its canonical bound")]
    Oversized(&'static str),
    #[error("claim mismatch: {0}")]
    ClaimMismatch(&'static str),
    #[error("NeoVM adapter failed: {0}")]
    Vm(String),
}
