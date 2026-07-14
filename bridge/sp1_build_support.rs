use std::{
    env,
    fs::{self, File, OpenOptions},
    io::{self, Write},
    path::{Path, PathBuf},
    process::Command,
};

pub const SP1_DOCKER_IMAGE: &str = "ghcr.io/succinctlabs/sp1@sha256:14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f";

const DOCKER_ELF_DIRECTORY: &str = "elf-compilation/docker/riscv64im-succinct-zkvm-elf/release";
const DOCKER_BUILD_LOCK_FILE: &str = "neo-n4-sp1-docker-build-v1.lock";

pub fn acquire_reproducible_build_lock() -> io::Result<File> {
    let path = env::temp_dir().join(DOCKER_BUILD_LOCK_FILE);
    let file = OpenOptions::new()
        .create(true)
        .read(true)
        .write(true)
        .truncate(false)
        .open(&path)?;
    lock_exclusive(&file)?;
    Ok(file)
}

#[cfg(unix)]
fn lock_exclusive(file: &File) -> io::Result<()> {
    use std::os::fd::AsRawFd;

    // SAFETY: `file` owns a valid open file descriptor for the duration of
    // this call, and `flock` neither retains nor dereferences a Rust pointer.
    let result = unsafe { libc::flock(file.as_raw_fd(), libc::LOCK_EX) };
    if result == 0 {
        Ok(())
    } else {
        Err(io::Error::last_os_error())
    }
}

#[cfg(not(unix))]
fn lock_exclusive(_file: &File) -> io::Result<()> {
    Ok(())
}

pub fn configure_reproducible_build(command: &mut Command) {
    let configured_image =
        env::var("SP1_DOCKER_IMAGE").unwrap_or_else(|_| SP1_DOCKER_IMAGE.to_owned());
    assert_eq!(
        configured_image, SP1_DOCKER_IMAGE,
        "SP1_DOCKER_IMAGE must match the audited immutable amd64 image"
    );
    command.args(["prove", "build", "--docker", "--locked"]);
    command.env("SP1_DOCKER_IMAGE", configured_image);
}

pub fn sanitize_nested_build_environment(command: &mut Command) {
    for key in [
        "RUSTC",
        "RUSTC_WRAPPER",
        "RUSTC_WORKSPACE_WRAPPER",
        "RUSTFLAGS",
        "CARGO_ENCODED_RUSTFLAGS",
        "CARGO_TARGET_DIR",
    ] {
        command.env_remove(key);
    }

    for key in [
        "HTTP_PROXY",
        "HTTPS_PROXY",
        "ALL_PROXY",
        "http_proxy",
        "https_proxy",
        "all_proxy",
    ] {
        if env::var(key).is_ok_and(|value| is_loopback_proxy(&value)) {
            command.env_remove(key);
        }
    }
}

pub fn isolate_loopback_docker_proxy(
    command: &mut Command,
    scratch_directory: &Path,
) -> io::Result<()> {
    let Some(source_directory) = docker_config_directory() else {
        return Ok(());
    };
    let source_config = source_directory.join("config.json");
    let Ok(contents) = fs::read(&source_config) else {
        return Ok(());
    };
    let mut config: serde_json::Value = serde_json::from_slice(&contents).map_err(|error| {
        io::Error::new(
            io::ErrorKind::InvalidData,
            format!("parse {}: {error}", source_config.display()),
        )
    })?;
    if !isolate_loopback_docker_config(&mut config) {
        return Ok(());
    }

    if scratch_directory.exists() {
        fs::remove_dir_all(scratch_directory)?;
    }
    fs::create_dir_all(scratch_directory)?;
    fs::write(
        scratch_directory.join("config.json"),
        serde_json::to_vec_pretty(&config).map_err(io::Error::other)?,
    )?;
    for name in ["contexts", "tls"] {
        let source = source_directory.join(name);
        if source.is_dir() {
            copy_directory(&source, &scratch_directory.join(name))?;
        }
    }
    command.env("DOCKER_CONFIG", scratch_directory);
    Ok(())
}

