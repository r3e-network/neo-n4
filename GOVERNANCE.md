# Neo N4 Governance Framework

## Overview

The Neo N4 governance system balances **decentralization**, **security**, and **efficiency** through a hybrid model combining:
- **Security Council** - Fast decision-making for critical issues
- **DAO Governance** - Community participation for protocol evolution
- **On-Chain Voting** - Transparent, verifiable decision execution

**Status:** Active  
**Version:** 1.0  
**Last Updated:** October 1, 2026

---

## Table of Contents

1. [Governance Structure](#1-governance-structure)
2. [Proposal Process](#2-proposal-process)
3. [Voting Mechanisms](#3-voting-mechanisms)
4. [Upgrade Procedures](#4-upgrade-procedures)
5. [Emergency Actions](#5-emergency-actions)
6. [Treasury Management](#6-treasury-management)

---

## 1. Governance Structure

### 1.1 Security Council

**Purpose:** Handle time-sensitive security and operational decisions

**Composition:**
- 7 members (5-of-7 multi-sig required)
- Mix of: Core team (3), ecosystem projects (2), external experts (2)
- Term: 6 months (staggered rotation)

**Powers:**
- Emergency pause/unpause contracts
- Approve critical security patches
- Manage operational parameters within bounds
- Execute treasury spending <$100k
- Fast-track urgent proposals (48-hour voting)

**Limitations:**
- Cannot change core protocol rules
- Cannot access >10% of treasury without DAO approval
- All actions are transparent on-chain
- Can be overridden by DAO (7-day veto window)

**Current Members:** [To be announced post-launch]

### 1.2 DAO (Decentralized Autonomous Organization)

**Purpose:** Community-driven protocol evolution

**Membership:**
- Open to all NEO stakers (minimum 100 NEO)
- Voting power proportional to stake
- Delegation supported

**Powers:**
- Protocol upgrades and parameter changes
- Treasury allocation >$100k
- Security Council member election
- Constitution amendments
- Veto Security Council actions (within 7 days)

**Voting Requirements:**
- Quorum: 10% of total staked NEO
- Approval: Simple majority (50%+) or supermajority (66%+) depending on proposal type
- Voting period: 7 days (standard) or 14 days (major changes)

### 1.3 Core Development Team

**Role:** Implementation, maintenance, technical guidance

**Responsibilities:**
- Implement approved proposals
- Maintain infrastructure
- Provide technical analysis for proposals
- Security monitoring
- Developer relations

**Accountability:**
- Reports to DAO quarterly
- Budget approved by DAO annually
- No unilateral decision power (requires DAO approval)

---

## 2. Proposal Process

### 2.1 Proposal Types

#### Type A: Minor Parameter Changes
**Examples:** Adjust base fee target, batch size, rate limits  
**Requirements:**
- Quorum: 10%
- Approval: 50%+
- Duration: 7 days
- Threshold: None (anyone can propose)

#### Type B: Protocol Upgrades
**Examples:** New features, contract upgrades, consensus changes  
**Requirements:**
- Quorum: 15%
- Approval: 66%+
- Duration: 14 days
- Threshold: 10,000 NEO stake or Security Council sponsor

#### Type C: Treasury Allocation
**Examples:** Ecosystem grants, strategic investments  
**Requirements:**
- Quorum: 10% (if <$100k), 20% (if >$100k)
- Approval: 50%+ (<$100k), 66%+ (>$100k)
- Duration: 7-14 days
- Threshold: 1,000 NEO stake

#### Type D: Constitutional Changes
**Examples:** Governance structure, voting rules, Security Council powers  
**Requirements:**
- Quorum: 30%
- Approval: 75%+
- Duration: 21 days
- Threshold: Security Council + 50,000 NEO community stake

#### Type E: Emergency Actions
**Examples:** Critical security patches, circuit breakers  
**Requirements:**
- Security Council only
- 5-of-7 multi-sig
- Executed immediately
- DAO veto window: 7 days

### 2.2 Proposal Lifecycle

```
1. Discussion (Forum) → 2. Draft Proposal → 3. Review Period → 4. Voting → 5. Execution
     ↓ (2-7 days)            ↓ (48 hours)        ↓ (7-21 days)    ↓ (varies)
Temperature Check      Technical Review      On-Chain Vote    Timelock + Deploy
```

#### Stage 1: Community Discussion (Informal)
- **Duration:** Minimum 2 days (recommended 7 days)
- **Location:** https://forum.neo-n4.network
- **Goal:** Gauge community sentiment, refine idea
- **Output:** Rough consensus before formal proposal

#### Stage 2: Draft Proposal (Formal)
- **Duration:** 48 hours
- **Location:** GitHub + forum
- **Requirements:**
  - Clear title and summary
  - Detailed specification
  - Implementation plan
  - Cost analysis (if applicable)
  - Security considerations
  - Alternative approaches considered
- **Review:** Core team provides technical feedback

#### Stage 3: Review Period
- **Duration:** 2-7 days (depends on complexity)
- **Activities:**
  - Security audit (if code changes)
  - Economic impact analysis
  - Community feedback incorporation
  - Final revisions
- **Gate:** Must pass technical review before voting

#### Stage 4: On-Chain Voting
- **Duration:** 7-21 days (see proposal types)
- **Mechanism:** Snapshot voting or on-chain governance contract
- **Delegation:** Voters can delegate to representatives
- **Transparency:** All votes public and verifiable

#### Stage 5: Execution
- **Timelock:** 48-72 hours (gives Security Council veto window)
- **Deployment:** Automated via governance contract or manual (complex changes)
- **Monitoring:** 7-day enhanced monitoring period post-deployment
- **Rollback:** Available for 30 days if critical issues discovered

### 2.3 Proposal Template

```markdown
# [Type] Proposal Title

## Summary
[One paragraph - what and why]

## Motivation
[Problem statement - why is this needed?]

## Specification
[Detailed technical specification]

### Parameters
- Parameter A: Current value → Proposed value
- Parameter B: Current value → Proposed value

### Implementation Plan
1. Step 1
2. Step 2
3. Step 3

## Security Considerations
[Risks, attack vectors, mitigations]

## Economic Impact
- Revenue impact: [Estimate]
- Cost impact: [Estimate]
- User impact: [Description]

## Alternatives Considered
1. Alternative A - Pros/Cons
2. Alternative B - Pros/Cons

## Timeline
- Proposal date: YYYY-MM-DD
- Voting start: YYYY-MM-DD
- Voting end: YYYY-MM-DD
- Execution (if passed): YYYY-MM-DD

## References
[Links to related discussions, technical docs, etc.]
```

---

## 3. Voting Mechanisms

### 3.1 Voting Power Calculation

**Basic Formula:**
```
voting_power = staked_NEO × time_multiplier × delegation_factor
```

**Time Multiplier (Lock-up Bonus):**
- No lock: 1x
- 3-month lock: 1.25x
- 6-month lock: 1.5x
- 1-year lock: 2x

**Delegation:**
- Delegators retain 100% voting power
- Delegates can vote on behalf of delegators
- Delegators can override delegate votes
- Delegation is revocable anytime

**Example:**
```
Alice stakes 10,000 NEO with 6-month lock:
  voting_power = 10,000 × 1.5 = 15,000 votes

Bob stakes 20,000 NEO, no lock, delegates to Carol:
  Bob's voting_power = 20,000 × 1.0 = 20,000 votes
  Carol can cast 20,000 votes on Bob's behalf
```

### 3.2 Voting Options

**Standard Proposals:**
- ✅ Yes (support)
- ❌ No (oppose)
- 🤷 Abstain (counted for quorum, not for approval)

**Multi-Choice Proposals:**
- Option A
- Option B
- Option C
- Ranked choice voting supported

**Approval Voting (Multiple Options):**
- Voters can approve multiple options
- Option with most approvals wins (if quorum met)

### 3.3 Quorum and Approval Rules

| Proposal Type | Quorum | Approval Threshold |
|---------------|--------|-------------------|
| Minor Parameter | 10% | 50%+ of votes cast |
| Protocol Upgrade | 15% | 66%+ of votes cast |
| Treasury (<$100k) | 10% | 50%+ of votes cast |
| Treasury (>$100k) | 20% | 66%+ of votes cast |
| Constitutional | 30% | 75%+ of votes cast |

**Quorum Calculation:**
```
quorum_met = (votes_cast / total_staked_NEO) >= quorum_threshold
```

**Approval Calculation:**
```
proposal_passes = (yes_votes / (yes_votes + no_votes)) >= approval_threshold
                  AND quorum_met
```

**Note:** Abstentions count toward quorum but not approval calculation

### 3.4 Vote Delegation

**How to Delegate:**
```bash
# Command-line
neo-governance delegate --to <delegate_address> --amount <NEO_amount>

# Smart contract call
invoke GovernanceController delegate [delegator, delegate, amount]
```

**Delegate Responsibilities:**
- Vote on all proposals during delegation period
- Provide voting rationale publicly
- Respond to delegator concerns
- Disclose conflicts of interest

**Top Delegates (by delegated stake):**
[Published weekly on governance portal]

---

## 4. Upgrade Procedures

### 4.1 Smart Contract Upgrades

**Process:**
1. **Proposal:** Submit upgrade proposal with new contract code
2. **Audit:** External security audit (mandatory for critical contracts)
3. **Testnet Deployment:** Deploy to testnet, run for minimum 7 days
4. **Voting:** DAO vote (Type B - Protocol Upgrade)
5. **Timelock:** 72-hour timelock after approval
6. **Execution:** Deploy to mainnet via governance contract
7. **Monitoring:** 30-day enhanced monitoring with rollback capability

**Upgrade Paths:**

#### Non-Breaking Upgrades (Minor)
- Add new features (backward compatible)
- Bug fixes
- Gas optimizations
- **Downtime:** None

#### Breaking Upgrades (Major)
- Change state structure
- Modify core logic
- API changes
- **Downtime:** Scheduled maintenance window (4-hour advance notice)

**Rollback Procedure:**
```
IF critical_bug_detected WITHIN 30_days:
    Security Council can trigger rollback (5-of-7)
    Restore previous contract version
    Refund affected users
```

### 4.2 Parameter Updates

**On-Chain Parameters (Governance-Controlled):**

| Parameter | Current | Range | Change Frequency |
|-----------|---------|-------|------------------|
| Base Fee Target | 50% | 30-70% | Monthly |
| Max Base Fee | 1000 Gwei | 100-10000 | Quarterly |
| Batch Size Target | 500 | 100-1000 | Monthly |
| Sequencer Stake | 100k NEO | 50k-500k | Quarterly |
| Withdrawal Delay | 7 days | 1-30 days | Rarely |
| Challenge Period | 7 days | 1-14 days | Rarely |

**Update Process:**
1. Propose new parameter value (with justification)
2. DAO vote (Type A - Minor Parameter)
3. Immediate execution (no timelock for parameters)
4. Monitor for 24 hours, rollback if issues

**Emergency Bounds:**
- Security Council can change parameters within ±20% of approved value
- Requires 5-of-7 multi-sig
- Must be ratified by DAO within 7 days or auto-reverts

### 4.3 Protocol Versioning

**Version Format:** `MAJOR.MINOR.PATCH`

**Examples:**
- `1.0.0` - Initial mainnet launch
- `1.1.0` - New feature (backward compatible)
- `1.1.1` - Bug fix
- `2.0.0` - Breaking change

**Compatibility:**
- Same MAJOR version: Fully compatible
- Different MAJOR version: May require migration
- Deprecation notice: Minimum 6 months for breaking changes

---

## 5. Emergency Actions

### 5.1 Emergency Pause

**Trigger Conditions:**
- Active exploit in progress
- Critical vulnerability discovered
- Oracle failure or manipulation
- L1 chain reorganization

**Who Can Pause:**
- Security Council (5-of-7 multi-sig)
- Any Security Council member (single-sig) if >$1M at risk

**What Gets Paused:**
- New deposits to SharedBridge
- Batch submissions (optional)
- Contract upgrades (optional)
- Specific vulnerable functions only (preferred)

**Duration:**
- Maximum: 7 days without DAO approval
- Extensions require DAO vote every 7 days
- Auto-unpause after 30 days if no DAO action

**Communication:**
- Public announcement within 1 hour
- Status page update
- Discord/Telegram/Twitter announcement
- Detailed post-mortem after resolution

### 5.2 Emergency Upgrades

**Fast-Track Process (Used for Critical Security Fixes):**

```
Discovery → Fix Development → Security Council Approval → Deploy
  (T+0)         (T+24h)              (T+48h)             (T+48h)
```

**Requirements:**
- Security Council 5-of-7 approval
- External auditor review (if time permits)
- Testnet deployment (if time permits)
- **No DAO vote required** (but DAO can veto within 7 days)

**Post-Deployment:**
- Detailed incident report within 7 days
- DAO vote to ratify emergency action within 14 days
- If DAO rejects: Rollback + Security Council investigation

### 5.3 Fund Recovery

**Scenario:** User funds stuck due to bug

**Process:**
1. User submits recovery request with proof
2. Security Council validates claim
3. Refund from insurance fund or treasury
4. Root cause fix deployed
5. Recovery report published

**Approval:**
- <$10k: Single Security Council member
- $10k-100k: 3-of-7 Security Council
- >$100k: 5-of-7 Security Council + DAO notification

---

## 6. Treasury Management

### 6.1 Treasury Structure

**Total Treasury:** ~$10M initial (from funding rounds)

**Allocation:**
- Operations (15%): $1.5M - Salaries, infrastructure
- Development (20%): $2M - Core protocol work
- Security (10%): $1M - Audits, bug bounties
- Ecosystem (25%): $2.5M - Grants, partnerships
- Reserve (30%): $3M - Long-term sustainability

**Governance:**
- <$10k: Core team discretion
- $10k-100k: Security Council approval
- >$100k: DAO vote required

### 6.2 Grant Program

**Focus Areas:**
- DeFi protocols on Neo N4
- Developer tools and SDKs
- Educational content
- Security tools
- Cross-chain bridges

**Grant Sizes:**
- Small: $5k-25k (quick approval)
- Medium: $25k-100k (committee review)
- Large: $100k-500k (DAO vote)

**Application Process:**
1. Submit proposal on grants portal
2. Initial screening (3 days)
3. Committee review (7 days)
4. Approval decision
5. Milestone-based disbursement

**Success Metrics:**
- Project completion rate
- User adoption
- Open source contributions
- Community engagement

### 6.3 Investment Strategy

**Conservative Portfolio (60%):**
- NEO staking (30%): 5% APY
- Stablecoins (20%): Lending protocols
- Blue-chip crypto (10%): BTC, ETH

**Growth Portfolio (30%):**
- Neo ecosystem projects
- Strategic partnerships
- Index funds (DeFi, NFT)

**Reserve (10%):**
- Cash equivalents
- Emergency fund

**Risk Management:**
- Maximum 5% in any single asset (except NEO)
- Quarterly rebalancing
- Monthly performance review

---

## 7. Conflict Resolution

### 7.1 Dispute Process

**Scenarios:**
- DAO vote contested (alleged manipulation)
- Security Council action disputed
- Proposal interpretation disagreement

**Resolution Steps:**
1. Informal mediation (community forum)
2. Formal arbitration (neutral 3rd party)
3. DAO appeal vote (final decision)

**Timeline:**
- Dispute filed: Within 7 days of decision
- Mediation: 7-14 days
- Arbitration: 14-30 days
- Appeal vote: 7 days

### 7.2 Security Council Removal

**Grounds:**
- Inactivity (>30 days)
- Conflict of interest
- Breach of duties
- Community loss of confidence

**Process:**
1. Petition with 10,000 NEO stake signatures
2. Investigation period (14 days)
3. DAO vote (20% quorum, 66% approval)
4. If passed: Immediate removal + election of replacement

### 7.3 Constitution Amendments

**Current Version:** 1.0 (October 1, 2026)

**Amendment Process:**
1. Proposal (requires 50,000 NEO stake)
2. Community discussion (minimum 30 days)
3. Draft finalization
4. DAO vote (Type D - Constitutional)
5. Supermajority required (75%+)

**Amendment History:**
- [None yet - initial constitution]

---

## Appendix A: Governance Timeline

### Historical Milestones

| Date | Event |
|------|-------|
| Oct 1, 2026 | Governance framework v1.0 launched |
| Oct 15, 2026 | First Security Council elected |
| Nov 1, 2026 | First DAO proposal voted on |
| Dec 1, 2026 | Treasury management plan approved |
| Q1 2027 | First protocol upgrade via governance |

### Upcoming Decisions

| Quarter | Planned Votes |
|---------|---------------|
| Q4 2026 | Treasury allocation, Parameter tuning |
| Q1 2027 | Security Council re-election, Protocol upgrade |
| Q2 2027 | Grant program expansion |
| Q3 2027 | Tokenomics review (if applicable) |

---

## Appendix B: Governance Tools

### On-Chain Contracts

- **GovernanceController:** `0x1234...` (mainnet)
- **VotingModule:** `0x5678...`
- **TreasuryManager:** `0x9abc...`
- **TimelockController:** `0xdef0...`

### Off-Chain Tools

- **Forum:** https://forum.neo-n4.network
- **Snapshot:** https://snapshot.org/#/neo-n4
- **Governance Dashboard:** https://governance.neo-n4.network
- **Documentation:** https://docs.neo-n4.network/governance

### CLI Commands

```bash
# Stake NEO for voting power
neo-governance stake --amount 1000

# Submit proposal
neo-governance propose --type parameter --title "..." --spec proposal.md

# Vote on proposal
neo-governance vote --proposal-id 42 --vote yes

# Delegate voting power
neo-governance delegate --to 0xabcd... --amount 500

# Check voting power
neo-governance power --address 0x1234...
```

---

## Appendix C: FAQ

**Q: Who can submit proposals?**  
A: Anyone with sufficient stake (varies by proposal type). See section 2.1.

**Q: How long does governance take?**  
A: 2-4 weeks for standard proposals, 48 hours for emergencies.

**Q: Can I change my vote?**  
A: Yes, until voting period ends.

**Q: What happens if quorum isn't met?**  
A: Proposal fails. Can be resubmitted after 30 days.

**Q: How are Security Council members selected?**  
A: Elected by DAO every 6 months (staggered terms).

**Q: Can the Security Council be overridden?**  
A: Yes, DAO can veto Security Council actions within 7 days.

**Q: What if there's a governance attack?**  
A: Time-weighted voting, stake lock-up, and Security Council veto provide defense.

---

**Document Version:** 1.0  
**Effective Date:** October 1, 2026  
**Next Review:** April 1, 2027  
**Contact:** governance@neo-n4.network

*This framework may be amended via the governance process defined herein.*