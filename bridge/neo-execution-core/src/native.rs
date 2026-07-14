use alloc::{
    collections::{BTreeMap, BTreeSet},
    string::ToString,
    vec,
    vec::Vec,
};

use num_bigint::{BigInt, BigUint, Sign};
use num_traits::{One, ToPrimitive, Zero};

use crate::{
    hashing::{hash160, hash256, merkle_root, normalize_signed_le},
    types::{
        BatchEffects, CanonicalStackValue, ExecutionError, ExecutionEvent, ExecutionPayload,
        L1Message, StorageDelta, StorageOperation, UInt160, UInt256,
    },
};

const L2_MESSAGE_ID: i32 = -103;
const L2_BRIDGE_ID: i32 = -104;
const BRIDGED_NEP17_ID: i32 = -109;
const TOKEN_MANAGEMENT_ID: i32 = -12;
const CONTRACT_MANAGEMENT_ID: i32 = -1;

const PREFIX_CONTRACT: u8 = 0x08;
const PREFIX_TOKEN_STATE: u8 = 0x0a;
const PREFIX_ACCOUNT_STATE: u8 = 0x0c;
const PREFIX_MESSAGE_OUTBOUND_NONCE: u8 = 0x01;
const KEY_MESSAGE_CHAIN_ID: u8 = 0x03;
const PREFIX_BRIDGE_MAPPING: u8 = 0x01;
const PREFIX_BRIDGE_DEPOSIT_CONSUMED: u8 = 0x02;
const PREFIX_BRIDGE_WITHDRAWAL_NONCE: u8 = 0x03;
const PREFIX_BRIDGE_MAPPING_BY_L2: u8 = 0x04;
const PREFIX_AUTHORIZED_BRIDGE: u8 = 0x03;
const KEY_BRIDGE: u8 = 0xfe;

const STACK_INTEGER: u8 = 0x21;
const STACK_BYTE_STRING: u8 = 0x28;
const STACK_STRUCT: u8 = 0x41;
const TOKEN_TYPE_FUNGIBLE: u8 = 1;
const MAX_TOKEN_DECIMALS: u8 = 18;

const L2_MESSAGE_CPU_FEE: i64 = 1 << 15;
const L2_BRIDGE_CPU_FEE: i64 = 1 << 15;
const BRIDGED_NEP17_CPU_FEE: i64 = 1 << 17;
const TOKEN_MANAGEMENT_CPU_FEE: i64 = 1 << 17;
const NATIVE_STORAGE_FEE: i64 = 1 << 7;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NativeTransitionV1 {
    pub storage_deltas: Vec<StorageDelta>,
    pub events: Vec<ExecutionEvent>,
    pub return_value: u64,
    pub native_fee: i64,
}

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct NativeCallContextV1 {
    pub chain_id: u32,
    pub caller: UInt160,
    pub exec_fee_factor: u32,
    pub storage_price: u32,
}

#[derive(Debug, Clone)]
struct TokenStateV1 {
    token_type: u8,
    owner: UInt160,
    name: Vec<u8>,
    symbol: Vec<u8>,
    decimals: u8,
    total_supply: BigInt,
    max_supply: BigInt,
}

struct TransitionState<'a> {
    base: &'a BTreeMap<Vec<u8>, Vec<u8>>,
    writes: BTreeMap<Vec<u8>, Option<Vec<u8>>>,
}

impl<'a> TransitionState<'a> {
    fn new(base: &'a BTreeMap<Vec<u8>, Vec<u8>>) -> Self {
        Self {
            base,
            writes: BTreeMap::new(),
        }
    }

    fn get(&self, key: &[u8]) -> Option<&[u8]> {
        match self.writes.get(key) {
            Some(Some(value)) => Some(value),
            Some(None) => None,
            None => self.base.get(key).map(Vec::as_slice),
        }
    }

    fn put(&mut self, key: Vec<u8>, value: Vec<u8>) {
        self.writes.insert(key, Some(value));
    }

    fn delete(&mut self, key: Vec<u8>) {
        self.writes.insert(key, None);
    }

    fn into_deltas(self) -> Vec<StorageDelta> {
        self.writes
            .into_iter()
            .filter_map(|(key, new_value)| {
                let old_value = self.base.get(&key).cloned();
                if old_value == new_value {
                    return None;
                }
                let operation = match (old_value.is_some(), new_value.is_some()) {
                    (false, true) => StorageOperation::Add,
                    (true, true) => StorageOperation::Update,
                    (true, false) => StorageOperation::Delete,
                    (false, false) => return None,
                };
                Some(StorageDelta {
                    key,
                    operation,
                    old_value,
                    new_value,
                })
            })
            .collect()
    }
}

#[must_use]
pub fn native_contract_hash(name: &str) -> UInt160 {
    let name = name.as_bytes();
    let mut script = Vec::with_capacity(25 + name.len());
    script.push(0x38);
    script.extend_from_slice(&[0x0c, 20]);
    script.extend_from_slice(&[0u8; 20]);
    script.push(0x10);
    script.extend_from_slice(&[0x0c, u8::try_from(name.len()).unwrap_or(u8::MAX)]);
    script.extend_from_slice(name);
    hash160(&script)
}

#[must_use]
pub fn l2_message_hash() -> UInt160 {
    native_contract_hash("L2MessageContract")
}

#[must_use]
pub fn l2_bridge_hash() -> UInt160 {
    native_contract_hash("L2BridgeContract")
}

#[must_use]
pub fn bridged_nep17_hash() -> UInt160 {
    native_contract_hash("BridgedNep17Contract")
}

#[must_use]
pub fn token_management_hash() -> UInt160 {
    native_contract_hash("TokenManagement")
}

#[must_use]
pub fn governance_hash() -> UInt160 {
    native_contract_hash("Governance")
}

#[must_use]
pub fn contract_management_key(hash: &UInt160) -> Vec<u8> {
    storage_key(CONTRACT_MANAGEMENT_ID, PREFIX_CONTRACT, &[hash])
}

pub fn apply_l1_inbox_v1(
    payload: &ExecutionPayload,
    state: &mut BTreeMap<Vec<u8>, Vec<u8>>,
) -> Result<(), ExecutionError> {
    if payload.l1_messages.is_empty() {
        return Ok(());
    }
    let mut transition = TransitionState::new(state);
    for message in &payload.l1_messages {
        apply_deposit_v1(payload.chain_id, &mut transition, message)?;
    }
    let deltas = transition.into_deltas();
    apply_transition(state, &deltas)?;
    Ok(())
}

