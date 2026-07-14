# 技术栈覆盖矩阵

完整的 L2 生态系统通常横跨 5 层 —— 协议合约、节点基础设施、运维工具、应用开发、
终端用户界面。本文档把每一层与 `neo4` 当前的出货情况、以及刻意留在仓外(第三方、
部署相关、外部依赖)的部分一一对照。

结构不是 ZKsync 特有的 —— 这些是任何 L2 栈都覆盖的类目。这里的实现是 Neo 原生的、
按 `doc.md` 从零写起;意图是与成熟 L2 生态为运维者和应用开发者提供的功能对等,而
不是某个其它项目源码的翻译。

---

## 第 1 层 —— 协议合约

- **链注册表(准入策略 + 按链 config)** ✅ — `contracts/NeoHub.ChainRegistry/`
- **共享 L1↔L2 桥(escrow、充值、提款)** ✅ — `contracts/NeoHub.SharedBridge/`
- **结算管理器(批次最终化、状态根锚定)** ✅ — `contracts/NeoHub.SettlementManager/`
- **验证器注册表(可插拔证明派发)** ✅ — `contracts/NeoHub.VerifierRegistry/`
- **代币注册表(规范 L1↔L2 资产映射)** ✅ — `contracts/NeoHub.TokenRegistry/`
- **消息路由(L1↔L2 + L2↔L2 跨链投递)** ✅ — `contracts/NeoHub.MessageRouter/`
- **DA 注册表(按批次的 DA 承诺存储)** ✅ — `contracts/NeoHub.DARegistry/`
- **排序器注册表 + 质押** ✅ — `contracts/NeoHub.SequencerRegistry/`、`SequencerBond/`
- **强制纳入合约** ✅ — `contracts/NeoHub.ForcedInclusion/`
- **乐观挑战博弈** ✅ — `contracts/NeoHub.OptimisticChallenge/`
- **治理 + 委员会 + timelock** ✅ — `contracts/NeoHub.GovernanceController/`
- **紧急暂停 + 逃生通道** ✅ — `contracts/NeoHub.EmergencyManager/`
- **欺诈验证器(仅审计用 v1/v2 结构参照)** ✅ — `contracts/NeoHub.GovernanceFraudVerifier/`（不进入生产 challenge 路径）
- **欺诈验证器(仅审计 v3 + committed-root-bound executable v4)** 🟡 — `contracts/NeoHub.RestrictedExecutionFraudVerifier/`（仅精确注册的单笔 Counter v4 可改变状态；通用 NeoVM ❌）
- **ZK verifier router** ✅ — `contracts/NeoHub.ContractZkVerifier/` 校验 `ProofType.Zk`
  envelope / VK id,并把proof-system 验证工作交给 L1 可部署验证器合约。

**26 个 NeoHub 合约项目。** 24 个生产合约 + 1 个仅审计用结构验证器 + 1 个测试 stub 全部经 `Neo.SmartContract.Framework` 通过类型检查；CI 用
`nccs` 编译每一个并校验 `.nef` + `.manifest.json` 工件。

- **L2 批次信息(chainId、批次号、L1 高度)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 桥(充值时铸造,提款时销毁)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 消息 I/O(出站发出 + 入站应用)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 费用分摊(排序器 / 证明者 / DA 占比)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 paymaster(费用抽象、赞助资产)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 system-config 缓存** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 外链桥对应合约** ✅ — `L2NativeExternalBridgeContract`
- **L2 bridged NEP-17 模板** ✅ — `BridgedNep17Contract`
- **L2 account-abstraction entry point** ✅ — `L2AccountAbstraction`
- **L2 interop verifier** ✅ — `L2InteropVerifier`

**10 个 L2 侧原生合约。**

---

## 第 2 层 —— 节点基础设施

