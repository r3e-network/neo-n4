#!/bin/bash
# Neo N4 Official Elastic Chains - Complete Deployment Script
# Handles: Processing, Deployment, Validation, Testing

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
TESTNET_RPC="https://testnet1.neo.coz.io:443"
TESTNET_MAGIC=894710606
ROLLUP_HUB="0x438786f19b73519714decc8268287aad3c4e6c3e"

# Official Elastic Chains Configuration
declare -A CHAINS=(
    ["dex"]="100:NeoSwap Chain:10000:100"
    ["gaming"]="200:NeoGame Chain:50000:500"
    ["defi"]="300:NeoFi Chain:5000:2000"
    ["social"]="400:NeoSocial Chain:100000:200"
    ["nft"]="500:NeoNFT Chain:20000:1000"
    ["payment"]="600:NeoPayment Chain:10000:1000"
    ["enterprise"]="700:NeoEnterprise Chain:5000:2000"
)

# Parse chain config: chainId:name:tps:blockTime
parse_config() {
    local config=$1
    echo "$config" | tr ':' ' '
}

echo -e "${BLUE}╔═══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║   Neo N4 Official Elastic Chains - Complete Deployment       ║${NC}"
echo -e "${BLUE}╚═══════════════════════════════════════════════════════════════╝${NC}"
echo ""

# Phase 1: Processing - Create chain configurations
echo -e "${GREEN}[Phase 1/4] Processing - Creating Chain Configurations${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

mkdir -p official-chains
cd official-chains

for template in "${!CHAINS[@]}"; do
    IFS=' ' read -r chain_id name tps block_time <<< "$(parse_config "${CHAINS[$template]}")"

    echo -e "\n${YELLOW}Creating $name (Chain ID: $chain_id)${NC}"

    # Create chain directory
    chain_dir="chain-${chain_id}"

    if [ -d "$chain_dir" ]; then
        echo "  ⚠️  Directory exists, cleaning..."
        rm -rf "$chain_dir"
    fi

    # Create chain configuration using CLI
    echo "  📝 Generating configuration..."
    dotnet run --project ../tools/Neo.Stack.Cli -- create-chain \
        --chain-id "$chain_id" \
        --template "$template" \
        --output "$chain_dir" \
        2>&1 | grep -E "(Created|chain.config.json|Template)" || true

    if [ -f "$chain_dir/chain.config.json" ]; then
        echo -e "  ✅ Configuration created: $chain_dir/chain.config.json"
    else
        echo -e "  ${RED}❌ Failed to create configuration${NC}"
        exit 1
    fi
done

echo -e "\n${GREEN}✅ Phase 1 Complete: All configurations created${NC}"

# Phase 2: Deployment - Bootstrap genesis and register chains
echo -e "\n${GREEN}[Phase 2/4] Deployment - Bootstrap Genesis & Register Chains${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

for template in "${!CHAINS[@]}"; do
    IFS=' ' read -r chain_id name tps block_time <<< "$(parse_config "${CHAINS[$template]}")"
    chain_dir="chain-${chain_id}"

    echo -e "\n${YELLOW}Deploying $name${NC}"

    # Bootstrap genesis state
    echo "  🔧 Bootstrapping genesis state..."
    dotnet run --project ../tools/Neo.Stack.Cli -- bootstrap-genesis \
        --chain-id "$chain_id" \
        --output "$chain_dir" \
        2>&1 | grep -E "(Bootstrapped|initialStateRoot|manifest)" || true

    if [ -f "$chain_dir/genesis-manifest.json" ]; then
        genesis_root=$(grep -oP '"initialStateRoot":\s*"\K[^"]+' "$chain_dir/genesis-manifest.json" | head -1)
        echo -e "  ✅ Genesis state root: ${genesis_root:0:16}..."
    else
        echo -e "  ${RED}❌ Failed to bootstrap genesis${NC}"
        exit 1
    fi

    # Create registration plan (simulated - actual registration requires wallet)
    echo "  📋 Creating registration plan..."
    cat > "$chain_dir/registration-plan.json" <<EOF
{
  "chainId": $chain_id,
  "chainName": "$name",
  "template": "$template",
  "genesisStateRoot": "$genesis_root",
  "rollupHub": "$ROLLUP_HUB",
  "network": "testnet",
  "status": "ready_for_registration"
}
EOF
    echo -e "  ✅ Registration plan created"
