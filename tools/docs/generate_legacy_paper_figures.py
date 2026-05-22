#!/usr/bin/env python3
"""Regenerate legacy Neo N4 SVG figures as paper-style technical diagrams.

This updates the older figures that existed before the paper-style figure set.
The output keeps the original filenames so README, whitepaper, architecture
chapters, and Chinese mirrors keep working.
"""

from __future__ import annotations

import html
import textwrap
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
EN_ROOT = ROOT / "docs" / "figures"
ZH_ROOT = ROOT / "docs" / "zh" / "figures"


@dataclass(frozen=True)
class LegacyFigure:
    rel: str
    title_en: str
    title_zh: str
    focus_en: str
    focus_zh: str
    family: str


LEGACY_FIGURES: tuple[LegacyFigure, ...] = (
    LegacyFigure("architecture.svg", "Neo N4 Three-Tier Architecture", "Neo N4 三层架构", "L1 settlement, optional gateway aggregation, elastic L2 execution, and NeoFS data availability.", "L1 结算、可选 Gateway 聚合、弹性 L2 执行和 NeoFS 数据可用性。", "architecture"),
    LegacyFigure("tx-lifecycle.svg", "Transaction Lifecycle", "交易生命周期", "How a user transaction becomes L2 state, evidence, and L1-finalized effects.", "用户交易如何变成 L2 状态、证据和 L1 终局效果。", "execution"),
    LegacyFigure("proof-aggregation.svg", "Proof Aggregation", "证明聚合", "How many L2 proof claims are reduced into compact settlement evidence.", "多条 L2 证明声明如何归约为紧凑结算证据。", "proof"),
    LegacyFigure("forced-inclusion.svg", "Forced Inclusion", "强制纳入", "Anti-censorship path from L1 request to L2 inclusion or sequencer accountability.", "从 L1 请求到 L2 纳入或排序器问责的抗审查路径。", "security"),
    LegacyFigure("telemetry-pipeline.svg", "Telemetry Pipeline", "遥测管线", "How runtime signals become metrics, alerts, audit logs, and operator decisions.", "运行信号如何变成指标、告警、审计日志和运维决策。", "operations"),
    LegacyFigure("trust-spectrum.svg", "Trust Spectrum", "信任光谱", "Security levels from trusted sidechain behavior to validity-proof finality.", "从受信侧链行为到有效性证明终局的安全层级。", "trust"),
    LegacyFigure("architecture/admission-states.svg", "L2 Admission State Machine", "L2 准入状态机", "The state gates that turn a configured L2 into an active settlement participant.", "把已配置 L2 变成活跃结算参与者的状态闸门。", "state"),
    LegacyFigure("architecture/bridge-sequences.svg", "Bridge Sequence Model", "桥接序列模型", "Deposit, withdrawal, and message paths across L1, L2, and relayer boundaries.", "充值、提款和消息路径如何跨越 L1、L2 和 relayer 边界。", "bridge"),
    LegacyFigure("architecture/byte-layout-depositpayload.svg", "DepositPayload Byte Layout", "DepositPayload 字节布局", "Canonical byte layout for deposits and replay-protected bridge payloads.", "充值和防重放桥 payload 的规范字节布局。", "byte"),
    LegacyFigure("architecture/byte-layout-externalcrosschainmessage.svg", "ExternalCrossChainMessage Byte Layout", "ExternalCrossChainMessage 字节布局", "Canonical envelope for heterogeneous-chain bridge messages.", "异构链桥消息的规范封装格式。", "byte"),
    LegacyFigure("architecture/byte-layout-l2batchcommitment.svg", "L2BatchCommitment Byte Layout", "L2BatchCommitment 字节布局", "Canonical commitment over L2 batch range, roots, DA, and proof inputs.", "覆盖 L2 批次范围、根、DA 和证明输入的规范承诺。", "byte"),
    LegacyFigure("architecture/byte-layout-l2chainconfig.svg", "L2ChainConfig Byte Layout", "L2ChainConfig 字节布局", "Canonical chain-admission bytes bound into the L1 registry.", "绑定进 L1 注册表的链准入规范字节。", "byte"),
    LegacyFigure("architecture/byte-layout-publicinputs.svg", "PublicInputs Byte Layout", "PublicInputs 字节布局", "Verifier-visible statement binding batch, roots, DA, and proof mode.", "验证器可见语句如何绑定批次、根、DA 和证明模式。", "byte"),
    LegacyFigure("architecture/chapter-map.svg", "Architecture Chapter Map", "架构章节地图", "How the architecture chapters relate to each other for learning and review.", "架构章节如何相互关联，服务学习和审计。", "docs"),
    LegacyFigure("architecture/contract-first-roadmap.svg", "Contract-First Roadmap", "合约优先路线图", "Why NeoHub stays deployable-contract first while L2 evolves through profiles and plugins.", "为什么 NeoHub 保持合约优先，而 L2 通过 profile 和插件演进。", "roadmap"),
    LegacyFigure("architecture/creation-lifecycle.svg", "L2 Creation Lifecycle", "L2 创建生命周期", "Operator workflow from scaffolded config to registered, running, and settled L2.", "从脚手架配置到注册、运行和结算 L2 的运维工作流。", "operations"),
    LegacyFigure("architecture/cross-l2-messaging-sequence.svg", "Cross-L2 Messaging Sequence", "跨 L2 消息序列", "How source L2 messages become globally verifiable and target-consumable.", "源 L2 消息如何变成全局可验证、目标可消费对象。", "bridge"),
    LegacyFigure("architecture/cross-tier-verification.svg", "Cross-Tier Verification Chain", "跨层验证链", "How L2 execution, NeoFS DA, proofs, and L1 settlement verify the same statement.", "L2 执行、NeoFS DA、证明和 L1 结算如何验证同一个语句。", "proof"),
    LegacyFigure("architecture/deployment-flow.svg", "Deployment Flow", "部署流程", "Production deployment gates from source freeze to L1/L2 activation evidence.", "从源码冻结到 L1/L2 激活证据的生产部署闸门。", "operations"),
    LegacyFigure("architecture/dividing-principle.svg", "L1/L2 Dividing Principle", "L1/L2 划分原则", "Decision model for placing responsibility on L1, L2, gateway, or off-chain services.", "把职责放到 L1、L2、Gateway 或链下服务的决策模型。", "architecture"),
    LegacyFigure("architecture/external-bridge-architecture.svg", "External Bridge Architecture", "外链桥架构", "How heterogeneous chains enter the same NeoHub message and asset model.", "异构链如何进入同一 NeoHub 消息和资产模型。", "bridge"),
    LegacyFigure("architecture/forced-inclusion-step1.svg", "Forced Inclusion Step 1", "强制纳入第一步", "The first anti-censorship handoff from L1 queue to L2 obligation.", "从 L1 队列到 L2 义务的第一段抗审查交接。", "security"),
    LegacyFigure("architecture/l1-concerns.svg", "NeoHub L1 Concerns", "NeoHub L1 关注点", "Settlement, bridge, messaging, security, governance, and external bridge responsibilities.", "结算、桥、消息、安全、治理和外链桥职责。", "architecture"),
    LegacyFigure("architecture/l1-l2-bridge.svg", "L1/L2 Bridge Model", "L1/L2 桥模型", "Asset, message, and settlement paths crossing the L1/L2 boundary.", "穿过 L1/L2 边界的资产、消息和结算路径。", "bridge"),
    LegacyFigure("architecture/l1-l2-decision-tree.svg", "L1/L2 Placement Decision Tree", "L1/L2 放置决策树", "Rules for choosing L1, L2, gateway, or off-chain placement.", "选择 L1、L2、Gateway 或链下位置的规则。", "state"),
    LegacyFigure("architecture/l2-anatomy.svg", "L2 Chain Anatomy", "L2 链剖面", "The four artifacts that define an N4 L2: config, executor, registry entry, and operators.", "定义 N4 L2 的四个产物：配置、执行器、注册表条目和运维者。", "architecture"),
    LegacyFigure("architecture/l2-components.svg", "L2 Component Architecture", "L2 组件架构", "Execution kernel, plugins, native contracts, DA writer, prover, and RPC surfaces.", "执行内核、插件、原生合约、DA writer、prover 和 RPC 表面。", "architecture"),
    LegacyFigure("architecture/l2-concerns.svg", "L2 Concerns", "L2 关注点", "Per-chain responsibilities for execution, state, fees, bridge, messaging, and operation.", "每条链在执行、状态、费用、桥、消息和运维上的职责。", "architecture"),
    LegacyFigure("architecture/neohub-anatomy.svg", "NeoHub Anatomy", "NeoHub 剖面", "NeoHub contract groups and the trust boundaries between them.", "NeoHub 合约组以及它们之间的信任边界。", "architecture"),
    LegacyFigure("architecture/new-l2-scaffold-tree.svg", "New L2 Scaffold Tree", "新 L2 脚手架树", "Files and artifacts produced when creating a new N4 L2 chain.", "创建一条新 N4 L2 链时产生的文件和产物。", "docs"),
    LegacyFigure("architecture/reading-paths.svg", "Architecture Reading Paths", "架构阅读路径", "Recommended reading orders for users, operators, auditors, and contributors.", "面向用户、运维、审计者和贡献者的推荐阅读顺序。", "docs"),
    LegacyFigure("architecture/runtime-channels.svg", "Runtime Channels", "运行时通道", "Control, data, proof, DA, and telemetry channels during L2 operation.", "L2 运行中的控制、数据、证明、DA 和遥测通道。", "operations"),
    LegacyFigure("architecture/settlement-sequence.svg", "Settlement Sequence", "结算序列", "The hot path from L2 batch sealing to NeoHub state-root acceptance.", "从 L2 批次封装到 NeoHub 接受状态根的热路径。", "proof"),
    LegacyFigure("architecture/system-tiers.svg", "System Tiers", "系统分层", "Four-tier system view: NeoHub, Gateway, elastic L2s, and operators.", "四层系统视图：NeoHub、Gateway、弹性 L2 和运维者。", "architecture"),
    LegacyFigure("architecture/trust-boundaries.svg", "Architecture Trust Boundaries", "架构信任边界", "Where untrusted input becomes validated fact across the N4 stack.", "未信任输入在 N4 栈中在哪里变成已验证事实。", "trust"),
    LegacyFigure("architecture/trust-minimization-gradient.svg", "Trust-Minimization Gradient", "信任最小化梯度", "The path from trusted operation to cryptographic validity and recoverable exits.", "从受信运行到密码学有效性和可恢复退出的路径。", "trust"),
    LegacyFigure("visual-guide/system-context.svg", "System Context", "系统上下文", "Actors, services, contracts, chains, and evidence surfaces in one view.", "参与者、服务、合约、链和证据表面的一图视图。", "architecture"),
    LegacyFigure("visual-guide/module-map.svg", "Protocol Component Map", "协议组件地图", "Conceptual component groups and their protocol responsibilities, not source folders.", "概念组件组及其协议职责，而不是源码文件夹。", "architecture"),
    LegacyFigure("visual-guide/deposit-flow.svg", "Deposit Flow", "充值流程", "How L1 custody becomes L2 credited balance through canonical bridge evidence.", "L1 托管如何通过规范桥证据变成 L2 记账余额。", "bridge"),
    LegacyFigure("visual-guide/withdrawal-flow.svg", "Withdrawal Flow", "提款流程", "How L2 burn/lock evidence becomes L1 release through settlement finality.", "L2 burn/lock 证据如何通过结算终局变成 L1 释放。", "bridge"),
    LegacyFigure("visual-guide/batch-lifecycle.svg", "Batch Lifecycle", "批次生命周期", "How L2 blocks become batch commitments, DA records, proofs, and accepted roots.", "L2 区块如何变成批次承诺、DA 记录、证明和被接受的根。", "proof"),
    LegacyFigure("visual-guide/external-bridge-flow.svg", "External Bridge Data Flow", "外链桥数据流", "How heterogeneous chain events become NeoHub-verifiable bridge messages.", "异构链事件如何变成 NeoHub 可验证桥消息。", "bridge"),
    LegacyFigure("visual-guide/deployment-pipeline.svg", "Production Deployment Pipeline", "生产部署管线", "Release gates, generated artifacts, deploy transactions, rehearsals, and rollback evidence.", "发布闸门、生成产物、部署交易、演练和回滚证据。", "operations"),
    LegacyFigure("visual-guide/data-structures.svg", "Core Data Structures", "核心数据结构", "The canonical objects whose hashes cross N4 trust boundaries.", "哈希会跨越 N4 信任边界的规范对象。", "byte"),
    LegacyFigure("visual-guide/trust-boundaries-map.svg", "Trust Boundary Map", "信任边界地图", "Input, operator, proof, DA, governance, and user-exit boundaries.", "输入、运维者、证明、DA、治理和用户退出边界。", "trust"),
    LegacyFigure("visual-guide/watcher-state-machine.svg", "Watcher State Machine", "Watcher 状态机", "External-chain watcher states from cursor polling to relay submission and quarantine.", "外链 watcher 从游标轮询到 relay 提交和隔离的状态。", "state"),
    LegacyFigure("visual-guide/testing-matrix.svg", "Verification Matrix", "验证矩阵", "Which tests prove which production claims across contracts, nodes, SDKs, docs, and supply chain.", "哪些测试证明合约、节点、SDK、文档和供应链的哪些生产声明。", "docs"),
    LegacyFigure("visual-guide/operator-runbook.svg", "Operator Runbook Flow", "运维 Runbook 流程", "Operational decision flow for healthy operation, degraded mode, recovery, and escalation.", "健康运行、降级、恢复和升级处理的运维决策流。", "operations"),
    LegacyFigure("visual-guide/project-artifact-map.svg", "Artifact Provenance Map", "产物来源地图", "How specifications, generated artifacts, binaries, reports, and deployment evidence relate.", "规格、生成产物、二进制、报告和部署证据如何关联。", "docs"),
)


