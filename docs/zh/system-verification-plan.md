# Neo N4 系统化验证计划

这份计划把 Neo N4 的验证拆成“模块 × 门禁 × 证据”的执行矩阵。它不是替代
`doc.md`，而是用来证明当前实现仍然符合架构、安全模型、运维体验和生产发布要求。

## 验证目标

- 每个 `doc.md` 子系统都必须有至少一个可执行门禁。
- 区分日常快速验证、发布验证和生产上线验证。
- 每个模块都留下可审计证据：命令、环境、输出路径、通过/失败、责任人。
- 同时验证正确性和生产完备性：合约、证明、工具链、SDK、文档、供应链、私网演练和已知 gap。

## 门禁等级

| 等级 | 名称 | 运行时机 | 目标 |
| --- | --- | --- | --- |
| L0 | 烟雾测试 | 每轮本地迭代 | 快速确认构建、主测试和 devnet 没坏。 |
| L1 | 日常全量 | 交付 / PR 前 | 第一方 .NET + 常规跨语言验证。 |
| L2 | 发布门禁 | tag / 部署前 | 合约 artifact、SP1、外链桥、覆盖率、私网关键路径。 |
| L3 | 生产就绪 | 主网上线前 | 钱包/HSM、治理、DA 留存、事故演练、真实 RPC/指标。 |

## 分模块验证矩阵

| 模块 | 范围 | L0 / L1 门禁 | L2 / L3 门禁 | 通过证据 |
| --- | --- | --- | --- | --- |
| 架构与文档 | `doc.md`、`docs/`、`README.md`、状态矩阵 | `mdbook build`；本地链接检查；当前测试数一致性 | 中英文对照；架构到代码映射复核 | mdBook 输出、断链日志、文档 diff |
| .NET 基线 | `Neo.L2.sln`、全部第一方项目 | `dotnet build`；`dotnet test` | `dotnet format --verify-no-changes`；覆盖率门禁 | build/test 日志、coverage JSON |
| 协议原语 | `Neo.L2.Abstractions`、canonical encoders、wire formats | 枚举判别值、序列化、payload 边界测试 | Rust / Solidity / SDK 字节向量一致性 | 单测日志、向量文件 |
| 批次 / 状态 / 执行 | `Batch`、`State`、`Executor`、`Executor.RiscV` | 单测；默认 devnet；executor seam 测试 | 持久化 rehydrate；`--executor riscv`；长随机 / invariant 测试 | devnet report、state-root 连续性日志 |
| NeoHub L1 合约 | `contracts/NeoHub.*` | 每个合约 `dotnet build` | 安装 `nccs`；生成 `.nef` + `.manifest.json`；manifest invariant；部署计划固定测试 | artifact 目录、deploy plan、manifest 测试 |
| Neo core L2 native | `external/neo` 的 L2 native contracts | `UT_L2NativeContracts` 过滤测试 | core fork policy 复核；native contract 回放 | Neo core 测试日志 |
| 桥与消息 | `Bridge`、`Messaging`、`AssetMapping`、`SharedBridge` | deposit / withdrawal / replay / root 单测和集成测试 | 跨语言向量；提款 Merkle proof；外链桥本地演练 | bridge 日志、向量文件 |
| DA 与持久化 | `Persistence`、`L2DA`、RocksDB、NeoFS-like writer | DA 单测；默认 in-memory devnet | 4 个 sample config devnet；DA retrieval / retention 演练 | DA report、rehydrate 输出 |
| 证明系统 | `Neo.L2.Proving`、`bridge/neo-zkvm-*`、`external/neo-zkvm` | .NET prover 单测；zkVM guest host-mode 测试 | `cargo prove build`；host release tests；ignored real-proof tests | proof bytes、VK、tamper rejection 日志 |
| RISC-V VM 栈 | `external/neo-riscv-vm`、C# P/Invoke seam | `cargo check -p neo-riscv-host`；`Executor.RiscV.UnitTests` | release host library；devnet `--executor riscv`；FFI 边界压力 | cargo 日志、native lib hash、devnet 输出 |
| Gateway 聚合 | `Neo.Plugins.L2Gateway`、`BinaryTreeAggregator`、`IRoundProver` | Gateway 单测 | 多 L2 聚合场景；替换 round prover 演练 | 聚合 proof 日志 |
| Operator 工具 | `tools/*`、`neo-stack`、deploy / bridge / faucet / explore / devnet | CLI 单测；`neo-stack` scaffold smoke | `run-local-rehearsal.ps1`；私网脚本 | 生成链目录、rehearsal report |
| SDK 与 UI | `.NET SDK`、TypeScript、Rust、Python、Experience Hub | .NET SDK 单测；vitest；Rust SDK cargo test；Python pytest；Node tests | 对 devnet RPC 做 API drift replay；SDK parity matrix | SDK 日志、API snapshot |
| 外链集成 | `watchers/*`、`external/foreign-contracts/*` | Rust watcher build/test；Foundry；Solana cargo test | `live-rpc` watcher；Anvil rehearsal；签名/HSM 演练 | watcher 日志、Foundry 日志、health/metrics scrape |
| 安全与供应链 | Rust/npm/NuGet/合约/文档信任边界 | `cargo audit`；npm audit；clippy；format | threat model 复核；依赖新鲜度；secret handling；事故演练 | audit 报告、dependency diff |

