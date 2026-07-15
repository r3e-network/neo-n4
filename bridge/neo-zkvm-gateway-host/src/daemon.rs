use crate::{
    GATEWAY_VK_BYTES32,
    protocol::{
        GROTH16_PROOF_BYTES, GatewayError, GatewayProofArtifacts, GatewayProofRequestManifest,
        GatewayProofResultManifest, MAX_MANIFEST_BYTES, MAX_REQUEST_BYTES, PROOF_SUFFIX,
        PUBLIC_VALUES_BYTES, PUBLIC_VALUES_SUFFIX, REQUEST_PAYLOAD_SUFFIX, RESULT_MANIFEST_SUFFIX,
        VERIFICATION_KEY_SUFFIX, request_id_from_manifest_path, result_manifest,
        validate_request_manifest,
    },
};
use neo_zkvm_gateway_guest::gateway_public_values;
use std::{
    fs::{File, OpenOptions},
    io::Write,
    path::{Path, PathBuf},
    sync::atomic::{AtomicBool, AtomicU64, Ordering},
    time::Duration,
};
use tracing::{error, info};

static SHUTDOWN: AtomicBool = AtomicBool::new(false);
static TEMP_COUNTER: AtomicU64 = AtomicU64::new(0);

#[derive(Debug, Clone)]
pub struct DaemonConfig {
    pub queue_directory: PathBuf,
    pub child_proof_directory: PathBuf,
    pub poll_interval: Duration,
}

pub fn run_daemon(config: &DaemonConfig) -> Result<(), GatewayError> {
    validate_directory(&config.queue_directory, "queue directory")?;
    validate_directory(&config.child_proof_directory, "child-proof directory")?;
    if config.poll_interval.is_zero() {
        return Err(GatewayError::Protocol(
            "poll interval must be positive".into(),
        ));
    }
    let _lock = acquire_queue_lock(&config.queue_directory)?;
    install_signal_handlers();
    info!(
        queue = %config.queue_directory.display(),
        child_proofs = %config.child_proof_directory.display(),
        "Gateway SP1 daemon started"
    );
    while !SHUTDOWN.load(Ordering::Acquire) {
        if let Err(error) = process_ready_requests(config) {
            error!(%error, "Gateway request scan failed closed");
        }
        interruptible_sleep(config.poll_interval);
    }
    Ok(())
}

pub fn process_ready_requests(config: &DaemonConfig) -> Result<usize, GatewayError> {
    validate_directory(&config.queue_directory, "queue directory")?;
    validate_directory(&config.child_proof_directory, "child-proof directory")?;
    let mut manifests = Vec::new();
    for entry in std::fs::read_dir(&config.queue_directory)
        .map_err(|error| GatewayError::io(&config.queue_directory, error))?
    {
        let entry = entry.map_err(|error| GatewayError::io(&config.queue_directory, error))?;
        let path = entry.path();
        if request_id_from_manifest_path(&path).is_some() {
            manifests.push(path);
        }
    }
    manifests.sort();

    let mut processed = 0;
    for manifest_path in manifests {
        match process_manifest(config, &manifest_path) {
            Ok(true) => processed += 1,
            Ok(false) => {}
            Err(error) => {
                error!(path = %manifest_path.display(), %error, "Gateway request rejected")
            }
        }
    }
    Ok(processed)
}

