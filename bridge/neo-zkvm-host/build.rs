//! Build script: invokes `cargo prove build` on bridge/neo-zkvm-guest so
//! the guest ELF is fresh at compile time. Falls back to the cached ELF if
//! the SP1 toolchain isn't available (host-only test runs that don't need
//! the live guest).

use std::env;
use std::path::PathBuf;
use std::process::Command;

fn main() {
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/src/main.rs");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/src/lib.rs");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/Cargo.toml");

    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
    let guest_dir = manifest_dir.join("../neo-zkvm-guest");

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
            println!(
                "cargo:warning=cargo prove build failed (exit {}), falling back to cached ELF{}",
                o.status,
                output_excerpt(&o.stdout, &o.stderr)
            );
        }
        Err(e) => {
            println!(
                "cargo:warning=cargo prove not on PATH ({}), assuming cached ELF at {}",
                e,
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

fn which_or_default() -> String {
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
