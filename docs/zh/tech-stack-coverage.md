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
- **欺诈验证器(治理仲裁模式参照)** ✅ — `contracts/NeoHub.GovernanceFraudVerifier/`
- **欺诈验证器(无信任 v3 —— 链上 Merkle 重派生)** ✅ — `contracts/NeoHub.RestrictedExecutionFraudVerifier/`
- **ZK verifier adapter** ✅ — `contracts/NeoHub.NativeZkVerifier/` 校验 `ProofType.Zk`
  envelope / VK id,并把重型证明数学交给 L1 native accelerator。

**24 个 NeoHub 可部署项目。** 23 个生产合约 + 1 个测试 stub 全部经 `Neo.SmartContract.Framework` 通过类型检查;CI 用
`nccs` 编译每一个并校验 `.nef` + `.manifest.json` 工件。

- **L2 批次信息(chainId、批次号、L1 高度)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 桥(充值时铸造,提款时销毁)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 消息 I/O(出站发出 + 入站应用)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 费用分摊(排序器 / 证明者 / DA 占比)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 paymaster(费用抽象、赞助资产)** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`
- **L2 system-config 缓存** ✅ — `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs`

**6 个 L2 侧原生合约。**

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
- **RISC-V ZK(Stage 2)证明者/验证器 —— 完整路径** ✅ — C# 的 `src/Neo.L2.Proving/RiscVZk/` 是进程内测试接缝(单测用 mock 证明者)。N4 L2 的规范执行目标是 PolkaVM-backed NeoVM2/RISC-V 路径:`external/neo-riscv-vm` + `src/Neo.L2.Executor.RiscV/` + `RiscVTransactionExecutor`。真正的 Stage-2 证明跑在进程外,由 `bridge/neo-zkvm-host/` 编排;当前 legacy Neo N3 VM guest 只作为兼容桥,目标是把 RISC-V execution receipt 边界作为 N4 parity testing 的统一接口。`bridge/neo-zkvm-host/` 带 `execute()` / `prove()` / `verify()` API 和 `prove-batch daemon --watch <dir>` CLI,可作为生产证明守护进程(运维者把封好的批次扔进队列目录,守护进程吐出 `<name>.proof.bin` + `<name>.proof.vk` 用于 L1 提交)。真正 CPU proof 生成 + 验证 + 篡改 hash 拒绝由 `#[ignore]` 测试覆盖,普通 CI 覆盖确定性的 C# RISC-V 证明接缝。
- **DA writer(in-memory / NeoFS / L1 / DAC / RocksDB)** ✅ — `src/Neo.Plugins.L2DA/`(5 种实现)
- **结算 RPC 客户端** ✅ — `src/Neo.L2.Settlement.Rpc/`
- **可观测性(Prometheus 形态)** ✅ — `src/Neo.L2.Telemetry/`、`Neo.Plugins.L2Metrics/`
- **审计流水线(6 项不变量检查)** ✅ — `src/Neo.L2.Audit/`
- **二分 / 欺诈证明博弈** ✅ — `src/Neo.L2.Challenge/`
- **跨链消息传递** ✅ — `src/Neo.L2.Messaging/`
- **资产注册表 + 充值/提款处理器** ✅ — `src/Neo.L2.Bridge/`
- **按 L2 的 RPC 方法面** ✅ — `src/Neo.Plugins.L2Rpc/`(10 个方法)
- **Phase-5 证明聚合** ✅ — `src/Neo.Plugins.L2Gateway/` —— `BinaryTreeAggregator`,带三种 `IRoundProver` 实现:`MultisigRoundProver`(Secp256r1 阈值证明)、`MerklePathRoundProver`(逐叶 包含证明)、`PassThroughRoundProver`(最低成本参照)。递归 ZK fold 变体(SP1 Compress / Halo2 / Risc0)由运维者经同一接缝接入

**16 个链下库 + 8 个插件。** 都有 `tests/Neo.*.UnitTests/` 镜像;1453 条测试横跨 34
个 .NET 工程通过。Rust workspace 出货 21 条默认 CI 测试(host-mode 密码学 + SDK +
zkVM execute 往返)加 2 条 `#[ignore]` 把关测试,演练真实 CPU 证明产生 + 验证(墙
钟 ~4 分钟)。TypeScript SDK 出货 15 条 vitest 测试。

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
- **应用开发者 SDK(TypeScript)** ✅ — `sdk/typescript/` —— `@neo-n4/sdk` 类型化封装所有 10 条 RPC 方法。15 条 vitest 测试在进程内 stub fetch 上通过。线协议形态 + 4 类错误分类与 .NET SDK 一致。
- **应用开发者 SDK(Rust)** ✅ — `sdk/rust/` —— `neo-n4-sdk` 类型化封装。10 条 mockito 驱动的测试通过。镜像 .NET + TS SDK。

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

