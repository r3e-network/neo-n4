//! Cursor + dedup persistence for the daemon. The daemon must remember:
//!
//! 1. The last-processed Eth block number so a restart resumes from the
//!    right place (any earlier and we re-submit; later and we miss
//!    events).
//! 2. Which (externalChainId, nonce) pairs have already been submitted
//!    so a transient RPC failure during submit doesn't lead to
//!    duplicate proof attempts (Neo's verifier rejects duplicates, but
//!    we'd rather catch them locally before paying the failed-tx gas).
//!
//! Real impl uses RocksDB; the mock impl is in-memory.

use std::collections::HashSet;

use thiserror::Error;

#[derive(Debug, Error)]
pub enum JournalError {
    #[error("io error: {0}")]
    Io(String),
}

pub trait Journal {
    /// Record that we processed an event up to `block_number` (inclusive).
    fn set_cursor(&mut self, block_number: u64) -> Result<(), JournalError>;

    /// Read the highest block we've processed, or 0 if never run.
    fn cursor(&self) -> Result<u64, JournalError>;

    /// Mark (externalChainId, nonce) as submitted. Returns true if this
    /// was a fresh insertion, false if it was already present.
    fn mark_submitted(&mut self, external_chain_id: u32, nonce: u64) -> Result<bool, JournalError>;

    /// Has this (externalChainId, nonce) already been submitted?
    fn is_submitted(&self, external_chain_id: u32, nonce: u64) -> Result<bool, JournalError>;
}

#[derive(Debug)]
pub struct InMemoryJournal {
    cursor: u64,
    submitted: HashSet<(u32, u64)>,
}

impl InMemoryJournal {
    pub fn new() -> Self {
        Self {
            cursor: 0,
            submitted: HashSet::new(),
        }
    }
}

impl Default for InMemoryJournal {
    fn default() -> Self {
        Self::new()
    }
}

impl Journal for InMemoryJournal {
    fn set_cursor(&mut self, block_number: u64) -> Result<(), JournalError> {
        if block_number > self.cursor {
            self.cursor = block_number;
        }
        Ok(())
    }

    fn cursor(&self) -> Result<u64, JournalError> {
        Ok(self.cursor)
    }

    fn mark_submitted(&mut self, external_chain_id: u32, nonce: u64) -> Result<bool, JournalError> {
        Ok(self.submitted.insert((external_chain_id, nonce)))
    }

    fn is_submitted(&self, external_chain_id: u32, nonce: u64) -> Result<bool, JournalError> {
        Ok(self.submitted.contains(&(external_chain_id, nonce)))
    }
}
