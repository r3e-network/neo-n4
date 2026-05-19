# Security Model

> Operator-facing summary of the Neo Elastic Network's security guarantees, threat
> model, and the user-visible labels that surface a chain's actual trust assumptions.
> The full threat model is in `doc.md` §17 and the formal write-up is in
> `WHITEPAPER.md` §12.

---

## What L1 guarantees

For any L2 chain registered in `NeoHub.ChainRegistry`:

- **Asset escrow integrity.** Canonical assets (GAS / NEO / USDT / USDC /
  BTC / NEP-17) live in
  `NeoHub.SharedBridge`. No L2 can mint, burn, or move escrowed assets except by
  finalizing a `withdrawalRoot` through the registered verifier.
- **Settlement determinism.** A `L2BatchCommitment` accepted by `SettlementManager`
  has been verified by the `IL2ProofVerifier` registered in `VerifierRegistry` for
  that chain's `ProofType`. The verifier additionally checks
  `commitment.PublicInputHash == hash(publicInputs)` — preventing a malicious
  prover from signing different inputs than the commitment claims.
- **Replay safety.** Every cross-chain message carries `(chainId, nonce)` and is
  deduped per-pair in `NeoHub.MessageRouter`.
- **Withdrawal finality.** Funds leave `SharedBridge` only on inclusion proofs
  against a *finalized* `withdrawalRoot`. Pre-finalization batches cannot release
  L1 assets.
- **Escape hatch.** `EmergencyManager` exposes a governance-gated path for users
  to withdraw against the last finalized state root if the L2 fails durably.

## What L1 does NOT guarantee (per chain, until the chain reaches Phase 4)

- **State-root validity for chains in Phase 0–2.** Until ZK validity proofs are
  online, an L2 in Phase 0 (sidechain) is trust-the-sequencer; Phase 1–2 chains
  are trust-the-multisig; Phase 3 chains are trust-the-challenge-window.
- **Sequencer liveness.** The sequencer can stall a chain. Forced inclusion +
  bond slashing make stall expensive; escape hatch makes it eventually unwindable.

The right way to think about this: **L1 verifies what the registered verifier
verifies.** Phase 4 (ZK validity) makes the verifier trustless. Phase 3
(optimistic) makes it as trusted as the bisection-game challenge window. Phase
0–2 stack governance (Neo Council, sequencer bonds) on top of multisig.

---

## Per-chain security labels

<p align="center">
  <img src="figures/trust-spectrum.svg" alt="Per-chain security spectrum: 0 sidechain (full sequencer trust), 1 settled L2 (DA + state-root commitments), 2 optimistic rollup (fraud-proof challenge window), 3 ZK validity (cryptographic finality). Each card shows what the user trusts, L1 guarantee, withdrawal time, failure mode, and an example deployment." width="900">
</p>

`ChainRegistry` requires every L2 to publish on-chain its security profile:

| Field             | Meaning                                                                    |
| ----------------- | -------------------------------------------------------------------------- |
| `securityLevel`   | `0` sidechain · `1` settled L2 · `2` optimistic rollup · `3` ZK validity   |
| `daMode`          | `1` NeoFS DA by default/recommended · `0` L1 DA · `2` external DA · `3` DAC |
| `gatewayEnabled`  | Whether the chain settles via Neo Gateway (Phase 5)                        |
| `permissionlessExit` | Whether `EmergencyManager` can be invoked unilaterally by users         |

Users read this via the `getsecuritylevel` RPC. UIs MUST surface these labels
prominently — particularly DAC chains (label them as such, no marketing
sugar-coating).

A simple operator rule: **if a wallet UX hides the security label, the chain's
security claim is downgraded to whatever the worst label could be.**

---

## Threats and mitigations

| #  | Threat                          | Primary mitigation                                                       | Code reference                          |
| -- | ------------------------------- | ------------------------------------------------------------------------ | --------------------------------------- |
| 1  | Sequencer censorship            | Forced inclusion + bond slashing + escape hatch                          | `Neo.L2.ForcedInclusion`, `Neo.L2.Censorship` |
| 2  | Invalid state root              | ZK validity proof (Phase 4) or optimistic challenge (Phase 3)            | `Neo.L2.Proving.RiscVZk`, `Neo.L2.Challenge` |
| 3  | Bridge exploit                  | Lock-mint vs burn-unlock invariants; rate limits; emergency pause        | `NeoHub.SharedBridge`, `EmergencyManager` |
| 4  | Replay attack (cross-chain)     | `(chainId, nonce)` envelope + per-pair dedup                             | `NeoHub.MessageRouter`, `Neo.L2.Messaging.L1MessageInbox` |
| 5  | DA unavailability               | Public DA security label in `ChainRegistry`; escape hatch on opacity     | `NeoHub.DARegistry`, `EmergencyManager` |
| 6  | Malicious validator committee   | Sequencer bonds; rotate-out via `SequencerRegistry`                      | `NeoHub.SequencerBond`, `NeoHub.SequencerRegistry` |
| 7  | Prover bug                      | `VerifierRegistry` upgrade behind governance delay + security council veto | `NeoHub.VerifierRegistry`, `NeoHub.GovernanceController` |
| 8  | Verifier upgrade attack         | Same governance-delay + veto path as #7                                  | `NeoHub.GovernanceController`           |
| 9  | Message duplication             | `MessageRouter` per-pair `(chainId, nonce)` dedup                         | `NeoHub.MessageRouter`                  |
| 10 | L2 contract bug                 | Local L2 emergency pause + `EmergencyManager` escape hatch               | `NeoHub.EmergencyManager`               |