| 层 | 组件 | ✅ 完成 | 🟡 脚手架 | 🔴 仓外 |
|----|-----:|-------:|----------:|-------:|
| L1 协议合约 | 15 | 15 | 0 | 0 |
| L2 原生合约 | 6 | 6 | 0 | 0 |
| 跨外链桥 | 13 | 13 | 0 | 0 |
| 节点基础设施 | 19 | 19 | 0 | 0 |
| 运维工具 | 12 | 12 | 0 | 0 |
| 应用开发 | 8 | 8 | 0 | 0 |
| 终端用户 UI | 5 | 5 | 0 | 0 |
| **合计** | **78** | **78** | **0** | **0** |

**Phase 4(SP1 ZK 证明)现已端到端可用。** `bridge/neo-zkvm-host/tests/end_to_end.rs`
测试加载已编译的 guest ELF,经真实 SP1 zkVM 跑(42 秒密码学证明工作),并校验
public-input 哈希与 host-mode 执行字节字节一致。工具链
(`sp1up` → `cargo prove build`)仍是运维者安装步骤,但集成是真的、被测的、
钉死在 CI 里的。

**到 Eth/Tron/Sol 的跨外链桥(doc.md §11.3 Phases B + C)。**
6 个链上合约(`MpcCommitteeVerifier`、`ExternalBridgeRegistry`、
`ExternalBridgeEscrow`、`ExternalBridgeBond`、`ExternalBridgeStubVerifier`、
`MpcCommitteeFraudVerifier`)加一个 L2 原生对应件
(`L2NativeExternalBridgeContract`),3 个 Rust watcher crate
(`watchers/neo-bridge-watcher-{eth,tron,sol}/` —— Eth:消息 + 签名核心,字节
对等测试;Tron:薄重导出,使用 Tron chain-ids;Sol:`Ed25519FileSigner` +
Solana chain-ids 0xE0000020..2F,演练曲线无关的 `Signer` trait,链上派发到
`CryptoLib.VerifyWithEd25519`),所有 3 条目标链的外链 router 工件
(`external/foreign-contracts/eth/` —— `NeoExternalBridgeRouter.sol` + 13
条 Foundry 测试,带真 `vm.sign` + `ecrecover`;`external/foreign-contracts/tron/`
—— README 指向 Eth 合约,因 TVM 是 EVM 风味,部署时带 Tron chainId 构造参数;
`external/foreign-contracts/sol/` —— ~638 行 Anchor 程序,使用 Solana 的 ed25519
sigverify precompile,源码就绪等待运维者 `anchor build`),以及一个运维 CLI
(`tools/Neo.External.Bridge.Cli/`,提供 genkey + committee-blob + deploy-bundle)。
`Neo.Hub.Deploy` 把整个桥栈与 NeoHub 一起脚手架化:23 步部署 + 16 条
post-deploy 提示。Phase C 的 `MpcCommitteeFraudVerifier` 让等价委员会成员的罚没
变成无许可(任何人都可提交对同一 `(chainId, nonce)` 签了两条字节不同消息的
密码学证明,然后把保证金当作奖励领走),由 `UT_MpcFraudProof_RealCrypto.cs`
中 7 条真 secp256k1 测试钉死。Trait 抽象在 secp256k1 和 ed25519 曲线家族间
通用迁移 —— 证实 Phase-B 的 trait 形态是对的。Live RPC 适配器(ethers-rs
`EventSource`、JSON-RPC `NeoSubmitter`、RocksDB `Journal`)是下一步。

所有此前在仓外的 Layer-4 + Layer-5 项目现已都在框架里出货:三种语言的类型化
SDK(.NET / TS / Rust)、覆盖 explorer + bridge + faucet UI 的静态 HTML Web
应用、mdBook 文档站点配置、文档化钱包接入模式。钱包接入仍按委托驱动,框架
永不持有私钥。

## 接下来

超出 spec-gap-plan 的计划项跟踪在 `CHANGELOG.md` 的 `[Unreleased]` 段。L2 开发框架
(本仓主要范围)按 `doc.md` §0–§22 已功能完整;后续迭代在运维体感(更多样例链、
更多自定义实操样例、更多 dApp 样例)上,而不是核心架构。
