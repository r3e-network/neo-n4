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
//! ```

use neo_bridge_watcher_eth::live::{
    EthRpcEventSource, EthRpcEventSourceBuilder, FileJournal, NeoRpcError,
    NeoRpcSubmitter, NeoRpcSubmitterBuilder,
};
use neo_bridge_watcher_eth::{CoreError, FileSigner, WatcherCore};
use serde::Deserialize;
use std::path::PathBuf;
use std::process::ExitCode;
use std::time::Duration;

fn main() -> ExitCode {
    let args: Vec<String> = std::env::args().collect();
    let config_path = match parse_args(&args) {
        Ok(p) => p,
        Err(msg) => {
            eprintln!("{msg}");
            print_usage();
            return ExitCode::from(1);
        }
    };

    let config = match load_config(&config_path) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("config error: {e}");
            return ExitCode::from(1);
        }
    };

    eprintln!(
        "neo-bridge-watcher-eth starting:\n  externalChainId = 0x{:08X}\n  ethRouter        = 0x{}\n  neoEscrow        = 0x{}\n  signer           = 0x{}\n  journalDir       = {}",
        config.external_chain_id,
        hex::encode(config.eth_router_address),
        hex::encode(config.neo_escrow_address),
        hex::encode(config.neo_signer_address),
        config.journal_dir.display(),
    );

    match run(config) {
        Ok(()) => ExitCode::SUCCESS,
        Err(e) => {
            eprintln!("fatal: {e}");
            ExitCode::from(1)
        }
    }
}

fn parse_args(args: &[String]) -> Result<PathBuf, String> {
    if args.len() == 2 && args[1] == "--help" || args.len() == 2 && args[1] == "-h" {
        print_usage();
        std::process::exit(0);
    }
    if args.len() != 3 || args[1] != "--config" {
        return Err(format!(
            "expected `--config <path>`, got: {}",
            args.iter().skip(1).cloned().collect::<Vec<_>>().join(" ")
        ));
    }
    Ok(PathBuf::from(&args[2]))
}

fn print_usage() {
    eprintln!(
        "Usage: neo-bridge-watcher-eth --config <watcher.toml>\n\
         \n\
         See the source for the full TOML schema. Required fields:\n\
         external_chain_id, eth_rpc_url, eth_router_address,\n\
         neo_rpc_url, neo_escrow_address, neo_signer_address,\n\
         signer_key_path, journal_dir\n\
         \n\
         Optional [poll] section: poll_interval_secs, backoff_initial_secs,\n\
         backoff_max_secs, eth_chunk_size, request_timeout_secs"
    );
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
}

#[derive(Deserialize, Default)]
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
}

fn default_poll_interval() -> u64 { 12 }
fn default_backoff_initial() -> u64 { 5 }
fn default_backoff_max() -> u64 { 300 }
fn default_eth_chunk_size() -> u64 { 5_000 }
fn default_request_timeout() -> u64 { 30 }

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
    let text = std::fs::read_to_string(path).map_err(|e| format!("read {}: {e}", path.display()))?;
    toml::from_str(&text).map_err(|e| format!("parse {}: {e}", path.display()))
}

fn run(config: Config) -> Result<(), String> {
    let signer = FileSigner::from_file(&config.signer_key_path)
        .map_err(|e| format!("load signer key: {e:?}"))?;
    let event_source = EthRpcEventSourceBuilder::new(
        config.eth_rpc_url.clone(),
        config.eth_router_address,
    )
    .chunk_size(config.poll.eth_chunk_size)
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

    let journal = FileJournal::open(&config.journal_dir)
        .map_err(|e| format!("open FileJournal: {e:?}"))?;

    let mut core = WatcherCore::new(
        config.external_chain_id,
        signer,
        event_source,
        submitter,
        journal,
    );

    eprintln!(
        "watcher ready (poll {}s, backoff {}-{}s)",
        config.poll.poll_interval_secs,
        config.poll.backoff_initial_secs,
        config.poll.backoff_max_secs,
    );

    let mut backoff_secs = config.poll.backoff_initial_secs;
    loop {
        match core.tick() {
            Ok(true) => {
                // Processed an event — reset backoff, immediately try
                // again to drain the queue.
                backoff_secs = config.poll.backoff_initial_secs;
                continue;
            }
            Ok(false) => {
                // No new events — sleep one poll interval.
                backoff_secs = config.poll.backoff_initial_secs;
                std::thread::sleep(Duration::from_secs(config.poll.poll_interval_secs));
            }
            Err(CoreError::AlreadySubmitted(nonce)) => {
                // Local journal already had this nonce — usually means
                // the daemon is replaying after a restart. Log + drop;
                // continue without sleeping.
                eprintln!("info: nonce {nonce} already submitted (journal hit, advancing)");
            }
            Err(CoreError::Submit(submit_err)) => {
                eprintln!("warn: submit failed: {submit_err:?} — backing off {backoff_secs}s");
                std::thread::sleep(Duration::from_secs(backoff_secs));
                backoff_secs = (backoff_secs * 2).min(config.poll.backoff_max_secs);
            }
            Err(CoreError::EventSource(es_err)) => {
                eprintln!("warn: event source: {es_err:?} — backing off {backoff_secs}s");
                std::thread::sleep(Duration::from_secs(backoff_secs));
                backoff_secs = (backoff_secs * 2).min(config.poll.backoff_max_secs);
            }
            Err(other) => {
                // Build / Sign / Proof / Journal errors are typically
                // unrecoverable — the message format or local state is
                // wrong. Log loudly but DON'T crash; retrying gives a
                // human time to fix the config without losing the
                // daemon process.
                eprintln!("error: unrecoverable-looking: {other:?} — backing off {backoff_secs}s");
                std::thread::sleep(Duration::from_secs(backoff_secs));
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
