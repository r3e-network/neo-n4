//! `neo-bridge-watcher-eth` — runnable v0 daemon binary.
//!
//! Wires the four live trait impls together — `FileSigner`,
//! `EthRpcEventSource`, `NeoRpcSubmitter`, `FileJournal` — into a
//! continuous loop that:
//!
//! 1. Polls the Eth router's `Locked` events from the journal cursor.
//! 2. For each event: builds the canonical `ExternalCrossChainMessage`,
//!    signs with the watcher's secp256k1 key, encodes the proof bytes,
//!    pre-checks via Neo `invokefunction`, hands the script bytes to
//!    the operator-supplied sign-and-send callback for actual
//!    submission.
//! 3. Journals the cursor + nonce only on success — a transient RPC
//!    error keeps both unchanged so the next iteration retries.
//! 4. Backs off on errors; sleeps between idle ticks.
//!
//! Usage:
//!
//! ```bash
//! cargo build --release -p neo-bridge-watcher-eth --features live-rpc
//! ./target/release/neo-bridge-watcher-eth --config watcher.toml
//! ```
//!
//! `watcher.toml` shape (all required):
//!
//! ```toml
//! external_chain_id   = 0xE0000002             # Sepolia
//! eth_rpc_url         = "https://rpc.sepolia.org"
//! eth_router_address  = "0xabcdef..."          # 20-byte hex
//! neo_rpc_url         = "https://rpc.testnet.neo.org"
//! neo_escrow_address  = "0x1111..."            # 20-byte LE hex
//! neo_signer_address  = "0xcafe..."            # 20-byte LE hex
//! signer_key_path     = "watcher.priv"         # 32-byte raw secp256k1 priv
//! journal_dir         = "./journal"
//!
//! [poll]
//! poll_interval_secs    = 12                   # ~Eth block time
//! backoff_initial_secs  = 5
//! backoff_max_secs      = 300
//! eth_chunk_size        = 5000                 # eth_getLogs chunk
//! request_timeout_secs  = 30
//! min_confirmations     = 12                   # Eth: 12 = ~99.9% finality
//! ```

use neo_bridge_watcher_eth::chains;
use neo_bridge_watcher_eth::live::{
    EthRpcEventSourceBuilder, FileJournal, HealthServer, HealthState, NeoRpcError,
    NeoRpcSubmitterBuilder,
};
use neo_bridge_watcher_eth::{CoreError, FileSigner, Journal, Signer, WatcherCore};
use serde::Deserialize;
use std::path::PathBuf;
use std::process::ExitCode;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::{Duration, Instant};

/// Shutdown flag flipped by SIGTERM / SIGINT handlers. The run loop
/// polls this between ticks + during interruptible sleeps so the
/// daemon can exit cleanly within ~100ms of a kill signal — important
/// for kubernetes / systemd graceful-shutdown windows (typically 30s
/// before SIGKILL escalation).
static SHUTDOWN_REQUESTED: AtomicBool = AtomicBool::new(false);

#[cfg(unix)]
extern "C" fn handle_shutdown_signal(_: libc::c_int) {
    // Signal handlers must be async-signal-safe. AtomicBool::store with
    // Relaxed ordering is — it compiles to a single memory write on
    // every platform we target. NO eprintln / String / heap allocations
    // allowed here. The run loop notices the flag on its next poll.
    SHUTDOWN_REQUESTED.store(true, Ordering::Relaxed);
}

/// Install signal handlers for SIGTERM (kubernetes / systemd shutdown)
/// and SIGINT (Ctrl-C). Both flip `SHUTDOWN_REQUESTED`. Idempotent —
/// re-registering would just no-op (libc::signal returns the previous
/// handler).
#[cfg(unix)]
fn install_shutdown_handlers() {
    // Form a function-pointer first then cast to libc's `sighandler_t`
    // (which is just `usize`). The two-step keeps the new "direct cast
    // of function item into an integer" lint quiet on Rust 2024+.
    let handler: extern "C" fn(libc::c_int) = handle_shutdown_signal;
    // SAFETY: libc::signal mutates a process-global table. Safe because
    // we only call it once at startup before any concurrent code runs;
    // the handler we install is async-signal-safe.
    unsafe {
        libc::signal(libc::SIGTERM, handler as libc::sighandler_t);
        libc::signal(libc::SIGINT, handler as libc::sighandler_t);
    }
}

/// Sleep for `duration`, but check the shutdown flag every 100ms and
/// return early (true) if shutdown is requested. Lets the daemon
/// respond to SIGTERM within ~100ms even if it was mid-poll-interval.
fn interruptible_sleep(duration: Duration) -> bool {
    let end = Instant::now() + duration;
    while Instant::now() < end {
        if SHUTDOWN_REQUESTED.load(Ordering::Relaxed) {
            return true;
        }
        let remaining = end.saturating_duration_since(Instant::now());
        std::thread::sleep(remaining.min(Duration::from_millis(100)));
    }
    SHUTDOWN_REQUESTED.load(Ordering::Relaxed)
}

