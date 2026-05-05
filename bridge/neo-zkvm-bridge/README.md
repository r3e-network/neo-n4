# neo-zkvm-bridge

C ABI shim that lets C# (`Neo.L2.Proving.Sp1`) call into [`neo-zkvm`](https://github.com/r3e-network/neo-zkvm)'s
`NeoProver` over P/Invoke. neo-zkvm is vendored as a git submodule at
`external/neo-zkvm` (init via `git submodule update --init --recursive` from the
repo root).

## Build

```bash
# Default build — bridge is buildable without neo-zkvm; all calls return NOT_IMPLEMENTED.
cargo build --release

# With the real SP1 prover enabled (uses the external/neo-zkvm submodule).
# Run `git submodule update --init --recursive` first if you cloned without --recurse-submodules.
cargo build --release --features real-prover

# Run unit tests
cargo test
```

The resulting `target/release/libneo_zkvm_bridge.{so,dylib,dll}` should be placed
alongside the C# binaries (Linux: `LD_LIBRARY_PATH` or `Plugins/`).

## ABI

```c
uint32_t neo_zkvm_abi_version(void);

int32_t  neo_zkvm_prove(
    const uint8_t *input_ptr, size_t input_len,
    uint8_t **output_ptr, size_t *output_len);

int32_t  neo_zkvm_verify(
    const uint8_t *proof_ptr, size_t proof_len);

void     neo_zkvm_free_buffer(uint8_t *ptr, size_t len);
```

Status codes:

| Code | Meaning                  |
| ---- | ------------------------ |
|  0   | OK                       |
| -1   | invalid input            |
| -2   | prove failed             |
| -3   | verify rejected          |
| -9   | not implemented (mock)   |

## Encoding

Inputs and proofs are `bincode`-serialized in the v0.1 ABI; a stable canonical encoding
will replace this in v2 once the spec lands.

## Why a separate bridge?

- C# can't directly link Rust crates; P/Invoke is the standard interop.
- `neo-zkvm` is moving fast; a thin shim lets the C# side track a stable ABI while the
  Rust prover internals churn.
- Optional `real-prover` feature keeps the C# build working even when neo-zkvm isn't
  installed on the dev machine (the C# side falls back to `MockRiscVProver` semantics).
