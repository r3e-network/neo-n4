# Systematic Review - 2026-05-17

Repository: `r3e-network/neo-n4`
Local path: `D:\Git\neo-n4`
Inventory rule: excludes this generated audit directory; treats `external/neo*`
as upstream/submodule boundary rather than first-party implementation.

## Executive Result

The follow-up audit found one concrete production-readiness gap in the
first-party operator surface: `neo-external-bridge deploy-bundle` accepted
malformed EVM router/member addresses and malformed committee blob hex while
still printing an operator checklist. That is now fixed and directly tested.

Current local verification result:

| Evidence | Result |
| --- | ---: |
| Solution projects | 102 |
| First-party `.csproj` files in parent repo | 97 |
| Test projects | 34 |
| Executed .NET tests | 1,423 passed, 0 failed |
| Inventory files reviewed | 759 |
| Candidate source symbols indexed | 3,708 |
| Markdown documents indexed | 88 |

The remaining release gates are still external to this local checkout:

- run the updated GitHub Actions workflow on GitHub after push;
- execute a real public Neo N4 devnet/testnet deployment rehearsal with funded
  accounts, RPC endpoints, contract deployments, and operator signatures.

## Evidence Files

| File | Purpose |
| --- | --- |
| `file-inventory.csv` | Per-file path, area, kind, line count, and byte count. |
| `area-summary.csv` | File/line/byte totals by architecture area. |
| `kind-summary.csv` | File/line/byte totals by language or file kind. |
| `symbol-index.csv` | Regex-based first-party function/type/symbol index for C#, Rust, TS, and Solidity. |
| `symbol-summary.csv` | Symbol totals by architecture area. |
| `dotnet-project-ledger.csv` | First-party project package refs, project refs, target framework, output type. |
| `test-entrypoint-ledger.csv` | Test projects, test files, static `[TestMethod]` count. |
| `module-test-coverage.csv` | One-to-one project/test-project mapping check. |
| `document-ledger.csv` | Markdown title, locale, word count, and zh/en counterpart check. |

## Fixed Finding

### F1 - External bridge deploy plan accepted malformed EVM/hex input

Severity: high for operator safety; fixed.

Boundary: `tools/Neo.External.Bridge.Cli`

Before:

- `--eth-router` could be `0x1234`;
- `--eth-addresses` entries could be malformed;
- `--committee-blob` only had a length check and could contain non-hex bytes;
- the CLI would still print a deployment runbook that looked actionable.

Fix:

- `DeployBundleCommand` now decodes and validates the Eth router as a 20-byte
  hex address.
- each Eth committee member is decoded and validated as a 20-byte hex address.
- committee blob input is decoded as non-empty hex bytes before size checks.
- printed EVM addresses and blobs are normalized to canonical lowercase `0x...`.
- `tests/Neo.External.Bridge.Cli.UnitTests` was added and included in
  `Neo.L2.sln`.

Validation:

- `dotnet test tests\Neo.External.Bridge.Cli.UnitTests\Neo.External.Bridge.Cli.UnitTests.csproj /p:NuGetAudit=false --nologo`
- `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo`

## Module Assessment

| Area | Assessment |
| --- | --- |
| Contracts | Artifact-oriented projects are present and build through the solution. Production confidence still depends on live devnet/testnet rehearsals because local unit tests cannot prove deployment wiring, account funding, and signer policy. |
| Runtime libraries | Broad direct unit coverage exists for state, proving, settlement RPC, bridge, messaging, sequencer, persistence, executor, audit, censorship, and forced-inclusion logic. |
| Node plugins | Direct unit projects cover plugin initialization, RPC surfaces, metrics, DA, gateway, bridge, batch, prover, and settlement plugin behavior. |
| Operator tools | Direct test coverage exists for the major CLIs; the external bridge CLI gap found by this review is fixed. |
| Watchers and foreign contracts | Treated as first-party where tracked outside upstream Neo; prior audit evidence covers watcher Rust tests/clippy and foreign contract tests. |
| SDKs | TypeScript and Rust SDK surfaces remain covered by the cross-language test gates recorded in the production-readiness audit. |
| Docs | Test/project counts were synchronized to 1,423 .NET tests across 34 projects and 102 solution projects; zh/en counterpart checks are recorded in `document-ledger.csv`. |
| Upstream Neo | `external/neo*` is a pinned upstream boundary. It is not counted as first-party function-review scope in this ledger. |

## Coverage Notes

The review uses full indexing plus risk-prioritized manual inspection. Every
included first-party file is listed in `file-inventory.csv`, and every indexed
candidate source symbol is listed in `symbol-index.csv`. Manual deep review was
focused on trust boundaries where defects have production impact: bridge
operator input, deployment plans, settlement/challenge paths, cross-chain
message formats, watcher/foreign-chain ingress, RPC surfaces, and release
documentation.

The one-to-one module/test mapping is intentionally mechanical. It will flag
artifact-only smart-contract projects that do not have `tests/<project>.UnitTests`
twins even when covered through deploy/integration tests. Those rows are useful
for planning stricter per-contract simulation tests, but they are not treated as
new blocking defects by themselves.
