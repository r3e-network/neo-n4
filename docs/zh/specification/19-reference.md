# 第 19 章：参考表

本章汇总常用查表信息。

## 19.1 目录速查

| 想做什么 | 看哪里 |
| --- | --- |
| 理解架构 | `docs/zh/specification/`, `docs/zh/architecture-atlas.md` |
| 启动 devnet | `tools/Neo.L2.Devnet`, `samples/*.config.json` |
| 部署 NeoHub | `tools/Neo.Hub.Deploy`, `contracts/NeoHub.*` |
| 写应用 | `sdk/typescript`, `src/Neo.L2.Sdk`, `sdk/rust` |
| 审计合约 | `contracts/NeoHub.*`, `tests/Neo.Hub.Deploy.UnitTests` |
| 审计 L2 native | `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` |
| 审计 proof | `src/Neo.L2.Proving`, `bridge/neo-zkvm-*` |
| 审计 DA | `src/Neo.Plugins.L2DA`, `contracts/NeoHub.DA*` |
| 审计外部桥 | `watchers/*`, `external/foreign-contracts/*`, `contracts/NeoHub.ExternalBridge*` |

## 19.2 合约速查

| 合约 | 类型 | 生产部署 |
| --- | --- | --- |
| `ChainRegistry` | NeoHub L1 deployable | 是 |
| `TokenRegistry` | NeoHub L1 deployable | 是 |
| `SharedBridge` | NeoHub L1 deployable | 是 |
| `SettlementManager` | NeoHub L1 deployable | 是 |
| `VerifierRegistry` | NeoHub L1 deployable | 是 |
| `ContractZkVerifier` | NeoHub L1 deployable | 是 |
| `DARegistry` | NeoHub L1 deployable | 是 |
| `DAValidator` | NeoHub L1 deployable | 是 |
| `MessageRouter` | NeoHub L1 deployable | 是 |
| `L1TxFilter` | NeoHub L1 deployable | 是 |
| `ExternalBridgeStubVerifier` | Test helper | 否 |
| `L2SystemConfigContract` | L2 native | genesis / native registry |
| `L2BridgeContract` | L2 native | genesis / native registry |

## 19.3 命令速查

| 命令 | 作用 |
| --- | --- |
| `dotnet build .\Neo.L2.sln -c Release` | 构建 .NET 全部项目 |
| `dotnet test .\Neo.L2.sln -c Release` | 运行 .NET 测试 |
| `dotnet format .\Neo.L2.sln --verify-no-changes --exclude external/` | 格式检查 |
| `mdbook build` | 构建文档站 |
| `node --test tests\experience-hub\*.test.mjs` | Experience Hub 测试 |
| `npm test` in `sdk/typescript` | TypeScript SDK 测试 |
| `neo-hub-deploy scaffold` | 生成 NeoHub plan |
| `neo-hub-deploy verify` | 验证 NeoHub artifacts / RPC |
| `neo-l2-devnet` | 本地 L2 devnet |
| `neo-l2-explore audit` | 状态根连续性审计 |

## 19.4 安全标签速查

| 标签 | 高安全含义 |
| --- | --- |
| Sequencer | 去中心化或 dBFT committee，而非单 operator |
| DA | NeoFS / L1 / 强可用 DA，而非无证明本地存储 |
| Proof | ZK / optimistic with challenge，而非 unchecked |
| Exit | permissionless exit，而非 operator assisted |
| Governance | multisig + timelock，而非单热钱包 |

## 19.5 生产禁忌

| 禁忌 | 原因 |
| --- | --- |
| 把 WIF 写进文档 | 直接泄露资金 |
| 把 NeoHub 做成 L1 native contract | 扩大 L1 core 修改面 |
| 把 EVM profile 写成 NeoX | 错误项目定位 |
| 把 test stub 进生产 plan | 证明路径失效 |
| 忽略 DA availability | 无法重放或挑战 |
| 忽略 decimals | 资产损失 |
| 提交原始 `.log` 审计输出 | 噪声和 secret 风险 |

## 19.6 继续阅读

| 主题 | 文档 |
| --- | --- |
| 架构专题 | [`../architecture-atlas.md`](../architecture-atlas.md) |
| NeoHub 工作流 | [`../neohub-architecture-and-workflows.md`](../neohub-architecture-and-workflows.md) |
| 线协议格式 | [`../architecture-wire-formats.md`](../architecture-wire-formats.md) |
| 信任边界 | [`../architecture-trust-boundaries.md`](../architecture-trust-boundaries.md) |
| L2 生命周期 | [`../architecture-l2-lifecycle.md`](../architecture-l2-lifecycle.md) |
| ZKsync 对比 | [`../zksync-comparison.md`](../zksync-comparison.md) |
| 测试覆盖 | [`../test-coverage.md`](../test-coverage.md) |
| 全栈验证 | [`../audit/full-stack-validation-2026-05-20/README.md`](../audit/full-stack-validation-2026-05-20/README.md) |

