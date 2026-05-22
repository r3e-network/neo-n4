# neo-zkvm-host

<!-- N4-CRATE-VISUAL-GUIDE-ZH:START -->

## 可视化学习指南

这些图是 `neo-zkvm-host` 自己目录下的 crate 专属学习资料，用来说明它在 Neo N4 中的位置、自己负责的技术边界、内部工作流，以及数据如何流经它。

完整的源码级解释见 [docs/learning-guide.zh.md](docs/learning-guide.zh.md)。

| 视图 | 图片 | 源文件 |
| --- | --- | --- |
| 在 Neo N4 中的位置 | ![位置](docs/figures/position.zh.svg) | [Mermaid](docs/figures/position.zh.mmd) |
| 技术原理 | ![技术原理](docs/figures/principles.zh.svg) | [Mermaid](docs/figures/principles.zh.mmd) |
| 架构 | ![架构](docs/figures/architecture.zh.svg) | [Mermaid](docs/figures/architecture.zh.mmd) |
| 工作流 | ![工作流](docs/figures/workflow.zh.svg) | [Mermaid](docs/figures/workflow.zh.mmd) |
| 数据流 | ![数据流](docs/figures/dataflow.zh.svg) | [Mermaid](docs/figures/dataflow.zh.mmd) |
| 模块图 | ![模块图](docs/figures/module-map.zh.svg) | [Mermaid](docs/figures/module-map.zh.mmd) |
| 公开 API 图 | ![公开 API 图](docs/figures/api-surface.zh.svg) | [Mermaid](docs/figures/api-surface.zh.mmd) |
| 测试证据图 | ![测试证据图](docs/figures/test-map.zh.svg) | [Mermaid](docs/figures/test-map.zh.mmd) |
| 依赖图 | ![依赖图](docs/figures/dependency-map.zh.svg) | [Mermaid](docs/figures/dependency-map.zh.mmd) |
| 实现全景图 | ![实现全景图](docs/figures/implementation-atlas.zh.svg) | [Mermaid](docs/figures/implementation-atlas.zh.mmd) |

### 在 Neo N4 中的作用

- **层级:** N4 零知识 host
- **目的:** 负责创建和检查 L2 批次证明的 SP1 宿主编排层。
- **主要输入:** L2 批次、guest ELF、prover 配置
- **主要输出:** 证明字节、验证报告、状态承诺
- **下游使用者:** 桥接 relayer、L1 verifier 适配器、devnet 脚本
- **扫描到的源码文件:** 4
- **扫描到的公开符号:** 10
- **扫描到的 Rust 测试:** 3

### 边界与职责

- **本 crate 负责:** 准备 SP1 输入、运行 prover、校验证明封装
- **本 crate 消费:** L2 批次、guest ELF、prover 配置
- **本 crate 产出:** 证明字节、验证报告、状态承诺
- **主要被谁使用:** 桥接 relayer、L1 verifier 适配器、devnet 脚本

### 源码地图快照

| 文件 | 为什么重要 | 公开 API | 测试 |
| --- | --- | ---: | ---: |
| `src/lib.rs` | crate 根、公开导出和顶层文档 | 10 | 0 |
| `tests/end_to_end.rs` | 外部行为或集成测试 | 0 | 3 |
| `build.rs` | 实现细节或辅助模块 | 0 | 0 |
| `src/bin/prove_batch.rs` | 额外二进制入口 | 0 | 0 |

### API 快照

| 类型 | 代表符号 |
| --- | --- |
| 类型 | ExecutionResult <br> ProofResult |
| 函数 | execute <br> prove <br> verify |
| Trait | 未扫描到公开符号 |
| 常量 | NEO_ZKVM_GUEST_ELF |

### 学习路径

1. 先看位置图，明确这个 crate 为什么存在、上游是谁、下游是谁。
2. 再看技术原理图，理解它的核心不变量、职责边界和维护规则。
3. 然后看模块图和 API 图，确定先读哪些文件、哪些符号。
4. 最后看工作流、数据流、测试证据图和依赖图，再进入源码会更容易理解。
5. 如果希望一张图看完整体，就看实现全景图；它把源码入口、API、数据流、测试、依赖和修改检查点放在一起。

<!-- N4-CRATE-VISUAL-GUIDE-ZH:END -->
