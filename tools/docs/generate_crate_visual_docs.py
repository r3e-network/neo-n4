#!/usr/bin/env python3
"""Generate crate-level visual learning guides for the Neo N4 workspace.

The generated files are intentionally simple, static assets:
- README.md / README.zh.md visual guide sections per crate
- per-crate position, principles, architecture, workflow, and dataflow diagrams
- source-aware module, public API, test evidence, and dependency diagrams
- deep per-crate learning guides under docs/learning-guide*.md
- English and Chinese Mermaid source diagrams
- English and Chinese SVG diagrams

The script uses only the Python standard library so it can run in a fresh clone.
"""

from __future__ import annotations

import html
import json
import re
import textwrap
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable

try:
    import tomllib
except ModuleNotFoundError:  # pragma: no cover
    import tomli as tomllib  # type: ignore


ROOT = Path(__file__).resolve().parents[2]
MARKER_EN = ("<!-- N4-CRATE-VISUAL-GUIDE:START -->", "<!-- N4-CRATE-VISUAL-GUIDE:END -->")
MARKER_ZH = (
    "<!-- N4-CRATE-VISUAL-GUIDE-ZH:START -->",
    "<!-- N4-CRATE-VISUAL-GUIDE-ZH:END -->",
)


@dataclass(frozen=True)
class CrateInfo:
    name: str
    path: Path
    description: str
    dependencies: tuple[str, ...]


@dataclass(frozen=True)
class Guide:
    layer_en: str
    layer_zh: str
    role_en: str
    role_zh: str
    inputs_en: tuple[str, ...]
    inputs_zh: tuple[str, ...]
    responsibilities_en: tuple[str, ...]
    responsibilities_zh: tuple[str, ...]
    outputs_en: tuple[str, ...]
    outputs_zh: tuple[str, ...]
    consumers_en: tuple[str, ...]
    consumers_zh: tuple[str, ...]
    workflow_en: tuple[str, ...]
    workflow_zh: tuple[str, ...]
    dataflow_en: tuple[str, ...]
    dataflow_zh: tuple[str, ...]


@dataclass(frozen=True)
class DependencyInfo:
    name: str
    kind: str


@dataclass(frozen=True)
class SourceFileInfo:
    path: str
    role_en: str
    role_zh: str
    public_symbols: tuple[str, ...]
    tests: tuple[str, ...]


@dataclass(frozen=True)
class SourceProfile:
    files: tuple[SourceFileInfo, ...]
    public_symbols: tuple[str, ...]
    tests: tuple[str, ...]
    dependencies: tuple[DependencyInfo, ...]
    module_declarations: tuple[str, ...]


def main() -> None:
    crates = discover_crates()
    index_rows_en: list[str] = []
    index_rows_zh: list[str] = []

    for crate in crates:
        guide = guide_for(crate)
        profile = analyze_crate(crate)
        write_crate_assets(crate, guide, profile)
        rel = crate.path.relative_to(ROOT).as_posix()
        readme_link_en = f"../{rel}/README.md"
        readme_link_zh = f"../../{rel}/README.zh.md"
        index_rows_en.append(
            f"| [`{crate.name}`]({readme_link_en}) | `{rel}` | {guide.layer_en} | {guide.role_en} |"
        )
        index_rows_zh.append(
            f"| [`{crate.name}`]({readme_link_zh}) | `{rel}` | {guide.layer_zh} | {guide.role_zh} |"
        )

    write_index(index_rows_en, index_rows_zh)
    print(f"Generated visual guides for {len(crates)} crates.")


def discover_crates() -> list[CrateInfo]:
    crates: list[CrateInfo] = []
    for cargo in sorted(ROOT.rglob("Cargo.toml")):
        if any(part in {".git", "target"} for part in cargo.parts):
            continue
        data = tomllib.loads(cargo.read_text(encoding="utf-8"))
        package = data.get("package")
        if not package:
            continue
        dependencies: list[str] = []
        for section in ("dependencies", "dev-dependencies", "build-dependencies"):
            dependencies.extend(data.get(section, {}).keys())
        crates.append(
            CrateInfo(
                name=package["name"],
                path=cargo.parent,
                description=package.get("description", ""),
                dependencies=tuple(sorted(set(dependencies))),
            )
        )
    return crates


PUBLIC_SYMBOL_RE = re.compile(
    r"(?m)^\s*pub(?:\([^)]*\))?\s+(?:async\s+|unsafe\s+|extern\s+\"[^\"]+\"\s+)?"
    r"(struct|enum|trait|fn|type|const|static)\s+([A-Za-z_][A-Za-z0-9_]*)"
)
TEST_RE = re.compile(r"(?m)^\s*(?:#\[[^\]]*test[^\]]*\]\s*)+(?:async\s+)?fn\s+([A-Za-z_][A-Za-z0-9_]*)")
MOD_RE = re.compile(r"(?m)^\s*(?:pub\s+)?mod\s+([A-Za-z_][A-Za-z0-9_]*)\s*;")
PUB_USE_RE = re.compile(r"(?m)^\s*pub\s+use\s+([^;]+);")


def analyze_crate(crate: CrateInfo) -> SourceProfile:
    source_files = sorted(
        file
        for file in crate.path.rglob("*.rs")
        if not any(part in {"target", ".git"} for part in file.parts)
    )
    file_infos: list[SourceFileInfo] = []
    public_symbols: list[str] = []
    tests: list[str] = []
    module_declarations: list[str] = []

    for file in source_files:
        rel = file.relative_to(crate.path).as_posix()
        text = file.read_text(encoding="utf-8", errors="replace")
        symbols = tuple(f"{kind} {name}" for kind, name in PUBLIC_SYMBOL_RE.findall(text))
        file_tests = tuple(TEST_RE.findall(text))
        mods = tuple(MOD_RE.findall(text))
        pub_uses = tuple(item.strip() for item in PUB_USE_RE.findall(text))
        public_symbols.extend(f"{rel}: {symbol}" for symbol in symbols)
        tests.extend(f"{rel}: {name}" for name in file_tests)
        module_declarations.extend(f"{rel}: mod {name}" for name in mods)
        module_declarations.extend(f"{rel}: pub use {name}" for name in pub_uses[:6])
        file_infos.append(
            SourceFileInfo(
                path=rel,
                role_en=file_role(rel, "en"),
                role_zh=file_role(rel, "zh"),
                public_symbols=symbols,
                tests=file_tests,
            )
        )

    dependencies: list[DependencyInfo] = []
    cargo = crate.path / "Cargo.toml"
    data = tomllib.loads(cargo.read_text(encoding="utf-8"))
    for section, label in (
        ("dependencies", "runtime"),
        ("dev-dependencies", "test"),
        ("build-dependencies", "build"),
    ):
        for dep in sorted(data.get(section, {}).keys()):
            dependencies.append(DependencyInfo(dep, label))

    return SourceProfile(
        files=tuple(file_infos),
        public_symbols=tuple(public_symbols),
        tests=tuple(tests),
        dependencies=tuple(dependencies),
        module_declarations=tuple(module_declarations),
    )


def file_role(path: str, lang: str) -> str:
    lowered = path.lower()
    role_en = "implementation detail or helper module"
    role_zh = "实现细节或辅助模块"
    hints = [
        ("src/lib.rs", "crate root, public exports, and top-level documentation", "crate 根、公开导出和顶层文档"),
        ("src/main.rs", "binary or CLI entrypoint", "二进制或 CLI 入口"),
        ("src/bin/", "additional binary entrypoint", "额外二进制入口"),
        ("tests/", "external behavior or integration test", "外部行为或集成测试"),
        ("examples/", "runnable example or tutorial fixture", "可运行示例或教程 fixture"),
        ("fuzz", "fuzzing harness and adversarial input exploration", "fuzz harness 与对抗输入探索"),
        ("template", "developer template and scaffold artifact", "开发者模板与脚手架产物"),
        ("abi", "wire format, stack value, or host/guest boundary type", "线格式、栈值或 host/guest 边界类型"),
        ("runtime", "execution runtime, state transition, or gas behavior", "执行 runtime、状态转换或 gas 行为"),
        ("interpreter", "VM interpreter and opcode semantics", "VM 解释器和 opcode 语义"),
        ("opcode", "opcode metadata, pricing, or canonical decode rules", "opcode 元数据、定价或标准解码规则"),
        ("syscall", "host syscall contract and dispatch boundary", "宿主 syscall 契约与分发边界"),
        ("host", "host-side orchestration and native integration", "host 侧编排与原生集成"),
        ("guest", "guest-side no_std facade or proof/runtime entry", "guest 侧 no_std 外观或证明/runtime 入口"),
        ("prover", "proof generation logic and proof envelope construction", "证明生成逻辑和证明封装"),
        ("verifier", "proof verification and public output checking", "证明验证和公开输出检查"),
        ("assembler", "developer assembly and script construction", "开发者汇编和脚本构造"),
        ("disassembler", "script inspection and opcode decoding", "脚本检查和 opcode 解码"),
        ("bridge", "bridge message, relay, or cross-chain boundary logic", "桥消息、relay 或跨链边界逻辑"),
        ("watcher", "source-chain event scanner and relay job creation", "源链事件扫描与 relay 任务创建"),
        ("client", "client-facing API wrapper", "面向客户端的 API 包装"),
        ("proof", "proof object, layout, and verification evidence", "证明对象、布局和验证证据"),
    ]
    for token, en, zh in hints:
        if token in lowered:
            role_en, role_zh = en, zh
            break
    return role_zh if lang == "zh" else role_en