- **批次构造器(block ↦ 批次封装)** ✅ — `src/Neo.L2.Batch/`、`Neo.Plugins.L2Batch/`
- **状态根生成器** ✅ — `src/Neo.L2.State/`
- **确定性批次执行器(证明目标)** ✅ — `src/Neo.L2.Executor/`
- **RISC-V 执行内核(PolkaVM 后备)** ✅ — `src/Neo.L2.Executor.RiscV/`(P/Invoke binding)
- **持久化后备(in-memory + RocksDB)** ✅ — `src/Neo.L2.Persistence/`
- **排序器委员会 provider** ✅ — `src/Neo.L2.Sequencer/`
- **审查检测** ✅ — `src/Neo.L2.Censorship/`
- **强制纳入 source** ✅ — `src/Neo.L2.ForcedInclusion/`
- **多签(Stage 0)证明者/验证器** ✅ — `src/Neo.L2.Proving.Attestation/`
- **乐观(Stage 1)证明者/验证器** ✅ — `src/Neo.L2.Proving.Optimistic/`
- **RISC-V ZK(Stage 2)证明者/验证器 —— 一个精确 SP1 semantic** ✅ —
  `Sp1SettlementExecutionStack` 固定完整持久 `NEO4STW1`、SHA-256 锁定的 host-native
  `neo-zkvm-executor`、atomic state replacement、`Sp1BatchProofProver`、program VK 与 ZK
  profile。native binary 与 SP1 RISC-V guest 调用同一
  `bridge/neo-execution-core/` + vendored `neo-vm-rs` stateful N4 V1 runtime；C# 在状态
  commit 前校验规范 `NEO4EXR1`，`bridge/neo-zkvm-host/` 再于 SP1 内重执行形成的
  `NEO4PWIT`。PolkaVM 路径(`external/neo-riscv-vm` + `Neo.L2.Executor.RiscV`)是独立
  `ChainMode.L2RiscV` profile，没有匹配 prover 时不继承 SP1 validity。CI 同时门禁真实
  terminal proof/tamper rejection 与非 ignored C#→release Rust native execution。
- **DA writer(in-memory / NeoFS / L1 / DAC / RocksDB)** ✅ — `src/Neo.Plugins.L2DA/`(5 种实现)
- **结算 RPC 客户端** ✅ — `src/Neo.L2.Settlement.Rpc/`
- **可观测性(Prometheus 形态)** ✅ — `src/Neo.L2.Telemetry/`、`Neo.Plugins.L2Metrics/`
- **审计流水线(6 项不变量检查)** ✅ — `src/Neo.L2.Audit/`
- **二分 / 欺诈证明博弈** ✅ — `src/Neo.L2.Challenge/`
- **跨链消息传递** ✅ — `src/Neo.L2.Messaging/`
- **资产注册表 + 充值/提款处理器** ✅ — `src/Neo.L2.Bridge/`
- **按 L2 的 RPC 方法面** ✅ — `src/Neo.Plugins.L2Rpc/`(10 个方法)
- **Phase-5 证明聚合** 🟡 — `src/Neo.Plugins.L2Gateway/` 与 `bridge/neo-zkvm-gateway-{guest,host}` —— `BinaryTreeAggregator`、Secp256r1/Merkle round、规范 binding、持久 outbox、崩溃安全证明恢复与 fail-closed SP1 6.2.1 递归终端证明已交付。RPC 固定调用 `SettlementManager.PublishGatewayGlobalRoot`，其有界 streaming verifier 绑定精确 finalized constituents 并原子转发到 `MessageRouter`。独立审计与真实递归证明部署证据仍是门禁，opaque round proof bytes 仍不能替代最终有效性证明。

**16 个核心链下库 + 2 个 RPC adapter 库 + 8 个插件。** 它们的
`tests/Neo.*.UnitTests/` 镜像计入当前 38 个 .NET 测试工程。Rust、TypeScript、
Python、Solidity 与 ignored 真实证明门禁均由 CI 动态发现和报告，不在说明文字中
钉死易漂移的用例数。

---

## 第 3 层 —— 运维工具

- **链创建 CLI(模板、脚手架)** ✅ — `tools/Neo.Stack.Cli/`(`create-chain`)
- **节点目录初始化** ✅ — `tools/Neo.Stack.Cli/`(`init-l2`)
- **链注册(configBytes hex 输出)** ✅ — `tools/Neo.Stack.Cli/`(`register-chain`)
- **Bridge adapter 部署计划** ✅ — `tools/Neo.Stack.Cli/`(`deploy-bridge-adapter`)
- **排序器 / 批处理器 / 证明者 preflight** ✅ — `tools/Neo.Stack.Cli/`(`start-{sequencer,batcher,prover}`)
- **批次提交 preflight** ✅ — `tools/Neo.Stack.Cli/`(`submit-batch`)
- **配置健全性检查** ✅ — `tools/Neo.Stack.Cli/`(`validate`)
- **声明式 L1 部署计划器** ✅ — `tools/Neo.Hub.Deploy/`(`scaffold` / `plan` / `verify`)
- **Post-deploy 接线提示** ✅ — `tools/Neo.Hub.Deploy/`(`PostDeployActions`)
- **进程内 devnet 运行器** ✅ — `tools/Neo.L2.Devnet/`(默认 5 批次;`--config`、`--data-dir`、`--metrics-port`)
- **样例链 config** ✅ — `samples/`(4 份模板,端到端验证)

