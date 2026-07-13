//! `prove-batch` — operator-facing CLI for the L2 prover.
//!
//! Three modes:
//! * One-shot execute: `prove-batch <hex>` — runs the guest in SP1's zkVM
//!   without proof generation. Fast, useful for development.
//! * One-shot prove:   `prove-batch --prove <hex> [--out proof.bin]` —
//!   generates a real ZK proof for a single batch and writes proof.bin
//!   + proof.vk + proof.public-values.bin to disk.
//! * Daemon:           `prove-batch daemon --watch <dir> [--archive <dir>]
//!                       [--poll-secs N]` — runs forever, watches `<dir>`
//!   for `*.batch.bin` files (each is a serialized BatchExecutionRequest),
//!   generates a proof for each, writes `<name>.proof.bin` +
//!   `<name>.proof.vk` + `<name>.proof.public-values.bin` next to the input,
//!   and renames the input to
//!   `<name>.batch.bin.done` so it's not re-processed. This is the
//!   recommended production deployment shape: a separate prover process
//!   that the sequencer drops sealed batches into via filesystem (or any
//!   other queue you wire up).

use std::ffi::OsStr;
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;
use tracing::{error, info, warn};

/// Set by the SIGTERM/SIGINT handler. The daemon loop checks this at the
/// top of every iteration and inside `interruptible_sleep` so a shutdown
/// signal lands cleanly even mid-poll. Proofs in progress are NOT interrupted
/// — they complete first (proof time is bounded), and then the loop exits.
static SHUTDOWN_REQUESTED: AtomicBool = AtomicBool::new(false);

#[cfg(unix)]
extern "C" fn shutdown_signal_handler(_sig: i32) {
    SHUTDOWN_REQUESTED.store(true, Ordering::Release);
}

#[cfg(unix)]
fn install_shutdown_signal_handlers() {
    // SAFETY: sigaction is the POSIX-standard signal API (more portable than
    // signal(), which has SysV-vs-BSD reset differences). The handler is
    // marked `extern "C"` and only touches an AtomicBool (signal-safe).
    // SA_RESTART ensures interrupted syscalls (e.g. read from the prover
    // pipe) are transparently retried rather than failing with EINTR.
    unsafe {
        let sa = libc::sigaction {
            sa_sigaction: shutdown_signal_handler as libc::sighandler_t,
            sa_flags: libc::SA_RESTART,
            sa_mask: std::mem::zeroed(),
        };
        libc::sigaction(libc::SIGTERM, &sa, std::ptr::null_mut());
        libc::sigaction(libc::SIGINT, &sa, std::ptr::null_mut());
    }
}

#[cfg(not(unix))]
fn install_shutdown_signal_handlers() {}

/// Sleep up to `dur`, but wake every 100ms to check for shutdown.
fn interruptible_sleep(dur: Duration) {
    let step = Duration::from_millis(100);
    let mut remaining = dur;
    while remaining > Duration::ZERO {
        if SHUTDOWN_REQUESTED.load(Ordering::Acquire) {
            return;
        }
        let slice = remaining.min(step);
        std::thread::sleep(slice);
        remaining = remaining.saturating_sub(slice);
    }
}

fn main() {
    // Initialize tracing subscriber for structured logging
    tracing_subscriber::fmt()
        .with_env_filter(
            tracing_subscriber::EnvFilter::try_from_default_env()
                .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info")),
        )
        .init();

    let args: Vec<String> = std::env::args().collect();
    if args.len() >= 2 && args[1] == "daemon" {
        run_daemon(&args[2..]);
        return;
    }
    run_oneshot(&args[1..]);
}

// ────────────── one-shot path ──────────────

