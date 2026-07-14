use sha2::{Digest, Sha256};
use sp1_sdk::{
    HashableKey, ProvingKey,
    blocking::{Elf, LightProver, Prover},
};
use std::{
    env, fs,
    path::{Path, PathBuf},
    process::Command,
};

#[path = "../sp1_build_support.rs"]
mod sp1_build_support;

mod pinned {
    include!("../neo-zkvm-gateway-guest/batch_vk_manifest.rs");
    include!("../neo-zkvm-guest/vk_manifest.rs");
    include!("../neo-zkvm-gateway-guest/vk_manifest.rs");
}

const TEST_BATCH_VK_WORDS: [u32; 8] = [
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
    for path in [
        "../neo-zkvm-gateway-guest/src",
        "../neo-zkvm-gateway-guest/Cargo.toml",
        "../neo-zkvm-gateway-guest/build.rs",
        "../neo-zkvm-gateway-guest/batch_vk_manifest.rs",
        "../neo-zkvm-gateway-guest/vk_manifest.rs",
        "../neo-zkvm-guest/vk_manifest.rs",
        "../neo-zkvm-guest/src",
        "../neo-zkvm-guest/Cargo.toml",
        "../neo-execution-core/src",
        "../neo-execution-core/Cargo.toml",
        "../sp1_build_support.rs",
    ] {
        println!("cargo:rerun-if-changed={path}");
    }
    println!("cargo:rerun-if-env-changed=CARGO_FEATURE_TEST_ONLY_VK");
    println!("cargo:rerun-if-env-changed=CARGO_PROVE");
    println!("cargo:rerun-if-env-changed=SP1_DOCKER_IMAGE");

    let manifest_dir = PathBuf::from(env::var_os("CARGO_MANIFEST_DIR").expect("manifest dir"));
    let output_dir = PathBuf::from(env::var_os("OUT_DIR").expect("OUT_DIR"));
    let test_only = env::var_os("CARGO_FEATURE_TEST_ONLY_VK").is_some();

    let artifacts = if test_only {
        test_only_artifacts(&output_dir)
    } else {
        production_artifacts(&manifest_dir, &output_dir)
    };
    write_outputs(&output_dir, &artifacts, test_only);
}

struct BuildArtifacts {
    batch_elf: PathBuf,
    gateway_elf: PathBuf,
    batch_vk_words: [u32; 8],
    batch_vk_bytes32: [u8; 32],
    gateway_vk_bytes32: [u8; 32],
    batch_elf_sha256: [u8; 32],
    gateway_elf_sha256: [u8; 32],
}

fn production_artifacts(manifest_dir: &Path, output_dir: &Path) -> BuildArtifacts {
    if env::var("CARGO_CFG_TARGET_FAMILY").as_deref() != Ok("unix") {
        panic!("production Gateway SP1 proving is supported only on Unix/WSL2 targets");
    }
    let workspace_target = manifest_dir.join("../../target");
    let batch_guest = manifest_dir.join("../neo-zkvm-guest");
    run_cargo_prove(&batch_guest, &workspace_target);
    let batch_source = sp1_build_support::docker_elf_path(&workspace_target, "neo-zkvm-guest");
    let batch_bytes = read_required_file(&batch_source, "batch guest ELF");

    let prover = LightProver::new();
    let batch_pk = prover
        .setup(Elf::Static(Box::leak(
            batch_bytes.clone().into_boxed_slice(),
        )))
        .unwrap_or_else(|error| panic!("derive batch guest VK: {error:?}"));
    let batch_vk_words = batch_pk.verifying_key().hash_u32();
    let batch_vk_bytes32 = batch_pk.verifying_key().bytes32_raw();
    require_nonzero(&batch_vk_words, "batch VK words");
    require_nonzero(&batch_vk_bytes32, "batch VK bytes32");
    let batch_elf_sha256 = Sha256::digest(&batch_bytes).into();

    let gateway_guest = manifest_dir.join("../neo-zkvm-gateway-guest");
    run_cargo_prove(&gateway_guest, &workspace_target);
    let gateway_source =
        sp1_build_support::docker_elf_path(&workspace_target, "neo-zkvm-gateway-guest");
    let gateway_bytes = read_required_file(&gateway_source, "Gateway guest ELF");
    let gateway_pk = prover
        .setup(Elf::Static(Box::leak(
            gateway_bytes.clone().into_boxed_slice(),
        )))
        .unwrap_or_else(|error| panic!("derive Gateway guest VK: {error:?}"));
    let gateway_vk_bytes32 = gateway_pk.verifying_key().bytes32_raw();
    require_nonzero(&gateway_vk_bytes32, "Gateway VK bytes32");
    let gateway_elf_sha256 = Sha256::digest(&gateway_bytes).into();

    validate_pinned_artifacts(
        &batch_vk_words,
        &batch_vk_bytes32,
        &gateway_vk_bytes32,
        &batch_elf_sha256,
        &gateway_elf_sha256,
    );

    let batch_elf = sp1_build_support::publish_verified_artifact(
        output_dir,
        "neo-zkvm-batch-guest.verified.elf",
        &batch_bytes,
    )
    .unwrap_or_else(|error| panic!("publish verified batch guest ELF: {error}"));
    let gateway_elf = sp1_build_support::publish_verified_artifact(
        output_dir,
        "neo-zkvm-gateway-guest.verified.elf",
        &gateway_bytes,
    )
    .unwrap_or_else(|error| panic!("publish verified Gateway guest ELF: {error}"));

    BuildArtifacts {
        batch_elf_sha256,
        gateway_elf_sha256,
        batch_elf,
        gateway_elf,
        batch_vk_words,
        batch_vk_bytes32,
        gateway_vk_bytes32,
    }
}

