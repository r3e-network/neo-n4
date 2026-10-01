# Neo N4 Security Incident Response Plan

## Document Control

| Version | Date | Author | Status |
|---------|------|--------|--------|
| 1.0 | 2026-10-01 | Security Team | Active |

**Classification:** Internal (share with partners/auditors as needed)  
**Review Cadence:** Quarterly or after any major incident  
**Owner:** Neo N4 Security Council

---

## Purpose

This document defines the procedures for identifying, responding to, and recovering from security incidents affecting the Neo N4 Layer 2 Rollup system. The goal is to minimize impact, preserve evidence, communicate effectively, and prevent recurrence.

---

## Incident Severity Classification

### P0 - Critical (Red Alert)

**Active exploitation with immediate financial or operational impact**

Examples:
- Funds being actively stolen from bridge
- Chain halted and users cannot access funds
- Private keys compromised
- Smart contract exploit draining user assets
- Zero-day vulnerability under active attack

**Response Time:** Immediate (activate war room within 15 minutes)  
**Escalation:** All hands, external security partners, coordinate with exchanges

### P1 - High (Orange Alert)

**Severe vulnerability discovered or incident with contained impact**

Examples:
- Critical vulnerability discovered but not yet exploited
- Partial service degradation affecting >50% users
- Governance attack detected but not successful
- Data breach exposing non-financial user information
- Attempted but failed attack on critical infrastructure

**Response Time:** Within 1 hour  
**Escalation:** Security team + on-call engineers + management

### P2 - Medium (Yellow Alert)

**Security issue with moderate impact or risk**

Examples:
- Non-critical vulnerability discovered
- Performance degradation from suspected malicious activity
- Suspicious transaction patterns detected
- Minor smart contract bug with no immediate exploit path
- Phishing campaign targeting users

**Response Time:** Within 4 hours  
**Escalation:** Security team + relevant domain experts

### P3 - Low (Blue Notice)

**Minor security concern or policy violation**

Examples:
- Outdated dependencies with CVEs
- Configuration drift from hardening baseline
- Policy violations (access control, logging)
- Suspicious but benign activity
- False positive alerts

**Response Time:** Within 24 hours  
**Escalation:** Security team tracks and resolves during business hours

---

## Incident Response Team (IRT)

### Core Team (Always Activated for P0/P1)

| Role | Responsibilities | Contact |
|------|------------------|---------|
| **Incident Commander** | Overall coordination, decision authority | On-call rotation |
| **Security Lead** | Technical analysis, containment strategy | security@neo-n4.network |
| **Engineering Lead** | System operations, fix deployment | ops@neo-n4.network |
| **Communications Lead** | Internal/external communications | comms@neo-n4.network |
| **Legal Counsel** | Regulatory compliance, legal implications | legal@neo-n4.network |

### Extended Team (Activated as Needed)

- Smart Contract Developers
- zkVM/Cryptography Experts
- External Security Auditors (Incident Response Retainer)
- Blockchain Forensics Team
- Exchange Liaison (for trading halts)
- Insurance Provider (for claims)

### War Room Channels

- **Slack:** #incident-response-active (private)
- **Video:** Zoom emergency bridge (link in runbook)
- **Docs:** Google Drive incident folder (restricted)
- **Status Page:** https://status.neo-n4.network

---

## Response Phases

## Phase 1: Detection & Triage (0-15 minutes)

### 1.1 Detection Sources

Incidents may be detected via:
- Automated monitoring alerts (Grafana, Prometheus)
- Bug bounty submission
- User reports (Discord, support tickets)
- Security audit findings
- External notification (exchange, researcher, media)
- Blockchain anomaly detection

### 1.2 Initial Assessment

**First Responder Actions:**
```bash
# 1. Capture snapshot of current state
date > /tmp/incident-$(date +%s).log
curl http://monitoring.neo-n4.network/metrics >> /tmp/incident-*.log

# 2. Check critical systems
systemctl status neo-l2-sequencer neo-l2-settlement
curl http://localhost:10332/getblockcount

# 3. Review recent alerts
journalctl -u neo-l2-* --since "1 hour ago" | grep -i "error\|critical"

# 4. Notify on-call
pagerduty-trigger --severity critical --message "Potential incident detected"
```

