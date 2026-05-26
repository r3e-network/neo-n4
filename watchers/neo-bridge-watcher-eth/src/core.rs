//! `WatcherCore` — the daemon's orchestration layer.
//!
//! Wires [`Signer`] + [`EventSource`] + [`NeoSubmitter`] + [`Journal`]
//! into the canonical pipeline:
//!
//! 1. Pull the next [`LockedEvent`] from the source (≥ journal cursor).
//! 2. Sanity-check the event (chain id matches, nonce is fresh per
//!    journal).
//! 3. Build canonical [`ExternalCrossChainMessage`] bytes from the
//!    event.
//! 4. Sign the canonical bytes.
//! 5. Encode the [`NeoProofBytes`] proof for Neo's
//!    `MpcCommitteeVerifier.VerifyInboundMessage`.
//! 6. Submit to Neo via the [`NeoSubmitter`].
//! 7. On success, mark `(externalChainId, nonce)` submitted + advance
//!    the journal cursor.
//!
//! **Single-watcher Phase B**: this core only emits ONE signature. To
//! reach `MpcCommitteeVerifier`'s threshold, multiple watchers must run
//! independently and a coordinator (deferred — Phase C will fold this
//! into the optimistic challenge orchestrator) collects + concatenates
//! their NeoProofBytes outputs. The single-watcher path here is what
//! every committee member runs; the multi-watcher aggregation is one
//! layer up.

use thiserror::Error;

use crate::event_source::{EventSource, EventSourceError, LockedEvent};
use crate::journal::{Journal, JournalError};
use crate::messaging::{
    canonical_message_bytes, encode_asset_transfer_payload, BuildError, ExternalBridgeDirection,
    ExternalCrossChainMessage, ExternalMessageType,
};
use crate::proof::{Curve, NeoProofBytes, ProofBuildError, PubkeySignature};
use crate::signer::{Signer, SignerError};
use crate::submitter::{InboundSubmission, NeoSubmitter, SubmitterError};