fn run_oneshot(args: &[String]) {
    let mut prove_mode = false;
    let mut out_path: Option<String> = None;
    let mut positional: Option<String> = None;
    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--prove" => prove_mode = true,
            "--out" => {
                i += 1;
                if i >= args.len() {
                    error!("--out requires a path");
                    std::process::exit(1);
                }
                out_path = Some(args[i].clone());
            }
            "-h" | "--help" => {
                print_usage();
                return;
            }
            other => {
                if positional.is_some() {
                    error!("unexpected argument: {}", other);
                    std::process::exit(1);
                }
                positional = Some(other.to_string());
            }
        }
        i += 1;
    }
    let Some(hex) = positional else {
        print_usage();
        std::process::exit(1);
    };
    let bytes = match hex_decode(&hex) {
        Ok(b) => b,
        Err(e) => {
            error!("invalid hex: {}", e);
            std::process::exit(1);
        }
    };

    if prove_mode {
        let path = out_path.unwrap_or_else(|| "proof.bin".to_string());
        match prove_one(&bytes, Path::new(&path)) {
            Ok(written) => {
                info!(
                    "public_input_hash = 0x{}",
                    hex_encode(&written.public_input_hash)
                );
                info!("proof_bytes_len   = {}", written.proof_len);
                info!("vk_bytes_len      = {}", written.vk_len);
                info!("public_values_len = {}", written.public_values_len);
                info!("proof_path        = {}", written.proof_path.display());
                info!("vk_path           = {}", written.vk_path.display());
                info!(
                    "public_values_path= {}",
                    written.public_values_path.display()
                );
            }
            Err(e) => {
                error!("proof generation failed: {}", e);
                std::process::exit(1);
            }
        }
    } else {
        match neo_zkvm_host::execute(&bytes) {
            Ok(result) => {
                info!(
                    "public_input_hash = 0x{}",
                    hex_encode(&result.public_input_hash)
                );
                info!("cycles            = {}", result.cycles);
            }
            Err(e) => {
                error!("execution failed: {}", e);
                std::process::exit(1);
            }
        }
    }
}

// ────────────── daemon path ──────────────

struct DaemonConfig {
    watch: PathBuf,
    archive: Option<PathBuf>,
    poll_secs: u64,
}

fn run_daemon(args: &[String]) {
    let cfg = parse_daemon_args(args).unwrap_or_else(|e| {
        error!("{}", e);
        print_usage();
        std::process::exit(1);
    });

    if !cfg.watch.is_dir() {
        error!(
            "--watch dir does not exist or is not a directory: {}",
            cfg.watch.display()
        );
        std::process::exit(1);
    }
    if let Some(a) = &cfg.archive {
        std::fs::create_dir_all(a).unwrap_or_else(|e| {
            error!("failed to create --archive dir {}: {}", a.display(), e);
            std::process::exit(1);
        });
    }

    info!(
        "prove-batch daemon: watching {} (poll every {}s, archive: {})",
        cfg.watch.display(),
        cfg.poll_secs,
        cfg.archive
            .as_ref()
            .map(|p| p.display().to_string())
            .unwrap_or_else(|| "<rename in place>".to_string())
    );

    // Install SIGTERM/SIGINT handlers so a k8s/systemd shutdown lands
    // cleanly. An in-flight proof completes first (sp1 prove is bounded
    // wall-time, no interrupt point inside it); the next loop iteration
    // sees the flag and exits.
    install_shutdown_signal_handlers();

    // Single-instance guard: acquire an exclusive advisory flock on a sentinel
    // file inside --watch. Without this, two prove-batch daemons pointed at the
    // same dir would both pick up the same *.batch.bin and double-prove (the
    // rename-on-success is the only existing dedup; it races between the two
    // daemons). On Linux the OS releases the flock when the process exits, so
    // we just need to hold the File alive for the daemon lifetime.
    let _lock = match acquire_watch_lock(&cfg.watch) {
        Ok(file) => file,
        Err(e) => {
            error!("{}", e);
            std::process::exit(1);
        }
    };

    loop {
        if SHUTDOWN_REQUESTED.load(Ordering::Acquire) {
            info!("prove-batch daemon: shutdown signal received — exiting cleanly");
            return;
        }
        match scan_once(&cfg) {
            Ok(processed) => {
                if processed > 0 {
                    info!("processed {} batch(es) this tick", processed);
                }
            }
            Err(e) => {
                error!("scan error: {}", e);
            }
        }
        interruptible_sleep(Duration::from_secs(cfg.poll_secs));
    }
}

