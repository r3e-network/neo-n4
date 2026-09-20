# 桥接方法面对应性模型 — 2026-09-18

doc.md §10/§11 桥接方法面与已部署 manifest 之间的 spec-to-implementation 对应性模型。
doc 声明的是单一逻辑桥接方法清单；实现将其拆分到 **L1 侧 SharedBridge 合约**（解析自
重新生成的 TestingArtifacts manifest）与 **L2 侧原生桥**（external/neo
`L2NativeContracts.cs`，源码锚定）。这是手写抽象，**不是** C#/NeoVM 验证器。

## 对应表钉住的内容

每个 doc 声明的方法都记录在其文档化的位置与参数数：

- SharedBridge：`registerMapping(1)`、`getL2Asset(2)`、`deposit(4)`、
  `publishMessageRoots(4)`、`sendMessage(3)`、`isL2ToL1MessageConsumed(1)`、
  `finalizeWithdrawal(9)`。
- L2 原生桥（源码锚定）：`GetL1Asset`、`GetL2Asset`。

文档化的重命名/合并/超集被钉住，漂移历史不丢失：

- `isMessageConsumed` → **重命名**为 `isL2ToL1MessageConsumed`（若重新引入过时名将被
  标记为漂移）。
- `routeMessage` + `enqueueL1ToL2Message` → **合并**为 `sendMessage`。
- `finalizeWithdrawal` 的 doc 参数数（4）被 V5 叶哈希绑定**有意超集**（9；At/WithProof/
  Emergency 变体 10/12/12）；四个变体必须都存在。
- `deposit` 参数数（4）与 doc 一致；参数**顺序**不同（asset 在前 vs targetChainId 在前），
  已记录在 doc.md §10。

`verify_bridge_abi.py` 共 16 项义务，全部通过。`bridge-abi-result.json` 记录规范/脚本
哈希和信任假设。四项自测：全绿、缺方法 fail-closed、重命名漂移捕获、缺 manifest
fail-closed。

## 边界与限制

本模型覆盖**方法面对应性**——doc 声明的桥接 ABI 已在其文档化位置以文档化参数数实现。
它不建模存取款状态机（bridge-conservation 模型）、提款叶 Merkle 绑定（batch-spec/
preimage 模型 + VmTests）或外部链（EVM/Tron/Solana）watcher 架构。doc.md §10 的方法
清单已在同一变更集中更新为实现面；doc 时代的名称在此保留为重命名/合并历史。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_bridge_abi.py
```

Windows 使用 `.venv-formal/Scripts/python`。