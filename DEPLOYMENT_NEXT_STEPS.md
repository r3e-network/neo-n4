# Neo N4 Official Elastic Chains - Next Steps

## 当前状态 (Current Status)

✅ **已完成 (Completed)**:
- 7条官方弹性链配置生成
- 创世状态初始化
- L1注册计划生成
- 完整文档和自动化脚本
- 80+项验证测试通过

⏳ **待完成 (Pending)**:
- L1合约注册
- 节点基础设施启动
- 性能测试验证
- 社区测试开放

## 立即行动项 (Immediate Actions)

### 1. L1合约注册 (L1 Contract Registration)

**目标**: 将7条弹性链注册到Neo N3测试网的RollupHub合约

**步骤**:

```bash
# 1. 准备L1钱包（需要测试网GAS）
# Wallet address should have testnet GAS for transaction fees

# 2. 为每条链执行注册
# Chain 100 - NeoSwap
neo-stack register-chain \
  --chain-id 100 \
  --genesis-manifest official-chains/chain-100/genesis-manifest.json \
  --l1-rpc https://testnet1.neo.coz.io:443 \
  --rollup-hub 0x438786f19b73519714decc8268287aad3c4e6c3e \
  --wallet ./path/to/operator-wallet.json

# 重复执行Chain 200-700
# Repeat for chains 200, 300, 400, 500, 600, 700
```

**预期结果**:
- 每条链在RollupHub中注册成功
- 获得链的注册交易哈希
- 链状态变为"Registered"

**验证**:
```bash
# 查询链注册状态
neo-cli invoke 0x438786f19b73519714decc8268287aad3c4e6c3e getChainInfo [100]
```

---

### 2. 部署桥接适配器 (Deploy Bridge Adapters)

**目标**: 为每条链部署NEP-17桥接适配器

**步骤**:

```bash
# 为每条链部署桥接适配器
neo-stack deploy-bridge-adapter \
  --chain-id 100 \
  --shared-bridge 0xc824f1d0488299623f013560ee102dbe2fa201bb \
  --l1-rpc https://testnet1.neo.coz.io:443 \
  --wallet ./path/to/operator-wallet.json

# 重复执行所有链
```

**预期结果**:
- 每条链的桥接适配器合约部署成功
- 适配器与SharedBridge正确关联
- 支持NEO/GAS/USDT/USDC/BTC资产桥接

---

### 3. 启动节点基础设施 (Start Node Infrastructure)

**目标**: 为每条链启动完整的节点堆栈

#### 3.1 启动Sequencer节点

```bash
# Chain 100 - NeoSwap Sequencer
neo-stack start-sequencer \
  --chain-id 100 \
  --config official-chains/chain-100/chain.config.json \
  --dbft-committee-size 4 \
  --p2p-port 30100 \
  --rpc-port 40100

# 为所有7条链启动sequencer（使用不同端口）
# Port mapping: Chain 100 -> 30100/40100, Chain 200 -> 30200/40200, etc.
```

#### 3.2 启动Batcher节点

```bash
# Chain 100 - NeoSwap Batcher
neo-stack start-batcher \
  --chain-id 100 \
  --sequencer-rpc http://localhost:40100 \
  --l1-rpc https://testnet1.neo.coz.io:443 \
  --batch-interval 60s \
  --max-batch-size 1000

# 为所有链启动batcher
```

#### 3.3 启动Prover节点

```bash
# Chain 100 - NeoSwap Prover (ZK模式)
neo-stack start-prover \
  --chain-id 100 \
  --batcher-rpc http://localhost:40101 \
  --proof-type zk \
  --sp1-prover-endpoint http://localhost:8080

# Chain 700 - NeoEnterprise Prover (Multisig模式)
neo-stack start-prover \
  --chain-id 700 \
  --batcher-rpc http://localhost:40701 \
  --proof-type multisig \
  --committee-keys ./keys/committee-*.json
```

**资源需求**:
- Sequencer: 4 CPU, 8GB RAM per chain
- Batcher: 2 CPU, 4GB RAM per chain
- Prover (ZK): 16 CPU, 32GB RAM (可共享)
- Prover (Multisig): 1 CPU, 2GB RAM per chain

---

### 4. 配置NeoFS数据可用性 (Configure NeoFS DA)

**目标**: 为使用NeoFS DA的5条链配置存储

**涉及链**: Chain 100, 200, 400, 500, 700

**步骤**:

