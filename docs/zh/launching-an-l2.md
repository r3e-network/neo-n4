# 在 Neo Elastic Network 上线一条新 L2 链

本指南把运维者从 `git clone` 一路带到一条已注册、产出批次的 L2 链。同时记录每个
"插入点" —— 不分叉框架就能定制按链特有的逻辑。

框架把每条 L2 视作独立执行内核 + 统一 NeoHub 注册。自定义链在*配置*(chain id、
DA 模式、proof type、排序器模型)和*注入组件*(交易执行器、DA writer、证明者、
排序器源)上有差异。其它一切 —— 结算协议、消息路由、提款验证 —— 都共享。

---

## 最快路径:`new-l2` 组合命令

```bash
# 单条命令:生成 chain.config.json,初始化节点工作目录(data/ logs/ Plugins/),
# 并脚手架出一个自定义执行器项目(csproj + 执行器骨架 + 状态接缝 + tx builder
# + KeyedStateStore 适配器 + README)以及一个并列的 MSTest 项目,带 3 条起步测试。
neo-stack new-l2 --name MyChain --chain-id 1099 --template rollup --output ./my-l2

# 组合命令跑完后,运维侧的 "Next" 输出会指向:
#   1. 对执行器脚手架做 dotnet build + dotnet test
#   2. 用 neo-stack validate 给 chain.config.json 做健全性检查
#   3. dotnet run --project tools/Neo.L2.Devnet -- 5 --config ./my-l2/chain.config.json
#   4. 编辑 MyChainExecutor.cs 把占位 NoOp 替换为真实 opcode
```

