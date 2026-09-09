# Architecture Iteration — Wave 2/3 (2026-09-07 continued)

## Overview

Continuation of [`architecture-iteration-2026-09-07.md`](./architecture-iteration-2026-09-07.md).
Wave 1 closed Validity settlement + Neo N3 witness correctness. This iteration
closes **lean wiring** (Wave 2) and starts **structure** (Wave 3) without
resurrecting deleted micro-contracts.

**Verdict (post-change):** Lean wiring for Gateway publish, LiveDeploy, FI hash
binding, SharedBridge local mappings, and hot-path native reuse is closed.
MessageRouterHash remains a deprecated alias for SharedBridge (does not auto-force
router construction). L1→L2 messaging opts in via `MessageRouterDeploymentHeight` against
SharedBridge (or legacy MessageRouterHash), polling `getL1ToL2Message` with legacy
`getL1ToL2`/`isConsumed` fallback. Optimistic host composition is documented advisory;
RollupHub and ZkLocalHost still fail-closed on optimistic production. Full god-file
splits and `doc.md` rename remain Wave 3 follow-ups where still open. Gateway batch VK
WORDS re-pinned from guest ELF (F4); full Docker prove still recommended for release.

## Decisions (locked for Wave 2/3)

1. **Public-input hash domain = 352 bytes always.**
   Preimage = prior 348-byte layout ‖ `forcedInclusionCount` (u32 LE), including
   when count is `0`. Rust `hash_public_inputs_with_forced` always appends the
   u32; C# `HashPublicInputs` / `EncodePublicInputs` / RollupHub
   `ComputePublicInputHash` match. Breaking change vs Wave-1 348-byte hashes —
   regenerate fixtures / parity pins in the same PR.

2. **Gateway global-root publish lives on RollupHub.**
   `PublishGatewayGlobalRoot` verifies the aggregation proof via ZkVerifier,
   advances per-chain watermarks, and in the same tx calls SharedBridge
   `PublishMessageRoots`. Off-chain Gateway RPC targets RollupHub (+ SharedBridge
   hash for message roots), not SettlementManager/MessageRouter.

3. **LiveDeploy default = lean 5 only.**
   `Sp1Groth16Verifier` → `ZkVerifier` → `GovernanceController` → `RollupHub` →
   `SharedBridge`. Post-deploy binds GC↔RollupHub↔SharedBridge. Any reference to
   deleted micro-contracts fail-closes with a clear error (no silent KeyNotFound).

4. **MessageRouterHash collapses to SharedBridgeHash.**
   Settings accept `SharedBridgeHash` (preferred) with `MessageRouterHash` as
   deprecated alias. Scanners read SharedBridge event surface.

5. **Optimistic is advisory / non-production.**
   `ZkLocalHostComposition` and production Wire reject optimistic proof profiles.
   `OptimisticLocalHostComposition` remains for lab/dev only; RollupHub continues
   to fail-closed on optimistic settlement.

6. **SharedBridge auth = RollupHub self-binding.**
   `PrefixSettlementManager` stores the RollupHub hash (name kept for storage
   layout stability). TokenRegistry external `Contract.Call` fallback is removed;
   only local mapping prefixes are authoritative.

7. **Wave 3 structure (this iteration, scoped):**
   - Hot path: reuse native execution output between witness build and state
     commit (avoid double `ExecuteNativeAsync` when hashes match).
   - Extract a minimal wire-digest CI check for public-input size/layout.
   - Full god-file splits deferred if they risk destabilizing Wave 2 pins;
     prefer focused extraction of FI/hash helpers and publish path first.

## Findings matrix (Wave 2/3)

