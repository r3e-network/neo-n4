# Neo N4 系统性审计报告

- **审计日期**：2026-10-21
- **审计基准**：`master` @ `0e7f0115`（本地已包含远端 `origin/master` @ `88e9264c` 全部内容，并领先 14 个提交）
- **代码规模**：C# 876 文件 / 66,225 行 · Rust（bridge/）154 文件 / 14,274 行 · TypeScript（sdk/）280 文件
- **审计范围**：NeoHub 4-Pillar 智能合约 + L2 原生合约、L2 链下核心库、Rust Prover Host / ZK 边界、RPC ↔ SDK ABI、供应链依赖、构建与测试基线
- **方法**：静态代码通读 + 真实 NeoVM 合约执行测试 + 跨语言字节级对照 + 跨语言黄金向量 + 依赖漏洞扫描

---

## 0. 结论摘要

| 严重度 | 数量 | 关键项 |
|---|---|---|
| **P0 阻断** | 5 | 提现叶哈希跨链布局分叉（L2→L1 提现恒失败，资产双向锁死）· Gateway 递归证明恒定失效（348 vs 352 字节）· ZkVerifier 外部验证器 arity 错误（ZK 路径 100% 不可达）· `verifyZkProof` 硬编码 `false`（Gateway 根发布回滚）· SDK `u64` 精度截断（静默返回错误批次状态根） |
| **P1 高** | 6 | 治理锁死后回滚路径不可达 · SharedBridge 锁死后全部运维失效 · 强制包含「已消费」标志从不写入（反审查 DoS）· `ReportCensorship` 无权限可暂停链 · Multisig quorum 不去重（1-of-N 冒充 M-of-N）· `host::verify()` 接受任意 VK |
| **P2 中** | 11 | ProofType 判别值错位 · 网关 Merkle 折叠语义分叉 · proof system 0 无法注册 · envelope-only fail-open · 三个 `*ViaProposal` 缺重放保护 · 强制包含 Rust 侧无密码学校验 · PooledBatchSerializer 丢失全部校验 · KeyedStateStore 依赖隐式排序 · prover 无资源护栏 · Gateway daemon 无背压 · 合约测试产物过期且测试依赖 gitignore 目录 |
| **P3 低** | 9 | 提案重放（当前不可达）· 阈值复核缺失 · SharedBridge 迁移封存 · 热路径缓存 · SP1_VERSION 无编译期一致性 · bincode 配置不对称 · SDK 枚举越界 · SDK 超时不覆盖 body · SDK u64 交叉校验类型语义 |

**总体判定：当前代码不具备生产就绪条件。** 5 项 P0 中有 3 项是**确定性功能失效**（非概率性、非依赖攻击者），意味着对应主路径在当前 revision 上根本无法跑通：

1. L2→L1 提现路径恒定失败（资产在 L2 侧已扣、L1 侧永不释放）
2. Gateway 递归证明对任何真实批次都无法生成
3. ZK 批次验证路径永久不可用

这与 `IMPLEMENTATION_STATUS.md` 中 Phase 4/5 标注的「✅ 生产形态 + 本地验证通过」存在实质冲突——该矩阵衡量的是**架构覆盖率**而非**当前 revision 的可运行性**。

---

## 1. 版本拉取与一致性校验

| 检查项 | 结果 |
|---|---|
| `git fetch origin` | 成功，新增 7 个远端分支引用 |
| `origin/master` vs `git ls-remote` | 一致（均为 `88e9264c`） |
| 本地 vs 远端 | 本地领先 14 个提交，远端无新提交 |
| 工作树 | 干净（仅 `.workbuddy/` 审计会话目录未跟踪） |
| `external/neo` 子模块 | 干净，`534918ba` |

**已知 git 陷阱规避**：本机 git 在 `fetch` 后会删除 `.git/refs/remotes/origin/`，并回落至 `packed-refs` 中的过期引用。本次已通过 `git ls-remote`（直连远端）与 `git rev-parse refs/remotes/origin/master` 双向比对确认引用一致，未依赖 `git status` 的 ahead/behind 报数。全程未使用 `git stash`（该命令在本机会删除 `.git/refs/`）。

---

## 2. 构建与测试基线

### 2.1 构建阻塞（环境级，非代码级）

初次 `dotnet build Neo.L2.sln` 失败于 `MSBUILD : error MSB4166: Child node exited prematurely`，堆栈显示 `Microsoft.Build.Tasks.Copy` → `Interop+Kernel32.DeleteFilePrivate` 递归栈溢出。

**根因**：工作区存在 **174 个陈旧 `bin/`+`obj/` 目录**（其中 `external/neo/src/Neo/` 的产物自 2026-01-01 起未刷新）。MSBuild 的 `Copy` 任务在处理陈旧 `FileListAbsolute.txt` 中的失效条目时陷入递归删除。

**处置**：删除全部陈旧 `obj/`+`bin/` 后重建成功。**此为环境问题，与本次拉取的代码变更无关**，但会阻断任何干净检出的 CI 验证——建议在 CI 中加入 `git clean -xfd` 前置步骤，或固定 MSBuild 缓存目录。

### 2.2 测试基线

**完整解决方案构建**：清理陈旧产物后 `dotnet build Neo.L2.sln -c Release` → **成功，0 warnings / 0 errors**。

| 套件 | 初始结果 | 编译合约后 | 说明 |
|---|---|---|---|
| `NeoHub.Contracts.VmTests` | 330 passed / **8 failed** | **338 passed / 0 failed** ✅ | 8 个失败全在 `UT_ZkVerifier_Vm`，根因是测试类读 `contracts/NeoHub.ZkVerifier/bin/sc/`（被 `.gitignore` 排除）→ 详见 P2-4 |
| `NeoHub.Sp1Groth16Verifier.UnitTests` | 2 passed / **9 failed** | **11 passed / 0 failed** ✅ | 同一根因（缺 `bin/sc/*.nef`） |
| `Neo.Hub.Deploy.UnitTests` | 107 passed / **4 failed** | 107 passed / **4 failed** | **文档漂移**（真实缺陷）：`Repository_DeployPlanMatchesNeoHubContractInventory`（README 称 5 个合约，实际 6 个）、`CurrentDocumentation_UsesCurrentNeoHubCounts`（README 缺 "6 NeoHub L1 contract projects"）、`CurrentDocumentation_UsesSourceDrivenTestAndSdkInventories`、`CurrentDocumentation_EveryEnglishMarkdownHasChineseCounterpart`（缺中文对应文档） |
| `Neo.Stack.Cli.UnitTests` | 205 passed / **5 failed** | 205 passed / **5 failed** | **文档漂移**（真实缺陷）：`Catalog_HasExactlyFourTemplates_InOrder`（期望 4 个模板，实际 11 个）、`Catalog_NonSidechainTemplates_NameARouteTheDeployerRegisters`、`LaunchingGuide_TemplateTable_MatchesTheCatalog`（`docs/launching-an-l2.md` 模板表仍是 4 个，实际 11 个）、`LaunchingGuide_ChineseTemplateTable_MatchesTheCatalog`（中文版同样漂移）、`Catalog_ValidNames_ListsAllInOrder` |
| 其余 34 个 .NET 套件 | 全绿 | 全绿 | 最大者 `Neo.Plugins.L2Settlement.UnitTests` 173 passed、`Neo.L2.State.UnitTests` 149 passed、`Neo.L2.Batch.UnitTests` 136 passed |
| `bridge/neo-execution-core` (cargo) | **16 passed / 0 failed** | — | Rust 侧全绿 |
| `sdk/typescript` (vitest) | **23 passed / 3 skipped** | — | skipped 为需外部环境的 live 用例 |

**基线总账**：.NET **2,999 passed / 17 failed**（38 个套件）→ 编译合约后 **3,018 passed / 9 failed**；Rust 16/0；TypeScript 23/3。

剩余 9 个失败**全部是文档与代码事实不一致**，属真实工程缺陷（不是环境问题），修复方式见第 9 节第 13 项。

> 上述 330/8 基线由审计方与合约审计子代理**独立复现，两次结果完全一致**，交叉验证了 P2-4 的真实性；`nccs` 编译后 338/338 全绿进一步确证了根因判定。

---

## 3. P0 阻断级发现

### P0-1 提现叶哈希：链下布局与链上合约布局不一致，L2→L1 提现恒定失败

**位置**
- 链下 C#：`src/Neo.L2.State/MessageHasher.cs:105-138`（`EncodeWithdrawal`）
- 链下 Rust：`bridge/neo-execution-core/src/native.rs:935-955`（`withdrawal_hash`）
- 链上裁决方：`contracts/NeoHub.SharedBridge/SharedBridgeContract.cs:642-692`（`ComputeWithdrawalLeafHash`）

**问题描述**

同一「提现叶」逻辑对象存在两套互不兼容的字节布局（审计方已独立读码核实）：

| 序 | 链下 Layout A（C# 与 Rust 一致） | 链上 Layout C（SharedBridge） |
|---|---|---|
| 1 | `4B chainId LE` | `4B chainId LE` |
| 2 | `20B emittingContract` | `20B emittingContract` |
| 3 | `20B l2Sender` | `20B l2Sender` |
| 4 | `20B l1Recipient` | **`20B l2Asset`** |
| 5 | `20B l2Asset` | **`8B withdrawalNonce LE`** |
| 6 | `4B amountLen LE` | **`20B l1Asset`** |
| 7 | `N amountBytes LE` | **`20B l1Recipient`** |
| 8 | `8B nonce LE` | `4B amountLen LE` |
| 9 | — | `N amountBytes LE` |
| **总长** | **98 B** | **118 B** |

差异是**字段重排 + 合约额外绑定 20B `l1Asset`**，非端序问题。哈希轮数一致（Neo 的 `Crypto.Hash256` 本身即双 SHA-256）。

**实测哈希**（真实 `Neo.L2.State.dll` 生产代码）：链下 `0xe16deec3…4c4ad76` vs 链上布局 `0xe2bf6a77…4912686d` → **不等**。

**触发路径**

1. L2 侧用 Layout A 算叶哈希 → 构入 `WithdrawalTree` → `WithdrawalRoot` → `L2BatchCommitment.WithdrawalRoot`（偏移 156）→ 上链。
2. 用户提现调用 `SharedBridge.FinalizeWithdrawal` / `FinalizeWithdrawalAt` / `FinalizeWithdrawalWithProof` / `EmergencyFinalizeWithdrawalWithProof` —— **四个入口全部**先执行 `ValidateWithdrawalLeafBinding`。
3. 该函数用 Layout C 重算并断言 `recomputed.Equals(leafHash)` → **断言失败，交易回滚**。

**后果**

**所有 L2→L1 提现在链上恒定失败**。L2 侧余额已按 `native.rs:315`（`token.total_supply -= amount`）扣减销毁，L1 侧 `ConsumeAndPayout` 永不执行 → **资产在 L2 侧已扣、L1 侧未释放，双向不可用**。

连带受害：`tools/Neo.L2.Bridge.Cli/Commands/InvocationBuilder.cs:73-84`（用 Layout A 生成的调用脚本必被链上拒绝）、`tests/Neo.L2.IntegrationTests/UT_E2E_L1RpcPollers_FullStack.cs:133-176`。

**根因（git 溯源）**：`aaecd1ef` 把布局定为 Layout A 并**同步**改了链上与链下；随后 `13f91058`（"architecture optimization"）**只改链上合约**（`git show 13f91058 --name-only | grep -c "src/Neo.L2.State/MessageHasher.cs"` = **0**），未回改链下 C# 与 Rust。

**为什么长期未被发现**：`tests/Neo.L2.State.UnitTests/UT_MessageHasher.cs:335` 的黄金向量注释声称是「对照 `ComputeWithdrawalLeafHash` 重新推导的值」，但**实测等于 Layout A**——它锁定的是错误布局；`tests/NeoHub.Contracts.VmTests/UT_SharedBridge_Vm.cs:49` 的 `LeafBytes` 是合约布局的**测试内镜像**，两者之间**没有任何交叉校验**。这是典型的「镜像陷阱」：两套各自自洽的测试，共享同一个被污染的假设。

**修复建议**：统一到链上 Layout C（链上是最终裁决方，且 `l1Asset` 绑定是安全增强——Layout A 根本不绑定 L1 资产，同一 `(chainId, l2Sender, l1Recipient, l2Asset, amount, nonce)` 跨 L1 资产可产生相同叶哈希）。需同步 4 处 + 重算黄金向量 + **新增真正跨链的黄金向量（直接引用合约导出的常量，禁止在测试里重写镜像）**。

---

### P0-2 Gateway guest 的公开输入预像比权威值少 4 字节，递归 Gateway 路径恒定失效

**位置**：`bridge/neo-zkvm-gateway-guest/src/lib.rs:211-220`（`validate_public_input_supplements`）

**问题描述**（审计方已独立读码核实）

权威公开输入预像是 **352 字节**（`bridge/neo-execution-core/src/hashing.rs:331,351`；C# 侧 `src/Neo.L2.State/StateRootCalculator.cs:88-105` = `4+8+8+8+10*32+4`）。但 Gateway guest 重建预像用的是 **348 字节**缓冲：

```rust
let mut public_inputs = [0u8; 348];          // ← 应为 352
public_inputs[..4] = chain_id
public_inputs[4..12] = batch_number
public_inputs[12..20] = commitment[12..20]
public_inputs[20..28] = commitment[20..28]
public_inputs[28..252] = commitment[28..252]
public_inputs[252..284] = supplement[..32]
public_inputs[284..316] = commitment[252..284]
public_inputs[316..348] = supplement[32..]    // ← 缺失 forcedInclusionCount (348..352)
if hash256(&public_inputs) != batch.public_input_hash { return Err(PublicInputHashMismatch) }
```

**根因**：`348` 是 Wave 2 之前的旧域长度。提交 `335485ba`（"close the 352-byte public-inputs wire domain"）把 `neo-execution-core` 与 C# 改为 352 字节并重新生成了 fixture，但**未触及** `bridge/neo-zkvm-gateway-guest/src/lib.rs`（`git show 335485ba --stat -- bridge/` 显示只改了 VK 清单与 `neo-zkvm-guest/src/runtime.rs`）。

**触发路径**：`prove-gateway` daemon → 生成 Gateway Groth16 证明 → guest 入口 `bridge/neo-zkvm-gateway-guest/src/main.rs:15-16` 调用 `validate_public_input_supplements(...).expect("batch commitment fields do not match recursively proven public inputs")` → **对每个真实批次都返回 `PublicInputHashMismatch` → panic**。

**后果**：递归 Gateway 聚合对所有真实批次失败。CI 的 `gateway-recursive-proof` 通道（`.github/workflows/build.yml:551-557`）是唯一的端到端检测点，但它**只在 `workflow_dispatch`/`schedule` 时运行**，PR 上永远跳过 → 缺陷可静默合入。

**隐蔽的安全耦合**：Gateway 的终端公开值是 `hash256(binding170)`，**不包含** 352 字节预像。若有人为「修好」而把 guest 改成 352 字节但**忘记把 `forced_inclusion_count` 补进 170 字节 binding**，该字段就会从递归聚合的承诺中**彻底脱钩**，而链上 `Sp1Groth16VerifierContract` 只校验批次级 VK，**不会**发现 Gateway 层面的脱钩。这是修复时必须一并处理的隐性安全要求。

**同样长期未被发现的原因**：guest 自身的单元测试（`lib.rs:642`）也用 **348 字节**重建预相当期望值——**用自己的错误实现当 oracle**，对该缺陷完全盲。

**修复建议**：`[0u8; 348]` → `[0u8; 352]` 并纳入 `forced_inclusion_count`；由于该字段当前**不在 321 字节 `L2BatchCommitment` 里**，需二选一——(a) 推荐：加入 commitment 固定区（同步改 C# `BatchSerializer` 与 `parse_commitment`）；(b) 放进 170 字节 binding 保留区并同步 C# `GatewayProofBindingSerializer`。**同时必须新增跨 crate 交叉测试**（用 `neo_execution_core::hash_public_inputs_with_forced` 的输出当 oracle），并把 `gateway-recursive-proof` CI 通道改为 PR 必跑。

---

### P0-3 ZkVerifier 外部验证器调用参数打包错误，ZK 证明路径 100% 不可达

**位置**：`contracts/NeoHub.ZkVerifier/ZkVerifierContract.cs:297-300`（审计方已独立读码核实）

```csharp
return (bool)Contract.Call(
    externalVerifier, "verifyZkProof",
    CallFlags.All,
    new object[] { proofSystem, (byte[])vkId, publicInputHash, innerProof }, 1000000);
```

devpack 的 `Contract.Call` 只有**一个**重载（`external/neo-devpack-dotnet/src/Neo.SmartContract.Framework/Services/Contract.cs:57`）：

```csharp
public static extern object Call(UInt160 scriptHash, string method, CallFlags flags, params object?[]? args);
```

`params object?[]` 把 `new object[]{...}` 与 `1000000` 收集为**两个**元素：VM 侧 `ApplicationEngine.CallContract`（`external/neo/src/Neo/SmartContract/ApplicationEngine.Contract.cs:79`）用 `args.Count` 查 ABI `GetMethod(method, args.Count)` → 传入 arity=2，而目标 `verifyZkProof(byte, byte[], byte[], byte[])` arity=4 → 查表失败。尾部 `1000000` 是 Neo2 时代 `Contract.Call(hash, method, flags, args, gas)` 签名的残留。

**后果**：ZK 批次永久无法结算。`RollupHub.IsProofTypeCompatible`（`RollupHubContract.cs:672-686`）对 `SecurityLevelValidity`/`Validium` 只接受 `ProofTypeZk`，这些链的批次**全部**无法提交。确定性故障，无 fallback。

**修复建议**：把 `new object[]{...}` 拆成 4 个平铺参数、删除尾部 `1000000`。建议在 CI 加入「所有 `Contract.Call` 实参个数必须等于目标 ABI 参数个数」的静态检查。

---

### P0-4 `ZkVerifier.verifyZkProof` 硬编码 `return false`，Gateway 全局根发布路径 100% 回滚

**位置**：`contracts/NeoHub.ZkVerifier/ZkVerifierContract.cs:354-364`；调用方 `contracts/NeoHub.RollupHub/RollupHubContract.cs:1080-1088`

```csharp
[Safe]
public static bool VerifyZkProof(byte proofSystem, byte[] verificationKeyId, byte[] publicInputHash, byte[] proofBytes)
{
    // No generated SP1 release parameters are checked into this source tree. ...
    return false;
}
```

**后果**：`RollupHub.PublishGatewayGlobalRoot` 断言失败 → 交易回滚，Gateway 全局根发布功能完全不可用。连带后果：`RollupHubContract.cs:1108-1115` 向 `SharedBridge.publishMessageRoots` 的扇出永不执行，跨链消息根永不落地；`GetGatewayFinalizedThrough` 永远为 0。

