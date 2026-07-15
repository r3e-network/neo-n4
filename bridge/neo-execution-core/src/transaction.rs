use alloc::{boxed::Box, vec::Vec};

use crate::{
    hash256,
    types::{
        ExecutionError, ParsedTransaction, Signer, TransactionWitness, WitnessCondition,
        WitnessRule, WitnessRuleAction,
    },
};

const MAX_TRANSACTION_BYTES: usize = 102_400;
const MAX_SIGNERS: usize = 16;
const MAX_ATTRIBUTES: usize = 16;
const MAX_SCRIPT_BYTES: usize = u16::MAX as usize;
const MAX_WITNESS_SCRIPT_BYTES: usize = 1024;
const MAX_WITNESS_SUBITEMS: usize = 16;
const MAX_WITNESS_NESTING: usize = 3;

const SCOPE_CALLED_BY_ENTRY: u8 = 0x01;
const SCOPE_CUSTOM_CONTRACTS: u8 = 0x10;
const SCOPE_CUSTOM_GROUPS: u8 = 0x20;
const SCOPE_WITNESS_RULES: u8 = 0x40;
const SCOPE_GLOBAL: u8 = 0x80;
const VALID_SCOPES: u8 = SCOPE_CALLED_BY_ENTRY
    | SCOPE_CUSTOM_CONTRACTS
    | SCOPE_CUSTOM_GROUPS
    | SCOPE_WITNESS_RULES
    | SCOPE_GLOBAL;

pub fn parse_transaction(bytes: &[u8]) -> Result<ParsedTransaction, ExecutionError> {
    if bytes.is_empty() || bytes.len() > MAX_TRANSACTION_BYTES {
        return Err(ExecutionError::Invalid("canonical Neo transaction size"));
    }
    let mut reader = TransactionReader::new(bytes);
    if reader.read_u8()? != 0 {
        return Err(ExecutionError::Invalid("Neo transaction version"));
    }
    let nonce = reader.read_u32()?;
    let system_fee = reader.read_i64()?;
    let network_fee = reader.read_i64()?;
    if system_fee < 0 || network_fee < 0 || system_fee.checked_add(network_fee).is_none() {
        return Err(ExecutionError::Invalid("Neo transaction fees"));
    }
    let valid_until_block = reader.read_u32()?;
    let signer_count = reader.read_var_count(MAX_SIGNERS, "transaction signers")?;
    if signer_count == 0 {
        return Err(ExecutionError::Invalid("empty transaction signers"));
    }
    let mut signers = Vec::with_capacity(signer_count);
    for _ in 0..signer_count {
        let signer = parse_signer(&mut reader)?;
        if signers
            .iter()
            .any(|existing: &Signer| existing.account == signer.account)
        {
            return Err(ExecutionError::Invalid("duplicate transaction signer"));
        }
        signers.push(signer);
    }

    let has_oracle_response = parse_attributes(&mut reader, signer_count)?;
    let script = reader.read_var_bytes(MAX_SCRIPT_BYTES, "transaction script")?;
    if script.is_empty() {
        return Err(ExecutionError::Invalid("empty transaction script"));
    }
    let unsigned_end = reader.position();
    let hash = hash256(&bytes[..unsigned_end]);

    let witness_count = reader.read_var_count(MAX_SIGNERS, "transaction witnesses")?;
    if witness_count != signer_count {
        return Err(ExecutionError::Invalid("transaction witness count"));
    }
    let mut witnesses = Vec::with_capacity(witness_count);
    for _ in 0..witness_count {
        witnesses.push(TransactionWitness {
            invocation_script: reader
                .read_var_bytes(MAX_WITNESS_SCRIPT_BYTES, "invocation script")?,
            verification_script: reader
                .read_var_bytes(MAX_WITNESS_SCRIPT_BYTES, "verification script")?,
        });
    }
    reader.ensure_end()?;

    Ok(ParsedTransaction {
        hash,
        nonce,
        system_fee,
        network_fee,
        valid_until_block,
        has_oracle_response,
        signers,
        script,
        witnesses,
    })
}