| ID | Sev | Finding | Status |
| -- | --- | ------- | ------ |
| F3b | P0 | FI count not in public-input hash | Closed (352-byte domain) |
| F4 | P0 | Gateway batch VK WORDS stale | Closed — WORDS re-derived from pinned guest ELF (`print_batch_vk_words`); full Docker prove still recommended for release |
| F7 | P0 | `PublishGatewayGlobalRoot` missing on RollupHub | Closed |
| F8 | P1 | LiveDeploy micro-contract post-deploy | Closed |
| F9 | P1 | Optimistic first-class off-chain | Closed (advisory docs + Zk/RollupHub fail-closed) |
| F11 | P1 | MessageRouterHash / scanners | Closed (deprecated alias → SharedBridgeHash; lean `getL1ToL2Message` + `MessageRouterDeploymentHeight` opt-in on SharedBridge) |
| F12 | P1 | SharedBridge TokenRegistry external hop | Closed (local RegisterMapping + refreshed NEF; VmTests retargeted) |
| F10 | P2 | God-modules | Closed partial — `wire/` split + C# broadcast/documents partials + `ProofWitnessStore` models/serializers split |
| F13 | P2 | Double native re-exec on hot path | Closed (reuse witness-phase native output) |
| F14 | P0 | Public-inputs **wire** encoding still 348 bytes on Rust while C# `BatchSerializer.PublicInputsSize` was 352; guest fixtures truncated `contentHash` (`need 32 bytes, have 28`) and every `UT_Sp1BatchProofProver` decode path failed. Production-ready audit wave 2026-09-09: `PublicInputs.forced_inclusion_count` added; `write_public_inputs`/`read_public_inputs` match C#; fixtures regenerated; out-of-band golden recomputed (`805b89e2…dd22c` with FI count 0). | Closed |

## Target architecture (Gateway + FI)

```
GatewayPublicationPipeline
  → ProofBoundRpcGlobalRootPublisher(RollupHub)
  → RollupHub.PublishGatewayGlobalRoot(epoch, root, proof, constituents…)
  → ZkVerifier.verify
  → advance watermarks
  → SharedBridge.PublishMessageRoots(chainId, batch, l2l1, l2l2)

SubmitBatchCore
  → ComputePublicInputHash(… ‖ forcedInclusionCount LE)
  → ConsumeForcedTransactionsInternal(count)
```

## API reference

### Public inputs (352-byte encode / hash preimage)

| Offset | Field |
| ------ | ----- |
| 0..348 | prior layout (chainId…blockContextHash) |
| 348..352 | `forcedInclusionCount` u32 LE |

### RollupHub

```csharp
bool PublishGatewayGlobalRoot(
    ulong epoch,
    byte[] globalRoot,
    byte proofSystem,
    byte[] publicInputs,
    byte[] proof,
    /* constituent metadata as existing Gateway ABI */);
```

### Deploy

```text
LiveDeploy --plan lean-default
  → 5 NEFs only; post-deploy: SetGovernance / SetSettlementManager(RollupHub) / SetEmergency
```

## Test plan

| Case | Expected |
| ---- | -------- |
| HashPublicInputs with ForcedInclusionCount=0 vs 3 | Distinct 352-byte domain hashes |
| C# ↔ Rust parity for forced count | Equal digests |
| RollupHub FI submit with mismatched count vs hash | Fail-closed |
| `UT_CanonicalEncodingParity_Vm` / `UT_SharedBridge_Vm` settle via RollupHub (352-byte hash), not SettlementManager | Green against lean NEFs |
| SharedBridge deposits/withdrawals use local `RegisterMapping` (no TokenRegistry hop) | Mapping required before Deposit / FinalizeWithdrawal* |
| RollupHub `VerifyWithdrawalLeafWithProof` rejects `leafIndex` with bits above proof depth (V5 terminator) | `index != 0` after fold → false |
| PublishGatewayGlobalRoot happy path | Watermark advance + SharedBridge roots |
| LiveDeploy Default post-deploy | No KeyNotFound; only lean hashes |
| ZkLocalHostComposition + Optimistic | Reject / fail-closed |
| Sp1StatefulBatchExecutor hot path | Single native exec when cache hit |

## Acceptance

- [x] 352-byte public-input domain green across C#/Rust/RollupHub tests
- [x] Gateway publisher method name targets RollupHub
- [x] LiveDeploy lean-only post-deploy
- [x] Optimistic production reject documented + enforced at Zk host / RollupHub
- [x] SharedBridge TokenRegistry external call removed
- [x] Hot-path native re-exec avoided when output reused
- [x] Audit canvas + IMPLEMENTATION_STATUS updated
- [x] Gateway WORDS re-pinned from pinned guest ELF (`print_batch_vk_words`; Docker prove still for release)