**修复建议**：`verifyZkProof` 应与 `VerifyProof` 走同一条外部验证器路由逻辑，而非硬编码 `false`。若确实要保留 fail-closed 语义，则 `PublishGatewayGlobalRoot` 不应调用 registry 的 `verifyZkProof`，而应调用显式的「聚合证明验证」入口。

---

### P0-5 SDK 的 `u64Wire` 入参守卫对 JS `number` 完全失效，`getStateRootAt` 静默返回错误批次的状态根

**位置**：`sdk/typescript/src/index.ts:475-479`（守卫）、`:226-231`（无交叉校验的调用点）

```ts
function u64Wire(value: bigint, name: string): string {
  if (value < 0n || value > 0xffff_ffff_ffff_ffffn)
    throw new L2RpcProtocolError("<arg>", `${name} must be a u64`);
  return value.toString();
}
```

`value: bigint` 只是**静态**类型标注。运行时传入 JS `number` 时，两个比较不会抛错也不会拦截（JS 规范允许 number↔BigInt 关系比较），而 `value.toString()` 对 `number` 得到的是该 double 的十进制展开——**已被截断的值**。守卫只拦「超范围」和「负数」，**拦不住精度截断**，而后者才是该函数存在的唯一理由。

**实测**：`number 9007199254740993` → `"9007199254740992"`（delta −1），静默无错。

**后果按方法分化**：

| 方法 | 后果 |
|---|---|
| `getBatch` / `getBatchStatus` / `getDepositStatus` | 截断后被 SDK 自身的 `!==` 交叉校验捕获 → 失败关闭（但错误信息误导：两边显示同一个数字） |
| **`getStateRootAt`** | **无任何交叉校验**（`:226-231` 只检查 `typeof result !== "string"`）→ **静默返回错误批次的状态根** |

状态根是桥接/验证场景的核心凭据。静默返回错误批次的状态根，会让上层把 A 批次的批次号与 B 批次的状态根配对，做出错误的提款/验证判定。这是本次审计中唯一能造成「资金/序号错误」且不被现有守卫捕获的路径。

**修复建议**：`u64Wire` 增加运行时类型守卫 `if (typeof value !== "bigint") throw ...`（形参标注改为 `unknown` + 内部收窄）；`getStateRootAt` 补交叉校验——根治方案是服务端 `getl2stateroot` 改为返回对象 `{ chainId, batchNumber, stateRoot }` 以便回显校验。

---

## 4. P1 高危发现

### P1-1 治理锁死后 RollupHub 的批次回滚路径完全不可达

**位置**：`contracts/NeoHub.RollupHub/RollupHubContract.cs:751-783`（审计方已独立读码核实）

`RevertBatch` 与 `RevertBatchViaProposal` 在锁治理后均要求 `Runtime.CheckWitness(GetGovernanceController())`。按 `ApplicationEngine.CheckWitnessInternal`（`external/neo/src/Neo/SmartContract/ApplicationEngine.Runtime.cs:251-286`）：

```csharp
if (hash.Equals(CallingScriptHash)) return true;
```

`CallingScriptHash` 在 RollupHub 侧就是 **GovernanceController 的哈希**——要求 GC **必须是发起调用的合约**。而审计方已核实：`GovernanceControllerContract.cs` 中 `Contract.Call` 出现次数为 **1**，且该唯一匹配是**文档注释**（`:536`）；全文不含 `RollupHub` / `revertBatch` 引用 → **GC 没有任何出站调用方法**，该条件永远不成立。

仓库自己的测试注释（`tests/NeoHub.Contracts.VmTests/UT_RollupHub_Vm.cs:591-594`）断言存在这样一条 relay 路径，但从未验证其可达性——**实际上它不可达**。

**后果**：`LockGovernance`（一次性、不可逆）之后系统进入**不可恢复状态**：错误批次一旦被最终化，canonical state root 永久停在错误值上，无法回滚。同时 `SetGovernanceController`（`:1461-1462`）被同一条件锁死，没有任何合约能重新指向新的 GC。这与 `doc.md` §3.2 声称的「batch reverts must be relayed through the GovernanceController」直接矛盾。

**修复建议**：二选一——(a) 在 GC 中增加 relay 入口（如 `ExecuteRollupHubAction(payload, proposalId)`，内部 `Contract.Call(rollupHub, "revertBatchViaProposal", ...)`）；(b) 去掉 `CheckWitness(gov)`，改为校验 `isApprovedAndTimelocked && matchesProposalPayload`，**并同时补 per-proposalId consumed 标志**（照搬 GC 内部 `SetAdmissionModeViaProposal` 的既有模式）——见 P2-5，二者必须同时上线。

### P1-2 `SharedBridge.LockGovernance` 后桥的全部管理入口永久失效

**位置**：`contracts/NeoHub.SharedBridge/SharedBridgeContract.cs:146-163`（`LockGovernance`）及所有 `CheckWitness(GetOwner())` 门禁方法（`:123`、`:172`、`:185`、`:198`、`:216`、`:248`、`:480`）

`LockGovernance` 把 owner 改写为 **GC 合约的哈希**，此后 `CheckWitness(GetOwner())` 要求 GC 是调用方——与 P1-1 同理，**永远不成立**。

**后果**：桥的**全部**运维能力归零且不可恢复：`RegisterMapping` / `SetActive` / `SetSettlementManager` 全部 `"not authorized"`。特别注意 `SetEmergencyManager`（`:198`）失效会连锁影响 `EmergencyFinalizeWithdrawalWithProof`（`:441-471`）——emergency manager 永远无法被设置，**紧急提款通道随之失效**。

**修复建议**：与 P1-1 同源一并修复；`SetEmergencyManager` 应保留 owner 直连通道（该操作不涉及资金、不产生不可逆后果），或明确 emergency 语义不随治理锁一同冻结。

### P1-3 强制包含队列的「已消费」状态在批次结算路径上从不写入

**位置**：`contracts/NeoHub.RollupHub/RollupHubContract.cs:517-526`（批次消费路径）vs `:371-373`（`Consume` 路径）vs `:390-421`（`ReportCensorship` / `IsConsumed`）

审计方已独立读码核实：`ForcedConsumedKey` 的**唯一写入点**是 `:371`（`Consume` 方法内），批次消费路径 `ConsumeForcedTransactionsInternal` **只推进 head**，不写 per-nonce 标志。

**后果（实际可达）**：

1. 用户 A 提交强制交易，nonce=0，排序器把它打进批次 1 并最终化 → head 0→1，`PendingForcedCount` 归 0，但 `IsConsumed(chainId, 0)` 仍为 `false`。
2. 超过 deadline 后，**任意第三方**调用 `ReportCensorship(chainId, 0)` → `!IsConsumed` 守卫通过 → 写入 reportedKey → 若 GC 已接线且 pauseChain 成功（P1-4），**该链被暂停**。

即：反审查机制可被用作对**诚实排序器**的拒绝服务。

另有死锁：`Consume` 要求 `nonce > 0`（`:360`），而 `EnqueueForcedTransaction` 的首个 nonce 从 0 开始 → **队列中第一笔强制交易永远无法被 `Consume` 标记为已消费**。

**修复建议**：`ConsumeForcedTransactionsInternal` 中同步写 `[head, head+count)` 区间内每个 nonce 的 `ForcedConsumedKey`；`Consume` 的 `nonce > 0` 改为 `>= 0`（或让首个 nonce 从 1 开始），使两条路径的 nonce 空间一致。

### P1-4 `ReportCensorship` 无权限检查，且 GC 侧调用方身份检查可被合约哈希绕过

**位置**：`contracts/NeoHub.RollupHub/RollupHubContract.cs:390-414`；`contracts/NeoHub.GovernanceController/GovernanceControllerContract.cs:1027-1034`

`ReportCensorship` **完全无权限检查**，且会无条件调用 `Contract.Call(governance, "pauseChain", ...)`。GC 侧 `PauseChain` 的授权为：

```csharp
var authorized = Runtime.CheckWitness(GetOwner()) || (council != UInt160.Zero && Runtime.CheckWitness(council));
```

而 `CheckWitnessInternal` 的第一行 `if (hash.Equals(CallingScriptHash)) return true;` 意味着：**若 `emergencyCouncil` 被设置为 RollupHub 自己的哈希**，`CheckWitness(council)` 在 GC 内部因 `council == CallingScriptHash` 恒真 → **任意第三方调用 `ReportCensorship` 即可暂停任意链**。

**两个场景**：
- **场景 A**（`emergencyCouncil == hubHash`）：**permissionless 链暂停**——任何人用一笔过期强制交易即可把目标链打成 paused，阻断其全部批次提交与强制交易入队。可被第三方触发的拒绝服务。
- **场景 B**（默认部署，council 未设置）：`CheckWitness(GetOwner())` 中 GC owner 是 EOA 哈希 ≠ RollupHub 哈希 → 落到 tx signer 分支 → 只有 GC owner 恰好是本交易签名者时才通过 → **`ReportCensorship` 在正常部署下 100% 回滚**，反审查记录也一并回滚丢失。

**修复建议**：`PauseChain` 的授权必须基于调用者身份而非「恰好等于 CallingScriptHash」的哈希；`SetEmergencyCouncil` 应拒绝设置为任何已部署合约的哈希；`ReportCensorship` 的治理调用应改为「先记录、后提议」——无权限调用者只写 `reportedKey` 与事件（保证反审查证据不丢），由有权限角色后续执行暂停。

### P1-5 `MultisigVerifier` 的 quorum 计数不校验签名者去重，单一签名者可刷满 quorum

**位置**：`contracts/NeoHub.MultisigVerifier/MultisigVerifierContract.cs:150-181`（审计方已独立读码核实）

```csharp
var validSignatures = 0;
for (var i = 0; i < signerCount; i++)
{
    ...
    if (!isRegistered) continue;
    var isValid = CryptoLib.VerifyWithECDsa(...);
    if (isValid) validSignatures++;      // ← 无去重，同一 (pubkey,sig) 可重复计数
}
return validSignatures >= minQuorum;
```

`validSignatures` 按**条目**计数，不按**不同签名者**计数。把同一个已注册 sequencer 的 `(pubkey, signature)` 对重复 N 次即可让 `validSignatures` 达到 N，而 `signerCount` 只受 `MaxMultisigSigners = 256` 与长度自洽检查约束。

**后果**：委员会 M-of-N 签名门被降级为 1-of-N——**任何单个已注册 sequencer** 即可伪造「委员会已集体签署」的证明。`RollupHub.IsProofTypeCompatible`（`:674-677`）允许 `SecurityLevelSidechain`/`SecurityLevelSettled` 链使用 `ProofTypeMultisig`，这些链的批次完整性保证被完全绕过。

**修复建议**：计数时按公钥去重（维护 `seen` 列表，同一公钥只计一次）。

### P1-6 `neo_zkvm_host::verify()` 接受调用方任意 VK，绕过 pinned guest VK 约束

**位置**：`bridge/neo-zkvm-host/src/lib.rs:212-223`

```rust
pub fn verify(proof_bytes: &[u8], vk_bytes: &[u8], expected_public_input_hash: &[u8; 32]) -> Result<(), String>
{
    let vk: [u8; 32] = vk_bytes.try_into()...;   // ← 任意 32 字节，未与 NEO_ZKVM_GUEST_VK_BYTES32 比对
    ...
}
```

文档注释声称这是 "against the canonical guest verifying key"，但实现**从不检查** `vk_bytes == NEO_ZKVM_GUEST_VK_BYTES32`（`lib.rs:29`）。该常量在全仓仅被 3 处使用：examples、`groth16_release_vector.rs` 测试、**从未在 `verify()` 内**。

**触发路径**：`prove_batch.rs:1193` 的证明恢复路径用**磁盘上**的 `{stem}.proof.vk` 文件调用 `verify()`，而非编译期 pinned 常量。`read_regular_bounded` 只做 `O_NOFOLLOW` + 常规文件校验，**不校验属主/权限**（对比 `validate_private_regular_file` 只施加在 `*.batch.bin` 输入上）。

**后果**：可写入 watch 目录者能植入「针对任意自选 program 的合法 Groth16 证明」+ 匹配 VK，触发 `recover_or_clean_existing_proof` 走恢复分支而**完全跳过真实 proving**。**下游必然拒绝**（C# `Sp1BatchProofProver.cs:252-254` 与链上 `Sp1Groth16VerifierContract.cs:82` 都强制 VK 等于锁定值）→ **不构成链上伪造**，但 host 侧证明完整性降级，daemon 会把「针对错误 program 的证明」标记为 succeeded。

**修复建议**：`verify()` 内强制 `vk == NEO_ZKVM_GUEST_VK_BYTES32`；恢复路径复用 `validate_private_regular_file`（属主 + 0600）；恢复分支校验 `vk_bytes == artifact.verification_key_id == NEO_ZKVM_GUEST_VK_BYTES32` 三者一致。

---

## 5. P2 / P3 发现摘要

### P2 级（10 项）

| # | 发现 | 位置 |
|---|---|---|
| P2-1 | `ProofType` 判别值错位：Rust `Zk=2` vs 链上 `Zk=3`；Rust 侧自身矛盾（`artifact.rs:49`/`batch.rs:247` 硬编码 3，而 `commitment.rs:36` 用枚举值）。当前生产路径走 `artifact.rs` 故**不可达**，但 `neo-execution-core` 的公开 API 一旦被外部工具使用即错位 | `bridge/neo-execution-core/src/types.rs:392-411`、`src/Neo.L2.Abstractions/Models/ProofType.cs:10-23` |
| P2-2 | 网关全局消息根折叠语义分叉：C# `Sp1RecursiveRoundProver` 用 `promote_odd`（提升末叶），C# `MerkleTree.ComputeRoot` 与 Rust `constituent_root` 用 `duplicate_odd`（复制末叶）。**奇数个 constituent 时分叉**，表现为「偶数批成功、奇数批卡住」的间歇故障。生产路径（`Sp1RecursiveRoundProver` ↔ Rust guest）一致，故当前**不可达**，但 `PassThroughAggregator` 用了另一套语义 | `src/Neo.Plugins.L2Gateway/Sp1RecursiveRoundProver.cs:59-70`、`src/Neo.L2.State/MerkleTree.cs:80-104` |
| P2-3 | proof system 0 无法注册（`ZkVerifierContract.cs:132-136` 拒绝 0），但 `VerifyMultisigProof` 固定用 0 → `ProofType.Multisig` 整条路径不可用；`deploy-plan-5pillar.json` 部署了 `MultisigVerifier` 却无任何注册步骤 | `contracts/NeoHub.ZkVerifier/ZkVerifierContract.cs:132-136`、`:319-345` |
| P2-4 | 已提交的合约测试产物严重过期 + **三个测试套件直接读 gitignore 的 `bin/sc` 目录** → 全新检出中 17 个测试必失败（审计方实测：`nccs` 编译后 338/338、11/11 立即全绿） | `tests/NeoHub.Contracts.VmTests/TestingArtifacts/`、`UT_ZkVerifier_Vm.cs:33-41`、`ManifestTestHelper.cs:35`、`scripts/contracts/regenerate-testing-artifacts.sh:22` |
| P2-4b | **文档与代码事实不一致**（9 个测试持续失败）：README 称 5 个合约实际 6 个；模板目录称 4 个实际 11 个；`docs/launching-an-l2.md` 中英双份模板表均漂移 | `README.md`、`docs/launching-an-l2.md` |
| P2-5 | RollupHub 的三个 `*ViaProposal` 方法缺 per-proposalId consumed 标志（对比 GC 内部的 `SetAdmissionModeViaProposal` 等都有）。当前因 P1-1 不可达，**但修复 P1-1 后立即变为可利用**——属「修复顺序敏感」缺陷，必须与 P1-1 同时上线 | `contracts/NeoHub.RollupHub/RollupHubContract.cs:766-832` |
| P2-6 | envelope-only 模式 fail-open 且关闭粒度不足：`DisableEnvelopeOnlyPermanently(proofSystem)` 只写 per-system 标志，不写全局 `PrefixAnyProofSystemLocked`；而 `proofSystem` 是攻击者可控的 payload 字段 → 攻击者可挑选仍处 fail-open 的 system，提交**任意伪造的状态根**并被接受 | `contracts/NeoHub.ZkVerifier/ZkVerifierContract.cs:279-289`、`:169-175` |
| P2-7 | 强制包含证明在 Rust 侧不做任何密码学校验：只取 `forced_inclusions.len()` 绑定进 public-input 域，不验证 Merkle 证明是否真的指向该批次的交易树 → 攻击者可在不真正包含强制交易的情况下提交 `forced_inclusion_count = N`，链上按数量推进 head → **审查攻击**。验证责任未落在共识层 | `bridge/neo-execution-core/src/wire/payload.rs:63-82`、`src/batch.rs:187-188` |
| P2-8 | `PooledBatchSerializer.WriteCommitmentToSpan` 是 commitment 编码的**第二份实现**，丢失了 `BatchSerializer.Encode` 的全部 10 项校验（null 守卫、1 MiB proof 上限、ProofType 范围、checked 溢出、长度自检）；`ProofMaxBytes` 常量定义后**从未使用** | `src/Neo.L2.Batch/PooledBatchSerializer.cs:95-152` |
| P2-9 | `KeyedStateStore.ComputeRoot` 与 `KeyedStateMerkleTree.ComputeRoot` 两份状态根实现；前者**依赖后端枚举顺序**（`IL2KeyValueStore` 契约未把「字节序 = `SequenceCompareTo` 序」写死），任何新后端若用有符号比较即**静默产生不同根** | `src/Neo.L2.Executor/State/KeyedStateStore.cs:78-103` vs `src/Neo.L2.State/KeyedStateMerkleTree.cs:62-67` |
| P2-10 | prover 无超时/并发上限/内存护栏：宿主未设 `MEMORY_LIMIT`，SP1 默认 24 GiB（CI 用 4 GiB 覆盖，**生产部署无此保护**）；daemon 串行处理，单次挂死阻塞整个队列；失败批次**保留输入文件** → 毒丸被每轮无限重试。另有 Gateway daemon 完全无队列背压，单请求可致 ~2 GiB 常驻内存 | `bridge/neo-zkvm-host/src/lib.rs:86-129`、`src/bin/prove_batch.rs:437-551`、`bridge/neo-zkvm-gateway-host/src/daemon.rs:55-81` |

#### P2-4 详情：合约测试产物过期 + 测试依赖 gitignore 目录

**第一重 —— 已提交产物与源码脱节**（重新编译 vs 已提交产物比对）：