**7 个 CLI 工具,合计 9 + 3 + 1 + 4 + 4 + 2 + 5 = 28 个子命令**(计入外链桥 CLI 的 genkey + committee-blob + deploy-bundle + chains-table + 按链 helper)。

`neo-l2-explore` CLI 是框架的终端区块浏览器:`label`(打印 §16.2 5 维安全标签)、
`batch <n>`(单个批次的完整规范承诺 + 状态)、`tail [N]`(浏览最近的批次)、
`audit [N]` —— 独有能力 —— 校验最近 N 个封装批次的状态根连续性,发现不连续时
返回非零。包装 `Neo.L2.Sdk.L2RpcClient`,任何运行 `Neo.Plugins.L2Rpc` 的节点
都是合法端点。

---

## 第 4 层 —— 应用开发

- **L2 合约框架(编译为 NeoVM 字节码)** ✅ — 用 `external/neo-devpack-dotnet/` 中的 `Neo.SmartContract.Framework`(已引入)
- **L2 感知合约模式文档化** ✅ — `docs/launching-an-l2.md`(5 个扩展点 + 3 个实操样例)
- **自定义 IDAWriter / ISequencerCommitteeProvider / IL2Prover 样例** ✅ — `docs/launching-an-l2.md`(实操样例)
- **L2 侧 dApp 样例** ✅ — `samples/contracts/`(跨链 greeter + 提款 demo)
- **样例链 config(rollup / gaming / validium / sidechain)** ✅ — `samples/*.config.json`(4 份模板,端到端验证)
- **应用开发者 SDK / 客户端库(.NET)** ✅ — `src/Neo.L2.Sdk/` —— 类型化 `L2RpcClient`,封装所有 10 条 doc.md §14.1 RPC 方法。失败模式分到 `L2RpcTransportException` / `L2RpcProtocolException` / `L2RpcServerException` / `L2RpcMismatchedChainIdException`,调用方可写定向重试策略。
- **应用开发者 SDK(TypeScript)** ✅ — `sdk/typescript/` —— `@neo-n4/sdk` 类型化封装所有 10 条 RPC 方法。vitest suite 在进程内 stub fetch 上覆盖线协议形态，4 类错误分类与 .NET SDK 一致。
- **应用开发者 SDK(Rust)** ✅ — `sdk/rust/` —— `neo-n4-sdk` 类型化封装。10 条 mockito 驱动的测试通过。镜像 .NET + TS SDK。
- **应用开发者 SDK(Python)** ✅ — `sdk/python/` —— 标准库类型化客户端，覆盖相同 10 条 RPC 方法和 4 类错误；`unittest` 固定响应解析、chain-id 交叉校验以及 transport/protocol/server 失败。

---

## 第 5 层 —— 终端用户界面

- **终端区块浏览器(CLI)** ✅ —— `tools/Neo.L2.Explore/`
  (`neo-l2-explore`)—— `label` / `batch <n>` / `tail [N]` /
  `audit [N]`(状态根连续性检查)。包装 `Neo.L2.Sdk.L2RpcClient`,可
  指向任何运行 `Neo.Plugins.L2Rpc` 的端点。
- **Web 区块浏览器 + 桥 UI + faucet UI** ✅ —— `sdk/web-explorer/index.html`
  —— 单文件静态 HTML(零构建工具),内联 JS SDK。Tab:Explore(label
  / 最新根 / batch)、Bridge(充值状态查询 + neo-bridge CLI 接力,产
  L1 调用 hex)、Faucet(localStorage 后备的冷却期 UI + neo-l2-faucet
  CLI 接力)、Audit(N 批次的状态根连续性)。任何静态文件托管都能放。
- **测试网 faucet(CLI)** ✅ —— `tools/Neo.L2.Faucet.Cli/`
  (`neo-l2-faucet`)—— 生产级 drip CLI(在第 3 层 / 运维工具一节
  涉及)。
- **文档站点(已渲染)** ✅ —— `book.toml` + `docs/SUMMARY.md` 出货
  mdBook 配置,把现有 markdown 文档渲染为可搜索静态站点
  (`mdbook serve` 本地预览,`mdbook build` 给 CI 部署到 GitHub
  Pages / S3 / Netlify)。
- **钱包接入模式** ✅ —— `docs/wallet-integration.md` —— 粘贴进钱包
  hex(冷钥流程)+ 委托签名(热钱包自动化)。NeoLine / Neon / NEP-6
  / Ledger / KMS 的实操样例。所有 CLI 输出规范 hex;框架永不接触私钥。

---

## 覆盖度评估

