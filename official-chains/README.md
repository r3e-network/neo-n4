# Neo N4 Official Elastic Chains

这个目录包含7条由Neo官方维护的弹性链（Elastic Chains）的完整配置、创世状态和注册计划。

## 概述

Neo N4弹性链架构参考了ZKsync的弹性链设计理念，但关键区别在于：
- **官方维护**：所有弹性链由Neo核心团队统一维护和运营
- **专业化分工**：每条链针对特定用例进行优化
- **统一标准**：共享相同的安全模型和互操作性协议
- **NeoFS DA**：使用NeoFS作为主要数据可用性层（成本降低100倍）

## 7条官方弹性链

| Chain ID | 名称 | 模板 | 用例 | DA层 | 证明类型 | TPS目标 |
|----------|------|------|------|------|----------|---------|
| 100 | NeoSwap Chain | dex | 去中心化交易所 | NeoFS | ZK | 10,000+ |
| 200 | NeoGame Chain | gaming | 链游和NFT游戏 | NeoFS | ZK | 50,000+ |
| 300 | NeoFi Chain | defi | DeFi协议 | L1 | ZK | 5,000 |
| 400 | NeoSocial Chain | social | 社交应用 | NeoFS | ZK | 100,000+ |
| 500 | NeoNFT Chain | nft | NFT铸造和交易 | NeoFS | ZK | 20,000+ |
| 600 | NeoPayment Chain | payment | 支付和结算 | L1 | ZK | 10,000 |
| 700 | NeoEnterprise Chain | enterprise | 企业级应用 | NeoFS | Multisig | 5,000 |

## 目录结构

```
official-chains/
├── chain-100/          # NeoSwap Chain (DEX)
│   ├── chain.config.json
│   ├── genesis-manifest.json
│   ├── registration-plan.json
│   └── data/state/     # 创世状态数据
├── chain-200/          # NeoGame Chain (Gaming)
├── chain-300/          # NeoFi Chain (DeFi)
├── chain-400/          # NeoSocial Chain (Social)
├── chain-500/          # NeoNFT Chain (NFT)
├── chain-600/          # NeoPayment Chain (Payment)
└── chain-700/          # NeoEnterprise Chain (Enterprise)
```

## 配置特点

### 1. NeoSwap Chain (100) - DEX专用链
- **超低延迟**：10,000+ TPS，<100ms撮合
- **Validium模式**：交易数据存储在NeoFS，降低成本
- **延迟退出**：优化流动性，适合高频交易
- **网关启用**：支持跨链资产交换

### 2. NeoGame Chain (200) - 游戏链
- **超高吞吐**：50,000+ TPS，<200ms延迟
- **NeoFS DA**：游戏状态和资产数据高效存储
- **ZK证明**：保证游戏公平性和资产安全
- **网关启用**：跨链游戏资产互通

### 3. NeoFi Chain (300) - DeFi链
- **高安全性**：Rollup模式 + L1 DA + ZK证明
- **无需许可退出**：用户随时可以安全退出
- **5,000 TPS**：平衡性能和安全性
- **网关启用**：支持跨链DeFi组合

### 4. NeoSocial Chain (400) - 社交链
- **极高吞吐**：100,000+ TPS，适合社交互动
- **低成本**：NeoFS存储，适合大量用户内容
- **200ms区块时间**：快速用户体验
- **网关启用**：社交身份跨链互通

### 5. NeoNFT Chain (500) - NFT链
- **NFT优化**：20,000+ TPS，专为NFT铸造和交易设计
- **Validium模式**：NFT元数据和资产高效存储
- **延迟退出**：优化NFT交易流动性
- **网关启用**：NFT跨链流转

### 6. NeoPayment Chain (600) - 支付链
- **支付优化**：10,000 TPS，适合高频小额支付
- **高安全性**：Rollup + L1 DA + ZK证明
- **无需许可退出**：用户资金安全保障
- **网关启用**：跨链支付场景

### 7. NeoEnterprise Chain (700) - 企业链
- **联盟链模式**：Sidechain + Multisig，适合企业场景
- **运营商协助退出**：企业级服务保障
- **5,000 TPS**：满足企业应用需求
- **NeoFS DA**：企业数据高效存储

## 部署状态

所有7条弹性链已完成：
- ✅ **配置生成**：使用neo-stack CLI创建配置
- ✅ **创世状态**：已初始化完整的创世状态
- ✅ **注册计划**：已生成L1注册所需的所有参数
- ⏳ **L1注册**：待提交到L1 RollupHub合约
- ⏳ **运营启动**：待启动sequencer、batcher、prover节点

## 使用方法

### 1. 查看链配置
```bash
cat official-chains/chain-100/chain.config.json
```

### 2. 查看创世状态
```bash
cat official-chains/chain-100/genesis-manifest.json
```

### 3. 查看注册计划
```bash
cat official-chains/chain-100/registration-plan.json
```

### 4. 运行验证测试
```bash
bash scripts/test-official-elastic-chains.sh
```

### 5. 重新部署（如需要）
```bash
bash scripts/deploy-official-elastic-chains.sh
```

## 技术规格

### 共享基础设施
- **dBFT共识**：所有链使用dBFT委员会进行排序
- **NeoVM/NeoVM2**：统一的虚拟机执行环境
- **SP1 ZK证明**：Groth16验证，安全高效
- **L1结算**：所有链最终在Neo N3主网结算

### 数据可用性层对比
| DA层 | 成本 | 安全性 | 使用场景 |
|------|------|--------|----------|
| NeoFS | 低（100x便宜）| 高 | DEX、Gaming、Social、NFT、Enterprise |
| L1 | 高 | 最高 | DeFi、Payment（高价值资产）|

### 安全等级
- **Validity (Rollup + ZK)**：DeFi、Payment
- **Validium (Off-chain DA + ZK)**：DEX、NFT
- **Sidechain (Multisig)**：Enterprise

## 与ZKsync Elastic Chains的对比

| 特性 | Neo N4 Elastic Chains | ZKsync Elastic Chains |
|------|----------------------|----------------------|
| 维护方 | Neo官方统一维护 | 任意第三方部署 |
| 数量 | 7条专用链 | 无限制 |
| 专业化 | 高度针对用例优化 | 通用配置 |
| DA层 | NeoFS (100x成本降低) | 多种选择 |
| 互操作性 | Neo Gateway统一互通 | Hyperchain互通 |
| 治理 | Neo理事会统一治理 | 各链独立治理 |

## 下一步计划

1. **L1合约注册**：将7条链注册到Neo N3测试网的RollupHub合约
2. **节点启动**：为每条链启动完整的sequencer、batcher、prover节点
3. **性能基准测试**：验证每条链的TPS和延迟指标
4. **跨链测试**：验证Neo Gateway的跨链互操作性
5. **社区测试**：开放给社区进行公开测试

## 参考文档

- [官方弹性链完整规划](../OFFICIAL_ELASTIC_CHAINS.md)
- [数据可用性层说明](../DA_LAYER_EXPLAINED_ZH.md)
- [架构文档](../doc.md) - 第3.2节（NeoHub）、第4节（Gateway）
- [实施状态](../IMPLEMENTATION_STATUS.md)

## 联系方式

如有问题或建议，请通过以下方式联系：
- GitHub Issues: https://github.com/r3e-network/neo-n4/issues
- 技术讨论：Neo社区Discord

---

**注意**：这些是测试网配置。生产网络部署将使用不同的链ID范围和更严格的安全参数。

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
