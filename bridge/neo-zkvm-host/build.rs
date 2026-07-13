//! Build script: invokes `cargo prove build` on bridge/neo-zkvm-guest so
//! the guest ELF is fresh at compile time. A cached ELF may only be used when
//! `NEO_ZKVM_ALLOW_CACHED_ELF=1` is set explicitly for host-only development.

use std::env;
use std::path::PathBuf;
use std::process::Command;

fn main() {
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/src");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/tests/fixtures/stateful_batch_v1.hex");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/Cargo.toml");
    println!("cargo:rerun-if-changed=../neo-execution-core/Cargo.toml");
    println!("cargo:rerun-if-changed=../neo-execution-core/src");
    println!("cargo:rerun-if-changed=../../external/neo-vm-rs/Cargo.toml");
    println!("cargo:rerun-if-changed=../../external/neo-vm-rs/src");
    println!("cargo:rerun-if-env-changed=NEO_ZKVM_ALLOW_CACHED_ELF");

    if env::var("CARGO_CFG_TARGET_FAMILY").as_deref() != Ok("unix") {
        println!(
            "cargo:warning=neo-zkvm-host SP1 proving is Unix/WSL2-only; compiling unsupported-platform stubs"
        );
        return;
    }

    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
    let guest_dir = manifest_dir.join("../neo-zkvm-guest");
    let allow_cached_elf = allow_cached_elf();

    // Prefer building fresh via `cargo prove build` if the SP1 toolchain is on
    // PATH. This guarantees the bundled ELF tracks the guest source.
    let cargo_prove = which_or_default();
    let mut prove_build = Command::new(&cargo_prove);
    prove_build
        .arg("prove")
        .arg("build")
        .current_dir(&guest_dir);
    strip_outer_cargo_tooling(&mut prove_build);
    let output = prove_build.output();

    let workspace_target = manifest_dir.join("../../target");
    let elf_path =
        workspace_target.join("elf-compilation/riscv64im-succinct-zkvm-elf/release/neo-zkvm-guest");

    match output {
        Ok(o) if o.status.success() => {
            // Built fresh.
        }
        Ok(o) => {
            let detail = output_excerpt(&o.stdout, &o.stderr);
            if !allow_cached_elf {
                panic!(
                    "cargo prove build failed (exit {}) while building {}; install the SP1 toolchain or set NEO_ZKVM_ALLOW_CACHED_ELF=1 only for host-only development{}",
                    o.status,
                    guest_dir.display(),
                    detail
                );
            }
            println!(
                "cargo:warning=cargo prove build failed (exit {}), using cached ELF because NEO_ZKVM_ALLOW_CACHED_ELF=1{}",
                o.status, detail
            );
        }
        Err(error) => {
            if !allow_cached_elf {
                panic!(
                    "cargo prove is not available on PATH ({error}); install SP1 with sp1up or set NEO_ZKVM_ALLOW_CACHED_ELF=1 only for host-only development"
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
            "guest ELF not found at {}; run `cargo prove build` in {}",
            elf_path.display(),
            guest_dir.display()
        );
    }
    println!("cargo:rustc-env=NEO_ZKVM_GUEST_ELF={}", elf_path.display());
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

fn strip_outer_cargo_tooling(command: &mut Command) {
    // `cargo clippy` and some CI wrappers inject rustc wrappers/flags into the
    // build-script environment. The nested SP1 guest build uses the succinct
    // RISC-V toolchain and should not inherit host compiler instrumentation.
    for key in [
        "RUSTC",
        "RUSTC_WRAPPER",
        "RUSTC_WORKSPACE_WRAPPER",
        "RUSTFLAGS",
        "CARGO_ENCODED_RUSTFLAGS",
    ] {
        command.env_remove(key);
    }
}
