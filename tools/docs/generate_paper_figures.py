#!/usr/bin/env python3
"""Generate paper-style Neo N4 architecture learning figures.

These figures are intended for the README and visual guide. They explain
technical architecture, principles, data flow, and workflow at protocol level;
they are not implementation maps or source-file inventories.
"""

from __future__ import annotations

import html
import textwrap
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
OUT_EN = ROOT / "docs" / "figures" / "paper"
OUT_ZH = ROOT / "docs" / "zh" / "figures" / "paper"


@dataclass(frozen=True)
class Figure:
    slug: str
    title_en: str
    title_zh: str
    subtitle_en: str
    subtitle_zh: str
    accent: str
    tint: str
    topology_en: tuple[tuple[str, str], ...]
    topology_zh: tuple[tuple[str, str], ...]
    model_en: tuple[tuple[str, str], ...]
    model_zh: tuple[tuple[str, str], ...]
    path_en: tuple[tuple[str, str], ...]
    path_zh: tuple[tuple[str, str], ...]
    checks_en: tuple[tuple[str, str], ...]
    checks_zh: tuple[tuple[str, str], ...]


FIGURES: tuple[Figure, ...] = (
    Figure(
        "neo-n4-architecture",
        "Neo N4 Layer-2 Stack Architecture",
        "Neo N4 Layer-2 栈架构",
        "Control plane, data plane, proof plane, and availability plane in one protocol view.",
        "在一张协议图中同时展示控制面、数据面、证明面和数据可用性面。",
        "#0369a1",
        "#eef7ff",
        (
            ("L1 settlement plane", "NeoHub deployable contracts: chain registry, shared bridge, token registry, settlement manager, verifier registry, governance."),
            ("Aggregation plane", "Optional Neo Gateway aggregates proof claims and cross-L2 messages before L1 submission."),
            ("L2 execution plane", "N4 L2 chains run NeoVM2/RISC-V by default and may add pluggable N4 execution profiles."),
            ("Availability plane", "NeoFS stores batch payloads, witnesses, and report artifacts referenced by commitments."),
        ),
        (
            ("L1 结算面", "NeoHub 可部署合约：链注册表、共享桥、资产注册表、结算管理器、验证器注册表、治理。"),
            ("聚合面", "可选 Neo Gateway 在提交到 L1 前聚合证明声明和跨 L2 消息。"),
            ("L2 执行面", "N4 L2 默认运行 NeoVM2/RISC-V，并可加入可插拔 N4 execution profile。"),
            ("可用性面", "NeoFS 保存批次 payload、见证和报告产物，并由承诺引用。"),
        ),
        (
            ("Boundary", "L1 owns final agreement; L2 owns throughput; NeoFS owns data retrievability; provers own evidence generation."),
            ("Invariant", "Asset custody, chain admission, state-root acceptance, and message finality converge at NeoHub."),
            ("Interface", "Every plane communicates by canonical hashes, roots, proofs, events, and registry entries."),
        ),
        (
            ("边界", "L1 负责最终共识，L2 负责吞吐，NeoFS 负责数据可取回，prover 负责证据生成。"),
            ("不变量", "资产托管、链准入、状态根接受、消息终局都收敛到 NeoHub。"),
            ("接口", "各平面只通过规范哈希、根、证明、事件和注册表条目通信。"),
        ),
        (
            ("Register", "operator registers L2 config and bridge parameters"),
            ("Execute", "L2 orders transactions and updates local state"),
            ("Publish DA", "batch data and witness artifacts are pinned to NeoFS"),
            ("Prove", "prover creates validity or configured evidence"),
            ("Settle", "NeoHub verifies and accepts the new state root"),
            ("Route", "withdrawals and messages become globally consumable"),
        ),
        (
            ("注册", "运维者注册 L2 配置和桥参数"),
            ("执行", "L2 排序交易并更新本地状态"),
            ("发布 DA", "批次数据和见证产物写入 NeoFS"),
            ("证明", "prover 生成有效性证明或配置的证据"),
            ("结算", "NeoHub 验证并接受新状态根"),
            ("路由", "提款和消息变成全局可消费对象"),
        ),
        (
            ("Layering", "No plane should smuggle authority from another plane."),
            ("Minimal L1", "NeoHub stays contract-first and avoids unnecessary L1 core changes."),
            ("Extensibility", "Extra VMs are N4 execution profiles, not a separate NeoX model."),
            ("Auditability", "Every accepted root must point to retrievable data and verification evidence."),
        ),
        (
            ("分层", "任何平面都不能偷用另一个平面的权限。"),
            ("最小 L1", "NeoHub 保持合约优先，避免不必要的 L1 core 修改。"),
            ("可扩展性", "额外 VM 是 N4 execution profile，不是 NeoX 模型。"),
            ("可审计性", "每个被接受的根都必须指向可取回数据和验证证据。"),
        ),
    ),
    Figure(
        "neo-n4-technical-principles",
        "Neo N4 Technical Principles",
        "Neo N4 技术原理",
        "The correctness model: deterministic execution, canonical commitments, data availability, and verifiable settlement.",
        "正确性模型：确定性执行、规范承诺、数据可用性和可验证结算。",
        "#b45309",
        "#fff7ed",
        (
            ("Deterministic execution", "same ordered inputs and same state produce the same post-state root"),
            ("Canonical commitments", "batches, messages, withdrawals, and public inputs have stable byte encodings"),
            ("Data availability", "NeoFS content identifiers bind accepted roots to retrievable data"),
            ("Verifiable settlement", "NeoHub accepts state changes only through configured verifier policy"),
        ),
        (
            ("确定性执行", "同一有序输入和同一状态必须产生同一后状态根"),
            ("规范承诺", "批次、消息、提款和公开输入都有稳定字节编码"),
            ("数据可用性", "NeoFS 内容标识把被接受的根绑定到可取回数据"),
            ("可验证结算", "NeoHub 只通过配置好的验证策略接受状态变化"),
        ),
        (
            ("State transition", "delta(S, Tx*, W) -> S' plus receipts, roots, and public inputs."),
            ("Acceptance rule", "accept iff registry authorizes chain, DA is bound, proof verifies, and root matches."),
            ("Rejection rule", "invalid data, missing DA, verifier mismatch, or replayed message cannot advance state."),
        ),
        (
            ("状态转换", "delta(状态, 交易序列, 见证) -> 新状态，并产生回执、根和公开输入。"),
            ("接受规则", "当且仅当链已注册、DA 已绑定、证明通过且根匹配时接受。"),
            ("拒绝规则", "无效数据、缺失 DA、验证器不匹配或重放消息不能推进状态。"),
        ),
        (
            ("Normalize", "convert transactions and bridge events into canonical objects"),
            ("Commit", "compute roots and public input hashes"),
            ("Publish", "write batch data and witness material to NeoFS"),
            ("Verify", "check proof envelope and verifier registry policy"),
            ("Finalize", "advance chain state and unlock messages"),
            ("Observe", "emit receipts, reports, metrics, and audit logs"),
        ),
        (
            ("规范化", "把交易和桥事件转换为规范对象"),
            ("承诺", "计算根和公开输入哈希"),
            ("发布", "把批次数据和见证材料写入 NeoFS"),
            ("验证", "检查证明封装和验证器注册策略"),
            ("终局", "推进链状态并解锁消息"),
            ("观测", "输出回执、报告、指标和审计日志"),
        ),
        (
            ("Safety", "wrong roots, missing data, and replayed messages must fail closed."),
            ("Liveness", "forced inclusion and operator runbooks provide recovery paths."),
            ("Cost", "L1 verifies compact evidence; heavy execution and data remain off-chain/L2."),
            ("User model", "users see unified NEO/GAS/stable/BTC assets across L2s."),
        ),
        (
            ("安全性", "错误根、缺失数据和重放消息必须默认失败。"),
            ("活性", "强制纳入和运维 runbook 提供恢复路径。"),
            ("成本", "L1 验证压缩证据，重执行和数据保留在链下或 L2。"),
            ("用户模型", "用户在所有 L2 上看到统一的 NEO/GAS/稳定币/BTC 资产。"),
        ),
    ),
    Figure(
        "neo-n4-dataflow",
        "Neo N4 End-to-End Data Flow",
        "Neo N4 端到端数据流",
        "How transactions, roots, witnesses, proofs, and messages move from L2 execution to L1 finality.",
        "交易、根、见证、证明和消息如何从 L2 执行移动到 L1 终局。",
        "#0f766e",
        "#ecfdf5",
        (
            ("User/API input", "wallets, SDKs, RPC, bridge UI, and external-chain watchers create typed intents"),
            ("L2 ordered data", "sequencer turns intents into ordered blocks and batch candidates"),
            ("Evidence data", "execution traces, receipts, roots, and witness material feed proof generation"),
            ("L1 consumable data", "state roots, withdrawal roots, message roots, proof bytes, and DA CIDs"),
        ),
        (
            ("用户/API 输入", "钱包、SDK、RPC、桥 UI 和外链 watcher 产生强类型意图"),
            ("L2 有序数据", "排序器把意图变成有序区块和候选批次"),
            ("证据数据", "执行轨迹、回执、根和见证材料进入证明生成"),
            ("L1 可消费数据", "状态根、提款根、消息根、证明字节和 DA CID"),
        ),
        (
            ("Data rule", "hashes cross trust boundaries; full payloads stay retrievable through NeoFS."),
            ("Proof rule", "proof statements bind batch range, pre-root, post-root, DA root, and verifier key."),
            ("Message rule", "L1/L2 and L2/L2 messages consume inclusion evidence and nonce state exactly once."),
        ),
        (
            ("数据规则", "跨信任边界传递哈希；完整 payload 通过 NeoFS 保持可取回。"),
            ("证明规则", "证明语句绑定批次范围、前状态根、后状态根、DA 根和验证密钥。"),
            ("消息规则", "L1/L2 与 L2/L2 消息只凭 inclusion 证据和 nonce 状态消费一次。"),
        ),
        (
            ("Intent", "signed transaction or bridge event"),
            ("Block", "ordered L2 execution output"),
            ("Batch", "commitment over blocks, receipts, and roots"),
            ("NeoFS", "content-addressed payload and witness storage"),
            ("Proof", "validity or configured evidence envelope"),
            ("Settlement", "NeoHub accepts root and releases dependent flows"),
        ),
        (
            ("意图", "已签名交易或桥事件"),
            ("区块", "有序 L2 执行输出"),
            ("批次", "覆盖区块、回执和根的承诺"),
            ("NeoFS", "内容寻址的 payload 和见证存储"),
            ("证明", "有效性证明或配置的证据封装"),
            ("结算", "NeoHub 接受根并释放依赖流程"),
        ),
        (
            ("Traceability", "every report should link intent, block, batch, DA CID, proof, and settlement tx."),
            ("Replay protection", "message hash plus nonce plus source chain prevents duplicate consumption."),
            ("DA health", "unavailable NeoFS payloads are settlement blockers, not warnings."),
            ("Backpressure", "batch/prover/DA queues need explicit metrics and retry state."),
        ),
        (
            ("可追踪性", "每份报告都应关联意图、区块、批次、DA CID、证明和结算交易。"),
            ("防重放", "消息哈希 + nonce + 源链防止重复消费。"),
            ("DA 健康", "NeoFS payload 不可用是结算阻断项，不是普通告警。"),
            ("背压", "batch/prover/DA 队列需要明确指标和重试状态。"),
        ),
    ),
    Figure(
        "neo-n4-workflow",
        "Neo N4 Production Workflow",
        "Neo N4 生产工作流",
        "The operational workflow from chain admission to deposits, batch settlement, withdrawals, and recovery.",
        "从链准入到充值、批次结算、提款和恢复的运维工作流。",
        "#7e22ce",
        "#faf5ff",
        (
            ("Admission", "chain config is reviewed, registered, and bound to bridge/verifier policy"),
            ("Runtime", "sequencer, batcher, DA writer, prover, gateway, and relayers run continuously"),
            ("Settlement", "accepted proofs advance state and make withdrawals/messages consumable"),
            ("Recovery", "forced inclusion, challenge, pause, and operator runbooks handle failure"),
        ),
        (
            ("准入", "链配置经过审核、注册，并绑定桥和验证器策略"),
            ("运行", "sequencer、batcher、DA writer、prover、gateway、relayer 持续运行"),
            ("结算", "被接受的证明推进状态，并让提款/消息可消费"),
            ("恢复", "强制纳入、挑战、暂停和运维 runbook 处理故障"),
        ),
        (
            ("Control loop", "configure -> run -> observe -> verify -> settle -> recover"),
            ("Gate", "each phase has explicit evidence before the next phase is allowed"),
            ("Escalation", "operator automation can retry, but governance controls destructive actions"),
        ),
        (
            ("控制循环", "配置 -> 运行 -> 观测 -> 验证 -> 结算 -> 恢复"),
            ("闸门", "每个阶段都有明确证据，才能进入下一阶段"),
            ("升级处理", "运维自动化可以重试，但破坏性动作由治理控制"),
        ),
        (
            ("Configure", "select L1/L2 branch, contracts, verifier, DA, token mapping"),
            ("Register", "write chain and bridge policy into NeoHub"),
            ("Operate", "produce blocks, batches, DA objects, and proofs"),
            ("Settle", "submit compact evidence and update canonical roots"),
            ("Serve users", "complete deposits, withdrawals, and cross-L2 messages"),
            ("Recover", "force inclusion, challenge, pause, rotate, or replay"),
        ),
        (
            ("配置", "选择 L1/L2 分支、合约、验证器、DA 和资产映射"),
            ("注册", "把链和桥策略写入 NeoHub"),
            ("运行", "产出区块、批次、DA 对象和证明"),
            ("结算", "提交压缩证据并更新规范根"),
            ("服务用户", "完成充值、提款和跨 L2 消息"),
            ("恢复", "强制纳入、挑战、暂停、轮换或重放"),
        ),
        (
            ("Evidence first", "do not mark a phase complete without an artifact or on-chain tx hash."),
            ("Secrets", "signers and deployment keys must stay outside browser/static docs."),
            ("Rollback", "every deployment needs a pause/rollback path and stored provenance."),
            ("Compatibility", "docs, configs, branches, and generated figures must describe the same system."),
        ),
        (
            ("证据优先", "没有产物或链上交易哈希，不标记阶段完成。"),
            ("密钥", "签名器和部署私钥不能进入浏览器或静态文档。"),
            ("回滚", "每次部署都需要暂停/回滚路径和已保存来源信息。"),
            ("一致性", "文档、配置、分支和生成图必须描述同一个系统。"),
        ),
    ),
)


