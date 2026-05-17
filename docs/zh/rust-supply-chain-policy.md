# Rust 供应链策略

本项目把 Rust advisory 视为发布输入，而不是表面警告。生产发布必须保持
`cargo audit` vulnerability 数量为零，并且必须移除、升级或显式接受
`Cargo.lock` 中仍存在的每一个信息级警告。

当前跟踪的审计证据:

- `docs/audit/production-readiness-audit.md`
- `docs/audit/repository-coverage-ledger.md`

原始命令日志和 `cargo audit --json` 输出属于本地 scratch 证据，不作为源码文件
提交。发布审批包应附带新鲜生成的原始日志。

## 当前状态

SP1 crate 固定为 `6.2.1`，与已安装的 `cargo-prove` 工具链一致:

- `bridge/neo-zkvm-host`: `sp1-sdk = 6.2.1`
- `bridge/neo-zkvm-guest`: `sp1-zkvm = 6.2.1`

刷新 advisory database 后运行 `cargo audit --json` 的当前结论:

- Vulnerabilities: `0`
- Informational warnings: `5` 个 unmaintained crate 和 `1` 个 unsound crate

## 已接受的信息级警告

这些警告当前被接受，因为它们经 SP1 或 vendored NeoVM 依赖传入，无法通过普通的
semver-compatible `cargo update` 在不 fork 上游证明依赖的情况下修复。

| Advisory | Package | 当前来源 | 处置 |
| --- | --- | --- | --- |
| RUSTSEC-2026-0002 | `lru 0.12.5` | `sp1-prover 6.2.1 -> sp1-sdk 6.2.1` | 附 reachability note 后接受 |
| RUSTSEC-2025-0141 | `bincode 1.3.3` | `neo-zkvm-host`、SP1 和 vendored NeoVM crates | 兼容性依赖，暂时接受 |
| RUSTSEC-2021-0139 | `ansi_term 0.12.1` | SP1 tracing stack | 传递依赖，暂时接受 |
| RUSTSEC-2025-0119 | `number_prefix 0.4.0` | SP1 progress/indicator stack | 传递依赖，暂时接受 |
| RUSTSEC-2024-0436 | `paste 1.0.15` | SP1/zkVM macro stack | 传递依赖，暂时接受 |
| RUSTSEC-2025-0134 | `rustls-pemfile 2.2.0` | `tonic 0.12.3` via SP1 prover types | 传递依赖，暂时接受 |

## `lru` Reachability Note

当前 `lru` advisory 针对 `lru::IterMut`。本地依赖路径:

```text
lru v0.12.5
`-- sp1-prover v6.2.1
    `-- sp1-sdk v6.2.1
        `-- neo-zkvm-host
```

SP1 6.2.1 中使用 `LruCache` 的位置:

- `sp1-prover-6.2.1/src/worker/controller/mod.rs`
- `sp1-prover-6.2.1/src/shapes.rs`

这些位置使用 cache construction、`get`、`put` 和 `push`；`neo-zkvm-host`
证明测试覆盖的 SP1 prover 路径没有调用 `lru::IterMut`。这仍然是上游依赖例外，
不是对该 crate 的健康背书。SP1 升级到 `lru >= 0.16.3` 或移除 `lru` 后应删除
该例外。

## 验证命令

发布前运行:

```bash
cargo audit --json
cargo tree -i lru --locked --workspace
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo test --workspace --release --locked
cargo prove build
cd bridge/neo-zkvm-host
cargo test --release --locked
cargo test --release --locked -- --ignored --nocapture
```

Windows 运维者应在 WSL2 或 Linux/macOS prover host 上运行 SP1 命令。原生
Windows 不是 SP1 prover-host 目标。如果 Windows 使用本地 HTTP proxy，WSL2
下载命令可能需要使用 Windows host gateway 地址，而不是 `127.0.0.1`。
