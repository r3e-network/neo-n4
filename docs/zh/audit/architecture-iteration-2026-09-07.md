# 架构迭代 — 2026-09-07（中文）

## 概览

针对 `doc.md` 与精简 NeoHub 四支柱整合（`docs/audit/neohub-lean-consolidation.md`）
对 `neo-n4` 做的系统性架构审计。

**结论：** 链上精简支柱与 SP1 执行核心（工件优先 + CAS 提交）结构健全。
生产离链接线与新的交易见证层仍使用破损或预精简方言，在 Validity 路径上留下
可阻断发布的正确性缺口。

## 已锁定决策

1. **清单 / 签名哈希 = Neo N3 单次 SHA-256。**
   `ParsedTransaction.hash` 用于 `GetSignData(network)` 必须是 `SHA256(unsigned)`
   （`Helper.CalculateHash`），不是 `Hash256`（双 SHA）。Merkle 叶、承诺与 DA 摘要
   继续使用 `Hash256`。Rust 将两者分别暴露为 `inventory_hash` 与 `hash256`。

2. **ZK 与 Multisig 生产结算使用 `submitAndFinalizeBatch`。**
   仅 `submitBatch` 会在 `RollupHub` 上留下 `StatusPending`，无法自行达到 `Finalized`。
   有效性路径必须原子完成。

3. **强制包含消费属于 `SubmitBatchCore`。**
   `ConsumeForcedTransactionsInternal` 按调用方提供的 `forcedInclusionCount` 推进队列。
   该计数始终绑定进 352 字节公钥输入哈希预映像（Wave 2）。

4. **Gateway 批次 VK WORDS 必须跟踪 guest VK 固定值。**
   任何 guest ELF/VK 轮换后，必须用同一验证密钥通过 `neo-zkvm-gateway-host` 重建
   `batch_vk_manifest.rs` WORDS（需 SP1 工具链）。

5. **Wave 2/3 续作见**
   [`architecture-iteration-2026-09-07-wave2.zh.md`](./architecture-iteration-2026-09-07-wave2.zh.md)。

## 发现矩阵

| ID | 严重度 | 发现 | Wave | 状态 |
| -- | ------ | ---- | ---- | ---- |
| F1 | P0 | 生产 ZK 硬编码 `submitBatch`；流水线从不调用 `SubmitAndFinalizeBatchAsync` | 1B | 已关闭 |
| F2 | P0 | 交易清单哈希使用 `Hash256`；Neo 钱包签单次 SHA256 id | 1A | 已关闭 |
| F3 | P0 | `ConsumeForcedTransactionsInternal` 是死代码 | 1C | 已关闭（计数参数；哈希绑定 → Wave 2） |
| F4 | P0 | Gateway `PINNED_BATCH_VK_WORDS` 相对 guest VK/ELF 过期 | 1A | 已关闭（从固定 ELF 重导） |
| F5 | P1 | Multisig 解析器收集 `m` 个公钥而非 `n` 个 | 1A | 已关闭 |
| F6 | P1 | 见证 SYSCALL 操作码为 Neo2 的 `0x68` 而非 Neo3 的 `0x41` | 1A | 已关闭 |
| F7 | P0 | 精简 RollupHub 缺少 `PublishGatewayGlobalRoot` | 2 | 已延期 |
| F8 | P1 | `LiveDeployCommand` 仍编排已删除的微合约 | 2 | 已延期 |
| F9 | P1 | Optimistic 离链路径仍是一等公民而 RollupHub fail-closed | 2 | 已延期 |
| F10 | P2 | 关键路径上的巨型模块（`wire.rs`、流水线、ProofWitnessStore） | 3 | 已延期 |

## 目标架构（Validity 路径）

```
Batcher → Sp1SettlementExecutionStack → 耐久证明工件
       → CanonicalSettlementPipeline.BroadcastAndPersistAsync
       → ISettlementClient.SubmitAndFinalizeBatchAsync
       → RollupHub.submitAndFinalizeBatch(...)
       → SubmitBatchCore（验证证明 + 消费强制包含）→ FinalizeBatchInternal
```

证明核心内部见证路径：

```
parse_transaction → inventory_hash (SHA256)
                 → verify_transaction_witnesses (Neo3 脚本 + ECDSA over SHA256(network‖hash))
                 → execute
```
