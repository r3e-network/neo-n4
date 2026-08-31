# 中文版本：Changelog

> 对应英文文档：[CHANGELOG.md](../../CHANGELOG.md)
> 维护规则（2026-08-31 重定，见审计报告 §6 与 §10 第 19 项）：英文 `CHANGELOG.md`（逾万行、
> 700+ 个带日期条目）是唯一权威的逐条记录。本页是它的**中文重大变更摘要索引**，不承诺
> 逐条锁步：英文新增条目不自动触发本页更新，只有当一条变更属于重大类别（安全修复、
> 审计定案、生产完备性变化）时才在此补充摘要。安全结论与测试证据以英文原文为准；
> 本页转述不得降低或扩大英文结论的前提与限制。

## 本页用途

英文 `CHANGELOG.md` 是项目历史变更的完整权威记录。本页为中文读者维护一个重大变更索引：
按日期倒序收录安全修复、审计定案与生产完备性变化的摘要条目，并有意容忍遗漏 ——
本页缺失某条目不代表该变更未发生；需要逐条证据时以英文原文为准。

## 中文摘要

- 2026-08-31 nightly SP1 release-gate dispatch，并把发布阻塞规则写成文字：唯一产出真实
  batch 与递归 SP1 proof 的 CI job 此前仅限 `workflow_dispatch`，而必需检查 `sp1-host` 在其余
  事件上断言重型 lane 为 `skipped` —— SP1 栈里的回归无法让作者看到的任何东西变红。定案：nightly
  排班拥有该 dispatch（cron `47 3 * * *`），`sp1-release-gates` 的 `if` 与 `sp1-host` 的成功
  断言均同样接受 `schedule`，沿用 sdk-conformance 的先例；PR/push 行为不变（重型 lane 仍
  skipped），断言改为每晚被行使。merge queue 归属被否决（仓库不用它，且逐 PR 重跑会乘上资源
  成本）。发布阻塞规则写入 `docs/release-readiness-checklist.md` §6（EN + zh）：nightly 失败或
  从未成功即阻塞发布，直到发布候选 commit 上手动 dispatch 三条 lane 全绿。钉住 `build.yml` 文本的
  CI 门禁自测同步更新：双事件 `if`、双事件 bash 分支，以及由"禁止 schedule"反转为正面钉住 nightly
  cron；PR/push 下断言 `skipped` 的不变式保留。

- 2026-08-31 Dependabot ignore 注释与其描述的告警状态对齐：cargo `ignore` 块的注释此前读起来像
  "这两条已处理"，而 Security 标签页三条告警全部仍然 open —— `ignore` 抑制的是更新 PR、不是
  告警。注释现在写明机制（告警以受追踪的接受风险保持 open）、点名全部三条在案 GHSA 及严重度、
  指向两份文档，并解释 `p3-symmetric` 不在清单里的原因（无已修补版本，Dependabot 永远不会为它
  发起更新 PR）。同时纠正一处引用：笔记所引的 lru 告警 id `GHSA-qqmc-hwqp-8g2w` 是另一条（2022
  年、use-after-free）lru 记录，在案告警是 `GHSA-rhfx-m35p-ff5j`。未轮换任何钉扎，ignore 集合
  不变；§10 第 17 条的第二个子动作（请 Succinct 把 Plonky3 0.4.3 challenger 修复合进 fork）已
  定案为"要问"，发起询问属外部沟通、留给维护者拍板。

- 2026-08-31 batcher 的 commit 处理器故障现在停插件、不停节点：核心对 `Blockchain.Committed`
  处理器异常的默认策略是 `StopNode`，batcher 内一次瞬态 sink/executor 故障会杀死整条链（审计
  H1）。`L2BatchPlugin` 现在带上审计时 grep 证明全 `src/` 都不存在的那条第一方覆写
  （`ExceptionPolicy => StopPlugin`），并且处理器在重新抛出之前先经持久 persist/ack 路径重试
  一次待持久化的 sealed batch：恢复成功的瞬态故障到不了核心分派；覆盖先行，四条新测试
  （救回不传播 / 重试也失败则重抛原异常且 batch 仍被持有 / 禁用不调用 / 策略为 `StopPlugin`），
  `Neo.Plugins.L2Batch.UnitTests` 70/70。其余 L2 插件保持核心默认。

