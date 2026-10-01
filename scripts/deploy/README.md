# Neo N4 Deployment Scripts

Automated deployment tooling for Neo N4 Layer 2 Rollup system.

## Overview

This directory contains scripts and configuration for deploying Neo N4 components to testnet and mainnet environments.

## Prerequisites

### Required Tools

- **Docker** (v20.10+) - Container runtime
- **kubectl** (v1.25+) - Kubernetes CLI
- **AWS CLI** (v2.0+) - AWS infrastructure management
- **Neo CLI** - Neo blockchain interaction
- **Git** - Version control

### Environment Variables

#### Testnet
```bash
export NEO_TESTNET_RPC="http://seed1.neo.org:10332"
export DEPLOYER_PRIVATE_KEY="your-private-key"
```

#### Mainnet (Additional Requirements)
```bash
export NEO_MAINNET_RPC="http://seed1.neo.org:10332"
export DEPLOYER_PRIVATE_KEY="your-private-key"
export MULTISIG_ADDRESSES="address1,address2,address3,..."
export DOCKER_REGISTRY="123456789.dkr.ecr.us-east-1.amazonaws.com"
export AWS_REGION="us-east-1"
```

## Quick Start

### Deploy to Testnet (All Components)

```bash
./deploy.sh --environment testnet
```

### Deploy to Mainnet (Dry Run First)

```bash
# Dry run to preview changes
./deploy.sh --environment mainnet --dry-run

# Actual deployment after review
./deploy.sh --environment mainnet
```

### Deploy Specific Components

```bash
# Deploy only smart contracts
./deploy.sh --environment testnet --components contracts

# Deploy sequencer and prover
./deploy.sh --environment mainnet --components sequencer,prover

# Deploy everything except monitoring
./deploy.sh --environment testnet --components contracts,sequencer,prover,rpc
```

## Components

### 1. Smart Contracts (L1)

Deploys Neo N3 smart contracts:
- **RollupHub** - Batch settlement and chain registry
- **SharedBridge** - Cross-chain asset bridge
- **GovernanceController** - Governance and access control
- **SettlementManager** - Proof verification

**Deployment Process:**
1. Compile contracts with `dotnet build`
2. Generate deployment transactions
3. Sign with deployer wallet
4. Submit to L1 network
5. Verify deployment

### 2. Sequencer Service

Deploys L2 transaction sequencer:
- Processes user transactions
- Produces blocks and batches
- Manages mempool and ordering
- Submits batches to L1

**Deployment Process:**
1. Build Docker image
2. Push to container registry
3. Deploy to Kubernetes cluster
4. Verify health endpoints

### 3. Prover Service

Deploys ZK proof generation:
- Generates SP1 proofs for batches
- Parallelized proof generation
- Proof submission to L1

**Infrastructure:**
- AWS c6i.8xlarge instances (GPU-enabled)
- Auto-scaling based on batch queue depth
- Redundancy: 4-8 prover instances

### 4. RPC Nodes

Deploys L2 RPC endpoints:
- Public RPC API
- WebSocket support
- Load balancing
- Rate limiting

**Endpoints:**
- `/getblockcount`
- `/sendrawtransaction`
- `/getbalance`
- More Neo-compatible RPC methods

### 5. Monitoring Infrastructure

Deploys observability stack:
- **Prometheus** - Metrics collection
- **Grafana** - Dashboards
- **Alertmanager** - Alert routing
- **Loki** - Log aggregation (optional)

## Configuration Files

### `config-testnet.json`
Configuration for testnet deployment:
- Lower batch sizes (500 tx)
- Shorter intervals (5 minutes)
- Fewer prover instances (4)

### `config-mainnet.json`
Production configuration:
- Larger batch sizes (1000 tx)
- Optimized intervals (10 minutes)
- More provers (8+)
- Rate limits and circuit breakers

## Deployment Flow

```
┌─────────────────────────────────────────────────────────────┐
│                    Deployment Pipeline                      │
└─────────────────────────────────────────────────────────────┘

1. Pre-flight Checks
   ├─ Verify tools installed
   ├─ Check environment variables
   ├─ Validate configuration files
   └─ Run pre-deployment tests

2. Build Phase
   ├─ Compile smart contracts
   ├─ Build Docker images
   ├─ Run unit tests
   └─ Generate deployment artifacts

3. Deployment Phase
   ├─ Deploy contracts to L1
   ├─ Deploy services to K8s
   ├─ Configure monitoring
   └─ Set up alerting

4. Verification Phase
   ├─ Health checks
   ├─ Integration tests
   ├─ Performance validation
   └─ Security verification

5. Post-Deployment
   ├─ Enable monitoring
   ├─ Notify team
   ├─ Update documentation
   └─ Create deployment report
```