fn parse_signer(reader: &mut TransactionReader<'_>) -> Result<Signer, ExecutionError> {
    let account = reader.read_fixed::<20>()?;
    let scopes = reader.read_u8()?;
    if scopes & !VALID_SCOPES != 0 || (scopes & SCOPE_GLOBAL != 0 && scopes != SCOPE_GLOBAL) {
        return Err(ExecutionError::Invalid("transaction witness scopes"));
    }
    let allowed_contracts = if scopes & SCOPE_CUSTOM_CONTRACTS != 0 {
        let count = reader.read_var_count(MAX_WITNESS_SUBITEMS, "allowed contracts")?;
        let mut contracts = Vec::with_capacity(count);
        for _ in 0..count {
            let contract = reader.read_fixed::<20>()?;
            if contracts.contains(&contract) {
                return Err(ExecutionError::Invalid("duplicate allowed contract"));
            }
            contracts.push(contract);
        }
        contracts
    } else {
        Vec::new()
    };
    let allowed_groups = if scopes & SCOPE_CUSTOM_GROUPS != 0 {
        let count = reader.read_var_count(MAX_WITNESS_SUBITEMS, "allowed groups")?;
        let mut groups = Vec::with_capacity(count);
        for _ in 0..count {
            let group = reader.read_fixed::<33>()?;
            validate_compressed_point(&group)?;
            if groups.contains(&group) {
                return Err(ExecutionError::Invalid("duplicate allowed group"));
            }
            groups.push(group);
        }
        groups
    } else {
        Vec::new()
    };
    let rules = if scopes & SCOPE_WITNESS_RULES != 0 {
        let count = reader.read_var_count(MAX_WITNESS_SUBITEMS, "witness rules")?;
        let mut rules = Vec::with_capacity(count);
        for _ in 0..count {
            rules.push(parse_witness_rule(reader)?);
        }
        rules
    } else {
        Vec::new()
    };
    Ok(Signer {
        account,
        scopes,
        allowed_contracts,
        allowed_groups,
        rules,
    })
}

fn parse_witness_rule(reader: &mut TransactionReader<'_>) -> Result<WitnessRule, ExecutionError> {
    let action = match reader.read_u8()? {
        0 => WitnessRuleAction::Deny,
        1 => WitnessRuleAction::Allow,
        _ => return Err(ExecutionError::Invalid("witness rule action")),
    };
    let condition = parse_witness_condition(reader, 0)?;
    Ok(WitnessRule { action, condition })
}

fn parse_witness_condition(
    reader: &mut TransactionReader<'_>,
    depth: usize,
) -> Result<WitnessCondition, ExecutionError> {
    if depth > MAX_WITNESS_NESTING {
        return Err(ExecutionError::Invalid("witness condition nesting"));
    }
    match reader.read_u8()? {
        0x00 => match reader.read_u8()? {
            0 => Ok(WitnessCondition::Boolean(false)),
            1 => Ok(WitnessCondition::Boolean(true)),
            _ => Err(ExecutionError::Invalid("witness boolean condition")),
        },
        0x01 => Ok(WitnessCondition::Not(Box::new(parse_witness_condition(
            reader,
            depth + 1,
        )?))),
        0x02 | 0x03 => {
            let condition_type = reader.bytes[reader.position() - 1];
            let count =
                reader.read_var_count(MAX_WITNESS_SUBITEMS, "witness condition children")?;
            if count == 0 {
                return Err(ExecutionError::Invalid("empty witness condition children"));
            }
            let mut children = Vec::with_capacity(count);
            for _ in 0..count {
                children.push(parse_witness_condition(reader, depth + 1)?);
            }
            if condition_type == 0x02 {
                Ok(WitnessCondition::And(children))
            } else {
                Ok(WitnessCondition::Or(children))
            }
        }
        0x18 => Ok(WitnessCondition::ScriptHash(reader.read_fixed::<20>()?)),
        0x19 => {
            let point = reader.read_fixed::<33>()?;
            validate_compressed_point(&point)?;
            Ok(WitnessCondition::Group(point))
        }
        0x20 => Ok(WitnessCondition::CalledByEntry),
        0x28 => Ok(WitnessCondition::CalledByContract(
            reader.read_fixed::<20>()?,
        )),
        0x29 => {
            let point = reader.read_fixed::<33>()?;
            validate_compressed_point(&point)?;
            Ok(WitnessCondition::CalledByGroup(point))
        }
        _ => Err(ExecutionError::Invalid("witness condition type")),
    }
}

