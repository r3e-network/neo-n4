# 规格空白修复计划

一份系统性计划,关掉本 consolidation 仓库还能解决的每一处 `doc.md` § 空白。条目按
范围(in-repo / upstream / operator)分组,组内按优先级排序。每条列出:(a) 规格怎么
说,(b) 当前代码是什么,(c) 关掉空白的最小有意义改动,(d) 验收标准。

跟踪方式:每关掉一项,`CHANGELOG.md` 留一行,引用本计划 ID(例如
`[plan: §16.1-admission]`),便于审稿人交叉对照。

## 仓内可即修(优先级排序)

### §16.1-admission —— ChainRegistry 咨询 GovernanceController.GetAdmissionMode()

**规格。** §16.1 定义了三种准入阶段 —— 许可制(owner 批准)、半许可制(只要
verifier+bridge 在批准集合就允许任意调用方)、无许可制(任意调用方)。
GovernanceController 已经经 `SetAdmissionMode(0..2)` 存储该模式。

**当前代码。** `ChainRegistry.RegisterChain` 无条件要求
`Runtime.CheckWitness(GetOwner())` —— 即不论 GovernanceController 配的什么模式,
始终是许可制。

**修法。** 给 ChainRegistry 加 `KeyGovernanceController` 存储(经 owner-only 的
`SetGovernanceController(hash)` 设置)。新增
`RegisterChainPublic(chainId, configBytes)`:
  1. 读 GovernanceController 哈希;未设置则拒绝。
  2. 调用 `GovernanceController.GetAdmissionMode()`。
  3. 分支:模式 2 → 任意调用方;模式 1 → 推迟(需要批准集合接线,记在下面
     §16.1-approved-sets);模式 0 → 拒绝并提示 "use RegisterChain"。

原来的 owner-only `RegisterChain` 留作许可制路径,向后兼容。

**验收。** 类型检查干净。现有测试仍绿(无回退)。新增测试钉住模式 0 时
RegisterChainPublic 拒绝、模式 2 时接受。

### §16.1-approved-sets —— GovernanceController 上批准的 verifier + bridge 集合

**规格。** 半许可准入要求 L2 的 verifier 和 bridgeAdapter 都在治理批准集合里。

**当前代码。** 任何地方都没有批准集合的存储。

**修法。** 给 GovernanceController 加:
  - `KeyApprovedVerifier = 0x0A` + `0x0A + verifierHash(20B) → 1`
  - `KeyApprovedBridgeAdapter = 0x0B` + `0x0B + bridgeHash(20B) → 1`
  - `[Safe] IsApprovedVerifier(UInt160)` / `IsApprovedBridgeAdapter(UInt160)`
  - `ApproveVerifier(UInt160)` / `ApproveBridgeAdapter(UInt160)`(owner-only,
    或在 council 框架可调用时走 M-of-N council)
  - `RevokeVerifier(UInt160)` / `RevokeBridgeAdapter(UInt160)`

然后在 `ChainRegistry.RegisterChainPublic` 模式 1 路径调用这些方法。

**验收。** approve/revoke/IsApproved 的测试 + 经 ChainRegistry 的集成测试。

### §16.2-config-bytes —— L2ChainConfig 线协议格式编码 SequencerModel + ExitModel

**规格。** §16.2 说每条 L2 必须公布 5 个安全标签维度。其中两个维度
(Sequencer、Exit)在 commit `340951a` 加进了链下 `L2ChainConfig` 记录,但链上
编码(`ChainRegistry.ConfigSize` 中的 89 字节)在其 5×1 字节字段里只携带 3 个维度。

**当前代码。** 链下记录已有这些新字段,带 init 默认值。链上字节没带。
RPC `getsecuritylevel` 只返回现有的 SecurityLevel 字节。

