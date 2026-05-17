# ZKsync Elastic Chain Comparison Validation - 2026-05-17

This audit compares Neo Elastic Network (`neo4`) with ZKsync Elastic Chain / ZK
Stack as documented by ZKsync on 2026-05-17. The goal is not source-level
equivalence. ZKsync is Ethereum/EraVM/zkEVM based; neo4 is NeoVM/NEP-17/NeoFS
based. The validation target is functional parity: whether neo4 provides the
same security and operator properties, intentionally diverges for a sound Neo
reason, or still has a production gap.

## Official ZKsync Baseline

Primary references:

- ZKsync Chains: shared bridge, interoperability, unified governance, modular
  chain customization:
  <https://docs.zksync.io/zk-stack/zk-chains>
- Shared Bridges: Bridgehub, chain registry, CTM, shared bridge, shared
  upgrades:
  <https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/shared-bridges>
- L1 contracts: DiamondProxy, Mailbox, Executor, ValidatorTimelock:
  <https://docs.zksync.io/zksync-protocol/contracts/l1-contracts>
- Gateway overview and features: optional proof aggregation middleware,
  Ethereum as root of trust, no Gateway custody:
  <https://docs.zksync.io/zksync-protocol/gateway>
  <https://docs.zksync.io/zksync-protocol/gateway/features>
- Validium / DA validation:
  <https://docs.zksync.io/zk-stack/customizations/validium>
- Local ZK Stack launch and operator tooling:
  <https://docs.zksync.io/zk-stack/running/quickstart>
- Transaction filtering:
  <https://docs.zksync.io/zk-stack/customizations/transaction-filtering>
- Custom base tokens:
  <https://docs.zksync.io/zk-stack/customizations/custom-base-tokens>
- ZKsync Connect messaging:
  <https://docs.zksync.io/zksync-network/unique-features/zksync-connect/messaging>
- Native account abstraction:
  <https://docs.zksync.io/zksync-protocol/era-vm/differences/native-vs-eip4337>

## Executive Verdict

Neo4 is a credible ZKsync Elastic Chain-inspired architecture, not a copy of
ZKsync. Its high-level shape is correct:

- One L1 hub contract suite anchors many L2 chains.
- A shared bridge and token registry avoid per-chain liquidity fragmentation.
- Chains carry a security label and chain config.
- Settlement, proof verification, DA, gateway aggregation, forced inclusion,
  governance, and emergency paths are separated into explicit modules.
- Devnet, deploy-plan, bridge, faucet, explorer, external-bridge, and stack CLI
  tooling exist.
- Real local rehearsal and remote CI have been run after the latest changes.

The project is professionally structured and substantially complete as a
framework/devnet stack. It should not be described as turnkey mainnet-equivalent
to ZKsync Elastic Chain yet. The remaining gaps are concrete and tracked:

1. On-L1 DA inclusion verifier for validium/DAC modes.
2. Canonical bridged NEP-17 template for L1 asset mappings.
3. Programmable L2 account abstraction equivalent to ZKsync native AA.
4. Staged upgrade timer with explicit notice/execution/cooldown windows.
5. Optional per-chain L1->L2 transaction filter hook.
6. L2-side interop message verifier equivalent to ZKsync Connect's L2 verifier.
7. Production examples for NeoFS, L1 signer, KMS/HSM, and dBFT validator-set
   wiring.
8. Public devnet/testnet deployment evidence with real RPC, funded accounts,
   governance accounts, and foreign testnet routes.

## System-Level Comparison

