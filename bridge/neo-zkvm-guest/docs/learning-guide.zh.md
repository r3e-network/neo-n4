# neo-zkvm-guest 源码级学习指南

这份文档从 crate 的真实 `Cargo.toml`、Rust 源码文件、公开符号和测试函数生成。目标是在读实现细节之前，先弄清楚这个 crate 自己负责什么、边界在哪里、应该从哪些文件开始读。

## 这个 Crate 是什么

| 主题 | 说明 |
| --- | --- |
| 层级 | N4 零知识 guest |
| 目的 | 在 SP1 证明环境中运行确定性 Neo L2 批处理执行的 guest 程序。 |
| 输入 | 公开批次输入、私有见证、共享执行核心 |
| 职责 | 运行可验证状态转换、输出公开值、拒绝非确定性行为 |
| 输出 | SP1 公开输出、状态根、执行摘要 |
| 使用者 | neo-zkvm-host、NativeZkVerifier 适配器、审计工具 |

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
| `src/lib.rs` | crate 根、公开导出和顶层文档 | 1 | 7 |
| `src/main.rs` | 二进制或 CLI 入口 | 1 | 0 |

## 公开 API 面

| 符号 | 文件 |
| --- | --- |
| `fn execute_batch` | `src/lib.rs` |
| `fn main` | `src/main.rs` |

## 模块与重导出信号

| 信号 |
| --- |
| `src/lib.rs: pub use neo_execution_core::{     execute_batch_with, hash256, merkle_root, BatchResult, ExecutionError, DEFAULT_PER_TX_GAS_LIMIT, }` |

## 测试证据

| 测试 | 文件 |
| --- | --- |
| `parse_then_execute_minimal` | `src/lib.rs` |
| `determinism_same_input_same_output` | `src/lib.rs` |
| `truncated_input_rejected` | `src/lib.rs` |
| `unsupported_version_rejected` | `src/lib.rs` |
| `merkle_root_single_leaf_is_leaf` | `src/lib.rs` |
| `merkle_root_empty_is_zero` | `src/lib.rs` |
| `merkle_root_changes_with_leaf_order` | `src/lib.rs` |

## 依赖边界

| 依赖 | 类型 |
| --- | --- |
| `neo-execution-core` | 运行时 |
| `neo-vm-guest` | 运行时 |
| `sp1-zkvm` | 运行时 |

## 建议阅读路径

1. 读 `src/lib.rs`：crate 根、公开导出和顶层文档。
2. 读 `src/main.rs`：二进制或 CLI 入口。

## 修改安全清单

- 保持职责边界不变：运行可验证状态转换、输出公开值、拒绝非确定性行为。
- 增加或删除主要执行步骤时，同步更新工作流图和数据流图。
- 修改公开 API 或状态转换行为时，更新“测试证据”中对应的测试。
- 源码结构变化后，在 Neo N4 仓库根目录重新运行 `python tools/docs/generate_crate_visual_docs.py`。
