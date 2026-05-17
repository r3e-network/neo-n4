# Private Network Testing

`scripts/private-network/Test-PrivateNetwork.ps1` is the repeatable local
private-network verification harness for Neo N4. It combines the operator CLI
bring-up path, the in-process devnet, and the repository CI checks into one
run with logs and a JSON summary.

## What It Builds

The harness creates isolated chain directories under
`artifacts/private-network/<run-id>/` and never writes private-network state
into source-controlled paths.

It exercises four local private-chain templates:

| Template | Purpose | Verification |
| --- | --- | --- |
| `rollup` | Optimistic rollup default | `create-chain`, `validate`, `init-l2`, register/deploy plan, sequencer/batcher/prover preflights, reference devnet |
| `validium` | NeoFS-style DA + gateway label | persistent `--executor counter` devnet, live `/metrics`, 0-batch rehydration |
| `zk-rollup` | Validity-proof label | real Neo VM executor devnet smoke |
| `sidechain` | External DA / no-proof label | config generation and validation |

The devnet path is in-process by design: it wires the same framework
components operators use, without requiring funded L1 accounts or public RPC
endpoints. It covers deposits, withdrawals, batch execution, DA commitments,
proof/public-input verification, audit checks, metrics, RPC snapshots, and
persistent RocksDB-backed state rehydration.

## Run It

From the repository root on Windows:

```powershell
.\scripts\private-network\Test-PrivateNetwork.ps1
```

Useful variants:

```powershell
# Faster local smoke while editing docs or scripts.
.\scripts\private-network\Test-PrivateNetwork.ps1 `
  -SkipRust -SkipRealSp1Proof -SkipForeignContracts -SkipSupplyChainAudit

# Keep the full matrix but skip the expensive real-proof tests.
.\scripts\private-network\Test-PrivateNetwork.ps1 -SkipRealSp1Proof

# Increase private-network batch count for devnet runs.
.\scripts\private-network\Test-PrivateNetwork.ps1 -Batches 10
```

Each run writes:

- `artifacts/private-network/<run-id>/logs/*.log` for command output.
- `artifacts/private-network/<run-id>/summary.json` for status, commands,
  exit codes, and log paths.
- `artifacts/private-network/latest-run.txt` pointing to the latest run.

`artifacts/` is gitignored, so repeated runs do not dirty the repository.

## Coverage Matrix

| Layer | Commands |
| --- | --- |
| .NET build/test | `dotnet build Neo.L2.sln`, `dotnet test Neo.L2.sln --no-build` |
| Neo contracts | `dotnet build` + `nccs` for every `NeoHub.*`, `L2Native.*`, and `Sample.*` contract; verifies `.nef` and `.manifest.json` |
| Operator CLI | `neo-stack create-chain`, `validate`, `init-l2`, `register-chain`, `deploy-bridge-adapter`, `start-sequencer`, `start-batcher`, `start-prover` |
| Private devnet | reference executor, counter executor with persistence and metrics, 0-batch rehydration, Neo VM executor |
| Rust bridge | PolkaVM RISC-V host, zkVM guest, Rust SDK, ETH/Tron/Solana watchers, clippy |
| SP1 zkVM | `cargo prove build`, workspace clippy/tests, optional real SP1 proof tests |
| Supply chain | RustSec `cargo audit`, TypeScript `npm audit` |
| External contracts | Foundry EVM router tests, Solana program tests |
| Documentation | `mdbook build` |

## Tooling Notes

- The script expects Windows PowerShell plus WSL2.
- WSL must have a stable Rust toolchain. The script uses the installed
  toolchain binaries directly to avoid unwanted `rustup` online channel syncs.
- If `nccs` is missing, the script installs `Neo.Compiler.CSharp` with
  `dotnet tool install -g`.
- If `mdbook` is missing, the script installs `mdbook v0.4.40` through Cargo.
- Foundry is expected in `~/.foundry/bin` for the EVM router tests.
- RustSec advisory data is downloaded into `artifacts/private-network/` and
  reused with `cargo audit --no-fetch --stale` to avoid WSL GitHub fetch
  instability.

## Interpreting Results

A successful run ends with:

```text
Private-network verification passed. Summary: <run-dir>\summary.json
```

If a step fails, open the referenced log in `summary.json`. The harness stops
at the first failing step so the failing module is explicit instead of buried
under later noise.
