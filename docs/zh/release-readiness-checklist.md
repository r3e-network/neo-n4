# 发布就绪检查清单

生产或公开测试网发布前使用本清单。每一项都应留下可复核证据:
日志、工件、交易哈希、合约哈希或签名审批。

## 1. 源码与依赖冻结

- 记录仓库提交、子模块提交和发布标签。
- 确认 `Cargo.lock`、TypeScript lockfile、NuGet 包图和合约 manifest
  在验证后没有未提交变化。
- 执行 `docs/rust-supply-chain-policy.md` 中的 Rust 供应链检查。
- 记录仍被接受的信息级 advisory，以及接受原因。

## 2. 本地验证关口

发布前运行完整本地矩阵:

```bash
dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo
dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build
mdbook build
cargo fmt --all -- --check
cargo clippy --workspace --all-targets --locked -- -D warnings
cargo test --workspace --release --locked
cargo audit --json
```

Windows 运维者应在 WSL2 中运行 SP1 证明命令:

```bash
cd bridge/neo-zkvm-guest
cargo prove build

cd ../neo-zkvm-host
cargo test --release --locked
cargo test --release --locked -- --ignored --nocapture
```

## 3. 合约工件复核

- 直接构建所有 `contracts/NeoHub.*`、`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` 和
  `samples/contracts/Sample.*` 项目。
- 记录每个 `.nef` 哈希、manifest 哈希、编译器版本和项目路径。
- 复核 manifest permissions、groups、supported standards 和 safe methods。
- 确认 devnet-only stub 没有注册到生产 registry。
- 确认 verifier kind 与生产 verifier 实现匹配。

## 4. 部署计划复核

- 使用目标网络配置生成部署计划。
- 复核 owner/governance 账户、多签策略和升级权限。
- 复核 verifier registry、challenge window、finality threshold 和桥确认数。
- 复核 ETH、SOL、TRON 及所有启用 EVM-family 链的 token mapping 和外链 ID。
- 广播前记录预期合约地址和哈希。

## 5. Devnet/Testnet 演练

生产前在真实 Neo N4 devnet/testnet 节点集上执行:

- 从干净状态部署合约。
- 初始化治理、verifier registry、桥 registry、token mapping、settlement
  manager、challenge 合约和 emergency controls。
- 对发布计划中的每种 proof type 至少提交一个有效 batch。
- 对每个启用的桥家族执行 deposit 和 withdrawal 流程。
- 执行 replay、wrong-chain、duplicate-signer、insufficient-confirmation 和
  expired-challenge 负向测试。
- 演练 optimistic fraud proof 提交、rollback 和 slashing。
- 重启 watcher 和 prover，验证 journal/cursor 恢复。
- 触发 `/healthz`、`/readyz`、metrics 和告警检查。
- 记录交易哈希、最终 state root、withdrawal root 和 watcher 日志。

## 6. CI 与发布审批

- 推送分支并要求扩展后的 GitHub Actions workflow 通过。
- 对 release candidate 手动 dispatch workflow，并设置
  `run_real_sp1_proof=true`。
- 要求 `SDK Conformance / Shared vectors (4 SDKs)` 通过，并以
  `require_live=true` 手动触发 `SDK Conformance`；保留离线与真实环境 JSON
  汇总，任何发现或执行零个真实环境测试的报告都必须拒绝。
- 将 CI run URL、本地验证日志、合约工件、部署计划和 devnet/testnet
  演练证据附到发布审批。
- 要求合约、prover、watcher/operator 和安全负责人签核。

## 7. 生产切换

- 对生产 RPC endpoint 重新执行只读 preflight。
- 确认私钥、HSM 策略、签名阈值和应急联系人。
- 只按已复核顺序部署；遇到哈希或 manifest 不匹配立即停止。
- 部署后验证 registry 状态、owner 状态、桥映射、challenge window 和
  watcher readiness，再开放用户流量。
- 首个生产 finality window 内保持 rollback 和 emergency pause 流程可用。