def main() -> None:
    OUT_EN.mkdir(parents=True, exist_ok=True)
    OUT_ZH.mkdir(parents=True, exist_ok=True)
    for figure in FIGURES:
        (OUT_EN / f"{figure.slug}.svg").write_text(render(figure, "en"), encoding="utf-8")
        (OUT_ZH / f"{figure.slug}.svg").write_text(render(figure, "zh"), encoding="utf-8")
    print(f"Generated {len(FIGURES) * 2} paper figures.")


def render(figure: Figure, lang: str) -> str:
    title = figure.title_zh if lang == "zh" else figure.title_en
    subtitle = figure.subtitle_zh if lang == "zh" else figure.subtitle_en
    topology = figure.topology_zh if lang == "zh" else figure.topology_en
    model = figure.model_zh if lang == "zh" else figure.model_en
    path = figure.path_zh if lang == "zh" else figure.path_en
    checks = figure.checks_zh if lang == "zh" else figure.checks_en
    t = terms(lang)
    parts = [
        '<svg xmlns="http://www.w3.org/2000/svg" width="1920" height="1180" viewBox="0 0 1920 1180" role="img">',
        "<defs>",
        f'<marker id="arrowAccent" viewBox="0 0 10 10" refX="9.2" refY="5" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 Z" fill="{figure.accent}"/></marker>',
        '<marker id="arrowGray" viewBox="0 0 10 10" refX="9.2" refY="5" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 Z" fill="#475569"/></marker>',
        '<marker id="arrowFault" viewBox="0 0 10 10" refX="9.2" refY="5" markerWidth="10" markerHeight="10" orient="auto"><path d="M0,0 L10,5 L0,10 Z" fill="#dc2626"/></marker>',
        '<filter id="shadow" x="-3%" y="-4%" width="106%" height="108%"><feDropShadow dx="0" dy="8" stdDeviation="10" flood-color="#0f172a" flood-opacity="0.10"/></filter>',
        "<style>",
        ".bg{fill:#f8fafc}.panel{fill:#ffffff;stroke:#cbd5e1;stroke-width:1.5;filter:url(#shadow)}",
        ".title{font:800 36px 'Inter','Segoe UI',Arial,sans-serif;fill:#0f172a}.subtitle{font:500 17px 'Inter','Segoe UI',Arial,sans-serif;fill:#475569}",
        ".panelTitle{font:750 22px 'Inter','Segoe UI',Arial,sans-serif;fill:#0f172a}.panelSub{font:500 15px 'Inter','Segoe UI',Arial,sans-serif;fill:#475569}",
        ".strong{font:760 15px 'Inter','Segoe UI',Arial,sans-serif;fill:#0f172a}.body{font:500 14px 'Inter','Segoe UI',Arial,sans-serif;fill:#334155}.small{font:500 13px 'Inter','Segoe UI',Arial,sans-serif;fill:#475569}",
        ".mono{font:650 14px 'Cascadia Mono','SFMono-Regular',Consolas,monospace;fill:#0f172a}.label{font:800 14px 'Inter','Segoe UI',Arial,sans-serif;fill:#ffffff}.caption{font:500 13px 'Inter','Segoe UI',Arial,sans-serif;fill:#64748b}",
        f'.accentArrow{{stroke:{figure.accent};stroke-width:2.6;fill:none;marker-end:url(#arrowAccent)}}.dashArrow{{stroke:#64748b;stroke-width:2.0;fill:none;stroke-dasharray:7 6;marker-end:url(#arrowGray)}}.faultArrow{{stroke:#dc2626;stroke-width:2.2;fill:none;stroke-dasharray:5 5;marker-end:url(#arrowFault)}}',
        "</style>",
        "</defs>",
        '<rect class="bg" width="1920" height="1180"/>',
        f'<text x="56" y="66" class="title">{esc(title)}</text>',
        f'<text x="56" y="98" class="subtitle">{esc(subtitle)}</text>',
        f'<rect x="56" y="118" width="1808" height="38" rx="6" fill="{figure.tint}" stroke="{figure.accent}" stroke-width="1.2"/>',
        f'<text x="76" y="143" class="small">{esc(t["read"])} <tspan class="strong">{esc(t["principle"])}</tspan></text>',
    ]
    parts.extend(panel(56, 178, 870, 392, "a", t["topology"], t["topology_sub"], figure))
    parts.extend(panel(966, 178, 898, 392, "b", t["model"], t["model_sub"], figure))
    parts.extend(panel(56, 606, 1240, 494, "c", t["path"], t["path_sub"], figure))
    parts.extend(panel(1334, 606, 530, 494, "d", t["checks"], t["checks_sub"], figure))
    parts.extend(draw_topology(92, 270, topology, figure))
    parts.extend(draw_model(1000, 252, model, figure, lang))
    parts.extend(draw_path(92, 730, path, figure, t))
    parts.extend(draw_checks(1368, 710, checks, figure, t))
    footer = "Paper-style figure generated from protocol concepts; it intentionally avoids source-file inventory."
    if lang == "zh":
        footer = "论文式图表根据协议概念生成；刻意不展示源码文件清单。"
    parts.append(f'<text x="56" y="1146" class="caption">{esc(footer)}</text>')
    parts.append("</svg>")
    return "\n".join(parts) + "\n"


