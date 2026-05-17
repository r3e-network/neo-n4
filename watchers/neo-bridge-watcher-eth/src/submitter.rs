//! Neo-side submission abstraction. `NeoSubmitter::submit_inbound` posts
//! the (messageBytes, proofBytes) pair to
//! `NeoHub.ExternalBridgeEscrow.Receive`. Real impls wrap a Neo JSON-RPC
//! client + signed tx; the mock impl just records what was submitted.

use thiserror::Error;

#[derive(Debug, Error)]
pub enum SubmitterError {
    #[error("rpc error: {0}")]
    Rpc(String),
    #[error("nonce already consumed on chain")]
    AlreadyConsumed,
    #[error("verifier rejected proof: {0}")]
    VerifierRejected(String),
}

/// One submission batch: the canonical message bytes + the wire-format
/// proof bytes (Neo flavor, see [`crate::proof::NeoProofBytes`]).
#[derive(Debug, Clone)]
pub struct InboundSubmission {
    pub external_chain_id: u32,
    pub message_bytes: Vec<u8>,
    pub proof_bytes: Vec<u8>,
}

pub trait NeoSubmitter {
    /// Post the submission to NeoHub.ExternalBridgeEscrow.Receive. On
    /// success, returns the Neo tx hash; on failure, surfaces a typed
    /// error the daemon's retry policy can branch on.
    fn submit_inbound(&mut self, submission: InboundSubmission)
        -> Result<[u8; 32], SubmitterError>;
}

/// Test fixture: records every submission. Returns deterministic tx
/// hashes (low byte = submission count) so tests can assert on order.
pub struct MockSubmitter {
    submissions: Vec<InboundSubmission>,
    /// If set, the next submit_inbound returns this error instead of
    /// recording the submission. Useful for testing retry paths.
    pub next_error: Option<SubmitterError>,
}

impl MockSubmitter {
    pub fn new() -> Self {
        Self {
            submissions: Vec::new(),
            next_error: None,
        }
    }

    pub fn submissions(&self) -> &[InboundSubmission] {
        &self.submissions
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
}
