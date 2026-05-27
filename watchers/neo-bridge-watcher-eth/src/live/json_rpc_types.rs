//! Shared JSON-RPC request/response types used by both Eth and Neo RPC modules.

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
    #[allow(dead_code)]
    pub jsonrpc: Option<String>,
    pub id: Option<u64>,
    pub result: Option<T>,
    pub error: Option<JsonRpcError>,
}
