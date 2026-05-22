#!/usr/bin/env python3
"""Generate crate-level technical visual learning guides for Neo N4.

The generated files are concept-first learning assets:
- README.md / README.zh.md technical visual sections per crate
- per-crate architecture, technical-principle, workflow, and dataflow diagrams
- state, proof, trust-boundary, lifecycle, and integration diagrams
- deep per-crate technical learning guides under docs/learning-guide*.md
- English and Chinese Mermaid and SVG diagrams

The diagrams explain how each crate fits into the Neo N4 system at the
architecture/protocol/runtime level. They deliberately avoid source-reading,
public API, test-map, and implementation-atlas views.
"""

from __future__ import annotations

import html
import json
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


def main() -> None:
    crates = discover_crates()
    index_rows_en: list[str] = []
    index_rows_zh: list[str] = []

    for crate in crates:
        guide = guide_for(crate)
        write_crate_assets(crate, guide)
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




@dataclass(frozen=True)
class DiagramSpec:
    slug: str
    title_en: str
    title_zh: str
    lens_en: str
    lens_zh: str


DIAGRAMS: tuple[DiagramSpec, ...] = (
    DiagramSpec("position", "System Position", "系统位置图", "where this crate sits in Neo N4", "它在 Neo N4 中的位置"),
    DiagramSpec("principles", "Technical Principles", "技术原理图", "the rules that make the design correct", "保证设计正确的技术规则"),
    DiagramSpec("architecture", "Conceptual Architecture", "概念架构图", "major technical blocks and boundaries", "主要技术块和边界"),
    DiagramSpec("workflow", "Workflow", "工作流图", "the ordered runtime process", "运行时的有序过程"),
    DiagramSpec("dataflow", "Data Flow", "数据流图", "how information, commitments, and evidence move", "信息、承诺和证据如何移动"),
    DiagramSpec("state-model", "State Model", "状态模型图", "state ownership, transitions, and finality", "状态归属、转换和终局性"),
    DiagramSpec("proof-flow", "Proof and Evidence Flow", "证明与证据流图", "how claims become verifiable evidence", "声明如何变成可验证证据"),
    DiagramSpec("trust-boundaries", "Trust Boundaries", "信任边界图", "what is trusted, checked, rejected, or observed", "哪些内容被信任、检查、拒绝或观测"),
    DiagramSpec("integration-map", "Integration Map", "集成关系图", "how this unit connects to the wider N4 stack", "该单元如何接入更大的 N4 栈"),
    DiagramSpec("lifecycle", "Runtime Lifecycle", "运行生命周期图", "from configuration through execution, evidence, and operation", "从配置到执行、证据和运维的生命周期"),
)

OBSOLETE_DIAGRAMS = ("module-map", "api-surface", "test-map", "dependency-map", "implementation-atlas")
SEQUENTIAL_DIAGRAMS = {"workflow", "dataflow", "proof-flow", "lifecycle"}


def write_crate_assets(crate: CrateInfo, guide: Guide) -> None:
    figures = crate.path / "docs" / "figures"
    figures.mkdir(parents=True, exist_ok=True)
    remove_obsolete_figures(figures)
    for spec in DIAGRAMS:
        nodes_en = diagram_nodes(crate, guide, spec.slug, "en")
        nodes_zh = diagram_nodes(crate, guide, spec.slug, "zh")
        write_svg(figures / f"{spec.slug}.svg", crate.name, spec, nodes_en, "en")
        write_svg(figures / f"{spec.slug}.zh.svg", crate.name, spec, nodes_zh, "zh")
        write_mermaid(figures / f"{spec.slug}.mmd", crate.name, spec, nodes_en)
        write_mermaid(figures / f"{spec.slug}.zh.mmd", crate.name, spec, nodes_zh)
    write_learning_guides(crate, guide)
    write_readme(crate, guide, "en")
    write_readme(crate, guide, "zh")


def remove_obsolete_figures(figures: Path) -> None:
    for slug in OBSOLETE_DIAGRAMS:
        for suffix in (".svg", ".zh.svg", ".mmd", ".zh.mmd"):
            old = figures / f"{slug}{suffix}"
            if old.exists():
                old.unlink()


