# 中文版本：Private Network Testing

> 对应英文文档：[docs/private-network-testing.md](../private-network-testing.md)
> 维护规则：英文文档发生结构、命令、路径、接口、合约数量、测试证据或安全结论变更时，本中文版本必须同步更新。

## 本页用途

这是用户或架构文档。中文版本用于让中文读者获得同等的阅读入口。

## 中文摘要

- 对应文件：docs/private-network-testing.md
- 中文路径：docs/zh/private-network-testing.md
- 适用范围：Neo N4 项目的文档、架构、模块、工具、合约、测试或审计证据的一部分。
- 一致性要求：术语、项目路径、命令、合约名称、模块名称、测试名称和安全结论必须与英文源文件保持一致。
- 生产完备要求：如果英文源文件声明某模块已完成、已验证、已部署演练或已通过测试，中文版本不能降低或扩大该结论；必须同步记录同样的前提和限制。

完整运行必须提供五个已审阅资产：`-NodeConfig`、`-BatcherNodeConfig`、
`-SequencerNeoCli`、`-BatcherNeoCli` 和 `-Prover`。脚本在仅当前用户可访问的 OS 临时目录
中工作，只白名单暂存可执行文件、根 runtime 二进制、`runtimes/` 下允许的二进制类型、
单个必需 plugin 的 runtime/config 和显式审阅的 node config；钱包、节点数据库、日志、
隐藏路径、任意 JSON、symlink/junction 均不复制。sequencer config 的
`UnlockWallet.Path` 必须是指向外部审阅、Neo 兼容 NEP-6 `.json` 钱包的绝对路径；preflight
会用配置密码实际打开钱包，解密配置委员会账户，并验证解密私钥确实派生出对应 validator
公钥。格式错误、密文与账户不匹配、密码错误、不支持的 adapter 文件或无关密钥均 fail closed；自定义
HSM wallet factory 需要单独审阅的 Neo.CLI 集成，不属于通用 preflight 的已验证能力。
临时目录在成功或失败时都由 `finally` 删除，清理失败会让运行 fail closed；普通编译仍写入
gitignored 的 `bin/`、`obj/`、`target/`，不宣称为隔离秘密状态。长期
`artifacts/private-network/<run-id>/` 只保留日志、脱敏命令/状态摘要和显式审计报告。
随后执行 `init-l2`；三个 `start-*` 命令都带显式
`--dry-run`，只证明配置、插件、钱包、可执行文件和参数一致，不把串行启动长驻进程冒充
dBFT 生命周期演练。需要跳过该预检时必须显式传入 `-SkipOperatorPreflight`；有资金的并发
dBFT/进程演练仍是独立发布证据。

```powershell
.\scripts\private-network\Test-PrivateNetwork.ps1 `
  -NodeConfig C:\reviewed\sequencer\config.json `
  -BatcherNodeConfig C:\reviewed\batcher\config.json `
  -SequencerNeoCli C:\reviewed\sequencer\Neo.CLI.dll `
  -BatcherNeoCli C:\reviewed\batcher\Neo.CLI.dll `
  -Prover C:\reviewed\prover\prove-batch.exe
```

跳过真实 proof 或增加 batch 数量但仍保留完整 operator 预检时，必须继续提供上述五个
参数；只有快速 smoke 才可显式使用 `-SkipOperatorPreflight`。

## 维护检查清单

- 英文源文件新增章节时，在这里补充对应中文章节或中文摘要。
- 英文源文件新增图表、SVG、Mermaid、流程图或架构图时，必须在 docs/zh/figures/ 下补齐同名中文图表。
- 英文源文件新增命令时，中文版本必须保留可复制命令，并说明 Windows / WSL2 前提。
- 英文源文件新增安全结论时，中文版本必须保留风险等级、影响范围、修复状态和验证证据。
- 英文源文件新增外部依赖或链上前提时，中文版本必须保留相同前提，不能把未验证的公网部署写成已完成。

## 同步状态

本文件已作为 docs/private-network-testing.md 的中文对应版本纳入仓库级本地化覆盖检查。后续修改由单元测试强制要求中文 counterpart 继续存在。