fn test_only_artifacts(output_dir: &Path) -> BuildArtifacts {
    let placeholder = sp1_build_support::publish_verified_artifact(
        output_dir,
        "test-only-placeholder.elf",
        b"NEO4 TEST ONLY - NOT A PROVING ELF",
    )
    .expect("write placeholder");
    BuildArtifacts {
        batch_elf: placeholder.clone(),
        gateway_elf: placeholder,
        batch_vk_words: TEST_BATCH_VK_WORDS,
        batch_vk_bytes32: [0x42; 32],
        gateway_vk_bytes32: [0x47; 32],
        batch_elf_sha256: [0u8; 32],
        gateway_elf_sha256: [0u8; 32],
    }
}

fn write_outputs(output_dir: &Path, artifacts: &BuildArtifacts, test_only: bool) {
    println!(
        "cargo:rustc-env=NEO_ZKVM_BATCH_GUEST_ELF={}",
        artifacts.batch_elf.display()
    );
    println!(
        "cargo:rustc-env=NEO_ZKVM_GATEWAY_GUEST_ELF={}",
        artifacts.gateway_elf.display()
    );

    let manifest = format!(
        concat!(
            "{{\"schemaVersion\":1,\"sp1Version\":\"{}\",\"testOnly\":{},",
            "\"batchVkWords\":[{}],\"batchVkBytes32\":\"{}\",",
            "\"gatewayVkBytes32\":\"{}\",\"batchElfSha256\":\"{}\",",
            "\"gatewayElfSha256\":\"{}\"}}"
        ),
        pinned::SP1_VERSION,
        test_only,
        artifacts
            .batch_vk_words
            .iter()
            .map(u32::to_string)
            .collect::<Vec<_>>()
            .join(","),
        hex::encode(artifacts.batch_vk_bytes32),
        hex::encode(artifacts.gateway_vk_bytes32),
        hex::encode(artifacts.batch_elf_sha256),
        hex::encode(artifacts.gateway_elf_sha256),
    );
    fs::write(output_dir.join("gateway-build-manifest.json"), &manifest)
        .expect("write Gateway build manifest");
    let constants = format!(
        concat!(
            "pub const TEST_ONLY_BUILD: bool = {test_only};\n",
            "pub const BATCH_VK_WORDS: [u32; 8] = {batch_vk_words:?};\n",
            "pub const BATCH_VK_BYTES32: [u8; 32] = {batch_vk_bytes32:?};\n",
            "pub const GATEWAY_VK_BYTES32: [u8; 32] = {gateway_vk_bytes32:?};\n",
            "pub const BUILD_MANIFEST_JSON: &str = {manifest:?};\n"
        ),
        test_only = test_only,
        batch_vk_words = artifacts.batch_vk_words,
        batch_vk_bytes32 = artifacts.batch_vk_bytes32,
        gateway_vk_bytes32 = artifacts.gateway_vk_bytes32,
        manifest = manifest,
    );
    fs::write(output_dir.join("gateway_build.rs"), constants).expect("write build constants");
}

