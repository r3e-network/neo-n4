# 结算方法面对应性模型 — 2026-09-18

doc.md §3.2 结算方法面与已部署 RollupHub 合约 manifest（解析自重新生成的
TestingArtifacts——与 VmTests 执行所用的 NEF/manifest 相同）之间的
spec-to-implementation 对应性模型。这是手写抽象，**不是** C#/NeoVM 验证器。

## 本模型曾捕获并现已钉住的出入

运行本检查暴露了真实的 doc↔code 出入：doc.md §3.2 声明了
`revertBatch(chainId, batchNumber)` 与 `lockGovernance()`，但合约两者皆未实现。两者现已
实现（见 CHANGELOG），模型钉住已关闭的方法面：

- **每个 doc 声明的结算方法都以声明的参数数存在**：`submitBatch(4)`、
  `submitAndFinalizeBatch(4)`、`finalizeBatch(2)`、`revertBatch(2)`、
  `publishGatewayGlobalRoot`（长尾，仅查存在性）、`setGovernanceController(1)`、
  `lockGovernance(0)`、`isGovernanceLocked(0)`、`getCanonicalStateRoot(1)`、
  `isProofTypeCompatible(2)`。doc.md §3.2 的签名已更新，记录 352 字节 public-inputs 域
  封入的第 4 个 `forcedInclusionCount` 参数（见 batch-spec 模型）。
- revert/lock 不变量：`revertBatch` 取 (chainId, batchNumber)；`lockGovernance` 无参；
  `isGovernanceLocked` 可查询。
- 注册表面存在性（`registerChain`/`updateChain`/`pauseChain`/`resumeChain`）：本合约的
  锁后授权守卫是唯一权威路径，owner 无法经由缺失入口绕过。

`verify_settlement_abi.py` 共 17 项义务，全部通过。`settlement-abi-result.json` 记录
规范/脚本哈希和信任假设。三项自测：全绿、缺方法 fail-closed、缺 manifest fail-closed。

## 边界与限制

本模型覆盖**方法面对应性**——doc 声明的结算 ABI 已按声明参数数实现。它不建模方法体的
执行语义、GovernanceController 中继提案机制、签名/witness 验证或桥接 ABI（独立义务）。
锁后授权依赖对 GovernanceController 合约的 `Runtime.CheckWitness`；中继路径的执行语义
在 GovernanceController 中。

## 复现

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_settlement_abi.py
```

Windows 使用 `.venv-formal/Scripts/python`。