use alloc::{
    string::{String, ToString},
    vec::Vec,
};

use serde_json::Value;

use crate::types::{
    ContractManifest, ContractParameterType, ExecutionError, ManifestEvent, ManifestMethod,
    ManifestPermission, PermissionContract, PermissionMethods,
};

const MAX_MANIFEST_BYTES: usize = u16::MAX as usize;
const MAX_ABI_ITEMS: usize = 1024;
const MAX_PARAMETERS: usize = 252;

pub(crate) fn parse_contract_manifest(
    bytes: &[u8],
    script_len: usize,
) -> Result<ContractManifest, ExecutionError> {
    if bytes.is_empty() {
        return Err(ExecutionError::Invalid("empty contract manifest"));
    }
    if bytes.len() > MAX_MANIFEST_BYTES {
        return Err(ExecutionError::Oversized("contract manifest"));
    }
    let value: Value = serde_json::from_slice(bytes)
        .map_err(|_| ExecutionError::Invalid("contract manifest JSON"))?;
    let object = value
        .as_object()
        .ok_or(ExecutionError::Invalid("contract manifest object"))?;
    let name = required_string(object.get("name"), "contract manifest name")?;
    if name.is_empty() || name.len() > 252 {
        return Err(ExecutionError::Invalid("contract manifest name"));
    }

    let groups = parse_groups(object.get("groups"))?;
    let abi = object
        .get("abi")
        .and_then(Value::as_object)
        .ok_or(ExecutionError::Invalid("contract manifest ABI"))?;
    let methods = parse_methods(abi.get("methods"), script_len)?;
    let events = parse_events(abi.get("events"))?;
    let permissions = parse_permissions(object.get("permissions"))?;

    Ok(ContractManifest {
        name,
        groups,
        methods,
        events,
        permissions,
    })
}

fn parse_groups(value: Option<&Value>) -> Result<Vec<[u8; 33]>, ExecutionError> {
    let groups = value
        .and_then(Value::as_array)
        .ok_or(ExecutionError::Invalid("contract manifest groups"))?;
    if groups.len() > MAX_ABI_ITEMS {
        return Err(ExecutionError::Oversized("contract manifest groups"));
    }
    let mut parsed = Vec::with_capacity(groups.len());
    for group in groups {
        let pubkey = group
            .as_object()
            .and_then(|object| object.get("pubkey"))
            .and_then(Value::as_str)
            .ok_or(ExecutionError::Invalid("contract manifest group"))?;
        let point = parse_fixed_hex::<33>(pubkey, false)?;
        validate_compressed_point(&point)?;
        if parsed.contains(&point) {
            return Err(ExecutionError::Invalid("duplicate contract manifest group"));
        }
        parsed.push(point);
    }
    Ok(parsed)
}

fn parse_methods(
    value: Option<&Value>,
    script_len: usize,
) -> Result<Vec<ManifestMethod>, ExecutionError> {
    let methods = value
        .and_then(Value::as_array)
        .ok_or(ExecutionError::Invalid("contract ABI methods"))?;
    if methods.is_empty() || methods.len() > MAX_ABI_ITEMS {
        return Err(ExecutionError::Invalid("contract ABI methods"));
    }
    let mut parsed = Vec::with_capacity(methods.len());
    for method in methods {
        let object = method
            .as_object()
            .ok_or(ExecutionError::Invalid("contract ABI method"))?;
        let name = required_string(object.get("name"), "contract ABI method name")?;
        if name.is_empty() || name.len() > 252 {
            return Err(ExecutionError::Invalid("contract ABI method name"));
        }
        let parameters = parse_parameters(object.get("parameters"))?;
        let return_type = parse_parameter_type(required_str(
            object.get("returntype"),
            "contract ABI return type",
        )?)?;
        let offset = object
            .get("offset")
            .and_then(Value::as_i64)
            .and_then(|offset| usize::try_from(offset).ok())
            .ok_or(ExecutionError::Invalid("contract ABI method offset"))?;
        if offset >= script_len {
            return Err(ExecutionError::Invalid("contract ABI method offset"));
        }
        let safe = object
            .get("safe")
            .and_then(Value::as_bool)
            .ok_or(ExecutionError::Invalid("contract ABI safe flag"))?;
        if parsed.iter().any(|existing: &ManifestMethod| {
            existing.name == name && existing.parameter_types.len() == parameters.len()
        }) {
            return Err(ExecutionError::Invalid("duplicate contract ABI method"));
        }
        parsed.push(ManifestMethod {
            name,
            parameter_types: parameters,
            return_type,
            offset,
            safe,
        });
    }
    Ok(parsed)
}