**Triage Checklist:**
- [ ] What is the observable behavior?
- [ ] Which systems/contracts are affected?
- [ ] Are user funds at risk?
- [ ] Is there active exploitation?
- [ ] What is the estimated severity (P0-P3)?
- [ ] Has this triggered automated safeguards?

### 1.3 Escalation Decision

**If P0/P1:**
1. Activate incident commander via PagerDuty
2. Post in #incident-response-active: "@channel P0/P1 incident declared"
3. Start war room video call
4. Notify management and security council within 15 minutes

**If P2/P3:**
1. Create incident ticket in issue tracker
2. Notify security team via Slack
3. Schedule sync within response SLA

---

## Phase 2: Containment (15 minutes - 4 hours)

### 2.1 Immediate Containment (P0 Only)

**Emergency Pause Activation**

If funds are actively at risk:

```bash
# Option 1: Emergency pause via GovernanceController (requires multi-sig)
./emergency-pause.sh --target RollupHub --reason "Active exploit CVE-2026-XXXXX"

# Option 2: Sequencer halt (faster, single-sig)
systemctl stop neo-l2-sequencer
neo-cli broadcast-halt-message --chain-id 1 --reason "Emergency maintenance"

# Option 3: SharedBridge pause (prevents deposits/withdrawals)
./multi-sig-tx.sh --contract SharedBridge --method Pause --signers 5of7
```

**Coordinate with Exchanges:**
```
Subject: URGENT - Neo N4 Trading Halt Request
Body:
We are experiencing a critical security incident affecting Neo N4 L2.
Request immediate trading halt for:
- NEO (if L1 affected)
- All N4-wrapped tokens
- N4 ecosystem tokens

Estimated resolution: [X] hours
Contact: security@neo-n4.network | +1-XXX-XXX-XXXX
```

### 2.2 Evidence Preservation

**Before making changes, capture forensic data:**

```bash
# Full system snapshot
tar -czf /forensics/snapshot-$(date +%s).tar.gz \
    /var/lib/neo-l2/state \
    /var/log/neo-l2 \
    /etc/neo-l2

# Smart contract state snapshot
neo-snapshot export --height current --output /forensics/contract-state.json

# Network traffic capture (if attack is ongoing)
tcpdump -i eth0 -w /forensics/traffic-$(date +%s).pcap port 10332 &

# Process memory dumps (if malware suspected)
gcore $(pgrep neo-l2-sequencer)
```

**Chain of custody:**
- Hash all evidence files: `sha256sum * > checksums.txt`
- Sign checksums: `gpg --sign checksums.txt`
- Upload to secure bucket: `aws s3 cp --recursive /forensics s3://neo-incident-evidence/`
- Document who accessed what when

### 2.3 Short-term Containment

**For Smart Contract Exploits:**
1. Pause affected contracts (if pause mechanism exists)
2. Deploy emergency withdrawal mechanism if needed
3. Coordinate with L1 validators to reject malicious transactions
4. Monitor for copycat attacks on similar patterns

**For Infrastructure Compromise:**
1. Isolate affected servers from network
2. Rotate compromised credentials immediately
3. Enable IP allowlisting for critical services
4. Deploy IDS/IPS rules to block attack traffic

**For Social Engineering/Phishing:**
1. Report phishing domains to registrars
2. Post public warning on official channels
3. Coordinate with wallet providers to show warnings
4. Monitor for credential reuse attempts

---

## Phase 3: Eradication (4-48 hours)

### 3.1 Root Cause Analysis

**Incident Analysis Template:**

```markdown
## Incident Summary
- **Date/Time:** 
- **Duration:**
- **Affected Systems:**
- **Impact:**

## Timeline of Events
- T-00:00 - Initial detection
- T+00:15 - Containment activated
- T+01:00 - Root cause identified
- ...

## Root Cause
[Detailed technical analysis]

## Attack Vector
[How did the attacker gain access/exploit the vulnerability?]

## Lessons Learned
[What should have prevented this?]
```