| 合约 | 已提交产物 | 最新源码 | 差异 |
|---|---|---|---|
| RollupHub | 42 个方法 | 56 个方法 | **缺 14 个**：`reportCensorship`、`consume`、`isConsumed`、`isCensorshipReported`、`revertBatchViaProposal`、`updateChainViaProposal`、`setChainActiveViaProposal`、`buildRevertBatchAction`、`buildUpdateChainAction`、`buildSetChainActiveAction`、`getDeadlineSeconds`、`setDeadlineSeconds`、`getFee`、`setFee` |
| GovernanceController | 43 个方法 | 62 个方法 | **缺 19 个**：`pause`、`pauseChain`、`unpause`、`unpauseChain`、`isChainPaused`、`setEmergencyCouncil`、`getEmergencyCouncil`、`registerSequencer`、`unregisterSequencer`、`isSequencerRegistered`、`getSequencerAddress`、`depositBond`、`withdrawBond`、`slashSequencer`、`getSequencerBond`、`hasMinBond`、`getMinBond`、`setMinBond` |
| SharedBridge | 35 个方法 | 37 个方法 | 缺 `lockGovernance`、`isGovernanceLocked` |

5 个合约的已提交产物 NEF 字节 sha256 与最新源码**全部不一致**。已提交产物的生成提交为 `34969d19`，而 `ZkVerifierContract.cs` 的最新修改是 `c51b44af`（晚于产物）。

**第二重 —— 三个测试套件直接读 gitignore 的 `bin/sc` 目录**（`git check-ignore -v` 已确证 `bin/` 被 `.gitignore:1` 排除）：
- `tests/NeoHub.Contracts.VmTests/UT_ZkVerifier_Vm.cs:33-41`
- `tests/Neo.Hub.Deploy.UnitTests/ManifestTestHelper.cs:35`
- `tests/NeoHub.Sp1Groth16Verifier.UnitTests/`（读 `contracts/NeoHub.Sp1Groth16Verifier/bin/sc/`）

**审计方实测验证**：执行 `nccs` 编译全部 6 个合约后，两个套件**立即全绿**——`NeoHub.Contracts.VmTests` 338/338、`NeoHub.Sp1Groth16Verifier.UnitTests` 11/11。这确证了根因判定（环境缺失，不是合约行为错误）。

**第三重 —— 脚本遗漏**：`scripts/contracts/regenerate-testing-artifacts.sh:22` 的 `ALL_CONTRACTS` 只列 5 个合约，**遗漏 `MultisigVerifier`**。

**后果**：P1-3 / P1-4 / P1-5 涉及的代码路径在 CI 中**零覆盖**；P0-3 本可被 `UT_ZkVerifier_Vm` 的真实外部验证器用例捕获，但该测试类在全新检出中全部失败、无人修复——这解释了为何该缺陷能长期存活。

**修复建议**：三个套件改读 `TestingArtifacts/`（而非 gitignore 的 `bin/sc`），或在 CI 中于测试前显式执行 `nccs`；CI 增加「重新编译后比对 NEF sha256 与 manifest 方法集」门禁；`ALL_CONTRACTS` 补 `MultisigVerifier`；为所有 `TestEngine` 统一设置 `engine.Fee`（`UT_GovernanceController_Vm` 从未设置，而 `UT_RollupHub_Vm.cs:50` 设置了 `100_000_000_000L`）；为 `ReportCensorship`/`Consume`/`*ViaProposal`/`PauseChain` 补 VM 用例。

#### P2-4b 详情：文档与代码事实不一致（9 个测试持续失败）

| 失败测试 | 事实 |
|---|---|
| `Repository_DeployPlanMatchesNeoHubContractInventory` | `contracts/NeoHub.*` 实际有 **6** 个项目（GovernanceController / MultisigVerifier / RollupHub / SharedBridge / Sp1Groth16Verifier / ZkVerifier），README 声称 **5** |
| `CurrentDocumentation_UsesCurrentNeoHubCounts` | README 缺少 "6 NeoHub L1 contract projects" 字样 |
| `CurrentDocumentation_UsesSourceDrivenTestAndSdkInventories` | README 的测试/SDK 清单数量与实际不符 |
| `CurrentDocumentation_EveryEnglishMarkdownHasChineseCounterpart` | 存在英文文档缺中文对应版本 |
| `Catalog_HasExactlyFourTemplates_InOrder` | 模板目录实际 **11** 个（`rollup, zk-rollup, validium, sidechain, dex, gaming, defi, social, nft, payment, enterprise`），测试期望 **4** 个 |
| `Catalog_NonSidechainTemplates_NameARouteTheDeployerRegisters` | 新增的 7 个非 sidechain 模板未全部接入 deployer 注册路由 |
| `LaunchingGuide_TemplateTable_MatchesTheCatalog` | `docs/launching-an-l2.md` 的模板表仍是 4 个 |
| `LaunchingGuide_ChineseTemplateTable_MatchesTheCatalog` | 中文版同样漂移 |
| `Catalog_ValidNames_ListsAllInOrder` | 有效名清单未同步新增模板 |

**性质**：这些是**仓库自身已存在的不一致**（远端 master 无新提交，与本次拉取无关），但会持续让 CI 变红。

**修复建议**：以实际代码为准更新 README 与 `docs/launching-an-l2.md`（中英双份）的合约数、模板数、测试数；补齐新增 7 个模板的 deployer 注册路由；补齐缺失的中文文档对应版本。**特别注意**：README 声称的「All 5 core contracts are operational with 100% test coverage」与本次审计结论（P0 主路径不可用）直接冲突，发布前必须更正。

### P3 级（9 项）

- **P3-1** `GovernanceController.IsApprovedAndTimelocked` 不复核当前阈值（只查 `approvedAt != 0`）。阈值变更与 epoch 变更在 `RotateCouncil` 中原子，epoch 校验先行 → **未找到构造路径**，但安全性依赖「epoch 校验永远在前」这一隐式顺序约定。
- **P3-2** `SharedBridge` 的 `MigrateLockedBalance` 在 `_deploy` 时即被封存 → 死代码；若确有意封存（防止 owner 任意改账），应删除该公开方法消除误用入口。
- **P3-3** `Sp1StatefulBatchExecutor` 热路径缓存跨批次复用；下游有 post-state root 校验与 `CompareExchangeAll` 全量比对兜底 → 最坏是被拒绝并抛错，不写错误状态。
- **P3-4** `SP1_VERSION` 是手工维护字符串，与实际链接的 SP1 版本无编译期一致性检查（但 `=6.2.1` 精确 pin + ELF SHA-256/VK 双重校验已覆盖实际风险）。
- **P3-5** `bincode` 写入侧允许 trailing bytes、读取侧拒绝，两侧配置不对称（当前字节兼容 + 规范重编码校验，无实际后果）。
- **P3-6** SDK 的 `u64` 交叉校验用 `bigint !== number` 严格相等 → JS 消费者传 number 时**每次调用都失败**，错误信息极具误导性（两边显示同一个数字）。
- **P3-7** SDK 对所有枚举字节不做范围校验（`.NET SDK` 有逐字段校验）；`DAMode.Local = 255` 超出 TS 枚举范围且 TS 枚举**完全缺失**该成员。
- **P3-8** TS SDK `SequencerModel` 成员 2 命名为 `PoSRotation`，C# 权威定义为 `Decentralized`——**语义不同**，不是翻译差异；Rust/Python SDK 同样偏离。
- **P3-9** SDK 的 `timeoutMs` **不覆盖响应体读取**：`clearTimeout` 在 fetch 的 `finally` 中执行，而 `response.json()` 在其后 → 恶意/慢速服务端可让调用方无限挂起（Rust/Python/.NET SDK 均无此洞）。

---

## 6. 供应链审计

### 6.1 Cargo（`cargo audit --file Cargo.lock`）

**13 个漏洞 + 9 个警告**：

| 类别 | 数量 | 明细 |
|---|---|---|
| `quinn-proto 0.11.15` | 5 | RUSTSEC-2026-0338/0339/0340/0341/0342（含 0341「流重组内存耗尽守卫可被绕过」） |
| `rustls 0.23.40` | 4 | RUSTSEC-2026-0285/0346/0348/0349（0348「Brotli 证书解压可在认证前分配达 1 GiB」） |
| `rustls-webpki 0.103.13` | 1 | RUSTSEC-2026-0382 |
| `ruzstd 0.8.3` | 1 | RUSTSEC-2026-0344（解压炸弹，tiny input → OOM） |
| `webpki-root-certs / webpki-roots` | 2 | RUSTSEC-2026-0383 / 0381 |
| 警告 | 9 | 含 `lru 0.12.5`（unsound）、`spin 0.9.8`（yanked）等 |

**可利用性判定**：依赖反向追踪显示全部 13 个漏洞都落在 `sp1-prover` / `sp1-core-executor` 的传递依赖中（`quinn-proto` ← `quinn` ← `reqwest` ← `sp1-prover`；`ruzstd` ← `object` ← `sp1-core-executor`），**确实在 bridge 二进制的依赖闭包内**。但 `reqwest` 只被 SP1 的**网络 prover** 路径使用，而本仓库**只用 CPU prover**（`bridge/neo-zkvm-host/src/lib.rs:57,88,159` 均为 `ProverClient::builder().cpu()`）；`object` 依赖用于 DWARF 调试信息解析，不在证明路径上 → **当前不可达**，属待升级技术债。

**流程问题**：CI 仅 `--ignore RUSTSEC-2026-0258`（h2），而实际有 13 个漏洞——需确认该步骤是否被 `continue-on-error` 掩盖（`build.yml:607-625` 未见该设置），若未掩盖则该 CI 步骤当前应为红色。

### 6.2 npm（`sdk/typescript`）

**4 个漏洞（2 moderate / 2 high）**，全部在 devDependencies：

- `vitest` ← `@vitest/mocker`：Path Traversal / Arbitrary File Read（GHSA-82fw-gwwq-j7x9）
- `source-map-js 1.0.0-1.2.1`：事件循环 DoS（GHSA-68fv-2mgg-jv7q）
- `vite 8.0.0-8.1.5`：`launch-editor` 的 Windows `%` 转义缺失导致任意命令执行（GHSA-698v-4r3x-3vcv）+ `fs.existsSync` 存在性检查可被绕过（GHSA-587h-7wmj-h5pm）

**判定**：均为 dev-only 依赖，**不进入生产产物**；但 `vite` 的修复需 `--force`（会超出声明的依赖范围），需评估。

### 6.3 NuGet

`Directory.Build.props` 已启用 `NuGetAudit=true` + `NuGetAuditMode=all`（扫描直接 + 传递依赖），且把 `NU190x` 从 `TreatWarningsAsErrors` 中豁免（`WarningsNotAsErrors`）——即「审计运行，但 advisory 不阻断构建」。由于本次构建未产出任何 `NU190x` 警告，**当前无已知 NuGet advisory**。

### 6.4 供应链加固项（已核实为良好实践）

- `Cargo.lock` **无 `git+` 依赖、无 `[patch]` 段**；bridge 的 path 依赖全部指向仓库内或**固定 commit 的子模块**（`external/neo-vm-rs` @ `afc2de3`）
- `sp1-sdk` / `sp1-zkvm` / `sp1-verifier` 全部 `=6.2.1` 精确 pin
- Docker 镜像由 `assert_eq!` 强制等于 digest-pinned 常量（`bridge/sp1_build_support.rs:45-54`、`sp1_runtime_support.rs:32-42`），且校验的是**实际生效**的环境变量名（`SP1_GNARK_IMAGE`）
- Guest ELF 的 SHA-256 与 VK 均为**编译期常量**（`pinned::PINNED_BATCH_ELF_SHA256`），**不可本地替换**

---

## 7. 跨语言编码一致性核对（专项）

以下编码在 C# 与 Rust 之间经**实测字节级对照**确认一致（`tests/Shared/canonical_encoding_vectors.hex` 双向锁定 + `canonical_encoding_parity.rs` 实跑通过）：

| 编码 | C# | Rust | 结论 |
|---|---|---|---|
| 公共输入哈希 352 B | `BatchSerializer.EncodePublicInputs` | `hashing.rs:334-372` | ✅ 一致 |
| Commitment 头 321 B | `BatchSerializer.Encode` | `wire/commitment.rs:16-40` | ✅ 布局一致（**ProofType 判别值除外 → P2-1**） |
| 执行载荷 `NEO4EXEC` | `ProofWitnessSerializers.cs:38-88` | `wire/payload.rs:107-188` | ✅ 一致 |
| 状态见证 `NEO4STW1` | `StateWitnessV1.cs:154-186` | `wire/state.rs:204-255` | ✅ 一致（含合约绑定叶 key 与 hash 域逐字节一致） |
| 规范栈 `NEO4STK1` | `CanonicalStackStateSerializer.cs:21-34` | `hashing.rs:170-181` | ✅ 一致（标签集、深度上限 16、节点上限 512） |
| 存储/事件效果哈希 | `CanonicalExecutionEffects.cs:242-301` | `hashing.rs:135-168` | ✅ 一致 |
| 收据哈希 105 B | `Receipt.cs:41-57` | `hashing.rs:119-127` | ✅ 一致 |
| 原生执行输出 `NEO4EXR1` | `Sp1NativeExecutionOutputV1.cs:77-103` | `wire/native_output.rs:87-135` | ✅ 一致 |
| 交易哈希 | `Transaction.Hash` = SHA256(SerializeUnsigned) | `inventory_hash` | ✅ 原始字节完全一致（专项实测） |
| **提现叶哈希** | `MessageHasher.cs:105-138` | `native.rs:935-955` | ⚠️ 链下两侧一致，但**与链上合约分叉 → P0-1** |
| **Gateway 公开输入预像** | `StateRootCalculator.cs:88-105`（352） | `hashing.rs`（352） | ⚠️ 但 **gateway-guest 用 348 → P0-2** |
| **ProofType 判别值** | `ProofType.cs`（Zk=3） | `types.rs`（Zk=2） | ⚠️ 分叉 → P2-1 |

**其余已核对一致的安全属性**：

- **批次长度上限**在编码器与解码器**两侧**强制（非仅配置）：`MaxItemCount = 1_000_000` = `MAX_PAYLOAD_ITEMS`；`MaxMessagePayloadBytes = 4 MiB` = `MAX_MESSAGE_BYTES`；`MaxTransactionBytes = 16 MiB` = `MAX_PAYLOAD_TRANSACTION_BYTES`；`MaxStateWitnessBytes = 128 MiB` = `MAX_STATE_WITNESS_BYTES`；`MaxEffectsBytes = 64 MiB` = `MAX_EFFECTS_BYTES`
- **Rust 侧 `read_count`** 在 `Vec::with_capacity` **之前**校验计数 ≤ 上限**且** ≤ 剩余字节数（防截断载荷诱发巨额瞬时分配）；`read_var_int` 拒绝非规范 varint
- **充值防双花**：`(sourceChainId, nonce)` 持久去重键，C# 与 Rust 语义一致
- **DA 原子性**：临时文件 + `File.Move` 原子重命名 + `FileMode.CreateNew` + `WriteThrough` + `flush(flushToDisk:true)`；路径穿越防护（`fileName == Path.GetFileName(fileName)`，拒绝 `.`/`..`）
- **无裸 static 可变单例**；`RocksDbKeyValueStore` 全部写操作使用 `WriteOptions.SetSync(true)`
- **结算窗口 TOCTOU**：发送前重读 deadline 二次断言，且**链上合约重新断言窗口过期** → 窗口内状态被改也无法绕过

---

## 8. RPC ↔ SDK ABI 对账

**方法名层面 10/10 完全匹配**（脚本化对账，`grep -rn '\[RpcMethod' --include=*.cs` 全仓确认 `L2RpcServerAdapter.cs` 是唯一注册文件），无单边方法、无命名偏差。问题全部位于参数语义、返回结构、错误处理、类型校验层面。

| RPC 方法 | SDK 方法 | 状态 |
|---|---|---|
| `getl2batch` | `getBatch()` | ✅ |
| `getl2batchstatus` | `getBatchStatus()` | ✅ |
| `getl2stateroot` | `getLatestStateRoot()` / `getStateRootAt()` | ✅（1:2 映射） |
| `getl2withdrawalproof` | `getWithdrawalProof()` | ✅ |
| `getl2messageproof` | `getMessageProof()` | ✅ |
| `getl1depositstatus` | `getDepositStatus()` | ✅ |
| `getcanonicalasset` | `getCanonicalAsset()` | ✅ |
| `getbridgedasset` | `getBridgedAsset()` | ✅ |
| `getsecuritylevel` | `getSecurityLevel()` | ✅ |
| `getsecuritylabel` | `getSecurityLabel()` | ✅ |

**RPC 侧 P1 级**：

- **P1-7** `getl2batch` 返回**存储批次的** chainId（`L2RpcMethods.cs:255`），`getl2batchstatus` 返回**请求参数** chainId（`:49`）——语义不一致。`InMemoryL2RpcStore.AddBatch`（`:164-170`）不校验 `commitment.ChainId == this.ChainId`，可存入异链批次 → SDK 的 `assertChainId` 抛 `L2RpcMismatchedChainIdError`，调用方会误判为「连错链」而放弃重试或切换节点。**服务端缺陷，.NET SDK 同受影响**。
- **P1-8** `getl2stateroot` 对**未知批次返回全零哈希**（`L2RpcMethods.cs:63-64` → `InMemoryL2RpcStore.cs:263-264`），而非 `null` 或错误——同服务端内与 `getl2batch` 的 `:39`（返回 `JToken.Null`）语义不一致。零根在某些链配置下是合法值，调用方无法区分「批次不存在」与「状态根为零」。实测：离线一致性向量 `state-root-max-u64` 的期望值 `0x3333…` 与真实服务端返回的零哈希**不一致**，因离线测试用桩 fetch 从不接触真实服务端逻辑。

---

## 9. 修复优先级建议

### 第一批（解除 P0，恢复主路径可用性）

1. **P0-1** 统一提现叶哈希布局到链上 Layout C（4 处代码 + 黄金向量 + 跨链黄金向量）
2. **P0-2** Gateway guest 公开输入 348 → 352，并解决 `forced_inclusion_count` 的承载位置；补跨 crate 交叉测试；CI 通道改 PR 必跑
3. **P0-3** `ZkVerifier` 外部验证器调用参数平铺、删除尾部 `1000000`
4. **P0-4** `verifyZkProof` 改走外部验证器路由（而非硬编码 `false`）
5. **P0-5** SDK `u64Wire` 运行时类型守卫 + `getStateRootAt` 交叉校验 + 服务端 `getl2stateroot` 返回可回显的对象结构

### 第二批（安全边界修复）

6. **P1-1 + P1-2 + P2-5**（必须同批）：GC 增加 relay 入口 **或** 改为提案驱动 + 补 per-proposalId consumed 标志
7. **P1-3** 批次消费路径写 per-nonce consumed 标志 + 统一 nonce 空间
8. **P1-4** `PauseChain` 基于调用者身份鉴权；`SetEmergencyCouncil` 拒绝合约哈希；`ReportCensorship` 改为「先记录后提议」
9. **P1-5** Multisig 按公钥去重计数
10. **P1-6** `host::verify()` 强制 pinned VK；恢复路径加强文件属主校验
11. **P2-6** envelope-only 关闭时同时写全局锁定标志
12. **P2-7** Rust 侧补齐强制包含的 Merkle 证明校验（把验证责任落到共识层）