- 2026-08-31 SP1 队列读路径容忍瞬态共享冲突并始终类型化失败：两个读漏斗
  （`AtomicFileQueueTransport.ReadBoundedPathAsync`、`Sp1GatewayProofProver.ReadBoundedFileAsync`）
  的裸 `File.ReadAllBytesAsync` 曾在全部类型化守卫之外，过滤驱动短暂持有刚改名的文件即以裸
  `IOException` 逃逸（全量两次运行测得一次失败）。现对两处统一给出既有重试惯例：瞬态
  `IOException` 在 2 秒窗口内以 50 毫秒间隔重试；重试耗尽的 `IOException` 明确归入协议的
  `InvalidDataException` 家族并保留内层异常。
- 2026-08-31 结算对账节奏接管挑战窗口过期：`OptimisticChallenge.FinalizeIfPastWindow`
  此前在树内没有任何调用方，Optimistic 链的终局化只能依赖"有人记得去调用"。归属定案为
  `Neo.Plugins.L2Settlement` 的对账节奏：新增 `ISettlementWindowFinalizer` 能力接口，
  pipeline 的 Challengeable 分支在链上截止期过后首次对账即提交终局化并持久记录
  `SettlementFinalized`；生产接线由新的 `OptimisticChallengeHash` 配置键门控，留空保持
  旧行为。无合约改动，`docs/launching-an-l2.md` 已写明归属。
- 2026-08-31 结算摘要绑定块区间：public-inputs preimage 从 332 扩到 348 字节，
  `firstBlock`/`lastBlock` 进入 settlement 合约验证的摘要；合约与全部消费方（gateway guest
  重建、SP1 host、witness artifact、golden 向量、三份 hex fixture）同次迁移，SettlementManager
  NEF 用钉住的 nccs 重发，三份 golden 摘要重钉。全量 38 程序集 / 2,943 测试 / 0 失败；
  Linux SP1 通道重跑前三处 dispatch-only 钉住点仍描述旧公式（审计报告 §11 记录）。
- 2026-08-31 devnet 按配置路由证明类型：runner 读取配置 `proofType` 而非硬编码 Multisig，
  不兼容的标签/证明配对在构建任何东西之前以退出码 2 拒绝。
- 2026-08-31 V2 跨边界编码对齐：321 字节 commitment 头、348 字节 public inputs、91 字节
  chain config、48+32·N proof 分帧，通过两侧都不拥有的共享 golden 向量互钉
  （.NET 编码器 ↔ 部署 NEF ↔ Rust 三腿，`tests/Shared/CanonicalEncodingVectors.cs`）。
- 2026-08-31 V2 文档半：`ChainMode.L2RiscV` 从未存在（执行引擎由 `--vm`/`--executor` 选择，
  `ChainMode` 不做运行时分发），public-inputs 编码器不是 L1 ABI。
- 2026-08-31 H18：`SecurityLevel ⇒ ProofType` 规则曾有四处实现、两处互为拷贝且三对判错、
  `Settled` 无 CLI 行；现在 `Neo.L2.ProofRouting` 是唯一 off-chain 表，链上
  `IsProofTypeCompatible` 以 `[Safe]` 暴露，`tests/Shared/ProofRoutingExpectations.cs`
  是两侧都无法改写的第三参考。
- 2026-08-31 §7.1：生产锁覆盖 pauser set 与 challenge 窗口/赏金，直接路径加锁并配套
  payload 绑定的 `*ViaProposal` 双胞胎。
- 2026-08-30 执行式子系统审计报告：七轨逐子系统"审计+验证"（RISC-V VM、SP1 zkVM、桥/资金
  路径、batch/state/DA、settlement/challenge/反审查、治理/遥测/CLI/RPC/文档），结论
  `C1`–`H19` 与 `V1`–`V8`，EN+zh 双语入库（`docs/audit/` + `docs/zh/audit/`）。
- 2026-08-30 C4：被接受的欺诈证明不再卡死其刚证明有欺诈的链（challenge 窗口清除、重提交
  重武装；VM 复现测试 + 链上阴性对照）。
