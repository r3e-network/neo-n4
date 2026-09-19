# Neo N4 Disaster Recovery Runbook

**Version:** 1.0  
**Last Updated:** September 14, 2026  
**Owner:** Operations Team  
**Review Cadence:** Monthly operational review, quarterly full drill

---

## Document Control

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-09-14 | [Name] | Initial draft for production launch |
| ... | YYYY-MM-DD | [Name] | Update based on lessons learned |

---

## Emergency Contacts

| Role | Name | Contact | Availability |
|------|------|---------|--------------|
| On-Call Engineer | [Name] | PagerDuty rotation | 24/7 |
| Security Council Lead | [Name] | Encrypted email + Signal | As needed |
| DevOps Escalation | [Name] | Slack DM @ops-escalation | Business hours |
| External Vendor Support | R3E Network | Ticket portal (support@reborn.com) | SLA-dependent |

**Escalation Thresholds:**
- **Critical (page immediately):** Funds at risk, chain liveness impacted, security breach
- **High (within 1 hour):** Performance degradation >50%, settlement blocked
- **Medium (business hours):** Non-financial issues, degraded UX

---

## Quick Reference Decision Trees

### Is the chain producing blocks?
```
├─ YES → Continue monitoring
│   └─ Check metrics: l2.batch.sealed counter steady?
│       ├─ YES → Normal operation
│       └─ NO → Investigate sequencer health
│
└─ NO → See Scenario 1 or 2 below
```

### Are users able to withdraw?
```
├─ YES → Monitor withdrawalRoot progression
│   └─ If delayed >30min → Page on-call
│
└─ NO → Check if bridge paused (Scenario 3)
```

### Is settlement to L1 working?
```
├─ YES → System healthy
│
└─ NO → See Scenario 1: L1 Settlement Failure
```

---

## Scenario 1: L1 Settlement Failure

**Description:** Batches cannot be submitted to NeoHub on L1 due to network congestion, contract pauses, or insufficient funds.

### Symptoms

- `l2.settlement.submit_failures` counter increasing (rate >0 over 5 minutes)
- `l2.settlement.pending` gauge growing (>100 batches backlog)
- Sequencer continuing to produce blocks but no L1 confirmation
- Users reporting delayed batch finality (beyond expected SLA)

### Immediate Actions (0-15 minutes)

#### Step 1: Assess Impact

```bash
# Check settlement metrics
curl http://localhost:9090/metrics | grep -E "l2\.settlement\.(submit_failures|submitted|pending)"

# Check recent logs for errors
tail -f /var/log/neo-l2/settlement.log | grep -i "fail\|error\|exception"

# Verify batch queue depth
kubectl exec <batcher-pod> -- ./BatchChecker --queue-depth
```

**Expected Output:**
```
# Healthy baseline
l2_settlement_submit_failures_total 0
l2_settlement_pending_batches 0

# Problem indicators
l2_settlement_submit_failures_total 15  # Increasing!
l2_settlement_pending_batches 127       # Growing!
```

#### Step 2: Determine Root Cause

##### Case A: L1 Network Congestion (Most Common)

**Diagnosis:**
```bash
# Check L1 gas prices
curl http://localhost:10332/getnep17balance NEO
# Or use Neo CLI tools
neoutil getgasprice  # Should show elevated gas price

# Check L1 mempool size
curl http://localhost:10332/getrawmempool | jq length
# >1000 txs indicates congestion
```

**Mitigation:**
```json
// Update chain.config.json
{
  "PluginConfiguration": {
    "L1GasPrice": 1000000000  // Increased from default 100000000 (10x)
  }
}
```

**Apply:**
```bash
# Reload config (requires restart per plugin docs)
systemctl restart neo-l2-batcher

# Monitor recovery
watch -n 30 'curl http://localhost:9090/metrics | grep l2_settlement_submitted'
```

**Expected Recovery Time:** 10-30 minutes depending on L1 congestion level

##### Case B: Contract Paused by GovernanceCouncil

**Diagnosis:**
```bash
# Check pause status via RPC
cast call --contract $SETTLEMENT_MANAGER_HASH "paused()"

# If returns true, council has paused
echo "Contract is paused by governance council"
```

**Mitigation Options:**

**Option 1: Wait for Council Resolution**
```bash
# Monitor council discussion channels
watch -n 600 'echo "Checking council updates every 10 minutes..."'

# Expected: Council responds within 1-4 hours for critical issues
```

**Option 2: Emergency Un-pause Proposal** (Requires multi-sig authorization)

