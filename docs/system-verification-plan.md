# Neo N4 system verification plan

This plan turns Neo N4 validation into a module-by-module release workflow. It is
not a replacement for `doc.md`; it is the execution checklist used to prove that
the implementation still matches the architecture, security model, and operator
experience.

## Verification goals

- Prove that each `doc.md` subsystem has at least one executable gate.
- Separate fast daily checks from expensive release gates.
- Keep module evidence auditable: command, environment, output path, and pass/fail.
- Validate both code correctness and production readiness: contracts, proofs,
  operator tooling, SDKs, docs, supply chain, private-network rehearsal, and gaps.

## Gate levels

| Level | Name | When to run | Expected duration | Purpose |
| --- | --- | --- | --- | --- |
| L0 | Smoke | every local iteration | minutes | Build confidence before editing more code. |
| L1 | Daily full | before handoff / PR | tens of minutes | Full first-party .NET + standard cross-language gates. |
| L2 | Release gate | before tagging / deployment | hours | Contract artifacts, SP1, private-network, external bridges, coverage. |
| L3 | Mainnet readiness | before production launch | operator-dependent | Wallet/HSM, live RPC, DA retention, incident drills, governance rehearsal. |

## Module matrix

| Module | Scope | L0 / L1 gates | L2 / L3 gates | Evidence |
| --- | --- | --- | --- | --- |
| Architecture and docs | `doc.md`, `docs/`, `README.md`, implementation status | `mdbook build`; local markdown link check; docs/current-count consistency | bilingual doc parity check; architecture-to-code trace review | mdBook output, link-check log, changed-doc diff |
| .NET solution baseline | `Neo.L2.sln`, all first-party projects | `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo`; `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo` | `dotnet format Neo.L2.sln --verify-no-changes --exclude external/`; coverage gate | build log, test trx/log, coverage JSON |
| Protocol primitives | `Neo.L2.Abstractions`, canonical encoders, wire formats | unit tests for discriminants, serializers, payload boundaries | cross-language byte-vector parity with Rust / Solidity / SDKs | unit-test log, parity vectors |
| Batch, state, executor | `Neo.L2.Batch`, `State`, `Executor`, `Executor.RiscV` | unit tests; default devnet; executor seam tests | persistent devnet rehydrate; RISC-V executor smoke with `libneo_riscv_host`; long random/invariant runs | devnet report, state-root continuity log |
| NeoHub L1 contracts | `contracts/NeoHub.*` | `dotnet build` for each contract | install `nccs`; emit `.nef` + `.manifest.json`; manifest invariant tests; deploy-plan scaffold pin | contract artifact directory, deploy plan, manifest invariant log |
| Neo core L2 native contracts | `external/neo` L2 native contract surface | filtered Neo unit tests for `UT_L2NativeContracts` | branch/fork policy review; native contract regression replay | Neo core test log |
| Bridge and messaging | `Neo.L2.Bridge`, `Messaging`, `AssetMapping`, `SharedBridge` | unit + integration tests for deposit, withdrawal, replay, message roots | cross-language vectors; withdrawal Merkle proof verification; external-bridge rehearsal | bridge test log, vector files |
| DA and persistence | `Neo.L2.Persistence`, `Neo.Plugins.L2DA`, RocksDB, NeoFS-like writer | DA unit tests; default in-memory devnet | sample-config devnet across rollup / validium / sidechain; DA retention / retrieval rehearsal | DA report, rehydrate output |
| Proof systems | `Neo.L2.Proving`, `bridge/neo-zkvm-*`, `external/neo-zkvm` | .NET prover unit tests; `bridge/neo-zkvm-guest` host-mode tests | `cargo prove build`; `neo-zkvm-host` release tests; ignored real-proof tests when toolchain is installed | proof bytes, VK bytes, tamper rejection log |
| RISC-V VM stack | `external/neo-riscv-vm`, C# P/Invoke seam | `cargo check -p neo-riscv-host`; `Neo.L2.Executor.RiscV.UnitTests` | release build of host library; devnet `--executor riscv`; FFI boundary stress | cargo log, native library hash, devnet output |
| Gateway aggregation | `Neo.Plugins.L2Gateway`, `BinaryTreeAggregator`, `IRoundProver` | gateway unit tests | multi-L2 aggregation scenario; round-prover substitution rehearsal | aggregation proof log |
| Operator tools | `tools/Neo.Stack.Cli`, deploy/bridge/faucet/explorer/devnet CLIs | CLI unit tests; `neo-stack` scaffold smoke | `scripts/deployment/run-local-rehearsal.ps1`; private-network script where applicable | generated chain dir, rehearsal report |
| SDKs and UI | `src/Neo.L2.Sdk`, `sdk/typescript`, `sdk/rust`, `sdk/python`, web/experience hub | .NET SDK tests; TypeScript vitest; Rust SDK cargo test; Python pytest if present; Node experience tests | API drift replay against devnet RPC; SDK parity matrix | SDK logs, API snapshots |
| Foreign-chain integrations | `watchers/*`, `external/foreign-contracts/*` | Rust watcher build/test; Foundry tests; Solana cargo tests | live-rpc watcher tests; local EVM Anvil rehearsal; signer/HSM integration rehearsal | watcher logs, Foundry log, health/metrics scrape |
| Security and supply chain | Rust, npm, NuGet, contracts, docs trust boundaries | `cargo audit`; npm audit; Rust clippy; dotnet format | threat-model review; dependency freshness; signer/secret handling; incident drills | audit reports, dependency diff |