### 3.2 Fix Development

**For Smart Contract Bugs:**
```bash
# 1. Develop fix in isolated branch
git checkout -b hotfix/CVE-2026-XXXXX

# 2. Add regression test
cat > tests/CVE-2026-XXXXX.cs << 'EOF'
[TestClass]
public class CVE2026XXXXXTests
{
    [TestMethod]
    public void TestExploitPrevented() { ... }
}
EOF

# 3. Run formal verification
cd scripts/formal && python verify_fixed_contract.py

# 4. External audit review (if P0)
send-to-auditor --contract RollupHub --diff main..hotfix/CVE-2026-XXXXX

# 5. Deploy to testnet first
deploy-testnet --contract RollupHub --branch hotfix/CVE-2026-XXXXX
```

**For Infrastructure Issues:**
1. Patch systems via ansible/terraform
2. Rebuild compromised servers from clean images
3. Restore from last known-good backup if needed
4. Update firewall rules, access controls

### 3.3 Testing & Validation

**Pre-deployment Checklist:**
- [ ] Fix tested on local devnet
- [ ] Fix tested on public testnet
- [ ] Formal verification passed (if applicable)
- [ ] External auditor reviewed (for P0/P1)
- [ ] Rollback plan documented
- [ ] Monitoring alerts configured for fix
- [ ] Post-deployment validation scripts ready

---

## Phase 4: Recovery (2-7 days)

### 4.1 Deployment

**Mainnet Deployment Procedure:**

```bash
# 1. Announce maintenance window (4 hours before)
post-announcement --channel all --message "Scheduled maintenance in 4 hours"

# 2. Snapshot state before deployment
neo-snapshot export --output pre-deployment-snapshot.json

# 3. Deploy fix with multi-sig
multi-sig-deploy --contract RollupHub --bytecode ./build/RollupHub.nef --signers 5of7

# 4. Verify deployment
verify-contract-hash --expected 0x1234... --actual $(get-deployed-hash RollupHub)

# 5. Resume services gradually
systemctl start neo-l2-sequencer
# Wait 10 minutes, monitor
systemctl start neo-l2-settlement
# Wait 10 minutes, monitor
unpause-contracts --all
```

### 4.2 Service Restoration

**Gradual Resumption:**
1. **Hour 0-1:** Internal testing, sequencer producing blocks
2. **Hour 1-2:** Enable deposits (small amounts only)
3. **Hour 2-4:** Enable withdrawals (with rate limits)
4. **Hour 4-8:** Increase rate limits progressively
5. **Hour 8-24:** Full service resumed
6. **Day 1-7:** Enhanced monitoring period

**Validation Checks:**
```bash
# Every 30 minutes for first 24 hours
./health-check.sh --full
./integrity-check.sh --compare-snapshot pre-deployment-snapshot.json
./alert-check.sh --ensure-no-new-anomalies
```

### 4.3 User Communication

**Status Page Updates:**
```
[RESOLVED] - Critical Security Incident - Oct 1, 2026

17:45 UTC - Services fully restored. All systems operational.
15:30 UTC - Hotfix deployed to mainnet. Gradual service resumption beginning.
12:00 UTC - Root cause identified. Fix developed and tested.
09:15 UTC - Incident detected. Emergency pause activated.

Summary: [Brief non-technical description]
Impact: [How many users? How much value?]
Resolution: [What was fixed?]
Next Steps: [What we're doing to prevent recurrence]

Full post-mortem will be published within 7 days.
```

**User Support:**
- Dedicated support channel for affected users
- FAQ document addressing common concerns
- Compensation policy (if applicable)

---

## Phase 5: Post-Incident (7-30 days)

### 5.1 Post-Mortem Report

**Public Post-Mortem Template:**

