//! Off-chain watcher daemon for the Neo Elastic Network's Eth ↔ Neo
//! external bridge.
//!
//! This crate is the **messaging + signing core** that future iterations
//! plug into a live daemon loop. The split keeps the
//! cryptographically-load-bearing code (canonical wire format encoding,
//! ECDSA signing) testable in isolation, with byte-for-byte parity tests
//! against the C# implementation in `src/Neo.L2.Bridge/External/`.
//!
//! What lives here today:
//!
//! - [`messaging`] — canonical `ExternalCrossChainMessage` encoder. Mirrors
//!   `Neo.L2.Messaging.ExternalMessageHasher` byte-for-byte.
//! - [`proof`] — `MpcCommitteePayload` encoders for both Neo (the on-chain
//!   `proofBytes` argument of `NeoHub.MpcCommitteeVerifier.VerifyInboundMessage`)
//!   and Eth (the indexed-signer wire format the
//!   `NeoExternalBridgeRouter` expects). Same signatures, different
//!   serializations — the watcher generates BOTH from a single signing
//!   round.
//! - [`signer`] — [`Signer`] trait + a file-based dev signer that loads a
//!   secp256k1 private key from a 32-byte file. Production deployments
//!   provide HSM-backed implementations.
//!
//! What's deferred to subsequent iterations:
//!
//! - Live Eth event subscription (ethers-rs WebSocket → `Locked` events).
//! - Live Neo RPC submission (JSON-RPC client posting to
//!   `NeoHub.ExternalBridgeEscrow.Receive`).
//! - RocksDB-backed last-processed-block journal (restart safety).
//! - Daemon main loop wiring all of the above.

pub mod core;
pub mod event_source;
pub mod journal;
pub mod messaging;
pub mod proof;
pub mod signer;
pub mod submitter;

/// Live JSON-RPC adapters connecting the trait abstractions to real
/// chains. Gated behind the `live-rpc` feature so default `cargo test`
/// keeps a lean dep tree (the messaging + signing + orchestration core
/// has no networking deps). Operators run
/// `cargo build --features live-rpc` when building the runnable daemon.
#[cfg(feature = "live-rpc")]
pub mod live;

pub use core::{CoreError, WatcherCore};
pub use event_source::{EventSource, EventSourceError, LockedEvent, MockEventSource};
pub use journal::{InMemoryJournal, Journal, JournalError};
pub use messaging::{
    canonical_message_bytes, message_hash, BuildError, ExternalBridgeDirection,
    ExternalMessageType, ExternalCrossChainMessage,
};
pub use proof::{Curve, EthProofBytes, NeoProofBytes, ProofBuildError};
pub use signer::{FileSigner, Signer, SignerError, SignerOutput};
pub use submitter::{InboundSubmission, MockSubmitter, NeoSubmitter, SubmitterError};
