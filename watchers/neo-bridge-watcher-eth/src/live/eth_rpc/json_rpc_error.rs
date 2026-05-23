use serde::Deserialize;

#[derive(Deserialize)]
pub(super) struct JsonRpcError {
    pub(super) code: i64,
    pub(super) message: String,
}