pub fn native_emit_message_v1(
    state: &BTreeMap<Vec<u8>, Vec<u8>>,
    context: &NativeCallContextV1,
    target_chain_id: u32,
    receiver: UInt160,
    message_type: u8,
    message_payload: Vec<u8>,
) -> Result<NativeTransitionV1, ExecutionError> {
    require_native_contracts(state, &[l2_message_hash()])?;
    if context.chain_id == 0
        || context.caller == [0u8; 20]
        || receiver == [0u8; 20]
        || target_chain_id == context.chain_id
        || message_type > 4
    {
        return Err(ExecutionError::Invalid("native message arguments"));
    }
    let configured_chain = read_unsigned_storage_integer(
        state
            .get(&storage_key(L2_MESSAGE_ID, KEY_MESSAGE_CHAIN_ID, &[]))
            .map(Vec::as_slice),
        "L2Message chain id",
    )?
    .to_u32()
    .ok_or(ExecutionError::Invalid("L2Message chain id"))?;
    if configured_chain != context.chain_id {
        return Err(ExecutionError::Invalid("L2Message chain id"));
    }

    let nonce_key = storage_key(
        L2_MESSAGE_ID,
        PREFIX_MESSAGE_OUTBOUND_NONCE,
        &[&context.caller],
    );
    let current =
        read_unsigned_storage_integer(state.get(&nonce_key).map(Vec::as_slice), "message nonce")?;
    let next = current + BigUint::one();
    let nonce = next
        .to_u64()
        .ok_or(ExecutionError::Invalid("message nonce overflow"))?;
    let mut transition = TransitionState::new(state);
    transition.put(nonce_key, unsigned_storage_integer(&next));
    let event = ExecutionEvent {
        script_hash: l2_message_hash(),
        name: "MessageEmitted".to_string(),
        state: CanonicalStackValue::Array(vec![
            stack_unsigned(BigUint::from(context.chain_id)),
            stack_unsigned(BigUint::from(target_chain_id)),
            stack_unsigned(BigUint::from(nonce)),
            CanonicalStackValue::ByteString(context.caller.to_vec()),
            CanonicalStackValue::ByteString(receiver.to_vec()),
            stack_unsigned(BigUint::from(message_type)),
            CanonicalStackValue::ByteString(message_payload),
        ]),
    };
    Ok(NativeTransitionV1 {
        storage_deltas: transition.into_deltas(),
        events: vec![event],
        return_value: nonce,
        native_fee: native_fee(
            L2_MESSAGE_CPU_FEE,
            NATIVE_STORAGE_FEE,
            1,
            context.exec_fee_factor,
            context.storage_price,
        )?,
    })
}

pub fn native_initiate_withdrawal_v1(
    state: &BTreeMap<Vec<u8>, Vec<u8>>,
    context: &NativeCallContextV1,
    l2_asset: UInt160,
    amount_bytes: &[u8],
    l1_recipient: UInt160,
) -> Result<NativeTransitionV1, ExecutionError> {
    require_native_contracts(
        state,
        &[
            l2_bridge_hash(),
            bridged_nep17_hash(),
            token_management_hash(),
        ],
    )?;
    require_bridge_authorization(&TransitionState::new(state))?;
    if context.chain_id == 0
        || context.caller == [0u8; 20]
        || l2_asset == [0u8; 20]
        || l1_recipient == [0u8; 20]
    {
        return Err(ExecutionError::Invalid("native withdrawal arguments"));
    }
    let amount = parse_positive_unsigned(amount_bytes, "withdrawal amount")?;
    require_mint_bound(&amount, "withdrawal amount")?;

    let mapping_key = storage_key(L2_BRIDGE_ID, PREFIX_BRIDGE_MAPPING_BY_L2, &[&l2_asset]);
    let mapping = read_mapping(
        state.get(&mapping_key).map(Vec::as_slice),
        "L2 bridge reverse mapping",
    )?;
    let l1_amount = scale_amount(&amount, mapping.2, mapping.1)?;

    let nonce_key = storage_key(
        L2_BRIDGE_ID,
        PREFIX_BRIDGE_WITHDRAWAL_NONCE,
        &[&context.caller],
    );
    let current_nonce = read_unsigned_storage_integer(
        state.get(&nonce_key).map(Vec::as_slice),
        "withdrawal nonce",
    )?;
    let next_nonce = current_nonce + BigUint::one();
    let nonce = next_nonce
        .to_u64()
        .ok_or(ExecutionError::Invalid("withdrawal nonce overflow"))?;

    let token_key = storage_key(TOKEN_MANAGEMENT_ID, PREFIX_TOKEN_STATE, &[&l2_asset]);
    let token_bytes = state
        .get(&token_key)
        .ok_or(ExecutionError::Invalid("missing token state"))?;
    let mut token = parse_token_state(token_bytes)?;
    validate_bridged_token(&token, &l2_asset, mapping.2)?;
    let amount_signed = BigInt::from_biguint(Sign::Plus, amount.clone());
    if token.total_supply < amount_signed {
        return Err(ExecutionError::Invalid("insufficient token supply"));
    }
    token.total_supply -= &amount_signed;

    let account_key = storage_key(
        TOKEN_MANAGEMENT_ID,
        PREFIX_ACCOUNT_STATE,
        &[&context.caller, &l2_asset],
    );
    let account_bytes = state
        .get(&account_key)
        .ok_or(ExecutionError::Invalid("missing token balance"))?;
    let balance = parse_account_state(account_bytes)?;
    if balance < amount_signed {
        return Err(ExecutionError::Invalid("insufficient token balance"));
    }
    let new_balance = balance - &amount_signed;

    let mut transition = TransitionState::new(state);
    transition.put(nonce_key, unsigned_storage_integer(&next_nonce));
    transition.put(token_key, encode_token_state(&token));
    if new_balance.is_zero() {
        transition.delete(account_key);
    } else {
        transition.put(account_key, encode_account_state(&new_balance));
    }

    let transfer = ExecutionEvent {
        script_hash: token_management_hash(),
        name: "Transfer".to_string(),
        state: CanonicalStackValue::Array(vec![
            CanonicalStackValue::ByteString(l2_asset.to_vec()),
            CanonicalStackValue::ByteString(context.caller.to_vec()),
            CanonicalStackValue::Null,
            stack_unsigned(amount.clone()),
        ]),
    };
    let withdrawal = ExecutionEvent {
        script_hash: l2_bridge_hash(),
        name: "WithdrawalEmitted".to_string(),
        state: CanonicalStackValue::Array(vec![
            CanonicalStackValue::ByteString(context.caller.to_vec()),
            CanonicalStackValue::ByteString(l1_recipient.to_vec()),
            CanonicalStackValue::ByteString(l2_asset.to_vec()),
            stack_unsigned(l1_amount),
            stack_unsigned(BigUint::from(nonce)),
        ]),
    };
    let cpu_fee = L2_BRIDGE_CPU_FEE
        .checked_add(BRIDGED_NEP17_CPU_FEE)
        .and_then(|fee| fee.checked_add(TOKEN_MANAGEMENT_CPU_FEE))
        .ok_or(ExecutionError::Invalid("native fee overflow"))?;
    Ok(NativeTransitionV1 {
        storage_deltas: transition.into_deltas(),
        events: vec![transfer, withdrawal],
        return_value: nonce,
        native_fee: native_fee(
            cpu_fee,
            NATIVE_STORAGE_FEE,
            3,
            context.exec_fee_factor,
            context.storage_price,
        )?,
    })
}