/// Acquire an exclusive advisory `flock` on `watch_dir/.prove-batch.lock`.
/// Returns the held `File` (released on Drop = process exit). Errors with a
/// clear message if another daemon already holds the lock.
#[cfg(unix)]
fn acquire_watch_lock(watch_dir: &Path) -> Result<std::fs::File, String> {
    use std::fs::OpenOptions;
    use std::os::fd::AsRawFd;
    let lock_path = watch_dir.join(".prove-batch.lock");
    let lock_file = OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(false)
        .open(&lock_path)
        .map_err(|e| format!("open {} for flock: {}", lock_path.display(), e))?;
    let fd = lock_file.as_raw_fd();
    // SAFETY: fd is a valid file descriptor we just obtained from a successful
    // open + held alive by `lock_file`. flock is signal-safe.
    let rc = unsafe { libc::flock(fd, libc::LOCK_EX | libc::LOCK_NB) };
    if rc != 0 {
        let err = std::io::Error::last_os_error();
        return Err(format!(
            "another prove-batch daemon already holds the lock on {} ({}). \
             Stop the other instance or pick a different --watch.",
            watch_dir.display(),
            err,
        ));
    }
    Ok(lock_file)
}

#[cfg(not(unix))]
fn acquire_watch_lock(_watch_dir: &Path) -> Result<std::fs::File, String> {
    // Non-Unix: skip the flock. Caller is responsible for not running two
    // daemons. (The watch loop's rename-on-success is best-effort dedup.)
    Err("prove-batch single-instance lock is currently Unix-only".into())
}

fn parse_daemon_args(args: &[String]) -> Result<DaemonConfig, String> {
    let mut watch: Option<PathBuf> = None;
    let mut archive: Option<PathBuf> = None;
    let mut poll_secs: u64 = 5;
    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--watch" => {
                i += 1;
                if i >= args.len() {
                    return Err("--watch requires a path".into());
                }
                watch = Some(PathBuf::from(&args[i]));
            }
            "--archive" => {
                i += 1;
                if i >= args.len() {
                    return Err("--archive requires a path".into());
                }
                archive = Some(PathBuf::from(&args[i]));
            }
            "--poll-secs" => {
                i += 1;
                if i >= args.len() {
                    return Err("--poll-secs requires an integer".into());
                }
                poll_secs = args[i].parse().map_err(|e| format!("--poll-secs: {}", e))?;
                if poll_secs == 0 {
                    return Err("--poll-secs must be positive".into());
                }
            }
            "-h" | "--help" => {
                print_usage();
                std::process::exit(0);
            }
            other => return Err(format!("unexpected daemon argument: {}", other)),
        }
        i += 1;
    }
    let watch = watch.ok_or_else(|| "daemon: --watch <dir> is required".to_string())?;
    Ok(DaemonConfig {
        watch,
        archive,
        poll_secs,
    })
}

/// One pass over the watch dir: prove every `*.batch.bin` we find and
/// atomically rename it so we don't re-pick it up next tick.
fn scan_once(cfg: &DaemonConfig) -> Result<usize, String> {
    let mut processed = 0usize;
    let entries = std::fs::read_dir(&cfg.watch)
        .map_err(|e| format!("read_dir {}: {}", cfg.watch.display(), e))?;
    let mut batches: Vec<PathBuf> = entries
        .filter_map(|e| e.ok().map(|d| d.path()))
        .filter(|p| {
            p.is_file()
                && p.file_name()
                    .and_then(OsStr::to_str)
                    .map(|n| n.ends_with(".batch.bin"))
                    .unwrap_or(false)
        })
        .collect();
    // Sort so processing order is deterministic across hosts (lexical = batch-number
    // order if the sequencer names files like 00000042.batch.bin).
    batches.sort();
    for path in batches {
        let name = path
            .file_name()
            .and_then(OsStr::to_str)
            .unwrap_or("<unnamed>")
            .to_string();
        let bytes = match std::fs::read(&path) {
            Ok(b) => b,
            Err(e) => {
                error!("read {}: {}", path.display(), e);
                continue;
            }
        };
        let stem = name.trim_end_matches(".batch.bin");
        let proof_path = path.with_file_name(format!("{}.proof.bin", stem));
        info!("proving {} ({} bytes)...", name, bytes.len());
        let t0 = std::time::Instant::now();
        match prove_one(&bytes, &proof_path) {
            Ok(written) => {
                info!(
                    "  ✓ {} → public_input_hash=0x{} proof={}B vk={}B public-values={}B in {:?}",
                    name,
                    hex_encode(&written.public_input_hash),
                    written.proof_len,
                    written.vk_len,
                    written.public_values_len,
                    t0.elapsed()
                );
                if let Err(e) = finalize_input(&path, cfg) {
                    warn!("  failed to archive/rename {}: {}", path.display(), e);
                }
                processed += 1;
            }
            Err(e) => {
                error!("  ✗ {} failed: {}", name, e);
                // Leave the input in place so an operator can retry / inspect.
                // A poison-pill batch would otherwise loop the daemon forever — log it
                // loudly so monitoring catches the repeated failure rather than
                // silently moving past it.
            }
        }
    }
    Ok(processed)
}

