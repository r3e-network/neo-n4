use serde::Serialize;

#[derive(Serialize)]
pub(super) struct JsonRpcRequest {
    pub(super) jsonrpc: &'static str,
    pub(super) id: u64,
    pub(super) method: &'static str,
    pub(super) params: serde_json::Value,
}