FAMILY_DETAILS: dict[str, dict[str, tuple[tuple[str, str], ...]]] = {
    "architecture": {
        "topology_en": (("Canonical layer", "NeoHub owns shared truth, registries, custody, verification policy, and governance."), ("Execution layer", "N4 L2 chains execute transactions with NeoVM2/RISC-V as the canonical default profile."), ("Evidence layer", "Gateway, provers, and NeoFS bind execution results to compact, retrievable evidence."), ("Consumer layer", "Wallets, SDKs, relayers, auditors, and operators consume accepted roots and events.")),
        "topology_zh": (("规范层", "NeoHub 负责共享事实、注册表、托管、验证策略和治理。"), ("执行层", "N4 L2 链执行交易，NeoVM2/RISC-V 是默认 canonical profile。"), ("证据层", "Gateway、prover 和 NeoFS 把执行结果绑定到紧凑且可取回的证据。"), ("消费层", "钱包、SDK、relayer、审计者和运维者消费已接受根和事件。")),
        "model_en": (("Separation", "authority, execution, evidence, and data availability remain separate planes"), ("Commitment", "cross-plane communication uses roots, hashes, receipts, reports, and registry entries"), ("Extensibility", "additional VMs are N4 execution profiles, not NeoX or a separate product line")),
        "model_zh": (("分离", "权限、执行、证据和数据可用性保持独立平面"), ("承诺", "跨平面通信使用根、哈希、回执、报告和注册表条目"), ("可扩展", "额外 VM 是 N4 execution profile，不是 NeoX 或另一条产品线")),
        "checks_en": (("Boundary", "each layer owns one explicit responsibility"), ("Finality", "L1 acceptance is the shared source of truth"), ("DA", "accepted roots reference retrievable data"), ("Compatibility", "docs, configs, and diagrams describe the same architecture")),
        "checks_zh": (("边界", "每层只负责一个明确职责"), ("终局", "L1 接受是共享事实源"), ("DA", "被接受根引用可取回数据"), ("一致性", "文档、配置和图描述同一架构")),
    },
    "bridge": {
        "topology_en": (("Source domain", "L1, L2, or heterogeneous-chain event creates a bridge intent."), ("Normalization", "watchers or contracts encode the event into canonical message bytes."), ("Verification", "NeoHub checks registry, nonce, proof, and asset policy before consumption."), ("Target domain", "target chain credits, releases, mints, burns, or routes the message exactly once.")),
        "topology_zh": (("源域", "L1、L2 或异构链事件产生桥意图。"), ("规范化", "watcher 或合约把事件编码成规范消息字节。"), ("验证", "NeoHub 在消费前检查注册表、nonce、证明和资产策略。"), ("目标域", "目标链精确一次地记账、释放、铸造、销毁或路由消息。")),
        "model_en": (("Message identity", "source chain, target chain, nonce, asset id, amount, and payload hash define uniqueness"), ("Replay rule", "consumed nonce and message hash cannot be reused"), ("Asset rule", "decimal scaling and custody state must be lossless and auditable")),
        "model_zh": (("消息身份", "源链、目标链、nonce、资产 id、数量和 payload 哈希定义唯一性"), ("防重放", "已消费 nonce 和消息哈希不能重复使用"), ("资产规则", "decimal 缩放和托管状态必须无损且可审计")),
        "checks_en": (("Confirmations", "source finality threshold is satisfied"), ("Nonce", "message is not replayed"), ("Asset mapping", "token id and decimals match registry policy"), ("Evidence", "proof or committee evidence binds the event")),
        "checks_zh": (("确认数", "源链终局阈值已满足"), ("Nonce", "消息没有重放"), ("资产映射", "token id 和 decimals 匹配注册策略"), ("证据", "证明或委员会证据绑定事件")),
    },
    "proof": {
        "topology_en": (("L2 execution", "ordered transactions produce pre/post roots, receipts, and public inputs."), ("DA publication", "batch payload and witness material are pinned to NeoFS and referenced by CID."), ("Proof system", "prover produces configured validity or evidence envelope."), ("L1 verification", "NeoHub routes verification and accepts only matching roots.")),
        "topology_zh": (("L2 执行", "有序交易产生前后状态根、回执和公开输入。"), ("DA 发布", "批次 payload 和见证材料写入 NeoFS，并由 CID 引用。"), ("证明系统", "prover 生成配置的有效性证明或证据封装。"), ("L1 验证", "NeoHub 路由验证，只接受匹配的根。")),
        "model_en": (("Statement", "batch range, pre-root, post-root, DA root, proof mode, and verifier key are bound"), ("Acceptance", "root advances only if proof verifies and public inputs match"), ("Failure", "bad proof, missing DA, or mismatched root leaves canonical state unchanged")),
        "model_zh": (("语句", "批次范围、前根、后根、DA 根、证明模式和验证密钥被绑定"), ("接受", "只有证明通过且公开输入匹配时根才推进"), ("失败", "坏证明、缺失 DA 或根不匹配不会改变规范状态")),
        "checks_en": (("Public inputs", "hashes and roots match committed data"), ("Verifier", "registry points to the expected verifier"), ("DA", "payload is retrievable"), ("Result", "accepted root is observable and auditable")),
        "checks_zh": (("公开输入", "哈希和根匹配已提交数据"), ("验证器", "注册表指向预期 verifier"), ("DA", "payload 可取回"), ("结果", "被接受根可观测且可审计")),
    },
    "security": {
        "topology_en": (("User protection", "users can force inclusion, withdraw, or rely on challenge paths."), ("Operator duty", "sequencers, batchers, and provers must satisfy deadlines and evidence gates."), ("Detection", "watchers and monitors detect censorship, timeout, mismatch, and missing data."), ("Enforcement", "NeoHub governance, bonds, pause, and challenge logic enforce recovery.")),
        "topology_zh": (("用户保护", "用户可以强制纳入、提款或依赖挑战路径。"), ("运维义务", "sequencer、batcher 和 prover 必须满足 deadline 和证据闸门。"), ("检测", "watcher 和监控发现审查、超时、不匹配和缺失数据。"), ("执行", "NeoHub 治理、保证金、暂停和挑战逻辑执行恢复。")),
        "model_en": (("Safety", "invalid or censored state cannot silently become final"), ("Liveness", "forced paths give users a way around stalled operators"), ("Accountability", "evidence maps failures to responsible actors")),
        "model_zh": (("安全性", "无效或被审查状态不能静默终局"), ("活性", "强制路径让用户绕过停滞运维者"), ("问责", "证据把故障映射到责任参与方")),
        "checks_en": (("Deadline", "timeout is measured against canonical height/time"), ("Evidence", "report includes queue item, root, message, or proof"), ("Action", "slash, pause, challenge, or force path is explicit"), ("Exit", "users retain a documented recovery route")),
        "checks_zh": (("Deadline", "超时按规范高度/时间衡量"), ("证据", "报告包含队列项、根、消息或证明"), ("动作", "罚没、暂停、挑战或强制路径明确"), ("退出", "用户保留有文档的恢复路径")),
    },
    "operations": {
        "topology_en": (("Configuration", "branches, contracts, verifier policy, DA, endpoints, and token mappings are fixed."), ("Runtime", "sequencer, batcher, prover, DA writer, relayer, and monitors run continuously."), ("Evidence", "artifacts, tx hashes, reports, metrics, and logs prove each operational phase."), ("Recovery", "rollback, pause, replay, challenge, and rotation procedures handle incidents.")),
        "topology_zh": (("配置", "分支、合约、验证器策略、DA、端点和资产映射固定。"), ("运行", "sequencer、batcher、prover、DA writer、relayer 和监控持续运行。"), ("证据", "产物、交易哈希、报告、指标和日志证明每个运维阶段。"), ("恢复", "回滚、暂停、重放、挑战和轮换流程处理事故。")),
        "model_en": (("Control loop", "configure -> run -> observe -> verify -> settle -> recover"), ("Gate", "a phase is complete only when evidence exists"), ("Scope", "automation retries routine failures; governance handles destructive actions")),
        "model_zh": (("控制循环", "配置 -> 运行 -> 观测 -> 验证 -> 结算 -> 恢复"), ("闸门", "只有存在证据时阶段才完成"), ("范围", "自动化重试常规故障；治理处理破坏性动作")),
        "checks_en": (("Provenance", "artifact and config digests are stored"), ("Secrets", "keys stay outside static docs and browsers"), ("Metrics", "queue, failure, and latency signals exist"), ("Rollback", "pause and recovery path is rehearsed")),
        "checks_zh": (("来源", "产物和配置摘要已保存"), ("密钥", "密钥不进入静态文档和浏览器"), ("指标", "队列、失败和延迟信号存在"), ("回滚", "暂停和恢复路径已演练")),
    },
    "byte": {
        "topology_en": (("Domain object", "logical value with typed fields and explicit versioning."), ("Canonical bytes", "stable ordering, fixed-width numeric encoding, and domain-separated prefixes."), ("Hash boundary", "hashes cross contracts, DA, proofs, relayers, and reports."), ("Verifier", "each consumer recomputes bytes before trusting the object.")),
        "topology_zh": (("领域对象", "带强类型字段和显式版本的逻辑值。"), ("规范字节", "稳定排序、固定宽度数值编码和域分隔前缀。"), ("哈希边界", "哈希跨越合约、DA、证明、relayer 和报告。"), ("验证器", "每个消费者在信任对象前重新计算字节。")),
        "model_en": (("Encoding", "type tag || version || chain ids || fields || nonce || payload hash"), ("Commitment", "hash(canonical bytes) is the only cross-boundary identifier"), ("Rejection", "ambiguous length, wrong version, or mismatched hash is rejected")),
        "model_zh": (("编码", "type tag || version || chain ids || fields || nonce || payload hash"), ("承诺", "hash(规范字节) 是唯一跨边界标识"), ("拒绝", "长度歧义、版本错误或哈希不匹配会被拒绝")),
        "checks_en": (("Version", "format version is explicit"), ("Domain", "type prefix prevents cross-format replay"), ("Length", "variable fields are length-delimited"), ("Hash", "all consumers recompute the same digest")),
        "checks_zh": (("版本", "格式版本显式"), ("域", "类型前缀防止跨格式重放"), ("长度", "可变字段带长度分隔"), ("哈希", "所有消费者重算同一摘要")),
    },
    "state": {
        "topology_en": (("Candidate", "input has been observed but is not yet canonical."), ("Validated", "shape, registry, policy, and evidence checks pass."), ("Committed", "state root, nonce, or registry entry advances."), ("Rejected", "invalid or late data is quarantined without advancing canonical state.")),
        "topology_zh": (("候选", "输入已被观测，但尚非规范事实。"), ("已验证", "结构、注册表、策略和证据检查通过。"), ("已提交", "状态根、nonce 或注册表条目前进。"), ("已拒绝", "无效或迟到数据被隔离，不推进规范状态。")),
        "model_en": (("Transition", "state moves only through named gates"), ("Finality", "committed state is observable through a canonical event or root"), ("Rollback", "rejection retains diagnostics and preserves previous state")),
        "model_zh": (("转换", "状态只能通过命名闸门移动"), ("终局", "已提交状态通过规范事件或根可观测"), ("回滚", "拒绝保留诊断并保持前一状态")),
        "checks_en": (("Initial", "starting state is defined"), ("Gate", "each transition has one acceptance condition"), ("Idempotence", "retries do not duplicate effects"), ("Diagnostics", "rejection reason is stored")),
        "checks_zh": (("初始", "起始状态已定义"), ("闸门", "每个转换只有一个接受条件"), ("幂等", "重试不会重复副作用"), ("诊断", "拒绝原因已保存")),
    },
    "docs": {
        "topology_en": (("Reader role", "user, operator, auditor, SDK developer, or contributor enters with a different question."), ("Concept layer", "architecture, trust, byte format, workflow, and runbook chapters answer protocol questions."), ("Artifact layer", "figures, examples, generated reports, and tests provide evidence."), ("Maintenance layer", "English and Chinese mirrors evolve together.")),
        "topology_zh": (("读者角色", "用户、运维、审计者、SDK 开发者或贡献者带着不同问题进入。"), ("概念层", "架构、信任、字节格式、工作流和 runbook 章节回答协议问题。"), ("产物层", "图、样例、生成报告和测试提供证据。"), ("维护层", "英文和中文镜像同步演进。")),
        "model_en": (("Reading path", "start from architecture, then follow data, trust, byte, and workflow chapters"), ("Traceability", "each diagram points to a protocol concept and evidence surface"), ("Consistency", "generated figures keep English and Chinese versions aligned")),
        "model_zh": (("阅读路径", "从架构开始，再进入数据、信任、字节和工作流章节"), ("可追踪", "每张图指向一个协议概念和证据表面"), ("一致性", "生成图保持英文和中文版本对齐")),
        "checks_en": (("Audience", "figure answers a reader question"), ("Mirror", "Chinese equivalent exists"), ("Evidence", "links to tests or artifacts are available"), ("Freshness", "diagram generator is committed")),
        "checks_zh": (("读者", "图回答明确读者问题"), ("镜像", "存在中文对应版本"), ("证据", "可链接测试或产物"), ("新鲜度", "图生成器已提交")),
    },
    "roadmap": {
        "topology_en": (("L1 contract-first", "NeoHub remains deployable and minimizes core changes."), ("L2 execution", "N4 L2 uses NeoVM2/RISC-V by default and evolves through profiles."), ("Data availability", "NeoFS is the DA layer for payloads, witnesses, and reports."), ("Interoperability", "shared bridge and heterogeneous watchers grow through adapters.")),
        "topology_zh": (("L1 合约优先", "NeoHub 保持可部署，并最小化 core 修改。"), ("L2 执行", "N4 L2 默认使用 NeoVM2/RISC-V，并通过 profile 演进。"), ("数据可用性", "NeoFS 是 payload、见证和报告的 DA 层。"), ("互操作", "共享桥和异构 watcher 通过适配器扩展。")),
        "model_en": (("Principle", "add capabilities at the narrowest layer that owns the responsibility"), ("Compatibility", "new execution profiles must preserve settlement and bridge semantics"), ("Evidence", "roadmap items graduate only with tests, docs, and deployment rehearsal")),
        "model_zh": (("原则", "在拥有该职责的最窄层添加能力"), ("兼容", "新 execution profile 必须保留结算和桥语义"), ("证据", "路线图事项只有带测试、文档和部署演练才毕业")),
        "checks_en": (("L1 scope", "no unnecessary native-contract or core dependency"), ("VM scope", "profile model stays N4-specific"), ("Bridge scope", "heterogeneous chains use normalized messages"), ("Release", "evidence exists before production claim")),
        "checks_zh": (("L1 范围", "没有不必要的 native-contract 或 core 依赖"), ("VM 范围", "profile 模型保持 N4 特定"), ("桥范围", "异构链使用规范消息"), ("发布", "生产声明前存在证据")),
    },
    "trust": {
        "topology_en": (("Untrusted input", "users, RPC endpoints, watchers, operators, and provers can be wrong or malicious."), ("Validation boundary", "canonical encodings, registries, DA references, and verifier policy check claims."), ("Accepted fact", "only verified roots, messages, and registry entries become shared truth."), ("Recovery", "challenge, forced inclusion, pause, and exit paths handle failures.")),
        "topology_zh": (("未信任输入", "用户、RPC 端点、watcher、运维者和 prover 都可能错误或恶意。"), ("验证边界", "规范编码、注册表、DA 引用和 verifier 策略检查声明。"), ("已接受事实", "只有已验证根、消息和注册表条目成为共享事实。"), ("恢复", "挑战、强制纳入、暂停和退出路径处理故障。")),
        "model_en": (("Minimization", "move from operator trust to verifiable evidence"), ("Compartmentalization", "failure in one chain or watcher should not corrupt global state"), ("Audit", "every accepted claim needs replayable inputs or retrievable evidence")),
        "model_zh": (("最小化", "从运维者信任移动到可验证证据"), ("隔离", "单链或 watcher 故障不应污染全局状态"), ("审计", "每个已接受声明都需要可重放输入或可取回证据")),
        "checks_en": (("Assumption", "explicitly name what is trusted"), ("Verifier", "state why the consumer can check it"), ("Blast radius", "failure stays bounded"), ("Recovery", "user or operator has a documented action")),
        "checks_zh": (("假设", "明确命名信任什么"), ("验证", "说明消费者为什么能检查"), ("影响范围", "故障保持有界"), ("恢复", "用户或运维者有文档动作")),
    },
    "execution": {
        "topology_en": (("Intent", "signed transaction or system message enters the L2 execution boundary."), ("Execution", "NeoVM2/RISC-V profile applies deterministic state transition rules."), ("Receipt", "execution output becomes receipts, logs, roots, and gas/accounting evidence."), ("Settlement input", "batcher folds receipts into commitments consumed by DA and proof systems.")),
        "topology_zh": (("意图", "已签名交易或系统消息进入 L2 执行边界。"), ("执行", "NeoVM2/RISC-V profile 应用确定性状态转换规则。"), ("回执", "执行输出变成回执、日志、根和 gas/计费证据。"), ("结算输入", "batcher 把回执折叠为 DA 和证明系统消费的承诺。")),
        "model_en": (("Determinism", "same ordered inputs and state produce the same output root"), ("Isolation", "VM effects are authorized by host and native/system contracts"), ("Accounting", "gas, failure, and side effects are recorded before settlement")),
        "model_zh": (("确定性", "同一有序输入和状态产生同一输出根"), ("隔离", "VM 副作用由 host 和原生/系统合约授权"), ("计费", "gas、失败和副作用在结算前记录")),
        "checks_en": (("Ordering", "transaction order is explicit"), ("VM", "execution profile is declared"), ("Effects", "receipts match state change"), ("Failure", "faults do not commit unauthorized effects")),
        "checks_zh": (("排序", "交易顺序明确"), ("VM", "execution profile 已声明"), ("副作用", "回执匹配状态变化"), ("失败", "fault 不提交未授权副作用")),
    },
}


