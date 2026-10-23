# 系统性审计 — 2026-10-21 至 10-23（中文执行摘要）

> 本文件是 `docs/audit/systematic-audit-2026-10-21.md`（十二轮审计全记录，中英混排工作日志）
> 的中文执行摘要。完整细节、负向验证记录与逐轮命令输出以英文根文件为准。

## 总览

十二轮审计覆盖：合约套件（6 个 NeoHub 合约）、跨语言编码面（Rust/C#/Python）、
bridge watcher、formal 模型、依赖安全、部署脚手架、CI 工作流与仓库卫生。

**最终基线**：合约 VM 362/362 · 部署 111/111 · parity 14/14 · guest 15/15 ·
跨语言门禁 119 断言（7 个面）· cargo audit 6→1（唯一残留不可达且上游阻塞）·
npm audit 0 · formal 模型 9/9 passed · CI 全绿。

## 修复的缺陷（4 个合约缺陷 + 2 个流程缺陷）

| 轮 | 缺陷 | 严重度 |
|---|---|---|
| 1-4 | 提现叶哈希链下 Layout A(98B) vs 链上 Layout C(118B) 分叉——`VerifyWithdrawalLeafWithProof` 只折路径不重算叶，对账查不出 | 高 |
| 10 | MultisigVerifier：脚手架从不 `SetMinQuorum`，部署默认 1-of-N 委员会（单 sequencer 签名定稿批次） | 中 |
| 11 | GovernanceController 治理中继 target 注册表键截断：index 压进 1 字节且无上限，第 257 个 target 静默覆盖 target 0 | 中 |
| 12 | 六个已完成产物从未 git add（含 CI 直接调用的门禁脚本）——push 即 CI 断裂 | 高 |

另修正三处错误断言（P2-3 mock 归因、保留字节归因、watcher stub 归因）。

## 建立的跨语言锚（7 个面）

以第三实现（Python）产出权威向量 → `tests/Shared/canonical_encoding_vectors.hex`
→ 两侧 `include_str!`/导出断言逐字节钉住，`CROSS_LANGUAGE_SURFACES` 门禁强制：

1. commitment（321B）
2. public-inputs（352B，含非零 forced count）
3. **withdrawal-leaf**（116+amountLen，Layout C——曾经的 4 个月分叉由此永久钉死）
4. withdrawal-merkle
5. chain-config（91B）
6. **external-message**（102+payloadLen——三份零锚镜像：Rust watcher 签名侧 / C# 消费侧 / 测试手搓）
7. **gateway-frontier**（同叶双折叠：承诺根 duplicate-odd / 消息根 promote-odd；既有测试只用 n=1 掩盖了不对称）

每个锚均经负向验证（注入分叉 → 测试失败 → 恢复 → 全绿）。

## 方法论沉淀

1. **n=1 盲区**（连续三轮）：count=0 的 payload、1 个构成元组、1 个签名者——「最省事的输入
   恰好落在语义分支重合的退化点」。清单项：带计数/列表/多态参数的测试必须覆盖每个枚举值/边界计数。
2. **镜像陷阱**：两侧各贴一份期望值无法发现两侧同错；必须引入任何一方都不产出的第三方向量。
3. **断言必实测**：注释里的「daemon refuses to start」之类断言要实际运行验证——三轮抓到归因错误。
4. **宽松解析分歧**：必须构造出使两边行为不同的输入实际跑通，不能停在「下游大概率会拒绝」。

## 依赖安全（第六轮）

rustls 全家升级（0.23.46 / pki-types 1.15.1 / webpki 0.103.16 / roots 1.0.10 /
root-certs 1.0.10），quinn-proto 0.11.19（5 个 advisory 清零），vite 8.3.5 /
vitest 4.1.12。生产 TLS 出口（Rust SDK 与 watcher）编译验证通过。
唯一残留 ruzstd（不可达 + 上游阻塞：`sp1-sdk → backtrace → object 0.37.3 → ^0.8.1`）。

## WSL 本地复刻 CI 车道（第七轮）

Ubuntu + Rust 1.88.0 + `CARGO_TARGET_DIR` 指向 WSL 原生路径，逐条复刻
`sp1-pr-gate`：execution-core 47 / gateway-guest 14 / guest 18 全过——关闭
「本机 Windows+1.95 验证 vs CI Linux+1.88」的双维度差距。

## Formal 模型 digest 刷新（第十二轮后）

合约变更触发模型的 fail-closed 门（设计行为）。逐一重审对应关系后刷新 9 个模型的
源 digest 并重生成 result JSON（全部 passed）：
SharedBridge 新增 admin 中继不动 escrow 账务；RollupHub 新增授权包装且全部状态转换预先存在
（nonce-0 修复实际是合约向 FIFO 模型对齐——模型本就建模首 nonce 为 0）；
governance 门语义未变；preimage 单射抽象布局无关；子模块 L2 合约账务未变。
同时修复 formal-model.yml 不拉子模块的既有 bug（自 9-25 起每次 push 必红）。

## 遗留事项

- `NeoHub.ExternalBridgeEscrow` 合约尚未落地；落地时必须锚定 `external-message` 向量的字节
  （watcher 已在对这些字节签名）
- ruzstd 随 sp1-sdk 下一次升级自然解决
- host crate 完整构建需 Docker（guest ELF SHA+VK 双 pin）；Docker Desktop 待 GUI 首次确认