| 层 | 当前评估 | 明确剩余的生产缺口 |
|----|----------|--------------------|
| L1 协议合约 | 🟡 | 乐观状态变更仅对精确受限 Counter v4 profile 无信任；通用 NeoVM/多交易 fraud proof fail closed。 |
| L2 原生合约 | ✅ | 生产 genesis 与治理演练属于部署证据，不是缺失代码。 |
| 跨外链桥 | ✅ | live committee/foreign-chain 部署证据由具体环境提供。 |
| 节点基础设施 | 🟡 | 精确语义 native SP1 执行、原子状态交接、terminal/recursive SP1 proof 与 SettlementManager 授权的 finalized-constituent 发布已交付；独立审计和同版本真实部署证据仍待完成。PolkaVM validity 仍需自己的匹配 proof profile。 |
| 运维工具 | ✅ | 钱包/HSM 托管与 live submission 是显式运维边界。 |
| 应用开发 | ✅ | 每次发布仍需生成真实节点 conformance 证据。 |
| 终端用户 UI | ✅ | 生产托管与钱包集成是部署选择。 |

**Phase 4 内置 `Sp1StatefulNeoVmV1` profile 已在本地端到端可用。** 非 ignored gate
让 C# 通过 release native executor 执行 bootstrapped Neo genesis state；真实证明 gate
载入编译后的 guest ELF，在 SP1 内运行相同 transition 并核验 committed public-input hash。
工具链(`sp1up` → digest-pinned Docker `cargo prove build --locked`)仍由运维安装。
同版本公网部署与独立审计证据仍缺失，因此这不是 mainnet-ready 声明。

**到 Eth/Tron/Sol 的跨外链桥(doc.md §11.3 Phases B + C)。**
6 个链上合约(`MpcCommitteeVerifier`、`ExternalBridgeRegistry`、
`ExternalBridgeEscrow`、`ExternalBridgeBond`、`ExternalBridgeStubVerifier`、
`MpcCommitteeFraudVerifier`)加一个 L2 原生对应件
(`L2NativeExternalBridgeContract`),3 个 Rust watcher crate
(`watchers/neo-bridge-watcher-{eth,tron,sol}/` —— Eth:消息 + 签名核心,字节
对等测试;Tron:薄重导出,使用 Tron chain-ids;Sol:`Ed25519FileSigner` +
Solana chain-ids 0xE0000020..2F,演练曲线无关的 `Signer` trait,链上派发到
`CryptoLib.VerifyWithEd25519`),所有 3 条目标链的外链 router 工件
(`external/foreign-contracts/eth/` —— `NeoExternalBridgeRouter.sol` + 44
条 Foundry 测试（37 条单链 + 7 条多链），带真 `vm.sign` + `ecrecover`；`external/foreign-contracts/tron/`
—— README 指向 Eth 合约,因 TVM 是 EVM 风味,部署时带 Tron chainId 构造参数;
`external/foreign-contracts/sol/` —— Anchor 程序,使用 Solana 的 ed25519
sigverify precompile,源码就绪等待运维者 `anchor build`),以及一个运维 CLI
(`tools/Neo.External.Bridge.Cli/`,提供 genkey + committee-blob + deploy-bundle)。
`Neo.Hub.Deploy` 把整个桥栈与 NeoHub 一起脚手架化：24 个生产部署步骤 +
post-deploy 提示。Phase C 的 `MpcCommitteeFraudVerifier` 让等价委员会成员的罚没
变成无许可(任何人都可提交对同一 `(chainId, nonce)` 签了两条字节不同消息的
密码学证明,然后把保证金当作奖励领走),由 `UT_MpcFraudProof_RealCrypto.cs`
中 7 条真 secp256k1 测试钉死。Trait 抽象在 secp256k1 和 ed25519 曲线家族间
通用迁移 —— 证实 Phase-B 的 trait 形态是对的。Live RPC 适配器(ethers-rs
`EventSource`、JSON-RPC `NeoSubmitter`、RocksDB `Journal`)是下一步。

所有此前在仓外的 Layer-4 + Layer-5 项目现已都在框架里出货:四种语言的类型化
SDK(.NET / TS / Rust)、覆盖 explorer + bridge + faucet UI 的静态 HTML Web
应用、mdBook 文档站点配置、文档化钱包接入模式。钱包接入仍按委托驱动,框架
永不持有私钥。

## 接下来

超出 spec-gap-plan 的计划项跟踪在 `CHANGELOG.md` 的 `[Unreleased]` 段。L2 开发框架
(本仓主要范围)按 `doc.md` §0–§22 已功能完整;后续迭代在运维体感(更多样例链、
更多自定义实操样例、更多 dApp 样例)上,而不是核心架构。