- 2026-08-30 V4：40 个在 Windows 从未执行的测试现在执行（sln 探测替代脆弱的仓库根遍历）。
- 2026-08-30 H16：暂停链现在同时停止 finalize 而不只是入金（`FinalizeBatch` 断言
  `ChainRegistry.isActive`；`RevertBatch` 有意不设防，保留事后修正能力）。
- 2026-08-30 H17：跨链 finality relay 从仅文档变为可部署（deployer 驱动 gateway 全局根锁引导）。
- 2026-08-30 V8 审计：仓库唯一的 Rust 依赖门禁（cargo audit）看不到 Dependabot 报的三条公告
  （RustSec 与 GHSA 图谱不同）；SP1 6.2.1→6.5.0 按字节对比不修复任何一条（两处被点名文件
  逐字节相同，且无更高 `-succinct` 构建），升级在证据前被拒绝。
- 2026-08-30 V6：唯一绕过注册表的指标在崩溃路径上；现为 `MetricNames` 常量、导出的
  Prometheus 名不变并由测试钉住，调用点由
  `EmissionSites_UseMetricNamesConstants_NotRawLiterals` 守卫。
- 2026-08-30 H19：反审查 deadline 现在部署路径即有界（`[60, 86400]` 与 owner 路径同一对
  常量；`enqueuedAt + deadline` 的 mod 2³² 截断风险以测量定案）。
- 2026-08-29 C1：SharedBridge 存款与 MessageRouter 条目在 batcher inbox 中按 family 去重，
  不再互相挤占或重复排空。
- 2026-08-29 H12：三个 owner 可重写信任根（OptimisticChallenge 窗口/赏金、pauser set 等）
  单向 `LockGovernance`；`SetOwner` 保留为密钥轮换路径，由合约 manifest 不变量测试背书。
- 2026-08-28 SP1 6.2.1 传递性公告（Dependabot High + 两条 Medium）以文档定案：无法从当前
  依赖图修复；`.github/dependabot.yml` 以 `ignore` 静默更新 PR（公告仍在 Security 页保持开放）。
- 2026-08-28 Windows 本地测试 fixture 逃离 JSON 路径转义。
- 2026-07-15 协调依赖维护：覆盖率收集器保留在 6.0.4，因为 10.0.1 会改变可执行行
  统计集合，并使同一提交在 90% 覆盖率门槛附近发生抖动；未来升级必须配套显式的覆盖率
  基线迁移。Ethereum watcher 将
  `sha3` 升级至 0.12、`toml` 升级至 1.1.3，并原子更新 Rust lockfile；GitHub
  workflows 同步升级至 `actions/checkout` 7、`actions/cache` 6、
  `docker/setup-buildx-action` 4、`docker/login-action` 4 与
  `docker/metadata-action` 6。
- 2026-07-15 SP1 release gate 并行化：明确执行 release validation 时，workspace release、
  真实 batch 证明和真实递归 Gateway 证明拆分到三个独立且版本固定的 SP1 runner。
  Pull request 与普通 master push 只运行快速 .NET、合约、原生执行与 Rust 兼容性门禁，
  不重复生成证明；operator 通过 `workflow_dispatch` 显式执行 release-grade lanes。两个证明
  lane 使用 SP1 上游 worker 参数串行化 core/recursion 工作，限制 trace buffer 与 shard
  大小，并在标准托管 runner 上执行 4 GiB guest 内存上限；每条独立 lane 保留 120 分钟的
  生产证明预算；Groth16 证明模式保持不变，也不允许 mock/dummy fallback。
- 2026-07-15 ChainRegistry 准入与治理状态闭合：`ChainRegistry` 在跨合约边界先以完整
  `BigInteger` 校验 `GovernanceController` 返回值必须严格为 0、1 或 2，再转换为 `byte`；
  负数、未定义值及 258 这类截断值都不能被误判成 permissionless，也不会写入 chain config
  或 genesis root。`LockGovernance` 现在同时冻结直接更新路径与 controller 信任根，bootstrap
  owner 锁定后不能替换 proposal authority；迁移必须部署版本化 registry，与
  VerifierRegistry 的既有策略一致。真实 ChainRegistry NEF/测试工件已重生成，非法模式、
  零副作用拒绝、锁后 controller 替换和原 controller 保留均有 VM 回归，NeoHub 合约 VM
  全套 551/551 通过。scaffold 与 live deploy 现在都会执行该不可逆锁，并在声明生产部署
  完成前回读验证锁状态。