def diagram_nodes(crate: CrateInfo, guide: Guide, slug: str, lang: str) -> list[tuple[str, str]]:
    p = profile(crate.name)
    g = localized_guide(guide, lang)
    if slug == "position":
        return [
            (label("Neo N4 layer", "Neo N4 层级", lang), g["layer"]),
            (label("Upstream domains", "上游技术域", lang), join_items(g["inputs"])),
            (crate.name, g["role"]),
            (label("Downstream domains", "下游技术域", lang), join_items(g["consumers"])),
            (label("Owned boundary", "拥有的边界", lang), join_items(g["responsibilities"])),
            (label("Outside boundary", "不拥有的边界", lang), tech("outside", p, lang)),
            (label("Learning focus", "学习重点", lang), sentence("Understand accepted facts, produced evidence, and next consumer.", "理解它接受的技术事实、产生的证据、以及下游如何消费。", lang)),
        ]
    if slug == "principles":
        return [
            (label("Technical objective", "技术目标", lang), g["role"]),
            (label("Determinism", "确定性", lang), tech("determinism", p, lang)),
            (label("Input constraints", "输入约束", lang), join_items(g["inputs"])),
            (label("Transition rule", "状态转换规则", lang), tech("transition", p, lang)),
            (label("Verifiable output", "可验证输出", lang), join_items(g["outputs"])),
            (label("Minimal trust", "最小信任", lang), tech("trust", p, lang)),
            (label("Layer separation", "分层边界", lang), tech("layering", p, lang)),
            (label("Observable evidence", "可观测证据", lang), tech("observe", p, lang)),
        ]
    if slug == "architecture":
        return [
            (label("Entry domain", "入口域", lang), join_items(g["inputs"])),
            (label("Normalization", "规范化层", lang), tech("normalize", p, lang)),
            (label("Core mechanism", "核心技术机制", lang), join_items(g["responsibilities"])),
            (label("State and commitment", "状态与承诺层", lang), tech("commit", p, lang)),
            (label("Evidence or result", "证据或结果层", lang), join_items(g["outputs"])),
            (label("Consumer boundary", "消费方边界", lang), join_items(g["consumers"])),
            (label("Failure path", "失败路径", lang), tech("failure", p, lang)),
            (label("Extension point", "扩展点", lang), tech("extend", p, lang)),
        ]
    if slug == "workflow":
        nodes = [(crate.name, sentence("Workflow shows technical phases, not a source call stack.", "工作流表达技术阶段，不表达源码调用栈。", lang))]
        nodes.append((label("Precondition", "前置条件", lang), join_items(g["inputs"])))
        nodes.extend((label(f"Stage {idx}", f"阶段 {idx}", lang), step) for idx, step in enumerate(g["workflow"], start=1))
        nodes.append((label("Success", "成功条件", lang), join_items(g["outputs"])))
        nodes.append((label("Reject", "拒绝条件", lang), tech("failure", p, lang)))
        return nodes
    if slug == "dataflow":
        nodes = [(crate.name, sentence("Dataflow shows information, commitments, evidence, and control signals.", "数据流表达信息、承诺、证据和控制信号如何移动。", lang))]
        nodes.extend((label(f"Data segment {idx}", f"数据段 {idx}", lang), step) for idx, step in enumerate(g["dataflow"], start=1))
        nodes.append((label("Commitment signal", "承诺信号", lang), tech("commit", p, lang)))
        nodes.append((label("Control signal", "控制信号", lang), tech("control", p, lang)))
        nodes.append((label("Next consumer", "后续消费方", lang), join_items(g["consumers"])))
        return nodes
    if slug == "state-model":
        return [
            (crate.name, sentence("State model explains what is remembered, committed, verified, or discarded.", "状态模型说明什么会被记住、提交、验证或丢弃。", lang)),
            (label("Input state", "输入状态", lang), join_items(g["inputs"])),
            (label("Working state", "工作状态", lang), tech("working", p, lang)),
            (label("Persistent state", "持久状态", lang), tech("persistent", p, lang)),
            (label("Transition", "状态转换", lang), tech("transition", p, lang)),
            (label("Commitment", "承诺", lang), tech("commit", p, lang)),
            (label("Finality", "终局条件", lang), tech("finality", p, lang)),
            (label("Rollback or reject", "回滚或拒绝", lang), tech("reject", p, lang)),
        ]
    if slug == "proof-flow":
        return [
            (crate.name, sentence("Proof/evidence flow shows how a claim becomes checkable.", "证明与证据流说明技术声明如何变成可检查结果。", lang)),
            (label("Claim", "声明", lang), sentence(f"This unit performed: {join_items(g['responsibilities'])}.", f"本单元完成了：{join_items(g['responsibilities'])}。", lang)),
            (label("Witness or input", "见证或输入", lang), join_items(g["inputs"])),
            (label("Execution or check", "执行或检查", lang), tech("proof_exec", p, lang)),
            (label("Public commitment", "公开承诺", lang), tech("commit", p, lang)),
            (label("Verifier", "验证者", lang), join_items(g["consumers"])),
            (label("Accepted result", "接受结果", lang), join_items(g["outputs"])),
            (label("Rejected result", "拒绝结果", lang), tech("reject", p, lang)),
        ]
    if slug == "trust-boundaries":
        return [
            (crate.name, sentence("Trust boundary shows what must be proven rather than believed.", "信任边界说明哪些假设必须被证明，而不是被相信。", lang)),
            (label("Trusted base", "可信基", lang), tech("trusted", p, lang)),
            (label("Untrusted input", "不可信输入", lang), join_items(g["inputs"])),
            (label("Validation boundary", "验证边界", lang), tech("validate", p, lang)),
            (label("Replay and ordering", "重放与顺序", lang), tech("ordering", p, lang)),
            (label("Authority", "权限模型", lang), tech("authority", p, lang)),
            (label("External systems", "外部系统", lang), tech("external", p, lang)),
            (label("Observed evidence", "观测证据", lang), tech("observe", p, lang)),
        ]
    if slug == "integration-map":
        return [
            (crate.name, g["layer"]),
            (label("L1/native verification", "L1/原生验证", lang), sentence("L1 consumes only commitments, messages, or proof results that can be checked.", "L1 只消费可检查的承诺、消息或证明结果。", lang)),
            (label("L2 execution", "L2 执行", lang), sentence("L2 owns transaction execution, state advancement, VM profiles, and batch boundaries.", "L2 负责交易执行、状态推进、VM profile 和批次边界。", lang)),
            (label("NeoFS DA", "NeoFS DA", lang), sentence("NeoFS stores batch data, witness or trace summaries, and retrievable evidence.", "NeoFS 保存批次数据、见证或轨迹摘要以及可取回证据。", lang)),
            (label("Proof system", "证明系统", lang), sentence("The proof system compresses L2 execution claims into verifiable evidence.", "证明系统把 L2 执行声明压缩为可验证证据。", lang)),
            (label("Gateway/API", "Gateway/API", lang), sentence("Gateway handles user routing, queries, submission, and health aggregation.", "Gateway 负责用户路由、查询、提交和健康状态聚合。", lang)),
            (label("Bridge and heterogeneous chains", "桥与异构链", lang), sentence("Bridge rules unify L1-L2, L2-L2, and heterogeneous-chain messages and assets.", "桥规则统一 L1-L2、L2-L2 和异构链消息与资产。", lang)),
            (label("Developer entry", "开发者入口", lang), sentence("SDKs, CLIs, templates, and examples expose the system as a usable experience.", "SDK、CLI、模板和示例把系统暴露成可使用的开发体验。", lang)),
        ]
    if slug == "lifecycle":
        return [
            (crate.name, sentence("Lifecycle shows how the unit reaches observable runtime operation.", "生命周期图说明技术单元如何进入可观测运行。", lang)),
            (label("Configuration", "配置", lang), tech("config", p, lang)),
            (label("Input intake", "输入接收", lang), join_items(g["inputs"])),
            (label("Normalization", "规范化", lang), tech("normalize", p, lang)),
            (label("Execution or verification", "执行或验证", lang), join_items(g["responsibilities"])),
            (label("Commit or output", "提交或输出", lang), join_items(g["outputs"])),
            (label("Consumption", "消费联动", lang), join_items(g["consumers"])),
            (label("Operation", "运维观测", lang), tech("observe", p, lang)),
        ]
    raise ValueError(f"unknown diagram slug: {slug}")


