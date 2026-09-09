//! Dev helper: print SP1 batch VK hash_u32() / bytes32_raw() from a verified guest ELF.
//! Run on Unix/WSL2 with Docker optional when NEO_ZKVM_ALLOW_CACHED_ELF=1 and a verified ELF exists.
//!
//! ```bash
//! NEO_ZKVM_ALLOW_CACHED_ELF=1 cargo run -p neo-zkvm-host --example print_batch_vk_words --release
//! ```

use sha2::{Digest, Sha256};
use sp1_sdk::{
    HashableKey, ProvingKey,
    blocking::{Elf, LightProver, Prover},
};
use std::path::PathBuf;

fn main() {
    let manifest = PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    let candidates = [
        manifest.join("../../target/release/build"),
        manifest.join("../../target/debug/build"),
    ];
    let mut elf_path = None;
    for root in candidates {
        if !root.exists() {
            continue;
        }
        for entry in std::fs::read_dir(&root).expect("read build dir") {
            let entry = entry.expect("dir entry");
            let path = entry.path().join("out/neo-zkvm-guest.verified.elf");
            if path.is_file() {
                elf_path = Some(path);
                break;
            }
        }
        if elf_path.is_some() {
            break;
        }
    }
    let elf_path = elf_path.expect("no neo-zkvm-guest.verified.elf under target/*/build/*/out");
    let bytes = std::fs::read(&elf_path).expect("read ELF");
    let sha = Sha256::digest(&bytes);
    println!("elf={}", elf_path.display());
    println!("elf_sha256={}", hex::encode(sha));

    let prover = LightProver::new();
    let pk = prover
        .setup(Elf::Static(Box::leak(bytes.into_boxed_slice())))
        .expect("setup batch guest");
    let words = pk.verifying_key().hash_u32();
    let bytes32 = pk.verifying_key().bytes32_raw();
    println!("PINNED_BATCH_VK_WORDS: {words:?}");
    print!("PINNED_BATCH_VK_BYTES32: [");
    for (i, b) in bytes32.iter().enumerate() {
        if i > 0 {
            print!(", ");
        }
        print!("0x{b:02x}");
    }
    println!("]");
}