fn parse_events(value: Option<&Value>) -> Result<Vec<ManifestEvent>, ExecutionError> {
    let events = value
        .and_then(Value::as_array)
        .ok_or(ExecutionError::Invalid("contract ABI events"))?;
    if events.len() > MAX_ABI_ITEMS {
        return Err(ExecutionError::Oversized("contract ABI events"));
    }
    let mut parsed = Vec::with_capacity(events.len());
    for event in events {
        let object = event
            .as_object()
            .ok_or(ExecutionError::Invalid("contract ABI event"))?;
        let name = required_string(object.get("name"), "contract ABI event name")?;
        if name.is_empty() || name.len() > 32 {
            return Err(ExecutionError::Invalid("contract ABI event name"));
        }
        let parameters = parse_parameters(object.get("parameters"))?;
        if parsed
            .iter()
            .any(|existing: &ManifestEvent| existing.name == name)
        {
            return Err(ExecutionError::Invalid("duplicate contract ABI event"));
        }
        parsed.push(ManifestEvent {
            name,
            parameter_types: parameters,
        });
    }
    Ok(parsed)
}

fn parse_parameters(value: Option<&Value>) -> Result<Vec<ContractParameterType>, ExecutionError> {
    let parameters = value
        .and_then(Value::as_array)
        .ok_or(ExecutionError::Invalid("contract ABI parameters"))?;
    if parameters.len() > MAX_PARAMETERS {
        return Err(ExecutionError::Oversized("contract ABI parameters"));
    }
    let mut parsed = Vec::with_capacity(parameters.len());
    for parameter in parameters {
        let parameter_type = parameter
            .as_object()
            .and_then(|object| object.get("type"))
            .and_then(Value::as_str)
            .ok_or(ExecutionError::Invalid("contract ABI parameter"))?;
        let parameter_type = parse_parameter_type(parameter_type)?;
        if parameter_type == ContractParameterType::Void {
            return Err(ExecutionError::Invalid("void contract ABI parameter"));
        }
        parsed.push(parameter_type);
    }
    Ok(parsed)
}

