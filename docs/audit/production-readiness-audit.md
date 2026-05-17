# Neo N4 Deep Audit Report

Generated: 2026-05-17 15:27 Asia/Shanghai
Repository: r3e-network/neo-n4

## Executive Assessment

Neo N4 is now in a substantially healthier state than the initial checkout: the primary .NET solution builds, every discovered .NET test passes, all contract projects directly emit NEF/manifest artifacts, TypeScript/Rust SDKs pass their test/build checks, the external Solidity/Solana contracts pass local tests, watcher crates pass tests and clippy, the Rust workspace passes release tests and warnings-as-errors clippy under WSL2, mdBook documentation builds, and package vulnerability scans are recorded.

The project is not yet a "mainnet-ready with no conditions" artifact. The remaining production gate is specific and bounded: deployed public devnet/testnet end-to-end contract flows should be exercised against a real Neo N4 node set with funded accounts and operator credentials before production. CI parity has been upgraded locally, but the updated GitHub Actions workflow still needs to be observed on the remote repository after push.

## Scope and Inventory

The audit generated machine-readable inventories, checklists, vulnerability
scan output, and command logs as local scratch artifacts. Those raw files are
intentionally not tracked in the repository; the curated tracked evidence is
this report plus [`repository-coverage-ledger.md`](./repository-coverage-ledger.md).
Regenerate raw logs by re-running the verification commands in the release
readiness checklist.

Inventory snapshot:

| Classification | Count |
| --- | ---: |
| Total files inventoried | 8,405 |
| First-party runtime files | 452 |
| Documentation files | 170 |
| Test files | 207 |
| Sample files | 17 |
| Upstream/external-boundary files | 7,559 |
| Indexed first-party functions/methods/symbols | 6,087 |

Note: the function-level audit is systematic rather than pretending every low-risk leaf function received equal manual attention. The full first-party surface was indexed and scanned; manual review focused on trust-boundary, settlement, bridge, proving, watcher, deployment, persistence, RPC, and documentation paths.

## Threat Model

Primary assets:

- L1 and L2 contract state: batches, withdrawals, bridges, governance, DA commitments, sequencer bonds, emergency controls.
- Cryptographic proof material: ZK, optimistic, MPC, restricted execution, external-chain proof payloads.
- Cross-chain message integrity: source/destination chain IDs, nonces, replay state, token mappings, bridge committee signatures.
- Operator/deployment controls: deploy manifests, verifier registry entries, watcher credentials, RPC endpoints.

Important trust boundaries:

- External chain RPC/watcher input into Neo contract submissions.
- Sequencer/prover output into settlement contracts.
- Challenge proofs into batch revert/slashing paths.
- Governance/owner-only calls into production contract configuration.
- CLI and deployment config into production contract addresses and verifier wiring.

Highest-impact failure classes reviewed:

- Unauthorized finalization or rollback of settlement batches.
- Optimistic challenge bypass or inability to revert after proven fraud.
- Cross-chain replay, wrong-chain proof acceptance, or unbound withdrawal leaves.
- Test/stub verifier registration in production routes.
- Missing contract artifacts or deployment plans drifting from docs.
- Untrusted RPC/deserialization/file-input paths in watchers and CLIs.
- Supply-chain advisories in Rust and .NET package graphs.

## Findings Fixed During Audit

### 1. Optimistic batch challenge path was not enforceable

Before the fix, optimistic `SubmitBatch` stored batches as pending, `FinalizeBatch` could finalize pending/challengeable states through the normal verifier path, and `OptimisticChallenge.Challenge` called `SettlementManager.RevertBatch` even though `RevertBatch` was owner-only. That meant a valid fraud proof could fail to revert the fraudulent batch.

Fixed in `contracts/NeoHub.SettlementManager/SettlementManagerContract.cs`:

- Added configured `OptimisticChallenge` storage and setter.
- `SubmitBatch` now marks optimistic proof type `2` as `StatusChallengeable`.
- Optimistic submit opens the challenge window on the configured challenge contract.
- `FinalizeBatch` requires the challenge contract witness for challengeable batches.
- `RevertBatch` authorizes either owner or the configured challenge contract, and challenge-contract rollback is restricted to challengeable batches.
- Optimistic payload parsing validates version, length, sequencer field, and nonzero account.

Key locations:

