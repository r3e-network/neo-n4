# 持久化

Neo Elastic Network 的 L2 组件通过 `Neo.L2.Persistence` 抽象来持久化关键状态。
**生产部署一律用 RocksDB**;内存版本只用于测试和 devnet。

## 为什么这件事重要

L2 状态并非铁板一块。一部分状态在重启时可以从 L1 重新派生(asset registry、批次
元数据)—— 丢了只是慢一点重启,不算 bug。但另一部分状态一旦丢失就会破坏安全或正
确性不变量:

| 状态                                  | 丢了会出什么事                                                       |
| ------------------------------------- | -------------------------------------------------------------------- |
| 已最终化的消息 包含证明         | 节点重启后 RPC 客户端查不到证明;桥停摆。                            |
| 提款 包含证明                   | 用户无法在 L1 领取已最终化的提款。                                   |
| 强制纳入的已用 nonce 集合             | L2 可能重新执行或拒绝某笔强制 tx —— 破坏 at-most-once。              |
| 确定性 state-store 内容               | 重启后的 post-state 根 ≠ 重启前的 pre-state 根;承诺产生分歧。       |
| DA blob 字节(L2 自己持有时)         | 验证者无法重建被声明过的内容。                                       |
| 排序器委员会 + 退出窗口               | 半途的退出 deadline 丢失;作恶排序器被重新接受或卡死。              |

每多一个 RocksDB 后备 store,就少一个由重启引发的事故来源。

## 抽象

`IL2KeyValueStore`(在 `Neo.L2.Persistence`)是每个可持久化组件都委托过去的、最小
的、字节键-字节值的 store 接口:

```csharp
public interface IL2KeyValueStore : IDisposable
{
    void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
    byte[]? Get(ReadOnlySpan<byte> key);
    bool Delete(ReadOnlySpan<byte> key);
    bool Contains(ReadOnlySpan<byte> key);
    IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(ReadOnlySpan<byte> prefix);
    long Count { get; }
}
```

仓库自带两种实现:

| 类                          | 适用场景         | 后备                              | 重启留存 |
| --------------------------- | --------------- | --------------------------------- | -------- |
| `InMemoryKeyValueStore`     | 测试、devnet    | `SortedDictionary<byte[], byte[]>` | 否       |
| `RocksDbKeyValueStore`      | 生产            | RocksDB 10.10(Snappy 压缩)       | 是       |

一套共用的 `KeyValueStoreContractTests` 同时作用于两种后备 —— 行为完全一致;未来要加
LevelDB / SQLite / 云端后备,只需新加一个 TestClass + `Create()` 工厂方法。

## 各组件接线

目前有 6 个组件把关键持久化状态委托给 `IL2KeyValueStore`。每个组件都有一个使用内存
后备的默认 ctor(适合测试)和一个接受调用方提供 store 的备用 ctor:

### 1. DA writer(`Neo.Plugins.L2DA.PersistentDAWriter`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/da");
plugin.WithWriter(new PersistentDAWriter(rocks, DAMode.NeoFS, ownsStore: true));
```

或者更简单 —— 在插件配置段里设置 `DataDirectory`,`L2DAPlugin` 会自动在该路径打开
RocksDB:

```json
{
  "PluginConfiguration": {
    "DAMode": 1,
    "DataDirectory": "/var/lib/neo-l2/da"
  }
}
```

### 2. State oracle(`Neo.L2.Executor.State.KeyedStateStore`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/state");
var store = new KeyedStateStore(rocks);
var oracle = new KeyedStateRootOracle(store);
```

`ComputeRoot()` 计算的 Merkle 根在 InMemory 和 RocksDB 后备之间一致 —— 即插即换。

### 3. 消息 router 已最终化证明(`Neo.L2.Messaging.InMemoryMessageRouter`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/messages");
var router = new InMemoryMessageRouter(
    inbox: null, outbox: null,
    finalized: rocks, ownsFinalized: true);
```

inbox + outbox 保持瞬态 —— 重启时从 L1 重新抽水。只有最终化证明 map 需要持久。

### 4. RPC store 证明(`Neo.Plugins.L2Rpc.InMemoryL2RpcStore`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/rpc-proofs");
var store = new InMemoryL2RpcStore(
    chainId: 1001,
    level: SecurityLevel.Optimistic,
    proofs: rocks,
    ownsProofs: true);
```

提款 + 消息证明共享同一个 RocksDB(用 1 字节 key 前缀区分)。其它 RPC 状态(批次、
资产映射、充值)仍是内存的 —— 都可以从 L1 重建。