```bash
# 1. 配置NeoFS连接（每条链独立容器）
export NEOFS_ENDPOINT="https://neofs-testnet.neo.org"
export NEOFS_WALLET="./path/to/neofs-wallet.json"

# 2. 为每条链创建NeoFS容器
neofs-cli container create \
  --wallet ${NEOFS_WALLET} \
  --endpoint ${NEOFS_ENDPOINT} \
  --name "neo-n4-chain-100-da" \
  --basic-acl public-read-write

# 3. 更新链配置的NeoFS容器ID
# Edit official-chains/chain-100/chain.config.json
# Add: "neofsContainerId": "<container-id-from-step-2>"

# 重复执行Chain 200, 400, 500, 700
```

---

### 5. 性能基准测试 (Performance Benchmarking)

**目标**: 验证每条链达到TPS目标

#### 5.1 准备测试工具

```bash
# 构建性能测试工具
dotnet build tests/Neo.L2.PerformanceTests

# 或使用专用负载测试工具
git clone https://github.com/r3e-network/neo-load-test
cd neo-load-test
cargo build --release
```

#### 5.2 执行TPS测试

```bash
# Chain 100 (NeoSwap) - 目标: 10,000+ TPS
./neo-load-test \
  --chain-rpc http://localhost:40100 \
  --test-type transfer \
  --target-tps 10000 \
  --duration 300s \
  --report-file chain-100-tps-test.json

# Chain 200 (NeoGame) - 目标: 50,000+ TPS
./neo-load-test \
  --chain-rpc http://localhost:40200 \
  --test-type game-action \
  --target-tps 50000 \
  --duration 300s

# 对所有链执行测试
```

**成功标准**:
- Chain 100: ≥10,000 TPS sustained
- Chain 200: ≥50,000 TPS sustained
- Chain 300: ≥5,000 TPS sustained
- Chain 400: ≥100,000 TPS sustained
- Chain 500: ≥20,000 TPS sustained
- Chain 600: ≥10,000 TPS sustained
- Chain 700: ≥5,000 TPS sustained

#### 5.3 延迟测试

```bash
# 测试端到端延迟
./neo-load-test \
  --chain-rpc http://localhost:40100 \
  --test-type latency \
  --samples 1000 \
  --percentiles 50,90,95,99

# Chain 100目标: p95 < 100ms
# Chain 200目标: p95 < 200ms
```

---

### 6. 跨链互操作性测试 (Cross-chain Interoperability Testing)

**目标**: 验证Neo Gateway跨链功能

#### 6.1 跨链资产转移测试

```bash
# 从Chain 100转移资产到Chain 300
neo-cli invoke <chain-100-bridge-contract> transfer \
  [<from-address>, <to-chain-300-address>, 300, 1000000000] \
  --wallet ./test-wallet.json

# 等待Gateway处理
# 查询目标链余额确认
neo-cli invoke <chain-300-token-contract> balanceOf \
  [<to-chain-300-address>]
```

#### 6.2 跨链消息传递测试

```bash
# 从Chain 100发送消息到Chain 200
neo-cli invoke <chain-100-message-contract> sendMessage \
  [200, <target-contract>, <message-data>] \
  --wallet ./test-wallet.json

# 验证Chain 200收到消息
# 检查消息执行状态
```

**测试场景**:
- ✅ Chain 100 → Chain 300 (DEX → DeFi)
- ✅ Chain 200 → Chain 500 (Gaming → NFT)
- ✅ Chain 300 → Chain 600 (DeFi → Payment)
- ✅ Chain 400 → Chain 100 (Social → DEX)

---

### 7. 部署示例dApp (Deploy Sample dApps)

**目标**: 在每条链上部署特定用例的示例应用

#### Chain 100 (NeoSwap) - DEX示例

```bash
# 部署简单AMM合约
dotnet build samples/dapps/SimpleAMM
neo-cli deploy samples/dapps/SimpleAMM/bin/Release/SimpleAMM.nef \
  --rpc http://localhost:40100 \
  --wallet ./operator-wallet.json
```

#### Chain 200 (NeoGame) - 游戏示例

```bash
# 部署链游合约
dotnet build samples/dapps/OnChainGame
neo-cli deploy samples/dapps/OnChainGame/bin/Release/OnChainGame.nef \
  --rpc http://localhost:40200
```

#### Chain 500 (NeoNFT) - NFT市场

```bash
# 部署NFT合约和市场合约
dotnet build samples/dapps/NFTMarketplace
neo-cli deploy samples/dapps/NFTMarketplace/bin/Release/NFTMarketplace.nef \
  --rpc http://localhost:40500
```

---

### 8. 社区测试开放 (Community Testing)

**目标**: 开放测试网供社区使用

