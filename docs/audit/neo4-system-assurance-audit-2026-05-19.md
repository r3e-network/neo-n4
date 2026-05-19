# Neo 4 system assurance audit

Date: 2026-05-19

## Scope

This checkpoint reviews the maintained `r3e-network/neo-n4` repository as a
Neo Stack Layer-2 system, with emphasis on completeness, professional structure,
architecture quality, user understanding, consistency, and local correctness.

The pass covers:

- Main repository inventory, documentation, diagrams, and user-facing guides.
- NeoHub deployable L1 contract suite and deployment tooling.
- N4 Layer-2 runtime modules, native L2 contracts in the r3e Neo core fork, and
  the NeoVM2/RISC-V execution boundary.
- NeoFS data-availability integration points and validation evidence.
- ZK verification routing through deployable NeoHub contracts plus L1 native
  accelerator hooks.
- SDKs, CLIs, samples, watchers, external bridge adapters, and test evidence.

Out of scope for a local-only assurance pass:

- A real public Neo devnet/testnet deployment with funded accounts.
- A live governance or multisig ceremony.
- Real external-chain liquidity and cross-chain routes.
- A fresh GitHub Actions run after this commit lands on `origin/master`.
- A third-party cryptographic/security audit.

## Evidence snapshot

| Area | Result |
| ---- | ------ |
| Repository ownership | Current remote is `origin` under `r3e-network/neo-n4`; no push target points to `neo-project`. |
| Repository inventory | 874 tracked files before this pass; this pass adds the missing localized Experience Hub assets and this audit record. |
| Core fork boundary | `external/neo` points at `r3e-network/neo`, branch `r3e/neo-n4-core`; L1/L2 Neo core changes remain in r3e-maintained branches. |
| VM boundary | NeoVM2/RISC-V is the canonical N4 Layer-2 execution path; optional VM support is modeled as N4 L2 execution profiles, not as NeoX. |
| Documentation parity | The missing Chinese counterparts for the two Experience Hub PNG assets were identified and fixed. |
| .NET tests | `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo` passed after the documentation fix. |
| Test count observed | 1,452 .NET tests passed across solution projects in the full run. |
| Rust tests | `cargo test --workspace --all-targets --locked` passed under WSL2. |
| TypeScript tests | `npm test` and `npm run build` passed for `sdk/typescript`. |
| Interactive tests | Node tests for Experience Hub report schemas and Interactive Runtime simulator passed. |
| Documentation build | `mdbook build` passed under WSL2. |
| Whitespace check | `git diff --check` reported no blocking whitespace error; PowerShell warned only that one touched file will normalize CRLF on next Git write. |

## Findings and disposition

### Fixed: Experience Hub figures lacked Chinese counterparts

The documentation localization test found that the newly added Experience Hub
PNG assets existed only in the English documentation tree:

- `docs/figures/experience-hub/neo-n4-experience-hub.png`
- `docs/experience-hub/concepts/neo-n4-experience-hub-concept.png`

Fix:

- Added `docs/zh/figures/experience-hub/neo-n4-experience-hub.png`.
- Added `docs/zh/docs/experience-hub/concepts/neo-n4-experience-hub-concept.png`.
- Updated `docs/zh/README.md` so the Chinese README uses the Chinese-side
  Experience Hub screenshot path.

This closes the active localization regression found by the full .NET solution
test run.

### Reviewed: historical `StubCommands.cs` filename is misleading but functional

The broad placeholder scan flagged
`tools/Neo.Stack.Cli/Commands/StubCommands.cs`. Manual review found that the
file is not an empty or fake implementation: it contains functional command
handlers for chain registration, bridge adapter deployment planning, service
preflights, and batch submission validation. The filename is historical.

Disposition:

- No correctness fix is required for this checkpoint.
- A future hygiene-only rename would reduce audit noise, but it should be done
  separately to avoid mixing cosmetic churn into assurance evidence.

### Reviewed: test-only stub contracts and handlers are contained

The scan also found test and fixture stubs, including
`NeoHub.ExternalBridgeStubVerifier` and unit-test HTTP/RPC handlers. These are
used as deterministic test doubles and are not part of the production NeoHub
deploy plan.

Disposition:

- Keep them, because they improve deterministic validation coverage.
- Continue to exclude test-only verifier contracts from production deployment
  manifests.

## Module-level assessment