pub fn derive_outbound_roots_v1(
    chain_id: u32,
    effects: &BatchEffects,
) -> Result<(UInt256, UInt256, UInt256), ExecutionError> {
    if chain_id == 0 {
        return Err(ExecutionError::Invalid("zero L2 chain id"));
    }
    let bridge = l2_bridge_hash();
    let messenger = l2_message_hash();
    let mut withdrawals = Vec::<UInt256>::new();
    let mut l2_to_l1 = Vec::<UInt256>::new();
    let mut l2_to_l2 = Vec::<UInt256>::new();
    let mut withdrawal_nonces = BTreeSet::<(UInt160, u64)>::new();
    let mut message_nonces = BTreeSet::<(UInt160, u64)>::new();

    for transaction in &effects.transactions {
        if !transaction.receipt.success {
            continue;
        }
        for event in &transaction.events {
            if event.script_hash == bridge && event.name == "WithdrawalEmitted" {
                let (sender, recipient, asset, amount, nonce) = parse_withdrawal_event(event)?;
                if !withdrawal_nonces.insert((sender, nonce)) {
                    return Err(ExecutionError::Invalid("duplicate withdrawal event nonce"));
                }
                withdrawals.push(withdrawal_hash(
                    chain_id, &bridge, &sender, &recipient, &asset, &amount, nonce,
                ));
            } else if event.script_hash == messenger && event.name == "MessageEmitted" {
                let message = parse_message_event(chain_id, event)?;
                if !message_nonces.insert((message.sender, message.nonce)) {
                    return Err(ExecutionError::Invalid("duplicate message event nonce"));
                }
                let leaf = message_hash(&message);
                if message.target_chain_id == 0 {
                    l2_to_l1.push(leaf);
                } else {
                    l2_to_l2.push(leaf);
                }
            }
        }
    }
    Ok((
        merkle_root(&withdrawals),
        merkle_root(&l2_to_l1),
        merkle_root(&l2_to_l2),
    ))
}

fn apply_deposit_v1(
    chain_id: u32,
    state: &mut TransitionState<'_>,
    message: &L1Message,
) -> Result<(), ExecutionError> {
    require_native_contracts_view(
        state,
        &[
            l2_bridge_hash(),
            bridged_nep17_hash(),
            token_management_hash(),
        ],
    )?;
    require_bridge_authorization(state)?;
    if message.message_type != 0 {
        return Err(ExecutionError::Unsupported("L1 inbox message type V1"));
    }
    if message.source_chain_id != 0
        || message.target_chain_id != chain_id
        || message.sender == [0u8; 20]
        || message.receiver != l2_bridge_hash()
    {
        return Err(ExecutionError::Invalid("L1 deposit routing"));
    }
    let (l1_asset, recipient, amount) = parse_deposit_payload(&message.payload)?;
    let replay_key = storage_key(
        L2_BRIDGE_ID,
        PREFIX_BRIDGE_DEPOSIT_CONSUMED,
        &[
            &message.source_chain_id.to_le_bytes(),
            &message.nonce.to_le_bytes(),
        ],
    );
    if state.get(&replay_key).is_some() {
        return Err(ExecutionError::Invalid("deposit replay"));
    }
    let mapping_key = storage_key(L2_BRIDGE_ID, PREFIX_BRIDGE_MAPPING, &[&l1_asset]);
    let (l2_asset, l1_decimals, l2_decimals) =
        read_mapping(state.get(&mapping_key), "L2 bridge asset mapping")?;
    let l2_amount = scale_amount(&amount, l1_decimals, l2_decimals)?;
    require_mint_bound(&l2_amount, "deposit amount")?;

    let contract_key = contract_management_key(&recipient);
    if state.get(&contract_key).is_some() {
        return Err(ExecutionError::Unsupported("contract deposit recipient V1"));
    }
    let token_key = storage_key(TOKEN_MANAGEMENT_ID, PREFIX_TOKEN_STATE, &[&l2_asset]);
    let token_bytes = state
        .get(&token_key)
        .ok_or(ExecutionError::Invalid("missing token state"))?;
    let mut token = parse_token_state(token_bytes)?;
    validate_bridged_token(&token, &l2_asset, l2_decimals)?;
    let signed_amount = BigInt::from_biguint(Sign::Plus, l2_amount.clone());
    token.total_supply += &signed_amount;
    if token.max_supply.sign() != Sign::Minus && token.total_supply > token.max_supply {
        return Err(ExecutionError::Invalid("token maximum supply"));
    }

    let account_key = storage_key(
        TOKEN_MANAGEMENT_ID,
        PREFIX_ACCOUNT_STATE,
        &[&recipient, &l2_asset],
    );
    let balance = match state.get(&account_key) {
        Some(bytes) => parse_account_state(bytes)?,
        None => BigInt::zero(),
    };
    if balance.sign() == Sign::Minus {
        return Err(ExecutionError::Invalid("negative token balance"));
    }
    let new_balance = balance + signed_amount;
    state.put(replay_key, vec![1]);
    state.put(token_key, encode_token_state(&token));
    state.put(account_key, encode_account_state(&new_balance));
    Ok(())
}

fn require_bridge_authorization(state: &TransitionState<'_>) -> Result<(), ExecutionError> {
    let bridge = l2_bridge_hash();
    let configured = storage_key(BRIDGED_NEP17_ID, KEY_BRIDGE, &[]);
    if state.get(&configured) == Some(bridge.as_slice()) {
        return Ok(());
    }
    let authorized = storage_key(BRIDGED_NEP17_ID, PREFIX_AUTHORIZED_BRIDGE, &[&bridge]);
    if state.get(&authorized).is_some() {
        return Ok(());
    }
    Err(ExecutionError::Invalid("BridgedNep17 bridge authorization"))
}

