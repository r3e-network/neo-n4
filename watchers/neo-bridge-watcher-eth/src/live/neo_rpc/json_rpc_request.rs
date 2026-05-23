use serde::Serialize;

#[derive(Serialize)]
pub(super) struct JsonRpcRequest<'a> {
    pub(super) jsonrpc: &'static str,
    pub(super) id: u64,
    pub(super) method: &'a str,
    pub(super) params: serde_json::Value,
}
