# Neo N4 全栈验证报告

日期：2026-05-20
仓库：`D:\Git\neo-n4`
分支：`master`
HEAD：`2ba7db9`

> **仅作为历史证据。** 2026-07-13 的实时 RPC 复核确认，记录中的 23 个合约和
> 13 笔 HALT 交易仍然存在，但已部署 NEF 的校验和与完成安全加固后的当前源码不一致。
> 详见 [`../../../audit/testnet-evidence-status-2026-07-13.json`](../../../audit/testnet-evidence-status-2026-07-13.json)。
> 本报告不得作为当前代码树的发布证据。

## 结果

已完成有证据记录的全栈验证。当前实现可以构建、测试、编译合约、运行使用 NeoFS DA 的私有 devnet 冒烟测试、运行规范默认的 NeoVM2/RISC-V 路径、生成并验证 SP1 zk proof、验证 EVM/Solana 桥组件、构建 SDK/文档、检查供应链安全，并通过 RPC 重新验证当前 Neo N3 testnet 部署状态。

本报告不会把仍需生产运营执行的动作描述为已完成。剩余生产门槛列在下方。

## 策展证据索引

原始命令输出只作为本地工作证据使用，不保留在 `docs/audit` 中。仓库只保留便于审阅、本地化和版本管理的 Markdown/JSON 摘要证据：

- `09-testnet-rpc.json`：2026-05-20 contract-first 部署的历史 Neo testnet RPC 验证。
- `testnet-dry-run.json`：23 个生产 NeoHub 合约的部署 dry-run 证据。
- `10-contract-zk-verifier.json`：链上 ABI、安全默认状态、畸形 proof FAULT、`VerifierRegistry` ZK 路由验证。
- `11-docs-consistency.md`：过时术语扫描和文档一致性证据。
- `README.md`：面向人的全栈验证摘要。
- `../testnet-deployment-2026-05-20-contract-first.json`：历史 testnet 部署报告。
- `../testnet-deployment-2026-05-20-contract-first-dry-run.json`：历史 testnet dry-run 报告。

## 通过摘要

- .NET solution：补齐本验证计划的中文 counterpart 后，restore/build/format/test 全部通过。
- 合约编译：生成 26 组可部署 `.nef`/manifest；46 个 manifest/deploy planner 测试通过。
- Neo core 与执行：10 个 L2 native core 测试和 10 个 NeoVM2/RISC-V executor 测试通过。
- 私有 devnet：general、gaming、exchange validium、privacy sidechain 配置完成；RISC-V 执行模式完成；所有运行都报告 NeoFS DA 和 `da_availability`。
- Rust 与 zkVM：workspace 测试通过；PolkaVM host check 通过；SP1 真实 proof 已生成并验证。
- 异构桥接：39 个 EVM 测试和 22 个 Solana 测试通过。
- SDK/文档/UI 数据：TypeScript SDK 16 个测试通过；Experience Hub Node 测试通过；mdBook 构建通过；文档完整性测试通过。
- 测试网：`https://testnet1.neo.coz.io:443` 上 23/23 个计划合约存在；13 个记录交易均为 `HALT`；23 个合约状态存在。

## 测试网状态

- Network magic：`894710606`
- Owner address：`NLtL2v28d7TyMEaXcPqtekunkFRksJ7wxu`
- `ContractZkVerifier`：`0xd52484a842b97555c56bd93ecf919df3f78366f7`
- `VerifierRegistry`：`0x3b96ba201a2ef32f98da7b72e14acb0329b6e017`
- `VerifierRegistry.getVerifier(ProofType.Zk=3)`：`0xd52484a842b97555c56bd93ecf919df3f78366f7`
- 本次 live deployment 动作：跳过，因为已有 live deployment 报告已通过独立 RPC 重新验证。

## 安全记录

- NuGet vulnerable package scan：无漏洞包。
- TypeScript SDK npm audit：0 vulnerabilities。
- cargo audit：0 个直接漏洞；上游仍有 `ansi_term`、`bincode`、`number_prefix`、`paste`、`rustls-pemfile`、`lru` 警告。
- `lru 0.12.5` 来自 `sp1-prover -> sp1-sdk -> neo-zkvm-host`；需要持续跟进 SP1 升级，或在上游支持后切换到 patched path。
- 精确扫描确认用户提供过的 testnet WIF 不存在于 Git 跟踪文件。
- 宽泛密钥模式匹配已复核为 placeholder、测试、lockfile hash、生成/审计证据或 Kubernetes secret 引用；未确认真实项目凭据泄露。

## 剩余生产门槛

- `ContractZkVerifier` 已部署并完成路由。
- 它当前默认安全，因为 public testnet proof systems 没有启用 proof verifier、verification key 或 envelope-only 模式。
- 生产 ZK acceptance 仍需要通过预期治理/运营流程注册真实 proof verifier 合约和 verification keys。
- 外部桥 committee registration 与每条链的 `L1TxFilter` wiring 仍是按支持链逐项执行的运营动作。
- 本次没有完成 Experience Hub 浏览器级视觉验证，因为当前会话未暴露 Browser plugin 执行入口；Node 层 hub 测试和 mdBook 验证已通过。

## 验证期间修复

- 补齐全栈验证计划的中文 counterpart。
- 从 `doc.md`、`docs/zh/doc.md` 和 Experience Hub 文案中移除用户可见 NeoX framing。
- 补齐 Phase 11 文档一致性报告的中文 counterpart。