def localized_guide(guide: Guide, lang: str) -> dict[str, str | tuple[str, ...]]:
    if lang == "zh":
        return {
            "layer": guide.layer_zh,
            "role": guide.role_zh,
            "inputs": guide.inputs_zh,
            "responsibilities": guide.responsibilities_zh,
            "outputs": guide.outputs_zh,
            "consumers": guide.consumers_zh,
            "workflow": guide.workflow_zh,
            "dataflow": guide.dataflow_zh,
        }
    return {
        "layer": guide.layer_en,
        "role": guide.role_en,
        "inputs": guide.inputs_en,
        "responsibilities": guide.responsibilities_en,
        "outputs": guide.outputs_en,
        "consumers": guide.consumers_en,
        "workflow": guide.workflow_en,
        "dataflow": guide.dataflow_en,
    }


def profile(name: str) -> str:
    lowered = name.lower()
    if "zk" in lowered or "proof" in lowered or "prover" in lowered or "verifier" in lowered:
        return "zk"
    if "riscv" in lowered or lowered in {"counter", "hello-world", "nep17-token", "storage", "devpack-test", "neo-contract-template"}:
        return "riscv"
    if "vm" in lowered:
        return "vm"
    if "bridge" in lowered or "watcher" in lowered or "router" in lowered:
        return "bridge"
    if "sdk" in lowered or "cli" in lowered or "devpack" in lowered:
        return "tooling"
    return "core"