fn docker_config_directory() -> Option<PathBuf> {
    env::var_os("DOCKER_CONFIG").map(PathBuf::from).or_else(|| {
        env::var_os("HOME")
            .map(PathBuf::from)
            .map(|home| home.join(".docker"))
    })
}

fn isolate_loopback_docker_config(config: &mut serde_json::Value) -> bool {
    if !remove_loopback_docker_proxies(config) {
        return false;
    }
    let mut isolated_config = serde_json::Map::new();
    if let Some(current_context) = config.get("currentContext").cloned() {
        isolated_config.insert("currentContext".to_owned(), current_context);
    }
    *config = serde_json::Value::Object(isolated_config);
    true
}

fn remove_loopback_docker_proxies(config: &mut serde_json::Value) -> bool {
    let Some(proxies) = config
        .get_mut("proxies")
        .and_then(serde_json::Value::as_object_mut)
    else {
        return false;
    };
    let mut changed = false;
    for settings in proxies.values_mut() {
        let Some(settings) = settings.as_object_mut() else {
            continue;
        };
        settings.retain(|key, value| {
            let is_proxy =
                key.to_ascii_lowercase().ends_with("proxy") && !key.eq_ignore_ascii_case("noProxy");
            let remove = is_proxy && value.as_str().is_some_and(is_loopback_proxy);
            changed |= remove;
            !remove
        });
    }
    changed
}

fn copy_directory(source: &Path, destination: &Path) -> io::Result<()> {
    fs::create_dir_all(destination)?;
    for entry in fs::read_dir(source)? {
        let entry = entry?;
        let source_path = entry.path();
        let destination_path = destination.join(entry.file_name());
        if entry.file_type()?.is_dir() {
            copy_directory(&source_path, &destination_path)?;
        } else {
            fs::copy(source_path, destination_path)?;
        }
    }
    Ok(())
}

fn is_loopback_proxy(value: &str) -> bool {
    value.contains("127.0.0.1") || value.contains("localhost") || value.contains("[::1]")
}

pub fn docker_elf_path(workspace_target: &Path, binary: &str) -> PathBuf {
    workspace_target.join(DOCKER_ELF_DIRECTORY).join(binary)
}

pub fn publish_verified_artifact(
    output_directory: &Path,
    file_name: &str,
    bytes: &[u8],
) -> io::Result<PathBuf> {
    if file_name.is_empty()
        || file_name == "."
        || file_name == ".."
        || Path::new(file_name)
            .file_name()
            .and_then(|name| name.to_str())
            != Some(file_name)
    {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            "verified artifact name must be one UTF-8 path leaf",
        ));
    }

    let directory_metadata = fs::symlink_metadata(output_directory)?;
    if directory_metadata.file_type().is_symlink() || !directory_metadata.is_dir() {
        return Err(io::Error::new(
            io::ErrorKind::InvalidInput,
            format!(
                "verified artifact output must be a real directory: {}",
                output_directory.display()
            ),
        ));
    }

    let destination = output_directory.join(file_name);
    match fs::symlink_metadata(&destination) {
        Ok(metadata) if metadata.file_type().is_symlink() || !metadata.is_file() => {
            return Err(io::Error::new(
                io::ErrorKind::InvalidInput,
                format!(
                    "verified artifact destination must be a regular file: {}",
                    destination.display()
                ),
            ));
        }
        Ok(_) => fs::remove_file(&destination)?,
        Err(error) if error.kind() == io::ErrorKind::NotFound => {}
        Err(error) => return Err(error),
    }

    let temporary = create_private_temporary(output_directory, file_name)?;
    let temporary_path = temporary.0;
    let mut file = temporary.1;
    let result = (|| {
        file.write_all(bytes)?;
        file.sync_all()?;
        make_read_only(&file)?;
        file.sync_all()?;
        fs::rename(&temporary_path, &destination)?;
        File::open(output_directory)?.sync_all()?;
        Ok(destination.clone())
    })();
    drop(file);
    if result.is_err() {
        let _ = fs::remove_file(&temporary_path);
    }
    result
}