要最简单的、可构建 + 可测试 + 可在 devnet 预览的起步路径,用[`new-l2`
组合命令](#最快路径new-l2-组合命令)。要细粒度控制(例如跳过给用
`ReferenceTransactionExecutor` 的链做执行器脚手架),用下面的
[5 命令路径](#快速路径5-条命令拉起一条-l2)。

## 快速路径:5 条命令拉起一条 L2

```bash
# 1. 从模板生成 config(rollup / zk-rollup / validium / sidechain)。
neo-stack create-chain --chain-id 1099 --template rollup --output ./my-l2

# 2. 初始化节点工作目录(data/ logs/ Plugins/)。
neo-stack init-l2 --chain-id 1099 --output ./my-l2

# 3. 打印 L1 注册计划(在许可制准入阶段或治理批准的半许可 / 无许可模式下跑)。
#    不带 --operator/--verifier/--bridge/--message:仅打印计划。
#    带这四个 UInt160 哈希(从 neo-hub-deploy bundle 拿到):
#    输出可粘贴进钱包的规范 91 字节 configBytes hex。
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <hash> --verifier <hash> --bridge <hash> --message <hash>

# 4. 打印 bridge adapter 部署计划(每条新链一次性)。
neo-stack deploy-bridge-adapter --chain-id 1099 --output ./my-l2

# 5. 跑排序器 + 批处理器 + 证明者。每个子命令打印 preflight 检查,
#    在链可接受交易时退出 0。
neo-stack start-sequencer --chain-id 1099 --output ./my-l2 &
neo-stack start-batcher  --chain-id 1099 --output ./my-l2 &
neo-stack start-prover   --chain-id 1099 --output ./my-l2 &
```

钱包把关步骤(#3、#4 和 `submit-batch`)打印结构化运维计划 —— 目标合约、参数、
已签 tx 模板、按编号的下一步 —— 而不是自动签名。运维者把计划喂给自己挑的钱包
(NEP-6 keystore、Ledger 等)。

不依赖 L1 的进程内完整 demo,见 `tools/Neo.L2.Devnet`:

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5
# 5 个批次端到端,真实 KeyedStateStore 连续性,跑后 audit 通过

# 或端到端预览你的运维模板 config(跑后 RPC 快照的 getsecuritylabel 反映 JSON
# 中的 §16.2 维度):
dotnet run --project tools/Neo.L2.Devnet -- 5 --config ./my-l2/chain.config.json

# 把任何东西打到 L1 之前,做 JSON 健全性检查:
neo-stack validate ./my-l2/chain.config.json
# ✅ valid: chainId=1099 securityLevel=Optimistic daMode=NeoFS ...
# (或 ❌ 直接指向出错的字段)
```

---

## 加自定义链逻辑(可选)

大多数 L2 链会定制交易在自身上的执行 —— 游戏 rollup 想要快速 counter 自增,
交易所 validium 想要 orderbook 操作,隐私侧链想要证明验证 opcode。框架的接缝是
[`ITransactionExecutor`](../../src/Neo.L2.Executor/ITransactionExecutor.cs) ——
单一方法,按 [`SPEC.md`](../../src/Neo.L2.Executor/SPEC.md) 确定性,接进标准流水线
让封装 / 证明 / 结算 / 欺诈证明都开箱即用。

```bash
# 1. 脚手架一个起步自定义执行器项目(csproj + 执行器骨架 + 状态接缝 + tx
#    builder + KeyedStateStore 适配器 + README,一次到位)。加 --with-tests
#    也输出一个并列 tests 项目,钉死占位 opcode(3 条起步测试:NoOp 成功 +
#    空 tx 失败 + 未知 opcode 失败)。
neo-stack scaffold-executor --name MyChain --chain-id 1099 --with-tests

# 输出(默认 ./samples/executors/MyChainExecutor):
#   MyChainExecutor.csproj
#   MyChainExecutor.cs              ← ITransactionExecutor 带 NoOp 占位
#   IMyChainState.cs                ← 状态接缝 + InMemory 实现
#   MyChainTxBuilder.cs             ← 规范 tx 字节构造器
#   MyChainKeyedStateStoreAdapter.cs← 接到 KeyedStateStore 的生产桥
#   README.md                       ← 5 步定制 checklist
#
# 带 --with-tests,还会有:./samples/executors/MyChainExecutor.UnitTests/

# 2. 脚手架开箱编译 + 测试通过。构建 + 测试:
dotnet build samples/executors/MyChainExecutor /p:NuGetAudit=false
dotnet test  samples/executors/MyChainExecutor.UnitTests /p:NuGetAudit=false

# 3. 编辑 MyChainExecutor.cs —— 把 Opcode.NoOp 替换为你链上的 opcode
#    (IncrementCounter、EmitWithdrawal、EmitMessage、AppSpecificOp、…)。
#    每个 opcode 是偏移 0 处的一字节;其它是 opcode 特有的 body。加 opcode
#    时同步在 UT_MyChainExecutor.cs 中加测试。
```

可作为"真实"自定义执行器参照的工作样例:
[`samples/executors/Sample.CounterChainExecutor`](../../samples/executors/Sample.CounterChainExecutor) ——
3 个 opcode(IncrementCounter / EmitWithdrawal / EmitMessage),按 sender 状态变更、
withdrawal 发出、经规范 `MessageBuilder.Build` 做 L2→L2 消息传递,完整 SPEC.md
确定性。已经过 `ReferenceBatchExecutor` + `KeyedStateRootOracle` + 多签证明者/
验证器在
[`tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs`](../../tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs)
做端到端测试。

脚手架的 `KeyedStateStoreAdapter` 是让你执行器的写入参与 post-state-root oracle
的桥。接好之后,`BatchExecutionResult.PostStateRoot` 反映真实的状态变更(不是
receipt root 的合成 XOR)—— 与直接 `KeyedStateStore` 写入的对等性钉死见
[`UT_KeyedStateStoreAdapter.cs`](../../tests/Sample.CounterChainExecutor.UnitTests/UT_KeyedStateStoreAdapter.cs)。

要看一个自定义执行器跑完整 devnet 流水线(充值 + 状态变更 + receipts + 提款 +
DA + 证明 + 验证 + audit):

```bash
# 在 Sample.CounterChainExecutor 接好的进程内 devnet 跑。每个批次除了照常的
# 一笔充值,还加 3 笔 Counter tx,演练 IncrementCounter(状态变更)、
# EmitWithdrawal(提款通道)、EmitMessage(L2→L2 跨链通道)。
dotnet run --project tools/Neo.L2.Devnet -- 5 --executor counter

# 关注每批次的 "[exec]" 行 —— gas + txRoot + L2-to-L2 root 都来自 Counter
# 执行器的实际输出。跑后 RPC 快照的 "state entries" 计数包含 Counter 写入
# 加上充值带来的 bridge 余额。
```

---

## 模板

`neo-stack create-chain --template <name>` 在四个起步点中选一个。每个写入不同的
`chain.config.json`(按 `doc.md` §6 + §16.2 设置 chainMode + daMode + proofType +
安全标签):

| 模板         | chainMode        | daMode    | proofType  | SecurityLevel | Exit             |
|--------------|------------------|-----------|------------|---------------|------------------|
| `rollup`     | L2RollupMode     | L1        | Optimistic | Optimistic    | Delayed          |
| `zk-rollup`  | L2RollupMode     | L1        | Zk         | Validity      | Permissionless   |
| `validium`   | L2ValidiumMode   | NeoFS     | Zk         | Validium      | Delayed          |
| `sidechain`  | SidechainMode    | External  | None       | Sidechain     | Permissionless   |

所有模板默认 `sequencerModel: DbftCommittee`(Neo 原生单块终结性)。所有都可在
`create-chain` 之后编辑 —— JSON 是运维者的财产。

覆盖不同用例(通用 DeFi rollup / 游戏链 / DEX validium / 隐私侧链)的开箱即跑
样例 config 见 [`samples/`](../../samples/README.md)。每份样例都通过
`neo-l2-devnet --config samples/<name>.config.json` 端到端验证。

### 乐观 rollup 运维者:接好 fraud verifier

`rollup` 模板的链跑 `proofType: Optimistic`,意味着 `NeoHub.OptimisticChallenge`
强制一个挑战窗口,期间任何方都可以提交欺诈证明。经
`Challenge(chainId, batchNumber, challenger, fraudProofBytes, fraudVerifier)`
提交,把实际密码学校验委派给由 `fraudVerifier` 参数指定的合约。

3 条路径,都在默认 `neo-hub-deploy plan` 的 23 步 bundle 中:

  1. **治理仲裁模式**(最简单的运维友好路径):部署
     `NeoHub.GovernanceFraudVerifier`。它对规范 `FraudProofPayload` 做结构性
     校验(v1=101 字节定长,或 v2=105+N 字节带 disputed-tx witness;长度 / 版本 /
     是否声明真实差异)并发出 accept/reject 事件交安全委员会仲裁。提挑战时把
     它的部署哈希作 `fraudVerifier` 传入。
  2. **无信任 v3 模式**(无委员会仲裁):部署
     `NeoHub.RestrictedExecutionFraudVerifier`。它在链上从每个
     `FraudProofPayload` v3 storage 证明(leaf-hash + Merkle siblings + leafIndex)
     重新派生 pre/post 状态根,并与 v1 头的 `PreStateRoot` 与
     `ReplayedPostStateRoot` 比对。一份能干净重建 + 声明真实差异的 v3 payload
     被自动接受。挑战者在链下生成 v3 payload(参照实现 + 与链上逻辑的对等性
     测试见 `Neo.L2.Challenge.V3StorageProofVerifier`)。
  3. **自定义 verifier**:出货你自己的 fraud verifier(例如在 L1 上以受限状态
     重新执行被争议交易的)。在 deploy bundle 中跳过两个参照 verifier,改注册
     你自己 verifier 的哈希。`FraudProofPayload` v2(DisputedTxBytes)和 v3
     (StorageProofs)线协议字段携带重新执行 verifier 所需的被争议 tx 字节 +
     storage manifest。

`neo-hub-deploy` 的 post-deploy actions 输出会浮现每个 verifier 的对应哈希,
让运维者知道哪个该作为 `fraudVerifier` 参数传入:

```
# 注:对 v1/v2 fraud proof(治理仲裁),传 GovernanceFraudVerifier.Hash …
# 注:对 v3 fraud proof(无信任 storage-proof 重新派生),传
#     RestrictedExecutionFraudVerifier.Hash …
```

---

## 架构:自定义逻辑插入哪里

框架的扩展面是一组接口,每条 L2 把自己的实现接到这些接口。样例接线在
`tools/Neo.L2.Devnet/Program.cs`;生产部署在同样的调用点替换为自家类。

```
┌─────────────────────────────────────────────────────────────────┐
│ Settlement(NeoHub)            —— 运维共享、不可变               │
│   ChainRegistry · SharedBridge · SettlementManager · ...        │
└──────────────────┬──────────────────────────────────────────────┘
                   │  IL2BatchExecutor.ApplyBatchAsync
                   │  ITransactionExecutor.ExecuteAsync
                   │  IL2Prover.ProveAsync       ──► IL2ProofVerifier
                   │  IDAWriter.PublishAsync     ──► IDAWriter.IsAvailableAsync
                   │  ISequencerCommitteeProvider.GetActiveCommitteeAsync
                   │  IForcedInclusionSource.DequeueOverdueAsync
                   │  IL2Metrics.IncrementCounter / RecordHistogram / SetGauge
                   ▼
┌─────────────────────────────────────────────────────────────────┐
│ 按 L2 的插入点(你来实现 / 配置)                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 运维者常定制的 5 个扩展点

- **`ITransactionExecutor`** —— 默认:`ReferenceTransactionExecutor`。
  替换以加领域特定 opcode;`neo-stack scaffold-executor` 输出起步,
  [`Sample.CounterChainExecutor`](../../samples/executors/Sample.CounterChainExecutor)
  是工作参照。
- **`IL2Prover`** / **`IL2ProofVerifier`** —— 默认:Multisig /
  Optimistic / Mock-RiscV。替换以走 Stage 2(ZK 有效性):
  `prove-batch daemon`(进程外 Rust 证明者,在
  `bridge/neo-zkvm-host/`)。
- **`IDAWriter`** —— 默认:NeoFS(`NeoFsLikeDAWriter`),或在设置
  `--data-dir` 时使用持久化 NeoFS-like 存储;`External` 只用
  `InMemoryDAWriter` 支撑测试/演示。可替换为真实 NeoFS SDK /
  L1 `sendrawtransaction`。
- **`ISequencerCommitteeProvider`** —— 默认:
  `InMemorySequencerCommitteeProvider`。替换以接到 neo 的
  `DBFTPlugin` 共识选择器。
- **`IRoundProver`**(仅 Phase 5)—— 默认:`PassThroughRoundProver`。
  替换为 SP1 Compress / Halo2 accumulator / Risc0 fold。

以上都接受 ctor 注入。插件 host 是单一组合根;`Neo.Plugins.L2Metrics.L2MetricsPlugin`
是规范模式 —— "插件 A 暴露 sink,插件 B 经 `WithMetrics(plugin.Metrics)` 读取"。

### 具体定制食谱

```csharp
// 1. 构建你的自定义交易执行器。要么 fork 工作样例
//    (samples/executors/Sample.CounterChainExecutor 已经有 3 个真 opcode),
//    要么脚手架一个新的:
//
//      neo-stack scaffold-executor --name MyChain --chain-id 1099
//
//    N4 的标准路径应接 RiscVTransactionExecutor / NeoVM2-RISC-V。
//    ApplicationEngine 只作为 legacy NeoVM 兼容路径。
var stateStore = new KeyedStateStore();              // 生产:RocksDB 后备
var stateAdapter = new MyChainKeyedStateStoreAdapter(stateStore);
var myExecutor = new MyChainExecutor(
    chainId: 1099,
    state: stateAdapter,                              // 执行器写入流入…
    emittingContract: emittingContractHash);

// 2. 接到插件 host 实例化的批次执行器。
var keyedStateOracle = new KeyedStateRootOracle(stateStore);  // …oracle 哈希的同一个 store
var batchExecutor = new ReferenceBatchExecutor(
    txExecutor: myExecutor,                          // ← 注入
    postStateRootOracle: keyedStateOracle,
    l1Processor: depositProcessor);

// 3. 注入你的 DA writer(例如你写的真实 NeoFS SDK 适配器)。
plugin.WithWriter(new MyNeoFsAdapter(neoFsClient));

// 4. 接好 metrics,让所有自定义组件经同一 sink 发指标。
plugin.WithMetrics(metricsPlugin.Metrics);
```

每个插入点形态相同:`Neo.L2.Abstractions` 中的接口,`Neo.L2.*`(in-memory / mock)
中的默认实现,插件上的注入钩子(`WithWriter`、`WithMetrics`、ctor 参数)。

完整自定义执行器流水线(精确接线形态)的端到端测试:
[`tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs`](../../tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs)。

### 实操样例:写一个自定义 `IDAWriter`

要支持新 DA 层(例如 Celestia、Avail,或自家链下 blob 服务),实现 `IDAWriter`
并把它传给 `L2DAPlugin.WithWriter()`。最小可行实现的解剖:

```csharp
using Neo.L2;

public sealed class CelestiaLikeDAWriter : IDAWriter
{
    private readonly ICelestiaClient _client;

    public CelestiaLikeDAWriter(ICelestiaClient client) => _client = client;

    // 选与你层级匹配的 DAMode 判别码(External=2 是按 Neo.L2.DAMode 给通用第三方
    // DA 层用的)。
    public DAMode Mode => DAMode.External;

    public async ValueTask<DAReceipt> PublishAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. 把 payload 提交给 DA 层;捕获该层的指针
        //    (Celestia:namespace + height + index;NeoFS:container+object id)。
        var pointer = await _client.SubmitBlobAsync(
            request.Payload, cancellationToken);

        // 2. 计算跨层承诺。一律用 Hash256(payload),让 DAAvailabilityCheck 能
        //    跨层比对,无需知道底层原生承诺方案。
        var commitment = Crypto.Hash256(request.Payload.Span);

        // 3. 返回 receipt —— Pointer 必须能经你层的 "按指针获取" RPC 往返。
        return new DAReceipt
        {
            Commitment = new UInt256(commitment),
            Layer = Mode,
            Pointer = pointer.AsMemory(),
        };
    }

    public async ValueTask<bool> IsAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        // 你层的 "是否仍可获取" 检查。许多层暴露 HEAD 风格端点,blob 仍 pin
        // 时返 200。
        return await _client.HeadAsync(receipt.Pointer, cancellationToken);
    }
}
```

然后在插件 host 接好:

```csharp
var dapw = new L2DAPlugin();
dapw.WithWriter(new CelestiaLikeDAWriter(myCelestiaClient));
dapw.WithMetrics(metricsPlugin.Metrics);  // 发出 l2.da.published / l2.da.errors
```

框架的 `MetricsEmittingDAWriter`(`WithMetrics` 调用时自动组合)会包装你传入的
任何 writer,所以你的自定义层与内置 writer 享有同样的可观测性,不用额外接线。

按这一形态实现的参照:
- `Neo.Plugins.L2DA.InMemoryDAWriter` —— 最简单
- `Neo.Plugins.L2DA.NeoFsLikeDAWriter` —— 内容寻址 blob store
- `Neo.Plugins.L2DA.JsonRpcL1DAWriter` —— 提交到 L1 NEP-17 风格合约
- `Neo.Plugins.L2DA.PersistentDAWriter` —— RocksDB 后备本地 store
- `Neo.Plugins.L2DA.CommitteeAttestedDAWriter` —— DAC 委员会多签

### 实操样例:写一个自定义 `ISequencerCommitteeProvider`

如果你的链用非 dBFT 排序器模型(中心化、PoS 轮转、oracle 选定等),实现
`ISequencerCommitteeProvider`,把你的选择逻辑喂给 L2 节点:

```csharp
using Neo.Cryptography.ECC;
using Neo.L2.Sequencer;

public sealed class StakeWeightedSequencerProvider : ISequencerCommitteeProvider
{
    private readonly IStakeOracle _stakes;
    private readonly int _maxSize;

    public uint ChainId { get; }

    public StakeWeightedSequencerProvider(uint chainId, IStakeOracle stakes, int maxSize)
    {
        ChainId = chainId;
        _stakes = stakes;
        _maxSize = maxSize;
    }

    public async ValueTask<IReadOnlyList<CommitteeMember>> GetActiveCommitteeAsync(
        CancellationToken cancellationToken = default)
    {
        // 从你的 stake oracle 拉当前前 N 名质押者。框架不关心你**怎么**选 ——
        // 它只期望一份 CommitteeMember 列表,每个带 PublicKey + L1Address +
        // Status(1=Active)+ ExitsAt。
        var top = await _stakes.GetTopByStakeAsync(_maxSize, cancellationToken);
        return top.Select(s => new CommitteeMember
        {
            PublicKey = s.PublicKey,
            L1Address = s.L1Address,
            Status = 1,                 // Active
            ExitsAtUnixSeconds = 0,     // 活跃成员无退出窗口
        }).ToList();
    }

    public ValueTask<int> GetMaxCommitteeSizeAsync(CancellationToken cancellationToken = default)
        => new ValueTask<int>(_maxSize);

    public ValueTask<bool> IsRegisteredAsync(ECPoint sequencerKey,
        CancellationToken cancellationToken = default)
        => _stakes.HasStakeAsync(sequencerKey, cancellationToken);
}
```

在持有排序器引用的组件里接好(通常是 L2 节点的共识选择器 —— `Neo.L2.Sequencer`
中现有的 `InMemorySequencerCommitteeProvider` 展示了你的自定义 provider 在需要
重启幸存时可遵循的、生产就绪的持久化 + 生命周期模式)。

L2 节点的 dBFT 插件在每轮之前 poll 此接口,所以换 provider 是切排序器模型唯一
链上可见步骤 —— NeoHub 的 `SequencerRegistry` 仍跟踪*谁注册*,但*选择策略*由 L2
决定。

### 实操样例:写一个自定义 `IL2Prover` + `IL2ProofVerifier`

Phase 4(ZK 有效性证明)允许运维者带自家证明系统 —— SP1 作为参照出货,但
想要 Halo2、Plonky3 或 Risc0 的链只需实现 prover/verifier 对,并把 verifier
注册到 L1 上的 `NeoHub.VerifierRegistry`。

两个接口都在 `Neo.L2.Abstractions`。这一对必须就同一线协议格式达成一致 ——
prover 在 `ProofResult.Proof` 中输出什么,verifier 之后就解码什么:

```csharp
using Neo.L2;

// Prover 侧:跑在排序器上,产生证明。
public sealed class Halo2Prover : IL2Prover
{
    private readonly IHalo2BackendClient _backend;

    public Halo2Prover(IHalo2BackendClient backend) => _backend = backend;

    public ProofType Kind => ProofType.Zk;  // 与 SP1 共享 ProofType.Zk;链上派发
                                            // 经 VerificationKeyId 区分。

    public async ValueTask<ProofResult> ProveAsync(
        ProofRequest request, CancellationToken cancellationToken = default)
    {
        // 1. 按 VerifierRegistry 期望的规范格式序列化 public input。
        var publicInputBytes = Neo.L2.Batch.BatchSerializer.EncodePublicInputs(
            request.PublicInputs);

        // 2. 把 witness(request.Witness)交给你的证明系统后备。
        var proofBytes = await _backend.ProveAsync(
            publicInputBytes, request.Witness, cancellationToken);

        // 3. 包进规范 RiscVProofPayload 信封。ProofSystem 字节区分 Sp1 / Halo2 /
        //    等,让 verifier 知道用哪个 decoder。
        var payload = new Neo.L2.Proving.RiscVZk.RiscVProofPayload
        {
            ProofSystem = Neo.L2.Proving.RiscVZk.ProofSystem.Halo2,  // 或你注册的 tag
            ProofBytes = proofBytes,
            VerificationKeyId = _backend.VerificationKeyId,
        };

        return new ProofResult
        {
            Proof = payload.Encode(),
            Kind = ProofType.Zk,
            PublicInputHash = Neo.L2.State.StateRootCalculator.HashPublicInputs(
                request.PublicInputs),
        };
    }
}

// Verifier 侧:跑在 L1(链下 pre-flight),并在 VerifierRegistry 注册的、做实际
// 密码学校验的 NeoVM 合约中镜像。
public sealed class Halo2Verifier : IL2ProofVerifier
{
    private readonly UInt256 _expectedVkId;
    private readonly IHalo2BackendClient _backend;

    public Halo2Verifier(UInt256 expectedVkId, IHalo2BackendClient backend)
    {
        _expectedVkId = expectedVkId;
        _backend = backend;
    }

    public ProofType Kind => ProofType.Zk;

    public async ValueTask<ProofVerificationResult> VerifyAsync(
        PublicInputs publicInputs, ReadOnlyMemory<byte> proof,
        CancellationToken cancellationToken = default)
    {
        // 1. 解码规范信封。坏字节 → 带清晰原因失败。
        Neo.L2.Proving.RiscVZk.RiscVProofPayload payload;
        try
        {
            payload = Neo.L2.Proving.RiscVZk.RiscVProofPayload.Decode(proof.Span);
        }
        catch (Exception ex)
        {
            return ProofVerificationResult.Fail($"decode: {ex.Message}");
        }

        // 2. VK pin:调用方期望的 vk 必须与 prover 声明的一致。
        if (!payload.VerificationKeyId.Equals(_expectedVkId))
            return ProofVerificationResult.Fail("vk mismatch");

        // 3. 交给你的 verifier 后备。
        var ok = await _backend.VerifyAsync(
            publicInputs, payload.ProofBytes, cancellationToken);
        return ok ? ProofVerificationResult.Ok : ProofVerificationResult.Fail("halo2 reject");
    }
}
```

Stage-2 ZK 有效性 的参照是 `bridge/neo-zkvm-host/` —— 生产证明守护进程
(`prove-batch daemon --watch <dir>`)。进程内测试用
`Neo.L2.Proving.RiscVZk.MockRiscVProver`(确定性占位)。把 verifier 接到链的启动
序列;经 `NeoHub.VerifierRegistry.RegisterVerifier(proofType, verifierHash)` 注册
匹配的链上 verifier 合约,让规范结算路径拾起它。

---

## 一图看生命周期

```
用户:    ──► 在 L1 上充值(SharedBridge.Deposit)
                                 ↓
L2:      ── L1MessageInbox 出队 → DepositProcessor 铸 L2 GAS
                                 ↓
         用户提交 L2 tx → 排序器排序 → 批处理器累积
                                 ↓
         批次封装 → StateRootGenerator 计算 7 个 root
                                 ↓
         DAWriter 发布批次 payload → DAReceipt → daCommitment
                                 ↓
         证明者产生证明(Multisig / Optimistic / ZK)
                                 ↓
L1:      SettlementManager.SubmitBatch(commitment, publicInputs, proof)
                                 ↓
         Verifier 派发 → 最终化 → 更新规范状态根
                                 ↓
用户:    提款:SharedBridge.FinalizeWithdrawalWithProof(...)
         (或 L2 卡死时 EmergencyManager.EscapeHatchExitWithProof)
```

每个箭头都是代码库里已有的合约或插件方法。规格映射见 `AGENTS.md` 的
"Mapping doc.md to code" 或 `docs/architecture-walkthrough.md`。

---

## 扩展 vs 分叉

本框架被设计为*扩展*,而非分叉。模式:

- **新链类型**(例如隐私链)→ 在 `CreateChainCommand.cs` 加一个 `--template`
  条目,带正确默认值;其它都复用共享 NeoHub。
- **新证明系统**(例如 Halo2)→ 实现 `IL2Prover` + `IL2ProofVerifier`,经
  `VerifierRegistry` 注册,把 L2 链 config 指向它。
- **新 DA 层**(例如 Celestia、Avail)→ 实现 `IDAWriter`,经 `L2DAPlugin.WithWriter()`
  接上,记录你声明的 `daMode` 字节。
- **新排序器模型**(例如 PoS 轮转)→ 实现 `ISequencerCommitteeProvider`,经
  `L2BridgePlugin` ctor 接上。

每条路径都让链留在 Neo Elastic Network 内部 —— 同一 SharedBridge、同一结算、同一
消息路由 —— 同时让链的*内部*成为运维者所需的样子。

---

## 上 L1:部署 NeoHub

`register-chain` 起作用之前,23 个生产 NeoHub 合约必须部署到目标 L1。仅测试用的
`ExternalBridgeStubVerifier` 不包含在默认 deploy bundle 中,也不得注册为生产 verifier。`neo-hub-deploy`
工具输出一个 deploy bundle,把每个合约、它的依赖、拓扑排序后解析的哈希都列出:

```bash
# 1. 脚手架一个起步 plan(23 个生产 NeoHub 部署步骤按依赖序,
#    包含 NativeZkVerifier 以及 v1/v2 与 v3 fraud verifier)。
dotnet run --project tools/Neo.Hub.Deploy -- scaffold \
    --output ./my-l2/deploy-plan.json

# 2. 编辑 plan,填 OWNER_REPLACE_ME / BOND_ASSET_REPLACE_ME 占位
#    (目标 L1 上的规范 GAS 哈希、你的运维多签哈希等)。plan 是 JSON ——
#    diff 友好 + 可编辑。

# 3. 拓扑排序 + 把 $step:<name> 占位解析为按部署顺序派生的确定性合约哈希。
#    输出 bundle 是你的钱包按序喂给 ContractManagement.Deploy 的脚本。
dotnet run --project tools/Neo.Hub.Deploy -- plan \
    --plan ./my-l2/deploy-plan.json \
    --output ./my-l2/deploy-bundle.json
```

bundle 的 `Invocations` 数组就是你钱包的部署脚本 —— 每条对应一次
`ContractManagement.Deploy` 调用,按序执行。每条带 `Name`、对应 `.nef` +
`.manifest.json` 路径,以及解析后的 `DeployData`(所有 `$step:<name>` 占位都已
被 bundle 中早先合约的哈希替换)。

bundle 的 "PostDeployActions" 段浮现在所有合约部署后必须跑的接线步骤(例如
`SequencerBond.RegisterSlasher(OptimisticChallenge)` 打破 bond↔challenge 循环、
`ChainRegistry.SetGovernanceController` 启用 §16.1 准入策略,以及按 fraud
verifier 的信息提示,告知传给 `OptimisticChallenge.Challenge` 的 `fraudVerifier`
参数应是哪个合约哈希)。

> **关于哈希的提示**:bundle 的按步 `Hash` 字段是*确定性 stub*,从步骤名派生
> (这样 `plan` 可在无钱包的情况下复现)。真实的 L1 合约哈希只在你的钱包调
> `ContractManagement.Deploy` 之后才存在。钱包返回每个真实哈希;把这些捕获到
> 下面的 `register-chain` 4 个 flag 里 —— **不要**用 bundle 中的 stub 哈希。

所有 23 步部署 + post-deploy 接线完成后,把**真实链上**合约哈希(由你的钱包从
每次 `ContractManagement.Deploy` 调用返回)捕获到 `register-chain` 的 4 个 flag:

```bash
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <你多签部署返回的真实哈希> \
    --verifier <你 VerifierRegistry 部署返回的真实哈希> \
    --bridge <你 bridge-adapter 部署返回的真实哈希> \
    --message <你 MessageRouter 部署返回的真实哈希>
```

这会输出可粘贴进钱包的规范 91 字节 `configBytes`,让钱包送到
`ChainRegistry.RegisterChain`(准入模式 0)或 `ChainRegistry.RegisterChainPublic`
(准入模式 1 + 2 —— 由 `GovernanceController.GetAdmissionMode` 把关的 §16.1
3 阶段流程)。

`RegisterChain` 一返回,L2 就活了 —— 排序器 + 批处理器 + 证明者插件开始产出批次
(上文 5 命令路径里的 `start-sequencer` / `start-batcher` / `start-prover`),
每批次的 commitment 经 L1 上的 `SettlementManager.SubmitBatch` 流通。

---

## 验证你的链已注册

```bash
# Devnet
dotnet run --project tools/Neo.L2.Devnet -- 3 --metrics-port 9090
curl http://127.0.0.1:9090/metrics | grep l2_batch_sealed

# 在 L1 上 register-chain 之后,查 NeoHub:
neo-cli invoke <ChainRegistryHash> getChainConfig <chainId>
# → 返回 91 字节(按 §16.2 编码的 L2ChainConfig);为空 = 未注册

# 或把 5 维 §16.2 安全标签作为单一对象查询:
neo-cli invoke <ChainRegistryHash> getSecurityLevel <chainId>
neo-cli invoke <ChainRegistryHash> getSequencerModel <chainId>
neo-cli invoke <ChainRegistryHash> getExitModel <chainId>
neo-cli invoke <ChainRegistryHash> getDAMode <chainId>
neo-cli invoke <ChainRegistryHash> getGatewayEnabled <chainId>
neo-cli invoke <ChainRegistryHash> getPermissionlessExit <chainId>
```

一条已注册、产出批次的链发出 `l2.batch.sealed`、`l2.settlement.submitted`、
`l2.proving.generated`、`l2.bridge.deposits`。审计框架
(`Neo.L2.Audit.ChainAuditor`)在跑后跑 6 项不变量检查;devnet 自动跑它们,在
末尾打印 ✅ / ❌。

---

## 证明者部署(Stage 2 ZK 有效性)

如果你的链跑 Stage-2(RISC-V ZK 有效性)模式,证明者应当是与排序器**分开的进
程** —— 这是所有主流 zk-rollup 的架构(Optimism 的 op-batcher/op-proposer
拆分、Arbitrum 的 BoLD provers、ZKsync 的 prover 子系统)。证明者是多 GB、多
CPU/GPU 的工作负载,自己有 SLA;把它耦合进排序器进程脆弱且不可扩展。

框架出货**两种** SP1 集成。挑一个:

### 推荐:进程外 Rust 证明守护进程

**这是生产路径。** 现代 sp1-sdk 6.2.1,依赖图更简单,匹配业界标准 L2 布局。

```bash
# 构建守护进程二进制(一次性):
cd bridge/neo-zkvm-host
cargo build --release
# → target/release/prove-batch

# 让排序器把封好的批次扔进队列目录(例如 /var/lib/neo-l2/batches/)——
# 格式就是规范 BatchExecutionRequest 字节,写入 <batch-number>.batch.bin。
# 该格式正是 `Neo.L2.Batch.BatchExecutionRequest.Encode()` 输出的。

# 跑守护进程(通常在 systemd / k8s 下):
prove-batch daemon \
    --watch /var/lib/neo-l2/batches \
    --archive /var/lib/neo-l2/proven \
    --poll-secs 5
```

守护进程 poll `--watch` 找 `*.batch.bin`,为每个产生真实 ZK 证明,写出
`<name>.proof.bin`(链上提交工件)+ `<name>.proof.vk`(verifying key,按 guest
ELF 稳定),再把输入移到 `--archive` 防止重复处理。失败时输入留在原地并大声报警,
让监控捕获 poison-pill 批次。

它实际证明的是:批次中每笔 tx 作为 Neo N3 VM 脚本被载入,由
`neo_vm_guest::execute`(从 `external/neo-zkvm/crates/neo-vm-guest` 引入,内含
完整纯 Rust 的 Neo N3 VM —— opcode、eval 栈、gas 计费、native 合约、storage)
执行。证明对每笔 tx 的 halt-or-fault、特定 gas 计数、特定栈顶结果做证明。
篡改任何执行细节都会破坏证明。

链上结算 tx 经 `NeoHub.SettlementManager.SubmitBatch` 提交 `<name>.proof.bin`、
`<name>.proof.vk` 和 public-input 承诺。`VerifierRegistry` 派发到已注册 verifier,
证明通过则链最终化。

### 提交前在链下校验证明

框架出货公开 `verify()`,让运维者付 L1 gas 之前能对证明做健全性检查:

```rust
use neo_zkvm_host;

let proof = std::fs::read("00000042.proof.bin")?;
let vk    = std::fs::read("00000042.proof.vk")?;
let expected_pi_hash: [u8; 32] = /* 来自 BatchExecutionRequest */;

neo_zkvm_host::verify(&proof, &vk, &expected_pi_hash)?;
```

当前电路尺寸下,在强 CPU 上验证约 42 秒。如果你想要双保险,把它折进证明守护
进程的提交前检查里(守护进程默认不跑 —— 证明者已经产出证明,所以验证只能抓
证明者自身的 bug)。

---

## 参考

- 规格:[`doc.md`](../../doc.md)(中文,权威)
- 架构:[`ARCHITECTURE.md`](../../ARCHITECTURE.md)(英文精炼)
- 按组件:[`IMPLEMENTATION_STATUS.md`](../../IMPLEMENTATION_STATUS.md)
- 导览:[`docs/architecture-walkthrough.md`](architecture-walkthrough.md)
- 可观测性:[`docs/telemetry.md`](telemetry.md)
- 持久化:[`docs/persistence.md`](persistence.md)
- 安全模型:[`docs/security-model.md`](security-model.md)
- 规格空白计划:[`docs/spec-gap-plan.md`](spec-gap-plan.md)
