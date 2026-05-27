//! Shared JSON-RPC request/response types and hex utilities used by both Eth and Neo RPC modules.

use serde::{Deserialize, Serialize};

/// JSON-RPC 2.0 request envelope.
#[derive(Serialize)]
pub struct JsonRpcRequest<'a> {
    pub jsonrpc: &'static str,
    pub id: u64,
    pub method: &'a str,
    pub params: serde_json::Value,
}

/// JSON-RPC 2.0 error object.
#[derive(Deserialize)]
pub struct JsonRpcError {
    pub code: i64,
    pub message: String,
}

/// JSON-RPC 2.0 response envelope.
#[derive(Deserialize)]
pub struct JsonRpcResponse<T> {
    pub id: Option<u64>,
    pub result: Option<T>,
    pub error: Option<JsonRpcError>,
}

/// Decode hex string with optional "0x" prefix into bytes.
pub fn decode_hex_bytes(s: &str) -> Result<Vec<u8>, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    hex::decode(s).map_err(|e| format!("hex: {e}"))
}

/// Decode hex string into exactly 32 bytes.
pub fn decode_hex32(s: &str) -> Result<[u8; 32], String> {
    let bytes = decode_hex_bytes(s)?;
    if bytes.len() != 32 {
        return Err(format!("expected 32 bytes, got {}", bytes.len()));
    }
    let mut out = [0u8; 32];
    out.copy_from_slice(&bytes);
    Ok(out)
}

/// Decode hex-encoded u64.
pub fn decode_hex_u64(s: &str) -> Result<u64, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    u64::from_str_radix(s, 16).map_err(|e| format!("u64 parse: {e}"))
}
