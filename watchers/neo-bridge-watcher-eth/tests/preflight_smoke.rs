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

use std::net::TcpListener;
use std::process::Command;

mod support;

use support::fake_rpc_server::FakeRpcServer;
use support::temp_dir::TempDir;

/// Build a tempdir + TOML config + 32-byte signer key + return paths.
struct PreflightFixture {
    _tmp: TempDir,
    config_path: std::path::PathBuf,
}

fn toml_basic_string(value: &str) -> String {
    let mut escaped = String::with_capacity(value.len());
    for ch in value.chars() {
        match ch {
            '\\' => escaped.push_str("\\\\"),
            '"' => escaped.push_str("\\\""),
            '\n' => escaped.push_str("\\n"),
            '\r' => escaped.push_str("\\r"),
            '\t' => escaped.push_str("\\t"),
            _ => escaped.push(ch),
        }
    }
    escaped
}

fn toml_path(path: &std::path::Path) -> String {
    toml_basic_string(path.to_string_lossy().as_ref())
}

fn build_fixture(eth_url: &str, neo_url: &str) -> PreflightFixture {
    let tmp = TempDir::new("preflight-test").unwrap();
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
        key = toml_path(&key_path),
        jrn = toml_path(&journal_dir),
    );
    std::fs::write(&config_path, toml).unwrap();
    PreflightFixture {
        _tmp: tmp,
        config_path,
    }
}

fn run_preflight(config_path: &std::path::Path) -> (i32, String) {
    run_with_args(&["--config", config_path.to_str().unwrap(), "--preflight"])
}

