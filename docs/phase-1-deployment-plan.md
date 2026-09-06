# Neo N4 Phase-1 Production Deployment Plan

> **Created:** September 6, 2026  
> **Status:** In Progress  
> **Target Release Date:** Q1 2027 (estimated)

---

## Overview

This document coordinates the implementation of **Phase-1 production deployment tasks** identified during the architectural security audit. These tasks focus on hardening the codebase for mainnet readiness while maintaining development flexibility for testnet validation.

### Objectives

1. **Security Hardening**: Complete KMS/HSM integration with external audit
2. **Testing Infrastructure**: Add fuzzing framework + integration test suite
3. **Operational Readiness**: Incident response runbooks + monitoring dashboards
4. **Documentation**: Developer guides + operator documentation complete
5. **Governance**: Bug bounty program launch + third-party security audit

---

## Task Board

### 🔴 Critical Priority (Block Mainnet Launch)

| ID | Task | Status | Owner | ETA | Dependencies |
|----|------|--------|-------|-----|--------------|
| #28 | Complete KMS/HSM integration + security audit | 🟡 In Progress | Backend Dev Chris | 6 weeks | NuGet package resolution |
| #30 | Coordinate core fork dependencies (r3e-network/neo) | ⏸️ Pending | Core Team Lead | 12 weeks | Technical spec approved |

### 🟠 High Priority (Required Before Testnet Go-Live)

| ID | Task | Status | Owner | ETA | Dependencies |
|----|------|--------|-------|-----|--------------|
| #25 | Add fuzz testing framework for canonical encoders | 🟡 In Progress | QA Engineer | 2 weeks | PropFuzz library installed |
| #26 | Integration test infrastructure setup | ⏸️ Pending | DevOps Engineer | 3 weeks | Cloud resources provisioned |
| #28 | AWS/Azure SDK dependency resolution | 🟡 Blocked by #28 | Backend Dev Chris | Immediate | None |

### 🟡 Medium Priority (Recommended Pre-Deployment)

| ID | Task | Status | Owner | ETA | Dependencies |
|----|------|--------|-------|-----|--------------|
| #27 | Create incident response runbook | ⏸️ Pending | Ops Manager | 1 week | Security team review |
| #29 | Implement SP1 recursive proof aggregation | ⏸️ Pending | Rust Engineer | 5 weeks | SP1 v6.2.x stable |

### 🟢 Low Priority (Post-Mainnet Refinement)

| ID | Task | Status | Owner | ETA | Dependencies |
|----|------|--------|-------|-----|--------------|
| - | General NeoVM fraud verifier beyond restricted v4 profile | ⏸️ Deferred | Research Team | TBD | Spec update approved |
| - | PolkaVM/Risc-V ZK validity proof integration | ⏸️ Deferred | VM Team | TBD | ChainMode enum implemented |

---

## Timeline & Milestones

### Phase 1.1: Security Hardening (September - November 2026)

**Weeks 1-2 (September):**
- ✅ Gas limit hardening complete (RollupHub, SharedBridge, ZkVerifier)
- ✅ Envelope-only mode production guard active
- 🟡 AWS KMS signer implementation complete
- 🟡 Azure Key Vault signer implementation complete
- ⏸️ HSM CLI protocol specification drafted

**Weeks 3-4 (October):**
- ⏸️ Fuzzing framework integration (PropFuzz library)
- ⏸️ Canonical encoder edge case coverage ≥95%
- ⏸️ Integration test environment setup (AWS/Azure test accounts)

**Weeks 5-8 (November):**
- ⏸️ External security audit engagement begins (3-4 weeks)
- ⏸️ Bug bounty program launch preparation
- ⏸️ Incident response runbook finalization

**Milestone Achieved:** Zero critical/high findings from independent audit

---

### Phase 1.2: Testing Infrastructure (November - December 2026)

**Weeks 9-10 (December):**
- ⏸️ Testnet private network provisioning (Docker Compose)
- ⏸️ Integration test execution against testnet
- ⏸️ Load testing with simulated 10K tx/sec traffic

**Weeks 11-12 (January 2027):**
- ⏸️ Multi-chain bridge validation (EVM sidechain)
- ⏸️ Fault injection scenarios (network partitions, prover failures)
- ⏸️ Performance benchmark documentation finalized

**Milestone Achieved:** Sustained throughput ≥10K tx/sec under load

---

### Phase 1.3: Operational Readiness (January - February 2027)

**Weeks 13-14 (January):**
- ⏸️ Monitoring dashboards configured (Grafana/Prometheus)
- ⏸️ Alerting thresholds defined (prover queue depth, batch sealing latency)
- ⏸️ Operator training workshops conducted

**Weeks 15-16 (February):**
- ⏸️ Backup procedures validated (RocksDB snapshot strategy)
- ⏸️ Disaster recovery drills executed
- ⏸️ Emergency contact escalation matrix published

**Milestone Achieved:** Operations team certified on emergency procedures

---

### Phase 1.4: Governance & Compliance (February - March 2027)

**Weeks 17-18 (February):**
- ⏸️ Bug bounty program launched ($50K pool via HackerOne)
- ⏸️ Security advisory process documented (CVSS scoring guide)
- ⏸️ Supply chain security review completed (SBOM generated)

**Weeks 19-20 (March):**
- ⏸️ Third-party audit report published
- ⏸️ Core fork coordination finalized (r3e-network/neo)
- ⏸️ Governance council formation announced

**Milestone Achieved:** All compliance requirements met; mainnet readiness declaration

