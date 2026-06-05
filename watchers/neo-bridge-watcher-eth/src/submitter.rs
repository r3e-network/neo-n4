//! Neo-side submission abstraction. `NeoSubmitter::submit_inbound` posts
//! the (messageBytes, proofBytes) pair to
//! `NeoHub.ExternalBridgeEscrow.Receive`. Real impls wrap a Neo JSON-RPC
//! client + signed tx; the mock impl just records what was submitted.

mod inbound_submission;

use std::collections::HashSet;
use thiserror::Error;

pub use inbound_submission::InboundSubmission;

#[derive(Debug, Error)]
pub enum SubmitterError {
    #[error("rpc error: {0}")]
    Rpc(String),
    #[error("nonce already consumed on chain")]
    AlreadyConsumed,
    #[error("verifier rejected proof: {0}")]
    VerifierRejected(String),
}

pub trait NeoSubmitter {
    /// Post the submission to NeoHub.ExternalBridgeEscrow.Receive. On
    /// success, returns the Neo tx hash; on failure, surfaces a typed
    /// error the daemon's retry policy can branch on.
    fn submit_inbound(&mut self, submission: InboundSubmission)
    -> Result<[u8; 32], SubmitterError>;

    /// Read the NeoHub.ExternalBridgeEscrow replay state for a candidate inbound.
    ///
    /// The daemon uses this as a confirmation guard before recovering from an
    /// `AlreadyConsumed` pre-check fault. Without the read, a faulty or malicious
    /// RPC endpoint could cause the local journal to advance solely from a string
    /// in a FAULT response.
    fn is_inbound_consumed(
        &mut self,
        external_chain_id: u32,
        nonce: u64,
    ) -> Result<bool, SubmitterError>;
}

/// Test fixture: records every submission. Returns deterministic tx
/// hashes (low byte = submission count) so tests can assert on order.
#[derive(Debug)]
pub struct MockSubmitter {
    submissions: Vec<InboundSubmission>,
    consumed_on_chain: HashSet<(u32, u64)>,
    /// If set, the next submit_inbound returns this error instead of
    /// recording the submission. Useful for testing retry paths.
    pub next_error: Option<SubmitterError>,
}

impl MockSubmitter {
    pub fn new() -> Self {
        Self {
            submissions: Vec::new(),
            consumed_on_chain: HashSet::new(),
            next_error: None,
        }
    }

    pub fn submissions(&self) -> &[InboundSubmission] {
        &self.submissions
    }

    pub fn mark_consumed_on_chain(&mut self, external_chain_id: u32, nonce: u64) {
        self.consumed_on_chain.insert((external_chain_id, nonce));
    }
}

impl Default for MockSubmitter {
    fn default() -> Self {
        Self::new()
    }
}

impl NeoSubmitter for MockSubmitter {
    fn submit_inbound(
        &mut self,
        submission: InboundSubmission,
    ) -> Result<[u8; 32], SubmitterError> {
        if let Some(err) = self.next_error.take() {
            return Err(err);
        }
        self.submissions.push(submission);
        let n = self.submissions.len() as u8;
        let mut hash = [0u8; 32];
        hash[0] = n;
        Ok(hash)
    }

    fn is_inbound_consumed(
        &mut self,
        external_chain_id: u32,
        nonce: u64,
    ) -> Result<bool, SubmitterError> {
        Ok(self.consumed_on_chain.contains(&(external_chain_id, nonce)))
    }
}