| Area | ZKsync Elastic Chain baseline | neo4 implementation | Verdict |
| --- | --- | --- | --- |
| Ecosystem model | Many ZKsync Chains form an Elastic Network, settling to Ethereum and sharing bridge/governance assumptions. | Many Neo L2s settle to NeoHub on Neo L1; security labels and chain config are explicit. | Functionally aligned. |
| L1 coordination hub | Bridgehub routes chain IDs, shared bridges, and chain contracts. | `NeoHub.ChainRegistry`, `MessageRouter`, `SharedBridge`, `SettlementManager`, `VerifierRegistry`, `TokenRegistry`, `DARegistry`. | Aligned, split by concern instead of one Bridgehub. |
| Chain type management | CTM deploys/initializes chain contracts and coordinates shared upgrades. | `VerifierRegistry` + `GovernanceController` + `neo-hub-deploy` planner. | Partial. Correct abstraction, but no on-chain per-chain factory or staged CTM-style upgrade workflow. |
| Shared bridge | Bridgehub/shared bridges lock assets once on L1 for all chains. | `NeoHub.SharedBridge` holds deposits and proof-based withdrawals; bridge CLI emits canonical invocation hex. | Strong functional parity. |
| Canonical assets | ZKsync shared bridges deploy/verify canonical L2 token contracts. | `TokenRegistry` maps assets; L2 bridge exists, but no default `L2Native.BridgedNep17Contract`. | Gap. |
| Batch settlement | ExecutorFacet commits/proves/executes batches and checks DA/proofs. | `SettlementManager`, proof payloads, verifier registry, SP1 prover host, optimistic challenge path. | Strong off-chain/devnet parity; public-network deployment still open. |
| Data availability | Rollup and validium modes; Stage-2 validium verifies DA inclusion on L1 via validators. | `DARegistry`, `L2DAPlugin`, `JsonRpcL1DAWriter`, `CommitteeAttestedDAWriter`, `NeoFsLikeDAWriter`. | Partial. L1 DA validator contract is missing. |
| Gateway/proof aggregation | Gateway aggregates proofs from multiple chains; assets remain locked on Ethereum, not Gateway. | `Neo.Plugins.L2Gateway.BinaryTreeAggregator`, `IRoundProver` implementations, `MessageRouter.PublishGlobalRoot`; assets remain in NeoHub. | Architecturally aligned. Recursive-ZK round prover remains operator-supplied. |
| Interop | ZKsync Connect verifies messages via Gateway MessageRoot and L2 message verifier. | L1-side message router and global root exist; L2-side local verifier does not. | Partial. |
| Sequencing/censorship | Configurable sequencing; priority queue escape hatch. | `SequencerRegistry`, `SequencerBond`, `ForcedInclusion`, `CensorshipDetector`, RPC source abstractions. | Strong framework parity; production dBFT selector wiring is still operator-specific. |
| Governance/security response | Shared governance, security council, freezing, permanent restrictions, ValidatorTimelock. | `GovernanceController`, `EmergencyManager`, immutable flags, timelock, challenge windows. | Strong but not complete; staged upgrade windows remain a gap. |
| Account abstraction/paymasters | Native AA at protocol level; all accounts and EOAs can use paymasters. | `L2PaymasterContract` top-up/sponsor model; no programmable account abstraction contract. | Gap. |
| Transaction filtering | Operators can filter L1->L2 and L2 transactions. | No per-chain `IL1TxFilter`; mempool/API filtering is not standardized. | Gap. |
| Tooling | `zkstack`, `zksync-cli`, Docker/local launch, Foundry deployment integration. | `Neo.Stack.Cli`, `Neo.Hub.Deploy`, `Neo.L2.Devnet`, bridge/faucet/explorer CLIs, local rehearsal script. | Good professional coverage; less turnkey than ZK Stack. |
| SDKs/samples | EVM ecosystem tooling plus many tutorials. | .NET/TS/Rust SDKs and several samples. | Partial; Go/Python SDKs and more app samples are missing. |

## Correctness Check By ZKsync Property

### 1. Shared L1 Trust Root

ZKsync: all chains inherit final verification and asset security from Ethereum.
Gateway is optional middleware and does not custody assets.

Neo4: NeoHub is the trust root. Gateway aggregation is optional and assets stay
in `NeoHub.SharedBridge` / external bridge escrows, not in the gateway plugin.

Verdict: correct architecture. The project should keep saying "NeoHub is the
root of trust" rather than implying Neo Gateway is a custody layer.

### 2. BridgeHub / Registry / SharedBridge

ZKsync: Bridgehub acts as the hub for bridges and chain registration; shared
bridges lock assets for all chains.

Neo4 evidence:

- `contracts/NeoHub.ChainRegistry/ChainRegistryContract.cs`
- `contracts/NeoHub.MessageRouter/MessageRouterContract.cs`
- `contracts/NeoHub.SharedBridge/SharedBridgeContract.cs`
- `contracts/NeoHub.TokenRegistry/TokenRegistryContract.cs`
- `tools/Neo.L2.Bridge.Cli/`

Verdict: complete at the framework layer. The split-contract design is more
Neo-native and easier to audit than copying Diamond/Bridgehub literally.

Remaining correctness issue: token canonicalization is incomplete until a
default bridged NEP-17 template exists.

### 3. Chain Type / Upgrades / Governance

ZKsync: CTM and shared upgrades keep chain implementations and verifiers aligned
across the ecosystem. Normal/shadow/instant upgrades and frozen non-updated
chains are part of the model.

Neo4 evidence:

- `contracts/NeoHub.VerifierRegistry/`
- `contracts/NeoHub.GovernanceController/`
- `contracts/NeoHub.EmergencyManager/`
- `tools/Neo.Hub.Deploy/`

Verdict: partially complete. Governance, verifier approval, immutable flags, and
timelock exist, but the project lacks CTM-grade staged upgrade semantics:
`propose -> notice -> execute -> cool-down -> freeze non-upgraded chain`.