fn validate_bridged_token(
    token: &TokenStateV1,
    asset: &UInt160,
    expected_decimals: u8,
) -> Result<(), ExecutionError> {
    if token.token_type != TOKEN_TYPE_FUNGIBLE
        || token.owner != bridged_nep17_hash()
        || token.decimals != expected_decimals
        || token.total_supply.sign() == Sign::Minus
        || token.max_supply < BigInt::from(-1)
    {
        return Err(ExecutionError::Invalid("bridged token metadata"));
    }
    let mut asset_preimage = token.owner.to_vec();
    asset_preimage.extend_from_slice(&token.name);
    if hash160(&asset_preimage) != *asset {
        return Err(ExecutionError::Invalid("bridged token asset id"));
    }
    Ok(())
}

fn parse_deposit_payload(bytes: &[u8]) -> Result<(UInt160, UInt160, BigUint), ExecutionError> {
    if bytes.len() < 45 {
        return Err(ExecutionError::Invalid("deposit payload"));
    }
    let mut l1_asset = [0u8; 20];
    l1_asset.copy_from_slice(&bytes[..20]);
    let mut recipient = [0u8; 20];
    recipient.copy_from_slice(&bytes[20..40]);
    let amount_len = i32::from_le_bytes(
        bytes[40..44]
            .try_into()
            .map_err(|_| ExecutionError::Truncated)?,
    );
    let amount_len = usize::try_from(amount_len)
        .map_err(|_| ExecutionError::Invalid("deposit amount length"))?;
    if amount_len == 0 || amount_len > 64 || 44 + amount_len != bytes.len() {
        return Err(ExecutionError::Invalid("deposit amount length"));
    }
    let amount_bytes = &bytes[44..];
    if amount_bytes.last() == Some(&0) || l1_asset == [0u8; 20] || recipient == [0u8; 20] {
        return Err(ExecutionError::Invalid("deposit payload"));
    }
    let amount = BigUint::from_bytes_le(amount_bytes);
    if amount.is_zero() {
        return Err(ExecutionError::Invalid("deposit amount"));
    }
    Ok((l1_asset, recipient, amount))
}

fn parse_positive_unsigned(bytes: &[u8], field: &'static str) -> Result<BigUint, ExecutionError> {
    if normalize_signed_le(bytes) != bytes {
        return Err(ExecutionError::Invalid(field));
    }
    let value = BigInt::from_signed_bytes_le(bytes)
        .to_biguint()
        .ok_or(ExecutionError::Invalid(field))?;
    if value.is_zero() {
        return Err(ExecutionError::Invalid(field));
    }
    Ok(value)
}

fn read_mapping(
    bytes: Option<&[u8]>,
    field: &'static str,
) -> Result<(UInt160, u8, u8), ExecutionError> {
    let bytes = bytes.ok_or(ExecutionError::Invalid(field))?;
    if bytes.len() != 22 {
        return Err(ExecutionError::Invalid(field));
    }
    let mut asset = [0u8; 20];
    asset.copy_from_slice(&bytes[..20]);
    if asset == [0u8; 20] || bytes[20] > MAX_TOKEN_DECIMALS || bytes[21] > MAX_TOKEN_DECIMALS {
        return Err(ExecutionError::Invalid(field));
    }
    Ok((asset, bytes[20], bytes[21]))
}

fn scale_amount(amount: &BigUint, from: u8, to: u8) -> Result<BigUint, ExecutionError> {
    if from > MAX_TOKEN_DECIMALS || to > MAX_TOKEN_DECIMALS {
        return Err(ExecutionError::Invalid("token decimals"));
    }
    if to >= from {
        return Ok(amount * BigUint::from(10u8).pow(u32::from(to - from)));
    }
    let divisor = BigUint::from(10u8).pow(u32::from(from - to));
    if amount % &divisor != BigUint::zero() {
        return Err(ExecutionError::Invalid("inexact decimal scaling"));
    }
    Ok(amount / divisor)
}

fn require_mint_bound(amount: &BigUint, field: &'static str) -> Result<(), ExecutionError> {
    if amount.is_zero() || amount > &(BigUint::one() << 128usize) {
        return Err(ExecutionError::Invalid(field));
    }
    Ok(())
}

fn parse_token_state(bytes: &[u8]) -> Result<TokenStateV1, ExecutionError> {
    let mut reader = NeoBinaryReader::new(bytes);
    reader.require(STACK_STRUCT, "token state")?;
    if reader.read_varint("token field count")? != 7 {
        return Err(ExecutionError::Invalid("token state"));
    }
    let token_type = reader
        .read_integer("token type")?
        .to_u8()
        .ok_or(ExecutionError::Invalid("token type"))?;
    let owner_bytes = reader.read_bytes(STACK_BYTE_STRING, "token owner")?;
    let owner: UInt160 = owner_bytes
        .try_into()
        .map_err(|_| ExecutionError::Invalid("token owner"))?;
    let name = reader.read_bytes(STACK_BYTE_STRING, "token name")?.to_vec();
    let symbol = reader
        .read_bytes(STACK_BYTE_STRING, "token symbol")?
        .to_vec();
    core::str::from_utf8(&name).map_err(|_| ExecutionError::Invalid("token name UTF-8"))?;
    core::str::from_utf8(&symbol).map_err(|_| ExecutionError::Invalid("token symbol UTF-8"))?;
    let decimals = reader
        .read_integer("token decimals")?
        .to_u8()
        .ok_or(ExecutionError::Invalid("token decimals"))?;
    let total_supply = reader.read_integer("token total supply")?;
    let max_supply = reader.read_integer("token max supply")?;
    reader.end("token state")?;
    let token = TokenStateV1 {
        token_type,
        owner,
        name,
        symbol,
        decimals,
        total_supply,
        max_supply,
    };
    if encode_token_state(&token) != bytes {
        return Err(ExecutionError::Invalid("non-canonical token state"));
    }
    Ok(token)
}

fn encode_token_state(token: &TokenStateV1) -> Vec<u8> {
    let mut bytes = vec![STACK_STRUCT, 7];
    write_integer(&mut bytes, &BigInt::from(token.token_type));
    write_bytes(&mut bytes, STACK_BYTE_STRING, &token.owner);
    write_bytes(&mut bytes, STACK_BYTE_STRING, &token.name);
    write_bytes(&mut bytes, STACK_BYTE_STRING, &token.symbol);
    write_integer(&mut bytes, &BigInt::from(token.decimals));
    write_integer(&mut bytes, &token.total_supply);
    write_integer(&mut bytes, &token.max_supply);
    bytes
}