fn main() -> ExitCode {
    #[cfg(unix)]
    install_shutdown_handlers();

    let args: Vec<String> = std::env::args().collect();
    let parsed = match parse_args(&args) {
        Ok(p) => p,
        Err(msg) => {
            eprintln!("{msg}");
            print_usage();
            return ExitCode::from(1);
        }
    };

    let config = match load_config(&parsed.config_path) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("config error: {e}");
            return ExitCode::from(1);
        }
    };

    if parsed.preflight {
        return match preflight(&config) {
            Ok(()) => {
                eprintln!("preflight: all checks passed");
                ExitCode::SUCCESS
            }
            Err(e) => {
                eprintln!("preflight: FAILED — {e}");
                ExitCode::from(1)
            }
        };
    }

    if parsed.journal_info {
        return match journal_info(&config) {
            Ok(()) => ExitCode::SUCCESS,
            Err(e) => {
                eprintln!("journal-info: {e}");
                ExitCode::from(1)
            }
        };
    }

    let chain_label = chains::name_for_chain_id(config.external_chain_id)
        .unwrap_or("(unknown chain — operator-allocated)");
    eprintln!(
        "neo-bridge-watcher-eth starting:\n  externalChainId = 0x{:08X} ({})\n  ethRouter        = 0x{}\n  neoEscrow        = 0x{}\n  signer           = 0x{}\n  journalDir       = {}",
        config.external_chain_id,
        chain_label,
        hex::encode(config.eth_router_address),
        hex::encode(config.neo_escrow_address),
        hex::encode(config.neo_signer_address),
        config.journal_dir.display(),
    );

    // Confirmation-buffer sanity: if the operator left min_confirmations
    // at 0 but this chain's recommendation is non-zero, emit a warning
    // surfacing the recommended value. Don't fail — the operator may
    // have a deliberate reason (testnet, L2 with separate L1 finality
    // signal); just point them at the right value if they didn't.
    if config.poll.min_confirmations == 0 {
        if let Some(recommended) = chains::recommended_confirmations(config.external_chain_id) {
            if recommended > 0 {
                eprintln!(
                    "WARNING: min_confirmations is 0 but chain 0x{:08X} ({}) \
                     recommends {} — short reorgs could produce phantom mints. \
                     Set [poll].min_confirmations in your TOML to silence \
                     this warning (and set explicitly to 0 if you mean \
                     no buffer).",
                    config.external_chain_id, chain_label, recommended
                );
            }
        } else {
            eprintln!(
                "WARNING: chain 0x{:08X} is not in the curated chains.rs table \
                 — verify your finality assumptions before production use.",
                config.external_chain_id
            );
        }
    }

    match run(config) {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("fatal: {e}");
            ExitCode::from(1)
        }
    }
}

struct ParsedArgs {
    config_path: PathBuf,
    preflight: bool,
    journal_info: bool,
}

fn parse_args(args: &[String]) -> Result<ParsedArgs, String> {
    if args.len() == 2 && (args[1] == "--help" || args[1] == "-h") {
        print_usage();
        std::process::exit(0);
    }
    if args.len() == 2 && (args[1] == "--version" || args[1] == "-V") {
        // CARGO_PKG_VERSION is baked in at compile time from
        // Cargo.toml's version field. Operators script around the
        // exact text — keep it stable: `<bin> <version>`.
        println!("{} {}", env!("CARGO_PKG_NAME"), env!("CARGO_PKG_VERSION"));
        std::process::exit(0);
    }
    if args.len() == 2 && args[1] == "--config-template" {
        // Print a fully-commented starter TOML to stdout. Operators:
        //   neo-bridge-watcher-eth --config-template > watcher.toml
        // Then edit the placeholders + run --preflight.
        print!("{}", config_template_text());
        std::process::exit(0);
    }

    // Accept `--config <path>` and optional flags in any position.
    // Reject any other tokens to surface typos loudly.
    let mut config_path: Option<PathBuf> = None;
    let mut preflight = false;
    let mut journal_info = false;
    let mut i = 1;
    while i < args.len() {
        match args[i].as_str() {
            "--config" => {
                if i + 1 >= args.len() {
                    return Err("expected a path after --config".into());
                }
                config_path = Some(PathBuf::from(&args[i + 1]));
                i += 2;
            }
            "--preflight" => {
                preflight = true;
                i += 1;
            }
            "--journal-info" => {
                journal_info = true;
                i += 1;
            }
            other => {
                return Err(format!(
                    "unexpected argument `{other}` (valid: --config <path>, --preflight, --journal-info, --config-template, --help, --version)"
                ));
            }
        }
    }
    let Some(config_path) = config_path else {
        return Err("expected `--config <path>` (or --help / --version)".into());
    };
    Ok(ParsedArgs {
        config_path,
        preflight,
        journal_info,
    })
}

