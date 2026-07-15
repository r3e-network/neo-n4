# 测试方法论 —— Neo Elastic Network

`neo4` 采用的测试方法。镜像 ZKsync 的 Foundry + Hardhat + 集成测试栈,
按系统每一片所在的位置翻译到 .NET / Rust / TypeScript / Solidity。

---

## 测试面(38 个 .NET 测试工程 + 跨语言门禁 + 2 个 SP1 发布关口)

| 层 | 框架 | 位置 | 内容 |
|---|---|---|---|
| 单元 | MSTest(xUnit 风) | `tests/`(solution 当前 38 个测试工程，包含集成套件) | 每类不变量、边界、空参 + 空字段守卫、指标 emission 钉 |
| 集成 | MSTest | `tests/Neo.L2.IntegrationTests/` | E2E 分阶段缝合(Phase 0 → 5)、审计管线、持久化恢复、NeoVM2/RISC-V 接缝、legacy NeoVM 兼容、自定义 executor 全栈 |
| 属性 / 不变量 | MSTest + seeded `System.Random` | `UT_BridgeInvariants_PropertyBased.cs`(17 测试) | 200 次操作的随机序列 × 4-8 种子 —— 每个不变量 1600-3200 次状态转移。在每个中间态断言桥账面 + nonce 唯一性 + 双向注册表的不变量 |
| 模糊 | MSTest + seeded `System.Random` | `UT_WireFormat_Fuzz.cs`(19 测试) | 随机字节序列喂给每个 decoder —— 必须 round-trip 或以类型化异常拒绝,永不崩溃 |
| 跨语言对等 | byte-vector 钉 + canonical-bytes-match-csharp | Rust watcher 测试 + Foundry 测试 | C# encoder + Rust + Solidity verifier 间字节相同 |
| 链上↔链下对等 | C# 复刻链上决策树 | `UT_OnChainMerkleVerifyParity`、`UT_RestrictedExecutionFraudVerifierParity`、`UT_GovernanceFraudVerifierParity`、`UT_MpcFraudProof_RealCrypto` | 链下算法复刻链上 verifier 并产出相同根 / 决策;漂移在单元测试期就暴露 |
| Foundry | Solidity 不变量 + 多链 | `external/foreign-contracts/eth/test/`（44 测试） | EVM-family Solidity router —— 37 个单链 + 7 个多链，固定 17 个主网槽的每实例状态隔离 |
| 真实 CPU SP1 prover | Rust `#[ignore]` | `bridge/neo-zkvm-host/tests/end_to_end.rs`(2 测试) | 真实 ZK 证明生成(~40s prove,~20s verify,2.78 MB 证明)+ 篡改哈希拒绝负向测试 |
| 真实递归 SP1 Gateway | Rust `#[ignore]` | `bridge/neo-zkvm-gateway-host/tests/real_recursive_release_gate.rs` | 生成真实 terminal batch proof，递归证明规范 Gateway binding，并由 host 验证最终 Groth16 proof |
| Native C# ↔ Rust 执行 | MSTest + release Rust binary | `UT_Sp1StatefulBatchExecutor` + `UT_CanonicalSettlementPipeline` | C# 捕获完整 Neo genesis，调用 SHA-256 锁定的 release `neo-zkvm-executor`，并在 proof artifact 前不修改状态地校验 `NEO4EXR1`；故障注入证明 artifact-first 重放、一次原子 post-state 提交、幂等重试与启动恢复 |
| Live-RPC | Rust `--features live-rpc` | `watchers/neo-bridge-watcher-eth/src/live/` + `tests/`（由源码动态发现） | 进程内 `FakeRpcServer` —— 通过真实 `reqwest::blocking` HTTP 周期演练 `EthRpcEventSource`+`NeoRpcSubmitter` |
| 四语言 SDK 一致性 | MSTest + cargo test + Vitest + unittest | `sdk/conformance/`、四套 SDK 测试目录、`scripts/ci/run_sdk_conformance.py` | 一份规范向量覆盖 RPC 形态、u64 序列化、哈希字节序、错误、分页信封与已签名 Neo N3 交易往返；真实 N3/N4 执行按需启用并输出机器可读计数 |
| execution-core | cargo test | `bridge/neo-execution-core/` | 后端无关的 batch/state/witness 解析、receipt/effects/root 计算、规范 `NEO4EXR1`、Merkle 确定性与后端依赖守卫 |
| zkvm-guest | cargo test | `bridge/neo-zkvm-guest/` | host-mode stateful execution/tamper、Rust golden vectors，以及真实 `neo-zkvm-executor` CLI 正负向 E2E |

