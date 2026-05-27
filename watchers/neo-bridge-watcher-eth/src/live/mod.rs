//! Live JSON-RPC adapters that connect the trait abstractions in
//! [`crate::event_source`] / [`crate::submitter`] / [`crate::journal`]
//! to real chains.
//!
//! Gated behind the `live-rpc` cargo feature so default `cargo test` keeps
//! a lean dep tree. Operators run `cargo build --features live-rpc` when
//! building the runnable daemon binary.
//!
//! What ships here today:
//!
//! - [`eth_rpc`] — `EthRpcEventSource` implementing
//!   [`crate::EventSource`] via Eth JSON-RPC `eth_getLogs` polling. Hand-
//!   rolled decoder for the `Locked` event (no `ethers-rs` / `alloy` —
//!   keeps the dep tree to `reqwest` + `serde_json` + `sha3`).
//!
//! Deferred to subsequent iterations:
//!
//! - `NeoSubmitter` HTTP impl (Neo JSON-RPC + signed-tx serialization).
//! - `Journal` RocksDB impl (replaces the in-memory journal for
//!   restart-safety).
//! - WebSocket `eth_subscribe`-based event source (poll is the v0;
//!   subscription is an optimization).

pub mod eth_rpc;
pub mod file_journal;
pub mod health;
pub(crate) mod json_rpc_types;
pub mod neo_rpc;

#[cfg(test)]
pub(crate) mod test_support;

pub use eth_rpc::{EthRpcError, EthRpcEventSource, EthRpcEventSourceBuilder};
pub use file_journal::FileJournal;
pub use health::{HealthServer, HealthState};
pub use neo_rpc::{NeoRpcError, NeoRpcSubmitter, NeoRpcSubmitterBuilder, SignAndSend};