**修法。** `ChainRegistry.ConfigSize` 从 89 升到 91。布局文档化:
`[4B chainId][20B operator][20B verifier][20B bridge][20B msg]` 加
`[1B securityLevel][1B daMode][1B gatewayEnabled][1B permissionlessExit]
[1B sequencer][1B exit][1B active]`(7×1 字节字段)。更新 deploy planner +
1 条构造 config 的测试。新增 RPC `getsecuritylabel`,返回完整 5 维标签。

**验收。** 合约编译通过;`getsecuritylabel` 可调用;现有 `getsecuritylevel`
仍工作。

### §16-council-veto —— Verifier/bridge 升级走 council 多签

**规格。** §16 要求"verifier 升级"+"shared bridge 升级"由安全委员会 M-of-N 批准
+ timelock 把关。

**当前代码。** GovernanceController 有 council 成员 + `Approve(proposalId)` + 已
存储的 `KeyTimelockSeconds` 值。但没有合约去读 proposal 投票结果作为执行的门;
timelock 在任何地方都未强制执行。VerifierRegistry + SharedBridge 的升级方法
都没把关。

**修法。**
  1. 给 GovernanceController 每个 proposal 加 `KeyExecutedAt`;一旦达到阈值,
     存达到阈值时的 unix 时间戳。
  2. 加 `[Safe] IsApprovedAndTimelocked(proposalId)`,如果批准数 ≥ 阈值且
     `now ≥ executedAt + timelock` 则返回 true。
  3. VerifierRegistry.UpdateVerifier、SharedBridge.UpgradeAsset 等,通过
     Contract.Call 在执行前咨询此方法。

**验收。** 类型检查 + 一个 deploy-planner 测试搭起接线。

### §12-l1-da-default —— 提供 JsonRpc 后备的 L1 DA writer 脚手架

**规格。** §12.1 列出 L1 DA 是三层 DA 之一。当前 bridge writer 由运维者提供。

**当前代码。** `L2DAPlugin.BuildDefaultWriter(DAMode.L1, ...)` 抛
NotSupportedException,除非运维者预先注入 writer。NeoFS / External / DAC 都有
内置默认;L1 没有。

**修法。** 加 `Neo.Plugins.L2DA.JsonRpcL1DAWriter`,接受 `JsonRpcClient` + 目标
合约哈希 + sign-and-send 委托(形态同 `RpcSettlementClient.SignAndSendAsync`)。
通过调用 `publishDABlob`(或 L1 DA 合约的对应方法 —— 这是另一规格点 ——
`NeoHub.DARegistry.RecordDACommitment` 是最接近的)实现 `IDAWriter`。接为
`BuildDefaultWriter(DAMode.L1, ...)` 的默认,并清晰提示运维者仍需配 L1 RPC + 签名器。

**验收。** 新 writer 编译通过 + 对进程内伪 RPC 客户端有单测。
`BuildDefaultWriter(DAMode.L1, dataDir=null)` 在经 `WithWriter()` 接好 writer 之后
不再抛错。

### §8-witness-canonical —— 钉死规范 Witness 线协议格式

**规格。** §8.4 列出 witness 内容(有序 tx、合约字节码、storage 读/写 witness、
原生合约状态 witness、消耗的 L1 消息、DA 数据、执行 trace)。

**当前代码。** `ProofRequest.Witness` 中的 witness 是不透明 `ReadOnlyMemory<byte>`。
没有序列化器钉死布局。不同证明后备(SP1、Halo2)目前可能想要不同格式。

**修法。** 加 `Neo.L2.Proving.WitnessRecord`,7 段 + 一个遵循 `BatchSerializer`
模式的规范 encoder/decoder。证明者可选用规范格式或包装自家。测试钉布局 + 往返 +
截断拒绝。

**验收。** WitnessRecord 序列化器 + 3-5 条测试;现有证明测试仍绿。

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
对聚合根的 inclusion 证明)—— 与 `PassThroughRoundProver` 参照实现并存。真正的
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
  - 8 条对等测试(`UT_GovernanceFraudVerifierParity`)在 C# 中模拟该合约
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
