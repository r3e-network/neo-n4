<!-- 本文档为 scripts/deploy/README.md 的中文版本。 -->

# Neo N4 部署脚本

Neo N4 Layer 2 Rollup 系统的自动化部署工具。

## 概述

本目录包含用于把 Neo N4 各组件部署到测试网和主网环境的脚本与配置。

## 前置条件

### 必需工具

- **Docker**（v20.10+）—— 容器运行时
- **kubectl**（v1.25+）—— Kubernetes CLI
- **AWS CLI**（v2.0+）—— AWS 基础设施管理
- **Neo CLI** —— 与 Neo 区块链交互
- **Git** —— 版本控制

### 环境变量

#### 测试网
```bash
export NEO_TESTNET_RPC="http://seed1.neo.org:10332"
export DEPLOYER_PRIVATE_KEY="your-private-key"
```

#### 主网（额外要求）
```bash
export NEO_MAINNET_RPC="http://seed1.neo.org:10332"
export DEPLOYER_PRIVATE_KEY="your-private-key"
export MULTISIG_ADDRESSES="address1,address2,address3,..."
export DOCKER_REGISTRY="123456789.dkr.ecr.us-east-1.amazonaws.com"
export AWS_REGION="us-east-1"
```

## 快速开始

### 部署到测试网（全部组件）

```bash
./deploy.sh --environment testnet
```

### 部署到主网（先做 dry run）

```bash
# Dry run to preview changes
./deploy.sh --environment mainnet --dry-run

# Actual deployment after review
./deploy.sh --environment mainnet
```

### 部署指定组件

```bash
# Deploy only smart contracts
./deploy.sh --environment testnet --components contracts

# Deploy sequencer and prover
./deploy.sh --environment mainnet --components sequencer,prover

# Deploy everything except monitoring
./deploy.sh --environment testnet --components contracts,sequencer,prover,rpc
```

## 组件

### 1. 智能合约（L1）

部署 Neo N3 智能合约：
- **RollupHub** —— 批次结算与链注册表
- **SharedBridge** —— 跨链资产桥
- **GovernanceController** —— 治理与访问控制
- **SettlementManager** —— 证明验证

**部署流程：**
1. 用 `dotnet build` 编译合约
2. 生成部署交易
3. 用部署者钱包签名
4. 提交到 L1 网络
5. 验证部署结果

### 2. Sequencer 服务

部署 L2 交易排序器：
- 处理用户交易
- 产出区块与批次
- 管理内存池与排序
- 向 L1 提交批次

**部署流程：**
1. 构建 Docker 镜像
2. 推送到容器镜像仓库
3. 部署到 Kubernetes 集群
4. 验证健康检查端点

### 3. Prover 服务

部署 ZK 证明生成：
- 为批次生成 SP1 证明
- 并行化证明生成
- 向 L1 提交证明

**基础设施：**
- AWS c6i.8xlarge 实例（启用 GPU）
- 依据批次队列深度自动扩缩容
- 冗余：4-8 个证明者实例

### 4. RPC 节点

部署 L2 RPC 端点：
- 公共 RPC API
- WebSocket 支持
- 负载均衡
- 速率限制

**端点：**
- `/getblockcount`
- `/sendrawtransaction`
- `/getbalance`
- 更多兼容 Neo 的 RPC 方法

### 5. 监控基础设施

部署可观测性栈：
- **Prometheus** —— 指标采集
- **Grafana** —— 仪表板
- **Alertmanager** —— 告警路由
- **Loki** —— 日志聚合（可选）

## 配置文件

### `config-testnet.json`
测试网部署配置：
- 较小的批次大小（500 笔交易）
- 更短的间隔（5 分钟）
- 更少的证明者实例（4）

### `config-mainnet.json`
生产配置：
- 更大的批次大小（1000 笔交易）
- 优化后的间隔（10 分钟）
- 更多证明者（8+）
- 速率限制与熔断器

## 部署流程

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

## 安全特性

