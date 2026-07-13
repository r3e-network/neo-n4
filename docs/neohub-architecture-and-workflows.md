# NeoHub architecture and workflows

NeoHub is the L1 anchor surface for the Neo Elastic Network. It is the place
where L2 chains become canonical: chains are registered, assets are escrowed,
batches are finalized, withdrawals are proven, messages are routed, sequencers
are bonded, and emergency or governance actions are enforced.

This document explains how NeoHub works as a system, how data moves through it,
and how each NeoHub contract participates in the main workflows.

## 1. Production boundary

The repository contains `contracts/NeoHub.*` projects as the canonical
deployable L1 contract bundle. NeoHub is not an L1 native-contract set; the
production target is deployed contracts plus optional node plugins, with only
minimal L1 core hooks in the `r3e-network/neo` fork when a hook cannot be
implemented as a contract or plugin:

- `r3e/neo-n3-core`: L1 core branch, based on upstream `master-n3`. It should
  stay close to upstream and must not register NeoHub business contracts as
  `NativeContract` instances.
- `r3e/neo-n4-core`: L2 execution-kernel branch, based on upstream `master`.
  L2 native contracts and the NeoVM2/RISC-V execution profile live here.

Current status:

- 24 `contracts/NeoHub.*` projects exist in `neo-n4`.
- 23 are production NeoHub contracts; `ExternalBridgeStubVerifier` is test-only.
- 23 are deployed by the production NeoHub deploy plan.
- `ContractZkVerifier` is a deployable NeoHub contract. It validates the
  N4 proof envelope and dispatches proof-system math to a governance-registered
  deployable verifier contract, keeping L1 core changes optional rather than
  required.
- L1 integration is through deployed contracts, node plugins, SDKs, CLIs,
  watchers, relayers, and operator services before considering any L1 core hook.

## 2. System view

```mermaid
flowchart TB
    user["User, wallet, dApp"] --> sdk["SDK / CLI / relayer"]
    sdk --> l2["N4 L2 chain"]
    l2 --> batcher["Batcher / prover / DA writer"]
    l2 --> l2native["L2 native contracts"]

    subgraph l1["Neo L1 deployed contracts: NeoHub"]
        chain["ChainRegistry"]
        token["TokenRegistry"]
        bridge["SharedBridge"]
        settle["SettlementManager"]
        verifier["VerifierRegistry"]
        nativezk["ContractZkVerifier"]
        msg["MessageRouter"]
        da["DARegistry + DAValidator"]
        seq["SequencerRegistry + SequencerBond"]
        force["ForcedInclusion"]
        challenge["OptimisticChallenge + fraud verifiers"]
        gov["GovernanceController + EmergencyManager"]
        ext["External bridge + MPC contracts"]
    end

    batcher --> da
    batcher --> settle
    settle --> verifier
    verifier --> nativezk
    nativezk --> nativeacc["Deployable proof verifier contract"]
    settle --> da
    settle --> seq
    bridge --> token
    bridge --> chain
    bridge --> settle
    msg --> chain
    msg --> settle
    msg --> l2native
    force --> l2
    challenge --> settle
    challenge --> seq
    gov --> chain
    gov --> verifier
    gov --> bridge
    gov --> settle
    ext --> bridge

    foreign["Foreign chains: EVM family, Tron, Solana"] --> watcher["Watchers / committee proofs"]
    watcher --> ext
```

The important design rule is that NeoHub owns L1 truth, not L2 execution. L2s
execute transactions and produce roots. NeoHub checks the roots, proof mode,
chain registration, bridge state, and security policy before accepting those
roots as final.

## 3. Contract planes