fn run_cargo_prove(directory: &Path, workspace_target: &Path) {
    let cargo_prove = env::var("CARGO_PROVE").unwrap_or_else(|_| {
        env::var_os("HOME")
            .map(PathBuf::from)
            .map(|home| home.join(".sp1/bin/cargo-prove"))
            .filter(|path| path.is_file())
            .map(|path| path.display().to_string())
            .unwrap_or_else(|| "cargo".to_string())
    });
    let mut command = Command::new(&cargo_prove);
    sp1_build_support::configure_reproducible_build(&mut command);
    command.current_dir(directory);
    sp1_build_support::sanitize_nested_build_environment(&mut command);
    let _build_lock = sp1_build_support::acquire_reproducible_build_lock()
        .unwrap_or_else(|error| panic!("acquire SP1 Docker build lock: {error}"));
    sp1_build_support::isolate_loopback_docker_proxy(
        &mut command,
        &workspace_target.join("sp1-docker-config"),
    )
    .unwrap_or_else(|error| panic!("prepare isolated Docker configuration: {error}"));
    let output = command
        .output()
        .unwrap_or_else(|error| panic!("run cargo prove in {}: {error}", directory.display()));
    if !output.status.success() {
        panic!(
            "cargo prove build failed in {} ({}): stdout={} stderr={}",
            directory.display(),
            output.status,
            String::from_utf8_lossy(&output.stdout),
            String::from_utf8_lossy(&output.stderr)
        );
    }
}

fn read_required_file(path: &Path, description: &str) -> Vec<u8> {
    let metadata = fs::symlink_metadata(path)
        .unwrap_or_else(|error| panic!("inspect {description} at {}: {error}", path.display()));
    if metadata.file_type().is_symlink() || !metadata.is_file() {
        panic!("{description} must be a regular file at {}", path.display());
    }
    fs::read(path)
        .unwrap_or_else(|error| panic!("read {description} at {}: {error}", path.display()))
}

fn require_nonzero<T>(values: &[T], description: &str)
where
    T: Default + PartialEq,
{
    if values.iter().all(|value| *value == T::default()) {
        panic!("{description} must not be zero");
    }
}

fn validate_pinned_artifacts(
    batch_vk_words: &[u32; 8],
    batch_vk_bytes32: &[u8; 32],
    gateway_vk_bytes32: &[u8; 32],
    batch_elf_sha256: &[u8; 32],
    gateway_elf_sha256: &[u8; 32],
) {
    let mut mismatches = Vec::new();
    collect_mismatch(
        &mut mismatches,
        batch_vk_words,
        &pinned::PINNED_BATCH_VK_WORDS,
        "batch VK words",
    );
    collect_mismatch(
        &mut mismatches,
        batch_vk_bytes32,
        &pinned::PINNED_BATCH_VK_BYTES32,
        "batch VK bytes32",
    );
    collect_mismatch(
        &mut mismatches,
        gateway_vk_bytes32,
        &pinned::PINNED_GATEWAY_VK_BYTES32,
        "Gateway VK bytes32",
    );
    collect_mismatch(
        &mut mismatches,
        batch_elf_sha256,
        &pinned::PINNED_BATCH_ELF_SHA256,
        "batch ELF SHA-256",
    );
    collect_mismatch(
        &mut mismatches,
        gateway_elf_sha256,
        &pinned::PINNED_GATEWAY_ELF_SHA256,
        "Gateway ELF SHA-256",
    );

    if !mismatches.is_empty() {
        panic!(
            "SP1 artifacts do not match the pinned manifest:\n{}",
            mismatches.join("\n")
        );
    }
}

fn collect_mismatch<T>(mismatches: &mut Vec<String>, actual: &T, pinned: &T, description: &str)
where
    T: std::fmt::Debug + PartialEq,
{
    if actual != pinned {
        mismatches.push(format!(
            "- {description}: actual={actual:?} pinned={pinned:?}"
        ));
    }
}
