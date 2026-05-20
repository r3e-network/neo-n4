# Neo N4 代码库清理实施计划

> **面向 agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**目标:** 删除可证明过时或重复的代码路径，同时不改变 Neo N4 运行时行为。

**架构:** 清理范围限定在结构和支持代码重复上，并要求测试可以覆盖。除非引用关系证明安全并且测试固定当前行为，否则不删除 protocol、contract、bridge、DA 或 VM 代码。

**技术栈:** .NET 10、MSTest、PowerShell、mdBook、Node test runner、Vitest。

---

### 任务 1: 统一 NeoHub Deploy 参数解析

**文件:**
- 新建: `tools/Neo.Hub.Deploy/ArgUtil.cs`
- 修改: `tools/Neo.Hub.Deploy/Program.cs`
- 修改: `tools/Neo.Hub.Deploy/PlanCommand.cs`
- 修改: `tools/Neo.Hub.Deploy/ScaffoldCommand.cs`
- 修改: `tools/Neo.Hub.Deploy/VerifyCommand.cs`
- 修改: `tools/Neo.Hub.Deploy/LiveDeployCommand.cs`
- 测试: `tests/Neo.Hub.Deploy.UnitTests/*.cs`

- [x] 添加一个共享的 internal `ArgUtil`，包含 `Get` 和 `HasFlag`。
- [x] 替换 plan、scaffold、verify 命令中的 `ArgUtilLocal` 副本。
- [x] 用共享 `ArgUtil.HasFlag` 替换 `LiveDeployCommand` 私有 helper。
- [x] 运行 `dotnet test tests/Neo.Hub.Deploy.UnitTests/Neo.Hub.Deploy.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`。

### 任务 2: 移除历史遗留的 StubCommands 文件名

**文件:**
- 移动: `tools/Neo.Stack.Cli/Commands/StubCommands.cs` -> `tools/Neo.Stack.Cli/Commands/OperatorPlanCommands.cs`
- 修改: 引用当前源码文件名的文档。
- 测试: `tests/Neo.Stack.Cli.UnitTests/Neo.Stack.Cli.UnitTests.csproj`

- [x] 将文件重命名为更准确的 operator-plan/preflight 命令文件。
- [x] 删除“文件名是历史遗留”的注释。
- [x] 更新指向旧文件名的当前文档和审计文档。
- [x] 运行 `dotnet test tests/Neo.Stack.Cli.UnitTests/Neo.Stack.Cli.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`。

### 任务 3: 删除过时的 NativeZkVerifier 测试网产物

**文件:**
- 删除仍描述已废弃 `NativeZkVerifier` 路线的未跟踪 `docs/audit/testnet-deployment-2026-05-20*.json` 文件。

- [x] 仅删除这些过时的未跟踪 evidence 文件。
- [x] 重新扫描当前源码和文档中的旧生产路线名称。

### 任务 4: 验证

- [x] 运行 `dotnet format .\Neo.L2.sln --verify-no-changes --exclude external/ --verbosity minimal`。
- [x] 运行 `dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`。
- [x] 运行 `dotnet test tests\Neo.Stack.Cli.UnitTests\Neo.Stack.Cli.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`。
- [x] 运行 `wsl bash -lc 'cd /mnt/d/Git/neo-n4 && mdbook build'`。
- [x] 运行 `node --test tests\experience-hub\*.test.mjs`。
