# 测试覆盖

Neo N4 使用证据优先的测试策略，而不是只给出一个单一百分比。

## 门槛

- `.NET`：`scripts/test-coverage.ps1` 使用 `coverlet.collector` 运行完整 `Neo.L2.sln` 测试，按源码文件/行合并 Cobertura 报告，并应用双门槛：协议/运行时源码根目录 90% line coverage，以及所有已上报源码 80% overall reported line coverage。
- `Contracts`：CI 构建每个 `contracts/NeoHub.*` 和 `samples/contracts/Sample.*` 项目，用 `nccs` 编译，并验证 `.nef` 与 `.manifest.json` 产物。
- `Neo core fork`：CI 在 r3e Neo core 子模块中运行 `UT_L2NativeContracts`。
- `Rust`：CI 运行 workspace tests、clippy、PolkaVM host checks、watcher feature checks、SP1 host/guest checks。
- `Foreign chains`：CI 运行 EVM router 的 Foundry 测试和 Solana router 的 Cargo 测试。
- `Experience Hub`：CI 运行 hub state、manifest generation、report schema redaction 的 Node 测试。
- `Docs`：CI 运行 `mdbook build`；production-gap 测试强制英文/中文文档 counterpart 成对存在。

## 本地命令

```powershell
dotnet test .\Neo.L2.sln -c Release /p:NuGetAudit=false --nologo
.\scripts\test-coverage.ps1 -Configuration Release -ResultsDirectory coverage/dotnet-local -Threshold 90
```

原始 coverage 输出写入 `coverage/`，该目录有意忽略。脚本会在所选结果目录中写入 `dotnet-line-coverage-summary.json`，方便审计复核。

## 范围

.NET coverage gate 对完整 solution 测试中被 Coverlet 输出的一方源码文件报告 line coverage。严格的 90% 门槛限定在 `src/` 和 `samples/executors`，也就是 protocol、runtime、proof、bridge、DA、state 和 executor 逻辑所在位置。CLI 与部署工具仍然计入 `reportedLineCoverage`，但使用 80% 的 overall reported 门槛，因为真实部署命令包含 RPC 与运维入口，更适合通过 planner tests、dry-runs、manifests 和 testnet evidence 验证。

摘要中还包含 `sourceFileAudit`，列出一方 `src/`、`tools/` 和 `samples/executors` 中未进入 Cobertura 报告的文件，避免审计时把已度量的 line coverage 误读为整个代码库的文件覆盖。

Neo 智能合约不以 line coverage 作为主指标，因为合约主要通过 compiler artifacts、manifest invariants、deployment planner tests 和 testnet RPC evidence 验证。
