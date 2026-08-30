# Neo N4 — 全系统审计（2026-08-29）

目标：审计整个仓库，并逐系统验证其正确性、专业性、一致性、完备性、安全性、
效率与健壮性。

方法：九路并行子系统评审 —— 其中包括针对 L1 资金路径的专项评审
（全部 26 个 `contracts/` 项目均完整通读）—— 外加在本工作站
（Windows 11 / net10.0 / Git Bash）上独立执行的可执行门禁。以下每一项发现都带有
证据层级标签，以免把实测事实与推断混为一谈。

证据层级：

- **[E1] 已执行** — 在本机的命令结果中直接观察到。
- **[E2] 已读码核实** — 我打开了被引用的代码并亲自确认了该机制。
- **[E3] 转述报告** — 由某子系统评审者提出，机制未经独立再确认。

HEAD `728e630a` 处测得的仓库规模：103 个第一方 `.csproj`；1,138 个 C# 文件
（158,547 行 — 生产 72,905 / 测试 85,642）；101 个 Rust 文件（22,070 行）；
约 119,281 行 TypeScript；27 个 `src/` 项目、26 个 `contracts/` 项目、7 个 `tools/`、
38 个测试项目、3 个 watcher crate。

---

## 1. 可执行门禁 — 实际运行了什么

| 门禁 | 结果 | 证据 |
| --- | --- | --- |
| `dotnet build Neo.L2.sln` | **0 warning、0 error** | [E1] `TreatWarningsAsErrors=true` 确实成立 |
| `dotnet test Neo.L2.sln` | 38 个程序集，**2,814 通过 / 0 失败 / 55 跳过** | [E1] |
| `dotnet format --verify-no-changes` | **exit 0** | [E1] 风格纪律是真实存在的，而非愿景陈述 |
| `mdbook build` | **exit 0** | [E1] |
| Devnet L0（`-- 5`） | **全部完成**，审计通过 ✅ | [E1] 5 个 batch sealed、5 deposits、5 withdrawals、5 个 Multisig proof 验证通过（每个 391 B）、state-root 连续性、DA 可用性、`alice balance 14850000 (expected 14850000)` |
| 4 个 sample 链配置 | **全部完成** | [E1] general-rollup / gaming-rollup / exchange-validium / privacy-sidechain |
| TypeScript SDK（`vitest`） | 23 通过、3 跳过（live conformance，可见） | [E1] |
| Rust：`neo-execution-core` | 14 通过 | [E1] |
| Rust：`neo-bridge-watcher-eth` | 85 通过 | [E1] |
| Rust：`neo-bridge-watcher-tron` | 7 通过 | [E1] |
| Rust：`neo-bridge-watcher-sol` | 9 通过 | [E1] |
| Rust：`neo-n4-sdk` | 17 通过、3 ignored | [E1] |
| `NeoHub.Sp1Groth16Verifier` 单元测试 | **12 通过** — 真实的、已提交的 SP1 6.2.1 Groth16 proof 经由编译后的 NEF 得到验证，外加逐字段篡改拒绝 | [E1]（经评审者执行）；`tests/fixtures/sp1-groth16-positive-vector-v1.json` |
| `dotnet test Neo.L2.Executor.RiscV.UnitTests` | **31 通过 / 0 跳过**（此前为 21 通过 / 10 跳过） | [E1] — 我构建了 `neo_riscv_host` 并闭合了该门禁；见 §3.1 |
| Python SDK（`unittest`） | **20 通过**（13 个 client + 7 个共享向量 conformance） | [E1] 在一个临时的 `uv` 环境下运行 `python -m unittest tests.test_client` 与 `…test_conformance_offline`（`cryptography` 是唯一第三方依赖；解释器中不存在，且未安装 `pytest`） |
| `cargo test --workspace` | **在 Windows 上失败** | [E1] `sp1-jit 6.2.1` 使用仅限 Linux 的 `libc::ftruncate` / `shm_unlink` / `create_anonymous_file` |

总体结论：该系统在其默认配置下确实端到端可用，且字节级的 proof verifier 是针对
一个真实的 SP1 proof 而非 mock 验证的。这是一个异常坚实的基线。以下缺陷集中在
（a）静默不执行的路径，以及（b）从未被组合在一起的子系统组合。

### 1.1 覆盖率台账 — 此处“全量”的含义

每一个第一方子系统都有交代，包括那些答案是“已评审、无实质问题”的子系统，
以及那些被有意不去 exercised 的子系统。

| 子系统 | 清单 | 评审于 |
| --- | --- | --- |
| L1 合约（资金路径） | 26 个 `contracts/` 项目，完整通读 | H12、H13、§5 合约组、§6 合约 LOW、§7 已清偿清单 |
| L2 节点插件 | 8 个 `src/Neo.Plugins.L2*` | C1、H1、H10、§5、§7 |
| 核心链下库 | 19 个 `src/Neo.L2.*` 库（含 `Executor`、`Executor.RiscV`、`State`、`Proving`、`Messaging`、`Persistence`、`Telemetry`、2 个 `*.Rpc` 适配器） | C2、H10、H11、§5（按主题） |
| Neo core fork | `external/neo` submodule（10 个 L2 native contract、插件宿主） | H1 的依据（`Plugin.cs:75`）、§9 gitlink 门禁（H9、§3.2）— 在集成接缝处评审，未作为上游重新审计 |
| 证明栈（Rust） | 5 个 `bridge/` crate（`neo-execution-core`、`neo-zkvm-{guest,host}`、`neo-zkvm-gateway-{guest,host}`） | §1（凡 Windows 可行即已执行）、§3.3、§7、§10 |
| Bridge watchers | 3 个 crate（`eth`、`sol`、`tron`） | H4、H5、§1 |
| SDK | TS、Python、Rust + 共享 `sdk/conformance/vectors/v1.json` | H7、§1（三套测试均已执行）、§7 |
| Web explorer | `sdk/web-explorer/index.html`（317 行） | 已阅读；一次 `fetch`、零 `innerHTML`、无实质发现；**未**在浏览器中 exercised |
| 运维工具 | 7 个 `tools/*` 项目（CLI、Devnet、Deploy、Explore、Faucet、Bridge、External-Bridge CLIs） | §5、§6、H13 的部署时序、§9 |
| Samples / 参考执行器 | `samples/contracts/` 2 个（`CrossChainGreeter`、`WithdrawalDemo`）、`samples/executors/` 1 个 | §5（semantic-id 奇偶一致性）、§7 |
| 测试清单 | 38 个项目 / 38 个已执行程序集 | §3.1（静默跳过缺陷）、§1 |
| 持久化 | `Neo.L2.Persistence`（RocksDB）+ 原子 CAS 路径 | §5 确定性、§7（刻意的 async-`Put` 设计，已在 `TASKS.md` 中裁定） |
| 文档 / 规范 / book | `doc.md`、`ARCHITECTURE.md`、`docs/`（含 `telemetry.md`、wire formats）、`book/`、`tools/manuscript`、`tools/docs` 生成器、zh 镜像 | §5/§6 的一致性行、§9 清扫；`mdbook build` 清洁（§1） |
| CI 与发布门禁 | `.github/workflows/*`、`scripts/ci/*`、coverage + audit 脚本 | §3.2–§3.5、§1 |
| **未被 exercised** | L1/L2 实网部署、`nccs` artifact 生成、`forge`/Solidity 测试、coverage 门禁（需要缺失的 `pwsh`）、`cargo deny`（未安装）、SP1 host/guest 出证（Windows 不兼容的 workspace 测试） | §10，以“未验证”而非“通过”陈述 |

未跟踪的本地残留，记录备查：`CODEX_DEEP_AUDIT/`（317 MB — 上一个 agent 下载的
foundry 工具链外加一个空的 `screenshots/`）、`target/`（31 GB）、`coverage/`
（60 MB）与 `artifacts/`（547 MB）。这四者都被正确 gitignore（`.gitignore:19,20,25,50`）
且均非仓库内容；我把它们原地保留。唯一值得提的卫生问题是：一个名字看起来像审计
交付物的工具缓存目录位于仓库根目录。

---

## 2. Critical 发现

### C1 — 充值与 L1→L2 消息在 batcher 收件箱中相撞；同时启用两者会令链停摆 [E2 已核实]

> **状态 — 已于 2026-08-29 修复（本报告写就之后）。** `L1MessageDrain` 现在会按内容
> 相等拒绝完全重复的消息，并把 `(sourceChainId, nonce)` 槽位声明的作用域收窄到
> `MessageType.Deposit` —— 这是唯一一族其 L2 消费方以该槽位为键的消息，因此两个
> 相互独立的 nonce 空间得以共存，而不再使 batcher 停摆。比较器被拓宽为对每个字段
> 的全序，因为合并后的序列会馈入 `l1MessageHash`，而 `List.Sort` 不稳定。
> `UT_L1MessageDrain` 新增了 §9 所要求的充值+路由组合 drain 引脚，外加三项回归测试。
> 下方 Fix 注记中的替代方案（预留一个专用充值 source-chain id，并在 L2 consumed-key
> 前缀中镜像该 id）**未**被采纳：为了一个已被 dedup 作用域收窄所闭合的活性缺陷，
> 去改动一处配对使用的链下 ↔ 链上字节格式，代价不成比例。

两条 L1→L2 通道都把 `SourceChainId = 0` 写入戳记，且各自维护一个从 1 开始的、
独立的按目标链 nonce 计数器：

- `contracts/NeoHub.SharedBridge/SharedBridgeContract.cs:24` `PrefixDepositNonce = 0x01 + chainId(4B)`，
  在 `:184`/`:592` 递增（`var next = current + 1`）。
- `contracts/NeoHub.MessageRouter/MessageRouterContract.cs:24` `PrefixL1ToL2Nonce = 0x01 + targetChainId(4B)`，
  并且 `:142` 以字面量 source chain id `0u` 编码该消息。
- `src/Neo.L2.Bridge/SharedBridgeDepositRecord.cs:127` 设置 `SourceChainId = 0`。

`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:133-149` 恰恰把这两条 drain 串接在一起，
而 `src/Neo.L2.Messaging/L1MessageDrain.cs:113-121` 按元组
`(SourceChainId, Nonce)` 去重 —— 该元组省略了消息种类。因此对任意一条链而言，
第一笔充值与第一条 `EnqueueL1ToL2` 消息都会键到 `(0,1)`。

影响：`Combine` 抛出 `InvalidOperationException`，batcher 停止 sealing，L2 冻结 ——
所有充值与提取全部停摆。任何账户只需支付少量 GAS 手续费即可到达该路径。
消息本身*是*可区分的（`MessageType.Deposit` 对 router 各类型）；只有去重键的
作用域被写窄了。链上消费使用相互独立的命名空间
（`PrefixDepositConsumed` / `PrefixInboundConsumed`），因此没有资金丢失或被重复入账 ——
这是一个活性缺陷，不是盗币。

值得肯定的是，作者的守卫是 fail-closed 的，且其消息文本点名了这一确切风险
（"SharedBridge deposit nonces and MessageRouter nonces must not collide under sourceChainId=0"），
说明此事是被预见过的。但一个把普通使用模式转化为链停摆的守卫是设计缺口，
而不是缓解措施。C1 还与下文 H1 叠加：`Blockchain.Committed` 路径上的未处理异常会
停止节点，因此该停摆可能升级为 sequencer 宕服。

