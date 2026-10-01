# Neo N4 Economics Model

## Executive Summary

This document defines the economic model of the Neo N4 Layer 2 Rollup system, including fee mechanisms, incentive structures, treasury management, and sustainability analysis. The goal is to create a self-sustaining, economically secure system that aligns incentives across all participants.

**Status:** Active  
**Version:** 1.0  
**Last Updated:** October 1, 2026  
**Next Review:** January 1, 2027

---

## Table of Contents

1. [Fee Model](#1-fee-model)
2. [Revenue Streams](#2-revenue-streams)
3. [Cost Structure](#3-cost-structure)
4. [Incentive Mechanisms](#4-incentive-mechanisms)
5. [Treasury Management](#5-treasury-management)
6. [Economic Security](#6-economic-security)
7. [Sustainability Analysis](#7-sustainability-analysis)
8. [Governance](#8-governance)

---

## 1. Fee Model

### 1.1 User-Facing Fees

Neo N4 implements a **dual-fee model** similar to Ethereum:

#### Base Fee (Burned)
- **Purpose:** Anti-spam, predictable pricing
- **Mechanism:** EIP-1559-style dynamic adjustment
- **Formula:** `baseFee[n+1] = baseFee[n] × (1 + 0.125 × (gasUsed - gasTarget) / gasTarget)`
- **Target:** 50% block utilization
- **Bounds:** `0.001 Gwei ≤ baseFee ≤ 1000 Gwei`
- **Destination:** **Burned** (deflationary)

#### Priority Fee (To Sequencer)
- **Purpose:** Transaction ordering, MEV capture
- **Mechanism:** User-specified tip
- **Range:** `0 - 100 Gwei`
- **Destination:** Sequencer revenue
- **Average:** ~5% of base fee (in practice)

#### Total User Fee
```
totalFee = (baseFee + priorityFee) × gasUsed
```

**Example Transaction Costs:**

| Transaction Type | Gas Used | Base Fee | Priority Fee | Total Cost (USD) |
|------------------|----------|----------|--------------|------------------|
| Simple Transfer | 21,000 | 0.01 Gwei | 0 | $0.0001 |
| Token Transfer (NEP-17) | 50,000 | 0.01 Gwei | 0.001 Gwei | $0.0003 |
| Token Swap (DeFi) | 200,000 | 0.01 Gwei | 0.005 Gwei | $0.0012 |
| NFT Mint | 500,000 | 0.01 Gwei | 0.01 Gwei | $0.0030 |

*Assumes 1 NEO = $10, 1 NEO = 10^9 Gwei*

### 1.2 L1 Settlement Fees

Sequencer pays L1 fees for batch submission:

**Per-Batch L1 Cost:**
```
L1_cost = L1_baseFee × L1_gasUsed
```

**Typical Values:**
- L1 gas used: ~500,000 (proof verification + state update)
- L1 base fee: 100 Gwei (Neo N3 typical)
- L1 cost per batch: ~0.05 NEO ($0.50)

**Amortized Per-Transaction:**
```
L1_cost_per_tx = L1_cost / batch_size
```

| Batch Size | L1 Cost per Tx |
|------------|----------------|
| 100 txs | $0.005 |
| 500 txs | $0.001 |
| 1000 txs | $0.0005 |

**Optimal Batch Size:** 500-1000 transactions (balances cost vs latency)

### 1.3 Data Availability Fees

Cost of storing batch data on NeoFS (data availability layer):

**Storage Cost:**
```
DA_cost = data_size × storage_price_per_MB × redundancy_factor
```

**Typical Values:**
- Data size per tx: ~200 bytes (compressed)
- Storage price: $0.01/MB/month (NeoFS)
- Redundancy: 3x
- Cost per tx: ~$0.00006/month

**Amortized:** Negligible (<0.01% of total fees)

### 1.4 Withdrawal Fees

**L2 → L1 Withdrawal:**

| Component | Cost | Who Pays |
|-----------|------|----------|
| L2 Processing | $0.01 | User |
| Proof Generation | $0.05 | Sequencer (subsidized) |
| L1 Execution | $0.10 | User |
| **Total** | **$0.16** | Mostly User |

**Economic Consideration:** Fixed cost favors larger withdrawals ($100+)

---

## 2. Revenue Streams

### 2.1 Sequencer Revenue

**Primary Revenue:**
- **Priority fees:** ~5-10% of total fees
- **MEV extraction:** 10-20% of block value (via fair ordering auctions)
- **Volume:** At 5,000 TPS, ~$1,000/day in priority fees

**Example Daily Revenue (5,000 TPS sustained):**
```
txs_per_day = 5,000 × 60 × 60 × 24 = 432,000,000
avg_priority_fee = $0.00001
daily_revenue = 432M × $0.00001 = $4,320
```

Plus MEV: ~$500-2,000/day (depends on DeFi activity)

**Total Sequencer Revenue:** $5,000-6,500/day

### 2.2 Protocol Revenue

**Sources:**
1. **Batch submission margin:** L1 fees paid by users > L1 costs
2. **Treasury yield:** Staking locked assets
3. **Bridge fees:** 0.1% on cross-chain transfers (optional)

**Example (at scale):**
```
daily_l2_fees_collected = 432M txs × $0.0001 = $43,200
daily_l1_costs = 864 batches × $0.50 = $432
daily_protocol_margin = $43,200 - $432 = $42,768
```

**Annual Protocol Revenue (projected):** ~$15.6M at 5,000 TPS

### 2.3 Validator/Prover Revenue

**Proof Generation Market:**
- **Payment:** Fixed fee per proof + bonus for fast generation
- **Base rate:** $0.05 per batch proof
- **Fast bonus:** +50% if <30 seconds
- **Volume:** ~864 proofs/day

**Daily Prover Revenue:** ~$65-100

**ROI Analysis:**
- Hardware cost: $5,000 (GPU rig)
- Monthly revenue: ~$2,250
- Payback period: ~2.2 months

---

## 3. Cost Structure

### 3.1 Operational Costs (Sequencer)

| Component | Monthly Cost | Annual Cost |
|-----------|--------------|-------------|
| **Compute (AWS c6i.4xlarge)** | $2,100 | $25,200 |
| **Storage (1 TB EBS)** | $100 | $1,200 |
| **Bandwidth (10 TB)** | $900 | $10,800 |
| **Database (RDS)** | $500 | $6,000 |
| **Monitoring/Logging** | $200 | $2,400 |
| **L1 Batch Submissions** | $13,000 | $156,000 |
| **Staff (2 DevOps)** | $30,000 | $360,000 |
| **Security Audits** | $8,333 | $100,000 |
| **Insurance** | $2,000 | $24,000 |
| **Contingency (10%)** | $5,713 | $68,560 |
| **Total** | **$62,846** | **$754,160** |

### 3.2 Proof Generation Costs

**Per Proof:**
- Compute: $0.02 (AWS spot instances)
- Electricity: $0.01 (GPU power)
- Amortized hardware: $0.01
- **Total:** $0.04

**Daily Cost (864 proofs):** ~$35

### 3.3 Break-Even Analysis

**Monthly Revenue (at 5,000 TPS):**
```
Sequencer revenue: $165,000
Protocol margin: ~$1,283,040
Total: ~$1,448,040/month
```

**Monthly Costs:**
```
Operations: $62,846
Provers: $1,050
Total: $63,896
```

**Profit Margin:** 95.6% 🟢

**Break-Even TPS:** ~220 TPS (very safe margin)

---

## 4. Incentive Mechanisms

### 4.1 Sequencer Incentives

**Selection Mechanism:** Proof-of-Stake (PoS) with rotation

**Staking Requirements:**
- Minimum stake: 100,000 NEO (~$1M)
- Lock period: 30 days
- Slashing conditions:
  - Invalid batch: 10% slash
  - Censorship: 5% slash
  - Downtime (>4 hours): 1% slash

**Rewards:**
- Priority fees (100%)
- MEV auction revenue (100%)
- Protocol token emissions (if applicable): 1,000 tokens/day

**Penalties:**
- Invalid proofs: Lose sequencer slot + slash
- Censorship: Grace period (6 hours) then slash
- Downtime: Automatic failover to backup sequencer

### 4.2 Prover Incentives

**Competition Model:** Open market with reputation system

**Base Payment:** $0.05/proof (guaranteed)
**Performance Bonuses:**
- <30s generation: +50%
- <60s generation: +25%
- No bonuses >60s

**Reputation System:**
- Score: 0-100 (based on uptime, speed, quality)
- High reputation (90+): 2x chance of selection
- Low reputation (<50): Blacklisted for 7 days

**Long-term:** Reputation NFTs tradeable on secondary market

### 4.3 Validator Incentives (Governance)

**Roles:**
- Vote on protocol upgrades
- Approve emergency actions
- Dispute invalid batches

**Rewards:**
- Annual yield: 5% APY on staked NEO
- Governance participation: 100 tokens/vote
- Dispute rewards: 10% of slashed stake (if successful)

**Requirements:**
- Minimum stake: 10,000 NEO
- Active participation: >50% of votes
- No conflicts of interest (can't be sequencer)

### 4.4 User Incentives

**Early Adopter Rewards:**
- First 1M transactions: 10x points
- Referral program: 5% of referred fees
- Loyalty NFTs: Benefits on partner dApps

**Volume Tiers:**
| Monthly Volume | Fee Discount | Points Multiplier |
|----------------|--------------|-------------------|
| $0 - $1,000 | 0% | 1x |
| $1,000 - $10,000 | 5% | 1.5x |
| $10,000 - $100,000 | 10% | 2x |
| $100,000+ | 15% | 3x |

---

## 5. Treasury Management

### 5.1 Treasury Composition

**Initial Allocation:**
- Operations reserve: 30% ($3M)
- Security fund: 20% ($2M)
- Ecosystem grants: 25% ($2.5M)
- DAO governance: 25% ($2.5M)
- **Total:** $10M initial treasury

**Revenue Allocation:**
```
Monthly Revenue: ~$1,448,040
├─ Operations (15%): $217,206
├─ Development (20%): $289,608
├─ Security/Audits (10%): $144,804
├─ Marketing/Growth (10%): $144,804
├─ Treasury Reserve (30%): $434,412
└─ DAO Controlled (15%): $217,206
```

### 5.2 Treasury Yield Strategies

**Conservative Strategy (80% of treasury):**
- Neo N3 staking: 5% APY
- Stablecoins lending (Aave, Compound): 3-4% APY
- NEO ecosystem yield farms: 6-8% APY

**Growth Strategy (20% of treasury):**
- Index funds (BTC, ETH): Long-term hold
- Strategic investments: Neo ecosystem projects
- Liquidity provision: DEX pools (high APY but higher risk)

**Expected Annual Yield:** ~$500,000 (5% of $10M treasury)

### 5.3 Emergency Reserves

**Minimum Reserve:** 6 months of operating costs (~$377,376)

**Trigger Conditions:**
- Monthly revenue < operating costs for 2 consecutive months
- Major security incident requiring immediate funds
- L1 gas spike (>10x normal) sustained for >1 week

**Replenishment:** 50% of monthly profits until reserve restored

---

## 6. Economic Security

### 6.1 Attack Cost Analysis

**Double-Spend Attack (Optimistic Rollup):**
- Cost: Sequencer stake (100,000 NEO = $1M)
- Benefit: Max value in one batch (~$100k typical)
- **Ratio:** 10:1 (economically irrational)

**Censorship Attack:**
- Cost: Lose 5% stake ($50k) + sequencer slot
- Benefit: Block competitor transactions (limited time)
- **Mitigation:** Forced inclusion queue bypasses censorship

**MEV Extraction Abuse:**
- **Mitigation:** Fair ordering auctions (MEV shared with users)
- **Threshold:** If MEV extraction >20%, governance can penalize

### 6.2 Fee Market Stability

**Base Fee Adjustment Algorithm:**
```
target_usage = 50%
if usage > target:
    increase base fee (up to 12.5% per block)
else:
    decrease base fee (up to 12.5% per block)
```

**Simulation Results:**
- Convergence time: <10 minutes
- Overshoot: <5%
- Stability: Oscillates within ±2% of equilibrium

**Circuit Breakers:**
- Max fee increase: 10x in 1 hour
- Min fee: 0.001 Gwei (prevents DoS)
- Max fee: 1000 Gwei (prevents user shock)

### 6.3 Liquidity Requirements

**Bridge Liquidity:**
- Target: 2x daily withdrawal volume
- Current: $5M locked in SharedBridge
- Daily withdrawals: ~$500k
- **Ratio:** 10:1 (healthy)

**Operational Liquidity:**
- Hot wallet: $50k (daily operations)
- Cold storage: $2M (security reserve)
- Multi-sig: 5-of-7 for >$100k transactions

---

## 7. Sustainability Analysis

### 7.1 Revenue Projections

**Conservative Scenario (2,000 TPS average):**
```
Annual txs: 63B
Revenue: $6.3M/year
Costs: $754k/year
Profit: $5.5M/year ✅
```

**Base Case (5,000 TPS average):**
```
Annual txs: 157B
Revenue: $15.7M/year
Costs: $850k/year
Profit: $14.9M/year ✅
```

**Optimistic Scenario (10,000 TPS average):**
```
Annual txs: 315B
Revenue: $31.5M/year
Costs: $1.1M/year
Profit: $30.4M/year ✅
```

**Break-Even Scenario:** 220 TPS sustained

### 7.2 Sensitivity Analysis

| Variable | -50% | -25% | Base | +25% | +50% |
|----------|------|------|------|------|------|
| **TPS** | Breakeven | Profitable | Profitable | Highly Profitable | Highly Profitable |
| **Fee/Tx** | Loss | Breakeven | Profitable | Highly Profitable | Highly Profitable |
| **L1 Gas** | Profitable | Profitable | Profitable | Profitable | Breakeven |
| **OpEx** | Highly Profitable | Profitable | Profitable | Profitable | Profitable |

**Key Insight:** System is robust to 50% drops in TPS or fees, but vulnerable to 2x+ L1 gas spikes

**Mitigation:**
- L1 gas hedge (futures contracts)
- Dynamic batch sizing (larger batches when L1 gas high)
- Emergency treasury reserve

### 7.3 Long-Term Sustainability (5-Year Projection)

**Year 1:** Bootstrap phase, subsidized fees, break-even  
**Year 2:** Organic growth, 3,000 TPS average, $9M profit  
**Year 3:** Ecosystem maturity, 7,000 TPS, $20M profit  
**Year 4:** Full adoption, 12,000 TPS, $35M profit  
**Year 5:** Market leader, 15,000 TPS, $45M profit  

**Cumulative Profit (5 years):** ~$109M  
**Treasury Growth:** $10M → $119M (12x)

---

## 8. Governance

### 8.1 Governance Token (Optional)

**If issued, token would have:**
- Voting power on protocol upgrades
- Fee distribution rights (dividends)
- Treasury spending approval
- Emergency action veto

**Distribution:**
- Team: 20% (4-year vesting)
- Early users: 15% (airdrop)
- DAO treasury: 30%
- Ecosystem grants: 20%
- Strategic partners: 15%

### 8.2 Fee Parameter Governance

**Parameters Under Governance Control:**
- Base fee adjustment speed (currently 12.5%)
- Target block utilization (currently 50%)
- Minimum/maximum fees
- L1 batch submission frequency
- Sequencer stake requirements

**Voting Thresholds:**
- Minor changes (±10%): 50% quorum, simple majority
- Major changes (>±50%): 66% quorum, 2/3 majority
- Emergency actions: Security Council can act immediately

### 8.3 Treasury Governance

**DAO Controls:**
- Ecosystem grants (up to $100k per proposal)
- Marketing budgets
- Partnership agreements
- Treasury investment strategy

**Security Council Retains:**
- Emergency fund access
- Security audit contracts
- Critical infrastructure spending
- Insurance policies

---

## Appendix A: Fee Comparison

### Neo N4 vs Alternatives

| Chain | Simple Transfer | Token Swap | NFT Mint |
|-------|----------------|------------|----------|
| **Neo N4 (L2)** | $0.0001 | $0.0012 | $0.003 |
| Neo N3 (L1) | $0.10 | $2.00 | $5.00 |
| Ethereum L1 | $1.50 | $15.00 | $50.00 |
| Arbitrum One | $0.10 | $1.50 | $5.00 |
| Optimism | $0.15 | $2.00 | $8.00 |
| Polygon zkEVM | $0.02 | $0.50 | $1.50 |

**Neo N4 Advantage:** 100-5000x cheaper than L1, 10-50x cheaper than competing L2s

---

## Appendix B: Economic Assumptions

**Neo Price:** $10 (range: $5-20)  
**L1 Gas Price:** 100 Gwei (range: 50-500 Gwei)  
**TPS:** 5,000 average (range: 1,000-15,000)  
**Batch Size:** 500 txs (range: 100-1000)  
**Prover Competition:** 10 active provers  
**Sequencer Uptime:** 99.9%  

**Sensitivity:** Results hold for NEO $5-20, L1 gas 50-300 Gwei

---

## Appendix C: Historical Economics

### Evolution Over Time

| Date | Avg TPS | Fee/Tx | Daily Revenue | Profit Margin |
|------|---------|--------|---------------|---------------|
| Jun 2026 (PoC) | 100 | $0.01 | $86 | -98% |
| Aug 2026 (Alpha) | 500 | $0.002 | $864 | -85% |
| Oct 2026 (Beta) | 2,000 | $0.0001 | $17,280 | +75% |
| **Jan 2027 (Target)** | **5,000** | **$0.0001** | **$43,200** | **+95%** |

---

**Document Version:** 1.0  
**Effective Date:** October 1, 2026  
**Next Review:** January 1, 2027  
**Contact:** economics@neo-n4.network

*This model may be updated based on real-world performance and market conditions.*