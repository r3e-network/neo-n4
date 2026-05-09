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
const LOCK_FILE: &str = ".lock";

pub struct FileJournal {
    dir: PathBuf,
    cursor: u64,
    submitted: HashSet<(u32, u64)>,
    /// Open append-only handle. Held for the journal's lifetime so
    /// every `mark_submitted` call appends without re-opening.
    consumed_log: File,
    /// Exclusive-mode `flock`'d sentinel file held for the journal's
    /// lifetime. The OS releases the flock on Drop or process exit
    /// (so a crashed previous instance never blocks restart). Prevents
    /// two watcher processes from racing on consumed.log — without
    /// this, interleaved 12-byte appends would corrupt the record
    /// alignment and invalidate every replay forever.
    #[cfg(unix)]
    _lock: File,
}

impl FileJournal {
    /// Open or create a journal directory. Replays `consumed.log` into
    /// the in-memory `submitted` set + reads the cursor.
    ///
    /// Acquires an exclusive advisory lock on `.lock` first; if another
    /// process is already using this journal directory, returns
    /// `JournalError::Io` immediately rather than racing on the
    /// append-only log. The lock auto-releases on Drop (or process exit).
    pub fn open(dir: impl AsRef<Path>) -> Result<Self, JournalError> {
        let dir = dir.as_ref().to_path_buf();
        std::fs::create_dir_all(&dir).map_err(|e| JournalError::Io(format!("create_dir: {e}")))?;

        // Acquire the inter-process lock BEFORE touching any journal
        // file. If the lock acquisition fails — another instance has
        // it — we want to bail out without having mutated anything.
        #[cfg(unix)]
        let lock_file = acquire_exclusive_lock(&dir.join(LOCK_FILE))?;

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

        Ok(Self {
            dir,
            cursor,
            submitted,
            consumed_log,
            #[cfg(unix)]
            _lock: lock_file,
        })
    }
}

