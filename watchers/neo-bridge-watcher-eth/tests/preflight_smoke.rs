//! Integration test: spawn the `neo-bridge-watcher-eth` binary with
//! `--preflight` against fake JSON-RPC servers and assert the exit
//! code on both happy + sad paths.
//!
//! The bin's `preflight()` function is private to the binary crate;
//! integration tests can't call it directly. So we go through
//! `Command::new(env!("CARGO_BIN_EXE_neo-bridge-watcher-eth"))` —
//! cargo builds the bin automatically for integration tests that
//! reference this env var.
//!
//! `FakeRpcServer` is duplicated inline rather than reused from
//! `live::test_support::FakeRpcServer` because that module is
//! `pub(crate)` (not exposed to integration tests, which are a
//! separate crate). The duplication is ~80 lines; promoting
//! `test_support` to `pub` would expose internal scaffolding to
//! every downstream consumer of the library.
//!
//! Gated behind `live-rpc`: the bin itself requires `live-rpc`
//! (per `Cargo.toml`'s `[[bin]] required-features = ["live-rpc"]`),
//! so an integration test that invokes the bin must also opt in.

#![cfg(feature = "live-rpc")]

use std::io::{Read, Write};
use std::net::TcpListener;
use std::process::Command;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;
use std::thread;
use std::time::Duration;

struct FakeRpcServer {
    url: String,
    stop: Arc<AtomicBool>,
    _handle: thread::JoinHandle<()>,
}

impl FakeRpcServer {
    fn spawn<F>(handler: F) -> Self
    where
        F: Fn(&str) -> String + Send + 'static,
    {
        let listener = TcpListener::bind("127.0.0.1:0").unwrap();
        listener.set_nonblocking(true).unwrap();
        let port = listener.local_addr().unwrap().port();
        let url = format!("http://127.0.0.1:{port}/");
        let stop = Arc::new(AtomicBool::new(false));
        let stop_c = stop.clone();
        let handle = thread::spawn(move || {
            while !stop_c.load(Ordering::Relaxed) {
                match listener.accept() {
                    Ok((mut stream, _)) => {
                        let _ = stream.set_nonblocking(false);
                        let mut buf = vec![0u8; 8192];
                        let n = stream.read(&mut buf).unwrap_or(0);
                        let req = String::from_utf8_lossy(&buf[..n]).to_string();
                        let body = req.split("\r\n\r\n").nth(1).unwrap_or("").to_string();
                        let resp = handler(&body);
                        let http = format!(
                            "HTTP/1.1 200 OK\r\n\
                             Content-Type: application/json\r\n\
                             Content-Length: {}\r\n\
                             Connection: close\r\n\
                             \r\n{}",
                            resp.len(),
                            resp
                        );
                        let _ = stream.write_all(http.as_bytes());
                    }
                    Err(e) if e.kind() == std::io::ErrorKind::WouldBlock => {
                        thread::sleep(Duration::from_millis(20));
                    }
                    Err(_) => break,
                }
            }
        });
        Self { url, stop, _handle: handle }
    }
}

impl Drop for FakeRpcServer {
    fn drop(&mut self) {
        self.stop.store(true, Ordering::Relaxed);
    }
}

/// Build a tempdir + TOML config + 32-byte signer key + return paths.
struct PreflightFixture {
    _tmp: tempdir::TempDir,
    config_path: std::path::PathBuf,
}

mod tempdir {
    use std::path::PathBuf;
    pub struct TempDir {
        path: PathBuf,
    }
    impl TempDir {
        pub fn new(prefix: &str) -> std::io::Result<Self> {
            let mut p = std::env::temp_dir();
            let ns = std::time::SystemTime::now()
                .duration_since(std::time::UNIX_EPOCH)
                .unwrap()
                .as_nanos();
            p.push(format!("{prefix}-{ns}"));
            std::fs::create_dir_all(&p)?;
            Ok(Self { path: p })
        }
        pub fn path(&self) -> &std::path::Path {
            &self.path
        }
    }
    impl Drop for TempDir {
        fn drop(&mut self) {
            let _ = std::fs::remove_dir_all(&self.path);
        }
    }
}

fn build_fixture(eth_url: &str, neo_url: &str) -> PreflightFixture {
    let tmp = tempdir::TempDir::new("preflight-test").unwrap();
    let key_path = tmp.path().join("watcher.priv");
    let journal_dir = tmp.path().join("journal");
    let config_path = tmp.path().join("watcher.toml");

    std::fs::write(&key_path, [0x42u8; 32]).unwrap();
    let toml = format!(
        r#"
external_chain_id   = 0xE0000030
eth_rpc_url         = "{eth_url}"
eth_router_address  = "0x0000000000000000000000000000000000000001"
neo_rpc_url         = "{neo_url}"
neo_escrow_address  = "0x0000000000000000000000000000000000000001"
neo_signer_address  = "0x0000000000000000000000000000000000000001"
signer_key_path     = "{key}"
journal_dir         = "{jrn}"
[poll]
min_confirmations = 15
request_timeout_secs = 5
"#,
        key = key_path.display(),
        jrn = journal_dir.display(),
    );
    std::fs::write(&config_path, toml).unwrap();
    PreflightFixture { _tmp: tmp, config_path }
}

