# Architecture (English Summary)

> This is an English distillation of [`doc.md`](./doc.md). The Chinese original is authoritative; this exists for cross-reference. Section numbers below match `doc.md`.

## §0 Goal

Build the **Neo Elastic Network** — multiple Neo 4 L2 chains anchored to Neo N3 / Neo 4 L1, sharing a unified bridge, settlement contract suite, proof aggregation layer, and cross-chain message protocol. Borrows from ZKsync Elastic Chain (shared bridge, chain registry, proof aggregation, native interop) but rebuilt on Neo's tech stack: dBFT 2.0 finality, NEP-17, NeoVM2/RISC-V execution, NeoFS.

## §1 Layered architecture

```
Neo N3 / Neo 4 L1               settlement, asset escrow, governance
    │
    ▼
NeoHub (L1 contracts)           registry, bridge, settlement, messaging
    │
    ▼
Neo Gateway (optional)          proof aggregation, inter-L2 message root
    │
    ▼
Multiple Neo 4 L2 chains        Neo 4 core + L2 extensions
```

L2 chains exist for: RWA, Stablecoin, DEX, Game, Enterprise, Privacy.

## §3.2 NeoHub components

Core L1 contract suite:

- **ChainRegistry** — register L2 chains; each entry = `{chainId, operatorManager, verifier, bridgeAdapter, messageAdapter, securityLevel(0-3), daMode(0-3), gatewayEnabled, permissionlessExit, active}`
- **SharedBridge** — escrow canonical GAS / NEO / USDT / USDC / BTC / NEP-17; mint/burn rules; deposit + withdrawal finalization
- **SettlementManager** — accept `L2BatchCommitment` (chainId, batchNumber, pre/postStateRoot, txRoot, receiptRoot, withdrawalRoot, l2ToL1MessageRoot, l2ToL2MessageRoot, daCommitment, publicInputHash, proofType, proof)
- **VerifierRegistry** — pluggable verifiers dispatched by `ProofType` (Multisig, Optimistic, Zk via ContractZkVerifier). Gateway proof aggregation reuses these same proof types; there is no separate `Aggregated` proof type.
- **ContractZkVerifier** — deployable `ProofType.Zk` router; validates the commitment/proof envelope, checks registered verification keys, and dispatches to deployable proof-verifier contracts
- **MessageRouter** — L1↔L2 and L2↔L2 message queues with replay protection
- **TokenRegistry** — canonical L1↔L2 asset mapping
- **DARegistry** — record DA commitments per chain
- **GovernanceController** — admission policy, verifier upgrade, bridge emergency control
- **EmergencyManager** — pause, escape hatch

## §4 Neo Gateway

Optional layer. Mirrors ZKsync Gateway: collects proofs from multiple Neo L2s, aggregates them, maintains `globalMessageRoot` for L2-to-L2, and publishes the aggregated global root to NeoHub. In the current path that publish is settlement-manager-witness-authorized (the `PublishGlobalRoot` / `GlobalRootMirrored` flow gates on the system witness); the aggregated proof itself is **not** verified on-chain — NeoHub trusts the authorized publisher rather than re-checking the aggregation cryptographically. On-chain verification of the aggregated proof is planned/roadmap. **Doesn't custody assets** — assets stay locked in NeoHub/SharedBridge.

## §5–§7 L2 chain internals

Each L2 = `Neo 4 core` + L2 extensions:

- **Sequencer** — dBFT committee preferred over centralized sequencer (one-block finality is a Neo strength)
- **Batcher** — packs L2 blocks into `L2BatchCommitment`
- **StateRootGenerator** — produces `preStateRoot`, `postStateRoot`, `txRoot`, `receiptRoot`, `withdrawalRoot`, `l2ToL1MessageRoot`, `l2ToL2MessageRoot`
- **DAWriter** — writes batch data to NeoFS DA by default; L1, external DA, and DAC are explicit overrides
- **ProverAdapter** — Stage 0 (multisig attestation) → Stage 1 (optimistic) → Stage 2 (ZK validity)
- **SettlementSubmitter** — submits batch to NeoHub or Gateway
- **BridgeAdapter** — L2-side handler for deposits / withdrawals
- **MessageAdapter** — L2-side cross-chain messaging
- **ForcedInclusionHandler** — anti-censorship: user can post tx directly to L1 forced-inclusion queue; sequencer must include before deadline or get slashed
- **DurableStateBackend** — `IL2KeyValueStore` over RocksDB by default; survives restarts. Six components persist state: keyed state, RPC proofs, message-router proofs, forced-inclusion nonces, sequencer committee + exit windows, DA payloads. See [`docs/persistence.md`](docs/persistence.md).
- **ChainAuditor** — runs 6 invariant checks (continuity, proof validity, no-zero-proof, public-input-hash, batch range, DA availability) against produced commitments; emits `l2.audit.runs` + `l2.audit.failures` for ops dashboards.

ChainMode: `L1Mode` | `SidechainMode` | `L2RollupMode` | `L2ValidiumMode`.

## §8 Proof system

**Don't prove the whole C# node.** Only prove the deterministic state transition function:

```
ApplyBatch(preStateRoot, orderedTxs, l1Messages, blockContext)
  → (postStateRoot, receiptsRoot, withdrawalRoot, messageRoot)
```

Public inputs include all roots above + `chainId`, `batchNumber`, `daCommitment`, `blockContextHash`. Witness includes: ordered txs, contract bytecode, storage read/write witness, native contract state witness, L1 messages consumed, DA data, execution trace.

VM proving target: NeoVM2 / RISC-V (compatible with RISC-V instruction set per Neo 4 roadmap).

## §9 Token model

- **Canonical GAS** lives only on Neo N3 / Neo 4 L1.
- **L2 GAS** = SharedBridge-locked GAS, represented on L2 as bridged GAS. **L2 cannot issue independent canonical GAS.**
- L2 fee defaults to bridged GAS; paymasters allow stablecoin / sponsored fees.
- **NEO** can be bridged to L2 but governance power stays on L1.
- **NEP-17** mapped via `TokenRegistry`.

## §10 Neo Connect (cross-chain)

- L1→L2: `NeoHub.enqueueL1ToL2Message()` → L2 watches queue → L2 includes in next batch.
- L2→L1: L2 emits message → `messageRoot` in batch → finalized on NeoHub → user submits Merkle proof to consume.
- L2→L2: source L2 emits → batch finalized → `globalMessageRoot` updated → relayer submits proof to target L2.
- **Cross-chain bundle**: user-facing single-tx that internally spans multiple L2s.

## §11 Bridge

**One SharedBridge in NeoHub** for all L2s — no per-chain bridges. Asset mapping = `{l1Asset, l2ChainId, l2Asset, assetType, mintBurn|lockMint, active}`. Withdrawals only off finalized `withdrawalRoot`. All bridge messages have `chainId` + `nonce` for replay protection.

## §12 Data Availability tiers

- **NeoFS DA** — canonical default; Neo-ecosystem-native, content-addressed, and retrievable.
- **L1 DA** — explicit high-cost override for chains that need every byte on L1.
- **DAC** — lowest cost, highest risk; must be visibly labeled in `ChainRegistry`.

## §13 L2 native contracts

Ten native contracts on L2 (all registered by Neo core at genesis in
`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`):

- `L2BridgeContract` — mint/burn bridged assets
- `L2MessageContract` — emit/consume cross-chain messages (event now carries `payload`)
- `L2BatchInfoContract` — expose `chainId`, `batchNumber`, L1 finalized height
- `L2FeeContract` — sequencer/prover/DA fee management
- `L2PaymasterContract` — stablecoin / sponsored fees
- `L2SystemConfigContract` — config synced from NeoHub
- `L2NativeExternalBridgeContract` — L2-side burn/mint counterpart of `NeoHub.ExternalBridgeEscrow`
- `BridgedNep17Contract` — canonical bridge-controlled NEP-17 template (USDT/USDC/BTC/NEO mappings)
- `L2AccountAbstraction` — programmable AA entry point (validator binding, nonce, magic value)
- `L2InteropVerifier` — mirrored-global-roots check + Merkle proof verification + local replay protection