fn parse_attributes(
    reader: &mut TransactionReader<'_>,
    signer_count: usize,
) -> Result<bool, ExecutionError> {
    let maximum = MAX_ATTRIBUTES.saturating_sub(signer_count);
    let count = reader.read_var_count(maximum, "transaction attributes")?;
    let mut seen_mask = 0u8;
    let mut has_oracle_response = false;
    for _ in 0..count {
        let attribute_type = reader.read_u8()?;
        let unique_bit = match attribute_type {
            0x01 => Some(1 << 0),
            0x11 => {
                has_oracle_response = true;
                let _id = reader.read_u64()?;
                let response_code = reader.read_u8()?;
                if !matches!(
                    response_code,
                    0x00 | 0x10 | 0x12 | 0x14 | 0x16 | 0x18 | 0x1a | 0x1c | 0x1f | 0xff
                ) {
                    return Err(ExecutionError::Invalid("oracle response code"));
                }
                let result = reader.read_var_bytes(MAX_SCRIPT_BYTES, "oracle response result")?;
                if response_code != 0 && !result.is_empty() {
                    return Err(ExecutionError::Invalid("oracle response result"));
                }
                Some(1 << 1)
            }
            0x20 => {
                let _height = reader.read_u32()?;
                Some(1 << 2)
            }
            0x21 => {
                let _hash = reader.read_fixed::<32>()?;
                None
            }
            0x22 => {
                let _keys = reader.read_u8()?;
                Some(1 << 3)
            }
            _ => return Err(ExecutionError::Invalid("transaction attribute type")),
        };
        if let Some(unique_bit) = unique_bit {
            if seen_mask & unique_bit != 0 {
                return Err(ExecutionError::Invalid("duplicate transaction attribute"));
            }
            seen_mask |= unique_bit;
        }
    }
    Ok(has_oracle_response)
}

fn validate_compressed_point(point: &[u8; 33]) -> Result<(), ExecutionError> {
    if p256::PublicKey::from_sec1_bytes(point).is_err() {
        return Err(ExecutionError::Invalid("compressed secp256r1 point"));
    }
    Ok(())
}

struct TransactionReader<'a> {
    bytes: &'a [u8],
    position: usize,
}

impl<'a> TransactionReader<'a> {
    fn new(bytes: &'a [u8]) -> Self {
        Self { bytes, position: 0 }
    }

    fn position(&self) -> usize {
        self.position
    }

    fn read_u8(&mut self) -> Result<u8, ExecutionError> {
        let value = *self
            .bytes
            .get(self.position)
            .ok_or(ExecutionError::Truncated)?;
        self.position += 1;
        Ok(value)
    }

    fn read_u16(&mut self) -> Result<u16, ExecutionError> {
        Ok(u16::from_le_bytes(self.read_fixed::<2>()?))
    }

    fn read_u32(&mut self) -> Result<u32, ExecutionError> {
        Ok(u32::from_le_bytes(self.read_fixed::<4>()?))
    }

    fn read_u64(&mut self) -> Result<u64, ExecutionError> {
        Ok(u64::from_le_bytes(self.read_fixed::<8>()?))
    }

    fn read_i64(&mut self) -> Result<i64, ExecutionError> {
        Ok(i64::from_le_bytes(self.read_fixed::<8>()?))
    }

    fn read_fixed<const N: usize>(&mut self) -> Result<[u8; N], ExecutionError> {
        let end = self
            .position
            .checked_add(N)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.position..end)
            .ok_or(ExecutionError::Truncated)?
            .try_into()
            .map_err(|_| ExecutionError::Truncated)?;
        self.position = end;
        Ok(value)
    }

    fn read_var_count(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<usize, ExecutionError> {
        let value = self.read_var_int()?;
        let value = usize::try_from(value).map_err(|_| ExecutionError::Oversized(field))?;
        if value > maximum {
            return Err(ExecutionError::Oversized(field));
        }
        Ok(value)
    }

    fn read_var_bytes(
        &mut self,
        maximum: usize,
        field: &'static str,
    ) -> Result<Vec<u8>, ExecutionError> {
        let length = self.read_var_count(maximum, field)?;
        let end = self
            .position
            .checked_add(length)
            .ok_or(ExecutionError::Truncated)?;
        let value = self
            .bytes
            .get(self.position..end)
            .ok_or(ExecutionError::Truncated)?
            .to_vec();
        self.position = end;
        Ok(value)
    }

    fn read_var_int(&mut self) -> Result<u64, ExecutionError> {
        match self.read_u8()? {
            value @ 0x00..=0xfc => Ok(u64::from(value)),
            0xfd => {
                let value = u64::from(self.read_u16()?);
                if value < 0xfd {
                    Err(ExecutionError::Invalid("non-canonical Neo varint"))
                } else {
                    Ok(value)
                }
            }
            0xfe => {
                let value = u64::from(self.read_u32()?);
                if value <= u64::from(u16::MAX) {
                    Err(ExecutionError::Invalid("non-canonical Neo varint"))
                } else {
                    Ok(value)
                }
            }
            0xff => {
                let value = self.read_u64()?;
                if value <= u64::from(u32::MAX) {
                    Err(ExecutionError::Invalid("non-canonical Neo varint"))
                } else {
                    Ok(value)
                }
            }
        }
    }

    fn ensure_end(&self) -> Result<(), ExecutionError> {
        if self.position == self.bytes.len() {
            Ok(())
        } else {
            Err(ExecutionError::Invalid("trailing transaction bytes"))
        }
    }
}
