# 系统性审计 — 2026-10-21 至 10-23（中文执行摘要）

> 本文件是 `docs/audit/systematic-audit-2026-10-21.md`（十四轮审计全记录，中英混排工作日志）
> 的中文执行摘要。完整细节、负向验证记录与逐轮命令输出以英文根文件为准。

## 总览

十四轮审计覆盖：合约套件（6 个 NeoHub 合约）、跨语言编码面（Rust/C#/Python）、
bridge watcher、formal 模型、依赖安全、部署脚手架、CI 工作流与仓库卫生。

**最终基线（2026-10-23 20:00 UTC）**：合约 VM 362/362 · 部署 111/111 ·
execution-core 49 · guest 18 · gateway-guest 15 · 跨语言门禁 119 断言（7 个面）·
cargo audit 漏洞 0（ruzstd 一项 ignore，3 条信息性警告同 sp1 阻塞簇）·
npm audit 0 · formal 模型 9/9 passed · **CI 四大工作流全绿**
（Integration Tests 与 formal model 均为数周以来首次）。

## 修复的缺陷（5 个高危 + 2 个中危 + 流程缺陷）

| 轮 | 缺陷 | 严重度 |
|---|---|---|
| 1-4 | 提现叶哈希链下 Layout A(98B) vs 链上 Layout C(118B) 分叉——`VerifyWithdrawalLeafWithProof` 只折路径不重算叶，对账查不出 | 高 |
| 10 | MultisigVerifier：脚手架从不 `SetMinQuorum`，部署默认 1-of-N 委员会（单 sequencer 签名定稿批次） | 中 |
| 11 | GovernanceController 治理中继 target 注册表键截断：index 压进 1 字节且无上限，第 257 个 target 静默覆盖 target 0 | 中 |
| 12 | 六个已完成产物从未 git add（含 CI 直接调用的门禁脚本）——push 即 CI 断裂 | 高 |
| 14 | **zkVM 见证解析器说错 VM 方言**：fork 重排了 opcode 表（PUSHDATA1=0x0C、0x21=NOP、PUSH1..16=0x11..0x20），`witness.rs` 按标准 N3 解析——所有真实交易见证被 fail-closed 拒绝，批量管道功能性死亡而测试全绿（fixture 手搓了同样错误的方言） | 高 |

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
第十四轮补充第八个维度的锚——**VM 方言**：C# ScriptBuilder 实产 40 字节逐字节
正向锚 + 标准 N3 形状负向拒绝（见根报告 A.16.1）。方言类分叉第三方向量也防不住
（向量会按错误方言生成），只有对拍真实 VM 的产出才能暴露。

## 方法论沉淀

1. **n=1 盲区**（连续三轮）：count=0 的 payload、1 个构成元组、1 个签名者——「最省事的输入
   恰好落在语义分支重合的退化点」。清单项：带计数/列表/多态参数的测试必须覆盖每个枚举值/边界计数。
2. **镜像陷阱**：两侧各贴一份期望值无法发现两侧同错；必须引入任何一方都不产出的第三方向量。
3. **断言必实测**：注释里的「daemon refuses to start」之类断言要实际运行验证——三轮抓到归因错误。
4. **宽松解析分歧**：必须构造出使两边行为不同的输入实际跑通，不能停在「下游大概率会拒绝」。
5. **CI 洋葱**（第十四轮）：未推送的工作树上叠加多轮工作 → CI 从未运行 → 失败点永远停在
   最早的断裂处，后面的断裂零信号。本地绿在此环境下无证明力；多轮推送循环、以 CI 为
   权威，是唯一可靠路径（本地构建持续崩溃时尤其如此）。

## 依赖安全（第六轮 + 第十四轮簇归档）

rustls 全家升级（0.23.46 / pki-types 1.15.1 / webpki 0.103.16 / roots 1.0.10 /
root-certs 1.0.10），quinn-proto 0.11.19（5 个 advisory 清零），vite 8.3.5 /
vitest 4.1.12。第十四轮把 vendored neo-zkvm lockfile 同步升级（跨仓库发布），
父仓库 h2 ignore 移除。生产 TLS 出口（Rust SDK 与 watcher）编译验证通过。

**sp1 6.2.1 阻塞簇**（全部为信息性警告，门禁通过，sp1-sdk 整簇升级后自动清零）：
ruzstd（backtrace→object 链）、lru ^0.12（sp1-prover 链，两条 unsound advisory）、
p3-challenger 0.3.3-succinct（SP1 专属 Plonky3 fork）。夜间 Dependabot 对后两者的
自动更新失败同因。

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

## 第十四轮：CI 马拉松（十二项被掩盖的断裂）

前十三轮成果首次推送后，修复像剥洋葱层层展开：未发布的子模块 pin → workflow 不拉
子模块 → formal digest 门禁 → vendored lockfile 11 个 advisory → 钉已删除合约的
陈旧 invariant 测试 → IntegrationTests 的 Exe 残留与配置错位（该 lane 历史从未
绿过）→ 守卫值漂移 → 覆盖率 0.13% 缺口 → 格式门禁。另补 4 个零覆盖源的单测
（池化序列化 / attestation 域 / finalizer 窗口）。全部细节见根报告 A.16。

## 遗留事项

- `NeoHub.ExternalBridgeEscrow` 合约尚未落地；落地时必须锚定 `external-message` 向量的字节
  （watcher 已在对这些字节签名）
- ruzstd / lru / p3-challenger 随 sp1-sdk 下一次整簇升级自然解决（guest ELF SHA+VK
  双 pin 须随之再生）
- host crate 完整构建需 Docker（guest ELF SHA+VK 双 pin）；Docker Desktop 待 GUI 首次确认
- 真证明 release gates 已于 10-23 20:00 UTC 以 workflow_dispatch 在修复后的 master 上
  触发（原需等待 10-24 的夜间调度）；结果记录于根报告