def terms(lang: str) -> dict[str, str]:
    if lang == "zh":
        return {
            "read": "读图方式：",
            "principle": "把 Neo N4 当作协议系统学习，而不是当作代码目录学习。",
            "topology": "系统拓扑",
            "topology_sub": "主要平面、边界和职责",
            "model": "技术模型",
            "model_sub": "正确性规则和接受条件",
            "path": "编号路径",
            "path_sub": "对象如何沿协议推进",
            "checks": "审计检查点",
            "checks_sub": "读者应重点验证的条件",
            "solid": "实线：状态 / 数据",
            "dashed": "虚线：证明 / 观测",
            "reject": "红线：拒绝 / 恢复",
        }
    return {
        "read": "Read as:",
        "principle": "learn Neo N4 as a protocol system, not as a source tree.",
        "topology": "System Topology",
        "topology_sub": "major planes, boundaries, and responsibilities",
        "model": "Technical Model",
        "model_sub": "correctness rules and acceptance conditions",
        "path": "Numbered Path",
        "path_sub": "how objects advance through the protocol",
        "checks": "Audit Checkpoints",
        "checks_sub": "conditions readers should verify",
        "solid": "solid: state / data",
        "dashed": "dashed: proof / observation",
        "reject": "red: reject / recover",
    }


