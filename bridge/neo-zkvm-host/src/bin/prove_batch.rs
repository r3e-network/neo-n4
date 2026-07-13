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
//!   for `*.batch.bin` files (each is a serialized ProofWitnessArtifactV1),
//!   generates a proof for each, writes `<name>.proof.bin` +
//!   `<name>.proof.vk` + `<name>.proof.public-values.bin` next to the input,
//!   and renames the input to
//!   `<name>.batch.bin.done` so it's not re-processed. This is the
//!   recommended production deployment shape: a separate prover process
//!   that the sequencer drops sealed batches into via filesystem (or any
//!   other queue you wire up).

use std::ffi::OsStr;
use std::fs::OpenOptions;
use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::time::Duration;
use tracing::{error, info, warn};

/// Set by the SIGTERM/SIGINT handler. The daemon loop checks this at the
/// top of every iteration and inside `interruptible_sleep` so a shutdown
/// signal lands cleanly even mid-poll. Proofs in progress are NOT interrupted
/// — they complete first (proof time is bounded), and then the loop exits.
static SHUTDOWN_REQUESTED: AtomicBool = AtomicBool::new(false);
static TEMP_COUNTER: AtomicU64 = AtomicU64::new(0);
const GROTH16_PROOF_BYTES: usize = 356;
const VERIFICATION_KEY_BYTES: usize = 32;
const PUBLIC_VALUES_BYTES: usize = 33;

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
        let mut sa: libc::sigaction = std::mem::zeroed();
        sa.sa_sigaction = shutdown_signal_handler as libc::sighandler_t;
        sa.sa_flags = libc::SA_RESTART;
        libc::sigemptyset(&mut sa.sa_mask);
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
    gateway_sidecars: Option<PathBuf>,
    poll_secs: u64,
}

fn run_daemon(args: &[String]) {
    let cfg = parse_daemon_args(args).unwrap_or_else(|e| {
        error!("{}", e);
        print_usage();
        std::process::exit(1);
    });

    if let Err(error) = validate_non_symlink_directory(&cfg.watch, "--watch") {
        error!("{}", error);
        std::process::exit(1);
    }
    if let Some(a) = &cfg.archive {
        std::fs::create_dir_all(a).unwrap_or_else(|e| {
            error!("failed to create --archive dir {}: {}", a.display(), e);
            std::process::exit(1);
        });
        if let Err(error) = validate_non_symlink_directory(a, "--archive") {
            error!("{}", error);
            std::process::exit(1);
        }
    }
    if let Some(directory) = &cfg.gateway_sidecars {
        std::fs::create_dir_all(directory).unwrap_or_else(|error| {
            error!(
                "failed to create --gateway-sidecars dir {}: {}",
                directory.display(),
                error
            );
            std::process::exit(1);
        });
        if let Err(error) = validate_non_symlink_directory(directory, "--gateway-sidecars") {
            error!("{}", error);
            std::process::exit(1);
        }
    }

    info!(
        "prove-batch daemon: watching {} (poll every {}s, archive: {}, Gateway sidecars: {})",
        cfg.watch.display(),
        cfg.poll_secs,
        cfg.archive
            .as_ref()
            .map(|p| p.display().to_string())
            .unwrap_or_else(|| "<rename in place>".to_string()),
        cfg.gateway_sidecars
            .as_ref()
            .map(|path| path.display().to_string())
            .unwrap_or_else(|| "<disabled>".to_string())
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
    use std::os::unix::fs::OpenOptionsExt;
    let lock_path = watch_dir.join(".prove-batch.lock");
    let lock_file = OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(false)
        .custom_flags(libc::O_NOFOLLOW)
        .open(&lock_path)
        .map_err(|e| format!("open {} for flock: {}", lock_path.display(), e))?;
    if !lock_file
        .metadata()
        .map_err(|error| format!("inspect {}: {error}", lock_path.display()))?
        .file_type()
        .is_file()
    {
        return Err(format!(
            "prove-batch lock must be a regular file: {}",
            lock_path.display()
        ));
    }
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
    let mut gateway_sidecars: Option<PathBuf> = None;
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
            "--gateway-sidecars" => {
                i += 1;
                if i >= args.len() {
                    return Err("--gateway-sidecars requires a path".into());
                }
                gateway_sidecars = Some(PathBuf::from(&args[i]));
            }
            "--poll-secs" => {
                i += 1;
                if i >= args.len() {
                    return Err("--poll-secs requires an integer".into());
                }
                poll_secs = args[i]
                    .parse()
                    .map_err(|error| format!("--poll-secs: {error}"))?;
                if poll_secs == 0 {
                    return Err("--poll-secs must be positive".into());
                }
            }
            "-h" | "--help" => {
                print_usage();
                std::process::exit(0);
            }
            other => return Err(format!("unexpected daemon argument: {other}")),
        }
        i += 1;
    }
    let watch = watch.ok_or_else(|| "daemon: --watch <dir> is required".to_string())?;
    Ok(DaemonConfig {
        watch,
        archive,
        gateway_sidecars,
        poll_secs,
    })
}