| Plane | Contracts | What the plane owns |
| --- | --- | --- |
| Chain identity | `ChainRegistry` | L2 admission, chain config, active/paused status, gateway flag, DA/security labels. |
| Asset registry | `TokenRegistry` | Canonical L1 asset to L2 asset mappings and token metadata. |
| Bridge custody | `SharedBridge` | L1 escrow, deposit messages, withdrawal finalization, withdrawal proof checks. |
| Settlement | `SettlementManager`, `VerifierRegistry`, `ContractZkVerifier` | Batch commitment validation, proof dispatch, ZK verifier-router dispatch to a deployable verifier contract, root finalization, batch status. |
| Data availability | `DARegistry`, `DAValidator` | DA commitments, DA mode validation, committee/DAC attestations. |
| Messaging | `MessageRouter`, `L1TxFilter` | L1-to-L2 queues, L2-to-L1 consumption, global roots, optional enqueue filtering. |
| Sequencer security | `SequencerRegistry`, `SequencerBond` | Active sequencers, bond accounting, slashing, exit windows. |
| Censorship resistance | `ForcedInclusion` | L1-posted forced transactions and inclusion deadlines. |
| Challenge/fraud | `OptimisticChallenge`, `GovernanceFraudVerifier`, `RestrictedExecutionFraudVerifier` | Fraud acceptance, challenge windows, restricted re-execution proof validation. |
| Governance/safety | `GovernanceController`, `EmergencyManager` | Admission policy, upgrade controls, pause/resume, escape hatch. |
| External bridge | `MpcCommitteeVerifier`, `MpcCommitteeFraudVerifier`, `ExternalBridgeRegistry`, `ExternalBridgeEscrow`, `ExternalBridgeBond`, `ExternalBridgeStubVerifier` | Foreign-chain event verification, committee bonding, external escrow. Stub verifier is test-only. |

## 4. Core data objects

| Object | Producer | Consumer | Why it matters |
| --- | --- | --- | --- |
| `L2ChainConfig` | Operator / governance | `ChainRegistry`, SDKs, explorers | Defines chain id, operators, verifier, bridge/message adapters, security level, DA mode, gateway mode, exit model, active flag. |
| `BatchCommitment` | L2 batcher | `SettlementManager`, verifiers, auditors | Canonical summary of L2 state transition: pre/post roots, tx root, receipt root, withdrawal root, message roots, DA commitment, public input hash, proof. |
| `DA commitment` | DA writer / batcher | `DARegistry`, `DAValidator`, auditors | Lets L1 and users know where batch data is available and under which DA trust model. |
| `Proof payload` | Prover / committee / challenger | `VerifierRegistry`, fraud verifiers | Establishes whether the submitted batch should be accepted or rejected under the configured proof mode. |
| `Deposit payload` | `SharedBridge` | L2 bridge native contract | Carries L1 escrow event into the target L2 mint/credit path. |
| `Withdrawal record` | L2 bridge native contract | `SharedBridge` | Included in the batch withdrawal root; users prove inclusion to unlock L1 escrow. |
| `Cross-chain message` | L1, L2, or external watcher | `MessageRouter`, L2 message contract, external bridge | Replay-protected message envelope with source/target chain ids and nonce. |
| `Fraud proof payload` | Challenger | `OptimisticChallenge`, fraud verifier | Shows that a finalized or pending batch is invalid under the selected fraud verifier. |

## 5. Contract dependency graph

```mermaid
flowchart LR
    gov["GovernanceController"] --> chain["ChainRegistry"]
    gov --> verifiers["VerifierRegistry"]
    gov --> emergency["EmergencyManager"]
    emergency --> settle["SettlementManager"]
    emergency --> bridge["SharedBridge"]

    chain --> bridge
    chain --> msg["MessageRouter"]
    token["TokenRegistry"] --> bridge

    daReg["DARegistry"] --> daVal["DAValidator"]
    daVal --> settle
    verifiers --> settle
    verifiers --> nativeZk["ContractZkVerifier"]
    nativeZk --> nativeAcc["Deployable proof verifier contract"]

    seqReg["SequencerRegistry"] --> bond["SequencerBond"]
    bond --> challenge["OptimisticChallenge"]
    challenge --> settle
    challenge --> govFraud["GovernanceFraudVerifier"]
    challenge --> rexFraud["RestrictedExecutionFraudVerifier"]

    force["ForcedInclusion"] --> settle
    filter["L1TxFilter"] --> msg

    extReg["ExternalBridgeRegistry"] --> extEscrow["ExternalBridgeEscrow"]
    extBond["ExternalBridgeBond"] --> mpc["MpcCommitteeVerifier"]
    mpc --> extEscrow
    extFraud["MpcCommitteeFraudVerifier"] --> extBond
```

Read the graph as control/data dependency, not strict call order. The runtime
flow below shows the order in which contracts are normally touched.

## 6. L2 registration workflow