fn run_preflight(config_path: &std::path::Path) -> (i32, String) {
    let exe = env!("CARGO_BIN_EXE_neo-bridge-watcher-eth");
    let output = Command::new(exe)
        .args([
            "--config",
            config_path.to_str().unwrap(),
            "--preflight",
        ])
        .output()
        .expect("failed to spawn watcher binary");
    let combined = format!(
        "{}\n{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    );
    (output.status.code().unwrap_or(-1), combined)
}

#[test]
fn preflight_passes_with_responsive_rpc_endpoints() {
    let eth = FakeRpcServer::spawn(|body: &str| {
        if body.contains("eth_blockNumber") {
            r#"{"jsonrpc":"2.0","id":1,"result":"0x100"}"#.to_string()
        } else {
            r#"{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"not found"}}"#.into()
        }
    });
    let neo = FakeRpcServer::spawn(|body: &str| {
        if body.contains("getversion") {
            // Real Neo returns a structured response; preflight just
            // checks for absence of `error` and presence of `result`.
            r#"{"jsonrpc":"2.0","id":1,"result":{"network":860833102}}"#.to_string()
        } else {
            r#"{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"unknown"}}"#.into()
        }
    });

    let fix = build_fixture(&eth.url, &neo.url);
    let (code, output) = run_preflight(&fix.config_path);

    assert_eq!(
        code, 0,
        "preflight against responsive fakes should exit 0; output:\n{output}"
    );
    assert!(
        output.contains("preflight: all checks passed"),
        "expected success line in output:\n{output}"
    );
    assert!(
        output.contains("[ok]   eth_rpc_url"),
        "expected eth probe success line; output:\n{output}"
    );
    assert!(
        output.contains("[ok]   neo_rpc_url"),
        "expected neo probe success line; output:\n{output}"
    );
    // The eth probe prints the head height it observed.
    assert!(
        output.contains("head = 256"),
        "expected to see head height (0x100 = 256) in output; got:\n{output}"
    );
}

#[test]
fn preflight_fails_when_eth_rpc_unreachable() {
    // Bind + drop to find an unused port; the daemon will fail to
    // connect when it tries to probe.
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    drop(listener);
    let eth_url = format!("http://127.0.0.1:{port}/");

    // Neo URL is a valid responsive fake — we want to assert the
    // failure point is specifically eth, not "first unreachable".
    let neo = FakeRpcServer::spawn(|_body: &str| {
        r#"{"jsonrpc":"2.0","id":1,"result":{"network":860833102}}"#.to_string()
    });

    let fix = build_fixture(&eth_url, &neo.url);
    let (code, output) = run_preflight(&fix.config_path);

    assert_eq!(code, 1, "preflight against unreachable eth_rpc should exit 1");
    assert!(
        output.contains("preflight: FAILED") && output.contains("eth_blockNumber"),
        "failure should name eth_blockNumber; got:\n{output}"
    );
}

#[test]
fn preflight_fails_when_eth_router_is_zero_address() {
    // No fakes needed — zero-address check fires before any RPC probe.
    let tmp = tempdir::TempDir::new("preflight-zero-router").unwrap();
    let key_path = tmp.path().join("watcher.priv");
    std::fs::write(&key_path, [0x42u8; 32]).unwrap();
    let cfg = tmp.path().join("watcher.toml");
    let toml = format!(
        r#"
external_chain_id   = 0xE0000030
eth_rpc_url         = "http://127.0.0.1:1"
eth_router_address  = "0x0000000000000000000000000000000000000000"
neo_rpc_url         = "http://127.0.0.1:1"
neo_escrow_address  = "0x0000000000000000000000000000000000000001"
neo_signer_address  = "0x0000000000000000000000000000000000000001"
signer_key_path     = "{}"
journal_dir         = "{}"
"#,
        key_path.display(),
        tmp.path().join("journal").display()
    );
    std::fs::write(&cfg, toml).unwrap();

    let (code, output) = run_preflight(&cfg);
    assert_eq!(code, 1);
    assert!(
        output.contains("eth_router_address is the zero address"),
        "expected zero-router rejection; got:\n{output}"
    );
}

#[test]
fn preflight_fails_when_neo_rpc_returns_jsonrpc_error() {
    let eth = FakeRpcServer::spawn(|_body: &str| {
        r#"{"jsonrpc":"2.0","id":1,"result":"0x10"}"#.to_string()
    });
    let neo = FakeRpcServer::spawn(|_body: &str| {
        // Server is reachable but returns a JSON-RPC error — operator
        // probably has the wrong URL (e.g. a generic web server).
        r#"{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"unknown method"}}"#
            .to_string()
    });

    let fix = build_fixture(&eth.url, &neo.url);
    let (code, output) = run_preflight(&fix.config_path);

    assert_eq!(code, 1);
    assert!(
        output.contains("preflight: FAILED") && output.contains("getversion"),
        "failure should name getversion + the rpc error; got:\n{output}"
    );
}