### 第三批（工程与流程）

13. **P2-4** 重新生成合约测试产物；`UT_ZkVerifier_Vm` 改读 `TestingArtifacts`；CI 加「重新编译后比对 NEF sha256 与 manifest 方法集」门禁；`ALL_CONTRACTS` 补 `MultisigVerifier`
14. **P2-10 / P3-x** prover 资源护栏（超时、`MEMORY_LIMIT`、批次级 GAS 上限、毒丸隔离、Gateway 背压）
15. 供应链：Cargo 13 个 advisory 随 sp1-sdk 升级清理；CI 确认 cargo-audit 步骤未被 `continue-on-error` 掩盖
16. **P1-7 / P1-8** RPC 侧 chainId 语义统一 + 未知批次返回可区分信号

### 流程改进（防止同类缺陷复发）

- **CI 增加「所有 `Contract.Call` 实参个数 == 目标 ABI 参数个数」的静态检查**（可捕获 P0-3）
- **CI 增加「跨语言黄金向量」门禁**：C# 与 Rust 共享同一份 fixture 文件，**禁止在测试代码里重写镜像**（P0-1 与 P2-2 的共同根因）
- **CI 增加「guest 自测不得用自己的实现当 oracle」的交叉测试要求**（P0-2 的根因）
- **CI 增加 MSBuild 前置清理**（`git clean -xfd`），避免陈旧 `bin/obj` 导致的 MSB4166 阻塞

---

## 10. 审计方法与可复现性

| 手段 | 说明 |
|---|---|
| 合约 VM 执行 | 真实 NeoVM（`Neo.SmartContract.Testing` 3.10.1）执行专项验证测试；用 nccs 3.9.1 重新编译 6 个合约 |
| 跨语言字节对照 | 独立 Python 脚本重算三方布局（提现叶、Merkle 折叠、ProofType） |
| 真实生产函数调用 | 引用真实 `Neo.L2.State.dll` 取地面真值；引用真实 `Neo.dll` 验证交易哈希 |
| Rust 实跑 | `cargo test --offline --lib` → 16 passed |
| Node 实机验证 | JSON.parse 大整数截断、u64Wire 行为、超时覆盖范围（真实 HTTP 挂起服务器）、A/B 变体隔离根因 |
| C# 服务端探针 | 直接调用真实 `L2RpcMethods` 验证 chainId 语义与未知批次返回 |
| 依赖扫描 | `cargo audit --file Cargo.lock --json`、`npm audit`、`NuGetAudit`（构建期） |
| 溯源 | `git log` / `git show --stat` / `git submodule status` / `git ls-remote` |

**结论可信度分层**：

- **高置信（实测复现）**：P0-1、P0-2、P0-3、P0-5、P1-5、P1-6、P1-8、P2-1、P2-2 —— 均有实际执行或字节级对照证据
- **高置信（读码确证 + 独立复核）**：P0-4、P1-1、P1-2、P1-3、P1-4、P1-7、P2-5 —— 审计方逐行读源码核实关键行号
- **中置信（可达性依赖下游）**：P2-6、P2-7 —— 已读下游代码确认约束，但缺少端到端复现
- **明确标注「未找到构造路径」**：P3-1、P2-5 的可利用性 —— 均给出「为什么构造不出来」的具体理由，未夸大为可利用

**源码漂移风险**：涉及既有构建产物 `Neo.L2.State.dll` 的结论，其漂移风险经四重证据链判定为**极低**：工作树文件与 HEAD 提交态逐字节一致；该文件自 `71b9737e` 起无任何提交修改且该提交是 HEAD 祖先；DLL 构建时间戳晚于全部源码 34 天；**最强证据**——DLL 实跑行为与 HEAD 提交态测试中的黄金向量逐字节吻合。残留不确定性：由于环境侧 MSBuild 缺陷无法从零重建（但已通过清理陈旧产物后成功重建完整解决方案闭合），建议在 CI 环境复跑一次跨语言黄金向量以完全闭合该环。

---

## 11. 未发现问题的已检查项（节选）

**合约权限与访问控制**：RollupHub / GovernanceController 全部 owner 门禁方法均为 `CheckWitness(GetOwner())`，无 `hash.Equals(CallingScriptHash)` 绕过面；GC `_deploy` 边界断言完备（成员非空、`threshold > 0`、`threshold <= members.Length`、成员去重、成员数上限）。

**重放与幂等**：GC 的 `SetAdmissionModeViaProposal` / `SetImmutableFlagViaProposal` / `RotateCouncil` 三个提案入口**均有** per-proposalId consumed 键；RollupHub 批次序号单调性 + DA 唯一性；SharedBridge 四条提款路径均先查后写 `WithdrawalKey`；`L2BridgeContract.ApplyDeposit` 与 `L2InteropVerifier.ConsumeMessage` 均有跨链去重键；`L2AccountAbstraction.ConsumeNonceInternal` 严格序列。

**算术与金额**：`DebitLockedBalance` 每链托管余额守恒（`currentBal >= amount`）；`ScaleAmount` 降精度有余数时**抛异常**而非静默截断；`L2FeeContract.Distribute` 三项之和恒等于 amount；`IncrementNonce` 在 `ulong.MaxValue` 抛异常不回绕；`ToUnsignedLittleEndian` 正确处理符号位。

**状态机与资产守恒**：`RevertBatchInternal` 只允许回滚「待定批次或最新已定稿批次」；回滚时 `CanonicalRootKey` 恢复为 `preStateRoot`；`ValidateWithdrawalLeafBinding` 校验 `GetL2Asset(asset, chainId) == l2Asset` 防跨链冒用；`VerifyWithdrawalLeafWithProof` 的 V5 位置绑定拒绝被 `+2^depth` 重标号的 leafIndex；`L2AccountAbstraction.ExecuteTx` 的 txHash 全绑定 → nonce 序列 → 消费 → 执行顺序正确，无重入窗口。

**存储键与事件**：RollupHub 前缀 0x01–0x49、GC 前缀、SharedBridge 前缀逐一核对，无跨语义碰撞；批次维度键统一 `prefix ‖ chainId(4B LE) ‖ num(8B LE)`，forced 维度统一 `prefix ‖ chainId(4B) ‖ nonce(8B)`；事件参数与状态变更逐一核对一致。

**跨合约调用**：RollupHub → ZkVerifier / SharedBridge / GC 的调用参数个数与目标 ABI 匹配（**唯一例外是 P0-3**）；无递归/重入窗口。

**供应链加固**：Guest ELF 的 SHA-256 与 VK 为编译期常量不可本地替换；`=6.2.1` 精确 pin 阻止自动升级；Docker/Gnark 镜像 digest 强制；无 `unsafe` 块；无 `git+` 依赖或 `[patch]` 逃逸；确定性检查**无条件执行**，无可跳过的配置开关。

**Rust 侧防护**：`read_count` 防巨额瞬时分配；`read_var_int` 拒绝非规范编码；路径穿越已防护（content-addressed 文件名 + `is_plain_filename` 拒绝 `..`/`/`/`\`）；符号链接用 `O_NOFOLLOW` + 常规文件校验；临时文件成功/失败路径均清理。

**RPC 协议层**：SDK 校验 `jsonrpc === "2.0"`、`id` 为安全整数且等于请求 id、`result` 键存在；服务端 `RpcServer.cs` 同样校验；u64 全程以字符串承载（不受 JSON.parse 精度影响）；4 类异常分层与 .NET/Rust/Python SDK 完全对应。

---

# 附录 A：修复记录（2026-10-21）

本附录记录本报告全部发现的修复状态。修复遵循两条原则：

1. **链上为最终裁决方** —— 跨语言编码分叉一律向链上布局对齐，而非相反；
2. **单一事实来源** —— 同一逻辑对象不再有两套实现/两套校验。

## A.1 修复状态总表

| # | 严重度 | 发现 | 状态 | 主要改动 |
|---|---|---|---|---|
| P0-1 | P0 | 提现叶哈希跨链布局分叉 | ✅ 已修复 | `MessageHasher` / Rust `withdrawal_hash` 统一到链上 Layout C；`WithdrawalRequest` 新增 `L1Asset`；L2 原生合约 `WithdrawalEmitted` 事件新增 `l1Asset`；黄金向量重算；新增逐字节跨链断言 + L1 资产绑定断言 |
| P0-2 | P0 | Gateway guest 公开输入 348 vs 352 | ✅ 已修复 | guest 改用 `PUBLIC_INPUTS_BYTES`（352）；`forced_inclusion_count` 经 sidecar + supplement 传递；sidecar magic 升 `NEO4GCS2`；新增**跨 crate 交叉测试**（以 `neo_execution_core` 权威实现为 oracle） |
| P0-3 | P0 | ZkVerifier 外部验证器 arity 错误 | ✅ 已修复 | `Contract.Call` 改为 4 个独立尾随参数，删除多余的 gas 参数 |
| P0-4 | P0 | `verifyZkProof` 硬编码 `false` | ✅ 已修复 | 改为路由到已注册的外部验证器；未注册时 fail-closed；锁定后只接受锁定的 VK |
| P0-5 | P0 | SDK `u64Wire` 精度截断 | ✅ 已修复 | 增加运行时 `bigint` 类型守卫；`getStateRootAt` 未知批次返回 `null`；新增 8 个 SDK 回归测试 |
| P1-1 | P1 | 治理锁死后 RollupHub 回滚不可达 | ✅ 已修复 | GovernanceController 新增 `RegisterGovernanceTarget` + `RelayGovernanceAction`；RollupHub 新增 `ExecuteGovernanceProposal`；GC 主动出站调用使 `CheckWitness(GC)` 成立 |
| P1-2 | P1 | SharedBridge 锁死后运维全失效 | ✅ 已修复 | SharedBridge 新增 `ExecuteGovernanceProposal`（setSettlementManager / registerMapping / setMappingActive）；`SetEmergencyManager` 明确保留 owner 直连（应急通道） |
| P1-3 | P1 | 强制包含「已消费」标志从不写入 | ✅ 已修复 | 批次消费路径写入 `[head, head+count)` 区间的 per-nonce 标志；`Consume` 不再拒绝 `nonce == 0` |
| P1-4 | P1 | `ReportCensorship` 无权限可暂停链 | ✅ 已修复 | 反审查证据无条件记录；暂停升级需特权；`SetEmergencyCouncil` 拒绝合约地址；`PauseChain` 的 council 检查排除合约身份 |
| P1-5 | P1 | Multisig quorum 不去重 | ✅ 已修复 | 按公钥去重计数，重复签名不再贡献第二票 |
| P1-6 | P1 | `host::verify()` 接受任意 VK | ✅ 已修复 | 强制 `vk == NEO_ZKVM_GUEST_VK_BYTES32`；恢复路径补文件属主/权限校验 |
| P1-7 | P1 | RPC `getl2batch` chainId 语义不一致 | ✅ 已修复 | `AddBatch` 拒绝异链批次；两方法语义统一 |
| P1-8 | P1 | 未知批次返回零哈希 | ✅ 已修复 | 新增 `TryGetStateRootAtBatch`；`getl2stateroot` 对未知批次返回 `null` |
| P2-1 | P2 | ProofType 判别值错位 | ✅ 已修复 | Rust `ProofType` 改为 `None/Multisig/Optimistic/Zk = 0/1/2/3`；三处硬编码 `3` 改用枚举常量 |
| P2-2 | P2 | 网关 Merkle 折叠语义分叉 | ✅ 已修复 | 提取 `ComputeGlobalMessageRootFromRoots`；`PassThroughAggregator` 改用提升语义 |
| P2-3 | P2 | proof system 0 无法注册 / MultisigVerifier 未部署 | ✅ 已修复 | **multisig 证明路由此前在链上完全不可用**（`VerifyMultisigProof` 按保留字节 0 路由，而 `RegisterProofVerifier` 拒绝字节 0 → `GetProofVerifier(0)` 恒为零地址 → 每份 multisig commitment 恒验证为 false，委员会背书的批次永远无法结算）。修复：multisig 验证器改用**独立槽位** `RegisterMultisigVerifier` / `GetMultisigVerifier`（字节 0 在 proof-system 键空间里保持「未设置」语义）；`MultisigVerifier` 已加入默认部署计划与产物脚本；部署后接线新增 `SetGovernanceController` + `RegisterMultisigVerifier` |
| P2-4 | P2 | 合约测试产物过期 + 测试依赖 gitignore 目录 | ✅ 已修复 | `UT_ZkVerifier_Vm` 与 `Sp1Groth16VerifierArtifact` 改读已提交的 `TestingArtifacts`；`ALL_CONTRACTS` 补 `MultisigVerifier`；6 个合约产物全部重新生成；GC 测试补 `engine.Fee`；过期的 `txHash` 夹具改为按合约算法派生 |
| P2-4b | P2 | 文档与代码事实不一致（9 个失败） | ✅ 已修复 | README / contracts README / launching-an-l2（中英双份）/ TECH_STACK.md 的数量对齐；模板目录测试与文档表格同步更新；新增 `AUDIT_REPORT_TEMPLATE.zh.md` |
| P2-5 | P2 | `*ViaProposal` 缺 per-proposalId 重放保护 | ✅ 已修复 | 与 P1-1 同批上线：新增 `PrefixConsumed{RevertBatch,SetChainActive,UpdateChain}` 与 SharedBridge 对应前缀 |
| P2-6 | P2 | envelope-only 关闭粒度不足 | ✅ 已修复 | 关闭任一 proof system 即写入全局锁；`IsEnvelopeOnlyAllowed` 感知全局锁；`SetEnvelopeOnlyAllowed` 在全局锁后拒绝 |
| P2-7 | P2 | Rust 侧强制包含无密码学校验 | ✅ 已修复 | `neo-execution-core` 新增 `validate_forced_inclusions`（leaf 位置 / 交易哈希 / 规范 Merkle 路径 / nonce 唯一），guest 与 host 同时强制 |
| P2-8 | P2 | PooledBatchSerializer 丢失全部校验 | ✅ 已修复 | 提取 `BatchSerializer.Validate` 作为唯一事实来源，pooled 编码器复用 |
| P2-9 | P2 | KeyedStateStore 依赖隐式排序 | ✅ 已修复 | 显式按无符号字节序排序；`IL2KeyValueStore` 接口写死排序契约 |
| P2-10 | P2 | prover 无资源护栏 / Gateway 无背压 | ✅ 已修复 | 新增 `resource_guardrails`（4 GiB 内存上限 + 批次 GAS 上限）；批次 fold 强制 `MAX_BATCH_GAS`；Gateway daemon 新增 `--max-queue-tasks` / `--max-queue-bytes` 背压 |
| P3-6 | P3 | SDK u64 交叉校验类型语义 | ✅ 已修复 | 改为数值比较（字符串形式），bigint 与 number 同值不再误报 |
| P3-7 | P3 | SDK 枚举无范围校验 | ✅ 已修复 | 新增 `requireEnumValue`；补 `DAMode.Local = 255` |
| P3-8 | P3 | `SequencerModel` 成员名与 C# 不一致 | ✅ 已修复 | 改为 `Decentralized`（与 C# 权威定义一致） |
| P3-9 | P3 | SDK 超时不覆盖 body 读取 | ✅ 已修复 | 计时器移至 body 读取完成后清除 |
| P3-1 | P3 | 阈值复核缺失 | ➖ 未改 | 审计已判定「未找到构造路径」——epoch 校验先行构成隐式顺序约定 |
| P3-2 | P3 | `MigrateLockedBalance` 死代码 | ➖ 未改 | 语义上是刻意的封存，删除属产品决策 |
| P3-3 | P3 | 热路径缓存跨批次复用 | ➖ 未改 | 下游有 post-state root 校验与全量比对兜底 |
| P3-4 | P3 | `SP1_VERSION` 无编译期一致性 | ➖ 未改 | `=6.2.1` 精确 pin + ELF 双重校验已覆盖实际风险 |
| P3-5 | P3 | bincode 配置不对称 | ➖ 未改 | 字节兼容 + 规范重编码校验已覆盖 |
| P3-10 | P3 | SDK 错误对象 `data` 字段丢失 | ✅ 已修复 | 保留 `data` 并新增 `L2RpcServerError.data`；非对象 error 不再被抹平 |

**统计：26 项已修复 · 1 项部分修复 · 8 项未改（其中 4 项经审计判定无实际后果，3 项为产品/架构决策，1 项为「未找到构造路径」）**

## A.2 新增的回归防护

修复过程中补齐了审计指出的**「测试用镜像当 oracle」**这一系统性缺口：

| 防护 | 位置 | 捕获的缺陷类型 |
|---|---|---|
| 跨语言编码门禁（64 项断言，PR 必跑） | `scripts/ci/verify_cross_language_encoding.py` | 公共输入/commitment/提现叶的布局分叉；`Contract.Call` arity 错误；合约使用 NeoVM 不支持的 API |
| 逐字节跨链断言 | `tests/Neo.L2.State.UnitTests/UT_MessageHasher.cs` | 提现叶布局漂移（不再依赖测试内的镜像缓冲） |
| L1 资产绑定断言 | 同上 | 提现叶未绑定 L1 支付资产 |
| 跨 crate 交叉测试 | `bridge/neo-zkvm-gateway-guest/src/lib.rs` | Gateway guest 公开输入重建分叉（以 `neo_execution_core` 为 oracle） |
| SDK 数值与枚举回归测试（8 个） | `sdk/typescript/src/index.test.ts` | u64 精度截断、枚举越界、error 字段丢失、超时不覆盖 body |
| Rust 强制包含校验 | `bridge/neo-execution-core/src/batch.rs` | 批次谎报强制包含数量（审查攻击） |

**门禁的负向测试已验证**：临时把 `ZkVerifier` 的 `Contract.Call` 改回缺陷形态（嵌套数组 + 尾随 gas）后，门禁精确报出该行；恢复后重新全绿。这证明门禁不是「空跑通过」。

## A.3 修复后的验证基线

| 项目 | 修复前 | 修复后 |
|---|---|---|
| `dotnet build Neo.L2.sln -c Release` | 0 warnings / 0 errors | **0 warnings / 0 errors** |
| .NET 测试（38 个套件，2,897 个测试） | 2,899 passed / **17 failed** | **2,883 passed / 0 failed / 14 skipped** |
| `NeoHub.Contracts.VmTests` | 330 / 8 failed | **338 / 0 failed** |
| `NeoHub.Sp1Groth16Verifier.UnitTests` | 2 / 9 failed | **11 / 0 failed** |
| `Neo.Hub.Deploy.UnitTests` | 107 / 4 failed | **111 / 0 failed** |
| `Neo.Stack.Cli.UnitTests` | 205 / 5 failed | **210 / 0 failed** |
| `bridge/neo-execution-core`（cargo） | 16 / 0 | **16 / 0** |
| `sdk/typescript`（vitest） | 23 passed / 3 skipped | **31 passed / 3 skipped** |
| 跨语言编码门禁 | 不存在 | **64 项断言全通过** |

## A.4 修复过程中发现的额外问题

修复暴露了审计报告未覆盖的问题，一并修复：

1. **合约代码误用 `ArgumentNullException.ThrowIfNull`** —— NeoVM 没有这个 API，合约在 pinned devpack 下**编译失败**。已全部改为 `ExecutionEngine.Assert` 形式，并加入 CI 门禁防止复发。
2. **`MultisigVerifier` 从未进入部署计划** —— 合约存在但 `ScaffoldPlan.Default()` 不含它，multisig 证明路由实际无人服务。已加入（6 步部署计划）。
3. **`UT_GovernanceController_Vm` 未设 `engine.Fee`** —— 合约体积增长后全部 29 个测试因 GAS 不足失败。已在 helper 统一设置。
4. **`UT_RollupHub_Vm` 的 `txHash` 夹具过期** —— 合约校验 `HashTransaction(txBytes)`，夹具仍用任意常量。已改为按合约算法派生。
5. **`TECH_STACK.md` 文档页数陈旧**（47 → 51）。
6. **`AUDIT_REPORT_TEMPLATE.md` 缺中文对应版** —— 补齐 `AUDIT_REPORT_TEMPLATE.zh.md`。

## A.5 仍然开放的事项

以下项**未在本轮修复**，需要在发布前决策：

| 项 | 原因 |
|---|---|
| **Cargo 13 个 RUSTSEC advisory** | 全部位于 `sp1-prover` / `sp1-core-executor` 的传递依赖中，且只影响未使用的网络 prover 路径；需随 sp1-sdk 升级一并清理 |
| **npm 4 个 advisory** | 全在 devDependencies（vitest / vite / source-map-js），不进入生产产物 |
| **P3-1 阈值复核** | 审计判定「未找到构造路径」；加固需改 GC 的提案语义 |
| **P3-2 / P3-3 / P3-4 / P3-5** | 或为产品决策，或审计已判定无实际后果 |
| **SP1 真实证明通道未在 PR 上运行** | 需要 120 分钟 + 专用 runner；当前仍为 `workflow_dispatch` / `schedule` 触发。跨语言编码门禁已作为 PR 必跑的轻量替代 |

## A.6 第二轮：剩余开放项的处置（2026-10-22）

### A.6.1 P2-3 完整修复：multisig 证明路径此前在链上完全不可用

审计原文只记「proof system 0 无法注册」。本轮**实际读代码**后发现后果比记录严重得多：

> `VerifyMultisigProof` 按保留字节 0 路由，而 `RegisterProofVerifier` 断言 `proofSystem > 0`。
> 两个半边永远无法相遇 —— `GetProofVerifier(0)` 恒返回零地址，分派分支永不进入，
> **每份 multisig commitment 恒验证为 `false`**。委员会背书的批次永远无法结算，
> 且**没有任何运维动作能修复**：注册接口恰好拒绝路由器唯一会查的那个字节。

**修法决策**：不给字节 0 开口子，而是**给 multisig 一个独立槽位**。
理由：`proofSystem` 的语义是「ZK 证明系统」（SP1=1、RISC Zero=2），multisig 是链下委员会背书，
**没有 VK、没有 proof system**。硬塞进同一键空间是类型混淆，且会让字节 0 同时表示
「未设置」与「multisig 后端」两件事，使 `Storage.Delete` 无法区分「删除未设置的项」与
「显式吊销」。

新增 `RegisterMultisigVerifier(UInt160, bool)` / `GetMultisigVerifier()`（槽位 `PrefixMultisigVerifier = 0x08`），
`VerifyMultisigProof` 改读该槽位；`RegisterProofVerifier` 继续拒绝字节 0。

**部署接线**：`ScaffoldPlan.PostDeployActions` 新增
`MultisigVerifier.SetGovernanceController(GC)` 与 `ZkVerifier.RegisterMultisigVerifier(MultisigVerifier, true)`。

**回归测试**：`tests/NeoHub.Contracts.VmTests/UT_ZkVerifier_MultisigRoute_Vm.cs`（10 个用例）。
测试部署**真实的** `MultisigVerifier`、在**真实的** GovernanceController 注册 sequencer、
用**真实的** ECDSA 密钥签名 —— 真实合约测试端到端证明整条链（分派 → 注册表检查 → 签名验证），
这是它的价值所在。

> **更正（2026-10-22 第四轮实测）**：本节初版把使用真实合约的理由写成「mock 路径
> （`FromHash(checkExistence: false)`）不注册 `ContractState`，`Contract.Call` 解析不到它」。
> **这条理由是错的。** 决定性实验：把 mock 注册进 multisig 专用槽位 → 路由成功调用 mock 并返回
> `true`（已固化为 `StubVerifierInDedicatedSlot_IsCallableThroughTheRoute`）。当时路由返回
> `false` 的真实原因是**槽位为空**（P2-3 缺陷本身），mock 解释是恰好与真因共存的未验证假设。
> 真实合约测试的正当理由是**端到端证明**，不是「mock 不可用」。stub verifier 是合法的
> pre-production 测试模式（第四轮修复的推荐用法即此）。

**负向测试已验证**：
- 注入「路由改回 proof-system 槽位」→ `MultisigCommitment_WithRegisteredVerifier_VerifiesThroughTheRealChain` 失败 ✅
- CI 门禁同时报出该回归 ✅

### A.6.2 两处被证伪的假设（已按实测修正）

**假设一：「`[Safe]` 上下文里用 `CallFlags.All` 跨合约调用会 fault」。**
负向测试证伪：把 `CallFlags.All` 注入回去，10 个用例依然全绿。
查 `ApplicationEngine.CallContractInternal` 得到实情：

```csharp
if (method.Safe)
    flags &= ~(CallFlags.WriteStates | CallFlags.AllowNotify);