```mermaid
sequenceDiagram
    participant Operator
    participant Governance as GovernanceController
    participant Chain as ChainRegistry
    participant Token as TokenRegistry
    participant Verifier as VerifierRegistry
    participant DA as DARegistry

    Operator->>Governance: request/admit chain policy
    Governance->>Chain: registerChain(chainId, configBytes)
    Operator->>Verifier: register proof verifier for chain/proofType
    Operator->>Token: register L1/L2 asset mappings
    Operator->>DA: register DA mode or committee metadata
    Chain-->>Operator: chain active with security labels
```

Registration is the first load-bearing step. A chain cannot safely accept
deposits, finalize batches, or consume messages until:

1. `ChainRegistry` has a non-zero active config.
2. `VerifierRegistry` knows which verifier handles the chain's proof mode.
3. `TokenRegistry` maps the assets the bridge may move.
4. `DARegistry` and `DAValidator` can evaluate the chain's DA mode.
5. Governance/emergency policy has a known owner or council path.

## 7. Deposit data flow

```mermaid
sequenceDiagram
    participant User
    participant Bridge as SharedBridge
    participant Token as TokenRegistry
    participant Chain as ChainRegistry
    participant Router as MessageRouter
    participant L2Bridge as L2BridgeContract
    participant L2 as Target L2

    User->>Bridge: deposit(l1Asset, targetChainId, receiver, amount)
    Bridge->>Chain: read chain config and active flag
    Bridge->>Token: read canonical asset mapping
    Bridge->>Bridge: lock L1 asset / record nonce
    Bridge->>Router: enqueue L1-to-L2 deposit message
    Router-->>L2: message observed by relayer / L2 node
    L2->>L2Bridge: consume deposit payload
    L2Bridge-->>User: mint or credit bridged asset
```

Deposit invariants:

- Asset custody remains on L1 in `SharedBridge`.
- Chain id and nonce are part of the message hash, so deposits cannot replay on
  another L2.
- `TokenRegistry` controls whether the asset is canonical, bridged, active, and
  mapped to the target L2 asset representation, including exact L1/L2 decimal
  metadata. NEO is the special platform mapping: L1 NEO stays indivisible
  (`decimals = 0`), while each L2 receives built-in decimal NEO (`decimals = 8`).
- `L1TxFilter` can restrict which L1-to-L2 messages are accepted for a chain.

## 8. Batch settlement data flow

```mermaid
sequenceDiagram
    participant L2 as L2 chain
    participant Batcher
    participant DA as DARegistry
    participant DAVal as DAValidator
    participant Settle as SettlementManager
    participant Verifier as VerifierRegistry
    participant NativeZk as ContractZkVerifier
    participant Native as Deployable proof verifier
    participant Chain as ChainRegistry

    L2->>Batcher: ordered blocks, txs, receipts, state updates
    Batcher->>Batcher: compute roots and publicInputHash
    Batcher->>DA: publish daCommitment
    Batcher->>Settle: commitBatch(BatchCommitment)
    Settle->>Chain: validate chain active + security config
    Settle->>DAVal: validate DA mode and commitment
    Settle->>Verifier: verify proofType + proof payload
    Verifier->>NativeZk: ProofType.Zk commitment
    NativeZk->>Native: verifyZkProof(proofSystem, vkId, publicInputHash, proofBytes)
    Native-->>NativeZk: accepted or rejected
    Verifier-->>Settle: accepted or rejected
    Settle-->>L2: finalized / pending / rejected batch status
```

Settlement is the load-bearing boundary. `SettlementManager` does not execute
the L2 batch. It enforces that the batch was produced by an admitted chain,
uses the configured DA/proof mode, and has a proof path that NeoHub accepts.
Once accepted, the post-state root, withdrawal root, and message roots become
the L1 source of truth for bridge and messaging claims.

For `ProofType.Zk`, the proof path is deliberately split. `VerifierRegistry`
routes the commitment to `ContractZkVerifier`, which checks the N4 batch
commitment layout, the RISC-V proof payload envelope, the registered
verification-key id, and the public-input hash boundary. It then calls the L1
deployable verifier contract ABI `verifyZkProof(...)` for proof-system math. This keeps
NeoHub deployable and upgradeable, lets each proof system evolve independently,
and leaves native/precompile acceleration as an optional plugin implementation of
the same verifier ABI rather than a required L1 dependency.