## 标准命令序列

### L0 烟雾测试

```bash
dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build
dotnet run --project tools/Neo.L2.Devnet -- 3
mdbook build
```

### L1 日常全量

```bash
dotnet format Neo.L2.sln --verify-no-changes --exclude external/
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo
pwsh ./scripts/test-coverage.ps1 -Configuration Release -ResultsDirectory coverage/dotnet-local -Threshold 90 -OverallThreshold 80

cd sdk/typescript && npm ci --no-audit --no-fund && npm test && npm run build
cd ../../sdk/rust && cargo build && cargo test
cd ../../bridge/neo-zkvm-guest && cargo build && cargo test
```

### L2 发布门禁

```bash
dotnet tool install -g Neo.Compiler.CSharp
for d in contracts/NeoHub.* samples/contracts/Sample.*; do
  name=$(basename "$d")
  project="$d/$name.csproj"
  dotnet build "$project" /p:NuGetAudit=false --nologo
  nccs "$project" --output "$d/bin/sc"
done

dotnet test external/neo/tests/Neo.UnitTests/Neo.UnitTests.csproj \
  --filter FullyQualifiedName~UT_L2NativeContracts \
  /p:NuGetAudit=false --nologo

(cd external/neo-riscv-vm && cargo check -p neo-riscv-host)
(cd watchers/neo-bridge-watcher-eth && cargo build --release && cargo test --release)
(cd watchers/neo-bridge-watcher-eth && cargo build --release --features live-rpc && cargo test --release --features live-rpc)
(cd watchers/neo-bridge-watcher-tron && cargo build --release && cargo test --release)
(cd watchers/neo-bridge-watcher-sol && cargo build --release && cargo test --release)

(cd external/foreign-contracts/eth && forge fmt --check && forge test -vv)
(cd external/foreign-contracts/sol && cargo test)
```

### L2 昂贵证明门禁

```bash
(cd bridge/neo-zkvm-guest && cargo prove build)
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo test --workspace --release --locked
(cd bridge/neo-zkvm-host && cargo test --release --locked -- --ignored --nocapture)
```

### L3 私网 / 生产演练

```powershell
pwsh ./scripts/deployment/run-local-rehearsal.ps1
pwsh ./scripts/private-network/Test-PrivateNetwork.ps1
```

L3 还必须补充 operator 现场证据：钱包/HSM 配置、治理提案演练、紧急暂停/恢复、DA
留存证明、RPC `/healthz` / `/metrics` 抓取、回滚与事故处理步骤。不得记录 secret 明文。

## 执行顺序

1. **环境归一化**：.NET 10、Rust stable、Node 20+、PowerShell、mdBook、Foundry、`nccs`、SP1 toolchain。
2. **先跑 L0**：任何失败先修复，不进入昂贵门禁。
3. **并行跑 L1**：.NET、docs、SDK、Rust guest、watcher default gates。
4. **按风险跑 L2**：合约、native core、bridge、DA/devnet、proof、foreign routers。
5. **最后跑 L3**：私网和生产演练只在 L2 通过后执行。
6. **发布验证账本**：每个 gate 记录命令、commit、环境、证据路径、结果、owner、follow-up。

## 通过标准

- L0 / L1 不允许失败。
- 生产 release candidate 必须通过 L2。
- real SP1 proof 可以是手动触发，但 production ZK chain 上线前必须有新鲜 real-proof 证据。
- devnet 必须覆盖 default、persistent rehydrate、sample config、选定 executor mode。
- 合约 artifacts 必须从同一个 release commit 生成。
- 任何接受的 gap 必须出现在 `IMPLEMENTATION_STATUS.md`、release checklist 或验证账本中。

## 证据账本模板

| 日期 | Commit | 模块 | Gate | 命令 | 结果 | 证据路径 | Owner | Follow-up |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| YYYY-MM-DD | `<sha>` | NeoHub contracts | L2 | `nccs ...` | pass/fail | `artifacts/...` | owner | issue/pr |