def panel(x: int, y: int, w: int, h: int, letter: str, title: str, subtitle: str, figure: Figure) -> list[str]:
    return [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="10" class="panel"/>',
        f'<rect x="{x}" y="{y}" width="{w}" height="54" rx="10" fill="{figure.tint}"/>',
        f'<circle cx="{x + 30}" cy="{y + 28}" r="16" fill="{figure.accent}"/>',
        f'<text x="{x + 30}" y="{y + 33}" text-anchor="middle" class="label">{esc(letter)}</text>',
        f'<text x="{x + 58}" y="{y + 32}" class="panelTitle">{esc(title)}</text>',
        f'<text x="{x + 58}" y="{y + 52}" class="panelSub">{esc(subtitle)}</text>',
    ]


def draw_topology(x: int, y: int, rows: tuple[tuple[str, str], ...], figure: Figure) -> list[str]:
    parts: list[str] = []
    box_w = 180
    gap = 28
    for idx, (title, body) in enumerate(rows):
        bx = x + idx * (box_w + gap)
        parts.extend(card(bx, y + (idx % 2) * 54, box_w, 208, title, body, figure.tint, figure.accent, wrap=22, lines=6))
        if idx < len(rows) - 1:
            parts.append(f'<path class="accentArrow" d="M{bx + box_w} {y + 104 + (idx % 2) * 54} C{bx + box_w + 12} {y + 104} {bx + box_w + 18} {y + 104} {bx + box_w + gap} {y + 104 + ((idx + 1) % 2) * 54}"/>')
    parts.append(f'<line x1="{x + 204}" y1="{y + 258}" x2="{x + 660}" y2="{y + 258}" stroke="{figure.accent}" stroke-width="1.4" stroke-dasharray="6 6"/>')
    parts.append(f'<text x="{x + 430}" y="{y + 282}" class="caption" text-anchor="middle">registry + commitments + proofs + DA references</text>')
    return parts