## Safety Features

### Multi-Signature Requirement (Mainnet)
- All contract deployments require 5-of-7 multi-sig approval
- 72-hour timelock before execution
- DAO veto window (7 days)

### Rollback Capability
- Automatic snapshots before deployment
- One-click rollback for first 30 days
- Preserved on-chain state for forensics

### Rate Limiting (Mainnet Launch)
- Initial TPS limit: 1,000
- Gradual increase over 30 days
- Circuit breakers at 10,000 TPS

### TVL Cap (First Month)
- Maximum $10M locked in SharedBridge
- Automatic deposit pause if exceeded
- Gradual increase based on stability

## Troubleshooting

### Deployment Fails at Contract Deployment

**Symptom:** `Failed to deploy RollupHub`

**Possible Causes:**
- Insufficient GAS in deployer wallet
- Network connectivity issues
- Contract compilation errors

**Solution:**
```bash
# Check wallet balance
neo-cli wallet balance

# Verify network connection
curl http://seed1.neo.org:10332

# Re-compile contracts
cd contracts && dotnet build --configuration Release
```

### Docker Image Push Fails

**Symptom:** `Failed to push image to registry`

**Solution:**
```bash
# Re-authenticate with AWS ECR
aws ecr get-login-password --region us-east-1 | \
    docker login --username AWS --password-stdin $DOCKER_REGISTRY

# Retry deployment
./deploy.sh --environment testnet --components sequencer
```

### Kubernetes Deployment Timeout

**Symptom:** `Deployment timed out waiting for pods`

**Solution:**
```bash
# Check pod status
kubectl get pods -n neo-n4-testnet

# View pod logs
kubectl logs -f <pod-name> -n neo-n4-testnet

# Describe pod for events
kubectl describe pod <pod-name> -n neo-n4-testnet
```

### Health Checks Fail

**Symptom:** `✗ http://localhost:10332 is not responding`

**Solution:**
```bash
# Check if service is running
docker ps | grep neo-n4

# Check service logs
docker logs <container-id>

# Port forwarding (if deployed to K8s)
kubectl port-forward svc/neo-n4-sequencer 10332:10332 -n neo-n4-testnet
```

## Advanced Usage

### Custom Deployment Order

```bash
# Deploy in stages with validation between each
./deploy.sh -e testnet -c contracts
# Validate contracts...

./deploy.sh -e testnet -c sequencer
# Validate sequencer...

./deploy.sh -e testnet -c prover,rpc,monitoring
```

### Partial Upgrade

```bash
# Upgrade only sequencer (keep contracts unchanged)
./deploy.sh -e mainnet -c sequencer --skip-tests
```

### Emergency Rollback

```bash
# Rollback to previous version
./rollback.sh --environment mainnet --component sequencer --version previous

# Rollback specific deployment
./rollback.sh --environment mainnet --deployment-id deploy-20261001-123456
```

## Maintenance

### Regular Updates

**Weekly:**
- Security patches for dependencies
- OS updates on infrastructure
- Log rotation and cleanup

**Monthly:**
- Contract parameter tuning
- Performance optimization
- Capacity planning review

**Quarterly:**
- Major version upgrades
- Architecture review
- Disaster recovery drills

## Security Considerations

### Deployment Keys

- **Never commit private keys** to version control
- Use environment variables or secrets management (AWS Secrets Manager, HashiCorp Vault)
- Rotate keys quarterly
- Use hardware wallets for mainnet multi-sig

### Network Security

- Deploy to private subnets
- Use security groups to restrict access
- Enable VPN for admin access only
- Regular penetration testing

### Monitoring & Alerting

- 24/7 monitoring active
- PagerDuty integration for critical alerts
- Automated incident response for common issues
- Quarterly security audit of infrastructure

## Support

- **Documentation:** https://docs.neo-n4.network/deployment
- **DevOps Team:** ops@neo-n4.network
- **Emergency Hotline:** [To be established]
- **Status Page:** https://status.neo-n4.network

## License

See [LICENSE](../../LICENSE) file in project root.

---

**Last Updated:** October 1, 2026  
**Maintained By:** Neo N4 DevOps Team