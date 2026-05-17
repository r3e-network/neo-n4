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
framework/devnet stack. The latest implementation closes the major in-repo
ZKsync parity gaps: DA validation, canonical bridged token template, L2 AA,
staged governance, L1->L2 filtering, and L2-side interop verification.

It should still not be described as turnkey mainnet-equivalent to ZKsync Elastic
Chain yet. The remaining gaps are concrete and tracked:

1. Production examples for NeoFS, L1 signer, KMS/HSM, and dBFT validator-set
   wiring.
2. Public devnet/testnet deployment evidence with real RPC, funded accounts,
   governance accounts, and foreign testnet routes.
3. More application samples and Go/Python SDK coverage.

## System-Level Comparison

| Area | ZKsync Elastic Chain baseline | neo4 implementation | Verdict |
| --- | --- | --- | --- |
| Ecosystem model | Many ZKsync Chains form an Elastic Network, settling to Ethereum and sharing bridge/governance assumptions. | Many Neo L2s settle to NeoHub on Neo L1; security labels and chain config are explicit. | Functionally aligned. |
| L1 coordination hub | Bridgehub routes chain IDs, shared bridges, and chain contracts. | `NeoHub.ChainRegistry`, `MessageRouter`, `SharedBridge`, `SettlementManager`, `VerifierRegistry`, `TokenRegistry`, `DARegistry`. | Aligned, split by concern instead of one Bridgehub. |
| Chain type management | CTM deploys/initializes chain contracts and coordinates shared upgrades. | `VerifierRegistry` + `GovernanceController` + `neo-hub-deploy` planner. | Partial. Correct abstraction, but no on-chain per-chain factory or staged CTM-style upgrade workflow. |
| Shared bridge | Bridgehub/shared bridges lock assets once on L1 for all chains. | `NeoHub.SharedBridge` holds deposits and proof-based withdrawals; bridge CLI emits canonical invocation hex. | Strong functional parity. |
| Canonical assets | ZKsync shared bridges deploy/verify canonical L2 token contracts. | `TokenRegistry` maps assets; `L2BridgeContract` mints/burns; `L2Native.BridgedNep17Contract` is the canonical template. | Substantially aligned; deterministic factory deployment remains tooling work. |
| Batch settlement | ExecutorFacet commits/proves/executes batches and checks DA/proofs. | `SettlementManager`, proof payloads, verifier registry, SP1 prover host, optimistic challenge path. | Strong off-chain/devnet parity; public-network deployment still open. |
| Data availability | Rollup and validium modes; Stage-2 validium verifies DA inclusion on L1 via validators. | `DARegistry`, `DAValidator`, `L2DAPlugin`, `JsonRpcL1DAWriter`, `CommitteeAttestedDAWriter`, `NeoFsLikeDAWriter`. | Aligned for DAC attestation; richer external/NeoFS proof adapters remain operator-specific. |
| Gateway/proof aggregation | Gateway aggregates proofs from multiple chains; assets remain locked on Ethereum, not Gateway. | `Neo.Plugins.L2Gateway.BinaryTreeAggregator`, `IRoundProver` implementations, `MessageRouter.PublishGlobalRoot`; assets remain in NeoHub. | Architecturally aligned. Recursive-ZK round prover remains operator-supplied. |
| Interop | ZKsync Connect verifies messages via Gateway MessageRoot and L2 message verifier. | L1-side message router/global root plus `L2Native.L2InteropVerifier` for local proof verification. | Aligned at helper-contract level. |
| Sequencing/censorship | Configurable sequencing; priority queue escape hatch. | `SequencerRegistry`, `SequencerBond`, `ForcedInclusion`, `CensorshipDetector`, RPC source abstractions. | Strong framework parity; production dBFT selector wiring is still operator-specific. |
| Governance/security response | Shared governance, security council, freezing, permanent restrictions, ValidatorTimelock. | `GovernanceController`, `EmergencyManager`, immutable flags, timelock, challenge windows, staged proposal windows. | Strong; per-chain admin factory remains intentionally different. |
| Account abstraction/paymasters | Native AA at protocol level; all accounts and EOAs can use paymasters. | `L2AccountAbstraction` validator/execute/paymaster hooks plus `L2PaymasterContract` top-up/sponsor model. | Functional AA pattern exists; not protocol-native like EraVM. |
| Transaction filtering | Operators can filter L1->L2 and L2 transactions. | `MessageRouter.SetL1TxFilter` + `NeoHub.L1TxFilter` standardizes L1->L2 pre-enqueue filtering. | Aligned for L1->L2; L2 mempool filtering remains operator-specific. |
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

Remaining correctness issue: deterministic default deployment of the bridged
NEP-17 template is still a tooling/operator workflow, not an on-chain factory.

### 3. Chain Type / Upgrades / Governance

ZKsync: CTM and shared upgrades keep chain implementations and verifiers aligned
across the ecosystem. Normal/shadow/instant upgrades and frozen non-updated
chains are part of the model.

Neo4 evidence:

- `contracts/NeoHub.VerifierRegistry/`
- `contracts/NeoHub.GovernanceController/`
- `contracts/NeoHub.EmergencyManager/`
- `tools/Neo.Hub.Deploy/`

Verdict: substantially complete for shared governance. Governance, verifier
approval, immutable flags, timelock, and staged windows exist:
`propose -> notice -> execute -> cool-down`.

Professional recommendation: treat freeze/non-upgraded-chain orchestration as
operator policy unless neo4 later adds a per-chain factory/admin layer.

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
- `contracts/NeoHub.DAValidator/`
- `src/Neo.Plugins.L2DA/`
- `src/Neo.L2.Abstractions/IDAWriter.cs`
- `src/Neo.Plugins.L2DA/CommitteeAttestedDAWriter.cs`

Verdict: aligned for DAC-style validium. DA modes, off-chain writers, on-chain
DA recording, and DAC committee attestation validation are real. External/NeoFS
proof adapters can still be specialized per operator.

### 6. Gateway And Interop

ZKsync: Gateway aggregates proofs and supports Gateway-based interop through
MessageRoot and L2-side verification.

Neo4 evidence:

- `src/Neo.Plugins.L2Gateway/BinaryTreeAggregator.cs`
- `src/Neo.Plugins.L2Gateway/IRoundProver.cs`
- `src/Neo.Plugins.L2Gateway/MultisigRoundProver.cs`
- `src/Neo.Plugins.L2Gateway/MerklePathRoundProver.cs`
- `contracts/NeoHub.MessageRouter/MessageRouterContract.cs`
- `contracts/L2Native.L2InteropVerifier/`
- `tests/Neo.Plugins.L2Gateway.UnitTests/`
- `tests/Neo.L2.IntegrationTests/UT_Mvp_Phase1_Cross_Component.cs`

Verdict: architecturally correct. The global root publication, off-chain
aggregation, and L2-side inclusion verifier now match the ZKsync Gateway model
at helper-contract level.

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
- `contracts/L2Native.L2AccountAbstraction/`

Verdict: functionally covered at contract level. Neo4 now has programmable
validator and execute hooks, optional paymaster charging, and nonce consumption
adapted to Neo's witness model.

This is still not protocol-native like EraVM AA, so wallets and SDKs need more
integration work before the UX is comparable to ZKsync.

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
| Core contracts | 9/10 | NeoHub/L2Native suite now includes DA validator, L1 filter, canonical bridged token, AA, and interop verifier. |
| Proof system | 8/10 | SP1 zkVM, RISC-V host binding, optimistic path, and verifier registry exist; recursive-ZK Gateway round proof is not turnkey. |
| Bridge/security | 8/10 | Shared bridge and external bridge are real; public-network route drills are still required. |
| Data availability | 8/10 | Modes, writers, DARegistry, and DAC attestation validation exist; richer external DA proof adapters remain operator-specific. |
| Interop | 9/10 | Global root, message routing, and L2-side verifier exist; live multi-chain drills remain. |
| Governance | 8/10 | Timelock, immutable flags, emergency manager, and staged windows exist; no per-chain factory/admin freeze layer. |
| DevEx/tooling | 7/10 | Strong CLIs and rehearsal; not yet as turnkey as `zkstack`/ZKsync portal ecosystem. |
| Tests/CI | 9/10 | Broad unit/integration/cross-language/zkVM CI, plus local deployment rehearsal. |
| Production readiness | 7/10 | Framework/devnet is strong; public deployment, operators, signers, DA validators, and live drills remain. |

Overall: 8.3/10 for production readiness, 9.0/10 for architecture and
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
- The repository now has the previously missing key contracts:
  `contracts/NeoHub.DAValidator`,
  `contracts/NeoHub.L1TxFilter`,
  `contracts/L2Native.BridgedNep17Contract`,
  `contracts/L2Native.L2AccountAbstraction`,
  `contracts/L2Native.L2InteropVerifier`.

Correct statement to use externally:

> Neo4 is an independent Neo-native implementation of the Elastic Chain pattern:
> shared L1 hub, shared bridge, chain registry, settlement, proof verification,
> data availability modes, and optional gateway aggregation. It is architecturally
> mature and devnet-validated, with the major in-repo ZKsync parity contracts now
> implemented. It is still not turnkey mainnet-equivalent until public deployment,
> funded testnet drills, production signer/DA examples, and broader SDK/sample
> coverage are complete.

Incorrect statement to avoid:

> Neo4 is a complete production clone of ZKsync Elastic Chain.

That is wrong because ZKsync has Ethereum/EraVM-specific system contracts,
production Gateway/network deployment, mature AA/paymaster support, and a larger
developer tooling surface that neo4 intentionally does not and should not copy
one-to-one.

## Recommended Next Work

Priority order:

1. Add public devnet/testnet deployment artifacts once real RPC/wallet/funds are
   available.
2. Add worked production examples for NeoFS, JSON-RPC L1 DA, KMS/HSM signing,
   and dBFT validator-set wiring.
3. Add deterministic bridged-token deployment tooling around
   `L2Native.BridgedNep17Contract`.
4. Expand app samples and generate Go/Python SDKs from the canonical RPC model.
