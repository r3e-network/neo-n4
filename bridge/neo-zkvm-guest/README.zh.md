# neo-zkvm-guest

该 Rust crate 同时提供两个共享同一 execution core 与 NeoVM runtime 的入口：

- `neo-zkvm-guest`：编译成 SP1 RISC-V ELF，在 zkVM 内重算完整状态转换并 commit
  public-input hash；这是证明者实际证明的程序。
- `neo-zkvm-executor`：host-native one-shot executor，供 C# 排序器计算与 guest
  字节一致的 transition，避免维护另一套 C# 执行语义。

生产数据流固定为：

```text
SealedBatch
  -> NEO4EXEC + complete pre-state NEO4STW1
  -> SHA-256 pinned neo-zkvm-executor
  -> validated NEO4EXR1
  -> atomic complete post-state commit + canonical NEO4PWIT
  -> prove-batch daemon -> SP1 proof -> L1 verifier
```

host-native executor 的接口是：

```text
--payload <规范 NEO4EXEC>
--state-witness <完整规范 pre-state NEO4STW1>
--output <create-new 规范 NEO4EXR1>
```

`NEO4EXR1` 绑定两份精确请求字节的 Hash256、固定 SP1 stateful NeoVM V1 semantic、
全部执行 roots 与 gas、完整 `NEO4EFX1`、完整 post-state `NEO4STW1`、结算
public-input hash 和域分隔尾部 content Hash256。C# 在一次原子完整状态替换前校验全部
绑定。该输出只是执行交接，不是 validity proof；生成的 `NEO4PWIT` 仍必须由 SP1 证明
并在 L1 验证。

N4 genesis V1 只支持有界 native/syscall profile，且一次 transition 内 deployed-contract
descriptor 集合不可增删替换；未覆盖行为 fail closed。扩大能力必须协调升级版本化 guest、
native executor、VK、verifier route 与跨语言向量。

<!-- N4-CRATE-VISUAL-GUIDE-ZH:START -->
## 技术可视化指南

这些图都放在本 crate 目录下，用技术架构视角解释 `neo-zkvm-guest`。重点是系统位置、技术原理、数据移动、工作流、状态、证明/证据、信任边界、集成关系和运行生命周期。

完整技术解释见 [docs/learning-guide.zh.md](docs/learning-guide.zh.md)。

| 视图 | 图 | Mermaid |
| --- | --- | --- |
| 系统位置图 | ![系统位置图](docs/figures/position.zh.svg) | [Mermaid](docs/figures/position.zh.mmd) |
| 技术原理图 | ![技术原理图](docs/figures/principles.zh.svg) | [Mermaid](docs/figures/principles.zh.mmd) |
| 概念架构图 | ![概念架构图](docs/figures/architecture.zh.svg) | [Mermaid](docs/figures/architecture.zh.mmd) |
| 工作流图 | ![工作流图](docs/figures/workflow.zh.svg) | [Mermaid](docs/figures/workflow.zh.mmd) |
| 数据流图 | ![数据流图](docs/figures/dataflow.zh.svg) | [Mermaid](docs/figures/dataflow.zh.mmd) |
| 状态模型图 | ![状态模型图](docs/figures/state-model.zh.svg) | [Mermaid](docs/figures/state-model.zh.mmd) |
| 证明与证据流图 | ![证明与证据流图](docs/figures/proof-flow.zh.svg) | [Mermaid](docs/figures/proof-flow.zh.mmd) |
| 信任边界图 | ![信任边界图](docs/figures/trust-boundaries.zh.svg) | [Mermaid](docs/figures/trust-boundaries.zh.mmd) |
| 集成关系图 | ![集成关系图](docs/figures/integration-map.zh.svg) | [Mermaid](docs/figures/integration-map.zh.mmd) |
| 运行生命周期图 | ![运行生命周期图](docs/figures/lifecycle.zh.svg) | [Mermaid](docs/figures/lifecycle.zh.mmd) |

### 技术角色

- **层级:** N4 零知识 guest
- **目的:** 在 SP1 证明环境中运行确定性 Neo L2 批处理执行的 guest 程序。
- **输入:** 公开批次输入 | 私有见证 | 共享执行核心
- **职责:** 运行可验证状态转换 | 输出公开值 | 拒绝非确定性行为
- **输出:** SP1 公开输出 | 状态根 | 执行摘要
- **消费方:** neo-zkvm-host | NativeZkVerifier 适配器 | 审计工具

### 阅读顺序

1. 先看系统位置图和概念架构图。
2. 再看技术原理图、信任边界图和状态模型图，理解为什么这样设计是正确的。
3. 然后看工作流图和数据流图，理解运行时如何移动。
4. 最后看证明/证据流、集成关系和生命周期，理解系统如何进入真实运行。
<!-- N4-CRATE-VISUAL-GUIDE-ZH:END -->

## 可复现生产构建

生产 guest ELF 必须通过 Docker 构建，并锁定经审计的 SP1 6.2.1 amd64 镜像
digest。宿主机原生构建会把路径或编译环境差异带入 ELF/VK，不能用于更新生产清单。

```bash
cd bridge/neo-zkvm-guest
export SP1_DOCKER_IMAGE=ghcr.io/succinctlabs/sp1@sha256:14d3c46eff7492f87e429bfbf618e3d33499ba7515b15c36eeb1bcaebc9f7b7f
cargo prove build --docker --locked
```

host-native executor 使用锁文件构建：

```bash
cargo build --release --locked -p neo-zkvm-guest --bin neo-zkvm-executor
# → target/release/neo-zkvm-executor
```

运维者必须把该 release binary 的 SHA-256 写入独立审阅/签名的 release manifest。
`Sp1StatefulBatchExecutor` 每次调用都边复制边校验，只执行 digest 匹配的隔离副本。
