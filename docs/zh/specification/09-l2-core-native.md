# 第 9 章：L2 Core 与原生合约

Neo N4 的 L2 不是“部署一组合约伪装成系统合约”。L2 侧真正属于链内核的能力应该进入 Neo core fork，作为 native contracts 在 genesis / native registry 中存在。

## 9.1 两个 Neo core 分支

| 分支 | 来源 | 用途 |
| --- | --- | --- |
| `r3e/neo-n3-core` | upstream `master-n3` | L1 core，尽量保持小改动 |
| `r3e/neo-n4-core` | upstream `master` | L2 execution kernel，承载 N4 native contracts 和执行 profile |

主仓库默认 submodule：

```text
external/neo -> r3e-network/neo, branch r3e/neo-n4-core
```

## 9.2 为什么 L2 需要 native contracts

L2 native contracts 处理的是“每条 L2 都必须有”的系统能力：

- 当前 chain config；
- 当前 batch / L1 anchor 信息；
- L1/L2 消息收发；
- bridged token mint/burn；
- fee accounting；
- paymaster；
- account abstraction；
- external bridge entry；
- interop verification。

这些能力如果作为普通用户合约部署，会产生几个问题：

| 问题 | 后果 |
| --- | --- |
| 地址不稳定 | SDK、桥、节点插件很难安全引用 |
| 初始化顺序复杂 | genesis 状态和系统状态容易错位 |
| 权限边界弱 | 用户合约与系统合约难以区分 |
| 升级语义不清 | core 升级和合约升级混在一起 |

## 9.3 `L2NativeContracts.cs`

核心文件：

```text
external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs
```

继承关系：

```text
NativeContract
  -> L2NativeContract
      -> L2SystemConfigContract
      -> L2BatchInfoContract
      -> L2MessageContract
      -> L2BridgeContract
      -> L2FeeContract
      -> L2PaymasterContract
      -> L2NativeExternalBridgeContract
      -> L2AccountAbstraction
      -> BridgedNep17Contract
      -> L2InteropVerifier
```

## 9.4 每个 native contract 的读法

| 合约 | 读代码时重点 |
| --- | --- |
| `L2SystemConfigContract` | 配置 key、更新权限、L1 anchor 信息 |
| `L2BatchInfoContract` | 当前 batch number、高度、L1 block reference |
| `L2MessageContract` | inbox/outbox、nonce、防重放 |
| `L2BridgeContract` | deposit credit、withdrawal burn、asset id |
| `L2FeeContract` | base fee、DA fee、prover fee、sequencer fee |
| `L2PaymasterContract` | fee sponsorship、授权和余额 |
| `L2NativeExternalBridgeContract` | external message / asset entry |
| `L2AccountAbstraction` | account validation、sponsor、nonce |
| `BridgedNep17Contract` | NEP-17 兼容接口和 bridged supply |
| `L2InteropVerifier` | L1/L2 proof 或 interop proof 校验 |

## 9.5 Native registry 测试

测试入口：

```powershell
dotnet test external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj `
  -c Release --filter FullyQualifiedName~UT_L2NativeContracts `
  /p:NuGetAudit=false --nologo
```

测试必须证明：

1. native contract 类存在；
2. `NativeContract` registry 注册它们；
3. ID / name / hash 稳定；
4. 基础调用语义符合预期。

## 9.6 L1 NeoHub 与 L2 native 的边界

| 能力 | 放在 L1 NeoHub | 放在 L2 native |
| --- | --- | --- |
| L2 注册 | 是 | 否 |
| L1 escrow | 是 | 否 |
| L2 mint/burn | 否 | 是 |
| batch finalization | 是 | 否 |
| 当前 batch 信息 | 否 | 是 |
| L1 proof verification | 是 | 否 |
| L2 message apply | 部分 | 是 |
| emergency pause root | 是 | L2 读取/响应 |

一句话：

> L1 负责“承认什么是真的”，L2 native contracts 负责“在 L2 内部如何执行这个真相”。