def guide_for(crate: CrateInfo) -> Guide:
    name = crate.name

    if name == "neo-vm-rs":
        return guide(
            "Shared VM core",
            "共享虚拟机核心",
            "Canonical Rust implementation of NeoVM 3.9.x semantics shared by RISC-V and zkVM paths.",
            "NeoVM 3.9.x 语义的 Rust 共享核心，供 RISC-V 与 zkVM 路径复用。",
            ["NeoVM bytecode", "initial stack", "syscall host callbacks"],
            ["NeoVM 字节码", "初始栈", "系统调用宿主回调"],
            ["Decode canonical opcodes", "Execute stack and state semantics", "Expose reusable runtime APIs"],
            ["解码标准操作码", "执行栈与状态语义", "暴露可复用运行时 API"],
            ["halt/fault result", "final stack", "gas/accounting evidence"],
            ["halt/fault 结果", "最终栈", "gas/计费证据"],
            ["neo-riscv-vm", "neo-zkvm", "Neo N4 execution core"],
            ["neo-riscv-vm", "neo-zkvm", "Neo N4 执行核心"],
            ["Load script", "Decode OpCode", "Execute semantics", "Invoke host syscall", "Return VM result"],
            ["加载脚本", "解码 OpCode", "执行语义", "调用宿主 syscall", "返回 VM 结果"],
            ["bytecode + stack", "shared runtime", "state transition", "execution evidence"],
            ["字节码 + 栈", "共享运行时", "状态转换", "执行证据"],
        )

    if name == "neo-execution-core":
        return guide(
            "N4 batch execution core",
            "N4 批处理执行核心",
            "Backend-neutral L2 batch transition primitives shared by fast execution and proof generation.",
            "后端无关的 L2 批处理状态转换核心，供快速执行和证明生成复用。",
            ["L2 batch", "previous state root", "execution parameters"],
            ["L2 批次", "前序状态根", "执行参数"],
            ["Validate batch shape", "Apply deterministic transition", "Commit new state root"],
            ["校验批次结构", "执行确定性状态转换", "提交新状态根"],
            ["execution trace", "new state root", "public proof inputs"],
            ["执行轨迹", "新状态根", "公开证明输入"],
            ["neo-zkvm-guest", "neo-zkvm-host", "gateway services"],
            ["neo-zkvm-guest", "neo-zkvm-host", "网关服务"],
            ["Accept batch", "Normalize inputs", "Run transition", "Hash outputs", "Publish evidence"],
            ["接收批次", "规范化输入", "运行状态转换", "哈希输出", "发布证据"],
            ["transactions", "execution core", "trace + commitments", "proof/public output"],
            ["交易", "执行核心", "轨迹 + 承诺", "证明/公开输出"],
        )

    if name == "neo-zkvm-guest":
        return guide(
            "N4 zk guest",
            "N4 零知识 guest",
            "SP1 guest program that runs deterministic Neo L2 batch execution inside the proof circuit.",
            "在 SP1 证明环境中运行确定性 Neo L2 批处理执行的 guest 程序。",
            ["public batch input", "private witness", "shared execution core"],
            ["公开批次输入", "私有见证", "共享执行核心"],
            ["Run verifiable transition", "Emit public values", "Reject nondeterminism"],
            ["运行可验证状态转换", "输出公开值", "拒绝非确定性行为"],
            ["SP1 public output", "state root", "execution digest"],
            ["SP1 公开输出", "状态根", "执行摘要"],
            ["neo-zkvm-host", "NativeZkVerifier adapter", "audit tooling"],
            ["neo-zkvm-host", "NativeZkVerifier 适配器", "审计工具"],
            ["Deserialize input", "Execute batch", "Commit public values", "Exit guest"],
            ["反序列化输入", "执行批次", "提交公开值", "退出 guest"],
            ["witness + batch", "guest executor", "public values", "proof artifact"],
            ["见证 + 批次", "guest 执行器", "公开值", "证明产物"],
        )

    if name == "neo-zkvm-host":
        return guide(
            "N4 zk host",
            "N4 零知识 host",
            "Host-side SP1 prover orchestration for creating and checking L2 batch proofs.",
            "负责创建和检查 L2 批次证明的 SP1 宿主编排层。",
            ["L2 batch", "guest ELF", "prover configuration"],
            ["L2 批次", "guest ELF", "prover 配置"],
            ["Prepare SP1 stdin", "Run prover", "Verify proof envelope"],
            ["准备 SP1 输入", "运行 prover", "校验证明封装"],
            ["proof bytes", "verification report", "state commitment"],
            ["证明字节", "验证报告", "状态承诺"],
            ["bridge relayer", "L1 verifier adapter", "devnet scripts"],
            ["桥接 relayer", "L1 verifier 适配器", "devnet 脚本"],
            ["Load ELF", "Encode input", "Prove", "Verify locally", "Export report"],
            ["加载 ELF", "编码输入", "生成证明", "本地验证", "导出报告"],
            ["batch data", "SP1 host", "proof + vk", "onchain verifier input"],
            ["批次数据", "SP1 host", "证明 + 验证键", "链上验证输入"],
        )

    if name.startswith("neo-bridge-watcher-"):
        chain = name.removeprefix("neo-bridge-watcher-").upper()
        return guide(
            "Cross-chain watcher",
            "跨链监听器",
            f"Observes {chain} bridge events and turns them into normalized Neo N4 relay messages.",
            f"监听 {chain} 桥事件，并转换为标准化 Neo N4 relay 消息。",
            [f"{chain} RPC/log stream", "bridge contract events", "checkpoint cursor"],
            [f"{chain} RPC/log 流", "桥合约事件", "检查点游标"],
            ["Filter bridge events", "Normalize payloads", "Protect replay/cursor state"],
            ["过滤桥事件", "规范化 payload", "保护重放与游标状态"],
            ["relay job", "audit log", "health metric"],
            ["relay 任务", "审计日志", "健康指标"],
            ["gateway", "shared bridge", "operator dashboard"],
            ["网关", "共享桥", "运维面板"],
            ["Poll source chain", "Decode logs", "Validate confirmations", "Emit relay job", "Persist cursor"],
            ["轮询源链", "解码日志", "校验确认数", "发出 relay 任务", "持久化游标"],
            ["source log", "watcher", "normalized event", "bridge message"],
            ["源链日志", "监听器", "标准事件", "桥消息"],
        )

    if name == "neo-n4-sdk":
        return guide(
            "Developer SDK",
            "开发者 SDK",
            "Rust client SDK for building tools and services that talk to Neo N4 APIs.",
            "用于构建访问 Neo N4 API 的工具和服务的 Rust SDK。",
            ["developer app", "gateway endpoint", "wallet/config"],
            ["开发者应用", "网关端点", "钱包/配置"],
            ["Encode API requests", "Handle bridge/proof models", "Return typed results"],
            ["编码 API 请求", "处理桥/证明模型", "返回强类型结果"],
            ["typed client result", "transaction request", "query response"],
            ["强类型客户端结果", "交易请求", "查询响应"],
            ["apps", "operators", "integration tests"],
            ["应用", "运维工具", "集成测试"],
            ["Create client", "Build request", "Sign or query", "Submit to gateway", "Decode response"],
            ["创建客户端", "构造请求", "签名或查询", "提交网关", "解码响应"],
            ["app intent", "SDK model", "RPC payload", "Neo N4 service response"],
            ["应用意图", "SDK 模型", "RPC payload", "Neo N4 服务响应"],
        )

    if name == "neo-external-bridge-router":
        return guide(
            "Foreign-chain bridge program",
            "异构链桥程序",
            "Solana-side bridge router that represents Neo N4 cross-chain lock, mint, burn, and unlock flows.",
            "Solana 侧桥路由程序，承载 Neo N4 跨链 lock/mint/burn/unlock 流程。",
            ["Solana instruction", "token account state", "bridge authority"],
            ["Solana 指令", "代币账户状态", "桥权限账户"],
            ["Validate route", "Move escrowed assets", "Emit bridge event"],
            ["校验路由", "移动托管资产", "发出桥事件"],
            ["bridge event", "escrow mutation", "relay evidence"],
            ["桥事件", "托管状态变化", "relay 证据"],
            ["watcher-sol", "gateway", "shared bridge"],
            ["watcher-sol", "网关", "共享桥"],
            ["Receive instruction", "Check accounts", "Update escrow", "Emit event", "Watcher relays"],
            ["接收指令", "检查账户", "更新托管", "发出事件", "监听器 relay"],
            ["instruction data", "router program", "event log", "Neo N4 bridge message"],
            ["指令数据", "路由程序", "事件日志", "Neo N4 桥消息"],
        )

    if name.startswith("neo-zkvm"):
        return guide_for_zkvm(crate)

    if name.startswith("neo-riscv") or name in {"counter", "hello-world", "nep17-token", "storage", "devpack-test", "neo-contract-template"}:
        return guide_for_riscv(crate)

    if name == "neo-vm-guest":
        return guide(
            "zkVM guest facade",
            "zkVM guest 外观层",
            "Guest-facing adapter that exposes shared NeoVM execution APIs in zkVM-compatible form.",
            "面向 zkVM guest 的适配层，以 zkVM 兼容方式暴露共享 NeoVM 执行 API。",
            ["guest bytecode", "stack input", "shared VM crate"],
            ["guest 字节码", "栈输入", "共享 VM crate"],
            ["Call shared VM", "Keep guest ABI small", "Return deterministic result"],
            ["调用共享 VM", "保持 guest ABI 小", "返回确定性结果"],
            ["guest execution result", "public output seed", "fault reason"],
            ["guest 执行结果", "公开输出种子", "fault 原因"],
            ["neo-zkvm-program", "neo-zkvm-prover", "examples"],
            ["neo-zkvm-program", "neo-zkvm-prover", "示例"],
            ["Receive guest input", "Invoke shared VM", "Collect output", "Commit result"],
            ["接收 guest 输入", "调用共享 VM", "收集输出", "提交结果"],
            ["script + args", "guest facade", "VM result", "proof public values"],
            ["脚本 + 参数", "guest 外观层", "VM 结果", "证明公开值"],
        )

    return fallback_guide(crate)


