# 生产就绪审计波次

> **功能：** production-ready-audit-wave  
> **分支：** master（用户批准在主工作树继续，不新建 worktree）  
> **基线：** `13f91058`

## 报告

### [S1] 问题

`neo-n4` 在本地/devnet 弹性链栈上架构已完备，但工作树中存在大量未提交的
Wave-1/2 架构迭代（约 58 个修改文件 + 新持久化/结算源码），尚未作为单一可评审
修订稳定下来。“生产就绪”仍受操作方/资金门槛与上游 Neo core 项限制。本波次必须：

1. 稳定脏 Wave-1/2 树，使构建 + 测试在当前修订上绿。
2. 关闭本仓库拥有的剩余结构/一致性缺口（非上游 core、非实盘部署证据）。
3. 留下耐久功能文档、CHANGELOG/状态更新与可评审证据链。

### [S2] 设计

#### 工作区决策

- 在主工作树 `D:\Git\neo-n4` 的 `master` 上继续（用户已批准）。
- 本轮不新建链接 worktree。
- 将既有未提交 Wave-1/2 工作视为本功能基线。

#### Windows 环境约束

本机 MSBuild node-reuse 缓存可能携带缺失 `ProgramFiles` / `ALLUSERSPROFILE` 的
损坏进程环境，导致 NuGet `_GetRestoreSettings` 抛出
`Value cannot be null. (Parameter 'path1')`。任何 restore/build 前：

1. `dotnet build-server shutdown`
2. 设置 `ProgramFiles`、`ProgramW6432`、`ProgramFiles(x86)`、`PROGRAMDATA`、
   `ALLUSERSPROFILE`、`DOTNET_CLI_HOME`、`NUGET_PACKAGES`、`MSBUILDDISABLENODEREUSE=1`

#### 稳定化契约

- `dotnet restore` + `dotnet build Neo.L2.sln /p:NuGetAudit=false` 必须成功。
- `dotnet test Neo.L2.sln /p:NuGetAudit=false` **零失败**。已知生产环境跳过项
  （live SDK / 真实原生）保持跳过，并标为 `PRE-EXISTING`。
- 参与 Wave-1/2 变更面的 Rust crate（`bridge/neo-execution-core`、
  `bridge/neo-zkvm-guest` 测试）在工具链可用时必须编译并通过单测。

#### 本波次内关闭的仓库内缺口

1. **编码 / 见证一致性** — 确认 352 字节公钥输入、inventory-hash 与见证拆分
   仍由测试钉住；补齐 Rust `PublicInputs.forced_inclusion_count` 线编码，
   使 C#↔Rust↔夹具三方一致；host `prove_compressed` / `prove-batch` 使用
   `hash_public_inputs_with_forced`。
2. **持久化拆分完整性** — `ProofWitnessStore.cs` 已拆为
   `KeyValueProofWitnessStore` + 模型 + 序列化器；无悬空引用。
3. **结算组合一致性** — 精简 LiveDeploy 与生产组合拒绝已删除微合约；
   optimistic 仅 advisory。
4. **文档/状态诚实性** — `IMPLEMENTATION_STATUS.md`、`CHANGELOG.md` 与架构迭代
   审计文档描述已交付状态，不宣称主网就绪。
5. **若测试暴露缺陷** — 以保留 Wave-1/2 契约的最小变更修复，并为行为缺陷加回归测试。

#### 明确不在范围

- 上游 Neo core 的 `ChainMode` 项（`r3e-network/neo`）。
- 实盘测试网/主网部署证据与 HSM/KMS 操作方凭证。
- 通用 NeoVM 多交易欺诈证明协议（仍为受限 v4）。
- Go SDK 与额外示例 dApp。
- Halo2/Risc0 替代 round prover。

### [S3] 边界

除已提交的 Wave-1/2 已钉住的 352 字节公钥输入域与精简 shared-bridge 映射外，
本波次不改 `doc.md` 线格式。任何进一步编码变更需要新 spec。

## 任务

- [x] T1: 撰写并维护本功能文档
- [x] T2: 在脏树上稳定 restore/build `Neo.L2.sln`
- [x] T3: 运行完整 .NET 测试并修复失败
- [x] T4: 运行变更 crate 的 Rust 测试
- [x] T5: 关闭测试/评审发现的残余一致性缺口
- [x] T6: 更新 CHANGELOG + IMPLEMENTATION_STATUS + 迭代审计笔记
- [x] T7: 独立评审完整变更（关键项已修复）
- [ ] T8: 将本文档定稿为 `status: delivered` 并填写 Report
