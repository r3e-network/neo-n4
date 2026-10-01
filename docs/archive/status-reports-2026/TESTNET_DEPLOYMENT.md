# Neo N4 Testnet Deployment Guide

**Status**: Ready for deployment  
**Date**: 2026-09-25  
**Network**: Neo N3 Testnet (Magic: 894710606)

---

## Prerequisites

### 1. Deployment Artifacts ✅

All contracts compiled and ready:

| Contract | Size | Hash |
|----------|------|------|
| GovernanceController | 11,599 bytes | TBD (post-deployment) |
| ZkVerifier | 2,719 bytes | TBD |
| MultisigVerifier | 2,500 bytes | TBD |
| RollupHub | 15,979 bytes | TBD |
| SharedBridge | 9,945 bytes | TBD |
| Sp1Groth16Verifier | 2,940 bytes | TBD |

### 2. Verification Keys ✅

**SP1 Batch Proof Verifier Key** (from `bridge/neo-zkvm-guest/vk_manifest.rs`):
```
00a619e3a891082a2d23e22b966ac4664725753466c60eda17e7f5c6fc7179ef
```

**Gateway Aggregation Verifier Key** (from `bridge/neo-zkvm-gateway-guest/vk_manifest.rs`):
```
0045e70b7add8250ad684cabc5aad40fe6f30d57d7df56850477df648449efa2
```

### 3. Network Configuration ✅

- **RPC Endpoint**: `https://testnet1.neo.coz.io:443`
- **Network Magic**: `894710606` (Neo N3 Testnet)
- **Initial L2 Chain ID**: `1`

### 4. Wallet Configuration ✅

**Deployer WIF**: `KyjFxv5kVCKQrFUUa7rv1iak8jZh6YsaRnyAsk63UREg7F7FdP7h`  
**Deployer Address**: `NikhQp1aAD1YFCiwknhM5LQQebj4464bCJ`

⚠️ **Security Note**: This WIF key is publicly visible in this documentation and conversation history. Only use for testnet deployment with test funds.

### 5. Governance Configuration

**Council Public Keys** (placeholder - update with actual committee):
```
02b3622bf4017bdfe317c58aed5f4c753f206b7db896046fa7d774bbc4bf7f8dc2
03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c
02ca0e27697b9c248f6f16e085fd0061e26f44da85b58ee835c110caa5ec3ba554
```

**Multisig Threshold**: `2` (2-of-3)

**Emergency Council**: `NikhQp1aAD1YFCiwknhM5LQQebj4464bCJ` (deployer address)

---

## Deployment Procedure

### Step 1: Environment Setup

```bash
cd /d/Git/neo-n4

# Set deployer WIF
export NEO_N4_TESTNET_WIF="KyjFxv5kVCKQrFUUa7rv1iak8jZh6YsaRnyAsk63UREg7F7FdP7h"

# Verify deployment artifacts
bash testnet-deploy.sh
```

### Step 2: Deploy Contracts

The deployment script will deploy contracts in dependency order:

1. **GovernanceController** (independent)
2. **ZkVerifier** (independent)
3. **MultisigVerifier** (independent)
4. **RollupHub** (depends on ZkVerifier)
5. **SharedBridge** (depends on RollupHub)
6. **Sp1Groth16Verifier** (independent)

```bash
# Execute deployment
bash testnet-deploy.sh execute
```

### Step 3: Post-Deployment Configuration

After successful deployment, configure contract relationships:

#### 3.1 Configure GovernanceController

```bash
# Register initial sequencers (example)
dotnet run --project tools/Neo.Stack.Cli -- \
  register-sequencer \
  --chain-id 1 \
  --pubkey <sequencer-pubkey> \
  --bond 10000 \
  --rpc https://testnet1.neo.coz.io:443
```

#### 3.2 Configure SharedBridge

```bash
# Set settlement manager (RollupHub address)
# This will be done automatically during deployment via deployData
```

#### 3.3 Register First L2 Chain

```bash
dotnet run --project tools/Neo.Stack.Cli -- \
  create-chain \
  --chain-id 1 \
  --sequencer-mode committee \
  --da-mode calldata \
  --proof-mode attestation \
  --output chain-1-config.json

dotnet run --project tools/Neo.Stack.Cli -- \
  register-chain \
  --config chain-1-config.json \
  --rpc https://testnet1.neo.coz.io:443
```

### Step 4: Lock Governance

After initial configuration is complete, lock governance to enable council-based administration:

```bash
# Lock RollupHub governance
# Invoke RollupHub.LockGovernance(governanceController)

# Lock SharedBridge governance  
# Invoke SharedBridge.LockGovernance(governanceController)
```

**Post-Lock**: All administrative operations require governance proposals via `GovernanceController.ProposeAction()`.

---

## Deployment Validation

### Verify Contract Deployments

