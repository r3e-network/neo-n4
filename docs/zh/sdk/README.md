# 中文版本：SDKs

> 对应英文文档：[sdk/README.md](../../../sdk/README.md)
> 维护规则：英文文档发生结构、命令、路径、接口、合约数量、测试证据或安全结论变更时，本中文版本必须同步更新。

## 本页用途

这是 Neo Elastic Network L2 RPC SDK 总览。中文版本用于解释各语言客户端、
安装入口、API 面和与 `Neo.Plugins.L2Rpc` 的关系。

## 中文摘要

- 对应文件：sdk/README.md。
- 当前 SDK 覆盖 C#/.NET、TypeScript、Rust、Python 四种类型化绑定，以及一个零构建静态 Web Explorer。
- 四种类型化 SDK 都遵循同一形状：构造函数接收 `(endpoint, chainId)`，响应内出现 chainId 时必须交叉校验。
- 四种类型化 SDK 都覆盖 `doc.md` §14.1 的 10 个 RPC 方法：批次、批次状态、状态根、提款证明、消息证明、L1 deposit 状态、资产映射、安全等级和完整安全标签。
- 错误分类保持一致：`Transport`、`Protocol`、`Server`、`MismatchedChainId`。
- Python SDK 位于 `sdk/python/`，使用标准库实现，测试命令为 `PYTHONPATH=sdk/python python3 -m unittest discover -s sdk/python/tests`。

## 维护检查清单

- 英文源文件新增章节时，在这里补充对应中文章节或中文摘要。
- 英文源文件新增图表、SVG、Mermaid、流程图或架构图时，必须在 docs/zh/figures/ 下补齐同名中文图表。
- 英文源文件新增命令时，中文版本必须保留可复制命令，并说明 Windows / WSL2 前提。
- 英文源文件新增安全结论时，中文版本必须保留风险等级、影响范围、修复状态和验证证据。
- 英文源文件新增外部依赖或链上前提时，中文版本必须保留相同前提，不能把未验证的公网部署写成已完成。

## 同步状态

本文件已作为 sdk/README.md 的中文对应版本纳入仓库级本地化覆盖检查。后续修改由单元测试强制要求中文 counterpart 继续存在。