def guide_for_zkvm(crate: CrateInfo) -> Guide:
    roles = {
        "neo-zkvm-cli": (
            "CLI and developer tooling for assembling, inspecting, proving, and verifying Neo zkVM programs.",
            "用于汇编、检查、生成证明和验证 Neo zkVM 程序的 CLI 与开发工具。",
            ["CLI command", "script/proof files", "prover options"],
            ["CLI 命令", "脚本/证明文件", "prover 选项"],
            ["Parse commands", "Use shared opcode metadata", "Run prove/verify workflows"],
            ["解析命令", "使用共享 opcode 元数据", "运行 prove/verify 工作流"],
            ["assembled script", "proof report", "inspection output"],
            ["汇编脚本", "证明报告", "检查输出"],
        ),
        "neo-zkvm-prover": (
            "Proof generation library that turns NeoVM execution inputs into verifiable proof artifacts.",
            "将 NeoVM 执行输入转换为可验证证明产物的证明生成库。",
            ["proof input", "guest program", "prover mode"],
            ["证明输入", "guest 程序", "prover 模式"],
            ["Hash inputs", "Run guest execution", "Build proof envelope"],
            ["哈希输入", "运行 guest 执行", "构建证明封装"],
            ["NeoProof", "public output", "prover report"],
            ["NeoProof", "公开输出", "prover 报告"],
        ),
        "neo-zkvm-verifier": (
            "Verifier library that checks proof envelopes, public outputs, and mode compatibility.",
            "校验证明封装、公开输出和模式兼容性的 verifier 库。",
            ["proof envelope", "verification key", "expected public output"],
            ["证明封装", "验证键", "期望公开输出"],
            ["Check proof format", "Verify public values", "Report validity"],
            ["检查证明格式", "验证公开值", "报告有效性"],
            ["verification result", "error reason", "audit evidence"],
            ["验证结果", "错误原因", "审计证据"],
        ),
        "neo-zkvm-program": (
            "SP1 guest binary entrypoint that binds proof inputs to deterministic NeoVM execution.",
            "SP1 guest 二进制入口，将证明输入绑定到确定性 NeoVM 执行。",
            ["SP1 stdin", "Neo proof input", "guest facade"],
            ["SP1 stdin", "Neo 证明输入", "guest 外观层"],
            ["Deserialize stdin", "Execute script", "Commit public values"],
            ["反序列化 stdin", "执行脚本", "提交公开值"],
            ["SP1 public values", "execution output", "fault status"],
            ["SP1 公开值", "执行输出", "fault 状态"],
        ),
        "neo-zkvm-examples": (
            "Runnable examples that demonstrate common proof flows and application patterns.",
            "展示常见证明流程和应用模式的可运行示例。",
            ["sample script", "example input", "local prover"],
            ["示例脚本", "示例输入", "本地 prover"],
            ["Demonstrate APIs", "Exercise edge cases", "Document expected outputs"],
            ["演示 API", "覆盖边界案例", "记录期望输出"],
            ["example proof", "tutorial output", "regression sample"],
            ["示例证明", "教程输出", "回归样本"],
        ),
        "neo-zkvm-fuzz": (
            "Fuzzing workspace for adversarial proof and VM input exploration.",
            "用于对证明与 VM 输入做对抗性探索的 fuzz 工作区。",
            ["random bytecode", "mutated proof", "seed corpus"],
            ["随机字节码", "变异证明", "种子语料"],
            ["Generate inputs", "Run no-panic checks", "Capture regressions"],
            ["生成输入", "运行 no-panic 检查", "捕获回归"],
            ["crash corpus", "regression case", "coverage signal"],
            ["崩溃语料", "回归案例", "覆盖率信号"],
        ),
    }
    role = roles.get(crate.name)
    if role is None:
        return fallback_guide(crate)
    role_en, role_zh, inputs_en, inputs_zh, resp_en, resp_zh, outputs_en, outputs_zh = role
    return guide(
        "Neo zkVM stack",
        "Neo zkVM 栈",
        role_en,
        role_zh,
        inputs_en,
        inputs_zh,
        resp_en,
        resp_zh,
        outputs_en,
        outputs_zh,
        ["zkVM users", "L2 prover service", "L1 verification adapter"],
        ["zkVM 用户", "L2 prover 服务", "L1 验证适配器"],
        ["Prepare input", "Run guest/host logic", "Generate or check proof", "Record evidence"],
        ["准备输入", "运行 guest/host 逻辑", "生成或校验证明", "记录证据"],
        ["execution input", crate.name, "proof/evidence artifact", "Neo N4 verification flow"],
        ["执行输入", crate.name, "证明/证据产物", "Neo N4 验证流程"],
    )