TECH_TEXT: dict[str, dict[str, tuple[str, str]]] = {
    "determinism": {
        "zk": ("same public input and witness produce the same public output and proof", "同一公开输入和见证必须得到同一公开输出与证明"),
        "riscv": ("same ABI input, guest module, and host callbacks produce the same result", "同一 ABI 输入、guest 模块和 host 回调必须得到同一结果"),
        "vm": ("same script, stack, gas, and syscall responses produce the same halt/fault state", "同一脚本、栈、gas 和 syscall 响应必须得到同一 halt/fault 状态"),
        "bridge": ("same source-chain event maps to one replay-protected bridge message", "同一源链事件映射到唯一、可重放保护的桥消息"),
        "tooling": ("same configuration and user intent produce the same request or package", "同一配置和用户意图产生同一请求或包"),
        "core": ("same input model produces the same output model", "同一输入模型得到同一输出模型"),
    },
    "transition": {
        "zk": ("guest execution is constrained by public values and verifier rules", "guest 执行受公开值和 verifier 规则约束"),
        "riscv": ("state changes through ABI, PolkaVM execution, and host syscall boundaries", "状态通过 ABI、PolkaVM 执行和 host syscall 边界变化"),
        "vm": ("opcode semantics, stack rules, gas, and syscall contracts define transitions", "opcode 语义、栈规则、gas 和 syscall 合同定义状态转换"),
        "bridge": ("events become normalized messages and then asset/state actions", "事件变成标准消息，再变成资产或状态动作"),
        "tooling": ("requests, configs, packages, or reports advance through checked stages", "请求、配置、包或报告通过校验阶段推进"),
        "core": ("explicit rules turn accepted input into checkable output", "显式规则把已接受输入转换成可检查输出"),
    },
    "trust": {
        "zk": ("trust verification keys and verifiers, not prover runtime environments", "信任验证密钥和 verifier，不信任 prover 运行环境"),
        "riscv": ("trust VM and host rules, not guest inputs or external callbacks", "信任 VM 与 host 规则，不信任 guest 输入或外部回调"),
        "vm": ("trust canonical NeoVM semantics, not script authors", "信任标准 NeoVM 语义，不信任脚本作者"),
        "bridge": ("trust confirmation and verification rules, not one watcher or RPC endpoint", "信任确认与验证规则，不信任单个 watcher 或 RPC 端点"),
        "tooling": ("trust validation and signing policy, not unchecked user input", "信任校验和签名策略，不信任未校验用户输入"),
        "core": ("trust only data that crossed validation boundaries", "只信任通过验证边界的数据"),
    },
    "layering": {
        "zk": ("guest proves computation, host orchestrates, L1 verifies compact results", "guest 证明计算，host 编排，L1 验证压缩结果"),
        "riscv": ("guest owns contract semantics; host owns resources, syscalls, and chain context", "guest 负责合约语义；host 负责资源、syscall 和链上下文"),
        "vm": ("VM semantics stay separate from host context and chain policy", "VM 语义与 host 上下文和链策略分离"),
        "bridge": ("watching, message normalization, asset state, and final verification are separated", "监听、消息规范化、资产状态和最终验证分层"),
        "tooling": ("interface, encoding, network submission, and diagnostics are separated", "界面、编码、网络提交和诊断分层"),
        "core": ("each layer carries one clear technical responsibility", "每层只承担一个清晰技术责任"),
    },
    "observe": {
        "zk": ("proof id, public output, verification result, duration, and failure reason", "proof id、公开输出、验证结果、耗时和失败原因"),
        "riscv": ("gas, syscalls, host trace, halt/fault, and execution digest", "gas、syscall、host trace、halt/fault 和执行摘要"),
        "vm": ("opcode progress, gas, stack digest, fault reason, and syscall boundary", "opcode 进度、gas、栈摘要、fault 原因和 syscall 边界"),
        "bridge": ("source event, confirmations, message hash, cursor, submit hash, and final state", "源事件、确认数、消息哈希、游标、提交哈希和最终状态"),
        "tooling": ("command, config digest, network, output artifacts, and diagnostics", "命令、配置摘要、网络、输出产物和诊断"),
        "core": ("input digest, output digest, state change, and rejection reason", "输入摘要、输出摘要、状态变化和拒绝原因"),
    },
    "normalize": {
        "zk": ("shape batch, witness, and public input into proof-system constraints", "把 batch、witness 和公开输入整理成证明系统约束"),
        "riscv": ("shape contract intent into ABI, stack values, host calls, and PolkaVM input", "把合约意图整理成 ABI、栈值、host call 和 PolkaVM 输入"),
        "vm": ("shape bytecode, stack, gas, and syscall responses into canonical VM context", "把字节码、栈、gas 和 syscall 响应整理成标准 VM 上下文"),
        "bridge": ("shape heterogeneous events into chain-neutral, hashable, replay-protected messages", "把异构事件整理成链无关、可哈希、可防重放消息"),
        "tooling": ("shape commands, config, and files into typed operations", "把命令、配置和文件整理成强类型操作"),
        "core": ("shape external inputs into a verifiable domain model", "把外部输入整理成可验证领域模型"),
    },
    "commit": {
        "zk": ("state root, public values, verification key, and proof digest", "状态根、公开值、验证密钥和证明摘要"),
        "riscv": ("ABI digest, execution result, gas report, and syscall trace", "ABI 摘要、执行结果、gas 报告和 syscall 轨迹"),
        "vm": ("script hash, final stack digest, halt/fault status, and gas", "脚本哈希、最终栈摘要、halt/fault 状态和 gas"),
        "bridge": ("message hash, nonce, source height, and asset action", "消息哈希、nonce、源链高度和资产动作"),
        "tooling": ("request digest, network profile, and output artifact hash", "请求摘要、网络 profile 和输出产物哈希"),
        "core": ("input digest, transition digest, and output digest", "输入摘要、转换摘要和输出摘要"),
    },
    "failure": {
        "zk": ("proving fails, local verification fails, public output mismatches, or verifier rejects", "证明生成失败、本地验证失败、公开输出不匹配或 verifier 拒绝"),
        "riscv": ("ABI decode fails, host callback rejects, gas is exhausted, or guest faults", "ABI 解码失败、host callback 拒绝、gas 耗尽或 guest fault"),
        "vm": ("invalid opcode, stack mismatch, gas exhaustion, syscall rejection, or fault", "非法 opcode、栈不匹配、gas 耗尽、syscall 拒绝或 fault"),
        "bridge": ("insufficient confirmations, nonce replay, message mismatch, or invalid asset state", "确认不足、nonce 重放、消息不匹配或资产状态无效"),
        "tooling": ("invalid config, signing-policy failure, network rejection, or output validation failure", "配置无效、签名策略失败、网络拒绝或输出校验失败"),
        "core": ("input violates constraints or output cannot be verified", "输入不满足约束或输出无法验证"),
    },
    "extend": {
        "zk": ("proof backend, verifier adapter, report format, and DA binding", "proof backend、verifier adapter、报告格式和 DA 绑定"),
        "riscv": ("VM profiles, host syscalls, ABI codecs, and contract templates", "VM profile、host syscall、ABI codec 和合约模板"),
        "vm": ("syscall host, gas policy, diagnostics, and compatibility profiles", "syscall host、gas 策略、诊断和兼容性 profile"),
        "bridge": ("source-chain watchers, message routes, asset mapping, and confirmation policy", "源链 watcher、消息路由、资产映射和确认策略"),
        "tooling": ("commands, templates, network profiles, and output formats", "命令、模板、网络 profile 和输出格式"),
        "core": ("input adapters, output adapters, and monitoring surfaces", "输入适配器、输出适配器和监控面"),
    },
    "control": {
        "zk": ("prove, verify, reject, retry, publish evidence", "证明、验证、拒绝、重试、发布证据"),
        "riscv": ("execute, syscall, charge gas, halt, fault", "执行、syscall、计费、halt、fault"),
        "vm": ("decode, execute, charge, syscall, halt, fault", "解码、执行、计费、syscall、halt、fault"),
        "bridge": ("poll, confirm, relay, consume, mark nonce", "轮询、确认、relay、消费、标记 nonce"),
        "tooling": ("prepare, validate, sign, submit, report", "准备、校验、签名、提交、报告"),
        "core": ("accept, normalize, execute, commit, reject", "接受、规范化、执行、提交、拒绝"),
    },
    "working": {
        "zk": ("witness, guest frame, prover memory, public-value buffer", "witness、guest frame、prover memory、公开值缓冲"),
        "riscv": ("guest memory, stack frame, host callback buffer", "guest memory、stack frame、host callback buffer"),
        "vm": ("instruction pointer, evaluation stack, gas counter", "指令指针、求值栈、gas 计数器"),
        "bridge": ("pending log, confirmation window, relay job", "待处理日志、确认窗口、relay 任务"),
        "tooling": ("command context, unsigned request, temporary artifact", "命令上下文、未签名请求、临时产物"),
        "core": ("validated input, working state, candidate output", "已校验输入、工作状态、候选输出"),
    },
    "persistent": {
        "zk": ("proof record, verification result, state-root commitment", "证明记录、验证结果、状态根承诺"),
        "riscv": ("contract state, gas report, execution receipt", "合约状态、gas 报告、执行回执"),
        "vm": ("committed VM result and host-visible receipt", "已提交 VM 结果和 host 可见回执"),
        "bridge": ("cursor, consumed nonce, escrow or mint state", "游标、已消费 nonce、托管或铸造状态"),
        "tooling": ("configuration, package, report, submitted transaction id", "配置、包、报告、已提交交易 id"),
        "core": ("accepted output and audit record", "已接受输出和审计记录"),
    },
    "finality": {
        "zk": ("verifier accepts proof and public output matches target state", "verifier 接受 proof 且公开输出匹配目标状态"),
        "riscv": ("host accepts result and includes state change in L2 transition", "host 接受结果并把状态变化纳入 L2 转换"),
        "vm": ("VM halts successfully and host accepts final stack and effects", "VM 成功 halt 且 host 接受最终栈和副作用"),
        "bridge": ("message is consumed and nonce is marked used", "消息被消费且 nonce 标记为已使用"),
        "tooling": ("target service accepts request and returns traceable result", "目标服务接受请求并返回可追踪结果"),
        "core": ("consumer accepts the output", "消费方接受输出"),
    },
    "reject": {
        "zk": ("reject proof, public output, or mode; state root does not advance", "拒绝 proof、公开输出或模式；状态根不推进"),
        "riscv": ("reject ABI, guest fault, or host callback; do not commit state change", "拒绝 ABI、guest fault 或 host callback；不提交状态变化"),
        "vm": ("enter fault or reject; unverifiable side effects are not committed", "进入 fault 或 reject；不可验证副作用不提交"),
        "bridge": ("drop or quarantine message; nonce and asset state do not advance", "丢弃或隔离消息；nonce 和资产状态不推进"),
        "tooling": ("return diagnostics; do not sign, submit, or publish", "返回诊断；不签名、不提交、不发布"),
        "core": ("reject output and retain diagnostics", "拒绝输出并保留诊断"),
    },
    "proof_exec": {
        "zk": ("replay deterministic computation inside the proof system", "在证明系统中重放确定性计算"),
        "riscv": ("execute at the PolkaVM and host boundary", "在 PolkaVM 与 host 边界执行"),
        "vm": ("execute under NeoVM semantics and check stack, gas, syscalls, and fault state", "按 NeoVM 语义执行并检查栈、gas、syscall 和 fault"),
        "bridge": ("verify source event, confirmations, nonce, asset action, and target message", "验证源事件、确认数、nonce、资产动作和目标消息"),
        "tooling": ("validate config, encoded result, signing policy, and target response", "校验配置、编码结果、签名策略和目标响应"),
        "core": ("execute deterministic rules and produce checkable output", "执行确定性规则并产生可检查输出"),
    },
    "trusted": {
        "zk": ("verification key, guest program hash, verifier rules", "验证密钥、guest 程序哈希、verifier 规则"),
        "riscv": ("PolkaVM semantics, host syscall rules, gas policy", "PolkaVM 语义、host syscall 规则、gas 策略"),
        "vm": ("NeoVM 3.9.x semantics, opcode table, syscall contract", "NeoVM 3.9.x 语义、opcode 表、syscall 合同"),
        "bridge": ("confirmation policy, message format, nonce rules, asset mapping", "确认策略、消息格式、nonce 规则、资产映射"),
        "tooling": ("configuration policy, signing policy, target-network profile", "配置策略、签名策略、目标网络 profile"),
        "core": ("explicit input constraints, transition rules, and output checks", "明确输入约束、转换规则和输出检查"),
    },
    "validate": {
        "zk": ("public input, proof envelope, verification key, and public output must match", "公开输入、proof envelope、验证密钥和公开输出必须一致"),
        "riscv": ("ABI, gas, syscall, host context, and execution result must match", "ABI、gas、syscall、host context 和执行结果必须一致"),
        "vm": ("opcode, stack types, gas, jump target, and syscall response must be valid", "opcode、栈类型、gas、jump target 和 syscall 响应必须合法"),
        "bridge": ("event, confirmations, message hash, nonce, and asset state all pass", "事件、确认数、消息哈希、nonce 和资产状态同时通过"),
        "tooling": ("config, params, signing, network, and output format validate", "配置、参数、签名、网络和输出格式通过校验"),
        "core": ("all inputs cross explicit validation boundaries", "所有输入进入显式校验边界"),
    },
    "ordering": {
        "zk": ("proof binds batch range and state root to prevent cross-batch reuse", "proof 绑定批次范围和状态根，避免跨批次复用"),
        "riscv": ("execution context binds call, state, and gas to prevent cross-context reuse", "执行上下文绑定调用、状态和 gas，避免跨上下文复用"),
        "vm": ("VM context binds script and host state", "VM 上下文绑定脚本和 host 状态"),
        "bridge": ("nonce, source height, and message hash provide ordering and replay protection", "nonce、源链高度和消息哈希提供顺序与重放保护"),
        "tooling": ("request id, network id, and signing domain prevent duplicate or cross-network use", "请求 id、网络 id 和签名域避免重复或跨网误用"),
        "core": ("input and output digests bind the same context", "输入摘要和输出摘要绑定同一上下文"),
    },
    "authority": {
        "zk": ("prover cannot advance state; verifier-accepted output is consumed", "prover 无权推进状态；只消费 verifier 接受的输出"),
        "riscv": ("guest cannot mutate chain state directly; host/syscall boundary authorizes effects", "guest 不能直接改链状态；host/syscall 边界授权副作用"),
        "vm": ("authority comes from invocation context and syscall host, not bytecode alone", "权限来自调用上下文和 syscall host，不来自字节码本身"),
        "bridge": ("asset actions require bridge rules, message evidence, and governance policy", "资产动作需要桥规则、消息证据和治理策略"),
        "tooling": ("signatures bind network, account, action, and validity period", "签名绑定网络、账户、动作和有效期"),
        "core": ("authority comes from boundary contracts, not caller claims", "权限来自边界契约，不来自调用者声明"),
    },
    "external": {
        "zk": ("proof backend, L1 verifier, NeoFS DA, report consumers", "证明 backend、L1 verifier、NeoFS DA、报告消费者"),
        "riscv": ("PolkaVM, L2 node, host services, contract tooling", "PolkaVM、L2 节点、host 服务、合约工具"),
        "vm": ("L2 execution engine, syscall host, bridge, state manager", "L2 执行引擎、syscall host、桥、状态管理器"),
        "bridge": ("L1/L2 RPC, heterogeneous chains, watchers, gateway, asset contracts", "L1/L2 RPC、异构链、watcher、gateway、资产合约"),
        "tooling": ("gateway, wallet, node RPC, developer environment", "gateway、wallet、节点 RPC、开发者环境"),
        "core": ("callers, consumers, monitoring, and persistence layer", "调用方、消费方、监控和持久化层"),
    },
    "config": {
        "zk": ("proving mode, guest image, verification key, DA policy", "proving mode、guest image、验证密钥、DA 策略"),
        "riscv": ("VM profile, ABI, host services, gas policy", "VM profile、ABI、host 服务、gas 策略"),
        "vm": ("opcode, gas, syscall rules, compatibility profile", "opcode、gas、syscall 规则、兼容性 profile"),
        "bridge": ("chain id, confirmations, asset mapping, message route, authority policy", "chain id、确认数、资产映射、消息路由、权限策略"),
        "tooling": ("network, account, endpoint, output format, safety policy", "网络、账户、端点、输出格式、安全策略"),
        "core": ("input model, output model, boundary policy", "输入模型、输出模型、边界策略"),
    },
    "outside": {
        "zk": ("does not own L1 governance, DA storage, or asset custody", "不负责 L1 治理、DA 存储或资产托管"),
        "riscv": ("does not own proof generation, bridge custody, or L1 finality", "不负责证明生成、桥托管或 L1 终局"),
        "vm": ("does not own consensus, networking, or asset bridging", "不负责共识、网络同步或资产桥接"),
        "bridge": ("does not own target-chain consensus; owns normalized relay semantics", "不负责目标链共识；负责标准化中继语义"),
        "tooling": ("does not own chain state; translates intent into safe operations", "不拥有链状态；把意图转换为安全操作"),
        "core": ("does not own external consensus; owns deterministic boundary behavior", "不拥有外部共识；负责边界内确定性行为"),
    },
}