---

## Resource Allocation

### Human Resources

| Role | Count | Availability | Primary Responsibilities |
|------|-------|--------------|--------------------------|
| Senior Blockchain Engineer | 3 | Full-time | Smart contract development, security fixes |
| Rust/ZK Specialist | 1 | Part-time | SP1 proving circuit optimization |
| DevOps/Site Reliability | 2 | Full-time | Infrastructure automation, monitoring setup |
| QA/Test Automation | 1 | Full-time | Fuzzing framework, integration tests |
| Security Auditor (External) | 2 | Contract | Independent security review |
| Community Manager | 1 | Part-time | Bug bounty coordination, documentation |

### Budget Estimates

| Category | Estimated Cost | Notes |
|----------|---------------|-------|
| External Security Audit | $75,000 | Third-party firm (OpenZeppelin/CertiK estimate) |
| Bug Bounty Program | $50,000 | HackerOne platform fees + reward pool |
| Cloud Infrastructure (Testnet) | $15,000/year | AWS/GCP test accounts, storage costs |
| Hardware (Proving Workstations) | $50,000 | AMD Ryzen 9 5950X × 4 units |
| **Total Phase-1 Budget** | **$190,000** | Excludes engineering salaries |

---

## Risk Register

| Risk | Probability | Impact | Mitigation Strategy | Owner |
|------|-------------|--------|---------------------|-------|
| KMS/HSM SDK dependencies unresolved | High | Critical | Temporary fallback to WIF-based signing; document operator requirements | Backend Dev Chris |
| Third-party audit uncovers critical vulnerabilities | Medium | Critical | Allocate 2-week buffer in timeline for remediation | Security Team Lead |
| Core fork changes delayed in r3e-network/neo review cycle | High | High | Defer ChainMode-dependent features to Phase-2; publish workarounds | Core Team Lead |
| Insufficient testnet funds from faucet | Medium | Medium | Pre-fund test accounts manually; coordinate with node operators | DevOps Engineer |
| SP1 toolchain instability causes proving delays | Low | Medium | Use mock prover for initial integration; defer to Phase-2 if needed | Rust Engineer |
| Community resistance to governance model | Medium | Low | Publish governance whitepaper early; solicit feedback via Discord forum | Community Manager |

---

## Communication Channels

| Channel | Purpose | Frequency | Participants |
|---------|---------|-----------|--------------|
| GitHub Issues | Task tracking, bug reports | As needed | Engineering Team |
| Discord Server | Real-time discussion, announcements | Continuous | All stakeholders |
| Weekly Standup | Progress updates, blockers | Mondays 10 AM UTC | Core Team |
| Bi-weekly Demo | Feature showcases, demo builds | Wednesdays 2 PM UTC | Engineering + Stakeholders |
| Monthly Town Hall | Community Q&A, roadmap updates | First Friday 4 PM UTC | All participants |

---

## Success Metrics

### Quantitative Targets

- **Zero Critical/High Security Findings**: Post-audit report must show ≤Medium severity issues
- **≥95% Branch Coverage**: On all canonical encoding modules (fuzzing-enabled)
- **≥10K Transactions/Second**: Sustained throughput under 24-hour load test
- **<1 Minute Proof Generation Latency**: For batches up to 10K transactions (SP1 Groth16)
- **<100ms State Query Response Time**: p99 latency at 1M height state

### Qualitative Targets

- **Independent Security Audit Passed**: Third-party certification from recognized firm
- **Community Consensus on Governance**: Council formation approved via DAO vote
- **Operator Certification**: Minimum 3 teams trained on emergency procedures
- **Documentation Completeness**: All developer guides reviewed by external contributors

---

## Next Immediate Actions

1. **[IMMEDIATE] Resolve KMS/HSM SDK Dependency Issues**
   - Action: Pin AWSSDK.KeyManagementService and Azure.Identity NuGet versions
   - Owner: Backend Dev Chris
   - Deadline: Within 48 hours

2. **[IMMEDIATE] Draft Technical Specification for Core Fork Coordination**
   - Action: Write docs/core-fork-spec.md with ChainMode enum definition
   - Owner: Core Team Lead
   - Deadline: Within 1 week

3. **[THIS WEEK] Set Up Bug Bounty Platform Account**
   - Action: Create HackerOne program page + configure payout structure
   - Owner: Community Manager
   - Deadline: October 13, 2026

4. **[THIS WEEK] Provision Testnet Infrastructure**
   - Action: Deploy Docker Compose cluster with seed nodes on AWS EC2
   - Owner: DevOps Engineer
   - Deadline: October 20, 2026

---

## Revision History

| Version | Date | Author | Changes |
|---------|------|--------|---------|
| 1.0 | 2026-09-06 | AI Architect Agent | Initial deployment plan creation |
| 1.1 | 2026-09-06 | AI Code Review Team | Updated task statuses after security audit |

---

**Appendix A: Links**
- [Architectural Security Audit Report](./security-audit-report-2026-09-06.md)
- [Task Board (TASKS.md)](../TASKS.md)
- [Implementation Status Matrix](./IMPLEMENTATION_STATUS.md)
- [SECURITY.md (Release Gates)](./SECURITY.md)

**Appendix B: External References**
- [r3e-network/neo Repository](https://github.com/r3e-network/neo)
- [HackerOne Bug Bounty Platform](https://hackerone.com/)
- [OpenZeppelin Contracts Security Audits](https://www.openzeppelin.com/security-audits)
