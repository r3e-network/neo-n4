# 第 15 章：SDK、应用与用户体验

N4 不只是节点和合约。应用开发者需要稳定 SDK、RPC、错误模型、钱包模式和可视化工具。

## 15.1 SDK 分层

| SDK | 路径 | 目标用户 |
| --- | --- | --- |
| .NET SDK | `src/Neo.L2.Sdk` | .NET 服务、工具、运维程序 |
| TypeScript SDK | `sdk/typescript` | Web / Node.js 应用 |
| Rust SDK | `sdk/rust` | Rust 服务、watcher、自动化 |
| Web Explorer | `sdk/web-explorer` | 静态 UI、演示和基础查询 |

## 15.2 RPC 设计原则

RPC 方法应该满足：

- 明确 chain id；
- 错误分类稳定；
- 返回结构可被三种 SDK 映射；
- 不要求客户端知道内部 DB 布局；
- 不暴露私钥或部署秘密。

典型错误分类：

| 错误 | 含义 |
| --- | --- |
| transport | 网络或 HTTP 失败 |
| protocol | JSON-RPC shape 错误 |
| server | L2 节点返回业务错误 |
| chain id mismatch | 客户端连接到了错误链 |

## 15.3 钱包接入模式

钱包不应该直接接触复杂部署逻辑。推荐模式：

| 模式 | 说明 |
| --- | --- |
| cold signing | CLI 输出 unsigned tx hex，钱包签名 |
| delegated signer | 运维服务拿有限权限签名 |
| multisig governance | 升级和 emergency action 多签 |
| read-only SDK | 普通 dApp 查询不需要签名 |

参考：[`../wallet-integration.md`](../wallet-integration.md)

## 15.4 应用如何发起 deposit

应用层不应该手写 L1 payload。推荐：

1. SDK 查询 token mapping；
2. SDK 检查 decimals 和 target chain；
3. wallet 签名 L1 deposit；
4. SDK 追踪 L1 tx；
5. L2 SDK 查询 deposit 是否被消费；
6. UI 显示 pending / credited / failed。

## 15.5 应用如何读取 L2 状态

推荐查询路径：

```text
App -> TypeScript/.NET/Rust SDK -> L2 RPC -> L2 plugin -> state/persistence
```

需要高安全时：

```text
App -> SDK -> L2 RPC
App -> SDK -> L1 NeoHub root
Compare local state root with accepted L1 root
```

## 15.6 Experience Hub 的角色

Experience Hub 是“教学 + 验证证据查看器”，不是生产控制台。

它应该展示：

- 架构拓扑；
- report timeline；
- DA 状态；
- prover 状态；
- L1 verification evidence；
- test evidence summary；
- private devnet vs public testnet 的边界。

它不应该做：

- 保存 WIF；
- 发送部署交易；
- 执行 emergency action；
- 伪造主网状态。

## 15.7 应用开发检查清单

| 检查 | 说明 |
| --- | --- |
| chain id 固定 | 防止连错链 |
| token decimals 正确 | 防止金额错误 |
| retry 策略按错误分类 | transport 可重试，protocol 通常不可重试 |
| 不把 proof 状态简化成 success | 区分 pending、submitted、verified、finalized |
| withdrawal 显示挑战期 | optimistic 模式下提款不是立即完成 |
| secrets 不进浏览器 | WIF/KMS/multisig 都在后端或钱包 |