### 多签要求（主网）
- 所有合约部署都需要 5-of-7 多签批准
- 执行前有 72 小时时间锁
- DAO 否决窗口（7 天）

### 回滚能力
- 部署前自动快照
- 前 30 天支持一键回滚
- 保留链上状态以供取证

### 速率限制（主网上线）
- 初始 TPS 上限：1,000
- 30 天内逐步提升
- 在 10,000 TPS 处设置熔断器

### TVL 上限（首月）
- SharedBridge 中最多锁定 $10M
- 超限自动暂停充值
- 根据稳定性逐步提升

## 故障排查

### 部署在合约部署阶段失败

**症状：** `Failed to deploy RollupHub`

**可能原因：**
- 部署者钱包的 GAS 不足
- 网络连接问题
- 合约编译错误

**解决方案：**
```bash
# Check wallet balance
neo-cli wallet balance

# Verify network connection
curl http://seed1.neo.org:10332

# Re-compile contracts
cd contracts && dotnet build --configuration Release
```

### Docker 镜像推送失败

**症状：** `Failed to push image to registry`

**解决方案：**
```bash
# Re-authenticate with AWS ECR
aws ecr get-login-password --region us-east-1 | \
    docker login --username AWS --password-stdin $DOCKER_REGISTRY

# Retry deployment
./deploy.sh --environment testnet --components sequencer
```

### Kubernetes 部署超时

**症状：** `Deployment timed out waiting for pods`

**解决方案：**
```bash
# Check pod status
kubectl get pods -n neo-n4-testnet

# View pod logs
kubectl logs -f <pod-name> -n neo-n4-testnet

# Describe pod for events
kubectl describe pod <pod-name> -n neo-n4-testnet
```

### 健康检查失败

**症状：** `✗ http://localhost:10332 is not responding`

**解决方案：**
```bash
# Check if service is running
docker ps | grep neo-n4

# Check service logs
docker logs <container-id>

# Port forwarding (if deployed to K8s)
kubectl port-forward svc/neo-n4-sequencer 10332:10332 -n neo-n4-testnet
```

## 进阶用法

### 自定义部署顺序

```bash
# Deploy in stages with validation between each
./deploy.sh -e testnet -c contracts
# Validate contracts...

./deploy.sh -e testnet -c sequencer
# Validate sequencer...

./deploy.sh -e testnet -c prover,rpc,monitoring
```

### 部分升级

```bash
# Upgrade only sequencer (keep contracts unchanged)
./deploy.sh -e mainnet -c sequencer --skip-tests
```

### 紧急回滚

```bash
# Rollback to previous version
./rollback.sh --environment mainnet --component sequencer --version previous

# Rollback specific deployment
./rollback.sh --environment mainnet --deployment-id deploy-20261001-123456
```

## 维护

### 常规更新

**每周：**
- 依赖的安全补丁
- 基础设施的操作系统更新
- 日志轮转与清理

**每月：**
- 合约参数调优
- 性能优化
- 容量规划评审

**每季度：**
- 大版本升级
- 架构评审
- 灾难恢复演练

## 安全考量

### 部署密钥

- **绝不把私钥提交**到版本控制
- 使用环境变量或密钥管理服务（AWS Secrets Manager、HashiCorp Vault）
- 每季度轮换密钥
- 主网多签使用硬件钱包

### 网络安全

- 部署到私有子网
- 使用安全组限制访问
- 仅为管理员访问启用 VPN
- 定期渗透测试

### 监控与告警

- 7×24 小时监控
- 关键告警接入 PagerDuty
- 常见问题的自动化事件响应
- 每季度对基础设施做安全审计

## 支持

- **文档：** https://docs.neo-n4.network/deployment
- **DevOps 团队：** ops@neo-n4.network
- **紧急热线：** [待建立]
- **状态页：** https://status.neo-n4.network

## 许可证

见项目根目录下的 [LICENSE](../../LICENSE) 文件。

---

**最后更新：** 2026 年 10 月 1 日  
**维护方：** Neo N4 DevOps 团队
