# 规格空白修复计划

一份系统性计划,关掉本 consolidation 仓库还能解决的每一处 `doc.md` § 空白。条目按
范围(in-repo / upstream / operator)分组,组内按优先级排序。每条列出:(a) 规格怎么
说,(b) 当前代码是什么,(c) 关掉空白的最小有意义改动,(d) 验收标准。

跟踪方式:每关掉一项,`CHANGELOG.md` 留一行,引用本计划 ID(例如
`[plan: §16.1-admission]`),便于审稿人交叉对照。

## 仓内(按 § 分项的状态)

### §16.1-admission ✅ 已闭合

`ChainRegistryContract` 通过 `RegisterChainPublic(chainId, configBytes)` 实现
§16.1 准入策略:

1. 读取已接线的 GovernanceController 哈希(由 owner-only 的
   `SetGovernanceController` 设置);未接线则带"必须先由 owner 接线"的清晰
   提示拒绝。
2. 调用 `GovernanceController.GetAdmissionMode()`。
3. 模式 0(许可制)→ 带 "use RegisterChain" 提示拒绝;模式 1(半许可制)→
   对 verifier 字节(偏移 24..43)与 bridge 字节(偏移 44..63)强制
   `IsApprovedVerifier` + `IsApprovedBridgeAdapter` 检查;模式 2(无许可制)→
   任意调用方;均落入与 owner-only 路径相同的 `WriteChainConfig` 写入路径。

owner-only 的 `RegisterChain` 保留为 §16.1 的"许可制"路径。

**文件。** `contracts/NeoHub.ChainRegistry/ChainRegistryContract.cs`。
测试覆盖模式 0/1/2 + 未接线时的 controller 拒绝。

### §16.1-approved-sets ✅ 已闭合

`GovernanceControllerContract` 承载已批准 verifier + 已批准 bridge 集合,
被 `ChainRegistry.RegisterChainPublic` 模式 1 使用:

  - `PrefixApprovedVerifier = 0x0A`(`0x0A + verifierHash(20B) → 1`)
  - `PrefixApprovedBridge = 0x0B`(`0x0B + bridgeHash(20B) → 1`)
  - `[Safe] IsApprovedVerifier(UInt160)` / `[Safe] IsApprovedBridgeAdapter(UInt160)`
  - `ApproveVerifier(UInt160)` / `RevokeVerifier(UInt160)` 及 bridge 对应方法
    (owner-only mutator)

ChainRegistry 模式 1 路径通过 `Contract.Call(...,
"isApprovedVerifier"/"isApprovedBridgeAdapter", CallFlags.ReadOnly, ...)`
咨询,断言错误中点出失败的具体维度。

**文件。** `contracts/NeoHub.GovernanceController/GovernanceControllerContract.cs`,
经 `ChainRegistryContract.RegisterChainPublic` 集成。

### §16.2-config-bytes ✅ 已闭合

`ChainRegistry.ConfigSize` 现为 91 字节(4 + 20×4 + 7),在独立字节字段里
携带全部 5 个 §16.2 安全标签维度:

  - `OffsetSecurityLevel = 84`、`OffsetDAMode = 85`、`OffsetGatewayEnabled = 86`、
    `OffsetPermissionlessExit = 87`、`OffsetSequencerModel = 88`、
    `OffsetExitModel = 89`,active 标志位于 `ConfigSize - 1`(= 90)。
  - 每个维度都有专用的 `[Safe] Get*` 读取器。
  - `Neo.L2.Abstractions` 提供 `SequencerModel` / `ExitModel` 枚举 + 规范的
    `L2ChainConfigSerializer` + `L2ChainConfigJsonReader`。
  - 链下 `getsecuritylabel` RPC 暴露完整 5 维标签(原 `getsecuritylevel`
    保留以向后兼容)。

### §16-council-veto ✅ 已闭合

`GovernanceController` 强制 §16 的 council 多签 + timelock 把关:

  - `Approve(proposalId, memberKey)` 记录每位 council 成员投票;首次达到
    阈值时,unix 时间戳存于 `PrefixApprovedAt = 0x0C`。
  - `[Safe] IsApprovedAndTimelocked(proposalId)` 在 `approvalCount ≥ threshold`
    且 `now ≥ approvedAt + timelockSeconds` 时返回 true。
  - `VerifierRegistry.SetGovernanceController` + `RegisterVerifierViaProposal`
    在执行 verifier 升级前咨询此把关。

### §12-l1-da-default ✅ 已闭合

