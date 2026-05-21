# 第 17 章：生产完备性

生产完备不是“测试通过”四个字。它需要代码、配置、密钥、监控、治理、审计和回滚路径全部闭环。

## 17.1 生产维度

| 维度 | 最低要求 |
| --- | --- |
| 代码 | 构建、格式、单元、集成、文档测试通过 |
| 合约 | manifest 固定、deploy plan 可复核、owner 明确 |
| 密钥 | WIF 不落盘，生产使用 multisig/KMS/HSM |
| DA | NeoFS 生产配置、复制策略、availability probe |
| Prover | verifier contract、VK、proof job queue、失败重试 |
| Bridge | 限额、pause、spent 状态、外部链 finality |
| Governance | timelock、多签、变更记录 |
| Observability | metrics、logs、alerts、dashboards |
| Incident | emergency runbook、rollback、escape hatch |

## 17.2 从 testnet 到 mainnet

| 项目 | testnet | mainnet |
| --- | --- | --- |
| owner | 可用测试账户 | 多签 / timelock |
| assets | 小额测试资产 | 真实资产、限额、审计 |
| proof | safe-by-default verifier route | 真实 verifier + VK |
| DA | NeoFS-like 或测试 NeoFS | 生产 NeoFS |
| watcher | 单机演练可接受 | 多方部署 + bond |
| monitoring | 手动检查可接受 | 自动告警 |

## 17.3 不允许伪装完成的事项

这些事项如果没有真实证据，不能写成完成：

- 主网部署；
- 真实资金桥接；
- 真实 ZK verifier 验证生产 proof；
- 外部链大额转移；
- 多签治理上线；
- NeoFS 生产复制和恢复演练；
- disaster recovery drill。

## 17.4 版本发布检查

发布前至少运行：

```powershell
dotnet restore .\Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet build .\Neo.L2.sln -c Release /p:NuGetAudit=false --nologo
dotnet test .\Neo.L2.sln -c Release /p:NuGetAudit=false --nologo
dotnet format .\Neo.L2.sln --verify-no-changes --exclude external/
wsl bash -lc 'cd /mnt/d/Git/neo-n4 && mdbook build'
node --test tests\experience-hub\*.test.mjs
```

Rust / zkVM / watcher 还需要：

```powershell
wsl bash -lc 'cd /mnt/d/Git/neo-n4 && cargo fmt --all -- --check'
wsl bash -lc 'cd /mnt/d/Git/neo-n4 && cargo test --workspace --release --locked'
```

## 17.5 审计证据如何保存

当前仓库规则：

| 类型 | 是否提交 |
| --- | --- |
| Markdown 总结 | 是 |
| JSON 结构化证据 | 是 |
| 原始 `.log` 命令输出 | 否 |
| WIF / secrets | 永不 |
| 大型临时 artifacts | 否 |

原因：

- 原始日志噪声大；
- 容易误带 secrets；
- 不利于长期阅读；
- 可通过命令重新生成。

## 17.6 生产事故处理

典型事故与响应：

| 事故 | 第一响应 |
| --- | --- |
| DA unavailable | pause settlement，切换读取端，恢复 NeoFS 数据 |
| prover backlog | 降低 batch rate，增加 prover worker |
| invalid batch | challenge / reject / rollback L2 pending state |
| bridge exploit | pause bridge，冻结 finalization，审计 spent 状态 |
| watcher equivocation | slash bond，替换 committee |
| governance key risk | emergency rotate，多签审计 |

## 17.7 读者最终应该能判断什么

读完本章后，你应该能区分：

- “代码实现完成”；
- “本地测试通过”；
- “testnet 演练通过”；
- “生产配置完成”；
- “mainnet 安全上线”。

这五者不是同一件事。