Fix：以 `(SourceChainId, MessageType-domain, Nonce)` 为键，或为充值路径预留
自己的 source-chain id，并在 L2 consumed-key 前缀中镜像该选择。

### C2 — `MerkleTree.Verify` 不受位置绑定 [E2 已核实]

`src/Neo.L2.State/MerkleTree.cs:143-164`：折叠方向完全来自
`proof.PathBitmap`；`proof.LeafIndex` 从未被读取。`GetProof`（`:133-138`）把
`LeafIndex` 与 `PathBitmap` 作为彼此独立的字段发出，而 `MerkleProofSerializer` 也将
二者分开编码。因此一份针对 leaf 0 的真实 proof 可以被重新标注为 `LeafIndex=1`
并仍然针对同一个 root 验证通过。`KeyedStateMerkleTree.Verify`（`:106-120`）却从
`leafIndex` 的比特位推导方向 —— 也就是说仓库里的两个验证器对于“何为权威”意见相左。

只要每个消费方都按值绑定 root，该缺陷就无法直接用于盗取资金；但任何
信任所上报 leaf 位置（message index、nonce 排序）的消费方都会暴露在外，而两套
约定彼此漂移本身就是一项正确性隐患。应针对此类消费方审计
`doc.md` §11 / 提取（withdrawal）路径。

Fix：在 `MerkleTree.Verify` 中由 `LeafIndex` 重算 bitmap 并断言二者相等。

---

## 3. 验证完整性发现（虚假信心）

这些发现不会破坏状态，但它们让“绿色”结果的含义小于其表面含义。它们正是本次
审计标题既写“2,814 tests pass”*又*写“其中约 45 个并未运行”的原因。

### 3.1 约 45 个测试在 Windows 上静默自我跳过；CI 永远看不到 [E1 已证明]

2,869 个测试中有 55 个被跳过，尽管本次运行打印了 `Passed!`。根因已被证明：
`tests/Directory.Build.props:4-5` **仅在 `'$(OS)' == 'Windows_NT'`** 时注入
`<RuntimeIdentifier>win-x64</RuntimeIdentifier>`，因此测试输出落在
`bin/Debug/net10.0/win-x64/` — 比 Linux 上深一层目录。11 个文件中的 40 个测试方法
用一个硬编码的五层上溯来定位已提交的证据文件：

```
Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..","..","..","..","..",
    "docs","audit","testnet-deployment-20260716-live.json"))
```

用这些测试自身的算术实测：

- 5 × `..` → `D:\Git\neo-n4\**tests**\docs\audit\…json` — `exists=False`（代码算出的路径）
- 6 × `..` → `D:\Git\neo-n4\docs\audit\…json` — `exists=True`（真实文件）

端到端确认：把该 JSON 复制到代码算出的路径后，
`Neo.Plugins.L2Batch.UnitTests` 从 66 个测试 / 1 跳过变为 **66 通过 / 0 跳过**，
且 `FromChainDirectory_LiveDeployReport_LoadsChainId` 确实执行并通过。因此底层
生产接线是**正确的** —— 坏掉的只是文件发现。`.audit-test-full.log` 的跳过消息，
逐字引用：`repo evidence file not found at D:\Git\neo-n4\tests\docs\audit\…`。

为何无人察觉：CI 只有 `ubuntu-latest`（`.github/workflows/build.yml` — 每一处
`runs-on`），那里不注入 RID，五层上溯恰好正确。而主 `dotnet test` 步骤
（`:96-97`）没有使用仓库自己的零跳过守卫
（`scripts/ci/run_dotnet_filtered_tests.py:85` "expected zero skipped tests"），
该守卫*确实*被应用于 `:222,232,240,296,317` 的过滤后合约/native 门禁。

仓库中已存在正确写法：`FindRepositoryRoot()`，被
`tests/Neo.Hub.Deploy.UnitTests/UT_ProductionGapClosure.cs:175+` 使用，这也是该
项目报告零跳过的原因。

Fix（任一即可，按优先级）：把这 40 处迁移到 `FindRepositoryRoot()`；或在主 Test
步骤中增加一次带 `--reject-skips` 的 `run_dotnet_filtered_tests.py` 运行；或去掉
仅限 Windows 的 RID 注入。

### 3.2 RISC-V 的“奇偶一致性”门禁从未在运行 — 一旦运行即通过 [E1]

10 个 `RealNative_*` 测试以 `DllNotFoundException: neo_riscv_host` 跳过。这是合理的
环境门控，但它意味着有状态 RISC-V 执行器与 `ApplicationEngine` 的逐字节奇偶一致性
未经验证。我把它构建了出来（在 `external/neo-riscv-vm` 中 `cargo build -p neo-riscv-host`，
21.9s，`crate-type=["rlib","cdylib"]`），把 DLL 放到测试程序集旁边，得到
**31 通过 / 0 跳过**。新获得验证的表面包括：
`RealNative_RetReceiptMatchesApplicationEngineByteForByte`、
`RealNative_StoragePutCommitsStateAndCanonicalEffects`、
`RealNative_CallbackOutOfGasRollsBackStateAndEffects`、
`RealNative_UnsupportedContractCallFaultsClosed` 以及另外 6 个。
建议提交一个 `scripts/build-native-host` 步骤，让该门禁可复现。

### 3.3 `cargo test --workspace` 无法在 Windows 上运行 [E1]

`sp1-jit 6.2.1` 在 `libc::ftruncate`、`libc::shm_unlink`、`create_anonymous_file` 上失败。
因此 `docs/system-verification-plan.md` 中记录的 L1 门禁（"`cd bridge/neo-zkvm-guest && cargo
build && cargo test`"）实际上仅限 Linux。后果：全部 SP1 出证 Rust 代码都是
`#[cfg(unix)]`，在 Windows 开发机上完全未被 exercised。`docs/getting-started.md`
应当写明 Windows 贡献者无法运行出证门禁。

### 3.4 NuGet advisory 门禁 fail-open [E1 已实测]

`.github/workflows/build.yml:87-94`，位于一行写着 "Non-overridable supply-chain gate"
的注释之下：

```bash
dotnet list Neo.L2.sln package --vulnerable --include-transitive | tee /tmp/nuget-vuln.log
if grep -qi "is affected by" /tmp/nuget-vuln.log; then exit 1; fi
echo "OK: no vulnerable NuGet packages"
```

在 `bash -e` 且没有 `pipefail` 的情况下，失败的 `dotnet list` 会产出状态为 0 的
管道与一个空日志。直接实测：`bash -e -c 'false | tee /dev/null; echo exit=$?'` → `exit=0`。
因此任何 restore 或网络错误都会打印 `OK: no vulnerable NuGet packages`。
（备案：该门禁当前确实一无所获：107 个项目、5 个不同 package、全部精确钉扎，
`api.nuget.org` 干净。）Fix：`set -o pipefail`，外加显式的退出码与预期项目数量断言。

### 3.5 `cargo audit --ignore` 是全域作用域，且策略表报告的是过滤后的结果 [E2]

`.github/workflows/build.yml:600-607` 在**全部五个** lockfile 的循环上应用
`--ignore RUSTSEC-2026-0258`，但该 advisory 属于 `h2 0.4.14`，而它只存在于
`external/neo-zkvm/Cargo.lock`（已核实：根 lockfile 是 `h2 0.4.19`）。
未来*根*依赖图回退到存在漏洞的 `h2` 时会被静默接受。
`docs/rust-supply-chain-policy.md:19-24` 为 `external/neo-zkvm` 记录 `Vulnerabilities: 0`，
而这一点只因同一个 RUSTSEC id 被忽略才成立。Fix：按 lockfile 收窄 ignore 作用域，
并再跑一遍不 ignore 的扫描；把表头那一列标注为 "as gated" 而不是 "0"。

---

## 4. High 发现

**H1 — 插件异常会停止节点** [E2]。`L2BatchPlugin.cs:478` 把异常重新抛入
`Blockchain.Committed` 处理器，而没有任何第一方插件覆写 `ExceptionPolicy`；
`external/neo/src/Neo/Plugins/Plugin.cs:75` 默认为 `UnhandledExceptionPolicy.StopNode`
（分派在 `:280-284`）。因此 `PersistAndAcknowledge`（`:580-597`）中一次瞬时的
RocksDB/DA/SP1 故障，或 `RecoverAndProcessCommittedBlocks`（`:489-500`）中的账本缺口，
都会杀死节点 —— 这是一种可远程表达的 sequencer 宕服。`TryRetryPendingSealedBatch()`
（`:565`）已经作为优雅路径存在。Fix：`StopPlugin` + 重试。

**H2 — 强制包含的截止期短于它所暂停的挑战窗口** [E3]。
`contracts/NeoHub.ForcedInclusion/ForcedInclusionContract.cs:195,495-503` 把截止期
封顶在 86,400 秒，而 `contracts/NeoHub.OptimisticChallenge/OptimisticChallengeContract.cs:170`
允许 `7*86400`。24 小时之后，`ReportCensorship` 可以暂停那些仍然合法可挑战的 batch。

**H3 — 审查规避（censorship）逃生阀除非手工接线否则必然失败** [E3]。
`ForcedInclusionContract.cs:503` 调用 `ChainRegistry.PauseChain`，后者要求
`CheckWitness(owner) || IsPauser(caller)`（`ChainRegistryContract.cs:485`）；
`ForcedInclusion` 并未被自动注册为 pauser，于是反审查路径恰好在最需要它的时刻失败。
在 `IsProductionReady()` 中断言这一点。

**H4 — ETH watcher 接受无头部交叉校验、0 确认数的 RPC log** [E3]。
`watchers/neo-bridge-watcher-eth/src/live/eth_rpc/eth_rpc_event_source.rs:109-128` 只校验
router 地址和一个区块号区间 — 没有 `blockHash` ↔ 头部绑定、没有 `logIndex`
检查 — 并且 `min_confirmations` 默认为 0
（`eth_rpc_event_source_builder.rs:33`、`poll_config.rs:52`；二进制只在
`bin/neo-bridge-watcher-eth.rs:454` 发出警告）。一个怀有敌意或发生重组的 HTTP
provider 可以伪造一条 `Locked` log，随后委员会签名就会抽干 escrow。
这是外部桥接表面中严重程度最高的条目。Fix：`eth_getBlockByNumber` → 要求
`log.blockHash == header.hash` 以及发出该 log 的交易确实存在；在 `build()` 内部
强制一个非零下限。

**H5 — Stub 签名器把资金标记为已提交** [E3]。唯一随附的 ETH 签名器返回
`SHA256(script)` 作为假的交易 hash（`bin/neo-bridge-watcher-eth.rs:744`、
`stub_sign_and_send.rs:11-28`），而 `NeoRpcSubmitter` 却把它作为已提交持久化并推进
游标。切换到真实签名器后，那些 `(chain,nonce)` 对永不再重试，于是资金滞留锁死在
外部链的 escrow 中。已确认存在的缓解控制：启动时若缺少 `--allow-stub-signer`
会硬性拒绝。Fix：对于合成 hash 永不持久化 submitted-set 条目。

