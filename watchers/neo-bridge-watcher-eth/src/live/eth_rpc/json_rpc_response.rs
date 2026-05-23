use serde::Deserialize;

use super::json_rpc_error::JsonRpcError;

#[derive(Deserialize)]
pub(super) struct JsonRpcResponse<T> {
    #[allow(dead_code)]
    pub(super) jsonrpc: Option<String>,
    #[allow(dead_code)]
    pub(super) id: Option<u64>,
    pub(super) result: Option<T>,
    pub(super) error: Option<JsonRpcError>,
}
