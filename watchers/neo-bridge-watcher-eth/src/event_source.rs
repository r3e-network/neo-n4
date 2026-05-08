//! Eth-side event source abstraction. The `Locked` event emitted by
//! `NeoExternalBridgeRouter.lockETHAndSend` / `lockERC20AndSend` flows
//! through this trait so the daemon's orchestration code is agnostic to
//! whether events come from a live `ethers-rs` WebSocket subscription, a
//! REST poller, or an injected fake driving an integration test.
//!
//! Mapping from Solidity to Rust:
//!
//! ```solidity
//! event Locked(
//!     uint32 indexed externalChainId,
//!     uint32 indexed neoChainId,
//!     uint64 indexed nonce,
//!     address sender,
//!     bytes20 neoRecipient,
//!     address asset,
//!     uint256 amount,
//!     bytes payload,
//!     uint64 deadline
//! );
//! ```
//!
//! `block_number` and `source_tx_hash` are not in the event log itself
//! — the watcher pulls them from the surrounding tx context. They land
//! in `LockedEvent` so the daemon can journal cursor + populate the
//! canonical message's `sourceTxRef` field.

use thiserror::Error;

#[derive(Debug, Clone, PartialEq, Eq)]
pub struct LockedEvent {
    /// `externalChainId` from the event — should equal the watcher's
    /// configured chain id (e.g. 0xE0000001 for Eth mainnet). The watcher
    /// rejects mismatches as a sanity check before processing.
    pub external_chain_id: u32,

    /// Target Neo L2 chain id.
    pub neo_chain_id: u32,

    /// Outbound nonce assigned by the router.
    pub nonce: u64,

    /// Eth-side sender (msg.sender at lock time).
    pub sender: [u8; 20],

    /// 20-byte Neo recipient.
    pub neo_recipient: [u8; 20],

    /// ERC-20 asset hash; `[0u8; 20]` for native ETH.
    pub asset: [u8; 20],

    /// Locked amount, big-endian uint256 (Eth's wire format). The
    /// canonical asset-transfer payload encoder converts to minimal-LE
    /// before signing.
    pub amount: [u8; 32],

    /// Arbitrary payload (call data), copied verbatim into the canonical
    /// message's payload field for `MSG_TYPE_CALL` / `MSG_TYPE_ASSET_AND_CALL`.
    pub payload: Vec<u8>,

    /// Deadline; 0 = no deadline.
    pub deadline: u64,

    /// Eth tx hash that emitted this event. Goes into `sourceTxRef`.
    pub source_tx_hash: [u8; 32],

    /// Block height the event was emitted at — the daemon journals this
    /// so a restart resumes from the right cursor.
    pub block_number: u64,
}

#[derive(Debug, Error)]
pub enum EventSourceError {
    #[error("rpc error: {0}")]
    Rpc(String),
    #[error("subscription closed")]
    SubscriptionClosed,
}

/// Anything that produces `LockedEvent`s, in order. The daemon polls
/// `next_event` with whatever cursor the journal has — implementations
/// resume from there.
///
/// The trait is intentionally synchronous + iterator-shaped to keep
/// integration tests (with `MockEventSource`) trivially driveable. A
/// real `ethers-rs` impl wraps an async subscription in a background
/// task and pushes events into a channel that this trait drains.
pub trait EventSource {
    /// Pull the next event ≥ the given start_block (inclusive). Returns
    /// `Ok(None)` if no more events are currently available — the
    /// caller is expected to backoff + retry.
    fn next_event(&mut self, start_block: u64) -> Result<Option<LockedEvent>, EventSourceError>;
}

/// Test fixture: in-memory queue of events, returned in insertion order
/// (or filtered by start_block).
pub struct MockEventSource {
    events: Vec<LockedEvent>,
}

impl MockEventSource {
    pub fn new() -> Self {
        Self { events: Vec::new() }
    }

    pub fn push(&mut self, event: LockedEvent) {
        self.events.push(event);
    }

    pub fn pending(&self) -> usize {
        self.events.len()
    }
}

impl Default for MockEventSource {
    fn default() -> Self {
        Self::new()
    }
}

impl EventSource for MockEventSource {
    fn next_event(&mut self, start_block: u64) -> Result<Option<LockedEvent>, EventSourceError> {
        // Find the first event ≥ start_block; pop it (FIFO over qualifying events).
        if let Some(idx) = self.events.iter().position(|e| e.block_number >= start_block) {
            Ok(Some(self.events.remove(idx)))
        } else {
            Ok(None)
        }
    }
}