**H6 — `neo-zkvm-executor` 的 SHA-256 钉扎由调用方提供** [E3]。
`src/Neo.Plugins.L2Settlement/Sp1SettlementExecutionStack.cs:46,128` 把摘要作为
构造函数参数接收。它在失配时 fail-closed（`Sp1StatefulBatchExecutor.cs:391-393`），
但只是自洽地 fail-closed，因此“被钉扎的二进制”在链下只是一个装饰性锚点。
对比链上那把真正被强制执行的 VK 锁。Fix：把已评审的摘要作为常量内嵌
（如 `bridge/neo-zkvm-guest/vk_manifest.rs` 所做的），并断言相等。

**H7 — SDK 未重新编码 .NET 客户端所验证的 commitment** [E3]。
`docs/sdk-conformance.md:26` / `docs/audit/p1-1-rpc-sdk-abi.md:26` 声称 `encoded` 字段会被
解码、重新编码并比对。只有 `src/Neo.L2.Sdk/L2RpcClient.cs:310-337` 确实如此；
`sdk/typescript/src/index.ts:397-416`、`sdk/python/neo_n4_sdk/__init__.py:400-410` 和
`sdk/rust/src/l2_batch_view.rs:41`（它同时接受任意 `proofType` u8）原样取用。
一个怀有敌意或有缺陷的节点可以返回与线字节不一致的结构化字段。
这意味着此前已关闭的 P1-1 发现在四套 SDK 中只关闭了一套。

**H8 — Faucet 的发放上限默认状态下静默失效** [E3]。
`tools/Neo.L2.Faucet.Cli/Program.cs:156-162` 把 journal 默认设为单次进程中的
`InMemoryKeyValueStore`，因此 `FaucetPolicy.Decide` 永远看到 `entry == null`
（`FaucetPolicy.cs:44`），冷却期/生命周期上限从不触发；`--max-per-drip` /
`--max-lifetime` / `--cooldown-seconds`（`:107-109`）没有下限。
`IMPLEMENTATION_STATUS.md:220-221` 却把它描述为"production drip with rate limiting +
RocksDB-persisted journal"。这与“四套 SDK 一份协议契约”式的生产就绪宣称相矛盾。

**H9 — Submodule 被钉扎在可 force-push 的 `codex/*` 分支上；gitlink 门禁只覆盖其中一个** [E1+E2]。
在本 checkout 上执行 `git -C <sm> branch -r --contains HEAD`：

| Submodule | 钉扎 commit 仅可从以下分支到达 |
| --- | --- |
| `external/neo` | `origin/r3e/neo-n4-core` ✅ 规范分支 |
| `external/neo-devpack-dotnet` | `origin/codex/system-audit-devpack` |
| `external/neo-riscv-vm` | `origin/codex/rustsec-2026-refresh` |
| `external/neo-vm-rs` | `origin/codex/rustsec-2026-refresh` |
| `external/neo-zkvm` | `origin/codex/rustsec-2026-refresh` |

这些是 agent 起草的临时分支，而它们之中包含 **nccs 合约编译器**
（`build.yml:151` "Build pinned nccs from the audited devpack submodule" — 即发出
每一个持有资金的 NeoHub 合约字节码的那个工具）以及整个出证/VM 栈。
对其中任何分支的一次 force-push 都会静默改变所有 artifact 的含义，且不存在
任何评审边界。由两名评审者独立发现。`scripts/ci/verify_neo_core_gitlink.py:61`
**只**检查 `Path("external/neo")`，于是 CI 打印一条绿色的
"Verify Neo gitlink is published on the official core branch"，而 5 个构建关键 gitlink
中有 4 个不受门禁约束。Fix：把这些 commit 快进到受保护分支，重指向 gitlink，
并把该门禁推广到每一个 submodule。

**H10 — 生产热路径上的无界增长** [E2 针对 H10a]。
- （a）`src/Neo.Plugins.L2Rpc/InMemoryL2RpcStore.cs:18-25` — `_batches`、`_statuses`、
  `_stateRoots`、`_deposits` 是以单调递增 batch 号为键的 `ConcurrentDictionary`，
  整个文件中没有任何逐出逻辑（唯一的 `TryRemove` 在 `:205-207`，维护的是 L1↔L2
  地址映射）。我已确认这是生产路径：`OpenFromChainDirectory`（`:92`/`:125`）被
  `src/Neo.Plugins.L2Settlement/MultisigLocalHostComposition.cs:130` 调用。
  只有提取/message proof 会进入 RocksDB。
- （b）`src/Neo.L2.Messaging/RpcMessageRouter.cs:52,282` — `_locallyConsumed` 永不裁剪，
  与 `_knownNonces`（`:349`）不同。[E3]
- （c）`src/Neo.L2.Executor/ApplicationEngineTransactionExecutor.cs:60` — `_consumedNonces` 是
  实例级 `HashSet`，只有 `.Add`（`:133`），从不清空也不持久化，而它在 `:126-128` 的
  注释却声称"at most once **per batch**"。要么注释错，要么作用域错；在一个热的
  执行器上重新执行同一 batch（witness 重生成、崩溃重 seal、
  `TryRetryPendingSealedBatch`）会产生虚假的 `duplicate nonce` → `Success=false`、
  `Gas=0` → 对相同输入得到不同的 `ReceiptRoot`/`PostStateRoot`，这会让诚实的
  sequencer 看起来像作恶者。同时也是无界内存增长。同一字段也存在于
  `RiscVTransactionExecutor.cs:53`。

**H11 — 每个 batch 对全量状态重新计算 root，且发生在提交线程上** [E3]。
`src/Neo.L2.Executor/MerkleStatePostStateRootOracle.cs:58-60` 枚举整个 KV store，
每个 batch 重新排序并重新哈希每一个 leaf；经由 `L2BatchPlugin.cs:582-583` 的
sync-over-async 从 `Blockchain.Committed` 同步到达。每 batch 的成本随状态总键数增长，
终将超过出块间隔。`Sp1StateWitnessSource.cs:238-254` 还会第二次克隆全部状态
（`entries.Select(e => (e.Key.ToArray(), e.Value.ToArray()))`），因此每个 batch 的
峰值内存 ≈ 全量状态的 2-3 倍且都在 LOH 中。Fix：增量式 dirty-key root；就地哈希。

**H12 — 不可逆锁模式只应用于 7 个治理表面中的 4 个，因此一个“已锁定”的生产部署
在其信任根上仍可被 owner 改写** [E2 已核实]。
> **状态 — 已于 2026-08-29 修复（本报告写就之后）。** `OptimisticChallenge`、
> `ExternalBridgeRegistry` 和 `MpcCommitteeVerifier` 各自获得了单向 `LockGovernance()`
> （由 GC 接线的前置条件、同时冻结 `SetGovernanceController`、`GovernanceLocked` 事件），
> 且下方点名的每一条即时 owner `Register*`/`Revoke*` 路径现在都断言 `!IsGovernanceLocked()`。
> `OptimisticChallenge` 原本完全没有 §16 表面，因此它新增了一套 ——
> `SetGovernanceController` 加上 proposal 门控的
> `RegisterFraudVerifier` / `RegisterPermissionlessFraudProfile` / `RevokeFraudVerifier`
> 孪生方法（撤销之所以受 council 门控，是因为一次即时撤销本身就是一条
> 拒绝 fraud proof 的路径）。`LiveDeployCommand` 接线 OC controller、调用全部三把锁，
> 并在可续跑调用列表与冒烟检查中都做回读断言；`ScaffoldPlan` 与
> `docs/launching-an-l2.md` 不再把 escrow 指针锁宣传为全覆盖。锁后的
> 仅管理态行为已在 VM 测试中钉扎（fraud proving 仍然罚没；有效的委员会 attestation
> 仍然验证通过）。下方行号是修复前的 HEAD `728e630a`。
`LockGovernance()`（单向，冻结即时 owner 路径）恰好存在于四个合约中 —
`ChainRegistryContract.cs:389`、`VerifierRegistryContract.cs:108`、`SettlementManagerContract.cs:268`、
`ExternalBridgeEscrowContract.cs:239` — 而 `LiveDeployCommand.cs:833,853,855` 调用了其中三个。
那三个决定*谁可以证明一次欺诈*以及*谁可以为外部链充值背书*的合约没有这道门：
- `OptimisticChallengeContract.cs:206` `RegisterFraudVerifier` 与 `:235`
  `RegisterPermissionlessFraudProfile` 只需 owner witness 即可即时写入。`:247-270` 的
  v4 自省只是从候选合约重读 `getSettlementManager` / `getExecutorSemanticId` / `getReplayDomain`，
  一个专门伪造的合约可以平凡地返回它们，所以它是防笔误而非防作恶。一旦被注册，
  `Challenge`（`:386`）会罚没整个开放窗口的全部 bond 并驱动
  `SettlementManager.RevertBatch`（`SettlementManagerContract.cs:542`，其
  `challengeAuthorized` 分支按设计在 `LockGovernance` 之后仍然有效 — 见 `:263-266`
  的 doc-comment）。
- `ExternalBridgeRegistryContract.cs:122` `RegisterVerifier` 即时写入
  `externalChainId → verifier` 分派表；额外检查只有 `0xE0_xx_xx_xx` 命名空间，
  以及候选者自身的 `bridgeKind()` 与所请求者匹配（`:240-256`）。随后
  `ExternalBridgeEscrow.Receive` 经由该表路由
  （`ExternalBridgeEscrowContract.cs:553-557`），因此把某条外部链指向一个宽松
  verifier 就会让 `Receive` 支付任意金额。
- `MpcCommitteeVerifierContract.cs:129` `RegisterCommittee` / `:148`
  `RegisterCommitteeWithMembers` 以同样方式替换签名集合。

这三个*都*另外暴露了 proposal 门控的变体
（`UpgradeVerifierViaProposal` `:132`、`RegisterCommittee*ViaProposal` `:162`/`:199`）且
`LiveDeployCommand.cs:845-851` 会为它们接线 `GovernanceController` — 所以安全路径存在，
但它永远无法被*强制执行*，因为没有任何东西阻止此后再走即时路径。
`Escrow.LockGovernance` 冻结的只是 escrow *指向* registry 的那个指针，
这正是“锁住的门、没锁的合页”那种情形。这既是安全缺陷，也是设计一致性缺陷：
仓库自己的 `ScaffoldPlan.cs:417,460,501,505` 把 `LockGovernance` 称为
"the irreversible production gate"，因此照着 scaffold 操作的运维者有理由相信
整套锁已被冻结。事实并非如此。
Fix：为 `OptimisticChallenge`、`ExternalBridgeRegistry` 和 `MpcCommitteeVerifier`
增加同样的单向 `LockGovernance`，让它禁用直接 `Register*`，并从 `LiveDeployCommand` 调用。