fn parse_permissions(value: Option<&Value>) -> Result<Vec<ManifestPermission>, ExecutionError> {
    let permissions = value
        .and_then(Value::as_array)
        .ok_or(ExecutionError::Invalid("contract manifest permissions"))?;
    if permissions.len() > MAX_ABI_ITEMS {
        return Err(ExecutionError::Oversized("contract manifest permissions"));
    }
    let mut parsed = Vec::with_capacity(permissions.len());
    for permission in permissions {
        let object = permission
            .as_object()
            .ok_or(ExecutionError::Invalid("contract manifest permission"))?;
        let contract_text = required_str(
            object.get("contract"),
            "contract manifest permission contract",
        )?;
        let contract = if contract_text == "*" {
            PermissionContract::Wildcard
        } else {
            let text = contract_text.strip_prefix("0x").unwrap_or(contract_text);
            match text.len() {
                40 => PermissionContract::Hash(parse_fixed_hex::<20>(text, true)?),
                66 => {
                    let point = parse_fixed_hex::<33>(text, false)?;
                    validate_compressed_point(&point)?;
                    PermissionContract::Group(point)
                }
                _ => return Err(ExecutionError::Invalid("contract permission descriptor")),
            }
        };
        let methods_value = object
            .get("methods")
            .ok_or(ExecutionError::Invalid("contract permission methods"))?;
        let methods = if methods_value.as_str() == Some("*") {
            PermissionMethods::Wildcard
        } else {
            let names = methods_value
                .as_array()
                .ok_or(ExecutionError::Invalid("contract permission methods"))?;
            if names.len() > MAX_ABI_ITEMS {
                return Err(ExecutionError::Oversized("contract permission methods"));
            }
            let mut parsed_names = Vec::with_capacity(names.len());
            for name in names {
                let name = name
                    .as_str()
                    .ok_or(ExecutionError::Invalid("contract permission method name"))?;
                if name.is_empty() || name.len() > 252 {
                    return Err(ExecutionError::Invalid("contract permission method name"));
                }
                if parsed_names.iter().any(|existing| existing == name) {
                    return Err(ExecutionError::Invalid(
                        "duplicate contract permission method",
                    ));
                }
                parsed_names.push(name.to_string());
            }
            PermissionMethods::Named(parsed_names)
        };
        parsed.push(ManifestPermission { contract, methods });
    }
    Ok(parsed)
}

fn parse_parameter_type(value: &str) -> Result<ContractParameterType, ExecutionError> {
    match value {
        "Any" => Ok(ContractParameterType::Any),
        "Boolean" => Ok(ContractParameterType::Boolean),
        "Integer" => Ok(ContractParameterType::Integer),
        "ByteArray" => Ok(ContractParameterType::ByteArray),
        "String" => Ok(ContractParameterType::String),
        "Hash160" => Ok(ContractParameterType::Hash160),
        "Hash256" => Ok(ContractParameterType::Hash256),
        "PublicKey" => Ok(ContractParameterType::PublicKey),
        "Signature" => Ok(ContractParameterType::Signature),
        "Array" => Ok(ContractParameterType::Array),
        "Map" => Ok(ContractParameterType::Map),
        "InteropInterface" => Ok(ContractParameterType::InteropInterface),
        "Void" => Ok(ContractParameterType::Void),
        _ => Err(ExecutionError::Invalid("contract parameter type")),
    }
}

fn required_string(value: Option<&Value>, field: &'static str) -> Result<String, ExecutionError> {
    Ok(required_str(value, field)?.to_string())
}

fn required_str<'a>(
    value: Option<&'a Value>,
    field: &'static str,
) -> Result<&'a str, ExecutionError> {
    value
        .and_then(Value::as_str)
        .ok_or(ExecutionError::Invalid(field))
}

fn parse_fixed_hex<const N: usize>(value: &str, reverse: bool) -> Result<[u8; N], ExecutionError> {
    if value.len() != N * 2 {
        return Err(ExecutionError::Invalid("fixed-width hex"));
    }
    let mut output = [0u8; N];
    for (index, chunk) in value.as_bytes().chunks_exact(2).enumerate() {
        let high = decode_nibble(chunk[0])?;
        let low = decode_nibble(chunk[1])?;
        let destination = if reverse { N - index - 1 } else { index };
        output[destination] = (high << 4) | low;
    }
    Ok(output)
}

fn decode_nibble(value: u8) -> Result<u8, ExecutionError> {
    match value {
        b'0'..=b'9' => Ok(value - b'0'),
        b'a'..=b'f' => Ok(value - b'a' + 10),
        b'A'..=b'F' => Ok(value - b'A' + 10),
        _ => Err(ExecutionError::Invalid("hex digit")),
    }
}

fn validate_compressed_point(point: &[u8; 33]) -> Result<(), ExecutionError> {
    if p256::PublicKey::from_sec1_bytes(point).is_err() {
        return Err(ExecutionError::Invalid("compressed secp256r1 point"));
    }
    Ok(())
}