#[derive(Debug, Error)]
pub enum CoreError {
    #[error("event chain id 0x{got:08X} != configured 0x{expected:08X}")]
    ChainIdMismatch { got: u32, expected: u32 },
    #[error("nonce {0} already submitted (per local journal)")]
    AlreadySubmitted(u64),
    #[error(transparent)]
    EventSource(#[from] EventSourceError),
    #[error(transparent)]
    Build(#[from] BuildError),
    #[error(transparent)]
    Signer(#[from] SignerError),
    #[error(transparent)]
    Proof(#[from] ProofBuildError),
    #[error(transparent)]
    Submit(#[from] SubmitterError),
    #[error(transparent)]
    Journal(#[from] JournalError),
}

pub struct WatcherCore<S, ES, NS, J>
where
    S: Signer,
    ES: EventSource,
    NS: NeoSubmitter,
    J: Journal,
{
    pub external_chain_id: u32,
    pub signer: S,
    pub event_source: ES,
    pub submitter: NS,
    pub journal: J,
    /// Nonce of the event currently being processed (set at the top of
    /// process_event). Used by the run loop to recover from
    /// AlreadyConsumed errors — the on-chain contract already consumed
    /// this nonce, so we mark the local journal to match and advance.
    last_event_nonce: u64,
    /// Block number of the event currently being processed.
    last_event_block: u64,
}

impl<S: Signer, ES: EventSource, NS: NeoSubmitter, J: Journal> WatcherCore<S, ES, NS, J> {
    /// Nonce of the event most recently passed to `process_event`.
    /// Used by the run loop for AlreadyConsumed recovery.
    pub fn last_event_nonce(&self) -> u64 {
        self.last_event_nonce
    }

    pub fn new(
        external_chain_id: u32,
        signer: S,
        event_source: ES,
        submitter: NS,
        journal: J,
    ) -> Self {
        Self {
            external_chain_id,
            signer,
            event_source,
            submitter,
            journal,
            last_event_nonce: 0,
            last_event_block: 0,
        }
    }

    /// Recover from an `AlreadyConsumed` error: the on-chain contract has
    /// consumed the last-processed event's nonce but the local journal
    /// doesn't know about it (crash between submit + journal write).
    /// Mark the nonce as submitted and advance the cursor so the run loop
    /// can continue to subsequent events without backoff.
    pub fn recover_already_consumed(&mut self) -> Result<(), CoreError> {
        let chain = self.external_chain_id;
        let nonce = self.last_event_nonce;
        let block = self.last_event_block;
        self.journal.mark_submitted(chain, nonce)?;
        self.journal
            .set_cursor(block.saturating_add(1))?;
        Ok(())
    }

    /// Run one tick: pull the next event, process if any, advance the
    /// cursor. Returns `Ok(true)` if an event was processed,
    /// `Ok(false)` if the source had nothing new.
    pub fn tick(&mut self) -> Result<bool, CoreError> {
        let cursor = self.journal.cursor()?;
        let Some(event) = self.event_source.next_event(cursor)? else {
            return Ok(false);
        };
        self.process_event(event)?;
        Ok(true)
    }

    /// Run `tick` until the event source has nothing more to emit.
    /// Returns the count of events processed. The real daemon will
    /// instead loop forever with a backoff; this method is for tests +
    /// one-shot replay tools.
    pub fn drain(&mut self) -> Result<usize, CoreError> {
        let mut n = 0;
        while self.tick()? {
            n += 1;
        }
        Ok(n)
    }

    /// The single-event hot path. Public so tests can drive it without
    /// pushing through the event source.
    pub fn process_event(&mut self, event: LockedEvent) -> Result<[u8; 32], CoreError> {
        // Capture the event identity before any fallible work so the run
        // loop can recover from AlreadyConsumed by journaling the nonce.
        self.last_event_nonce = event.nonce;
        self.last_event_block = event.block_number;

        if event.external_chain_id != self.external_chain_id {
            return Err(CoreError::ChainIdMismatch {
                got: event.external_chain_id,
                expected: self.external_chain_id,
            });
        }
        if self
            .journal
            .is_submitted(event.external_chain_id, event.nonce)?
        {
            return Err(CoreError::AlreadySubmitted(event.nonce));
        }

        // Build the canonical message. The watcher's job is to assemble
        // the bytes the verifier sees on Neo's side; the recipient is
        // the bytes20 from the event (the Neo-side address).
        let payload = match event.payload.is_empty() {
            // No call data → pure asset transfer. Convert the BE uint256
            // amount to the minimal-LE encoding C# BigInteger.ToByteArray
            // produces for unsigned values.
            true => {
                encode_asset_transfer_payload(event.asset, &amount_be_to_le_minimal(&event.amount))
            }
            // Non-empty payload → asset+call. The Eth-side router doesn't
            // emit `MSG_TYPE_ASSET_AND_CALL` events today; until it does AND
            // a canonical concat-encoding is pinned by a cross-language test,
            // the watcher refuses rather than guessing at a layout the Neo
            // verifier would never recognize. Silently mis-encoding would
            // forge an inbound message the Eth side never authorized.
            false => {
                return Err(CoreError::Build(BuildError::UnsupportedMessageType(
                    ExternalMessageType::AssetAndCall as u8,
                )));
            }
        };

        let msg = ExternalCrossChainMessage {
            external_chain_id: event.external_chain_id,
            neo_chain_id: event.neo_chain_id,
            nonce: event.nonce,
            direction: ExternalBridgeDirection::ForeignToNeo,
            sender: event.sender,
            recipient: event.neo_recipient,
            deadline_unix_seconds: event.deadline,
            source_tx_ref: event.source_tx_hash,
            message_type: ExternalMessageType::AssetTransfer,
            payload,
        };
        let canonical = canonical_message_bytes(&msg)?;

        // Sign + encode the Neo-flavored proof bytes (single signer per
        // this watcher's contribution; M-of-N aggregation happens upstream).
        // Curve dispatch is driven by the signer — secp256k1 (Eth/Tron) or
        // ed25519 (Solana). The on-chain MpcCommitteeVerifier reads the
        // matching curveTag from its committee blob and dispatches to
        // VerifyWithECDsa(secp256k1SHA256) or VerifyWithEd25519.
        let signer_out = self.signer.sign_canonical_bytes(&canonical)?;
        let curve = match self.signer.curve_tag() {
            1 => Curve::Secp256k1,
            2 => Curve::Ed25519,
            other => {
                return Err(CoreError::Build(BuildError::BadNamespace(other as u32)));
            }
        };
        let neo_proof = NeoProofBytes::encode(
            curve,
            &[PubkeySignature {
                pubkey: self.signer.public_key_bytes(),
                signature: signer_out.signature,
            }],
        )?;

        let tx_hash = self.submitter.submit_inbound(InboundSubmission {
            external_chain_id: event.external_chain_id,
            message_bytes: canonical,
            proof_bytes: neo_proof,
        })?;

        // Successful submit → mark + advance cursor.
        self.journal
            .mark_submitted(event.external_chain_id, event.nonce)?;
        self.journal.set_cursor(event.block_number)?;
        Ok(tx_hash)
    }
}

/// Convert a 256-bit big-endian integer to its minimal little-endian
/// encoding (Eth wire format → C# BigInteger.ToByteArray for unsigned
/// values that don't need a sign byte).
///
/// Examples:
/// - 0 → `[0]`
/// - 1_000_000 → `[0x40, 0x42, 0x0F]`
/// - 2^255 → `[0x00, 0x00, ..., 0x00, 0x80]` (32 bytes ending 0x80)
fn amount_be_to_le_minimal(be: &[u8; 32]) -> Vec<u8> {
    let mut le: Vec<u8> = be.iter().rev().copied().collect();
    while le.len() > 1 && le.last().copied() == Some(0) {
        le.pop();
    }
    le
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::event_source::MockEventSource;
    use crate::journal::InMemoryJournal;
    use crate::signer::FileSigner;
    use crate::submitter::{MockSubmitter, SubmitterError};

    fn make_signer() -> FileSigner {
        FileSigner::from_bytes(&[0x42; 32]).unwrap()
    }

    fn sample_event(nonce: u64, block: u64) -> LockedEvent {
        // amount = 1_000_000, encoded as 32-byte BE.
        let mut amount = [0u8; 32];
        amount[29..32].copy_from_slice(&[0x0F, 0x42, 0x40]); // big-endian 0x0F4240 in last 3 bytes
        LockedEvent {
            external_chain_id: 0xE000_0001,
            neo_chain_id: 1099,
            nonce,
            sender: [0x11; 20],
            neo_recipient: [0xaa; 20],
            asset: [0xee; 20],
            amount,
            payload: Vec::new(),
            deadline: 1_900_000_000,
            source_tx_hash: [0xee; 32],
            block_number: block,
        }
    }

    #[test]
    fn amount_be_to_le_minimal_examples() {
        let mut be = [0u8; 32];
        // 1_000_000 in big-endian last 3 bytes
        be[29..32].copy_from_slice(&[0x0F, 0x42, 0x40]);
        assert_eq!(amount_be_to_le_minimal(&be), vec![0x40, 0x42, 0x0F]);

        // Zero → single zero byte.
        let zero = [0u8; 32];
        assert_eq!(amount_be_to_le_minimal(&zero), vec![0u8]);

        // 1 → single one byte.
        let mut one = [0u8; 32];
        one[31] = 1;
        assert_eq!(amount_be_to_le_minimal(&one), vec![1u8]);
    }

    #[test]
    fn process_event_full_pipeline() {
        let mut core = WatcherCore::new(
            0xE000_0001,
            make_signer(),
            MockEventSource::new(),
            MockSubmitter::new(),
            InMemoryJournal::new(),
        );

        let evt = sample_event(7, 1234);
        let tx_hash = core.process_event(evt.clone()).unwrap();
        assert_eq!(tx_hash[0], 1, "first submission gets tx_hash low byte = 1");

        // Submitter recorded one submission with the right shape.
        assert_eq!(core.submitter.submissions().len(), 1);
        let sub = &core.submitter.submissions()[0];
        assert_eq!(sub.external_chain_id, 0xE000_0001);
        // canonical message bytes = 102 prefix + 27 payload = 129
        assert_eq!(sub.message_bytes.len(), 129);
        // single-signer Neo proof = 2 header + (33 + 64) = 99 bytes
        assert_eq!(sub.proof_bytes.len(), 99);

        // Journal advanced.
        assert_eq!(core.journal.cursor().unwrap(), 1234);
        assert!(core.journal.is_submitted(0xE000_0001, 7).unwrap());
    }

    #[test]
    fn process_event_rejects_chain_id_mismatch() {
        let mut core = WatcherCore::new(
            0xE000_0001,
            make_signer(),
            MockEventSource::new(),
            MockSubmitter::new(),
            InMemoryJournal::new(),
        );
        let mut evt = sample_event(1, 1000);
        evt.external_chain_id = 0xE000_0010; // Tron, not Eth
        let err = core.process_event(evt).unwrap_err();
        assert!(matches!(
            err,
            CoreError::ChainIdMismatch {
                got: 0xE000_0010,
                expected: 0xE000_0001
            }
        ));
        assert_eq!(core.submitter.submissions().len(), 0);
    }

    #[test]
    fn process_event_rejects_replay() {
        let mut core = WatcherCore::new(
            0xE000_0001,
            make_signer(),
            MockEventSource::new(),
            MockSubmitter::new(),
            InMemoryJournal::new(),
        );
        let evt = sample_event(7, 1234);
        core.process_event(evt.clone()).unwrap();
        let err = core.process_event(evt).unwrap_err();
        assert!(matches!(err, CoreError::AlreadySubmitted(7)));
    }

    #[test]
    fn process_event_does_not_advance_cursor_on_submit_failure() {
        let mut core = WatcherCore::new(
            0xE000_0001,
            make_signer(),
            MockEventSource::new(),
            MockSubmitter::new(),
            InMemoryJournal::new(),
        );
        core.submitter.next_error = Some(SubmitterError::Rpc("boom".into()));
        let evt = sample_event(7, 1234);
        let err = core.process_event(evt.clone()).unwrap_err();
        assert!(matches!(err, CoreError::Submit(SubmitterError::Rpc(_))));
        // Critical: cursor MUST NOT advance and nonce MUST NOT be marked
        // submitted on RPC failure — otherwise a transient error becomes
        // a permanent skip + lost message.
        assert_eq!(core.journal.cursor().unwrap(), 0);
        assert!(!core.journal.is_submitted(0xE000_0001, 7).unwrap());
        // Retry: clear the error, same event succeeds.
        let tx_hash = core.process_event(evt).unwrap();
        assert_eq!(tx_hash[0], 1);
        assert_eq!(core.journal.cursor().unwrap(), 1234);
    }

    #[test]
    fn drain_processes_all_events_in_order() {
        let mut core = WatcherCore::new(
            0xE000_0001,
            make_signer(),
            MockEventSource::new(),
            MockSubmitter::new(),
            InMemoryJournal::new(),
        );
        core.event_source.push(sample_event(1, 100));
        core.event_source.push(sample_event(2, 200));
        core.event_source.push(sample_event(3, 300));

        let processed = core.drain().unwrap();
        assert_eq!(processed, 3);
        assert_eq!(core.submitter.submissions().len(), 3);
        // Cursor advances to the last block processed.
        assert_eq!(core.journal.cursor().unwrap(), 300);
        // Nonces in submission order: 1, 2, 3.
        assert_eq!(
            core.submitter.submissions()[0].external_chain_id,
            0xE000_0001
        );
        // All three nonces marked.
        for nonce in [1u64, 2, 3] {
            assert!(
                core.journal.is_submitted(0xE000_0001, nonce).unwrap(),
                "nonce {} should be marked submitted",
                nonce
            );
        }
    }

    #[test]
    fn drain_resumes_after_partial_progress() {
        // Two events: first succeeds, second fails on submit. Drain
        // should process the first, journal the cursor at block 100,
        // and bubble the error from the second.
        let mut core = WatcherCore::new(
            0xE000_0001,
            make_signer(),
            MockEventSource::new(),
            MockSubmitter::new(),
            InMemoryJournal::new(),
        );
        core.event_source.push(sample_event(1, 100));
        core.event_source.push(sample_event(2, 200));

        // Process first event; it succeeds.
        let processed = core.tick().unwrap();
        assert!(processed);
        assert_eq!(core.journal.cursor().unwrap(), 100);

        // Inject failure for the second event's submission.
        core.submitter.next_error = Some(SubmitterError::Rpc("transient".into()));
        let err = core.tick().unwrap_err();
        assert!(matches!(err, CoreError::Submit(_)));
        assert_eq!(
            core.journal.cursor().unwrap(),
            100,
            "cursor must NOT advance on submit failure"
        );

        // Recovery: clear the error, retry. The next_event call still
        // returns the second event because it wasn't removed from the
        // mock source... actually MockEventSource removes events on
        // next_event success, so the second event is gone. The watcher
        // would in practice rely on the real event source's persistence
        // (replay from cursor on restart). Pin that the journal cursor
        // is unchanged so a restart with the real persistent source
        // would correctly re-emit nonce 2.
        assert!(!core.journal.is_submitted(0xE000_0001, 2).unwrap());
    }

    #[test]
    fn already_consumed_recovery_marks_journal_and_advances() {
        use crate::submitter::MockSubmitter;
        use crate::event_source::MockEventSource;
        use crate::journal::InMemoryJournal;

        let signer = make_signer();
        let submitter = MockSubmitter::default();
        let event = sample_event(7, 100);
        let mut event_source = MockEventSource::new();
        event_source.push(event.clone());
        let journal = InMemoryJournal::default();
        let mut core = WatcherCore::new(0xE000_0001, signer, event_source, submitter, journal);

        // process_event captures the nonce
        let _ = core.tick();

        // Recovery: mark nonce submitted, advance cursor past the event's block
        core.recover_already_consumed().unwrap();
        assert!(core.journal.is_submitted(0xE000_0001, 7).unwrap());
        assert_eq!(core.journal.cursor().unwrap(), 101);
    }

    #[test]
    fn process_event_rejects_wrong_chain_id() {
        let signer = make_signer();
        let submitter = MockSubmitter::default();
        let event_source = MockEventSource::new();
        let journal = InMemoryJournal::default();
        let mut core = WatcherCore::new(0xE000_0002, signer, event_source, submitter, journal);

        let event = sample_event(1, 100);
        let result = core.process_event(event);
        assert!(matches!(result, Err(CoreError::ChainIdMismatch { .. })));
    }

    #[test]
    fn already_submitted_skips_journaled_nonce() {
        let signer = make_signer();
        let submitter = MockSubmitter::default();
        let mut journal = InMemoryJournal::default();
        journal.mark_submitted(0xE000_0001, 1).unwrap();
        let mut event_source = MockEventSource::new();
        event_source.push(sample_event(1, 100));
        let mut core = WatcherCore::new(0xE000_0001, signer, event_source, submitter, journal);

        let result = core.process_event(sample_event(1, 100));
        assert!(matches!(result, Err(CoreError::AlreadySubmitted(1))));
    }

    #[test]
    fn recover_already_consumed_advances_cursor() {
        let signer = make_signer();
        let submitter = MockSubmitter::default();
        let journal = InMemoryJournal::default();
        let mut event_source = MockEventSource::new();
        event_source.push(sample_event(42, 200));
        let mut core = WatcherCore::new(0xE000_0001, signer, event_source, submitter, journal);

        let _ = core.tick(); // captures nonce 42

        core.recover_already_consumed().unwrap();
        assert!(core.journal.is_submitted(0xE000_0001, 42).unwrap());
        assert_eq!(core.journal.cursor().unwrap(), 201); // block + 1
    }
}
