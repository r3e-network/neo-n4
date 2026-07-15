# Private Network Testing

`scripts/private-network/Test-PrivateNetwork.ps1` is the repeatable local
private-network verification harness for Neo N4. It combines the reviewed-asset operator CLI
configuration preflight, the in-process devnet, and the repository CI checks into one
run with logs and a JSON summary.

## What It Builds

The harness creates chain configuration, staged operator deployments, private-network state, and
generated WSL scripts in a per-run OS temporary directory with owner-only permissions. It removes
that directory in `finally`, on success or failure. Repository builds still use their ordinary
gitignored `bin/`, `obj/`, and `target/` outputs; they are not represented as isolated secret
state. `artifacts/private-network/<run-id>/` retains only logs, the redacted command/status
summary, and explicit audit reports; private-network state is never retained there or written
into source-controlled paths.

It exercises four local private-chain templates:

| Template | Purpose | Verification |
| --- | --- | --- |
| `rollup` | Optimistic rollup default | `create-chain`, `validate`, reviewed-config `init-l2`, register/deploy plan, explicit dry-run sequencer/batcher/prover preflights, reference devnet |
| `validium` | NeoFS-style DA + gateway label | persistent `--executor counter` devnet, live `/metrics`, 0-batch rehydration |
| `zk-rollup` | Validity-proof label | NeoVM2/RISC-V executor gate plus devnet smoke |
| `sidechain` | NeoFS DA / no-proof label | config generation and validation |

The devnet path is in-process by design: it wires the same framework
components operators use, without requiring funded L1 accounts or public RPC
endpoints. It covers deposits, withdrawals, batch execution, DA commitments,
proof/public-input verification, audit checks, metrics, RPC snapshots, and
persistent RocksDB-backed state rehydration.

## Run It

From the repository root on Windows:

```powershell
.\scripts\private-network\Test-PrivateNetwork.ps1 `
  -NodeConfig C:\reviewed\sequencer\config.json `
  -BatcherNodeConfig C:\reviewed\batcher\config.json `
  -SequencerNeoCli C:\reviewed\sequencer\Neo.CLI.dll `
  -BatcherNeoCli C:\reviewed\batcher\Neo.CLI.dll `
  -Prover C:\reviewed\prover\prove-batch.exe
```

The sequencer and batcher executable paths identify complete reviewed deployment directories.
The harness stages only the executable, approved root runtime binaries, approved binary file types
under `runtimes/`, the one required plugin runtime/config, and the explicitly reviewed node config.
Wallets, node databases, logs, hidden paths, arbitrary JSON, and symbolic links/junctions are never
copied. Because wallets stay outside the temporary tree, the sequencer config must use an absolute
`UnlockWallet.Path` to a reviewed Neo-compatible NEP-6 `.json` wallet. The preflight opens that
wallet with the configured password, decrypts a configured committee account, and verifies that
the decrypted private key derives the exact configured validator public key; malformed or
mismatched encrypted keys, wrong passwords, unsupported adapter files, and unrelated wallet keys
fail closed. A custom HSM wallet factory therefore requires a separately reviewed
Neo.CLI integration and is not accepted by this generic preflight. `Storage.Path` should resolve to
the isolated `data` and `batcher-data` directories (for example `../data` and `../batcher-data`).
Operator checks are deliberately `--dry-run`: they prove configuration, plugin, wallet,
executable, and argument consistency without launching three long-running processes serially. A
funded concurrent dBFT/process-lifecycle rehearsal remains a separate release artifact and must
not be inferred from this harness.

Useful variants:

```powershell
# Faster local smoke while editing docs or scripts.
.\scripts\private-network\Test-PrivateNetwork.ps1 `
  -SkipOperatorPreflight -SkipRust -SkipRealSp1Proof `
  -SkipForeignContracts -SkipSupplyChainAudit

# Keep the full matrix but skip the expensive real-proof tests.
$reviewedAssets = @{
  NodeConfig = "C:\reviewed\sequencer\config.json"
  BatcherNodeConfig = "C:\reviewed\batcher\config.json"
  SequencerNeoCli = "C:\reviewed\sequencer\Neo.CLI.dll"
  BatcherNeoCli = "C:\reviewed\batcher\Neo.CLI.dll"
  Prover = "C:\reviewed\prover\prove-batch.exe"
}
.\scripts\private-network\Test-PrivateNetwork.ps1 @reviewedAssets -SkipRealSp1Proof

# Increase private-network batch count for devnet runs.
.\scripts\private-network\Test-PrivateNetwork.ps1 @reviewedAssets -Batches 10
```

Each run writes:

- `artifacts/private-network/<run-id>/logs/*.log` for command output.
- `artifacts/private-network/<run-id>/summary.json` for status, commands,
  exit codes, and log paths.
- `artifacts/private-network/latest-run.txt` pointing to the latest run.

`summary.json` records whether the temporary working directory was removed. Cleanup failure makes
the run fail closed. `artifacts/` is gitignored, so repeated runs do not dirty the repository.

## Coverage Matrix

| Layer | Commands |
| --- | --- |
| .NET build/test | `dotnet build Neo.L2.sln`, `dotnet test Neo.L2.sln --no-build` |
| Neo contracts | `dotnet build` + `nccs` for every deployable `NeoHub.*` and `Sample.*` contract; verifies `.nef` and `.manifest.json`; runs Neo core tests for N4 L2 native contracts in `external/neo` |
| Operator CLI | `neo-stack create-chain`, `validate`, `init-l2`, `register-chain`, `deploy-bridge-adapter`, `start-sequencer`, `start-batcher`, `start-prover` |
| Private devnet | reference executor, counter executor with persistence and metrics, 0-batch rehydration, NeoVM2/RISC-V gate, legacy NeoVM compatibility executor |
| Rust bridge | PolkaVM RISC-V host, zkVM guest, Rust SDK, ETH/Tron/Solana watchers, clippy |
| SP1 zkVM | `cargo prove build`, workspace clippy/tests, optional real SP1 proof tests |
| Supply chain | RustSec `cargo audit`, TypeScript `npm audit` |
| External contracts | Foundry EVM router tests, Solana program tests |
| Documentation | `mdbook build` |

## Tooling Notes

- The script expects Windows PowerShell plus WSL2.
- Unless `-SkipOperatorPreflight` is explicit, all five reviewed operator asset parameters are
  mandatory and validated before the expensive build/test matrix starts.
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