done

echo -e "\n${GREEN}✅ Phase 2 Complete: All chains ready for registration${NC}"

# Phase 3: Validation - Verify configurations and genesis states
echo -e "\n${GREEN}[Phase 3/4] Validation - Verify Configurations${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

validation_passed=0
validation_failed=0

for template in "${!CHAINS[@]}"; do
    IFS=' ' read -r chain_id name tps block_time <<< "$(parse_config "${CHAINS[$template]}")"
    chain_dir="chain-${chain_id}"

    echo -e "\n${YELLOW}Validating $name${NC}"

    # Validate configuration file
    config_file="$chain_dir/chain.config.json"
    if [ -f "$config_file" ]; then
        echo "  ✅ Configuration file exists"

        # Validate JSON structure
        if jq empty "$config_file" 2>/dev/null; then
            echo "  ✅ Configuration is valid JSON"

            # Check required fields
            required_fields=("chainId" "template" "chainMode" "daMode" "proofType")
            all_present=true

            for field in "${required_fields[@]}"; do
                if jq -e ".$field" "$config_file" >/dev/null 2>&1; then
                    echo "  ✅ Field '$field' present"
                else
                    echo -e "  ${RED}❌ Field '$field' missing${NC}"
                    all_present=false
                fi
            done

            if [ "$all_present" = true ]; then
                ((validation_passed++))
            else
                ((validation_failed++))
            fi
        else
            echo -e "  ${RED}❌ Invalid JSON format${NC}"
            ((validation_failed++))
        fi
    else
        echo -e "  ${RED}❌ Configuration file missing${NC}"
        ((validation_failed++))
    fi

    # Validate genesis manifest
    manifest_file="$chain_dir/genesis-manifest.json"
    if [ -f "$manifest_file" ]; then
        echo "  ✅ Genesis manifest exists"

        if jq -e '.initialStateRoot' "$manifest_file" >/dev/null 2>&1; then
            state_root=$(jq -r '.initialStateRoot' "$manifest_file")
            if [[ $state_root =~ ^0x[0-9a-fA-F]{64}$ ]]; then
                echo "  ✅ Valid state root format"
            else
                echo -e "  ${RED}❌ Invalid state root format${NC}"
            fi
        fi
    else
        echo -e "  ${RED}❌ Genesis manifest missing${NC}"
    fi

    # Validate registration plan
    if [ -f "$chain_dir/registration-plan.json" ]; then
        echo "  ✅ Registration plan created"
    fi
done

echo -e "\n${GREEN}✅ Phase 3 Complete: Validation Results${NC}"
echo "  Passed: $validation_passed chains"
echo "  Failed: $validation_failed chains"

if [ $validation_failed -gt 0 ]; then
    echo -e "${RED}❌ Validation failed for some chains${NC}"
    exit 1
fi

# Phase 4: Testing - Run integration tests
echo -e "\n${GREEN}[Phase 4/4] Testing - Integration Tests${NC}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# Create test suite
cat > ../test-elastic-chains.sh <<'TESTEOF'
#!/bin/bash
# Integration tests for official elastic chains

test_passed=0
test_failed=0

run_test() {
    local test_name=$1
    local test_command=$2

    echo -n "  Testing: $test_name... "
    if eval "$test_command" >/dev/null 2>&1; then
        echo "✅"
        ((test_passed++))
    else
        echo "❌"
        ((test_failed++))
    fi
}

