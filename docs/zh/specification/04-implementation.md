# 第 4 章：实现导读

本章按代码目录解释 Neo N4 如何实现。读者可以把它当成源码地图。

## 4.1 仓库分层

| 目录 | 学习重点 |
| --- | --- |
| `contracts/NeoHub.*` | L1 可部署合约，掌握 NeoHub 的状态机 |
| `external/neo` | r3e Neo core fork，掌握 L2 native contracts 和 execution kernel |
| `src/Neo.L2.Abstractions` | 所有模块共享的数据模型和接口 |
| `src/Neo.L2.Batch` | batch 构造、序列化、commitment 形成 |
| `src/Neo.L2.Executor*` | 确定性执行和 NeoVM2/RISC-V executor |
| `src/Neo.L2.State` | 状态 root、Merkle/MPT 相关逻辑 |
| `src/Neo.L2.Proving` | proof registry、attestation、optimistic、RISC-V ZK seam |
| `src/Neo.L2.Bridge` | deposit、withdrawal、asset mapping |
| `src/Neo.Plugins.L2*` | 节点插件集成层 |
| `tools/*` | 运维和部署 CLI |
| `bridge/*` | Rust execution core 和 SP1 zkVM host/guest |
| `watchers/*` | 外部链 watcher |
| `sdk/*` | 应用 SDK 和 web explorer |
| `tests/*` | 模块对应的单元和集成测试 |

## 4.2 抽象层：`Neo.L2.Abstractions`

抽象层定义系统所有跨模块对象。任何模块如果需要沟通，应优先使用这里的类型，而不是重新定义 JSON 或 ad hoc byte array。

关键接口：

```text
IDAWriter            -> DA 发布与可用性检查
IL2BatchExecutor     -> 确定性批次执行函数
IL2ProofVerifier     -> proof 验证接口
ISettlementClient    -> L1 settlement RPC/调用抽象
IBridgeAdapter       -> bridge 适配器
IMessageRouter       -> 消息路由抽象
```

`IL2BatchExecutor` 的设计说明了整个证明系统的核心边界：

```csharp
public interface IL2BatchExecutor
{
    ValueTask<BatchExecutionResult> ApplyBatchAsync(
        BatchExecutionRequest request,
        CancellationToken cancellationToken = default);
}
```

这个函数必须是确定性的：不能读取时钟、随机数、网络、日志或本地机器状态。证明系统证明的就是这个函数在给定输入上的输出。

## 4.3 批次构造：`Neo.L2.Batch`

`Neo.L2.Batch` 的主要责任是把 L2 blocks / transactions 变成 batch：

```text
L2 blocks
  -> BatchBuilder
  -> L2Batch
  -> BatchSerializer
  -> L2BatchCommitment
```

读代码顺序：

1. `src/Neo.L2.Batch/L2Batch.cs`
2. `src/Neo.L2.Batch/BatchBuilder.cs`
3. `src/Neo.L2.Batch/BatchSerializer.cs`
4. `tests/Neo.L2.Batch.UnitTests`

学习重点：

- batch number 如何递增；
- pre/post root 如何连接；
- transactions、receipts、messages、withdrawals 如何形成 roots；
- 序列化如何避免不同实现得出不同 hash。

## 4.4 执行层：NeoVM2/RISC-V

执行层有两个相关入口：

| 入口 | 路径 | 作用 |
| --- | --- | --- |
| C# executor seam | `src/Neo.L2.Executor` | 默认确定性执行抽象 |
| RISC-V executor | `src/Neo.L2.Executor.RiscV` | NeoVM2/RISC-V via PolkaVM 路径 |

`RiscVTransactionExecutor` 的任务是把 L2 transaction 输入送进 RISC-V host，并把结果转换成 batch execution result 可以消费的形态。

注意：未来 EVM/WASM/Move 等生态应该按 executor profile 接入，不应绕过 `IL2BatchExecutor` 的确定性边界。

## 4.5 L2 native contracts

L2 native contracts 在 `external/neo` fork 中实现：

```text
external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs
```

注册入口在 Neo core 的 `NativeContract` registry。测试入口：

```powershell
dotnet test external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj `
  -c Release --filter FullyQualifiedName~UT_L2NativeContracts `
  /p:NuGetAudit=false --nologo
```

读代码时优先关注：

- 每个 native contract 的 storage prefix；
- 方法名称是否稳定；
- 与 L1 payload / root 的字段是否一致；
- 是否有测试证明 registry 中确实存在这些合约。

## 4.6 NeoHub deployable contracts

NeoHub 不是一个单体项目。每个合约在 `contracts/NeoHub.<Name>/` 下独立编译。

生产部署计划来自：

```text
tools/Neo.Hub.Deploy/ScaffoldPlan.cs
```

相关 CLI：

```powershell
dotnet run --project tools\Neo.Hub.Deploy -- scaffold --output plan.json
dotnet run --project tools\Neo.Hub.Deploy -- plan --plan plan.json --output bundle.json
dotnet run --project tools\Neo.Hub.Deploy -- verify --plan plan.json --rpc https://testnet1.neo.coz.io:443
```

设计重点：

- 24 个生产合约进入默认 deploy plan；结构性 v1/v2 审计验证器与测试 stub 均不进入；
- `ExternalBridgeStubVerifier` 只用于测试；
- `ContractZkVerifier` 替代旧 `NativeZkVerifier` 路线；
- post-deploy action 会提示 operator 注册 DA、verifier、bridge 和 filter。

## 4.7 DA 插件

DA 实现位于：

```text
src/Neo.Plugins.L2DA
```

概念上每个 DA writer 都实现：

```text
Publish(batch bytes) -> DAReceipt
IsAvailable(receipt) -> bool
```

NeoFS DA 是默认叙事方向，其他模式用于测试、fallback 或特定部署环境。

## 4.8 Proving 与 zkVM

证明层分两部分：

| 部分 | 路径 | 说明 |
| --- | --- | --- |
| .NET proof seam | `src/Neo.L2.Proving` | registry、attestation、optimistic、RISC-V ZK seam |
| Rust zkVM host/guest | `bridge/neo-zkvm-host`, `bridge/neo-zkvm-guest` | SP1 proof generation / verification |
| execution core | `bridge/neo-execution-core` | 后端中立的 batch parsing、receipt/state folding、public input hash |

真实生产 ZK 路径必须满足：

1. guest ELF 可复现；
2. public input hash 与 L2 batch commitment 一致；
3. proof verifier contract 通过治理注册；
4. verification key 通过治理注册；
5. malformed proof 必须失败。

## 4.9 SDK 与 RPC

SDK 让应用不需要直接拼 JSON-RPC：

| SDK | 路径 |
| --- | --- |
| .NET | `src/Neo.L2.Sdk` |
| TypeScript | `sdk/typescript` |
| Rust | `sdk/rust` |
| Web Explorer | `sdk/web-explorer` |

读 SDK 时关注：

- 10 个 RPC 方法的 shape 是否一致；
- 错误分类是否一致；
- chain id mismatch 是否被显式处理；
- secret 是否永远不进入浏览器或文档。