**H13 — 紧急 kill-switch 只覆盖三个持有资产合约中的一个，与它自己文件头所述的
不变式相矛盾** [E2 已核实]。
`EmergencyManagerContract.cs:11-13` 记录着 "Other NeoHub contracts consult `IsPaused` before
mutating state."。在仓库范围内搜索 `IsPaused` 只找到一个消费方 —
`SharedBridgeContract.cs:131-135`，它守卫 `:152`（deposit）、`:244`、`:280`、`:324`，
外加 `:361` 与 `:551` 两处仅限暂停的逃生阀。在
`ExternalBridgeEscrowContract.cs` 与 `SettlementManagerContract.cs` 中 grep
`paused|Paused|emergency` 完全找不到暂停守卫：
`ExternalBridgeEscrow.Receive`（`:484`）继续释放外部桥资金，
`SettlementManager.SubmitBatch`/`FinalizeBatch` 在 `IsPaused()` 为真时继续接受并结算 batch。
因此这个被记录为 "the global pause flag" 的运维动作
（`EmergencyManagerContract.cs:103`、`doc.md:133` "提供 emergency pause"）并不能阻止
事件响应者所需要阻止的三件事中的两件，而 `EmergencyManager` 自己的逃生阀
（`:143,:185`）也无法从这两者中转移任何东西。Fix：为 `Receive`、`SubmitBatch` 和
`FinalizeBatch` 增加该守卫（或者把 doc-comment 与 `doc.md` §15.5 收窄为
"SharedBridge pause only"，并明确说明哪些仍然保持可用）。

---

## 5. Medium 发现（分组）

**确定性 / 正确性**
- `MerkleStatePostStateRootOracle.ResolveAsync` 忽略 `preStateRoot`、`receiptRoot` 与
  `blockContext`（在 `:52-56` 的注释中已承认），并针对**活动** store 计算 root；
  `RocksDbKeyValueStore.EnumerateInternal:268` 打开迭代器时没有 RocksDB snapshot 且
  不取 `_writeGate`，因此并发写入者在迭代中途是可被观察到的。在默认的单一提交线程
  拓扑下这不会触发，这正是我把它评为 Medium 而非评审者提议的 Critical 的原因；
  它是一个潜在隐患，需要一个 snapshot 或同一把门，外加一个 fail-closed 检查，
  断言推导出的 pre-root 与 `request.PreStateRoot` 相符。[E2]
- `BatchSealer.cs:387-394` 为多达 50 个区块构建**一个** `BatchBlockContext`，且
  `BuildPersistingBlock:237-238` 设置 `Index = L1FinalizedHeight` 和一个共享时间戳，
  因此多区块 batch 中的每一笔交易看到的都是同一个 `Runtime.BlockIndex`/`Timestamp` —
  多区块 batch 不可复现。[E3]
- `ExecutionStateTransaction.Commit:197-201` 用逐条 `Put` 调用加上手写的补偿来应用变更，
  尽管 `IAtomicL2KeyValueStore.CompareExchangeBatch`（原子的且 sync 的）早已存在，
  并在别处被正确使用（`ProofWitnessStore`）。[E3]
- `ApplicationEngineTransactionExecutor.cs:157-161,197-201` 把*环境性*故障
  （`DllNotFoundException`、`Commit` 期间的瞬时 IO）转换成一条共识可见的失败 receipt
  并馈入 `ReceiptRoot`；`:165-168` 针对 runner 故障的处理已经是正确的。
  `RiscVTransactionExecutor.cs:155-159,225-230` 是同一形状。[E3]
- `StateWitnessV1.MaxEntries = 65_536`（`:133`）为**完整**状态快照设了上限，与多 GB 的
  RocksDB 状态相矛盾；而 `MaxKeyBytes = 4096`（`:136`）对比
  `KeyedStateMerkleTree.MaxKeySize = 1024`（`:53`）意味着一个 2 KiB 的键会通过
  witness 校验，随后在 `ComputeRoot` 内部抛异常。[E3]
- `BatchBuilder.SealArtifact:138-170` 丢弃 `Batch.Withdrawals`、`L2ToL1Messages`、
  `L2ToL2Messages`，使得 `AddWithdrawal`/`AddL2ToL1Message` 对被 seal 的 artifact 成为
  静默 no-op，其对应的 sealer 指标（`BatchSealer.cs:139-151`）永远为 0。
  `BatchSerializer.Encode` 缺少其自身 `Decode:145` 所强制的 `lastBlock >= firstBlock` 检查；
  `MessageHasher.EncodeMessage:38` 没有 payload 上限，而 `DecodeMessage:75` 拒绝 >1 MiB。[E3]
- `SettlementManagerContract.cs:1012` — `return storedRoot.Equals((UInt256)current);` 省略了
  两个同类验证器都强制的 `index == 0` 终止条件（对照 `MessageRouterContract.cs:603`，
  它有这个检查），于是 proof 只需到达*某个*等于已存储 root 的祖先节点即可。
  [E3 — 值得针对链上折叠再确认]
- `SettlementManagerContract.cs:444-466` — `firstBlock`/`lastBlock` 不在 332 字节的
  public-input 原像之中，因此被证明的 root 并不绑定所声称的区块区间。[E3]
- `StateRootCalculator.cs:88-99` / `hashing.rs:297-314` — public-input 原像没有
  version/profile/domain 字节，attestation payload 也不带 epoch 或过期时间，因此
  Stage-0 attestation 在字节上完全相同，并且在未来所有 profile 下永久有效。
  阈值算术本身是正确的（先去重再验证、`seen.Count < Threshold`、构造函数拒绝
  `threshold > N`）。[E3]
- `RiscVProofPayload.cs:93-96` 接受 `ProofSystem` 字节 0，而路由器要求 1..4
  （`ContractZkVerifier:355-358`）— 解析分歧，活性风险。[E3]
- `AssetRegistry.cs:50-52` — 重指向某个 L2 资产会静默逐出另一个
  `(L1Asset, L2ChainId)` 映射，使一个仍在接受充值的 L1 资产成为孤儿。[E3]
- `ContractZkVerifierContract.cs:334-346` 的 envelope-only 模式在不做任何密码学校验的
  情况下返回 `true`。它默认关闭、存在单向锁，且 `LiveDeployCommand.cs:819-836` 确实
  执行了全部三把锁 — 但这样一来生产可靠性就取决于运维者不跳步，而
  `VerifierRegistry:90-96` 在 `lockGovernance` 之前提供一条即时的 owner `registerVerifier`。
  Fix：除非报告两把锁均已设置，否则让 `SubmitBatch` fail closed。[E3]
- `tools/Neo.Hub.Deploy` 只注册 `ProofType.Zk`，因此 Multisig/Optimistic commitment
  会在 `VerifierRegistry:256` 处失败 — 是 fail-closed，但 §7.5 的 Stage-0/1 结算以及
  整个 OptimisticChallenge/v4 路径在生产中未接线，这与 "All phases ✅" 相矛盾。[E3]
- `L2BatchPlugin.cs:392-393,485` — `_sink`/`_sealer` 在无内存屏障的情况下发布
  （读者可能看到 `_sealer != null, _sink == null`），并且 `ProcessCommittedBlock` 与
  `TryRetryPendingSealedBatch` 都是驱动非线程安全 `BatchSealer` 的公开入口。[E3]
- `BatchSealer.cs:360-373` 约束的是 batch *交易条数*而从不约束字节大小，因此
  `MaxBlocksPerBatch=50` × 大交易可能超过 `MaxEncodedBytes`/DA blob 上限；
  `L2Batch.cs:80` 在 `AddBlock` 时允许 `blockIndex == LastBlock`，同时又吸收该区块的交易。[E3]
- `RpcSharedBridgeDepositScanner.cs:46,110,165` — `finalityDepth = 1` 是默认值，而
  `VerifyResumeHashAsync` 在已终局历史发生变化时*抛异常*，且不回滚已经入账的 nonce，
  因此一次 2 深的 L1 重组会硬停整条流水线，且手工恢复并不安全。[E3]
- `RpcForcedInclusionSource.cs:182,192,249` 与 `RpcMessageRouter.cs:257,267` 用裸的
  `DateTime.UtcNow` 做缓存过期，而代码库其余部分注入 `IClock`/`FakeClock`；一次时钟
  回拨可以把一个陈旧的 drained 集合钉住 — 丢掉强制交易，也就是该队列本为防范的
  审查 — 而且它不可测试。相关：`BatchSealer.cs:340`
  （`if (_forcedDrain is null) return;`）使强制包含纯粹变成可选项，因此一个未配置的
  sequencer 会收取 `EnqueueForcedTransaction` 的手续费却永不包含它们。[E3]

**Sync-over-async / 在提交路径上阻塞** — `L2BatchPlugin.cs:385,387,583,652,655` 与
`L1MessageDrain.cs:53,74`（包括跨网络的 L1 RPC）、`MultisigRoundProver.cs:94-95`、
`L1MessageDrain.cs:652` 的 `DrainAsync(int.MaxValue)` 无界。一个缓慢的 L1 会停住 L2
提交，并通过 H1 变成节点死亡。[E3 多数；`:583` 与 `:652` 两处站点为 E2 可见]

**RPC/指标卫生** — `InMemoryMetrics.cs:95-103` + `PrometheusExporter.cs:132-152`：
内部的 `name{k=v,k2=v}` 键不是单射的，且被朴素的 `,`/`=` 切分重新解析，因此一个包含
`,` 或 `=` 的 tag 值可以丢掉或复制一条序列，而 `ToPromName:129` 不剥离 CR/LF
→ exposition 行注入；`L2SettlementPlugin.cs:791-794` 打上
`("exception", ex.GetType().Name)` 标签；`_counters`/`_gauges` 没有键数量上限。[E3]
`L2BatchPlugin.cs:477` 以裸字面量发出 `l2_batch_on_block_committed_error`，逃逸出
`docs/telemetry.md:126-128` 声称被强制执行的 `MetricCatalog` 反射测试。[E3]

**未鉴权的运维端点** — `MetricsRequestHandler.cs:82-95` 以无鉴权、不区分 HTTP 方法的方式
提供 `/operatorstatus` 与 `/healthprobe`，暴露合约 hash、L1 端点、高度与队列深度。
默认 loopback（`src/Neo.Plugins.L2Metrics/Settings.cs:21,45`），但绑定到非 loopback 时
不发出警告；并且 `external/neo/src/Plugins/RpcServer/RpcServer.json:13` 以
`AllowOrigins: []` 发布 `EnableCors: true` → `AllowAnyOrigin()`（`RpcServer.cs:193-205`），
于是任意网页都能读取 loopback RPC，而 `neo-stack init-l2` 从不写出一个收紧的
RpcServer 配置。[E3]

**CLI 安全** — `tools/Neo.Stack.Cli/Commands/ArgUtil.cs:7-15` 静默忽略未知选项，并对
一个尾部无值 flag 返回默认值；在 `--broadcast` 路径上
（`OperatorPlanCommands.cs:182,400,568`）一个拼错的 `--chain-id`/`--output`/
`--settlement-manager` 会从一个非预期的配置产生一笔已签名、已广播的 L1 交易，
且没有任何诊断。[E3]

**Windows 密钥加固** — `tools/Neo.External.Bridge.Cli/Commands/GenKeyCommand.cs:88-97`：
0600 加固仅限 POSIX，因此在 Windows 上 `watcher.priv` 继承 `BUILTIN\Users:R` 且不会
尝试任何 `FileSecurity`；`priv` 从不 zeroize（`:56`），而 `--print-priv`（`:100`）
未经确认就把密钥回显。同样的“仅限 Unix”不对称性也见于
`AtomicFileQueueTransport.cs:303-321`。[E3] — 由于本次审计在 Windows 上运行，此项有实质相关性。

