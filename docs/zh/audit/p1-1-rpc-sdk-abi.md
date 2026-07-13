# P1-1 RPC / SDK ABI 审计

本审计以 `doc.md` §14.1 为权威，对照 N4 `RpcServer` 注册端口、
`L2RpcMethods` 与四套 typed SDK。

## 规范矩阵

所有 `u32` 使用 JSON number；所有 `u64` 请求参数与响应字段使用规范十进制
JSON string；哈希使用 Neo RPC 展示字节序。

| RPC | §14.1 参数 | RpcServer adapter | 返回与绑定 | .NET | TypeScript | Rust | Python |
|---|---|---|---|---|---|---|---|
| `getl2batch` | `chainId, batchNumber` | `GetL2Batch` | batch 或 `null`；三个 u64 字段为 string | `GetBatchAsync` | `getBatch` | `get_batch` | `get_batch` |
| `getl2batchstatus` | `chainId, batchNumber` | `GetL2BatchStatus` | 绑定 chain 与 batch | `GetBatchStatusAsync` | `getBatchStatus` | `get_batch_status` | `get_batch_status` |
| `getl2stateroot` | `chainId, batchNumber?` | `GetL2StateRoot` | 省略 batch 返回最新值 | latest / at | latest / at | latest / at | latest / at |
| `getl2withdrawalproof` | `chainId, withdrawalLeafHash` | `GetL2WithdrawalProof` | proof hex 或 `null` | withdrawal proof | withdrawal proof | withdrawal proof | withdrawal proof |
| `getl2messageproof` | `chainId, messageHash` | `GetL2MessageProof` | proof hex 或 `null` | message proof | message proof | message proof | message proof |
| `getl1depositstatus` | `sourceChainId, nonce` | `GetL1DepositStatus` | 绑定 source chain 与 nonce | deposit status | deposit status | deposit status | deposit status |
| `getcanonicalasset` | `l2Asset` | `GetCanonicalAsset` | UInt160 或 `null` | canonical asset | canonical asset | canonical asset | canonical asset |
| `getbridgedasset` | `l1Asset, chainId` | `GetBridgedAsset` | 拒绝跨链参数 | bridged asset | bridged asset | bridged asset | bridged asset |
| `getsecuritylevel` | `chainId` | `GetSecurityLevel` | 单维安全等级 | security level | security level | security level | security level |
| `getsecuritylabel` | `chainId` | `GetSecurityLabel` | §16.2 五维标签 | security label | security label | security label | security label |

## 冲突处置

- state-root 的 batch 选择器改为可选，以覆盖最新值与历史值两个必要查询。
- proof 查询保留安全的链绑定；withdrawal 以 SharedBridge 实际消费的
  `withdrawalLeafHash` 为键。
- deposit 身份明确为 `(sourceChainId, nonce)`。
- `getbridgedasset` 按原规范补齐 `chainId`，并同步四 SDK。
- `getsecuritylabel` 因 §16.2 五维标签要求纳入规范面；旧接口继续兼容。
- 四 SDK 严格校验 JSON-RPC 版本、数值 id 回显、chain 与 request identity。

当前 `external/neo` 提供网络级 `RpcServerPlugin.RegisterMethods`。本仓库真实 HTTP
测试会启动 `NeoSystem` 与官方 Kestrel RpcServer 并调用全部规范方法；这只是本地
集成证据，不是公网 testnet 或部署证据。未来 core 更新必须保留等价注册端口并让
真实 HTTP 测试继续全绿，否则不得接受。
