#!/bin/bash
# Neo N4 Testnet Validation Script - Direct Contract Testing
set -e

# Deployed contract addresses
ROLLUP_HUB="0x438786f19b73519714decc8268287aad3c4e6c3e"
SHARED_BRIDGE="0xc824f1d0488299623f013560ee102dbe2fa201bb"
GOVERNANCE="0xc1b770e7b61b5768b23b557e6ce61a09e5c45629"
ZK_VERIFIER="0x8b674ba61f37b4aa6127e41df419d110efc6c5ef"
SP1_VERIFIER="0xeae0a192b4cbdb75d846fba5dafcaa1171b517d8"

RPC="https://testnet1.neo.coz.io:443"

echo "╔═══════════════════════════════════════════════════════════════╗"
echo "║       Neo N4 Testnet Validation - Contract State Query       ║"
echo "╚═══════════════════════════════════════════════════════════════╝"
echo ""

# Function to query contract state via RPC
query_contract() {
    local contract=$1
    local method=$2
    local params=$3

    echo "Querying $method on $contract..."

    curl -s -X POST "$RPC" \
        -H "Content-Type: application/json" \
        -d "{
            \"jsonrpc\": \"2.0\",
            \"method\": \"invokefunction\",
            \"params\": [\"$contract\", \"$method\" $params],
            \"id\": 1
        }" | jq -r '.result.stack[0].value // .error.message // "no result"'
}

echo "[1] Validating RollupHub State"
echo "================================"
echo -n "  Owner: "
query_contract "$ROLLUP_HUB" "getOwner" ""
echo -n "  Governance Controller: "
query_contract "$ROLLUP_HUB" "getGovernanceController" ""
echo -n "  Shared Bridge: "
query_contract "$ROLLUP_HUB" "getSharedBridge" ""
echo -n "  Chain Count: "
query_contract "$ROLLUP_HUB" "getChainCount" ""
echo ""

echo "[2] Validating SharedBridge State"
echo "=================================="
echo -n "  Owner: "
query_contract "$SHARED_BRIDGE" "getOwner" ""
echo -n "  Settlement Manager: "
query_contract "$SHARED_BRIDGE" "getSettlementManager" ""
echo -n "  Emergency Manager: "
query_contract "$SHARED_BRIDGE" "getEmergencyManager" ""
echo -n "  Governance Locked: "
query_contract "$SHARED_BRIDGE" "isGovernanceLocked" ""
echo ""

echo "[3] Validating GovernanceController State"
echo "=========================================="
echo -n "  Council Count: "
query_contract "$GOVERNANCE" "getCouncilCount" ""
echo -n "  Threshold: "
query_contract "$GOVERNANCE" "getThreshold" ""
echo -n "  Proposal Count: "
query_contract "$GOVERNANCE" "getProposalCount" ""
echo ""

echo "[4] Validating ZkVerifier State"
echo "================================"
echo -n "  SP1 VK Registered: "
query_contract "$ZK_VERIFIER" "isVerificationKeyRegistered" ", [1]"
echo -n "  SP1 Proof Verifier: "
query_contract "$ZK_VERIFIER" "getProofVerifier" ", [1]"
echo -n "  SP1 Config Locked: "
query_contract "$ZK_VERIFIER" "isProofSystemConfigurationLocked" ", [1]"
echo ""

echo "═══════════════════════════════════════════════════════════════"
echo "Validation complete!"
echo ""