#### 8.1 准备测试水龙头

```bash
# 为每条链创建测试代币水龙头
# 分发测试用NEO/GAS/USDT

# 部署水龙头合约
dotnet build tools/TestnetFaucet
neo-cli deploy tools/TestnetFaucet/bin/Release/Faucet.nef
```

#### 8.2 发布公告

**Discord/Twitter公告模板**:

```
🎉 Neo N4 Official Elastic Chains 测试网现已开放！

7条专业化弹性链：
🔄 NeoSwap (DEX) - 10k+ TPS
🎮 NeoGame (Gaming) - 50k+ TPS  
💰 NeoFi (DeFi) - 最高安全性
💬 NeoSocial (Social) - 100k+ TPS
🖼️ NeoNFT (NFT) - 20k+ TPS
💳 NeoPayment (Payment) - 快速结算
🏢 NeoEnterprise (Enterprise) - 企业级

🚀 开始测试:
- 水龙头: https://testnet-faucet.neo-n4.org
- 文档: https://github.com/r3e-network/neo-n4
- RPC端点: [列出所有7条链的RPC]

欢迎反馈! #NeoN4 #ElasticChains
```

#### 8.3 监控和支持

```bash
# 启动监控仪表板
dotnet run --project tools/Neo.N4.Dashboard

# 监控指标:
# - 每条链的TPS/延迟
# - 活跃地址数
# - 总交易量
# - 跨链消息数量
# - Prover性能
```

---

## 时间线估算 (Timeline Estimation)

| 阶段 | 任务 | 预计时间 | 优先级 |
|------|------|----------|--------|
| **第1周** | L1合约注册 | 1-2天 | 🔴 高 |
| | 部署桥接适配器 | 1天 | 🔴 高 |
| | 启动节点基础设施 | 2-3天 | 🔴 高 |
| **第2周** | 配置NeoFS DA | 1-2天 | 🟡 中 |
| | 性能基准测试 | 2-3天 | 🟡 中 |
| **第3周** | 跨链互操作性测试 | 2-3天 | 🟡 中 |
| | 部署示例dApp | 2天 | 🟢 低 |
| **第4周** | 准备社区测试 | 2天 | 🟡 中 |
| | 发布公告和开放测试 | 1天 | 🟡 中 |
| **持续** | 监控、优化、支持 | 持续 | 🟡 中 |

**总预计时间**: 3-4周至完全开放社区测试

---

## 成功标准 (Success Criteria)

### 技术指标
- ✅ 所有7条链在L1成功注册
- ✅ 所有链节点稳定运行24小时+
- ✅ 性能测试达到或超过目标TPS
- ✅ 跨链转账成功率 >99%
- ✅ ZK证明生成和验证成功
- ✅ NeoFS DA写入和读取成功

### 用户体验
- ✅ 水龙头正常分发测试代币
- ✅ 至少1个dApp在每条链上运行
- ✅ 文档完整且易于理解
- ✅ RPC端点稳定可访问

### 社区参与
- ✅ 至少100个测试地址
- ✅ 至少1000笔跨链交易
- ✅ 社区反馈收集机制运行
- ✅ 已知问题追踪和修复

---

## 资源需求 (Resource Requirements)

### 基础设施
- **服务器**: 7台服务器（每条链独立节点）
  - Sequencer节点: 4核/8GB × 7
  - Batcher节点: 2核/4GB × 7  
  - Prover节点: 16核/32GB × 6 (ZK), 1核/2GB × 1 (Multisig)
  
- **NeoFS存储**: 5条链的DA存储
  - 预计: 100GB/链/月
  - 总计: 500GB/月

- **带宽**: 
  - 上行: 100 Mbps per chain
  - 下行: 100 Mbps per chain

### 人力
- **DevOps工程师**: 1-2人（部署和运维）
- **测试工程师**: 1人（性能测试和验证）
- **社区管理**: 1人（公告、支持、反馈收集）

### 预算（测试网3个月）
- 服务器成本: ~$2000-3000/月
- NeoFS存储: ~$100/月
- 人力成本: 根据团队配置
- 总计: ~$6000-9000 (基础设施)

---

## 联系方式 (Contact)

**技术问题**: 
- GitHub Issues: https://github.com/r3e-network/neo-n4/issues
- Discord: Neo开发者频道

**运维支持**:
- Email: devops@neo-n4.org (待建立)

**社区讨论**:
- Discord: Neo社区
- Twitter: @NeoSmartEcon

---

**最后更新**: 2026-09-25  
**状态**: 等待L1注册开始

