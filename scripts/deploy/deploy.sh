#!/bin/bash
# Neo N4 Automated Deployment Script
# Version: 1.0
# Last Updated: 2026-10-01
#
# Usage:
#   ./deploy.sh --environment [testnet|mainnet] --components [all|contracts|sequencer|...]
#
# Prerequisites:
#   - Neo CLI installed
#   - AWS CLI configured (for infrastructure)
#   - Docker installed (for services)
#   - Proper credentials in environment variables

set -euo pipefail

# Color codes for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
LOG_DIR="$PROJECT_ROOT/logs/deploy"
TIMESTAMP=$(date +%Y%m%d-%H%M%S)
LOG_FILE="$LOG_DIR/deploy-$TIMESTAMP.log"

mkdir -p "$LOG_DIR"

# Logging functions
log_info() {
    echo -e "${BLUE}[INFO]${NC} $1" | tee -a "$LOG_FILE"
}

log_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1" | tee -a "$LOG_FILE"
}

log_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1" | tee -a "$LOG_FILE"
}

log_error() {
    echo -e "${RED}[ERROR]${NC} $1" | tee -a "$LOG_FILE"
}

# Error handler
error_exit() {
    log_error "$1"
    log_error "Deployment failed. Check $LOG_FILE for details."
    exit 1
}

trap 'error_exit "Unexpected error occurred at line $LINENO"' ERR

# Parse command line arguments
ENVIRONMENT=""
COMPONENTS="all"
DRY_RUN=false
SKIP_TESTS=false

while [[ $# -gt 0 ]]; do
    case $1 in
        --environment|-e)
            ENVIRONMENT="$2"
            shift 2
            ;;
        --components|-c)
            COMPONENTS="$2"
            shift 2
            ;;
        --dry-run)
            DRY_RUN=true
            shift
            ;;
        --skip-tests)
            SKIP_TESTS=true
            shift
            ;;
        --help|-h)
            cat << EOF
Neo N4 Deployment Script

Usage:
  ./deploy.sh --environment <env> [options]

Required:
  --environment, -e    Target environment (testnet|mainnet)

Optional:
  --components, -c     Components to deploy (default: all)
                       Options: all, contracts, sequencer, prover, rpc, monitoring
  --dry-run           Simulate deployment without making changes
  --skip-tests        Skip pre-deployment tests
  --help, -h          Show this help message

Examples:
  ./deploy.sh --environment testnet
  ./deploy.sh --environment mainnet --components contracts --dry-run
  ./deploy.sh -e testnet -c sequencer,prover

Components:
  - contracts:   L1 smart contracts (RollupHub, SharedBridge, etc.)
  - sequencer:   L2 sequencer service
  - prover:      ZK proof generator
  - rpc:         RPC nodes
  - monitoring:  Metrics and alerting infrastructure
  - all:         Deploy everything

EOF
            exit 0
            ;;
        *)
            error_exit "Unknown option: $1. Use --help for usage."
            ;;
    esac
done

# Validate arguments
if [[ -z "$ENVIRONMENT" ]]; then
    error_exit "Environment is required. Use --environment testnet or --environment mainnet"
fi

if [[ "$ENVIRONMENT" != "testnet" && "$ENVIRONMENT" != "mainnet" ]]; then
    error_exit "Invalid environment: $ENVIRONMENT. Must be 'testnet' or 'mainnet'"
fi

# Banner
cat << 'EOF'
╔═══════════════════════════════════════╗
║     Neo N4 Deployment Manager        ║
╚═══════════════════════════════════════╝
EOF

log_info "Environment: $ENVIRONMENT"
log_info "Components: $COMPONENTS"
log_info "Dry Run: $DRY_RUN"
log_info "Log File: $LOG_FILE"
echo ""

# Pre-flight checks
log_info "Running pre-flight checks..."

# Check required tools
check_tool() {
    if ! command -v "$1" &> /dev/null; then
        error_exit "$1 is not installed or not in PATH"
    fi
}

