# Neo N4 弹性链系统架构评审报告

- 日期：2026-10-25（第十八轮）
- 范围：系统架构 / 系统设计 / 系统实现 / 项目布局 / 代码架构 / 验证与发布工程
- 依据：`doc.md`（1991 行主规格）、`ARCHITECTURE.md`、`IMPLEMENTATION_STATUS.md`、合约与 Rust/C# 源码实测、过去十七轮缺陷修复与真证明验证记录
- 方法：规格 ↔ 实现逐项对照；结论均可追溯到文件与行为，不采信宣传性表述

---

## 1. 执行摘要

**总体判断：Neo N4 的架构设计达到行业一线水准，其"信任最小化"的落实方式在同类 L2 项目中属于上游；当前系统处于"密码学校验已贯通、生产化证据未齐"的阶段——不是 demo，但离 mainnet 还差部署证据与独立审计，而不是差架构。**

三个最有分量的观察：

1. **架构的强项不在"用了 ZK"，而在于对"哪些东西绝不能信任"的系统性回答。** 证明验证不可绕过（部署即强制 verifier registry、无"零地址跳过"模式）、genesis 根不可变、合约不可升级（`_deploy(update)` 直接返回）、guest 内编译期锁定 child VK、公开输入哈希绑定强制入列数、fail-closed 遍布所有未就绪路径。这些不是注释里的承诺，是逐行可验的代码。
2. **执行正确性的工程基础是"单一权威 + 多处镜像钉死"。** 同一 Rust 执行核心同时服务 C# 节点（host-native 进程外调用，SHA-256 钉二进制）与 SP1 guest；编码面以唯一权威向量三语言锚定；guest ELF → VK → gateway 内嵌 VK → Groth16 向量 → 链上合约 pin 五处镜像构成可复现供应链。第十七轮发现的"共享传递依赖跨树传染"也已固化为升级纪律。
3. **最大的架构边界（必须对外讲清）**：当前 ZK 有效性证明覆盖的是**有界原生/syscall 执行子集**（`N4 genesis V1`，不支持语义 fail-closed），不是任意 NeoVM 合约的通用执行证明；通用路径是 PolkaVM profile，需配套 prover。同时 Phase 3 乐观挑战窗口是**受限 v4**（仅精确注册的 transition），通用 optimistic 未接线（合约直接 fail-closed）。系统的"ZK rollup"标签在这两个覆盖度完成前只能按 profile 授予，不能泛化。

**就绪度结论**（与 IMPLEMENTATION_STATUS 一致，本评审独立核对）：Phase 0/1/2/4/5 的代码形状与集成路径存在且经本地/CI 验证；**所有阶段的 `Current-revision deployment evidence` 与 `Production-ready` 均为 ❌**——阻塞项是精确修订的部署证据、独立安全审计、运维演练，而非架构返工。

---

## 2. 系统架构评审

### 2.1 分层与信任边界

```
Neo N3 / Neo 4 L1   结算、资产托管、治理（dBFT 终局性）
  └─ NeoHub 4-Pillar 合约套件（部署型合约，非 L1 原生）
       └─ 可选 Neo Gateway（SP1 递归聚合 + 全局消息根；不托管资产）
            └─ N 条 Neo 4 L2（Neo core fork + L2 扩展）
```

评价：

- **职责切分正确且少有冗余。** Gateway 只做证明聚合与消息根，不碰资产；资产只在 SharedBridge；结算只在 RollupHub；治理只在 GovernanceController。这条"资产/结算/证明/治理"四分离的边界，是 ZKsync Elastic Chain 模式在 Neo 技术栈上的正确移植。
- **L1 集成方式选型务实。** NeoHub 以**部署合约 + 插件**而非 L1 原生合约交付，避免了对 L1 共识层的侵入式修改；L2 侧必要的核心 delta（10 个 L2 native contracts、ChainMode、执行钩子）集中在 `r3e-network/neo` fork 的独立分支（`r3e/neo-n4-core` / `r3e/neo-n3-core`），fork 政策文档化、上游同步受控。这是维护成本最低的合理结构。
- **DA 分层（L1 / NeoFS / external / DAC）与安全标签绑定**，且 `AssertSecurityConfigurationCompatible` 在结算时强制 securityLevel × daMode × proofType 组合合法——"链不能以低于其宣传安全级别的方式结算"是**链上强制**而非链下自律，这是多数 L2 没做到的。

### 2.2 NeoHub 4-Pillar 合约架构（5,328 行 / 6 项目）