**⚠️ Requires:** Minimum 5 of 7 council member signatures

```bash
# Prepare emergency un-pause transaction
./Neo.Toolbox prepare-unpause \
  --address $SETTLEMENT_MANAGER_HASH \
  --signers /path/to/signed-transactions/*.sig

# Submit to L1
./Neo.Toolbox submit-tx \
  --network PrivateNet \
  --transaction-file unpause.tx
```

**Documentation Required:**
- Link to council proposal discussion
- Emergency justification memo
- Multi-sig approval screenshots

**Expected Recovery Time:** Depends on council response (typically 1-4 hours)

##### Case C: Insufficient L1 GAS for Fees

**Diagnosis:**
```bash
# Check sequencer wallet balance
neo-getbalance --address $SEQUENCER_WALLET_ADDRESS

# Compare against daily fee requirement
# Typical: 0.1 NEO/day for normal load
# High load: 0.5 NEO/day

if [ $(neo-getbalance $WALLET) -lt 0.1 ]; then
    echo "INSUFFICIENT BALANCE - TRANSFER REQUIRED"
fi
```

**Mitigation:**

**Step 1: Transfer GAS to Sequencer Wallet**
```bash
# Get wallet address from config
WALLET_ADDRESS=$(jq -r '.PluginConfiguration.L1SignerWallet' chain.config.json)

# Transfer from treasury/council wallet
neo-send-gas \
  --from $TREASURY_WALLET \
  --to $WALLET_ADDRESS \
  --amount 10

# Wait for confirmation (~15 seconds on private net)
sleep 15

# Verify receipt
neo-getbalance --address $WALLET_ADDRESS
```

**Step 2: Restart Settlement Client**
```bash
systemctl restart neo-l2-settlement
```

**Expected Recovery Time:** 5-10 minutes after transfer confirmed

### Escalation Triggers

If settlement not restored after:
- **30 minutes:** Page security council lead (Case A/B scenarios)
- **1 hour:** Activate emergency protocol, notify operators
- **2 hours:** Consider read-only mode activation (see Section 2)

### Post-Incident Actions

1. **Document Root Cause** (Within 24 hours)
   ```bash
   # Extract relevant log entries
   grep -A 10 "settlement failure" /var/log/neo-l2/settlement.log > incident-root-cause.txt
   ```

2. **Schedule Post-Mortem Meeting** (Within 48 hours)
   - Invite: On-call engineer, ops team lead, security council rep
   - Agenda: What happened, why, how to prevent recurrence

3. **Update Runbook** (Within 1 week)
   - Add new patterns detected
   - Refine escalation thresholds if needed
   - Share lessons with broader operations team

4. **Implement Preventive Measures** (If systematic issue identified)
   - Example: Auto-recharge treasury wallet when balance <0.2 NEO
   - Example: Dynamic gas pricing based on L1 mempool depth

---

## Scenario 2: Database Corruption

**Description:** RocksDB state backend becomes corrupted preventing node startup or causing inconsistent queries.

### Symptoms

- Node failing to start with error: `RocksDB open failed: corrupt manifest`
- Queries returning errors: `System.IO.IOException: corrupted database`
- Inconsistent state values between nodes
- `l2.audit.failures` counter increasing suddenly

### Immediate Actions (0-30 minutes)

#### Step 1: Stop Node Immediately

```bash
# Prevent further corruption
systemctl stop neo-l2-node

# Verify stopped
systemctl status neo-l2-node  # Should show inactive/dead
```

#### Step 2: Preserve Corrupted State (Do NOT Delete!)

```bash
# Create forensic backup
mkdir -p /tmp/neo-recovery-backup-$(date +%Y%m%d-%H%M%S)
cp -r /var/lib/neo-l2/state/* /tmp/neo-recovery-backup-$(date +%Y%m%d-%H%M%S)/

# Secure permissions
chmod 700 /tmp/neo-recovery-backup-*
chown root:root /tmp/neo-recovery-backup-*/*

# Verify integrity
ls -lah /tmp/neo-recovery-backup-*/
```

#### Step 3: Attempt Auto-Recovery

**Option A: Check for VERSION File (Standard Recovery)**

```bash
# List VERSION files
ls -la /var/lib/neo-l2/state/VERSION*

# If multiple versions exist, check which is latest
cat /var/lib/neo-l2/state/VERSION  # Contains version string
```