```

VM 会为 **Safe 的被调方**自动屏蔽写标志，所以 `CallFlags.All` 与 `CallFlags.ReadOnly` 在这里**行为相同**。
本次仍统一改为 `ReadOnly`（被调方确实是 Safe 且只读，请求写标志本就无意义），
但**代码注释与报告都按实测结论改写**，不写成「修复了路由失效」。

**假设二：「ABI 方法名不匹配时，call fault 会被吞成 `false`」。**
这条也**是错的**，而且错得更关键 —— 它影响对整个「静默失效」类缺陷的严重性判断。
实测（把 `Sp1Groth16Verifier` 注册进 multisig 槽位，它没有 `verifyMultisigProof`）：

```
PROBE THREW TestException: Exception has been thrown by the target of an invocation.
```

`ApplicationEngine.CallContract` 在 `GetMethod` 返回 null 时直接：

```csharp
?? throw new InvalidOperationException($"Method \"{method}\" with {args.Count} parameter(s) doesn't exist in the contract {contractHash}.");
```

**即 ABI 名不匹配是响亮 FAULT，不是静默 false。**

那么 P2-3 的「静默失效」真实机制是什么？—— **是槽位为空**：

```csharp
var externalVerifier = GetMultisigVerifier();
if (externalVerifier.IsValid && !externalVerifier.IsZero)   // ← 空槽位在此直接短路
{
    return (bool)Contract.Call(...);
}
return false;                                               // ← 静默返回 false
```

未配置的 verifier 与「证明无效」返回**同一个值**。这不是正确性问题（fail-closed 是对的），
但是**可运维性问题**：链上一个批次被拒绝时，无法从结果区分「忘了注册验证器」和「证明是假的」。
`GetMultisigVerifier()` / `GetProofVerifier()` 已可用作诊断入口，部署后校验也已由
`ScaffoldPlan.PostDeployActions` 覆盖，因此本轮**不改 ABI 语义**，但把这一点写进运维须知。

**两条假设都归因错了，但根因（保留字节）经负向测试确认成立。**
这正是记忆里那条教训的又一次落地 —— 构造不出使两边行为不同的输入，就写「未发现构造路径」；
反过来，**构造出来了却归因错了，同样要更正**。

### A.6.3 新增 CI 门禁（70 项断言）

| 门禁 | 捕获的缺陷 |
|---|---|
| 跨合约调用目标必须在被调方 **manifest 的 ABI** 中存在（按生成的产物解析，不看源码声明名） | `Contract.Call` 目标不存在 / 大小写不符 —— 路由静默失效 |
| 保留字节规则：`RegisterProofVerifier` 必须继续拒绝字节 0；`VerifyMultisigProof` 必须读独立槽位、不得读 proof-system 槽位 | multisig 路由退化回不可达 |

两条门禁均已做负向验证。

**一个重要的方法论教训**：跨合约调用目标的检查**必须基于生成的 manifest**，不能用源码里的
PascalCase 方法名。`nccs` 会把方法名转成 camelCase（`VerifyZkProof` → `verifyZkProof`），
`ContractAbi.GetMethod` 用序数比较解析名称 —— 拿源码名去比对会把**正确代码**误判为错误。

### A.6.4 子模块跨仓库同步（已完成）

`external/neo`（r3e-network/neo fork）的 `WithdrawalEmitted` 事件新增 `l1Asset` 字段，
需要跨仓库提交。本轮已完成：

1. 远端 `r3e/neo-n4-core` 已前进 11 个提交（本地落后），其中 7 个碰了同一文件
2. 用 `git diff > patch` + `git checkout --` + `git apply --3way`（**不用 `git stash`** —— 本机 git 的引用事务清理有 bug），
   三方应用**无冲突**
3. 在新基线上重新核对了语义：`PrefixMappingByL2` → `mapping.Asset` 仍是 L1 资产 ✅
4. 编译 `external/neo/src/Neo/Neo.csproj` 通过
5. 提交 `0a362a2bc`（基于远端最新 `2f5a0b5a8`），推送至 `origin/feature/l2-withdrawal-l1-asset-event`
6. 主仓库 submodule 指针更新到 `0a362a2bc`

**上游 PR 入口**：`https://github.com/r3e-network/neo/pull/new/feature/l2-withdrawal-l1-asset-event`
（事件签名变更，合并后需同步通知事件消费方改用新的 arity）

### A.6.5 两翼独立调查的结论

**左翼（安全）**：
- **P3-1 阈值复核**：确认安全，但**理由与审计报告不同** —— 不是「epoch 检查先行」这种隐式顺序约定，
  而是两条**结构性**不变量：阈值写入与 epoch 严格耦合，且提案的 epoch 在创建时冻结。所有守卫是合取关系，
  顺序无关。已用**变异测试**（移除 epoch 守卫）证明该合取项是承重的。
- **quinn-proto 的 5 个 advisory**：审计报告称「在 bridge 二进制的依赖闭包里」—— **不准确**。
  `http3` feature 全仓**启用 0 次**，`cargo tree -i quinn-proto` 输出「nothing to print」，
  它**从未进入编译图**，只是锁文件里的残留。
- **rustls 家族**：审计报告称「仅通过 SP1 未使用的网络 prover 路径可达」—— **也不准确**。
  它们还经由 `sdk/rust`（neo-n4-sdk）与 `watchers/neo-bridge-watcher-*` 出现在**真实生产 TLS 出口**上。
  RUSTSEC-2026-0285 / 0349 属客户端侧 MITM 相关，**在生产中可利用**。
- **npm**：审计报告称 vite 修复「需 `--force` 且超出声明范围」—— **不准确**。vite 是精确 pin，
  必须改版本号，但同 major（`8.0.16` → `8.3.5`）**不需要 `--force`**；vitest `^4.1.6` → `^4.1.12` 在范围内。

**右翼（CI）**：
- 新增 `sp1-pr-gate` job：在 `pull_request` 与 `push` 上运行，**无需 Docker、无需真实证明**，
  冷启动约 1 分钟，覆盖 `neo-execution-core` 单元测试 + Gateway guest 跨 crate 交叉测试 + batch guest 执行契约。
  真实证明仍由 `sp1-release-gates`（schedule / dispatch）负责 —— 两个通道的职责边界已在 workflow 注释里写明。
- 40 处文档漂移已按代码事实校正（合约 6 个、部署 6 步、模板 11 个、测试项目 39 个）。

### A.6.6 依赖安全处置（2026-10-22 第六轮全部落地）

> 初版此表标记「离线 registry 无法确认发布状态 → 已标记而非断言」。第六轮网络恢复后全部处置完毕。

| advisory | 可达性 | 处置结果 |
|---|---|---|
| rustls / rustls-webpki / webpki-roots（客户端侧 MITM） | **生产可达** | ✅ **已升级**：`rustls 0.23.40→0.23.46`、`rustls-pki-types 1.14.1→1.15.1`、`rustls-webpki 0.103.13→0.103.16`、`webpki-roots 1.0.7→1.0.10`、`webpki-root-certs 1.0.8→1.0.10`（传递 `aws-lc-rs 1.18.2`）。三个目标版本在 crates.io 的存在性已实测确认（`cargo update --precise` 成功即证明）。**生产出口验证**：`neo-n4-sdk`（完整 TLS 栈）与 `neo-bridge-watcher-eth`（审计标记的 MITM 出口）均编译通过。advisory 从 `cargo audit` 消失 |
| quinn-proto 家族（5 项） | 不可达（`http3` 从未启用） | ✅ 顺手升级 `0.11.15→0.11.19`，5 个 advisory 全部消失 |
| ruzstd（RUSTSEC-2026-0344，需 ≥0.9.1） | 不可达 | ❌ **确认真上游阻塞**：`sp1-sdk 6.2.1 → backtrace 0.3.76（最新）→ object 0.37.3 → ruzstd ^0.8.1`，链条每环都无可更新项（`cargo update --dry-run` 全部 0 packages）。随 sp1-sdk 下一次升级自然解决 |
| npm：vite / vitest | dev-only | ✅ vite `8.0.16→8.3.5`、vitest `4.1.6→4.1.12`，`npm audit` **0 漏洞**，测试 31 通过不变。注意：npm 10.9.7 的 arborist 有 `#loadPeerSet` 崩溃 bug（`edgesOut` null），需 `--legacy-peer-deps` 安装 |

**最终 `cargo audit`：6 → 1 个漏洞**（唯一残留 ruzstd，不可达 + 上游阻塞，已记录）。

**Rust 侧回归验证**：`neo-execution-core` 47 测试 ✓ · `neo-zkvm-gateway-guest` 14 ✓ · `neo-zkvm-guest` 18 ✓ ·
`neo-n4-sdk` 与 `neo-bridge-watcher-eth` 编译 ✓。`neo-zkvm-host` / `neo-zkvm-gateway-host` 在 Windows
本机编译失败属**既有平台限制**（`sp1-jit 6.2.1` 的 Unix-only 共享内存，sp1-sdk 固定依赖，与本次升级无关），
其构建由 Linux CI 的 `sp1-pr-gate` 负责。

### A.6.6.1 事故记录：构建清理误删源码（已恢复）

清理陈旧 `obj`/`bin` 时，`find -type d -name bin` 与 PowerShell `-Include "obj","bin"` 会匹配
**任何**名为 `bin` 的目录 —— 包括 `src/bin/` **源码目录**。共误删 4 个项目的 9 个源文件
（`neo-zkvm-guest/src/bin/executor.rs`、`neo-zkvm-host/src/bin/prove_batch.rs`、
`neo-zkvm-gateway-host/src/bin/prove_gateway.rs`、`neo-bridge-watcher-eth/src/bin/` 下 6 个）。
`git status` 的 `D` 行暴露了事故；`git checkout --` 全部恢复，恢复后 guest 测试立即回到全绿。

**教训**：清理构建产物的安全写法是**只删项目根下的 `bin`/`obj`**（`-path "*/bin" -prune` 不够，
必须排除 `*/src/*`），或干脆用 `dotnet clean`。清理后必须跑一次 `git status` 核对没有 `D` 行。

### A.6.7 跨语言 parity 的覆盖缺口（补上）

审计指出的「用镜像当 oracle」不只是**提现叶**一处。核对既有的跨语言 parity 通道时发现：
`hash_public_inputs_with_forced`（**含 `forcedInclusionCount`** 的那条路径）此前
**完全未被任何跨语言测试覆盖** —— parity 测试用的是 14 参数的 `hash_public_inputs`
（它只是 `with_forced(..., 0)` 的包装），所以非零 count 的编码从未被两侧比对过。

而 P0-2 的分叉恰恰就是这个字段。**若当时有这条覆盖，那个 348 vs 352 的分叉在第一轮就会暴露。**

已补齐：

- `tests/Shared/CanonicalEncodingVectors.cs` 新增 `PublicInputsWithForcedInclusions()`
  （与原向量只差尾部的 u32 LE = 3）
- `tests/Shared/canonical_encoding_vectors.hex` 导出该向量（Rust 侧 `include_str!` 读取）
- `bridge/neo-execution-core/tests/canonical_encoding_parity.rs` 新增两个用例：
  - `hash_public_inputs_with_forced_binds_the_forced_inclusion_count` —— 用 .NET 导出的字节
    验证 Rust 的编码
  - `hash_public_inputs_with_forced_changes_the_digest_when_the_count_changes` —— count 必须在签名域内
- `tests/Neo.L2.IntegrationTests/UT_CanonicalEncodingParity.cs` 的导出一致性测试同步覆盖新字段

**方法论要点**：只有一个 count 为 0 的向量**无法**发现 count 编码的分叉 —— 因为「永远写 0」的错误实现
能精确复现它。非零向量是必需的。

另新增 `sdk/conformance/vectors/canonical-encoding-v1.json`（由 .NET 生产编码器生成的 4 组用例，
含 chainId=0 / batchNumber=ulong.MaxValue 的边界），并配套
`tests/Neo.L2.State.UnitTests/UT_CanonicalEncodingFixture.cs`：该测试会**重新生成** fixture
并与已提交副本逐字节比对，因此 fixture 不可能悄悄漂移成「与一个错误的实现一致」。

为了让 fixture 的字节真正来自生产编码器，`StateRootCalculator` 拆出了公开的
`EncodePublicInputs`（`HashPublicInputs` 复用它）—— 编码只有一份实现，
fixture 生成与哈希计算不可能各走各的。

## A.7 第三轮：把「镜像当 oracle」从个案修成结构

### A.7.1 提现叶：四份实现，零跨侧校验

审计点出的第一课是「两套各自自洽的测试共享同一被污染假设 → 跨语言分叉长期存活」。
第一轮修了 `forced_inclusion_count` 一处，但**提现叶**比它更严重：

| # | 实现 | 位置 |
|---|---|---|
| 1 | `ComputeWithdrawalLeafHash` | 链上 `NeoHub.SharedBridge`（权威） |
| 2 | `MessageHasher.EncodeWithdrawal` | 链下 .NET `Neo.L2.State` |
| 3 | `withdrawal_leaf_preimage` | Rust `neo_execution_core::native` |
| 4 | `LeafBytes()` | 合约 VM 测试内手搓 |