**生命周期** — `L2RpcPlugin.cs:97-118` 释放了适配器（`L2RpcServerAdapter.cs:55-62`）但
从不把它从进程静态的 `RpcServerPlugin.handlers`
（`external/neo/.../RpcServer.cs:25,137-143`）中移除，于是同一进程上后续创建的
RpcServer 会永久重复注册一个已释放的适配器，全部 10 个 L2 方法都会失败。[E3]

**效率** — `RocksDbKeyValueStore.Count:78-90` 是一次全库迭代，却以 `LeafCount` 暴露；
`L2DataCacheAdapter.cs:126-133` 通过从键 0 开始枚举来“seek”（反向 seek 会先物化再
`.Reverse()`），使 native 合约的枚举器变成每区块 O(N²)；
`ExecutionStateTransaction.GetCore:240` 每次读取都分配一个 `key.ToArray()`；
`ProofWitnessStore.QuarantineRevertedTailAsync:888-964` 在一把锁下复制整个 store，
CAS 重试时最多 8 次；`Sp1StateWitnessSource.CommitTransition` 调用 `Capture` ≥5 次，
而 `CompareExchangeAll` 每个 batch 重写整个 DB。[E3]

**供应链** — 没有根 `rust-toolchain.toml`，而 `external/neo-riscv-vm` 钉扎
`channel = "stable"`；Actions 按版本 tag 而非 SHA 钉扎（`actions/checkout@v7`、
`dtolnay/rust-toolchain@1.88.0`、可浮动大版本的 `foundry-rs/foundry-toolchain@v1`）；
`forge install --no-git foundry-rs/forge-std` 拉取默认分支（`lib/` 被 gitignore）；
Python extras 解析 `cryptography>=44,<47` 且没有 lockfile；
`build-watcher-image.yml:51-53,113-115` 以 `packages: write` 发布到 GHCR，
既无 cosign 签名也无 SBOM；没有仓库级 `NuGet.config`；
`external/neo-riscv-vm` 未随附 LICENSE，而 `src/Neo.L2.Executor.RiscV` 针对它构建。[E3]

**文档一致性** — `repository-coverage-ledger.md:11`（R3，状态 `closed`）断言
`ExternalBridgeRegistry` 的 bridge kinds“gate production registration”；该合约在 `:252`
只有一条自洽性 `Assert(declaredKind == bridgeKind)`，没有任何 CI 强制，而
`SECURITY.md:97-98` 的措辞是正确的 "Deploy CI *should* refuse"。
实际只存在一项愿景，文字却把它写成了已交付的控制。[E3]
`IMPLEMENTATION_STATUS.md` — 36 行按项目测试计数中有 21 行陈旧，例如 L2Settlement
`:348` 声称 73 / 实际 159，L2Batch `:345` 48→66，Sequencer `:337` 35→48；有两行
**高估**：`L2.Persistence` `:351` 声称 70 / 实际 53（`[DataRow]` 展开并不解释这一差异）
以及 `L2.State` `:330` 120→89。合计声称约 2,041，实际 2,724。[E3]
双语对齐：全部 39 个 `docs/*.md` 都有一个 `docs/zh/` 文件，但四个根镜像只是摘要 —
`docs/zh/IMPLEMENTATION_STATUS.md` 是英文体积的 9%，`AGENTS.md` 20%（没有
canonical-encoding 章节、没有 Don'ts，对 `little-endian`/`91` 零命中）、
`SECURITY.md` 32%、`CHANGELOG.md` 2.3% — 而它们自己的文件头强制要求不得发散，
且执行该要求的测试（`UT_ProductionGapClosure.cs:175-235`）只检查 5 个具名文件中的
短语存在性，这使得 `docs/test-coverage.md:13` 在实质上具有误导性。[E3]
`docs/architecture-wire-formats.md:220-221` 引用了失效路径
`src/Neo.L2.Proving/MultisigProofPayload.cs` / `RiscVProofPayload.cs`（实际位于
`Attestation/` 与 `RiscVZk/` 之下）。[E3]
陈旧计数：`AGENTS.md:58` "16 core off-chain libs" → 实际 17（27 个 `src/` 目录、
8 个插件、2 个 `*.Rpc`）；`AGENTS.md:168` "12 CLI subcommands" → 实际接线 13
（`bootstrap-genesis` 未被计入），而“那 3 条钱包命令只打印 plan 而不执行钱包侧提交”
现在是**错的** — 三条都会广播并在链上验证
（`OperatorPlanCommands.cs:182-217,400-460,568-596`）。
`repository-coverage-ledger.md:22` 引用了不存在的 `external/upstream`。[E3]

**L1 合约 — 资产损失 / 活性（已评审 26 个项目；下列条目均已在源码中核实）**
- **无 delta 转账导致的充值超额入账。** `SharedBridgeContract.cs:184-203` 用
  `asset.transfer(...)` 拉取 token，随后按*声称*的 `amount` 调用
  `IncrementLocked(targetChainId, asset, amount)`；没有 `balanceOf` 前后对比，而
  `OnNEP17Payment`（`:210`）断言金额为正，却从不把它与实际入账值比较。
  `ExternalBridgeEscrowContract.TransferIntoCustody`（`:770-791`）已经做的正是正确的事
  （`balanceAfter == balanceBefore + amount`），所以仓库知道这个模式，而守护按链记账的
  那座桥恰恰是省略它的那一座。因此一笔带手续费的 transfer 型或 rebase 型资产会把
  `locked` 记到高于真实托管的值 → 超额提取并抽干该链的 escrow。
  已找到并纳入考量的缓解因素：`Deposit` 拒绝未映射/未激活的资产
  （`:164-172`，`getL2Asset` + token registry 上的 `isActive`），所以利用需要运维者
  主动映射这样一个资产 — 因此是 MEDIUM，而不是 Critical。同一形状见
  `SequencerBondContract.cs:187` 与 `ExternalBridgeBondContract.cs:176-179`。
  Fix：复用托管 delta 检查。[E2]
- **envelope-only 对 proof system 2-4 仍然可达。** `ContractZkVerifierContract.cs:344-346`
  在某个 proof system 未配置 verifier 合约且设置了 envelope-only 标志时，返回 `true`
  且不做任何证明运算。该标志默认关闭（`IsEnvelopeOnlyAllowed:196-200`），
  被记录为仅限 devnet（`:169-174`），并且存在单向的
  `DisableEnvelopeOnlyPermanently` + `LockProofSystemConfiguration` — 但
  `LiveDeployCommand.cs:827-831` **只**对 SP1 应用它们，而 `SettlementManager`
  （`:369-375`）在为 `SecurityLevelValidity` 链接受 `ProofTypeZk` 之前从不要求任何一把锁。
  于是 Risc0/Halo2/Axiom 距离“无证明结算”始终只差一次 owner 调用。
  Fix：在 SM 中当 `securityLevel ≥ 3` 时断言
  `isEnvelopeOnlyLocked && isProofSystemConfigurationLocked`，并在部署中锁定全部四个系统。[E2]
- **`MessageTypeCall` 被消费后被丢弃。** `ExternalBridgeEscrowContract.cs:533-537` 接受
  message type 0/1/2，`:561-565` 在副作用之前写入重放键，而 `:567-571` 只对类型 0 和 2
  付款 — 一次纯调用会烧掉 `(externalChainId, neoChainId, nonce)` 而什么都不交付，
  且没有退款路径。Fix：在分派存在之前拒绝类型 1。[E2]
- **`Send` 既不校验路由也不退款。** `ExternalBridgeEscrowContract.cs:425` 既不校验资产
  路由也不校验映射是否激活，而 `Receive` 的对称强制确实存在，
  `SharedBridgeContract.cs:156-172` 在充值侧也做了强制；走陈旧路由的出站资金会被搁死。[E2]
- **背书消息绑定网络但不绑定兑付合约。** 更正评审者的表述：被签名的原像*确实*携带
  目标域 — `ExternalMessageHasher.cs:46-57` 序列化 `ExternalChainId`、`NeoChainId`、
  `Nonce`、`Direction`、`Sender`、`Recipient`、deadline、`SourceTxRef`、类型与 payload
  长度，且 `ExternalBridgeEscrowContract.cs:499-508` 断言内嵌的 `neoChainId` 等于
  escrow 自身的值。真正的残余是没有任何东西把*兑付合约的身份或 L1 网络*混入原像
  （`MpcCommitteeVerifierContract.cs:439` 针对裸字节做验证；consumed key 位于某一个
  escrow 的存储中，见 `:547`），因此一份 M-of-N attestation 在每个共享委员会密钥的
  部署中都可兑付一次 — 也就是密钥复用下的 testnet→mainnet 重放，或一次重新部署的
  escrow。这是一项运维密钥管理隐患加上一个缺失的域标签，而不是一条签名伪造路径。
  Fix：把 `Runtime.ExecutingScriptHash` 与 L1 chain magic 作为前缀混入被签名的字节。[E2]
- **未定价的、以攻击者为键的存储增长。** `MessageRouterContract.cs:126-146` 的
  `EnqueueL1ToL2` 是无需许可的，且写入最多 128 KiB 而不收协议费（对照
  `ForcedInclusionContract.cs:365-400`，它收费）；合约支付存储 GAS，因此余额可能被耗尽，
  使诚实流量 FAULT。`SettlementManager` 同样为每个 batch 存储一整个 ≤1 MiB 的带 proof
  头部（`:383`）且不做修剪。对攻击者并非免费 — 他们要付正常的每笔交易 GAS —
  但强加给共享合约的成本超过调用者自身承担的成本。[E2]
- **`ForcedInclusionContract.cs:481-504` 的骚扰面（从评审者的 MEDIUM 下调）。**
  `ReportCensorship` 无法作用于任意链：它要求先前存在一条已付费的
  `EnqueueForcedTransaction` 条目，拒绝调用方自带的 sequencer 归因（`:483-485`），
  且只有当 `nowSec ≥ deadline` 时才暂停（`:496-498`）— 而 sequencer 只需把该强制交易
  包含进来即可拆弹。真正的残余是 `ResumeChain` 之后可重复的、已定价的
  暂停/恢复抖动；那是 LOW，而无需许可的暂停权限本身值得它已经携带的那条设计注释。[E2]
- 合约测试表面恰好在 High 所在之处最为单薄：
  `tests/NeoHub.Contracts.VmTests/UT_SharedBridge_Vm.cs` 有 3 个 `[TestMethod]`，
  `UT_L2PayoutAdapter_Vm.cs` 有 1 个，缺少重放/二次 finalize、伪造 leaf、暂停门控或
  `MigrateLockedBalance` 鉴权用例，且没有任何测试覆盖 owner 安装的敌意 verifier 或
  committee（H12）、`MessageTypeCall` 损失，或 `SecurityLevelValidity` 下的
  envelope-only 接受。[E1 已计数]

---

## 6. Low / 信息性

