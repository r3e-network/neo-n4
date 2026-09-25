#!/bin/bash
# Neo N4 Official Elastic Chains - Comprehensive Test Suite
# Tests: Configuration, Genesis, Integration, Performance

set -e

# Find official-chains directory
if [ -d "official-chains" ]; then
    CHAINS_DIR="official-chains"
elif [ -d "../official-chains" ]; then
    CHAINS_DIR="../official-chains"
else
    echo "Error: official-chains directory not found"
    echo "Please run deploy-official-elastic-chains.sh first"
    exit 1
fi

cd "$CHAINS_DIR"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m'

# Test counters
TOTAL_TESTS=0
PASSED_TESTS=0
FAILED_TESTS=0

# Test categories
CONFIG_TESTS=0
GENESIS_TESTS=0
INTEGRATION_TESTS=0
PERFORMANCE_TESTS=0

run_test() {
    local category=$1
    local test_name=$2
    local test_command=$3

    ((TOTAL_TESTS++))

    echo -n "  [$category] $test_name... "

    if eval "$test_command" >/dev/null 2>&1; then
        echo -e "${GREEN}✅ PASS${NC}"
        ((PASSED_TESTS++))
        return 0
    else
        echo -e "${RED}❌ FAIL${NC}"
        ((FAILED_TESTS++))
        return 1
    fi
}

echo -e "${BLUE}╔═══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║     Neo N4 Official Elastic Chains - Test Suite              ║${NC}"
echo -e "${BLUE}╚═══════════════════════════════════════════════════════════════╝${NC}"
echo ""

# Test Suite 1: Configuration Tests
echo -e "${GREEN}[Test Suite 1/4] Configuration Tests${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