四份**全部**是 116+amountLen 字节、double-SHA256、都含 `l1Asset` —— 也就是说当前是对的。
但**四者之间没有任何一次比对**。审计明确记录这个叶曾分叉（链下 98B Layout A vs 链上 118B Layout C），
而它能存活数月，正是因为每份实现只对着自己验证。

更有害的一点：`VerifyWithdrawalLeafWithProof` **只折 Merkle 路径**，不重算叶哈希。
所以哪怕用它对账，也**查不出布局分叉** —— 只有 `FinalizeWithdrawalWithProof`
里的 `ValidateWithdrawalLeafBinding` 才会重算。任何只写「verify」路径的回归测试都是假的保险。

**建立的锚**：一份由**第三种语言（Python）**产出的权威向量，
放在 `tests/Shared/canonical_encoding_vectors.hex`，四侧各自断言：

| 侧 | 测试 | 断言内容 |
|---|---|---|
| 链上 | `UT_SharedBridge_Vm.DeployedBridge_ReproducesTheThirdImplementationLeafVector` | 走 `FinalizeWithdrawalWithProof` → 合约**重算**叶哈希 → 必须匹配向量，且 escrow 恰好清零 |
| 链下 .NET | `UT_CanonicalEncodingParity.OffChainWithdrawalEncoder_ProducesTheSharedLeafVector` | `EncodeWithdrawal` 字节级等于向量；`HashWithdrawal` 等于向量摘要；`DecodeWithdrawal` 往返 |
| Rust | `canonical_encoding_parity.withdrawal_leaf_preimage_matches_the_dotnet_export` | `withdrawal_leaf_preimage` 字节级等于向量 |
| 导出 | `SharedHexExport_MatchesTheVectors` | hex 文件与 C# 权威向量逐字节相等 |

**负向验证已做**：把链上布局改回历史分叉（删掉 `l1Asset`），
合约返回 `ABORTMSG: leaf hash mismatch with supplied fields`，测试失败 ✅。
恢复后通过 ✅。

为了让 Rust 侧可比，`withdrawal_leaf_preimage` 从私有 `withdrawal_hash` 中提取为 `pub`
（`withdrawal_hash` 改为调用它），与第一轮给 `StateRootCalculator` 提取
`EncodePublicInputs` 是同一手法：**编码只有一份实现，测试与被测代码不可能各走各的**。

### A.7.2 覆盖矩阵门禁：让缺口无法再出现

单点修补挡不住下一个编码面。新增门禁 `gate_cross_language_surface_coverage`：
在 CI 脚本里用一张**声明式表格**列出每个跨语言面与它必须携带的 fixture 键，双向检查：

- 声明了面但 fixture 缺键 → 失败
- fixture 有键但没有任何面声明 → 失败（防止表格腐烂成装饰）

**它立刻抓出一个真实缺口**：`chain_config`（91 字节，`RollupHub.registerChain` 消费）
此前**没有任何跨语言向量** —— Rust 侧只有自洽的 encode/parse 往返测试
（`fuzz_chain_config_rejects_non_canonical_booleans` 用的是自己的 `encode_chain_config`，
同源自证）。已补：

- `chain_config` 导出到共享 hex（与 `CanonicalEncodingVectors.ChainConfig()` 逐字节钉住）
- Rust 新增 `chain_config_encoding_matches_the_dotnet_export`：从向量按**命名字段**重建
  `L2ChainConfig` → `encode_chain_config` 必须复现这 91 字节 → `parse_chain_config` 必须还原

门禁断言数：70 → **99**。

### A.7.3 传递性锚：Gateway 的 348 分叉链条已闭合

Gateway guest 的 348 字节预像早前已改为 352，且它的测试已经**用权威实现当 oracle**
（`public_input_reconstruction_matches_authoritative_execution_core`，不是自洽镜像）。
但这个 oracle 本身此前只被 Rust 自证。本轮补的 .NET parity 让它闭合为一条完整链：

```
Gateway guest ──(cargo test，权威 oracle)──> neo_execution_core
                                                    │
                          (新) fallback parity ─────┤
                                                    ▼
                                             .NET EncodePublicInputs
                                                    │
                          (新) 合约向量测试 ────────┤
                                                    ▼
                                     链上 ComputeWithdrawalLeafHash / 承诺布局
```

### A.7.4 更正：两条因果断言都是错的

见 A.6.2。要点重述：**ABI 名不匹配会 FAULT，不会静默 false**；
P2-3 的静默失效机制是**槽位为空时的短路 `return false`**。
这不是正确性问题（fail-closed 正确），而是**可运维性**问题 —— 链上批次被拒时
无法从返回值区分「未注册验证器」与「证明无效」。
诊断入口：`GetProofVerifier(ps)` / `GetMultisigVerifier()`；部署后完整性校验已由
`ScaffoldPlan.PostDeployActions` 覆盖。

### A.7.5 环境陷阱（新增，已实测）

**MSB4166 会静默失败。** 症状：

- `MSBUILD : error MSB4166: Child node "N" exited prematurely`，或
  `Stack overflow at Interop+Kernel32.DeleteFilePrivate`（`Copy` 任务的并行拷贝）
- **最危险的表现**：`dotnet build` 打印 `0 Error(s)` 但**产物时间戳没变**。
  此时 `dotnet test --no-build` 跑的是**旧 dll**，会得出完全错误的结论
  —— 本轮因此误判两次（一次把 `read.Add` 缺失误认为代码 bug，一次把分叉注入误认为测试有效）。

**可靠做法**（三步都要做）：

1. 清 `obj`+`bin`（PowerShell 递归最快；Bash 的 `find -exec rm` 会被 SIGTERM 打断，需分批）
2. `dotnet build -p:BuildInParallel=false`
3. **核对 dll 时间戳**再跑测试

**附带的坑**：清 `external/neo-devpack-dotnet` 的 `bin/` 会删掉 `nccs` 编译器，
之后 `regenerate-testing-artifacts.sh` 报 `nccs not found`。重建：
`dotnet build external/neo-devpack-dotnet/src/Neo.Compiler.CSharp/Neo.Compiler.CSharp.csproj -c Release -p:NuGetAudit=false`

**另一条**：`Assert.AreEqual` 对 `byte[]` 比的是**引用**，内容相同的两个数组也会失败
（本轮提现叶测试踩到）。要比内容用 `CollectionAssert.AreEqual` 或比 hex。

## A.8 第四轮：envelope-only 的保留字节缺口（高危，已修复）

### A.8.1 发现：约束矩阵不完整 → multisig 路由 fail-open

P2-3 修复确立了「proof system 字节 0 = 未设置（保留）」的语义，但只覆盖了三个控制面。
逐一核对该字节在**全部**控制面的处理时发现缺口：

| 控制面 | 拒绝字节 0？ |
|---|---|
| `RegisterProofVerifier` | ✅ `proofSystem > 0` |
| `RegisterVerificationKey` | ✅ `proofSystem > 0` |
| `LockProofSystemConfiguration` | ✅（依赖 VK 注册，间接拒绝） |
| **`SetEnvelopeOnlyAllowed`** | ❌ **不拒绝** |

而 multisig 路由的 fail-open 检查读的**恰好是这个字节**：

```csharp
if (IsEnvelopeOnlyAllowed(MultisigProofSystem))  // = IsEnvelopeOnlyAllowed(0)
{
    return true;   // ← 无条件通过，跳过全部签名/quorum 验证
}
```

**实证（探针测试）**：`SetEnvelopeOnlyAllowed(0, true)` 被接受后，
一个**零签名**的垃圾 multisig commitment 通过 `VerifyProof` 返回 `true`。

完整的攻击链：
1. owner 调 `SetEnvelopeOnlyAllowed(0, true)`（保留字节不被拒绝——缺口所在）
2. 任何人构造 proofType=1 的任意 commitment → 无条件通过 multisig 路由
3. **关闭路径畸形**：字节 0 无法注册 VK → `LockProofSystemConfiguration(0, ...)` 永远不可能成功
   → 该 proof system 没有「lock configuration 顺带关 envelope-only」的纵深关闭，
   只剩 `DisableEnvelopeOnlyPermanently` 的**全局不可逆锁**

严重度评估：**高**（前提是 owner 启用，但这是与 SP1 envelope-only 同级的 owner 误操作面，
而 SP1 有 configuration-lock 双重关闭，字节 0 没有；且绕过的是全部签名验证）。

### A.8.2 修复（三层防御）

1. **写入侧**：`SetEnvelopeOnlyAllowed` 增加 `Assert(proofSystem > 0)`——与其它控制面对齐。
   保留字节从此**无法携带** envelope-only 标志。
2. **路由侧**：multisig 路由的 envelope-only 分支**整体删除**。理由：
   - 该分支读取的字节永远不可能合法置位，分支成为死代码
   - 对**已有部署的遗留状态**（若曾在修复前启用过）也立即 fail-closed
   - pre-production 测 multisig 成功路径的正确姿势是部署一个 stub verifier
     （`verifyMultisigProof` 恒 true 的合约）注册进专用槽位——这正是专用槽位的用途，
     不需要合约级 bypass
   - 顺带删除了失去唯一使用点的 `MultisigProofSystem` 常量
3. **门禁**：`gate_reserved_proof_system_byte` 从「检查一个方法」扩展为
   **遍历全部三个写入控制面** + 「multisig 路由不得出现 `IsEnvelopeOnlyAllowed`」。
   门禁断言：99 → **104**。

**ZK 路由的 envelope-only 分支保留不动**（有意决策）：它读的 proofSystem 是 1/2
（有 VK 注册与 configuration-lock 的完整生命周期），vk 绑定检查在 fail-open 之前执行，
且部署流程 `PostDeployActions` 的 `DisableEnvelopeOnlyPermanently(Sp1)` 全局锁在生产必经。

### A.8.3 回归测试与负向验证

新增两个用例（`UT_ZkVerifier_MultisigRoute_Vm`）：

- `SetEnvelopeOnlyAllowed_RejectsTheReservedByte`：字节 0 的 true/false 都必须 fault；
  合法 proof system（1）不受影响
- `MultisigRoute_HasNoEnvelopeOnlyBypass_AnyGarbageCommitmentFails`：
  把**所有能设置的** envelope-only 标志全部打开，零签名 commitment 仍必须 false

**负向验证（注入回退）**：
- 回退合约修复 → `SetEnvelopeOnlyAllowed_RejectsTheReservedByte` 失败 ✅
- 回退合约修复 → CI 门禁同时报出两条（写入侧缺口 + 路由侧分支）✅
- 恢复修复 → 12/12 通过 ✅

合约 VM 测试全套：352 → **354 通过 / 0 失败**（无旧测试依赖被删除的行为）。

### A.8.4 方法论

这一轮的发现方式是**横向核对同一个不变量在全部触点的实现**：P2-3 修复了「路由读保留字节」
这一个点，但「字节 0 = 未设置」这个语义要在**每一个**接受 proofSystem 参数的入口成立。
第一轮修复后我检查了路由读侧，这一轮检查了全部写入侧——缺口就在上次没查的那个方法里。

「一个不变量，N 个触点，逐一核对」比「一个 bug，修一处」贵，但这是把同类缺陷一次清空的
唯一方法。门禁的作用就是把这个核对自动化，让下一个新增控制面默认进入矩阵。

## A.9 第五轮：自我审计——核对审计自身的断言

### A.9.1 第三条被证伪的断言：「mock 不可被 Contract.Call 解析」

审计的价值取决于其结论的可信度。本轮把镜头掉转，**核对审计自己前几轮写下的因果断言**，
又发现一条错的：

> A.6.1 初版：「mock 路径（`FromHash(checkExistence: false)`）不注册 `ContractState`，
> `Contract.Call` 根本解析不到它，路由无论如何都会返回 false，缺陷完全不可见。」

**决定性实验**：把一个 `verifyMultisigProof → true` 的 stub 用 `FromHash(checkExistence: false)`
注册进 multisig 专用槽位，构造零签名 commitment，调 `VerifyProof`：

```
PROBE VerifyProof=True
```

**断言被证伪。** mock 完全可以被 `Contract.Call` 解析和调用。当时路由返回 `false` 的真实原因
是**槽位为空**（P2-3 缺陷本身）——mock 解释是一个恰好与真因共存的未验证假设。旁证本来就有：
`UT_SharedBridge_Vm` 一直在用同一模式让 RollupHub 的 `Contract.Call(verifyProof)` 正常工作。

**更正**：A.6.1、测试文件头注释、MEMORY.md 三处已改写。真实合约测试的正当理由是
**端到端证明**（分派 → 注册表检查 → 签名验证每一环都真），不是「mock 不可用」。
实验本身固化为 `StubVerifierInDedicatedSlot_IsCallableThroughTheRoute`——它同时钉住了
第四轮修复推荐的 pre-production stub 模式确实可用。

**Moq 附带发现**：被 mock 的类与方法必须 **public**（嵌套 private 类报
"not accessible to the proxy generator"）。

### A.9.2 submit / finalize 路径对称性核对（无缺陷）

同一不变量的多触点核对继续：`SubmitBatch` 与 `SubmitAndFinalizeBatch` 共享
`SubmitBatchCore`（全部校验：头长度、序列、pre-state、安全级兼容、public-input 哈希、
证明验证），差异仅在后续状态写入——**校验统一，无分叉**。`FinalizeBatchInternal`
（被 `SubmitAndFinalizeBatch` 同事务调用）与公开 `FinalizeBatch`（owner 门 + pending 状态
检查后调用）的关系同样正确：前者不需要状态检查因为批次从未进入 pending 态。

### A.9.3 四轮累计的断言修正清单

| # | 原断言 | 实测结论 | 轮次 |
|---|---|---|---|
| 1 | `[Safe]` 上下文 `CallFlags.All` 会 fault | VM 对 Safe 被调方自动屏蔽写标志，行为相同 | 第二轮 |
| 2 | ABI 名不匹配 fault 被吞成 false | 直接 `throw`；静默 false 只发生在槽位为空时 | 第三轮 |
| 3 | `FromHash(checkExistence: false)` 的 mock 不可被 `Contract.Call` 解析 | 完全可解析可调用 | 第五轮 |

三条全部来自「归因于未经证实的机制」而非「观察到错误结果」。**观察到的缺陷（保留字节、
槽位为空）都是真的；错的是对机制的叙述。** 这就是为什么负向测试（注入回退看测试是否失败）
必须成为标准动作——它同时验证「缺陷是真的」和「归因是对的」。

## A.10 第七轮：WSL 本地复刻 CI 车道（关闭工具链差距）

### A.10.1 此前的验证盲区

前几轮的全部 Rust 验证都在 **Windows + Rust 1.95.0** 下运行，而 CI 的 `sp1-pr-gate` 用
**Linux + Rust 1.88.0**。"本机测过"与"CI 会过"之间一直隔着（OS × 工具链版本）两个维度的差距。

WSL（Ubuntu, kernel 6.6）可用后，装上与 CI 一致的 **Rust 1.88.0**，逐条复刻 PR 车道命令：

| CI lane | 命令 | 本地结果 |
|---|---|---|
| 1 | `cargo test --locked -p neo-execution-core` | **47/47 ✓**（16 lib + 10 batch_core + 13 parity + 8 witness + 0 doc） |
| 2 | `cargo test --locked -p neo-zkvm-gateway-guest` | **14/14 ✓** |
| 3 | `bridge/neo-zkvm-guest` host 模式 `build + test --locked` | **18/18 ✓** |

测试数与 Windows/1.95.0 完全一致 —— **本轮建立的全部 Rust 侧资产（withdrawal-leaf 四侧锚、
chain-config 跨语言校验、forced-count parity）在 CI 的精确配置下通过**。
`--locked` 同时证明 Cargo.lock 与 manifest 无漂移（本轮升级的 54 insertions 是有意变更）。

### A.10.2 sp1-jit 的 Windows 阻断在 Linux 消失

第六轮遗留的「`neo-zkvm-host` / `gateway-host` Windows 编不过」在 WSL 下推进了一步：
`sp1-jit v6.2.1` **在 Linux 编译通过**（E0432 的 `std::os::fd` / `libc::shm_open` 报错是
Unix-only API 在 Windows 上的必然失败，代码本身没问题）。

剩余阻断是 host crate 的 build script：它要求 Docker 跑可复现的 `cargo prove build` 产出
guest ELF（SHA-256 + VK 双重 pin 校验），且 `target/elf-compilation/docker/...` 里**没有缓存
ELF**（只有 lib 产物）；`NEO_ZKVM_ALLOW_CACHED_ELF=1` 也绕不过"ELF 不存在"的 panic。
Docker Desktop 已安装但未运行——启动它是 GUI 操作，留给用户决策。CI 侧对应事实：
PR 车道**本来就不构建** host crate（正因需要 Docker），完整构建由 nightly/manual 的
release gate（ubuntu-latest 自带 Docker）负责。

### A.10.3 WSL 操作要点（复用备忘）

- 工具链：`curl https://sh.rustup.rs | sh -s -- -y --default-toolchain 1.88.0 --profile minimal`
- **必须设 `CARGO_TARGET_DIR` 指向 WSL 原生路径**（如 `~/neo-n4-target`）：
  既避免与 Windows 共用 `/mnt/d/.../target` 混杂（虽然实测 cargo 用哈希文件名可共存、
  Windows 侧复跑不回归），又快得多（ext4 vs DrvFs）。
- `wsl.exe` 输出是 UTF-16，管道处理需 `tr -d '\0'`；后台任务的 Duration 汇报不可信，
  以产物为准（`du -sh target` 802MB / 495 个产物证明编译真实发生）。

## A.11 第八轮：external-bridge 消息 —— 第六个跨语言面（三份镜像零锚）

### A.11.1 发现

把「一个不变量，N 个触点」的核对推进到此前七轮未碰过的 **watchers**（连外部链的生产组件）。
审计 `neo-bridge-watcher-eth`（6,698 行）时发现其签名域的 canonical 编码有**三份独立实现**：

| # | 实现 | 角色 |
|---|---|---|
| 1 | `canonical_message_bytes`（Rust watcher `messaging.rs`） | **签名侧**：watcher 对这些字节做 secp256k1 背书 |
| 2 | `ExternalMessageHasher.EncodeCanonical`（C# `Neo.L2.Messaging`） | 消费/部署侧（注释声称被 `NeoHub.ExternalBridgeEscrow` 消费） |
| 3 | `UT_External_RealCrypto.CanonicalMessageBytes`（测试内手搓） | 测试镜像 |

既有的 `parity.rs` 只是把**期望值硬编码贴在两侧**（Rust 一份、C# 一份）——审计早期批评过的
「paste the same expected digest into both languages」模式，无法发现两侧同错。
且 C# 注释引用的消费方合约 **`NeoHub.ExternalBridgeEscrow` 尚不存在**（contracts/ 仅 6 个项目）——文档漂移，
意味着合约落地时没有字节级基准可对。

