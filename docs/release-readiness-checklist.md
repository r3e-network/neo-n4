# Release Readiness Checklist

Use this checklist before any production or public testnet release. It is
intentionally evidence-driven: every item should leave a log, artifact, tx hash,
contract hash, or signed approval that can be reviewed later.

## 1. Source and Dependency Freeze

- Record the repository commit, submodule commits, and release tag.
- Confirm `Cargo.lock`, TypeScript lockfile, NuGet package graph, and contract
  manifests are committed and unchanged after verification.
- Run Rust supply-chain checks from `docs/rust-supply-chain-policy.md`.
- Record accepted informational advisories and the reason each remains accepted.

## 2. Local Verification Gates

Run the full local matrix before deploying:

```bash
dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build
mdbook build
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo test --workspace --release --locked
cargo audit --json
```

For SP1 proving on Windows, run the following under WSL2:

```bash
cd bridge/neo-zkvm-guest
cargo prove build

cd ../neo-zkvm-host
cargo test --release --locked
cargo test --release --locked -- --ignored --nocapture
```

## 3. Contract Artifact Review

- Build every deployable `contracts/NeoHub.*` and
  `samples/contracts/Sample.*` project directly.
- Run `external/neo` native-contract tests for the N4 L2 native contracts.
- Record each `.nef` hash, manifest hash, compiler version, and project path.
- Review manifest permissions, groups, supported standards, and safe methods.
- Confirm devnet-only stubs are not registered in production registries.
- Confirm verifier kind values match production verifier implementations.

## 4. Deployment Plan Review

- Generate the deployment plan with the exact target network config.
- Review owner/governance accounts, multisig policy, and upgrade authority.
- Review verifier registry entries, challenge windows, finality thresholds, and
  bridge confirmation settings.
- Review token mappings and external chain IDs for ETH, SOL, TRON, and any EVM
  family chains enabled for the release.
- Record expected contract addresses and hashes before broadcasting.

## 5. Devnet/Testnet Rehearsal

Run this on a real Neo N4 devnet/testnet node set before production:

- Deploy contracts from a clean state.
- Initialize governance, verifier registry, bridge registry, token mappings,
  settlement manager, challenge contract, and emergency controls.
- Submit at least one valid batch for each proof type intended for release.
- Execute deposit and withdrawal flows for each enabled bridge family.
- Run replay, wrong-chain, duplicate-signer, insufficient-confirmation, and
  expired-challenge negative tests.
- Exercise optimistic fraud proof submission, rollback, and slashing paths.
- Restart watchers and prover processes to verify journal/cursor recovery.
- Trigger `/healthz`, `/readyz`, metrics, and alert checks during the run.
- Record tx hashes, final state roots, withdrawal roots, and watcher logs.

## 6. CI and Release Approval

- Push the branch and require the expanded GitHub Actions workflow to pass.
- Confirm the required `SP1 compatibility and manual release proof gate` (`sp1-host`) is green.
  On ordinary pull requests and `master` pushes that job only aggregates the fast .NET, contract,
  and Rust compatibility lanes and must report the real-proof matrix as **skipped**. For a
  release candidate, manually dispatch the `build` workflow so the three `sp1-release-gates`
  lanes (workspace release, terminal batch proof, recursive Gateway proof) run; the aggregate
  gate then requires every lane to succeed. Mock/dummy proofs are forbidden, and a skipped or
  incomplete real-proof step on a release dispatch is not acceptable evidence.
- Require `SDK Conformance / Shared vectors (4 SDKs)` and manually dispatch
  `SDK Conformance`; manual dispatch automatically requires the live job and its configured
  credentials. Retain the offline and live JSON
  summaries and reject any report with zero discovered or executed live tests.
- Attach CI run URLs, local verification logs, contract artifacts, deployment
  plan, and devnet/testnet rehearsal evidence to the release approval.
- Require sign-off from contract, prover, watcher/operator, and security owners.

## 7. Production Cutover

- Re-run read-only preflight checks against production RPC endpoints.
- Confirm private keys, HSM policy, signer thresholds, and emergency contacts.
- Deploy in the reviewed order only; stop on any hash or manifest mismatch.
- After deployment, verify registry state, owner state, bridge mappings,
  challenge windows, and watcher readiness before enabling user traffic.
- Keep rollback and emergency pause procedures available during the first
  production finality window.
