#!/bin/bash
# Neo N4 Testnet Deployment Script
# Generated: 2026-09-25

set -e

# Configuration
export NEO_N4_TESTNET_WIF="KyjFxv5kVCKQrFUUa7rv1iak8jZh6YsaRnyAsk63UREg7F7FdP7h"

# SP1 Verification Keys (from vk_manifest.rs)
SP1_BATCH_VK="00a619e3a891082a2d23e22b966ac4664725753466c60eda17e7f5c6fc7179ef"
GATEWAY_VK="0045e70b7add8250ad684cabc5aad40fe6f30d57d7df56850477df648449efa2"

# Network Configuration
RPC_ENDPOINT="https://testnet1.neo.coz.io:443"
NETWORK_MAGIC="894710606"  # Neo N3 Testnet
L2_CHAIN_ID="1"

# Governance Configuration (PLACEHOLDER - needs real committee keys)
GOVERNANCE_COUNCIL="02b3622bf4017bdfe317c58aed5f4c753f206b7db896046fa7d774bbc4bf7f8dc2,03b209fd4f53a7170ea4444e0cb0a6bb6a53c2bd016926989cf85f9b0fba17a70c,02ca0e27697b9c248f6f16e085fd0061e26f44da85b58ee835c110caa5ec3ba554"
GOVERNANCE_THRESHOLD="2"

# Emergency Configuration
EMERGENCY_COUNCIL="NikhQp1aAD1YFCiwknhM5LQQebj4464bCJ"  # Derived from WIF key

# Domain Configuration for fraud proofs (SHA256 hashes of domain strings)
FRAUD_REPLAY_DOMAIN="34f50a3abd881f11e79491ee5a68d43a2aff136404d60fb17eedeba257416c79"
GATEWAY_REPLAY_DOMAIN="f70d74c1019695c62e1215a7b2bba444152449d19318d2cf49ab90db6e30d90c"

echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║          Neo N4 Testnet Deployment Configuration              ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""
echo "Network:           Neo N3 Testnet (Magic: $NETWORK_MAGIC)"
echo "RPC Endpoint:      $RPC_ENDPOINT"
echo "L2 Chain ID:       $L2_CHAIN_ID"
echo "SP1 Batch VK:      $SP1_BATCH_VK"
echo "Gateway VK:        $GATEWAY_VK"
echo "Governance:        $GOVERNANCE_THRESHOLD-of-3 multisig"
echo "Emergency Council: $EMERGENCY_COUNCIL"
echo ""
echo "═══════════════════════════════════════════════════════════════"
echo ""

# Verify deployment artifacts
echo "[1/3] Verifying deployment artifacts..."
for contract in GovernanceController ZkVerifier MultisigVerifier RollupHub SharedBridge Sp1Groth16Verifier; do
  nef="contracts/NeoHub.$contract/bin/sc/NeoHub.$contract.nef"
  manifest="contracts/NeoHub.$contract/bin/sc/NeoHub.$contract.manifest.json"
  if [[ -f "$nef" && -f "$manifest" ]]; then
    size=$(stat -c%s "$nef" 2>/dev/null || stat -f%z "$nef")
    echo "  ✓ $contract ($size bytes)"
  else
    echo "  ✗ $contract MISSING"
    exit 1
  fi
done
echo ""

# Build deployment tool
echo "[2/3] Building deployment tool..."
dotnet build tools/Neo.Hub.Deploy --configuration Release -v quiet
echo "  ✓ Neo.Hub.Deploy built"
echo ""

# Execute deployment
echo "[3/3] Deploying to testnet..."
echo ""
echo "DEPLOYMENT COMMAND:"
echo "==================="
echo ""
cat << DEPLOY_CMD
dotnet run --project tools/Neo.Hub.Deploy --configuration Release -- deploy-testnet \
  deploy-plan-5pillar.json \
  --rpc $RPC_ENDPOINT \
  --expected-network $NETWORK_MAGIC \
  --l2-chain-id $L2_CHAIN_ID \
  --sp1-program-vkey $SP1_BATCH_VK \
  --fraud-replay-domain $FRAUD_REPLAY_DOMAIN \
  --gateway-program-vkey $GATEWAY_VK \
  --gateway-replay-domain $GATEWAY_REPLAY_DOMAIN \
  --governance-council "$GOVERNANCE_COUNCIL" \
  --governance-threshold $GOVERNANCE_THRESHOLD \
  --emergency-council $EMERGENCY_COUNCIL \
  --wif-env NEO_N4_TESTNET_WIF
DEPLOY_CMD
echo ""
echo "═══════════════════════════════════════════════════════════════"
echo ""
echo "⚠️  REVIEW REQUIRED:"
echo ""
echo "1. Governance council keys above are PLACEHOLDER public keys"
echo "2. Emergency council address is derived from the provided WIF"
echo "3. Fraud/gateway replay domains are testnet defaults"
echo ""
echo "To proceed with actual deployment, run:"
echo "  bash testnet-deploy.sh execute"
echo ""
echo "To see deployment plan only:"
echo "  bash testnet-deploy.sh plan"
echo ""

if [[ "$1" == "execute" ]]; then
  echo "Executing deployment in 5 seconds... (Ctrl+C to abort)"
  sleep 5
  
  dotnet run --project tools/Neo.Hub.Deploy --configuration Release -- deploy-testnet \
    deploy-plan-5pillar.json \
    --rpc "$RPC_ENDPOINT" \
    --expected-network "$NETWORK_MAGIC" \
    --l2-chain-id "$L2_CHAIN_ID" \
    --sp1-program-vkey "$SP1_BATCH_VK" \
    --fraud-replay-domain "$FRAUD_REPLAY_DOMAIN" \
    --gateway-program-vkey "$GATEWAY_VK" \
    --gateway-replay-domain "$GATEWAY_REPLAY_DOMAIN" \
    --governance-council "$GOVERNANCE_COUNCIL" \
    --governance-threshold "$GOVERNANCE_THRESHOLD" \
    --emergency-council "$EMERGENCY_COUNCIL" \
    --wif-env NEO_N4_TESTNET_WIF
elif [[ "$1" == "plan" ]]; then
  echo "Generating deployment plan..."
  dotnet run --project tools/Neo.Hub.Deploy --configuration Release -- verify deploy-plan-5pillar.json
fi
