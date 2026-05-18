# 计划:ApplicationEngine 接入 + 真实状态根批次执行器

> 把 `IMPLEMENTATION_STATUS.md` 里剩下的两行 "Reference / scaffolding" 替换成真正的
> 生产实现。

## 我们在替换什么

- **`ITransactionExecutor`**
  - *当下:* `ReferenceTransactionExecutor` —— 罐头收据。
  - *当时目标:* `ApplicationEngineTransactionExecutor` —— 通过 Neo 的
    `ApplicationEngine` 跑 legacy NeoVM。当前 Neo N4 L2 的标准目标是
    `RiscVTransactionExecutor` / NeoVM2-RISC-V；本文保留为 legacy NeoVM
    兼容路径的工作记录。
- **`IL2BatchExecutor`**
  - *当下:* `ReferenceBatchExecutor` —— 占位的 post-state 根。
  - *目标:* `MerkleStateBatchExecutor` —— 真正的密码学状态根。

两个接口都已经存在;两个都已有进程内 devnet 级别的 `Reference*` 实现。本计划要把
每一个变成真正的生产版本。

## 约束检查

`external/neo/src/Neo/SmartContract/ApplicationEngine.cs` 在引入的 submodule 里完整存
在 —— VM 接入所需的一切都已就位。

`external/neo/src/Neo/Persistence/DataCache.cs` 是带 7 个 `protected abstract` 方法
的抽象类 —— 我们可以在现有 `IL2KeyValueStore` 之上实现它。

引入的 Neo core **不**自带 Merkle Patricia Trie。这没关系:真正的生产 L2(ZKsync、
Polygon zkEVM、Optimism)用的是**对 (key, value) 对按键排序的二叉 Merkle 树**,而
不是 MPT。MPT 是以太坊主网的优化。我们会在 `Neo.L2.State` 里建二叉 Merkle 变体
—— 同样的密码学保证,验证更简单。

## 阶段(每一个都是干净、可合入的 PR)

### 阶段 A —— ApplicationEngine 后备的交易执行器

**A1. `L2DataCacheAdapter`**(`Neo.L2.Persistence` 新文件)
- 在 `IL2KeyValueStore` 之上实现 Neo 的抽象 `DataCache`。
- 7 个 protected 方法(`AddInternal`、`DeleteInternal`、`ContainsInternal`、
  `GetInternal`、`SeekInternal`、`TryGetInternal`、`UpdateInternal`)翻译成 KV 操作。
- `SeekInternal` 需要按前缀有序扫描 —— 现有每个 `IL2KeyValueStore` 实现都有
  `EnumeratePrefix(prefix)`,我们做包装。
- 测试:happy-path put/get/delete/seek;经抽象基类的 commit-track 机制做往返。

**A2. `L2TransactionContainer`**(`Neo.L2.Executor` 新文件)
- 实现 Neo 的 `IVerifiable` 接口,暴露 L2 交易的哈希 + 签名,供 ApplicationEngine
  内部 witness 验证。
- 把现有 L2 tx 模型映射到 Neo 的 `Transaction`。

**A3. `ApplicationEngineTransactionExecutor`**(`Neo.L2.Executor` 新文件)
- 实现 `ITransactionExecutor.Execute(L2Transaction, IL2KeyValueStore)`。
- 用 `L2DataCacheAdapter` 包装 KV store;构造 dummy `Block`(timestamp = 批次首块
  时间, index = 批次首块高度);调用 `ApplicationEngine.Run(script, cache, container)`。
- 捕获引擎的 `State`(HALT/FAULT)、`GasConsumed`、`ResultStack` 和 `Notifications`。
- 产出真实的 `TransactionReceipt`,包含状态、gas、事件日志。

**A4. 测试**(新 `tests/Neo.L2.Executor.UnitTests/UT_ApplicationEngineTransactionExecutor.cs`)
- Happy path:trivial 脚本(例如 `PUSH1 RET`)跑到 HALT,返回 1。
- FAULT path:抛错的脚本 → receipt status = Failed,无状态变更。
- Gas 超限:无限循环脚本撞上 gas 上限,FAULT。
- Notifications:发出 notification 的脚本,在 receipt 里被捕获。

### 阶段 B —— 真正的状态根批次执行器

**B1. `KeyedStateMerkleTree`**(`Neo.L2.State` 新文件)
- 在按键排序的 `(key, value)` 对上构建二叉 Merkle 树。
- 根 = `Hash256(left ‖ right)` 递归,带单叶树边界处理。
- Inclusion 证明:每叶 sibling 列表(复用本 session 出货的
  `MerklePathRoundProver` 验证约定)。
- 测试:同一集合 → 确定性根;单字段编辑 → 根变化;1..16 各尺寸下每片叶子的
  inclusion-proof 往返。

**B2. `MerkleStateBatchExecutor`**(`Neo.L2.Executor` 新文件)
- 实现 `IL2BatchExecutor`。
- 对每笔交易:
  1. 快照 `IL2KeyValueStore` 的 pre-state。
  2. 经 `ApplicationEngineTransactionExecutor` 跑。
  3. 把 data-cache 变更提交到底层 KV store。
- 所有交易跑完后:
  1. 枚举 KV store 的全量 key 集合。
  2. 在 `(key, value)` 对上构建 `KeyedStateMerkleTree`。
  3. 返回它的根作为 `postStateRoot`。
- 可重现:同一起点 KV state + 同一组交易 → 字节相同的根(这是证明目标的不变量)。