PATH_STEPS: dict[str, tuple[tuple[str, str], ...]] = {
    "architecture": (("Register", "chain config and policy enter NeoHub"), ("Execute", "L2 produces ordered state transitions"), ("Publish", "NeoFS stores data and witnesses"), ("Prove", "configured proof path creates evidence"), ("Settle", "NeoHub accepts verified root"), ("Consume", "users, relayers, and apps consume final facts")),
    "bridge": (("Observe", "source event or tx is observed"), ("Normalize", "canonical message bytes are built"), ("Verify", "registry, nonce, and evidence pass"), ("Commit", "message or asset state advances"), ("Relay", "target receives inclusion evidence"), ("Consume", "target action executes exactly once")),
    "proof": (("Seal", "batch commitment is computed"), ("Publish DA", "payload and witness are pinned"), ("Prove", "proof envelope is generated"), ("Verify", "local and L1 checks run"), ("Accept", "state root advances"), ("Report", "evidence and tx hash are recorded")),
    "security": (("Request", "user or monitor submits protected action"), ("Queue", "L1 records obligation or report"), ("Deadline", "operator must satisfy inclusion/proof window"), ("Detect", "censorship or timeout is observed"), ("Enforce", "challenge, slash, pause, or force path runs"), ("Recover", "user or chain returns to safe path")),
    "operations": (("Configure", "network, contracts, DA, verifier, and tokens are fixed"), ("Deploy", "artifacts and on-chain txs are produced"), ("Run", "operators generate batches, DA, proofs, and relays"), ("Observe", "metrics, logs, and reports are emitted"), ("Decide", "runbook chooses retry, pause, rollback, or escalate"), ("Record", "evidence is archived")),
    "byte": (("Define", "fields, versions, and domain tags are fixed"), ("Encode", "canonical bytes are serialized"), ("Hash", "digest crosses trust boundaries"), ("Store", "payload or witness is retrievable"), ("Verify", "consumer recomputes bytes and digest"), ("Reject", "mismatch or ambiguity fails closed")),
    "state": (("Candidate", "event or config is observed"), ("Validate", "shape, policy, and evidence checks pass"), ("Admit", "state transition becomes eligible"), ("Commit", "root, nonce, or registry entry advances"), ("Emit", "canonical event records the transition"), ("Quarantine", "invalid data is isolated")),
    "docs": (("Question", "reader chooses a role and goal"), ("Map", "figure points to the relevant concept"), ("Read", "chapter explains the protocol rule"), ("Trace", "evidence links to artifacts and tests"), ("Mirror", "Chinese and English stay aligned"), ("Regenerate", "script updates figures consistently")),
    "roadmap": (("Principle", "choose the narrowest responsible layer"), ("Contract", "keep L1 support deployable-first"), ("Profile", "extend L2 through N4 execution profiles"), ("DA", "bind data through NeoFS"), ("Bridge", "normalize heterogeneous messages"), ("Graduate", "require tests, docs, and rehearsal")),
    "trust": (("Claim", "untrusted actor submits a claim"), ("Check", "consumer validates encoding, policy, and evidence"), ("Bound", "blast radius is limited by chain/route/registry"), ("Accept", "claim becomes shared fact only after verification"), ("Reject", "bad claim cannot advance canonical state"), ("Recover", "challenge, force, pause, or exit path remains available")),
    "execution": (("Submit", "signed tx enters L2"), ("Order", "sequencer fixes transaction position"), ("Execute", "VM profile applies deterministic rules"), ("Receipt", "logs, gas, and effects are recorded"), ("Batch", "batcher folds outputs into roots"), ("Settle", "proof path makes result final")),
}