fn create_private_temporary(
    output_directory: &Path,
    file_name: &str,
) -> io::Result<(PathBuf, File)> {
    for sequence in 0..128u8 {
        let path = output_directory.join(format!(
            ".{file_name}.tmp-{}-{sequence}",
            std::process::id()
        ));
        let mut options = OpenOptions::new();
        options.create_new(true).write(true);
        #[cfg(unix)]
        {
            use std::os::unix::fs::OpenOptionsExt;
            options.mode(0o600);
        }
        match options.open(&path) {
            Ok(file) => return Ok((path, file)),
            Err(error) if error.kind() == io::ErrorKind::AlreadyExists => continue,
            Err(error) => return Err(error),
        }
    }
    Err(io::Error::new(
        io::ErrorKind::AlreadyExists,
        "could not allocate a unique verified-artifact temporary file",
    ))
}

#[cfg(unix)]
fn make_read_only(file: &File) -> io::Result<()> {
    use std::os::unix::fs::PermissionsExt;

    file.set_permissions(fs::Permissions::from_mode(0o400))
}

#[cfg(not(unix))]
fn make_read_only(file: &File) -> io::Result<()> {
    let mut permissions = file.metadata()?.permissions();
    permissions.set_readonly(true);
    file.set_permissions(permissions)
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;
    use std::time::{SystemTime, UNIX_EPOCH};

    #[test]
    fn loopback_proxy_is_replaced_with_minimal_context_only_config() {
        let mut config = json!({
            "auths": {"registry.example": {"auth": "secret"}},
            "credsStore": "desktop",
            "currentContext": "colima-sp1",
            "proxies": {
                "default": {
                    "httpProxy": "http://127.0.0.1:7890",
                    "httpsProxy": "http://localhost:7890",
                    "noProxy": "localhost,127.0.0.1"
                }
            }
        });

        assert!(isolate_loopback_docker_config(&mut config));
        assert_eq!(json!({"currentContext": "colima-sp1"}), config);
    }

    #[test]
    fn reachable_proxy_config_is_left_unchanged() {
        let original = json!({
            "currentContext": "remote",
            "proxies": {
                "default": {"httpsProxy": "https://proxy.example:8443"}
            }
        });
        let mut config = original.clone();

        assert!(!isolate_loopback_docker_config(&mut config));
        assert_eq!(original, config);
    }

    #[test]
    fn verified_artifact_is_an_exact_private_snapshot() {
        let directory = temporary_directory("verified-snapshot");
        let path = publish_verified_artifact(&directory, "guest.elf", b"verified").unwrap();

        assert_eq!(fs::read(&path).unwrap(), b"verified");
        assert!(fs::symlink_metadata(&path).unwrap().is_file());
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            assert_eq!(
                fs::metadata(&path).unwrap().permissions().mode() & 0o777,
                0o400
            );
        }

        fs::remove_dir_all(directory).unwrap();
    }

    #[cfg(unix)]
    #[test]
    fn verified_artifact_rejects_symbolic_link_destination() {
        use std::os::unix::fs::symlink;

        let directory = temporary_directory("verified-symlink");
        let outside = directory.with_extension("outside");
        fs::write(&outside, b"outside").unwrap();
        symlink(&outside, directory.join("guest.elf")).unwrap();

        assert!(publish_verified_artifact(&directory, "guest.elf", b"verified").is_err());
        assert_eq!(fs::read(&outside).unwrap(), b"outside");

        fs::remove_dir_all(directory).unwrap();
        fs::remove_file(outside).unwrap();
    }

    fn temporary_directory(label: &str) -> PathBuf {
        let nonce = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        let directory = env::temp_dir().join(format!(
            "neo-n4-sp1-build-support-{label}-{}-{nonce}",
            std::process::id()
        ));
        fs::create_dir(&directory).unwrap();
        directory
    }
}
