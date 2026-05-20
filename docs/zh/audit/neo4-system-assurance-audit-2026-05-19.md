# Neo 4 系统保证审计

日期：2026-05-19

## 范围

本检查点把 `r3e-network/neo-n4` 主仓库作为 Neo Stack Layer-2 系统来审核，
重点关注完整性、专业结构、架构质量、用户理解成本、一致性和本地正确性。

本轮覆盖：

- 主仓库清单、文档、图表和用户向导。
- NeoHub 可部署 L1 合约套件和部署工具。
- N4 Layer-2 运行时模块、r3e Neo core fork 中的 L2 原生合约，以及
  NeoVM2/RISC-V 执行边界。
- NeoFS 数据可用性接入点和验证证据。
- 通过可部署 NeoHub 合约和 L1 可部署验证器合约的 ZK 验证路径。
- SDK、CLI、示例、watcher、外链桥适配器和测试证据。

本地保证检查不覆盖：

- 带资金账户的真实 Neo 公共 devnet/testnet 部署。
- 真实治理或多签仪式。
- 真实外链流动性和跨链路由。
- 本提交落到 `origin/master` 后的 GitHub Actions 最新运行。
- 第三方密码学 / 安全审计。

## 证据快照

| 领域 | 结果 |
| ---- | ---- |
| 仓库归属 | 当前 remote 是 `r3e-network/neo-n4` 下的 `origin`；没有 push 目标指向 `neo-project`。 |
| 仓库清单 | 本轮开始前有 874 个受控文件；本轮补上缺失的本地化 Experience Hub 图像和本审计记录。 |
| Core fork 边界 | `external/neo` 指向 `r3e-network/neo` 的 `r3e/neo-n4-core` 分支；L1/L2 Neo core 改动保留在 r3e 维护的分支。 |
| VM 边界 | NeoVM2/RISC-V 是规范的 N4 Layer-2 执行路径；可选 VM 支持以 N4 L2 execution profile 表达，而不是 NeoX。 |
| 文档本地化 | 已发现并修复两张 Experience Hub PNG 缺少中文 counterpart 的问题。 |
| .NET 测试 | `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo` 在文档修复后通过。 |
| 已观察测试数 | 全量 solution 测试中观察到 1,452 个 .NET 测试通过。 |
| Rust 测试 | WSL2 下 `cargo test --workspace --all-targets --locked` 通过。 |
| TypeScript 测试 | `sdk/typescript` 的 `npm test` 和 `npm run build` 通过。 |
| 交互测试 | Experience Hub report schema 与 Interactive Runtime simulator 的 Node 测试通过。 |
| 文档构建 | WSL2 下 `mdbook build` 通过。 |
| 空白检查 | `git diff --check` 没有阻塞性空白错误；PowerShell 只提示一个已改文件下次 Git 写入时会规范化 CRLF。 |

## 发现与处理

### 已修复：Experience Hub 图像缺少中文 counterpart

文档本地化测试发现新加入的 Experience Hub PNG 只存在于英文文档树：

- `docs/figures/experience-hub/neo-n4-experience-hub.png`
- `docs/experience-hub/concepts/neo-n4-experience-hub-concept.png`

修复：

- 新增 `docs/zh/figures/experience-hub/neo-n4-experience-hub.png`。
- 新增 `docs/zh/docs/experience-hub/concepts/neo-n4-experience-hub-concept.png`。
- 更新 `docs/zh/README.md`，让中文 README 引用中文侧 Experience Hub 截图路径。

这关闭了全量 .NET solution 测试暴露出的当前本地化回归。

### 已复核：operator-plan 命令文件命名

宽泛 placeholder 扫描命中了
`tools/Neo.Stack.Cli/Commands/OperatorPlanCommands.cs`。人工复核后确认，该文件不是空实现
或假实现：里面包含链注册、桥适配器部署计划、服务预检查和批次提交校验等功能性命令。

处理：

- 本检查点不需要做正确性修复。
- 旧的 `StubCommands.cs` 文件名已在整理中移除，并替换为 `OperatorPlanCommands.cs`。

### 已复核：测试专用 stub 合约和 handler 被约束在测试边界

扫描还命中了测试夹具和 stub，包括 `NeoHub.ExternalBridgeStubVerifier` 以及单元测试用
HTTP/RPC handler。这些是确定性测试替身，不属于生产 NeoHub 部署计划。

处理：

- 保留它们，因为它们提高了可重复验证覆盖。
- 继续确保测试专用 verifier 合约不进入生产部署 manifest。

## 模块级评估