## 9. Withdrawal data flow

```mermaid
sequenceDiagram
    participant User
    participant L2Bridge as L2BridgeContract
    participant L2
    participant Settle as SettlementManager
    participant Bridge as SharedBridge

    User->>L2Bridge: withdraw(l2Asset, amount, l1Recipient)
    L2Bridge->>L2: burn or lock L2 representation
    L2->>L2: include withdrawal record in withdrawalRoot
    L2->>Settle: finalize batch containing withdrawalRoot
    User->>Bridge: finalizeWithdrawalWithProof(record, Merkle proof)
    Bridge->>Settle: read finalized withdrawalRoot
    Bridge->>Bridge: verify proof and consume nonce
    Bridge-->>User: unlock L1 asset
```

Withdrawal invariants:

- Withdrawals are only valid against a finalized `withdrawalRoot`.
- The proof includes chain id, batch number, recipient, asset, amount, and nonce.
- `SharedBridge` must consume the withdrawal once and only once.
- If the batch is challenged and reverted, the withdrawal root must no longer be
  accepted for new claims.

## 10. Message routing data flow

```mermaid
flowchart LR
    l1sender["L1 sender"] --> router["MessageRouter"]
    router --> l2inbox["Target L2 inbox"]
    l2sender["Source L2 sender"] --> l2root["L2 message root"]
    l2root --> settle["SettlementManager"]
    settle --> router
    router --> l1consumer["L1 consumer"]
    router --> l2target["Target L2 message contract"]
    gateway["Optional Neo Gateway"] --> router
```

`MessageRouter` is the canonical message index. It handles:

- L1-to-L2 enqueue: L1 contracts or users enqueue messages for a target L2.
- L2-to-L1 consume: users prove a message was included in a finalized L2 root.
- L2-to-L2 route: source L2 message roots can be aggregated through Gateway or
  proven directly depending on the chain configuration.
- Replay protection: source chain, target chain, nonce, message type, sender,
  receiver, and payload are part of the canonical hash.

## 11. Forced inclusion and challenge workflow

```mermaid
flowchart TB
    user["User posts forced transaction on L1"] --> force["ForcedInclusion"]
    force --> queue["Per-chain forced inclusion queue"]
    queue --> sequencer["Sequencer must include before deadline"]
    sequencer --> included{"Included in L2 batch?"}
    included -->|"yes"| settle["SettlementManager finalizes normal batch"]
    included -->|"no"| challenge["OptimisticChallenge"]
    challenge --> bond["SequencerBond slashing path"]
    challenge --> fraud["Fraud verifier"]
    fraud --> accepted{"Fraud accepted?"}
    accepted -->|"yes"| revert["Batch reverted / sequencer slashed"]
    accepted -->|"no"| final["Challenge rejected / batch remains"]
```

The security stack is layered:

- `ForcedInclusion` gives users an L1 escape route when an L2 sequencer censors
  transactions.
- `SequencerRegistry` determines which sequencers are active for a chain.
- `SequencerBond` holds slashable value and controls exit windows.
- `OptimisticChallenge` coordinates challenges over submitted batches.
- `GovernanceFraudVerifier` verifies structural v1/v2 fraud payloads used by
  governance arbitration.
- `RestrictedExecutionFraudVerifier` keeps v3 storage-proof payloads governance-only;
  v4 reads the committed SettlementManager header and executes the declared
  single-transaction Counter semantic before permissionless slashing.

## 12. External bridge data flow

```mermaid
sequenceDiagram
    participant Foreign as Foreign chain router
    participant Watcher
    participant MPC as MpcCommitteeVerifier
    participant Registry as ExternalBridgeRegistry
    participant Escrow as ExternalBridgeEscrow
    participant Bond as ExternalBridgeBond
    participant Bridge as SharedBridge

    Foreign->>Watcher: lock / message event
    Watcher->>Watcher: wait min confirmations, journal event
    Watcher->>MPC: submit committee proof
    MPC->>Bond: check signer set / bonded committee
    MPC-->>Escrow: event accepted
    Escrow->>Registry: resolve external chain + asset route
    Escrow->>Bridge: mint/credit or enqueue target message
```