fn process_manifest(config: &DaemonConfig, manifest_path: &Path) -> Result<bool, GatewayError> {
    let request_id = request_id_from_manifest_path(manifest_path)
        .ok_or_else(|| GatewayError::Protocol("non-canonical request manifest filename".into()))?
        .to_string();
    let result_path = config
        .queue_directory
        .join(format!("{request_id}{RESULT_MANIFEST_SUFFIX}"));
    let manifest_bytes = read_regular_bounded(manifest_path, MAX_MANIFEST_BYTES)?;
    let manifest: GatewayProofRequestManifest = serde_json::from_slice(&manifest_bytes)?;
    if manifest.request_id != request_id {
        return Err(GatewayError::Protocol(
            "requestId does not match readiness-manifest filename".into(),
        ));
    }
    let request_path = config
        .queue_directory
        .join(format!("{request_id}{REQUEST_PAYLOAD_SUFFIX}"));
    let request_bytes = read_regular_bounded(&request_path, MAX_REQUEST_BYTES)?;
    let request = validate_request_manifest(&manifest, &request_bytes, &GATEWAY_VK_BYTES32)?;
    if result_path.exists() {
        validate_existing_result(
            &config.queue_directory,
            &manifest,
            &gateway_public_values(&request.binding),
        )?;
        return Ok(false);
    }
    remove_orphan_artifacts(&config.queue_directory, &request_id)?;
    let artifacts = crate::prove_request(&request, &request_bytes, &config.child_proof_directory)?;
    publish_artifacts(&config.queue_directory, &manifest, &artifacts)?;
    Ok(true)
}

fn validate_existing_result(
    queue: &Path,
    request: &GatewayProofRequestManifest,
    expected_public_values: &[u8; PUBLIC_VALUES_BYTES],
) -> Result<(), GatewayError> {
    let request_id = &request.request_id;
    let result_path = queue.join(format!("{request_id}{RESULT_MANIFEST_SUFFIX}"));
    let result_bytes = read_regular_bounded(&result_path, MAX_MANIFEST_BYTES)?;
    let result: GatewayProofResultManifest = serde_json::from_slice(&result_bytes)?;
    let proof_bytes = read_regular_bounded(
        &queue.join(format!("{request_id}{PROOF_SUFFIX}")),
        GROTH16_PROOF_BYTES as u64,
    )?;
    let verification_key = read_regular_bounded(
        &queue.join(format!("{request_id}{VERIFICATION_KEY_SUFFIX}")),
        GATEWAY_VK_BYTES32.len() as u64,
    )?;
    let public_values = read_regular_bounded(
        &queue.join(format!("{request_id}{PUBLIC_VALUES_SUFFIX}")),
        PUBLIC_VALUES_BYTES as u64,
    )?;
    let artifacts = GatewayProofArtifacts {
        proof_bytes,
        verification_key: verification_key.try_into().map_err(|value: Vec<u8>| {
            GatewayError::Protocol(format!(
                "Gateway verification key must be 32 bytes, got {}",
                value.len()
            ))
        })?,
        public_values: public_values.try_into().map_err(|value: Vec<u8>| {
            GatewayError::Protocol(format!(
                "Gateway public values must be {PUBLIC_VALUES_BYTES} bytes, got {}",
                value.len()
            ))
        })?,
    };
    let expected_result = result_manifest(request, &artifacts);
    if result != expected_result {
        return Err(GatewayError::Protocol(
            "Gateway result manifest does not exactly bind the published artifacts".into(),
        ));
    }
    crate::prover::verify_gateway_artifacts(&artifacts, expected_public_values)
}

fn remove_orphan_artifacts(queue: &Path, request_id: &str) -> Result<(), GatewayError> {
    let mut removed = false;
    for suffix in [PROOF_SUFFIX, VERIFICATION_KEY_SUFFIX, PUBLIC_VALUES_SUFFIX] {
        let path = queue.join(format!("{request_id}{suffix}"));
        let metadata = match std::fs::symlink_metadata(&path) {
            Ok(metadata) => metadata,
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => continue,
            Err(error) => return Err(GatewayError::io(&path, error)),
        };
        if metadata.file_type().is_symlink() || !metadata.file_type().is_file() {
            return Err(GatewayError::Protocol(format!(
                "orphan Gateway artifact must be a regular non-symlink file: {}",
                path.display()
            )));
        }
        std::fs::remove_file(&path).map_err(|error| GatewayError::io(&path, error))?;
        removed = true;
    }
    if removed {
        sync_parent_directory(&queue.join("orphan-cleanup"))?;
    }
    Ok(())
}

