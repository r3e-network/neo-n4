//! File-backed `Journal` impl for the watcher daemon's restart safety.
//!
//! Two files in a single directory:
//! - `cursor.bin` — a single u64 LE: the highest block number we've
//!   processed. Atomic-renamed on update so a crash mid-write can't
//!   corrupt it.
//! - `consumed.log` — append-only log of 12-byte records:
//!   `[4B chainId LE][8B nonce LE]`. On startup we replay the file
//!   into an in-memory `HashSet`. New marks append-and-fsync.
//!
//! Why not `rocksdb` / `sled`: this watcher writes O(1) records per
//! event (typically dozens per day on a quiet bridge, hundreds on a
//! busy one). The append-only log is simpler, dependency-free, and
//! easier to inspect by an operator (`xxd consumed.log`).

use crate::journal::{Journal, JournalError};
use std::collections::HashSet;
use std::fs::{File, OpenOptions};
use std::io::{Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

const CURSOR_FILE: &str = "cursor.bin";
const CONSUMED_FILE: &str = "consumed.log";

pub struct FileJournal {
    dir: PathBuf,
    cursor: u64,
    submitted: HashSet<(u32, u64)>,
    /// Open append-only handle. Held for the journal's lifetime so
    /// every `mark_submitted` call appends without re-opening.
    consumed_log: File,
}

impl FileJournal {
    /// Open or create a journal directory. Replays `consumed.log` into
    /// the in-memory `submitted` set + reads the cursor.
    pub fn open(dir: impl AsRef<Path>) -> Result<Self, JournalError> {
        let dir = dir.as_ref().to_path_buf();
        std::fs::create_dir_all(&dir).map_err(|e| JournalError::Io(format!("create_dir: {e}")))?;

        // Replay consumed.log.
        let consumed_path = dir.join(CONSUMED_FILE);
        let mut submitted = HashSet::new();
        if consumed_path.exists() {
            let mut f = File::open(&consumed_path)
                .map_err(|e| JournalError::Io(format!("open consumed: {e}")))?;
            let mut buf = Vec::new();
            f.read_to_end(&mut buf)
                .map_err(|e| JournalError::Io(format!("read consumed: {e}")))?;
            // 12-byte records. A trailing partial record (e.g., from a
            // crash mid-append) gets discarded — replay-safe because
            // mark_submitted is idempotent.
            for chunk in buf.chunks_exact(12) {
                let chain_id = u32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]);
                let nonce = u64::from_le_bytes([
                    chunk[4], chunk[5], chunk[6], chunk[7], chunk[8], chunk[9], chunk[10],
                    chunk[11],
                ]);
                submitted.insert((chain_id, nonce));
            }
        }

        // Read cursor.
        let cursor_path = dir.join(CURSOR_FILE);
        let cursor = if cursor_path.exists() {
            let mut f = File::open(&cursor_path)
                .map_err(|e| JournalError::Io(format!("open cursor: {e}")))?;
            let mut buf = [0u8; 8];
            f.read_exact(&mut buf)
                .map_err(|e| JournalError::Io(format!("read cursor: {e}")))?;
            u64::from_le_bytes(buf)
        } else {
            0
        };

        // Open consumed.log for append.
        let consumed_log = OpenOptions::new()
            .create(true)
            .append(true)
            .open(&consumed_path)
            .map_err(|e| JournalError::Io(format!("open consumed for append: {e}")))?;

        Ok(Self { dir, cursor, submitted, consumed_log })
    }
}

impl Journal for FileJournal {
    fn set_cursor(&mut self, block_number: u64) -> Result<(), JournalError> {
        if block_number <= self.cursor {
            return Ok(());
        }
        // Atomic rename for crash-safety: write to cursor.bin.tmp, then
        // rename over cursor.bin. POSIX rename is atomic within a
        // filesystem.
        let tmp_path = self.dir.join(format!("{CURSOR_FILE}.tmp"));
        let final_path = self.dir.join(CURSOR_FILE);
        {
            let mut f = File::create(&tmp_path)
                .map_err(|e| JournalError::Io(format!("create cursor.tmp: {e}")))?;
            f.write_all(&block_number.to_le_bytes())
                .map_err(|e| JournalError::Io(format!("write cursor: {e}")))?;
            f.sync_all()
                .map_err(|e| JournalError::Io(format!("sync cursor: {e}")))?;
        }
        std::fs::rename(&tmp_path, &final_path)
            .map_err(|e| JournalError::Io(format!("rename cursor: {e}")))?;
        self.cursor = block_number;
        Ok(())
    }

    fn cursor(&self) -> Result<u64, JournalError> {
        Ok(self.cursor)
    }

