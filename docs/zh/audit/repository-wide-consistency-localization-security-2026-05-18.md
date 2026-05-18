# 仓库级一致性、本地化与安全审计

日期：2026-05-18

## 范围

本轮审计覆盖 neo-n4 主仓库维护责任边界：

- 本轮结束后主仓库 840 个受控文件。
- 主仓库范围内 71 个英文 Markdown 文档。
- 文档树下 48 个英文 SVG / 图表资产。
- 23 个 `contracts/NeoHub.*` 可部署 L1 项目。
- 22 个生产 NeoHub 部署步骤，排除测试专用的 `NeoHub.ExternalBridgeStubVerifier`。
- r3e Neo core fork 中 10 个 N4 L2 原生合约：`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`。
- .NET solution、TypeScript SDK、Rust workspace、watcher crate、外链桥合约和 mdBook 文档。

本地化门禁不覆盖：生成目录 `artifacts/`、vendored `external/foreign-contracts/eth/lib/forge-std`、以及外部子模块内部文件（`external/neo`、`external/neo-devpack-dotnet`、`external/neo-riscv-vm`、`external/neo-zkvm`）。这些属于上游或生成输入，不属于 neo-n4 主仓库直接维护的文档范围。

## 已修复内容

- 为每个缺失中文版本的主仓库英文 Markdown 文档补齐 `docs/zh/...` 对应文件。
- 为 `docs/figures/visual-guide/` 的 SVG 视觉导览补齐 `docs/zh/figures/visual-guide/` 中文图表。
- 更新 `docs/zh/visual-guide.md`，使中文页面引用中文 SVG，而不是英文 SVG。
- 新增单元测试：每个主仓库英文 Markdown 文档必须有中文 counterpart。
- 新增单元测试：中文 Markdown counterpart 必须包含中文文本。
- 新增单元测试：每个主仓库英文图表资产必须有中文 counterpart。
- 新增单元测试：中文 SVG counterpart 必须包含中文字符。
- 移除 `neo-stack scaffold-executor` 生产模板中的 `TODO` 生成标记。
- 加固 `neo-external-bridge genkey` 私钥输出，改为使用 `FileMode.CreateNew` 原子创建，避免检查后写入的竞态。

## 安全审计结论

本轮没有留下新的可报告高影响安全问题。

重点检查面包括：

- 私钥生成与文件输出。
- JSON-RPC HTTP 客户端和响应 `id` 校验。
- Metrics HTTP 监听器的请求边界和状态文本处理。
- 合约清单、部署计划边界和原生合约归属。
- 主仓库源码和文档中的密钥模式扫描。
- 主仓库生产代码中的占位/未完成标记扫描。
- 主仓库文档相对链接完整性。
- .NET、TypeScript 依赖公告检查，以及 Rust workspace 测试覆盖。

剩余外部门槛：

- 本地 WSL 的 `cargo audit` 仍因 WSL 到 GitHub / RustSec 的网络路径失败而无法拉取 advisory-db。CI workflow 包含 `cargo audit`；本轮把本地失败记录为环境限制，而不是代码缺陷。

## 验证证据

本轮执行过的命令：

```powershell
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_ProductionGapClosure
dotnet test tests\Neo.External.Bridge.Cli.UnitTests\Neo.External.Bridge.Cli.UnitTests.csproj /p:NuGetAudit=false --nologo
dotnet test tests\Neo.Stack.Cli.UnitTests\Neo.Stack.Cli.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_ScaffoldExecutorCommand
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet test external\neo\tests\Neo.UnitTests\Neo.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_L2NativeContracts
dotnet list Neo.L2.sln package --vulnerable --include-transitive
npm audit --audit-level=high
npm test
npm run build
```

```bash
cargo test --workspace --all-targets --locked
cd external/foreign-contracts/sol && cargo test --all --locked
cd external/foreign-contracts/eth && PATH=/mnt/d/Git/neo-n4/CODEX_DEEP_AUDIT/tools/foundry/bin:$PATH forge test
mdbook build
cargo audit
```

观察结果：

- `UT_ProductionGapClosure`：9 通过，0 失败。
- `Neo.External.Bridge.Cli.UnitTests`：16 通过，0 失败。
- `UT_ScaffoldExecutorCommand`：16 通过，0 失败。
- 完整 `dotnet test Neo.L2.sln`：solution 内所有测试项目通过。
- Neo core L2 原生合约测试：6 通过，0 失败。
- `nccs`：25 个可部署合约 / sample 项目全部编译并生成 `.nef` + `.manifest.json`。
- TypeScript SDK：15 通过，0 失败；`tsc` 构建通过。
- .NET vulnerable package audit：列出的项目没有报告易受攻击的包。
- `npm audit --audit-level=high`：0 vulnerabilities。
- Rust workspace 测试通过；真实证明测试按设计标记 ignored，host/guest 执行冒烟通过。
- Solana 外链桥 router 测试：4 通过，0 失败。
- Foundry `forge test`：21 通过，0 失败。
- `mdbook build` 通过。
- 本地 `cargo audit`：拉取 RustSec advisory-db 时被 WSL 到 GitHub 的网络路径阻断。