def tech(key: str, profile_name: str, lang: str) -> str:
    en, zh = TECH_TEXT[key][profile_name]
    return zh if lang == "zh" else en


def label(en: str, zh: str, lang: str) -> str:
    return zh if lang == "zh" else en


def sentence(en: str, zh: str, lang: str) -> str:
    return zh if lang == "zh" else en


def write_svg(path: Path, crate_name: str, spec: DiagramSpec, nodes: list[tuple[str, str]], lang: str) -> None:
    width = 1760
    columns = 3
    gap_x = 38
    gap_y = 30
    box_w = 500
    box_h = 168
    rows_count = max(1, (len(nodes) + columns - 1) // columns)
    height = 210 + rows_count * box_h + (rows_count - 1) * gap_y + 96
    panel_h = height - 68
    title = spec.title_zh if lang == "zh" else spec.title_en
    subtitle = "Neo N4 技术原理学习图" if lang == "zh" else "Neo N4 technical learning diagram"
    palette = {
        "position": ("#22c55e", "#052e1a"),
        "principles": ("#f59e0b", "#3a2500"),
        "architecture": ("#38bdf8", "#082f49"),
        "workflow": ("#a78bfa", "#2e1065"),
        "dataflow": ("#14b8a6", "#042f2e"),
        "state-model": ("#60a5fa", "#0b2451"),
        "proof-flow": ("#f97316", "#3b1700"),
        "trust-boundaries": ("#fb7185", "#4c0519"),
        "integration-map": ("#34d399", "#052e2b"),
        "lifecycle": ("#c084fc", "#341455"),
    }
    accent, fill = palette[spec.slug]
    parts = [
        f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}" role="img" aria-label="{escape(crate_name)} {escape(title)}">',
        "<defs>",
        '<linearGradient id="bg" x1="0" y1="0" x2="1" y2="1"><stop offset="0%" stop-color="#08111f"/><stop offset="100%" stop-color="#111827"/></linearGradient>',
        '<filter id="shadow" x="-15%" y="-15%" width="130%" height="130%"><feDropShadow dx="0" dy="14" stdDeviation="12" flood-color="#020617" flood-opacity="0.42"/></filter>',
        "</defs>",
        '<rect width="1760" height="100%" fill="url(#bg)"/>',
        f'<rect x="34" y="34" width="1692" height="{panel_h}" rx="10" fill="#0f172a" stroke="#334155" stroke-width="2"/>',
        f'<text x="72" y="92" fill="#f8fafc" font-family="Inter,Segoe UI,Arial,sans-serif" font-size="34" font-weight="700">{escape(crate_name)}: {escape(title)}</text>',
        f'<text x="72" y="132" fill="#cbd5e1" font-family="Inter,Segoe UI,Arial,sans-serif" font-size="19">{escape(subtitle)} - {escape(spec.lens_zh if lang == "zh" else spec.lens_en)}</text>',
    ]
    start_x = 92
    start_y = 178
    for index, (node_title, body) in enumerate(nodes, start=1):
        col = (index - 1) % columns
        row = (index - 1) // columns
        x = start_x + col * (box_w + gap_x)
        y = start_y + row * (box_h + gap_y)
        parts.append(f'<rect x="{x}" y="{y}" width="{box_w}" height="{box_h}" rx="8" fill="{fill}" stroke="{accent}" stroke-width="2" filter="url(#shadow)"/>')
        parts.append(f'<rect x="{x + 18}" y="{y + 18}" width="42" height="32" rx="6" fill="#020617" stroke="{accent}" stroke-width="1"/>')
        parts.append(f'<text x="{x + 39}" y="{y + 41}" text-anchor="middle" fill="{accent}" font-family="Inter,Segoe UI,Arial,sans-serif" font-size="16" font-weight="700">{index}</text>')
        title_lines = wrap_text(node_title, 34)
        body_lines = wrap_text(body, 58)
        text_y = y + 34
        for line_index, line in enumerate(title_lines[:2]):
            parts.append(f'<text x="{x + 76}" y="{text_y + line_index * 24}" fill="#f8fafc" font-family="Inter,Segoe UI,Arial,sans-serif" font-size="21" font-weight="700">{escape(line)}</text>')
        body_start = text_y + 42 + max(0, len(title_lines[:2]) - 1) * 12
        for line_index, line in enumerate(body_lines[:5]):
            parts.append(f'<text x="{x + 26}" y="{body_start + line_index * 22}" fill="#dbeafe" font-family="Inter,Segoe UI,Arial,sans-serif" font-size="17">{escape(line)}</text>')
    footer = "Conceptual diagram generated from crate role metadata; not a source-code reading map."
    if lang == "zh":
        footer = "本图根据 crate 技术角色元数据生成；不是源码阅读图，也不是实现全景图。"
    parts.append(f'<text x="72" y="{height - 44}" fill="#94a3b8" font-family="Inter,Segoe UI,Arial,sans-serif" font-size="16">{escape(footer)}</text>')
    parts.append("</svg>")
    path.write_text("\n".join(parts) + "\n", encoding="utf-8")