def draw_model(x: int, y: int, rows: tuple[tuple[str, str], ...], figure: Figure, lang: str) -> list[str]:
    formula = "delta(state, input, witness) -> state' + evidence" if lang == "en" else "delta(状态, 输入, 见证) -> 新状态 + 证据"
    accept = "accept iff registered && DA-bound && verifier-accepted && root-matched" if lang == "en" else "接受条件 = 已注册 && DA 已绑定 && 验证器接受 && 根匹配"
    parts = [
        f'<rect x="{x}" y="{y}" width="828" height="62" rx="8" fill="{figure.tint}" stroke="{figure.accent}" stroke-width="1.5"/>',
        f'<text x="{x + 20}" y="{y + 38}" class="mono">{esc(formula)}</text>',
    ]
    for idx, (title, body) in enumerate(rows):
        by = y + 90 + idx * 72
        parts.extend(numbered_row(x + 12, by, 630, 54, idx + 1, title, body, figure))
        if idx < len(rows) - 1:
            parts.append(f'<path class="accentArrow" d="M{x + 328} {by + 54} L{x + 328} {by + 72}"/>')
    parts.extend(card(x + 672, y + 106, 146, 174, "Acceptance", accept, "#ffffff", figure.accent, wrap=18, lines=7))
    parts.append(f'<path class="dashArrow" d="M{x + 642} {y + 208} L{x + 672} {y + 208}"/>')
    parts.append(f'<text x="{x + 14}" y="{y + 310}" class="mono">{esc(accept)}</text>')
    return parts