The external bridge plane is intentionally separate from normal L2 settlement:
foreign chains do not produce Neo L2 batches. Watchers observe foreign-chain
events, committee proofs authenticate them, and NeoHub routes the accepted event
into the same asset/message model used by Neo L2s.

## 13. Governance and emergency workflow

```mermaid
sequenceDiagram
    participant Council as Governance council
    participant Gov as GovernanceController
    participant Emergency as EmergencyManager
    participant Chain as ChainRegistry
    participant Verifier as VerifierRegistry
    participant Bridge as SharedBridge
    participant Settle as SettlementManager

    Council->>Gov: propose parameter/verifier/admission change
    Gov->>Gov: enforce multisig/timelock/policy
    Gov->>Chain: update admission or chain config
    Gov->>Verifier: update verifier route
    Council->>Emergency: pause on critical incident
    Emergency->>Bridge: block bridge-sensitive path
    Emergency->>Settle: block settlement-sensitive path
    Emergency-->>Council: enable escape hatch where configured
```

The emergency path is intentionally narrow. It should stop unsafe state
transitions or bridge operations, not silently rewrite history. Recovery should
be visible through events and operator runbooks.

## 14. Per-contract reference

| Contract | Primary job | Key inputs | Key outputs/events | Normal callers |
| --- | --- | --- | --- | --- |
| `ChainRegistry` | Store canonical L2 chain configuration and active state. | `chainId`, `configBytes`, governance owner. | Chain registered/paused/resumed; security labels become queryable. | Governance, operator tooling, settlement/bridge/message readers. |
| `TokenRegistry` | Store canonical asset mapping between L1 assets and L2 representations. | L1 asset, L2 chain id, L2 asset, asset type, mode, active flag. | Mapping registered/updated; bridge can resolve asset route. | Governance/operator, `SharedBridge`. |
| `DARegistry` | Record DA commitments by chain and batch. | `chainId`, `batchNumber`, `daCommitment`, DA mode. | Commitment recorded; commitment queryable during settlement/audit. | Batcher/DA writer, `SettlementManager`. |
| `DAValidator` | Validate DA mode-specific attestations and commitment shape. | DA committee metadata, commitment, batch context. | DA accepted/rejected. | `SettlementManager`, operator setup. |
| `L1TxFilter` | Optional per-chain policy hook for L1-to-L2 enqueues. | Sender, receiver, message type, payload, chain config. | Accepted/rejected enqueue decision. | `MessageRouter`. |
| `VerifierRegistry` | Map proof types to verifier contracts. | `proofType`, verifier hash, governance owner. | Verifier registered/updated; proof dispatch result. | `SettlementManager`, governance. |
| `ContractZkVerifier` | Validate `ProofType.Zk` commitment/proof envelopes and dispatch proof-system work to deployable verifier contracts. | Batch commitment bytes, proof-system tag, verification-key id, public-input hash, verifier contract hash. | ZK proof accepted/rejected; verification keys, verifier contracts, and envelope-only mode registered/removed. | `VerifierRegistry`, governance/operator. |
| `SettlementManager` | Validate and finalize L2 batch commitments. | `BatchCommitment`, DA commitment, proof payload, chain config. | Batch committed/finalized/reverted; roots stored for bridge/message proofs. | Batcher, gateway, challenge system. |
| `SharedBridge` | Custody L1 assets and finalize withdrawals. | Deposits, withdrawal records, Merkle proofs, asset mappings. | Deposit enqueued; withdrawal finalized; consumed proof marker. | Users, relayers, L2 bridge adapters. |
| `MessageRouter` | Route replay-protected L1/L2 messages. | Message envelope, source/target chain ids, nonce, roots/proofs. | L1-to-L2 enqueued; L2-to-L1 consumed; global root published. | Users, L2 nodes, relayers, settlement/gateway. |
| `EmergencyManager` | Pause/resume critical NeoHub operations and expose escape hatch controls. | Council/owner witness, pause scope, settlement/bridge references. | Paused/resumed/escape hatch events. | Security council, governance. |
| `GovernanceController` | Control admission, verifier upgrades, protocol parameters, and bridge/governance wiring. | Governance proposal, owner/council witness, target config. | Parameter changed, verifier route changed, chain admission changed. | Governance council, operator tooling. |
| `SequencerBond` | Hold slashable sequencer value and process slashing/withdrawal windows. | Chain id, sequencer, amount, slasher, exit request. | Bond deposited/slashed/withdrawn; active balance. | Sequencers, `OptimisticChallenge`, governance. |
| `SequencerRegistry` | Track active sequencers and committee membership per chain. | Chain id, sequencer account, metadata, activation/exit action. | Sequencer registered/removed/exit-started. | Governance/operator, settlement/challenge readers. |
| `ForcedInclusion` | Store user-posted forced transactions and inclusion deadlines. | Chain id, sender, transaction bytes, deadline policy. | Forced transaction queued/consumed/expired. | Users, L2 sequencer, challenge tooling. |
| `OptimisticChallenge` | Run optimistic fraud challenge flow over disputed batches. | Chain id, batch number, challenger, fraud payload, verifier. | Challenge opened/accepted/rejected; batch status updated. | Challengers, settlement, sequencer bond. |
| `GovernanceFraudVerifier` | Validate structural v1/v2 fraud payloads for governance-mediated challenges. | Versioned fraud payload, claimed/replayed roots, optional witness. | FraudProofAccepted or FraudProofRejected with reason code. | `OptimisticChallenge`, governance challenge path. |
| `RestrictedExecutionFraudVerifier` | Validate governance-only structural v3 or committed-root-bound executable restricted v4. | V3 evidence, or v4 chain/batch/root/transcript/claim/tx/storage witness. | False for honest/invalid/unsupported claims; true only for a wrong committed Counter transition. | `OptimisticChallenge`; v4 single-tx Counter profile, not general NeoVM. |
| `MpcCommitteeVerifier` | Verify external-chain committee signatures and signer threshold. | External event hash, committee signatures, signer metadata. | External event accepted/rejected. | Watchers, `ExternalBridgeEscrow`. |
| `MpcCommitteeFraudVerifier` | Challenge or slash incorrect external committee attestations. | Fraud proof over committee-signed external event. | Fraud accepted/rejected; slashing path enabled. | Challengers, `ExternalBridgeBond`. |
| `ExternalBridgeRegistry` | Register foreign chains, asset routes, and bridge adapters. | External chain id, foreign asset/router, Neo asset/chain route. | External route registered/updated. | Operator/governance, `ExternalBridgeEscrow`. |
| `ExternalBridgeEscrow` | Hold or release assets for foreign-chain bridge events. | Verified external event, route, recipient, amount. | External deposit/withdrawal/message consumed. | Watchers, external bridge relayers, `SharedBridge`. |
| `ExternalBridgeBond` | Bond and slash external bridge committee members. | Committee member, bond amount, fraud/slash request. | Bond deposited/slashed/withdrawn. | Committee members, fraud verifier, governance. |
| `ExternalBridgeStubVerifier` | Test-only verifier used for local scaffolding and negative tests. | Stub event/proof data. | Deterministic test result. | Unit/integration tests only. Not in production deploy bundle. |

