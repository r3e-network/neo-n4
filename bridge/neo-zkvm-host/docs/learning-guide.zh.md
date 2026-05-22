# neo-zkvm-host 源码级学习指南

这份文档从 crate 的真实 `Cargo.toml`、Rust 源码文件、公开符号和测试函数生成。目标是在读实现细节之前，先弄清楚这个 crate 自己负责什么、边界在哪里、应该从哪些文件开始读。

## 这个 Crate 是什么

| 主题 | 说明 |
| --- | --- |
| 层级 | N4 零知识 host |
| 目的 | 负责创建和检查 L2 批次证明的 SP1 宿主编排层。 |
| 输入 | L2 批次、guest ELF、prover 配置 |
| 职责 | 准备 SP1 输入、运行 prover、校验证明封装 |
| 输出 | 证明字节、验证报告、状态承诺 |
| 使用者 | 桥接 relayer、L1 verifier 适配器、devnet 脚本 |

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
| `src/lib.rs` | crate 根、公开导出和顶层文档 | 10 | 0 |
| `tests/end_to_end.rs` | 外部行为或集成测试 | 0 | 3 |
| `build.rs` | 实现细节或辅助模块 | 0 | 0 |
| `src/bin/prove_batch.rs` | 额外二进制入口 | 0 | 0 |

## 公开 API 面

| 符号 | 文件 |
| --- | --- |
| `const NEO_ZKVM_GUEST_ELF` | `src/lib.rs` |
| `const NEO_ZKVM_GUEST_ELF` | `src/lib.rs` |
| `struct ExecutionResult` | `src/lib.rs` |
| `struct ProofResult` | `src/lib.rs` |
| `fn execute` | `src/lib.rs` |
| `fn prove` | `src/lib.rs` |
| `fn verify` | `src/lib.rs` |
| `fn execute` | `src/lib.rs` |
| `fn prove` | `src/lib.rs` |
| `fn verify` | `src/lib.rs` |

## 模块与重导出信号

未扫描到 `mod` 或 `pub use` 声明。

## 测试证据

| 测试 | 文件 |
| --- | --- |
| `execute_guest_in_zkvm_matches_host_run` | `tests/end_to_end.rs` |
| `prove_and_verify_real_zk_proof` | `tests/end_to_end.rs` |
| `verify_rejects_mismatched_public_input_hash` | `tests/end_to_end.rs` |

## 依赖边界

| 依赖 | 类型 |
| --- | --- |
| `neo-zkvm-guest` | 测试 |
| `serial_test` | 测试 |

## 建议阅读路径

1. 读 `src/lib.rs`：crate 根、公开导出和顶层文档。
2. 读 `tests/end_to_end.rs`：外部行为或集成测试。
3. 读 `build.rs`：实现细节或辅助模块。
4. 读 `src/bin/prove_batch.rs`：额外二进制入口。

## 修改安全清单

- 保持职责边界不变：准备 SP1 输入、运行 prover、校验证明封装。
- 增加或删除主要执行步骤时，同步更新工作流图和数据流图。
- 修改公开 API 或状态转换行为时，更新“测试证据”中对应的测试。
- 源码结构变化后，在 Neo N4 仓库根目录重新运行 `python tools/docs/generate_crate_visual_docs.py`。