PATH_STEPS_ZH: dict[str, tuple[tuple[str, str], ...]] = {
    "architecture": (("注册", "链配置和策略进入 NeoHub"), ("执行", "L2 产生有序状态转换"), ("发布", "NeoFS 存储数据和见证"), ("证明", "配置的证明路径生成证据"), ("结算", "NeoHub 接受已验证根"), ("消费", "用户、relayer 和应用消费终局事实")),
    "bridge": (("观测", "源事件或交易被观测"), ("规范化", "构造规范消息字节"), ("验证", "注册表、nonce 和证据通过"), ("提交", "消息或资产状态推进"), ("中继", "目标收到 inclusion 证据"), ("消费", "目标动作精确执行一次")),
    "proof": (("封装", "计算批次承诺"), ("发布 DA", "payload 和见证写入 DA"), ("证明", "生成 proof envelope"), ("验证", "运行本地和 L1 检查"), ("接受", "状态根推进"), ("报告", "记录证据和交易哈希")),
    "security": (("请求", "用户或监控提交受保护动作"), ("排队", "L1 记录义务或报告"), ("期限", "运维者必须满足纳入/证明窗口"), ("检测", "观测到审查或超时"), ("执行", "挑战、罚没、暂停或强制路径运行"), ("恢复", "用户或链回到安全路径")),
    "operations": (("配置", "网络、合约、DA、验证器和资产固定"), ("部署", "产生产物和链上交易"), ("运行", "运维者生成批次、DA、证明和中继"), ("观测", "输出指标、日志和报告"), ("决策", "runbook 选择重试、暂停、回滚或升级"), ("记录", "归档证据")),
    "byte": (("定义", "字段、版本和域标签固定"), ("编码", "序列化规范字节"), ("哈希", "摘要跨越信任边界"), ("存储", "payload 或见证可取回"), ("验证", "消费者重算字节和摘要"), ("拒绝", "不匹配或歧义默认失败")),
    "state": (("候选", "事件或配置被观测"), ("验证", "结构、策略和证据检查通过"), ("准入", "状态转换具备资格"), ("提交", "根、nonce 或注册表条目前进"), ("事件", "规范事件记录转换"), ("隔离", "无效数据被隔离")),
    "docs": (("问题", "读者选择角色和目标"), ("地图", "图指向相关概念"), ("阅读", "章节解释协议规则"), ("追踪", "证据链接产物和测试"), ("镜像", "中英文保持对齐"), ("生成", "脚本一致更新图片")),
    "roadmap": (("原则", "选择拥有职责的最窄层"), ("合约", "L1 支持保持合约优先"), ("Profile", "通过 N4 execution profile 扩展 L2"), ("DA", "通过 NeoFS 绑定数据"), ("桥", "规范化异构消息"), ("毕业", "需要测试、文档和演练")),
    "trust": (("声明", "未信任参与方提交声明"), ("检查", "消费者验证编码、策略和证据"), ("限制", "影响范围按链/路由/注册表限制"), ("接受", "只有验证后才成为共享事实"), ("拒绝", "坏声明不能推进规范状态"), ("恢复", "挑战、强制、暂停或退出路径可用")),
    "execution": (("提交", "已签名交易进入 L2"), ("排序", "sequencer 固定交易位置"), ("执行", "VM profile 应用确定性规则"), ("回执", "记录日志、gas 和副作用"), ("批次", "batcher 把输出折叠成根"), ("结算", "证明路径让结果终局")),
}


