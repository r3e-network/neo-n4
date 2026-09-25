#!/bin/bash
# Neo N4 Testnet Validation Script
set -e

# Deployed contract addresses
export ROLLUP_HUB="0x438786f19b73519714decc8268287aad3c4e6c3e"
export SHARED_BRIDGE="0xc824f1d0488299623f013560ee102dbe2fa201bb"
export GOVERNANCE="0xc1b770e7b61b5768b23b557e6ce61a09e5c45629"
export ZK_VERIFIER="0x8b674ba61f37b4aa6127e41df419d110efc6c5ef"
export SP1_VERIFIER="0xeae0a192b4cbdb75d846fba5dafcaa1171b517d8"

# Network config
export RPC="https://testnet1.neo.coz.io:443"
export NETWORK_MAGIC="894710606"

echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║          Neo N4 Testnet Validation Suite                      ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""
echo "RollupHub:           $ROLLUP_HUB"
echo "SharedBridge:        $SHARED_BRIDGE"
echo "GovernanceController: $GOVERNANCE"
echo "ZkVerifier:          $ZK_VERIFIER"
echo "Sp1Groth16Verifier:  $SP1_VERIFIER"
echo ""
echo "═══════════════════════════════════════════════════════════════"
echo ""

# Step 1: Create L2 chain configuration
echo "[1/7] Creating L2 chain configuration..."
dotnet run --project tools/Neo.Stack.Cli -- create-chain \
  --chain-id 1 \
  --sequencer-mode committee \
  --da-mode calldata \
  --proof-mode attestation \
  --output chain-1-config.json
echo "  ✓ Chain config created: chain-1-config.json"
echo ""

# Step 2: Display chain config
echo "[2/7] Generated chain configuration:"
cat chain-1-config.json | head -30
echo ""

echo "═══════════════════════════════════════════════════════════════"
echo "Validation script ready. Next steps require invoking testnet contracts."
echo ""