---

## 测试原则(摘自 ZKsync Era 并适配)

### 1. 每个 decoder 都以类型化异常拒绝垃圾输入

线协议 decoder 不允许对随机字节输入崩溃。Fuzz suite(`UT_WireFormat_Fuzz`)
喂 500 个随机字节序列 × 多个种子;允许的异常只有 `ArgumentException` /
`InvalidDataException`。任何 `NullReferenceException`、`IndexOutOfRangeException`、
`OverflowException` 都意味着缺失的边界检查。

### 2. 良好输入的 round-trip 是恒等

每对 encoder/decoder 满足 `decode(encode(x)) == x`。`UT_WireFormat_Fuzz`
对模糊化(树形状、叶数、金额、地址)元组断言 Merkle 证明和 `DepositPayload`
两者。encoder 与 decoder 间的漂移在单元测试期暴露,而非在 L1 结算时暴露。

### 3. 不变量在长随机操作序列中保持

ZKsync 的 Foundry `invariant_*` 函数在随机交易序列的每个中间态断言性质。
neo4 的 `UT_BridgeInvariants_PropertyBased` 用 seeded 随机游走测试镜像:

- **AssetRegistry 双向一致性** —— 对每个 active mapping,
  `TryGetByL2(l2)` 与 `TryGetByL1(l1, chainId)` 必须解析到同一记录。
  在每次 register / re-register / SetActive 后断言。
- **提现 nonce 唯一性** —— 每次成功的 Stage 把 (sender, nonce) 加入
  intra-batch consumed;每次 SealBatch 提升到 cross-batch consumed;
  任何 (sender, nonce) 的再次 Stage 必须抛错。
- **存款收取金额账面** —— 由 `IL2Metrics` 发出的 `DepositsProcessed`
  计数器等于未抛错的 `Process()` 调用次数。

每个不变量测试运行 200 次操作 × 4-8 个种子 —— 每个不变量 1600 ~ 3200
次状态转移。种子固定,回归字节级可复现。

### 4. 链上 ↔ 链下对等由 C# 复刻钉

ZKsync 的 Foundry 测试同时演练合约与参考 Rust 实现并对比。neo4 在 C#
中采取同样方法:每个链上 verifier 都有一个对等测试,复刻其决策树
(`UT_OnChainMerkleVerifyParity` 对应 SettlementManager、
`UT_RestrictedExecutionFraudVerifierParity` 对应 v3 结构性 root 重推导 fraud
verifier、`UT_GovernanceFraudVerifierParity` 对应仅审计用 v1/v2 结构 verifier、
`UT_MpcFraudProof_RealCrypto` 对应 Phase-C MPC 委员会 fraud verifier)。
链下漂移在单元测试期暴露,而非运行时。

40 迭代验证发现的状态树 Merkle 约定漂移正是这套模式抓到的:
`UT_KeyedStateMerkleTree_NeoClassicParity` 现已钉
`KeyedStateMerkleTree.ComputeRoot(pairs) == MerkleTree.ComputeRoot(HashEntry leaves)`
在 10 种基数(包括以前发散的奇数情形)下。

### 5. 端到端阶段缝合验证完整管线