def draw_path(x: int, y: int, rows: tuple[tuple[str, str], ...], figure: Figure, t: dict[str, str]) -> list[str]:
    parts: list[str] = []
    xs = [x, x + 198, x + 396, x + 594, x + 792, x + 990]
    for idx, ((title, body), px) in enumerate(zip(rows, xs), start=1):
        parts.extend(path_node(px, y, 150, 120, idx, title, body, figure))
        if idx < len(rows):
            parts.append(f'<path class="accentArrow" d="M{px + 150} {y + 60} L{xs[idx]} {y + 60}"/>')
    parts.append(f'<path class="dashArrow" d="M{x + 90} {y + 170} C{x + 320} {y + 244} {x + 760} {y + 244} {x + 1040} {y + 170}"/>')
    parts.extend(card(x + 390, y + 184, 210, 120, "Reject / Recover", "missing data, bad proof, replay, timeout, governance pause", "#fff1f2", "#dc2626", wrap=28, lines=4))
    parts.append(f'<path class="faultArrow" d="M{x + 470} {y + 120} L{x + 470} {y + 184}"/>')
    parts.append(f'<text x="{x}" y="{y + 340}" class="caption">{esc(t["solid"])} · {esc(t["dashed"])} · {esc(t["reject"])}</text>')
    return parts


