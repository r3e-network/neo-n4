# Neo N4 Codebase Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove provably obsolete or duplicated code paths without changing Neo N4 runtime behavior.

**Architecture:** Keep cleanup bounded to structure and support-code duplication that tests can cover. Do not delete protocol, contract, bridge, DA, or VM code unless references prove it is unused and tests keep the current behavior pinned.

**Tech Stack:** .NET 10, MSTest, PowerShell, mdBook, Node test runner, Vitest.

---

### Task 1: Consolidate NeoHub Deploy Argument Parsing

**Files:**
- Create: `tools/Neo.Hub.Deploy/ArgUtil.cs`
- Modify: `tools/Neo.Hub.Deploy/Program.cs`
- Modify: `tools/Neo.Hub.Deploy/PlanCommand.cs`
- Modify: `tools/Neo.Hub.Deploy/ScaffoldCommand.cs`
- Modify: `tools/Neo.Hub.Deploy/VerifyCommand.cs`
- Modify: `tools/Neo.Hub.Deploy/LiveDeployCommand.cs`
- Test: `tests/Neo.Hub.Deploy.UnitTests/*.cs`

- [x] Add one shared internal `ArgUtil` helper with `Get` and `HasFlag`.
- [x] Replace `ArgUtilLocal` copies in plan, scaffold, and verify commands.
- [x] Replace the private `LiveDeployCommand.HasFlag` helper with shared `ArgUtil.HasFlag`.
- [x] Run `dotnet test tests/Neo.Hub.Deploy.UnitTests/Neo.Hub.Deploy.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`.

### Task 2: Remove Historical StubCommands Filename

**Files:**
- Move: `tools/Neo.Stack.Cli/Commands/StubCommands.cs` -> `tools/Neo.Stack.Cli/Commands/OperatorPlanCommands.cs`
- Modify: docs that refer to the current source filename.
- Test: `tests/Neo.Stack.Cli.UnitTests/Neo.Stack.Cli.UnitTests.csproj`

- [x] Rename the file to match its current responsibility: operator-plan/preflight commands.
- [x] Remove comments that say the filename is historical.
- [x] Update current docs and audit docs that point readers at the old filename.
- [x] Run `dotnet test tests/Neo.Stack.Cli.UnitTests/Neo.Stack.Cli.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`.

### Task 3: Remove Obsolete NativeZkVerifier Testnet Artifacts

**Files:**
- Delete untracked `docs/audit/testnet-deployment-2026-05-20*.json` files that still describe the retired `NativeZkVerifier` route.

- [x] Delete only the obsolete untracked evidence files.
- [x] Re-scan current source/docs for old production-route names.

### Task 4: Verification

- [x] Run `dotnet format .\Neo.L2.sln --verify-no-changes --exclude external/ --verbosity minimal`.
- [x] Run `dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`.
- [x] Run `dotnet test tests\Neo.Stack.Cli.UnitTests\Neo.Stack.Cli.UnitTests.csproj -c Release /p:NuGetAudit=false --nologo`.
- [x] Run `wsl bash -lc 'cd /mnt/d/Git/neo-n4 && mdbook build'`.
- [x] Run `node --test tests\experience-hub\*.test.mjs`.