fn print_usage() {
    eprintln!(
        "Usage: neo-bridge-watcher-eth --config <watcher.toml> [--preflight]\n\
         \n\
         Flags:\n\
           --config <path>     Path to TOML config (required for normal runs).\n\
           --preflight         Validate config + RPC reachability + signer + journal,\n\
                               then exit. Does NOT start the watch loop.\n\
           --journal-info      Print the journal's cursor + consumed-record\n\
                               summary + recent records, then exit. Read-only;\n\
                               does NOT acquire the journal flock (safe to run\n\
                               while the watcher daemon is also running).\n\
           --config-template   Print a starter TOML config to stdout + exit.\n\
                               Pipe to a file: `... --config-template > watcher.toml`\n\
                               then edit placeholders + run --preflight.\n\
           --version, -V       Print version + exit.\n\
           --help, -h          Print this help + exit.\n\
         \n\
         See the source for the full TOML schema. Required fields:\n\
         external_chain_id, eth_rpc_url, eth_router_address,\n\
         neo_rpc_url, neo_escrow_address, neo_signer_address,\n\
         signer_key_path, journal_dir\n\
         \n\
         Optional [poll] section: poll_interval_secs, backoff_initial_secs,\n\
         backoff_max_secs, eth_chunk_size, request_timeout_secs,\n\
         min_confirmations, start_block\n\
         \n\
         Optional [health] section: bind, threshold_secs"
    );
}

/// Starter TOML config emitted by `--config-template`. Inline so it
/// stays in sync with `Config` + `PollConfig` + `HealthConfig` structs
/// — adding a field here without updating those structs (or vice
/// versa) surfaces at runtime preflight, not at silent fall-through.
fn config_template_text() -> &'static str {
    r#"# neo-bridge-watcher-eth — starter config
# ────────────────────────────────────────
# Generated by `neo-bridge-watcher-eth --config-template`.
# Edit the placeholders below + run `--preflight` to validate.
#
# Required fields:

# Foreign chain id from `watchers/neo-bridge-watcher-eth/src/chains.rs`.
# Examples: 0xE0000001 (Eth mainnet), 0xE0000002 (Sepolia),
# 0xE0000030 (BSC mainnet), 0xE0000040 (Polygon mainnet), etc.
external_chain_id   = 0xE0000002

# JSON-RPC endpoint for the foreign chain.
eth_rpc_url         = "https://rpc.sepolia.org"

# 20-byte address of the deployed NeoExternalBridgeRouter on the foreign chain.
eth_router_address  = "0xREPLACE_WITH_DEPLOYED_ROUTER_ADDR"

# Neo RPC endpoint.
neo_rpc_url         = "https://rpc.testnet.neo.org"

# 20-byte address of NeoHub.ExternalBridgeEscrow on Neo.
neo_escrow_address  = "0xREPLACE_WITH_NEOHUB_ESCROW_ADDR"

# 20-byte address of the watcher's Neo account (signs the inbound submissions).
neo_signer_address  = "0xREPLACE_WITH_WATCHER_NEO_ACCOUNT"

# Path to the watcher's 32-byte secp256k1 private key file (raw bytes, mode 0600).
# Generate one via: `dotnet run --project tools/Neo.External.Bridge.Cli -- genkey --out watcher.priv`
signer_key_path     = "./watcher.priv"

# Directory for the journal (cursor.bin + consumed.log + .lock).
# Survives restart; one watcher per directory (flock-protected).
journal_dir         = "./journal"

# ─── Optional polling tuning ───
[poll]
poll_interval_secs    = 12      # Block time of the foreign chain (Eth ~12s, BSC ~3s)
backoff_initial_secs  = 5       # First backoff on transient errors
backoff_max_secs      = 300     # Backoff ceiling (exp doubling up to this)
eth_chunk_size        = 5000    # `eth_getLogs` block-range cap (free-tier RPCs cap at 10k)
request_timeout_secs  = 30      # Per-request HTTP timeout

# Block-finality buffer. Defends against short-reorg phantom mints.
# See chains.rs::recommended_confirmations for per-chain guidance:
#   Eth mainnet: 12 (or 32 for Casper-finalized)
#   BSC: 15 · Polygon PoS: 256 · Avalanche: 1 · Tron: 19
#   Arbitrum/Optimism/Base/Linea: 0 (operator pairs with L1 batch finality signal)
#   Testnets: smaller (5 for Eth testnets, 1 for most L2 testnets)
min_confirmations     = 5

# Optional first-run cursor bootstrap. When the journal cursor is below this,
# the daemon advances to start_block at startup. Skip if you want to scan from
# genesis. Monotonic — restarts read the journal cursor as normal.
# start_block         = 38_400_000

