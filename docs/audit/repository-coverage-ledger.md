# Repository Coverage Ledger

Generated: 2026-05-17 15:27 Asia/Shanghai

Status vocabulary: `closed`, `fixed`, `deferred`, `not_applicable`.

| Row | Area | Boundary | Families / Controls | Status | Evidence |
| --- | --- | --- | --- | --- | --- |
| R1 | `contracts/NeoHub.SharedBridge` | Asset release authorization | Withdrawal leaf binding, replay, token registry mapping | fixed | Contract direct builds pass; full .NET test suite passes; earlier audit fixed withdrawal preimage/token mapping checks |
| R2 | `contracts/NeoHub.SettlementManager` | Batch and withdrawal root finality | Proof type routing, challengeable status, finalization/revert authorization | fixed | Optimistic challenge path fixed; `Neo.L2.Proving.UnitTests` included in 1,423 passing .NET tests; SettlementManager contract builds |
| R3 | `contracts/NeoHub.*` registries | Governance and registry authorization | Owner/governance controls, bridge-kind registration, devnet stub exclusion | closed | `ExternalBridgeRegistry` bridge kinds gate production registration; docs corrected; contract artifacts verified |
| R4 | `contracts/L2Native.*` | L2 native bridge and message contracts | Mint/burn permissions, message routing, system config | closed | Solution build, direct contract build, NEF/manifest verification, and full test suite pass |
| R5 | `src/Neo.L2.*` canonical encoders | Wire format and hash invariants | Endian handling, hash256, Merkle proof serialization, optimistic payload layout | fixed | Optimistic wire format updated; .NET proving tests pass; watcher parity tests pass |
| R6 | `src/Neo.Plugins.L2*` | Node plugin runtime surfaces | RPC methods, plugin init, metrics, gateway/batch/bridge/prover plugins | closed | Full .NET test suite covers plugin unit projects; NuGet vulnerable scan reports none |
| R7 | `tools/*` | CLI operator surfaces | File IO, JSON parsing, deployment plan correctness, wallet-plan outputs | fixed | `Neo.External.Bridge.Cli.UnitTests` added; `deploy-bundle` now rejects malformed EVM addresses and committee blob hex; CLI unit tests included in 1,423 .NET tests |
| R8 | `watchers/*` | Foreign bridge watcher daemons | RPC trust, replay journals, signature proof construction, health endpoints | closed | ETH watcher release tests + clippy pass; SOL/TRON watcher tests + clippy pass; WSL2 workspace release tests and clippy pass |
| R9 | `external/foreign-contracts/eth|sol` | Foreign on-chain routers | Signature threshold, replay, per-chain isolation, Solana PDA constraints | closed | Foundry ETH tests pass; Solana cargo tests pass |
| R10 | `bridge/neo-zkvm-*` | ZK proving boundary | Guest input parsing, host/prover trust, SP1 dependency/platform constraints | closed | WSL2 SP1 6.2.1 installed; final `cargo prove build`, `cargo test --release --locked`, ignored real-proof tests, `cargo fmt`, and workspace clippy pass; build-script/profile warnings cleaned; Rust advisory exceptions documented |
| R11 | `sdk/*` | Client SDKs | RPC decoding, error taxonomy, type safety | closed | TypeScript tests/build/audit pass; Rust SDK tests/build pass; Rust SDK clippy passes under workspace gate |
| R12 | `docs/*` and root docs | Documentation completeness | Executable commands, counts, architecture diagrams, production warnings | fixed | Final mdBook build passes; visual guide generated; SP1 docs corrected; test counts synchronized; Rust supply-chain policy and release readiness checklist added |
| R13 | `.github/*`, build props, deploy artifacts | CI/deploy verification | Test matrix, contract artifacts, supply-chain audit coverage | fixed | `.github/workflows/build.yml` expanded to include SP1 6.2.1, Rust fmt/clippy/workspace release tests, cargo audit, TypeScript build/audit, foreign Solana tests, and optional real-proof workflow dispatch; YAML parses locally; in-process devnet default/counter/neovm rehearsals pass |
| R14 | `external/upstream` | Pinned upstream dependency boundary | Upstream code boundary and advisory posture | closed | Treated as upstream boundary, not first-party manual-edit scope; package/advisory scans recorded |

Remaining release gates are not hidden pass results:

- The expanded CI workflow must still run successfully on GitHub after the changes are pushed.
- A public Neo N4 devnet/testnet deployment rehearsal with funded accounts, RPC endpoints, and operator credentials is still required before production release.
