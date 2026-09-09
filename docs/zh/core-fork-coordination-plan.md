# 核心分叉协调计划

本文规定弹性 L2 链不可避免的 `r3e-network/neo` 分叉改动，覆盖任务板 30（核心分叉协调）、11（F07 ChainMode 激活钩子）、12（F08 ApplicationEngine 受限状态模式）。除非明确标注仅 L1，否则全部改动面向 L2 核心分支 `r3e/neo-n4-core`。

**基准事实：**
- 子模块 `external/neo` 指向 r3e/neo-n4-core 的可验证源码提交。
- `ChainMode` 枚举**仅**存在于本仓库 `src/Neo.L2.Abstractions/Models/ChainMode.cs`，恰好 4 个成员；运行时不按它分发，只是操作方标签（doc.md §6）。**不得**增加第五个成员来表达 RISC-V；执行引擎由 `--executor riscv` / `chain.config.json` 的 `vm: "neovm2-riscv"` 选择。
- 核心中的事实 L2 标记是 `NativeContract.L2SystemConfig.GetChainId(snapshot)`：L1/未初始化为 0，活跃 L2 为非零。该模式已用于治理验证者选择。
- 核心已注册原生合约：`L2SystemConfig`、`L2BatchInfo`、`L2Message`、`L2Bridge`、`L2Fee`、`L2Paymaster`、`L2NativeExternalBridge`、`L2AccountAbstraction`、`BridgedNep17`、`L2InteropVerifier`。

---

## F07 — L2 模式激活钩子

### F07.1 L2 系统配置创世交接

**目标分支：** `r3e/neo-n4-core`  
**目标文件：** `external/neo/src/Neo/SmartContract/Native/L2SystemConfigContract.cs`

**当前行为：** 无 OnPersist/PostPersist 覆盖；原生引导依赖离链 `NeoVMGenesisBootstrap` 手工回放。  
**目标行为：** 在 `L2SystemConfigContract` 增加 `InitializeAsync`，创世时写入默认设置槽与委员会授权地址，消除脚本模拟式 bootstrap。

**风险：** 共识破坏性 **YES**——已部署 L2 必须协调滚动升级或接受重新创世；`chainId == 0` 守卫保证已配置链不自动重初始化。  
**测试：** 单元测试验证存储键；集成测试验证无需 `NeoVMGenesisBootstrap.RunOnCache` 即可启动。  
**排序：** 必须先于 F07.2/F07.3 与 GAS 门控落地。

### F07.2 GAS 供应门控 — L2 激活时仅允许桥证明的铸造/销毁

**目标文件：** `external/neo/src/Neo/SmartContract/Native/Governance.cs`

**当前行为：** `OnPersistAsync` 无条件销毁网络费并向出块者铸造，不区分 L2 模式。  
**目标行为：** 当 `GetChainId(snapshot) != 0` 时，限制为桥 attested 的 mint/burn 路径，禁止与规范 GAS 供应模型冲突的无条件增发。

**风险：** 共识破坏性 YES；需与 bridge 合约 `canBurnNetworkFees` 接口对齐。  
**测试：** L1 行为不变；L2 模式下非桥路径 fail-closed。

### F07.3 NEO 治理限制 — L2 禁用投票/候选注册

当链为 L2（chainId 非 0）时，禁用 `Vote` / `RegisterCandidate`，治理验证者集以 `L2SystemConfig` 为准（与 doc.md §13.2 意图一致）。

### F07.x 其他策略钩子

- Policy 合约 L2 模式：`feeFactor` / `storagePrice` 可读，变更受限。
- Oracle 可选：允许 L2 跳过 Oracle 原生合约。

---

## F08 — ApplicationEngine 受限状态重执行模式

为 v4 欺诈证明在 L1 上的安全重执行提供快照隔离：

- 见证背书缓存：`TryGet` 对未知键 fail-closed；`Find` 拒绝敌意前缀扫描。
- 禁止把活库状态泄入受限执行上下文。
- 启用完整 bisection（多交易）而不仅限 Counter 单步。

**集成测试：** `UT_RestrictedFraudVerifier_Reexecute_MultiTx_Bisection`。  
**风险：** 不改变共识分叉（本地验证语义），但必须证明无状态泄漏。

---

## F09 — NeoVM2 / RISC-V 执行档位（非第五 ChainMode）

- 当前：`RiscVTransactionExecutor` 通过 P/Invoke 调用 PolkaVM host。
- **禁止**增加第五个 `ChainMode` 成员。
- 推荐：可选 `ProtocolSettings.ExecutorMode`（`NeoVmMode { Standard, RiscV }`）+ 插件级 `INeoVmExecutorProvider`；CLI 继续用 `--executor riscv`。

---

## 本仓库可先行的准备项

| 项 | 说明 | 状态 |
|----|------|------|
| `canBurnNetworkFees` 接口签名与规格 | F07.2 门控消费方 | 可先落地 |
| Bridge 规格文档 | 记录门控接口 | 可先落地 |
| ChainMode 注释澄清 | 四模式、仅标签、不运行时分发 | 可先落地 |

---

## 规范冲突处理原则

1. 若 `doc.md` 与代码不一致，规范优先；若规范与现实 API 不一致，先提出规范更新再改代码。
2. 任何第五 `ChainMode` 提案一律拒绝。
3. 原生合约初始化、GAS 供应、治理投票语义属于核心分叉范围，不得在本仓库用插件“假装”解决。
