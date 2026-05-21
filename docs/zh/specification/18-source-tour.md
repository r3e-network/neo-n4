# 第 18 章：源码阅读路线

本章像读 Linux 内核书一样给出源码阅读路线。不要从随机文件开始读；按系统边界读。

## 18.1 第一条路线：从协议对象开始

阅读顺序：

```text
src/Neo.L2.Abstractions/Models/L2ChainConfig.cs
src/Neo.L2.Abstractions/Models/L2BatchCommitment.cs
src/Neo.L2.Abstractions/Models/ProofType.cs
src/Neo.L2.Abstractions/Models/DAMode.cs
src/Neo.L2.Abstractions/IDAWriter.cs
src/Neo.L2.Abstractions/IL2BatchExecutor.cs
```

目标：理解模块之间传递什么，而不是先陷入实现细节。

## 18.2 第二条路线：从一笔交易到 batch

```text
src/Neo.L2.Batch/L2Batch.cs
src/Neo.L2.Batch/BatchBuilder.cs
src/Neo.L2.Batch/BatchSerializer.cs
src/Neo.L2.Executor/
src/Neo.L2.Executor.RiscV/
src/Neo.L2.State/
```

问题清单：

- batch 是如何选取交易的？
- serializer 是否规范？
- executor 输出哪些 roots？
- state root 如何计算？
- RISC-V executor 与默认 executor 的边界在哪里？

## 18.3 第三条路线：从 batch 到 L1 settlement

```text
src/Neo.L2.Proving/
src/Neo.L2.Settlement.Rpc/
contracts/NeoHub.SettlementManager/
contracts/NeoHub.VerifierRegistry/
contracts/NeoHub.ContractZkVerifier/
contracts/NeoHub.DARegistry/
contracts/NeoHub.DAValidator/
```

问题清单：

- proof payload 如何被生成？
- verifier registry 如何 dispatch？
- DA commitment 在哪里检查？
- state root continuity 在哪里保证？

## 18.4 第四条路线：资产桥

```text
contracts/NeoHub.TokenRegistry/
contracts/NeoHub.SharedBridge/
src/Neo.L2.Bridge/AssetRegistry.cs
src/Neo.L2.Bridge/DepositProcessor.cs
src/Neo.L2.Bridge/WithdrawalProcessor.cs
external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs
```

问题清单：

- L1/L2 decimals 如何记录？
- NEO 的 0 -> 8 decimals 如何处理？
- withdrawal spent 状态在哪里？
- L2 mint/burn 是否只能由系统路径触发？

## 18.5 第五条路线：运维 CLI

```text
tools/Neo.Stack.Cli/
tools/Neo.Hub.Deploy/
tools/Neo.L2.Devnet/
tools/Neo.L2.Explore/
tools/Neo.L2.Faucet.Cli/
tools/Neo.L2.Bridge.Cli/
tools/Neo.External.Bridge.Cli/
```

问题清单：

- operator 如何创建链目录？
- NeoHub deploy plan 如何生成？
- devnet 如何注入 sample config？
- explorer 如何审计 state root continuity？

## 18.6 第六条路线：测试证明

```text
tests/Neo.Hub.Deploy.UnitTests/
tests/Neo.L2.Batch.UnitTests/
tests/Neo.L2.Executor.RiscV.UnitTests/
tests/Neo.L2.Bridge.UnitTests/
tests/Neo.L2.Proving.UnitTests/
tests/Neo.Plugins.L2Gateway.UnitTests/
tests/experience-hub/
```

读测试的技巧：

- 先看测试名称；
- 再看输入对象；
- 最后看断言；
- 把断言写回规格书里的不变量。

## 18.7 如何评估一个新改动

任何新改动都要问：

| 问题 | 如果答案是否定的 |
| --- | --- |
| 是否保持协议对象一致？ | 先改 shared model |
| 是否有单元测试？ | 不能合并 |
| 是否影响文档？ | 同步中英文 docs |
| 是否影响图表？ | 同步中文图表 |
| 是否影响生产门槛？ | 更新 readiness checklist |
| 是否引入新 trust assumption？ | 更新 security model |

