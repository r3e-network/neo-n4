# Neo N4 Gateway SP1 Host

`prove-gateway` is the independent SP1 6.2.1 recursive Gateway prover daemon from `doc.md` §4.1.
It derives and locks the batch/Gateway VKs from freshly built ELFs, resolves only canonical flat
compressed sidecars, verifies every child, generates a 356-byte Gateway Groth16 proof, verifies it
again on the host, and publishes the result manifest last.

```bash
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