除了环境故障→receipt 混同（§5）之外，其余条目都属于卫生问题：
`SharedBridgeContract.cs:68` 断言 `settlementManager.IsValid` 却不像其邻居 `:67`
那样断言 `!IsZero`；`eth_rpc_event_source.rs:47` 的 `self.chunk_size - 1` 在
`eth_chunk_size = 0` 时下溢，而 `build()` 中没有校验；`poll_config.rs` 的
`poll_interval_secs = 0` 会忙等，`backoff_initial_secs = 0` 永不增长（`*2`）；
`FaucetPolicy.cs:46` 在未来日期的 journal 时间戳上发生 `ulong` 下溢从而绕过冷却期；
`OperatorPlanCommands.cs:541` 无界的 `File.ReadAllBytes`；`StartOperatorCommands.cs:56,78`
是已死的 `GetAwaiter().GetResult()` 包装，而 `:371` 吞掉 `NullReferenceException`；
`ExternalCommandTransactionSigner.cs:178` 丢弃适配器 stderr，与
`docs/operator-signer-command-protocol.md:53` 相矛盾；`L2MetricsPlugin.cs:113-143` 的
"must be installed before Start" 未强制执行；`bridge/neo-zkvm-host/src/lib.rs:120-121` 的
`prove()` 针对由它自己 `setup()` 派生的 VK 做验证，而不是 `NEO_ZKVM_GUEST_VK_BYTES32`，
只能通过 `build.rs:123-126` 的 ELF hash 闭合该环路；`Sp1BatchProofProver.cs:151-153`
接受一个已存在的结果文件，而 `:242-252` 不做任何本地 proof 合理性/配对检查，
因此畸形 proof 会消耗 L1 gas；`ReadBoundedPathAsync:261-265` 存在一个
`FileInfo.Length`→`ReadAllBytesAsync` 的 TOCTOU 分配窗口；
`NeoExternalBridgeRouter.sol:417` 使用裸 `ecrecover`，既不检查高 `s` 也不检查
`v∈{27,28}`（签名可塑性；重放在 `:326,:356` 以 nonce 为键，且
`MAX_COMMITTEE_SIZE=64` 使 `seenBitmap` 处于范围内，因此不影响资金）；
`Sp1StateWitnessSource.cs:271` 把
`Manifest.ToJson().ToString()`（Newtonsoft 的顺序/版本敏感性）提交进 state root，而
`:71,76` 使用一个每进程随机的 `Dictionary<byte[],…>` 比较器（只因为 root 会重新排序
才无害）；`KeyedStateMerkleTree.cs:26-35` 关于“无域标签”的论证低估了一条约 2⁶⁴ 的
磨题路径；`docs/architecture-wire-formats.md:59` 的 alt-text 说 "16 fields"/"9 shared"
而代码中是 14/9；带日期的审计报告仍然呈现 "1,430 passing tests"
（现为 2,724+ 方法声明 / 2,869 已执行）。

合约层面的 LOW，均不破坏可靠性：`firstBlock`/`lastBlock` 被排除在
`publicInputHash` 之外（`SettlementManagerContract.cs:444-466`、`StateRootCalculator.cs:88-97`），
使头部偏移 12-27 不受 proof 认证，而 `ContinuityCheck.cs:66` 把它们当作权威；SM 的
`SubmitBatch` 只把 commitment 约束为 `Length >= ProofBytesOffset`（`:321`）— 精确的
`321+proofLen == length` 检查在 optimistic（`:1343`）与 ZK（`ContractZkVerifier:311`）
路径上都有，唯独 Multisig 没有，于是一份背书的 commitment 可以携带未受约束的尾部字节；
SM 的 leaf-proof 循环（`:989-1012`）省略了 `MessageRouter:603` 所具有的
`index == 0` 规范性检查；`SequencerRegistry:142` 与 `ForcedInclusion:354` 接受未注册的
`chainId`；`ForcedInclusion:537` 把罚没奖励支付给 `CallingScriptHash`。[E2]

---

## 7. 可证明稳健的部分（而且这一部分异常坚实）

**编码纪律。** 全部 9 个被钉扎的编码器都存在，且每一处记录在案的精确定小与代码
完全一致：91 字节的 `L2ChainConfig` 在三个地方字节级相同（链下
`L2ChainConfigSerializer.ConfigSize = 4 + 20*4 + 7`、链上 `ChainRegistry.ConfigSize`、
`docs/architecture-wire-formats.md:97`），包括字段顺序与 §16.2 的 7 个尾部字节；
`PublicInputsSize` 332、`CommitmentFixedSize` 321（含 1 MiB proof 上限）、
`ExternalMessageHasher.FixedPrefixSize` 102、`DepositPayload` 最小 44 字节。
通篇 little-endian；每个变长字段都是 4 字节 LE 长度前缀；leaf 原像是
`[4B klen][k][4B vlen][v]`，因此 `a|b` 无法与 `ab` 相撞；
`HashEntry`/`HashLeaf`/witness 树在三个调用点产生字节相同的 leaf；所有解码器都拒绝
尾部多余字节；`BigInteger` 金额为最短长度无符号 LE（无可塑性）。
未发现任何链下↔链上的布局错配。

**record 相等性。** 每一个含 `ReadOnlyMemory<byte>` 字段的在范围内 record
（`L2BatchCommitment`、`CrossChainMessage`、`DAPublishRequest`、`DAReceipt`、`ProofRequest`、
`ProofResult`、`ExecutionPayloadV1`、`ProofWitnessArtifactV1`、`StateWitnessEntryV1`、
`ContractWitnessV1`、`CanonicalStorageChange`、`CanonicalExecutionEvent`、
`Sp1StateWitnessSnapshot`）都以 `SequenceEqual`/`AddBytes` 正确覆写了
`Equals`/`GetHashCode`，正如 `AGENTS.md` 所要求。

**根路径上的确定性卫生。** 没有任何 `Dictionary<,>` 枚举馈入 root；任何 root 路径中
都没有 `DateTime.Now`、未播种的 `Random`、`Parallel.*` 或对 `Task.WhenAll` 顺序的依赖；
没有区域设置相关的格式化；`UInt256`/`UInt160` 按内容比较与哈希；
`LexicographicByteArrayComparer` 使用 `SequenceCompareTo`，与 RocksDB 的
`BytewiseComparator` 一致，因此状态排序是可靠的；`MessageTree`/`WithdrawalTree`
在 `Add` 时正确使其缓存树失效；`ExecutionStateTransaction` 的 overlay 隔离与
锁纪律未见任何顺序倒置。

**proof 可靠性（最关键的部分）。** 全部 12 个 public input 都在
`SettlementManagerContract.cs:446-462` 被绑定，包括 post-state root、batch 号、
chainId 与 previous root，连续性在 `:349-351`，重算哈希相等性在 `:357-360`；
链下镜像是 `VerifierRegistry.cs:76-105`。guest 会重新执行并*拒绝*不实声称
（`batch.rs:90` pre-root、`:236-243` `ClaimMismatch`、`main.rs:19` 提交计算所得哈希）。
VK 路由被锁定（`ContractZkVerifier:322-324`、terminal `:86-91`，而 `:104-109` 把
vk + pv-digest + exit + vkroot + nonce 折叠进配对运算实际消费的线性组合，见
`:115-121`，并在 `:140-150` 做规范化 Fr 检查）。Optimistic 窗口被钳制在
`[60s, 7d]`、单次触发、`claimId` 有重放防护、CEI 正确、verifier 走 allowlist、
畸形 proof `return false`，而 v4 verifier 确实重新执行并要求
`expectedPostRoot != committedPostRoot`。Attestation 阈值算术无 off-by-one。

**原子性是真实的，不是宣传。** 单个 `WriteBatch` + `SetSync(true)` + 全量 snapshot CAS
（`RocksDbKeyValueStore.cs:32,237-256,291-304`）；artifact 字节在 commit 之前已验证
（`Sp1StatefulBatchExecutor.cs:170-178`）；commit 后重算 root
（`Sp1StateWitnessSource.cs:163-172`）；`ProofWitnessStore` 正确使用了原子且 synced 的
路径。非 sync 的 `Put`/`Delete` 默认值是一个**刻意的、有记录的**选择 —
`RocksDbKeyValueStore.cs:20-26` 说明了这一点，且 `TASKS.md` 已把
"RocksDB doc/code drift" 作为已解决关闭，并给出运维者覆盖路径。这不是缺陷。

**不可信输入加固。** `ManifestWireReader.ReadLengthPrefixedBytes`/`EnsureAvailable:1822-1855`
在分配之前同时以最大值*和*剩余缓冲区长度约束攻击者提供的长度；`StateWitnessV1`
上限 128 MiB / 1 MiB；`wire.rs:1114-1129` 在 `with_capacity` 之前拒绝虚增的计数
（无长度前缀分配 DoS）；`NeoFsRestDABackend.ReadBoundedAsync:301-337` 先检查
content-length，再用 `ArrayPool` 流式读取；`AtomicFileQueueTransport.cs:272-286`
带有 path-traversal/reparse 守卫与有界读取。

**RPC 边界。** 全部 10 个 L2 RPC 方法都是只读的 — 仓库中唯一的 `[RpcMethod]`
在 `L2RpcServerAdapter.cs:25-53`；不存在 submit/prove/settle/sign/mint/faucet/debug/admin
方法。每个参数都做了边界与类型检查（`L2RpcMethods.cs:197-230`：拒绝 NaN/Inf/小数，
拒绝前导零/符号/空白），跨链读取被 `AssertOurChain:188-192` 阻断，无 SQL，
无 shell（`UseShellExecute=false` + `ArgumentList`），没有由 RPC 参数拼出的路径，
`NormalizePath` + 精确 switch 阻断了 `..` 穿越，store 缺失时 fail-closed 的
`method-not-found`（`L2RpcPlugin.cs:87-93,144-145`），`MetricsHttpServer` 把并发
限制在 32 并带 5 秒期限、饱和时返回 503，且任何第一方代码或配置中都没有
`0.0.0.0` 绑定。`doc.md` §14.1 的十方法 ABI 精确成立。

**密码学。** 任何第一方 C#/Rust/TS/Python 中都没有
MD5/SHA1/`RijndaelManaged`/DES/TripleDES/`BinaryFormatter`/MessagePack 处理不可信数据/
`TypeNameHandling`/隐曲线 ECDSA/`SkipCertificateCheck`/关闭 TLS 校验。
Double-SHA256 一致应用（`MerkleTree.cs:172`、`MessageHasher.cs:24,102` 的
`Crypto.Hash256`；`hashing.rs:18-24` 的 `hash256()`），且所有域分隔哈希都会馈入这第二遍，
因此长度扩展不可达；单遍 `SHA256.HashData` 的用途是把完整性对照固定值，不是 MAC。
所有密钥生成/nonce 工作使用 `RandomNumberGenerator`；`new Random`/`Random.Shared`
仅出现在已播种的测试中；`Guid.NewGuid()` 仅用于临时路径与 trace id。
没有提交任何 `.env`/PEM/WIF/NEP2/token；唯一命中是脱敏测试里的
`"Wif": "Kxshouldnotbehere"`。密钥材料只经 stdin 流入并被 zeroize
（`LocalKeyTransactionSigner.cs:54-62,118-123`；`FileSigner` 使用 `Zeroizing` +
low-S 规范化），且 settlement 配置主动拒绝 `Wif`/`SignerWif`/`OperatorWif`/
`PrivateKey` 键（`src/Neo.Plugins.L2Settlement/Settings.cs:270-286`）。

