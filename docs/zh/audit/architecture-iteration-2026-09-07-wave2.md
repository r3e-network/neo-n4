# 架构迭代 — Wave 2/3（2026-09-07 续，中文）

## 概览

[`architecture-iteration-2026-09-07.zh.md`](./architecture-iteration-2026-09-07.md) 的续作。
Wave 1 关闭了 Validity 结算与 Neo N3 见证正确性。本轮关闭 **精简接线**（Wave 2）
并启动 **结构**（Wave 3），且不复活已删除的微合约。

**结论（变更后）：** Gateway 发布、LiveDeploy、强制包含哈希绑定、SharedBridge
本地映射与热路径原生复用的精简接线已关闭。`MessageRouterHash` 仍是 SharedBridge
的弃用别名（不会自动强制构造 router）。L1→L2 消息通过 SharedBridge 上的
`MessageRouterDeploymentHeight` 启用（或遗留 MessageRouterHash），轮询
`getL1ToL2Message`（回退 `getL1ToL2`/`isConsumed`）。Optimistic 主机构成为文档级
advisory；RollupHub 与 ZkLocalHost 对 optimistic 生产仍 fail-closed。完整巨型文件
拆分与 `doc.md` 重命名仍是 Wave 3 后续项。Gateway 批次 VK WORDS 已从 guest ELF 重固定（F4）。

## Wave 2/3 已锁定决策

1. **公钥输入哈希域始终为 352 字节。**
   预映像 = 原 348 字节布局 ‖ `forcedInclusionCount`（u32 LE），计数为 `0` 时亦然。
   Rust `hash_public_inputs_with_forced` 总是追加该 u32；C# `HashPublicInputs` /
   `EncodePublicInputs` / RollupHub `ComputePublicInputHash` 一致。
   相对 Wave-1 348 字节哈希为破坏性变更 — 须同 PR 再生夹具与奇偶校验。

2. **Gateway 全局根发布落在 RollupHub。**
   `PublishGatewayGlobalRoot` 经 ZkVerifier 验证聚合证明，推进每链水位，并在同一交易
   中调用 SharedBridge `PublishMessageRoots`。

3. **LiveDeploy 默认 = 仅精简 5 件。**
   `Sp1Groth16Verifier` → `ZkVerifier` → `GovernanceController` → `RollupHub` →
   `SharedBridge`。部署后绑定 GC↔RollupHub↔SharedBridge。引用已删除微合约必须
   fail-closed 并给出明确错误。

4. **MessageRouterHash 塌缩为 SharedBridgeHash。**
   Settings 接受 `SharedBridgeHash`（首选），`MessageRouterHash` 为弃用别名。

5. **Optimistic 为 advisory / 非生产。**
   `ZkLocalHostComposition` 与生产 Wire 拒绝 optimistic 证明档位。

6. **SharedBridge 鉴权 = RollupHub 自绑定。**
   `PrefixSettlementManager` 存储 RollupHub 哈希（名称为存储布局稳定性保留）。

7. **Wave 3 结构（本轮范围）：**
   - 热路径：见证构建与状态提交之间复用原生执行输出。
   - 为公钥输入尺寸/布局抽出最小线摘要 CI 检查。
   - 完整巨型文件拆分若会破坏 Wave 2 固定值则延期。

## 发现矩阵（Wave 2/3）

| ID | 严重度 | 发现 | 状态 |
| -- | ------ | ---- | ---- |
| F3b | P0 | 强制包含计数未进入公钥输入哈希 | 已关闭（352 字节域） |
| F4 | P0 | Gateway 批次 VK WORDS 过期 | 已关闭 — 从固定 guest ELF 重导 |
| F7 | P0 | RollupHub 缺少 `PublishGatewayGlobalRoot` | 已关闭 |
| F8 | P1 | LiveDeploy 微合约部署后步骤 | 已关闭 |
| F9 | P1 | Optimistic 仍是一等离链公民 | 已关闭（advisory + Zk/RollupHub fail-closed） |
| F11 | P1 | MessageRouterHash / 扫描器 | 已关闭（弃用别名 → SharedBridgeHash） |
| F12 | P1 | SharedBridge TokenRegistry 外部跳转 | 已关闭（本地 RegisterMapping） |
| F10 | P2 | 巨型模块 | 部分关闭 — `wire/` 拆分 + C# 广播/文档分部 + ProofWitnessStore 模型/序列化拆分 |
| F13 | P2 | 热路径双重原生重执行 | 已关闭（复用见证阶段原生输出） |
| F14 | P0 | 公钥输入**线**编码：Rust 仍 348 字节而 C# 为 352；guest 夹具截断 contentHash | 已关闭（2026-09-09 生产就绪审计波次：补 `forced_inclusion_count`、对齐读写、再生夹具、重算黄金向量） |

## 公钥输入（352 字节编码 / 哈希预映像）

| 偏移 | 字段 |
| ---- | ---- |
| 0..348 | 先前布局（chainId…blockContextHash） |
| 348..352 | `forcedInclusionCount` u32 LE |