`Neo.Plugins.L2DA.JsonRpcL1DAWriter` 作为默认的 L1-DA writer 出货:

  - 包装 `JsonRpcClient` + `SignAndSendAsync` 委托(形态同 `RpcSettlementClient`)。
  - 通过调用配置好的 L1 合约方法(默认最接近的是
    `NeoHub.DARegistry.RecordDACommitment`)实现 `IDAWriter`。
  - 13 条单测对进程内伪 RPC 客户端。
  - 接好 writer 后,`L2DAPlugin.BuildDefaultWriter(DAMode.L1, ...)` 不再抛错。

### §8-witness-canonical ⏭ 推迟

原计划加 `Neo.L2.Proving.WitnessRecord` 钉死 §8.4 的 witness 布局
(有序 tx / 字节码 / storage 读写 / 原生状态 / L1 消息 / DA 数据 / trace)。

**决定。** 在没有真正的 prover 锁定它之前过早 —— 不同后备(SP1、Halo2)想要
不同格式。等 SP1 工具链集成落地、guest ELF 定下它期望的 witness 形状之后再
评估。在那之前,`ProofRequest.Witness` 仍保持不透明的
`ReadOnlyMemory<byte>`。

## 上游 / 仓外(跟踪但此处不修)

### §13.2-native-adjustments —— L2 模式下 GAS / NEO / Oracle / Policy 调整

属于 neo-project/neo Neo 4 core。L2 模式(按 §6 ChainMode)需要 core 改:GAS 供应
由 bridge 把关、NEO 治理仍在 L1、Oracle 可选。作为上游协调工作跟踪;在 Neo 4 core
出 ChainMode 钩子之前,本仓库无可执行项。

### §14.1-rpcserver-wrapper —— `[RpcMethod]` 装饰的包装类

待 Neo 4 的 RpcServer 插件源码。9 个 L2 RPC 方法以普通方法形式存在于
`Neo.Plugins.L2Rpc.L2RpcMethods`;把它们注册到 neo 的 RpcServer 派发器的包装类
需要那份源。作为待集成跟踪 —— 当 neo-modules(或 Neo 4 RpcServer 落地处)可用,
生成 partial class。

### §4-recursive-zk —— 真正的 Neo Gateway round prover

**状态更新**:Phase 5 聚合现已出货**两份生产级 `IRoundProver` 实现** ——
`MultisigRoundProver`(Secp256r1 阈值证明轮)+ `MerklePathRoundProver`(逐成员
对聚合根的 包含证明)—— 与 `PassThroughRoundProver` 参照实现并存。真正的
密码学,不带工具链依赖。剩下的递归-ZK fold 变体(SP1 Compress / Halo2
accumulator / Risc0 STARK fold)在运维者带上 SP1 工具链时,接到同一
`IRoundProver` 接缝。

## 运维特定(不在仓内修)

### §14.2-wallet-integration

**状态更新**:`docs/wallet-integration.md` 现已记录两套生产模式 —— 粘贴进钱包
hex(冷钥流程)+ 委托签名(热钱包自动化)—— 含 NeoLine / Neon / NEP-6 / Ledger /
KMS 的实操样例。所有 CLI 都输出规范 hex;生产热路径
(`RpcSettlementClient` 等)接受 `SignAndSendAsync` 委托,运维者把它接到自己
偏好的签名路径。框架永不持有私钥,但接入模式文档已出货。

### §11-l1-signer-for-submitbatch

`RpcSettlementClient.SignAndSendAsync` 是委托;具体签名由运维者提供。理由同上。

### §16.3-dbft-consensus-integration

把 `Neo.L2.Sequencer` 接进 Neo 的 `DBFTPlugin` 共识选择器是部署相关的。

## 总结

