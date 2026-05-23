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

mod locked_event;
mod mock_event_source;

use thiserror::Error;

pub use locked_event::LockedEvent;
pub use mock_event_source::MockEventSource;

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