fn publish_artifacts(
    queue: &Path,
    request: &GatewayProofRequestManifest,
    artifacts: &GatewayProofArtifacts,
) -> Result<(), GatewayError> {
    let result = result_manifest(request, artifacts);
    let result_bytes = serde_json::to_vec(&result)?;
    publish_exact_atomic(
        &queue.join(format!("{}{}", request.request_id, PROOF_SUFFIX)),
        &artifacts.proof_bytes,
    )?;
    publish_exact_atomic(
        &queue.join(format!("{}{}", request.request_id, VERIFICATION_KEY_SUFFIX)),
        &artifacts.verification_key,
    )?;
    publish_exact_atomic(
        &queue.join(format!("{}{}", request.request_id, PUBLIC_VALUES_SUFFIX)),
        &artifacts.public_values,
    )?;
    publish_exact_atomic(
        &queue.join(format!("{}{}", request.request_id, RESULT_MANIFEST_SUFFIX)),
        &result_bytes,
    )?;
    Ok(())
}

fn publish_exact_atomic(path: &Path, bytes: &[u8]) -> Result<(), GatewayError> {
    if path.exists() {
        let existing = read_regular_bounded(path, bytes.len() as u64)?;
        if existing == bytes {
            return Ok(());
        }
        return Err(GatewayError::Protocol(format!(
            "existing idempotent artifact differs: {}",
            path.display()
        )));
    }
    let suffix = TEMP_COUNTER.fetch_add(1, Ordering::Relaxed);
    let file_name = path
        .file_name()
        .and_then(|name| name.to_str())
        .ok_or_else(|| GatewayError::Protocol("artifact filename is not UTF-8".into()))?;
    let temporary =
        path.with_file_name(format!(".{file_name}.tmp-{}-{suffix}", std::process::id()));
    let result = (|| {
        let mut file = OpenOptions::new()
            .create_new(true)
            .write(true)
            .open(&temporary)
            .map_err(|error| GatewayError::io(&temporary, error))?;
        file.write_all(bytes)
            .map_err(|error| GatewayError::io(&temporary, error))?;
        file.sync_all()
            .map_err(|error| GatewayError::io(&temporary, error))?;
        match std::fs::hard_link(&temporary, path) {
            Ok(()) => sync_parent_directory(path),
            Err(error) if path.exists() => {
                let existing = read_regular_bounded(path, bytes.len() as u64)?;
                if existing == bytes {
                    Ok(())
                } else {
                    Err(GatewayError::io(path, error))
                }
            }
            Err(error) => Err(GatewayError::io(path, error)),
        }
    })();
    let _ = std::fs::remove_file(&temporary);
    result
}

fn read_regular_bounded(path: &Path, max_bytes: u64) -> Result<Vec<u8>, GatewayError> {
    let mut options = OpenOptions::new();
    options.read(true);
    #[cfg(unix)]
    {
        use std::os::unix::fs::OpenOptionsExt;
        options.custom_flags(libc::O_NOFOLLOW);
    }
    let mut file = options
        .open(path)
        .map_err(|error| GatewayError::io(path, error))?;
    let metadata = file
        .metadata()
        .map_err(|error| GatewayError::io(path, error))?;
    if !metadata.file_type().is_file() {
        return Err(GatewayError::Protocol(format!(
            "queue artifact must be a regular non-symlink file: {}",
            path.display()
        )));
    }
    if metadata.len() > max_bytes {
        return Err(GatewayError::Protocol(format!(
            "queue artifact exceeds {max_bytes} bytes: {}",
            path.display()
        )));
    }
    let mut bytes = Vec::with_capacity(metadata.len() as usize);
    use std::io::Read;
    let read_limit = max_bytes
        .checked_add(1)
        .ok_or_else(|| GatewayError::Protocol("queue artifact size limit overflow".into()))?;
    (&mut file)
        .take(read_limit)
        .read_to_end(&mut bytes)
        .map_err(|error| GatewayError::io(path, error))?;
    if bytes.len() as u64 != metadata.len() || bytes.len() as u64 > max_bytes {
        return Err(GatewayError::Protocol(format!(
            "queue artifact changed while reading: {}",
            path.display()
        )));
    }
    Ok(bytes)
}

