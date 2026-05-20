# Neo N4 全栈验证计划

> 对应英文文档：[docs/superpowers/plans/2026-05-20-neo-n4-full-stack-validation.md](../../../../docs/superpowers/plans/2026-05-20-neo-n4-full-stack-validation.md)

**目标：** 对 Neo N4 做系统性、可复现、证据优先的全栈验证，覆盖代码、合约、文档、图表、私有 devnet、RISC-V 执行、zkVM 证明、跨链组件和 Neo N3 testnet 部署状态。

**架构原则：** 每个阶段都必须产出可检查证据。命令成功、日志完整、链上状态可独立读取，才允许标记通过。任何失败都必须记录精确原因、影响范围和修复路径，不能用“应该没问题”代替验证。

**技术栈：** .NET 10、Neo C# compiler (`nccs`)、Neo N3 RPC、NeoHub 可部署合约、r3e Neo core fork、NeoVM2/RISC-V via PolkaVM、SP1 zkVM、Rust/Cargo、Foundry、Node/Vitest、mdBook、WSL2。

---

## 证据策略

- 创建证据目录：`docs/audit/full-stack-validation-2026-05-20/`。
- 可复用验证证据保存为 Markdown/JSON。
- 原始命令输出只保留在忽略的 scratch 目录或当前终端会话中，不提交到 `docs/audit`。
- WIF/private key 只能通过当前进程环境变量传递，不能写入文档、日志或命令脚本。
- 不使用历史记忆作为通过依据；每个阶段必须有本轮新输出。

## Phase 0：仓库与环境基线

验证内容：

- `git status --short --branch`
- `git submodule status --recursive`
- `.NET SDK`
- `node` / `npm`
- `nccs`
- WSL2 中的 `cargo`、`rustc`、`clippy`、`mdbook`、`cargo audit`、`cargo prove`

通过标准：

- 当前分支和脏文件状态明确。
- 4 个 submodule 均可解析。
- Windows 和 WSL2 工具链均可运行。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 1：.NET 全量构建、格式与测试

验证内容：

- `dotnet restore .\Neo.L2.sln`
- `dotnet build .\Neo.L2.sln -c Release`
- `dotnet format .\Neo.L2.sln --verify-no-changes --exclude external/`
- `dotnet test .\Neo.L2.sln -c Release --no-build`

覆盖范围：

- `src/**`
- `tools/**`
- `tests/**`
- 通过 ProjectReference 覆盖的 `external/neo/src/**`

通过标准：

- Build 成功，`0 Error(s)`。
- Format exit code 为 `0`。
- 每个测试项目 `Failed: 0`。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 2：NeoHub 合约编译与 manifest invariant

验证内容：

- 对所有 `contracts/NeoHub.*` 和 `samples/contracts/Sample.*` 执行 `dotnet build`。
- 对同一批项目执行 `nccs`，产出 `.nef` 和 `.manifest.json`。
- 运行 `Neo.Hub.Deploy.UnitTests` 中的 manifest、planner、plan command 不变量测试。

通过标准：

- 所有 deployable contract 都有 fresh `.nef` 和 `.manifest.json`。
- `ContractZkVerifier` 暴露 deployable verifier route。
- 不存在 `NativeZkVerifier`、`setNativeAccelerator`、`getNativeAccelerator` 生产路径。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 3：r3e Neo core fork 与 L2 原生合约

验证内容：

- `external/neo/tests/Neo.UnitTests` 中的 `UT_L2NativeContracts`。
- `tests/Neo.L2.Executor.RiscV.UnitTests`。

通过标准：

- L2 native contract set 与 r3e Neo core fork 一致。
- RISC-V executor 单测全部通过。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 4：私有 devnet、NeoFS DA 与执行模式

验证内容：

- 4 个 sample config：
  - `general-rollup.config.json`
  - `gaming-rollup.config.json`
  - `exchange-validium.config.json`
  - `privacy-sidechain.config.json`
- 默认 reference executor。
- canonical `--executor riscv`，即 NeoVM2/RISC-V via PolkaVM。

通过标准：

- 4 个 sample config 都完成 3 批次 devnet run。
- 输出包含 `DA writer = NeoFsLikeDAWriter`。
- audit pass 中包含 `da_availability`。
- RISC-V 模式输出包含 `RiscVTransactionExecutor` 和 `NeoVM2/RISC-V via PolkaVM`。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 5：Rust execution core、SDK、watchers 与 SP1 zkVM

验证内容：

- `cargo fmt --all -- --check`
- `cargo test --workspace --release --locked`
- `cargo clippy --workspace --all-targets --locked -- -D warnings`
- ETH watcher default/live-rpc clippy
- TRON/SOL watcher clippy
- `external/neo-riscv-vm` 的 `cargo check -p neo-riscv-host`
- `bridge/neo-zkvm-guest` 的 `cargo prove build`
- `bridge/neo-zkvm-host` 的 ignored real proof tests

通过标准：

- Rust formatting 通过。
- Workspace release tests 通过。
- Clippy 0 warning。
- PolkaVM-backed RISC-V host 编译通过。
- SP1 guest ELF 构建通过。
- 真实 proof 生成、验证、篡改 public input hash 拒绝测试均通过。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 6：外链 EVM/Solana 合约

验证内容：

- `external/foreign-contracts/eth`：
  - `forge fmt --check`
  - `forge test -vv`
- `external/foreign-contracts/sol`：
  - `cargo test --locked`

通过标准：

