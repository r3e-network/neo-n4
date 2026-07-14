# 中文版本：Changelog

> 对应英文文档：[CHANGELOG.md](../../CHANGELOG.md)
> 维护规则：英文文档发生结构、命令、路径、接口、合约数量、测试证据或安全结论变更时，本中文版本必须同步更新。

## 本页用途

这份文档记录项目的历史变更。中文版本作为变更日志的中文入口，维护当前结构、阅读规则和重要变更索引。

## 中文摘要

- 对应文件：CHANGELOG.md
- 中文路径：docs/zh/CHANGELOG.md
- 适用范围：Neo N4 项目的文档、架构、模块、工具、合约、测试或审计证据的一部分。
- 一致性要求：术语、项目路径、命令、合约名称、模块名称、测试名称和安全结论必须与英文源文件保持一致。
- 生产完备要求：如果英文源文件声明某模块已完成、已验证、已部署演练或已通过测试，中文版本不能降低或扩大该结论；必须同步记录同样的前提和限制。
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
  L1 `SettlementObserved` 后发布 hash-bound ack 才清理 content-addressed 证据，禁止 TTL。
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

- 英文源文件新增章节时，在这里补充对应中文章节或中文摘要。
- 英文源文件新增图表、SVG、Mermaid、流程图或架构图时，必须在 docs/zh/figures/ 下补齐同名中文图表。
- 英文源文件新增命令时，中文版本必须保留可复制命令，并说明 Windows / WSL2 前提。
- 英文源文件新增安全结论时，中文版本必须保留风险等级、影响范围、修复状态和验证证据。
- 英文源文件新增外部依赖或链上前提时，中文版本必须保留相同前提，不能把未验证的公网部署写成已完成。

## 同步状态

本文件已作为 CHANGELOG.md 的中文对应版本纳入仓库级本地化覆盖检查。后续修改由单元测试强制要求中文 counterpart 继续存在。
