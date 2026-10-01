# Neo N4 Bug Bounty Program

## Overview

The Neo N4 Bug Bounty Program is designed to incentivize security researchers and the community to responsibly discover and disclose security vulnerabilities in the Neo N4 Layer 2 Rollup system.

**Program Status:** 🟢 Active  
**Last Updated:** October 1, 2026  
**Managed By:** Neo N4 Security Team

---

## Scope

### In Scope

The following components are eligible for bug bounty rewards:

#### **Critical Scope (Highest Rewards)**
- **L1 Smart Contracts**
  - `NeoHub.RollupHub` - Batch settlement and chain registry
  - `NeoHub.SharedBridge` - Cross-chain asset bridge
  - `NeoHub.GovernanceController` - Governance and access control
  - `NeoHub.SettlementManager` - Proof verification and finalization
  
- **L2 Core Infrastructure**
  - Sequencer (batch production and ordering)
  - zkVM Prover (SP1 proof generation)
  - State transition execution engine
  - Bridge deposit/withdrawal mechanisms

#### **High-Priority Scope**
- Data availability layer (NeoFS integration)
- RPC nodes and API endpoints
- Forced transaction inclusion queue
- Emergency pause mechanisms
- Multi-sig wallet implementations

#### **Medium-Priority Scope**
- Monitoring and alerting systems
- SDK implementations (critical bugs only)
- Documentation (if leading to exploitable misuse)

### Out of Scope

The following are **NOT** eligible for rewards:
- ❌ Social engineering attacks
- ❌ Phishing attacks
- ❌ Physical attacks on infrastructure
- ❌ Denial of Service (DoS) attacks
- ❌ Spam or content injection
- ❌ UI/UX bugs without security impact
- ❌ Rate limiting issues
- ❌ Missing HTTPS (already known)
- ❌ Third-party services (unless directly impacts Neo N4)
- ❌ Already known issues listed in GitHub Issues
- ❌ Theoretical vulnerabilities without proof of concept

---

## Severity Classification

### Critical (P0) - Up to $100,000 USD

**Direct loss of user funds or catastrophic system failure**

Examples:
- Unauthorized withdrawal of bridged assets
- Minting unbacked L2 tokens
- Stealing locked L1 funds from SharedBridge
- Bypassing zkVM proof verification
- Sequencer can forge state transitions
- Governance takeover allowing arbitrary code execution
- Permanent chain halt that cannot be recovered

### High (P1) - Up to $50,000 USD

**Severe impact but with constraints or requiring specific conditions**

Examples:
- Denial of service that halts batch submission >24 hours
- Bypass of forced inclusion queue (censorship)
- Incorrect state root finalization under specific conditions
- Gas griefing attacks causing >10x cost increase
- Replay attacks on cross-chain messages
- Emergency pause bypass
- Governance vote manipulation

### Medium (P2) - Up to $10,000 USD

**Significant issues with limited impact or requiring user interaction**

Examples:
- Front-running attacks with financial impact
- Information leakage of private data
- Transaction ordering manipulation
- MEV extraction beyond intended design
- Batch withholding attacks (temporary)
- Oracle manipulation
- Incorrect fee calculation

### Low (P3) - Up to $1,000 USD

**Minor security issues with minimal impact**

Examples:
- Logging sensitive information
- Best practice violations
- Configuration issues
- Non-exploitable code quality issues
- Gas inefficiencies
- Edge cases without clear exploit path

---

## Reward Guidelines

### Factors Affecting Reward Amount

**Increase Reward (+):**
- Detailed proof-of-concept exploit code
- Suggested fix with pull request
- Responsible disclosure timeline followed
- Clear, professional writeup
- Multiple attack vectors demonstrated
- Impact on mainnet vs testnet

**Decrease Reward (-):**
- Already reported by another researcher
- Requires unlikely user actions
- Partial or incomplete vulnerability
- Public disclosure before fix
- Lack of proof-of-concept
- Poor quality report

### Bonus Rewards

- 🎖️ **First Reporter Bonus:** +20% if you're the first to report this class of bug
- 🎖️ **Comprehensive Report Bonus:** +15% for exceptional documentation quality
- 🎖️ **Suggested Fix Bonus:** +10% for providing working fix with tests
- 🎖️ **Responsible Disclosure Bonus:** +10% for coordinating disclosure timeline

---

## Submission Process

### Step 1: Prepare Your Report

Your report must include:
1. **Summary** - One paragraph describing the vulnerability
2. **Severity Assessment** - Your classification (P0/P1/P2/P3)
3. **Affected Components** - Specific contracts, files, or services
4. **Proof of Concept** - Reproducible steps or exploit code
5. **Impact Analysis** - What can an attacker achieve?
6. **Suggested Fix** - Optional but encouraged
7. **Discoverer Info** - Contact details and payout preference

### Step 2: Submit Securely