def main() -> None:
    for fig in LEGACY_FIGURES:
        render_to(EN_ROOT / fig.rel, fig, "en")
        render_to(ZH_ROOT / fig.rel, fig, "zh")
    print(f"Generated {len(LEGACY_FIGURES) * 2} legacy paper-style figures.")


def render_to(path: Path, figure: LegacyFigure, lang: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(render(figure, lang), encoding="utf-8")


def render(figure: LegacyFigure, lang: str) -> str:
    title = figure.title_zh if lang == "zh" else figure.title_en
    focus = figure.focus_zh if lang == "zh" else figure.focus_en
    details = FAMILY_DETAILS[figure.family]
    topology = details["topology_zh" if lang == "zh" else "topology_en"]
    model = details["model_zh" if lang == "zh" else "model_en"]
    checks = details["checks_zh" if lang == "zh" else "checks_en"]
    steps = PATH_STEPS_ZH[figure.family] if lang == "zh" else PATH_STEPS[figure.family]
    terms = localized_terms(lang)
    accent = family_accent(figure.family)
    tint = family_tint(figure.family)
    width, height = 1920, 1180
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{esc(title)}">',
        "<defs>",
        f'<marker id="arrowAccent" viewBox="0 0 10 10" refX="9.2" refY="5" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 Z" fill="{accent}"/></marker>',
        '<marker id="arrowGray" viewBox="0 0 10 10" refX="9.2" refY="5" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 Z" fill="#475569"/></marker>',
        '<marker id="arrowFault" viewBox="0 0 10 10" refX="9.2" refY="5" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 Z" fill="#dc2626"/></marker>',
        '<filter id="shadow" x="-3%" y="-4%" width="106%" height="108%"><feDropShadow dx="0" dy="8" stdDeviation="10" flood-color="#0f172a" flood-opacity="0.10"/></filter>',
        "<style>",
        ".bg{fill:#f8fafc}.panel{fill:#ffffff;stroke:#cbd5e1;stroke-width:1.5;filter:url(#shadow)}",
        ".title{font:800 36px 'Inter','Segoe UI',Arial,sans-serif;fill:#0f172a}.subtitle{font:500 17px 'Inter','Segoe UI',Arial,sans-serif;fill:#475569}",
        ".panelTitle{font:750 22px 'Inter','Segoe UI',Arial,sans-serif;fill:#0f172a}.panelSub{font:500 15px 'Inter','Segoe UI',Arial,sans-serif;fill:#475569}",
        ".strong{font:760 15px 'Inter','Segoe UI',Arial,sans-serif;fill:#0f172a}.body{font:500 14px 'Inter','Segoe UI',Arial,sans-serif;fill:#334155}.small{font:500 13px 'Inter','Segoe UI',Arial,sans-serif;fill:#475569}",
        ".mono{font:650 14px 'Cascadia Mono','SFMono-Regular',Consolas,monospace;fill:#0f172a}.label{font:800 14px 'Inter','Segoe UI',Arial,sans-serif;fill:#ffffff}.caption{font:500 13px 'Inter','Segoe UI',Arial,sans-serif;fill:#64748b}",
        f'.accentArrow{{stroke:{accent};stroke-width:2.6;fill:none;marker-end:url(#arrowAccent)}}.dashArrow{{stroke:#64748b;stroke-width:2.0;fill:none;stroke-dasharray:7 6;marker-end:url(#arrowGray)}}.faultArrow{{stroke:#dc2626;stroke-width:2.2;fill:none;stroke-dasharray:5 5;marker-end:url(#arrowFault)}}',
        "</style>",
        "</defs>",
        f'<rect class="bg" width="{width}" height="{height}"/>',
        f'<text x="56" y="66" class="title">{esc(title)}</text>',
        f'<text x="56" y="98" class="subtitle">{esc(focus)}</text>',
        f'<rect x="56" y="118" width="1808" height="38" rx="6" fill="{tint}" stroke="{accent}" stroke-width="1.2"/>',
        f'<text x="76" y="143" class="small">{esc(terms["read"])} <tspan class="strong">{esc(terms["paper"])}</tspan></text>',
    ]
    parts.extend(panel(56, 178, 870, 392, "a", terms["topology"], terms["topology_sub"], accent, tint))
    parts.extend(panel(966, 178, 898, 392, "b", terms["model"], terms["model_sub"], accent, tint))
    parts.extend(panel(56, 606, 1240, 494, "c", terms["path"], terms["path_sub"], accent, tint))
    parts.extend(panel(1334, 606, 530, 494, "d", terms["checks"], terms["checks_sub"], accent, tint))
    parts.extend(draw_topology(92, 270, topology, accent, tint))
    parts.extend(draw_model(1000, 252, model, figure.family, accent, tint, lang))
    parts.extend(draw_path(92, 730, steps, accent, tint, terms))
    parts.extend(draw_checks(1368, 710, checks, accent, tint, terms))
    footer = f'{terms["footer"]} · {figure.rel}'
    parts.append(f'<text x="56" y="1146" class="caption">{esc(footer)}</text>')
    parts.append("</svg>")
    return "\n".join(parts) + "\n"


