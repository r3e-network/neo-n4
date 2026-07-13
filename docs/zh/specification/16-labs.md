# 第 16 章：实验课

本章用实验帮助读者把书里的概念跑起来。每个实验都对应一个系统能力。

## 16.1 实验 1：构建全仓库

目标：确认本机工具链可以构建 N4。

```powershell
dotnet restore .\Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet build .\Neo.L2.sln -c Release /p:NuGetAudit=false --nologo
```

你应该看到：

```text
Build succeeded.
0 Error(s)
```

学到的东西：

- `.NET 10` 是主 runtime；
- `external/neo` 通过 project reference 进入构建；
- 合约、工具、插件和 SDK 在一个 solution 中协同。

## 16.2 实验 2：运行 L2 devnet

```powershell
dotnet run --project tools\Neo.L2.Devnet\Neo.L2.Devnet.csproj `
  -c Release -- 3 --config samples\general-rollup.config.json
```

观察：

- batch 是否递增；
- state root 是否连续；
- DA writer 是否出现；
- audit 是否通过。

## 16.3 实验 3：RISC-V executor smoke

```powershell
dotnet run --project tools\Neo.L2.Devnet\Neo.L2.Devnet.csproj `
  -c Release -- 3 --config samples\general-rollup.config.json --executor riscv
```

目标：验证默认 N4 L2 VM 路线，即 NeoVM2/RISC-V via PolkaVM。

## 16.4 实验 4：生成 NeoHub 部署计划

```powershell
dotnet run --project tools\Neo.Hub.Deploy -- scaffold --output plan.json
dotnet run --project tools\Neo.Hub.Deploy -- plan --plan plan.json --output bundle.json
```

打开 `plan.json`，检查：

- 是否包含 23 个生产合约，并排除仅审计/测试合约；
- 是否包含 `ContractZkVerifier`；
- 是否不包含 test-only `ExternalBridgeStubVerifier`；
- post-deploy actions 是否提示 DA / verifier / filter wiring。

## 16.5 实验 5：验证 NeoHub artifacts

```powershell
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj `
  -c Release /p:NuGetAudit=false --nologo
```

这个测试集验证：

- deploy plan 与合约目录一致；
- 文档本地化规则；
- ContractZkVerifier 当前路线；
- `docs/audit` 策展证据规则。

## 16.6 实验 6：构建文档书

```powershell
wsl bash -lc 'cd /mnt/d/Git/neo-n4 && mdbook build'
```

这一步证明：

- `docs/SUMMARY.md` 没有坏链接；
- 新增中文书能被 mdBook 渲染；
- 图表路径基本正确。

## 16.7 实验 7：TypeScript SDK

```powershell
Push-Location sdk\typescript
npm test
npm run build
Pop-Location
```

目标：确认应用开发者 SDK 的类型和错误模型没有破坏。

## 16.8 实验 8：读一个 batch

用 `tools/Neo.L2.Explore` 查询本地节点或样例 endpoint：

```powershell
dotnet run --project tools\Neo.L2.Explore -- batch 1 --rpc http://localhost:10332
```

关注输出中的：

- batch number；
- pre/post state roots；
- tx root；
- receipt root；
- withdrawal root；
- DA commitment。

## 16.9 实验 9：审计状态根连续性

```powershell
dotnet run --project tools\Neo.L2.Explore -- audit 10 --rpc http://localhost:10332
```

如果连续性失败，说明某个 batch 的 `PreStateRoot` 没有接上前一个 `PostStateRoot`。

## 16.10 实验 10：检查生产门槛

阅读：

```text
docs/zh/release-readiness-checklist.md
docs/zh/audit/full-stack-validation-2026-05-20/README.md
```

把每个“已验证”和“仍需生产配置”的项分开。不要把本地 devnet 通过误写成 mainnet production ready。