def write_mermaid(path: Path, crate_name: str, spec: DiagramSpec, nodes: list[tuple[str, str]]) -> None:
    lines = ["flowchart TB"]
    for idx, (title, body) in enumerate(nodes, start=1):
        lines.append(f'    N{idx}["{escape_mermaid(str(idx) + ". " + title + ": " + body)}"]')
    if spec.slug in SEQUENTIAL_DIAGRAMS:
        for idx in range(1, len(nodes)):
            lines.append(f"    N{idx} --> N{idx + 1}")
    else:
        for idx in range(2, len(nodes) + 1):
            lines.append(f"    N1 --> N{idx}")
        if len(nodes) >= 6:
            lines.append("    N2 -. constrains .-> N4")
            lines.append("    N4 -. produces .-> N5")
            lines.append("    N5 -. consumed by .-> N6")
    lines.append("    classDef core fill:#0f172a,stroke:#38bdf8,color:#e5f2ff,stroke-width:2px;")
    lines.append("    classDef support fill:#111827,stroke:#64748b,color:#f8fafc;")
    lines.append("    class N1 core;")
    if len(nodes) > 1:
        lines.append("    class " + ",".join(f"N{idx}" for idx in range(2, len(nodes) + 1)) + " support;")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")


def write_readme(crate: CrateInfo, guide: Guide, lang: str) -> None:
    path = crate.path / ("README.zh.md" if lang == "zh" else "README.md")
    existing = path.read_text(encoding="utf-8") if path.exists() else f"# {crate.name}\n"
    start, end = MARKER_ZH if lang == "zh" else MARKER_EN
    section = render_readme_section_zh(crate, guide, start, end) if lang == "zh" else render_readme_section_en(crate, guide, start, end)
    path.write_text(replace_marked_section(existing, start, end, section), encoding="utf-8")