def localized_terms(lang: str) -> dict[str, str]:
    if lang == "zh":
        return {
            "read": "读图方式：",
            "paper": "论文式技术图；强调架构、原理、数据流、工作流和审计条件。",
            "topology": "技术拓扑",
            "topology_sub": "参与平面、边界和职责",
            "model": "技术模型",
            "model_sub": "形式化规则和接受条件",
            "path": "编号协议路径",
            "path_sub": "关键对象如何推进",
            "checks": "审计检查点",
            "checks_sub": "读者应验证的条件",
            "solid": "实线：状态 / 数据",
            "dashed": "虚线：证明 / 观测",
            "fault": "红线：拒绝 / 恢复",
            "acceptance": "接受规则",
            "footer": "由 legacy paper figure generator 生成；保留原文件名，避免文档链接失效",
        }
    return {
        "read": "Read as:",
        "paper": "paper-style technical figure focused on architecture, principles, data flow, workflow, and audit conditions.",
        "topology": "Technical Topology",
        "topology_sub": "planes, boundaries, and responsibilities",
        "model": "Technical Model",
        "model_sub": "formal rules and acceptance conditions",
        "path": "Numbered Protocol Path",
        "path_sub": "how key objects advance",
        "checks": "Audit Checkpoints",
        "checks_sub": "conditions readers should verify",
        "solid": "solid: state / data",
        "dashed": "dashed: proof / observation",
        "fault": "red: reject / recover",
        "acceptance": "Acceptance rule",
        "footer": "Generated by the legacy paper figure generator; original filename preserved so documentation links stay stable",
    }


