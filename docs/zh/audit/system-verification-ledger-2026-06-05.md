# 系统验证账本 — 2026-06-05

这份账本记录新增 Neo N4 系统化验证计划后的第一轮模块化验证。

## 运行上下文

| 字段 | 值 |
| --- | --- |
| 日期 | 2026-06-05 |
| Commit | `9d44d4d` |
| Workspace | `/Users/jinghuiliao/git/r3e/neo-n4` |
| .NET SDK | 初始为 `10.0.107`；Homebrew 环境修复后为 `10.0.108` |
| Node.js | `v24.4.1` |
| Rust | `rustc 1.91.0-nightly (523d3999d 2025-08-30)` |

## L0 烟雾门禁

| 模块 | 命令 | 结果 | 证据 |
| --- | --- | --- | --- |
| .NET 解决方案基线 | `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo` | ✅ 通过 | 0 warnings，0 errors；解决方案项目全部构建成功。 |
| .NET 单元 / 集成测试 | `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build` | ✅ 通过 | 所有发现的测试项目通过，0 failures。 |
| Devnet 烟雾测试 | `dotnet run --project tools/Neo.L2.Devnet -- 3` | ✅ 通过 | 3 个批次 sealed；审计通过；3 deposits；3 withdrawals；3 multisig proofs；NeoFS-like DA payloads 可用。 |
| 文档站点 | `mdbook build` | ✅ 通过 | HTML book 输出到 `/Users/jinghuiliao/git/r3e/neo-n4/book`。 |

## Devnet 关键证据

- Chain `1001`，3 个批次。
- 最终 state root：
  `0x09cd9533c2aa48aca97b768db68f2e43bcac1f92efb9a27acad239a88d78e0fa`。
- Security label：
  `securityLevel=Optimistic daMode=NeoFS sequencer=DbftCommittee exit=Permissionless gateway=False`。
- Alice 最终余额：`5940000`，与 expected value 一致。
- 审计检查通过：
  continuity、non-zero proof、proof verification、batch ranges、DA availability。
- 指标已输出：
  `l2.batch.sealed=3`、`l2.bridge.deposits=3`、
  `l2.bridge.withdrawals=3`、`l2.proving.generated{kind=Multisig}=3`。

## 下一批门禁

## 环境修复

| 工具 | 动作 | 结果 |
| --- | --- | --- |
| PowerShell | `pwsh` 缺失后，通过 `brew install powershell` 安装。 | ✅ `pwsh 7.6.2` 可用。Homebrew 同时安装了 `dotnet 10.0.108`，后续 .NET 命令使用 SDK `10.0.108`。 |

## L1 日常门禁

| 模块 | 命令 | 结果 | 证据 |
| --- | --- | --- | --- |
| .NET format | `dotnet format Neo.L2.sln --verify-no-changes --exclude external/` | ✅ 通过 | 无需格式化变更。 |
| .NET 覆盖率 | `pwsh ./scripts/test-coverage.ps1 -Configuration Release -ResultsDirectory coverage/dotnet-local -Threshold 90 -OverallThreshold 80` | ✅ 通过 | Gate line coverage `4242 / 4651 = 91.21%`；overall reported line coverage `6026 / 7194 = 83.76%`；34 个 Cobertura 文件。 |
| TypeScript SDK | 在 `sdk/typescript` 运行 `npm ci --no-audit --no-fund && npm test && npm run build` | ✅ 通过 | 安装 76 个 packages；Vitest `16 / 16` 通过；`tsc` build 通过。 |
| Rust SDK | 在 `sdk/rust` 运行 `cargo build && cargo test` | ✅ 通过 | build 通过；integration tests `10 / 10` 通过。 |
| zkVM guest host-mode | 在 `bridge/neo-zkvm-guest` 运行 `cargo build && cargo test` | ✅ 通过 | build 通过；guest tests `8 / 8` 通过。 |

覆盖率摘要：
`/Users/jinghuiliao/git/r3e/neo-n4/coverage/dotnet-local/dotnet-line-coverage-summary.json`。

## L2 发布门禁