# ─── Optional health + metrics endpoint ───
# Required for k8s readiness/liveness probes + Prometheus scraping.
# Unset = no health server (one-off CLI runs).
[health]
bind                  = "0.0.0.0:9090"   # Listen address; ClusterIP / private
threshold_secs        = 120              # /healthz returns 503 after this many
                                          # seconds without a successful tick.
"#
}

/// Validate the operator's deployment without starting the watch loop.
///
/// Walks each external dependency in order — config / signer / journal /
/// Eth RPC / Neo RPC — and prints `[ok]` or `[FAIL]` per check. Aborts
/// on the first hard failure (Err return); soft warnings (e.g.
/// missing chains.rs entry, low min_confirmations) print but don't
/// fail. Designed for `kubectl apply` / systemd ExecStartPre / CI
/// gate flows: exit 0 = safe to start, non-zero = config issue.
fn preflight(config: &Config) -> Result<(), String> {
    eprintln!(
        "preflight: starting checks for chain 0x{:08X}",
        config.external_chain_id
    );

    // 1. external_chain_id namespace + chain table.
    if config.external_chain_id & 0xFF00_0000 != 0xE000_0000 {
        return Err(format!(
            "external_chain_id 0x{:08X} not in 0xE0_xx_xx_xx namespace",
            config.external_chain_id
        ));
    }
    match chains::name_for_chain_id(config.external_chain_id) {
        Some(name) => eprintln!(
            "[ok]   chain id 0x{:08X} ({name})",
            config.external_chain_id
        ),
        None => eprintln!(
            "[warn] chain id 0x{:08X} not in curated chains.rs table — verify finality assumptions",
            config.external_chain_id
        ),
    }

    // 2. All-zero address guards. A typo'd `eth_router_address =
    //    "0x0000..."` deserializes successfully (it's a valid 20-byte
    //    value) but every locked event would route to the zero
    //    address, silently corrupting the bridge. Catching this at
    //    preflight is much cheaper than catching it after deploy.
    if config.eth_router_address == [0u8; 20] {
        return Err("eth_router_address is the zero address — likely a config typo".into());
    }
    if config.neo_escrow_address == [0u8; 20] {
        return Err("neo_escrow_address is the zero address — likely a config typo".into());
    }
    if config.neo_signer_address == [0u8; 20] {
        return Err("neo_signer_address is the zero address — likely a config typo".into());
    }
    eprintln!(
        "[ok]   eth_router_address  = 0x{}",
        hex::encode(config.eth_router_address)
    );
    eprintln!(
        "[ok]   neo_escrow_address  = 0x{}",
        hex::encode(config.neo_escrow_address)
    );
    eprintln!(
        "[ok]   neo_signer_address  = 0x{}",
        hex::encode(config.neo_signer_address)
    );

    // 3. Confirmation buffer guidance.
    if config.poll.min_confirmations == 0 {
        if let Some(rec) = chains::recommended_confirmations(config.external_chain_id) {
            if rec > 0 {
                eprintln!(
                    "[warn] [poll].min_confirmations = 0 but recommended {rec} for this chain"
                );
            } else {
                eprintln!("[ok]   min_confirmations = 0 (recommendation matches)");
            }
        }
    } else {
        eprintln!(
            "[ok]   min_confirmations = {}",
            config.poll.min_confirmations
        );
    }

    // 3. Signer key file.
    let signer = FileSigner::from_file(&config.signer_key_path)
        .map_err(|e| format!("signer key {}: {e:?}", config.signer_key_path.display()))?;
    eprintln!(
        "[ok]   signer key loaded from {} ({} bytes pubkey)",
        config.signer_key_path.display(),
        signer.public_key_bytes().len()
    );

    // 4. Journal dir creatable + flock-able. We acquire + drop in this
    //    scope; the actual run() call later re-opens. Avoids locking
    //    the dir for the rest of the preflight invocation.
    {
        let _journal = FileJournal::open(&config.journal_dir)
            .map_err(|e| format!("journal {}: {e:?}", config.journal_dir.display()))?;
        eprintln!(
            "[ok]   journal_dir {} opened (flock acquired + released)",
            config.journal_dir.display()
        );
    }

    // 5. Eth RPC reachability — build the source + actually probe
    //    `eth_blockNumber`. Validates URL + DNS + TLS + the endpoint
    //    speaking the JSON-RPC dialect we expect, all in one round
    //    trip. Uses a tighter timeout (5s) than the run loop's
    //    request_timeout — preflight is a quick sanity check, not
    //    a long-running poll.
    let preflight_timeout = Duration::from_secs(5);
    let source =
        EthRpcEventSourceBuilder::new(config.eth_rpc_url.clone(), config.eth_router_address)
            .request_timeout(preflight_timeout)
            .build()
            .map_err(|e| format!("build EthRpcEventSource: {e:?}"))?;
    let head = source
        .fetch_block_number()
        .map_err(|e| format!("eth_blockNumber on {}: {e:?}", config.eth_rpc_url))?;
    eprintln!(
        "[ok]   eth_rpc_url {} responsive (head = {head})",
        config.eth_rpc_url
    );

    // 5b. eth_getCode on the router address. Catches operators
    //     passing an EOA / wrong proxy / typo'd contract address —
    //     all of which would silently fail to emit Locked events
    //     forever. A successful contract returns >= 1 byte of code.
    probe_eth_get_code(
        &config.eth_rpc_url,
        config.eth_router_address,
        preflight_timeout,
    )
    .map_err(|e| {
        format!(
            "eth_getCode on router {}: {e}",
            hex::encode(config.eth_router_address)
        )
    })?;
    eprintln!("[ok]   eth_router_address has bytecode (eth_getCode returned > 0 bytes)");

    // 6. Neo RPC reachability — direct reqwest probe of `getversion`,
    //    which every Neo node implements. Avoids needing a public
    //    head-probe on NeoRpcSubmitter (which is structured around
    //    invokefunction, not raw queries).
    probe_neo_rpc(&config.neo_rpc_url, preflight_timeout)
        .map_err(|e| format!("getversion on {}: {e}", config.neo_rpc_url))?;
    eprintln!(
        "[ok]   neo_rpc_url {} responsive (getversion succeeded)",
        config.neo_rpc_url
    );

    Ok(())
}

