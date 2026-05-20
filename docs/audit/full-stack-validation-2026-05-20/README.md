# Neo N4 Full-Stack Validation Report

Date: 2026-05-20
Repository: `D:\Git\neo-n4`
Branch: `master`
HEAD: `2ba7db9`

## Result

Validated with recorded evidence. The implementation builds, tests, compiles contracts, runs private devnet smoke tests with NeoFS DA, runs the canonical NeoVM2/RISC-V path, produces and verifies an SP1 zk proof, validates EVM/Solana bridge components, builds SDK/docs, verifies supply-chain posture, and revalidates the current Neo N3 testnet deployment through RPC.

This report does not claim that every production operator action is complete. The remaining production gates are listed below.

## Curated Evidence Index

Raw command output was used as local working evidence and is intentionally not retained in `docs/audit`. The repository keeps compact Markdown and JSON evidence that can be reviewed, localized, and versioned:

- `09-testnet-rpc.json`: independent Neo testnet RPC validation for the current contract-first deployment.
- `testnet-dry-run.json`: deployment dry-run evidence for the 23 production NeoHub contracts.
- `10-contract-zk-verifier.json`: chain ABI, safe defaults, malformed proof faulting, and `VerifierRegistry` ZK route validation.
- `11-docs-consistency.md`: obsolete terminology scan and documentation consistency evidence.
- `README.md`: human-readable full-stack validation summary.
- `../testnet-deployment-2026-05-20-contract-first.json`: current testnet deployment report.
- `../testnet-deployment-2026-05-20-contract-first-dry-run.json`: current testnet dry-run report.

## Pass Summary

- .NET solution: restore/build/format/test passed after adding the missing Chinese counterpart for this validation plan.
- Contract compilation: 26 deployable `.nef`/manifest pairs produced; 46 manifest/deploy planner tests passed.
- Neo core and execution: 10 L2 native core tests and 10 NeoVM2/RISC-V executor tests passed.
- Private devnet: general, gaming, exchange validium, and privacy sidechain configs completed; RISC-V execution mode completed; all runs reported NeoFS DA and `da_availability`.
- Rust and zkVM: workspace tests passed; PolkaVM host checked; SP1 real proof generated and verified.
- Foreign bridge surface: 39 EVM tests and 22 Solana tests passed.
- SDK/docs/UI data: TypeScript SDK 16 tests passed; Experience Hub Node tests passed; mdBook built; documentation gap tests passed.
- Testnet: 23/23 planned contracts exist on `https://testnet1.neo.coz.io:443`; 13 recorded transactions are `HALT`; 23 contract states are present.

## Testnet State

- Network magic: `894710606`
- Owner address: `NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu`
- `ContractZkVerifier`: `0xd52484a842b97555c56bd93ecf919df3f78366f7`
- `VerifierRegistry`: `0x3b96ba201a2ef32f98da7b72e14acb0329b6e017`
- `VerifierRegistry.getVerifier(ProofType.Zk=3)`: `0xd52484a842b97555c56bd93ecf919df3f78366f7`
- Live deployment action in this pass: skipped because the existing live deployment report was independently verified through RPC.

## Security Notes

- NuGet vulnerable package scan: no vulnerable packages.
- npm audit for TypeScript SDK: 0 vulnerabilities.
- cargo audit: 0 direct vulnerabilities; upstream warnings remain for `ansi_term`, `bincode`, `number_prefix`, `paste`, `rustls-pemfile`, and `lru`.
- `lru 0.12.5` is pulled through `sp1-prover -> sp1-sdk -> neo-zkvm-host`; monitor SP1 upgrades or vendor/pin a patched path when upstream supports it.
- Exact scan confirmed the provided testnet WIF is absent from Git-tracked files.
- Broad secret-pattern matches were reviewed as placeholders, tests, lockfile hashes, generated/audit evidence, or Kubernetes secret references; no real project credential was confirmed.

## Remaining Production Gates

- `ContractZkVerifier` is deployed and wired.
- It is safe by default because no proof verifier, verification key, or envelope-only mode is enabled for public testnet proof systems.
- Production ZK acceptance still requires registering real proof verifier contracts and verification keys through the intended governance/operator flow.
- External bridge committee registration and per-chain `L1TxFilter` wiring remain operator-specific actions for each supported foreign chain.
- Browser-level visual validation of the Experience Hub was not completed because the Browser plugin execution entrypoint was not exposed in this session; Node-level hub tests and mdBook validation did pass.

## Fixes Made During Validation

- Added missing Chinese counterpart for the full-stack validation plan.
- Removed user-facing NeoX framing from `doc.md`, `docs/zh/doc.md`, and Experience Hub copy.
- Added Chinese counterpart for the Phase 11 documentation consistency report.
