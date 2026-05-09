# Architecture: Trust boundaries

> Architecture-from-trust-perspective view of the Neo Elastic
> Network. Maps every cross-tier flow to: who signs, who verifies,
> what's trusted, what's defended against.
>
> Companion to:
> - [`architecture-l2-lifecycle.md`](./architecture-l2-lifecycle.md) — *flow* (where things happen)
> - [`architecture-wire-formats.md`](./architecture-wire-formats.md) — *bytes* (what crosses the wire)
> - [`security-model.md`](./security-model.md) — *threats* (what can go wrong, mitigations)
>
> This doc bridges the three: at every boundary in the flow, what
> bytes carry the trust signal, and what the receiver verifies.

## Table of contents

1. [Trust boundary map](#1-trust-boundary-map)
2. [What's trusted at each boundary](#2-whats-trusted-at-each-boundary)
3. [Cross-tier verification chain](#3-cross-tier-verification-chain)
4. [Defense-in-depth per flow](#4-defense-in-depth-per-flow)
5. [Failure modes + their detection](#5-failure-modes--their-detection)
6. [The trust-minimization gradient](#6-the-trust-minimization-gradient)

---

## 1. Trust boundary map

A "trust boundary" is anywhere bytes cross from one trust domain to
another. Every boundary has a **producer** (signs / commits the
bytes) and a **consumer** (verifies them). Defense-in-depth means
multiple consumers re-verify independently — so a corrupt producer
can't get a free pass.

```text
                    ┌───────────────────────────────────┐
                    │  L1 user                          │
                    │  (Neo wallet / NeoLine / Neon)    │
                    └────────────────┬──────────────────┘
                                     │  (1) signed L1 tx
                                     │      [trust: user's wallet]
                                     ▼
   ────────────────  Boundary A: user → L1 NeoHub  ────────────────
                                     │
                                     ▼
                    ┌───────────────────────────────────┐
                    │  NeoHub L1 contracts              │
                    │  ───────                          │
                    │  · SharedBridge                   │
                    │  · MessageRouter                  │
                    │  · SettlementManager              │
                    │  · ChainRegistry · ...            │
                    └────────────────┬──────────────────┘
                                     │  (2) committed events
                                     │      [trust: dBFT 2.0 finality]
                                     ▼
   ────────────  Boundary B: NeoHub → L2 batcher (off-chain)  ─────
                                     │
                                     ▼
                    ┌───────────────────────────────────┐
                    │  L2 batcher (off-chain operator)  │
                    │  ───────                          │
                    │  · subscribes to Block.Committed  │
                    │  · seals BatchCommitment          │
                    │  · drives prover daemon           │
                    └────────────────┬──────────────────┘
                                     │  (3) BatchCommitment + proof
                                     │      [trust: validity proof OR
                                     │              optimistic challenge OR
                                     │              committee threshold]
                                     ▼
   ─────────  Boundary C: batcher → L1 SettlementManager  ────────
                                     │
                                     ▼
                    ┌───────────────────────────────────┐
                    │  SettlementManager + VerifierRegistry │
                    │  recomputes publicInputHash       │
                    │  dispatches to proofType-specific │
                    │  verifier                         │
                    └────────────────┬──────────────────┘
                                     │  (4) accepted batch =
                                     │      L1-final state root
                                     ▼
   ──────  Boundary D: settled batch → L2 user (withdrawal)  ─────
                                     │
                                     ▼
                    ┌───────────────────────────────────┐
                    │  L1 user claims withdrawal        │
                    │  via Merkle proof                 │
                    └───────────────────────────────────┘

   [Cross-foreign-chain bridge — separate trust path]

   ┌──────────────────────┐                ┌────────────────────────────┐
   │  Eth user            │                │  Watcher daemon (off-chain)│
   │  (locks ETH/ERC-20)  │                │  · M-of-N committee member │
   └──────────┬───────────┘                │  · signs canonical bytes   │
              │                            └────────────┬───────────────┘
              │  (E1) Locked event                      │
              │       [trust: Eth dBFT/PoW finality +   │
              │        confirmation buffer]             │
              ▼                                         │
        ────  Boundary E: external chain → watcher  ───┤
                                                       │
                                                       │  (E2) ExternalCrossChainMessage
                                                       │       + secp256k1 signatures
                                                       │       [trust: M-of-N committee]
                                                       ▼
                                          ┌────────────────────────────┐
                                          │  ExternalBridgeEscrow on   │
                                          │  Neo, verified by          │
                                          │  MpcCommitteeVerifier      │
                                          └────────────────────────────┘
```

**5 cross-tier boundaries** in the system. Each has a different
trust assumption (signed by whom, verified by what), and each is
independently re-checked downstream — defense in depth.

---

## 2. What's trusted at each boundary

For each boundary, three questions: WHAT is the trust assumption,
WHO bears the trust, and WHAT mechanisms enforce it.

### Boundary A: user → L1 NeoHub

| Question         | Answer                                                         |
|------------------|----------------------------------------------------------------|
| Trusts what?     | The user's signature is over the canonical L1 tx                |
| Trust held by?   | Neo's L1 consensus (dBFT 2.0)                                   |
| Enforced by      | dBFT 2.0 finality + Secp256r1 signature verification            |
| Failure mode     | User's key compromised → unauthorized tx                        |
| Mitigation       | Out of scope (wallet security is the user's responsibility)     |

### Boundary B: NeoHub → L2 batcher

| Question         | Answer                                                         |
|------------------|----------------------------------------------------------------|
| Trusts what?     | The batcher reads `Blockchain.Committed` events directly        |
| Trust held by?   | dBFT 2.0 finality (Neo L1 has agreed the events are committed)  |
| Enforced by      | dBFT's 2/3 honest-majority assumption                           |
| Failure mode     | dBFT halt or > 1/3 byzantine → finality stalls                  |
| Mitigation       | Operator runs their own RPC node; alerts on staleness           |

### Boundary C: batcher → L1 SettlementManager (the load-bearing boundary)

This is the boundary that makes Neo Elastic Network "trust-minimized"
— the L1 has no way to know what happened on L2 except through this
verification path. Every byte is checked.

| Question         | Answer                                                         |
|------------------|----------------------------------------------------------------|
| Trusts what?     | Whichever proof type the chain config picks (security level 0-3) |
| Trust held by?   | Cryptographic verifier (ZK / ECDSA threshold / optimistic dispute) |
| Enforced by      | `VerifierRegistry` → proofType-specific verify path             |
| Failure mode     | Proof invalid / wrong public-input / proof type mismatch        |
| Mitigation       | Hard reject; submitter pays the gas, batch doesn't land         |

| Proof type        | Trust assumption                                              |
|-------------------|---------------------------------------------------------------|
| `RiscVZk` (=1)    | SP1 zkVM proof — math (the proof IS the verification)         |
| `Multisig` (=0)   | M-of-N committee signed the BatchCommitment                   |
| `Optimistic` (=2) | Challenge window + bisection game — game-theoretic            |

### Boundary D: settled batch → L2 user (withdrawal)

| Question         | Answer                                                         |
|------------------|----------------------------------------------------------------|
| Trusts what?     | The Merkle proof for the user's withdrawal leaf                 |
| Trust held by?   | Hash collision-resistance + the batch's `withdrawalRoot`        |
| Enforced by      | `SharedBridge.VerifyWithdrawalLeafWithProof`                    |
| Failure mode     | User submits bad leafIdx + matching proof but wrong amount      |
| Mitigation       | Merkle proof MUST hash to the recorded `withdrawalRoot`         |

### Boundary E: external chain → watcher → Neo escrow

The cross-foreign-chain bridge (Eth/EVM family / Tron / Solana → Neo).
Different trust model from the other boundaries because there's no
cryptographic light client of the foreign chain on Neo — instead an
M-of-N committee attests to the foreign event.

| Question         | Answer                                                         |
|------------------|----------------------------------------------------------------|
| Trusts what?     | M-of-N committee signed the canonical `ExternalCrossChainMessage` |
| Trust held by?   | The committee (slashable bonds, equivocation-detectable)        |
| Enforced by      | `MpcCommitteeVerifier` checks M signatures over canonical bytes |
| Failure mode     | Committee equivocation OR > N-M+1 collusion                     |
| Mitigation       | Phase C: `MpcCommitteeFraudVerifier` proves equivocation +     |
|                  | slashes via `ExternalBridgeBond`. Phase D (future): ZK light   |
|                  | client replaces committee.                                      |

The committee model is explicitly **trust-minimized but not
trustless** — see [`security-model.md`](./security-model.md) for
the gradient.

---

## 3. Cross-tier verification chain

A single bridge transaction crosses 3-4 trust boundaries and
accumulates verifications at each. The same canonical bytes get
re-hashed + re-verified at every step:

```text
   L1 user signs tx                                      ┐
            │                                            │
   1. Secp256r1 sig over L1 tx                           │  [user owns key]
            │                                            ┘
            ▼
   ┌─────────────────────────────────────┐
   │  Neo L1: dBFT verifies + commits    │
   │  SharedBridge locks asset           │
   │  emits DepositReady event           │
   └────────────────┬────────────────────┘
                    │
            ┌───────┴────────┐
            │  CrossChain-   │   ← canonical bytes
            │  Message       │     (MessageHasher)
            │  (deposit)     │
            └───────┬────────┘
                    │  hash = sha256(canonical_bytes)
                    ▼                                    ┐
   2. L2 batcher reads + replays                         │  [reads
            │                                            │  Blockchain
   3. NeoVM applies the deposit on L2                    │  events
            │                                            │  directly]
   4. L2 receipt commits to it                           ┘
            │
            ▼
   ┌─────────────────────────────────────┐
   │  L2 batcher seals batch:            │
   │   · txRoot       (Merkle of txs)    │
   │   · receiptRoot  (Merkle of recs)   │
   │   · postStateRoot                   │
   │   · publicInputHash                 │
   └────────────────┬────────────────────┘
                    │
            ┌───────┴────────┐
            │  L2BatchComm-  │   ← canonical bytes
            │  itment        │     (BatchSerializer,
            │                │      321+N bytes)
            └───────┬────────┘
                    │
                    ▼                                    ┐
   5. SP1 zkVM proves execute_batch(payload)             │  [math:
            │                                            │   proof IS
   6. Proof's public-input == on-chain                   │   verification]
      publicInputHash (recomputed by                     ┘
      SettlementManager)
            │
            ▼
   ┌─────────────────────────────────────┐
   │  Neo L1: SettlementManager accepts  │
   │  batch as canonical                 │
   │  emits SettlementAccepted           │
   └────────────────┬────────────────────┘
                    │
                    │
   7. L2 user makes a withdrawal at some later batch B
            │
            ▼
   ┌─────────────────────────────────────┐
   │  Batch B's withdrawalRoot           │
   │  contains user's withdrawal leaf    │
   └────────────────┬────────────────────┘
                    │
                    ▼                                    ┐
   8. User submits Merkle proof on L1                    │  [hash
            │                                            │   collision-
   9. SharedBridge.VerifyWithdrawalLeafWithProof:        │   resistance]
            │   keccak/sha256 chain hashes to            ┘
            │   withdrawalRoot
            ▼
   ┌─────────────────────────────────────┐
   │  Asset released                     │
   └─────────────────────────────────────┘
```

**Key insight:** the same `canonical_bytes` get hashed multiple
times by different actors in different places. Whichever party
encodes the bytes, all subsequent parties who hold the bytes can
recompute the hash from scratch. This means you can't "unsign" or
"re-sign" — the bytes commit to themselves.

---

## 4. Defense-in-depth per flow

For each cross-tier flow, multiple checks run independently. A
single corrupt actor can't get past all of them:

| Flow                       | Check 1                                                  | Check 2                                                | Check 3                                                  |
|----------------------------|----------------------------------------------------------|--------------------------------------------------------|----------------------------------------------------------|
| L2 batch settlement        | `proofType` is in defined enum range                     | `proof.length` ≤ 1 MiB                                 | `publicInputHash` recomputed from on-chain commitment fields matches the proof's claim |
| L1→L2 deposit              | `SharedBridge.Deposit` requires asset locked + msg.value matches | L2 batcher recomputes canonical message hash; rejects mismatch | `L2NativeBridge` checks the L2 chainId in the message matches its own |
| L2→L1 withdrawal           | Withdrawal leaf in batch's `withdrawalRoot`              | Merkle proof hashes to `withdrawalRoot`                | `consumedWithdrawals[leafHash]` set; replay rejected      |
| Cross-L2 message           | Source L2 produces canonical bytes; `MessageRouter` recomputes hash | Destination L2 batcher recomputes hash from wire bytes | `consumedInboundMessages[srcChain][nonce]` set            |
| External-chain deposit     | Watcher signs canonical bytes (off-chain)                | M-of-N committee threshold checked on-chain            | `consumedInbound[chainId][nonce]` set; replay rejected    |
| External-chain withdrawal  | L2 batches the withdrawal request                        | Committee co-signs the canonical bytes                 | Foreign-chain router's `ecrecover` (Eth) / `verify_ed25519` (Solana) of M signatures over the bytes |

Each check is independent — a bug or compromise in one doesn't
bypass the others. (E.g., if the prover's `publicInputHash` claim
drifts from the on-chain commitment, the verifier rejects even if
the proof itself is valid for some OTHER public-input.)

---

## 5. Failure modes + their detection

### Component-level failures

| Component                  | Failure                                          | Detection                                                | Recovery                                          |
|----------------------------|--------------------------------------------------|----------------------------------------------------------|---------------------------------------------------|
| Sequencer (single)         | Crashes / unresponsive                           | Block-producing stalls; alerts on `forced inclusion` deadline | Operator restart; dBFT majority reaches finality without it |
| dBFT majority              | < 2/3 honest                                     | Finality stalls; no `Block.Committed` events             | Manual intervention; potentially governance vote  |
| Batcher                    | Crashes mid-tick                                 | `last_tick_success_unix` ages out                        | Watcher's journal flock unlocks on process exit; restart |
| Prover daemon              | OOM / crash                                      | `prove-batch` exits non-zero; batch never sealed         | Operator restart; idempotent against journal      |
| DA writer                  | Storage backend down                             | `da.publish_failures` counter spikes                     | Pluggable backend; fall through to L1 mode        |
| External-chain watcher     | RPC provider rate-limited                        | `watcher_last_error_unix` recent + retry backoff         | Daemon retries; operator can swap RPC URL         |
| External committee member  | Equivocates (signs two different messages for same nonce) | `MpcCommitteeFraudVerifier` accepts the equivocation proof | Bond slashed; reporter rewarded                  |

### Cryptographic / cross-tier failures

| Failure                                | Where detected                                | Symptom                                              |
|----------------------------------------|-----------------------------------------------|------------------------------------------------------|
| Proof tampered with                    | `VerifierRegistry.Verify`                     | `BatchRejected: invalid proof`                       |
| `publicInputHash` doesn't match        | `SettlementManager` (recomputes from commitment) | `BatchRejected: public-input mismatch`              |
| Withdrawal Merkle proof wrong          | `SharedBridge.VerifyWithdrawalLeafWithProof`  | `WithdrawalRejected: bad merkle path`                |
| External committee threshold not met   | `MpcCommitteeVerifier.VerifyInboundMessage`   | `Committee threshold not met (got M-1, need M)`      |
| External chain id outside namespace    | `ExternalBridgeEscrow.Receive`                | `Reject: externalChainId not in 0xE0_xx_xx_xx`       |
| Replay attempt (deposit, withdrawal, message) | Replay-protection map per (chainId, nonce) | `Reject: nonce already consumed`                  |

### What CAN'T be detected by the framework

These need operator-side observability + alerting:

- **The L1 user's wallet is compromised** — out of scope; user's responsibility.
- **An external chain reorgs deeper than `min_confirmations`** — operator's chain-id-specific config; the watcher's confirmation buffer is the defense, but a deep enough reorg defeats it.
- **An external chain is socially-engineered into accepting a fraudulent transaction at the application layer** — out of scope; foreign chain's responsibility.

See [`security-model.md`](./security-model.md) § "Threats and
mitigations" for the full table.

---

## 6. The trust-minimization gradient

Different parts of the system live at different points on the
trust-minimization spectrum. Operators can pick:

```text
   trust    ◀──────────────────────────────────────────▶  trustless
   maximizing                                              math-only

   ┌─────────┬────────────┬─────────────┬────────────┬────────────────┐
   │ Sequen- │ Optimistic │ Multisig    │ Permission-│ ZK validity    │
   │ cer-only│ challenge  │ threshold   │ less exit  │ proof          │
   │ (Solo)  │ (bisection)│ (M-of-N)    │ (escape    │ (SP1 zkVM)     │
   │         │            │             │  hatch)    │                │
   ├─────────┼────────────┼─────────────┼────────────┼────────────────┤
   │ trust   │ trust =    │ trust =     │ trust =    │ trust = math   │
   │ = solo  │ 1 honest   │ M-of-N      │ permission-│ (provable      │
   │ operator│ challenger │ committee   │ less       │  validity)     │
   │         │ exists     │             │ withdrawal │                │
   ├─────────┼────────────┼─────────────┼────────────┼────────────────┤
   │ Phase 0 │ Phase 1+   │ Phase 0+    │ Phase 1+   │ Phase 4+       │
   │ POC     │ default    │ external    │ optional   │ default for    │
   │         │ (rollups)  │ bridge      │            │ ZK rollups     │
   └─────────┴────────────┴─────────────┴────────────┴────────────────┘

   securityLevel:    0           1              0           1            2-3
   exitModel:        Optim.      Optim.         (varies)    Permis.      ZkValidity
```

**Operator's choice.** Each L2 chain picks its slot via the §16.2
config dimensions (`securityLevel`, `exitModel`, etc., encoded in
the 91-byte `L2ChainConfig`). The same NeoHub L1 supports all
combinations.

**External bridge specifically lives at Multisig today.** Phase D
(future) replaces the committee with a ZK light client of the
foreign chain — moving external bridge from Multisig to ZkValidity
on this gradient. The 6-contract external-bridge architecture
(MpcCommitteeVerifier / ExternalBridgeEscrow / ExternalBridgeBond /
MpcCommitteeFraudVerifier / ExternalBridgeStubVerifier /
ExternalBridgeRegistry) is designed to keep the surface stable
across that transition — only the verifier in the ChainRegistry
slot swaps.

---

## See also

- [`architecture-l2-lifecycle.md`](./architecture-l2-lifecycle.md) — the *flow* (creation/deployment/connection).
- [`architecture-wire-formats.md`](./architecture-wire-formats.md) — the *bytes* (canonical layouts).
- [`security-model.md`](./security-model.md) — the *threats* (what can go wrong, mitigations).
- [`external-bridge-roadmap.md`](./external-bridge-roadmap.md) — Phase B/C/D progression for the cross-foreign-chain bridge.
- [`doc.md`](../doc.md) — master spec (authoritative).
