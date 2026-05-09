# Neo Elastic Network — 中文文档

> 本目录是英文架构文档（[`docs/`](../)）的中文版镜像。
> 内容与英文文档保持同步；当两者发生冲突时，[`doc.md`](../../doc.md)（中文母版规范）为权威。

## 翻译进度

英文架构文档体系包含 5 个主要章节 + 1 个 L1 vs L2 设计分析章节，
每章节都会逐步翻译并配上中文图表。当前进度：

| 章节 | 英文版 | 中文版 |
|------|--------|--------|
| 文档导航（Atlas） | [`architecture-atlas.md`](../architecture-atlas.md) | [`architecture-atlas.md`](./architecture-atlas.md) ✅ |
| L2 链生命周期 | [`architecture-l2-lifecycle.md`](../architecture-l2-lifecycle.md) | _翻译中_ |
| L1 vs L2 职责划分 | [`architecture-l1-vs-l2.md`](../architecture-l1-vs-l2.md) | _翻译中_ |
| 数据线格式 | [`architecture-wire-formats.md`](../architecture-wire-formats.md) | _待翻译_ |
| 信任边界 | [`architecture-trust-boundaries.md`](../architecture-trust-boundaries.md) | _待翻译_ |
| 术语表 + 组件目录 | [`architecture-glossary.md`](../architecture-glossary.md) | _待翻译_ |

## 图表

[`figures/architecture/`](./figures/architecture/) 存放
中文版 SVG 图表，与英文版（[`docs/figures/architecture/`](../figures/architecture/)）
一一对应。SVG 内的所有文字标签均翻译成中文，几何布局保持一致。

| 图表 | 文件 | 状态 |
|------|------|------|
| 系统四层拓扑 | [`system-tiers.svg`](./figures/architecture/system-tiers.svg) | ✅ |
| NeoHub L1 合约组成 | [`neohub-anatomy.svg`](./figures/architecture/neohub-anatomy.svg) | ✅ |
| L1 ↔ L2 跨层数据流 | [`l1-l2-bridge.svg`](./figures/architecture/l1-l2-bridge.svg) | ✅ |
| 信任边界总图 | [`trust-boundaries.svg`](./figures/architecture/trust-boundaries.svg) | ✅ |

## 术语对照

为保持中英文术语一致，使用以下对照表（与 [`doc.md`](../../doc.md) 的用法对齐）：

| 英文 | 中文 |
|------|------|
| L1 anchor | L1 锚定层 |
| canonical (wire format / bytes) | 规范（线格式 / 字节） |
| settlement | 结算 |
| bridge | 桥 |
| escrow | 托管 |
| messaging | 消息传递 |
| sequencer | 排序器 |
| batcher | 批处理器 |
| prover (daemon) | 证明守护进程 |
| DA writer | DA 写入器 |
| watcher (daemon) | 中继器（守护进程） |
| committee | 委员会 |
| slash / slashable | 罚没 / 可罚没 |
| forced inclusion | 强制包含 |
| optimistic challenge | 乐观挑战 |
| trust boundary | 信任边界 |
| economic security | 经济安全 |
| reorg | 重组 |
| min_confirmations | 最小确认数 |
| chain config | 链配置 |
| chain id | 链 ID |
| §16.2 dimensions | §16.2 维度 |
| executor | 执行器 |
| native contract | 原生合约 |
| plugin | 插件 |
| cursor | 游标 |
| nonce | 随机数 / 序号 |
| Merkle proof | Merkle 证明 |
| public input hash | 公共输入哈希 |

## 与英文版的对应规则

- **目录结构镜像**：每个 `docs/<name>.md` 对应 `docs/zh/<name>.md`。
- **图表镜像**：每个 `docs/figures/architecture/<name>.svg` 对应
  `docs/zh/figures/architecture/<name>.svg`。SVG 内文字翻译，几何不变。
- **链接相对路径**：中文版章节之间互相链接使用 `./` 前缀；
  指向英文版（如 `doc.md` 母版）使用 `../../` 前缀。
- **代码 / 配置 / 命令**：所有代码块、TOML 字段名、CLI 参数、
  合约名都保持原文（不翻译），与英文版一致。
- **同步原则**：英文版有更新时，中文版应在合理时间内同步。
  当前所有英文章节为 2026-05-09 之后的版本。

## 入口

新读者建议从 [`architecture-atlas.md`](./architecture-atlas.md) 开始 ——
它会根据角色（运维 / SDK 开发 / 审计 / 贡献者）推荐阅读路径。
