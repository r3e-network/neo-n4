use serde::Deserialize;

#[derive(Deserialize)]
pub(crate) struct PollConfig {
    #[serde(default = "default_poll_interval")]
    pub(crate) poll_interval_secs: u64,
    #[serde(default = "default_backoff_initial")]
    pub(crate) backoff_initial_secs: u64,
    #[serde(default = "default_backoff_max")]
    pub(crate) backoff_max_secs: u64,
    #[serde(default = "default_eth_chunk_size")]
    pub(crate) eth_chunk_size: u64,
    #[serde(default = "default_request_timeout")]
    pub(crate) request_timeout_secs: u64,
    /// Block-finality buffer. The watcher will not emit events from
    /// blocks less than this many confirmations deep — guards against
    /// short-reorg phantom mints. Per-chain guidance lives in
    /// `neo_bridge_watcher_eth::chains` doc + `min_confirmations`
    /// builder method docs. Default 0 (no buffer, testnet-only).
    /// Operators MUST set a chain-appropriate value for production.
    #[serde(default)]
    pub(crate) min_confirmations: u64,
    /// First-run cursor bootstrap. When the journal's cursor is
    /// strictly less than `start_block`, the daemon advances the
    /// cursor to `start_block` at startup — useful when deploying
    /// a watcher mid-stream against a chain that's been running for
    /// months (default behavior would re-scan from genesis, hammering
    /// the operator's RPC budget). Default 0 (start at genesis).
    ///
    /// Important: this advances the cursor MONOTONICALLY (only forward).
    /// It cannot rewind a journal that's already past `start_block`.
    /// To rewind, the operator manually clears the journal directory
    /// — opt-in destructive behavior, not a config knob.
    #[serde(default)]
    pub(crate) start_block: u64,
}

// Manual Default impl — `#[serde(default = "fn")]` only fires for fields
// that are present-but-unset INSIDE an existing [poll] table. When
// [poll] is omitted entirely, serde falls back to PollConfig::default()
// for the whole struct; #[derive(Default)] would zero every field
// (poll_interval=0 + backoff=0 = tight infinite spin). This impl
// matches the per-field defaults instead.
impl Default for PollConfig {
    fn default() -> Self {
        Self {
            poll_interval_secs: default_poll_interval(),
            backoff_initial_secs: default_backoff_initial(),
            backoff_max_secs: default_backoff_max(),
            eth_chunk_size: default_eth_chunk_size(),
            request_timeout_secs: default_request_timeout(),
            min_confirmations: 0,
            start_block: 0,
        }
    }
}

fn default_poll_interval() -> u64 {
    12
}
fn default_backoff_initial() -> u64 {
    5
}
fn default_backoff_max() -> u64 {
    300
}
fn default_eth_chunk_size() -> u64 {
    5_000
}
fn default_request_timeout() -> u64 {
    30
}