Professional recommendation: track this as a production governance hardening
task, not as an MVP blocker.

### 4. Batch Settlement And Proof Systems

ZKsync: ExecutorFacet accepts batches through commit/prove/execute and validates
proofs/data availability.

Neo4 evidence:

- `contracts/NeoHub.SettlementManager/`
- `contracts/NeoHub.VerifierRegistry/`
- `contracts/NeoHub.OptimisticChallenge/`
- `contracts/NeoHub.RestrictedExecutionFraudVerifier/`
- `bridge/neo-zkvm-host/`
- `external/neo-zkvm/`
- `src/Neo.L2.Proving/`
- `src/Neo.L2.Executor.RiscV/`

Verdict: strong and credible. Neo4 has real proof seams and a real SP1 zkVM path
for Neo VM execution, plus an optimistic challenge path.

Correctness caveat: public testnet/mainnet finality has not been proven without
real RPC endpoints and funded governance/operator accounts. Local rehearsal is
necessary evidence, not a substitute for public deployment.

### 5. Data Availability

ZKsync: validium has stages, with Stage 2 verifying DA inclusion on L1 through
`L1DAValidator`-style contracts.

Neo4 evidence:

- `contracts/NeoHub.DARegistry/`
- `src/Neo.Plugins.L2DA/`
- `src/Neo.L2.Abstractions/IDAWriter.cs`
- `src/Neo.Plugins.L2DA/CommitteeAttestedDAWriter.cs`

Verdict: partial. DA modes and off-chain writers are real. The missing piece is
an on-L1 `NeoHub.DAValidator` that verifies DAC/NeoFS/L1 inclusion proofs during
settlement.

This is a correctness gap for validium production claims.

### 6. Gateway And Interop

ZKsync: Gateway aggregates proofs and supports Gateway-based interop through
MessageRoot and L2-side verification.

Neo4 evidence:

- `src/Neo.Plugins.L2Gateway/BinaryTreeAggregator.cs`
- `src/Neo.Plugins.L2Gateway/IRoundProver.cs`
- `src/Neo.Plugins.L2Gateway/MultisigRoundProver.cs`
- `src/Neo.Plugins.L2Gateway/MerklePathRoundProver.cs`
- `contracts/NeoHub.MessageRouter/MessageRouterContract.cs`
- `tests/Neo.Plugins.L2Gateway.UnitTests/`
- `tests/Neo.L2.IntegrationTests/UT_Mvp_Phase1_Cross_Component.cs`

Verdict: architecturally correct. The global root publication and off-chain
aggregation shape match the ZKsync Gateway model.

Remaining gap: no `L2Native.L2InteropVerifier` equivalent for dApps to verify
peer-chain messages locally using a published global root. Current verification
is L1-side.

### 7. Sequencing And Censorship Resistance

ZKsync: chain operators can choose centralized/decentralized sequencing and
priority queues remain an escape hatch.

Neo4 evidence:

- `contracts/NeoHub.SequencerRegistry/`
- `contracts/NeoHub.SequencerBond/`
- `contracts/NeoHub.ForcedInclusion/`
- `src/Neo.L2.Sequencer/`
- `src/Neo.L2.ForcedInclusion/`
- `src/Neo.L2.Censorship/`

Verdict: good framework parity. The major missing production step is wiring the
Neo dBFT validator-set selector to `SequencerRegistry` in a live node deployment.

### 8. Account Abstraction, Paymasters, And UX

ZKsync: native AA is protocol-level; all accounts and paymasters are first-class.

Neo4 evidence:

- `contracts/L2Native.L2PaymasterContract/`

Verdict: incomplete relative to ZKsync. Neo4 has a paymaster-like contract, but
not a full programmable account model with `validateTx`, `payForTx`, and
`executeTx` semantics adapted to Neo's witness model.

This is not fatal for L2 settlement correctness, but it is a major UX and
developer-experience gap compared with ZKsync.

### 9. Operator Tooling

ZKsync: `zkstack` creates an ecosystem, deploys the bridge/chain contracts,
initializes a chain, and starts the server in a guided flow.

Neo4 evidence:

- `tools/Neo.Stack.Cli/`
- `tools/Neo.Hub.Deploy/`
- `tools/Neo.L2.Devnet/`
- `tools/Neo.L2.Bridge.Cli/`
- `tools/Neo.External.Bridge.Cli/`
- `scripts/deployment/run-local-rehearsal.ps1`
- `docs/audit/deployment-rehearsal-2026-05-17/README.md`

Verdict: professional but less turnkey. The new local rehearsal script closes
the zero-credential local verification gap. Public deployment still requires a
real wallet/signing path and funded networks.

## Completeness Scorecard

