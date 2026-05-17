# Security Audit Report - 2026-05-17

This report records the repository security pass performed on 2026-05-17.
The review focused on bridge-finalization authorization, committee identity
binding, dependency vulnerability exposure, secret leakage, and CI token scope.

## Scope

- Neo L2 .NET solution, contracts, CLIs, plugins, and tests.
- EVM external bridge router and Foundry tests.
- Solana external bridge router and Anchor dependencies.
- Rust bridge watchers and Rust SDK.
- TypeScript SDK dependency audit.
- GitHub Actions security-sensitive token usage.

## Fixed Findings

### EVM router accepted empty proofs before committee registration

`NeoExternalBridgeRouter._verifyQuorum` previously allowed the deployment-time
default state `committee.length == 0` and `threshold == 0`. If funds were locked
before `setCommittee`, an attacker could submit an empty proof header and
finalize a correctly shaped withdrawal up to the router's locked balance.

Fix:

- Require a non-empty registered committee before proof verification.
- Require `threshold > 0 && threshold <= committee.length`.
- Reject `sigCount > committee.length`.
- Added a Foundry regression test proving empty proofs are rejected before
  committee registration.

Files:

- `external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`
- `external/foreign-contracts/eth/test/NeoExternalBridgeRouter.t.sol`
- `external/foreign-contracts/eth/README.md`

### Solana sigverify parser did not bind offset instruction indices

The Solana bridge compared pubkey and message bytes inside the ed25519
precompile instruction, but it did not require the signature, pubkey, and
message offset instruction indices to point to the same instruction. Solana's
ed25519 program supports cross-instruction references, so a malformed
precompile instruction could cause the runtime to verify bytes from another
instruction while the bridge parser compared attacker-controlled bytes in the
current instruction.

Fix:

- Parse and validate `sig_instruction_index`, `pubkey_instruction_index`, and
  `message_instruction_index`.
- Require all three instruction indices to be `u16::MAX`, meaning the precompile
  verified the same in-instruction tuple that the bridge inspects.
- Bound-check the signature, pubkey, and message slices before comparing.
- Added unit tests for accepted same-instruction offsets and rejected
  cross-instruction pubkey/message offsets.
- Upgraded the Solana router from Anchor 0.30 to Anchor 1.0.2, removing the
  vulnerable `curve25519-dalek 3.2.1` dependency path reported by RustSec.

Files:

- `external/foreign-contracts/sol/programs/neo-external-bridge-router/src/lib.rs`
- `external/foreign-contracts/sol/programs/neo-external-bridge-router/Cargo.toml`
- `external/foreign-contracts/sol/Cargo.lock`
- `external/foreign-contracts/sol/README.md`

### Deployment CLI did not bind Neo committee pubkeys to EVM addresses

`neo-external-bridge deploy-bundle` validated committee blob length and threshold
shape, but it did not verify that each provided EVM address was derived from the
same secp256k1 compressed pubkey at the same committee index. This could produce
an apparently valid deploy bundle with mismatched identities, duplicate voting
power, or unusable cross-chain committee configuration.

Fix:

- Decode the committee blob as 33-byte compressed secp256k1 public keys.
- Reject invalid secp256k1 points.
- Reject duplicate committee pubkeys.
- Reject duplicate EVM committee addresses.
- Derive `keccak256(uncompressed_pubkey[1..])[12..]` and require it to match
  `--eth-addresses[i]`.
- Added unit tests for invalid points, duplicate pubkeys, duplicate addresses,
  and pubkey/address mismatches.

Files:

- `tools/Neo.External.Bridge.Cli/Commands/DeployBundleCommand.cs`
- `tests/Neo.External.Bridge.Cli.UnitTests/UT_ExternalBridgeCli.cs`

### Panic hardening

Several already-validated decoding paths used `unwrap()` after length checks.
These were not observed as exploitable in the tested flows, but they were
rewritten to avoid panic-prone idioms in production paths.

Files:

- `external/foreign-contracts/sol/programs/neo-external-bridge-router/src/lib.rs`
- `sdk/rust/src/lib.rs`
- `watchers/neo-bridge-watcher-eth/src/bin/neo-bridge-watcher-eth.rs`
- `watchers/neo-bridge-watcher-eth/src/core.rs`

## Verification Evidence

All commands below completed successfully on Windows with WSL2 where needed.

- `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo`
- `dotnet list Neo.L2.sln package --vulnerable --include-transitive`
  - No vulnerable NuGet packages reported for solution projects.
- `external/foreign-contracts/eth`: `forge test -vv`
  - 21 tests passed.
- `external/foreign-contracts/sol`: `cargo test`
  - 4 tests passed.
- Root Rust watchers: `cargo test -p neo-bridge-watcher-eth -p neo-bridge-watcher-sol -p neo-bridge-watcher-tron`
  - Watcher tests and doctest passed.
- `sdk/rust`: `cargo test`
  - 10 tests passed.
- `sdk/typescript`: `npm audit --audit-level=moderate`
  - 0 vulnerabilities.
- Root `cargo audit --no-fetch --stale`
  - No vulnerability failures; 6 upstream maintenance/unsoundness warnings
    remain through SP1 transitive dependencies.
- Solana router `cargo audit --no-fetch --stale`
  - No vulnerability failures; 1 upstream `bincode` unmaintained warning remains
    through Anchor/Solana transitive dependencies.
- Secret scan:
  - No committed private keys or high-confidence tokens found.
  - The only token-pattern hit was `${{ secrets.GITHUB_TOKEN }}` in the GHCR
    publish workflow, scoped with `contents: read` and `packages: write`.

## Remaining Risk Register

No known exploitable issue remains in the reviewed bridge/CLI paths after these
fixes and tests. The following items are not closed by source-only auditing and
should remain tracked for production readiness:

- Run a real Neo N4 devnet/testnet deployment rehearsal covering bridge
  registration, committee rotation, EVM lock/finalize, Solana lock/finalize,
  watcher restart, replay rejection, and failed-submission recovery.
- Monitor SP1 and Anchor/Solana dependency releases for the RustSec warning-only
  items currently reported through transitive dependencies.
- Consider adding a CI security lane that runs `cargo audit`, `npm audit`, the
  NuGet vulnerable package check, and a secret scanner on every pull request.

