# 数据可用性（DA）层级指南

本文说明 Neo Elastic Network 中如何选择与运维 DA（Data Availability）写入器，覆盖成本、性能、信任假设、部署步骤与各生产级后端的运维手册。

## 概览

框架支持多个 DA 层级，在成本、性能、去中心化与信任假设之间权衡。核心接口：

- **`IDAWriter`** — 所有 DA 写入器的基接口
- **`IProductionDAWriter`** — 生产就绪实现的标记接口，要求独立检索校验
- **`IProductionDAReader`** — 独立读取契约，防止生产者伪造可用性证明

### 生产实现

| 写入器 | 接口 | 模式 | 状态 | 用途 |
|--------|------|------|------|------|
| `NeoFsRestDAWriter` + `NeoFsRestDAReader` | `IProductionDAWriter` | `DAMode.NeoFS` | 生产可用 | 高吞吐、NeoFS 集群去中心化存储 |
| `JsonRpcL1DAWriter` | `IDAWriter`（非标记） | `DAMode.L1` | 生产可用 | 通过 L1 交易锚定获得最大无信任 |
| `InMemoryDAWriter` | `IDAWriter` | `DAMode.Local` | 仅开发 | 测试与本地开发 |
| `PersistentDAWriter` | `IDAWriter` | `DAMode.Local` | 本地耐久 | 节点本地 RocksDB 持久化（非公开 DA） |

## 选型决策

```
是否生产部署？
├─ 否 → 测试用 InMemoryDAWriter
└─ 是 → 主约束是什么？
    ├─ 无信任优先 → JsonRpcL1DAWriter
    │   └─ 成本约 0.5–2 GAS/批次（随大小变化）
    ├─ 吞吐与成本 → NeoFsRestDAWriter
    │   └─ 信任假设：NeoFS 验证者分发副本
    └─ 混合 → 同时跑两层
        ├─ 主：NeoFS（成本/性能）
        └─ 锚定：关键批次走 L1 交易
```

### 关键对比

| 维度 | NeoFS REST 网关 | L1 交易锚定 |
|------|-----------------|-------------|
| 每批成本 | 约 $0.01–$0.10 | 0.1–2 GAS（随批次大小） |
| 吞吐 | 约 100 MB/s 上传 | 受 L1 TPS 限制 |
| 检索延迟 | 本地 <100ms，跨区 ~1s | 最终性后链上查询 |
| 信任模型 | NeoFS 多签委员会 | 每个全节点可验证 |
| 批次大小上限 | 默认单对象 64 MiB | 受 L1 交易大小约束 |
| 公开可验证性 | 需离链验证 | 原生链上验证 |
| 最适合 | 通用 rollup、高交易量 | 监管要求、最高安全批次 |

## 生产硬性要求

1. **禁止**在生产 profile 中静默回退到本地/模拟 DA。
2. 启用独立 reader（`IProductionDAReader`）时，可用性判断以 reader 为准。
3. 异常“不可用”原因必须可区分并告警（不同的 log reason），不得把探测失败与真丢失混为一谈。
4. `PersistentDAWriter` 仅提供节点本地耐久，**不是**公开数据可用性层。
5. DAC / 委员会证明写入器必须由操作方注入真实签名与凭证；框架不提供默认委员会密钥。

## 成本粗算（参考）

典型 1 MB 批次、约 13 秒一块、保留 30 天、副本因子 3：月存储量级约数百 GB-day。以 $0.01/GB-day 估算为个位数美元/月量级（随集群定价变化）。L1 锚定则主要为交易费，批次越大费用越高。

## 运维手册要点

### NeoFS REST

- 配置 endpoint、桶、凭证；使用 `WithProductionBackend` 接入 writer+reader。
- 验证：上传后由独立 reader 拉取并校验哈希。
- 故障：区域不可达时先确认其他区域副本；全部失败才升级为不可用。

### L1 锚定

- 配置 L1 RPC、DA 合约与签名者（操作方注入 `INeoTransactionSigner`）。
- 承诺约定：`Commitment = Hash256(payload)`；指针为 32 字节交易哈希。
- 交易 FAULT 或方法缺失必须记为异常原因并失败关闭。

### 监控

- 指标：`l2.da.published` / `publish_latency_ms` / `publish_failures`（按模式打标签）。
- `/readyz` 在 settlement 或 DA 过期时返回 503。

## 安全边界

- DA 写入器不得拥有链上结算权限。
- 生产部署文档必须写明实际选用的 tier 与信任假设。
- 任何“降级到本地 DA”的配置变更都会削弱生产保证，必须留下警告日志。