fn parse_account_state(bytes: &[u8]) -> Result<BigInt, ExecutionError> {
    let mut reader = NeoBinaryReader::new(bytes);
    reader.require(STACK_STRUCT, "account state")?;
    if reader.read_varint("account field count")? != 1 {
        return Err(ExecutionError::Invalid("account state"));
    }
    let balance = reader.read_integer("account balance")?;
    reader.end("account state")?;
    if balance.sign() == Sign::Minus || encode_account_state(&balance) != bytes {
        return Err(ExecutionError::Invalid("account state"));
    }
    Ok(balance)
}

fn encode_account_state(balance: &BigInt) -> Vec<u8> {
    let mut bytes = vec![STACK_STRUCT, 1];
    write_integer(&mut bytes, balance);
    bytes
}

fn write_integer(bytes: &mut Vec<u8>, value: &BigInt) {
    let encoded = signed_bytes(value);
    write_bytes(bytes, STACK_INTEGER, &encoded);
}

fn write_bytes(bytes: &mut Vec<u8>, tag: u8, value: &[u8]) {
    bytes.push(tag);
    write_varint(bytes, value.len() as u64);
    bytes.extend_from_slice(value);
}

fn write_varint(bytes: &mut Vec<u8>, value: u64) {
    match value {
        0..=0xfc => bytes.push(value as u8),
        0xfd..=0xffff => {
            bytes.push(0xfd);
            bytes.extend_from_slice(&(value as u16).to_le_bytes());
        }
        0x1_0000..=0xffff_ffff => {
            bytes.push(0xfe);
            bytes.extend_from_slice(&(value as u32).to_le_bytes());
        }
        _ => {
            bytes.push(0xff);
            bytes.extend_from_slice(&value.to_le_bytes());
        }
    }
}

fn signed_bytes(value: &BigInt) -> Vec<u8> {
    value.to_signed_bytes_le()
}

struct NeoBinaryReader<'a> {
    bytes: &'a [u8],
    offset: usize,
}

impl<'a> NeoBinaryReader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, offset: 0 }
    }

    fn read_u8(&mut self) -> Result<u8, ExecutionError> {
        let value = *self
            .bytes
            .get(self.offset)
            .ok_or(ExecutionError::Truncated)?;
        self.offset += 1;
        Ok(value)
    }

    fn require(&mut self, expected: u8, field: &'static str) -> Result<(), ExecutionError> {
        if self.read_u8()? != expected {
            return Err(ExecutionError::Invalid(field));
        }
        Ok(())
    }

    fn read_varint(&mut self, field: &'static str) -> Result<u64, ExecutionError> {
        let first = self.read_u8()?;
        let value = match first {
            0x00..=0xfc => u64::from(first),
            0xfd => {
                let value = u16::from_le_bytes(self.read_fixed::<2>()?);
                if value < 0xfd {
                    return Err(ExecutionError::Invalid(field));
                }
                u64::from(value)
            }
            0xfe => {
                let value = u32::from_le_bytes(self.read_fixed::<4>()?);
                if value <= u32::from(u16::MAX) {
                    return Err(ExecutionError::Invalid(field));
                }
                u64::from(value)
            }
            0xff => {
                let value = u64::from_le_bytes(self.read_fixed::<8>()?);
                if value <= u64::from(u32::MAX) {
                    return Err(ExecutionError::Invalid(field));
                }
                value
            }
        };
        Ok(value)
    }

    fn read_fixed<const N: usize>(&mut self) -> Result<[u8; N], ExecutionError> {
        let end = self
            .offset
            .checked_add(N)
            .ok_or(ExecutionError::Truncated)?;
        let slice = self
            .bytes
            .get(self.offset..end)
            .ok_or(ExecutionError::Truncated)?;
        self.offset = end;
        slice.try_into().map_err(|_| ExecutionError::Truncated)
    }

    fn read_bytes(&mut self, tag: u8, field: &'static str) -> Result<&'a [u8], ExecutionError> {
        self.require(tag, field)?;
        let length = usize::try_from(self.read_varint(field)?)
            .map_err(|_| ExecutionError::Invalid(field))?;
        let end = self
            .offset
            .checked_add(length)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.offset..end)
            .ok_or(ExecutionError::Truncated)?;
        self.offset = end;
        Ok(value)
    }

    fn read_integer(&mut self, field: &'static str) -> Result<BigInt, ExecutionError> {
        let bytes = self.read_bytes(STACK_INTEGER, field)?;
        let value = BigInt::from_signed_bytes_le(bytes);
        if signed_bytes(&value) != bytes {
            return Err(ExecutionError::Invalid(field));
        }
        Ok(value)
    }

    fn end(&self, field: &'static str) -> Result<(), ExecutionError> {
        if self.offset != self.bytes.len() {
            return Err(ExecutionError::Invalid(field));
        }
        Ok(())
    }
}

fn parse_withdrawal_event(
    event: &ExecutionEvent,
) -> Result<(UInt160, UInt160, UInt160, BigUint, u64), ExecutionError> {
    let values = event_array(event, 5, "WithdrawalEmitted event")?;
    let sender = stack_hash160(&values[0], "withdrawal sender")?;
    let recipient = stack_hash160(&values[1], "withdrawal recipient")?;
    let asset = stack_hash160(&values[2], "withdrawal asset")?;
    let amount = stack_positive(&values[3], "withdrawal amount")?;
    if amount.to_bytes_le().len() > 64 {
        return Err(ExecutionError::Oversized("withdrawal amount"));
    }
    let nonce = stack_unsigned_integer(&values[4], "withdrawal nonce")?
        .to_u64()
        .ok_or(ExecutionError::Invalid("withdrawal nonce"))?;
    if sender == [0u8; 20] || recipient == [0u8; 20] || asset == [0u8; 20] || nonce == 0 {
        return Err(ExecutionError::Invalid("WithdrawalEmitted event"));
    }
    Ok((sender, recipient, asset, amount, nonce))
}

fn parse_message_event(chain_id: u32, event: &ExecutionEvent) -> Result<L1Message, ExecutionError> {
    let values = event_array(event, 7, "MessageEmitted event")?;
    let source_chain_id = stack_unsigned_integer(&values[0], "message source chain")?
        .to_u32()
        .ok_or(ExecutionError::Invalid("message source chain"))?;
    let target_chain_id = stack_unsigned_integer(&values[1], "message target chain")?
        .to_u32()
        .ok_or(ExecutionError::Invalid("message target chain"))?;
    let nonce = stack_unsigned_integer(&values[2], "message nonce")?
        .to_u64()
        .ok_or(ExecutionError::Invalid("message nonce"))?;
    let sender = stack_hash160(&values[3], "message sender")?;
    let receiver = stack_hash160(&values[4], "message receiver")?;
    let message_type = stack_unsigned_integer(&values[5], "message type")?
        .to_u8()
        .ok_or(ExecutionError::Invalid("message type"))?;
    let payload = match &values[6] {
        CanonicalStackValue::ByteString(bytes) => bytes.clone(),
        _ => return Err(ExecutionError::Invalid("message payload")),
    };
    if source_chain_id != chain_id
        || target_chain_id == source_chain_id
        || nonce == 0
        || sender == [0u8; 20]
        || receiver == [0u8; 20]
        || message_type > 4
    {
        return Err(ExecutionError::Invalid("MessageEmitted event"));
    }
    Ok(L1Message {
        source_chain_id,
        target_chain_id,
        nonce,
        sender,
        receiver,
        message_type,
        payload,
    })
}