#[cfg(unix)]
fn acquire_queue_lock(queue: &Path) -> Result<File, GatewayError> {
    use std::os::fd::AsRawFd;
    use std::os::unix::fs::OpenOptionsExt;
    let path = queue.join(".prove-gateway.lock");
    let file = OpenOptions::new()
        .create(true)
        .write(true)
        .truncate(false)
        .custom_flags(libc::O_NOFOLLOW)
        .open(&path)
        .map_err(|error| GatewayError::io(&path, error))?;
    if !file
        .metadata()
        .map_err(|error| GatewayError::io(&path, error))?
        .file_type()
        .is_file()
    {
        return Err(GatewayError::Protocol(format!(
            "Gateway daemon lock must be a regular file: {}",
            path.display()
        )));
    }
    let result = unsafe { libc::flock(file.as_raw_fd(), libc::LOCK_EX | libc::LOCK_NB) };
    if result != 0 {
        return Err(GatewayError::Protocol(format!(
            "another prove-gateway daemon already holds {}",
            path.display()
        )));
    }
    Ok(file)
}

#[cfg(not(unix))]
fn acquire_queue_lock(_: &Path) -> Result<File, GatewayError> {
    Err(GatewayError::Protocol(
        "prove-gateway daemon lock requires Unix/WSL2".into(),
    ))
}

fn validate_directory(path: &Path, description: &str) -> Result<(), GatewayError> {
    let metadata =
        std::fs::symlink_metadata(path).map_err(|error| GatewayError::io(path, error))?;
    if metadata.file_type().is_symlink() || !metadata.file_type().is_dir() {
        return Err(GatewayError::Protocol(format!(
            "{description} must be a non-symlink directory: {}",
            path.display()
        )));
    }
    Ok(())
}

fn sync_parent_directory(path: &Path) -> Result<(), GatewayError> {
    let parent = path
        .parent()
        .ok_or_else(|| GatewayError::Protocol("artifact path has no parent".into()))?;
    File::open(parent)
        .and_then(|directory| directory.sync_all())
        .map_err(|error| GatewayError::io(parent, error))
}

#[cfg(unix)]
extern "C" fn signal_handler(_: i32) {
    SHUTDOWN.store(true, Ordering::Release);
}

#[cfg(unix)]
fn install_signal_handlers() {
    unsafe {
        let mut action: libc::sigaction = std::mem::zeroed();
        action.sa_sigaction = signal_handler as libc::sighandler_t;
        action.sa_flags = libc::SA_RESTART;
        libc::sigemptyset(&mut action.sa_mask);
        libc::sigaction(libc::SIGTERM, &action, std::ptr::null_mut());
        libc::sigaction(libc::SIGINT, &action, std::ptr::null_mut());
    }
}

#[cfg(not(unix))]
fn install_signal_handlers() {}