The codebase enforces a much wider catalog of defensive invariants beyond the
threat-model 10. A non-exhaustive list of the structural ones (each with a
pinning regression test):

- **Cross-batch withdrawal-nonce dedup.** A user cannot reuse a `(sender, nonce)`
  withdrawal across batches even after `WithdrawalProcessor.SealBatch` clears the
  in-flight set.
- **Public-input hash equality at the prover boundary.** `L2SettlementPlugin`
  rejects a proof whose `publicInputHash` differs from the settler's computed
  hash, before submitting to L1 — preventing wasted L1 round-trips.
- **Empty-proof rejection at prove time.** A non-`None` `ProofType` paired with
  empty `Proof` bytes is rejected at the prove boundary, not waited-for at audit
  time.
- **Strict length match in decoders.** Trailing bytes after the documented
  payload are rejected; without this, an attacker could append padding that L1
  hashes but L2 strips, creating a malleability surface.
- **Enum-byte validation at decode.** Proof-type / DA-mode / etc. discriminants
  are bounds-checked before cast, preventing undefined enum values from
  silently propagating downstream.
- **Defensive copy on stored bytes.** Stores that retain payloads (`InMemoryL2RpcStore`,
  `InMemoryMessageRouter`, `KeyedStateStore`, `InMemoryDAWriter`) clone on insert and
  on iteration, so caller buffer reuse can't silently corrupt records.
- **Signer-set deduplication BEFORE signature verification.** Without this, a
  malicious prover could submit `MaxSigners=256` copies of one valid signature
  and force 256 redundant ECDSA verifications before the duplicate check fires.
- **Subscriber-failure isolation on plugin events.** A throwing `OnBatchSealed`
  subscriber is contained per-subscriber so it can't surface its exception to
  Neo's `Blockchain.Committed` and destabilize block import.
- **Metric-sink isolation from business state.** A throwing `IL2Metrics`
  implementation cannot leave committed state with the caller seeing an exception;
  every business-state call site uses `MetricsExtensions.SafeIncrementCounter`
  + try/catch wrapping. Worst-case bug found: a metric throw after
  `SubmitBatchAsync` succeeded would re-queue an already-on-L1 commitment in a
  retry loop — fixed.
- **JSON-RPC response-id validation.** `JsonRpcClient.CallAsync` rejects a
  response whose `id` doesn't match the request's, per JSON-RPC 2.0 §5.

See `CHANGELOG.md` from iter 67 onward for the full catalog with one-paragraph
rationale per defense.

---

## Reserved chain ids

`chainId = 0` is reserved as the **L1 sentinel**. The convention is encoded in
`L2Outbox.L1ChainId` and enforced at every external mutator across the contract
suite — `RegisterChain`, `EnqueueForcedTransaction`, `Deposit`, `FinalizeWithdrawal`,
`SequencerRegistry.Register`, `SequencerBond.Deposit`, `MessageRouter.EnqueueL1ToL2`,
`OptimisticChallenge.OpenWindow`, `EmitMessage` (L2 native), and the `L2SystemConfig` /
`L2MessageContract` `_deploy` paths. Off-chain sites also reject it via
`ChainIdValidator.ValidateL2`.

Why so many enforcement points: the L2 routing layer interprets `chainId == 0` as
"this message goes to L1." A registered L2 with `chainId = 0` would silently
misroute every L2→L2 message as L2→L1, dropping them at the gateway and breaking
every other chain in the network. Defense-in-depth here means both the deploy-time
admission gate (`ChainRegistry`) and every individual contract that takes a
chainId rejects 0 — so an operator misconfig only ever produces a clear error
at the contract entry point, never silent misrouting.

---

## Operator checklist

Before launching an L2:

- [ ] Set `securityLevel` honestly. If you're a Phase-2 chain (no challenge
      window yet), don't label as `optimistic rollup`.
- [ ] Use `daMode=NeoFS` for the canonical N4 DA path. If you explicitly use
      L1, external DA, or DAC, label it honestly; UIs should warn on DAC.
- [ ] Wire the metrics plugin. The `Neo.Plugins.L2Metrics` plugin hosts an
      `IL2Metrics` sink + `MetricsHttpServer` exposing `/metrics`, `/healthz`,
      `/readyz`. Attach Prometheus and dashboard from `docs/telemetry.md`.
- [ ] Audit the deploy bundle. `Neo.Hub.Deploy plan` produces a deterministic,
      dependency-resolved sequence of L1 deploys; review every step against the
      `ChainRegistry` config you intend to register.
- [ ] Run the in-process devnet (`tools/Neo.L2.Devnet`) end-to-end before
      pointing the plugins at a live Neo network.
- [ ] Configure the audit framework. `Neo.L2.Audit.ChainAuditor` accepts a
      sequence of `IAuditCheck` implementations; the default suite
      (`ContinuityCheck`, `ProofValidityCheck`, `NoZeroProofCheck`,
      `PublicInputHashConsistencyCheck`, `BatchRangeCheck`,
      `DAAvailabilityCheck`) catches the typical "drifted state" modes within
      minutes of a bad batch. The devnet runner shows the canonical wiring.

---

## Reporting issues

Security issues should be reported to the Neo project security mailbox before
public disclosure. See the main `neo-project/neo` repo for the current
disclosure policy.