/// One pass over the watch dir: prove every `*.batch.bin` we find and
/// atomically rename it so we don't re-pick it up next tick.
fn scan_once(cfg: &DaemonConfig) -> Result<usize, String> {
    validate_non_symlink_directory(&cfg.watch, "watch directory")?;
    if let Some(directory) = &cfg.gateway_sidecars {
        validate_non_symlink_directory(directory, "Gateway sidecar directory")?;
    }
    let mut processed = 0usize;
    let entries = std::fs::read_dir(&cfg.watch)
        .map_err(|e| format!("read_dir {}: {}", cfg.watch.display(), e))?;
    let mut batches = Vec::new();
    for entry in entries {
        let entry = entry.map_err(|error| format!("read_dir entry: {error}"))?;
        let path = entry.path();
        if path
            .file_name()
            .and_then(OsStr::to_str)
            .is_some_and(|name| name.ends_with(".batch.bin"))
        {
            batches.push(path);
        }
    }
    // Sort so processing order is deterministic across hosts (lexical = batch-number
    // order if the sequencer names files like 00000042.batch.bin).
    batches.sort();
    for path in batches {
        let name = path
            .file_name()
            .and_then(OsStr::to_str)
            .unwrap_or("<unnamed>")
            .to_string();
        let bytes =
            match read_regular_bounded(&path, neo_execution_core::MAX_PROOF_WITNESS_ARTIFACT_BYTES)
            {
                Ok(b) => b,
                Err(e) => {
                    error!("read {}: {}", path.display(), e);
                    continue;
                }
            };
        let stem = name.trim_end_matches(".batch.bin");
        let proof_path = path.with_file_name(format!("{stem}.proof.bin"));
        info!("proving {} ({} bytes)...", name, bytes.len());
        let t0 = std::time::Instant::now();
        let proof_result = match recover_or_clean_existing_proof(&bytes, &proof_path) {
            Ok(Some(written)) => {
                info!("  recovering verified proof artifacts for {}", name);
                Ok(written)
            }
            Ok(None) => prove_one(&bytes, &proof_path),
            Err(error) => Err(error),
        };
        match proof_result {
            Ok(written) => {
                if let Some(directory) = &cfg.gateway_sidecars
                    && let Err(error) = publish_gateway_sidecar(&bytes, &written, directory)
                {
                    error!("  ✗ {} Gateway sidecar failed: {}", name, error);
                    continue;
                }
                info!(
                    "  ✓ {} → public_input_hash=0x{} proof={}B vk={}B public-values={}B in {:?}",
                    name,
                    hex_encode(&written.public_input_hash),
                    written.proof_len,
                    written.vk_len,
                    written.public_values_len,
                    t0.elapsed()
                );
                if let Err(e) = finalize_input(&path, cfg, &bytes) {
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

fn publish_gateway_sidecar(
    request_bytes: &[u8],
    terminal: &WrittenProof,
    directory: &Path,
) -> Result<(), String> {
    validate_non_symlink_directory(directory, "Gateway sidecar directory")?;
    let recursive = neo_zkvm_host::prove_compressed(request_bytes)?;
    if recursive.public_input_hash != terminal.public_input_hash {
        return Err(
            "terminal and compressed proofs committed different public-input hashes".into(),
        );
    }
    let sidecar = neo_zkvm_gateway_guest::encode_child_sidecar(
        recursive.chain_id,
        recursive.batch_number,
        &recursive.public_input_hash,
        &recursive.l1_message_hash,
        &recursive.block_context_hash,
        &recursive.proof_bytes,
    )
    .map_err(|error| format!("encode recursive child sidecar: {error}"))?;
    let filename = neo_zkvm_gateway_guest::canonical_child_sidecar_filename(
        recursive.chain_id,
        recursive.batch_number,
        &recursive.public_input_hash,
    );
    publish_exact_atomic(&directory.join(filename), &sidecar)
}

fn publish_exact_atomic(path: &Path, bytes: &[u8]) -> Result<(), String> {
    match read_regular_bounded(path, bytes.len()) {
        Ok(existing) if existing == bytes => return Ok(()),
        Ok(_) => {
            return Err(format!(
                "existing idempotent artifact differs: {}",
                path.display()
            ));
        }
        Err(error) if error.kind() != std::io::ErrorKind::NotFound => {
            return Err(format!("inspect existing {}: {error}", path.display()));
        }
        Err(_) => {}
    }

    let file_name = path
        .file_name()
        .and_then(OsStr::to_str)
        .ok_or_else(|| "artifact filename is not valid UTF-8".to_string())?;
    let suffix = TEMP_COUNTER.fetch_add(1, Ordering::Relaxed);
    let temporary =
        path.with_file_name(format!(".{file_name}.tmp-{}-{suffix}", std::process::id()));
    let result = (|| {
        let mut file = OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&temporary)
            .map_err(|error| format!("create {}: {error}", temporary.display()))?;
        file.write_all(bytes)
            .map_err(|error| format!("write {}: {error}", temporary.display()))?;
        file.sync_all()
            .map_err(|error| format!("sync {}: {error}", temporary.display()))?;
        match std::fs::hard_link(&temporary, path) {
            Ok(()) => {
                sync_parent_directory(path)?;
                Ok(())
            }
            Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => {
                let existing = read_regular_bounded(path, bytes.len()).map_err(|read_error| {
                    format!("inspect existing {}: {read_error}", path.display())
                })?;
                if existing == bytes {
                    Ok(())
                } else {
                    Err(format!("publish {}: {error}", path.display()))
                }
            }
            Err(error) => Err(format!("publish {}: {error}", path.display())),
        }
    })();
    let _ = std::fs::remove_file(&temporary);
    result
}

fn finalize_input(path: &Path, cfg: &DaemonConfig, expected: &[u8]) -> Result<(), String> {
    let current = read_regular_bounded(path, expected.len())
        .map_err(|error| format!("re-read {} before finalization: {error}", path.display()))?;
    if current != expected {
        return Err(format!(
            "input changed while proving and will not be finalized: {}",
            path.display()
        ));
    }
    let name = path.file_name().ok_or("input path has no file name")?;
    if let Some(archive) = &cfg.archive {
        let dest = archive.join(name);
        finalize_exact_no_replace(path, &dest)?;
    } else {
        let mut dest = path.to_path_buf();
        let new_name = format!(
            "{}.done",
            path.file_name().and_then(OsStr::to_str).unwrap_or("")
        );
        dest.set_file_name(new_name);
        finalize_exact_no_replace(path, &dest)?;
    }
    Ok(())
}

fn validate_non_symlink_directory(path: &Path, description: &str) -> Result<(), String> {
    let metadata = std::fs::symlink_metadata(path)
        .map_err(|error| format!("inspect {description} {}: {error}", path.display()))?;
    if metadata.file_type().is_symlink() || !metadata.file_type().is_dir() {
        return Err(format!(
            "{description} must be a non-symlink directory: {}",
            path.display()
        ));
    }
    Ok(())
}

fn read_regular_bounded(path: &Path, max_bytes: usize) -> std::io::Result<Vec<u8>> {
    let mut options = OpenOptions::new();
    options.read(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.custom_flags(libc::O_NOFOLLOW);
    }
    let mut file = options.open(path)?;
    let metadata = file.metadata()?;
    if !metadata.file_type().is_file() {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidData,
            "artifact must be a regular non-symlink file",
        ));
    }
    if metadata.len() > max_bytes as u64 {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidData,
            format!("artifact exceeds {max_bytes} bytes"),
        ));
    }
    let read_limit = u64::try_from(max_bytes)
        .ok()
        .and_then(|limit| limit.checked_add(1))
        .ok_or_else(|| std::io::Error::other("artifact size limit overflow"))?;
    let mut bytes = Vec::with_capacity(metadata.len() as usize);
    (&mut file).take(read_limit).read_to_end(&mut bytes)?;
    if bytes.len() > max_bytes || bytes.len() as u64 != metadata.len() {
        return Err(std::io::Error::new(
            std::io::ErrorKind::InvalidData,
            "artifact changed while reading",
        ));
    }
    Ok(bytes)
}

