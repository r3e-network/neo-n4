# Neo N4 Crate 可视化指南

这个索引把每个 Rust crate 链接到它自己目录下的本地可视化学习文档。每个 crate 目录都包含位置、技术原理、架构、工作流、数据流五类中英文图。

| Crate | 路径 | 层级 | 目的 |
| --- | --- | --- | --- |
| [`neo-execution-core`](../../bridge/neo-execution-core/README.zh.md) | `bridge/neo-execution-core` | N4 批处理执行核心 | 后端无关的 L2 批处理状态转换核心，供快速执行和证明生成复用。 |
| [`neo-zkvm-guest`](../../bridge/neo-zkvm-guest/README.zh.md) | `bridge/neo-zkvm-guest` | N4 零知识 guest | 在 SP1 证明环境中运行确定性 Neo L2 批处理执行的 guest 程序。 |
| [`neo-zkvm-host`](../../bridge/neo-zkvm-host/README.zh.md) | `bridge/neo-zkvm-host` | N4 零知识 host | 负责创建和检查 L2 批次证明的 SP1 宿主编排层。 |
| [`neo-external-bridge-router`](../../external/foreign-contracts/sol/programs/neo-external-bridge-router/README.zh.md) | `external/foreign-contracts/sol/programs/neo-external-bridge-router` | 异构链桥程序 | Solana 侧桥路由程序，承载 Neo N4 跨链 lock/mint/burn/unlock 流程。 |
| [`neo-riscv-abi`](../../external/neo-riscv-vm/crates/neo-riscv-abi/README.zh.md) | `external/neo-riscv-vm/crates/neo-riscv-abi` | NeoVM2 / RISC-V 执行 profile | RISC-V 执行路径共享的 ABI、栈值、codec tag 与 opcode 元数据重导出。 |
| [`neo-riscv-contract-harness`](../../external/neo-riscv-vm/crates/neo-riscv-contract-harness/README.zh.md) | `external/neo-riscv-vm/crates/neo-riscv-contract-harness` | NeoVM2 / RISC-V 执行 profile | 用于合约级 RISC-V 执行和 syscall 模拟的测试 harness。 |
| [`neo-riscv-devpack`](../../external/neo-riscv-vm/crates/neo-riscv-devpack/README.zh.md) | `external/neo-riscv-vm/crates/neo-riscv-devpack` | NeoVM2 / RISC-V 执行 profile | 用于编译和准备 RISC-V Neo 合约的开发者打包工具。 |
| [`neo-riscv-guest`](../../external/neo-riscv-vm/crates/neo-riscv-guest/README.zh.md) | `external/neo-riscv-vm/crates/neo-riscv-guest` | NeoVM2 / RISC-V 执行 profile | NeoVM2/RISC-V 合约的 guest 侧外观层与合约运行时胶水。 |
| [`neo-riscv-guest-module`](../../external/neo-riscv-vm/crates/neo-riscv-guest-module/README.zh.md) | `external/neo-riscv-vm/crates/neo-riscv-guest-module` | NeoVM2 / RISC-V 执行 profile | PolkaVM guest 模块入口，将 guest runtime 打包成可执行 RISC-V 代码。 |
| [`neo-riscv-host`](../../external/neo-riscv-vm/crates/neo-riscv-host/README.zh.md) | `external/neo-riscv-vm/crates/neo-riscv-host` | NeoVM2 / RISC-V 执行 profile | 执行 PolkaVM guest 模块、计费 gas 并桥接 syscall 的 host runtime。 |
| [`counter`](../../external/neo-riscv-vm/examples/counter/README.zh.md) | `external/neo-riscv-vm/examples/counter` | NeoVM2 / RISC-V 执行 profile | 最小状态变更 counter 合约示例。 |
| [`devpack-test`](../../external/neo-riscv-vm/examples/devpack-test/README.zh.md) | `external/neo-riscv-vm/examples/devpack-test` | NeoVM2 / RISC-V 执行 profile | 开发包冒烟测试合约。 |
| [`hello-world`](../../external/neo-riscv-vm/examples/hello-world/README.zh.md) | `external/neo-riscv-vm/examples/hello-world` | NeoVM2 / RISC-V 执行 profile | 用于 RISC-V 工具链的 hello-world 合约示例。 |
| [`nep17-token`](../../external/neo-riscv-vm/examples/nep17-token/README.zh.md) | `external/neo-riscv-vm/examples/nep17-token` | NeoVM2 / RISC-V 执行 profile | 用于 RISC-V 执行的 NEP-17 风格代币合约示例。 |
| [`storage`](../../external/neo-riscv-vm/examples/storage/README.zh.md) | `external/neo-riscv-vm/examples/storage` | NeoVM2 / RISC-V 执行 profile | 聚焦存储 syscall 行为的合约示例。 |
| [`neo-riscv-fuzz`](../../external/neo-riscv-vm/fuzz/README.zh.md) | `external/neo-riscv-vm/fuzz` | NeoVM2 / RISC-V 执行 profile | 面向 RISC-V VM 执行、ABI codec、host/guest 边界的 fuzz 支撑。 |
| [`neo-contract-template`](../../external/neo-riscv-vm/templates/contract/README.zh.md) | `external/neo-riscv-vm/templates/contract` | NeoVM2 / RISC-V 执行 profile | 创建新 Neo RISC-V 合约的起始模板。 |
| [`neo-vm-rs`](../../external/neo-vm-rs/README.zh.md) | `external/neo-vm-rs` | 共享虚拟机核心 | NeoVM 3.9.x 语义的 Rust 共享核心，供 RISC-V 与 zkVM 路径复用。 |
| [`neo-vm-guest`](../../external/neo-zkvm/crates/neo-vm-guest/README.zh.md) | `external/neo-zkvm/crates/neo-vm-guest` | zkVM guest 外观层 | 面向 zkVM guest 的适配层，以 zkVM 兼容方式暴露共享 NeoVM 执行 API。 |
| [`neo-zkvm-cli`](../../external/neo-zkvm/crates/neo-zkvm-cli/README.zh.md) | `external/neo-zkvm/crates/neo-zkvm-cli` | Neo zkVM 栈 | 用于汇编、检查、生成证明和验证 Neo zkVM 程序的 CLI 与开发工具。 |
| [`neo-zkvm-examples`](../../external/neo-zkvm/crates/neo-zkvm-examples/README.zh.md) | `external/neo-zkvm/crates/neo-zkvm-examples` | Neo zkVM 栈 | 展示常见证明流程和应用模式的可运行示例。 |
| [`neo-zkvm-program`](../../external/neo-zkvm/crates/neo-zkvm-program/README.zh.md) | `external/neo-zkvm/crates/neo-zkvm-program` | Neo zkVM 栈 | SP1 guest 二进制入口，将证明输入绑定到确定性 NeoVM 执行。 |
| [`neo-zkvm-prover`](../../external/neo-zkvm/crates/neo-zkvm-prover/README.zh.md) | `external/neo-zkvm/crates/neo-zkvm-prover` | Neo zkVM 栈 | 将 NeoVM 执行输入转换为可验证证明产物的证明生成库。 |
| [`neo-zkvm-verifier`](../../external/neo-zkvm/crates/neo-zkvm-verifier/README.zh.md) | `external/neo-zkvm/crates/neo-zkvm-verifier` | Neo zkVM 栈 | 校验证明封装、公开输出和模式兼容性的 verifier 库。 |
| [`neo-zkvm-fuzz`](../../external/neo-zkvm/fuzz/README.zh.md) | `external/neo-zkvm/fuzz` | Neo zkVM 栈 | 用于对证明与 VM 输入做对抗性探索的 fuzz 工作区。 |
| [`neo-n4-sdk`](../../sdk/rust/README.zh.md) | `sdk/rust` | 开发者 SDK | 用于构建访问 Neo N4 API 的工具和服务的 Rust SDK。 |
| [`neo-bridge-watcher-eth`](../../watchers/neo-bridge-watcher-eth/README.zh.md) | `watchers/neo-bridge-watcher-eth` | 跨链监听器 | 监听 ETH 桥事件，并转换为标准化 Neo N4 relay 消息。 |
| [`neo-bridge-watcher-sol`](../../watchers/neo-bridge-watcher-sol/README.zh.md) | `watchers/neo-bridge-watcher-sol` | 跨链监听器 | 监听 SOL 桥事件，并转换为标准化 Neo N4 relay 消息。 |
| [`neo-bridge-watcher-tron`](../../watchers/neo-bridge-watcher-tron/README.zh.md) | `watchers/neo-bridge-watcher-tron` | 跨链监听器 | 监听 TRON 桥事件，并转换为标准化 Neo N4 relay 消息。 |