fn run_with_args(args: &[&str]) -> (i32, String) {
    let exe = env!("CARGO_BIN_EXE_neo-bridge-watcher-eth");
    let output = Command::new(exe)
        .args(args)
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
        } else if body.contains("eth_getCode") {
            // Return a tiny but non-empty bytecode hex — preflight
            // just checks "more than 0x" so a single byte is enough.
            r#"{"jsonrpc":"2.0","id":1,"result":"0x6080604052"}"#.to_string()
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
        output.contains("[ok]   eth_router_address has bytecode"),
        "expected eth_getCode probe success line; output:\n{output}"
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
fn preflight_fails_when_router_address_has_no_bytecode() {
    // eth_blockNumber succeeds (RPC is reachable) but eth_getCode
    // returns "0x" — operator passed an EOA / non-existent address
    // / typo'd a contract address.
    let eth = FakeRpcServer::spawn(|body: &str| {
        if body.contains("eth_blockNumber") {
            r#"{"jsonrpc":"2.0","id":1,"result":"0x10"}"#.to_string()
        } else if body.contains("eth_getCode") {
            r#"{"jsonrpc":"2.0","id":1,"result":"0x"}"#.to_string()
        } else {
            r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
        }
    });
    // Neo isn't reached — eth_getCode fires + fails first.
    let neo = FakeRpcServer::spawn(|_body: &str| {
        r#"{"jsonrpc":"2.0","id":1,"result":{"network":860833102}}"#.to_string()
    });

    let fix = build_fixture(&eth.url, &neo.url);
    let (code, output) = run_preflight(&fix.config_path);

    assert_eq!(code, 1);
    assert!(
        output.contains("preflight: FAILED") && output.contains("no bytecode"),
        "failure should name the no-bytecode condition; got:\n{output}"
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

    assert_eq!(
        code, 1,
        "preflight against unreachable eth_rpc should exit 1"
    );
    assert!(
        output.contains("preflight: FAILED") && output.contains("eth_blockNumber"),
        "failure should name eth_blockNumber; got:\n{output}"
    );
}

#[test]
fn preflight_fails_when_eth_router_is_zero_address() {
    // No fakes needed — zero-address check fires before any RPC probe.
    let tmp = TempDir::new("preflight-zero-router").unwrap();
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
        toml_path(&key_path),
        toml_path(&tmp.path().join("journal"))
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
    let eth = FakeRpcServer::spawn(|body: &str| {
        if body.contains("eth_blockNumber") {
            r#"{"jsonrpc":"2.0","id":1,"result":"0x10"}"#.to_string()
        } else if body.contains("eth_getCode") {
            r#"{"jsonrpc":"2.0","id":1,"result":"0x6080604052"}"#.to_string()
        } else {
            r#"{"jsonrpc":"2.0","id":1,"result":null}"#.to_string()
        }
    });
    let neo = FakeRpcServer::spawn(|_body: &str| {
        // Server is reachable but returns a JSON-RPC error — operator
        // probably has the wrong URL (e.g. a generic web server).
        r#"{"jsonrpc":"2.0","id":1,"error":{"code":-32601,"message":"unknown method"}}"#.to_string()
    });

    let fix = build_fixture(&eth.url, &neo.url);
    let (code, output) = run_preflight(&fix.config_path);

    assert_eq!(code, 1);
    assert!(
        output.contains("preflight: FAILED") && output.contains("getversion"),
        "failure should name getversion + the rpc error; got:\n{output}"
    );
}

// ─── operator-flag smoke tests ───────────────────────────────────────
//
// These pin the read-only operator UX (--version / --config-template /
// --journal-info / --help) in CI. Each spawns the bin as a subprocess
// + asserts on stdout/stderr + exit code; same pattern as the
// preflight tests above.

#[test]
fn version_flag_prints_pkg_name_and_version() {
    let (code, output) = run_with_args(&["--version"]);
    assert_eq!(code, 0, "--version must exit 0; got {code}\n{output}");
    // Format pinned: `<bin> <semver>`. Operators script around this.
    let trimmed = output.trim();
    assert!(
        trimmed.starts_with("neo-bridge-watcher-eth "),
        "expected 'neo-bridge-watcher-eth X.Y.Z'; got '{trimmed}'"
    );
    let version = trimmed.trim_start_matches("neo-bridge-watcher-eth ");
    assert!(
        version.split('.').count() == 3,
        "expected semver X.Y.Z; got '{version}'"
    );
}

#[test]
fn version_short_form_is_equivalent() {
    let (code1, out1) = run_with_args(&["--version"]);
    let (code2, out2) = run_with_args(&["-V"]);
    assert_eq!(code1, 0);
    assert_eq!(code2, 0);
    assert_eq!(
        out1.trim(),
        out2.trim(),
        "--version and -V must print identical output"
    );
}

#[test]
fn config_template_emits_parseable_toml() {
    // --config-template prints to stdout. Pipe to a file, replace
    // placeholders, run --preflight against it. End-to-end pin that
    // the template stays in sync with the Config / PollConfig /
    // HealthConfig structs.
    let (code, output) = run_with_args(&["--config-template"]);
    assert_eq!(code, 0, "--config-template must exit 0");

    // Sanity: every required field appears as a TOML assignment.
    for field in [
        "external_chain_id",
        "eth_rpc_url",
        "eth_router_address",
        "neo_rpc_url",
        "neo_escrow_address",
        "neo_signer_address",
        "signer_key_path",
        "journal_dir",
    ] {
        assert!(
            output.contains(&format!("{field}   ")) || output.contains(&format!("{field}  ")),
            "template missing required field `{field}`:\n{output}"
        );
    }
    // Both optional sections present + commented intro.
    assert!(output.contains("[poll]"));
    assert!(output.contains("[health]"));
    assert!(output.contains("min_confirmations"));
    assert!(
        output.contains("# start_block"),
        "start_block should be commented out by default"
    );

    // End-to-end: substitute placeholders, run preflight against
    // unreachable URLs (should fail at the RPC probe, NOT at TOML
    // parse — that's what we're verifying).
    let tmp = TempDir::new("template-test").unwrap();
    let key_path = tmp.path().join("watcher.priv");
    std::fs::write(&key_path, [0x42u8; 32]).unwrap();
    let cfg_path = tmp.path().join("watcher.toml");

    // Template uses REPLACE_WITH_* + relative paths; substitute them.
    let mut toml = output.replace("\n\n", "\n").to_string();
    // Strip the trailing stderr blob from run_with_args (it's empty here).
    if let Some(idx) = toml.rfind('\n') {
        if toml[idx..].trim().is_empty() {
            toml.truncate(idx);
        }
    }
    toml = toml
        .replace(
            "0xREPLACE_WITH_DEPLOYED_ROUTER_ADDR",
            "0x0000000000000000000000000000000000000001",
        )
        .replace(
            "0xREPLACE_WITH_NEOHUB_ESCROW_ADDR",
            "0x0000000000000000000000000000000000000001",
        )
        .replace(
            "0xREPLACE_WITH_WATCHER_NEO_ACCOUNT",
            "0x0000000000000000000000000000000000000001",
        )
        .replace("./watcher.priv", &toml_path(&key_path))
        .replace("./journal", &toml_path(&tmp.path().join("journal")))
        .replace("0.0.0.0:9090", "127.0.0.1:0");
    std::fs::write(&cfg_path, toml).unwrap();

    // Now run --preflight: TOML parse should succeed; the RPC probe
    // will fail (Sepolia URL is reachable but the router address
    // 0x0000...01 won't have bytecode there). The relevant assertion
    // is that we reach beyond the TOML parsing stage.
    let (_pcode, poutput) = run_preflight(&cfg_path);
    // We don't care about exit code (depends on Sepolia reachability
    // from CI runners). What we DO care about: the TOML parsed,
    // which we can verify by seeing the early "preflight: starting"
    // line. A TOML-parse failure would say "config error: parse..."
    // with no preflight line.
    assert!(
        poutput.contains("preflight: starting checks for chain"),
        "expected the template's TOML to parse + preflight to start;\n{poutput}"
    );
}

#[test]
fn journal_info_reads_hand_crafted_journal() {
    // Build a journal directory with cursor=42 + 3 records on 2
    // chains, then run --journal-info + assert the output.
    let tmp = TempDir::new("journal-info-test").unwrap();
    let key_path = tmp.path().join("watcher.priv");
    std::fs::write(&key_path, [0x42u8; 32]).unwrap();
    let journal_dir = tmp.path().join("journal");
    std::fs::create_dir_all(&journal_dir).unwrap();
    // cursor.bin: u64 LE = 42
    std::fs::write(journal_dir.join("cursor.bin"), 42u64.to_le_bytes()).unwrap();
    // consumed.log: 3 records (4B chain id LE + 8B nonce LE)
    let mut log = Vec::new();
    for (chain_id, nonce) in [(0xE000_0030u32, 1u64), (0xE000_0030, 2), (0xE000_0001, 99)] {
        log.extend_from_slice(&chain_id.to_le_bytes());
        log.extend_from_slice(&nonce.to_le_bytes());
    }
    std::fs::write(journal_dir.join("consumed.log"), &log).unwrap();

    let cfg_path = tmp.path().join("watcher.toml");
    let toml = format!(
        r#"
external_chain_id   = 0xE0000030
eth_rpc_url         = "http://127.0.0.1:1"
eth_router_address  = "0x0000000000000000000000000000000000000001"
neo_rpc_url         = "http://127.0.0.1:1"
neo_escrow_address  = "0x0000000000000000000000000000000000000001"
neo_signer_address  = "0x0000000000000000000000000000000000000001"
signer_key_path     = "{}"
journal_dir         = "{}"
"#,
        toml_path(&key_path),
        toml_path(&journal_dir)
    );
    std::fs::write(&cfg_path, toml).unwrap();

    let (code, output) = run_with_args(&["--config", cfg_path.to_str().unwrap(), "--journal-info"]);
    assert_eq!(code, 0, "--journal-info must exit 0; output:\n{output}");

    // Cursor printed.
    assert!(
        output.contains("cursor:       42"),
        "expected cursor=42 in output:\n{output}"
    );
    // Per-chain breakdown — both chain ids appear with their counts.
    assert!(
        output.contains("0xE0000030 (BNB Smart Chain mainnet)") && output.contains("→  2"),
        "expected BSC summary line:\n{output}"
    );
    assert!(
        output.contains("0xE0000001 (Ethereum mainnet)") && output.contains("→  1"),
        "expected Eth mainnet summary line:\n{output}"
    );
    // Recent records section.
    assert!(
        output.contains("recent (last 3 records):"),
        "expected recent records header:\n{output}"
    );
    assert!(
        output.contains("nonce=99"),
        "expected nonce=99 in recent:\n{output}"
    );
}

#[test]
fn unknown_flag_surfaces_error_with_valid_list() {
    let (code, output) = run_with_args(&["--bogus-flag"]);
    assert_eq!(code, 1, "unknown flag must exit 1");
    assert!(
        output.contains("--bogus-flag") && output.contains("--journal-info"),
        "error should name the bad flag + list valid flags:\n{output}"
    );
}