fn finalize_input(path: &Path, cfg: &DaemonConfig) -> Result<(), String> {
    let name = path.file_name().ok_or("input path has no file name")?;
    if let Some(archive) = &cfg.archive {
        let dest = archive.join(name);
        std::fs::rename(path, &dest).map_err(|e| format!("rename to {}: {}", dest.display(), e))?;
    } else {
        let mut dest = path.to_path_buf();
        let new_name = format!(
            "{}.done",
            path.file_name().and_then(OsStr::to_str).unwrap_or("")
        );
        dest.set_file_name(new_name);
        std::fs::rename(path, &dest).map_err(|e| format!("rename to {}: {}", dest.display(), e))?;
    }
    Ok(())
}

// ────────────── shared ──────────────

struct WrittenProof {
    public_input_hash: [u8; 32],
    proof_len: usize,
    vk_len: usize,
    public_values_len: usize,
    proof_path: PathBuf,
    vk_path: PathBuf,
    public_values_path: PathBuf,
}

/// Generate a proof for `bytes` and write the exact on-chain proof, raw
/// program VK, and committed public values as three explicit artifacts.
fn prove_one(bytes: &[u8], proof_path: &Path) -> Result<WrittenProof, String> {
    let result = neo_zkvm_host::prove(bytes)?;
    std::fs::write(proof_path, &result.proof_bytes)
        .map_err(|e| format!("write proof to {}: {}", proof_path.display(), e))?;
    let stem = proof_path
        .to_str()
        .ok_or("proof path is not valid UTF-8")?
        .trim_end_matches(".bin");
    let vk_path = PathBuf::from(format!("{}.vk", stem));
    let public_values_path = PathBuf::from(format!("{}.public-values.bin", stem));
    std::fs::write(&vk_path, result.vk_bytes)
        .map_err(|e| format!("write vk to {}: {}", vk_path.display(), e))?;
    std::fs::write(&public_values_path, result.public_values).map_err(|e| {
        format!(
            "write public values to {}: {}",
            public_values_path.display(),
            e
        )
    })?;
    Ok(WrittenProof {
        public_input_hash: result.public_input_hash,
        proof_len: result.proof_bytes.len(),
        vk_len: result.vk_bytes.len(),
        public_values_len: result.public_values.len(),
        proof_path: proof_path.to_path_buf(),
        vk_path,
        public_values_path,
    })
}

fn print_usage() {
    info!("usage:");
    info!("  prove-batch <hex>                                     # one-shot execute (no proof)");
    info!(
        "  prove-batch --prove <hex> [--out proof.bin]           # one-shot prove + verify-key emit"
    );
    info!("  prove-batch daemon --watch <dir> [--archive <dir>]    # production prover daemon");
    info!("              [--poll-secs N]");
    info!("");
    info!("daemon mode:");
    info!("  watches <dir> for *.batch.bin (raw BatchExecutionRequest bytes), proves each,");
    info!("  emits <name>.proof.bin + <name>.proof.vk + <name>.proof.public-values.bin.");
    info!("  After a successful proof, the input is renamed or archived. The three outputs");
    info!("  are the exact artifacts consumed by ContractZkVerifier/Sp1Groth16Verifier.");
    info!("  The input is renamed to *.batch.bin.done (or moved into --archive if set)");
    info!("  so it isn't re-processed. Failures leave the input in place and log loudly so a");
    info!("  monitoring system can alert on repeated failures (poison-pill batches).");
}

fn hex_decode(s: &str) -> Result<Vec<u8>, String> {
    let s = s.strip_prefix("0x").unwrap_or(s);
    hex::decode(s).map_err(|e| format!("invalid hex: {e}"))
}

fn hex_encode(b: &[u8]) -> String {
    hex::encode(b)
}