**Rust 纪律。** prover 守护进程在 `#[cfg(test)]` 之外**零**
`.unwrap()`/`.expect()`（全部 4 处都在测试模块的 ≥ 1323 行）；没有任何
`std::sync::Mutex` 守卫跨越 `.await` 持有；`unsafe` 只有 `sigaction`/`flock`/`geteuid`；
迭代器都以 `using` 释放；`src/` 中每一个 `CancellationTokenSource` 都是 `using` 作用域；
无跨 await 持锁；`BatchSealer` 约束 batch 大小、强制交易数（256）与
L1 消息数（1024）；无 socket 耗尽（所有 `HttpClient` 均为实例持有并带 30-45 秒超时）。

**桥接不变式。** `ValidateWithdrawalLeafBinding` 以 chainId 作为第一个域分隔符重算
leaf hash，而 `WithdrawalKey = 0x03+chainId+leafHash` 做了去重，root 只来自已注册的
SettlementManager 并带 `CheckWitness(sm)`，且每一个 `VerifyWithdrawalLeaf*` 都以
`StatusFinalized` 为门 — 不存在 inclusion/settlement 混淆，跨链重放已闭合。
`PrefixLockedBalance` 约束每座 escrow 的按链上限；`Deposit` 符合 CEI；
`OnNEP17Payment` 的 pending-marker 阻止未经请求的转账；`AssetAmount.Scale`
对任何非精确的 down-scale **抛异常**，而 `TokenRegistryContract.cs:94-114`
按资产类型钉扎 decimals，因此不存在静默的小数截断；inbound 外部桥兑付受
`0xE0` 命名空间、带签名的 chainIds、`direction==2`、deadline、类型白名单、
payload 上限、精确长度约束，且 consumed key 写在付款*之前*；MPC 委员会强制
`0 < threshold <= size` 并用 `seenBitmap` 对签名者去重，而 advisory verifier
只能在治理 witness 之后触达 `Slash`；ETH watcher 的游标语义正确
（`set_cursor(block_number)` 而非 +1、`(chain,nonce)` submitted-set、
`AlreadyConsumed` 再确认需要链上 proof、`amount_be_to_le_minimal` ≡ C# 最短 LE、
`AssetAndCall` 是直接拒绝而非猜测），且缺少 `--allow-stub-signer` 时启动硬性拒绝。
约有 71 个以攻击命名的测试（`Replay|Reorg|DoubleMint|Collision|Tamper|
Spoof|Unauth`，含 `UT_BridgeInvariants_PropertyBased`、`UT_External_RealCrypto`、
`UT_MpcFraudProof_RealCrypto`）。

**专业性。** 在 `TreatWarningsAsErrors` 下 0 警告；`dotnet format` 清洁；`mdbook`
清洁；`src/`+`tools/` 中零 `TODO`/`FIXME`/`NotImplementedException`；零被禁止的
`// added for X` 式注释；没有提交的 `bin`/`obj`/`target`/`*.trx`/`*.dll`
（9 处 `src/bin` 命中是 Rust 二进制目标的源码目录）；`__pycache__` 正确 gitignore；
83 KB 的 `IMPLEMENTATION_STATUS.md` 中点名的全部 117 个类形状符号都能解析到
真实声明 — 零幻觉类型；138 个被跟踪的 markdown 文件中 0 条失效相对链接、
0 处缺失插图；每个 `src/` 项目与每个 `tools/` 项目各有一个单元测试项目且
**无例外**；CI 自带自测试的门禁助手（`scripts/ci/tests/test_ci_gates.py`），
并为 Groth16 verifier 准备了真实的正向向量 fixture。

**L1 合约已清偿。** 在每个被检查的接缝上，链下 ↔ 链上编码都字节级一致：
提取 leaf（`MessageHasher.cs:121-133` ↔ `SharedBridgeContract.cs:440-476`，
chainId 优先的域分隔、double-SHA256）、344 字节的 `publicInputHash` 原像与 root 顺序
（`SettlementManagerContract.cs` 把声明的头部偏移 284 处的值绑定到 proof，并在
`:350,516-519` 复查 pre-state 连续性）、321 字节的头部偏移、38 字节的 RISC-V payload，
以及 Gateway roots —— 包括 `SettlementManager:847-848` 处的 `duplicateOdd:true`
精确复现了 `MerkleTree.ComputeRoot` 对奇数 leaf 的自配对，对照
`BinaryTreeAggregator` / `MerklePathRoundProver.Combine` 中的裸提升。
链上 `Neo.Crypto.Hash256` 是忠实的 `Sha256(Sha256(x))`。BN254 interop 拒绝非规范坐标
并强制执行 G2 子群检查，`Bn254Add` 的 GT 乘法是合法的，因此该 verifier 的
3-pairing Groth16 等式成立，且它自身的 `IsCanonicalScalar` 守卫是承重构件；
`TryDeserializeScalar` 的归约风险已闭合。每一条付款路径都满足
checks-effects-interaction（consumed key 先于转账：`SharedBridge:496-514`、
`Escrow:547-559`、`PayoutAdapter:121-131`），且所有外部 verifier 调用都使用
`CallFlags.ReadOnly`（`Escrow:553`、`Challenge:437`）。
`GovernanceController` 的 payload 绑定（`MatchesProposalPayload`、互异的 `neo4-gov:`
标签）、council-epoch 过期、否决权以及 `RotateCouncil` 的完备性都是可靠的，而
`SetAdmissionMode` 只能收紧。MPC 阈值在成员索引去重之后强制执行，因此委员会
blob 不可塑。v4 fraud proof 绑定了 profile、batch、chain 与 `claimId`，而 advisory
v1/v2 与 verifier 的 v3 路径从 `Challenge` 不可达（`:403-422`）。
`RestrictedExecutionFraudVerifier:644` 处的 `unchecked(previousValue + amount)`
由 `CounterChainExecutor.cs:126` 对应。全部 26 个项目中零存储前缀冲突。

就其规模而言，这是一份纪律性显著优于常规水平的仓库。

---

## 8. 维度结论

| 维度 | 结论 | 依据 |
| --- | --- | --- |
| **正确性** | 内核良好，组合处存在缺口 | 编码、root、proof 绑定已核实为精确（E1/E2）。C1 与 C2 是真实缺陷；另有 6 项 Medium 正确性条目属于从未被组合过的跨子系统接缝（多区块 batch context、被丢弃的 withdrawals、env→receipt 混同）。 |
| **专业性** | 卓越 | E1 清洁构建（警告视为错误）、清洁 format、清洁 mdbook、零 TODO、测试项目无一例外的全覆盖配对、自测试的 CI 助手。 |
| **一致性** | 良好，但量化性表述在漂移 | 所有固定字节大小在 code+contract+doc 中一致，且每一个被检查的链下↔链上编码接缝都字节精确（§7）。但 21/36 测试计数行陈旧、库/子命令计数有误、一项控制被夸大为已交付、zh 根镜像在一纸其测试无法强制执行的同步令之下只是摘要 — 而合约侧还有两处 doc 与 code 矛盾：`EmergencyManagerContract.cs:11-13` 陈述了一条 3 个持有资产合约中只有 1 个遵守的不变式（H13），而 `ScaffoldPlan.cs:417,460,501,505` 把 `LockGovernance` 宣传为*那把*不可逆的生产锁，而 7 个治理表面中有 3 个从未实现它（H12）。 |
| **完备性** | 最弱的一维 | "All phases ✅" 对生产接线而言并不成立：`Neo.Hub.Deploy` 只注册 `ProofType.Zk`，Stage-0/1 结算与 OptimisticChallenge/v4 路径未接线，而审查规避逃生阀在缺少手工 pauser 注册时必然失败。约 45 个测试静默不执行（§3.1）。在资金路径上对抗深度并不均匀：SettlementManager/Challenge/DAValidator/MPC/Sp1Groth16 测试套件是真正的篡改与伪造测试，但 `UT_SharedBridge_Vm.cs` 只有 3 个方法、`UT_L2PayoutAdapter_Vm.cs` 只有 1 个，使重放、伪造 leaf、暂停门控与敌意 owner 用例处于未测试状态。 |
| **安全性** | 设计很强，其边界取决于运维者特权而非代码 | 密码学卫生清洁，RPC 表面只读且边界良好，原子性真实，proof 可靠性有良好绑定（E2），Groth16/BN254 路径已验证，每一条付款路径都满足 CEI 与 `ReadOnly` 纪律（§7）。在 26 个合约中未发现任何无特权盗币路径。残余在于中心 owner 的权力未被不可逆地约束：H12 使 fraud-verifier allowlist、外部桥分派表与 MPC 委员会永远可被替换，proof system 2-4 距“无证明结算”只差一次 owner 调用；H13 意味着 kill-switch 无法停下 3 个持有资产合约中的 2 个。另加一处无 delta 的托管入账（§5，Medium，仍未修复），以及 H4 未校验的 RPC log、H9 编译器/prover gitlink 钉在可 force-push 分支上而门禁只覆盖 5 个中的 1 个、H6 装饰性的链下二进制钉扎；再加 fail-open 的 advisory 门禁（§3.4/3.5）。 |
| **效率** | MVP 规模下够用，目标规模下不够 | 每个 batch 对全量状态重算 root 并 2-3× 物化状态（H11）、O(N) 的 `Seek`（`L2DataCacheAdapter.cs:126`）、O(N) 的 `Count`、revert 时整库复制；无界字典（H10）。这些全都是“每 batch 按状态规模”的复杂度，而这恰是一个 L2 最不该有的形态。在 L1 上，未修剪的每 batch ≤1 MiB 头部加上无费用的 ≤128 KiB 收件箱写入，把 GAS 成本压在共享合约而非调用者身上。 |
| **健壮性** | 需要改进 — 可用性是软肋 | H1 使瞬时故障对节点致命；C1 使一个普通使用模式令链停摆；H2/H3 使反审查路径要么坏掉要么危险；重组处理是抛异常而非回退；sync-over-async 把 L1 延迟放到 L2 提交线程上；而事件响应原语本身是部分的（H13），因此“暂停整个网络”会让外部桥付款与 batch 结算继续运行。 |

这些行是按写作当时给出的评分。C1 与 H12 此后已获修复（见其状态注记、§9 第
1–2 项与 §11 附录），这抬起了 Security 与 Robustness 残余项的一部分，
但其余各行未变。

---

## 9. 建议修复顺序

按写作当时记录；第 1 与第 2 项已于 2026-08-29 落地（见 C1 与 H12 的状态注记）。

1. ✅ **C1** — 为收件箱去重键划分命名空间；补上今天尚不存在的充值+路由组合 drain 测试。
2. ✅ **H12** — 为 `OptimisticChallenge`、`ExternalBridgeRegistry` 与
   `MpcCommitteeVerifier` 增加单向 `LockGovernance`，禁用即时 `Register*` 路径，
   并从 `LiveDeployCommand` 调用它。工作量小、机械化，而它就是 fraud-proofing 与
   外部桥分派表上“owner 今天是诚实的”与“owner 将来无法作恶”之间的差别。