for chain_id in 100 200 300 400 500 600 700; do
    config_file="chain-${chain_id}/chain.config.json"

    # Test: Config file exists
    run_test "CONFIG" "Chain $chain_id config exists" "test -f $config_file"
    ((CONFIG_TESTS++))

    if [ -f "$config_file" ]; then
        # Test: Valid JSON
        run_test "CONFIG" "Chain $chain_id valid JSON" "jq empty $config_file"
        ((CONFIG_TESTS++))

        # Test: Required fields present
        run_test "CONFIG" "Chain $chain_id has chainId" "jq -e '.chainId' $config_file"
        run_test "CONFIG" "Chain $chain_id has template" "jq -e '.template' $config_file"
        run_test "CONFIG" "Chain $chain_id has chainMode" "jq -e '.chainMode' $config_file"
        run_test "CONFIG" "Chain $chain_id has daMode" "jq -e '.daMode' $config_file"
        run_test "CONFIG" "Chain $chain_id has proofType" "jq -e '.proofType' $config_file"
        ((CONFIG_TESTS+=5))

        # Test: Chain ID matches directory
        run_test "CONFIG" "Chain $chain_id ID matches" "jq -e \".chainId == $chain_id\" $config_file"
        ((CONFIG_TESTS++))

        # Test: Template is valid
        run_test "CONFIG" "Chain $chain_id valid template" "
            template=\$(jq -r '.template' $config_file)
            [[ \"\$template\" =~ ^(dex|gaming|defi|social|nft|payment|enterprise)$ ]]
        "
        ((CONFIG_TESTS++))
    fi
done

# Test: All chain IDs are unique
run_test "CONFIG" "All chain IDs unique" "
    cat chain-*/chain.config.json 2>/dev/null | \
    jq -r '.chainId' | \
    sort | uniq -d | \
    wc -l | \
    grep -q '^0$'
"
((CONFIG_TESTS++))

# Test: Expected templates assigned correctly
run_test "CONFIG" "DEX template for chain 100" "jq -e '.template == \"dex\"' chain-100/chain.config.json"
run_test "CONFIG" "Gaming template for chain 200" "jq -e '.template == \"gaming\"' chain-200/chain.config.json"
run_test "CONFIG" "DeFi template for chain 300" "jq -e '.template == \"defi\"' chain-300/chain.config.json"
run_test "CONFIG" "Social template for chain 400" "jq -e '.template == \"social\"' chain-400/chain.config.json"
run_test "CONFIG" "NFT template for chain 500" "jq -e '.template == \"nft\"' chain-500/chain.config.json"
run_test "CONFIG" "Payment template for chain 600" "jq -e '.template == \"payment\"' chain-600/chain.config.json"
run_test "CONFIG" "Enterprise template for chain 700" "jq -e '.template == \"enterprise\"' chain-700/chain.config.json"
((CONFIG_TESTS+=7))

echo ""

# Test Suite 2: Genesis State Tests
echo -e "${GREEN}[Test Suite 2/4] Genesis State Tests${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

for chain_id in 100 200 300 400 500 600 700; do
    manifest_file="chain-${chain_id}/genesis-manifest.json"

    # Test: Genesis manifest exists
    run_test "GENESIS" "Chain $chain_id genesis exists" "test -f $manifest_file"
    ((GENESIS_TESTS++))

    if [ -f "$manifest_file" ]; then
        # Test: Valid JSON
        run_test "GENESIS" "Chain $chain_id manifest valid JSON" "jq empty $manifest_file"
        ((GENESIS_TESTS++))

        # Test: Has initialStateRoot
        run_test "GENESIS" "Chain $chain_id has state root" "jq -e '.initialStateRoot' $manifest_file"
        ((GENESIS_TESTS++))

        # Test: State root format (0x + 64 hex chars)
        run_test "GENESIS" "Chain $chain_id state root format" "
            state_root=\$(jq -r '.initialStateRoot' $manifest_file)
            [[ \$state_root =~ ^0x[0-9a-fA-F]{64}$ ]]
        "
        ((GENESIS_TESTS++))

        # Test: Has store field
        run_test "GENESIS" "Chain $chain_id has store field" "jq -e '.store' $manifest_file"
        ((GENESIS_TESTS++))
    fi
done

# Test: All state roots are unique
run_test "GENESIS" "All state roots unique" "
    cat chain-*/genesis-manifest.json 2>/dev/null | \
    jq -r '.initialStateRoot' | \
    sort | uniq -d | \
    wc -l | \
    grep -q '^0$'
"
((GENESIS_TESTS++))

# Test: State directories exist
for chain_id in 100 200 300 400 500 600 700; do
    run_test "GENESIS" "Chain $chain_id state dir exists" "test -d chain-${chain_id}/data/state"
    ((GENESIS_TESTS++))
done

echo ""

# Test Suite 3: Integration Tests
echo -e "${GREEN}[Test Suite 3/4] Integration Tests${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Test: Registration plans created
for chain_id in 100 200 300 400 500 600 700; do
    run_test "INTEGRATION" "Chain $chain_id registration plan" "test -f chain-${chain_id}/registration-plan.json"
    ((INTEGRATION_TESTS++))
done

# Test: DA mode consistency
run_test "INTEGRATION" "DEX uses NeoFS DA" "jq -e '.daMode == \"NeoFS\"' chain-100/chain.config.json"
run_test "INTEGRATION" "Gaming uses NeoFS DA" "jq -e '.daMode == \"NeoFS\"' chain-200/chain.config.json"
run_test "INTEGRATION" "DeFi uses L1 DA" "jq -e '.daMode == \"L1\"' chain-300/chain.config.json"
run_test "INTEGRATION" "Social uses NeoFS DA" "jq -e '.daMode == \"NeoFS\"' chain-400/chain.config.json"
run_test "INTEGRATION" "NFT uses NeoFS DA" "jq -e '.daMode == \"NeoFS\"' chain-500/chain.config.json"
run_test "INTEGRATION" "Payment uses L1 DA" "jq -e '.daMode == \"L1\"' chain-600/chain.config.json"
run_test "INTEGRATION" "Enterprise uses NeoFS DA" "jq -e '.daMode == \"NeoFS\"' chain-700/chain.config.json"
((INTEGRATION_TESTS+=7))

# Test: Proof type consistency
run_test "INTEGRATION" "DEX uses ZK proofs" "jq -e '.proofType == \"Zk\"' chain-100/chain.config.json"
run_test "INTEGRATION" "DeFi uses ZK proofs" "jq -e '.proofType == \"Zk\"' chain-300/chain.config.json"
run_test "INTEGRATION" "Enterprise uses Multisig" "jq -e '.proofType == \"Multisig\"' chain-700/chain.config.json"
((INTEGRATION_TESTS+=3))

# Test: Security level consistency
run_test "INTEGRATION" "DeFi has Validity security" "jq -e '.securityLevel == \"Validity\"' chain-300/chain.config.json"
run_test "INTEGRATION" "Payment has Validity security" "jq -e '.securityLevel == \"Validity\"' chain-600/chain.config.json"
run_test "INTEGRATION" "Enterprise has Sidechain security" "jq -e '.securityLevel == \"Sidechain\"' chain-700/chain.config.json"
((INTEGRATION_TESTS+=3))

# Test: Gateway enabled for cross-chain chains
run_test "INTEGRATION" "DEX gateway enabled" "jq -e '.gatewayEnabled == true' chain-100/chain.config.json"
run_test "INTEGRATION" "Gaming gateway enabled" "jq -e '.gatewayEnabled == true' chain-200/chain.config.json"
run_test "INTEGRATION" "DeFi gateway enabled" "jq -e '.gatewayEnabled == true' chain-300/chain.config.json"
((INTEGRATION_TESTS+=3))

echo ""

# Test Suite 4: Performance Configuration Tests
echo -e "${GREEN}[Test Suite 4/4] Performance Configuration Tests${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Test: Chain modes are correct
run_test "PERFORMANCE" "DEX uses Validium mode" "jq -e '.chainMode == \"L2ValidiumMode\"' chain-100/chain.config.json"
run_test "PERFORMANCE" "Gaming uses Rollup mode" "jq -e '.chainMode == \"L2RollupMode\"' chain-200/chain.config.json"
run_test "PERFORMANCE" "DeFi uses Rollup mode" "jq -e '.chainMode == \"L2RollupMode\"' chain-300/chain.config.json"
run_test "PERFORMANCE" "Social uses Rollup mode" "jq -e '.chainMode == \"L2RollupMode\"' chain-400/chain.config.json"
run_test "PERFORMANCE" "NFT uses Validium mode" "jq -e '.chainMode == \"L2ValidiumMode\"' chain-500/chain.config.json"
run_test "PERFORMANCE" "Payment uses Rollup mode" "jq -e '.chainMode == \"L2RollupMode\"' chain-600/chain.config.json"
run_test "PERFORMANCE" "Enterprise uses Sidechain mode" "jq -e '.chainMode == \"SidechainMode\"' chain-700/chain.config.json"
((PERFORMANCE_TESTS+=7))

# Test: Exit models are appropriate
run_test "PERFORMANCE" "DEX has Delayed exit" "jq -e '.exitModel == \"Delayed\"' chain-100/chain.config.json"
run_test "PERFORMANCE" "DeFi has Permissionless exit" "jq -e '.exitModel == \"Permissionless\"' chain-300/chain.config.json"
run_test "PERFORMANCE" "Payment has Permissionless exit" "jq -e '.exitModel == \"Permissionless\"' chain-600/chain.config.json"
run_test "PERFORMANCE" "Enterprise has OperatorAssisted exit" "jq -e '.exitModel == \"OperatorAssisted\"' chain-700/chain.config.json"
((PERFORMANCE_TESTS+=4))

# Test: Sequencer model is dBFT
for chain_id in 100 200 300 400 500 600 700; do
    run_test "PERFORMANCE" "Chain $chain_id uses dBFT" "jq -e '.sequencerModel == \"DbftCommittee\"' chain-${chain_id}/chain.config.json"
    ((PERFORMANCE_TESTS++))
done

echo ""

# Summary Report
echo -e "${BLUE}╔═══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║                      Test Summary                             ║${NC}"
echo -e "${BLUE}╚═══════════════════════════════════════════════════════════════╝${NC}"
echo ""

echo "📊 Overall Statistics:"
echo "  Total tests run: $TOTAL_TESTS"
echo -e "  ${GREEN}Passed: $PASSED_TESTS${NC}"
echo -e "  ${RED}Failed: $FAILED_TESTS${NC}"
echo ""

echo "📋 Test Categories:"
echo "  Configuration tests: $CONFIG_TESTS"
echo "  Genesis state tests: $GENESIS_TESTS"
echo "  Integration tests: $INTEGRATION_TESTS"
echo "  Performance tests: $PERFORMANCE_TESTS"
echo ""

if [ $FAILED_TESTS -eq 0 ]; then
    echo -e "${GREEN}✅ All tests passed! Official Elastic Chains are ready.${NC}"
    echo ""
    echo "🎉 Success Rate: 100% ($PASSED_TESTS/$TOTAL_TESTS)"
    exit 0
else
    echo -e "${RED}❌ Some tests failed. Please review the output above.${NC}"
    echo ""
    echo "📉 Success Rate: $(( PASSED_TESTS * 100 / TOTAL_TESTS ))% ($PASSED_TESTS/$TOTAL_TESTS)"
    exit 1
fi