def render_readme_section_en(crate: CrateInfo, guide: Guide, start: str, end: str) -> str:
    rows = "\n".join(f"| {spec.title_en} | ![{spec.title_en}](docs/figures/{spec.slug}.svg) | [Mermaid](docs/figures/{spec.slug}.mmd) |" for spec in DIAGRAMS)
    return f"""
{start}
## Technical Visual Guide

These diagrams are local to this crate and explain `{crate.name}` at the technical architecture level. They focus on system role, principles, data movement, workflow, state, proof/evidence, trust boundaries, integration, and runtime lifecycle.

Full technical explanation: [docs/learning-guide.md](docs/learning-guide.md).

| View | Diagram | Mermaid |
| --- | --- | --- |
{rows}

### Technical Role

- **Layer:** {guide.layer_en}
- **Purpose:** {guide.role_en}
- **Inputs:** {join_items(guide.inputs_en)}
- **Responsibilities:** {join_items(guide.responsibilities_en)}
- **Outputs:** {join_items(guide.outputs_en)}
- **Consumers:** {join_items(guide.consumers_en)}

### Reading Order

1. Start with system position and conceptual architecture.
2. Read technical principles, trust boundaries, and state model to understand correctness.
3. Follow workflow and dataflow to see runtime movement.
4. Use proof/evidence flow, integration map, and lifecycle for operational understanding.
{end}
""".strip()


def render_readme_section_zh(crate: CrateInfo, guide: Guide, start: str, end: str) -> str:
    rows = "\n".join(f"| {spec.title_zh} | ![{spec.title_zh}](docs/figures/{spec.slug}.zh.svg) | [Mermaid](docs/figures/{spec.slug}.zh.mmd) |" for spec in DIAGRAMS)
    return f"""
{start}
## 技术可视化指南

这些图都放在本 crate 目录下，用技术架构视角解释 `{crate.name}`。重点是系统位置、技术原理、数据移动、工作流、状态、证明/证据、信任边界、集成关系和运行生命周期。

完整技术解释见 [docs/learning-guide.zh.md](docs/learning-guide.zh.md)。

| 视图 | 图 | Mermaid |
| --- | --- | --- |
{rows}

### 技术角色

- **层级:** {guide.layer_zh}
- **目的:** {guide.role_zh}
- **输入:** {join_items(guide.inputs_zh)}
- **职责:** {join_items(guide.responsibilities_zh)}
- **输出:** {join_items(guide.outputs_zh)}
- **消费方:** {join_items(guide.consumers_zh)}

### 阅读顺序

1. 先看系统位置图和概念架构图。
2. 再看技术原理图、信任边界图和状态模型图，理解为什么这样设计是正确的。
3. 然后看工作流图和数据流图，理解运行时如何移动。
4. 最后看证明/证据流、集成关系和生命周期，理解系统如何进入真实运行。
{end}
""".strip()


def write_learning_guides(crate: CrateInfo, guide: Guide) -> None:
    docs = crate.path / "docs"
    docs.mkdir(parents=True, exist_ok=True)
    (docs / "learning-guide.md").write_text(render_learning_guide_en(crate, guide), encoding="utf-8")
    (docs / "learning-guide.zh.md").write_text(render_learning_guide_zh(crate, guide), encoding="utf-8")


def render_learning_guide_en(crate: CrateInfo, guide: Guide) -> str:
    rows = "\n".join(f"| {idx} | [{spec.title_en}](figures/{spec.slug}.svg) | {spec.lens_en}. |" for idx, spec in enumerate(DIAGRAMS, start=1))
    workflow = "\n".join(f"{idx}. {step}" for idx, step in enumerate(guide.workflow_en, start=1))
    dataflow = "\n".join(f"{idx}. {step}" for idx, step in enumerate(guide.dataflow_en, start=1))
    p = profile(crate.name)
    return f"""# {crate.name} Technical Learning Guide

This guide explains `{crate.name}` as a Neo N4 technical unit. It is written for architecture learning: what the unit is responsible for, which assumptions make it correct, how data moves, how state changes, how evidence is checked, and where it plugs into the wider Neo N4 stack.

## Technical Contract

| Aspect | Meaning |
| --- | --- |
| Layer | {md_cell(guide.layer_en)} |
| Purpose | {md_cell(guide.role_en)} |
| Inputs | {md_cell(join_items(guide.inputs_en))} |
| Responsibilities | {md_cell(join_items(guide.responsibilities_en))} |
| Outputs | {md_cell(join_items(guide.outputs_en))} |
| Consumers | {md_cell(join_items(guide.consumers_en))} |

## Diagram Set

| # | Diagram | What to learn |
| --- | --- | --- |
{rows}

## Architecture Model

`{crate.name}` receives {join_items(guide.inputs_en)} and owns this boundary: {join_items(guide.responsibilities_en)}. It emits {join_items(guide.outputs_en)}, which are consumed by {join_items(guide.consumers_en)}.

Layering rule: {tech("layering", p, "en")}.

## Workflow

{workflow}

Failure path: {tech("failure", p, "en")}.

## Data Flow

{dataflow}

Commitment signal: {tech("commit", p, "en")}.

## State, Proof, and Trust

- State transition: {tech("transition", p, "en")}.
- Finality: {tech("finality", p, "en")}.
- Trust model: {tech("trust", p, "en")}.
- Validation boundary: {tech("validate", p, "en")}.
- Replay and ordering: {tech("ordering", p, "en")}.

## Integration and Operation

- NeoFS DA: NeoFS stores batch data, witness or trace summaries, and retrievable evidence.
- Proof system: The proof system compresses L2 execution claims into verifiable evidence.
- Gateway/API: Gateway handles user routing, queries, submission, and health aggregation.
- Bridge and heterogeneous chains: Bridge rules unify L1-L2, L2-L2, and heterogeneous-chain messages and assets.
- Observable evidence: {tech("observe", p, "en")}.

Regenerate these technical diagrams from the Neo N4 repository root with:

```powershell
python tools/docs/generate_crate_visual_docs.py
```
"""