/// Read-only journal inspection. Does NOT acquire the flock — safe to run
/// while the watcher daemon is also running. Reads cursor.bin + consumed.log
/// directly + prints a summary.
fn journal_info(config: &Config) -> Result<(), String> {
    let dir = &config.journal_dir;
    if !dir.exists() {
        return Err(format!("journal_dir {} does not exist", dir.display()));
    }

    // Read cursor.bin (8-byte LE u64). If absent, journal hasn't been
    // written to yet — cursor is implicitly 0.
    let cursor_path = dir.join("cursor.bin");
    let cursor: u64 = if cursor_path.exists() {
        let bytes = std::fs::read(&cursor_path).map_err(|e| format!("read cursor.bin: {e}"))?;
        if bytes.len() != 8 {
            return Err(format!(
                "cursor.bin is {} bytes, expected 8 — journal corrupted",
                bytes.len()
            ));
        }
        let mut cursor_bytes = [0u8; 8];
        cursor_bytes.copy_from_slice(&bytes);
        u64::from_le_bytes(cursor_bytes)
    } else {
        0
    };

    // Read consumed.log (12-byte records: 4B chainId LE + 8B nonce LE).
    // Truncated trailing records are dropped (replay-safe; matches what
    // FileJournal::open does on startup).
    let consumed_path = dir.join("consumed.log");
    let consumed_bytes = if consumed_path.exists() {
        std::fs::read(&consumed_path).map_err(|e| format!("read consumed.log: {e}"))?
    } else {
        Vec::new()
    };
    let record_count = consumed_bytes.len() / 12;
    let truncated = consumed_bytes.len() % 12 != 0;

    // Group counts by chain id for the summary.
    use std::collections::BTreeMap;
    let mut per_chain: BTreeMap<u32, u64> = BTreeMap::new();
    for chunk in consumed_bytes.chunks_exact(12) {
        let chain_id = u32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]);
        *per_chain.entry(chain_id).or_insert(0) += 1;
    }

    println!("journal_dir:  {}", dir.display());
    println!("cursor:       {cursor} (block height)");
    println!(
        "consumed:     {record_count} records{}",
        if truncated {
            " (+ trailing partial record dropped on next reopen)"
        } else {
            ""
        }
    );
    if !per_chain.is_empty() {
        println!("by chain:");
        for (chain_id, count) in &per_chain {
            let label = chains::name_for_chain_id(*chain_id).unwrap_or("(unknown)");
            println!("  0x{chain_id:08X} ({label})  →  {count}");
        }
    }

    // Print last 5 records for quick visual sanity-check.
    let total = consumed_bytes.len() / 12;
    if total > 0 {
        let preview = total.min(5);
        let start = (total - preview) * 12;
        println!("recent (last {preview} records):");
        for chunk in consumed_bytes[start..].chunks_exact(12) {
            let chain_id = u32::from_le_bytes([chunk[0], chunk[1], chunk[2], chunk[3]]);
            let nonce = u64::from_le_bytes([
                chunk[4], chunk[5], chunk[6], chunk[7], chunk[8], chunk[9], chunk[10], chunk[11],
            ]);
            println!("  chain=0x{chain_id:08X}  nonce={nonce}");
        }
    }

    Ok(())
}