def guide_for_riscv(crate: CrateInfo) -> Guide:
    roles = {
        "neo-riscv-abi": (
            "Shared ABI, stack values, codec tags, and opcode metadata re-exports for RISC-V execution.",
            "RISC-V 执行路径共享的 ABI、栈值、codec tag 与 opcode 元数据重导出。",
            ["shared VM types", "host/guest boundary", "serialized stack values"],
            ["共享 VM 类型", "host/guest 边界", "序列化栈值"],
            ["Define stable ABI", "Re-export shared metadata", "Encode/decode stack values"],
            ["定义稳定 ABI", "重导出共享元数据", "编码/解码栈值"],
            ["ABI types", "codec helpers", "runtime constants"],
            ["ABI 类型", "codec helper", "运行时常量"],
        ),
        "neo-riscv-guest": (
            "Guest-side facade and contract runtime glue for NeoVM2/RISC-V contracts.",
            "NeoVM2/RISC-V 合约的 guest 侧外观层与合约运行时胶水。",
            ["contract bytecode", "ABI stack", "syscall stubs"],
            ["合约字节码", "ABI 栈", "syscall stub"],
            ["Call shared VM runtime", "Expose no_std contract APIs", "Bridge syscalls"],
            ["调用共享 VM runtime", "暴露 no_std 合约 API", "桥接 syscall"],
            ["guest result", "syscall request", "stack mutation"],
            ["guest 结果", "syscall 请求", "栈变化"],
        ),
        "neo-riscv-guest-module": (
            "PolkaVM guest module entrypoint that packages the guest runtime into executable RISC-V code.",
            "PolkaVM guest 模块入口，将 guest runtime 打包成可执行 RISC-V 代码。",
            ["PolkaVM imports", "guest runtime", "encoded execution input"],
            ["PolkaVM import", "guest runtime", "编码执行输入"],
            ["Expose exports", "Call guest runtime", "Return encoded output"],
            ["暴露 export", "调用 guest runtime", "返回编码输出"],
            ["guest.polkavm", "encoded output", "host callback calls"],
            ["guest.polkavm", "编码输出", "host callback 调用"],
        ),
        "neo-riscv-host": (
            "Host runtime that executes PolkaVM guest modules, accounts gas, and bridges syscalls.",
            "执行 PolkaVM guest 模块、计费 gas 并桥接 syscall 的 host runtime。",
            ["PolkaVM module", "execution context", "host syscall provider"],
            ["PolkaVM 模块", "执行上下文", "宿主 syscall provider"],
            ["Instantiate module", "Charge opcodes", "Marshal stack values", "Return VM result"],
            ["实例化模块", "按 opcode 计费", "编组栈值", "返回 VM 结果"],
            ["execution result", "gas report", "host trace"],
            ["执行结果", "gas 报告", "host 轨迹"],
        ),
        "neo-riscv-devpack": (
            "Developer packaging utilities for compiling and preparing RISC-V Neo contracts.",
            "用于编译和准备 RISC-V Neo 合约的开发者打包工具。",
            ["contract source", "template config", "toolchain settings"],
            ["合约源码", "模板配置", "工具链设置"],
            ["Build artifacts", "Validate metadata", "Package deployment files"],
            ["构建产物", "校验元数据", "打包部署文件"],
            ["contract package", "manifest", "developer diagnostics"],
            ["合约包", "manifest", "开发诊断"],
        ),
        "neo-riscv-contract-harness": (
            "Test harness for contract-level RISC-V execution and syscall simulation.",
            "用于合约级 RISC-V 执行和 syscall 模拟的测试 harness。",
            ["contract module", "mock context", "expected stack"],
            ["合约模块", "mock 上下文", "期望栈"],
            ["Initialize test context", "Run contract export", "Assert state/stack"],
            ["初始化测试上下文", "运行合约 export", "断言状态/栈"],
            ["test result", "trace", "failure diagnostics"],
            ["测试结果", "轨迹", "失败诊断"],
        ),
        "neo-riscv-fuzz": (
            "Fuzzing support for RISC-V VM execution, ABI codecs, and host/guest boundaries.",
            "面向 RISC-V VM 执行、ABI codec、host/guest 边界的 fuzz 支撑。",
            ["seed corpus", "generated opcodes", "mutated stack values"],
            ["种子语料", "生成 opcode", "变异栈值"],
            ["Generate valid scripts", "Exercise codecs", "Find host/guest mismatches"],
            ["生成有效脚本", "覆盖 codec", "发现 host/guest 不一致"],
            ["regression seed", "crash case", "coverage signal"],
            ["回归 seed", "崩溃案例", "覆盖率信号"],
        ),
    }
    examples = {
        "counter": "Minimal state-changing counter contract example.",
        "hello-world": "Small hello-world contract example for the RISC-V toolchain.",
        "nep17-token": "NEP-17 style token contract example for RISC-V execution.",
        "storage": "Storage-focused contract example for host syscall behavior.",
        "devpack-test": "Development pack smoke-test contract.",
        "neo-contract-template": "Starter template for new Neo RISC-V contracts.",
    }
    examples_zh = {
        "counter": "最小状态变更 counter 合约示例。",
        "hello-world": "用于 RISC-V 工具链的 hello-world 合约示例。",
        "nep17-token": "用于 RISC-V 执行的 NEP-17 风格代币合约示例。",
        "storage": "聚焦存储 syscall 行为的合约示例。",
        "devpack-test": "开发包冒烟测试合约。",
        "neo-contract-template": "创建新 Neo RISC-V 合约的起始模板。",
    }
    if crate.name in roles:
        role_en, role_zh, inputs_en, inputs_zh, resp_en, resp_zh, outputs_en, outputs_zh = roles[crate.name]
    elif crate.name in examples:
        role_en = examples[crate.name]
        role_zh = examples_zh[crate.name]
        inputs_en = ["developer source", "template runtime", "test context"]
        inputs_zh = ["开发者源码", "模板 runtime", "测试上下文"]
        resp_en = ["Demonstrate contract pattern", "Exercise tooling", "Provide learning fixture"]
        resp_zh = ["演示合约模式", "覆盖工具链", "提供学习 fixture"]
        outputs_en = ["compiled contract", "example result", "tutorial artifact"]
        outputs_zh = ["编译后合约", "示例结果", "教程产物"]
    else:
        return fallback_guide(crate)
    return guide(
        "NeoVM2 / RISC-V execution profile",
        "NeoVM2 / RISC-V 执行 profile",
        role_en,
        role_zh,
        inputs_en,
        inputs_zh,
        resp_en,
        resp_zh,
        outputs_en,
        outputs_zh,
        ["RISC-V host", "Neo N4 L2 node", "developer tooling"],
        ["RISC-V host", "Neo N4 L2 节点", "开发者工具"],
        ["Prepare contract/input", "Encode ABI", "Execute in PolkaVM path", "Collect result", "Validate evidence"],
        ["准备合约/输入", "编码 ABI", "在 PolkaVM 路径执行", "收集结果", "验证证据"],
        ["contract input", crate.name, "host/guest boundary", "Neo N4 state transition"],
        ["合约输入", crate.name, "host/guest 边界", "Neo N4 状态转换"],
    )


def fallback_guide(crate: CrateInfo) -> Guide:
    deps = list(crate.dependencies[:4]) or ["workspace APIs"]
    deps_zh = ["工作区 API" if dep == "workspace APIs" else dep for dep in deps]
    return guide(
        "Neo N4 support crate",
        "Neo N4 支撑 crate",
        crate.description or f"Support crate `{crate.name}` in the Neo N4 workspace.",
        crate.description or f"Neo N4 工作区中的支撑 crate `{crate.name}`。",
        deps,
        deps_zh,
        ["Provide focused APIs", "Keep boundaries testable", "Integrate with the N4 stack"],
        ["提供聚焦 API", "保持边界可测试", "集成到 N4 栈"],
        ["typed API", "runtime artifact", "testable behavior"],
        ["强类型 API", "运行时产物", "可测试行为"],
        ["Neo N4 services", "tests", "developers"],
        ["Neo N4 服务", "测试", "开发者"],
        ["Receive input", "Validate assumptions", "Run crate logic", "Return typed output"],
        ["接收输入", "校验假设", "运行 crate 逻辑", "返回强类型输出"],
        ["input model", crate.name, "domain logic", "output model"],
        ["输入模型", crate.name, "领域逻辑", "输出模型"],
    )


def guide(
    layer_en: str,
    layer_zh: str,
    role_en: str,
    role_zh: str,
    inputs_en: Iterable[str],
    inputs_zh: Iterable[str],
    responsibilities_en: Iterable[str],
    responsibilities_zh: Iterable[str],
    outputs_en: Iterable[str],
    outputs_zh: Iterable[str],
    consumers_en: Iterable[str],
    consumers_zh: Iterable[str],
    workflow_en: Iterable[str],
    workflow_zh: Iterable[str],
    dataflow_en: Iterable[str],
    dataflow_zh: Iterable[str],
) -> Guide:
    return Guide(
        layer_en,
        layer_zh,
        role_en,
        role_zh,
        tuple(inputs_en),
        tuple(inputs_zh),
        tuple(responsibilities_en),
        tuple(responsibilities_zh),
        tuple(outputs_en),
        tuple(outputs_zh),
        tuple(consumers_en),
        tuple(consumers_zh),
        tuple(workflow_en),
        tuple(workflow_zh),
        tuple(dataflow_en),
        tuple(dataflow_zh),
    )


def write_crate_assets(crate: CrateInfo, guide: Guide, profile: SourceProfile) -> None:
    figures = crate.path / "docs" / "figures"
    figures.mkdir(parents=True, exist_ok=True)

    diagrams = {
        "position": (
            position_nodes(crate, guide, "en"),
            position_nodes(crate, guide, "zh"),
        ),
        "principles": (
            principles_nodes(crate, guide, "en"),
            principles_nodes(crate, guide, "zh"),
        ),
        "architecture": (
            architecture_nodes(crate, guide, "en"),
            architecture_nodes(crate, guide, "zh"),
        ),
        "workflow": (
            workflow_nodes(crate.name, guide, "en"),
            workflow_nodes(crate.name, guide, "zh"),
        ),
        "dataflow": (
            dataflow_nodes(crate.name, guide, "en"),
            dataflow_nodes(crate.name, guide, "zh"),
        ),
        "module-map": (
            module_nodes(crate.name, profile, "en"),
            module_nodes(crate.name, profile, "zh"),
        ),
        "api-surface": (
            api_nodes(crate.name, profile, "en"),
            api_nodes(crate.name, profile, "zh"),
        ),
        "test-map": (
            test_nodes(crate.name, profile, "en"),
            test_nodes(crate.name, profile, "zh"),
        ),
        "dependency-map": (
            dependency_nodes(crate.name, profile, "en"),
            dependency_nodes(crate.name, profile, "zh"),
        ),
    }

    for diagram, (nodes_en, nodes_zh) in diagrams.items():
        write_svg(figures / f"{diagram}.svg", crate.name, diagram, nodes_en, "en")
        write_svg(figures / f"{diagram}.zh.svg", crate.name, diagram, nodes_zh, "zh")
        write_mermaid(figures / f"{diagram}.mmd", crate.name, diagram, nodes_en)
        write_mermaid(figures / f"{diagram}.zh.mmd", crate.name, diagram, nodes_zh)

    write_learning_guides(crate, guide, profile)
    write_readme(crate, guide, profile, "en")
    write_readme(crate, guide, profile, "zh")