| 模块 | 命令 | 结果 | 证据 |
| --- | --- | --- | --- |
| NeoHub + sample contract artifacts | 对 `contracts/NeoHub.*` 与 `samples/contracts/Sample.*` 执行 `dotnet build` + `nccs --output bin/sc` | ✅ 通过 | 26 个可部署合约全部生成 `.nef` 与 `.manifest.json`。 |
| NeoHub manifest invariants | `NEO_N4_REQUIRE_FRESH_MANIFESTS=1 dotnet test tests/Neo.Hub.Deploy.UnitTests/Neo.Hub.Deploy.UnitTests.csproj --filter "FullyQualifiedName~UT_ContractManifestInvariants\|FullyQualifiedName~UT_OptimisticChallengeAllowlist"` | ✅ 通过 | `17 / 17` 测试通过。 |
| Neo core L2 native contracts | `dotnet test external/neo/tests/Neo.UnitTests/Neo.UnitTests.csproj --filter FullyQualifiedName~UT_L2NativeContracts /p:NuGetAudit=false --nologo` | ✅ 修复后通过 | `10 / 10` 测试通过。 |
| Neo core mempool regression | `dotnet test external/neo/tests/Neo.UnitTests/Neo.UnitTests.csproj --filter FullyQualifiedName~UT_MemoryPool /p:NuGetAudit=false --nologo` | ✅ 通过 | `28 / 28` 测试通过，包含 2 个新增锁回归测试。 |
| RISC-V host | `cd external/neo-riscv-vm && cargo check -p neo-riscv-host` | ✅ 通过 | `neo-riscv-host` 在 workspace toolchain 下检查通过。 |
| ETH watcher default | `cd watchers/neo-bridge-watcher-eth && cargo build --release && cargo test --release` | ✅ 通过 | library、daemon smoke、parity、preflight 测试全部通过；合计 `95` 个测试。 |
| ETH watcher live-rpc | `cd watchers/neo-bridge-watcher-eth && cargo build --release --features live-rpc && cargo test --release --features live-rpc` | ✅ 通过 | 启用 `live-rpc` 后同一 release 测试矩阵通过。 |
| TRON watcher | `cd watchers/neo-bridge-watcher-tron && cargo build --release && cargo test --release` | ✅ 通过 | unit、parity、doc tests 通过；合计 `7` 个测试。 |
| Solana watcher | `cd watchers/neo-bridge-watcher-sol && cargo build --release && cargo test --release` | ✅ 通过 | unit 与 parity tests 通过；合计 `9` 个测试。 |
| ETH foreign router | `cd external/foreign-contracts/eth && forge fmt --check && forge test -vv` | ✅ 通过 | 安装 Homebrew Foundry `1.7.1`；按 README 克隆 gitignored `lib/forge-std`；Foundry `39 / 39` 测试通过。 |
| Solana foreign router | `cd external/foreign-contracts/sol && cargo test` | ✅ 通过 | Anchor/Solana router tests `22 / 22` 通过。 |

## L2 昂贵证明门禁

| 模块 | 命令 | 结果 | 证据 |
| --- | --- | --- | --- |
| SP1 guest ELF | `cd bridge/neo-zkvm-guest && cargo prove build` | ✅ 通过 | 构建 RISC-V ELF：`target/elf-compilation/riscv64im-succinct-zkvm-elf/release/neo-zkvm-guest`。 |
| 顶层 Rust workspace | `cargo fmt --all -- --check && cargo clippy --workspace --all-targets --locked -- -D warnings && cargo test --workspace --release --locked` | ✅ 通过 | format、clippy、release tests、doc tests 通过；非 ignored zkVM host end-to-end 测试通过。 |
| Real SP1 proof | `cd bridge/neo-zkvm-host && cargo test --release --locked -- --ignored --nocapture` | ✅ 通过 | `2 / 2` ignored real-proof 测试通过；proof generation `28.30s`，proof verification `13.05s`，篡改 public-input hash 被拒绝。 |

## 本轮验证中修复的问题

- 修复 `external/neo/src/Neo/Ledger/MemoryPool.cs` 的 `MemoryPool.Count`：读锁必须用 `ExitReadLock` 释放，不能误用 `ExitWriteLock`。
- 修复 `external/neo/src/Neo/Ledger/MemoryPool.cs` 的 `MemoryPool.Clear`：写锁必须用 `ExitWriteLock` 释放，不能误用 `ExitReadLock`。
- 在 `external/neo/tests/Neo.UnitTests/Ledger/UT_MemoryPool.cs` 添加空 mempool block persist 与 `Clear()` 锁释放回归测试。

## 最终回归扫尾

| 模块 | 命令 | 结果 | 证据 |
| --- | --- | --- | --- |
| .NET 解决方案基线 | `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo` | ✅ 通过 | 在 `external/neo` MemoryPool 修复后重跑；0 warnings，0 errors。 |
| .NET 单元 / 集成测试 | `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build` | ✅ 通过 | 在 `external/neo` MemoryPool 修复后重跑；所有发现的测试项目通过，0 failures。 |
| 文档站点 | `mdbook build` | ✅ 通过 | 账本更新后重跑；HTML book 输出到 `/Users/jinghuiliao/git/r3e/neo-n4/book`。 |
| Diff hygiene | `git diff --check && git -C external/neo diff --check` | ✅ 通过 | 顶层 repo 与 `external/neo` submodule diff 均无 whitespace errors。 |