- `contracts/NeoHub.SettlementManager/SettlementManagerContract.cs:28`
- `contracts/NeoHub.SettlementManager/SettlementManagerContract.cs:115`
- `contracts/NeoHub.SettlementManager/SettlementManagerContract.cs:141`
- `contracts/NeoHub.SettlementManager/SettlementManagerContract.cs:168`
- `contracts/NeoHub.SettlementManager/SettlementManagerContract.cs:196`
- `contracts/NeoHub.SettlementManager/SettlementManagerContract.cs:487`

### 2. Optimistic proof payload did not bind slash target to the registered sequencer key

Fixed in `src/Neo.L2.Proving/Optimistic/OptimisticProofPayload.cs` and `src/Neo.L2.Proving/Optimistic/OptimisticVerifier.cs`:

- Wire format upgraded to version `2`.
- Payload now includes sequencer `UInt160` at offset 61.
- Decode rejects zero sequencer and bad signature lengths.
- Verifier derives the expected Neo account from the registered sequencer key and rejects mismatches.
- Unit tests cover layout and mismatch rejection.

Key locations:

- `src/Neo.L2.Proving/Optimistic/OptimisticProofPayload.cs:30`
- `src/Neo.L2.Proving/Optimistic/OptimisticProofPayload.cs:45`
- `src/Neo.L2.Proving/Optimistic/OptimisticProofPayload.cs:73`
- `src/Neo.L2.Proving/Optimistic/OptimisticProofPayload.cs:88`
- `src/Neo.L2.Proving/Optimistic/OptimisticVerifier.cs:57`

### 3. Stub-verifier documentation drift was corrected

Docs now state the production code-enforced behavior: `ExternalBridgeRegistry` accepts only production bridge kinds 1/2/3, while the stub reports kind 0 and is therefore not registrable through that production path.

Key locations:

- `docs/architecture-l1-vs-l2.md:137`
- `docs/architecture-l1-vs-l2.md:170`
- `docs/zh/architecture-l1-vs-l2.md`

### 4. SP1 prover setup and WSL2 validation were completed

The host/guest README files now use the current official SP1 install URL and explicitly document that SP1 is Linux/macOS-oriented; native Windows prover-host execution should use WSL2 or a Linux/macOS host. WSL2 Ubuntu was configured with the required system packages, Rust `1.95.0`, `cargo-prove sp1 6.2.1`, and host-gateway proxy settings for GitHub access.

Key locations:

- `bridge/neo-zkvm-host/README.md:40`
- `bridge/neo-zkvm-host/README.md:49`
- `bridge/neo-zkvm-guest/README.md`

Reference checked: https://docs.succinct.xyz/docs/sp1/getting-started/install

### 5. Missing runtime entrypoints were restored

The audit restored missing binary entrypoints required by the workspace:

- `watchers/neo-bridge-watcher-eth/src/bin/neo-bridge-watcher-eth.rs`
- `bridge/neo-zkvm-host/src/bin/prove_batch.rs`

### 6. Test and status documentation was synchronized

The documented .NET test count was updated to the current 1,423 passing tests, and the optimistic sequencer account/signature binding coverage was documented.

Updated files include:

- `README.md`
- `IMPLEMENTATION_STATUS.md`
- `AGENTS.md`
- `docs/testing-approach.md`
- `docs/README.md`

### 7. CI parity was expanded

The main GitHub Actions workflow now mirrors the local audit gates more closely:

- SP1 job cache key and toolchain setup were updated for SP1 `6.2.1`.
- WSL/Linux Rust workspace gates were added: `cargo fmt --all -- --check`, `cargo clippy --workspace --all-targets --locked -- -D warnings`, and `cargo test --workspace --release --locked`.
- Real SP1 proof tests can be run manually via the `run_real_sp1_proof` workflow-dispatch input.
- `cargo audit --json`, TypeScript build/audit, and the foreign Solana program test were added.

Key location:

- `.github/workflows/build.yml`

### 8. External bridge deploy bundle validation was hardened

The systematic follow-up audit found that `neo-external-bridge deploy-bundle`
validated committee size and threshold, but did not validate that the Eth router,
Eth committee members, or committee blob were well-formed hex bytes before
printing the operator runbook.

Fixed in `tools/Neo.External.Bridge.Cli/Commands/DeployBundleCommand.cs`:

- `--eth-router` must now be a 20-byte hex address.
- each `--eth-addresses[N]` entry must now be a 20-byte hex address.
- `--committee-blob` must now decode as non-empty hex bytes.
- accepted EVM addresses and committee blobs are normalized in the printed plan.
- `tests/Neo.External.Bridge.Cli.UnitTests` was added to pin the CLI route,
  key generation file safety, committee duplicate/point validation, deploy
  threshold checks, and malformed EVM/hex rejection.