```markdown
# Neo N4 Incident Post-Mortem - [Date]

## Executive Summary
[2-3 sentences for non-technical audience]

## What Happened
[Detailed timeline]

## Impact
- Users affected: X
- Funds at risk: $Y
- Downtime: Z hours
- Actual losses: $N (if any)

## Root Cause
[Technical deep-dive]

## What Went Well
- Fast detection (T+15min)
- Effective containment
- Good communication

## What Went Wrong
- Late deployment of monitoring
- Manual process failed
- Insufficient testing

## Action Items
1. [Preventive measure] - Owner: [Name] - Due: [Date]
2. [Detective measure] - Owner: [Name] - Due: [Date]
3. [Corrective measure] - Owner: [Name] - Due: [Date]

## Timeline
[Detailed minute-by-minute for first 4 hours]

## Appendix
[Technical details, logs, code diffs]
```

### 5.2 Follow-Up Actions

**Immediate (Week 1):**
- [ ] Deploy additional monitoring for this attack class
- [ ] Update runbooks with lessons learned
- [ ] Compensate affected users (if policy requires)
- [ ] Brief insurance provider
- [ ] File regulatory notifications (if required)

**Short-term (Month 1):**
- [ ] Conduct security audit focused on similar vulnerabilities
- [ ] Update threat model
- [ ] Run tabletop exercise simulating similar incident
- [ ] Train team on new procedures

**Long-term (Quarter 1):**
- [ ] Architectural changes to prevent class of bugs
- [ ] Formal verification of related components
- [ ] Update bug bounty program scope
- [ ] Review and update this IRP

### 5.3 Compensation Policy

**Automatic Compensation (if user funds affected):**
- 100% principal restored from insurance/treasury
- Plus: Gas fees incurred during incident
- Plus: Opportunity cost (pro-rated interest)
- Plus: Goodwill gesture (discretionary)

**Claims Process:**
```
1. User submits claim with proof of loss
2. Security team validates claim
3. Payment processed within 30 days
4. Insurance claim filed (if policy covers)
```

---

## Playbooks (Quick Reference)

### Playbook A: Smart Contract Exploit

```
1. EMERGENCY PAUSE contract immediately
2. Snapshot chain state (evidence)
3. Identify exploit transaction hash
4. Analyze contract bytecode + transaction trace
5. Develop and test patch
6. Audit patch (if P0)
7. Deploy to testnet
8. Deploy to mainnet with multi-sig
9. Monitor for copycat attacks
10. Post-mortem + compensation
```

### Playbook B: Private Key Compromise

```
1. IMMEDIATELY rotate compromised keys
2. Revoke access for compromised accounts
3. Review all transactions from compromised keys
4. Identify stolen funds, coordinate recovery
5. Enable MFA/HSM for all critical keys
6. Audit all other keys for potential exposure
7. Update access control policies
8. Forensic analysis of how keys were stolen
9. Report to law enforcement (if criminal)
10. Post-mortem + policy updates
```

### Playbook C: Chain Halt / Liveness Failure

```
1. Check sequencer status
2. Check L1 connectivity
3. Check DA layer availability
4. Restart services in order: DA -> Sequencer -> Settlement
5. If restart fails, rollback to last known-good state
6. Investigate root cause (OOM? Bug? Attack?)
7. Deploy fix if needed
8. Resume batch production
9. Verify finalization working
10. Extended monitoring period
```

### Playbook D: Data Breach

```
1. Identify scope: what data was accessed?
2. Contain: block attacker access immediately
3. Preserve evidence for forensics
4. Legal assessment: notification requirements?
5. Notify affected users within 72 hours (GDPR)
6. Offer credit monitoring (if PII exposed)
7. Review access controls
8. Penetration test to find similar vulnerabilities
9. Update privacy policy if needed
10. Report to regulators (if required)
```

---

## Training & Drills

### Quarterly Tabletop Exercises

**Scenario Examples:**
1. Bridge exploit draining $10M
2. Sequencer compromise publishing invalid batches
3. zkVM vulnerability allowing fake proofs
4. Multi-sig key theft
5. Coordinated social engineering attack

**Drill Format:**
- 90-minute facilitated discussion
- All IRT members participate
- Walk through each response phase
- Document gaps and improvements
- Update runbooks afterwards

### Annual Full-Scale Drill

- Red team vs Blue team exercise
- Simulate real attack on testnet
- Test entire IRP end-to-end
- Measure response times
- Validate communication channels
- External observers/auditors grade performance

---

## Contact Information