def position_nodes(crate: CrateInfo, guide: Guide, lang: str) -> list[tuple[str, str]]:
    deps = short_dependencies(crate, lang)
    if lang == "zh":
        return [
            ("Neo N4 层级", guide.layer_zh),
            ("上游输入", join_items(guide.inputs_zh)),
            (crate.name, guide.role_zh),
            ("直接依赖", join_items(deps)),
            ("下游使用者", join_items(guide.consumers_zh)),
            ("学习重点", "先定位上下游，再阅读本 crate 的边界、核心数据结构和测试。"),
        ]
    return [
        ("Neo N4 layer", guide.layer_en),
        ("Upstream inputs", join_items(guide.inputs_en)),
        (crate.name, guide.role_en),
        ("Direct dependencies", join_items(deps)),
        ("Downstream users", join_items(guide.consumers_en)),
        ("Learning focus", "Locate upstream and downstream first, then read this crate boundary, core data structures, and tests."),
    ]


def principles_nodes(crate: CrateInfo, guide: Guide, lang: str) -> list[tuple[str, str]]:
    if lang == "zh":
        return [
            ("职责边界", guide.role_zh),
            ("核心原则", join_items(guide.responsibilities_zh)),
            ("输入契约", join_items(guide.inputs_zh)),
            ("状态与产物", join_items(guide.outputs_zh)),
            ("集成方式", join_items(guide.consumers_zh)),
            ("维护规则", "保持边界小、语义确定、测试可复现。"),
        ]
    return [
        ("Responsibility boundary", guide.role_en),
        ("Core principles", join_items(guide.responsibilities_en)),
        ("Input contract", join_items(guide.inputs_en)),
        ("State and artifacts", join_items(guide.outputs_en)),
        ("Integration contract", join_items(guide.consumers_en)),
        ("Maintenance rule", "Keep the boundary small, deterministic, and reproducible under tests."),
    ]


def architecture_nodes(crate: CrateInfo, guide: Guide, lang: str) -> list[tuple[str, str]]:
    deps = short_dependencies(crate, lang)
    crate_name = crate.name
    if lang == "zh":
        return [
            ("公开入口", join_items(guide.inputs_zh)),
            (crate_name, guide.role_zh),
            ("内部组件", join_items(guide.responsibilities_zh)),
            ("依赖边界", join_items(deps)),
            ("输出产物", join_items(guide.outputs_zh)),
            ("系统位置", f"{guide.layer_zh} -> {join_items(guide.consumers_zh)}"),
        ]
    return [
        ("Public entrypoints", join_items(guide.inputs_en)),
        (crate_name, guide.role_en),
        ("Internal components", join_items(guide.responsibilities_en)),
        ("Dependency boundary", join_items(deps)),
        ("Output artifacts", join_items(guide.outputs_en)),
        ("System position", f"{guide.layer_en} -> {join_items(guide.consumers_en)}"),
    ]


def workflow_nodes(crate_name: str, guide: Guide, lang: str) -> list[tuple[str, str]]:
    steps = guide.workflow_zh if lang == "zh" else guide.workflow_en
    return [(f"Step {idx}", step) for idx, step in enumerate(steps, start=1)]


def dataflow_nodes(crate_name: str, guide: Guide, lang: str) -> list[tuple[str, str]]:
    steps = guide.dataflow_zh if lang == "zh" else guide.dataflow_en
    return [(f"Data {idx}", step) for idx, step in enumerate(steps, start=1)]


def module_nodes(crate_name: str, profile: SourceProfile, lang: str) -> list[tuple[str, str]]:
    top_files = rank_files(profile)[:5]
    if lang == "zh":
        nodes = [(crate_name, f"源码文件 {len(profile.files)} 个，公开符号 {len(profile.public_symbols)} 个")]
        nodes.extend((file.path, file.role_zh) for file in top_files)
        if len(profile.files) > len(top_files):
            nodes.append(("其他源码", f"还有 {len(profile.files) - len(top_files)} 个文件在详细学习指南中列出"))
        return nodes[:6]
    nodes = [(crate_name, f"{len(profile.files)} source files, {len(profile.public_symbols)} public symbols")]
    nodes.extend((file.path, file.role_en) for file in top_files)
    if len(profile.files) > len(top_files):
        nodes.append(("Other source", f"{len(profile.files) - len(top_files)} more files are listed in the detailed learning guide"))
    return nodes[:6]


def api_nodes(crate_name: str, profile: SourceProfile, lang: str) -> list[tuple[str, str]]:
    grouped = group_public_symbols(profile.public_symbols)
    if lang == "zh":
        return [
            (crate_name, "公开 API 面由源码扫描生成"),
            ("类型", summarize_symbols(grouped.get("type", ()), "zh")),
            ("函数", summarize_symbols(grouped.get("fn", ()), "zh")),
            ("Trait", summarize_symbols(grouped.get("trait", ()), "zh")),
            ("常量", summarize_symbols(grouped.get("const", ()), "zh")),
            ("详细列表", "见 docs/learning-guide.zh.md 的 API 表"),
        ]
    return [
        (crate_name, "public API surface generated from source scan"),
        ("Types", summarize_symbols(grouped.get("type", ()), "en")),
        ("Functions", summarize_symbols(grouped.get("fn", ()), "en")),
        ("Traits", summarize_symbols(grouped.get("trait", ()), "en")),
        ("Constants", summarize_symbols(grouped.get("const", ()), "en")),
        ("Detailed list", "see docs/learning-guide.md API table"),
    ]


def test_nodes(crate_name: str, profile: SourceProfile, lang: str) -> list[tuple[str, str]]:
    test_files = [file for file in profile.files if file.tests or file.path.startswith("tests/")]
    top = sorted(test_files, key=lambda file: (len(file.tests), file.path), reverse=True)[:5]
    if lang == "zh":
        nodes = [(crate_name, f"测试函数 {len(profile.tests)} 个，测试文件 {len(test_files)} 个")]
        nodes.extend((file.path, summarize_tests(file.tests, "zh")) for file in top)
        if not top:
            nodes.append(("测试证据", "未扫描到 Rust #[test]；请查看 workspace 级测试或外部验证"))
        return nodes[:6]
    nodes = [(crate_name, f"{len(profile.tests)} test functions across {len(test_files)} test files")]
    nodes.extend((file.path, summarize_tests(file.tests, "en")) for file in top)
    if not top:
        nodes.append(("Test evidence", "No Rust #[test] scanned; check workspace-level or external verification"))
    return nodes[:6]


def dependency_nodes(crate_name: str, profile: SourceProfile, lang: str) -> list[tuple[str, str]]:
    runtime = [dep.name for dep in profile.dependencies if dep.kind == "runtime"]
    test = [dep.name for dep in profile.dependencies if dep.kind == "test"]
    build = [dep.name for dep in profile.dependencies if dep.kind == "build"]
    internal = [dep.name for dep in profile.dependencies if dep.name.startswith(("neo-", "r3e", "n4"))]
    if lang == "zh":
        return [
            (crate_name, "Cargo.toml 依赖边界"),
            ("运行时依赖", summarize_list(runtime, "无运行时依赖")),
            ("测试依赖", summarize_list(test, "无测试依赖")),
            ("构建依赖", summarize_list(build, "无构建依赖")),
            ("Neo 内部依赖", summarize_list(internal, "无内部 crate 依赖")),
            ("边界检查", "依赖越少，crate 越容易独立理解和测试"),
        ]
    return [
        (crate_name, "Cargo.toml dependency boundary"),
        ("Runtime deps", summarize_list(runtime, "no runtime deps")),
        ("Test deps", summarize_list(test, "no test deps")),
        ("Build deps", summarize_list(build, "no build deps")),
        ("Neo internal deps", summarize_list(internal, "no internal crate deps")),
        ("Boundary check", "fewer dependencies make the crate easier to understand and test"),
    ]


def rank_files(profile: SourceProfile) -> list[SourceFileInfo]:
    return sorted(
        profile.files,
        key=lambda file: (
            file.path != "src/lib.rs",
            file.path != "src/main.rs",
            -(len(file.public_symbols) * 3 + len(file.tests)),
            file.path,
        ),
    )


def group_public_symbols(symbols: Iterable[str]) -> dict[str, tuple[str, ...]]:
    grouped: dict[str, list[str]] = {"type": [], "fn": [], "trait": [], "const": []}
    for symbol in symbols:
        short = symbol.split(": ", 1)[-1]
        kind, _, name = short.partition(" ")
        if kind in {"struct", "enum", "type"}:
            grouped["type"].append(name)
        elif kind == "fn":
            grouped["fn"].append(name)
        elif kind == "trait":
            grouped["trait"].append(name)
        elif kind in {"const", "static"}:
            grouped["const"].append(name)
    return {key: tuple(values) for key, values in grouped.items()}