fn sync_parent_directory(path: &Path) -> Result<(), String> {
    let parent = path
        .parent()
        .filter(|value| !value.as_os_str().is_empty())
        .unwrap_or_else(|| Path::new("."));
    std::fs::File::open(parent)
        .and_then(|directory| directory.sync_all())
        .map_err(|error| format!("sync {}: {error}", parent.display()))
}

fn finalize_exact_no_replace(source: &Path, destination: &Path) -> Result<(), String> {
    match std::fs::hard_link(source, destination) {
        Ok(()) => sync_parent_directory(destination)?,
        Err(error) if error.kind() == std::io::ErrorKind::AlreadyExists => {
            if !same_file(source, destination)? {
                return Err(format!(
                    "finalized destination already exists with different content: {}",
                    destination.display()
                ));
            }
        }
        Err(error) => {
            return Err(format!(
                "link {} to {}: {error}",
                source.display(),
                destination.display()
            ));
        }
    }
    std::fs::remove_file(source)
        .map_err(|error| format!("remove finalized input {}: {error}", source.display()))?;
    sync_parent_directory(source)
}

#[cfg(unix)]
fn same_file(left: &Path, right: &Path) -> Result<bool, String> {
    use std::os::unix::fs::MetadataExt;
    let left = std::fs::symlink_metadata(left)
        .map_err(|error| format!("inspect {}: {error}", left.display()))?;
    let right = std::fs::symlink_metadata(right)
        .map_err(|error| format!("inspect {}: {error}", right.display()))?;
    Ok(left.file_type().is_file()
        && right.file_type().is_file()
        && left.dev() == right.dev()
        && left.ino() == right.ino())
}