ZKsync 的 `era-test-node` 起一条本地实链,跑脚本场景。neo4 在
`tests/Neo.L2.IntegrationTests/` 中有对应的集成测试:

- `UT_Mvp_Phase0_Sidechain` —— Phase-0 MVP(deposit → batch → withdraw)
- `UT_Mvp_Phase1_Cross_Component` —— Phase-1 NeoHub v0 + SharedBridge
- `UT_Mvp_Phase2_FullStack` —— Phase-2 batch settlement + Gateway 聚合
- `UT_Mvp_Phase3_OptimisticChallenge` —— Phase-3 挑战窗口 + 欺诈证明
- `UT_Mvp_AllPhases_FullStack` —— 全阶段缝合
- `UT_E2E_RealVM_FullStack` —— legacy NeoVM 兼容路径,验证状态根连续性
- `UT_E2E_CustomExecutor_FullStack` —— `Sample.CounterChainExecutor` 端到端
- `UT_E2E_AuditPipeline` —— `ChainAuditor` 对健康 + 损坏场景
- `UT_E2E_Persistence_FullStack` —— 4 个 RocksDB store 从一个根目录恢复
- `UT_E2E_L1RpcPollers_FullStack` —— RPC 轮询器组合
- `UT_E2E_L2MetricsPlugin_CompositionRoot` —— 每个仪表化组件 → 一个 sink

### 6. 真实密码学证明端到端验证

ZKsync 的 prover 测试 `#[ignore]` 门控完整真实证明流程,因为代价昂贵。
neo4 同样:`bridge/neo-zkvm-host/tests/end_to_end.rs` 有两个 `#[ignore]`
测试,演练真实 SP1 证明生成(~40s prove,~20s verify)与篡改哈希拒绝。
本地运行:

```bash
cd bridge/neo-zkvm-host
cargo test --release --tests -- --ignored
```

`bridge/neo-zkvm-gateway-host/tests/real_recursive_release_gate.rs` 另行生成并由 host
验证一份真实 recursive Gateway Groth16 proof，其 child 是真实 compressed batch proof。
完整运行成本显著高于普通 PR 测试，因此只在 schedule/manual gate 执行。
在 Apple Silicon 上应启用 crate 的 `native-gnark` feature，并把 `LIBCLANG_PATH` 指向
Homebrew LLVM 的 `lib` 目录。SP1 6.2.1 发布的 gnark 容器仅支持 amd64；上游原生 backend
可以避开 QEMU 运行时崩溃，且不会改变 proof system、circuit、VK 或 host 验证边界。

生产执行边界不是 ignored：CI 会构建 release `neo-zkvm-executor`，通过
`NEO_ZKVM_EXECUTOR` 传入精确路径，再运行聚焦 C# 测试，完成 Neo genesis bootstrap、
真实进程边界、native result 校验与状态提交。单元测试还证明 binary 被替换、请求 hash
伪造、output 畸形或进程失败时状态不会变化。

### 7. 跨语言线协议对等

ZKsync 把同一组字节向量通过他们的 Solidity verifier、Rust prover、TS SDK
都跑一遍。neo4 有对应的钉测试:

- `canonical_bytes_match_csharp_vector`(Rust watcher) —— 与 C#
  `Neo.L2.Messaging.ExternalMessageHasher` 字节级对等
- `message_hash_matches_csharp_vector`(Rust watcher) —— 哈希计算相同
- `external/foreign-contracts/eth/` 中的 44 个 Foundry 测试 —— 同一 Solidity
  router 在 14 个 EVM 链族 + 17 个主网槽间不变部署

---

## CI 集成

`.github/workflows/build.yml` 在每次 push + PR 运行仓库构建套件；
`.github/workflows/sdk-conformance.yml` 独立强制执行四语言共享 SDK 合约与
按需启用的真实节点矩阵：