def summarize_symbols(symbols: Iterable[str], lang: str) -> str:
    values = list(dict.fromkeys(symbols))
    if not values:
        return "未扫描到公开符号" if lang == "zh" else "no public symbols scanned"
    suffix = f" +{len(values) - 4}" if len(values) > 4 else ""
    return " | ".join(values[:4]) + suffix


def summarize_tests(tests: Iterable[str], lang: str) -> str:
    values = list(tests)
    if not values:
        return "测试文件或外部验证入口" if lang == "zh" else "test file or external verification entry"
    suffix = f" +{len(values) - 4}" if len(values) > 4 else ""
    return " | ".join(values[:4]) + suffix


def summarize_list(values: Iterable[str], empty: str) -> str:
    items = list(dict.fromkeys(values))
    if not items:
        return empty
    suffix = f" | +{len(items) - 5}" if len(items) > 5 else ""
    return " | ".join(items[:5]) + suffix


def short_dependencies(crate: CrateInfo, lang: str) -> tuple[str, ...]:
    deps = tuple(dep for dep in crate.dependencies if not dep.startswith("pretty_assertions"))[:5]
    if deps:
        return deps
    return ("无直接 Cargo 依赖",) if lang == "zh" else ("no direct Cargo dependencies",)


def join_items(items: Iterable[str]) -> str:
    return " | ".join(items)


def write_svg(path: Path, crate_name: str, diagram: str, nodes: list[tuple[str, str]], lang: str) -> None:
    width = 1280
    height = 760
    margin_x = 70
    gap = 26
    box_w = 340
    box_h = 130
    title = f"{crate_name} {diagram.title()}"
    subtitle = "Neo N4 crate visual learning guide" if lang == "en" else "Neo N4 crate 可视化学习图"
    rows = [nodes[:3], nodes[3:]]
    y_positions = [155, 415]

    parts = [
        '<?xml version="1.0" encoding="UTF-8"?>',
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{html.escape(title)}">',
        "<defs>",
        '<linearGradient id="bg" x1="0" x2="1" y1="0" y2="1"><stop offset="0" stop-color="#071018"/><stop offset="1" stop-color="#102033"/></linearGradient>',
        '<marker id="arrow" markerWidth="12" markerHeight="12" refX="10" refY="6" orient="auto"><path d="M2,2 L10,6 L2,10 Z" fill="#38d978"/></marker>',
        "</defs>",
        '<rect x="0" y="0" width="1280" height="760" fill="url(#bg)"/>',
        '<rect x="34" y="34" width="1212" height="692" rx="18" fill="#0c1722" stroke="#1e3a4d" stroke-width="2"/>',
        f'<text x="70" y="82" fill="#f6fbff" font-size="30" font-family="Segoe UI, Arial, sans-serif" font-weight="700">{html.escape(title)}</text>',
        f'<text x="70" y="114" fill="#aebdcc" font-size="17" font-family="Segoe UI, Arial, sans-serif">{html.escape(subtitle)}</text>',
    ]

    node_centers: list[tuple[float, float]] = []
    for row_idx, row in enumerate(rows):
        row_width = len(row) * box_w + (len(row) - 1) * gap
        start_x = (width - row_width) / 2
        y = y_positions[row_idx]
        for col_idx, (label, body) in enumerate(row):
            x = start_x + col_idx * (box_w + gap)
            node_centers.append((x + box_w, y + box_h / 2))
            parts.extend(svg_box(x, y, box_w, box_h, label, body, col_idx + row_idx))
            if col_idx > 0:
                x1 = start_x + (col_idx - 1) * (box_w + gap) + box_w
                y1 = y + box_h / 2
                x2 = x - 8
                parts.append(
                    f'<line x1="{x1 + 8:.0f}" y1="{y1:.0f}" x2="{x2:.0f}" y2="{y1:.0f}" stroke="#38d978" stroke-width="2.5" marker-end="url(#arrow)" opacity="0.9"/>'
                )
    if len(rows[0]) and len(rows[1]):
        parts.append(
            '<path d="M640 300 C640 350 640 370 640 412" stroke="#52a6ff" stroke-width="2.5" fill="none" marker-end="url(#arrow)" opacity="0.85"/>'
        )

    parts.append(
        '<text x="70" y="704" fill="#6f8194" font-size="14" font-family="Segoe UI, Arial, sans-serif">Generated from Cargo package metadata and Neo N4 architecture roles. Edit the companion .mmd source or rerun tools/docs/generate_crate_visual_docs.py.</text>'
    )
    parts.append("</svg>")
    path.write_text("\n".join(parts) + "\n", encoding="utf-8")


def svg_box(x: float, y: float, w: int, h: int, label: str, body: str, idx: int) -> list[str]:
    stroke = ["#38d978", "#52a6ff", "#ffca3a", "#38d978", "#52a6ff", "#a855f7"][idx % 6]
    body_lines = wrap_text(body, 34)
    parts = [
        f'<rect x="{x:.0f}" y="{y:.0f}" width="{w}" height="{h}" rx="12" fill="#101f2d" stroke="{stroke}" stroke-width="2"/>',
        f'<text x="{x + 22:.0f}" y="{y + 34:.0f}" fill="{stroke}" font-size="18" font-family="Segoe UI, Arial, sans-serif" font-weight="700">{html.escape(label)}</text>',
    ]
    for line_idx, line in enumerate(body_lines[:4]):
        parts.append(
            f'<text x="{x + 22:.0f}" y="{y + 66 + line_idx * 22:.0f}" fill="#d9e6f2" font-size="15" font-family="Segoe UI, Arial, sans-serif">{html.escape(line)}</text>'
        )
    return parts


def wrap_text(value: str, width: int) -> list[str]:
    if re.search(r"[\u4e00-\u9fff]", value):
        return [value[i : i + 22] for i in range(0, len(value), 22)]
    lines: list[str] = []
    for chunk in value.split(" | "):
        lines.extend(textwrap.wrap(chunk, width=width) or [""])
    return lines


def write_mermaid(path: Path, crate_name: str, diagram: str, nodes: list[tuple[str, str]]) -> None:
    lines = ["flowchart LR"]
    ids = []
    focus_id = ""
    for idx, (label, body) in enumerate(nodes, start=1):
        node_id = f"N{idx}"
        ids.append(node_id)
        if not focus_id and (label == crate_name or crate_name in body):
            focus_id = node_id
        safe = f"{label}: {body}".replace('"', "'")
        lines.append(f'    {node_id}["{safe}"]')
    for left, right in zip(ids, ids[1:]):
        lines.append(f"    {left} --> {right}")
    lines.append(f'    classDef crate fill:#101f2d,stroke:#38d978,color:#f6fbff')
    lines.append(f"    class {focus_id or 'N' + str(max(1, (len(ids) + 1) // 2))} crate")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_readme(crate: CrateInfo, guide: Guide, profile: SourceProfile, lang: str) -> None:
    if lang == "zh":
        readme = crate.path / "README.zh.md"
        start, end = MARKER_ZH
        section = readme_section_zh(crate, guide, profile)
        title = f"# {crate.name}\n\n"
    else:
        readme = crate.path / "README.md"
        start, end = MARKER_EN
        section = readme_section_en(crate, guide, profile)
        title = f"# {crate.name}\n\n"

    existing = readme.read_text(encoding="utf-8") if readme.exists() else title
    updated = replace_marked_section(existing, start, end, section)
    readme.write_text(updated, encoding="utf-8")