| Pillar | 职责 | 评审 |
|---|---|---|
| RollupHub（1793 行） | 链注册、批次结算、DA 记录、强制入列、提款 Merkle 验证、Gateway 发布 | 核心正确；见下 |
| SharedBridge（1053 行） | 资产金库、映射、消息路由、紧急提款 | 不变量与防重放完整 |
| ZkVerifier（553 行） | 证明信封路由、VK 注册表、SP1 配对 | 注册表部署一次写入不可变 |
| GovernanceController（1400 行） | 多签、timelock、双层暂停、罚没、council rotation | 提案载荷字节级复核，防重放按 action 维度 |
| + Sp1Groth16Verifier（290 行） | 无状态 SP1 Groth16 终端验证 | released-VK 硬钉（与向量同源刷新） |
| + MultisigVerifier（239 行） | 多签 attestation | 阶段化信任 |

**值得点名的好设计：**

- **0-hop 原生存储直读直写**替代微合约互调：降低 gas 35–50%（规格目标），同时减少跨合约调用引入的权限/重入面。`SubmitAndFinalizeBatch` 单步原子结算消除了两步交易窗口。
- **Fail-closed 是一致的设计语言。** `_deploy` 拒绝零 verifier registry（注释明写"不存在零地址跳过验证的模式"）；optimistic proofType 在挑战窗口未接线前直接拒绝；不支持的执行语义在 guest 侧 fail closed。**系统的默认姿态是拒绝，而不是放行。**
- **信任锚的连续性**：`batchNumber == latestFinalized + 1`、`preStateRoot == canonicalRoot`、首批 pre-state 必须等于注册时不可变的 genesis 根——不存在"第一个提交者自定义信任锚"的开口。
- **RevertBatch 的边界**收敛到"pending 或最新 finalized 一批"，回滚恢复 pre-state 并**回绕强制入列队首**（用户的强制交易在治理回滚后仍存活）；Gateway 已发布批次永不可回滚。`lockGovernance()` 后 owner 失去直接回滚/换件能力，只剩阈值提案 + timelock 路径——**owner 权力有明确的、链上强制的退役机制**。

### 2.3 证明系统架构（本系统最硬的部分）

- **证明对象正确**：只证明确定性状态转移函数（`ApplyBatch`），不证明 P2P/RPC/钱包/DB——避免"证明整个节点"的不可行路径。
- **同一执行核心，两种宿主**：`neo-execution-core`（no_std）同时被 SP1 guest（RISC-V）与 host-native `neo-zkvm-executor`（C# 节点的执行边界）使用。C# 侧 `Sp1StatefulBatchExecutor` 以 SHA-256 钉住 native 二进制、进程外执行、30 分钟超时、单并发门。**这从结构上消灭了"C# 与 Rust 两套语义实现长期漂移"这一类 L2 常见病**——第十七轮前发现的 VM 方言分叉属于编码层，已通过 fork 方言统一 + 权威向量收编。
- **递归聚合的边界是显式协议而非黑盒**：`NEO4GWP1 || binding170 || countLE32 || ...`、child public values 固定 `0x00 || batch.PublicInputHash`、child 必须是 SP1 6.2.1 compressed proof 且由 **guest 内编译期锁定的 batch VK** 验证、请求不得携带 VK/路径/Groth16 child。sidecar 按 canonical 文件名寻址、拒符号链接、原子发布、ready marker 前复验。这是把"供应链与证明链当作同一个安全对象"的正确做法。
- **链上验证与链下 pin 同源**：合约 `ExpectedProgramVKey` 与 `vk_manifest.rs`、Groth16 释放向量同值刷新；已实测真证明（356B Groth16）经 NeoVM 配对验证通过。

### 2.4 治理与升级模型

- **不可升级合约 + 显式迁移**（`_deploy(update)` 直接返回；SharedBridge 提供 owner 控制的 `MigrateLockedBalance` + 一次性 `SealLockedBalanceMigration`）。这是 Uniswap 式的"无升级攻击面 + 可迁移"路线，与"verifier registry 部署一次写入不可变"一致。**代价是 bug 修复只能迁移——因此迁移 runbook 的演练必须列入发布门槛（见 §5 建议 A-4）。**
- **治理提案的执行是字节级自证**：`ExecuteGovernanceProposal` 从 action 参数重推编码并与提案载荷逐字节比对，relayer 无法偷换参数；每个 action 类型消费一次，防"一个提案回滚多批"。
- **安全标签体系**（securityLevel / daMode / sequencer / exit / gateway）使信任分级对用户可见，而非一刀切宣称 trustless。

---

## 3. 系统实现与代码架构评审

### 3.1 模块组织（实测）