- Solidity router 格式通过。
- Foundry EVM 测试全部通过。
- Solana router 测试全部通过。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 7：SDK、Experience Hub、文档站与图文一致性

验证内容：

- `sdk/typescript`：
  - `npm install`
  - `npm test`
  - `npm run build`
  - `npm audit --audit-level=moderate`
- `tests/experience-hub/*.test.mjs`
- `mdbook build`
- 文档和本地化 invariant 测试

通过标准：

- TypeScript SDK 测试和 `tsc` 通过。
- Experience Hub manifest/report schema 测试通过。
- mdBook 构建通过。
- 英文 Markdown 和 SVG 均有中文 counterpart。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 8：安全与供应链扫描

验证内容：

- `dotnet list .\Neo.L2.sln package --vulnerable --include-transitive`
- `npm audit --audit-level=moderate`
- `cargo audit --json`
- 若 WSL 到 RustSec/GitHub 网络失败，使用本地 advisory DB fallback。
- WIF/private key 泄漏扫描。

通过标准：

- NuGet 无 vulnerable package。
- npm 无 moderate 及以上漏洞。
- RustSec vulnerability count 为 0。
- 上游 warning 必须记录依赖链，不能隐藏。
- WIF 不出现在 tracked files 或源码/文档工作树。

记录位置：`docs/audit/full-stack-validation-2026-05-20/README.md`

## Phase 9：Neo N3 testnet 部署与链上状态验证

验证内容：

- `neo-hub-deploy scaffold`
- `neo-hub-deploy plan`
- `neo-hub-deploy verify`
- testnet dry-run
- 如当前链上报告缺失或过期，再执行 live deploy。
- 通过 RPC 独立读取 application log 和 contract state。

通过标准：

- plan 仍为 23 个 production steps。
- artifact verify 为 `23 ok / 0 missing`。
- dry-run 不发送交易。
- live deploy 只有在必要时执行。
- 所有真实 tx application log 为 `HALT`。
- 23 个 NeoHub 合约都能通过 `getcontractstate` 读取。

证据文件：

- rerun 部署命令时，只在本地保留命令输出。
- `09-testnet-rpc.json`

## Phase 10：ContractZkVerifier 生产安全状态

验证内容：

- 链上 ABI 必须包含：
  - `registerVerificationKey`
  - `isVerificationKeyRegistered`
  - `registerProofVerifier`
  - `getProofVerifier`
  - `setEnvelopeOnlyAllowed`
  - `isEnvelopeOnlyAllowed`
  - `verify`
- 链上 ABI 必须不包含：
  - `setNativeAccelerator`
  - `getNativeAccelerator`
- proofSystem `1..4` 默认状态。
- malformed proof 的 `verify` 行为。

通过标准：

- 必要 ABI 存在。
- 旧 native accelerator ABI 不存在。
- 公开 testnet 上没有默认打开 envelope-only。
- 未注册 sample VK。
- malformed commitment 触发 FAULT。

证据文件：`10-contract-zk-verifier.json`

## Phase 11：README、架构文档与术语一致性

验证内容：

- `README.md`
- `ARCHITECTURE.md`
- `WHITEPAPER.md`
- `doc.md`
- `docs/README.md`
- `docs/neohub-architecture-and-workflows.md`
- `docs/security-model.md`
- `docs/technical-roadmap.md`
- `docs/zh/**`
- `docs/figures/**/*.svg`
- `docs/figures/experience-hub/neo-n4-experience-hub.png`

通过标准：

- 不再出现 `NativeZkVerifier`、`SetNativeAccelerator`、`GetNativeAccelerator`、`L1_NATIVE_ZK_VERIFIER_HASH` 等旧生产路线。
- `native accelerator` 只能出现在回归测试中，用于断言旧路线禁止。
- VM 表述必须是 Neo Stack / N4 Layer-2 execution profiles，不使用 NeoX 框架描述本项目。
- NeoVM2/RISC-V 仍是默认 canonical L2 VM。

证据文件：`11-docs-consistency.md`

## Phase 12：最终全栈 sign-off 报告

最终报告路径：`docs/audit/full-stack-validation-2026-05-20/README.md`

报告必须包含：

- 分支、submodule、脏文件状态。
- 每个阶段运行的命令。
- pass/fail 数量。
- testnet RPC network 和 block height。
- 合约哈希和交易哈希。
- 已知剩余生产缺口。
- 仍受上游控制的安全 warning。

必须明确写出的剩余门槛：

- `ContractZkVerifier` 已部署并接入 `VerifierRegistry`。
- 它当前是安全默认状态：没有 verifier、没有 VK、没有 envelope-only。
- 真实 SP1 proof 在 L1 上完成生产验证之前，还需要部署并注册生产 proof-system verifier contract 和真实 VK。

## 停止条件

- 任何 private key / WIF 出现在文件中，立即停止。
- testnet deployment 预测 owner 不符合预期，立即停止。
- `VerifierRegistry.getVerifier(3)` 指向非预期 `ContractZkVerifier`，立即停止。
- 公开 testnet 上 `ContractZkVerifier` 未经批准打开 envelope-only，立即停止。
- 旧 native-ZK route 出现在生产代码、部署计划、README 或图表中，立即停止。

## 完成标准

- 所有阶段都有本轮新证据。
- 每个命令要么 exit `0`，要么有明确失败原因和修复路径。
- 最终报告存在。
- testnet 状态经过 RPC 独立核验。
- WIF/private key 没有出现在 tracked files 或源码/文档工作树中。
- 剩余生产缺口被明确写出，不伪装成已完成。