3. **H13 + 托管 delta** — 要么在 `ExternalBridgeEscrow.Receive`、
   `SettlementManager.SubmitBatch`/`FinalizeBatch` 中强制 `IsPaused` 不变式，
   要么把该不变式改写为与现实一致；并把 `ExternalBridgeEscrow.TransferIntoCustody` 的
   `balanceOf` 前后检查移植到 `SharedBridge.Deposit`、`SequencerBond` 与 `ExternalBridgeBond`。
4. **H1** — 在 L2 插件上设 `ExceptionPolicy => StopPlugin` + 使用既有重试路径。
5. **H4 / H5** — watcher 的头部交叉校验、`build()` 内的非零确认数下限，
   以及 stub 签名器下不得持久化 submitted 状态。
6. **H9** — 把四个 `codex/*` gitlink 移到受保护分支上；把
   `verify_neo_core_gitlink.py` 推广到每一个 submodule。
7. **§3.1–3.5** — 恢复验证完整性：`FindRepositoryRoot()`、主 Test 步骤上的零跳过守卫、
   `set -o pipefail`、按 lockfile 作用域的 audit ignore、提交进仓库的 native-host 构建步骤。
   成本极低，而它让其余所有结论重新变得可信。
8. **H2 / H3** — 把强制包含的截止期钳制到最大挑战窗口之上，并自注册 pauser，
   在 `IsProductionReady()` 中断言。
9. **C2** — 为 `MerkleTree.Verify` 加位置绑定；增加随机化 Merkle 奇偶一致性测试
   （今天只有固定的 `{1,2,3,4,5,7,8,15,16}` 形状与 4 个手写向量，
   且没有任何测试断言 `siblings.Count == depth(totalLeaves)` 或 bitmap/`LeafIndex` 一致性）。
10. **合约证明/活性加固** — 在 `SettlementManager` 中为 `securityLevel ≥ 3` 要求
    `isEnvelopeOnlyLocked` + `isProofSystemConfigurationLocked`，并在部署中锁定全部四个
    proof system；在分派存在之前于 `Receive` 中拒绝 `MessageTypeCall`；为 `Send`
    增加路由校验与退款路径；把 `ExecutingScriptHash` + L1 chain magic 混入
    attestation 原像；对 `EnqueueL1ToL2` 收费门控。
11. **H10 / H11** — 为 RPC store 与 router consumed-set 增加逐出；把 `_consumedNonces`
    按 batch 定作用域（或让注释与现实一致）；改为增量式 dirty-key state root。
12. **H7 / H8** — 把 commitment 重编码检查移植进共享向量里的 TS/Python/Rust SDK；
    让 faucet journal 成为强制项。
13. **一致性清扫** — 从 TRX 重新生成 `IMPLEMENTATION_STATUS.md` 的计数，修正
    `AGENTS.md` 中两处陈旧计数（16→17 个库、12→13 个子命令，以及如今已不成立的
    “只打印 plan”那句话），重分类 ledger 的 R3，修正两处失效的 wire-format 路径，
    为带日期的审计报告加上 "superseded" 横幅，在 H12/H13 落地前收窄 `EmergencyManager`
    与 `ScaffoldPlan` 的锁声明，并且要么真正镜像那四份 zh 根文档，要么把它们改名为摘要。

## 10. 残余风险 / 本轮未验证

`cargo test --workspace` 无法在 Windows 上运行（§3.3），因此 SP1 host/guest 出证
crate 与两个被 `#[ignore]` 的真实 proof Rust 测试在本轮从未被 exercised；
C#↔Rust 执行器等价性依赖那个已提交的 Groth16 正向向量加上 CI 仅限 Linux 的
`sp1-release-gates`。硬编码的 Groth16 VK 点（Alpha/Beta/Gamma/Delta/IC0-5）与
`digest[0] & 0x1F` 归约未被独立推导 — 通过的 12 项 verifier 测试套件是唯一证据。
`contracts/` 现已在源码层面覆盖全部 26 个项目（§5、§6、§7、H12、H13），但关于它仍有
三件事在本轮未获证明：H12 的 owner 敌意场景如今已有 VM 引脚并被执行
（568 个 `NeoHub.Contracts.VmTests` 用例全绿，包含锁后 challenge/slash 与锁后
attestation 回归），而 H13 的场景仍然一个都没有；`LiveDeployCommand` 的锁时序已被
阅读并在调用列表/冒烟检查层面做了单元引脚，但从未执行，因此没有任何一次部署真正
端到端跑在链上；而 Solidity 一侧（`NeoExternalBridgeRouter.sol`）只是被阅读，
既未编译也未 `forge test`。没有执行任何 L1/L2 部署、`forge test`、`cargo deny`
（未安装；无根 `deny.toml`）、coverage 门禁（`scripts/test-coverage.ps1` 需要 `pwsh`，
本机没有），也没有跑过 NuGet/npm advisory 新鲜度检查 — `cargo audit` 通过
`--no-fetch` 使用了本地缓存的 advisory DB，而 `npm audit` 需要一个 registry 覆盖。
L2 合约 artifact 生成（`nccs`）未运行。

## 11. 附录 — 修复 C1 / H12 过程中浮出的发现（2026-08-29）

这些是落地 §9 第 1–2 项所要求的 E2 读码 + E1 执行过程的产物，不属于最初那一轮。
§10 的最后一行（"`nccs` was not run"）已被 A4 取代，A4 *确实*运行了它。

**A4 — 25 个已提交 VM 合约 artifact 中有 20 个无法从钉扎的 devpack 复现，其中 2 个
相对于其自身未改动的源码已经陈旧** [E1 已实测]。
解码每个 `tests/NeoHub.Contracts.VmTests/TestingArtifacts/*.artifacts.cs` 的 NEF 头部
得到两种编译器来源：**20 个文件**为
`Neo.Compiler.CSharp 3.9.1+5fa9566e5165ede2165a9be1f4a0120c17602697`，
**5 个文件**为 `3.9.1+82117c4799fde63e8c230e9e9696b66d794c6ed7`（`ChainRegistry`、
`ExternalBridgeEscrow`、`L2PayoutAdapter`、`MessageRouter`、`SettlementManager`）。
submodule gitlink 为 `git ls-tree HEAD external/neo-devpack-dotnet` → `82117c47…`，
因此只有*少数派*是由钉扎编译器发出的；多数派来自一个机器全局的
`~/.dotnet/tools/nccs`（`nccs --version` → `3.9.1+5fa9566e…`），它既不是被跟踪
源码树的构建产物，也不存在于干净 clone 中。`AGENTS.md` 把
`external/neo-devpack-dotnet` 呈现为编译器的记录来源，因此该 artifact 集合与
所声明的工具链策略不一致。
此外独立地，重建两个在 `728e630a` 处 C# **未被触碰**的合约
（`NeoHub.ForcedInclusion`、`NeoHub.SharedBridge` — 二者在 `git status` 中均未显示被修改）
仍然重写了它们已提交的 artifact：NEF 从 `7,280 → 7,305` B 与 `8,591 → 8,702` B，
manifest 文本不同，方法与事件*名*集合相同（偏移发生了移动）。两侧携带同一个
编译器戳，因此差异是源码漂移而非工具链 — 那些 artifact 是由更早的源码生成的，
而仓库从未察觉。它本该察觉：manifest 不变式测试会把已提交版本与新发出的版本比对，
但它们在缺少新构建时自我跳过（§3.1），且只在
`NEO_N4_REQUIRE_FRESH_MANIFESTS=1` 下运行（该模式在本机为绿：19/19，
与 CI 的 `--minimum-tests 19` 相符）。所以一个陈旧 artifact 在默认情况下是不可见的，
而一次“绿色”的默认运行并不能证明 `contracts/` 与 `TestingArtifacts/` 彼此一致。
Fix：只从钉扎 submodule 的构建产物发出 artifact（停止依赖全局工具），
一次性重新发出全部 25 个使该集合只有单一来源，并在任何有 `contracts/**.cs` 文件变更的
CI job 中把 fresh-manifest 模式设为强制。

**A5 — C1 的修复移除了 batcher 停摆；router 仍然没有 V1 执行路径，因此端到端启用它
仍在后一个阶段被阻塞** [E2 已核实]。
`CanonicalL1MessageProcessor.ApplyBatchAsync` 对任何非 `Deposit` 消息抛
`NotSupportedException`（`src/Neo.L2.Executor/CanonicalNativeExecutionAdapter.cs:85-86`：
"not supported by N4 genesis V1"），而 `ProofWitnessSerializers.Validate` 会把每一个
*已知* `MessageType` 接受进 witness（`src/Neo.L2.Batch/ProofWitnessSerializers.cs:292`），
且只要提供了 router，`L2BatchPlugin` 就会把 router drain 接线进生产组合根
（`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:140-146`）。`ReferenceBatchExecutor` 对
非空收件箱要求一个 canonical processor（`src/Neo.L2.Executor/ReferenceBatchExecutor.cs:84`），
而仓库中唯一的构造点没有传（`tools/Neo.L2.Devnet/Program.cs:227`）。
净效应：C1 之后，一个充值+路由的组合收件箱会 seal 而不再抛异常，但一个真的携带
`Call` / `Event` / governance 条目的 batch 仍然无法由随附的 V1 profile 执行 —
故障从“无法 seal”移到了“无法执行”，这正是 §9 第 10 项以
“在分派存在之前拒绝 `Receive` 中的 `MessageTypeCall`”所描述的同一道边界。
此项是记录而非修复，因为闭合它意味着在 `doc.md` §10 / `SPEC.md` 中规定 router
的应用语义（一次规范变更），而不是打一个补丁。在此之前，诚实的运维指引是
“充值 + router 可以一起 drain；router 消息尚不能在 L2 上执行”。

**A6 — 一个 gateway 测试的 5 秒假守护进程期限不是并行安全的，而 CI 运行的是同一次
全解决方案调用** [E1 已观察]。
修复后的 `dotnet test Neo.L2.sln` 运行（38 个程序集，2,844 通过 / 1 失败 / 45 跳过）
恰好只失败一个用例：
`UT_Sp1GatewayProofProver.ProveAsync_RequestUsesCanonicalBindingAndBatchEncodings`
抛出 `TimeoutException: Gateway SP1 daemon did not publish …gateway-result.json within 00:00:05`
（`src/Neo.Plugins.L2Gateway/Sp1GatewayProofProver.cs:337`）。单独重跑
`tests/Neo.Plugins.L2Gateway.UnitTests` 得到 103/103 全绿，因此这是并行运行下的
调度饥饿，不是回归 — 且与 C1/H12 无关。这个 5 秒界限纯粹是测试本地的
（`tests/Neo.Plugins.L2Gateway.UnitTests/UT_Sp1GatewayProofProver.cs:198-202` 给一个
测试内的假响应器传 `resultTimeout: 5 s` / `pollInterval: 10 ms`；`Sp1GatewayProofProver.cs:34`
的生产默认是 30 分钟），而 CI 的 Test 步骤使用同一次全解决方案
`dotnet test Neo.L2.sln`（`.github/workflows/build.yml:97`），
因此任何负载较重的 runner 都能把它变红。
Fix：给那次假往返一个对负载不敏感的预算（数十秒），或给该测试类加上 `[DoNotParallelize]`。