def readme_section_en(crate: CrateInfo, guide: Guide, profile: SourceProfile) -> str:
    file_rows = readme_file_rows(profile, "en")
    api_rows = readme_api_rows(profile, "en")
    return f"""{MARKER_EN[0]}

## Crate Visual Learning Guide

These diagrams are local to this crate. They explain `{crate.name}` as an independent unit: where it sits in the Neo N4 stack, which boundary it owns, how its internal workflow runs, and how data moves through it.

For the full source-level explanation, read [docs/learning-guide.md](docs/learning-guide.md).

| View | Diagram | Source |
| --- | --- | --- |
| Position in Neo N4 | ![Position](docs/figures/position.svg) | [Mermaid](docs/figures/position.mmd) |
| Technical principles | ![Principles](docs/figures/principles.svg) | [Mermaid](docs/figures/principles.mmd) |
| Architecture | ![Architecture](docs/figures/architecture.svg) | [Mermaid](docs/figures/architecture.mmd) |
| Workflow | ![Workflow](docs/figures/workflow.svg) | [Mermaid](docs/figures/workflow.mmd) |
| Dataflow | ![Dataflow](docs/figures/dataflow.svg) | [Mermaid](docs/figures/dataflow.mmd) |
| Module map | ![Module map](docs/figures/module-map.svg) | [Mermaid](docs/figures/module-map.mmd) |
| Public API surface | ![Public API surface](docs/figures/api-surface.svg) | [Mermaid](docs/figures/api-surface.mmd) |
| Test evidence | ![Test evidence](docs/figures/test-map.svg) | [Mermaid](docs/figures/test-map.mmd) |
| Dependency map | ![Dependency map](docs/figures/dependency-map.svg) | [Mermaid](docs/figures/dependency-map.mmd) |

### Role in Neo N4

- **Layer:** {guide.layer_en}
- **Purpose:** {guide.role_en}
- **Primary inputs:** {', '.join(guide.inputs_en)}
- **Primary outputs:** {', '.join(guide.outputs_en)}
- **Downstream consumers:** {', '.join(guide.consumers_en)}
- **Source files scanned:** {len(profile.files)}
- **Public symbols scanned:** {len(profile.public_symbols)}
- **Rust tests scanned:** {len(profile.tests)}

### Boundary and Responsibilities

- **Owns:** {', '.join(guide.responsibilities_en)}
- **Consumes:** {', '.join(guide.inputs_en)}
- **Produces:** {', '.join(guide.outputs_en)}
- **Used by:** {', '.join(guide.consumers_en)}

### Source Map Snapshot

| File | Why it matters | Public API | Tests |
| --- | --- | ---: | ---: |
{file_rows}

### API Snapshot

| Kind | Representative symbols |
| --- | --- |
{api_rows}

### Learning Path

1. Start with the position diagram to understand why this crate exists and who calls it.
2. Read the technical principles diagram to identify the invariants and responsibility boundary.
3. Use the module map and API surface to identify the files and symbols to read first.
4. Follow the workflow, dataflow, test, and dependency diagrams before changing code.

{MARKER_EN[1]}
"""


def readme_section_zh(crate: CrateInfo, guide: Guide, profile: SourceProfile) -> str:
    file_rows = readme_file_rows(profile, "zh")
    api_rows = readme_api_rows(profile, "zh")
    return f"""{MARKER_ZH[0]}

## 可视化学习指南

这些图是 `{crate.name}` 自己目录下的 crate 专属学习资料，用来说明它在 Neo N4 中的位置、自己负责的技术边界、内部工作流，以及数据如何流经它。

完整的源码级解释见 [docs/learning-guide.zh.md](docs/learning-guide.zh.md)。

| 视图 | 图片 | 源文件 |
| --- | --- | --- |
| 在 Neo N4 中的位置 | ![位置](docs/figures/position.zh.svg) | [Mermaid](docs/figures/position.zh.mmd) |
| 技术原理 | ![技术原理](docs/figures/principles.zh.svg) | [Mermaid](docs/figures/principles.zh.mmd) |
| 架构 | ![架构](docs/figures/architecture.zh.svg) | [Mermaid](docs/figures/architecture.zh.mmd) |
| 工作流 | ![工作流](docs/figures/workflow.zh.svg) | [Mermaid](docs/figures/workflow.zh.mmd) |
| 数据流 | ![数据流](docs/figures/dataflow.zh.svg) | [Mermaid](docs/figures/dataflow.zh.mmd) |
| 模块图 | ![模块图](docs/figures/module-map.zh.svg) | [Mermaid](docs/figures/module-map.zh.mmd) |
| 公开 API 图 | ![公开 API 图](docs/figures/api-surface.zh.svg) | [Mermaid](docs/figures/api-surface.zh.mmd) |
| 测试证据图 | ![测试证据图](docs/figures/test-map.zh.svg) | [Mermaid](docs/figures/test-map.zh.mmd) |
| 依赖图 | ![依赖图](docs/figures/dependency-map.zh.svg) | [Mermaid](docs/figures/dependency-map.zh.mmd) |

### 在 Neo N4 中的作用

- **层级:** {guide.layer_zh}
- **目的:** {guide.role_zh}
- **主要输入:** {'、'.join(guide.inputs_zh)}
- **主要输出:** {'、'.join(guide.outputs_zh)}
- **下游使用者:** {'、'.join(guide.consumers_zh)}
- **扫描到的源码文件:** {len(profile.files)}
- **扫描到的公开符号:** {len(profile.public_symbols)}
- **扫描到的 Rust 测试:** {len(profile.tests)}

### 边界与职责

- **本 crate 负责:** {'、'.join(guide.responsibilities_zh)}
- **本 crate 消费:** {'、'.join(guide.inputs_zh)}
- **本 crate 产出:** {'、'.join(guide.outputs_zh)}
- **主要被谁使用:** {'、'.join(guide.consumers_zh)}

### 源码地图快照

| 文件 | 为什么重要 | 公开 API | 测试 |
| --- | --- | ---: | ---: |
{file_rows}

### API 快照

| 类型 | 代表符号 |
| --- | --- |
{api_rows}

### 学习路径

1. 先看位置图，明确这个 crate 为什么存在、上游是谁、下游是谁。
2. 再看技术原理图，理解它的核心不变量、职责边界和维护规则。
3. 然后看模块图和 API 图，确定先读哪些文件、哪些符号。
4. 最后看工作流、数据流、测试证据图和依赖图，再进入源码会更容易理解。

{MARKER_ZH[1]}
"""


def readme_file_rows(profile: SourceProfile, lang: str) -> str:
    rows: list[str] = []
    for file in rank_files(profile)[:8]:
        role = file.role_zh if lang == "zh" else file.role_en
        rows.append(
            f"| `{file.path}` | {md_cell(role)} | {len(file.public_symbols)} | {len(file.tests)} |"
        )
    if not rows:
        empty = "未扫描到 Rust 源文件" if lang == "zh" else "No Rust source files scanned"
        rows.append(f"| - | {empty} | 0 | 0 |")
    return "\n".join(rows)


def readme_api_rows(profile: SourceProfile, lang: str) -> str:
    grouped = group_public_symbols(profile.public_symbols)
    labels = {
        "type": "类型" if lang == "zh" else "Types",
        "fn": "函数" if lang == "zh" else "Functions",
        "trait": "Trait",
        "const": "常量" if lang == "zh" else "Constants",
    }
    return "\n".join(
        f"| {labels[key]} | {md_cell(summarize_symbols(grouped.get(key, ()), lang))} |"
        for key in ("type", "fn", "trait", "const")
    )


def write_learning_guides(crate: CrateInfo, guide: Guide, profile: SourceProfile) -> None:
    docs = crate.path / "docs"
    docs.mkdir(parents=True, exist_ok=True)
    (docs / "learning-guide.md").write_text(
        learning_guide_en(crate, guide, profile),
        encoding="utf-8",
    )
    (docs / "learning-guide.zh.md").write_text(
        learning_guide_zh(crate, guide, profile),
        encoding="utf-8",
    )


def learning_guide_en(crate: CrateInfo, guide: Guide, profile: SourceProfile) -> str:
    return f"""# {crate.name} Source-Level Learning Guide

This guide is generated from the crate's actual `Cargo.toml`, Rust source files, public symbols, and test functions. It is meant to help a reader understand what this crate owns before reading implementation details.

## What This Crate Is

| Topic | Detail |
| --- | --- |
| Layer | {guide.layer_en} |
| Purpose | {guide.role_en} |
| Inputs | {', '.join(guide.inputs_en)} |
| Responsibilities | {', '.join(guide.responsibilities_en)} |
| Outputs | {', '.join(guide.outputs_en)} |
| Consumers | {', '.join(guide.consumers_en)} |

## Visual Reading Order

| Step | Diagram | Use it to learn |
| ---: | --- | --- |
| 1 | [Position](figures/position.svg) | Why this crate exists and where it sits in Neo N4. |
| 2 | [Principles](figures/principles.svg) | The invariants and boundaries this crate must protect. |
| 3 | [Module map](figures/module-map.svg) | Which files are the best entry points. |
| 4 | [Public API surface](figures/api-surface.svg) | Which exported symbols form the crate contract. |
| 5 | [Architecture](figures/architecture.svg) | How inputs, internal components, dependencies, and outputs connect. |
| 6 | [Workflow](figures/workflow.svg) | The normal execution path. |
| 7 | [Dataflow](figures/dataflow.svg) | How data is transformed across the crate boundary. |
| 8 | [Test evidence](figures/test-map.svg) | Which tests protect the behavior. |
| 9 | [Dependency map](figures/dependency-map.svg) | Which dependencies are runtime, test, or build-only. |

## Source File Map

{source_file_table(profile, "en")}

## Public API Surface

{public_api_table(profile, "en")}

## Module and Re-Export Signals

{module_signal_table(profile, "en")}

## Test Evidence

{test_evidence_table(profile, "en")}

## Dependency Boundary

{dependency_table(profile, "en")}

## Suggested Reading Path

{reading_path(profile, "en")}

## Change Safety Checklist

- Keep the stated responsibility boundary intact: {', '.join(guide.responsibilities_en)}.
- Update the workflow and dataflow diagrams when adding or removing major execution steps.
- Add or update tests in the files listed under Test Evidence when public API or state-transition behavior changes.
- Re-run `python tools/docs/generate_crate_visual_docs.py` from the Neo N4 repository root after source layout changes.
"""


