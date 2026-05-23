use serde::Deserialize;

#[derive(Deserialize, Debug)]
pub(super) struct InvokeFunctionResult {
    /// VM exit state - `"HALT"` on success, `"FAULT"` on revert.
    pub(super) state: String,
    /// Pre-built NeoVM script as hex. Operators can submit this exact
    /// blob via `sendrawtransaction` once wrapped in a signed tx.
    pub(super) script: String,
    /// Optional exception message on FAULT.
    pub(super) exception: Option<String>,
}