def family_accent(family: str) -> str:
    return {
        "architecture": "#0369a1",
        "bridge": "#0f766e",
        "proof": "#c2410c",
        "security": "#be123c",
        "operations": "#7e22ce",
        "byte": "#4f46e5",
        "state": "#1d4ed8",
        "docs": "#475569",
        "roadmap": "#b45309",
        "trust": "#be123c",
        "execution": "#047857",
    }[family]


def family_tint(family: str) -> str:
    return {
        "architecture": "#eef7ff",
        "bridge": "#ecfdf5",
        "proof": "#fff3e7",
        "security": "#fff1f2",
        "operations": "#faf5ff",
        "byte": "#eef2ff",
        "state": "#eff6ff",
        "docs": "#f8fafc",
        "roadmap": "#fff7ed",
        "trust": "#fff1f2",
        "execution": "#ecfdf3",
    }[family]


def panel(x: int, y: int, w: int, h: int, letter: str, title: str, subtitle: str, accent: str, tint: str) -> list[str]:
    return [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" class="panel"/>',
        f'<rect x="{x}" y="{y}" width="{w}" height="54" rx="10" fill="{tint}"/>',
        f'<circle cx="{x + 30}" cy="{y + 28}" r="16" fill="{accent}"/>',
        f'<text x="{x + 30}" y="{y + 33}" text-anchor="middle" class="label">{esc(letter)}</text>',
        f'<text x="{x + 58}" y="{y + 32}" class="panelTitle">{esc(title)}</text>',
        f'<text x="{x + 58}" y="{y + 52}" class="panelSub">{esc(subtitle)}</text>',
    ]


def draw_topology(x: int, y: int, rows: tuple[tuple[str, str], ...], accent: str, tint: str) -> list[str]:
    parts: list[str] = []
    box_w = 180
    gap = 28
    for idx, (title, body) in enumerate(rows):
        bx = x + idx * (box_w + gap)
        by = y + (idx % 2) * 54
        parts.extend(card(bx, by, box_w, 208, title, body, tint, accent, wrap=22, lines=6))
        if idx < len(rows) - 1:
            nx = x + (idx + 1) * (box_w + gap)
            ny = y + ((idx + 1) % 2) * 54 + 104
            parts.append(f'<path class="accentArrow" d="M{bx + box_w} {by + 104} C{bx + box_w + 12} {by + 104} {nx - 18} {ny} {nx} {ny}"/>')
    parts.append(f'<line x1="{x + 204}" y1="{y + 258}" x2="{x + 660}" y2="{y + 258}" stroke="{accent}" stroke-width="1.4" stroke-dasharray="6 6"/>')
    parts.append(f'<text x="{x + 430}" y="{y + 282}" class="caption" text-anchor="middle">canonical boundary: hashes, roots, events, proofs, DA references</text>')
    return parts


def draw_model(x: int, y: int, rows: tuple[tuple[str, str], ...], family: str, accent: str, tint: str, lang: str) -> list[str]:
    formula = formula_for(family, lang)
    accept = accept_for(family, lang)
    parts = [
        f'<rect x="{x}" y="{y}" width="828" height="62" rx="8" fill="{tint}" stroke="{accent}" stroke-width="1.5"/>',
        f'<text x="{x + 20}" y="{y + 38}" class="mono">{esc(formula)}</text>',
    ]
    for idx, (title, body) in enumerate(rows):
        by = y + 90 + idx * 72
        parts.extend(numbered_row(x + 12, by, 630, 54, idx + 1, title, body, accent))
        if idx < len(rows) - 1:
            parts.append(f'<path class="accentArrow" d="M{x + 328} {by + 54} L{x + 328} {by + 72}"/>')
    parts.extend(card(x + 672, y + 106, 146, 174, "Acceptance" if lang == "en" else "接受", accept, "#ffffff", accent, wrap=18, lines=7))
    parts.append(f'<path class="dashArrow" d="M{x + 642} {y + 208} L{x + 672} {y + 208}"/>')
    parts.append(f'<text x="{x + 14}" y="{y + 310}" class="mono">{esc(accept)}</text>')
    return parts


def draw_path(x: int, y: int, rows: tuple[tuple[str, str], ...], accent: str, tint: str, terms: dict[str, str]) -> list[str]:
    parts: list[str] = []
    xs = [x, x + 198, x + 396, x + 594, x + 792, x + 990]
    for idx, ((title, body), px) in enumerate(zip(rows, xs), start=1):
        parts.extend(path_node(px, y, 150, 120, idx, title, body, accent, tint))
        if idx < len(rows):
            parts.append(f'<path class="accentArrow" d="M{px + 150} {y + 60} L{xs[idx]} {y + 60}"/>')
    parts.append(f'<path class="dashArrow" d="M{x + 90} {y + 170} C{x + 320} {y + 244} {x + 760} {y + 244} {x + 1040} {y + 170}"/>')
    parts.extend(card(x + 390, y + 184, 210, 120, "Reject / Recover", "bad evidence, replay, missing data, timeout, policy mismatch", "#fff1f2", "#dc2626", wrap=28, lines=4))
    parts.append(f'<path class="faultArrow" d="M{x + 470} {y + 120} L{x + 470} {y + 184}"/>')
    parts.append(f'<text x="{x}" y="{y + 340}" class="caption">{esc(terms["solid"])} · {esc(terms["dashed"])} · {esc(terms["fault"])}</text>')
    return parts


