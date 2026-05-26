use std::time::Duration;

use crate::event_source::LockedEvent;

use super::{EthRpcError, EthRpcEventSource, DEFAULT_BLOCK_CHUNK, DEFAULT_REQUEST_TIMEOUT};

/// Configuration for an [`EthRpcEventSource`].
pub struct EthRpcEventSourceBuilder {
    pub(super) rpc_url: String,
    pub(super) router_address: [u8; 20],
    pub(super) chunk_size: u64,
    pub(super) request_timeout: Duration,
    /// Block-finality buffer: the source never polls events from blocks
    /// less than `min_confirmations` deep from the chain head. Chosen
    /// per-chain by the operator based on the target chain's reorg
    /// characteristics. Default 0 (no buffer - caller's job to set).
    pub(super) min_confirmations: u64,
    /// Keep an in-memory FIFO of decoded events; pop one per
    /// `next_event` call.
    pub(super) queue: std::collections::VecDeque<LockedEvent>,
    /// Where the next chunk poll resumes from. Updated as we drain
    /// blocks past `start_block`.
    pub(super) next_block_to_poll: u64,
}

impl EthRpcEventSourceBuilder {
    pub fn new(rpc_url: impl Into<String>, router_address: [u8; 20]) -> Self {
        Self {
            rpc_url: rpc_url.into(),
            router_address,
            chunk_size: DEFAULT_BLOCK_CHUNK,
            request_timeout: DEFAULT_REQUEST_TIMEOUT,
            min_confirmations: 0,
            queue: std::collections::VecDeque::new(),
            next_block_to_poll: 0,
        }
    }

    /// Override the historical-query chunk size (default 5_000 blocks).
    /// Free-tier providers may cap at 10k; larger means fewer round-trips
    /// when catching up after a long downtime.
    pub fn chunk_size(mut self, n: u64) -> Self {
        self.chunk_size = n;
        self
    }

    /// Override the per-request HTTP timeout (default 30 s).
    pub fn request_timeout(mut self, t: Duration) -> Self {
        self.request_timeout = t;
        self
    }

    /// Set the minimum confirmations buffer. The source will not emit
    /// events from blocks less than `n` deep from the chain head, so a
    /// reorg shallower than `n` blocks cannot produce a phantom mint.
    ///
    /// Per-chain guidance (see [`crate::chains`]):
    /// - Ethereum mainnet: 12 (~99.9% finality), 32 for finalized
    /// - BSC: 15
    /// - Polygon PoS: 256 (heuristic finality; longer for hard finality)
    /// - Arbitrum / Optimism / Base: 0 - finality follows L1 batch posts;
    ///   operators wait for L1 confirmation via a separate signal.
    /// - Avalanche C-Chain: 1 (snowman++ has near-instant finality)
    /// - Tron: 19 (super-representative-confirmed)
    /// - L2 testnets / devnets: 0 (don't slow down dev cycles)
    ///
    /// Default 0 - the operator MUST opt in for production deployments.
    pub fn min_confirmations(mut self, n: u64) -> Self {
        self.min_confirmations = n;
        self
    }

    pub fn build(self) -> Result<EthRpcEventSource, EthRpcError> {
        let client = reqwest::blocking::Client::builder()
            .timeout(self.request_timeout)
            .build()
            .map_err(|e| EthRpcError::Http(format!("client build: {e}")))?;
        Ok(EthRpcEventSource {
            client,
            rpc_url: self.rpc_url,
            router_address: self.router_address,
            chunk_size: self.chunk_size,
            min_confirmations: self.min_confirmations,
            queue: self.queue,
            next_block_to_poll: self.next_block_to_poll,
            next_request_id: std::sync::atomic::AtomicU64::new(1),
        })
    }
}