#[cfg(not(unix))]
fn same_file(_: &Path, _: &Path) -> Result<bool, String> {
    Ok(false)
}

// ────────────── shared ──────────────

#[derive(Debug)]
struct WrittenProof {
    public_input_hash: [u8; 32],
    proof_len: usize,
    vk_len: usize,
    public_values_len: usize,
    proof_path: PathBuf,
    vk_path: PathBuf,
    public_values_path: PathBuf,
}

fn proof_artifact_paths(proof_path: &Path) -> Result<(PathBuf, PathBuf), String> {
    let stem = proof_path
        .to_str()
        .ok_or("proof path is not valid UTF-8")?
        .trim_end_matches(".bin");
    Ok((
        PathBuf::from(format!("{stem}.vk")),
        PathBuf::from(format!("{stem}.public-values.bin")),
    ))
}

fn recover_or_clean_existing_proof(
    request_bytes: &[u8],
    proof_path: &Path,
) -> Result<Option<WrittenProof>, String> {
    let (vk_path, public_values_path) = proof_artifact_paths(proof_path)?;
    let paths = [proof_path, vk_path.as_path(), public_values_path.as_path()];
    let existing = paths
        .iter()
        .map(|path| match std::fs::symlink_metadata(path) {
            Ok(_) => Ok(true),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
            Err(error) => Err(format!("inspect {}: {error}", path.display())),
        })
        .collect::<Result<Vec<_>, _>>()?;
    if existing.iter().all(|exists| !exists) {
        return Ok(None);
    }

    if existing.iter().all(|exists| *exists) {
        let validated = (|| {
            let proof_bytes = read_regular_bounded(proof_path, GROTH16_PROOF_BYTES)
                .map_err(|error| format!("read {}: {error}", proof_path.display()))?;
            let vk_bytes = read_regular_bounded(&vk_path, VERIFICATION_KEY_BYTES)
                .map_err(|error| format!("read {}: {error}", vk_path.display()))?;
            let public_values = read_regular_bounded(&public_values_path, PUBLIC_VALUES_BYTES)
                .map_err(|error| format!("read {}: {error}", public_values_path.display()))?;
            if proof_bytes.len() != GROTH16_PROOF_BYTES
                || vk_bytes.len() != VERIFICATION_KEY_BYTES
                || public_values.len() != PUBLIC_VALUES_BYTES
            {
                return Err("existing proof artifact set has non-canonical lengths".to_string());
            }
            let public_input_hash = expected_public_input_hash(request_bytes)?;
            let mut expected_public_values = [0u8; PUBLIC_VALUES_BYTES];
            expected_public_values[1..].copy_from_slice(&public_input_hash);
            if public_values != expected_public_values {
                return Err(
                    "existing public values do not match the canonical witness artifact".into(),
                );
            }
            neo_zkvm_host::verify(&proof_bytes, &vk_bytes, &public_input_hash)?;
            Ok(WrittenProof {
                public_input_hash,
                proof_len: proof_bytes.len(),
                vk_len: vk_bytes.len(),
                public_values_len: public_values.len(),
                proof_path: proof_path.to_path_buf(),
                vk_path: vk_path.clone(),
                public_values_path: public_values_path.clone(),
            })
        })();
        if let Ok(written) = validated {
            return Ok(Some(written));
        }
    }

    remove_orphan_proof_artifacts(&paths)?;
    Ok(None)
}