echo "Running integration tests..."

# Test 1: Configuration files parseable
for config in official-chains/chain-*/chain.config.json; do
    chain=$(basename $(dirname "$config"))
    run_test "Parse $chain config" "jq empty $config"
done

# Test 2: Genesis manifests valid
for manifest in official-chains/chain-*/genesis-manifest.json; do
    chain=$(basename $(dirname "$manifest"))
    run_test "Validate $chain genesis" "jq -e '.initialStateRoot' $manifest"
done

# Test 3: State roots are unique
run_test "State roots unique" "
    cat official-chains/chain-*/genesis-manifest.json | \
    jq -r '.initialStateRoot' | \
    sort | uniq -d | \
    wc -l | \
    grep -q '^0$'
"

# Test 4: Chain IDs are unique
run_test "Chain IDs unique" "
    cat official-chains/chain-*/chain.config.json | \
    jq -r '.chainId' | \
    sort | uniq -d | \
    wc -l | \
    grep -q '^0$'
"

# Test 5: Templates match expected values
run_test "Template assignments" "
    grep -q '\"template\": \"dex\"' official-chains/chain-100/chain.config.json && \
    grep -q '\"template\": \"gaming\"' official-chains/chain-200/chain.config.json && \
    grep -q '\"template\": \"defi\"' official-chains/chain-300/chain.config.json
"

echo ""
echo "Test Results:"
echo "  Passed: $test_passed"
echo "  Failed: $test_failed"
echo ""

if [ $test_failed -eq 0 ]; then
    echo "✅ All tests passed!"
    exit 0
else
    echo "❌ Some tests failed"
    exit 1
fi
TESTEOF

chmod +x ../test-elastic-chains.sh

echo "  🧪 Running test suite..."
if bash ../test-elastic-chains.sh; then
    echo -e "${GREEN}✅ All integration tests passed${NC}"
else
    echo -e "${RED}❌ Some tests failed${NC}"
    exit 1
fi

echo -e "\n${GREEN}✅ Phase 4 Complete: All tests passed${NC}"

# Final summary
echo ""
echo -e "${BLUE}╔═══════════════════════════════════════════════════════════════╗${NC}"
echo -e "${BLUE}║                   Deployment Summary                          ║${NC}"
echo -e "${BLUE}╚═══════════════════════════════════════════════════════════════╝${NC}"
echo ""

echo -e "${GREEN}✅ All 4 Phases Complete!${NC}"
echo ""
echo "📊 Deployment Statistics:"
echo "  Total chains: ${#CHAINS[@]}"
echo "  Configurations created: ${#CHAINS[@]}"
echo "  Genesis states bootstrapped: ${#CHAINS[@]}"
echo "  Validation passed: $validation_passed"
echo "  Tests passed: ✅"
echo ""

echo "📁 Output Directory: ./official-chains/"
echo ""

echo "📋 Created Chains:"
for template in "${!CHAINS[@]}"; do
    IFS=' ' read -r chain_id name tps block_time <<< "$(parse_config "${CHAINS[$template]}")"
    echo "  ├─ Chain $chain_id: $name ($template template)"
done

echo ""
echo -e "${YELLOW}⚠️  Note: Actual on-chain registration requires:${NC}"
echo "  1. Deployer wallet with testnet GAS"
echo "  2. Execute registration transactions"
echo "  3. Wait for L1 confirmation"
echo ""

echo -e "${GREEN}📝 Next Steps:${NC}"
echo "  1. Review configurations: ls official-chains/chain-*/chain.config.json"
echo "  2. Check genesis states: ls official-chains/chain-*/genesis-manifest.json"
echo "  3. Manual registration: neo-stack register-chain --chain-id <id>"
echo "  4. Start sequencers: neo-stack start-sequencer --chain-id <id>"
echo ""

echo -e "${GREEN}✅ Official Elastic Chains Ready for Production Deployment!${NC}"
