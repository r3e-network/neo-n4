//! Build script: invokes the pinned Docker `cargo prove build` on
//! bridge/neo-zkvm-guest so the guest ELF is reproducible and fresh at compile
//! time. A cached ELF may only be used when
//! `NEO_ZKVM_ALLOW_CACHED_ELF=1` is set explicitly for host-only development.

use sha2::{Digest, Sha256};
use sp1_sdk::{
    HashableKey, ProvingKey,
    blocking::{Elf, LightProver, Prover},
};
use std::{env, fs, path::PathBuf, process::Command};

#[path = "../sp1_build_support.rs"]
mod sp1_build_support;

#[allow(dead_code)]
mod pinned {
    include!("../neo-zkvm-guest/vk_manifest.rs");
}

fn main() {
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/src");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/tests/fixtures/stateful_batch_v1.hex");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/tests/fixtures/native_transition_v1.hex");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/Cargo.toml");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/vk_manifest.rs");
    println!("cargo:rerun-if-changed=../neo-execution-core/Cargo.toml");
    println!("cargo:rerun-if-changed=../neo-execution-core/src");
    println!("cargo:rerun-if-changed=../../external/neo-vm-rs/Cargo.toml");
    println!("cargo:rerun-if-changed=../../external/neo-vm-rs/src");
    println!("cargo:rerun-if-changed=../sp1_build_support.rs");
    println!("cargo:rerun-if-env-changed=NEO_ZKVM_ALLOW_CACHED_ELF");
    println!("cargo:rerun-if-env-changed=SP1_DOCKER_IMAGE");

    if env::var("CARGO_CFG_TARGET_FAMILY").as_deref() != Ok("unix") {
        println!(
            "cargo:warning=neo-zkvm-host SP1 proving is Unix/WSL2-only; compiling unsupported-platform stubs"
        );
        return;
    }

    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
    let output_dir = PathBuf::from(env::var("OUT_DIR").unwrap());
    let guest_dir = manifest_dir.join("../neo-zkvm-guest");
    let allow_cached_elf = allow_cached_elf();

    // Prefer building fresh through SP1's pinned Docker image. This guarantees
    // the bundled ELF tracks the source without inheriting host paths or host
    // compiler differences into the program verification key.
    let cargo_prove = which_or_default();
    let mut prove_build = Command::new(&cargo_prove);
    sp1_build_support::configure_reproducible_build(&mut prove_build);
    prove_build.current_dir(&guest_dir);
    sp1_build_support::sanitize_nested_build_environment(&mut prove_build);
    let workspace_target = manifest_dir.join("../../target");
    let _build_lock = sp1_build_support::acquire_reproducible_build_lock()
        .unwrap_or_else(|error| panic!("acquire SP1 Docker build lock: {error}"));
    sp1_build_support::isolate_loopback_docker_proxy(
        &mut prove_build,
        &workspace_target.join("sp1-docker-config"),
    )
    .unwrap_or_else(|error| panic!("prepare isolated Docker configuration: {error}"));
    let output = prove_build.output();

    let elf_path = sp1_build_support::docker_elf_path(&workspace_target, "neo-zkvm-guest");

    match output {
        Ok(o) if o.status.success() => {
            // Built fresh.
        }
        Ok(o) => {
            let detail = output_excerpt(&o.stdout, &o.stderr);
            if !allow_cached_elf {
                panic!(
                    "reproducible cargo prove build failed (exit {}) while building {}; install SP1 plus Docker or set NEO_ZKVM_ALLOW_CACHED_ELF=1 only for host-only development{}",
                    o.status,
                    guest_dir.display(),
                    detail
                );
            }
            println!(
                "cargo:warning=reproducible cargo prove build failed (exit {}), using cached ELF because NEO_ZKVM_ALLOW_CACHED_ELF=1{}",
                o.status, detail
            );
        }
        Err(error) => {
            if !allow_cached_elf {
                panic!(
                    "cargo prove is not available on PATH ({error}); install SP1 plus Docker or set NEO_ZKVM_ALLOW_CACHED_ELF=1 only for host-only development"
                );
            }
            println!(
                "cargo:warning=cargo prove not on PATH ({error}), using cached ELF because NEO_ZKVM_ALLOW_CACHED_ELF=1 at {}",
                elf_path.display()
            );
        }
    }

    if !elf_path.exists() {
        panic!(
            "guest ELF not found at {}; run the pinned Docker `cargo prove build --docker --locked` in {}",
            elf_path.display(),
            guest_dir.display()
        );
    }
    let elf =
        fs::read(&elf_path).unwrap_or_else(|error| panic!("read {}: {error}", elf_path.display()));
    validate_pinned_artifact(&elf);
    let verified_elf = sp1_build_support::publish_verified_artifact(
        &output_dir,
        "neo-zkvm-guest.verified.elf",
        &elf,
    )
    .unwrap_or_else(|error| panic!("publish verified batch guest ELF: {error}"));
    println!(
        "cargo:rustc-env=NEO_ZKVM_GUEST_ELF={}",
        verified_elf.display()
    );
}

fn validate_pinned_artifact(elf: &[u8]) {
    let elf_sha256: [u8; 32] = Sha256::digest(elf).into();
    if elf_sha256 != pinned::PINNED_BATCH_ELF_SHA256 {
        panic!(
            "batch guest ELF SHA-256 mismatch: actual={elf_sha256:?} pinned={:?}",
            pinned::PINNED_BATCH_ELF_SHA256
        );
    }
    let leaked = Box::leak(elf.to_vec().into_boxed_slice());
    let prover = LightProver::new();
    let proving_key = prover
        .setup(Elf::Static(leaked))
        .unwrap_or_else(|error| panic!("derive batch guest VK: {error:?}"));
    let verification_key = proving_key.verifying_key().bytes32_raw();
    if verification_key != pinned::PINNED_BATCH_VK_BYTES32 {
        panic!(
            "batch guest VK mismatch: actual={verification_key:?} pinned={:?}",
            pinned::PINNED_BATCH_VK_BYTES32
        );
    }
}

fn output_excerpt(stdout: &[u8], stderr: &[u8]) -> String {
    let combined = format!(
        "\nstdout:\n{}\nstderr:\n{}",
        String::from_utf8_lossy(stdout),
        String::from_utf8_lossy(stderr)
    );
    let lines: Vec<_> = combined.lines().take(24).collect();
    if lines.iter().all(|line| line.trim().is_empty()) {
        String::new()
    } else {
        format!("; {}", lines.join(" | "))
    }
}

fn allow_cached_elf() -> bool {
    matches!(
        env::var("NEO_ZKVM_ALLOW_CACHED_ELF").as_deref(),
        Ok("1") | Ok("true") | Ok("TRUE") | Ok("yes") | Ok("YES")
    )
}

fn which_or_default() -> String {
    if let Ok(path) = env::var("CARGO_PROVE") {
        return path;
    }
    if let Some(home) = env::var_os("HOME") {
        let sp1up_path = PathBuf::from(home).join(".sp1/bin/cargo-prove");
        if sp1up_path.is_file() {
            return sp1up_path.display().to_string();
        }
    }
    // Most operators have cargo-prove on PATH after `sp1up`.
    "cargo".to_string()
}