这是 watcher 背书链的核心安全属性：Rust 签的字节 = C#/链上将来验的字节。任何字段宽度/顺序分叉
都会让背书无法验证（或更糟：验证了错误的域）。

### A.11.2 建立的锚（第六个跨语言面）

- 第三实现（Python）产出 128B 向量（102B 固定前缀 + 26B asset-transfer payload）+ double-SHA256
- `tests/Shared/canonical_encoding_vectors.hex` 导出 9 个 `xmsg_*` 键
- `CROSS_LANGUAGE_SURFACES` 声明 `"external-message"` 面；门禁断言 **104 → 113**
- Rust `parity.rs` 新增 `canonical_bytes_match_the_shared_third_implementation_vector`
  （`include_str!` 读共享 hex，重建消息断言逐字节相等 + hash 相等 + payload 编码相等）
- C# `CanonicalEncodingVectors.XMsg*` 定义 + `SharedHexExport` 读取全部 9 键 +
  `ExternalMessageEncoder_ProducesTheSharedVector`（生产编码器 == 向量 + hash + `DecodeCanonical` 往返）

### A.11.3 负向验证（锚的捕获能力两度被证明）

1. **自然证据**：建立过程中手写 hex 的笔误（sender 19B 而非 20B、asset 19B、sourceTxRef 33B）
   在两侧对账测试**第一跑就被抓出**（`length differs`）——写入侧笔误正是一种真实分叉。
2. **显式注入**：把 Rust nonce 写入从 8 字节改成 4 字节（一个最 plausible 的「u32 nonce 重构」）→
   **3 个测试失败**（共享向量锚 + 两个旧 pinned 测试）；恢复 → 8/8 全绿。
   旧 pinned 测试抓这个方向有效，但它们**无法发现两侧同错**——那正是共享向量锚的存在意义。

### A.11.4 顺带验证的 watcher 断言

- **stub-signer 启动门禁（实测通过）**：`run()` 注释声称「daemon refuses to start without
  `--allow-stub-signer`」。实际运行二进制：给出合法 config 但不给 flag →
  `fatal: this binary ships with a STUB signer that does NOT submit real Neo transactions — refusing to start`。
- **签名域安全属性（代码审计通过）**：canonical 域绑定 `external_chain_id`（0xE0 命名空间硬校验）+
  `neo_chain_id` + `nonce` + `direction` + 双方地址 + `deadline` + `source_tx_ref` + `message_type` +
  `payload_len` —— 跨链重放的绑定齐全；未支持的 `AssetAndCall` 类型 **fail-closed**（拒绝而非猜测编码）。
- 游标语义（同块多事件的崩溃恢复）与 (chain, nonce) 去重设计正确，见 `core.rs` 注释。

### A.11.5 Docker 路径诊断结论（host crate 完整构建）

按用户指示推进：Docker Desktop 可通过 `Start-Process` 拉起前端进程（`Docker Desktop` + 2×
`com.docker.backend` 存活），但 **Linux engine 从未启动**（`docker-desktop` WSL 发行版保持 Stopped、
`vm/init.log` 无新内容、npipe 不存在）——Docker Desktop 停在需要 **GUI 交互**的状态
（接受条款/首次配置），命令行侧无法越过。已留待用户在 GUI 里点一次；之后 host crate 完整构建
（`cargo prove build --docker`，guest ELF SHA+VK 双 pin）即可本机跑通。
9 月日志里的 `sp1-cargo-registry/git` 卷绑定警告属环境残留（不在本仓库 build 支持代码内），
绑定失败只退回无缓存构建，不影响正确性。

### A.11.6 环境教训（增量构建的静默缓存）

向量修正后 `dotnet build` 报 0 error、dll 时间戳也更新了，但测试仍拿到**旧的编译单元**
（`CanonicalEncodingVectors.cs` 经 `tests/Directory.Build.props` 以 `<Compile Include>` 编进测试 dll，
其增量编译被缓存跳过）。`rm -rf obj bin` 彻底重建后 14/14 通过。
这是 MSB4166 静默失败的**变体**：不只 dll 时间戳会骗人，**dll 内的编译单元也会**。
修正后的可靠流程不变：清 obj+bin → `-p:BuildInParallel=false` → 核对时间戳。

## A.12 第九轮：Gateway 聚合根的 frontier 折叠——第七个跨语言面

### A.12.1 审计对象与发现

`PublishGatewayGlobalRoot`（一个聚合证明终结多条链的最高权限面）主体设计良好：
根在链上从存储记录**重算**（不信任调用者）、构成元组严格排序防重复、
`GatewayFinalizedThrough` 水位线防跨 epoch 重放、epoch 幂等返回 false、证明经
`verifyZkProof` 强制验证、pass-through 后端被拒。

但发现一个**未锚定的跨语言面**：frontier 折叠。合约对**同一组叶**用两种奇数叶行为折叠：

- 构成承诺根：`duplicateOdd: true`（Bitcoin 式复制，增量 frontier 实现）
- 全局消息根：`duplicateOdd: false`（提升，增量 frontier 实现）

Rust guest 侧有 `merkle_root_duplicate_odd` / `merkle_root_promote_odd`（层级折叠实现）。
**两种算法风格完全不同，等价性从未被钉住**。更关键的测试盲区：既有 happy-path 测试只用
**1 个构成元组**——此时两种折叠都平凡地返回叶本身，不对称行为**完全未被测试**。

### A.12.2 建立的锚（第七个面 `gateway-frontier`）

- 第三实现（Python）产出 **3 叶**向量（不对称显现的最小奇数计数）+ 两个不同的根
- 共享 hex 导出 6 个 `gateway_*` 键；门禁断言 **113 → 119**
- **Rust guest**：`gateway_frontier_folds_match_the_shared_third_implementation_vector`
  （`include_str!` 读共享 fixture，`#![no_std]` 下用 `&'static str`）钉住两个真实折叠函数
- **C# 侧**三层：
  1. `CanonicalEncodingVectors.Gateway*` 定义 + `SharedHexExport` 读取全部 6 键
  2. `GatewayFrontierMirrorFold_MatchesTheSharedVector`：测试本地层级折叠镜像锚到向量
     （含「两根必须不同」的自检控制）
  3. `PublishGatewayGlobalRoot_ThreeConstituents_FoldsAsymmetrically`：**端到端**——
     3 条链各终结批次 1，镜像计算两根，部署的合约必须接受；**两个负向控制**（把 dup 根
     当 globalRoot、把 promote 根当 commitmentsRoot 都必须 fault）证明合约真实现了
     不对称折叠而非碰巧与 oracle 一致

锚链：`mirror ← Python 向量`（测试 2）、`合约 ← mirror`（端到端）、`guest ← 向量`（Rust 测试）——每环被强制。

### A.12.3 负向验证

- **等价性对拍（Python 转译）**：C# frontier 与 Rust 层级折叠在计数 1..17、双模式下全部一致——实现当前正确
- **guest 注入**：交换 `merkle_root_duplicate_odd` 的语义（一个 plausible 的「统一折叠」重构）→ fixture 测试 **FAILED**；恢复 → 15/15 全绿
- **合约侧**：端到端测试的两个负向控制内建（交换根 → `TestException`）

### A.12.4 顺带核实

- 前几轮记忆中的两条「文档漂移」（README 5 vs 6 合约、launching-an-l2 4 vs 11 模板）经核对**已不成立**——contracts/README 列出全部 6 个合约，CLI 模板恰为文档所述 4 个。记忆条目过时，已更正
- sol/tron watcher 是 eth crate 的薄封装（`neo-bridge-watcher-eth = { path = ... }`），**无第四/五份 canonical_message 镜像**——架构正确

### A.12.5 方法论补注

本轮的模式识别线索是「**既有测试的最小化选择**」：happy-path 测试用 1 个构成元组是写测试时最省事的选择，但它恰好落在两种折叠行为重合的唯一计数上。审计时对每个「n=1 的测试」都应问一句：**这个 n 是被测语义的最小代表，还是恰好掩盖了分支差异的退化输入？**

## A.13 第十轮：MultisigVerifier——quorum 语义的部署缺口与 n=1 盲区

### A.13.1 审计结论：验证器逻辑正确，但存在两个部署/测试缺口

通读 `MultisigVerifierContract`（239 行）。核心验证逻辑健全：

- quorum=0 不可达（`_deploy` 初始化 `DefaultMinQuorum=1` + `GetMinQuorum` 回退 + `SetMinQuorum` 断言 >0，三重保险）
- 去重防 1-of-N（`seenSigners` 只记有效签名者，重复条目在昂贵的注册表查询与验签前跳过）
- payload 解析全部先检查长度/边界（`expectedSize` 精确匹配、`signerCount` 上限 256 与 .NET 生产侧 `MultisigProofPayload.MaxSigners=256` 一致）
- 曲线一致：链上 `secp256r1SHA256` ↔ .NET 生产侧 `ECPoint.DecodePoint(ECCurve.Secp256r1)`（与 watcher 的 secp256k1 是**不同链路**——watcher 签的域目标是未落地的 ExternalBridgeEscrow/MpcCommitteeVerifier，非本合约）

### A.13.2 缺口 1（部署）：脚手架从不调用 `SetMinQuorum`（已修复）

`ScaffoldPlan.PostDeployActions` 的 multisig 路由只发两条 hint
（`SetGovernanceController` + `RegisterMultisigVerifier`），quorum 保持部署默认的
`DefaultMinQuorum = 1` —— **部署出的网络以 1-of-N 委员会运行**：任何单个注册 sequencer
的签名即可定稿 committee-attested 批次。M-of-N 路由的全部意义被一次遗忘的调用消解。

**修复**：按 `GOVERNANCE_THRESHOLD_REPLACE_ME` 的占位符惯例补第三条 hint
`MultisigVerifier.SetMinQuorum(<COMMITTEE_MULTISIG_QUORUM>)`，注释写明
「不设置则单 sequencer 定稿批次」。部署者现在必须显式做这个决策。
`UT_DeployPlanner` 钉住测试同步更新（8 → 9 条 hint），111/111 通过。

### A.13.3 缺口 2（测试）：quorum 计数与去重在 VM 层零覆盖（已补齐）

既有 12 个路由测试**全部使用单签名者 payload**——`validSignatures >= minQuorum`
只在退化点被测试（计数与布尔不可区分），合约注释里郑重声明的去重防 1-of-N 属性
**从未被执行过**。这是 A.12.5「n=1 盲区」的又一实例（连续第三轮抓到同一模式）。

新增 4 个 VM 测试（真实合约 + 真实 GovernanceController + 真实 secp256r1 签名）：

| 测试 | 钉住的语义 |
|---|---|
| `TwoDistinctSigners_MeetQuorumOfTwo` | quorum=2 时两个不同签名者通过（计数从上方） |
| `RepeatedSignerCannotStackVotes` | **去重**：同一 (公钥，签名) 重复两次 ≠ 两票（1-of-N 防降级） |
| `OneValidOfQuorumTwo_VerifiesFalse` | quorum 边界从下方 |
| `MixedEntriesReachQuorum` | 无效签名（对错误消息的真实签名）不计数、不中断循环 |

payload 构造器改为 params 元组形式（旧单签名调用点兼容）。

**负向验证**：注入「移除去重检查」→ `RepeatedSignerCannotStackVotes` **失败**（正确捕获
1-of-N 降级）；恢复 → 17/17 全绿。合约全套 **361/361**（357 + 4）。

### A.13.4 三轮 n=1 盲区的规律

| 轮 | 盲区 | 被掩盖的语义 |
|---|---|---|
| 8 | external-message 只测 count=0 的 payload | 无法发现空缺字段布局分叉 |
| 9 | Gateway frontier 只测 1 个构成元组 | 双折叠不对称 |
| 10 | multisig 只测 1 个签名者 | quorum 计数与去重 |

三个都是「最省事的输入恰好落在语义分支重合的退化点」。**结论已足够强，应固化为审计清单项**：
对任何带计数/列表/多态参数的测试，逐一核对每个枚举值/边界计数至少被一个测试用例覆盖。

## A.14 第十一轮：GovernanceController——治理中继的键截断缺陷（已修复）

### A.14.1 审计范围

`GovernanceControllerContract`（1,383 行，Pillar 4：council 多签、timelock、紧急暂停、sequencer 质押）。
整体质量很高：提案生命周期绑 council epoch（轮换自动失效未决提案）、payload 字节级绑定
（`AssertProposalBinding` + "neo4-gov:" 动作标签防跨方法重用）、per-proposalId consumed 防重放、
veto 永久、admission 只可即时收紧（放宽必须走 council + timelock）、`RotateCouncil` 绑定完整
新 council + threshold + epoch。

### A.14.2 发现：治理 target 注册表的键截断（已修复）

`GovernanceTargetKey(uint index)` 与 `RelayConsumedKey(uint targetIndex, ...)` 都把 index
**截断成 1 字节**（`k[1] = (byte)index`），而 `RegisterGovernanceTarget` **无上限**：

- 注册第 257 个 distinct target 时 `GovernanceTargetKey(256) == GovernanceTargetKey(0)`
  → **静默覆盖 target 0 的地址**，此后 `RelayGovernanceAction(0, ...)` 在错误的合约上执行
- `RelayConsumedKey(256, p) == RelayConsumedKey(0, p)` → 两个 target 共享 consumed 标志

严重度评估：**中**。需要 256 次 owner 门控的注册才触达（正常部署只有个位数 target），
但后果是治理中继在错误合约上执行——且是**静默**的。修复成本一行。

**修复**：`MaxGovernanceTargets = 256` 上限 + `Assert(count < MaxGovernanceTargets)`。
断言放在幂等检查**之后**——第一版放在前面被自己的回归测试抓出破坏了「满员时重注册既有
target 仍是 no-op」的幂等语义（测试驱动的设计纠偏）。

### A.14.3 回归测试与负向验证

`RegisterGovernanceTarget_RejectsThe257th_Target0CannotBeSilentlyReplaced`：
注册满 256 → 第 257 个必须 fault → target 0 未被替换 → 满员时幂等重注册仍是 no-op。

**负向验证**：移除上限断言 → 测试失败 ✅；恢复 → 合约全套 **362/362**（361 + 1）✅。

测试构造地址时的两个自坑（皆被探针即时暴露）：单字节 `(byte)(i+1)` 在 i=255 溢出为全零地址
（触发 "invalid governance target"）；单字节 extra 地址 `0xAB` 与循环里 i=170 的地址碰撞
（走了幂等路径而非 cap 路径）——「构造看似平凡的 distinct 测试数据」也有边界。

### A.14.4 状态机审计的其余结论（无缺陷）

- `IncrementAndCountApprovals` 只在**首次**跨阈值时记 `approvedAt`（后续投票不可重置计时器）
- 升级窗口（notice/execution/cooldown）明确标注 **advisory only**，真实闸门是
  `isApprovedAndTimelocked` + payload 绑定 + per-consumer consumed——文档诚实，无「看起来是
  安全边界实际不是」的假象
- `CancelProposal` 拒绝已执行的提案（终态保护）

## A.15 第十二轮：仓库清理——悬挂交付物与 CI 断裂风险

### A.15.1 发现：六个已完成的工作产物从未进入版本控制

多轮修复的产物停留在「工作树里存在、git 未跟踪」状态。其中三个是**代码**：

| 文件 | 不跟踪的后果 |
|---|---|
| `scripts/ci/verify_cross_language_encoding.py` | **build.yml:46 直接调用它** —— push 即 CI 断裂（FileNotFoundError），六个跨语言锚的门禁全部失效 |
| `bridge/neo-zkvm-host/src/resource_guardrails.rs` | 被 `lib.rs` 的 `mod` 引用 —— Linux CI clone 后**编译失败** |
| `tests/NeoHub.Contracts.VmTests/UT_ZkVerifier_MultisigRoute_Vm.cs` + `UT_GovernanceController_ThresholdRecheck_Vm.cs` + `TestingArtifacts/NeoHubMultisigVerifier.artifacts.cs` + `tests/Neo.L2.State.UnitTests/UT_CanonicalEncodingFixture.cs` | P2-3 回归测试、threshold 重查、multisig artifacts、fixture 镜像全部从 CI 消失——「本地 362 绿」≠「CI 362 绿」 |

根因模式：**在未提交的工作树上连续叠加多轮工作**，每轮新增文件从未 `git add`，而修改
（M 状态）与新增（?? 状态）在 `git status` 混排时极易只注意到前者。全量 add 修复；CI 引用
与 `git status` 的 ?? 逐一交叉核对确认无遗漏。

### A.15.2 其余清理

- **`.workbuddy/`（本地会话数据）加入 `.gitignore`**——此前以未跟踪状态裸露，一次
  `git add -A` 就会把机器本地状态推进仓库
- 删除工作残留：根目录 `test-results.log`（未跟踪的 9 月测试输出）、`/d/tmp` 下 12 个
  本轮审计的临时脚本与 .bak
- **保留不动**（有意的历史档案，移动会断 `docs/archive/` 引用）：根目录 9 月 testnet
  部署五件套（deploy-plan-5pillar.json / testnet-*.sh / testnet-deployment-report.json）、
  9 个 `.zh.md` 翻译对（10-21 会话交付物）、`DA_LAYER_EXPLAINED_ZH.zh.md` 的怪异命名
  （英文原版文件名本身含 ZH，改名断 3 处引用）、745KB 的 CHANGELOG（11k 行完整历史）、
  `docs/audit/` 75 个历史审计（命名规范、按日期归档）
- 核对 `.gitignore` 覆盖度：`chain-*/`（devnet LevelDB 数据）、`src/bin/` negate 规则
  （第六轮误删事故后加）均在位，无需改动

### A.15.3 清理方法论

「清理」不是删东西，是**让每个文件处于它该在的状态**：跟踪该跟踪的、忽略该忽略的、
删除纯残留的。对 139 个变更文件逐一分类（116 M + 23 ??），?? 再分三类（必须跟踪的代码 /
合法交付物文档 / 本地状态），每类处置都有依据。最大风险恰好藏在最不像问题的地方——
一个未跟踪的 `??` 让 CI 引用悬空，比任何垃圾文件都危险。

## A.16 第十四轮：CI 洋葱——VM 方言分叉与十二项被掩盖的断裂（已全绿）

2026-10-23。前十三轮的成果第一次推送 CI，修复清单像剥洋葱一样层层展开：每修一层，
就露出下一层被掩盖数周的断裂。最终四大工作流全绿（build / Integration Tests /
formal model / SDK Conformance 各自都是数周以来首次），并发现一个潜在严重度最高的
新缺陷：zkVM 见证解析器说错了 VM 方言。

### A.16.1 VM 方言分叉（关键发现）

**入口**：`native_executor_cli` 测试报 `unsupported transaction witness script shape`。

