# Neo N4 Gateway SP1 Host

`prove-gateway` is the independent SP1 6.2.1 recursive Gateway prover daemon from `doc.md` §4.1.
It derives and locks the batch/Gateway VKs from freshly built ELFs, resolves only canonical flat
compressed sidecars, verifies every child, generates a 356-byte Gateway Groth16 proof, verifies it
again on the host, and publishes the result manifest last.

Production builds derive both program VKs only from `cargo prove build --docker --locked`
under the audited immutable SP1 6.2.1 amd64 image digest
`sha256:14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f`.
The build script rejects another `SP1_DOCKER_IMAGE` and validates both Docker
ELF hashes plus VKs against the guest's split `batch_vk_manifest.rs` and
`vk_manifest.rs`; the Gateway guest never compiles its own output pins.

```bash
export SP1_DOCKER_IMAGE=ghcr.io/succinctlabs/sp1@sha256:14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f
export SP1_GNARK_IMAGE=ghcr.io/succinctlabs/sp1-gnark@sha256:be8555f1ad90870acd8c6ec7fd3ba0b1a2133ea9cddf25e130665aa651129e54
cargo build --release -p neo-zkvm-gateway-host
target/release/prove-gateway build-manifest
target/release/prove-gateway daemon --queue /srv/gateway/queue \
  --child-proofs /srv/gateway/compressed-batches
```

Sidecars are SP1 `SP1ProofWithPublicValues::save` files named only from the canonical tuple:

```text
<chainId-hex8>-<batchNumber-hex16>-<publicInputHash-hex64>.sp1-compressed-proof.bin
```

The request cannot supply a VK or path. Core, Plonk, Groth16, TEE-wrapped, wrong-version,
wrong-public-value, symlink, oversized, and non-regular child sidecars fail closed.

Fast parser/protocol tests explicitly use a non-proving build:

```bash
cargo test -p neo-zkvm-gateway-host --features test-only-vk
```

The real recursive gate is intentionally ignored because it performs two real SP1 proof stages:

```bash
cargo test --release -p neo-zkvm-gateway-host \
  proves_and_host_verifies_real_recursive_gateway_groth16 -- --ignored --exact
```

The published SP1 6.2.1 gnark image is `linux/amd64` only. On Apple Silicon, use SP1's upstream
native gnark backend so the final wrapper proof runs as native arm64 Go instead of through an
unstable amd64 emulation layer. This changes only the wrapper-prover transport; the test still
host-verifies the same pinned Groth16 proof and exact Gateway VK:

```bash
export LIBCLANG_PATH="$(brew --prefix llvm)/lib"
cargo test --release -p neo-zkvm-gateway-host --features native-gnark \
  proves_and_host_verifies_real_recursive_gateway_groth16 -- --ignored --exact
```