def draw_checks(x: int, y: int, rows: tuple[tuple[str, str], ...], figure: Figure, t: dict[str, str]) -> list[str]:
    parts = [f'<rect x="{x}" y="{y}" width="462" height="294" rx="8" fill="#ffffff" stroke="#cbd5e1" stroke-width="1.2"/>']
    for idx, (title, body) in enumerate(rows):
        ry = y + 10 + idx * 68
        fill = figure.tint if idx % 2 == 0 else "#ffffff"
        parts.append(f'<rect x="{x + 10}" y="{ry}" width="442" height="58" rx="6" fill="{fill}"/>')
        parts.append(f'<text x="{x + 22}" y="{ry + 23}" class="strong">{esc(title)}</text>')
        for line_idx, line in enumerate(wrap(body, 39)[:2]):
            parts.append(f'<text x="{x + 150}" y="{ry + 20 + line_idx * 17}" class="small">{esc(line)}</text>')
    parts.append(f'<line x1="{x + 136}" y1="{y + 14}" x2="{x + 136}" y2="{y + 282}" stroke="{figure.accent}" stroke-width="1.2" stroke-dasharray="4 5"/>')
    parts.append(f'<rect x="{x}" y="{y + 316}" width="462" height="74" rx="8" fill="#f8fafc" stroke="#cbd5e1" stroke-width="1.2"/>')
    parts.append(f'<line x1="{x + 20}" y1="{y + 340}" x2="{x + 66}" y2="{y + 340}" stroke="{figure.accent}" stroke-width="2.6" marker-end="url(#arrowAccent)"/>')
    parts.append(f'<text x="{x + 82}" y="{y + 344}" class="small">{esc(t["solid"])}</text>')
    parts.append(f'<line x1="{x + 20}" y1="{y + 364}" x2="{x + 66}" y2="{y + 364}" stroke="#64748b" stroke-width="2.0" stroke-dasharray="7 6" marker-end="url(#arrowGray)"/>')
    parts.append(f'<text x="{x + 82}" y="{y + 368}" class="small">{esc(t["dashed"])}</text>')
    parts.append(f'<line x1="{x + 292}" y1="{y + 364}" x2="{x + 336}" y2="{y + 364}" stroke="#dc2626" stroke-width="2.2" stroke-dasharray="5 5" marker-end="url(#arrowFault)"/>')
    parts.append(f'<text x="{x + 350}" y="{y + 368}" class="small">{esc(t["reject"])}</text>')
    return parts


