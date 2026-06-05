use serde::Deserialize;
use serde_json::Value;

#[derive(Deserialize, Debug)]
pub(super) struct InvokeFunctionResult {
    /// VM exit state - `"HALT"` on success, `"FAULT"` on revert.
    pub(super) state: String,
    /// Pre-built NeoVM script as hex. Operators can submit this exact
    /// blob via `sendrawtransaction` once wrapped in a signed tx.
    #[serde(default)]
    pub(super) script: String,
    /// Optional exception message on FAULT.
    pub(super) exception: Option<String>,
    /// VM evaluation stack. Safe read calls such as `isInboundConsumed`
    /// return their result here.
    #[serde(default)]
    pub(super) stack: Vec<InvokeStackItem>,
}

#[derive(Deserialize, Debug)]
pub(super) struct InvokeStackItem {
    #[serde(rename = "type")]
    item_type: String,
    value: Value,
}

impl InvokeFunctionResult {
    pub(super) fn first_bool(&self) -> Result<bool, String> {
        let item = self
            .stack
            .first()
            .ok_or_else(|| "invokefunction stack is empty".to_string())?;
        if item.item_type != "Boolean" {
            return Err(format!(
                "expected Boolean stack item, got {}",
                item.item_type
            ));
        }
        if let Some(value) = item.value.as_bool() {
            return Ok(value);
        }
        if let Some(value) = item.value.as_str() {
            return match value {
                "true" | "True" | "1" => Ok(true),
                "false" | "False" | "0" => Ok(false),
                other => Err(format!("invalid Boolean stack value {other:?}")),
            };
        }
        Err(format!("invalid Boolean stack value {}", item.value))
    }
}
