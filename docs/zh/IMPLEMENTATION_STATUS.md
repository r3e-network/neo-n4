# 中文版本：Implementation Status

> 对应英文文档：[IMPLEMENTATION_STATUS.md](../../IMPLEMENTATION_STATUS.md)
> 维护规则：英文文档发生结构、命令、路径、接口、合约数量、测试证据或安全结论变更时，本中文版本必须同步更新。

## 本页用途

这份文档是模块实现状态和生产缺口矩阵。中文版本用于审计实现覆盖、剩余门槛和测试证据。

## 中文摘要

- 对应文件：IMPLEMENTATION_STATUS.md
- 中文路径：docs/zh/IMPLEMENTATION_STATUS.md
- 适用范围：Neo N4 项目的文档、架构、模块、工具、合约、测试或审计证据的一部分。
- 一致性要求：术语、项目路径、命令、合约名称、模块名称、测试名称和安全结论必须与英文源文件保持一致。
- 生产完备要求：如果英文源文件声明某模块已完成、已验证、已部署演练或已通过测试，中文版本不能降低或扩大该结论；必须同步记录同样的前提和限制。
- 生产部署要求显式 M-of-N GovernanceController，并对 SettlementManager 执行不可逆 lock；
  hot owner 不能再重接 proof/DA/message 安全依赖或直接回滚。异常 finalized-head 回滚必须
  精确绑定 proposal payload、达到 threshold、经过 timelock 且只能消费一次。
- `Neo.Plugins.L2Settlement` 会在任何执行或 DA 副作用前校验前序 batch、连续区块范围和
  pre/post state root；ChainRegistry 会把同一个非零 genesis root 与 chain config 原子注册并
  永久禁止替换，batch 1 的提交/终局和 off-chain 重启都必须先交叉验证该 L1 trust anchor。
  生产组合还要求 witness 与 forced-inclusion event cursor 都由显式
  durable store 提供。重试仍通过不可变工件和原子状态提交恢复，测试/自定义 `Wire` 保留
  caller-owned 依赖注入边界。
- `NeoHub.ForcedInclusion` 的生产 spam-control token 固定为 Neo N3 原生 GAS；部署、
  `SetGasToken`、非零 `SetFee`、`IsProductionReady` 和 enqueue 都拒绝替代 NEP-17。
  enqueue 向经过 witness 的 `Runtime.Transaction.Sender` 收费并提交该身份，不把入口 script
  hash 当成 EOA；nonce/entry 与 consume 重放标记采用外调前写入、FAULT 原子回滚，避免恶意回调复用状态。
- 当前 solution 含 38 个 .NET 测试项目。2026-07-15 串行全量 TRX 运行发现 2,591 项：
  2,587 通过、0 失败；1 项真实原生 executor 和 3 项精确部署 live SDK 测试因缺少外部
  fixture 明确未执行。英文矩阵的每行数字均来自该次 runner 输出，而不是历史估算。
- CI 会从硬编码的 `https://github.com/r3e-network/neo.git` fetch `r3e/neo-n4-core`，而不
  信任 PR 可修改的 submodule `origin`，并验证 `external/neo` gitlink 是其祖先；
  任何只存在于临时 feature branch、尚未发布到官方 R3E core 分支的引用都会 fail closed。
- P1-1 RPC/SDK ABI 已按 `doc.md` §14.1 对齐：官方 RpcServer 网络级注册端口可通过真实 HTTP 暴露 10 个规范方法与 state-root 重载；四 SDK 统一 u64 十进制字符串、链与请求身份绑定以及 JSON-RPC id/version 校验。该证据仅为本地集成测试，不代表公网部署。
- Phase 4 本地执行闭环现由 `Sp1SettlementExecutionStack` 固定组合：同一持久状态库的
  genesis root、完整 `NEO4STW1`、独立审阅 SHA-256 锁定的
  `neo-zkvm-executor`、原子状态库、持久 proof queue、program VK 与 ZK profile。
  native binary 与 SP1 guest 调用同一 Rust execution core；真实 C#→Rust gate 会执行已
  bootstrap 的 Neo genesis 交易，并在不修改状态的前提下拒绝错误请求哈希、语义、
  post-state root 或 public input。settlement 必须先原子持久化并按字节验证不可变 proof
  artifact，再从该 artifact 重放完全相同的 transition，并以完整 pre-state snapshot 执行
  一次原子 `CompareExchangeAll`；并发 writer 只能有一个成功。幂等重试和启动恢复会修复
  artifact/state 交接处的崩溃窗口。content-addressed SP1 queue 使用 `0700` 目录、`0600`
  工件、16-GiB/64-task 硬上限；只有 `SettlementFinalized` 持久化并发布 hash-bound ack 后，
  daemon 才幂等清理 request/proof/VK/public-values/result/archive，禁止 TTL 提前删除。
  terminal 与 recursive 真实 SP1 proof 均是 required CI job 的无条件步骤。N4 genesis V1 不允许转换中
  增删替换合约 descriptor，未覆盖语义 fail closed。该证据仍是本地/CI 证据，不代表已有
  同版本公网部署或独立审计。