fn expected_public_input_hash(request_bytes: &[u8]) -> Result<[u8; 32], String> {
    let artifact = neo_execution_core::parse_proof_witness_artifact(request_bytes)
        .map_err(|error| format!("parse proof witness artifact: {error}"))?;
    Ok(neo_execution_core::hash_public_inputs(
        artifact.public_inputs.chain_id,
        artifact.public_inputs.batch_number,
        &artifact.public_inputs.pre_state_root,
        &artifact.public_inputs.post_state_root,
        &artifact.public_inputs.tx_root,
        &artifact.public_inputs.receipt_root,
        &artifact.public_inputs.withdrawal_root,
        &artifact.public_inputs.l2_to_l1_message_root,
        &artifact.public_inputs.l2_to_l2_message_root,
        &artifact.public_inputs.l1_message_hash,
        &artifact.public_inputs.da_commitment,
        &artifact.public_inputs.block_context_hash,
    ))
}

fn remove_orphan_proof_artifacts(paths: &[&Path]) -> Result<(), String> {
    for path in paths {
        match std::fs::symlink_metadata(path) {
            Ok(metadata)
                if metadata.file_type().is_symlink() || !metadata.file_type().is_file() =>
            {
                return Err(format!(
                    "orphan proof artifact must be a regular non-symlink file: {}",
                    path.display()
                ));
            }
            Ok(_) => {}
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(format!("inspect {}: {error}", path.display())),
        }
    }
    let mut removed = false;
    for path in paths {
        match std::fs::remove_file(path) {
            Ok(()) => removed = true,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => {}
            Err(error) => return Err(format!("remove {}: {error}", path.display())),
        }
    }
    if removed {
        sync_parent_directory(paths[0])?;
    }
    Ok(())
}