/// `eth_getCode` probe for the router address. Returns Ok(()) iff
/// the response is a hex string of at least 1 byte (i.e., NOT "0x"
/// alone). "0x" with no bytecode means the address is either an EOA
/// or non-existent — in either case the watcher would never see
/// Locked events from it, so we hard-fail at preflight.
fn probe_eth_get_code(rpc_url: &str, address: [u8; 20], timeout: Duration) -> Result<(), String> {
    let client = reqwest::blocking::Client::builder()
        .timeout(timeout)
        .build()
        .map_err(|e| format!("build client: {e}"))?;
    let req = serde_json::json!({
        "jsonrpc": "2.0",
        "id": 1,
        "method": "eth_getCode",
        "params": [format!("0x{}", hex::encode(address)), "latest"]
    });
    let resp: serde_json::Value = client
        .post(rpc_url)
        .json(&req)
        .send()
        .map_err(|e| format!("send: {e}"))?
        .json()
        .map_err(|e| format!("parse JSON-RPC response: {e}"))?;
    if let Some(err) = resp.get("error") {
        return Err(format!("rpc error: {err}"));
    }
    let code = resp
        .get("result")
        .and_then(|v| v.as_str())
        .ok_or_else(|| "response missing string `result`".to_string())?;
    let stripped = code.strip_prefix("0x").unwrap_or(code);
    if stripped.is_empty() {
        return Err(format!(
            "address has no bytecode — either an EOA or non-existent. \
             Did you pass the right router address? (got '{code}')"
        ));
    }
    Ok(())
}

/// Lightweight probe: POST `getversion` to a Neo JSON-RPC node. Returns
/// Ok(()) iff the response is a valid JSON-RPC reply with no `error`
/// field. We don't care what's in `result` — just that the endpoint
/// is reachable + speaks the dialect.
fn probe_neo_rpc(rpc_url: &str, timeout: Duration) -> Result<(), String> {
    let client = reqwest::blocking::Client::builder()
        .timeout(timeout)
        .build()
        .map_err(|e| format!("build client: {e}"))?;
    let req = serde_json::json!({
        "jsonrpc": "2.0",
        "id": 1,
        "method": "getversion",
        "params": []
    });
    let resp: serde_json::Value = client
        .post(rpc_url)
        .json(&req)
        .send()
        .map_err(|e| format!("send: {e}"))?
        .json()
        .map_err(|e| format!("parse JSON-RPC response: {e}"))?;
    if let Some(err) = resp.get("error") {
        return Err(format!("rpc error: {err}"));
    }
    if resp.get("result").is_none() {
        return Err("response has no `result` and no `error`".into());
    }
    Ok(())
}

#[derive(Deserialize)]
struct Config {
    external_chain_id: u32,
    eth_rpc_url: String,
    #[serde(deserialize_with = "deserialize_addr20")]
    eth_router_address: [u8; 20],
    neo_rpc_url: String,
    #[serde(deserialize_with = "deserialize_addr20")]
    neo_escrow_address: [u8; 20],
    #[serde(deserialize_with = "deserialize_addr20")]
    neo_signer_address: [u8; 20],
    signer_key_path: PathBuf,
    journal_dir: PathBuf,
    #[serde(default)]
    poll: PollConfig,
    #[serde(default)]
    health: HealthConfig,
}

/// Optional health-check HTTP endpoint config. When `bind` is unset
/// the daemon runs without a health server (suitable for one-off
/// CLI runs); k8s/systemd deployments set `bind = "0.0.0.0:9090"`
/// and probe `/healthz` for readiness/liveness.
#[derive(Deserialize, Default)]
struct HealthConfig {
    /// Bind address (e.g. "0.0.0.0:9090" or "127.0.0.1:9090"). Unset
    /// = no health server.
    bind: Option<String>,
    /// Seconds without a successful tick before /healthz returns 503.
    /// Default 120 — covers a 12s poll interval + up to 60s backoff
    /// with margin for one full retry cycle.
    #[serde(default = "default_health_threshold")]
    threshold_secs: u64,
}

fn default_health_threshold() -> u64 {
    120
}

#[derive(Deserialize)]
struct PollConfig {
    #[serde(default = "default_poll_interval")]
    poll_interval_secs: u64,
    #[serde(default = "default_backoff_initial")]
    backoff_initial_secs: u64,
    #[serde(default = "default_backoff_max")]
    backoff_max_secs: u64,
    #[serde(default = "default_eth_chunk_size")]
    eth_chunk_size: u64,
    #[serde(default = "default_request_timeout")]
    request_timeout_secs: u64,
    /// Block-finality buffer. The watcher will not emit events from
    /// blocks less than this many confirmations deep — guards against
    /// short-reorg phantom mints. Per-chain guidance lives in
    /// `neo_bridge_watcher_eth::chains` doc + `min_confirmations`
    /// builder method docs. Default 0 (no buffer, testnet-only).
    /// Operators MUST set a chain-appropriate value for production.
    #[serde(default)]
    min_confirmations: u64,
    /// First-run cursor bootstrap. When the journal's cursor is
    /// strictly less than `start_block`, the daemon advances the
    /// cursor to `start_block` at startup — useful when deploying
    /// a watcher mid-stream against a chain that's been running for
    /// months (default behavior would re-scan from genesis, hammering
    /// the operator's RPC budget). Default 0 (start at genesis).
    ///
    /// Important: this advances the cursor MONOTONICALLY (only forward).
    /// It cannot rewind a journal that's already past `start_block`.
    /// To rewind, the operator manually clears the journal directory
    /// — opt-in destructive behavior, not a config knob.
    #[serde(default)]
    start_block: u64,
}