### 9. Release readiness checklist was added

The remaining live-network production gate is now documented as an evidence-driven release checklist covering source freeze, local verification, contract artifact review, deployment plan review, real devnet/testnet rehearsal, CI approval, and production cutover controls.

Key location:

- `docs/release-readiness-checklist.md`

## Verification Matrix

Fresh verification artifacts were written on 2026-05-17.

| Surface | Command/result | Status |
| --- | --- | --- |
| .NET solution build | `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo` | Pass |
| .NET tests | TRX summary: 34 files, 1,423 total, 1,423 passed, 0 failed | Pass |
| .NET package advisories | `dotnet list ... package --vulnerable --include-transitive` | Pass, no vulnerable packages reported |
| Contract artifacts | 30 contract projects, 0 build failures, 0 missing NEF/manifest artifacts | Pass |
| Documentation build | `mdbook build` | Pass |
| TypeScript SDK | `npm test -- --run`, `npm run build`, `npm audit --audit-level=moderate` | Pass, 15 tests, 0 npm vulnerabilities |
| Rust SDK | `cargo test`, `cargo build` | Pass, 10 integration tests |
| Solidity external contracts | `forge test -vv` | Pass, 20 tests |
| Solana external program | `cargo test` | Pass, 1 test |
| ZK guest | `cargo test` | Pass, 8 tests |
| ETH watcher | `cargo test --release --features live-rpc`, `cargo clippy --release --all-targets --features live-rpc -- -D warnings` | Pass, 85 tests across unit/parity/preflight/doc surfaces |
| SOL watcher | `cargo test`, `cargo clippy --all-targets -- -D warnings` | Pass, 9 tests |
| TRON watcher | `cargo test`, `cargo clippy --all-targets -- -D warnings` | Pass, 7 tests |
| Rust formatting | `cargo fmt --all -- --check` under WSL2 | Pass |
| Rust workspace clippy | `cargo clippy --workspace --all-targets --locked -- -D warnings` under WSL2 | Pass |
| Rust workspace release tests | `cargo test --workspace --release --locked` under WSL2 | Pass; 67 tests passed, 2 ignored proof tests separately covered, 0 failed |
| Rust cargo audit | `cargo audit --json` after SP1 6.2.1 update with Windows proxy for advisory DB refresh | No vulnerabilities; 5 unmaintained warnings and 1 unsound warning, all documented in `docs/rust-supply-chain-policy.md` |
| ZK guest WSL2/SP1 build | `cargo prove build` | Pass |
| ZK host WSL2/SP1 default | `cargo test --release --locked` in `bridge/neo-zkvm-host` | Pass; guest ELF built, 1 default end-to-end zkVM execute test passed |
| ZK host WSL2/SP1 real proof | `cargo test --release --locked -- --ignored --nocapture` in `bridge/neo-zkvm-host` | Pass; real proof generated in 38.7s, verified in 12.9s, and tampered public-input hash rejected |
| GitHub Actions workflow syntax | Parsed `.github/workflows/build.yml` with PyYAML after CI expansion | Pass; 9 jobs detected |
| In-process devnet, persistent default executor | `dotnet run --project tools\Neo.L2.Devnet -- 5 --data-dir <scratch>` followed by `-- 0 --data-dir <same>` | Pass; 5 batches sealed/audited, state/DA continuity passed, restart rehydrated persisted state |
| In-process devnet, Counter executor | `dotnet run --project tools\Neo.L2.Devnet -- 3 --executor counter --data-dir <scratch>` | Pass; 3 batches sealed/audited, custom executor tx roots and L2 message roots generated |
| In-process devnet, NeoVM executor | `dotnet run --project tools\Neo.L2.Devnet -- 3 --executor neovm --data-dir <scratch>` | Pass; real NeoVM executor bootstrapped 45 native-contract keys and sealed/audited 3 batches |
| ZK host native Windows | `cargo test --release` | Expected platform failure in `sp1-jit` POSIX imports; WSL2 path is the supported Windows workflow |

## Security Scan Result

No new unresolved critical/high application vulnerability remains from the reviewed high-risk paths after the fixes above. The most serious issue found was the optimistic challenge/revert authorization gap, and it has been fixed and regression-tested.

Validated closures:

| Area | Disposition | Evidence |
| --- | --- | --- |
| Optimistic challenge finalization/revert | Fixed | SettlementManager build + proving unit tests pass |
| Sequencer slash-target binding | Fixed | Optimistic payload/verifier tests pass |
| Stub verifier production registration | Suppressed by code control, docs corrected | Bridge kind gate documented |
| SharedBridge withdrawal leaf/token mapping | Fixed before final matrix | Contract tests/build pass |
| Hardcoded production secrets | Not found | `secret-scan.csv` contains docs/test/config examples, not production keys |
| .NET vulnerable packages | Not found | NuGet vulnerable scan passes |
| npm vulnerable packages | Not found | npm audit reports 0 vulnerabilities |
| Rust vulnerable packages | Not found | cargo-audit vulnerabilities count 0 |

Engineering hygiene closures added after WSL2 validation:

- Updated SP1 crates to `6.2.1` to match installed `cargo-prove sp1 6.2.1`.
- Removed an ignored member-level `[profile.release]` from `bridge/neo-zkvm-guest/Cargo.toml`; Cargo workspaces only honor profiles from the workspace root.
- Hardened `bridge/neo-zkvm-host/build.rs` so nested `cargo prove build` does not inherit host/clippy compiler wrappers and does not leak child `cargo:` directives as parent warnings.
- Cleaned Rust 1.95 clippy warnings in guest/host/SDK code without changing runtime behavior.

Rust cargo-audit warnings accepted under `docs/rust-supply-chain-policy.md`:

- `ansi_term 0.12.1` / RUSTSEC-2021-0139 / unmaintained
- `bincode 1.3.3` / RUSTSEC-2025-0141 / unmaintained
- `number_prefix 0.4.0` / RUSTSEC-2025-0119 / unmaintained
- `paste 1.0.15` / RUSTSEC-2024-0436 / unmaintained
- `rustls-pemfile 2.2.0` / RUSTSEC-2025-0134 / unmaintained
- `lru 0.12.5` / RUSTSEC-2026-0002 / unsound, patched in `>=0.16.3`; current SP1 usage uses `LruCache::{get,put,push}` and does not call the advised `IterMut` path

## Production Readiness Assessment

Ready or close to ready:

- Repository structure is coherent across contracts, src, tools, watchers, SDKs, docs, and tests.
- Core build/test/dev documentation is now substantially consistent with the code.
- The contract set emits deployable artifacts through the explicit
  `dotnet build` + `nccs` path validated in
  [`deployment-rehearsal-2026-05-17`](./deployment-rehearsal-2026-05-17/).
- Cross-chain watcher logic has meaningful tests for chain IDs, replay/cursor behavior, proof layout, message hash parity, signer handling, and health endpoints.
- The SDKs and CLIs are covered by focused tests and package scans.
- Documentation now includes architecture diagrams and a visual guide for user-friendly system understanding.
- WSL2 is a validated Windows operator path for SP1 proving, including real proof generation and verification.

Still not production-complete without the following gates:

- Run public-network deployment rehearsals against a real Neo N4 devnet/testnet, including governance setup, verifier registry wiring, optimistic challenge windows, bridge deposits/withdrawals, watcher failover, replay attempts, and emergency paths. The local Windows + WSL2 deployment rehearsal now passes and is documented, but it does not replace funded public-network deployment evidence.
- Add or document production verifier contracts/operators for any verifier registry entries that are intentionally supplied out-of-tree.
- Observe the expanded GitHub Actions workflow on the remote repository after push; local validation passed, but the changed workflow still needs a remote GitHub Actions run.

## Recommended Release Gates

Before a production release, require:

1. Full public devnet/testnet deployment rehearsal from clean state using the documented production NeoHub deploy steps and real funded operator accounts.
2. End-to-end bridge test for ETH, SOL, and TRON paths with wrong-chain, replay, duplicate-signer, and timeout/finality negative tests.
3. Contract hash/manifest review against deployment manifests.
4. Successful remote run of the expanded CI workflow, including direct contract artifact checks, mdBook build, Rust advisory policy check, and Linux SP1 proof tests.

## Final Status

The repository is professionally structured and now passes the broad local verification matrix available on this Windows host plus the WSL2 SP1 prover path and local in-process devnet rehearsals. It is not blocked by normal build/test failures. The remaining gaps are production-gate items: public devnet/testnet operational rehearsal with real credentials/funds and observing the expanded CI workflow on GitHub after push.