| 模块族 | 评估 | 证据 / 理由 |
| ------ | ---- | ----------- |
| NeoHub L1 合约 | 对可部署合约架构而言结构完整。 | 合约按职责拆分；部署工具和测试覆盖计划生成与生产步骤边界。NeoHub 保持为可部署合约代码，而不是侵入式 L1 原生合约。 |
| L1 可部署 ZK 验证器合约 | 架构边界正确。 | proof-system 验证工作放在可部署验证器合约；`ContractZkVerifier` 仍是 NeoHub 治理和结算流暴露的可部署适配器。 |
| L2 原生合约 | N4 链内系统行为放置合理。 | L2 专属原生合约位于 r3e Neo core fork，而不是部署后的临时合约。 |
| 执行层 | 分层先进且合理。 | NeoVM2/RISC-V 是默认执行模型；共享 Rust execution core 保持 proof input 逻辑后端无关；可选 VM profile 是扩展点。 |
| 数据可用性 | 与“使用 NeoFS 作为 DA”的要求一致。 | 文档和 Experience Hub 已把 NeoFS DA 作为一等层描述，包含内容寻址、复制和存储证明验证。 |
| 桥和消息 | 模块化良好。 | Shared bridge、message router、token registry、forced inclusion、challenge、censorship 模块分离并有测试覆盖。 |
| 资产 | L1/L2 decimals 模型正确。 | L1 NEO 保持不可分割；L2 NEO/GAS 以及标准 USDT/USDC/BTC 映射带显式 decimals 和缩放检查。 |
| 外链 | 扩展模型合理。 | 外链桥 adapter 和 watcher crate 按链拆分，便于后续 Avalanche/EVM 类链扩展，不污染 NeoHub 核心逻辑。 |
| 工具链 | 运维侧覆盖强。 | `Neo.Stack.Cli`、bridge CLI、deploy tooling、devnet tooling、faucet tooling、explorer tooling 均被 solution 测试覆盖。 |
| 用户体验 | 已显著改善并有文档支撑。 | Experience Hub 以控制台式视觉方式解释架构、数据流、验证证据和测试证据。 |
| 本地化 | 已由测试门禁强制。 | Markdown、SVG 和 figure counterpart 规则能发现缺失中文资产；本轮已修复当前 PNG 缺口。 |
| 安全姿态 | 对本检查点而言本地上成立。 | 已复核变更区没有发现新的高影响问题；更高保证仍需要真实网络和第三方审计。 |

## 架构结论

该仓库正在沿着专业 Neo Stack Layer-2 实现的方向推进：

- L1 改动面保持克制：NeoHub 是可部署合约加插件/服务，只有性能关键的密码学加速需要 L1 native 支持。
- N4 Layer-2 core 改动位于 r3e Neo core fork，因此 L2 原生行为由 L2 执行 core 拥有，而不是部署后补丁。
- NeoFS 被作为数据可用性层处理，而不是可选存储附属品。
- VM 架构具备未来兼容性，同时不把项目混同为 NeoX：NeoVM2/RISC-V 保持规范默认路径，其他 VM 通过 N4 L2 execution profile 接入。
- 文档集已经具备视觉入口、详细架构指南、工作流图和中文 counterpart。

## 剩余生产门槛

这些不是本地代码缺陷，但在声称生产网络完全验证前必须关闭：

- 当前改动 push 后，在 GitHub 上跑通同一套 CI workflow。
- 使用带资金账户的真实 Neo 公共 devnet/testnet 做部署演练。
- 记录真实合约 hash、治理/多签地址、DA CID、proof job ID 和桥交易 hash。
- 演练真实 L1 到 L2、L2 到 L1、L2 到 L2 资产流动及失败场景。
- 主网承载价值前完成第三方安全和密码学审计。

## 本检查点验证命令

```powershell
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj --filter CurrentDocumentation_EveryEnglishFigureHasChineseCounterpart /p:NuGetAudit=false --nologo
dotnet test tests\Neo.Hub.Deploy.UnitTests\Neo.Hub.Deploy.UnitTests.csproj --filter CurrentDocumentation /p:NuGetAudit=false --nologo
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo
node --test tests\interactive-runtime\simulator.test.mjs tests\experience-hub\hub-state.test.mjs tests\experience-hub\manifest-generator.test.mjs tests\experience-hub\report-schemas.test.mjs
npm test
npm run build
git diff --check
```

```bash
cargo test --workspace --all-targets --locked
mdbook build
```

本地观察结果：

- 文档 counterpart 测试通过：5 通过，0 失败。
- Experience Hub 和 Interactive Runtime Node 测试通过：15 通过，0 失败。
- 完整 `.NET` solution 测试通过，观察到 1,452 个测试。
- WSL2 下 root Rust workspace 测试通过：70 通过，0 失败，2 个真实证明测试按设计 ignored。
- TypeScript SDK 测试通过：15 通过，0 失败；`tsc` build 通过。
- WSL2 下 `mdbook build` 通过。
- 空白检查没有阻塞错误。