// Manual Default impl — `#[serde(default = "fn")]` only fires for fields
// that are present-but-unset INSIDE an existing [poll] table. When
// [poll] is omitted entirely, serde falls back to PollConfig::default()
// for the whole struct; #[derive(Default)] would zero every field
// (poll_interval=0 + backoff=0 = tight infinite spin). This impl
// matches the per-field defaults instead.
impl Default for PollConfig {
    fn default() -> Self {
        Self {
            poll_interval_secs: default_poll_interval(),
            backoff_initial_secs: default_backoff_initial(),
            backoff_max_secs: default_backoff_max(),
            eth_chunk_size: default_eth_chunk_size(),
            request_timeout_secs: default_request_timeout(),
            min_confirmations: 0,
            start_block: 0,
        }
    }
}

fn default_poll_interval() -> u64 {
    12
}
fn default_backoff_initial() -> u64 {
    5
}
fn default_backoff_max() -> u64 {
    300
}
fn default_eth_chunk_size() -> u64 {
    5_000
}
fn default_request_timeout() -> u64 {
    30
}

fn deserialize_addr20<'de, D: serde::Deserializer<'de>>(d: D) -> Result<[u8; 20], D::Error> {
    use serde::de::Error;
    let s: String = Deserialize::deserialize(d)?;
    let s = s.strip_prefix("0x").unwrap_or(&s);
    let bytes = hex::decode(s).map_err(D::Error::custom)?;
    if bytes.len() != 20 {
        return Err(D::Error::custom(format!(
            "address must be 20 bytes (got {})",
            bytes.len()
        )));
    }
    let mut out = [0u8; 20];
    out.copy_from_slice(&bytes);
    Ok(out)
}

fn load_config(path: &PathBuf) -> Result<Config, String> {
    let text =
        std::fs::read_to_string(path).map_err(|e| format!("read {}: {e}", path.display()))?;
    toml::from_str(&text).map_err(|e| format!("parse {}: {e}", path.display()))
}