### 5. 强制纳入的已用 nonce 集合(`Neo.L2.ForcedInclusion.InMemoryForcedInclusionSource`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/forced-inclusion");
var src = new InMemoryForcedInclusionSource(
    chainId: 1001,
    consumed: rocks,
    ownsConsumed: true);
```

待处理队列保持内存。只有已用 nonce 集合需要持久 —— 丢了会让排序器重新纳入或拒绝
已经被消耗的强制 tx。

### 6. 排序器委员会成员(`Neo.L2.Sequencer.InMemorySequencerCommitteeProvider`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/sequencer");
using var p = new InMemorySequencerCommitteeProvider(
    chainId: 1001,
    store: rocks,
    ownsStore: true);
```

内存字典保持热路径;所有写入(Register / BeginExit / Finalize)都影子写入 KV store。
构造时,成员被从 store 还原到字典里 —— 重启可以从上次进程停下的位置接着跑,**包括
半途的退出窗口**。没有持久化时,半途退出中的节点重启可能丢失 `ExitsAtUnixSeconds`
deadline,要么把本应进入冷却的排序器重新接受,要么拒绝完成一个窗口已过的退出。

## 运维配置范例

一个生产 L2 节点通常划出一个总目录,给每个 store 一个子目录:

```
/var/lib/neo-l2/
├── da/                  # PersistentDAWriter
├── state/               # KeyedStateStore
├── messages/            # InMemoryMessageRouter(已最终化证明)
├── rpc-proofs/          # InMemoryL2RpcStore(提款 + 消息)
├── forced-inclusion/    # InMemoryForcedInclusionSource(已消耗)
└── sequencer/           # InMemorySequencerCommitteeProvider(成员 + 退出窗口)
```

每个 RocksDB 实例都是独立的 —— **不是**同一数据库的 column family。这是有意为之:
一个数据库损坏不会拖垮其它的,运维者也可以单独备份/恢复。

## Devnet 运行器

进程内 devnet 运行器(`tools/Neo.L2.Devnet`)支持 `--data-dir` 标志,会在一个根目录
下自动接好其中四个 store —— 这是看持久化故事最快的端到端方式:

```bash
# 首次运行 —— 写入 RocksDB
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/devnet1

# 二次运行 —— 同一目录,状态保留
dotnet run --project tools/Neo.L2.Devnet -- 0 --data-dir /tmp/devnet1
# → "[wire] sequencer committee: 3 active members"  (从 store 还原,而非重新注册)
# → "[wire] keyed state store + oracle (5 initial entries)"  (Alice 的余额已恢复)
```

带 `--data-dir <path>` 时,devnet 会创建以下子目录:

```
<path>/
├── state/        # KeyedStateStore
├── rpc-proofs/   # InMemoryL2RpcStore(提款 + 消息证明)
├── sequencer/    # InMemorySequencerCommitteeProvider
└── da/           # PersistentDAWriter(DAMode.NeoFS + dataDir)
```

不带 `--data-dir` 时,所有 store 都是内存版 —— 测试无妨,但重启即丢。生产部署一律
应当设置 `--data-dir`(基于插件的部署用对应配置 key)。

## 运维 checklist

- [ ] 每个需要持久的 L2 组件都用 `(IL2KeyValueStore)` ctor 重载。空参默认 ctor 仅
      用于测试 —— 不要发布。
- [ ] 传给 `RocksDbKeyValueStore` 的目录在持久化存储上(不是 `tmpfs` 或临时容器卷)。
- [ ] 备份覆盖上述 6 个子目录。只备份其中之一可能让 L2 在恢复时进入不一致状态(例
      如:已消耗 nonce 在,但对应的最终化证明丢失)。
- [ ] 跑 L2 节点的进程对每个目录都有写权限。
- [ ] 计划停机时,显式 dispose store(`using` 块),让 RocksDB 把 memtable 落盘。

## 添加新持久化后备

要加(比如)LevelDB 或 DynamoDB 这类云端 KV:

1. 实现 `IL2KeyValueStore`。
2. 给 `tests/Neo.L2.Persistence.UnitTests/UT_KeyValueStore.cs` 加一个 TestClass,
   继承自 `KeyValueStoreContractTests`,提供指向你的后备的 `Create()` 工厂方法。
   12 条共享合约测试照原样跑。
3. 在相应插件的 `WithWriter` / ctor 注入点接上。

消费者侧无需改动 —— 抽象让它们与后备无关。

## 另请参阅

- `src/Neo.L2.Persistence/IL2KeyValueStore.cs` —— 接口,带完整 XML 文档。
- `src/Neo.L2.Persistence/RocksDbKeyValueStore.cs` —— 生产后备。
- `tests/Neo.L2.Persistence.UnitTests/UT_KeyValueStore.cs` —— 合约测试。