| 层 | 规模 | 评价 |
|---|---|---|
| `contracts/` 6 项目 | 5,328 行 | 高内聚低文件数；常量偏移集中定义 |
| `src/` 27 项目（Neo.L2.* 库 + Neo.Plugins.* 插件） | 50,821 行 C# | 库/插件分层清晰；Abstractions 仅依赖 Neo core |
| `bridge/` 5 crate | 13,552 行 Rust | execution-core no_std 可进 guest；hashing 注释直接引用审计镜像陷阱 |
| `watchers/` | 6,998 行 | 外链 watcher 为 eth crate 的 path 薄封装，canonical_message 无额外镜像 |
| `sdk/`（rust + typescript） | 小 | RPC 方法名 10/10 对齐（实测） |
| `tests/` 39 个 .NET 项目 + Rust 套件 | **90,025 行** | 测试/生产 ≈ **1.77 : 1**，罕见的高密度 |
| `docs/` 60+ 文档 + runbooks | — | 权威顺序显式声明（doc.md > ARCHITECTURE > walkthrough > STATUS） |

**实现质量的直接证据**（抽查）：`CanonicalSettlementPipeline` 的 durable 队列 + poison 检查点 + 恢复状态（`SettlementRecoveryStatus`/`SettlementPoisonedException`）；`L2SettlementProductionComposition` 组合根注入与生命周期管理；持久化层 `CompareExchangeAll` 以完整 pre-state 比对 + 单 WriteBatch 提交防并发覆盖；七类组件重开测试钉住持久化。

### 3.2 跨语言一致性工程（本项目的差异化能力）

唯一权威向量 `tests/Shared/canonical_encoding_vectors.hex`（由第三种语言产出，非任何一方输出）+ C#/Rust/合约 VM 三侧钉死；跨语言 fixture 必须来自**真实生产侧输出**（禁止两侧手搓——"自造 oracle"被明确列为反模式）；fork VM 方言（PUSHDATA1/2/4=0x0C..0x0E）在 witness 方言锚测试中固定。这套机制直接源自真实缺陷（提现叶 98B vs 118B、352 vs 348、ProofType 判别值），是"把事故转化为结构防线"的成熟工程文化。

### 3.3 可复现供应链

guest ELF SHA+VK 双 pin、build.rs 先派生 VK 再比较并一次报出全部 actual、digest 钉住的 SP1/GNARK 镜像、cargo-prove 与 rustc 版本钉值、`cargo-prove-locked.sh` 只允许 `--docker --locked`。**依赖升级与 pin 的耦合已成文**（含"CI 快速 lane 对 pin 失配无感"的教训）。

---

## 4. 验证与发布工程

- **测试金字塔完整**：编码向量锚、单元测试、VM 合约测试（含真实 Groth16 配对）、跨语言差分（live C#↔Rust 随机执行）、集成/devnet、release 车道全量套件。
- **CI 分层合理**：push 快速 lane（编码/单元/类型/审计）+ dispatch/夜间 release lane（Docker 可复现构建、fmt/clippy、真证明）。本轮 **SP1 workspace release 首次全过**；两个真证明 lane 的 CI 失败已定性为标准 runner 内存上限（本地 70 GiB 全过），非代码缺陷。
- **发布纪律文档化**：`SECURITY.md`（范围、私密披露、devnet stub 明确排除生产）、`release-readiness-checklist.md`（源冻结→本地门禁→产物审查→部署计划→devnet 演练→CI 批准→生产切换）、`INCIDENT_RESPONSE.md`、operator runbooks。

---

## 5. 架构级风险与改进建议（分级）

### A 级：主网前必须闭合（多为证据与边界，而非返工）

- **A-1 通用执行证明的覆盖度边界必须显式化并纳入发布标签。** 当前 `Sp1StatefulNeoVmV1` 是有界原生/syscall profile（descriptor 集固定、不支持语义 fail-closed）。建议：① 在 `getsecuritylabel`/链注册元数据中把"执行覆盖度"作为独立维度发布（bounded-native vs general-neovm），避免把 bounded 链宣传为通用 ZK rollup；② 把 PolkaVM profile 的 prover 配套列为 Phase 4 的硬门槛；③ 维持 fail-closed，禁止以"先放开再补"方式扩大语义。
- **A-2 乐观路径的当前形态与路线图需对齐口径。** 合约已 fail-closed 拒绝 optimistic proofType（正确），Phase 3 仅受限 v4。建议：文档与对外材料统一表述为"受限 optimistic（exact-registered transitions）+ 挑战编排已实现（BisectionGame）"，通用 optimistic 单独立项，不与现有 settled/ZK 混称。
- **A-3 owner/多签密钥面是最高价值目标，需独立密钥治理证据。** `FinalizeBatch`、`RegisterChain`、迁移、紧急暂停均经 owner/多签；`lockGovernance()` 是 owner 权力的链上退役点。发布清单应强制：部署即锁定（含 SharedBridge 结算授权绑定 RollupHub + readback smoke）、council 门限与 timelock 参数审计、owner 私钥的 HSM/多签托管证明、以及一次完整的"提案→timelock→执行→回滚"演练记录。
- **A-4 不可升级合约的迁移路径必须演练。** 建议将"SharedBridge 迁移 + seal + 新套件切换 + 资产/根对账"写成可执行 runbook 并在 devnet 演练一次，作为发布证据的一部分。
- **A-5 精确修订部署证据与独立审计**（与 STATUS 的 ❌ 列一致）：固定 commit 的可复现产物（NEF/manifest/VK）、部署记录、链上正/反 smoke、独立安全审计。这是当前唯一横贯所有 Phase 的硬阻塞。