| Module family | Assessment | Evidence / rationale |
| ------------- | ---------- | -------------------- |
| NeoHub L1 contracts | Structurally complete for deployable-contract architecture. | Contracts are split by responsibility; deployment tooling and tests cover plan generation and production-step boundaries. NeoHub remains deployable contract code instead of invasive L1 native-contract code. |
| L1 native ZK accelerator | Correct architectural boundary. | Heavy ZK math belongs in native accelerator hooks, while `NativeZkVerifier` remains the deployable adapter exposed to NeoHub governance and settlement flows. |
| L2 native contracts | Correct place for N4 chain-internal system behavior. | L2-specific native contracts live in the r3e Neo core fork, not in ad hoc post-deploy contracts. |
| Execution | Advanced and appropriately layered. | NeoVM2/RISC-V is the default execution model; shared Rust execution core keeps proof input logic backend-neutral; optional VM profiles are extension points. |
| Data availability | Design is aligned with the stated requirement to use NeoFS as DA. | Documentation and Experience Hub now describe NeoFS DA as a first-class layer with content addressing, replication, and proof-of-storage validation. |
| Bridge and messaging | Good modularity. | Shared bridge, message router, token registry, forced inclusion, challenge, and censorship modules are separated and tested. |
| Assets | Correct L1/L2 decimal model. | L1 NEO remains indivisible while L2 NEO/GAS and canonical USDT/USDC/BTC mappings carry explicit decimals and scaling checks. |
| External chains | Extensible model is reasonable. | Foreign-chain bridge adapters and watcher crates are separated by chain, which keeps Avalanche/EVM-like expansion from contaminating NeoHub core logic. |
| Tooling | Operator-facing coverage is strong. | `Neo.Stack.Cli`, bridge CLI, deploy tooling, devnet tooling, faucet tooling, and explorer tooling are covered by solution tests. |
| User experience | Improved and now documentation-backed. | The Experience Hub provides a visual control-room style explanation of architecture, dataflow, validation evidence, and test evidence. |
| Localization | Enforced by tests. | Markdown, SVG, and figure counterpart rules catch missing Chinese assets; this pass fixed the active PNG gap. |
| Security posture | Locally sound for this checkpoint. | No new high-impact issue was identified in reviewed changed areas; remaining high-assurance gates require live networks and third-party review. |

## Architecture verdict

The repository is moving in the right direction for a professional Neo Stack
Layer-2 implementation:

- The L1 change surface is intentionally small: NeoHub is deployable contract
  code plus plugins/services, while only performance-critical cryptographic
  acceleration needs native L1 support.
- N4 Layer-2 core changes live in the r3e Neo core fork, so native L2 behavior
  is owned by the L2 execution core rather than bolted on after deployment.
- NeoFS is treated as the data-availability layer instead of an optional storage
  afterthought.
- The VM architecture is future-compatible without confusing the project with
  NeoX: NeoVM2/RISC-V stays canonical and other VMs can be introduced as N4 L2
  execution profiles.
- The documentation set now has a visual entry point, detailed architecture
  guides, workflow diagrams, and localized Chinese counterparts.

## Remaining production gates

These are not local code defects, but they must be closed before claiming a
production network is fully validated:

- Run the same CI workflow on GitHub after the current changes are pushed.
- Rehearse a real Neo public devnet/testnet deployment with funded accounts.
- Record real contract hashes, governance/multisig addresses, DA CIDs, proof
  job IDs, and bridge transaction hashes.
- Exercise real L1-to-L2, L2-to-L1, and L2-to-L2 asset movement with failure
  drills.
- Run a third-party security and cryptography review before mainnet value.

## Verification commands for this checkpoint

```powershell
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj --filter CurrentDocumentation_EveryEnglishFigureHasChineseCounterpart /p:NuGetAudit=false --nologo
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj --filter CurrentDocumentation /p:NuGetAudit=false --nologo
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo
node --test tests\interactive-runtime\simulator.test.mjs tests\experience-hub\hub-state.test.mjs tests\experience-hub\manifest-generator.test.mjs tests\experience-hub\report-schemas.test.mjs
npm test
npm run build
git diff --check
```

```bash
cargo test --workspace --all-targets --locked
mdbook build
```

Observed local result:

- Documentation counterpart tests passed: 5 passed, 0 failed.
- Experience Hub and Interactive Runtime Node tests passed: 15 passed, 0 failed.
- Full `.NET` solution test passed with 1,452 tests observed.
- Root Rust workspace tests passed under WSL2: 70 passed, 0 failed, 2 ignored real-prover tests.
- TypeScript SDK tests passed: 15 passed, 0 failed; `tsc` build passed.
- `mdbook build` passed under WSL2.
- Whitespace check had no blocking error.
