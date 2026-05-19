# 测试方法论 —— Neo Elastic Network

`neo4` 采用的测试方法。镜像 ZKsync 的 Foundry + Hardhat + 集成测试栈,
按系统每一片所在的位置翻译到 .NET / Rust / TypeScript / Solidity。

---

## 测试面(1453 .NET + 166 跨语言 + 2 SP1 发布关口)

| 层 | 框架 | 位置 | 内容 |
|---|---|---|---|
| 单元 | MSTest(xUnit 风) | `tests/Neo.L2.*.UnitTests/`(34 个工程) | 每类不变量、边界、空参 + 空字段守卫、指标 emission 钉 |
| 集成 | MSTest | `tests/Neo.L2.IntegrationTests/` | E2E 分阶段缝合(Phase 0 → 5)、审计管线、持久化恢复、NeoVM2/RISC-V 接缝、legacy NeoVM 兼容、自定义 executor 全栈 |
| 属性 / 不变量 | MSTest + seeded `System.Random` | `UT_BridgeInvariants_PropertyBased.cs`(17 测试) | 200 次操作的随机序列 × 4-8 种子 —— 每个不变量 1600-3200 次状态转移。在每个中间态断言桥账面 + nonce 唯一性 + 双向注册表的不变量 |
| 模糊 | MSTest + seeded `System.Random` | `UT_WireFormat_Fuzz.cs`(19 测试) | 随机字节序列喂给每个 decoder —— 必须 round-trip 或以类型化异常拒绝,永不崩溃 |
| 跨语言对等 | byte-vector 钉 + canonical-bytes-match-csharp | Rust watcher 测试 + Foundry 测试 | C# encoder + Rust + Solidity verifier 间字节相同 |
| 链上↔链下对等 | C# 复刻链上决策树 | `UT_OnChainMerkleVerifyParity`、`UT_RestrictedExecutionFraudVerifierParity`、`UT_GovernanceFraudVerifierParity`、`UT_MpcFraudProof_RealCrypto` | 链下算法复刻链上 verifier 并产出相同根 / 决策;漂移在单元测试期就暴露 |
| Foundry | Solidity 不变量 + 多链 | `external/foreign-contracts/eth/test/`(21 测试) | EVM-family Solidity router —— 14 个单链 + 7 个多链,固定 17 个主网槽的每实例状态隔离 |
| 真实 CPU SP1 prover | Rust `#[ignore]` | `bridge/neo-zkvm-host/tests/end_to_end.rs`(2 测试) | 真实 ZK 证明生成(~40s prove,~20s verify,2.78 MB 证明)+ 篡改哈希拒绝负向测试 |
| Live-RPC | Rust `--features live-rpc` | `watchers/neo-bridge-watcher-eth/tests/`(55 测试) | 进程内 `FakeRpcServer` —— 通过真实 `reqwest::blocking` HTTP 周期演练 `EthRpcEventSource`+`NeoRpcSubmitter` |
| TS SDK | vitest | `sdk/typescript/`(15 测试) | RPC client 表面;.NET / Rust / TS 错误分类对等 |
| Rust SDK | cargo test + mockito | `sdk/rust/`(10 测试) | RPC client;与 TS + .NET 表面相同 |
| execution-core | cargo test | `bridge/neo-execution-core/`(5 测试) | 后端无关的批次解析、receipt/state 折叠、Merkle 确定性、后端依赖守卫 |
| zkvm-guest | cargo test | `bridge/neo-zkvm-guest/`(7 测试) | 通过共享 batch core 在主机模式执行 Neo N3 VM |

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
`UT_RestrictedExecutionFraudVerifierParity` 对应 v3 trustless fraud
verifier、`UT_GovernanceFraudVerifierParity` 对应 v1/v2 治理仲裁 verifier、
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

### 7. 跨语言线协议对等

ZKsync 把同一组字节向量通过他们的 Solidity verifier、Rust prover、TS SDK
都跑一遍。neo4 有对应的钉测试:

- `canonical_bytes_match_csharp_vector`(Rust watcher) —— 与 C#
  `Neo.L2.Messaging.ExternalMessageHasher` 字节级对等
- `message_hash_matches_csharp_vector`(Rust watcher) —— 哈希计算相同
- `external/foreign-contracts/eth/` 中的 21 个 Foundry 测试 —— 同一 Solidity
  router 在 14 个 EVM 链族 + 17 个主网槽间不变部署

---

## CI 集成

`.github/workflows/build.yml` 在每次 push + PR 跑完整套件:

1. `test` —— `dotnet test Neo.L2.sln`(1453 测试 / 34 工程)
2. `contracts` —— 安装 `Neo.Compiler.CSharp`,类型检查 23 个 `NeoHub.*`
   deployable 合约加 2 个 `Sample.*` 合约,断言 25 个 `.nef` + 25 个
   `.manifest.json` artifact;随后运行 `external/neo` 的
   `UT_L2NativeContracts` 来验证 10 个 Neo core L2 native contracts
3. `bridge` —— Rust workspace 的 `cargo check`
4. `neo-zkvm-host` —— `cargo build` + 非 ignored 测试(2 个真实 CPU
   ignored 测试在 nightly 运行,不在 per-PR)
5. `sdk-typescript` —— `npx vitest run`(15 测试)
6. `foreign-evm` —— `forge test`(20 个 Solidity 测试)
7. `docs-site` —— `mdbook build` + 链接检查

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