def draw_checks(x: int, y: int, rows: tuple[tuple[str, str], ...], accent: str, tint: str, terms: dict[str, str]) -> list[str]:
    parts = [f'<rect x="{x}" y="{y}" width="462" height="294" rx="8" fill="#ffffff" stroke="#cbd5e1" stroke-width="1.2"/>']
    for idx, (title, body) in enumerate(rows):
        ry = y + 10 + idx * 68
        fill = tint if idx % 2 == 0 else "#ffffff"
        parts.append(f'<rect x="{x + 10}" y="{ry}" width="442" height="58" rx="6" fill="{fill}"/>')
        parts.append(f'<text x="{x + 22}" y="{ry + 23}" class="strong">{esc(title)}</text>')
        for line_idx, line in enumerate(wrap_text(body, 39)[:2]):
            parts.append(f'<text x="{x + 150}" y="{ry + 20 + line_idx * 17}" class="small">{esc(line)}</text>')
    parts.append(f'<line x1="{x + 136}" y1="{y + 14}" x2="{x + 136}" y2="{y + 282}" stroke="{accent}" stroke-width="1.2" stroke-dasharray="4 5"/>')
    parts.append(f'<rect x="{x}" y="{y + 316}" width="462" height="74" rx="8" fill="#f8fafc" stroke="#cbd5e1" stroke-width="1.2"/>')
    parts.append(f'<line x1="{x + 20}" y1="{y + 340}" x2="{x + 66}" y2="{y + 340}" stroke="{accent}" stroke-width="2.6" marker-end="url(#arrowAccent)"/>')
    parts.append(f'<text x="{x + 82}" y="{y + 344}" class="small">{esc(terms["solid"])}</text>')
    parts.append(f'<line x1="{x + 20}" y1="{y + 364}" x2="{x + 66}" y2="{y + 364}" stroke="#64748b" stroke-width="2.0" stroke-dasharray="7 6" marker-end="url(#arrowGray)"/>')
    parts.append(f'<text x="{x + 82}" y="{y + 368}" class="small">{esc(terms["dashed"])}</text>')
    parts.append(f'<line x1="{x + 292}" y1="{y + 364}" x2="{x + 336}" y2="{y + 364}" stroke="#dc2626" stroke-width="2.2" stroke-dasharray="5 5" marker-end="url(#arrowFault)"/>')
    parts.append(f'<text x="{x + 350}" y="{y + 368}" class="small">{esc(terms["fault"])}</text>')
    return parts


def card(x: int, y: int, w: int, h: int, title: str, body: str, fill: str, stroke: str, *, wrap: int, lines: int) -> list[str]:
    parts = [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{fill}" stroke="{stroke}" stroke-width="1.4"/>',
        f'<text x="{x + 14}" y="{y + 28}" class="strong">{esc(title)}</text>',
    ]
    for idx, line in enumerate(wrap_text(body, wrap)[:lines]):
        parts.append(f'<text x="{x + 14}" y="{y + 58 + idx * 18}" class="body">{esc(line)}</text>')
    return parts


def numbered_row(x: int, y: int, w: int, h: int, idx: int, title: str, body: str, accent: str) -> list[str]:
    return [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="#ffffff" stroke="#cbd5e1" stroke-width="1.2"/>',
        f'<circle cx="{x + 26}" cy="{y + 27}" r="15" fill="{accent}"/>',
        f'<text x="{x + 26}" y="{y + 32}" text-anchor="middle" class="label">{idx}</text>',
        f'<text x="{x + 54}" y="{y + 23}" class="strong">{esc(title)}</text>',
        f'<text x="{x + 54}" y="{y + 43}" class="small">{esc(wrap_text(body, 66)[0])}</text>',
    ]


def path_node(x: int, y: int, w: int, h: int, idx: int, title: str, body: str, accent: str, tint: str) -> list[str]:
    parts = [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{tint}" stroke="{accent}" stroke-width="1.5"/>',
        f'<rect x="{x + 10}" y="{y + 10}" width="30" height="24" rx="5" fill="{accent}"/>',
        f'<text x="{x + 25}" y="{y + 27}" text-anchor="middle" class="label">{idx}</text>',
        f'<text x="{x + 48}" y="{y + 28}" class="strong">{esc(wrap_text(title, 12)[0])}</text>',
    ]
    for line_idx, line in enumerate(wrap_text(body, 18)[:4]):
        parts.append(f'<text x="{x + 12}" y="{y + 58 + line_idx * 17}" class="small">{esc(line)}</text>')
    return parts


def formula_for(family: str, lang: str) -> str:
    if lang == "zh":
        return {
            "architecture": "accepted_fact = registered && executed && DA_bound && verified",
            "bridge": "message_id = H(source || target || nonce || asset || payload)",
            "proof": "accept_root ⇔ verify(proof, public_inputs) && DA_available",
            "security": "recover ⇔ deadline_expired || invalid_claim || missing_DA",
            "operations": "operate = configure -> run -> observe -> decide -> record",
            "byte": "digest = H(type_tag || version || canonical_fields)",
            "state": "S_next = transition(S, event) 或 S 保持不变",
            "docs": "reader_question -> concept -> figure -> evidence",
            "roadmap": "capability -> narrowest_responsible_layer -> evidence",
            "trust": "trust_minimized ⇔ claim is independently checkable",
            "execution": "S' = execute(profile, S, ordered_tx)",
        }[family]
    return {
        "architecture": "accepted_fact = registered && executed && DA_bound && verified",
        "bridge": "message_id = H(source || target || nonce || asset || payload)",
        "proof": "accept_root iff verify(proof, public_inputs) && DA_available",
        "security": "recover iff deadline_expired || invalid_claim || missing_DA",
        "operations": "operate = configure -> run -> observe -> decide -> record",
        "byte": "digest = H(type_tag || version || canonical_fields)",
        "state": "S_next = transition(S, event) or S stays unchanged",
        "docs": "reader_question -> concept -> figure -> evidence",
        "roadmap": "capability -> narrowest_responsible_layer -> evidence",
        "trust": "trust_minimized iff claim is independently checkable",
        "execution": "S' = execute(profile, S, ordered_tx)",
    }[family]


def accept_for(family: str, lang: str) -> str:
    if lang == "zh":
        return {
            "architecture": "只有注册、DA、证明和根匹配时成为共享事实",
            "bridge": "注册策略、nonce、资产映射和证据同时通过",
            "proof": "证明通过且公开输入匹配已提交根",
            "security": "故障证据有效且动作在治理/协议权限内",
            "operations": "每个阶段都有可审计证据",
            "byte": "消费者重算相同规范字节和哈希",
            "state": "转换闸门通过；否则保持原状态",
            "docs": "图回答明确问题且中英文一致",
            "roadmap": "能力有测试、文档和部署演练",
            "trust": "声明可独立验证且故障影响有界",
            "execution": "执行确定、授权且回执匹配状态变化",
        }[family]
    return {
        "architecture": "shared fact only after registry, DA, proof, and root match",
        "bridge": "registry policy, nonce, asset mapping, and evidence pass together",
        "proof": "proof verifies and public inputs match committed roots",
        "security": "failure evidence is valid and action is protocol-authorized",
        "operations": "each phase has auditable evidence",
        "byte": "consumer recomputes identical canonical bytes and digest",
        "state": "transition gate passes; otherwise previous state remains",
        "docs": "figure answers a clear question and has a Chinese mirror",
        "roadmap": "capability has tests, docs, and deployment rehearsal",
        "trust": "claim is independently verifiable and blast radius is bounded",
        "execution": "execution is deterministic, authorized, and receipt-matched",
    }[family]


def wrap_text(value: str, width: int) -> list[str]:
    lines: list[str] = []
    for raw in str(value).split("\n"):
        lines.extend(textwrap.wrap(raw, width=width, break_long_words=False, replace_whitespace=False) or [""])
    return lines


def esc(value: str) -> str:
    return html.escape(str(value), quote=True)


if __name__ == "__main__":
    main()