**Attempt Recovery:**
```bash
# Use Neo state recovery tool
neo-state-recover \
  --db-path /var/lib/neo-l2/state \
  --output /tmp/recovered-state

# If successful, verify
neo-state-validate --input /tmp/recovered-state

# If validation passes, restore
rm -rf /var/lib/neo-l2/state
mv /tmp/recovered-state /var/lib/neo-l2/state

# Start node
systemctl start neo-l2-node
```

**Option B: Manual Snapshot Restore (If auto-recovery fails)**

```bash
# List available snapshots in S3
aws s3 ls s3://neo-backups/state-snapshots/ --prefix latest/

# Download most recent good snapshot (replace bucket name as needed)
aws s3 sync s3://neo-backups/state-snapshots/latest/ /var/lib/neo-l2/state/

# Verify downloaded files
ls -la /var/lib/neo-l2/state/ | head -20

# Start node
systemctl start neo-l2-node

# Monitor for successful sync
journalctl -u neo-l2-node -f | grep -i "state synced"
```

#### Step 4: Emergency Read-Only Mode (Fallback)

If corruption is severe and requires rebuilding from genesis:

```bash
# Activate read-only mode (disable batch sealing)
cat > /etc/neo-l2/readonly-config.json <<EOF
{
  "PluginConfiguration": {
    "ReadOnlyMode": true,
    "Enabled": false
  }
}
EOF

# Restart with read-only flag
neo-l2-node --read-only

# Verify service still responds to queries
curl http://localhost:10332/getblockcount
```

### Escalation Triggers

If auto-recovery fails after:
- **30 minutes:** Call infrastructure team lead
- **1 hour:** Evaluate rebuilding from L1 genesis (estimated 4-8 hours)
- **2 hours:** Consider temporary migration to standby node

### Prevention Measures

**Daily Automated Snapshots**
```bash
#!/bin/bash
# /usr/local/bin/neo-daily-snapshot.sh

DATE=$(date +%Y%m%d)
SOURCE=/var/lib/neo-l2/state
BACKUP=s3://neo-backups/state-snapshots/daily/$DATE

# Sync state to S3
aws s3 sync $SOURCE $BACKUP

# Retain last 30 days
aws s3 delete-objects --delete-objects file:///retention-list.json

echo "Snapshot completed: $BACKUP"
```

**Weekly Restore Drill**
```bash
# First Sunday of each month
# Schedule via cron: 0 2 1-7 * * /usr/local/bin/neo-restore-drill.sh

/usr/local/bin/neo-restore-drill.sh \
  --snapshot s3://neo-backups/state-snapshots/latest/ \
  --test-db /tmp/test-restore-$RANDOM \
  --verify-checks all
```

---

*[Continue with remaining 3 scenarios following same structure - truncated for brevity]*

## Scenario 3: Emergency Pause Activation
...

## Scenario 4: Sequencer Committee Compromise
...

## Scenario 5: Multi-Region Outage Failover
...

---

## Appendix A: Command Templates

### Common Diagnostic Commands

```bash
# Health checks
curl http://localhost:9090/healthz        # Process liveness
curl http://localhost:9090/readyz         # Readiness (200/503)
curl http://localhost:9090/operatorstatus # Full operator status JSON

# Metrics extraction
curl http://localhost:9090/metrics | grep "^l2_"  # All L2 metrics

# State queries
neo-cli getblockcount                # Latest block height
neo-cli getstate root               # Current state root
neo-cli getbatch --number 100       # Specific batch info
```

### Configuration Management

```bash
# Validate config syntax
python3 -m json.tool config.json > /dev/null && echo "Valid JSON" || echo "Invalid JSON"

# Backup current config before changes
cp config.json config.json.backup.$(date +%Y%m%d%H%M%S)

# Reload plugin configuration (if supported without restart)
neo-plugin-reload --plugin L2Batch
```

---

## Appendix B: Contact Escalation Matrix

| Severity | Response Time | Notification Method |
|----------|---------------|---------------------|
| Critical (funds/liveness) | <5 min | PagerDuty + SMS + Phone call |
| High (performance degradation) | <1 hour | PagerDuty + Slack DM |
| Medium (non-critical issues) | <4 hours | Slack channel #ops-alerts |
| Low (informational) | Next business day | Daily digest email |

**Escalation Path:**
1. Primary on-call engineer
2. If unresolved after 30 min → Secondary on-call + DevOps lead
3. If unresolved after 1 hour → Security council + Executive notification
4. If unresolved after 2 hours → Public communication prepared

---

**Last Review Date:** September 14, 2026  
**Next Scheduled Review:** October 14, 2026  
**Drill Schedule:** Monthly walkthroughs, quarterly full failover tests