**🔐 Encrypted Submission (Recommended):**
```
Email: security@neo-n4.network
PGP Key: [Download from website]
```

**Alternative Channels:**
- GitHub Security Advisory (private): https://github.com/r3e-network/neo-n4/security/advisories
- HackerOne: https://hackerone.com/neo-n4 (if enrolled)

**⚠️ DO NOT:**
- Create public GitHub issues for vulnerabilities
- Discuss in Discord/Telegram before disclosure
- Share exploit code publicly

### Step 3: Await Response

**Timeline Expectations:**
- Initial response: Within 48 hours
- Severity triage: Within 5 business days
- Reward decision: Within 10 business days of fix deployment
- Payment processing: Within 30 days of approval

### Step 4: Coordinated Disclosure

We follow a **90-day disclosure timeline**:
- **Day 0:** Vulnerability reported
- **Day 0-30:** Validation, fix development, testing
- **Day 30-60:** Deployment to testnet, staging
- **Day 60-90:** Mainnet deployment, monitoring
- **Day 90+:** Public disclosure (coordinated with reporter)

**Early Public Disclosure:**
- Active exploitation: Immediate public disclosure after fix
- Critical severity: 7-14 day expedited timeline if safe

---

## Payment Methods

We offer flexible payout options:

1. **Cryptocurrency (Preferred)**
   - NEO (on Neo N3 mainnet)
   - USDT/USDC (on Neo N3 or Ethereum)
   - Bitcoin, Ethereum
   
2. **Fiat Currency**
   - Bank transfer (international)
   - PayPal (for amounts <$5,000 USD)
   
3. **Other**
   - Donation to charity of your choice
   - Neo N4 ecosystem grants (2x value in project tokens)

**Tax Considerations:**
- You are responsible for taxes in your jurisdiction
- We may require KYC for payments >$10,000 USD
- 1099 forms issued for US recipients (if applicable)

---

## Hall of Fame

We publicly recognize researchers who have responsibly disclosed vulnerabilities:

### 2026
- 🥇 **[Researcher Name]** - Critical state transition bug - $75,000
- 🥈 **[Researcher Name]** - Bridge withdrawal bypass - $50,000
- 🥉 **[Researcher Name]** - Sequencer DoS vector - $15,000

*(Updated quarterly)*

---

## Rules and Legal

### Safe Harbor

We commit to:
- ✅ Not pursue legal action for good-faith security research
- ✅ Work with you to understand and address the issue
- ✅ Publicly credit you (if desired) after fix deployment
- ✅ Process your reward fairly and promptly

You must:
- ✅ Not violate any laws during your research
- ✅ Not access user data beyond proof-of-concept
- ✅ Not publicly disclose before coordinated timeline
- ✅ Not demand ransom or engage in extortion
- ✅ Make a good-faith effort to avoid harm

### Eligibility

You are eligible if you:
- ✅ Are the first to report the unique vulnerability
- ✅ Adhere to our disclosure policy
- ✅ Comply with local laws
- ✅ Are not a current Neo N4 team member or contractor

You are **NOT eligible** if you:
- ❌ Are on an OFAC sanctions list
- ❌ Reside in an embargoed country
- ❌ Found the bug while performing authorized penetration testing
- ❌ Are under 18 years old (without parental consent)

### Modifications

We reserve the right to:
- Modify reward amounts based on severity re-assessment
- Modify program scope and rules with 30-day notice
- Cancel or pause the program temporarily during audits
- Award discretionary bonuses for exceptional contributions

---

## Contact

- **Email:** security@neo-n4.network
- **PGP Key:** [To be published]
- **Emergency Hotline:** [To be established]
- **GitHub:** https://github.com/r3e-network/neo-n4/security

**Response SLA:**
- Critical issues: 4 hours
- High issues: 24 hours
- Medium/Low issues: 72 hours

---

## Frequently Asked Questions

**Q: Can I submit multiple vulnerabilities at once?**  
A: Yes, but please submit each as a separate report for clear tracking.

**Q: What if my bug is a duplicate?**  
A: First valid submission wins. We'll notify you within 48 hours if it's a duplicate.

**Q: Can I test on mainnet?**  
A: No. Use testnet or local devnet only. Mainnet testing without permission is out of scope.

**Q: How do you calculate rewards for chained exploits?**  
A: We assess the maximum achievable impact, not sum of individual steps.

**Q: What if I disagree with the severity assessment?**  
A: You can appeal within 7 days with additional justification. Final decision by security team.

**Q: Can researchers from any country participate?**  
A: Yes, except those in OFAC-restricted jurisdictions or under international sanctions.

---

**Last Updated:** October 1, 2026  
**Version:** 1.0  
**Next Review:** January 1, 2027

*This program may be modified or terminated at any time. Current version always available at: https://neo-n4.network/security/bug-bounty*