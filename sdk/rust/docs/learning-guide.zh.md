# neo-n4-sdk 源码级学习指南

这份文档从 crate 的真实 `Cargo.toml`、Rust 源码文件、公开符号和测试函数生成。目标是在读实现细节之前，先弄清楚这个 crate 自己负责什么、边界在哪里、应该从哪些文件开始读。

## 这个 Crate 是什么

| 主题 | 说明 |
| --- | --- |
| 层级 | 开发者 SDK |
| 目的 | 用于构建访问 Neo N4 API 的工具和服务的 Rust SDK。 |
| 输入 | 开发者应用、网关端点、钱包/配置 |
| 职责 | 编码 API 请求、处理桥/证明模型、返回强类型结果 |
| 输出 | 强类型客户端结果、交易请求、查询响应 |
| 使用者 | 应用、运维工具、集成测试 |

## 可视化阅读顺序

| 步骤 | 图 | 用它学习什么 |
| ---: | --- | --- |
| 1 | [位置图](figures/position.zh.svg) | 这个 crate 为什么存在、在 Neo N4 中处于哪里。 |
| 2 | [技术原理图](figures/principles.zh.svg) | 这个 crate 必须保护的不变量和职责边界。 |
| 3 | [模块图](figures/module-map.zh.svg) | 哪些源码文件是最好的入口。 |
| 4 | [公开 API 图](figures/api-surface.zh.svg) | 哪些导出符号构成 crate 契约。 |
| 5 | [架构图](figures/architecture.zh.svg) | 输入、内部组件、依赖和输出如何连接。 |
| 6 | [工作流图](figures/workflow.zh.svg) | 正常执行路径。 |
| 7 | [数据流图](figures/dataflow.zh.svg) | 数据如何跨越 crate 边界并被转换。 |
| 8 | [测试证据图](figures/test-map.zh.svg) | 哪些测试保护行为。 |
| 9 | [依赖图](figures/dependency-map.zh.svg) | 哪些依赖是运行时、测试或构建期依赖。 |

## 源码文件地图

| 文件 | 作用 | 公开符号 | 测试 |
| --- | --- | ---: | ---: |
| `src/lib.rs` | crate 根、公开导出和顶层文档 | 40 | 0 |
| `tests/integration.rs` | 外部行为或集成测试 | 0 | 10 |

## 公开 API 面

| 符号 | 文件 |
| --- | --- |
| `enum SecurityLevel` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum DAMode` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum SequencerModel` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum ExitModel` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum ProofType` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `enum BatchStatus` | `src/lib.rs` |
| `fn from_u8` | `src/lib.rs` |
| `struct L2BatchView` | `src/lib.rs` |
| `fn proof_type` | `src/lib.rs` |
| `struct BatchStatusResponse` | `src/lib.rs` |
| `fn status` | `src/lib.rs` |
| `struct DepositStatusResponse` | `src/lib.rs` |
| `struct SecurityLevelResponse` | `src/lib.rs` |
| `fn level` | `src/lib.rs` |
| `struct SecurityLabelResponse` | `src/lib.rs` |
| `fn security_level` | `src/lib.rs` |
| `fn da_mode` | `src/lib.rs` |
| `fn sequencer` | `src/lib.rs` |
| `fn exit` | `src/lib.rs` |
| `enum L2RpcError` | `src/lib.rs` |
| `type Result` | `src/lib.rs` |
| `struct L2RpcClient` | `src/lib.rs` |
| `fn new` | `src/lib.rs` |
| `fn chain_id` | `src/lib.rs` |
| `fn get_batch` | `src/lib.rs` |
| `fn get_batch_status` | `src/lib.rs` |
| `fn get_latest_state_root` | `src/lib.rs` |
| `fn get_state_root_at` | `src/lib.rs` |
| `fn get_withdrawal_proof` | `src/lib.rs` |
| `fn get_message_proof` | `src/lib.rs` |
| `fn get_deposit_status` | `src/lib.rs` |
| `fn get_canonical_asset` | `src/lib.rs` |
| `fn get_bridged_asset` | `src/lib.rs` |
| `fn get_security_level` | `src/lib.rs` |
| `fn get_security_label` | `src/lib.rs` |

## 模块与重导出信号

未扫描到 `mod` 或 `pub use` 声明。

## 测试证据

| 测试 | 文件 |
| --- | --- |
| `ctor_rejects_zero_chain_id` | `tests/integration.rs` |
| `ctor_rejects_non_http_scheme` | `tests/integration.rs` |
| `ctor_rejects_invalid_url` | `tests/integration.rs` |
| `get_latest_state_root_returns_string` | `tests/integration.rs` |
| `get_security_label_decodes_all_dimensions` | `tests/integration.rs` |
| `get_withdrawal_proof_decodes_hex` | `tests/integration.rs` |
| `get_withdrawal_proof_null_returns_none` | `tests/integration.rs` |
| `server_error_surfaces_with_code` | `tests/integration.rs` |
| `http_502_surfaces_as_transport_error` | `tests/integration.rs` |
| `mismatched_chain_id_surfaces_as_mismatch_error` | `tests/integration.rs` |

## 依赖边界

| 依赖 | 类型 |
| --- | --- |
| `reqwest` | 运行时 |
| `serde` | 运行时 |
| `serde_json` | 运行时 |
| `thiserror` | 运行时 |
| `tokio` | 运行时 |
| `mockito` | 测试 |
| `tokio` | 测试 |

## 建议阅读路径

1. 读 `src/lib.rs`：crate 根、公开导出和顶层文档。
2. 读 `tests/integration.rs`：外部行为或集成测试。

## 修改安全清单

- 保持职责边界不变：编码 API 请求、处理桥/证明模型、返回强类型结果。
- 增加或删除主要执行步骤时，同步更新工作流图和数据流图。
- 修改公开 API 或状态转换行为时，更新“测试证据”中对应的测试。
- 源码结构变化后，在 Neo N4 仓库根目录重新运行 `python tools/docs/generate_crate_visual_docs.py`。
