# neo-vm-rs

`neo-vm-rs` 是 Neo N4 Rust 执行 profile crate 使用的共享 NeoVM 语义与解释器 crate。它保持确定性，并兼容 `no_std + alloc`，使 PolkaVM/RISC-V runtime 与 zkVM/proving runtime 可以复用同一套 VM-facing 类型和执行行为，而不需要继承彼此的 host 或 prover stack。

## 范围

该 crate 拥有 N4 VM 消费者之间必须保持一致的公共语义：

- 规范 NeoVM opcode 元数据和 byte decoding
- 共享执行结果与 VM state 报告类型
- ABI/proof 边界使用的共享 stack value 表示
- 共享 Neo syscall hash 和固定参数数量元数据
- 共享执行限制常量
- RISC-V guest facade 使用的规范 NeoVM2 解释器入口

它不包含 host runtime、storage engine、verifier 或 prover。这些能力保留在消费方 crate 中：

- `neo-riscv-vm`：规范 N4 Layer-2 NeoVM2/RISC-V 执行 profile
- `neo-zkvm`：面向证明的 zkVM 集成与 verifier 工具

## 目录结构

- `src/vm`：opcode 元数据和执行常量
- `src/abi`：stack value 与执行结果 wire type
- `src/interpreter`：`no_std` NeoVM2 解释器、retained-state helper 和 host syscall trait
- `src/host`：syscall hash 与 stack 参数元数据

## 验证

运行与 CI 相同的命令：

```powershell
cargo fmt --all -- --check
cargo clippy --all-targets --all-features -- -D warnings
cargo test --locked --all-targets
cargo check --locked --no-default-features --all-targets
cargo bench --locked --no-run
```

测试套件覆盖规范 opcode byte 接受和 gap 拒绝、元数据 round trip、syscall hash vector、stack value 转换语义、execution result 的 serde wire 兼容性、解释器 smoke 执行、slot 处理、host syscall delegation，以及 try/catch exception flow。

benchmark target 会编译 Criterion benchmark，覆盖 opcode decode、metadata lookup、syscall helper 和 stack value conversion。CI 使用 `--no-run` 编译 benchmark；性能跟踪 job 可以在专用硬件上运行同一 target。