1. `test` —— `dotnet test Neo.L2.sln`(完整当前 solution 清单；准确用例数由 runner 报告)
2. `contracts` —— 构建精确锁定的 `external/neo-devpack-dotnet` 编译器，动态发现
   全部 `NeoHub.*` 与 `Sample.*` 合约项目，校验每个 `.nef` +
   `.manifest.json` artifact；随后运行 `external/neo` 的
   `UT_L2NativeContracts` 来验证 10 个 Neo core L2 native contracts
3. `bridge` —— locked Rust build/tests，加 release `neo-zkvm-executor` 与真实聚焦
   C#→Rust native execution gate
4. `sp1-host` —— required 汇总门禁，名称为 `SP1 compatibility and manual release proof gate`。
   在 PR/`master` 上它只要求快速的 .NET、合约与 Rust lane，并断言昂贵矩阵保持 skipped。
   在 `workflow_dispatch` 上运行三条 `sp1-release-gates` lane（workspace release、terminal
   batch proof、recursive Gateway proof），汇总门禁要求每条成功且禁止 mock/dummy
5. `rust-audit` —— 按文档化可达性策略审查每个生产 Cargo lockfile
6. `sdk-typescript`、`sdk-python` 与 Rust/.NET SDK 门禁 —— 覆盖打包、类型、单元测试与共享向量
7. `foreign-evm` 与 `foreign-solana` —— 锁定依赖的外链合约测试
8. `experience-hub` —— 静态体验界面的 schema 与脱敏测试
9. `docs-site` —— `mdbook build` + 链接检查
10. `sdk-conformance` —— 强制离线 .NET/Rust/TypeScript/Python 一致性，以及凭据门控的
    真实 N3/N4 交易路径

每个 job 绿色之前 PR 不能合并。Dependabot 维持 cargo / NuGet / npm 依赖
更新。

---

## 如何添加新测试

| 被测代码种类 | 测试工程 | 模式 |
|---|---|---|
| 链下库 | `tests/Neo.L2.<Lib>.UnitTests/` | MSTest `[TestMethod]`,每个行为一个;每个边界 `[DataRow]` |
| 链上合约 verifier | `tests/Neo.L2.<Lib>.UnitTests/UT_<Contract>Parity.cs` | 在 C# 中复刻链上决策树;断言字节级线协议 |
| 线协议 encoder | `tests/Neo.L2.State.UnitTests/UT_WireFormat_Fuzz.cs` | 添加新 `[TestMethod]` + `[DataRow]` 种子;每种子 500 随机字节 |
| 桥 / 消息不变量 | `tests/Neo.L2.Bridge.UnitTests/UT_BridgeInvariants_PropertyBased.cs` | 200 ops × 4-8 种子的 seeded 随机游走;每步断言不变量 |
| 完整管线行为 | `tests/Neo.L2.IntegrationTests/UT_E2E_<scenario>.cs` | E2E,使用 `InMemoryKeyValueStore` + `AttestationProver` + `KeyedStateRootOracle` |
| 跨语言线协议 | Rust:`watchers/neo-bridge-watcher-*/src/messaging.rs` `#[cfg(test)]`;Foundry:`external/foreign-contracts/eth/test/` | 硬编码 `csharp_vector` 字节字面量;断言实现产出相同字节 |
| 真实 CPU ZK 证明 | `bridge/neo-zkvm-host/tests/end_to_end.rs` | `#[test]` + `#[ignore]` + `#[serial_test::serial]` |

---

## 参见

- [`zksync-comparison.md`](zksync-comparison.md) —— ZKsync Elastic Chain
  与 neo4 的组件级对应表(这些测试模式的来源)
- [`../../CONTRIBUTING.md`](../../CONTRIBUTING.md) —— 完整开发流程
- [`../../.github/PULL_REQUEST_TEMPLATE.md`](../../.github/PULL_REQUEST_TEMPLATE.md) ——
  合并前清单(合约变更时跑对等测试,等等)
