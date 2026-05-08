//! Build script: invokes `cargo prove build` on bridge/neo-zkvm-guest so
//! the guest ELF is fresh at compile time. Falls back to the cached ELF if
//! the SP1 toolchain isn't available (host-only test runs that don't need
//! the live guest).

use std::env;
use std::path::PathBuf;
use std::process::Command;

fn main() {
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/src/main.rs");
    println!("cargo:rerun-if-changed=../neo-zkvm-guest/Cargo.toml");

    let manifest_dir = PathBuf::from(env::var("CARGO_MANIFEST_DIR").unwrap());
    let guest_dir = manifest_dir.join("../neo-zkvm-guest");

    // Prefer building fresh via `cargo prove build` if the SP1 toolchain is on
    // PATH. This guarantees the bundled ELF tracks the guest source.
    let cargo_prove = which_or_default();
    let status = Command::new(&cargo_prove)
        .arg("prove")
        .arg("build")
        .current_dir(&guest_dir)
        .status();

    let workspace_target = manifest_dir.join("../../target");
    let elf_path = workspace_target
        .join("elf-compilation/riscv64im-succinct-zkvm-elf/release/neo-zkvm-guest");

    match status {
        Ok(s) if s.success() => {
            // Built fresh.
        }
        Ok(s) => {
            println!(
                "cargo:warning=cargo prove build failed (exit {}), falling back to cached ELF",
                s
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

fn which_or_default() -> String {
    // Most operators have cargo-prove on PATH after `sp1up`.
    "cargo".to_string()
}