def card(x: int, y: int, w: int, h: int, title: str, body: str, fill: str, stroke: str, *, wrap: int, lines: int) -> list[str]:
    parts = [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{fill}" stroke="{stroke}" stroke-width="1.4"/>',
        f'<text x="{x + 14}" y="{y + 28}" class="strong">{esc(title)}</text>',
    ]
    for idx, line in enumerate(wrap_text(body, wrap)[:lines]):
        parts.append(f'<text x="{x + 14}" y="{y + 58 + idx * 18}" class="body">{esc(line)}</text>')
    return parts


def numbered_row(x: int, y: int, w: int, h: int, idx: int, title: str, body: str, figure: Figure) -> list[str]:
    return [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="#ffffff" stroke="#cbd5e1" stroke-width="1.2"/>',
        f'<circle cx="{x + 26}" cy="{y + 27}" r="15" fill="{figure.accent}"/>',
        f'<text x="{x + 26}" y="{y + 32}" text-anchor="middle" class="label">{idx}</text>',
        f'<text x="{x + 54}" y="{y + 23}" class="strong">{esc(title)}</text>',
        f'<text x="{x + 54}" y="{y + 43}" class="small">{esc(wrap_text(body, 66)[0])}</text>',
    ]


def path_node(x: int, y: int, w: int, h: int, idx: int, title: str, body: str, figure: Figure) -> list[str]:
    parts = [
        f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="8" fill="{figure.tint}" stroke="{figure.accent}" stroke-width="1.5"/>',
        f'<rect x="{x + 10}" y="{y + 10}" width="30" height="24" rx="5" fill="{figure.accent}"/>',
        f'<text x="{x + 25}" y="{y + 27}" text-anchor="middle" class="label">{idx}</text>',
        f'<text x="{x + 48}" y="{y + 28}" class="strong">{esc(wrap_text(title, 12)[0])}</text>',
    ]
    for line_idx, line in enumerate(wrap_text(body, 18)[:4]):
        parts.append(f'<text x="{x + 12}" y="{y + 58 + line_idx * 17}" class="small">{esc(line)}</text>')
    return parts


def wrap_text(value: str, width: int) -> list[str]:
    lines: list[str] = []
    for raw in str(value).split("\n"):
        lines.extend(textwrap.wrap(raw, width=width, break_long_words=False, replace_whitespace=False) or [""])
    return lines


def wrap(value: str, width: int) -> list[str]:
    return wrap_text(value, width)


def esc(value: str) -> str:
    return html.escape(str(value), quote=True)


if __name__ == "__main__":
    main()
