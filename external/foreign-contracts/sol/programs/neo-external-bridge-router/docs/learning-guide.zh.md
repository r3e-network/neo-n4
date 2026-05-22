# neo-external-bridge-router 源码级学习指南

这份文档从 crate 的真实 `Cargo.toml`、Rust 源码文件、公开符号和测试函数生成。目标是在读实现细节之前，先弄清楚这个 crate 自己负责什么、边界在哪里、应该从哪些文件开始读。

## 这个 Crate 是什么

| 主题 | 说明 |
| --- | --- |
| 层级 | 异构链桥程序 |
| 目的 | Solana 侧桥路由程序，承载 Neo N4 跨链 lock/mint/burn/unlock 流程。 |
| 输入 | Solana 指令、代币账户状态、桥权限账户 |
| 职责 | 校验路由、移动托管资产、发出桥事件 |
| 输出 | 桥事件、托管状态变化、relay 证据 |
| 使用者 | watcher-sol、网关、共享桥 |

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
| `src/lib.rs` | crate 根、公开导出和顶层文档 | 15 | 21 |

## 公开 API 面

| 符号 | 文件 |
| --- | --- |
| `fn initialize` | `src/lib.rs` |
| `fn set_committee` | `src/lib.rs` |
| `fn lock_sol_and_send` | `src/lib.rs` |
| `fn finalize_withdrawal` | `src/lib.rs` |
| `struct Initialize` | `src/lib.rs` |
| `struct SetCommittee` | `src/lib.rs` |
| `struct LockSolAndSend` | `src/lib.rs` |
| `struct FinalizeWithdrawal` | `src/lib.rs` |
| `struct BridgeState` | `src/lib.rs` |
| `fn space` | `src/lib.rs` |
| `struct ConsumedNonce` | `src/lib.rs` |
| `const SPACE` | `src/lib.rs` |
| `struct LockedEvent` | `src/lib.rs` |
| `struct WithdrawalFinalizedEvent` | `src/lib.rs` |
| `enum BridgeError` | `src/lib.rs` |

## 模块与重导出信号

未扫描到 `mod` 或 `pub use` 声明。

## 测试证据

| 测试 | 文件 |
| --- | --- |
| `sigverify_parser_accepts_same_instruction_offsets` | `src/lib.rs` |
| `sigverify_parser_rejects_cross_instruction_message_offset` | `src/lib.rs` |
| `sigverify_parser_rejects_cross_instruction_pubkey_offset` | `src/lib.rs` |
| `validate_committee_accepts_well_formed` | `src/lib.rs` |
| `validate_committee_rejects_empty` | `src/lib.rs` |
| `validate_committee_rejects_too_large` | `src/lib.rs` |
| `validate_committee_rejects_zero_threshold` | `src/lib.rs` |
| `validate_committee_rejects_threshold_above_size` | `src/lib.rs` |
| `validate_committee_rejects_duplicate_member` | `src/lib.rs` |
| `validate_committee_accepts_unanimity_threshold` | `src/lib.rs` |
| `validate_committee_accepts_max_size` | `src/lib.rs` |
| `read_u32_le_happy_path` | `src/lib.rs` |
| `read_u32_le_returns_zero_on_underflow` | `src/lib.rs` |
| `read_u64_le_happy_path` | `src/lib.rs` |
| `read_u64_le_returns_zero_on_underflow` | `src/lib.rs` |
| `read_uint_le_variable_length` | `src/lib.rs` |
| `read_for_seeds_under_length_returns_zero` | `src/lib.rs` |
| `canonical_message_offsets_are_pinned` | `src/lib.rs` |
| `canonical_message_layout_round_trips` | `src/lib.rs` |
| `direction_constants_disjoint` | `src/lib.rs` |
| `message_type_constants` | `src/lib.rs` |

## 依赖边界

| 依赖 | 类型 |
| --- | --- |
| `anchor-lang` | 运行时 |
| `solana-instructions-sysvar` | 运行时 |
| `solana-sdk-ids` | 运行时 |

## 建议阅读路径

1. 读 `src/lib.rs`：crate 根、公开导出和顶层文档。

## 修改安全清单

- 保持职责边界不变：校验路由、移动托管资产、发出桥事件。
- 增加或删除主要执行步骤时，同步更新工作流图和数据流图。
- 修改公开 API 或状态转换行为时，更新“测试证据”中对应的测试。
- 源码结构变化后，在 Neo N4 仓库根目录重新运行 `python tools/docs/generate_crate_visual_docs.py`。