| Category | Score | Rationale |
| --- | ---: | --- |
| Architecture | 9/10 | Correct L1/L2/Gateway split and Neo-native replacements for ZKsync primitives. |
| Core contracts | 8/10 | NeoHub suite is broad; DA validator and canonical bridged token remain missing. |
| Proof system | 8/10 | SP1 zkVM, RISC-V host binding, optimistic path, and verifier registry exist; recursive-ZK Gateway round proof is not turnkey. |
| Bridge/security | 8/10 | Shared bridge and external bridge are real; public-network route drills are still required. |
| Data availability | 6/10 | Modes and writers exist; L1 inclusion verification is the missing production gate. |
| Interop | 7/10 | Global root and message routing exist; L2-side verifier is missing. |
| Governance | 7/10 | Timelock, immutable flags, emergency manager exist; staged upgrade process is incomplete. |
| DevEx/tooling | 7/10 | Strong CLIs and rehearsal; not yet as turnkey as `zkstack`/ZKsync portal ecosystem. |
| Tests/CI | 9/10 | Broad unit/integration/cross-language/zkVM CI, plus local deployment rehearsal. |
| Production readiness | 7/10 | Framework/devnet is strong; public deployment, operators, signers, DA validators, and live drills remain. |

Overall: 7.8/10 for production readiness, 8.7/10 for architecture and
engineering professionalism.

## Professionalism Validation

Strengths:

- Clear module boundaries: contracts, runtime libraries, plugins, tools, SDKs,
  watchers, docs, tests.
- Neo-native design choices are documented instead of blindly copying EraVM.
- CI covers .NET, Rust, Foundry, Solana cargo tests, and real SP1 zkVM host.
- Deployment planner and local rehearsal script make operator paths repeatable.
- Security and production-readiness documents explicitly avoid pretending that
  public-network deployment has happened.

Weaknesses:

- Some older docs use "parity" too broadly. Prefer "functional parity" or
  "architectural parity" unless public deployment and user-facing flows have
  been proven.
- Operator-supplied seams are well designed, but the repo still needs worked
  examples for real NeoFS, L1 JSON-RPC signing, KMS/HSM, and dBFT integration.
- ZKsync has a much more mature app-developer ecosystem; neo4 samples and SDKs
  are still thin by comparison.

## Correctness Validation

Validated facts from this workspace:

- `scripts/deployment/run-local-rehearsal.ps1` passed locally on
  `D:\Git\neo-n4\artifacts\local-deployment-rehearsal\20260517-200257`.
- GitHub Actions run
  <https://github.com/r3e-network/neo-n4/actions/runs/25990418398> completed
  successfully for commit `8199856`.
- The repository has explicit files for the major positive mappings:
  `NeoHub.SharedBridge`, `NeoHub.ChainRegistry`, `NeoHub.MessageRouter`,
  `NeoHub.SettlementManager`, `Neo.Plugins.L2Gateway`, `Neo.L2.Proving`,
  `Neo.L2.Executor.RiscV`, `Neo.Stack.Cli`, and the external bridge watchers.
- The repository does not currently have the key gap contracts:
  `contracts/NeoHub.DAValidator`,
  `contracts/L2Native.BridgedNep17Contract`,
  `contracts/L2Native.L2AccountAbstraction`,
  `contracts/L2Native.L2InteropVerifier`.

Correct statement to use externally:

> Neo4 is an independent Neo-native implementation of the Elastic Chain pattern:
> shared L1 hub, shared bridge, chain registry, settlement, proof verification,
> data availability modes, and optional gateway aggregation. It is architecturally
> mature and devnet-validated, but not yet equivalent to ZKsync Elastic Chain as a
> production network until the remaining DA, bridged-token, AA, interop-verifier,
> staged-governance, and public deployment gates are closed.

Incorrect statement to avoid:

> Neo4 is a complete production clone of ZKsync Elastic Chain.

That is wrong because ZKsync has Ethereum/EraVM-specific system contracts,
production Gateway/network deployment, mature AA/paymaster support, and a larger
developer tooling surface that neo4 intentionally does not and should not copy
one-to-one.

## Recommended Next Work

Priority order:

1. Implement `NeoHub.DAValidator` and wire it into `SettlementManager` for
   `DAMode.DAC` / validium-style chains.
2. Add `L2Native.BridgedNep17Contract` and default deterministic asset
   deployment from `TokenRegistry` or the operator deploy planner.
3. Add staged governance windows to `GovernanceController`.
4. Add an optional `IL1TxFilter` hook to `MessageRouter` / chain config.
5. Add `L2Native.L2InteropVerifier` for Gateway-root-based L2-side message
   verification.
6. Add a minimal programmable AA contract and extend paymaster flows.
7. Add public devnet/testnet deployment artifacts once real RPC/wallet/funds are
   available.
8. Expand app samples and generate Go/Python SDKs from the canonical RPC model.
