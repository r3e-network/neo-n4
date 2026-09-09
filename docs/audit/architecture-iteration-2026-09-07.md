# Architecture Iteration — 2026-09-07

## Overview

Systemic architecture audit of `neo-n4` against `doc.md` and the lean NeoHub
4-pillar consolidation (`docs/audit/neohub-lean-consolidation.md`).

**Verdict:** On-chain lean pillars and the SP1 execution core (artifact-first +
CAS commit) are structurally sound. Production off-chain wiring and the new
transaction-witness layer still speak a broken or pre-lean dialect, creating
ship-blocking correctness gaps on the Validity path.

## Decisions (locked)

1. **Inventory / sign hash = Neo N3 single SHA-256.**
   `ParsedTransaction.hash` used for `GetSignData(network)` must be
   `SHA256(unsigned)` (`Helper.CalculateHash`), not `Hash256` (double SHA).
   Merkle leaves, commitments, and DA digests continue to use `Hash256`.
   Rust exposes both as `inventory_hash` and `hash256` to prevent reuse bugs.

2. **ZK and Multisig production settlement uses `submitAndFinalizeBatch`.**
   `submitBatch` alone leaves `StatusPending` on `RollupHub` and never reaches
   `Finalized` without a separate owner finalize. Validity must be atomic.

3. **Forced-inclusion consume is part of `SubmitBatchCore`.**
   `ConsumeForcedTransactionsInternal` advances the queue by a caller-supplied
   `forcedInclusionCount`. The count is always bound into the 352-byte
   public-input hash preimage (Wave 2).

4. **Gateway batch VK WORDS must track guest VK pins.**
   After any guest ELF/VK rotation, `neo-zkvm-gateway-guest/batch_vk_manifest.rs`
   WORDS are regenerated from the same verifying key via
   `neo-zkvm-gateway-host` build (requires SP1 toolchain).

5. **Wave 2/3 continued in** [`architecture-iteration-2026-09-07-wave2.md`](./architecture-iteration-2026-09-07-wave2.md).

## Findings matrix

| ID | Sev | Finding | Wave | Status |
| -- | --- | ------- | ---- | ------ |
| F1 | P0 | Production ZK hardcodes `submitBatch`; pipeline never calls `SubmitAndFinalizeBatchAsync` | 1B | Closed |
| F2 | P0 | Tx inventory hash uses `Hash256`; Neo wallets sign single-SHA256 id | 1A | Closed |
| F3 | P0 | `ConsumeForcedTransactionsInternal` is dead code | 1C | Closed (count param; hash bind → Wave 2) |
| F4 | P0 | Gateway `PINNED_BATCH_VK_WORDS` stale vs guest VK/ELF | 1A | Closed (re-derived from pinned ELF) |
| F5 | P1 | Multisig parser collects `m` keys instead of `n` | 1A | Closed |
| F6 | P1 | Witness SYSCALL opcode `0x68` (Neo2) vs Neo3 `0x41` | 1A | Closed |
| F7 | P0 | Gateway `PublishGatewayGlobalRoot` missing on lean RollupHub | 2 | Deferred |
| F8 | P1 | `LiveDeployCommand` still orchestrates deleted micro-contracts | 2 | Deferred |
| F9 | P1 | Optimistic off-chain paths remain first-class while RollupHub fail-closes | 2 | Deferred |
| F10 | P2 | God-modules on critical path (`wire.rs`, pipeline, ProofWitnessStore) | 3 | Deferred |

## Target architecture (Validity path)

```
Batcher → Sp1SettlementExecutionStack → durable proof artifact
       → CanonicalSettlementPipeline.BroadcastAndPersistAsync
       → ISettlementClient.SubmitAndFinalizeBatchAsync
       → RollupHub.submitAndFinalizeBatch(commitment, l1Hash, ctxHash, forcedCount)
       → SubmitBatchCore (verify proof + consume FI) → FinalizeBatchInternal
```

Witness path inside the proving core:

```
parse_transaction → inventory_hash (SHA256)
                 → verify_transaction_witnesses (Neo3 script + ECDSA over SHA256(network‖hash))
                 → execute
```

## API reference (Wave 1 surface)

### Rust (`neo-execution-core`)

- `inventory_hash(input) -> UInt256` — single SHA-256 (Neo inventory id)
- `hash256(input) -> UInt256` — double SHA-256 (commitment / Merkle domain)
- `verify_transaction_witnesses(tx, network)` — Neo3 `SYSCALL=0x41`, m-of-n keys

### C# settlement

- `RpcSettlementClient.SubmitAndFinalizeBatchAsync` — real override
- Production submitter emits `submitAndFinalizeBatch` for Zk/Multisig
- `RollupHub.SubmitBatch` / `SubmitAndFinalizeBatch` take `uint forcedInclusionCount`

### Public-input hash

Preimage = prior 348-byte layout ‖ `forcedInclusionCount` (u32 LE). Hash remains
`Hash256(preimage)`.

## Usage examples

```csharp
// Production ZK broadcast (pipeline)
await client.SubmitAndFinalizeBatchAsync(commitment, publicInputs, ct);

// RollupHub (on-chain)
SubmitAndFinalizeBatch(commitmentBytes, l1MessageHash, blockContextHash, forcedInclusionCount);
```

```rust
let hash = inventory_hash(&unsigned_bytes); // Neo Transaction.Hash
verify_transaction_witnesses(&tx, network)?;
```

## Test plan

| Case | Expected |
| ---- | -------- |
| Neo-core-shaped single-sig + single-SHA256 sign data | Authorizes execution |
| 1-of-2 / 2-of-3 multisig | Authorizes; m-of-m still works |
| Tampered signature / wrong account | `Invalid` fail-closed |
| Production wiring method name for Zk | `submitAndFinalizeBatch` |
| Pipeline ZK broadcast via InMemory client | Batch status `Finalized` |
| FI enqueue then submit with count | Head advances; underflow fail-closed |
| Gateway host pin check | WORDS match guest VK |

## Acceptance (Wave 1)

- [x] Real Neo sign-hash semantics verified in Rust tests (`witness_core` 8/8)
- [x] ZK production path calls `SubmitAndFinalizeBatchAsync` / emits `submitAndFinalizeBatch`
- [x] FI consume hooked into `SubmitBatchCore` (count parameter)
- [x] Gateway WORDS re-pinned (requires `cargo prove` / gateway-host SP1 build)
- [x] `IMPLEMENTATION_STATUS.md` + this document + canvas updated
- [x] Guest stateful fixtures regenerated for Neo3 witness + inventory hash

## Wave 2 / 3 roadmap (deferred)

See plan todos `wave2-lean-wiring` and `wave3-structure`. No code in this
iteration beyond documentation of the deferral.