fn event_array<'a>(
    event: &'a ExecutionEvent,
    count: usize,
    field: &'static str,
) -> Result<&'a [CanonicalStackValue], ExecutionError> {
    match &event.state {
        CanonicalStackValue::Array(values) if values.len() == count => Ok(values),
        _ => Err(ExecutionError::Invalid(field)),
    }
}

fn stack_hash160(
    value: &CanonicalStackValue,
    field: &'static str,
) -> Result<UInt160, ExecutionError> {
    let CanonicalStackValue::ByteString(bytes) = value else {
        return Err(ExecutionError::Invalid(field));
    };
    bytes
        .as_slice()
        .try_into()
        .map_err(|_| ExecutionError::Invalid(field))
}

fn stack_positive(
    value: &CanonicalStackValue,
    field: &'static str,
) -> Result<BigUint, ExecutionError> {
    let value = stack_unsigned_integer(value, field)?;
    if value.is_zero() {
        return Err(ExecutionError::Invalid(field));
    }
    Ok(value)
}

fn stack_unsigned_integer(
    value: &CanonicalStackValue,
    field: &'static str,
) -> Result<BigUint, ExecutionError> {
    let CanonicalStackValue::Integer(bytes) = value else {
        return Err(ExecutionError::Invalid(field));
    };
    if normalize_signed_le(bytes) != *bytes {
        return Err(ExecutionError::Invalid(field));
    }
    let value = BigInt::from_signed_bytes_le(bytes);
    value.to_biguint().ok_or(ExecutionError::Invalid(field))
}

fn withdrawal_hash(
    chain_id: u32,
    emitting_contract: &UInt160,
    sender: &UInt160,
    recipient: &UInt160,
    asset: &UInt160,
    amount: &BigUint,
    nonce: u64,
) -> UInt256 {
    let amount = amount.to_bytes_le();
    let mut bytes = Vec::with_capacity(96 + amount.len());
    bytes.extend_from_slice(&chain_id.to_le_bytes());
    bytes.extend_from_slice(emitting_contract);
    bytes.extend_from_slice(sender);
    bytes.extend_from_slice(recipient);
    bytes.extend_from_slice(asset);
    bytes.extend_from_slice(&(amount.len() as u32).to_le_bytes());
    bytes.extend_from_slice(&amount);
    bytes.extend_from_slice(&nonce.to_le_bytes());
    hash256(&bytes)
}

fn message_hash(message: &L1Message) -> UInt256 {
    let mut bytes = Vec::with_capacity(61 + message.payload.len());
    bytes.extend_from_slice(&message.source_chain_id.to_le_bytes());
    bytes.extend_from_slice(&message.target_chain_id.to_le_bytes());
    bytes.extend_from_slice(&message.nonce.to_le_bytes());
    bytes.extend_from_slice(&message.sender);
    bytes.extend_from_slice(&message.receiver);
    bytes.push(message.message_type);
    bytes.extend_from_slice(&(message.payload.len() as u32).to_le_bytes());
    bytes.extend_from_slice(&message.payload);
    hash256(&bytes)
}

fn storage_key(id: i32, prefix: u8, parts: &[&[u8]]) -> Vec<u8> {
    let length = 5 + parts.iter().map(|part| part.len()).sum::<usize>();
    let mut key = Vec::with_capacity(length);
    key.extend_from_slice(&id.to_le_bytes());
    key.push(prefix);
    for part in parts {
        key.extend_from_slice(part);
    }
    key
}

fn require_native_contracts(
    state: &BTreeMap<Vec<u8>, Vec<u8>>,
    hashes: &[UInt160],
) -> Result<(), ExecutionError> {
    for hash in hashes {
        if state
            .get(&contract_management_key(hash))
            .is_none_or(Vec::is_empty)
        {
            return Err(ExecutionError::Invalid("native contract state witness"));
        }
    }
    Ok(())
}

fn require_native_contracts_view(
    state: &TransitionState<'_>,
    hashes: &[UInt160],
) -> Result<(), ExecutionError> {
    for hash in hashes {
        if state
            .get(&contract_management_key(hash))
            .is_none_or(<[u8]>::is_empty)
        {
            return Err(ExecutionError::Invalid("native contract state witness"));
        }
    }
    Ok(())
}

fn read_unsigned_storage_integer(
    bytes: Option<&[u8]>,
    field: &'static str,
) -> Result<BigUint, ExecutionError> {
    let Some(bytes) = bytes else {
        return Ok(BigUint::zero());
    };
    if normalize_signed_le(bytes) != bytes {
        return Err(ExecutionError::Invalid(field));
    }
    BigInt::from_signed_bytes_le(bytes)
        .to_biguint()
        .ok_or(ExecutionError::Invalid(field))
}

fn unsigned_storage_integer(value: &BigUint) -> Vec<u8> {
    if value.is_zero() {
        return Vec::new();
    }
    let mut bytes = value.to_bytes_le();
    if bytes.last().is_some_and(|byte| byte & 0x80 != 0) {
        bytes.push(0);
    }
    bytes
}

fn stack_unsigned(value: BigUint) -> CanonicalStackValue {
    CanonicalStackValue::Integer(unsigned_storage_integer(&value))
}

fn native_fee(
    cpu_fee: i64,
    storage_fee: i64,
    trampoline_count: i64,
    exec_fee_factor: u32,
    storage_price: u32,
) -> Result<i64, ExecutionError> {
    cpu_fee
        .checked_mul(i64::from(exec_fee_factor))
        .and_then(|fee| {
            storage_fee
                .checked_mul(i64::from(storage_price))
                .and_then(|storage| fee.checked_add(storage))
        })
        .and_then(|fee| {
            trampoline_count
                .checked_mul(i64::from(exec_fee_factor))
                .and_then(|trampoline| fee.checked_add(trampoline))
        })
        .ok_or(ExecutionError::Invalid("native fee overflow"))
}