Adjusted contracts: `GAS` (bridge-controlled supply), `NEO` (bridged but governance L1), `Oracle` (local or via L1), `Policy` (local fee, bridge/security via NeoHub).

## §14 RPC / SDK / Tooling

L2 RPC additions: `getl2batch`, `getl2batchstatus`, `getl2stateroot`, `getl2withdrawalproof`, `getl2messageproof`, `getl1depositstatus`, `getcanonicalasset`, `getbridgedasset`, `getsecuritylevel`, `getsecuritylabel` (§16.2 label — five base dimensions: `securityLevel`, `daMode`, `sequencer`, `exit`, `gateway`. Proof-mode is collapsed into `securityLevel`; bridge-mode is operator-described in chain registry metadata rather than published on the label).

`neo-stack` CLI: `create-chain`, `init-l2`, `register-chain`, `deploy-bridge-adapter`, `start-{sequencer,batcher,prover}`, `submit-batch`.

## §16 Three-layer governance

- **L1**: NeoHub upgrade, verifier upgrade, bridge upgrade, emergency pause, L2 admission policy
- **L2 local**: sequencer committee, local fee policy, local app-chain params, local DA mode (within approved range)
- **App**: dApp rules, RWA issuer policy, stablecoin policy, enterprise permissioning

Every L2 must publish security labels: chain type, DA mode, proof mode, sequencer model, exit model, bridge model.

## §17 Threat model + mitigations

10 threats (sequencer censorship, invalid state root, bridge exploit, replay, DA unavailability, malicious validator committee, prover bug, verifier upgrade attack, message duplication, L2 contract bug). Each has named mitigations (forced inclusion, ZK validity proof, rate limits, nonce + chainId, DA security label, governance delay + security council veto, etc.).

## §18 Phased rollout

| Phase | Goal                                      | Security label             |
| ----- | ----------------------------------------- | -------------------------- |
| 0     | Neo 4 sidechain PoC                       | sidechain                  |
| 1     | NeoHub v0 + SharedBridge                  | connected sidechain        |
| 2     | Batch settlement                          | settled L2                 |
| 3     | Optimistic challenge window               | optimistic rollup          |
| 4     | NeoVM2 / RISC-V validity proof            | zk validity rollup         |
| 5     | Neo Gateway aggregation + L2-L2 messages  | Neo Elastic Network        |
| 6     | Neo Stack CLI + templates                 | (permissionless launch)    |

## §20 MVP

Smallest deliverable that proves the architecture works:

1. User can deposit GAS from Neo N3 to Neo 4 L2 devnet
2. User can deploy / call a Neo contract on the L2
3. L2 produces a `L2BatchCommitment`
4. Batch lands on NeoHub
5. User withdraws GAS back to N3 via `withdrawalRoot` proof

**Out of MVP scope:** full ZK proof, permissionless L2 launch, all-token bridge, L2-L2 contract calls, Gateway aggregation. These come in later phases.

## §22 Key design tradeoffs

| Question                | Choice                            | Reason                                |
| ----------------------- | --------------------------------- | ------------------------------------- |
| L2 execution kernel     | Neo 4 core                        | Run NeoVM2/RISC-V with native contracts and Neo tooling |
| Sequencer               | dBFT committee                    | Native one-block finality             |
| L1 settlement           | NeoHub                            | One state/asset/message/governance root |
| Bridge                  | SharedBridge                      | Avoid per-chain bridge fragmentation  |
| Proof phasing           | Attestation → Optimistic → ZK     | Lower early bar, retain trustless target |
| VM proving target       | NeoVM2 / RISC-V                   | Aligns with Neo 4 roadmap             |
| DA                      | L1 + NeoFS + DAC tiered           | Different cost/security per chain    |
| Cross-chain             | Neo Connect (native message+call) | Not just an asset bridge              |
| Multi-L2 scaling        | Neo Gateway + Neo Stack           | Network effect                        |
