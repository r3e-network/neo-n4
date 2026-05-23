use serde::Deserialize;

#[derive(Deserialize, Debug, Clone)]
pub(super) struct RawLog {
    /// The router contract address. Already constrained at the
    /// request level by `eth_getLogs`'s `address` filter, but kept on
    /// the struct so a future hardening pass can cross-check it
    /// post-decode without changing the wire shape.
    #[allow(dead_code)]
    pub(super) address: String,
    /// Topic hashes; Solidity events put indexed args into topics[1..].
    /// topics[0] is the event signature hash.
    pub(super) topics: Vec<String>,
    /// ABI-encoded data for non-indexed args.
    pub(super) data: String,
    #[serde(rename = "blockNumber")]
    pub(super) block_number: String,
    #[serde(rename = "transactionHash")]
    pub(super) transaction_hash: String,
}