## 阶段成熟度矩阵

各列彼此独立；设计和代码完成不代表已有同版本部署，更不代表生产完备。

| 阶段 | 目标 | 设计/规范 | 代码形态 | 集成路径 | 密码学强制 | 当前版本部署证据 | 生产完备 |
| ---- | ---- | :-------: | :------: | :------: | :--------: | :--------------: | :------: |
| 0 | 侧链 PoC | ✅ | ✅ | ✅ 本地 devnet | N/A | ❌ 无同版本公开部署 | ❌ |
| 1 | NeoHub v0 + 共享桥 | ✅ | ✅ 26 项目 / 24 生产 | ✅ planner + runtime | 🟡 取决于安全档 | ❌ 无同版本审阅部署 | ❌ |
| 2 | 批次结算 | ✅ | ✅ | ✅ 本地端到端 | 🟡 取决于多签/乐观/ZK 档 | ❌ 无同版本审阅部署 | ❌ |
| 3 | 乐观挑战窗口 | ✅ | 🟡 受限可执行 v4 | 🟡 单笔 Counter 状态转移 | 🟡 仅精确注册 v4；通用 NeoVM fail closed | ❌ 无同版本审阅部署 | ❌ |
| 4 | NeoVM2 / RISC-V ZK 有效性 | ✅ | ✅ PolkaVM profile + 精确语义 SP1 profile | ✅ native C#→Rust + terminal proof 本地/CI 门禁 | ✅ `Sp1StatefulNeoVmV1` native/guest 对等、固定 Groth16 验证器、绑定与篡改拒绝；PolkaVM validity 仍需匹配 prover | ❌ 缺少审阅后 NEF/VK 部署证据 | ❌ |
| 5 | Neo Gateway 证明聚合 | ✅ | ✅ aggregator + 持久 outbox | ✅ 原子结算发布路径 | 🟡 SP1 有效性档强制；attested/Merkle 档信任不同 | ❌ 缺少同版本真实执行部署 | ❌ |
| 6 | Neo Stack CLI / 模板 | ✅ | ✅ 12 条命令 | 🟡 三条钱包门禁命令输出运维计划 | N/A | ❌ 无同版本运维部署记录 | ❌ |

图例：✅ 当前版本已有证据 · 🟡 部分完成或依赖档位/配置 · ❌ 缺失 · N/A 不适用。
在独立审计、可复现发布工件、精确版本部署记录、线上正负向冒烟测试及
[`SECURITY.md`](../../SECURITY.md) 的运维要求全部满足前，任何阶段都不能标记为生产完备。

## 维护检查清单

- 英文源文件新增章节时，在这里补充对应中文章节或中文摘要。
- 英文源文件新增图表、SVG、Mermaid、流程图或架构图时，必须在 docs/zh/figures/ 下补齐同名中文图表。
- 英文源文件新增命令时，中文版本必须保留可复制命令，并说明 Windows / WSL2 前提。
- 英文源文件新增安全结论时，中文版本必须保留风险等级、影响范围、修复状态和验证证据。
- 英文源文件新增外部依赖或链上前提时，中文版本必须保留相同前提，不能把未验证的公网部署写成已完成。

## 同步状态

本文件已作为 IMPLEMENTATION_STATUS.md 的中文对应版本纳入仓库级本地化覆盖检查。后续修改由单元测试强制要求中文 counterpart 继续存在。