/// Acquire an exclusive advisory `flock` on the lock-sentinel file.
///
/// Uses `LOCK_EX | LOCK_NB` so the call returns immediately rather than
/// blocking — operators want fast feedback if another instance is already
/// running, not a hang. The lock is released by the OS on FD close (Drop
/// or process exit), so a crashed previous instance can never block a
/// fresh start.
#[cfg(unix)]
fn acquire_exclusive_lock(lock_path: &Path) -> Result<File, JournalError> {
    use std::os::fd::AsRawFd;
    let lock_file = OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(false)
        .open(lock_path)
        .map_err(|e| JournalError::Io(format!("open .lock: {e}")))?;
    let fd = lock_file.as_raw_fd();
    // SAFETY: fd is a valid file descriptor we just obtained from a
    // successful open + held alive by `lock_file`. flock is signal-safe
    // and has no aliasing concerns. The flags are POSIX-defined constants.
    let rc = unsafe { libc::flock(fd, libc::LOCK_EX | libc::LOCK_NB) };
    if rc != 0 {
        let err = std::io::Error::last_os_error();
        return Err(JournalError::Io(format!(
            "another watcher instance has this journal locked at {} \
             (flock LOCK_EX|LOCK_NB: {})",
            lock_path.display(),
            err
        )));
    }
    Ok(lock_file)
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

    /// Pin replay correctness + performance at production-realistic
    /// scale. A busy bridge hits ~500 inbounds/day; over a year that's
    /// ~180k records. The reopen scan must complete in well under a
    /// second so a watcher restart is fast. 5000 records ≈ 60 KB,
    /// representative without making the test slow.
    #[test]
    fn large_consumed_log_replays_correctly() {
        let dir = temp_dir("large-replay");
        const N: u64 = 5_000;

        // Write phase: 5000 marks across 4 chains.
        {
            let mut j = FileJournal::open(&dir).unwrap();
            for i in 0..N {
                let chain = match i % 4 {
                    0 => 0xE000_0001, // Eth
                    1 => 0xE000_0010, // Tron
                    2 => 0xE000_0030, // BSC
                    _ => 0xE000_0040, // Polygon
                };
                assert!(
                    j.mark_submitted(chain, i).unwrap(),
                    "mark {i} on chain 0x{chain:08X} should be new"
                );
            }
            j.set_cursor(N).unwrap();
        }

        // Replay phase: reopen + verify every record + cursor.
        let start = std::time::Instant::now();
        let j = FileJournal::open(&dir).unwrap();
        let elapsed = start.elapsed();
        assert!(
            elapsed.as_millis() < 500,
            "replay of {N} records took {elapsed:?} — too slow"
        );
        assert_eq!(j.cursor().unwrap(), N);
        for i in 0..N {
            let chain = match i % 4 {
                0 => 0xE000_0001,
                1 => 0xE000_0010,
                2 => 0xE000_0030,
                _ => 0xE000_0040,
            };
            assert!(
                j.is_submitted(chain, i).unwrap(),
                "mark {i} on chain 0x{chain:08X} did not replay"
            );
        }
        // Sanity: a record that was NEVER written stays unmarked.
        assert!(!j.is_submitted(0xE000_0001, N + 100).unwrap());
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// A corrupt cursor.bin (less than 8 bytes — could happen if the
    /// disk filled mid-write before the atomic rename, leaving a
    /// partial cursor.bin from a previous deployment) must surface a
    /// typed error at reopen — not silently fall back to 0, which
    /// would re-process every event since genesis on the next tick.
    #[test]
    fn corrupt_short_cursor_returns_error() {
        let dir = temp_dir("corrupt-cursor");
        std::fs::create_dir_all(&dir).unwrap();
        let cursor_path = dir.join(CURSOR_FILE);
        // 4 bytes — too short for a u64.
        std::fs::write(&cursor_path, [0xAA; 4]).unwrap();

        let result = FileJournal::open(&dir);
        match result {
            Err(JournalError::Io(msg)) => assert!(
                msg.contains("cursor"),
                "error should name the cursor field, got: {msg}"
            ),
            Ok(_) => panic!(
                "must NOT silently succeed with a corrupt cursor.bin — \
                 would re-process every event since genesis"
            ),
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// `set_cursor` uses an atomic rename; cursor.bin.tmp must NOT
    /// remain in the directory after a successful set. A leak would
    /// indicate the rename pattern was bypassed (and the next set
    /// would race with the leftover handle).
    #[test]
    fn set_cursor_does_not_leak_tmp_file() {
        let dir = temp_dir("no-tmp-leak");
        let mut j = FileJournal::open(&dir).unwrap();
        for n in 1..=10 {
            j.set_cursor(n * 100).unwrap();
        }
        // Drop the journal so any held handles are released.
        drop(j);

        let tmp = dir.join(format!("{CURSOR_FILE}.tmp"));
        assert!(
            !tmp.exists(),
            "cursor.bin.tmp should not survive successful set_cursor — atomic rename invariant"
        );
        let cursor = dir.join(CURSOR_FILE);
        assert!(cursor.exists(), "cursor.bin should exist after writes");
        let bytes = std::fs::read(&cursor).unwrap();
        assert_eq!(
            bytes.len(),
            8,
            "cursor.bin must be exactly 8 bytes (one u64 LE)"
        );
        assert_eq!(
            u64::from_le_bytes(bytes[..8].try_into().unwrap()),
            1000,
            "cursor.bin must hold the final cursor value"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// Concurrent-instance detection: two `FileJournal::open` calls on
    /// the same directory cannot both succeed. Without this defense,
    /// two watcher processes pointed at the same journal would
    /// interleave 12-byte appends to consumed.log and corrupt every
    /// future replay (records misaligned by 1+ bytes parse as garbage
    /// chain ids / nonces).
    #[cfg(unix)]
    #[test]
    fn second_open_on_same_dir_fails_with_lock_error() {
        let dir = temp_dir("concurrent-open");
        let first = FileJournal::open(&dir).expect("first open succeeds");

        let result = FileJournal::open(&dir);
        match result {
            Err(JournalError::Io(msg)) => {
                assert!(
                    msg.contains("locked") || msg.contains("flock"),
                    "lock-failure message must name the lock mechanism: got '{msg}'"
                );
            }
            Ok(_) => panic!(
                "second open MUST fail while the first instance holds the lock"
            ),
        }

        // After the first instance drops, the lock is released and a
        // fresh open succeeds — operators can restart cleanly.
        drop(first);
        let _ = FileJournal::open(&dir).expect("reopen after first instance closes");
        let _ = std::fs::remove_dir_all(&dir);
    }

    /// Stress test: interleave cursor advances with marks across
    /// multiple chains. After drop + reopen, every mark replays AND
    /// the cursor matches the highest set value. Pins that the
    /// cursor.bin and consumed.log are independent (one's writes don't
    /// stomp the other's).
    #[test]
    fn interleaved_cursor_and_marks_persist_independently() {
        let dir = temp_dir("interleaved");
        const STEPS: u64 = 100;

        // Mixed sequence: at each step bump cursor, mark on chain A,
        // then mark on chain B.
        {
            let mut j = FileJournal::open(&dir).unwrap();
            for step in 1..=STEPS {
                j.set_cursor(step * 10).unwrap();
                assert!(j.mark_submitted(0xE000_0001, step).unwrap());
                assert!(j.mark_submitted(0xE000_0030, step).unwrap());
            }
        }

        // Reopen + verify both axes.
        let j = FileJournal::open(&dir).unwrap();
        assert_eq!(
            j.cursor().unwrap(),
            STEPS * 10,
            "cursor must hold the final value across reopen"
        );
        for step in 1..=STEPS {
            assert!(
                j.is_submitted(0xE000_0001, step).unwrap(),
                "Eth mark step {step} dropped"
            );
            assert!(
                j.is_submitted(0xE000_0030, step).unwrap(),
                "BSC mark step {step} dropped"
            );
        }
        // Cross-chain isolation: BSC mark for step S doesn't show up as
        // an Eth mark for the same nonce.
        assert!(!j.is_submitted(0xE000_0040, 1).unwrap());
        let _ = std::fs::remove_dir_all(&dir);
    }
}
