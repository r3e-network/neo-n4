# neo-bridge-watcher-tron 源码级学习指南

这份文档从 crate 的真实 `Cargo.toml`、Rust 源码文件、公开符号和测试函数生成。目标是在读实现细节之前，先弄清楚这个 crate 自己负责什么、边界在哪里、应该从哪些文件开始读。

## 这个 Crate 是什么

| 主题 | 说明 |
| --- | --- |
| 层级 | 跨链监听器 |
| 目的 | 监听 TRON 桥事件，并转换为标准化 Neo N4 relay 消息。 |
| 输入 | TRON RPC/log 流、桥合约事件、检查点游标 |
| 职责 | 过滤桥事件、规范化 payload、保护重放与游标状态 |
| 输出 | relay 任务、审计日志、健康指标 |
| 使用者 | 网关、共享桥、运维面板 |

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
| 10 | [实现全景图](figures/implementation-atlas.zh.svg) | 用一张高密度图同时理解用途、源码入口、API、工作流、数据流、依赖、测试和修改检查点。 |

## 源码文件地图

| 文件 | 作用 | 公开符号 | 测试 |
| --- | --- | ---: | ---: |
| `src/lib.rs` | crate 根、公开导出和顶层文档 | 3 | 2 |
| `tests/parity.rs` | 外部行为或集成测试 | 0 | 4 |

## 公开 API 面

| 符号 | 文件 |
| --- | --- |
| `const TRON_MAINNET_CHAIN_ID` | `src/lib.rs` |
| `const TRON_NILE_TESTNET_CHAIN_ID` | `src/lib.rs` |
| `const TRON_SHASTA_TESTNET_CHAIN_ID` | `src/lib.rs` |

## 模块与重导出信号

| 信号 |
| --- |
| `src/lib.rs: pub use neo_bridge_watcher_eth::chains` |
| `src/lib.rs: pub use neo_bridge_watcher_eth::*` |

## 测试证据

| 测试 | 文件 |
| --- | --- |
| `tron_chain_ids_have_foreign_namespace_prefix` | `src/lib.rs` |
| `tron_chain_ids_disjoint_from_eth_and_solana` | `src/lib.rs` |
| `canonical_bytes_emit_tron_chain_id_at_offset_zero` | `tests/parity.rs` |
| `canonical_bytes_diverge_from_eth_only_at_chain_id_position` | `tests/parity.rs` |
| `message_hash_differs_from_eth_for_same_other_fields` | `tests/parity.rs` |
| `fixed_prefix_still_102_bytes` | `tests/parity.rs` |

## 依赖边界

| 依赖 | 类型 |
| --- | --- |
| `hex` | 运行时 |
| `neo-bridge-watcher-eth` | 运行时 |
| `sha2` | 测试 |

## 建议阅读路径

1. 读 `src/lib.rs`：crate 根、公开导出和顶层文档。
2. 读 `tests/parity.rs`：外部行为或集成测试。

## 修改安全清单

- 保持职责边界不变：过滤桥事件、规范化 payload、保护重放与游标状态。
- 增加或删除主要执行步骤时，同步更新工作流图和数据流图。
- 修改公开 API 或状态转换行为时，更新“测试证据”中对应的测试。
- 源码结构变化后，在 Neo N4 仓库根目录重新运行 `python tools/docs/generate_crate_visual_docs.py`。
