# neo-execution-core 源码级学习指南

这份文档从 crate 的真实 `Cargo.toml`、Rust 源码文件、公开符号和测试函数生成。目标是在读实现细节之前，先弄清楚这个 crate 自己负责什么、边界在哪里、应该从哪些文件开始读。

## 这个 Crate 是什么

| 主题 | 说明 |
| --- | --- |
| 层级 | N4 批处理执行核心 |
| 目的 | 后端无关的 L2 批处理状态转换核心，供快速执行和证明生成复用。 |
| 输入 | L2 批次、前序状态根、执行参数 |
| 职责 | 校验批次结构、执行确定性状态转换、提交新状态根 |
| 输出 | 执行轨迹、新状态根、公开证明输入 |
| 使用者 | neo-zkvm-guest、neo-zkvm-host、网关服务 |

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
| `src/lib.rs` | crate 根、公开导出和顶层文档 | 0 | 0 |
| `src/types.rs` | 实现细节或辅助模块 | 7 | 0 |
| `src/hashing.rs` | 实现细节或辅助模块 | 6 | 0 |
| `tests/batch_core.rs` | 外部行为或集成测试 | 0 | 5 |
| `src/batch.rs` | 实现细节或辅助模块 | 1 | 0 |
| `src/wire.rs` | 实现细节或辅助模块 | 1 | 0 |

## 公开 API 面

| 符号 | 文件 |
| --- | --- |
| `fn execute_batch_with` | `src/batch.rs` |
| `fn merkle_root` | `src/hashing.rs` |
| `fn hash256` | `src/hashing.rs` |
| `fn hash_receipt` | `src/hashing.rs` |
| `fn fold_state_root` | `src/hashing.rs` |
| `fn apply_l1_message` | `src/hashing.rs` |
| `fn hash_public_inputs` | `src/hashing.rs` |
| `const BATCH_WIRE_VERSION` | `src/types.rs` |
| `const DEFAULT_PER_TX_GAS_LIMIT` | `src/types.rs` |
| `struct BatchResult` | `src/types.rs` |
| `struct VmExecutionReceipt` | `src/types.rs` |
| `enum ExecutionError` | `src/types.rs` |
| `struct BatchRequest` | `src/types.rs` |
| `struct L1Message` | `src/types.rs` |
| `fn parse_batch_request` | `src/wire.rs` |

## 模块与重导出信号

| 信号 |
| --- |
| `src/lib.rs: mod batch` |
| `src/lib.rs: mod hashing` |
| `src/lib.rs: mod types` |
| `src/lib.rs: mod wire` |
| `src/lib.rs: pub use batch::execute_batch_with` |
| `src/lib.rs: pub use hashing::{     apply_l1_message, fold_state_root, hash256, hash_public_inputs, hash_receipt, merkle_root, }` |
| `src/lib.rs: pub use types::{     BatchRequest, BatchResult, ExecutionError, L1Message, VmExecutionReceipt, BATCH_WIRE_VERSION,     DEFAULT_PER_TX_GAS_LIMIT, }` |
| `src/lib.rs: pub use wire::parse_batch_request` |

## 测试证据

| 测试 | 文件 |
| --- | --- |
| `executes_batch_through_backend_agnostic_receipts` | `tests/batch_core.rs` |
| `deterministic_for_same_input_and_executor` | `tests/batch_core.rs` |
| `rejects_bad_wire_inputs` | `tests/batch_core.rs` |
| `merkle_root_is_stable_and_ordered` | `tests/batch_core.rs` |
| `manifest_stays_backend_agnostic` | `tests/batch_core.rs` |

## 依赖边界

| 依赖 | 类型 |
| --- | --- |
| `sha2` | 运行时 |

## 建议阅读路径

1. 读 `src/lib.rs`：crate 根、公开导出和顶层文档。
2. 读 `src/types.rs`：实现细节或辅助模块。
3. 读 `src/hashing.rs`：实现细节或辅助模块。
4. 读 `tests/batch_core.rs`：外部行为或集成测试。
5. 读 `src/batch.rs`：实现细节或辅助模块。
6. 读 `src/wire.rs`：实现细节或辅助模块。

## 修改安全清单

- 保持职责边界不变：校验批次结构、执行确定性状态转换、提交新状态根。
- 增加或删除主要执行步骤时，同步更新工作流图和数据流图。
- 修改公开 API 或状态转换行为时，更新“测试证据”中对应的测试。
- 源码结构变化后，在 Neo N4 仓库根目录重新运行 `python tools/docs/generate_crate_visual_docs.py`。
