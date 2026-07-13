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
- 2026-07-14 P1-1：统一 `doc.md` §14.1、官方 N4 RpcServer adapter、`L2RpcMethods` 与四 SDK 的 10 方法 ABI；所有 u64 使用十进制 JSON string，补齐 bridged-asset 链绑定、proof identity、state-root 可选 batch 与 `getsecuritylabel`，并以共享 conformance、本地真实 Kestrel RpcServer 测试和同 ABI 的 Web Explorer 内联客户端门禁锁定。未声明公网 testnet 或部署证据。
- 2026-07-14 SDK 发布门禁：新增中英文四语言一致性指南、机器可读离线/真实环境报告要求与发布清单；TypeScript 发布包只包含构建后的 `dist`，不再携带源码目录。
- 2026-07-14 Operator 签名边界：新增 fail-closed `--signer-command`，以固定 account、verification script、canonical sign data、超时与 fee-witness 长度校验连接 HSM/KMS 或钱包适配器；补齐独立 sequencer、prover、batcher 进程的真实启动说明，未提供凭据时不声明链上广播证据。
- 2026-07-14 Batch 插件边界：补齐不可变 sink/input wiring、chain domain、metrics 重连、pending retry 顺序与 forced-inclusion durable nonce 过滤测试；null drain 在持久化前 fail closed，L1 消费仍严格推迟到 settlement finality。
- 2026-07-14 Gateway prover 绑定：补齐 proof-system 范围、production backend allowlist、取消、aggregate backend 与 canonical binding 的 fail-closed 测试。
- 2026-07-14 执行事务完整性：补齐 before-image 补偿成功与 commit/rollback 双失败聚合错误、overlay 原子操作和生命周期、canonical effect 字节相等性/哈希及畸形版本测试，并删除无调用的 event copy helper。
- 2026-07-14 治理法定人数恢复：新增 2-of-3 委员会丢失一个签名者后由其余两个成员完成 epoch 绑定、timelock 保护的完整轮换 VM 证据；明确无 owner 绕过，低于阈值时必须停止治理并走另行审计的紧急迁移；同步修正强制包含罚没文档，deadline 后已证明的审查不会被迟到消费抹除。
- 2026-07-14 原生 RISC-V 覆盖门禁：覆盖脚本会构建、复制并强制加载锁定的 `neo_riscv_host` 平台库，记录 SHA-256，缺失时直接失败；真实 ABI 门禁自动发现全部 `RealNative_` 测试并覆盖 Notify、复杂栈、运行时上下文、存储迭代、回滚与错误路径。

## 维护检查清单

- 英文源文件新增章节时，在这里补充对应中文章节或中文摘要。
- 英文源文件新增图表、SVG、Mermaid、流程图或架构图时，必须在 docs/zh/figures/ 下补齐同名中文图表。
- 英文源文件新增命令时，中文版本必须保留可复制命令，并说明 Windows / WSL2 前提。
- 英文源文件新增安全结论时，中文版本必须保留风险等级、影响范围、修复状态和验证证据。
- 英文源文件新增外部依赖或链上前提时，中文版本必须保留相同前提，不能把未验证的公网部署写成已完成。

## 同步状态

本文件已作为 CHANGELOG.md 的中文对应版本纳入仓库级本地化覆盖检查。后续修改由单元测试强制要求中文 counterpart 继续存在。