- 2026-07-15 settlement finality、崩溃安全回滚与治理锁：`Pending`/`Challengeable` 只记录
  已观察，不再触发 proof queue ack、forced-inclusion consumption、pending retirement 或工件
  清理；这些动作必须等待 L1 `Finalized`。`Reverted` canonical 尾部会把精确 artifact/proof
  隔离，原子恢复经过认证的 pre-tail state snapshot，并以崩溃幂等检查点完成后才允许同编号
  重提；完成检查点只进行键级原子删除，不再复制整个数据库；启动时逐个查询本地 artifact
  的 L1 状态并验证 proof manifest、连续 finality 与
  canonical root。两个内置 store 新增带条件的原子 `CompareExchangeBatch`，RocksDB 使用单次
  同步 WAL `WriteBatch`，关闭跨 wrapper 的 artifact/rollback 竞态。SettlementManager 生产接线
  绑定 GovernanceController 后执行不可逆 lock：hot owner 不能重接安全依赖或直接回滚；异常
  finalized-head 回滚必须匹配绑定 executing contract、达到 threshold、timelock 且只能消费一次
  的精确 proposal payload，从而阻止跨部署重放。
  live deploy 要求显式、互异且 threshold >= 2 的 M-of-N council，拒绝隐式 1-of-1。
- 2026-07-15 不可变链上创世信任锚与精确委员会密钥预检：ChainRegistry 两条准入路径都将
  非零 `genesisStateRoot` 与 91 字节 chain config 原子注册并永久禁止替换；Settlement 在提交
  和终局 batch 1 时都要求连接该根，首批终局前或首批回滚后也返回该根。off-chain profile
  固定并交叉验证同一个值，首个提交者或重启后的运维者都不能静默建立不同信任锚。sequencer
  NEP-6 预检从账户元数据提升为实际解密并验证派生公钥等于配置 validator；即使同一钱包中
  其他账户密码有效，只要委员会账户密文损坏或被替换也会 fail closed。VM、settlement 与 CLI
  回归覆盖缺失/零值/替换根、首批绑定与回退、profile 不匹配、CLI 必填参数和密文替换。
- 2026-07-15 结算连续性与生产持久化 fail-closed：在执行、DA 发布、工件提交和状态确认前
  拒绝缺失前序、区块断链或状态根断链；新增持久存储能力标记，`WireProduction` 拒绝易失
  witness/forced-event store；私网运维预检要求已审阅配置和二进制，并以三个显式 dry-run
  检查参数与部署一致性；runtime/plugin 只白名单暂存到仅当前用户可访问并在 `finally`
  删除的临时目录，钱包、节点数据、日志、任意 JSON、隐藏路径与链接不进入长期工件；
  sequencer dry-run 会实际解密委员会账户并要求派生公钥精确匹配配置 validator，
  格式错误、密文错配、无关或不支持的钱包文件均 fail closed。权限开放的审查上报保持 ABI，但只能携带零地址归因；governance
  必须独立复核已终局 dBFT 证据后，才能在单独的授权调用中指定 slash 目标。
  强制包含 fee token 固定为 Neo N3 原生 GAS，deploy/config/readiness/enqueue 全路径拒绝
  替代 NEP-17；enqueue 向经过 witness 的 transaction sender 收费并提交该身份，不再误用入口
  invocation script hash；consume 在只读 root 外调前预写重放标记并依赖 FAULT 原子回滚。新增 batch 0、
  genesis root、缺失前序、block/state 断链及前序 block overflow 的零副作用回归测试。
  CI 新增强制 ancestry gate：`external/neo` gitlink 必须已经发布到 `r3e-network/neo` 的
  `r3e/neo-n4-core`，并从硬编码 canonical R3E URL 获取分支，不信任 PR 可修改的 submodule
  `origin`；不能依赖仅存在于临时 feature branch 的 core commit。完整 38 项目 TRX
  盘点发现 2,591 项测试：2,587 通过、0 失败、4 项精确部署/native fixture 测试明确受环境门禁。
