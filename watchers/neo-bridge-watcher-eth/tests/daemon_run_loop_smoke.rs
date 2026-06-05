//! End-to-end smoke test for the daemon's run loop.
//!
//! Spawns the `neo-bridge-watcher-eth` binary against an in-process
//! fake JSON-RPC pair (Eth + Neo), lets it run for ~2s, sends SIGTERM,
//! and asserts:
//!
//!  - daemon exits with code 0 (graceful shutdown succeeded)
//!  - the eth fake received at least one `eth_blockNumber` request
//!    (the run loop's hot path actually ran)
//!
//! This is the smallest meaningful end-to-end test of the run loop
//! that doesn't require valid Locked-event ABI encoding. A more
//! ambitious follow-up could feed a synthetic event + verify the
//! journal advances; for now this pins the shutdown + RPC-poll
//! integration which is what kept failing during CI repair work.
//!
//! Gated behind `live-rpc` (the bin requires it).

#![cfg(all(unix, feature = "live-rpc"))]

use std::os::fd::AsRawFd;
use std::os::unix::process::ExitStatusExt;
use std::path::{Path, PathBuf};
use std::process::{Command, Stdio};
use std::sync::Arc;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::thread;
use std::time::{Duration, Instant};

mod support;

use support::fake_rpc_server::FakeRpcServer;
use support::temp_dir::TempDir;

fn build_test_config(eth_url: &str, neo_url: &str, journal_dir: &Path) -> (TempDir, PathBuf) {
    let tmp = TempDir::new("daemon-run-loop").unwrap();
    let key_path = tmp.path().join("watcher.priv");
    std::fs::write(&key_path, [0x42u8; 32]).unwrap();
    let cfg_path = tmp.path().join("watcher.toml");
    let toml = format!(
        r#"
external_chain_id   = 0xE0000001
eth_rpc_url         = "{eth_url}"
eth_router_address  = "0x0000000000000000000000000000000000000001"
neo_rpc_url         = "{neo_url}"
neo_escrow_address  = "0x0000000000000000000000000000000000000001"
neo_signer_address  = "0x0000000000000000000000000000000000000001"
signer_key_path     = "{key}"
journal_dir         = "{jrn}"

[poll]
poll_interval_secs    = 1
backoff_initial_secs  = 1
backoff_max_secs      = 5
eth_chunk_size        = 100
request_timeout_secs  = 5
min_confirmations     = 0
"#,
        key = key_path.display(),
        jrn = journal_dir.display(),
    );
    std::fs::write(&cfg_path, toml).unwrap();
    (tmp, cfg_path)
}

/// SIGTERM the subprocess. Rust's `Child::kill()` sends SIGKILL; we
/// need SIGTERM to exercise the daemon's graceful shutdown path.
fn sigterm(child: &std::process::Child) -> std::io::Result<()> {
    let pid = child.id() as libc::pid_t;
    // SAFETY: passing a valid pid + a known signal constant. libc::kill
    // is signal-safe; nothing in this scope holds locks across the
    // call.
    let rc = unsafe { libc::kill(pid, libc::SIGTERM) };
    if rc == 0 {
        Ok(())
    } else {
        Err(std::io::Error::last_os_error())
    }
}

/// Read accumulated stdout/stderr from a fd in a non-blocking way.
fn drain_pipe(file: &mut std::fs::File) -> String {
    use std::io::Read;
    let mut buf = String::new();
    let _ = file.read_to_string(&mut buf);
    buf
}

#[test]
fn daemon_run_loop_starts_polls_and_shuts_down_on_sigterm() {
    // Eth fake: counts requests + responds with 0x100 head, empty
    // logs (no events to process), and bytecode for the router.
    let eth_calls = Arc::new(AtomicUsize::new(0));
    let eth_calls_c = eth_calls.clone();
    let eth = FakeRpcServer::spawn(move |body: &str| {
        eth_calls_c.fetch_add(1, Ordering::Relaxed);
        if body.contains("eth_blockNumber") {
            r#"{"jsonrpc":"2.0","id":1,"result":"0x100"}"#.to_string()
        } else if body.contains("eth_getLogs") {
            r#"{"jsonrpc":"2.0","id":1,"result":[]}"#.to_string()
        } else if body.contains("eth_getCode") {
            r#"{"jsonrpc":"2.0","id":1,"result":"0x6080604052"}"#.to_string()
        } else {
            r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
        }
    });
    // Neo fake: shouldn't be touched (no events to submit), but
    // accept getversion just in case + return null for everything.
    let neo = FakeRpcServer::spawn(|_body: &str| {
        r#"{"jsonrpc":"2.0","id":1,"result":{"network":860833102}}"#.to_string()
    });

    // Build config + journal dir.
    let journal_dir = std::env::temp_dir().join(format!(
        "daemon-loop-journal-{}",
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap()
            .as_nanos()
    ));
    let _ = std::fs::remove_dir_all(&journal_dir);
    let (_tmp, cfg_path) = build_test_config(&eth.url, &neo.url, &journal_dir);

    // Spawn the daemon binary. --allow-stub-signer is required: the daemon refuses
    // to start with the built-in stub signer otherwise (so production deployments
    // can't silently no-op submissions). The smoke test only exercises the run-loop
    // signal-handling shape, so the stub is fine here.
    let exe = env!("CARGO_BIN_EXE_neo-bridge-watcher-eth");
    let mut child = Command::new(exe)
        .args([
            "--config",
            cfg_path.to_str().unwrap(),
            "--allow-stub-signer",
        ])
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .expect("spawn daemon");

    // Let the run loop iterate at least once (poll_interval_secs=1).
    thread::sleep(Duration::from_millis(2_500));

    // Send SIGTERM. The graceful shutdown's interruptible_sleep
    // checks the flag every 100ms — even if the daemon was mid-poll
    // backoff, exit should land within ~100-200ms.
    sigterm(&child).expect("sigterm");

    // Wait for exit with a hard 5s ceiling. Production grace period
    // is 30s; for the test we lean tight to fail fast on regressions.
    let start = Instant::now();
    let exit_status = loop {
        match child.try_wait().expect("try_wait") {
            Some(status) => break status,
            None => {
                if start.elapsed() > Duration::from_secs(5) {
                    let _ = child.kill();
                    panic!("daemon did not exit within 5s of SIGTERM");
                }
                thread::sleep(Duration::from_millis(50));
            }
        }
    };

    // Read both stdout + stderr to drain. We don't care about stdout
    // (it's only used by --version + --config-template); the run
    // loop logs to stderr.
    let mut stderr_pipe = unsafe {
        use std::os::fd::FromRawFd;
        let fd = child.stderr.as_ref().unwrap().as_raw_fd();
        std::fs::File::from_raw_fd(libc::dup(fd))
    };
    let stderr_text = drain_pipe(&mut stderr_pipe);

    // Assertions.
    assert_eq!(
        exit_status.code(),
        Some(0),
        "daemon should exit cleanly on SIGTERM. signal={:?} stderr:\n{}",
        exit_status.signal(),
        stderr_text,
    );

    let eth_call_count = eth_calls.load(Ordering::Relaxed);
    assert!(
        eth_call_count >= 2,
        "expected the run loop to have iterated at least once (≥2 eth RPC calls — \
         eth_blockNumber + eth_getLogs); got {eth_call_count}\nstderr:\n{stderr_text}"
    );

    let _ = std::fs::remove_dir_all(&journal_dir);
}