fn interruptible_sleep(duration: Duration) {
    let mut remaining = duration;
    while !remaining.is_zero() && !SHUTDOWN.load(Ordering::Acquire) {
        let step = remaining.min(Duration::from_millis(100));
        std::thread::sleep(step);
        remaining = remaining.saturating_sub(step);
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn request_manifest() -> GatewayProofRequestManifest {
        GatewayProofRequestManifest {
            schema_version: 1,
            request_id: "ab".repeat(32),
            request_hash: "ab".repeat(32),
            binding_hash: "bc".repeat(32),
            proof_system: 1,
            aggregation_backend_id: 0xc2,
            verification_key: "47".repeat(32),
            request_file: format!("{}{}", "ab".repeat(32), REQUEST_PAYLOAD_SUFFIX),
        }
    }

    fn artifacts() -> GatewayProofArtifacts {
        GatewayProofArtifacts {
            proof_bytes: vec![0x11; 356],
            verification_key: [0x47; 32],
            public_values: [0x22; 33],
        }
    }

    #[test]
    fn result_manifest_is_published_last() {
        let directory = tempfile::tempdir().unwrap();
        let request = request_manifest();
        let conflict = directory
            .path()
            .join(format!("{}{}", request.request_id, PUBLIC_VALUES_SUFFIX));
        std::fs::write(&conflict, b"conflict").unwrap();

        assert!(publish_artifacts(directory.path(), &request, &artifacts()).is_err());
        assert!(
            !directory
                .path()
                .join(format!("{}{}", request.request_id, RESULT_MANIFEST_SUFFIX))
                .exists()
        );
    }

    #[test]
    fn atomic_publication_is_exact_and_idempotent() {
        let directory = tempfile::tempdir().unwrap();
        let path = directory.path().join("artifact");
        publish_exact_atomic(&path, b"exact").unwrap();
        publish_exact_atomic(&path, b"exact").unwrap();
        assert_eq!(std::fs::read(path).unwrap(), b"exact");
        assert!(publish_exact_atomic(&directory.path().join("artifact"), b"other").is_err());
    }

    #[test]
    fn orphan_outputs_are_removed_before_reproving() {
        let directory = tempfile::tempdir().unwrap();
        let request = request_manifest();
        let request_path = directory.path().join(&request.request_file);
        std::fs::write(&request_path, b"request").unwrap();
        for suffix in [PROOF_SUFFIX, VERIFICATION_KEY_SUFFIX, PUBLIC_VALUES_SUFFIX] {
            std::fs::write(
                directory
                    .path()
                    .join(format!("{}{}", request.request_id, suffix)),
                b"partial",
            )
            .unwrap();
        }

        remove_orphan_artifacts(directory.path(), &request.request_id).unwrap();

        assert!(request_path.exists());
        for suffix in [PROOF_SUFFIX, VERIFICATION_KEY_SUFFIX, PUBLIC_VALUES_SUFFIX] {
            assert!(
                !directory
                    .path()
                    .join(format!("{}{}", request.request_id, suffix))
                    .exists()
            );
        }
    }

    #[cfg(unix)]
    #[test]
    fn orphan_cleanup_rejects_symlink_artifacts() {
        use std::os::unix::fs::symlink;
        let directory = tempfile::tempdir().unwrap();
        let request = request_manifest();
        let target = directory.path().join("target");
        std::fs::write(&target, b"proof").unwrap();
        symlink(
            &target,
            directory
                .path()
                .join(format!("{}{}", request.request_id, PROOF_SUFFIX)),
        )
        .unwrap();

        assert!(remove_orphan_artifacts(directory.path(), &request.request_id).is_err());
        assert!(target.exists());
    }

    #[cfg(unix)]
    #[test]
    fn queue_lock_rejects_symlink() {
        use std::os::unix::fs::symlink;
        let directory = tempfile::tempdir().unwrap();
        let target = directory.path().join("target");
        let lock = directory.path().join(".prove-gateway.lock");
        std::fs::write(&target, b"lock").unwrap();
        symlink(&target, &lock).unwrap();
        assert!(acquire_queue_lock(directory.path()).is_err());
    }

    #[cfg(unix)]
    #[test]
    fn queue_reader_rejects_symlink() {
        use std::os::unix::fs::symlink;
        let directory = tempfile::tempdir().unwrap();
        let target = directory.path().join("target");
        let link = directory.path().join("link");
        std::fs::write(&target, b"data").unwrap();
        symlink(&target, &link).unwrap();
        assert!(read_regular_bounded(&link, 4).is_err());
    }
}