- 2026-07-14 原生 SP1 执行与原子状态交接：在现有 `neo-zkvm-guest` crate 内新增
  host-native `neo-zkvm-executor`，与 SP1 guest 共享精确 `neo-execution-core` 与 stateful
  NeoVM runtime；新增 Rust/C# `NEO4EXR1` golden、完整请求/语义/roots/gas/effects/
  post-state/public-input 绑定、完整快照 CAS 的 `IAtomicL2KeyValueStore.CompareExchangeAll`、state continuity 与
  contract-binding 校验，以及 SHA-256 锁定隔离副本的 `Sp1StatefulBatchExecutor` 和完整
  `Sp1SettlementExecutionStack`。流水线先持久化并重新读取不可变 proof artifact，再重放
  相同 native transition 并原子提交精确 post-state；重试和启动恢复均幂等，因此崩溃不能
  推进没有持久恢复记录的状态。非 ignored CI gate 会让 C# 调用 release Rust binary 执行
  bootstrapped Neo genesis 交易。N4 genesis V1 对未覆盖 native/syscall 与合约 descriptor
  增删替换仍 fail closed；同版本公网部署和独立审计仍是发布门槛。
- 2026-07-14 SP1 生产加固：terminal 与 recursive 真实 proof 变为 required CI job 的无条件
  步骤；prover queue 强制 `0700` 目录、`0600` 文件、16-GiB/64-task 背压，并且只有 durable
  L1 `SettlementFinalized` 后发布 hash-bound ack 才清理 content-addressed 证据，禁止 TTL。
  build script 还会从同一 ELF byte snapshot 推导 SHA-256/VK，写入 Cargo `OUT_DIR` 的只读
  verified copy 并仅嵌入该副本，消除共享 target 的校验/包含竞态。