    fn mark_submitted(&mut self, external_chain_id: u32, nonce: u64) -> Result<bool, JournalError> {
        if !self.submitted.insert((external_chain_id, nonce)) {
            return Ok(false);
        }
        // Append the 12-byte record + fsync. fsync ensures the in-memory
        // set + on-disk log can't diverge across a crash.
        let mut record = [0u8; 12];
        record[0..4].copy_from_slice(&external_chain_id.to_le_bytes());
        record[4..12].copy_from_slice(&nonce.to_le_bytes());
        // Append to the always-open handle.
        self.consumed_log
            .seek(SeekFrom::End(0))
            .map_err(|e| JournalError::Io(format!("seek end: {e}")))?;
        self.consumed_log
            .write_all(&record)
            .map_err(|e| JournalError::Io(format!("append consumed: {e}")))?;
        self.consumed_log
            .sync_all()
            .map_err(|e| JournalError::Io(format!("sync consumed: {e}")))?;
        Ok(true)
    }

    fn is_submitted(&self, external_chain_id: u32, nonce: u64) -> Result<bool, JournalError> {
        Ok(self.submitted.contains(&(external_chain_id, nonce)))
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn temp_dir(label: &str) -> PathBuf {
        let mut p = std::env::temp_dir();
        // Use both a label + a nanosecond timestamp so parallel cargo
        // test invocations don't collide.
        let ns = std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        p.push(format!("neo-bridge-watcher-eth-{label}-{ns}"));
        let _ = std::fs::remove_dir_all(&p);
        p
    }

    #[test]
    fn empty_journal_returns_zero_cursor() {
        let dir = temp_dir("empty");
        let j = FileJournal::open(&dir).unwrap();
        assert_eq!(j.cursor().unwrap(), 0);
        assert!(!j.is_submitted(0xE0000001, 1).unwrap());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn cursor_round_trip_across_reopen() {
        let dir = temp_dir("cursor-roundtrip");
        {
            let mut j = FileJournal::open(&dir).unwrap();
            j.set_cursor(1234).unwrap();
            assert_eq!(j.cursor().unwrap(), 1234);
        }
        {
            let j = FileJournal::open(&dir).unwrap();
            assert_eq!(j.cursor().unwrap(), 1234);
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn cursor_does_not_regress() {
        let dir = temp_dir("cursor-monotone");
        let mut j = FileJournal::open(&dir).unwrap();
        j.set_cursor(100).unwrap();
        j.set_cursor(50).unwrap(); // lower — should be ignored
        assert_eq!(j.cursor().unwrap(), 100);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn mark_submitted_persists_across_reopen() {
        let dir = temp_dir("submitted-persists");
        {
            let mut j = FileJournal::open(&dir).unwrap();
            assert!(j.mark_submitted(0xE0000001, 7).unwrap());
            assert!(j.mark_submitted(0xE0000001, 8).unwrap());
            assert!(j.mark_submitted(0xE0000010, 7).unwrap()); // diff chain
            assert!(!j.mark_submitted(0xE0000001, 7).unwrap()); // duplicate
        }
        {
            let j = FileJournal::open(&dir).unwrap();
            assert!(j.is_submitted(0xE0000001, 7).unwrap());
            assert!(j.is_submitted(0xE0000001, 8).unwrap());
            assert!(j.is_submitted(0xE0000010, 7).unwrap());
            assert!(!j.is_submitted(0xE0000010, 8).unwrap());
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn truncated_consumed_log_is_replayed_safely() {
        // Simulate a crash mid-append: write 11 bytes (instead of 12)
        // for the second record; reopen should drop the partial record.
        let dir = temp_dir("truncated-replay");
        std::fs::create_dir_all(&dir).unwrap();
        let consumed_path = dir.join(CONSUMED_FILE);
        let mut f = std::fs::File::create(&consumed_path).unwrap();
        // First record: chainId 0xE0000001, nonce 7 (valid 12 bytes).
        f.write_all(&0xE0000001u32.to_le_bytes()).unwrap();
        f.write_all(&7u64.to_le_bytes()).unwrap();
        // Second record: 11 bytes (truncated by 1).
        f.write_all(&0xE0000001u32.to_le_bytes()).unwrap();
        f.write_all(&8u64.to_le_bytes()[..7]).unwrap();
        drop(f);

        let j = FileJournal::open(&dir).unwrap();
        assert!(j.is_submitted(0xE0000001, 7).unwrap(),
            "first complete record must replay");
        assert!(!j.is_submitted(0xE0000001, 8).unwrap(),
            "truncated record dropped silently — replay-safe");
        let _ = std::fs::remove_dir_all(&dir);
    }
}
