use std::{env, fs, path::PathBuf};

#[allow(dead_code)]
mod pinned {
    include!("batch_vk_manifest.rs");
}

const TEST_ONLY_BATCH_VK: [u32; 8] = [
    0x5445_5354,
    0x4f4e_4c59,
    0x4e45_4f34,
    0x4757_5631,
    0x0102_0304,
    0x1122_3344,
    0x5566_7788,
    0x99aa_bbcc,
];

fn main() {
    println!("cargo:rerun-if-changed=batch_vk_manifest.rs");
    println!("cargo:rerun-if-env-changed=CARGO_FEATURE_TEST_ONLY_VK");

    let test_only = env::var_os("CARGO_FEATURE_TEST_ONLY_VK").is_some();
    let words = if test_only {
        TEST_ONLY_BATCH_VK
    } else {
        pinned::PINNED_BATCH_VK_WORDS
    };
    if words.iter().all(|word| *word == 0) {
        panic!("the compile-time batch verification key must not be zero");
    }

    let output = PathBuf::from(env::var_os("OUT_DIR").expect("OUT_DIR"));
    let source = format!(
        "pub const BATCH_VK_WORDS: [u32; 8] = {words:?};\npub const BATCH_VK_IS_TEST_ONLY: bool = {test_only};\n"
    );
    fs::write(output.join("batch_vk.rs"), source).expect("write batch_vk.rs");
}