fn apply_transition(
    state: &mut BTreeMap<Vec<u8>, Vec<u8>>,
    deltas: &[StorageDelta],
) -> Result<(), ExecutionError> {
    for delta in deltas {
        match &delta.new_value {
            Some(value) => {
                state.insert(delta.key.clone(), value.clone());
            }
            None => {
                if state.remove(&delta.key).is_none() {
                    return Err(ExecutionError::Invalid("native delete target"));
                }
            }
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    extern crate std;

    use super::*;
    use crate::{
        hashing::keyed_state_root_from_map,
        types::{BatchBlockContext, CanonicalReceiptV1, ProtocolConfig, TransactionEffects},
    };

    const CHAIN_ID: u32 = 1099;

    struct DepositFixture {
        payload: ExecutionPayload,
        state: BTreeMap<Vec<u8>, Vec<u8>>,
        account_key: Vec<u8>,
        token_key: Vec<u8>,
        replay_key: Vec<u8>,
    }

    #[test]
    fn native_hashes_match_n4_core_goldens() {
        assert_eq!(
            l2_bridge_hash(),
            hex20("e44d9687201a211e2ee8809d55b42152442a9a05")
        );
        assert_eq!(
            l2_message_hash(),
            hex20("789d668525bfeb59a82300bdecebffc864f24295")
        );
        assert_eq!(
            bridged_nep17_hash(),
            hex20("4fca40807f7bc98fc22f9642cc66302f9a4d3318")
        );
        assert_eq!(
            token_management_hash(),
            hex20("9f040ea4a8448f015af645659b0fb2ae7dc500ae")
        );
        assert_eq!(
            governance_hash(),
            hex20("67ca70350663bf258ca513049467c6059d15e74c")
        );
    }

    #[test]
    fn deposit_v1_updates_real_native_token_state_and_rejects_replay() {
        let DepositFixture {
            payload,
            mut state,
            account_key,
            token_key,
            replay_key,
        } = deposit_fixture();
        let pre_root = keyed_state_root_from_map(&state);
        assert_eq!(pre_root, payload.pre_state_root);

        apply_l1_inbox_v1(&payload, &mut state).unwrap();
        let post_root = keyed_state_root_from_map(&state);
        assert_eq!(
            pre_root,
            hex32("4900cdb769870ea32d24f925965824a6efcd13ac7a495982b4004c3abb42a1a9")
        );
        assert_eq!(
            post_root,
            hex32("bac5b837912c96232e40e471c9b5c2bff886f8b1cdbe8417004ed0ad4df143ac")
        );
        assert_eq!(
            state.get(&replay_key).map(Vec::as_slice),
            Some([1].as_slice())
        );
        assert_eq!(
            parse_account_state(state.get(&account_key).unwrap()).unwrap(),
            BigInt::from(12_345_600u64)
        );
        assert_eq!(
            parse_token_state(state.get(&token_key).unwrap())
                .unwrap()
                .total_supply,
            BigInt::from(12_345_605u64)
        );

        let replay = apply_l1_inbox_v1(&payload, &mut state).unwrap_err();
        assert_eq!(replay, ExecutionError::Invalid("deposit replay"));
    }

    #[test]
    fn deposit_v1_tampering_fails_closed() {
        let DepositFixture { payload, state, .. } = deposit_fixture();
        let cases = [
            (
                "source",
                mutate(&payload, |message| message.source_chain_id = 1),
            ),
            (
                "target",
                mutate(&payload, |message| message.target_chain_id += 1),
            ),
            (
                "receiver",
                mutate(&payload, |message| message.receiver[0] ^= 1),
            ),
            (
                "amount",
                mutate(&payload, |message| *message.payload.last_mut().unwrap() = 0),
            ),
            ("type", mutate(&payload, |message| message.message_type = 2)),
        ];
        for (name, tampered) in cases {
            let mut candidate = state.clone();
            assert!(
                apply_l1_inbox_v1(&tampered, &mut candidate).is_err(),
                "{name}"
            );
            assert_eq!(candidate, state, "{name} must not partially mutate state");
        }

        let mut missing_mapping = state.clone();
        let l1_asset = [0x22; 20];
        missing_mapping.remove(&storage_key(
            L2_BRIDGE_ID,
            PREFIX_BRIDGE_MAPPING,
            &[&l1_asset],
        ));
        assert!(apply_l1_inbox_v1(&payload, &mut missing_mapping).is_err());

        let mut decimals = state.clone();
        let mapping = decimals
            .get_mut(&storage_key(
                L2_BRIDGE_ID,
                PREFIX_BRIDGE_MAPPING,
                &[&l1_asset],
            ))
            .unwrap();
        mapping[21] = 19;
        assert!(apply_l1_inbox_v1(&payload, &mut decimals).is_err());
    }

    #[test]
    fn outbound_v1_roots_bind_native_abi_order_and_parameters() {
        let effects = outbound_effects();
        let roots = derive_outbound_roots_v1(CHAIN_ID, &effects).unwrap();
        assert_eq!(
            roots.0,
            hex32("737a25af11ca940d4e9004a43bd0129f3000df624a9d845752439d819ddae593")
        );
        assert_eq!(
            roots.1,
            hex32("00f3a7ccaf825db24ea4a674fd9be0af0a5f7cda0f8ebd28d9ca345b4ef1e8af")
        );
        assert_eq!(
            roots.2,
            hex32("d6aa57f677872096fa2ce57c58d652f0e632179fa3a2fe5d2b6fb1c957c10881")
        );

        let mut tampered = effects.clone();
        let CanonicalStackValue::Array(values) = &mut tampered.transactions[0].events[0].state
        else {
            unreachable!()
        };
        values[3] = stack_unsigned(BigUint::from(124u8));
        assert_ne!(
            derive_outbound_roots_v1(CHAIN_ID, &tampered).unwrap().0,
            roots.0
        );

        let mut malformed = effects.clone();
        malformed.transactions[0].events[1].state = CanonicalStackValue::Array(Vec::new());
        assert_eq!(
            derive_outbound_roots_v1(CHAIN_ID, &malformed).unwrap_err(),
            ExecutionError::Invalid("MessageEmitted event")
        );

        let mut spoofed = effects.clone();
        spoofed.transactions[0].events[1].script_hash = [0x99; 20];
        let spoofed_roots = derive_outbound_roots_v1(CHAIN_ID, &spoofed).unwrap();
        assert_eq!(spoofed_roots.1, [0u8; 32]);
    }

    fn deposit_fixture() -> DepositFixture {
        let owner = bridged_nep17_hash();
        let name = b"Wrapped GAS".to_vec();
        let mut asset_preimage = owner.to_vec();
        asset_preimage.extend_from_slice(&name);
        let l2_asset = hash160(&asset_preimage);
        let l1_asset = [0x22; 20];
        let recipient = [0x33; 20];
        let token = TokenStateV1 {
            token_type: TOKEN_TYPE_FUNGIBLE,
            owner,
            name,
            symbol: b"WGAS".to_vec(),
            decimals: 8,
            total_supply: BigInt::from(5u8),
            max_supply: BigInt::from(1_000_000_000u64),
        };
        let token_key = storage_key(TOKEN_MANAGEMENT_ID, PREFIX_TOKEN_STATE, &[&l2_asset]);
        let account_key = storage_key(
            TOKEN_MANAGEMENT_ID,
            PREFIX_ACCOUNT_STATE,
            &[&recipient, &l2_asset],
        );
        let replay_key = storage_key(
            L2_BRIDGE_ID,
            PREFIX_BRIDGE_DEPOSIT_CONSUMED,
            &[&0u32.to_le_bytes(), &7u64.to_le_bytes()],
        );
        let mut state = BTreeMap::new();
        for hash in [
            l2_bridge_hash(),
            bridged_nep17_hash(),
            token_management_hash(),
        ] {
            state.insert(contract_management_key(&hash), b"native".to_vec());
        }
        state.insert(
            storage_key(BRIDGED_NEP17_ID, KEY_BRIDGE, &[]),
            l2_bridge_hash().to_vec(),
        );
        let mut mapping = l2_asset.to_vec();
        mapping.extend_from_slice(&[6, 8]);
        state.insert(
            storage_key(L2_BRIDGE_ID, PREFIX_BRIDGE_MAPPING, &[&l1_asset]),
            mapping,
        );
        state.insert(token_key.clone(), encode_token_state(&token));

        let amount = 123_456u64.to_le_bytes();
        let amount = amount[..3].to_vec();
        let mut deposit = l1_asset.to_vec();
        deposit.extend_from_slice(&recipient);
        deposit.extend_from_slice(&(amount.len() as i32).to_le_bytes());
        deposit.extend_from_slice(&amount);
        let message = L1Message {
            source_chain_id: 0,
            target_chain_id: CHAIN_ID,
            nonce: 7,
            sender: [0x11; 20],
            receiver: l2_bridge_hash(),
            message_type: 0,
            payload: deposit,
        };
        let pre_state_root = keyed_state_root_from_map(&state);
        let payload = ExecutionPayload {
            chain_id: CHAIN_ID,
            batch_number: 1,
            first_block: 1,
            last_block: 1,
            pre_state_root,
            block_context: BatchBlockContext {
                l1_finalized_height: 10,
                first_block_timestamp: 100,
                last_block_timestamp: 100,
                sequencer_committee_hash: [0x44; 32],
                network: 0x334f_454e,
            },
            l1_messages: vec![message],
            forced_inclusions: Vec::new(),
            transactions: Vec::new(),
        };
        DepositFixture {
            payload,
            state,
            account_key,
            token_key,
            replay_key,
        }
    }

    fn mutate<F>(payload: &ExecutionPayload, mutate: F) -> ExecutionPayload
    where
        F: FnOnce(&mut L1Message),
    {
        let mut payload = payload.clone();
        mutate(&mut payload.l1_messages[0]);
        payload
    }

    fn outbound_effects() -> BatchEffects {
        let withdrawal = ExecutionEvent {
            script_hash: l2_bridge_hash(),
            name: "WithdrawalEmitted".to_string(),
            state: CanonicalStackValue::Array(vec![
                CanonicalStackValue::ByteString(vec![0x44; 20]),
                CanonicalStackValue::ByteString(vec![0x77; 20]),
                CanonicalStackValue::ByteString(vec![0x88; 20]),
                stack_unsigned(BigUint::from(123u8)),
                stack_unsigned(BigUint::one()),
            ]),
        };
        let l1_message = ExecutionEvent {
            script_hash: l2_message_hash(),
            name: "MessageEmitted".to_string(),
            state: CanonicalStackValue::Array(vec![
                stack_unsigned(BigUint::from(CHAIN_ID)),
                stack_unsigned(BigUint::zero()),
                stack_unsigned(BigUint::one()),
                CanonicalStackValue::ByteString(vec![0x44; 20]),
                CanonicalStackValue::ByteString(vec![0x55; 20]),
                stack_unsigned(BigUint::from(2u8)),
                CanonicalStackValue::ByteString(b"abc".to_vec()),
            ]),
        };
        let l2_message = ExecutionEvent {
            script_hash: l2_message_hash(),
            name: "MessageEmitted".to_string(),
            state: CanonicalStackValue::Array(vec![
                stack_unsigned(BigUint::from(CHAIN_ID)),
                stack_unsigned(BigUint::from(2200u32)),
                stack_unsigned(BigUint::from(2u8)),
                CanonicalStackValue::ByteString(vec![0x44; 20]),
                CanonicalStackValue::ByteString(vec![0x66; 20]),
                stack_unsigned(BigUint::from(3u8)),
                CanonicalStackValue::ByteString(b"xyz".to_vec()),
            ]),
        };
        BatchEffects {
            transactions: vec![TransactionEffects {
                receipt: CanonicalReceiptV1 {
                    tx_hash: [0x11; 32],
                    success: true,
                    gas_consumed: 1,
                    storage_delta_hash: [0u8; 32],
                    events_hash: [0u8; 32],
                },
                storage_deltas: Vec::new(),
                events: vec![withdrawal, l1_message, l2_message],
            }],
        }
    }

    fn hex20(value: &str) -> UInt160 {
        let mut bytes = [0u8; 20];
        for (index, chunk) in value.as_bytes().chunks_exact(2).enumerate() {
            bytes[index] = (nibble(chunk[0]) << 4) | nibble(chunk[1]);
        }
        bytes
    }

    fn hex32(value: &str) -> UInt256 {
        let mut bytes = [0u8; 32];
        for (index, chunk) in value.as_bytes().chunks_exact(2).enumerate() {
            bytes[index] = (nibble(chunk[0]) << 4) | nibble(chunk[1]);
        }
        bytes
    }

    fn nibble(value: u8) -> u8 {
        match value {
            b'0'..=b'9' => value - b'0',
            b'a'..=b'f' => value - b'a' + 10,
            _ => panic!("invalid hex"),
        }
    }

    #[allow(dead_code)]
    fn default_config() -> ProtocolConfig {
        ProtocolConfig {
            exec_fee_factor: crate::DEFAULT_EXEC_FEE_FACTOR,
            storage_price: crate::DEFAULT_STORAGE_PRICE,
            address_version: crate::DEFAULT_ADDRESS_VERSION,
            per_tx_gas_limit: crate::DEFAULT_PER_TX_GAS_LIMIT,
        }
    }
}