### B 级：中期演进（架构韧性）

- **B-1 活性与审查抵抗依赖运营者的部分要量化。** 强制入列 + deadline + 罚没已具备；建议为 `enqueueForcedInclusion → 被打包` 的端到端时延、以及 `FinalizeBatch`（owner-only）的活单点设定可观测 SLO 与降级预案（谁能在 owner 失联时推进？是否应引入链级 operatorManager 的权限化 finalize，当前该字段仅是 config 元数据）。
- **B-2 DA 可用性的可证明性。** NeoFS 为默认 DA，建议把"任意已结算批次的 DA 可取回"做成连续的外部挑战/巡检（而非仅记录 daCommitment），并把 DA 不可用的链上应对（暂停结算而非接受）纳入应急演练。
- **B-3 Gateway 消息活性与 watermark 不可逆推进的运维边界。** 建议监控 watermark 停滞、sidecar 孤儿清理、以及 4096 上限的聚合批次大小对 gas 的影响（streaming frontier 已是 O(log n)，需实测高负载 gas 曲线）。
- **B-4 依赖治理的窗口化。** 已确立"guest 树升级并入 pin 刷新窗口"铁律；建议为 sp1 整簇升级（含 num-bigint 0.5、thiserror、p256 0.14、sp1-verifier 6.5.0、ruzstd/lru/p3-challenger 上游解锁）安排一次完整窗口（含真证明向量再生，本地约 2.5 小时）。

### C 级：优化建议（不阻塞）

- **C-1 src 项目粒度**：27 个项目中若干小型库（Telemetry/Audit 等）可考虑合并以减少工程开销；但现有库/插件边界清晰，非必要不动。
- **C-2 文档防漂移**：60+ 文档建议由覆盖矩阵自动校验关键数字（合约数、RPC 方法数、测试计数），延续"数字必须来自工具输出"的纪律。
- **C-3 权限字段的语义澄清**：`L2ChainConfig.operatorManager` 当前为元数据，结算权限实为"开放 submit + owner finalize"。建议要么在文档明确此模型（正确性由验证器保证、活性由 owner 保证），要么在未来版本把 finalize 权限下沉到链级 operator。

---

## 6. 与世界级参照系的相对位置

| 维度 | Neo N4 现状 | 参照 |
|---|---|---|
| 共享桥 + 链注册 + 聚合层 | 与 ZKsync Elastic Chain 同构，且 Gateway 不托管资产 | ZKsync Elastic Chain |
| 证明边界（只证 STF、同一执行核心双宿主） | 正确且少见地杜绝了双语义实现漂移 | 优于多数自研执行层 |
| 安全级别链上强制（securityLevel×proofType×DA） | **强于**仅靠链下标签的通行做法 | 行业上游 |
| 不可升级 + 迁移 + 治理锁定退役 | 与 Uniswap/成熟 DeFi 同级 | 行业上游 |
| 可复现供应链与 pin 链 | digest 镜像 + 五处镜像 + 升级耦合纪律 | 行业上游 |
| 通用执行证明覆盖度 | 当前 bounded profile，通用待 PolkaVM | **落后**于通用 zkEVM 的覆盖承诺 |
| 生产证据（部署/独立审计/演练） | 未齐 | **落后**于已上主网的 rollup |

**结论：架构方向正确、信任工程扎实、验证文化突出；要把"世界最高标准"坐实，剩下的不是再设计，而是（1）通用执行证明覆盖度的达成或显式边界化，（2）精确修订的部署证据 + 独立审计 + 密钥与迁移演练。**

---

## 附：本评审的事实核查口径

- 代码量/文件数由 `find|wc` 实测；测试比 90,025 / 50,821 ≈ 1.77 为 C# src 对 tests 全部（含 Rust/合约测试行数混入 tests 目录统计，口径已在文中注明）。
- "bounded native/syscall profile" 出自 `IMPLEMENTATION_STATUS.md` 对 `neo-zkvm-guest` 的描述与 `Sp1StatefulBatchExecutor` 源码；"optimistic fail-closed" 出自 `RollupHubContract.SubmitBatchCore` 的显式断言。
- 真证明与 release 车道结论来自 2026-10-24 本地 WSL 实跑（单证明 ~45 分钟、gateway 三重 ~84 分钟）与 CI 运行记录。