def render_learning_guide_zh(crate: CrateInfo, guide: Guide) -> str:
    rows = "\n".join(f"| {idx} | [{spec.title_zh}](figures/{spec.slug}.zh.svg) | {spec.lens_zh}。 |" for idx, spec in enumerate(DIAGRAMS, start=1))
    workflow = "\n".join(f"{idx}. {step}" for idx, step in enumerate(guide.workflow_zh, start=1))
    dataflow = "\n".join(f"{idx}. {step}" for idx, step in enumerate(guide.dataflow_zh, start=1))
    p = profile(crate.name)
    return f"""# {crate.name} 技术学习指南

这份指南把 `{crate.name}` 当作 Neo N4 的一个技术单元来解释。它不是源码阅读图，而是帮助读者理解：这个单元负责什么、哪些技术假设保证它正确、数据如何移动、状态如何变化、证据如何被验证、它如何接入 Neo N4 的整体架构。

## 技术契约

| 维度 | 含义 |
| --- | --- |
| 层级 | {md_cell(guide.layer_zh)} |
| 目的 | {md_cell(guide.role_zh)} |
| 输入 | {md_cell(join_items(guide.inputs_zh))} |
| 职责 | {md_cell(join_items(guide.responsibilities_zh))} |
| 输出 | {md_cell(join_items(guide.outputs_zh))} |
| 消费方 | {md_cell(join_items(guide.consumers_zh))} |

## 图表集合

| # | 图 | 学什么 |
| --- | --- | --- |
{rows}

## 架构模型

`{crate.name}` 接收 {join_items(guide.inputs_zh)}，拥有的边界是：{join_items(guide.responsibilities_zh)}。它输出 {join_items(guide.outputs_zh)}，然后由 {join_items(guide.consumers_zh)} 消费。

分层规则：{tech("layering", p, "zh")}。

## 工作流

{workflow}

失败路径：{tech("failure", p, "zh")}。

## 数据流

{dataflow}

承诺信号：{tech("commit", p, "zh")}。

## 状态、证明和信任

- 状态转换：{tech("transition", p, "zh")}。
- 终局条件：{tech("finality", p, "zh")}。
- 信任模型：{tech("trust", p, "zh")}。
- 验证边界：{tech("validate", p, "zh")}。
- 重放与顺序：{tech("ordering", p, "zh")}。

## 集成和运行

- NeoFS DA：NeoFS 保存批次数据、见证或轨迹摘要以及可取回证据。
- 证明系统：证明系统把 L2 执行声明压缩为可验证证据。
- Gateway/API：Gateway 负责用户路由、查询、提交和健康状态聚合。
- 桥与异构链：桥规则统一 L1-L2、L2-L2 和异构链消息与资产。
- 可观测证据：{tech("observe", p, "zh")}。

在 Neo N4 仓库根目录重新生成这些技术图：

```powershell
python tools/docs/generate_crate_visual_docs.py
```
"""


def wrap_text(value: str, width: int) -> list[str]:
    value = " ".join(str(value).split())
    if not value:
        return [""]
    if contains_cjk(value):
        return wrap_cjk(value, width)
    lines: list[str] = []
    for segment in value.split(" | "):
        lines.extend(textwrap.wrap(segment, width=width, break_long_words=False, break_on_hyphens=False) or [segment])
    return lines


def contains_cjk(value: str) -> bool:
    return any("\u4e00" <= char <= "\u9fff" for char in value)


def wrap_cjk(value: str, width: int) -> list[str]:
    chunks: list[str] = []
    current = ""
    current_width = 0
    for char in value:
        char_width = 2 if "\u4e00" <= char <= "\u9fff" else 1
        if current and current_width + char_width > width:
            chunks.append(current)
            current = char
            current_width = char_width
        else:
            current += char
            current_width += char_width
    if current:
        chunks.append(current)
    return chunks


def escape(value: str) -> str:
    return html.escape(str(value), quote=True)


def escape_mermaid(value: str) -> str:
    return str(value).replace('"', "'").replace("\n", " ")


def join_items(items: Iterable[str]) -> str:
    return " | ".join(str(item) for item in items)


def md_cell(value: str) -> str:
    return str(value).replace("|", "<br>").replace("\n", " ").strip()


def replace_marked_section(existing: str, start: str, end: str, section: str) -> str:
    start_index = existing.find(start)
    end_index = existing.find(end)
    if start_index != -1 and end_index != -1 and end_index > start_index:
        return existing[:start_index].rstrip() + "\n\n" + section.strip() + existing[end_index + len(end):].rstrip() + "\n"
    separator = "\n\n" if existing.strip() else ""
    return existing.rstrip() + separator + section.strip() + "\n"


def write_index(rows_en: list[str], rows_zh: list[str]) -> None:
    docs = ROOT / "docs"
    zh_docs = ROOT / "docs" / "zh"
    docs.mkdir(parents=True, exist_ok=True)
    zh_docs.mkdir(parents=True, exist_ok=True)
    diagram_names_en = ", ".join(spec.title_en for spec in DIAGRAMS)
    diagram_names_zh = "、".join(spec.title_zh for spec in DIAGRAMS)
    (docs / "crate-visual-guide.md").write_text(
        "# Neo N4 Crate Technical Visual Guide\n\n"
        "This index links every Rust crate to its own local technical visual guide. "
        f"Each crate directory contains these concept-first diagrams in English and Chinese: {diagram_names_en}. "
        "The diagrams explain architecture, principles, workflow, dataflow, state, evidence, trust, integration, and lifecycle rather than source-code implementation.\n\n"
        "| Crate | Path | Layer | Purpose |\n"
        "| --- | --- | --- | --- |\n"
        + "\n".join(rows_en)
        + "\n",
        encoding="utf-8",
    )
    (zh_docs / "crate-visual-guide.md").write_text(
        "# Neo N4 Crate 技术可视化指南\n\n"
        "这个索引把每个 Rust crate 链接到它自己目录下的本地技术可视化文档。"
        f"每个 crate 都包含这些中英文技术学习图：{diagram_names_zh}。"
        "这些图解释架构、原理、工作流、数据流、状态、证据、信任、集成和生命周期，而不是源码实现。\n\n"
        "| Crate | 路径 | 层级 | 目的 |\n"
        "| --- | --- | --- | --- |\n"
        + "\n".join(rows_zh)
        + "\n",
        encoding="utf-8",
    )
    manifest = {
        "generated_crates": len(rows_en),
        "diagram_count_per_crate": len(DIAGRAMS),
        "diagram_kind": "concept-first technical architecture diagrams",
        "diagrams_per_crate": [spec.slug for spec in DIAGRAMS],
        "obsolete_implementation_diagrams_removed": list(OBSOLETE_DIAGRAMS),
        "learning_guides_per_crate": ["docs/learning-guide.md", "docs/learning-guide.zh.md"],
        "english_index": "docs/crate-visual-guide.md",
        "chinese_index": "docs/zh/crate-visual-guide.md",
    }
    (docs / "crate-visual-guide.manifest.json").write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