**6 项仓内**按优先级:
  1. ✅ §16.1-admission —— 已关闭:`ChainRegistry.SetGovernanceController` +
     `RegisterChainPublic` 模式 0/1/2 分支。
  2. ✅ §16.1-approved-sets —— 已关闭:
     `GovernanceController.{Approve,Revoke}{Verifier,BridgeAdapter}` +
     `RegisterChainPublic` 模式 1 咨询的 `IsApproved*`。
  3. ✅ §16.2-config-bytes —— 已关闭:`ConfigSize` 89 → 91 字节,
     `OffsetSequencerModel` / `OffsetExitModel` 常量,单一职责的
     `[Safe] Get*` 读取器,`SequencerModel` / `ExitModel` 枚举落到
     Abstractions。
  4. ✅ §16-council-veto —— 已关闭:`GovernanceController.GetApprovedAt` +
     `IsApprovedAndTimelocked`,`VerifierRegistry.SetGovernanceController` +
     `RegisterVerifierViaProposal`(咨询 timelock 门)。
  5. ✅ §12-l1-da-default —— 已关闭:`Neo.Plugins.L2DA.JsonRpcL1DAWriter`
     (`JsonRpcClient` + 已签 tx 委托,13 条单测)。
  6. ⏭ §8-witness-canonical —— **延后**(计划备注:"在没有真正以它为目标的
     prover 之前太早 —— 等 SP1 工具链落地、guest ELF 定义其 witness 形态时
     再评估")。

**同窗口里关闭的二阶空白**(增项,不在原 6 项之列):
  - Abstractions 中规范的 91 字节 `L2ChainConfigSerializer` +
    `L2ChainConfigJsonReader`;在运维者提供哈希时,CLI 的 `register-chain`
    直接输出 hex。
  - `ChainRegistry` 把 §16.2 读取 API 凑齐(`GetSecurityLevel` /
    `GetDAMode` / `GetGatewayEnabled` / `GetPermissionlessExit`),与已有
    `GetSequencerModel` / `GetExitModel` 对称。
  - `GovernanceController.GetApprovalCount` `[Safe]` 公开读取。
  - 链下 RPC `getsecuritylabel` 暴露完整 5 维 §16.2 标签。
  - `ScaffoldPlan.PostDeployActions` 浮现
    `ChainRegistry.SetGovernanceController` +
    `VerifierRegistry.SetGovernanceController` 的 post-deploy 接线
    (+ 现有 `SequencerBond.RegisterSlasher`)。

**生产可读性审计后续**(诚实回答"是不是都正确完整实现了?"之后的若干迭代):
  - `IMPLEMENTATION_STATUS.md` 增加显式的"生产可读性审计"章节,把
    生产级 vs MVP 形态 vs 参照脚手架(运维必须替换)vs 计划打印器(CLI
    并未真正签名/提交)vs 设计上仓外项 一一编目。
  - `NeoHub.ForcedInclusion` 出货真正可配置的反垃圾费
    (`SetFee` / `SetFeeRecipient` / `SetGasToken`);默认 0 = 保留无费
    的 legacy。关掉"无费 MVP"的标注。
  - `NeoHub.GovernanceFraudVerifier`(第 14 个 NeoHub 合约)以治理仲裁
    乐观链的结构性 fraud verifier 参照形式出货。解码规范的 101 字节
    `FraudProofPayload`,校验长度 / 版本 / 是否声称真实差异,并发出
    accept/reject 事件(带原因码)供 council 审议。关掉链上
    `fraudVerifier` 调用点空白。已接进 `ScaffoldPlan.Default()` +
    `PostDeployActions` 信息提示。
  - 13 条对等测试(`UT_GovernanceFraudVerifierParity`)在 C# 中模拟该合约
    的判定树,让改常量 / 顺序 / 偏移的 refactor 在单测时即被抓住。
  - "MVP" 注释清理:`ChallengeOrchestrator.InspectAsync`(收敛路径已存于
    `*WithBisection`),`SettlementManager.VerifyWithdrawalLeaf{,At}` 和
    `EmergencyManager.EscapeHatchExit`(故意的单叶快路径,不是未完成
    MVP),`BatchSealer.SealBatch`(sealer 只是 tx 收集阶段;executor
    pass 才出真根)。
  - `CountApprovals` 私有 helper 改名为 `IncrementAndCountApprovals`,以
    免 bump-and-return 语义被误读;`[Safe] GetApprovalCount` 是纯读对应件。
  - `samples/` 出货 4 份开箱即用的链 config,覆盖不同用例
    (general-rollup / gaming-rollup / exchange-validium /
    privacy-sidechain),经 `neo-l2-devnet --config` 端到端验证。
  - `samples/contracts/` 出货 2 个 dApp 示例(`CrossChainGreeter`、
    `WithdrawalDemo`),由 CI 作为第 21 + 22 个合约编译。
  - `docs/tech-stack-coverage.md` 诚实的 5 层空白分析:**61/62 组件 ✅、
    1 🟡(SP1 工具链离线)、0 🔴。** Phase 5 聚合 + 此前所有"仓外"的
    Layer-4/5 行(TS/Rust SDK、Web 应用、mdBook、faucet、钱包文档)都
    在仓内出货。

**3 项上游**已跟踪但被外部依赖卡住。

**3 项运维特定**显式不在本仓范围。

节奏:每个 loop 迭代修一项仓内项;commit 信息引用本计划 ID。每修一项后做一次复审,
捕捉二阶空白。