def learning_guide_zh(crate: CrateInfo, guide: Guide, profile: SourceProfile) -> str:
    return f"""# {crate.name} 源码级学习指南

这份文档从 crate 的真实 `Cargo.toml`、Rust 源码文件、公开符号和测试函数生成。目标是在读实现细节之前，先弄清楚这个 crate 自己负责什么、边界在哪里、应该从哪些文件开始读。

## 这个 Crate 是什么

| 主题 | 说明 |
| --- | --- |
| 层级 | {guide.layer_zh} |
| 目的 | {guide.role_zh} |
| 输入 | {'、'.join(guide.inputs_zh)} |
| 职责 | {'、'.join(guide.responsibilities_zh)} |
| 输出 | {'、'.join(guide.outputs_zh)} |
| 使用者 | {'、'.join(guide.consumers_zh)} |

## 可视化阅读顺序

| 步骤 | 图 | 用它学习什么 |
| ---: | --- | --- |
| 1 | [位置图](figures/position.zh.svg) | 这个 crate 为什么存在、在 Neo N4 中处于哪里。 |
| 2 | [技术原理图](figures/principles.zh.svg) | 这个 crate 必须保护的不变量和职责边界。 |
| 3 | [模块图](figures/module-map.zh.svg) | 哪些源码文件是最好的入口。 |
| 4 | [公开 API 图](figures/api-surface.zh.svg) | 哪些导出符号构成 crate 契约。 |
| 5 | [架构图](figures/architecture.zh.svg) | 输入、内部组件、依赖和输出如何连接。 |
| 6 | [工作流图](figures/workflow.zh.svg) | 正常执行路径。 |
| 7 | [数据流图](figures/dataflow.zh.svg) | 数据如何跨越 crate 边界并被转换。 |
| 8 | [测试证据图](figures/test-map.zh.svg) | 哪些测试保护行为。 |
| 9 | [依赖图](figures/dependency-map.zh.svg) | 哪些依赖是运行时、测试或构建期依赖。 |

## 源码文件地图

{source_file_table(profile, "zh")}

## 公开 API 面

{public_api_table(profile, "zh")}

## 模块与重导出信号

{module_signal_table(profile, "zh")}

## 测试证据

{test_evidence_table(profile, "zh")}

## 依赖边界

{dependency_table(profile, "zh")}

## 建议阅读路径

{reading_path(profile, "zh")}

## 修改安全清单

- 保持职责边界不变：{'、'.join(guide.responsibilities_zh)}。
- 增加或删除主要执行步骤时，同步更新工作流图和数据流图。
- 修改公开 API 或状态转换行为时，更新“测试证据”中对应的测试。
- 源码结构变化后，在 Neo N4 仓库根目录重新运行 `python tools/docs/generate_crate_visual_docs.py`。
"""


def source_file_table(profile: SourceProfile, lang: str) -> str:
    header = "| File | Role | Public symbols | Tests |\n| --- | --- | ---: | ---: |"
    if lang == "zh":
        header = "| 文件 | 作用 | 公开符号 | 测试 |\n| --- | --- | ---: | ---: |"
    rows = [
        f"| `{file.path}` | {md_cell(file.role_zh if lang == 'zh' else file.role_en)} | {len(file.public_symbols)} | {len(file.tests)} |"
        for file in rank_files(profile)
    ]
    return header + "\n" + ("\n".join(rows) if rows else "| - | No source files scanned | 0 | 0 |")


def public_api_table(profile: SourceProfile, lang: str) -> str:
    if not profile.public_symbols:
        return "No public Rust symbols were scanned." if lang == "en" else "未扫描到公开 Rust 符号。"
    header = "| Symbol | File |\n| --- | --- |" if lang == "en" else "| 符号 | 文件 |\n| --- | --- |"
    rows = []
    for item in profile.public_symbols:
        file, symbol = item.split(": ", 1)
        rows.append(f"| `{md_cell(symbol)}` | `{md_cell(file)}` |")
    return header + "\n" + "\n".join(rows)


def module_signal_table(profile: SourceProfile, lang: str) -> str:
    if not profile.module_declarations:
        return "No `mod` or `pub use` declarations were scanned." if lang == "en" else "未扫描到 `mod` 或 `pub use` 声明。"
    header = "| Signal |\n| --- |" if lang == "en" else "| 信号 |\n| --- |"
    rows = [f"| `{md_cell(item)}` |" for item in profile.module_declarations]
    return header + "\n" + "\n".join(rows)


def test_evidence_table(profile: SourceProfile, lang: str) -> str:
    if not profile.tests:
        return "No Rust `#[test]` functions were scanned in this crate." if lang == "en" else "这个 crate 中未扫描到 Rust `#[test]` 函数。"
    header = "| Test | File |\n| --- | --- |" if lang == "en" else "| 测试 | 文件 |\n| --- | --- |"
    rows = []
    for item in profile.tests:
        file, test = item.split(": ", 1)
        rows.append(f"| `{md_cell(test)}` | `{md_cell(file)}` |")
    return header + "\n" + "\n".join(rows)


def dependency_table(profile: SourceProfile, lang: str) -> str:
    if not profile.dependencies:
        return "No direct Cargo dependencies." if lang == "en" else "没有直接 Cargo 依赖。"
    header = "| Dependency | Kind |\n| --- | --- |" if lang == "en" else "| 依赖 | 类型 |\n| --- | --- |"
    labels = {"runtime": "运行时", "test": "测试", "build": "构建"}
    rows = [
        f"| `{md_cell(dep.name)}` | {md_cell(labels.get(dep.kind, dep.kind) if lang == 'zh' else dep.kind)} |"
        for dep in profile.dependencies
    ]
    return header + "\n" + "\n".join(rows)


def md_cell(value: str) -> str:
    return value.replace("\n", " ").replace("|", "<br>").strip()


def reading_path(profile: SourceProfile, lang: str) -> str:
    files = rank_files(profile)[:6]
    if not files:
        return "No source files were scanned." if lang == "en" else "未扫描到源码文件。"
    if lang == "zh":
        return "\n".join(
            f"{idx}. 读 `{file.path}`：{file.role_zh}。"
            for idx, file in enumerate(files, start=1)
        )
    return "\n".join(
        f"{idx}. Read `{file.path}`: {file.role_en}."
        for idx, file in enumerate(files, start=1)
    )


def replace_marked_section(existing: str, start: str, end: str, section: str) -> str:
    pattern = re.compile(re.escape(start) + r".*?" + re.escape(end), re.S)
    if pattern.search(existing):
        return pattern.sub(section.strip(), existing).rstrip() + "\n"
    separator = "\n\n" if existing.strip() else ""
    return existing.rstrip() + separator + section.strip() + "\n"


def write_index(rows_en: list[str], rows_zh: list[str]) -> None:
    docs = ROOT / "docs"
    zh_docs = ROOT / "docs" / "zh"
    docs.mkdir(parents=True, exist_ok=True)
    zh_docs.mkdir(parents=True, exist_ok=True)
    (docs / "crate-visual-guide.md").write_text(
        "# Neo N4 Crate Visual Guide\n\n"
        "This index links every Rust crate to its own local visual learning guide. "
        "Each crate directory contains position, principles, architecture, workflow, dataflow, module, API, test, and dependency diagrams in English and Chinese, plus a source-level learning guide.\n\n"
        "| Crate | Path | Layer | Purpose |\n"
        "| --- | --- | --- | --- |\n"
        + "\n".join(rows_en)
        + "\n",
        encoding="utf-8",
    )
    (zh_docs / "crate-visual-guide.md").write_text(
        "# Neo N4 Crate 可视化指南\n\n"
        "这个索引把每个 Rust crate 链接到它自己目录下的本地可视化学习文档。"
        "每个 crate 目录都包含位置、技术原理、架构、工作流、数据流、模块、API、测试、依赖九类中英文图，并包含源码级学习指南。\n\n"
        "| Crate | 路径 | 层级 | 目的 |\n"
        "| --- | --- | --- | --- |\n"
        + "\n".join(rows_zh)
        + "\n",
        encoding="utf-8",
    )

    manifest = {
        "generated_crates": len(rows_en),
        "diagrams_per_crate": [
            "position",
            "principles",
            "architecture",
            "workflow",
            "dataflow",
            "module-map",
            "api-surface",
            "test-map",
            "dependency-map",
        ],
        "deep_guides_per_crate": ["docs/learning-guide.md", "docs/learning-guide.zh.md"],
        "english_index": "docs/crate-visual-guide.md",
        "chinese_index": "docs/zh/crate-visual-guide.md",
    }
    (docs / "crate-visual-guide.manifest.json").write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )


if __name__ == "__main__":
    main()