## Canonical command sequence

### L0 smoke

```bash
dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build
dotnet run --project tools/Neo.L2.Devnet -- 3
mdbook build
```

### L1 daily full

```bash
dotnet format Neo.L2.sln --verify-no-changes --exclude external/
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo
pwsh ./scripts/test-coverage.ps1 -Configuration Release -ResultsDirectory coverage/dotnet-local -Threshold 90 -OverallThreshold 80

cd sdk/typescript && npm ci --no-audit --no-fund && npm test && npm run build
cd ../../sdk/rust && cargo build && cargo test
cd ../../bridge/neo-zkvm-guest && cargo build && cargo test
```

### L2 release gate

```bash
# NeoHub + sample contract artifacts.
dotnet tool install -g Neo.Compiler.CSharp
for d in contracts/NeoHub.* samples/contracts/Sample.*; do
  name=$(basename "$d")
  project="$d/$name.csproj"
  dotnet build "$project" /p:NuGetAudit=false --nologo
  nccs "$project" --output "$d/bin/sc"
done

# Native L2 contracts in the r3e Neo core fork.
dotnet test external/neo/tests/Neo.UnitTests/Neo.UnitTests.csproj \
  --filter FullyQualifiedName~UT_L2NativeContracts \
  /p:NuGetAudit=false --nologo

# Rust bridge / watcher gates.
(cd external/neo-riscv-vm && cargo check -p neo-riscv-host)
(cd watchers/neo-bridge-watcher-eth && cargo build --release && cargo test --release)
(cd watchers/neo-bridge-watcher-eth && cargo build --release --features live-rpc && cargo test --release --features live-rpc)
(cd watchers/neo-bridge-watcher-tron && cargo build --release && cargo test --release)
(cd watchers/neo-bridge-watcher-sol && cargo build --release && cargo test --release)

# Foreign routers.
(cd external/foreign-contracts/eth && forge fmt --check && forge test -vv)
(cd external/foreign-contracts/sol && cargo test)
```

### L2 expensive proof gate

```bash
(cd bridge/neo-zkvm-guest && cargo prove build)
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo test --workspace --release --locked
(cd bridge/neo-zkvm-host && cargo test --release --locked -- --ignored --nocapture)
```

### L3 private-network / production rehearsal

```powershell
pwsh ./scripts/deployment/run-local-rehearsal.ps1
pwsh ./scripts/private-network/Test-PrivateNetwork.ps1
```

Operator-specific L3 evidence must also include wallet/HSM configuration,
governance proposal rehearsal, emergency pause / unpause rehearsal, DA retention
proof, RPC health and metrics scrape, and incident rollback instructions.

## Execution order

1. **Normalize environment** — .NET 10, Rust stable, Node 20+, PowerShell,
   mdBook, Foundry, `nccs`, SP1 toolchain when running proof gates.
2. **Run L0 smoke** — fail fast before deeper work.
3. **Run module L1 gates in parallel** — .NET solution, docs, SDKs, Rust guest,
   watcher default gates.
4. **Run L2 release gates by risk** — contracts first, native core, bridge,
   DA/devnet, proof stack, foreign routers.
5. **Run L3 operator rehearsal** — only after L2 passes; record environment and
   secrets handling without exposing secret material.
6. **Publish verification ledger** — command, commit, environment, artifact path,
   pass/fail, owner, and follow-up issue for each failed gate.

## Pass criteria

- No L0 or L1 failure.
- L2 must pass for any production release candidate.
- Real SP1 proof gate may remain manually triggered, but a production ZK chain
  cannot launch without fresh real-proof evidence.
- Devnet must pass default, persistent rehydrate, sample config, and selected
  executor modes.
- Contract artifacts must be generated from the same commit being released.
- Any accepted gap must be visible in `IMPLEMENTATION_STATUS.md`, the release
  checklist, or the verification ledger.

## Evidence ledger template

| Date | Commit | Module | Gate | Command | Result | Evidence path | Owner | Follow-up |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| YYYY-MM-DD | `<sha>` | NeoHub contracts | L2 | `nccs ...` | pass/fail | `artifacts/...` | owner | issue/pr |