- 2026-07-14 文档真值与发布归属：按设计、代码形态、集成、密码学强制、同版本部署证据和生产完备六个维度拆分阶段状态；统一为 26 个 NeoHub 项目、24 个生产部署步骤、38 个 .NET 测试工程、44 个 Foundry 测试及四套类型化 SDK，并以源树驱动测试防止再次漂移；补齐样例合约与安全报告的 R3E Network 归属，启用 GitHub 私密漏洞报告、Dependabot 安全修复、secret scanning 与 push protection，同时明确当前仍无生产 tag 或 release。
- 2026-07-14 P1-1：统一 `doc.md` §14.1、官方 N4 RpcServer adapter、`L2RpcMethods` 与四 SDK 的 10 方法 ABI；所有 u64 使用十进制 JSON string，补齐 bridged-asset 链绑定、proof identity、state-root 可选 batch 与 `getsecuritylabel`，并以共享 conformance、本地真实 Kestrel RpcServer 测试和同 ABI 的 Web Explorer 内联客户端门禁锁定。未声明公网 testnet 或部署证据。
- 2026-07-14 SDK 发布门禁：新增中英文四语言一致性指南、机器可读离线/真实环境报告要求与发布清单；TypeScript 发布包只包含构建后的 `dist`，不再携带源码目录。
- 2026-07-14 Operator 签名边界：新增 fail-closed `--signer-command`，以固定 account、verification script、canonical sign data、超时与 fee-witness 长度校验连接 HSM/KMS 或钱包适配器；补齐独立 sequencer、prover、batcher 进程的真实启动说明，未提供凭据时不声明链上广播证据。
- 2026-07-14 Batch 插件边界：补齐不可变 sink/input wiring、chain domain、metrics 重连、pending retry 顺序与 forced-inclusion durable nonce 过滤测试；null drain 在持久化前 fail closed，L1 消费仍严格推迟到 settlement finality。
- 2026-07-14 Gateway prover 绑定：补齐 proof-system 范围、production backend allowlist、取消、aggregate backend 与 canonical binding 的 fail-closed 测试。
- 2026-07-14 执行事务完整性：补齐 before-image 补偿成功与 commit/rollback 双失败聚合错误、overlay 原子操作和生命周期、canonical effect 字节相等性/哈希及畸形版本测试，并删除无调用的 event copy helper。
- 2026-07-14 治理法定人数恢复：新增 2-of-3 委员会丢失一个签名者后由其余两个成员完成 epoch 绑定、timelock 保护的完整轮换 VM 证据；明确无 owner 绕过，低于阈值时必须停止治理并走另行审计的紧急迁移；同步修正强制包含罚没文档，deadline 后已证明的审查不会被迟到消费抹除。
- 2026-07-14 原生 RISC-V 覆盖门禁：覆盖脚本会构建、复制并强制加载锁定的 `neo_riscv_host` 平台库，记录 SHA-256，缺失时直接失败；真实 ABI 门禁自动发现全部 `RealNative_` 测试并覆盖 Notify、复杂栈、运行时上下文、存储迭代、回滚与错误路径。
- 2026-07-14 Gateway 递归 SP1 与原子终局发布：新增独立 SP1 6.2.1 guest/host，严格校验 `NEO4GWP1` 请求、固定 170 字节 `NEO4GWR2` binding、排序承诺与根、编译期锁定 batch VK 的压缩子证明，并由 host 再验证终端 Groth16；崩溃恢复重新验证完整 marker，只清理 regular non-symlink orphan。新增 `SettlementManager.PublishGatewayGlobalRoot`，以精确 finalized batch references、O(log 4096) 双根重建、每链不可回退 watermark 和同交易 `MessageRouter` 调用闭合授权与最终化绑定。Phase 5 仅因独立审计与真实递归证明部署证据未完成而保持部分完成。
- 2026-07-14 Gateway 零消息根一致性：当 constituent 消息根树的规范结果确实为零时，Rust、.NET、SettlementManager 与 MessageRouter 统一接受已证明的零 `globalMessageRoot`；epoch proof-input 记录独立表示发布存在性，constituent、domain、VK 与 proof 校验保持不变。
- 2026-07-14 SP1 wrapper 供应链与 Apple Silicon 稳定性：独立固定 gnark wrapper 的不可变 amd64 manifest digest，Docker backend 缺少精确引用时在证明前 fail closed；Apple Silicon 使用 SP1 上游 `native-gnark` backend，避免不可靠的 amd64 仿真，同时私网脚本同时执行 batch terminal 与 recursive Gateway 两个真实证明门禁。
- 2026-07-14 外链入站 payout 闭环：L1 immutable adapter、RocksDB relay 与 L2 native `ApplyPayout` 形成 enqueue/prepare/credit/ack 的可恢复状态机；跨 EVM wire 的 foreign asset 全程使用 opaque network-order `ExternalAssetId`，scanner 与 L2 invocation 保持原始 20-byte 顺序，并以非对称地址向量防止误套 Neo `UInt160` 端序。

## 维护检查清单

- 英文源文件出现重大变更（安全修复、审计定案、生产完备性变化）且值得中文入口呈现时，
  在"中文摘要"顶部补充一条摘要；普通条目不强制。
- 英文源文件新增图表、SVG、Mermaid、流程图或架构图时，必须在 docs/zh/figures/ 下补齐同名中文图表。
- 英文源文件新增命令时，中文版本必须保留可复制命令，并说明 Windows / WSL2 前提。
- 英文源文件新增安全结论时，中文版本必须保留风险等级、影响范围、修复状态和验证证据。
- 英文源文件新增外部依赖或链上前提时，中文版本必须保留相同前提，不能把未验证的公网部署写成已完成。

## 同步状态

本文件已作为 `CHANGELOG.md` 的中文对应文件纳入仓库级本地化覆盖检查：单元测试
（`CurrentDocumentation_EveryEnglishMarkdownHasChineseCounterpart`）强制要求本文件继续存在，
但该检查只覆盖"存在"，不校验条目级同步 —— 这与上方重定后的维护规则一致，不是缺陷。
2026-08-28 至 2026-08-31 的重大条目已在本页重定时回填；更早的 2026-07 摘要条目照旧保留。
权威的安全结论与测试证据：英文 `CHANGELOG.md`、`docs/audit/`（及其 `docs/zh/audit/` 镜像）。