check_tool "docker"
check_tool "git"
check_tool "neo-cli" || log_warning "neo-cli not found (may be optional)"
check_tool "aws" || log_warning "AWS CLI not found (may be optional)"

# Check environment variables
if [[ "$ENVIRONMENT" == "mainnet" ]]; then
    required_vars=(
        "NEO_MAINNET_RPC"
        "DEPLOYER_PRIVATE_KEY"
        "MULTISIG_ADDRESSES"
    )
    
    for var in "${required_vars[@]}"; do
        if [[ -z "${!var:-}" ]]; then
            error_exit "Required environment variable $var is not set"
        fi
    done
fi

log_success "Pre-flight checks passed"
echo ""

# Load configuration
CONFIG_FILE="$PROJECT_ROOT/scripts/deploy/config-$ENVIRONMENT.json"
if [[ ! -f "$CONFIG_FILE" ]]; then
    error_exit "Configuration file not found: $CONFIG_FILE"
fi

log_info "Loaded configuration: $CONFIG_FILE"

# Build components
build_components() {
    log_info "Building components..."
    
    if [[ "$SKIP_TESTS" == false ]]; then
        log_info "Running tests..."
        cd "$PROJECT_ROOT"
        dotnet test --configuration Release --no-build || error_exit "Tests failed"
        log_success "All tests passed"
    else
        log_warning "Skipping tests (--skip-tests flag used)"
    fi
    
    log_info "Building contracts..."
    cd "$PROJECT_ROOT/contracts"
    dotnet build --configuration Release || error_exit "Contract build failed"
    
    log_info "Building services..."
    cd "$PROJECT_ROOT/src"
    dotnet build --configuration Release || error_exit "Service build failed"
    
    log_success "Build completed"
}

# Deploy smart contracts
deploy_contracts() {
    log_info "Deploying smart contracts to $ENVIRONMENT..."
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warning "[DRY RUN] Would deploy contracts (skipping actual deployment)"
        return
    fi
    
    # Deploy each contract
    local contracts=("RollupHub" "SharedBridge" "GovernanceController" "SettlementManager")
    
    for contract in "${contracts[@]}"; do
        log_info "Deploying $contract..."
        
        # Build deployment transaction
        neo-cli contract deploy \
            --contract "$PROJECT_ROOT/contracts/NeoHub.$contract/bin/Release/net8.0/$contract.nef" \
            --manifest "$PROJECT_ROOT/contracts/NeoHub.$contract/bin/Release/net8.0/$contract.manifest.json" \
            --rpc "$NEO_MAINNET_RPC" \
            --wallet "$DEPLOYER_WALLET" \
            || error_exit "Failed to deploy $contract"
        
        log_success "$contract deployed successfully"
    done
    
    log_success "All contracts deployed"
}

# Deploy sequencer
deploy_sequencer() {
    log_info "Deploying sequencer to $ENVIRONMENT..."
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warning "[DRY RUN] Would deploy sequencer (skipping actual deployment)"
        return
    fi
    
    # Build Docker image
    log_info "Building sequencer Docker image..."
    cd "$PROJECT_ROOT"
    docker build -t neo-n4-sequencer:latest -f src/Neo.Plugins.L2Batch/Dockerfile . \
        || error_exit "Failed to build sequencer image"
    
    # Push to registry (if configured)
    if [[ -n "${DOCKER_REGISTRY:-}" ]]; then
        log_info "Pushing to registry: $DOCKER_REGISTRY"
        docker tag neo-n4-sequencer:latest "$DOCKER_REGISTRY/neo-n4-sequencer:$TIMESTAMP"
        docker push "$DOCKER_REGISTRY/neo-n4-sequencer:$TIMESTAMP"
    fi
    
    # Deploy to infrastructure
    log_info "Deploying sequencer service..."
    kubectl apply -f "$PROJECT_ROOT/scripts/deploy/k8s/sequencer.yaml" \
        || error_exit "Failed to deploy sequencer to Kubernetes"
    
    log_success "Sequencer deployed"
}

