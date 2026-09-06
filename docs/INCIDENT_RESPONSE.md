# Neo N4 Incident Response Runbook

> **Version:** 2.0  
> **Last Updated:** September 6, 2026  
> **Classification:** OPERATIONAL (Internal Use Only)  
> **Review Cycle:** Quarterly or after any incident

---

## Table of Contents

1. [Emergency Contacts](#1-emergency-contacts)
2. [Incident Classification](#2-incident-classification)
3. [Emergency Pause Activation](#3-emergency-pause-activation)
4. [State Root Reconstruction](#4-state-root-reconstruction)
5. [DA Layer Corruption Recovery](#5-da-layer-corruption-recovery)
6. [SP1 Prover State Machine Desync](#6-sp1-prover-state-machine-desync)
7. [Sequencer Committee Equivocation](#7-sequencer-committee-equivocation)
8. [Cross-Chain Bridge Exploit Containment](#8-cross-chain-bridge-exploit-containment)
9. [Communication Templates](#9-communication-templates)

---

## 1. Emergency Contacts

### Primary Escalation Path

| Role | Contact | Method | Availability |
|------|---------|--------|--------------|
| Security Team Lead | security@neo-n4.io | Email + PGP | 24/7 on-call rotation |
| Core Developer | dev@r3e.network | Discord + GitHub | Business hours (UTC 9–18) |
| Governance Council | council@neo-n4.io | Multisig proposal | Pre-scheduled meetings |
| External Auditor | audit-firm@security.com | Secure channel | As needed |

### Emergency Communication Channels

- **Discord Emergency Channel**: `#emergency-incidents` (access restricted to on-call team)
- **GitHub Issues**: Create high-priority issues with label `P0-critical`
- **PGP Encrypted Email**: security@neo-n4.io
- **War Room**: Video bridge available upon request (contact on-call engineer)

### Detection Infrastructure

| System | Endpoint | Purpose |
|--------|----------|---------|
| Metrics HTTP | `GET /metrics` on node metrics port (default 9090) | Prometheus exposition — all `l2.*` metrics |
| Health probe | `GET /healthz` | Liveness check |
| Readiness | `GET /readyz` | Returns 503 when settlement stale |
| Operator status | `GET /operatorstatus` | Full JSON status document |

See [docs/telemetry.md](telemetry.md) for the complete metric catalog.

---

## 2. Incident Classification

### Severity Levels

#### SEVERE (P0) — Immediate Action Required

**Definition**: Active threat causing immediate fund loss, consensus failure, or network split.

**Examples**:
- Smart contract exploit with ongoing fund drain
- Double-signing by sequencer committee member
- DA layer corruption affecting batch finalization
- Emergency pause required to halt malicious activity

**Detection signals**: `l2.settlement.poisoned` gauge = 1, `l2.da.is_available_results` = 0, `l2.challenge.fraud_proofs` counter rising.

**Response Time**: <15 minutes  
**Required Actions**: Immediate containment, public disclosure within 1 hour

---

#### HIGH (P1) — Urgent Response Needed

**Definition**: Significant vulnerability with potential for major impact but not actively exploited.

**Examples**:
- Reproducible DoS attack vector identified
- Invalid state root detected in finalized batch
- Sequencer committee member compromise suspected
- Prover latency exceeding SLA (no proofs generated for >30 minutes)

**Detection signals**: `l2.settlement.submit_failures` rising, `l2.proving.rejected` counter > 0, `l2.settlement.confirmation_lag_batches` > 10.

**Response Time**: <1 hour  
**Required Actions**: Containment planning, stakeholder notification within 4 hours

---

#### MEDIUM (P2) — Scheduled Investigation

**Definition**: Issue requiring investigation but no immediate risk to funds or network safety.

**Examples**:
- Monitoring alert threshold exceeded (proving latency spike)
- Performance degradation below SLA targets
- DA publish latency above normal (`l2.da.publish_latency_ms` p99 > 5000)
- Minor RPC endpoint timeouts (`l2.rpc.failures` counter rising)

**Response Time**: <24 hours  
**Required Actions**: Triage assignment, weekly status updates

---

#### LOW (P3) — Routine Maintenance

**Definition**: Cosmetic issues, feature requests, or documentation improvements without security implications.

**Examples**:
- Non-critical log noise
- Explorer UI glitches
- Feature parity enhancement requests
- Optimization opportunities for batch processing

**Response Time**: Next sprint planning  
**Required Actions**: Backlog entry, prioritize based on impact

---

## 3. Emergency Pause Activation

### Purpose

Immediately freeze all cross-chain operations on a specific L2 chain (or globally) to prevent further losses during an active exploit or critical vulnerability discovery.

### Contract Methods

The `NeoHub.GovernanceController` contract (source: `contracts/NeoHub.GovernanceController/GovernanceControllerContract.cs`) exposes:

| Method | Authorization | Effect |
|--------|--------------|--------|
| `Pause()` | Owner or EmergencyCouncil | Sets global paused flag — all chains frozen |
| `Unpause()` | Owner only | Clears global paused flag |
| `PauseChain(uint chainId)` | Owner or EmergencyCouncil | Pauses a single chain |
| `UnpauseChain(uint chainId)` | Owner only | Resumes a single chain |
| `IsPaused()` | Public read | Returns global pause state |
| `IsChainPaused(uint chainId)` | Public read | Returns true if globally paused OR chain-specifically paused |

Additionally, `NeoHub.RollupHub` (source: `contracts/NeoHub.RollupHub/RollupHubContract.cs`) exposes:

| Method | Effect |
|--------|--------|
| `PauseChain(uint chainId)` | Marks chain inactive — rejects `SubmitBatch` |
| `ResumeChain(uint chainId)` | Marks chain active |
| `IsChainActive(uint chainId)` | Query active status |

### Prerequisites

- **Authority**: Governance owner key OR EmergencyCouncil multisig (set via `SetEmergencyCouncil`)
- **Verification**: Confirm incident qualifies as SEVERE (P0) severity
- **Technical Access**: Operator with `NEO_N4_OPERATOR_WIF` environment variable configured

### Detection

**Symptoms**:
- Active fund drain visible in `l2.bridge.withdrawals` counter spike
- `l2.settlement.poisoned` gauge = 1
- Unexpected `OnBondSlashed` events from GovernanceController

**Metric signals**:
```
l2_bridge_withdrawals_total       — sudden spike
l2_settlement_poisoned            — value 1
l2_challenge_fraud_proofs_total   — non-zero
```

### Immediate Containment

#### Step 1: Council Authorization (target: 5 minutes)

Coordinate via Discord `#emergency-incidents`. Required threshold: governance owner signature OR EmergencyCouncil witness.

#### Step 2: Execute Pause

Use `neo-stack` with `--broadcast` to invoke the GovernanceController contract. The CLI requires `--rpc <url>` and `NEO_N4_OPERATOR_WIF` set (or `--signer-command` for HSM-backed signing).

**Per-chain pause** (preferred — limits blast radius):

```powershell
# Operator-provided: invoke GovernanceController.PauseChain(chainId) via neo-stack
# Requires: NEO_N4_OPERATOR_WIF set to governance owner or emergency council key
neo-stack register-chain --broadcast --rpc https://<l1-rpc-endpoint> `
    --expected-network <magic> `
    --dry-run
# NOTE: PauseChain is invoked as a contract call through the governance owner wallet.
# The neo-stack CLI handles signed L1 execution for register-chain, deploy-bridge-adapter,
# and submit-batch. For governance pause, use the operator's Neo wallet tooling to call:
#   Contract: NeoHub.GovernanceController
#   Method:   PauseChain(uint chainId)
#   Witness:  Owner or EmergencyCouncil
```

**Global pause** (nuclear option — freezes all chains):

```text
Contract: NeoHub.GovernanceController
Method:   Pause()
Witness:  Owner or EmergencyCouncil
Effect:   All chains report IsChainPaused == true
Event:    OnEmergencyPaused
```

#### Step 3: Verify Pause On-Chain

Query the contract state via any Neo RPC node:

```text
Contract: NeoHub.GovernanceController
Method:   IsChainPaused(uint chainId)   [Safe — read-only]
Expected: true

Contract: NeoHub.RollupHub
Method:   IsChainActive(uint chainId)   [Safe — read-only]
Expected: false (if RollupHub.PauseChain also invoked)
```

### Recovery Procedure

1. Confirm exploit vector is patched (contract upgrade or off-chain mitigation)
2. Governance owner invokes `GovernanceController.UnpauseChain(chainId)` or `GovernanceController.Unpause()`
3. If RollupHub was also paused: invoke `RollupHub.ResumeChain(chainId)`
4. Monitor `l2.settlement.submitted` counter resumes incrementing
5. Verify `l2.bridge.deposits` and `l2.bridge.withdrawals` resume normal flow

### Verification

- [ ] `GovernanceController.IsChainPaused(chainId)` returns `false`
- [ ] `RollupHub.IsChainActive(chainId)` returns `true`
- [ ] `l2.settlement.submitted` counter incrementing within 5 minutes of unpause
- [ ] `GET /readyz` returns HTTP 200
- [ ] No new `l2.settlement.submit_failures` after unpause

### Rollback

If unpause triggers renewed exploit activity:
1. Immediately re-invoke `GovernanceController.PauseChain(chainId)`
2. Escalate to P0 — full forensic analysis required before next unpause attempt

### Estimated Recovery Time

- Pause execution: < 2 minutes (single L1 transaction)
- Verification: < 5 minutes
- Unpause after fix: 15–60 minutes (depends on patch complexity)

### Escalation Criteria

- Pause transaction fails (insufficient GAS, witness rejection) → escalate to core dev team
- Pause succeeds but withdrawals continue (contract bug) → escalate to external auditor
- Council unreachable for > 10 minutes → owner key holder acts unilaterally per pre-authorization

---

## 4. State Root Reconstruction

### Purpose

Recover corrupted or inconsistent L2 state from RocksDB backups when the state root diverges from the on-chain commitment.

### Detection

**Symptoms**:
- Node rejects valid batches with state root mismatch
- `l2.settlement.submit_failures` counter rising
- `l2.settlement.poisoned` gauge = 1 (ordered settlement head requires explicit operator recovery)
- Explorer displays incorrect account balances

**Metric signals**:
```
l2_settlement_submit_failures_total    — rising
l2_settlement_poisoned                 — value 1
l2_audit_failures_total               — non-zero
```

**Log signals**: Settlement plugin logs "state root mismatch" or "canonical state root divergence" errors.

### Immediate Containment

1. Stop the prover to prevent generating proofs against corrupted state:
   ```powershell
   # If running via neo-stack:
   # Terminate the start-prover process (Ctrl+C or kill)
   # The prover is launched via: neo-stack start-prover --prover <prove-batch-path>
   ```
2. Stop the batcher to prevent sealing new batches against bad state
3. Record the last known-good batch number from `l2.settlement.submitted` counter

### Recovery Procedure

#### Option A: RocksDB Backup Restore (Primary — Preferred)

The `neo-stack` CLI provides integrated backup/restore for RocksDB state directories. Full documentation: [docs/persistence.md](persistence.md).

**Prerequisites**:
- Valid backup archive exists (created via `neo-stack rocksdb-backup`)
- Node processes stopped (sequencer, batcher, prover)
- Sufficient disk space for pre-restore backup

**Step 1: Create a safety backup of current (corrupted) state**

```powershell
neo-stack rocksdb-backup --node-data-dir /var/lib/neo-l2 --backup-dir /mnt/backups/pre-recovery --retention-days 7
```

**Step 2: Restore from last known-good backup**

```powershell
neo-stack rocksdb-restore --snapshot-file /mnt/backups/backup-<YYYYMMDD-HHMMSS>.zip --target-dir /var/lib/neo-l2
```

The restore command:
- Extracts the ZIP archive to a temporary directory
- Moves existing data to `../pre-restore-backup-<timestamp>` (automatic rollback point)
- Copies recovered files into the target directory
- Opens the restored RocksDB and verifies integrity (read test via `Count` property)

**Step 3: Restart services**

```powershell
neo-stack start-sequencer --neo-cli <path-to-neo-cli> --dry-run
neo-stack start-batcher --neo-cli <path-to-neo-cli> --dry-run
neo-stack start-prover --prover <path-to-prove-batch>
```

Remove `--dry-run` once satisfied the launch configuration is correct.

**Step 4: Allow state catch-up**

The node replays batches from the restored checkpoint. Monitor:
- `l2.settlement.confirmation_lag_batches` decreasing toward 0
- `l2.batch.sealed` counter incrementing

#### Option B: Incremental Replay from Genesis (Fallback)

When no backup is available, full state reconstruction from genesis:

1. Initialize a fresh L2 working directory:
   ```powershell
   neo-stack init-l2 --chain-config chain.config.json
   ```
2. Bootstrap genesis state:
   ```powershell
   neo-stack bootstrap-genesis --chain-config chain.config.json
   ```
3. Re-register the chain (if the genesis root changed):
   ```powershell
   neo-stack register-chain --broadcast --rpc <l1-rpc-url> --expected-network <magic>
   ```
4. Start sequencer and allow full replay from L1 batch history

### Verification

- [ ] `GovernanceController.IsChainPaused(chainId)` — confirm still paused during recovery
- [ ] Genesis root matches `RollupHub.GetGenesisStateRoot(chainId)` on-chain value
- [ ] `l2.settlement.confirmation_lag_batches` reaches 0
- [ ] `l2.settlement.poisoned` gauge returns to 0
- [ ] `GET /readyz` returns HTTP 200
- [ ] Spot-check 10 account balances against independent source

### Rollback

If restore produces worse state:
1. The `rocksdb-restore` command automatically creates `../pre-restore-backup-<timestamp>`
2. Re-run `neo-stack rocksdb-restore` with the pre-restore backup as `--snapshot-file`
3. If all restores fail, fall back to Option B (genesis replay)

### Estimated Recovery Time

| Scenario | Duration |
|----------|----------|
| RocksDB restore (1 GB archive) | ~5 minutes |
| RocksDB restore (10 GB archive) | ~30 minutes |
| Catch-up replay after restore | 10–60 minutes (depends on lag) |
| Full genesis replay | 2–8 hours |

### Escalation Criteria

- Restore integrity check fails → try next most recent backup
- All backups corrupted → escalate to core dev team for genesis replay assistance
- State root still diverges after replay → possible contract-level bug, escalate to auditor

---

## 5. DA Layer Corruption Recovery

### Purpose

Recover from data availability failures where batch commitment data is lost or inaccessible.

### Detection

**Symptoms**:
- `l2.da.publish_failures` counter rising
- `l2.da.is_available_results` gauge = 0 (DA backend unavailable)
- `l2.da.pending_batches` gauge growing without bound
- `RollupHub.IsBatchDAAvailable(chainId, batchNumber)` returns false for recent batches

**Metric signals** (mode-tagged: `{mode="NeoFS"}` or `{mode="L1"}`):
```
l2_da_publish_failures_total{mode="NeoFS"}     — rising
l2_da_is_available_results{mode="NeoFS"}       — value 0
l2_da_pending_batches                          — growing
l2_da_neofs_read_after_write_failures_total    — non-zero (NeoFS verification failure)
```

**DA writer implementations** (source: `src/Neo.Plugins.L2DA/`):

| Writer | Mode | Failure Pattern |
|--------|------|-----------------|
| `NeoFsRestDAWriter` | NeoFS REST gateway | Network timeout, blob store full, replica count drop |
| `JsonRpcL1DAWriter` | L1 transaction anchor | L1 RPC unreachable, insufficient GAS, confirmation delay |
| `PersistentDAWriter` | Durable wrapper | Underlying RocksDB corruption at `/var/lib/neo-l2/da/` |
| `CommitteeAttestedDAWriter` | Committee-attested | Threshold signatures unavailable |

### Immediate Containment

1. Identify which DA mode is failing from the `{mode=...}` label on metrics
2. If NeoFS: check `l2.da.neofs_replica_count` — if below threshold, data may be partially lost
3. If L1: check `l2.da.l1_confirmation_blocks` — if rising, L1 may be congested
4. Stop the batcher to prevent new batches from accumulating without DA publication

### Recovery Procedure

#### Scenario A: NeoFS Blob Unavailable

1. **Verify scope**: Query `RollupHub.GetBatchDACommitment(chainId, batchNumber)` for affected batches
2. **Check replicas**: `l2.da.neofs_replica_count` gauge indicates current replication
3. **Re-publish from local store**: If `PersistentDAWriter` at `/var/lib/neo-l2/da/` still holds the blob bytes, restart the DA writer — it will re-publish on availability check
4. **Restore DA store from backup**:
   ```powershell
   neo-stack rocksdb-backup --node-data-dir /var/lib/neo-l2 --backup-dir /mnt/backups/da-recovery
   neo-stack rocksdb-restore --snapshot-file /mnt/backups/backup-<timestamp>.zip --target-dir /var/lib/neo-l2
   ```
5. **Re-register DA commitment**: After blob is available again, invoke:
   ```text
   Contract: NeoHub.RollupHub
   Method:   RecordBatchDA(uint chainId, ulong batchNumber, UInt256 daCommitment, ulong firstBlock, ulong lastBlock)
   ```

#### Scenario B: L1 Transaction Archive Loss

1. **Query multiple L1 RPC endpoints** for the original DA transaction
2. **Reconstruct from local state**: The `PersistentDAWriter` RocksDB at `/var/lib/neo-l2/da/` retains published payloads
3. **Re-anchor**: Submit a new L1 transaction with the same DA commitment bytes
4. **Verify**: `RollupHub.IsBatchDAAvailable(chainId, batchNumber)` returns true

#### Scenario C: PersistentDAWriter RocksDB Corruption

1. Stop all L2 services
2. Restore the DA subdirectory from backup:
   ```powershell
   neo-stack rocksdb-restore --snapshot-file /mnt/backups/backup-<timestamp>.zip --target-dir /var/lib/neo-l2/da
   ```
3. Restart services — `PersistentDAWriter` will re-verify existing commitments on startup

### Verification

- [ ] `l2.da.is_available_results` gauge = 1 for the affected mode
- [ ] `l2.da.pending_batches` gauge decreasing toward 0
- [ ] `l2.da.publish_failures` counter stops incrementing
- [ ] `RollupHub.IsBatchDAAvailable(chainId, batchNumber)` returns true for all recent batches
- [ ] `l2.settlement.submitted` resumes incrementing (DA was blocking settlement)

### Rollback

- If re-published data conflicts with on-chain commitments, halt and escalate — the commitment is immutable once recorded
- DA restore from backup may lose recent publications → re-publish from batcher history

### Estimated Recovery Time

| Scenario | Duration |
|----------|----------|
| NeoFS transient outage (network) | 5–30 minutes (wait for recovery) |
| NeoFS blob re-publish from local store | 10–20 minutes |
| L1 re-anchoring | 15–45 minutes (depends on L1 congestion) |
| Full DA store restore from backup | 10–30 minutes |

### Escalation Criteria

- All DA replicas lost AND no local backup → P0, state reconstruction required (Section 4)
- DA commitments on-chain don't match any available data → external auditor engagement
- Persistent failures > 2 hours → consider chain pause (Section 3) until DA is restored

### Long-term Prevention

- Configure `PersistentDAWriter` with RocksDB at `/var/lib/neo-l2/da/` (see [docs/persistence.md](persistence.md))
- Schedule regular `neo-stack rocksdb-backup` covering all seven persistence subdirectories
- Monitor `l2.da.neofs_replica_count` with alert threshold below desired replication factor

---

## 6. SP1 Prover State Machine Desync

### Purpose

Recover when the SP1 prover daemon's internal state diverges from the settlement layer's committed height, causing proof generation failures or stale proofs.

### Detection

**Symptoms**:
- `l2.proving.rejected` counter rising (local verifier rejecting proofs before submission)
- `l2.proving.latency_ms` histogram showing timeouts or extreme values
- `l2.settlement.pending` gauge growing (artifacts not reconciled on L1)
- Prover logs show "state root mismatch" or "witness height != committed height"

**Metric signals**:
```
l2_proving_rejected_total                  — rising
l2_proving_latency_ms_max                  — > 600000 (10 min timeout)
l2_settlement_pending                      — growing without corresponding submitted
l2_settlement_confirmation_lag_batches     — stuck at non-zero
```

**Architecture context**: The SP1 prover stack consists of:
- `bridge/neo-zkvm-host/` — Rust prover daemon (SP1 SDK 6.2.1, out-of-process)
- `bridge/neo-zkvm-guest/` — RISC-V guest program (re-executes stateful N4 V1 profile)
- `src/Neo.L2.Proving/` — C# proving orchestration (`Sp1SettlementExecutionStack`)
- `src/Neo.L2.Persistence/KeyValueProofWitnessStore` — durable proof artifacts and checkpoints

### Immediate Containment

1. **Stop the prover process**:
   ```powershell
   # The prover is launched via: neo-stack start-prover --prover <prove-batch-path>
   # Terminate the process (Ctrl+C or kill the child process)
   ```
2. **Do NOT clear the proof-witness store** — it contains rollback checkpoints needed for recovery
3. **Record current state**: Note the values of `l2.settlement.pending` and `l2.proving.generated` for post-recovery comparison

### Recovery Procedure

The `KeyValueProofWitnessStore` (at `/var/lib/neo-l2/proof-witness/`) provides crash-idempotent checkpoint completion and startup reconciliation. Recovery steps:

**Step 1: Allow startup reconciliation**

On restart, the proof-witness store:
- Queries L1 for every local artifact
- Requires the local proof manifest
- Validates contiguous finality and the canonical state root
- Only then permits recovery side effects

**Step 2: Restore proof-witness store from backup** (if reconciliation fails)

```powershell
neo-stack rocksdb-restore --snapshot-file /mnt/backups/backup-<timestamp>.zip --target-dir /var/lib/neo-l2/proof-witness
```

**Step 3: Restart the prover**

```powershell
neo-stack start-prover --prover <path-to-prove-batch>
```

The prover will re-derive proofs from the last committed checkpoint. The `Sp1SettlementExecutionStack` implements idempotent retry — re-proving an already-settled batch is a no-op.

**Step 4: Monitor recovery**

Watch metrics:
- `l2.proving.generated` counter starts incrementing again
- `l2.settlement.pending` gauge decreases
- `l2.settlement.submitted` counter increments (proofs accepted on L1)

### Verification

- [ ] `l2.proving.rejected` counter stops incrementing
- [ ] `l2.settlement.pending` returns to 0
- [ ] `l2.settlement.confirmation_lag_batches` decreasing
- [ ] `l2.proving.latency_ms` returns to normal range (< 600000ms for SP1 Groth16)
- [ ] Prover host process running without error logs

### Rollback

- If restarted prover immediately produces rejected proofs: stop prover, restore proof-witness store from older backup, retry
- If proof-witness restore corrupts settlement state: fall back to Section 4 (full state reconstruction)

### Estimated Recovery Time

| Scenario | Duration |
|----------|----------|
| Simple restart with reconciliation | 5–15 minutes |
| Proof-witness restore from backup | 15–30 minutes |
| Re-prove backlog (per batch) | 5–20 minutes per SP1 Groth16 proof |
| Full recovery with large backlog | 1–4 hours |

### Escalation Criteria

- Prover restarts but immediately crashes → check `bridge/neo-zkvm-host/` logs for SP1 SDK errors
- Reconciliation fails repeatedly → proof-witness store may be permanently corrupted, escalate to core dev
- `l2.settlement.poisoned` = 1 after recovery attempt → settlement head requires manual operator intervention

---

## 7. Sequencer Committee Equivocation

### Purpose

Detect and respond to sequencer committee members producing conflicting state commitments (double-signing), and slash their bond.

### Detection

**Symptoms**:
- Two valid batch submissions for the same batch number with different state roots
- `l2.challenge.fraud_proofs` counter non-zero
- `l2.censorship.reports` counter rising (related anti-censorship detection)
- `OnBondSlashed` event emitted from GovernanceController

**Metric signals**:
```
l2_challenge_fraud_proofs_total       — non-zero
l2_censorship_reports_total           — rising
l2_sequencer_committee_size           — decreasing (post-slash removal)
```

**Contract events to monitor** (GovernanceController):
- `OnBondSlashed(uint chainId, UInt160 sequencer, BigInteger amount, UInt160 recipient)`
- `OnSequencerUnregistered(uint chainId, ECPoint sequencerKey)`

### Immediate Containment

1. **Identify the equivoking sequencer**: Check `GovernanceController.GetSequencerAddress(chainId, sequencerKey)` for the offending key
2. **Pause the chain** if equivocation is ongoing (Section 3):
   ```text
   Contract: NeoHub.GovernanceController
   Method:   PauseChain(uint chainId)
   ```
3. **Preserve evidence**: Export batch commitments from both conflicting submissions

### Recovery Procedure

**Step 1: Slash the offending sequencer's bond**

```text
Contract: NeoHub.GovernanceController
Method:   SlashSequencer(uint chainId, UInt160 sequencer, BigInteger amount, UInt160 recipient)
Witness:  Governance owner
Effect:   Transfers bonded amount to recipient, emits OnBondSlashed
```

Query current bond before slashing:
```text
Contract: NeoHub.GovernanceController
Method:   GetSequencerBond(uint chainId, UInt160 sequencer)   [Safe — read-only]
```

**Step 2: Unregister the sequencer**

```text
Contract: NeoHub.GovernanceController
Method:   UnregisterSequencer(uint chainId, ECPoint sequencerKey)
Witness:  Governance owner
Effect:   Removes from active committee, emits OnSequencerUnregistered
```

**Step 3: Verify minimum bond coverage remains**

```text
Contract: NeoHub.GovernanceController
Method:   HasMinBond(uint chainId, UInt160 sequencer)   [Safe — read-only]
Method:   GetMinBond()                                   [Safe — read-only]
```

Ensure remaining sequencers still meet the minimum bond threshold.

**Step 4: Rotate council if threshold compromised**

If the equivoking member was also a governance council member:
```text
Contract: NeoHub.GovernanceController
Method:   RotateCouncil(ECPoint[] newMembers, uint newThreshold)
Witness:  Governance owner + timelock (IsApprovedAndTimelocked)
```

**Step 5: Unpause chain**

Once the bad actor is removed and committee integrity restored:
```text
Contract: NeoHub.GovernanceController
Method:   UnpauseChain(uint chainId)
```

### Verification

- [ ] `GovernanceController.IsSequencerRegistered(chainId, offenderKey)` returns `false`
- [ ] `GovernanceController.GetSequencerBond(chainId, offender)` returns 0
- [ ] `l2.sequencer.committee_size` reflects reduced count
- [ ] All remaining sequencers satisfy `HasMinBond` = true
- [ ] `l2.challenge.fraud_proofs` counter stops incrementing
- [ ] Chain unpaused and `l2.batch.sealed` resuming

### Rollback

- Slashing is irreversible (by design — no rollback for confirmed equivocation)
- If slashing was erroneous (false positive): re-register the sequencer via `RegisterSequencer(chainId, key, address)` and require new bond deposit via `DepositBond(chainId, sequencer, amount)`

### Estimated Recovery Time

| Action | Duration |
|--------|----------|
| Evidence collection + identification | 15–30 minutes |
| Slash + unregister transactions | < 5 minutes |
| Council rotation (if needed, subject to timelock) | Timelock period (configured at deploy, typically 24–72 hours) |
| Full recovery with new sequencer onboarding | 1–4 hours |

### Escalation Criteria

- Multiple sequencers equivocating simultaneously → possible consensus-level attack, pause all chains
- Slash transaction rejected (insufficient permissions) → governance owner key compromised, escalate immediately
- Bond already withdrawn before slash → check `WithdrawBond` call history, may indicate insider threat

---

## 8. Cross-Chain Bridge Exploit Containment

### Purpose

Contain and recover from active exploitation of the cross-chain bridge (SharedBridge deposits/withdrawals or external bridge watchers).

### Detection

**Symptoms**:
- Abnormal `l2.bridge.withdrawals` spike without corresponding deposits
- `l2.bridge.withdrawals_rejected` counter staying at 0 while withdrawals spike (bypassed validation)
- Large value transfers in `OnWithdrawalFinalized` events from SharedBridge
- External bridge watcher (`watchers/neo-bridge-watcher-eth/`) reporting anomalous cross-chain messages

**Metric signals**:
```
l2_bridge_withdrawals_total          — sudden spike
l2_bridge_deposits_rejected_total    — should be non-zero if validation catching attacks
l2_bridge_withdrawals_rejected_total — zero during successful exploit (bad sign)
l2_settlement_poisoned               — may be 1 if settlement detects inconsistency
```

**Contract events to monitor** (SharedBridge):
- `OnDepositEnqueued(uint chainId, ulong nonce, UInt160 asset, UInt160 sender, BigInteger amount)`
- `OnWithdrawalFinalized(uint chainId, UInt160 asset, UInt160 recipient, BigInteger amount)`

### Immediate Containment

**Step 1: Pause the affected chain** (Section 3)

```text
Contract: NeoHub.GovernanceController
Method:   PauseChain(uint chainId)
```

This prevents new `SharedBridge.Deposit` calls from being processed for the target chain.

**Step 2: Assess locked balances**

```text
Contract: NeoHub.SharedBridge
Method:   GetLockedBalance(uint chainId, UInt160 asset)   [Safe — read-only]
```

Compare against expected totals. A deficit indicates drained funds.

**Step 3: Identify attack vector**

Query recent deposits:
```text
Contract: NeoHub.SharedBridge
Method:   GetDeposit(uint chainId, ulong nonce)   [Safe — read-only]
```

Check withdrawal proofs: The `FinalizeWithdrawalWithProof` and `EmergencyFinalizeWithdrawalWithProof` methods have different authorization requirements — determine which path was exploited.

### Recovery Procedure

**Step 1: Freeze remaining assets**

If the locked balance migration has not been sealed:
```text
Contract: NeoHub.SharedBridge
Method:   SealLockedBalanceMigration()
Effect:   Prevents further MigrateLockedBalance calls
Query:    IsLockedBalanceMigrationSealed()   [Safe — read-only]
```

**Step 2: Forensic analysis**

- Export all `OnWithdrawalFinalized` events from the attack window
- Cross-reference with `l2.bridge.withdrawals` metric timestamps
- Identify recipient addresses that received unauthorized withdrawals
- Check if `EmergencyFinalizeWithdrawalWithProof` was invoked (requires governance authority — indicates insider threat)

**Step 3: External bridge containment**

For attacks via external chain bridges (`watchers/neo-bridge-watcher-eth/`, `watchers/neo-bridge-watcher-sol/`, `watchers/neo-bridge-watcher-tron/`):
- Stop the watcher process for the affected chain
- Verify watcher configuration in `samples/watcher-configs/`
- Check for signature replay across chains

**Step 4: Asset recovery**

- If within the challenge window: fraud proof submission via `l2.challenge.fraud_proofs` pipeline
- Coordinate with external chain teams for fund freezing at destination
- Document all affected addresses for potential legal action

**Step 5: Restore bridge operation**

After patch deployment:
1. Verify `SharedBridge.GetLockedBalance` matches expected post-incident totals
2. Unpause chain: `GovernanceController.UnpauseChain(chainId)`
3. Monitor `l2.bridge.deposits` and `l2.bridge.withdrawals` for normal patterns

### Verification

- [ ] `GovernanceController.IsChainPaused(chainId)` returns `false` after recovery
- [ ] `SharedBridge.GetLockedBalance(chainId, asset)` matches expected value
- [ ] `l2.bridge.withdrawals_rejected` counter incrementing (validation active)
- [ ] No new unauthorized `OnWithdrawalFinalized` events
- [ ] External bridge watchers running and reporting normal state
- [ ] `GET /readyz` returns HTTP 200

### Rollback

- If unpause triggers renewed drain: immediately re-pause via `GovernanceController.PauseChain(chainId)`
- Bridge pause is the terminal safety state — there is no "partial unpause"

### Estimated Recovery Time

| Phase | Duration |
|-------|----------|
| Detection to pause | < 15 minutes |
| Forensic analysis | 2–8 hours |
| Patch development + deployment | 4–24 hours |
| Post-patch verification | 1–2 hours |
| Full service restoration | 24–48 hours total |

### Escalation Criteria

- Funds drained > threshold (configurable per deployment) → immediate public disclosure + law enforcement notification
- `EmergencyFinalizeWithdrawalWithProof` exploited → governance key compromise, rotate owner via `GovernanceController.SetOwner(newOwner)`
- Multiple chains affected simultaneously → global `GovernanceController.Pause()` and full system audit

---

## 9. Communication Templates

### Public Incident Disclosure (SEVERE / P0)

```text
EMERGENCY: SECURITY INCIDENT DISCLOSURE

Severity: CRITICAL (P0)
Status: [CONTAINED / INVESTIGATING / RESOLVED]
Affected Components: [SharedBridge / Sequencer Committee / Settlement / DA Layer]
Timestamp: [ISO 8601]

Summary:
[2-3 sentence non-technical description of what happened]

Impact:
- Funds affected: [amount or NONE]
- Users affected: [count or estimate]
- Service downtime: [duration]

Actions Taken:
1. [Immediate containment measure — e.g., "Chain paused via GovernanceController.PauseChain"]
2. [Investigation initiated]
3. [Fix deployed if applicable]

Next Steps:
- [Timeline for full resolution]
- [Compensation plan if applicable]

Contact: security@neo-n4.io (PGP encrypted)
```

### Internal War Room Status Update

```text
INCIDENT STATUS UPDATE — [Incident ID]
Time: [UTC timestamp]
Severity: [P0/P1/P2]
Phase: [Detection / Containment / Recovery / Verification / Resolved]

Current State:
- Affected metrics: [list l2.* metrics with current values]
- Containment status: [paused/degraded/operational]
- Recovery progress: [step N of M]

Blockers:
- [Any blocking issues]

Next Actions:
- [ ] [Action item with owner and ETA]
- [ ] [Action item with owner and ETA]

Decision Needed:
- [Any decisions requiring council/owner input]
```

### Post-Mortem Report Template

```markdown
# Incident Post-Mortem: [Incident ID]

## Classification
- **Severity**: [P0/P1/P2/P3]
- **Duration**: [detection to resolution]
- **Affected Components**: [list]
- **Root Cause Category**: [exploit / misconfiguration / software bug / infrastructure]

## Timeline (all times UTC)
| Time | Event |
|------|-------|
| HH:MM | Detection — [method: metric alert / manual observation / external report] |
| HH:MM | Containment — [action taken] |
| HH:MM | Recovery initiated — [procedure followed from this runbook] |
| HH:MM | Verification complete |
| HH:MM | Service restored |

## Root Cause
[Technical explanation with references to specific contracts, metrics, or code paths]

## Impact Assessment
- **Funds**: [quantified loss or NONE]
- **Availability**: [duration of degraded service]
- **Data integrity**: [any state divergence, batches affected]

## Detection Effectiveness
- **Time to detect**: [duration]
- **Detection method**: [which metric/alert caught it]
- **Could detection be faster?**: [yes/no + how]

## Recovery Effectiveness
- **Runbook section used**: [Section N]
- **Runbook accuracy**: [accurate / gaps found — describe]
- **Recovery time vs estimate**: [actual vs predicted]

## Action Items
| Priority | Action | Owner | Due Date |
|----------|--------|-------|----------|
| HIGH | [Prevent recurrence] | [team] | [date] |
| MEDIUM | [Improve detection] | [team] | [date] |
| LOW | [Update documentation] | [team] | [date] |

## Runbook Updates Required
- [ ] Section [N]: [describe needed change]
```

### Council Vote Request Template

```text
COUNCIL-VOTE-REQUEST
Reason: [Brief description of incident requiring governance action]
Action: [PauseChain(chainId) / Pause() / SlashSequencer / RotateCouncil]
Duration: [INDEFINITE / estimated hours]
Evidence: [Link to metrics dashboard / event logs / forensic report]

Vote (required threshold per GovernanceController.GetThreshold()):
[ ] APPROVE — execute immediately
[ ] REJECT — insufficient evidence
[ ] ABSTAIN — conflict of interest

Deadline: [timestamp + 15 minutes]
```

---

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-09-06 | Operations Team | Initial draft |
| 2.0 | 2026-09-06 | Operations Team | Complete rewrite: verified all commands against repo, integrated RocksDB backup/restore CLI, replaced invented scripts with real contract methods and neo-stack subcommands, added detection metrics from telemetry catalog |

---

**Disclaimer**: Contract method invocations require appropriate authorization (governance owner key or emergency council witness). The `neo-stack` CLI signed execution requires `NEO_N4_OPERATOR_WIF` or `--signer-command` configuration. Always verify recovery procedures in a private network (see [docs/private-network-testing.md](private-network-testing.md)) before production use.