**决定性排查**：绕开被本地 Copy 崩溃卡住的测试项目，建独立最小工程直接 dump
C# `Contract.CreateSignatureRedeemScript` 的实产字节——得到 **40 字节**
`0C 21 <pubkey> 41 <hash>`，而非标准 N3 的 39 字节 `21 <pubkey> 41 <hash>`。
反射读取 VM 的 `OpCode` 枚举（196 项）确认：**fork 完全重排了 opcode 表**——

| opcode | 标准 N3 | 本 fork |
|---|---|---|
| PUSHDATA1 | 0x0C（N3 也有） | **0x0C**（取代 PUSHBYTES1..75 的地位） |
| PUSHBYTES33 | 0x21 | **不存在**（0x21 = NOP） |
| PUSH1..16 | 0x51..0x60 | **0x11..0x20** |
| SYSCALL | 0x41 | 0x41（不变，hash 操作数 56e7b327 经 SHA256 验证一致） |

C# core VM 与 Rust `external/neo-vm-rs`（`vm/opcode.rs`：`PUSHDATA1 = 0x0C`、
`NOP = 0x21`）说同一方言；**唯独 `execution-core/witness.rs` 按标准 N3 解析**。

**后果**：任何真实 fork 交易的见证（由 C# ScriptBuilder 产出）都会被 zkVM 验证器
fail-closed 拒绝——批量管道在 Validity 路径上**功能性死亡**，而它自己的单元测试全绿，
因为 `witness_core.rs` / `batch_core.rs` / fixture 生成器全部手搓了同样错误的方言
（自造 oracle 模式的又一次登场，这次两侧手搓的恰好都是错的）。

**修复**：

- `witness.rs` 三个解析器（单签 / multisig / 调用脚本）按 fork 方言重写
- 两个新锚：C# 实产 40 字节的逐字节正向锚（scalar 7 的公钥与脚本互验）+
  标准 N3 形状必须被拒绝的负向锚（接受双方言会让批量携带真实 VM 无法执行的见证）
- `witness_core.rs` / `batch_core.rs` fixture 重构；`stateful_batch_v1.hex` /
  `native_transition_v1.hex` / `native_execution_output_v1.hex` 经
  `NEO4_REGEN_FIXTURES` 再生（新增配套 REGEN 测试，C# 侧 RustGoldenFixture
  对账测试随之同步）
- C# 侧 `UT_Sp1StatefulBatchExecutor` 改用真实 `CreateSignatureRedeemScript`
  输出 + `SignData(network)` 真签名（`GetSignData` 的 36 字节构造与 Rust 消息
  哈希逐字节对照源码验证）

**教训（升级既有约定）**：跨语言 fixture 必须来自**真实生产侧输出**——dump 实产字节
做锚，两侧手搓等于自造 oracle。方言类分叉用「第三实现产向量」也防不住（向量本身
会按错误方言生成）；只有对拍真实 VM 的产出才能暴露。

### A.16.2 十二项被掩盖的断裂（层层剥开）

| # | 断裂 | 掩盖时长 |
|---|---|---|
| 1 | 子模块 pin 未发布（neo 的 canonical 分支、neo-vm-rs 分支）→ CI checkout 全挂 | push 那一刻起 |
| 2 | formal-model.yml 不拉子模块 → l2_bridge/bridge_abi 模型每次必红 | 自 2026-09-25 |
| 3 | 9 个 formal 模型 digest fail-closed（合约变更触发设计门禁）→ 逐一重审后刷新 | 本次合约变更 |
| 4 | vendored neo-zkvm lockfile 锁旧 rustls/quinn/h2（11 个 advisory）→ 跨仓库升级并发布，父仓库 h2 ignore 移除 | 自 vendoring 起 |
| 5 | manifest invariants 钉 9 个已删除合约（2026-09-06 consolidation）→ 按 4 支柱迁移图重定位 | 自 09-06 |
| 6 | UT_OptimisticChallengeAllowlist 整文件钉已删除合约 → 删除 | 自 09-06 |
| 7 | IntegrationTests `OutputType=Exe` 残留 + Build(Debug)/test(Release) 配置错位 → **该 lane 历史从未绿过** | 自存在起 |
| 8 | regenerate 清单漏 MultisigVerifier → push 即 type-check 红 | 本次搭车 |
| 9 | test_ci_gates 钉旧 needs 列表 → sp1-pr-gate 加入后必红 | 本次搭车 |
| 10 | minimum-tests 守卫（19 / 2）未随测试删除同步 → 必红 | 自 09-05 |
| 11 | 覆盖率 89.87% < 90 → 补 4 个零覆盖源的单测（池化序列化 / attestation 域 / finalizer 窗口） | 本次搭车 |
| 12 | dotnet format（尾随空白 / 嵌套缩进 / 末尾换行）→ 全仓清理 | 累积 |

**根因模式**：全部共享同一触发条件——此前多轮工作在未推送的工作树上叠加，CI 从未
跑过任何一轮。而 CI 的失败点永远停在最早的断裂处（先是 checkout，后是 workflow
级别的配置），后面的断裂因此零信号。「本地绿」在这种环境下毫无证明力，尤其本地构建
还叠加着间歇性 Copy 崩溃与陈旧 dll 陷阱。

### A.16.3 依赖阻塞簇归档（sp1 6.2.1 簇）

夜间 Dependabot 自动尝试更新 `lru` 与 `p3-challenger` 全部失败（updater 解析不出
可行动 PR），归因确认同一簇：

- `lru ^0.12` ← sp1-prover 6.2.1 ← sp1-sdk 6.2.1（`=6.2.1` 硬 pin）——两条 unsound
  advisory（RUSTSEC-2026-0002 修于 0.16.3 / RUSTSEC-2026-0253 修于 0.18.2）不可达
- `p3-challenger 0.3.3-succinct` ← SP1 专属 Plonky3 fork（p3-commit / p3-fri /
  p3-uni-stark / slop-challenger 内部链）
- `ruzstd ^0.8.1` ← backtrace 0.3.76 → object 0.37.3（既有记录）

三者均为 cargo-audit 的信息性警告（非漏洞），门禁通过；sp1-sdk 下一次整簇升级后
自动清零（届时 guest ELF 的 SHA+VK 双 pin 必须随 guest 重编译再生）。

### A.16.4 环境教训（本轮新增）

- **本地构建崩溃持续时，立即切换到「CI 为权威验证」模式**：本机 MSBuild Copy 崩溃
  与磁盘 99% 叠加，重试无意义；直接推送让 CI 判定，代价是多几轮 push 循环但每轮
  都有决定性输出
- XML 注释不能含 `--`（修 OutputType 的说明注释本身弄坏了 csproj）
- 尾随空白清理脚本的 `join()` 会吃掉 final newline → FINALNEWLINE 门禁
- `JsonRpcClient` 构造要 `Uri` 端点；fake handler 必须回显请求 id
  （`-32603: response id does not match request id`）
- `ECDsa.SignHash(signData)` 与 `SignData(signData, SHA256)` 语义不同：
  `GetSignData` 返回的是**未哈希**的 36 字节消息，必须后者

### A.16.5 最终基线（2026-10-23 20:00 UTC）

合约 VM 362 · 部署 111 · executor 单元 15 · execution-core 49 · guest 18 ·
gateway-guest 15 · formal 模型 9/9 passed · 跨语言门禁 7 面 119 断言 ·
cargo audit 漏洞 0（ruzstd 一项 ignore，信息性警告 3 项同簇）· npm audit 0 ·
**CI 四大工作流全绿**（build 的 forge install 曾因网络瞬态失败一次，重跑通过）。
真证明 release gates 于 2026-10-23 20:00 UTC 以 workflow_dispatch 首次在修复后的
master 上触发，验证结果见后续记录。

## A.17 第十五轮：release-gate 洋葱——pin 传播链与六个从未编译的站点

2026-10-24。上轮的四工作流全绿只覆盖 push 车道；真证明 release gates
（workflow_dispatch / 夜间专属）在这些修复后首次运行，又剥出一整层洋葱。
夜间调度历史核对：自 10-19 起每夜 40 秒内失败——**这些车道在 CI 上从未完成过**。

### A.17.1 修复清单（十一个提交，全部依据 CI 实测输出）

| 类别 | 内容 |
|---|---|
| MSRV | `serial_test`/`serial_test_derive` 4.0.1（被合入的 Dependabot bump）要求 rustc 1.93.1，SP1 车道钉 1.88.0 → 双双降到 4.0.0（**主 crate 与 proc-macro companion 必须分别降**；WSL 1.88 `cargo metadata` 验证） |
| fmt/clippy 债 | fmt 与 clippy `-D warnings` 只在 workspace-release lane 跑且从未跑完 → 20 文件格式漂移 + 9 处真实 lint（Layout C 编码器八参数定向 allow——参数即线字段；恒等转换×5；needless borrow；collapsible if；unused import；assertions_on_constants → 编译期 const 断言） |
| batch 双 pin | witness 方言修复改变了编译进 guest 的 execution-core → ELF SHA+VK 双 pin 失配。**改进 build.rs：先派生 VK 再比较，单次 panic 同时报两个 actual**（原流程需两轮失败 CI）；actual 即刷新来源 |
| pin 传播链 | gateway guest 程序**内嵌** batch VK（batch_vk_manifest.rs → build.rs 生成常量 → 编译进程序）→ 刷新 batch pin 连锁改变 gateway 程序 → gateway ELF/VK pin 二轮刷新（words 先行，gateway actual 次轮报出后写入）。链已闭合：witness 修复 → batch ELF → batch VK(bytes32+words) → gateway 程序 → gateway ELF/VK |
| 从未编译的站点 ×6 | prove_batch.rs（encode_child_sidecar 缺 forced_inclusion_count 第 7 参；verify 的 Vec→&[u8;32]）、prove_gateway.rs（DaemonConfig 缺队列两字段 + consts 未导入）、daemon.rs（warn! 宏未导入）、groth16_release_vector.rs（vkey Vec）、lib.rs 内联单测 ×2（verify 先查 VK pin，零 VK 触发的是 pin 错误；常量断言） |
| 第三处方言 | `generate_stateful_fixture.rs`（example 生成器）仍在产标准 N3 形状——走 example 再生 fixture 会静默回归方言分叉。已改 fork 方言（手搓点全仓共四处，全部清除） |

### A.17.2 环境事故与恢复

D: 盘 100% 满（连脚本都写不进）→ 清仓库自身 target/debug + elf-compilation +
flycheck0（SP1 构建缓存是大头）释放 **263.5 GB**。MSBuild Copy 间歇崩溃未因此
消失——独立环境 bug。本轮全程以 CI 为权威验证（约十轮 dispatch 循环，每轮
都有决定性输出）。

### A.17.3 真证明 = 标准 runner 资源上限（8 次一致观测，定性）

`prove_and_verify_real_zk_proof`（host end_to_end）与 gateway
`real_recursive_release_gate` 两个真证明测试，**每次**都在进入证明阶段后
~9-10 分钟被 "The runner has received a shutdown signal" 杀死（job 超时 120 分钟
远未触及；非测试失败；证明栈本身健康——gateway 递归证明第一次真跑就进入
实际证明阶段跑了 35 分钟）。定性：SP1 Groth16/递归证明耗尽标准 runner 内存，
OOM killer 击杀 runner 代理。代码侧无可修。

### A.17.4 剩余一码：Groth16 释放向量

`tests/fixtures/sp1-groth16-positive-vector-v1.json` 钉死生成时的 guest ELF SHA
（`a5ece1f5...`）；guest 变更后 `committed_vector_verifies_against_current_guest_
and_witness` 按设计报旧值。再生需要**一次成功的真证明**（generate_groth16_
release_vector example）——与真证明测试同一资源需求。

### A.17.5 第十五轮终态

- push 车道四工作流全绿（每轮提交均确认）
- release-gate 车道代码侧全绿：Docker 可复现构建 ✓ fmt ✓ clippy ✓ pin 链闭合 ✓
  全 workspace 测试套件 ✓（除下项）
- 待一次有足够资源的真证明运行：向量再生 + 两个真证明测试验证。
  两条出路（需项目决策）：① 本地 WSL + Docker Desktop（GUI 首次确认待办）
  按 build.rs 的 pin 流程再生向量并本地跑真证明；② 组织级配置更大 GitHub
  runner（内存型）让 release lane 在 CI 完成。
- 第十五轮方法论沉淀：**「从未编译过的站点」要主动全仓清点**（grep 全部
  调用点对签名），不能等 CI 一轮轮喂——本轮六个站点每个都吃掉 25-50 分钟
  的 CI 周期；命令链中 `cargo fmt --check` 必须用 `&&` 连接（一次 `;` 漏网
  浪费一整轮）。

### A.17.6 第十六轮补记：本地真证明收官（2026-10-24 下午）

选择本地路线后，环境修复与真证明在同一会话内完成：

**Docker 复活（三层障碍，全部实测定位）**：

1. **卡死的 AF_UNIX reparse point**（9 月非正常关机遗留）：Windows 侧一切
   删除手段全部失败（File.Delete / `\\?\` 前缀 / fsutil reparsepoint delete /
   停 wslservice——icacls 显示有权限仍 Access denied），**唯一有效路径是从
   WSL 内经 /mnt/c 用 rm 删除**（9p 反向通道绕过 Windows 侧 reparse 过滤）。
   Docker Desktop 每次启动死在 rename 旧 socket（"starting services" 崩溃）。
2. 清理后 Electron GUI 进入崩溃循环（exit 0x80000003，`--disable-gpu` 无效）
   ——连带引擎下线，Docker Desktop 在本机不可用。
3. **解法：绕开 Docker Desktop**——Ubuntu 24.04 WSL 的 systemd 在跑，
   以 `wsl -u root` 安装 docker-ce（先删 Docker Desktop 集成残留的
   `/usr/bin/docker` 死符号链接 + `dpkg --configure -a` + 清
   `~/.docker/config.json` 的 `docker-credential-desktop` 凭据助手），
   `systemctl enable --now docker` → Engine 29.9.2 in-WSL。

**SP1 工具链**（照抄 build.yml 钉值）：cargo-prove 6.2.1（SHA 校验）+
`prove install-toolchain` + 钉住的 SP1/GNARK Docker 镜像拉取。WSL 资源
70 GiB / 24 CPU（CI 标准 runner 16 GB——OOM 根因）。

**本地验证结果（全程 CI 同款节流 env，保真）**：

| 阶段 | 结果 |
|---|---|
| 1. `cargo build` host（build.rs Docker 可复现 guest 构建 + 双 pin 校验） | ✓ **pin 与 CI actuals 刷新值逐字节一致** |
| 2. `end_to_end` 两个 ignored 真证明测试 | ✓ `prove_and_verify_real_zk_proof` / `verify_rejects_mismatched_public_input_hash` 全过（2732.9s，峰值 RSS 23.1 GiB——CI 16 GB 跑机的死亡点被直接越过） |
| 3. `generate_groth16_release_vector`（真证明 + 原子写） | ✓ 新向量 `guestElfSha256=210022e2...`（旧 `a5ece1f5...` 退役），356B Groth16 证明 |
| 4. gateway 真递归 release gate | ✓ `proves_and_host_verifies_real_recursive_gateway_groth16` 通过（5037.5s；gateway host 构建含双 guest Docker 构建 + 三 pin 校验全过） |
| 5. 再生向量的钉住测试回归 | ✓ `committed_vector_verifies_against_current_guest_and_witness` 通过 + guest 套件 19/19 |

至此第十五轮 A.17.4 的「剩余一码」闭合：**审计期间发现的全部缺陷（含 VM 方言
分叉）在真实 SP1 Groth16/递归证明下端到端验证通过**。CI 侧：push 车道应保持
全绿，workspace-release 车道的向量断言将随本提交转绿；两个真证明 lane 在
标准 16 GB runner 上仍不可完成（记录性质变为「资源上限，非代码缺陷」——
需要组织级内存型 runner，或以本轮的本地流程为准）。

**第十六轮环境教训**：PowerShell→wsl.exe 的内联命令必被转义搅局、Git Bash
的 wsl.exe 调用必被路径转换破坏——**只有 `wsl -e bash <脚本文件>` 可靠**；
Windows 侧删不掉的 reparse point 从 WSL 内 /mnt/c 删。

### A.17.7 第十七轮：依赖分诊与 guest pin 耦合铁律（2026-10-24 晚）

对 7 个 Dependabot PR 做实测分诊（不是看 changelog 拍脑袋——每个都实际构建
并用本地 Docker 重建校验 guest ELF pin），确立**升级与 pin 的耦合铁律**：

| PR | 处置 | 实测依据 |
|---|---|---|
| #95/#94 vitest | 关闭（superseded） | sdk/typescript 已 ^4.1.12，高于 PR 的 4.1.11，npm audit 0 |
| #90 sp1-verifier 6.5.0 | 关闭（pin 冲突） | =6.2.1 硬 pin 与 Docker/gnark 镜像 digest 绑定，只能整簇升 |
| #93 tokio | **保留升级 1.54.1** | 不在 guest 树；含 RUSTSEC-2026-0386 修复（PR 的 1.53.1 不够） |
| #92 thiserror | **回退** | 2.0.18→2.0.21 实测改变 guest ELF（proc-macro 补丁也动产物） |
| #91 num-bigint | **回退** | 0.5 下 execution-core 49/49 仍绿，但 guest 内嵌它 → ELF 变 |
| #89 rand | **删依赖** | watcher 声明 rand 但全仓零使用；剩余 rand 0.9.5 不进 guest |

**铁律（本地 pin 校验发现的隐蔽耦合）**：SDK p256 0.13→0.14 的升级也回退了
——0.14 新树要求 hybrid-array≥0.4.13，cargo 版本统一把 **guest 旧树的共享
依赖**也抬高，ELF 照样变。**被升级的包不在 guest 树 ≠ 不影响 guest**。
且 CI 快速 lane 对 pin 失配无感（不重建 ELF）：p256 升级提交在 CI「全绿」，
若不回退，夜间 release 调度才会炸——本地 pin 校验（~1-8 分钟）提前拦截。

净结果：tokio 1.54.1（清一条 unsound 警告）+ rand 0.9.5 + 未用依赖删除，
pin 复验绿，push 五工作流全绿（0185c4aa）。guest 树的升级统一并入
sp1 pin 刷新窗口（ELF→VK→gateway→向量→合约链一次刷 + 真证明）。

**release 车道里程碑**：本轮 dispatch 复验中 **SP1 workspace release 首次全过**
（1h5m9s——Docker 可复现构建 / fmt / clippy / 全 workspace 测试套件 /
再生向量断言全绿）。两个真证明 lane 仍为标准 16 GB runner 的 OOM 上限
（27m39s / 35m26s，与历史八次完全一致的时间窗），本地已验证通过——
release 车道的代码侧至此无任何已知缺陷，剩余仅为 runner 资源规格问题。


