use serde::Deserialize;

use super::json_rpc_error::JsonRpcError;

#[derive(Deserialize)]
pub(super) struct JsonRpcResponse<T> {
    #[allow(dead_code)]
    pub(super) jsonrpc: Option<String>,
    pub(super) id: Option<u64>, // validated in send_rpc()
    pub(super) result: Option<T>,
    pub(super) error: Option<JsonRpcError>,
}