# Deploy prover
deploy_prover() {
    log_info "Deploying prover to $ENVIRONMENT..."
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warning "[DRY RUN] Would deploy prover (skipping actual deployment)"
        return
    fi
    
    log_info "Building prover Docker image..."
    cd "$PROJECT_ROOT"
    docker build -t neo-n4-prover:latest -f src/Neo.Prover/Dockerfile . \
        || error_exit "Failed to build prover image"
    
    if [[ -n "${DOCKER_REGISTRY:-}" ]]; then
        docker tag neo-n4-prover:latest "$DOCKER_REGISTRY/neo-n4-prover:$TIMESTAMP"
        docker push "$DOCKER_REGISTRY/neo-n4-prover:$TIMESTAMP"
    fi
    
    log_info "Deploying prover service..."
    kubectl apply -f "$PROJECT_ROOT/scripts/deploy/k8s/prover.yaml" \
        || error_exit "Failed to deploy prover"
    
    log_success "Prover deployed"
}

# Deploy RPC nodes
deploy_rpc() {
    log_info "Deploying RPC nodes to $ENVIRONMENT..."
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warning "[DRY RUN] Would deploy RPC nodes (skipping)"
        return
    fi
    
    log_info "Deploying RPC nodes..."
    kubectl apply -f "$PROJECT_ROOT/scripts/deploy/k8s/rpc-nodes.yaml" \
        || error_exit "Failed to deploy RPC nodes"
    
    log_success "RPC nodes deployed"
}

# Deploy monitoring
deploy_monitoring() {
    log_info "Deploying monitoring infrastructure to $ENVIRONMENT..."
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warning "[DRY RUN] Would deploy monitoring (skipping)"
        return
    fi
    
    log_info "Deploying Prometheus..."
    kubectl apply -f "$PROJECT_ROOT/scripts/private-network/metrics/prometheus-deployment.yaml"
    
    log_info "Deploying Grafana..."
    kubectl apply -f "$PROJECT_ROOT/scripts/private-network/metrics/grafana-deployment.yaml"
    
    log_success "Monitoring deployed"
}

# Health check
health_check() {
    log_info "Running health checks..."
    
    local endpoints=(
        "http://localhost:10332/getblockcount"
        "http://localhost:9090/metrics"
    )
    
    for endpoint in "${endpoints[@]}"; do
        if curl -sf "$endpoint" > /dev/null; then
            log_success "✓ $endpoint is healthy"
        else
            log_warning "✗ $endpoint is not responding"
        fi
    done
}

# Main deployment flow
main() {
    log_info "Starting deployment to $ENVIRONMENT..."
    echo ""
    
    # Build step
    build_components
    echo ""
    
    # Deploy based on components selection
    IFS=',' read -ra COMP_ARRAY <<< "$COMPONENTS"
    
    for component in "${COMP_ARRAY[@]}"; do
        case "$component" in
            all)
                deploy_contracts
                deploy_sequencer
                deploy_prover
                deploy_rpc
                deploy_monitoring
                ;;
            contracts)
                deploy_contracts
                ;;
            sequencer)
                deploy_sequencer
                ;;
            prover)
                deploy_prover
                ;;
            rpc)
                deploy_rpc
                ;;
            monitoring)
                deploy_monitoring
                ;;
            *)
                log_warning "Unknown component: $component (skipping)"
                ;;
        esac
        echo ""
    done
    
    # Post-deployment checks
    if [[ "$DRY_RUN" == false ]]; then
        health_check
    fi
    
    echo ""
    log_success "╔═══════════════════════════════════════╗"
    log_success "║   Deployment completed successfully!  ║"
    log_success "╚═══════════════════════════════════════╝"
    echo ""
    log_info "Deployment log saved to: $LOG_FILE"
    
    if [[ "$DRY_RUN" == true ]]; then
        log_warning "This was a DRY RUN. No actual changes were made."
    fi
}

# Run main deployment
main