fn run(config: Config) -> Result<(), String> {
    let signer = FileSigner::from_file(&config.signer_key_path)
        .map_err(|e| format!("load signer key: {e:?}"))?;
    let event_source =
        EthRpcEventSourceBuilder::new(config.eth_rpc_url.clone(), config.eth_router_address)
            .chunk_size(config.poll.eth_chunk_size)
            .min_confirmations(config.poll.min_confirmations)
            .request_timeout(Duration::from_secs(config.poll.request_timeout_secs))
            .build()
            .map_err(|e| format!("build EthRpcEventSource: {e:?}"))?;

    // The sign-and-send callback. v0 emits a stable warning + returns a
    // synthetic tx hash derived from the script bytes — operators run
    // their own signed-tx submission against the script hex from
    // `invokefunction`. Production deployments replace this closure with
    // a real HSM/KMS-driven signer that POSTs `sendrawtransaction`.
    let sign_and_send = StubSignAndSend;

    let submitter = NeoRpcSubmitterBuilder::new(
        config.neo_rpc_url.clone(),
        config.neo_escrow_address,
        config.neo_signer_address,
        sign_and_send,
    )
    .request_timeout(Duration::from_secs(config.poll.request_timeout_secs))
    .build()
    .map_err(|e| format!("build NeoRpcSubmitter: {e:?}"))?;

    let mut journal =
        FileJournal::open(&config.journal_dir).map_err(|e| format!("open FileJournal: {e:?}"))?;

    // First-run cursor bootstrap: if start_block is set + the journal's
    // cursor is below it, advance. set_cursor is monotonic, so calling
    // it on a journal that's already past start_block is a safe no-op.
    if config.poll.start_block > 0 {
        let cur = journal
            .cursor()
            .map_err(|e| format!("read journal cursor: {e:?}"))?;
        if cur < config.poll.start_block {
            journal
                .set_cursor(config.poll.start_block)
                .map_err(|e| format!("bootstrap cursor: {e:?}"))?;
            eprintln!(
                "journal cursor bootstrapped to start_block = {} (was {})",
                config.poll.start_block, cur
            );
        }
    }

    let mut core = WatcherCore::new(
        config.external_chain_id,
        signer,
        event_source,
        submitter,
        journal,
    );

    // Health server: started before the loop so probes work during
    // startup. Optional — operators running one-off CLI without
    // kubernetes don't need it. `with_chain_id` tags every emitted
    // Prometheus metric with `{chain_id="0x..."}` so multi-chain
    // operator setups get cleanly disambiguated time series.
    let health_state = HealthState::with_chain_id(config.external_chain_id);
    let _health_server = if let Some(bind) = &config.health.bind {
        match HealthServer::spawn(bind, health_state.clone(), config.health.threshold_secs) {
            Ok(server) => {
                eprintln!(
                    "health server listening on http://{} (threshold {}s)",
                    server.bound_addr, config.health.threshold_secs
                );
                Some(server)
            }
            Err(e) => {
                return Err(format!("bind health server on {bind}: {e}"));
            }
        }
    } else {
        None
    };

    eprintln!(
        "watcher ready (poll {}s, backoff {}-{}s)",
        config.poll.poll_interval_secs,
        config.poll.backoff_initial_secs,
        config.poll.backoff_max_secs,
    );

    let mut backoff_secs = config.poll.backoff_initial_secs;
    loop {
        // Check shutdown at the top of every iteration. Any sleep below
        // returns early when SHUTDOWN_REQUESTED is set; control falls
        // through to the next iteration which exits cleanly here.
        if SHUTDOWN_REQUESTED.load(Ordering::Relaxed) {
            eprintln!("shutdown signal received — exiting cleanly");
            return Ok(());
        }
        match core.tick() {
            Ok(true) => {
                // Processed an event — reset backoff, immediately try
                // again to drain the queue.
                health_state.record_tick(true);
                health_state.record_submission();
                backoff_secs = config.poll.backoff_initial_secs;
                continue;
            }
            Ok(false) => {
                // No new events — sleep one poll interval.
                health_state.record_tick(false);
                backoff_secs = config.poll.backoff_initial_secs;
                interruptible_sleep(Duration::from_secs(config.poll.poll_interval_secs));
            }
            Err(CoreError::AlreadySubmitted(nonce)) => {
                // Local journal already had this nonce — usually means
                // the daemon is replaying after a restart. Log + drop;
                // continue without sleeping.
                eprintln!("info: nonce {nonce} already submitted (journal hit, advancing)");
                health_state.record_tick(false);
            }
            Err(CoreError::Submit(submit_err)) => {
                eprintln!("warn: submit failed: {submit_err:?} — backing off {backoff_secs}s");
                health_state.record_error(format!("submit: {submit_err:?}"));
                interruptible_sleep(Duration::from_secs(backoff_secs));
                backoff_secs = (backoff_secs * 2).min(config.poll.backoff_max_secs);
            }
            Err(CoreError::EventSource(es_err)) => {
                eprintln!("warn: event source: {es_err:?} — backing off {backoff_secs}s");
                health_state.record_error(format!("event_source: {es_err:?}"));
                interruptible_sleep(Duration::from_secs(backoff_secs));
                backoff_secs = (backoff_secs * 2).min(config.poll.backoff_max_secs);
            }
            Err(other) => {
                // Build / Sign / Proof / Journal errors are typically
                // unrecoverable — the message format or local state is
                // wrong. Log loudly but DON'T crash; retrying gives a
                // human time to fix the config without losing the
                // daemon process.
                eprintln!("error: unrecoverable-looking: {other:?} — backing off {backoff_secs}s");
                health_state.record_error(format!("{other:?}"));
                interruptible_sleep(Duration::from_secs(backoff_secs));
                backoff_secs = (backoff_secs * 2).min(config.poll.backoff_max_secs);
            }
        }
    }
}

/// v0 stub: emits a clear warning + returns a synthetic tx hash so the
/// watcher's journal advances, but does NOT actually sign + submit a
/// Neo transaction. Operators replace with a real HSM/KMS-backed signer
/// in production.
struct StubSignAndSend;

impl neo_bridge_watcher_eth::live::SignAndSend for StubSignAndSend {
    fn sign_and_send(&mut self, script: &[u8]) -> Result<[u8; 32], NeoRpcError> {
        eprintln!(
            "STUB: would submit Neo tx with script ({} bytes): 0x{}",
            script.len(),
            hex::encode(script),
        );
        eprintln!(
            "      Replace StubSignAndSend with an HSM/KMS-backed impl that wraps the script in a signed Neo Transaction + POSTs `sendrawtransaction`. The watcher journals as if submission succeeded so the cursor advances; an operator must externally confirm the tx landed on Neo before relying on the journal state."
        );
        // Synthetic tx hash: SHA256 of the script. Stable + identifiable
        // in operator logs; collisions are a non-issue for v0.
        use sha2::{Digest, Sha256};
        let mut hasher = Sha256::new();
        hasher.update(script);
        let digest = hasher.finalize();
        let mut out = [0u8; 32];
        out.copy_from_slice(&digest);
        Ok(out)
    }
}