```bash
# Check all contract deployments on testnet explorer
# https://testnet.neo.org/

# Verify contract script hashes match deployment plan
```

### Verify Inter-Contract Wiring

```bash
# Verify RollupHub → ZkVerifier linkage
# Invoke RollupHub.GetZkVerifier() → should return ZkVerifier address

# Verify SharedBridge → RollupHub linkage
# Invoke SharedBridge.GetSettlementManager() → should return RollupHub address

# Verify GovernanceController sequencer registry
# Invoke GovernanceController.GetSequencerCount(chainId=1) → should return registered count
```

### Run Smoke Tests

#### Test 1: Chain Registration

```bash
dotnet run --project tools/Neo.Stack.Cli -- \
  list-chains \
  --rpc https://testnet1.neo.coz.io:443

# Expected: Chain ID 1 with configured parameters
```

#### Test 2: Asset Deposit

```bash
# Deposit 100 GAS to L2
# Invoke SharedBridge.Deposit(chainId=1, asset=GAS, amount=100_00000000)

# Verify: OnDeposit event emitted with correct parameters
```

#### Test 3: Batch Submission

```bash
# Submit test batch (requires running sequencer)
dotnet run --project tools/Neo.Stack.Cli -- \
  start-sequencer \
  --chain-id 1 \
  --l1-rpc https://testnet1.neo.coz.io:443 \
  --batch-interval 60

# Wait for batch submission
# Verify: RollupHub.GetLatestBatch(chainId=1) returns batch #1
```

#### Test 4: Withdrawal

```bash
# Submit withdrawal from L2 (requires L2 node running)
# Prove withdrawal inclusion
# Finalize withdrawal on L1 via SharedBridge.FinalizeWithdrawal()

# Verify: Withdrawal completed, assets returned to L1 account
```

---

## Troubleshooting

### Issue: Insufficient GAS for Deployment

**Solution**: Transfer testnet GAS to deployer address:
```
NikhQp1aAD1YFCiwknhM5LQQebj4464bCJ
```

Request from Neo testnet faucet: https://neowish.ngd.network/

### Issue: Contract Deployment Fails

**Check**:
1. Verify NEF and manifest files exist
2. Check RPC endpoint connectivity: `curl https://testnet1.neo.coz.io:443`
3. Verify network magic matches testnet: `894710606`
4. Check deployer account has sufficient GAS balance

### Issue: Invalid Verification Key

**Resolution**: Verification keys are pinned in:
- `bridge/neo-zkvm-guest/vk_manifest.rs` (batch proof VK)
- `bridge/neo-zkvm-gateway-guest/vk_manifest.rs` (gateway VK)

If keys are updated, recompile guest programs and update deployment script.

### Issue: Governance Lock Fails

**Common causes**:
1. Settlement manager not configured on SharedBridge
2. Invalid governance controller address
3. Already locked (check `IsGovernanceLocked()`)

**Resolution**: Ensure `SharedBridge.SetSettlementManager(rollupHub)` is called before locking.

---

## Post-Deployment Checklist

- [ ] All 6 contracts deployed successfully
- [ ] Contract addresses recorded in deployment log
- [ ] RollupHub → ZkVerifier linkage verified
- [ ] SharedBridge → RollupHub linkage verified
- [ ] Initial L2 chain registered (Chain ID 1)
- [ ] At least one sequencer registered for Chain ID 1
- [ ] Smoke test: Deposit successful
- [ ] Smoke test: Batch submission successful
- [ ] Smoke test: Withdrawal successful
- [ ] Governance locked on RollupHub
- [ ] Governance locked on SharedBridge
- [ ] Contract addresses published to team
- [ ] Testnet explorer links documented

---

## Contract Addresses (Post-Deployment)

**Update this section after deployment:**

| Contract | Address | Tx Hash |
|----------|---------|---------|
| GovernanceController | `0x...` | `0x...` |
| ZkVerifier | `0x...` | `0x...` |
| MultisigVerifier | `0x...` | `0x...` |
| RollupHub | `0x...` | `0x...` |
| SharedBridge | `0x...` | `0x...` |
| Sp1Groth16Verifier | `0x...` | `0x...` |

---

## Next Steps After Testnet Deployment

1. **Monitor Initial Batches**: Watch first 10 batches for any anomalies
2. **Performance Testing**: Submit high-volume transactions to test throughput
3. **Proof System Validation**: Verify multisig attestation, then test ZK proof submission
4. **Governance Testing**: Create and execute test proposal via GovernanceController
5. **Security Audit**: Run comprehensive security audit before mainnet
6. **Documentation**: Update public documentation with testnet addresses
7. **Community Testing**: Open testnet to community testers

---

**Deployment Script**: `testnet-deploy.sh`  
**Deployment Plan**: `deploy-plan-5pillar.json`  
**Support**: dev@r3e.network
