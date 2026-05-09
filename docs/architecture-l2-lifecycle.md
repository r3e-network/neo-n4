# Architecture: L2 chain lifecycle

> Detailed walkthrough of how the Neo Elastic Network is structured,
> and how an L2 chain goes from "doesn't exist" to "registered,
> producing batches, connected to the shared bridge + cross-chain
> messaging".
>
> Companion to [`architecture-walkthrough.md`](./architecture-walkthrough.md)
> (which covers the *transaction* lifecycle within an L2) and
> [`launching-an-l2.md`](./launching-an-l2.md) (which is the
> operator-step-by-step guide). This document is the architecture
> view: what each step does, which components are touched, what
> wire data crosses which boundary.

## Table of contents

1. [System at a glance](#1-system-at-a-glance)
2. [The four tiers in detail](#2-the-four-tiers-in-detail)
3. [Anatomy of an L2 chain](#3-anatomy-of-an-l2-chain)
4. [Creation: from zero to registered](#4-creation-from-zero-to-registered)
5. [Deployment: contracts going on-chain](#5-deployment-contracts-going-on-chain)
6. [Runtime connection: how an L2 talks to L1](#6-runtime-connection-how-an-l2-talks-to-l1)
7. [Cross-L2 messaging](#7-cross-l2-messaging)
8. [External-chain bridge connection](#8-external-chain-bridge-connection)
9. [Component cross-reference](#9-component-cross-reference)

---

## 1. System at a glance

The Neo Elastic Network is **four tiers** of components plus the
off-chain infrastructure that connects them:

```text
┌──────────────────────────────────────────────────────────────────────────┐
│  TIER 1 · L1 anchor (Neo N3 / Neo 4)                                     │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  NeoHub — 20 contracts                                             │  │
│  │  settlement · bridge · messaging · governance · external-bridge    │  │
│  └────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
                              ▲
                              │  sealed batches · proofs · deposits ·
                              │  cross-L2 message routing · external bridge
                              │
┌──────────────────────────────────────────────────────────────────────────┐
│  TIER 2 · Neo Gateway (optional, Phase 5)                                │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  BinaryTreeAggregator   +   3 production IRoundProver impls        │  │
│  │  (log-N rounds)             (Multisig · MerklePath · PassThrough)  │  │
│  └────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────┘
                              ▲
                              │  optional aggregation across multiple L2s
                              │
┌──────────────────────────────────────────────────────────────────────────┐
│  TIER 3 · Elastic L2 chains (N of them)                                  │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────┐        │
│  │  L2 chain A      │  │  L2 chain B      │  │  L2 chain C      │        │
│  │  ──────────      │  │                  │  │                  │        │
│  │  Neo 4 core      │  │                  │  │                  │   ...  │
│  │  + 8 plugins     │  │                  │  │                  │        │
│  │  + 7 natives     │  │                  │  │                  │        │
│  └──────────────────┘  └──────────────────┘  └──────────────────┘        │
└──────────────────────────────────────────────────────────────────────────┘
                              ▲
                              │  runs the L2 plugins; produces batches
                              │
┌──────────────────────────────────────────────────────────────────────────┐
│  TIER 4 · Off-chain operators (per L2 + bridge daemons)                  │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌──────────┐ ┌──────────────────┐   │
│  │Sequencer│ │ Batcher │ │ Prover  │ │ DA writer│ │ External-chain   │   │
│  │         │ │         │ │ daemon  │ │          │ │ watcher daemons  │   │
│  └─────────┘ └─────────┘ └─────────┘ └──────────┘ └──────────────────┘   │
└──────────────────────────────────────────────────────────────────────────┘
```

**What flows where:**

| Flow                         | From → To                                         | Wire format                                    |
|------------------------------|---------------------------------------------------|------------------------------------------------|
| Sealed batch + proof         | Batcher → NeoHub.SettlementManager                | `BatchSerializer` (canonical 32-byte fields)   |
| DA payload                   | DA writer → NeoFS / L1 / committee                | `IDAWriter` impl-specific                      |
| Cross-L2 message             | L2 sender → NeoHub.MessageRouter → L2 receiver    | `MessageHasher` canonical bytes                |
| L1→L2 deposit                | User → NeoHub.SharedBridge → L2NativeBridge       | `DepositPayload`                               |
| L2→L1 withdrawal             | L2 user → SettlementManager Merkle proof          | `WithdrawalRecord` + Merkle path               |
| External chain → Neo         | EVM/Solana → Watcher → ExternalBridgeEscrow       | `ExternalCrossChainMessage` (102B + payload)   |
| Aggregated proof (Phase 5)   | Gateway → SettlementManager                       | `BinaryTreeAggregator` round proofs            |

---

## 2. The four tiers in detail

### Tier 1: NeoHub (L1)

The L1 anchor. **20 contracts** grouped by concern:

```text
    ┌──────────────────────┐  ┌──────────────────────┐  ┌─────────────────┐
    │  Settlement          │  │  Bridge              │  │  Messaging      │
    │  ──────────          │  │  ──────              │  │  ─────────      │
    │  SettlementManager   │  │  SharedBridge        │  │  MessageRouter  │
    │  VerifierRegistry    │  │  TokenRegistry       │  │  DARegistry     │
    │                      │  │  ChainRegistry       │  │                 │
    └──────────────────────┘  └──────────────────────┘  └─────────────────┘

    ┌──────────────────────┐  ┌──────────────────────┐
    │  Security            │  │  Governance          │
    │  ────────            │  │  ──────────          │
    │  SequencerRegistry   │  │  GovernanceController│
    │  SequencerBond       │  │  EmergencyManager    │
    │  ForcedInclusion     │  │                      │
    │  OptimisticChallenge │  │                      │
    └──────────────────────┘  └──────────────────────┘

    ┌────────────────────────────────────────────────────────────────────┐
    │  External Bridge (Phase B/C, doc.md §11.3)                         │
    │  ─────────────                                                     │
    │  MpcCommitteeVerifier   ExternalBridgeRegistry  ExternalBridgeBond │
    │  ExternalBridgeEscrow   ExternalBridgeStubVerifier                 │
    │  MpcCommitteeFraudVerifier                                         │
    └────────────────────────────────────────────────────────────────────┘
```

Lives at `contracts/NeoHub.*` — every contract type-checks via
`Neo.SmartContract.Framework`; CI compiles each with `nccs` and
verifies the `.nef` + `.manifest.json` artifacts.

**Key relationships:**
- `SettlementManager` consumes proofs validated by `VerifierRegistry`,
  triggers `SharedBridge.ApplyWithdrawals` on each accepted batch.
- `SharedBridge` looks up chain config via `ChainRegistry` + token
  metadata via `TokenRegistry`.
- `OptimisticChallenge` escalates to `GovernanceController` for
  fraud-verifier upgrades behind multisig + timelock.
- `ExternalBridgeEscrow` looks up the curve-tagged verifier via
  `ExternalBridgeRegistry`; `MpcCommitteeFraudVerifier` slashes
  bonds posted to `ExternalBridgeBond`.

### Tier 2: Neo Gateway (optional, Phase 5)

Aggregates many L2s' proofs into one settlement post on L1. Reduces
L1 gas cost when running >1 L2 chain.

- `BinaryTreeAggregator` — log-N round narrowing across N constituent batches.
- `IRoundProver` — 3 production impls + a recursive-ZK seam:
  - `MultisigRoundProver` — Secp256r1 threshold-attested rounds
  - `MerklePathRoundProver` — per-constituent inclusion proofs
  - `PassThroughRoundProver` — minimal-cost reference
  - SP1 Compress / Halo2 / Risc0 fold variants plug into the same trait

Optional: a single L2 doesn't need a Gateway. Multi-L2 deployments
that want lower per-batch L1 gas costs flip on `gatewayEnabled`
in the chain config.

### Tier 3: L2 chains

Each L2 = **Neo 4 core (the consensus + VM kernel) + 8 plugins +
7 native contracts**. Plugins live at `src/Neo.Plugins.L2*/`,
native contracts at `contracts/L2Native.*`. The Neo 4 core itself
is vendored as a git submodule at `external/neo`.

```text
    ┌─────────────────────────────────────────────────────────────┐
    │  Neo 4 core (external/neo, vendored submodule)              │
    │  ─────────                                                  │
    │  dBFT 2.0 consensus · NeoVM · Blockchain · Mempool          │
    └────────────────────────────┬────────────────────────────────┘
                                 │
                                 │  Block.Committed events,
                                 │  ApplicationEngine.Run hooks
                                 ▼
    ┌─────────────────────────────────────────────────────────────┐
    │  L2 plugins (src/Neo.Plugins.L2*)                           │
    │  ──────────                                                 │
    │  L2Batch  ─▶  L2Settlement  ─▶  L2Prover                    │
    │     │              │                                        │
    │     ▼              ▼                                        │
    │  L2Bridge      L2DA                                         │
    │     │              │                                        │
    │     ▼              ▼                                        │
    │  L2Rpc · L2Gateway · L2Metrics                              │
    └────────────────────────────┬────────────────────────────────┘
                                 │
                                 │  invokes native contracts
                                 ▼
    ┌─────────────────────────────────────────────────────────────┐
    │  L2 native contracts (contracts/L2Native.*)                 │
    │  ─────────────────                                          │
    │  L2BridgeContract       L2MessageContract                   │
    │  L2BatchInfoContract    L2BatchSeal                         │
    │  L2FeeContract          L2ExitWindow                        │
    │  L2NativeExternalBridgeContract                             │
    └─────────────────────────────────────────────────────────────┘
```

The 8 plugins + 7 native contracts implement the `doc.md` §5–§13
layered architecture (batch sealing / settlement / bridge / DA /
proving / RPC / gateway / metrics).

### Tier 4: Off-chain operators

Each L2 needs at least one of each:

| Operator        | What it does                                         | Source                                  |
|-----------------|------------------------------------------------------|-----------------------------------------|
| Sequencer       | dBFT 2.0 consensus member; produces L2 blocks        | `Neo.L2.Sequencer/`                     |
| Batcher         | Subscribes to `Blockchain.Committed`, seals batches, submits to L1 | `Neo.L2.Batch/` + `Neo.Plugins.L2Batch` |
| Prover daemon   | SP1 zkVM proves the batch (Phase 4)                  | `bridge/neo-zkvm-host/` (Rust binary)   |
| DA writer       | Publishes batch payload to NeoFS / L1 / committee    | `Neo.L2.DA*` + injected `IDAWriter`     |
| External-chain watcher | (External bridge only) relays events from EVM/Solana → Neo | `watchers/neo-bridge-watcher-*/` |

---

## 3. Anatomy of an L2 chain

Every L2 chain is fully described by **four artifacts**:

```text
    ┌─────────────────────────────────────────────────────────────────┐
    │   What defines an L2 chain                                      │
    └─────────────────────────────────────────────────────────────────┘

    ┌──────────────────────────────────────┐
    │  1. chain.config.json                │  drives the on-chain config
    │     (91-byte canonical config        │  + the executor's behavior
    │      + JSON metadata)                │
    └────────────────┬─────────────────────┘
                     │
                     ▼
    ┌──────────────────────────────────────┐
    │  2. ITransactionExecutor impl        │  ApplicationEngine-based,
    │     (compiled into the L2 plugin     │  OR custom for app-specific
    │      set on each sequencer)          │  chains
    └────────────────┬─────────────────────┘
                     │
                     ▼
    ┌──────────────────────────────────────┐
    │  3. ChainRegistry entry on L1        │  operatorManager · verifier
    │     (24-byte UInt160 references      │  · bridgeAdapter · messageAdapter
    │      + 91-byte configBytes)          │  · securityLevel · daMode · ...
    └────────────────┬─────────────────────┘
                     │
                     ▼
    ┌──────────────────────────────────────┐
    │  4. Off-chain operators              │  sequencer · batcher
    │     (running the L2 + reaching L1)   │  · prover · DA writer
    └──────────────────────────────────────┘
```

### The §16.2 chain config dimensions

The chain config carries 5 dimensions. Operators pick values per
chain; the same NeoHub L1 supports any combination:

| Dimension       | Values (range)                | Meaning                                                |
|-----------------|-------------------------------|--------------------------------------------------------|
| `securityLevel` | 0 · 1 · 2 · 3                 | 0 = sidechain (lowest); 3 = full ZK rollup (highest)   |
| `daMode`        | InMemory · External · L1 · DAC| where the batch payload goes                           |
| `sequencerModel`| Solo · Committee · Permissionless | how blocks are produced                            |
| `exitModel`     | Optimistic · Permissionless · ZkValidity | how withdrawals settle                       |
| `gatewayEnabled`| bool                          | whether this L2 batches into the shared Gateway        |

Encoded as a 91-byte canonical wire format via `L2ChainConfigSerializer`
(see `Neo.L2.Abstractions/L2ChainConfigSerializer.cs`).

### Templates

`neo-stack list-templates` ships 4 starting points:

| Template      | securityLevel | daMode    | exitModel       | gatewayEnabled |
|---------------|---------------|-----------|-----------------|----------------|
| `rollup`      | 2             | L1        | Optimistic      | true           |
| `zk-rollup`   | 3             | L1        | ZkValidity      | true           |
| `validium`    | 2             | DAC       | Optimistic      | true           |
| `sidechain`   | 1             | InMemory  | Permissionless  | false          |

---

## 4. Creation: from zero to registered

The full lifecycle of a chain from `git clone` to its first sealed
batch landing on L1, expressed as a numbered sequence between
actors:

```text
          Operator        neo-stack     filesystem      L1 wallet      NeoHub.ChainRegistry      L2 sequencer
              │              │              │              │                  │                        │
              │              │              │              │                  │                        │
   1.  ──── new-l2 ────▶     │              │              │                  │                        │
   2.        │   ──── scaffold ────────▶    │              │                  │                        │
              │              │              │              │                  │                        │
            ─── Phase 1: scaffolding only — chain has identity but no on-chain presence ───
              │              │              │              │                  │                        │
   3.  ──── validate ────▶   │              │              │                  │                        │
   4.        │   ──── JSON sanity-check ──▶ │              │                  │                        │
   5.        │  ◀──── OK / errors ─────     │              │                  │                        │
              │              │              │              │                  │                        │
   6.  ──── register-chain ─▶│              │              │                  │                        │
   7.        │  ◀── 91-byte configBytes hex + ChainRegistry.RegisterChain plan │                       │
              │              │  (plan-printer; never holds keys)               │                        │
   8.  ─── paste args ─────────────────────▶│                                  │                        │
   9.        │              │              │  ─── RegisterChain(chainId, configBytes, ...) ─▶          │
  10.        │              │              │  ◀───────── tx accepted ─────────                          │
              │              │              │              │                  │                        │
  11.  ─── deploy-bridge-adapter ▶         │              │                  │                        │
  12.        │  ◀── L2NativeBridge deploy plan            │                  │                        │
              │              │              │  ─── deploy ─▶ (operator wallet)                          │
              │              │              │              │                  │                        │
            ─── Phase 2: NeoHub knows the chain; bridges + messaging unlocked ───
              │              │              │              │                  │                        │
  13.  ─── start-sequencer ▶│              │              │                  │                        │
  14.        │  ◀── preflight + "compose with neo-cli" instructions          │                        │
  15.  ─── launch neo-cli + L2 plugins ─────────────────────────────────────────────▶                  │
  16.        │              │              │              │                  │  ─── dBFT 2.0 starts ─▶│
              │              │              │              │                  │                        │
  17.  ─── start-batcher ──▶│              │              │                  │                        │
  18.        │              │              │              │                  │  ◀── BatchInfoContract ┘
  19.        │              │              │              │  ◀──── SettlementManager.SubmitBatch ───── │
  20.        │              │              │              │                  │ ◀── settlement accepted │
              │              │              │              │                  │                        │
            ─── Phase 3: L2 batches land on L1; bridge + messaging work end-to-end ───
```

Three phases:

| Phase | What's true after this phase                                      |
|-------|-------------------------------------------------------------------|
| 1     | Local files exist; chain has an identity but no on-chain presence |
| 2     | NeoHub knows the chain; sequencer is producing L2 blocks          |
| 3     | L2 batches land on L1; bridge + messaging work end-to-end         |

### The `new-l2` composite

The `neo-stack new-l2 --name X --chain-id Y --template Z` command
strings together three lower-level operations. What gets generated:

```text
    ./MyChain/
    ├── chain.config.json              ← 91-byte-encodable canonical config
    ├── MyChainExecutor/
    │   ├── MyChainExecutor.csproj
    │   ├── src/
    │   │   ├── MyChainExecutor.cs     ← ITransactionExecutor stub
    │   │   ├── MyChainStateSeam.cs    ← state-store binding
    │   │   └── MyChainTxBuilder.cs    ← deposit/message tx helpers
    │   └── README.md
    ├── MyChainExecutor.UnitTests/     ← --with-tests
    │   ├── MyChainExecutor.UnitTests.csproj
    │   └── *.Tests.cs                 ← 3 starter tests
    └── data/  logs/  Plugins/         ← node working dirs
```

The `MyChainExecutor` scaffold is a starting point for chains that
need custom transaction semantics (e.g., RWA chain with KYC checks,
DEX chain with built-in matching). Chains that just need standard
NeoVM + NEP-17 don't need to customize — they use the
`ApplicationEngineTransactionExecutor` shipped in `src/Neo.L2.Executor/`.

### The 3-phase admission policy

Permissionless chain registration is gated through `[plan: §16.1-admission]`
— the L2 chain registry has 3 tiers:

```text
    ┌──────────┐         ┌──────────┐         ┌──────────┐
    │ Approved │ ──────▶ │ Stamped  │ ──────▶ │  Active  │
    └────┬─────┘         └────┬─────┘         └────┬─────┘
         │                    │                    │
         │ Phase 1            │ Phase 2            │ Phase 3
         │ ───────            │ ───────            │ ───────
         │ Operator-supplied  │ NeoHub stamps the  │ Sequencer bond
         │ config + signature │ config with a      │ posted; batch
         │ accepted by        │ deployment ID;     │ submission
         │ ChainRegistry.     │ bridges + messaging│ accepted by
         │                    │ unlocked.          │ SettlementManager.
```

---

## 5. Deployment: contracts going on-chain

Which contracts go where, in what order:

```text
    ┌─────────────────────────────────────────────────────────────────┐
    │  Step 1 — Deploy NeoHub (one-time, per network)                 │
    │  ──────                                                         │
    │  neo-hub-deploy plan                                            │
    │      → 20-step ordered deploy bundle                            │
    │  Operator wallet submits each step                              │
    │      → NeoHub fully deployed:                                   │
    │        SharedBridge · SettlementManager · ChainRegistry ·       │
    │        MessageRouter · ... (20 contracts total)                 │
    └────────────────────────────┬────────────────────────────────────┘
                                 │
                                 ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │  Step 2 — Register one L2 chain (per chain)                     │
    │  ──────                                                         │
    │  neo-stack register-chain --config chain.config.json            │
    │      → emits ChainRegistry.RegisterChain plan                   │
    │  Operator wallet submits plan                                   │
    │      → L2 has chainId, configBytes recorded                     │
    └────────────────────────────┬────────────────────────────────────┘
                                 │
                                 ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │  Step 3 — Deploy L2 bridge adapter (per chain)                  │
    │  ──────                                                         │
    │  neo-stack deploy-bridge-adapter --chain-id 1099                │
    │      → emits L2NativeBridgeContract deploy plan                 │
    │  Operator wallet submits                                        │
    │      → L2NativeBridge live; cross-tier transfers enabled        │
    └────────────────────────────┬────────────────────────────────────┘
                                 │
                                 ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │  Step 4 — Wire messaging adapter (optional, per chain)          │
    │  ──────                                                         │
    │  Operator deploys L2MessageContract                             │
    │  MessageRouter.RegisterAdapter(chainId, l2MessageHash)          │
    │      → cross-L2 messaging enabled                               │
    └─────────────────────────────────────────────────────────────────┘
```

Every command emits a structured plan rather than submitting directly
— the framework never holds private keys. Operators paste the
generated hex/UInt160 args into their wallet of choice (NeoLine,
Neon, NEP-6, Ledger, KMS-driven custom signer). See
[`docs/wallet-integration.md`](./wallet-integration.md) for the
patterns.

### Contract addresses an L2 needs to know

After deployment, an L2's config carries 4 NeoHub UInt160 references:

```toml
# In the L2's runtime config (separate from chain.config.json):
neo_hub_chain_registry      = 0x...  # ChainRegistry
neo_hub_settlement_manager  = 0x...  # SettlementManager
neo_hub_shared_bridge       = 0x...  # SharedBridge
neo_hub_message_router      = 0x...  # MessageRouter (if cross-L2 enabled)
```

Plus its own L2-side contracts:
```toml
l2_native_bridge_hash       = 0x...  # this L2's L2NativeBridgeContract
l2_native_message_hash      = 0x...  # this L2's L2MessageContract
l2_batch_info_hash          = 0x...  # this L2's L2BatchInfoContract
```

---

## 6. Runtime connection: how an L2 talks to L1

Once deployed, an L2 chain is "connected" via three independent
channels — each runs on its own cadence:

```text
    ┌──────────────────────────────┐                 ┌──────────────────────────────┐
    │  L2 chain (running)          │                 │  L1 NeoHub                   │
    │                              │                 │                              │
    │   Blockchain                 │                 │                              │
    │       │                      │                 │                              │
    │       │ Block.Committed       │                 │                              │
    │       ▼                      │                 │                              │
    │   L2BatchPlugin ──seal──▶ ──┼──▶ off-chain     │                              │
    │       │              │     │   prover daemon  │                              │
    │       │              │     │   (SP1 zkVM)     │                              │
    │       │              ▼     │                  │                              │
    │       │           DA writer ──▶ NeoFS / L1 ──▶│  DARegistry                  │
    │       │                    │                  │                              │
    │       └─── SubmitBatch ───────────────────────▶│  SettlementManager           │
    │                            │                  │  (verifies via               │
    │                            │                  │   VerifierRegistry)          │
    │                            │                  │                              │
    │   L2BridgeContract ◀── DepositReady ─────────  │  SharedBridge                │
    │                ─── WithdrawalReady ──────────▶ │                              │
    │                            │                  │                              │
    │   L2MessageContract ◀── InboundMessage ─────── │  MessageRouter               │
    │                ─── OutboundMessage ──────────▶ │                              │
    └──────────────────────────────┘                 └──────────────────────────────┘
```

### Channel 1 — Settlement (the hot path)

For every L2 block:

```text
    L2 Blockchain   L2BatchPlugin    BatchSealer    Prover daemon    SettlementManager
        │              │                  │              │                   │
        │ Committed ───▶                  │              │                   │
        │              │── tx batch + ───▶│              │                   │
        │              │   post-state-root │              │                   │
        │              │                  │              │                   │
        │              │                  │ build       │                   │
        │              │                  │ BatchCommitment                  │
        │              │                  │ (canonical                       │
        │              │                  │ 32-byte fields)                  │
        │              │                  │              │                   │
        │              │                  │── BatchPayload ─▶                │
        │              │                  │              │                   │
        │              │                  │              │ SP1 zkVM proves   │
        │              │                  │              │ execute_batch(...)│
        │              │                  │              │                   │
        │              │                  │ ◀── validity_proof + vk ──       │
        │              │                  │              │                   │
        │              │                  │── SubmitBatch(commitment, proof, vk) ──▶
        │              │                  │              │                   │
        │              │                  │              │ ◀── VerifierRegistry.Verify
        │              │                  │              │                   │
        │              │                  │ ◀── SettlementAccepted event ────│
```

Wire format: `BatchSerializer` (`Neo.L2.Batch/`) — 32-byte fields
in canonical order. See `architecture-walkthrough.md` § "transaction
lifecycle" for the per-tx zoom-in.

### Channel 2 — Bridge (asset transfers)

**L1 → L2 deposit:**

```text
    L1 user         NeoHub.SharedBridge      L2BridgeContract        L2 user
       │                    │                       │                   │
       │── Deposit(chainId, asset, amount, recipient) ─▶                │
       │                    │                       │                   │
       │                    │ Lock asset, emit                          │
       │                    │ DepositReady event                        │
       │                    │                       │                   │
       │            (L2 batcher polls events)       │                   │
       │                    │── DepositReady ──────▶│                   │
       │                    │                       │                   │
       │                    │                       │ Mint wrapped      │
       │                    │                       │ asset, credit     │
       │                    │                       │ recipient         │
       │                    │                       │                   │
       │                    │                       │── balance bump ──▶│
```

**L2 → L1 withdrawal:**

```text
    L2 user         L2BridgeContract        SharedBridge          L1 user
       │                    │                       │                   │
       │── Withdraw(asset, amount, recipient) ─────▶│                   │
       │                    │                       │                   │
       │                    │ Burn wrapped asset,                       │
       │                    │ emit WithdrawalReady                      │
       │                    │                       │                   │
       │  (Withdrawal record sealed in next batch;                      │
       │   after batch finalizes on L1, recipient claims)               │
       │                    │                       │                   │
       │                    │                       │◀─── ClaimWithdrawal(batchId, leafIdx, merkleProof)
       │                    │                       │                   │
       │                    │                       │ VerifyWithdrawal- │
       │                    │                       │ LeafWithProof     │
       │                    │                       │                   │
       │                    │                       │── release asset ─▶│
```

Wire format: `DepositPayload` for L1→L2, `WithdrawalRecord` +
Merkle path for L2→L1. Both encoders live in `Neo.L2.Bridge/`.

### Channel 3 — Cross-L2 messaging (optional)

See [§7](#7-cross-l2-messaging) below.

---

## 7. Cross-L2 messaging

When `gatewayEnabled = true` and `messageAdapter` is configured,
L2-A can send a message to L2-B without touching L1 manually:

```text
    User on L2-A    L2-A.L2MessageContract     NeoHub.MessageRouter     L2-B.L2MessageContract     Recipient on L2-B
        │                       │                         │                         │                       │
        │── SendMessage(targetChainId, payload) ─▶        │                         │                       │
        │                       │                         │                         │                       │
        │                       │ emit                                              │                       │
        │                       │ OutboundMessage event                             │                       │
        │                       │                         │                         │                       │
        │     (L2-A batcher includes msg in next sealed batch)                      │                       │
        │                       │                         │                         │                       │
        │                       │── (via batch settlement) RouteMessage             │                       │
        │                       │   (srcChainId, dstChainId, payload, hash) ──▶     │                       │
        │                       │                         │                         │                       │
        │                       │     (L2-B batcher polls MessageRouter for inbounds)                       │
        │                       │                         │── InboundMessage ──────▶│                       │
        │                       │                         │                         │                       │
        │                       │                         │                         │ VerifyMessageHash     │
        │                       │                         │                         │ (against canonical    │
        │                       │                         │                         │  encoder)             │
        │                       │                         │                         │                       │
        │                       │                         │                         │── deliver payload ───▶│
```

`MessageHasher` (`Neo.L2.Messaging/`) is the canonical encoder —
both endpoints recompute the hash from the wire bytes; the
contract never trusts an off-wire hash. End-to-end the message
crosses 3 trust boundaries (L2-A consensus → L1 settlement →
L2-B consensus); each boundary independently verifies the hash.

---

## 8. External-chain bridge connection

Cross-foreign-chain bridge (Phase B/C, `doc.md` §11.3) lets an
external chain (Eth/EVM family / Solana / Tron) deposit + withdraw
through the same SharedBridge surface. Architecturally:

```text
    ┌─────────────────────────┐          ┌─────────────────────────────┐
    │  External chain         │          │  Off-chain                  │
    │  (e.g. BSC mainnet)     │          │                             │
    │                         │          │  neo-bridge-watcher-eth     │
    │  NeoExternalBridge-     │   Locked │  daemon                     │
    │  Router.sol ────────────┼──event──▶│  · secp256k1/ed25519 sign  │
    │  (deployed unchanged    │          │  · /healthz, /metrics       │
    │   on any of 14 EVM-     │          │  · flock journal            │
    │   family chains)        │          │  · min_confirmations buffer │
    │                         │          │                             │
    └─────────────────────────┘          └─────────────────────────────┘
                ▲                                       │
                │                                       │ ExternalCrossChainMessage
                │                                       │ (102B prefix + payload)
                │                                       │
                │ withdrawal seal                       ▼
                │                            ┌─────────────────────────┐
                │                            │  NeoHub L1              │
                │                            │                         │
                │              ┌────────────│  ExternalBridgeEscrow   │
                │              │  burn/mint  │      │                  │
                │              │  trigger    │      │ verify via       │
                │              │             │      ▼                  │
                │              │             │  MpcCommitteeVerifier   │
                │              │             │      │                  │
                │              │             │      │ lookup verifier  │
                │              │             │      ▼                  │
                │              │             │  ExternalBridgeRegistry │
                │              │             │                         │
                │              │             │  (equivocation slash)   │
                │              │             │  MpcCommitteeFraud-     │
                │              │             │  Verifier ──slash──▶    │
                │              │             │     ExternalBridgeBond  │
                │              │             └─────────────────────────┘
                │              │                                       
                │              ▼
    ┌─────────────────────────────────────┐
    │  L2 chain                           │
    │                                     │
    │  L2NativeExternalBridgeContract     │
    │                                     │
    └─────────────────────────────────────┘
```

**One contract serves the entire EVM family.** The same
`NeoExternalBridgeRouter.sol` deploys unchanged on Ethereum / BSC /
Polygon / Arbitrum / Optimism / Base / Avalanche / Linea / zkSync /
Scroll / Mantle / Fantom / Celo / Tron — its constructor takes
`externalChainId` from the canonical 16-slot family-bank allocation
in `watchers/neo-bridge-watcher-eth/src/chains.rs`. See
[`external-bridge-evm-chains.md`](./external-bridge-evm-chains.md)
for the 5-step onboarding runbook.

The watcher daemon (production-ready: graceful SIGTERM, `/healthz`,
`/metrics`, flock-based concurrent-instance detection,
`min_confirmations` reorg buffer, `--preflight` validation) lives
at `watchers/neo-bridge-watcher-eth/`. K8s + systemd manifests in
[`deploy/`](../watchers/neo-bridge-watcher-eth/deploy/).

---

## 9. Component cross-reference

Which `neo-stack` subcommand touches which component:

| Subcommand               | Touches (L1)                      | Touches (filesystem)             | Touches (L2)              |
|--------------------------|-----------------------------------|----------------------------------|---------------------------|
| `create-chain`           | —                                 | `chain.config.json`              | —                         |
| `init-l2`                | —                                 | `data/`, `logs/`, `Plugins/`     | —                         |
| `register-chain`         | `ChainRegistry.RegisterChain`     | —                                | —                         |
| `deploy-bridge-adapter`  | `SharedBridge.RegisterAdapter`    | —                                | `L2NativeBridgeContract`  |
| `start-sequencer`        | (preflight only)                  | reads config                     | dBFT 2.0 starts           |
| `start-batcher`          | `SettlementManager.SubmitBatch`   | reads config                     | `L2BatchPlugin` runs      |
| `start-prover`           | (no L1 contact)                   | reads config                     | `L2ProverPlugin` runs     |
| `submit-batch`           | `SettlementManager.SubmitBatch`   | reads batch payload              | —                         |
| `validate`               | —                                 | `chain.config.json` JSON checks  | —                         |
| `scaffold-executor`      | —                                 | `<Name>Executor.csproj` + tests  | —                         |
| `new-l2`                 | composite of create + init + scaffold | composite                    | —                         |
| `list-templates`         | —                                 | prints to stdout                 | —                         |

### Operator deploy planner

For NeoHub itself (one-time, per-network):

```bash
# Generate the 20-step ordered bundle:
dotnet run --project tools/Neo.Hub.Deploy -- plan

# Verify the bundle's invariants:
dotnet run --project tools/Neo.Hub.Deploy -- verify

# Each step is a structured operator-plan: {contract, method, args}.
# The operator's wallet executes them in order.
```

For external-bridge committee setup (per-foreign-chain):

```bash
dotnet run --project tools/Neo.External.Bridge.Cli -- committee-blob \
    --pubs-file watchers.pubs    # one pub33 hex per line
# Outputs: Neo blob (hex) + matching Eth address list

dotnet run --project tools/Neo.External.Bridge.Cli -- deploy-bundle \
    --external-chain-id 0xE0000030 \
    --verifier <UInt160> --registry <UInt160> --escrow <UInt160> \
    --eth-router 0x... --threshold 4 \
    --committee-blob 0x... --eth-addresses 0x...,0x...,...
# Outputs: ordered checklist for both Neo + Eth wallets.
```

---

## See also

- [`ARCHITECTURE.md`](../ARCHITECTURE.md) — English summary of `doc.md` (§-by-§).
- [`WHITEPAPER.md`](../WHITEPAPER.md) — formal whitepaper.
- [`doc.md`](../doc.md) — master Chinese spec (authoritative).
- [`architecture-walkthrough.md`](./architecture-walkthrough.md) — narrative tour
  of the codebase, including the per-transaction lifecycle.
- [`launching-an-l2.md`](./launching-an-l2.md) — operator-step-by-step guide
  for running an L2 (this doc covers the architecture; that one covers commands).
- [`external-bridge-roadmap.md`](./external-bridge-roadmap.md) — Phase B/C
  cross-foreign-chain bridge.
- [`external-bridge-evm-chains.md`](./external-bridge-evm-chains.md) — onboarding
  a new EVM chain in 5 steps.
- [`security-model.md`](./security-model.md) — threat model + mitigations.
- [`tech-stack-coverage.md`](./tech-stack-coverage.md) — what's vendored vs reimplemented.