/// Generate a proof for `bytes` and write the exact on-chain proof, raw
/// program VK, and committed public values as three explicit artifacts.
fn prove_one(bytes: &[u8], proof_path: &Path) -> Result<WrittenProof, String> {
    let result = neo_zkvm_host::prove(bytes)?;
    publish_exact_atomic(proof_path, &result.proof_bytes)?;
    let (vk_path, public_values_path) = proof_artifact_paths(proof_path)?;
    publish_exact_atomic(&vk_path, &result.vk_bytes)?;
    publish_exact_atomic(&public_values_path, &result.public_values)?;
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
    info!("              [--gateway-sidecars <dir>] [--poll-secs N]");
    info!("");
    info!("daemon mode:");
    info!("  watches <dir> for *.batch.bin (raw ProofWitnessArtifactV1 bytes), proves each,");
    info!("  emits <name>.proof.bin + <name>.proof.vk + <name>.proof.public-values.bin.");
    info!("  With --gateway-sidecars, also emits one atomically published, tuple-bound SP1");
    info!("  compressed child sidecar containing the missing public-input hash fields.");
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

#[cfg(test)]
mod tests {
    use super::*;

    fn test_directory(name: &str) -> PathBuf {
        let directory = std::env::temp_dir().join(format!(
            "neo-zkvm-host-{name}-{}-{}",
            std::process::id(),
            TEMP_COUNTER.fetch_add(1, Ordering::Relaxed)
        ));
        std::fs::create_dir(&directory).unwrap();
        directory
    }

    #[test]
    fn atomic_publication_is_exact_and_no_clobber() {
        let directory = test_directory("publish");
        let artifact = directory.join("artifact");
        publish_exact_atomic(&artifact, b"exact").unwrap();
        publish_exact_atomic(&artifact, b"exact").unwrap();
        assert_eq!(std::fs::read(&artifact).unwrap(), b"exact");
        assert!(publish_exact_atomic(&artifact, b"different").is_err());
        std::fs::remove_dir_all(directory).unwrap();
    }

    #[test]
    fn partial_proof_artifacts_are_removed_before_retry() {
        let directory = test_directory("orphan-proof");
        let proof_path = directory.join("0001.proof.bin");
        std::fs::write(&proof_path, b"partial").unwrap();

        assert!(
            recover_or_clean_existing_proof(b"not-needed-for-partial-cleanup", &proof_path)
                .unwrap()
                .is_none()
        );
        assert!(!proof_path.exists());
        std::fs::remove_dir_all(directory).unwrap();
    }

    #[cfg(unix)]
    #[test]
    fn proof_recovery_rejects_symlink_artifacts() {
        use std::os::unix::fs::symlink;
        let directory = test_directory("orphan-proof-symlink");
        let proof_path = directory.join("0001.proof.bin");
        let target = directory.join("target");
        std::fs::write(&target, b"partial").unwrap();
        symlink(&target, &proof_path).unwrap();

        assert!(
            recover_or_clean_existing_proof(b"request", &proof_path)
                .unwrap_err()
                .contains("non-symlink")
        );
        assert!(target.exists());
        std::fs::remove_dir_all(directory).unwrap();
    }

    #[cfg(unix)]
    #[test]
    fn bounded_reader_and_lock_reject_symlinks() {
        use std::os::unix::fs::symlink;
        let directory = test_directory("symlink");
        let target = directory.join("target");
        let input = directory.join("input.batch.bin");
        let lock = directory.join(".prove-batch.lock");
        std::fs::write(&target, b"payload").unwrap();
        symlink(&target, &input).unwrap();
        symlink(&target, &lock).unwrap();
        assert!(read_regular_bounded(&input, 32).is_err());
        assert!(acquire_watch_lock(&directory).is_err());
        std::fs::remove_dir_all(directory).unwrap();
    }

    #[cfg(unix)]
    #[test]
    fn finalization_recovers_idempotently_after_link_only_crash() {
        let directory = test_directory("finalize");
        let source = directory.join("batch.bin");
        let destination = directory.join("batch.bin.done");
        std::fs::write(&source, b"payload").unwrap();
        std::fs::hard_link(&source, &destination).unwrap();
        finalize_exact_no_replace(&source, &destination).unwrap();
        assert!(!source.exists());
        assert_eq!(std::fs::read(&destination).unwrap(), b"payload");
        std::fs::remove_dir_all(directory).unwrap();
    }
}