## 15. How to read NeoHub during an audit

Use this order when auditing or changing NeoHub:

1. Start with `ChainRegistry`, because every flow is scoped by `chainId` and
   chain security labels.
2. Read `SettlementManager` and `VerifierRegistry`, because they define which
   roots are accepted as L1 truth.
3. Read `SharedBridge` and `MessageRouter`, because they consume finalized roots
   and expose the user-facing bridge/message surface.
4. Read `DARegistry` and `DAValidator`, because invalid DA assumptions weaken
   settlement even if proof dispatch is correct.
5. Read `SequencerRegistry`, `SequencerBond`, `ForcedInclusion`, and
   `OptimisticChallenge`, because they determine censorship and fraud recovery.
6. Read `GovernanceController` and `EmergencyManager`, because upgrade and pause
   authority can override normal liveness assumptions.
7. Read the external bridge contracts last, because they add a separate trust
   domain: foreign-chain finality plus committee attestation.

For byte-level layouts, cross-check
[`architecture-wire-formats.md`](./architecture-wire-formats.md). For trust
assumptions, cross-check
[`architecture-trust-boundaries.md`](./architecture-trust-boundaries.md). For
core fork and deployed-contract boundary rules, cross-check
[`core-fork-policy.md`](./core-fork-policy.md).