### Internal Emergency Contacts

| Role | Primary | Backup | Phone |
|------|---------|--------|-------|
| Incident Commander | [Name] | [Name] | +1-XXX-XXX-XXXX |
| Security Lead | [Name] | [Name] | +1-XXX-XXX-XXXX |
| Engineering Lead | [Name] | [Name] | +1-XXX-XXX-XXXX |
| Legal | [Name] | [Name] | +1-XXX-XXX-XXXX |

### External Partners

- **Security Auditor (Incident Retainer):** [Firm Name] - incident@auditor.com
- **Blockchain Forensics:** [Firm Name] - emergency@forensics.com
- **Cyber Insurance:** [Provider] - Policy #XXXXX - claims@insurance.com
- **Legal Counsel:** [Firm Name] - emergency@lawfirm.com
- **PR Firm:** [Name] - crisis@prfirm.com

### Regulatory Contacts

- **[Relevant regulator]:** compliance@regulator.gov
- **Law Enforcement:** [Cybercrime unit contact]

---

## Appendix A: Communication Templates

### Template 1: Initial Incident Notification (Internal)

```
Subject: [P0/P1] Security Incident Declared

Team,

A [P0/P1] security incident has been declared at [TIME].

Summary: [One sentence description]
Impact: [Affected systems/users]
Status: [Containment in progress / Under investigation]

War Room: [Zoom link]
Slack: #incident-response-active

All hands required. Drop current work and join immediately.

- Incident Commander
```

### Template 2: Public Status Update

```
We are currently investigating a [service disruption / security issue] 
affecting Neo N4 Layer 2. Our team is actively working on resolution.

Current status: [Services paused / Limited functionality / Under maintenance]
Estimated resolution: [X hours / Ongoing investigation]
User funds: [Safe / At risk - details below]

We will provide updates every [30 minutes / 1 hour] on our status page:
https://status.neo-n4.network

For urgent inquiries: support@neo-n4.network
```

### Template 3: Post-Resolution Announcement

```
Subject: Neo N4 Service Restored - Incident Summary

The security incident affecting Neo N4 has been resolved. All services
are now operational.

What happened: [Brief description]
Timeline: [Detection to resolution]
Impact: [Users/funds affected]
Resolution: [What we fixed]
Compensation: [If applicable]

We take security extremely seriously. A detailed post-mortem will be
published within 7 days at: https://neo-n4.network/security/incidents

Thank you for your patience and continued trust.
```

---

## Appendix B: Tools & Scripts

### Monitoring Dashboard

```bash
# Launch incident response dashboard
docker run -d -p 3000:3000 neo-n4/incident-dashboard
# Opens comprehensive view:
# - Real-time metrics
# - Recent alerts
# - Transaction anomalies
# - System health
```

### Evidence Collection Script

```bash
#!/bin/bash
# /usr/local/bin/collect-evidence.sh
INCIDENT_ID=$1
EVIDENCE_DIR="/forensics/$INCIDENT_ID"
mkdir -p "$EVIDENCE_DIR"

# System state
neo-cli export-state > "$EVIDENCE_DIR/chain-state.json"
docker ps -a > "$EVIDENCE_DIR/containers.txt"
systemctl list-units > "$EVIDENCE_DIR/services.txt"

# Logs
journalctl --since "24 hours ago" > "$EVIDENCE_DIR/system-logs.txt"
cp -r /var/log/neo-l2 "$EVIDENCE_DIR/app-logs/"

# Network
ss -tulpn > "$EVIDENCE_DIR/network-connections.txt"
iptables -L -n > "$EVIDENCE_DIR/firewall-rules.txt"

# Hash everything
sha256sum "$EVIDENCE_DIR"/* > "$EVIDENCE_DIR/checksums.txt"
gpg --sign "$EVIDENCE_DIR/checksums.txt"

echo "Evidence collected in $EVIDENCE_DIR"
```

---

**Document Version:** 1.0  
**Effective Date:** October 1, 2026  
**Next Review:** January 1, 2027  
**Approval:** [Security Council Chair]

*This is a living document. Suggest improvements via: security@neo-n4.network*