**B3. 测试**(新 `tests/Neo.L2.Executor.UnitTests/UT_MerkleStateBatchExecutor.cs`)
- 空批次:`postStateRoot == preStateRoot`(无状态变更)。
- 单交易:应用后根改变。
- Replay:同一组交易跑两遍 → 同一最终根。
- 确定性:同一批次序列两次独立跑,产出字节相同的根。

### 阶段 C —— 集成

**C0. NeoVM genesis bootstrap helper** ✅ —— `src/Neo.L2.Executor/NeoVMGenesisBootstrap.cs`
- 不用 Akka actor,复刻 `NeoSystem.Blockchain.Initialize` 中相关切片。在
  `L2DataCacheAdapter` 上跑 `OnPersist` + `PostPersist` 脚本。能干净地编译 + 执行,
  把写入透传到底层 KV store。
- 早先关于 "cache 透传 gap" 的诊断是错的:写入**确实**经 Neo 标准 child-cache
  `Commit()` 链透传出去。真正的 bug 是 `IsInitialized` 误报(gas=0 时
  ApplicationEngine.Create 在读 PolicyContract 之前就短路了 → 在空 store 上返回 true)。
  修复:直接对 PolicyContract 的 ExecFeeFactor key 做存储探测。
- **端到端已验证**:`BootstrappedStore_RunsRealNeoVMScript_HALT` 钉死在 `Run()`
  之后,`ApplicationEngineTransactionExecutor` 经 Neo VM 跑真实 PUSH1 脚本并拿到
  Success receipt。

**C1. Devnet 接线** —— `tools/Neo.L2.Devnet`
- 加 `--executor neovm` 标志,把 `ReferenceTransactionExecutor` 切到
  `ApplicationEngineTransactionExecutor`,仅用于 legacy 兼容。
- 加 `--executor riscv` / `--executor neovm2-riscv`,作为 Neo N4 L2 的
  标准 NeoVM2/RISC-V 路径。
- 加 `--state-root merkle` 标志,把 `ReferenceBatchExecutor` 切到
  `MerkleStateBatchExecutor`。
- 两个标志互不依赖 —— 运维者可以混搭。

**C2. 端到端集成测试**
- 新 `tests/Neo.L2.IntegrationTests/UT_E2E_RealVM_FullStack.cs`。
- 接上 `ApplicationEngineTransactionExecutor` + `MerkleStateBatchExecutor`
  + 现有 `BatchBuilder` 流水线 + 现有审计流水线。
- 用真实 Neo-VM 脚本执行驱动 5 个批次。
- 断言:状态根连续(每个批次的 preStateRoot == 上一个批次的 postStateRoot)、
  审计流水线接受每个批次、happy-path 中无 FAULT 状态交易。

## 沙箱内可验证的部分

✅ 干净编译。
✅ 单元测试通过(happy-path、FAULT-path、gas 超限)。
✅ 端到端集成测试通过(经真实 VM 的状态根连续性)。
🔴 真实世界 gas 精度 / 主网合约兼容性 —— 需要真正部署的合约套件来测,超出此沙箱
   范围。

## 范围外

- **Witness/签名验证** —— ApplicationEngine 处理这部分,但 L2 交易的 witness 格式
  是规格里的另一块(目前临时拼凑)。阶段 A3 接通引擎;跨所有签名方案的全面 witness
  验证是后续工作。
- **跨分片状态读** —— L2 执行期间与 NeoHub L1 合约的互操作。框架已经有 L1 RPC
  poller;把它们接进 ApplicationEngine 的 interop service 是另一个阶段。
- **经 Sample.CounterChainExecutor 的自定义执行器** —— 已经出货;新的
  `ApplicationEngineTransactionExecutor` 现在是 legacy NeoVM 兼容路径；
  Neo N4 L2 默认走 NeoVM2/RISC-V。

## LOC 估算

| 阶段 | 源 LOC | 测试 LOC |
|------|-------:|---------:|
| A1 | ~150 | ~100 |
| A2 | ~80 | ~40 |
| A3 | ~250 | ~200 |
| A4(只是测试) | 0 | (并入 A3 计) |
| B1 | ~180 | ~150 |
| B2 | ~200 | ~150 |
| B3(只是测试) | 0 | (并入 B2 计) |
| C1 | ~50 | (并入 C2 计) |
| C2 | 0 | ~250 |
| **合计** | **~910** | **~890** |

## 执行顺序

严格 A1 → A2 → A3 → A4 → B1 → B2 → B3 → C1 → C2。

每个阶段单独 commit / PR。A1–A4 可独立于 B 出货(新交易执行器在现有
`ReferenceBatchExecutor` 上工作正常)。B 可独立于 C 出货。

## 风险

1. **`DataCache` 快照语义**:抽象基类有微妙的"未提交变更 vs 已提交"行为。这里
   出错 = 状态根不匹配,只在可重现性测试中暴露。
   缓解:A1 测试包括显式 commit-后-重读 用例。

2. **`Block` 构造**:ApplicationEngine 需要一个有合理 index/timestamp/witness 的
   "persisting block"。我们在 A3 构造一个 dummy;如果 Neo VM 期望某些字段被填,
   跑起来会以非显然方式 FAULT。
   缓解:从最简单的脚本(PUSH1)起步,逐步加复杂度。

3. **Merkle 树键序**:`IL2KeyValueStore.EnumeratePrefix` 的顺序必须匹配
   `KeyedStateMerkleTree` 的预期(按 key 字节字典序)。
   缓解:B1 第一条测试就显式断言已知输入下的顺序。

4. **Gas 计费**:Neo 默认 `TestModeGas` 巨大;生产链会想要按 tx 的 gas 预算。
   A3 接受按 tx 的 gas-limit 参数。同时测有界 + 无界两种模式。
