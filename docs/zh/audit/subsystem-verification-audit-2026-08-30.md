# Neo N4 — 子系统验证审计（2026-08-30）

本轮是 [2026-08-29 全系统审计](./full-system-audit-2026-08-29.md) 的执行半程。
那份报告是对全部 26 个 `contracts/` 项目以及链下库的通读与交叉核对；本报告则针对
`neo-n4` 独有的那些子系统 —— PolkaVM RISC-V 执行核心、SP1 zkVM 结算栈、承载资产的桥接路径、
batch/state-root/DA 流水线、optimistic 挑战与反审查机制，以及运维者表面（治理锁、部署器、
CLI、telemetry、RPC）—— 并把它们驱动起来：构建、运行、插桩，并把被测 artifact 与
声称产出它的那份源码做比对。本轮修订中，七条 track 已全部收口。

有两项约定沿用下来，另有一项是新增的。

- **证据层级**不变：**[E1]** 已执行并观察到，**[E2]** 已读码并与第二处代码位置交叉核对，
  **[E3]** 已计数。
- **发现编号延续** 2026-08-29 报告：新的 Critical 为 `C3`+，新的 High 为
  `H14`+，新的验证完整性发现为 `V1`+（上一份报告的第 3 类条目是
  `§3.n`）。既有 ID（`C1`、`C2`、`H1`–`H13`、`A4`–`A6`）含义不变，并在 §7 中
  重新标注状态，而不是重新编号。
- **本轮新增**：以下每一项头条发现都由本报告作者针对所引用的文件与行号重新核实过，
  而非采信自某条 track 的运行结果。凡是未能在这一轮复核中存活的 track 结果，都出现在 §8（更正）中，
  而不是 §3–§5。

## 1. 范围与方法

七条 track，彼此刻意不重叠，每条的任务书都要求它在 2026-08-29 报告之上继续推进，
而不是复述那份报告：

| Track | 子系统 | 落点 |
| --- | --- | --- |
| T1 | `external/neo-riscv-vm`（PolkaVM host + guest + 适配器插件） | §3、§4 |
| T2 | `bridge/neo-zkvm-{guest,host}`、`neo-zkvm-executor` 钉扎、`Sp1SettlementExecutionStack`、`NeoHub.Sp1Groth16Verifier` | §5 V3、V7、V8、§7 |
| T3 | `NeoHub.SharedBridge` / `ExternalBridgeEscrow` / `MpcCommitteeVerifier` / `VerifierRegistry`、外部 EVM + Solana 程序、`L2NativeContracts.cs` | §5 V5、§7 |
| T4 | `Neo.L2.Batch`、`Neo.Plugins.L2Batch`、`Neo.L2.State`、`Neo.Plugins.L2DA` | §4 H15、§6 |
| T5 | `NeoHub.SettlementManager` / `OptimisticChallenge` / `ForcedInclusion` / `Censorship` | §3 C4、§4 H16、H19、§5 V5、§6 |
| T6 | 横跨 26 个合约的治理锁、`Neo.Hub.Deploy`、`Neo.Stack.Cli`、telemetry + RPC 运维者表面 | §4 H17、H18、§5 V6、§6 |
| T7 | CI 拓扑、测试跳过分类学、docs/台账一致性 | §5 V1、V2、V4 |

这里的“验证”指的是这个具体问题：*那个被绿色对勾跑过的 artifact，是否就是源码所描述的那个
artifact？* 在 RISC-V 路径上，答案是否定的，而这正是本报告的头条（§3）。

## 2. 实际运行了什么

全部在 Windows（`win-x64`）上本地执行，除另有说明外所有命令 exit 0：

| 门禁 | 结果 |
| --- | --- |
| `dotnet test tests/Neo.Hub.Deploy.UnitTests` | 113 通过、0 失败 |
| `dotnet test --filter FullyQualifiedName~CurrentDocumentation` | 8 通过、0 失败、0 跳过 |
| `mdbook build`（仓库根 `book.toml`，`src = "docs"`） | exit 0 |
| `NEO_RISCV_NATIVE_TESTS=1` RISC-V native 测试套件 | 10 个测试在本地执行并通过 |
| batch / state / DA 套件（5 个项目） | `Neo.L2.Batch` 68/68 · `Neo.Plugins.L2Batch` 65 通过 + 1 跳过 · `Neo.L2.State` 120/120 · `Neo.Plugins.L2DA` 109/109 · `Neo.L2.Abstractions` 79 通过 + 1 跳过 — 全部 exit 0 |
| §10 第 4 项落地后的 `dotnet test tests/Neo.Hub.Deploy.UnitTests` | 115 通过、0 失败（在上面那 113 个之外新增两个 Gateway 解析器测试） |
| `dotnet test Neo.L2.sln`，两次，在 §10 第 1–4 项之后 | 第一次：总计 2,897，**1 个失败** —— §5 V7，而失败发生在那一分支根本未触碰的项目里；第二次：总计 2,897，0 失败、5 跳过，exit 0 |
| §10 第 5 项落地后的 `dotnet test tests/NeoHub.Contracts.VmTests` | 575 通过、0 失败（`UT_ForcedInclusion_Vm` 17/17） |
| `dotnet test Neo.L2.sln`，在 §10 第 5 项之后 | 38 个程序集，总计 2,899，0 失败、5 跳过，exit 0 |
| 为 §5 V8 对照的 `cargo audit --file Cargo.lock` 与 Dependabot API | audit：`found: false, count: 0`，exit 0 —— 而同一份 lockfile 上挂着 3 条 open 告警，其中一条 High |

那两处跳过属于 §5 V4 那一类的自我跳过，不是失败。这五个项目由 track 上报的计数被独立复现，
且完全吻合。（`V4` 已在本分支修好，时间在这张表记录之后：那两个项目现在是
`Neo.Plugins.L2Batch` 66/66 与 `Neo.L2.Abstractions` 80/80，`Skipped: 0` —— 每个项目各多出一个真正在
跑的测试，因为原本跳过的那一个现在开始执行了。）

至于 `master` 上的 CI，相关拓扑见 §5（V1）。

## 3. Critical

### C3 — RISC-V 执行核心是针对一个已被其自身源码取代的 guest 二进制来测试的，而它交付的又是另一个二进制 [E1 proven]

`external/neo-riscv-vm` 在编译期把 guest 模块内嵌进 host 库：

```
crates/neo-riscv-host/src/runtime_cache.rs:250
    include_bytes!("../../../crates/neo-riscv-guest-module/guest.polkavm")
```

`guest.polkavm` 是已提交的（`external/neo-riscv-vm/.gitignore:3` 把它从忽略名单中强制放出来），并且不存在 `build.rs` ——
这个 blob 就是上一次由人生成并提交的那个东西。Git 溯源需要在 submodule 内部读取
（从父仓库执行 `git log` 对 submodule 路径不会返回任何内容，这正是它得以长期隐形的方式）：

| 事件 | Commit | 日期 |
| --- | --- | --- |
| `guest.polkavm` 最后一次重新生成 | `efc3791` | 2026-05-20 |
| guest 源码：`static mut` → `AtomicU32`、callback 生命周期文档 | `2d1a6e7` | 2026-05-26 |
| guest 源码："resolve critical/high security findings across 4 audit rounds" | `d18298b` | 2026-05-27 |
| guest 源码："harden RISC-V runtime for Rust 2024" | `03e1139` | 2026-06-05 |

工作树是干净的，因此这是已提交的状态，而非本地漂移。**每一个 RISC-V 测试所执行的那个二进制
—— 包括 CI 专门的 native 步骤 —— 早于三轮 guest 侧的安全与运行时加固，而那些改动从未被执行过。**

没有任何机制能够察觉。重新生成的路径是存在的 —— `scripts/regenerate-guest-blob.sh`，它需要
nightly cargo 与 `polkatool 0.32.0` —— 但它是无条件的：它会构建、链接、覆盖，并打印 `Wrote …`。
它从不把重新生成的 blob 与已提交的那个做比较，因此它无法报告漂移。在 `.github/workflows/` 中
grep `regenerate-guest-blob` 与 `package-adapter-plugin` 返回零命中：没有任何 workflow 调用它，
也没有任何测试断言该 blob 与产出它的那份源码相符。

更糟的是，这两条路径对 artifact *究竟是什么*并不一致。`external/neo-riscv-vm/scripts/package-adapter-plugin.sh:20-25`
会在 `cargo build -p
neo-riscv-host --release` 之前立刻用当前 guest 源码重新生成 `guest.polkavm`。因此运维者安装的
release 插件里包含一个刚刚编译出来的 guest，其测试套件从未运行过它的行为；而测试套件认证的又是
一个无人交付的 blob。这把仓库自身在别处的标准反转了：SP1 执行器恰恰是为了避免此事而被摘要钉扎
（§5 V3），而失效的正是那个钉扎；PolkaVM guest 则根本没有钉扎。

为何这是 Critical 而非 High：`neo-n4` 关于 `L2RiscV`/PolkaVM 的有效性叙事建立在
“同一个 runtime 在 prover 内部重新执行”之上。这个论断说的是一个二进制。而就 guest 而言，
测试里的二进制既不是源码树里的二进制，也不是包里的二进制，并且没有任何门禁 —— CI、测试或构建 ——
能够分辨。

修复，按价值排序：(1) 增加一道新鲜度门禁，在 CI 中重新构建 guest，若该 blob 的 SHA-256 与
已提交者不同即失败；(2) 把 guest blob 的 SHA-256 记录为一个常量并在测试中断言它，
就像 `Sp1StatefulBatchExecutor` 断言其执行器摘要那样；(3) 让 `package-adapter-plugin.sh`
要么做提交校验，要么构建进一份暂存副本，而不是改动被跟踪的源码树。

### C4 — 一次成功的 fraud proof 会永久杀死它刚刚保护好的那条链 [E2]

`OpenWindow` 拒绝为一个已经存在的窗口重新装载，而窗口键只在一处写入、也只在一处删除：

```
OptimisticChallengeContract.cs:647-650   deadlineKey = DeadlineKey(…) → Assert(Get == null, "window already open") → Put
OptimisticChallengeContract.cs:781-782   the only Storage.Delete of deadlineKey + sequencerKey, inside FinalizeIfPastWindow
```

`grep -n "Storage.Delete" contracts/NeoHub.OptimisticChallenge/OptimisticChallengeContract.cs`
返回三处命中：`:446`（一条 approved-verifier 条目）与 `:781-782`。而一旦某次挑战成功，
`FinalizeIfPastWindow` 就不可达了，因为 `:776-777` 以
`"batch was challenged; cannot finalize"` 这条消息断言 `AcceptedFraudKey == null`。

`Challenge` 的接受路径（`:722-762`）写入 `AcceptedFraudKey`（`:737`）、消费该 claim（`:738`）、
调用 `revertBatch`、罚没 bond 并发出 `OnChallengeAccepted` —— 而从不删除 `deadlineKey` 或
`SequencerKey`。于是在一次正当且被正确验证的 fraud proof 之后：

1. `SettlementManager.RevertBatch:542` → `RevertBatchCore` 把那个槽位标成 `StatusReverted`（`:669`）
   并回退 `latestFinalizedBatch`（`:660`）。
2. `SubmitBatch:337-341` 明确邀请对该槽位做一次修正后的重新提交 —— 它自己的注释写着
   "the chain is never permanently wedged by a revert"。
3. 这次重新提交是 optimistic 的，所以 `SubmitBatch:391-395` 会针对同一个 `(chainId, batchNumber)`
   调用 `openWindow` —— 而 `:648` 以 `"window already open"` FAULT。由于 `:335` 要求
   `batchNumber == latest + 1`，其他任何槽位也都不可达。

这条链再也无法推进。结算合约自己所宣称的那条 revert 恢复路径，在每一条 optimistic 链上都不可达；
而挑战合约里没有任何管理员路径能清掉这个陈旧窗口，因此实际可行的恢复手段是“注册一条新的链”。

为何这是 [E2] 而不是 [E1]：这套机制是我从头读到尾、横跨两份合约的四个断言，
但没有任何测试执行过这个序列。复现只需要 `UT_OptimisticChallenge_Vm` 里的三步 ——
提交 optimistic、成功挑战、重新提交那个修正后的 batch ——
而这一缺失的测试正是这个缺陷能够发布的相当一部分原因。

修复：在一次成功的 `Challenge` 末尾删除 `deadlineKey` 与 `SequencerKey`。
accepted-fraud 标记才是那份持久记录，仅凭它就足以让 `FinalizeIfPastWindow` 保持关闭，
因此清掉这个窗口在语义上不损失什么；另一种做法（让 `OpenWindow` 覆写一个已过期的窗口）更弱，
因为它同时会把 finalize 路径为一个*并未*被挑战、只是未被终局化的 batch 重新打开。

与 `H18` 的耦合才是让这件事从理论变成紧急的原因。部署器今天只注册 ZK verifier，
因此一条 optimistic 链根本无从提交，这个死锁被一个更高优先级的失败掩盖住了。一旦有人靠注册
optimistic verifier 修好了 `H18` —— 也就是 §10 所推荐的那个修复 —— 它就会在 CLI 称之为
"the safe default" 的那个模板上变成活的。先修 `C4`，再修 `H18`，并把重新提交的那道测试与两者一起落地。

**状态 —— 已在本分支修复，并重新定级为 [E1]。** `Challenge` 现在会在写入 accepted-fraud 标记的
同一个状态变更块里删掉 `deadlineKey` 与 `SequencerKey`（`OptimisticChallengeContract.cs:744-745`），
位置在外部 `revertBatch` 调用之前。三道 VM 测试覆盖了这次改动、以及它绝不能削弱的两道闸：
`Challenge_AcceptedProof_ConsumesWindow_SoResubmitCanReArm`、
`Challenge_AcceptedProof_ReArmedWindow_StillRejectsSecondChallenge`（故意换一个不同的 `claimId`，
好让做决定的那道闸是批级别的 `"already accepted"`，而不是更早的那道 claim 闸）、以及
`Challenge_AcceptedProof_ReArmedWindow_StillCannotFinalize`（在全新的窗口背后，accepted 标记依然关着
`FinalizeIfPastWindow`，这正是本修复与"让 `OpenWindow` 覆盖过期窗口"那个更弱方案的区分点）。

挣到这一层级的是反向对照：把合约源码还原、并用钉住的 `nccs` 3.9.1 重新发射它的 NEF 之后，三道测试
全部以 `ABORTMSG is executed. Reason: window already open` 失败。这就是把死锁在链上跑了出来，
而不是从四个断言里推断出来，因此上文的 [E2] 标记作为审计当时的状态保留在此、并于此处作废。
带上修复后，`tests/NeoHub.Contracts.VmTests` 是 571/571，fresh-manifest 门在
`NEO_N4_REQUIRE_FRESH_MANIFESTS=1` 下 19/19 通过，整个解决方案 2,893 道测试 0 失败、45 跳过。
这 45 个没有一个是本次修复带来的，其中 40 个只发生在 Windows 上：即 §5 V4 的证据文件向上回溯
（`Neo.Plugins.L2Settlement` 27 个、`Neo.L2.IntegrationTests` 9 个、另外四个项目共 4 个）。
其余 5 个与平台无关，是真正的 env 门，§11 已逐一点名。本段最初把这 9 个 `IntegrationTests` 的跳过
称作 "env 门"，那是错的 —— 见 §8 第 14 条。`V4` 已于同日修好；同一次全解决方案运行现在是 2,893
道测试、0 失败、5 跳过。

仍然不存在的是"两份真合约"的测试：`UT_SettlementManager_Vm.cs:185` 把 `OptimisticChallenge` 接成了
mock，所以 `SubmitBatch` 自己的那条重新提交分支，只是通过这些测试直接发起的 `OpenWindow` 调用被
间接触及。该发现所点名的 `SubmitBatch:391-395` 那条跨合约接缝，因此仍停留在 [E2]。

## 4. High

### H14 — 两个 profile 中的 `panic = "abort"` 使每一处 FFI panic 边界都成为死代码 [E1]

`external/neo-riscv-vm/Cargo.toml:42-48` 在**两个** profile 下都设置了 `panic = "abort"` ——
`[profile.release]` 与 `[profile.dev]`。`crates/neo-riscv-host/src/ffi.rs` 用十个
`std::panic::catch_unwind(AssertUnwindSafe(…))` 分支守卫 host 回调表面 —— `:682`、`:756`、`:859`、
`:968`、`:1073`、`:1159`、`:1216`、`:1329`、`:1416`、`:1492` —— 每一个的写法都是把一次 Rust panic
转换成返回给 Neo 的 `FAULT` receipt。在 `abort` 之下没有可供捕获的展开：进程会在那十个分支中
任一分支里的第一次 panic 处死去。dev profile 与 release 同样要紧，因为本地运行与大多数 CI 相关
调试用的就是它。

这是 H1 的 Rust 侧孪生：执行核心内部一次普通故障会变成 sequencer 宕服，而不是一个被拒绝的区块。
修复：去掉 `panic = "abort"`（测量展开开销；若确有影响，就只对 guest crate 保留 `abort`，
因为它没有需要跨越的 FFI 边界），或者把那十个分支替换成一处显式的 `extern "C"` 捕获表面，
使其无法被 profile 配置掉。

### H15 — batch 中每个区块都以同一个 `Runtime.Block.Index`（一个 L1 高度）与冻结的首区块时间戳执行 [E1]

`BatchSealer.SealBatch` 每个 batch 恰好构建一个上下文：

```
src/Neo.Plugins.L2Batch/BatchSealer.cs:387-394
    builder.WithBlockContext(new BatchBlockContext {
        L1FinalizedHeight = _l1FinalizedHeight?.Invoke() ?? 0,
        FirstBlockTimestamp = _firstBlockTimestamp, … });
```

在 `MaxBlocksPerBatch = 50`（`src/Neo.Plugins.L2Batch/Settings.cs:33`）之下，两个执行器随后把
这一个上下文映射到它所执行的每一个区块的持久化区块头部：

```
src/Neo.L2.Executor/ApplicationEngineTransactionExecutor.cs:237-238   Index = ctx.L1FinalizedHeight
                                                                      Timestamp = ctx.FirstBlockTimestamp
src/Neo.L2.Executor.RiscV/RiscVHostExecutionContext.cs:605-606        (identical mapping)
```

因此 `Runtime.Block.Index` 把一个 **L1** 已终局高度报告为 L2 区块索引，而对全部 50 个区块而言
`Runtime.Time` 都是该 batch 的首区块时间戳。两个执行器彼此一致，所以*今天*不存在 proposer/settlement
分歧；而这一分歧之所以仍处于潜伏状态，只是因为范围内没有任何消费方会对这些头部字段做哈希
（`L2NativeContracts.cs` 既不读 `Runtime.Time` 也不读 `CurrentIndex`）。

该发现的内容是：这道接缝没有被组合起来，而且代码与其自身的安全注释相矛盾：
`ApplicationEngineTransactionExecutor.cs:227-230` 写着 "for L2 chains, the L2 block height +
timestamp drive contract behavior, not L1's"，随后却赋上 L1 高度。第一个对时间或高度敏感的系统合约，
或持久化头部的任意消费方，都会把它变成一次共识分裂。修复：把逐区块的索引与时间戳贯穿进执行过程
（batch 插件已经两者齐备 —— `L2BatchPlugin.cs:501-505` 把它们传给 `ProcessCommittedBlock`），
并在两个执行器上用一个测试把这个映射钉住。

### H16 — 暂停一条链并不能阻止终局化：两条变更路径中只有一条会读取 `isActive` [E1]

这是对 H13 的增量，H13 覆盖的是 `EmergencyManager` 全局标志。*按链*的暂停有同一个漏洞、
更小的爆炸半径，但文档层面的论据更有力：

- `SettlementManager.SubmitBatch:330-331` 调用 `ChainRegistry.isActive` 并对其做断言。
- `SettlementManager.FinalizeBatch:479-533` —— 那个记录规范 state root、
  推进 `latestFinalized`、更新 gateway 水位线并发出 `BatchFinalized` 的函数 —— 从不读取它。
  `isActive` 在整个文件中恰好出现一次。
- `ChainRegistry.PauseChain:482-499` 除了翻转那个字节之外什么都不做。

于是那个*确实*已接线的应急处置原语（`RegisterPauser` + `PauseChain`，在
`LiveDeployCommand.cs:801-802` 部署）挡住了新的提交，而每一个已经处于 `Pending` 或
`Challengeable` 的 batch 仍在继续终局化，并向前滚动 `SharedBridge` 付款所承诺的那个 root。
在事故中暂停一条链的运维者得到的是一个界面状态，而不是一次停止。修复：在 `FinalizeBatch`
中断言 `isActive`（而 `RevertBatch` 必须保持可在暂停状态下调用，否则无法恢复），并补上先暂停、
再分别尝试两者的 VM 测试。

**状态 —— 已在当前分支修复，并重新定级为 [E1]。** `FinalizeBatch` 现在断言的就是
`SubmitBatch` 断言的那个字节（`SettlementManagerContract.cs:509-510`），并且复用的正是该函数
为了重验安全等级而已经载入的 `chainRegistry` 句柄，因此这次暂停只多付出一次只读的跨合约调用，
不新增任何存储槽位。所以自本次变更起 `isActive` 在该文件中出现**两次**；上面"恰好出现一次"
的记录作为审计时点状态保留。`RevertBatch:551` 刻意不加守卫，而这正是修复必须保住的不对称性 ——
一条被暂停的链仍然必须可回滚，否则那个唯一能撤销错误 root 的动作，恰好在最需要它的时刻变得不可用。

实测下来的爆炸半径比这条发现当初假设的更小，值得记录，因为它界定了这次改动的风险边界。
`finalizeBatch` 在仓库内恰好有一个调用方，即
`OptimisticChallenge.FinalizeIfPastWindow:791`，而那个调用方在外部调用*之前*就删掉了
`deadlineKey` 与 `SequencerKey` —— 于是一次 FAULT 会把删除原子地回滚掉，等 `ResumeChain`
之后终局化可以直接重试。这不是第二个 `C4`：暂停不会困住任何 batch。
`finalizeIfPastWindow` 本身在 `src/` 与 `tools/` 下根本没有链外驱动，所以这个守卫改变的只是
VM 测试实际执行的那条路径。

随修复落地两个测试：`FinalizeBatch_RejectsPausedChain` 断言的是 FAULT 的*消息文本*而不只是
异常类型，随后恢复并终局化同一个 batch，使暂停不可能是终结性的；
`RevertBatch_StillWorksOnPausedChain` 钉住那条无守卫的恢复路径。把这两个测试区分开来的正是
反向对照，这也是第二个测试值得存在的理由：把合约源码回退、并用钉住的 `nccs` 3.9.1 重新生成
它的 NEF 之后，`FinalizeBatch_RejectsPausedChain` 以
`Expected exception of exact type TestException but no exception was thrown`
失败 —— 被暂停的链确实终局化了，这是执行出来的而非读出来的 ——
而第二个测试在两种构建下都通过，这正是一个"守卫缺席"测试应该有的行为。
重新生成的 artifact 挪动了 NEF 字节与方法偏移量，103 项的 ABI 名字集合保持不变。
`tests/NeoHub.Contracts.VmTests` 为 573/573、0 skipped；整个解决方案为 2,895 个测试、0 失败、
5 skipped —— 即 §5 V4 的 2,893 加上这两个，且跳过数未变，而这道算术正是"别的东西都没动"的确认。

### H17 — 文档所述的 Gateway global-root 路径在部署器产出的每一次部署上都会 FAULT [E1]

除非 global-root 治理已被锁定，否则 `MessageRouter.PublishGlobalRoot` 会拒绝*第一次*发布：

```
contracts/NeoHub.MessageRouter/MessageRouterContract.cs:269-270
    ExecutionEngine.Assert(IsGlobalRootGovernanceLocked(), "global root governance not locked");
```

仓库内唯一的调用链是面向运维者的：
`SettlementManager.PublishGatewayGlobalRoot:778` → 校验各组成链的前缘 →
在 `:866-881` 处 `Contract.Call(messageRouter, "publishGlobalRoot", …)`。`docs/launching-an-l2.md:1076`
指示运维者提交的正是这一调用。

`MessageRouter.LockGlobalRootGovernance`（`:338`）仅限 owner，且在整个产品中没有任何调用方：
`tools/Neo.Hub.Deploy/*.cs` 中对 `GlobalRoot` 零命中，`Neo.Stack.Cli` 中也没有，而
`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` 没有对应物。它只被直接调用它的
VM 测试 exercised（`UT_MessageRouter_Vm.cs:118`、`:296`），而这恰恰是这一缺口得以保持绿色的原因。
于是就部署形态而言，Phase-5 的跨链终局性中继是不可用的，除非运维者知道要手工发出一道未被文档
记录的锁 —— 而部署器的冒烟过程永远不会察觉，因为它根本不尝试一次 global-root 发布。

这与 H12 为其余信任根修掉的是同一个失效模式：锁存在、正确，却没有被接进运维者实际执行的那套序列。
修复：把 `LockGlobalRootGovernance` 排进 `LiveDeployCommand` 中其他锁的旁边，把它加入 CLI 的 plan
文本，并把冒烟过程扩展到一次端到端的 `PublishGatewayGlobalRoot`。

**状态 —— 本分支已修，但对建议的修复做了一处有意偏离。** 部署器现在把这三步 bootstrap 作为
post-deploy 步骤 `:894-900` 执行，位于 `SettlementManager.SetMessageRouter`（`:892`）与
`ChainRegistry.LockGovernance`（`:901`）之间：`MessageRouter.SetGovernanceController` →
`SetGlobalRootVerifier` → `LockGlobalRootGovernance`。每一步都带一个读回完成检查
（`getGovernanceController`、`getGlobalRootVerifier`、`isGlobalRootGovernanceLocked`），因此崩溃后重跑
只会跳过已经生效的那几步，而不是重新签发 owner 交易。

这个顺序不是风格选择，而是合约强制的：`SetGovernanceController:329` 与 `SetGlobalRootVerifier:315`
都断言 `!IsGlobalRootGovernanceLocked()`，而 `LockGlobalRootGovernance:341-343` 同时断言 controller
非零 *且* verifier 已配置。所以先加锁必然 fault，而没有 controller 就加锁会以
`"wire GovernanceController before locking"` fault。这与 `UT_MessageRouter_Vm.cs:109-119`
（`ConfigureAndLockGateway`）在测试里早已使用的序列完全一致 —— 只是部署器从来没被告知过。

profile 的 verifier 是 `Sp1Groth16Verifier`，不是 `ContractZkVerifier`，因为
`PublishGlobalRoot:286-296` 派发的是 `verifyZkProof(byte,byte[],byte[],byte[])`，而
`ContractZkVerifier` 暴露的是另一套 ABI。`proofSystem` 是 SP1（`1`），backend 是 `0xC2`，即
`Sp1GatewayProofProver` 盖上的那个递归 Gateway backend；`tools/Neo.Hub.Deploy` 并不引用 gateway
插件，所以这个字面量只能像 `ProofSystemSp1` 与 `ProofTypeZk` 一样在本地配对。

把接线修好之后浮出第二个缺口：这组 profile 无法在仓库内推导。Gateway guest program 的 vkey 与
replay domain 是在 `GatewayHostComposition.OpenSp1(chainDir, gatewayVk, signer, replayDomain,
verificationKeyId, …)` 由运维者提供的，而且哪都没有持久化，于是 `deploy-testnet` 新增两个必填开关 ——
`--gateway-program-vkey` 与 `--gateway-replay-domain` —— 它们与 SP1/fraud 参数共用既有解析器
（`ParseProgramVKey`、`ParseRequiredReplayDomain`），以 `gatewayProgramVKeyRaw` /
`gatewayReplayDomain` / `gatewayAggregationBackend` 写入部署报告，并按 raw 字节序打印，便于运维者把
同一串 hex 贴给 Gateway host。补文档时顺带发现 `--sp1-program-vkey` 自写成之日就是必填、却从未出现在
`Program.cs` 的用法文本里，这次一并补上。

偏离之处在于：冒烟过程**没有**尝试端到端的 `PublishGatewayGlobalRoot`。那需要一份真实的 SP1 递归聚合
证明 —— 也就是把编译好的 guest ELF 和一个活的证明器放进部署路径 —— 因此冒烟改为读回全部被比较的
表面（`:984-989`）：verifier 哈希、`getGlobalRootProofSystem`、`getGlobalRootAggregationBackend`、
`getGlobalRootVerificationKeyId`、`getGlobalRootReplayDomain` 与 `isGlobalRootGovernanceLocked`。
这六项正是 `PublishGlobalRoot:269-278` 在派发前逐一断言的值，所以按报告里那组 tuple 配置的 Gateway
host 不可能再被治理门禁拒绝。这一半是被执行过的、不是推断出来的：
`UT_MessageRouter_Vm.PublishGlobalRoot_BindsEpochRootConstituentsBackendDomainAndProof:383`
先经 `ConfigureAndLockGateway` 锁定 profile，随后对携带精确已注册 tuple 的发布断言 `IsTrue`，
并对该 tuple 每一个被改错的元素断言 fault。冒烟仍未覆盖的是*证明本身* —— 真实 Gateway 聚合上的
`verifyZkProof` —— 那部分由 gateway 一侧（`Sp1GatewayProofProver` 的再验证、VM 测试）覆盖，而不是
这里。把这条边界写下来是有意义的：六次读回证明 profile 正确，并不证明发布一定会成功。

`plan` 同样把这三步作为提示输出（`ScaffoldPlan.cs`，以 MessageRouter + GovernanceController +
Sp1Groth16Verifier 三者都在为前提），带上 `GATEWAY_PROGRAM_VKEY_REPLACE_ME` /
`GATEWAY_REPLAY_DOMAIN_REPLACE_ME` 占位，并指向加锁后轮换用的 `SetGlobalRootVerifierViaProposal`，
于是 plan 文本与真实序列保持同一顺序；`PostDeployActions` 因此从 41 变 44，
`PostDeployActions_DefaultPlan_EmitsAllWiringHints` 的位置化尾部也随之重编号。

测试：两个新增解析器测试（`ParseGatewayProgramVKey_*`、`ParseRequiredGatewayReplayDomain_*`）断言
共用的 helper 报出的是 **Gateway** 那个开关名而不是 SP1 的；H12 那条“凡接了 controller 的表面都必须
上锁”的循环被推广为接受 MessageRouter 那个不同名字的门禁，而不是把它豁免掉。顺序测试把新增三步钉成
连续、且紧邻 `ChainRegistry.LockGovernance` 之前，并逐字节断言 `setGlobalRootVerifier` 的脚本。
反向冒烟测试新增六条不匹配条目，其中一条是 pass-through backend `0xFE`。
`tests/Neo.Hub.Deploy.UnitTests` 为 115/115（此前 113，+2 个解析器测试），
`tests/NeoHub.Contracts.VmTests` 保持 573/573，因为没有改动任何合约源码。整解决方案计数见 §10 第 4 项。

一个值得保留的度量注记：本次改动之后第一次全解决方案运行报告了唯一一处失败，位置在
`tests/Neo.L2.Proving.UnitTests` —— `ProveAsync_TamperedExecutionSemantic_IsRejected` 期望
`InvalidDataException` 却捕获到 `IOException`（`The process cannot access the file
'…f0712…proof.result.json' because it is being used by another process`），来自
`AtomicFileQueueTransport.ReadBoundedPathAsync:265`。该项目与本分支无关，且单独运行时 3/3 通过。
见 §5 V7。

### H18 — `rollup` 模板发出 Optimistic commitment，而部署只注册了 ZK verifier [E1]

`tools/Neo.Stack.Cli/Commands/TemplateCatalog.cs:30-36` 把 `rollup` 做成**第一个**模板 ——
`All[0]`，并且 `Resolve(string name)` 在遇到未知名称时回退到它（`:63-64`）—— 其 `ProofType: "Optimistic"`，
宣传语称它是 "the safe default"。它的 ZK 同胞是 `zk-rollup`（`:38`-`:41`）。但
`tools/Neo.Hub.Deploy/LiveDeployCommand.cs:36` 只声明了 `ProofTypeZk = 3`，而 `:833-834` 是部署器
中唯一一处 `RegisterVerifier` 调用，只注册 ZK verifier、别无其他。因此一条由默认模板创建、
并由文档所述部署器部署的链，会提交那些 `VerifierRegistry` 没有对应条目的 proof type 的 batch，
而 `submitBatch` 会在 `VerifierRegistryContract.cs:256` 处 FAULT（`"no verifier for proof type"`）。
运维者看到的是一条来自结算侧的拒绝，却没有任何提示说明这一不匹配源自模板与部署器之间的分歧。

要么模板应当默认 ZK，要么部署器应当为被声明为 optimistic 的链注册 optimistic verifier，
而 `neo-stack` 应当把模板的 `ProofType` 与部署计划做交叉核对。修复量很小；要点在于这两个工具
对旗舰模板的默认安全态势意见相左。

### H19 — 反审查截止期在 owner 路径上有界，在 deploy 路径上无界 [E2]

`ForcedInclusion` 只存储一个全局的 `deadlineSeconds`，并在为每条条目盖章时读取它。下面这段摘录
引用的是修复前的源码及其修复前行号；其后正文里的每一处引用都已针对修复后的文件重新核对过。

```
ForcedInclusionContract.cs:130-133   _deploy: deadline = (uint)(BigInteger)arr[2]; Assert(deadline > 0, "deadline must be positive")
ForcedInclusionContract.cs:192-199   SetDeadlineSeconds: Assert(seconds >= 60 && seconds <= 86400, "deadline out of bounds [60, 86400]")
ForcedInclusionContract.cs:373-374   enqueuedAt = (uint)(Runtime.Time / 1000u); EncodeEntry(…, enqueuedAt + deadline)
```

于是同一个数值在 owner 改动它时会被做范围检查，而在部署器设置它时不做检查。仅由部署期那一个
字段就派生出两个后果，而究竟适用哪一个，取决于 NCCS 如何编译 `:389` 处的那个 `uint` 加法 ——
本轮无法从源码判定这一点，所以它是作为一处需要 VM 测试来落定的危害来陈述的，而不是一个已被证实
的行为：

- 如果该算术会饱和，或者这个数值本身就很大，那么审查窗口实际上永远不会到期，而
  `ReportCensorship` 的全部意义 —— 也就是 §17 那个 escape hatch，它让任何人都有可能证明某个
  sequencer 正在无视 L1 队列并据此暂停该链（`:511` `if (nowSec < deadline)
  return false;`，然后 `:518` `pauseChain`）—— 在一条部署时把截止期写错的链上就是失效的。
  部署数据里的一个 `uint` 字段静默地关掉了整条反审查保证，而 `IsProductionReady()` 并不检查它
  （`:269-281` 既没有 pauser 也没有截止期边界）。
- 如果这次加法按 mod 2³² 回绕，存储下来的截止期就会落在过去，每一条条目都可被立即举报，
  而任何无需许可的调用方都能随意 `pauseChain`。

用同一个单行改动把两个方向一起修掉：在 `_deploy` 里施加 `SetDeadlineSeconds` 已经强制执行的
`[60, 86400]` 边界，并补上那个把溢出行为钉住的 VM 测试。

这里真正健全的部分同样值得说清楚，以免该发现被过度解读：单个条目的截止期一旦写入就不可变更。
nonce 严格递增（`:384-385`），每次 enqueue 恰好一处 `Put`，而整个合约里不存在任何更新路径，
所以一个搞审查的 sequencer **无法**靠续期截止期来推迟一次强制包含 —— 这一设计里那个经典的逃逸
手段在此是缺席的。`ReportCensorship` 同样对每条条目只生效一次（`:513` 设置一个
`reportedKey`），而 `:511` 处的比较在相等时即算作已到期，这正是一个截止期应有的方向。

**状态 —— 已在本分支修复，而该发现拒绝猜测的那个问题现在有了结论。** `_deploy` 施加了与
`SetDeadlineSeconds` 原本已强制的同一个 `[60, 86400]` 窗口
（`ForcedInclusionContract.cs:140-146`），并且两处守卫现在读同一对常量
（`MinDeadlineSeconds` / `MaxDeadlineSeconds`，`:70` 与 `:73`），因此这两处不可能再次各自漂移。

那个 VM 测试是以测量、而不是以阅读 NCCS 的方式落定这段算术的：**数值按 mod 2³² 截断，而虚拟机会
halt。** 一次在合法最大截止期下的对照 enqueue 存下 `1,468,681,716`；同一次 enqueue 先推进
`4,294,880,900` 秒后存下 `1,468,595,320`；两个存储字段之差恰好等于推进量，而这只可能在
`(uint)(Runtime.Time / 1000u)`（`:388`）与 `enqueuedAt + deadline`（`:389`）都截断、都不检查时成立。
`EnqueueDeadlineSum_TruncatesModuloTwoTo32InsteadOfFaulting` 把这个关系钉住了。

因此上面两个危害分支都是活的，分界在于数值大小，而不在于 NCCS 发射了哪条指令：一个仍在上限之内
的越界值（`100,000,000`）会让审查窗口实际上永远到不了期、`ReportCensorship` 失效；而一个连 2³²
都装不下的值（`3,000,000,000`）会把存储的截止期回绕到 1984 年，使每一条条目都可被立即举报，并把
一次无需许可的 `pauseChain` 交给第一个调用方。同样是这个截断，说明了上界为什么就是让整个问题够不
着的那道防线：只要 `deadline ≤ 86400`，`enqueuedAt + deadline < 2³²` 在一条链 2106 年之前的全部
生命周期内都成立，因此不需要更多改动，而这个测试的存在是为了记录残留，不是为了守住它。

对树内任何调用方都不是破坏性变更，因为没有人提供第三个部署元素。部署器只传两个
（`artifacts/local-deployment-rehearsal/*/hub/deploy-bundle.json` 里的
`resolvedDeployData: ["OWNER_REPLACE_ME", <settlement manager>]`），于是生效的是
`DefaultDeadlineSeconds = 7200`（`:63`）—— 落在窗口之内 —— 而没有任何 `.json` 模板或 C# 调用方
传递截止期。

负向对照，好让这里的 [E1] 断言名副其实：只把被跟踪的产物
（`TestingArtifacts/NeoHubForcedInclusion.artifacts.cs`）回退到修复前的 NEF，新的部署测试就会
失败并点名截止期 `1`。所以这道守卫是在链上执行，而不是在 C# 里执行。重新发射只改变了正好两行
（`Manifest` 与 `Nef` 两个属性），而 ABI 的名字集合与 `HEAD` 完全一致。

`UT_ForcedInclusion_Vm` 17/17（原为 15）、`NeoHub.Contracts.VmTests` 575/575、全解决方案
38 个程序集 / 2,899 个测试 / 0 失败 / 5 跳过（即 H17 那一轮的 2,897 加上这里的两个）。

## 5. 验证完整性发现

这些发现决定了其余任何发现是否可信。

### V1 — SP1 的必需检查之所以变绿，正是*因为*那些重型通道没有运行 [E1]

```
.github/workflows/build.yml:385-387   sp1-release-gates: if: github.event_name == 'workflow_dispatch'
build.yml:516                          cargo test --workspace --release
build.yml:520                          cargo test (neo-zkvm-host, real proof)
build.yml:532                          gateway-host recursive proof
build.yml:565-569                      if dispatch → test …= success; else test …= skipped
```

`sp1-release-gates` —— 唯一一个编译 workspace 并产出真实 batch 与递归 SP1 proof（含篡改门禁）的
job —— 仅限 `workflow_dispatch`。而 `master` 分支保护所要求的聚合 job `sp1-host`，
在每一个非 dispatch 事件上都断言其结果等于 `skipped`。2026-08-29 报告只是顺带提到这一点
（§10 中的 "CI's Linux-only `sp1-release-gates`"）；那是轻描淡写。在 PR 上以及推送到 `master` 时，
SP1 出证栈不只是未被观察，它*必须缺席*才能让那道必需检查通过，于是 `bridge/neo-zkvm-host` 中一次
真实的回归无法让作者看到的任何东西变红。改为 nightly 或由 merge queue 调度的 dispatch，
既能保住资源上限，又能让这条断言变得有意义。

本轮给出的是一次执行实例，而不是只读推断。PR #52 的 head `20d7ce80`（workflow run
`33301282516`，2026-08-30 以 `6116d659` 合入 `master`）把全部 14 个 required context 都报成
`completed/success` —— 包括 `SP1 compatibility and manual release proof gate`，check-run
`99230673595` —— 而同一个 run 里那条重型 lane 记的是 `skipped`，`matrix.name`，check-run
`99229821429`。那个 PR 改动了 `contracts/`、`tests/`、`tools/` 与 `src/`，并重新生成了 VM 合约
artifact；SP1 的执行与证明栈没有被任何把关它合并的检查执行过，而看上去覆盖它的那道必需检查恰恰
*因为*这一缺席才通过。

一条审计轨迹事实，记下来是因为这份报告若跳过它就自相矛盾：那次 merge 是在零 approve 的情况下到达
`master` 的。`master` 设了 `enforce_admins: true`，所以 admin merge 会被直接拒绝
（"New changes require approval from someone other than the last pusher"），必须先把它关掉、
再立刻打开，才落得下去。这个决定里没有任何 `V1`..`V6` 发现牵涉在内，这次开关本身也不是代码缺陷 ——
但一条发布路径上唯一的审阅控制，能被推送它的同一个身份用一次 API 调用移除，这和本节所讲的
“检查错了东西的门禁”是同一类问题，§10 的修复顺序应当据此给予权重。

### V2 — "off-chain ↔ on-chain encodings are paired" 这一不变式没有任何跨边界测试 [E1]

`tests/NeoHub.Contracts.VmTests/NeoHub.Contracts.VmTests.csproj` 引用了
`Neo.SmartContract.Testing`、MSTest 与测试 SDK —— 以及**零个 `ProjectReference`**。因此这些 VM
测试无法调用 `BatchSerializer`、`MessageHasher`、`MerkleProofSerializer` 或
`L2ChainConfigSerializer`；它们手工拼出字节缓冲区（`UT_SettlementManager_Vm.cs:70-122`）并再次
硬编码常量（`UT_ChainRegistry_Vm.cs` 重复了 `ConfigSize = 91`）。那五处真正要紧的配对已被手工核对
且逐字节精确 —— 321 字节的 commitment 头部、332 字节的 public inputs、48+32N 的 proof 分帧、
提取 leaf 哈希、91 字节的链配置 —— 但这种一致是靠复制粘贴的纪律维持的，而最接近“钉扎”的那个东西
—— `UT_OnChainMerkleVerifyParity.cs` —— 只是该合约折叠的一段 C# *复制品*，而非合约本身。

与此相伴，有一条文档陈述是假的：`src/Neo.L2.Batch/BatchSerializer.cs:12-14` 说该编码器产出
"the byte format that the settlement contract reads"，这对 commitment 头部成立，
对 public-inputs 那一半则不成立 —— 而后者从不被传输。这正是 `C2` 类编码漂移得以隐形的机制；
只需要一个同时引用两侧的测试项目就可以把它关闭。

### V3 — SP1 执行器的 "funded release pin" 只是装饰，且它的拒绝路径没有任何测试 [E1]

这是此前的 `H6`，如今已由执行确认。`Sp1SettlementExecutionStack.cs:46,127` 与
`ZkLocalHostComposition.cs:87,110` 把 `executorSha256` 作为一个**由调用方提供的参数**接收 ——
整个仓库中不存在任何被钉扎的常量 —— 而构造函数只拒绝全零或长度不符的摘要
（`src/Neo.L2.Executor/Witness/Sp1StatefulBatchExecutor.cs:31,41,65-69`）。真正的比较发生在更晚的
`:390-393`（`"Native SP1 execution binary SHA-256 differs from the
pinned operator digest"`）。唯一能够走到那里的测试，是从被测二进制自己算出期望值：

```
tests/Neo.L2.Executor.UnitTests/UT_Sp1StatefulBatchExecutor.cs:318
    SHA256.HashData(File.ReadAllBytes(executable!))   // passed as the expected pin
```

由它所认证的 artifact 派生出的钉扎不可能失败，而且没有任何测试提供一个错误的摘要，于是那条拒绝
分支从未被执行过。对照 `ChainRegistry` 的 `RegisterChain`（§7），那是一道靠读码发现的正确守卫。
修复：一个已提交的常量摘要 + 一个反向测试 + 一个重新计算它的 CI 步骤。

### V4 — 27 个 settlement 测试在 Windows 上自我跳过，因为证据文件的向上回溯忽略了 RID 子目录 [E1]

2026-08-29 报告的 §3.1（"~45 tests silently self-skip on Windows"）仍未修复，而根因如今已被精确
定位：

```
tests/Neo.Plugins.L2Settlement.UnitTests/UT_MultisigLocalHostComposition.cs:26-34
    Path.Combine(AppContext.BaseDirectory, "..","..","..","..","..", "docs","audit", …)   // 5 levels
tests/Directory.Build.props:4-5   injects RuntimeIdentifier=win-x64 on Windows only
```

一旦设置了 RID，`AppContext.BaseDirectory` 就会多出一段 `win-x64/`，于是向上五层落在
`tests/`，而观察到的消息逐字为
`repo evidence file not found at D:\Git\neo-n4\tests\docs\audit\testnet-deployment-20260716-live.json`
— 仅那一个项目就有 27 个测试为 `Inconclusive`，仅在 Windows 上，而在 Linux CI 上是绿色的。

同一条错误路径在另外两个项目中也被确认，因此*扩散范围*比 §3.1 所描述的更广：
`Neo.Plugins.L2Batch.UnitTests.FromChainDirectory_LiveDeployReport_LoadsChainId` 与
`Neo.L2.Abstractions.UnitTests.Parse_RealTestnetEvidenceReport_IfPresent` 都以一字不差的同一条
消息跳过，而这段脆弱的向上回溯出现在 6 个测试项目的 10 个文件中。本轮没有重新统计全仓库总数，
所以 §3.1 的 "~45" 仍是未经证实的 —— 这里实际测到的只有 `L2Settlement` 里的 27 个。
并请留意第二个名字：
一个被写成“可以容忍证据文件*缺失*”的测试，却无法定位一个*确实存在*的文件，于是在 Windows 上
它永远报告 "not found"，而没有人察觉这句谎话。

修复只需要一个辅助函数 —— 通过向上探测 `Neo.L2.sln` 来解析仓库根目录 —— 并应用到全部十处。
它不应等待 §10 中的任何决策。

**状态 —— 已在本分支修复，而 §3.1 的 "~45" 如今是一个测出来的数字，不再是一个估计。**
`tests/Shared/RepoRoot.cs` 通过一路向上遍历 `AppContext.BaseDirectory` 的祖先目录、直到找到
`Neo.L2.sln` 来解析仓库根 —— 有没有 RID 子目录都成立 —— 并暴露测试所读取的那一个证据路径。
它是在 `tests/Directory.Build.props` 里以编译链接
（`<Compile Include="$(MSBuildThisFileDirectory)Shared/RepoRoot.cs" Link="Shared\RepoRoot.cs" />`）
交付的，而不是项目引用，因此每个测试程序集各自拿到一份 `internal` 拷贝，没有任何生产项目会新增一个
仅测试用的依赖。那 10 个文件里全部 33 处向上回溯表达式现在都读 `RepoRoot.LiveTestnetEvidence`；
逐文件的处数依次为 8/8/7/2/2/2/1/1/1/1。

测得的效果来自一次全解决方案运行（同样 2,893 道测试）：全仓库跳过数 **45 → 5**。受影响的六个项目
现在都报告 `Skipped: 0` —— `Neo.L2.Abstractions` 80、`Neo.L2.IntegrationTests` 40、
`Neo.Plugins.L2Batch` 66、`Neo.Plugins.L2Prover` 21、`Neo.Plugins.L2Settlement` 168、
`Neo.Stack.Cli` 189 —— 于是 40 个在 Windows 上从未执行过的测试如今在跑，而且全部通过。
总数 2,893 保持不变，这正是"差异来自被重新启用的覆盖面、而不是被删掉的测试"的证据。
这把 §3.1 的 "~45" 收敛成了精确值，也补上了 §8 第 12 条记下的"总数从未被重新统计"那条局限。

关于这个数字的两道交叉核对。2026-08-29 报告独立统计了受影响的面，写作
"40 test methods across 11 files"，而它的测试方法数与这里测得的增量完全一致 —— 它的 55 个跳过，
减去它自己 §3.2 记录为已经关闭的 10 个 RISC-V 跳过，正好是 45，也就是修复前的数字。它的文件数多算了
一个：对修复前那个提交重新统计，得到 10 个文件、33 处向上回溯表达式。这两个数可以按项目对上 ——
`Neo.Plugins.L2Settlement` 里 27 处表达式 ↔ 27 个测试，`Neo.L2.IntegrationTests` 里 2 ↔ 9
（一处表达式在测试体 `UT_E2E_HostComposition_FromDeployReport.cs:47` 里，另一处在被其余八道测试调用的
`ResolveDeployReportPath():3458` 里），其余四处站点各自 1 ↔ 1。而这道辅助函数是把仓库里本已存在的
模式推广开来，而不是另造第三种：`tests/Neo.Hub.Deploy.UnitTests/UT_ProductionGapClosure.cs:706` 的
`FindRepositoryRoot()`，加上 `NeoHub.Sp1Groth16Verifier.UnitTests` 里的三份私有副本，它们向上探测
`Neo.L2.sln` 的方式与现在的 `RepoRoot` 完全一致。`tests/` 里已不再有任何手工上溯 —— 没有任何测试再用
`AppContext.BaseDirectory` 拼接 `".."` 来构造仓库路径，那棵树里剩下的点号字面量只是喂给反向测试的路径
穿越输入、以及脚手架命令所断言的那些相对 csproj 引用 —— 所以下一个需要读取仓库文件的测试，也没办法把
这一类缺陷重新引进来了。

守卫仍然是守卫 —— 这类修复最可能在这一点上被悄悄弄坏：把
`docs/audit/testnet-deployment-20260716-live.json` 藏起来，
`Parse_RealTestnetEvidenceReport_IfPresent` 就会再次跳过，而它的消息现在点名
`D:\Git\neo-n4\docs\audit\testnet-deployment-20260716-live.json` —— 一条正确的路径 ——
而修复前的消息点名的是 `D:\Git\neo-n4\tests\docs\audit\…`。把文件放回去，同一道测试报告 Passed，
而且这不是一次空过的通过：它断言 `L2ChainId == 20260716`、`Contracts.Count == 24` 以及一字不差的
`ChainRegistry` hash，所以这是 Windows 上第一次拿解析器去比对真实的证据文件。`dotnet format
Neo.L2.sln --verify-no-changes` 干净。

### V5 — 兑付路径的 Merkle 验证器被 mock 掉了，恰在那个本应捕获伪造 leaf 的测试里 [E1]

`tests/NeoHub.Contracts.VmTests/UT_SharedBridge_Vm.cs:69-72` 为每一个 SharedBridge 测试在 fixture
中安装了 `VerifyWithdrawalLeafWithProof(It.IsAny<…>) → true`。后果是：**任何地方的 VM 测试都没有
exercised 那条真正付钱的链上路径上的提取 inclusion 校验。** `UT_SharedBridge_Vm` 中每一条接受/拒绝
断言都是在验证器被 stub 成一个常量的情况下通过的，因此一次让折叠接受任何东西的回归不会被捕获。

独立地看，两条链上折叠都不受位置绑定。`VerifyWithdrawalLeafWithProof:989-1012` 与
`VerifyStateLeafWithProof:1115-1134` 形状相同，且都以
`return storedRoot.Equals((UInt256)current);` 结束 —— 在循环中按所提供的每个 sibling 把 `index`
移位一次，而当循环结束时**没有**任何对 `index == 0` 的检查。因此在 leaf 索引 `i` 上有效的 proof
对任意 `k ≥ depth` 在 `i + 2^k` 上同样有效 —— inclusion 绑定的是 leaf 哈希，而不是唯一的位置，
这就是 `C2` 那一类缺陷在合约中的形态。

爆炸半径比单看 `C2` 所暗示的要小，而值得把原因说准确。
`SharedBridge.FinalizeWithdrawalWithProof` 会由所声称的字段重新推导 leaf
（`ValidateWithdrawalLeafBinding`、`:326-328`），并按 `WithdrawalKey(chainId, withdrawalLeafHash)`
去重（`:329`），因此兑付锚定在 leaf 的*内容*上，并且在不引用位置的情况下受到重放保护。
所以这个位置缺口今天并不会在那条路径上导致盗币。它真正破坏的是任何把 `(root, index)` 当作身份的
消费者 —— 一个按 index 去重的 relayer，或一个以 index 寻址的 "has withdrawal #n been proven" 查询 ——
它们可能被出示同一个 leaf 的两个位置。两条折叠的修复方式都是补上终止条件（被存储的 root 不携带
depth，因此正是 `index == 0` 这项检查在绑定位置），去掉 `UT_SharedBridge_Vm` 的 stub，
并为每条折叠各加一个反向 VM 测试。`C2` 仍未修复，而且是两份报告中价值最高的未修复项。

### V6 — 仓库里守卫最严密的那个表面有一处绕过，而它恰好落在宕机路径上 [E1 counted]

telemetry 值得在讲缺陷之前先记一功。`MetricNames.cs` 声明了 39 个 metric 常量，
`MetricCatalog.Descriptions` 恰好有 39 个以这些常量为键的条目，而
`tests/Neo.L2.Telemetry.UnitTests/UT_MetricCatalog.cs:13-26` / `:32-43` 通过反射在**两个**方向上
强制这一映射 —— 一个没有描述的新常量，或一条指向已被删除常量的描述，都会让构建失败。
全部 39 个名字也都出现在运维者目录里（`docs/telemetry.md` §"Metric catalog"；
对该文档按名字逐个 grep 发现零个缺失），而 `docs/telemetry.md:214-226` 处的 exposition 样例与代码
实际渲染出来的东西相符：`PrometheusExporter.cs:15` 记录了 `.` → `_` 这条映射（Prometheus 禁止使用点号），
`:129` 实现它，counter 获得 `_total`（`:39`），histogram 渲染为 `summary` 并带 `_count` / `_sum` / `_max`
（`:42-59`）。

这道守卫的盲点在于它反射的是*常量*，因此它看不见一个从不自定义常量、直接传字符串的调用点。
这样的调用点恰好只存在一处：

```
$ git grep -nE '(Safe)?(IncrementCounter|SetGauge|RecordSummary|Observe)\("[^"]+"' -- src tools
src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:477:  _metrics.SafeIncrementCounter("l2_batch_on_block_committed_error");
```

那个名字不在 `MetricNames` 里，于是 `MetricCatalog.GetHelp` 落到 `MetricCatalog.cs:18` 的那个通用
字符串上，而该 metric 在任何文档中都没有记载 —— 与此同时，仓库自己那份“如何新增一个 metric”
流程的第 1 步（`docs/telemetry.md:231`）写的就是要先声明一个常量。孤立地看后果很小，
但它的位置特别糟：`:477` 正是 `H1` 所指的那条能够把节点停掉的路径上、
在 `OnBlockCommitted` 的 catch 块里被递增的那个计数器。它是运维者恰恰在那场事故中会去画成曲线的
那个数字，而它渲染出来带的是一行占位的 HELP 和一个并不存在的目录条目。

修复分两部分。立刻：把这个字面量提升为一个 `MetricNames` 常量并给它一条目录条目，
此后那道既有的反射测试就会让它保持诚实。长期：增加一道扫描发射点上字符串字面量的检查，
因为目前的完备性测试遍历的是*常量*，所以在结构上就看不见那段绕开了登记表的代码。
这才是这一发现可泛化的那一半 —— 一道以登记表为键的完备性检查，检测不到一个绕过它的调用方。

### V7 — SP1 队列的“先查存在、再读内容”窗口不容忍任何共享冲突，而逃逸出的异常是无类型的 [E1]

这处是在度量 H17 时撞见的，不是刻意去找的。Windows 上连续两次全解决方案运行：第一次报
`2,897 总计 / 1 失败`，第二次报 `2,897 / 0`。失败出现在 `tests/Neo.L2.Proving.UnitTests`，
而那条分支根本没碰这个项目：

```
Failed ProveAsync_TamperedExecutionSemantic_IsRejected [60 ms]
  Assertion failed. Expected exception of exact type InvalidDataException but caught IOException.
  System.IO.IOException: The process cannot access the file
  '…\neo-n4-batch-prover-tests\f73f636e…\f07120bf…78d5c.proof.result.json'
  because it is being used by another process.
    at AtomicFileQueueTransport.ReadBoundedPathAsync (AtomicFileQueueTransport.cs:265)
    at Sp1BatchProofProver.ReadAndValidateResultAsync (Sp1BatchProofProver.cs:208)
```

复现率是量出来的，不是猜的：全解决方案两次运行里出现一次；而单独运行
`tests/Neo.L2.Proving.UnitTests` 是 `3/3` 通过 —— 这是争用的特征，不是逻辑的特征。

允许它发生的代码形状是一个没有容忍度的 check-then-use 窗口。`ProveAsync` 先等结果文件出现
（`Sp1BatchProofProver.cs:154`），然后才去读它（`AtomicFileQueueTransport.cs:249-269`）；
`ReadBoundedPathAsync` 把*缺失*（`:256-258`）、*超限*（`:262-264`）与*读取期间被改动*（`:266-268`）
都转成 `InvalidDataException`，但 `:265` 那次 `File.ReadAllBytesAsync` 恰好落在所有这些守卫之外，
于是那一瞬间的共享冲突会以裸 `IOException` 逃逸。在 Windows 上这个窗口是真实的：此处的写入方使用
`FileShare.None`（`:137`、`:294`）并以 `File.Move` 就位（`:106`），而一个刚被改名进临时目录的文件
可能短时间被过滤驱动持有（在 38 个程序集并行运行下，通常就是杀毒软件）。仓库自己也承认这一点，
因为共享冲突在其它四处早已被捕获后重试或吞掉 ——

```
$ git grep -n "catch (IOException" -- src
src/Neo.L2.Executor/Witness/Sp1StatefulBatchExecutor.cs:437:  catch (IOException) { }
src/Neo.L2.Proving/RiscVZk/AtomicFileQueueTransport.cs:108:   catch (IOException) when (File.Exists(path))
src/Neo.L2.Proving/RiscVZk/AtomicFileQueueTransport.cs:147:   catch (IOException) when (stopwatch.Elapsed < _resultTimeout)
src/Neo.Plugins.L2Gateway/Sp1GatewayProofProver.cs:377:       catch (IOException) when (File.Exists(path))
```

—— 其中两处就在同一个 transport 里，位于它的写入与获取发布锁的路径上。所以读路径缺的不是没人想到
的容忍度，而是既有惯例唯一没被应用到的那个位置；而逃逸出的异常，也正是该 transport 自身
`InvalidDataException` 约定之外的唯一一类。

后果是有界的，且值得说准：这是一个*瞬态*变成一个*类型化失败缺口*，不是一次被证明的结算中断。
测试之所以能抓到它，只因为它断言的是精确异常类型而不是“某个异常”。修复：把既有的重试惯例应用到
`ReadBoundedPathAsync` 的那次读取上（文件此时已被确认存在，所以这是等待，不是语义变更），并明确
决定重试耗尽后的 `IOException` 是否归入协议的 `InvalidDataException` 家族。
`src/Neo.Plugins.L2Gateway/Sp1GatewayProofProver.cs:415-432` 提供了一个结构完全相同的 helper ——
同样的 `File.Exists` 检查、同样无守卫的 `File.ReadAllBytesAsync`（在 `:429`）—— 所以无论答案是哪个，
两处读取都应保持一致。

### V8 — CI 里唯一那道 Rust 依赖门禁看不见 Dependabot 报出的那些公告，而其中 High 那一条是活的 [E1 门禁盲区 + 可达性]

GitHub 上这个仓库有三条 open 的 Dependabot 告警。三条都是 Rust，三条都出自*同一份* `Cargo.lock`，
并且三条都是同一天建立的：

```
$ gh api "repos/r3e-network/neo-n4/dependabot/alerts?state=open"
3 | high | p3-challenger  | < 0.4.3        | first patched 0.4.3 | GHSA-vj64-rjf3-w3v7  | created 2026-07-15
2 | low  | p3-symmetric   | <= 0.5.2       | no patch            | GHSA-3g92-f9ch-qjcm  | created 2026-07-15
1 | low  | lru            | >=0.9.0,<0.16.3| first patched 0.16.3| GHSA-rhfx-m35p-ff5j  | created 2026-07-15
```

其中两条早已有书面结论：`docs/audit/sp1-transitive-advisories-2026-08-28.md` 评估了
`p3-challenger` 与 `lru`，记录了 RustSec 没有对应条目因此 CI 门禁保持绿色，把修复方案指明为一次
协同的 SP1 升级而不是 lockfile bump，并引用了那次被关闭的 Dependabot sp1-6.3.1 尝试（PR #23，
4 个检查失败）。那份笔记是有用的工作，本发现不复述它。V8 补充的是那份笔记在 2026-08-28 还无法说出的
四件事，每一件都是在这里对着被钉扎的源码量出来的，而不是推断出来的：challenger 公告的*两个*机制里
究竟哪一个在 `0.3.3-succinct` 这个 fork 里仍然成立（那份笔记明确拒绝猜，称该 backport
“not publicly recorded”）；这次升级的具体目标发布版本；对 `p3-symmetric` 的评估（那份笔记完全没覆盖）；
以及接受风险台账上的一处缺口，在本节末尾点出。

这个仓库对“我们的 Rust 依赖审计过了吗？”的回答是一个 CI job：

```
$ grep -n "cargo audit" .github/workflows/build.yml
590:      - name: cargo audit (production Rust lockfiles)
600:          for lockfile in \
601-            Cargo.lock \
...
607-            cargo audit --file "$lockfile" --ignore RUSTSEC-2026-0258 --json
```

那个循环从 `Cargo.lock` 开始 —— 正是 Dependabot 点名的那份 manifest —— 而在本地对着当天的
advisory 数据库它是通过的：

```
$ cargo audit --file Cargo.lock --json | head -c 300
{"database":{"advisory-count":1226,"last-updated":"2026-08-29T08:11:09+02:00"},
 "lockfile":{"dependency-count":614},"vulnerabilities":{"found":false,"count":0,"list":[]}
```

所以那盏绿色的 `cargo audit` 对这三条公告不构成任何证据：这个 job 读的是 RustSec，Dependabot 读的
是 GitHub Advisory Database，而针对 `p3-challenger` 这两个数据库给出了矛盾的答案。这又是 §5 那个形状
—— 一道因为与它所声称要断言的性质无关的原因而变绿的检查 —— 只是这里有一点值得单列的区别：与其他
V 类发现不同，这里没有任何东西是写错的。仓库其实已经刻意地承载过一次同类的不一致，带着解释性注释和
一条针对 `RUSTSEC-2026-0258` 的 `--ignore`（`build.yml:592-599`）。区别在于 h2 那一例是被披露的，
而这一例是看不见的。

**这条 High 不是噪音，而一次针对本树的 grep 会把它错误地排除。** 公告标题里点名了
`MultiField32Challenger`，而它在仓库里的全部命中都只是审计文档：

```
$ git grep -ln "MultiField32Challenger"
docs/audit/sp1-transitive-advisories-2026-08-28.md      ← 以及本报告；没有 .rs，也没有 .cs
$ sed -n '6p' ~/.cargo/…/slop-challenger-6.2.1/src/lib.rs
pub use p3_challenger::*;
```

被点名的这个类型是在一个改名后的 crate 之下被整体再导出的（`slop-challenger` → `slop_*` 系列），
并且正是捆绑的 SP1 路径所证明的那批递归配置的 transcript challenger：

```
~/.cargo/…/slop-basefold-6.2.1/src/config.rs:13,50   MultiField32Challenger<F, Bn254Fr, OuterPerm, …>
~/.cargo/…/slop-bn254-6.2.1/src/lib.rs:17,75,104     type Challenger = MultiField32Challenger<…>
Cargo.lock:2718                                       p3-challenger 0.3.3-succinct
```

**在被钉扎的这一对参数上，什么成立、什么不成立。** 公告正文描述的是较新的 Plonky3 代码
（`reduce_32`、`num_f_elms = PF::bits() / 64`）；被钉扎的 fork 并不是那一版，所以标题里的两个断言
必须分别对着真正交付的代码来核对：

- *挑战值熵损失* —— **不成立。** 被钉扎的 `num_f_elms` 是
  `PF::bits() / F::bits() / 2`（`p3-challenger-0.3.3-succinct/src/multi_field_challenger.rs:47`），
  在 BN254 这一对参数上等于 4 个 2⁶⁴ 基的 limb = 256 位，因此 `split_32`（`:77`）覆盖了完整的
  254 位域元素。公告里那个 3-limb 版本（192 位）就做不到。
- *transcript 可延展性* —— **成立。** `duplexing()` 通过 `reduce_31` 吸收
  `input_buffer.chunks(num_duplex_elms)`（`:66-67`、
  `p3-field-0.3.3-succinct/src/helpers.rs:134`），既不记录 chunk 长度，而 `sample()` 又会把手上
  那截不完整的 buffer 直接 duplex 掉（`:172-175`）。于是末尾一个零观测吸收后的状态与根本没有那个
  观测时相同：海绵的输入不是单射的，只差若干末尾零元素的两条 transcript 会采样出同样的结果。

这是那条活的代码路径上一个真实的性质，而它的后果的边界就是 transcript 可延展性一贯的边界：它让一个
prover 能在不改变挑战值的前提下改写自己的 public inputs，这对任何把 transcript 或其承诺的 public
inputs 当作唯一的东西来说都是有意义的。它不是一个伪造证明的结果，我也没有尝试针对结算路径构造一个
—— 剩下的问题是 N4 侧是否有消费者依赖 batch 或 Gateway sidecar 之间 transcript/proof-input 的唯一性，
那是对 `AtomicFileQueueTransport` 与 sidecar 绑定的分析，不是对这个 crate 的分析。

**修复路径现在有了名字。** 08-28 那份笔记正确地得出被钉扎的图里没有任何版本能表达这个修复，并且要求
“a release whose dependency graph pins `p3-challenger >= 0.4.3`”，但没有指出这样一次发布存在。它存在 ——
当前的 `slop-challenger` 正好要求那个已修复的构建：

```
$ curl -s https://crates.io/api/v1/crates/slop-challenger/6.5.0/dependencies | grep -A1 p3-challenger
"crate_id":"p3-challenger"  "req":"=0.4.3-succinct"
```

但 `slop-challenger 6.2.1` 要求的是 `0.3.3-succinct`（即 `^0.3.3-succinct`），而本仓库以精确版本钉扎
这条工具链，涉及仓内三份 manifest 加那份被 vendor 的 workspace：

```
bridge/neo-zkvm-host/Cargo.toml:31,50          sp1-sdk = { version = "=6.2.1", … }
bridge/neo-zkvm-gateway-host/Cargo.toml:28,36  sp1-sdk = { version = "=6.2.1", … }
external/neo-zkvm/Cargo.toml:27                sp1-sdk = { version = "=6.2.1", … }   (submodule)
```

因此要关掉这条 High，需要一次 SP1 6.2.1 → 6.5.0 的发布 bump，而这次 bump 是贴着规范的工作，
不是依赖刷新：`doc.md:372` 把“SP1 6.2.1 compressed proof”写成了需求文本，`AGENTS.md`、
`ARCHITECTURE.md` 与 `IMPLEMENTATION_STATUS.md:266-267` 都点名 6.2.1，build script 从单一的 Docker ELF
快照嵌入 SHA-256/VK，而 `NeoHub.Sp1Groth16Verifier` 是一个不可变的、兼容 SP1 v6.1 的 wrapper，通过
BN254 interop 验证。以上每一项都要针对 6.5.0 重新确立，而被 vendor 的 submodule 住在
`r3e-network/neo-zkvm`（它当前的 gitlink 已经在一条 `codex/rustsec-2026-refresh` 分支上），
所以这是一个跨仓变更。另外还要注意：即便完成了这样的 bump，解析出来的版本会是 `0.4.3-succinct`，
而它在 semver 里排在 `0.4.3` *之前*（更小）—— Dependabot 之后是否会关掉这条告警，这里没有验证，
必须去查，不能假设。

`lru` 那条不需要新分析：`lru 0.12.5` 确实由 `sp1-prover 6.2.1` 引入
（`[dependencies.lru] version = "0.12.4"`），而该需求之内不存在已修补的发布 —— 这也正是 08-28
那份笔记已经得出的结论。

`p3-symmetric` 是仓库从未评估过的那一条，而我对它的第一遍判断以一种有教益的方向错了：把公告的包名
对着一个从标题里猜出来的符号去 grep，会显得那个脆弱构造在 `0.3.3-succinct` 里不存在。它并不是不存在。
公告针对的是 `PaddingFreeSponge::hash_iter`，它在处理最后一个不完整的 block 时把状态里的旧元素留在
原地，而被钉扎的这个 crate 里两个海绵变体都在，且都还没修：

```
$ grep -n "pub struct" ~/.cargo/…/p3-symmetric-0.3.3-succinct/src/sponge.rs
15:pub struct PaddingFreeSponge<P, const WIDTH: usize, const RATE: usize, const OUT: usize>
52:pub struct MultiField32PaddingFreeSponge<
$ grep -rn "Pad10Sponge" ~/.cargo/…/p3-symmetric-0.3.3-succinct/src/     # → no matches
```

`Pad10Sponge` 是上游修复的那一半，所以被钉扎的 fork 携带着脆弱的行为。它是可达的，并且在 BN254 配置
里是活的 —— 该配置把 `type Hasher` 设为 `MultiField32PaddingFreeSponge<…>`
（`slop-bn254-6.2.1/src/lib.rs:83`）。收窄它的是这段话，出自公告自己的 impact 一节、而不是这里现编的
说辞：*“在待哈希元素数量事先已知且固定的场合（多数 STARK 即是如此），该方法是抗碰撞的。这个脆弱性
只在恶意用户能够操纵待哈希元素数量时才适用。”* 在本树里，prover 栈中两处 `hash_iter` 调用点分别是
`slop-merkle-tree-6.2.1/src/p3sync.rs:137`（哈希一个固定的字面量数组）与
`slop-merkle-tree-6.2.1/src/tcs.rs:146`（为一次 FRI 批量 decommitment 哈希
`vec![claimed_values_slices]`，其长度跟随 query 形状）。SP1 的对抗方能否操纵*那个*长度，是唯一没查的
问题，而我没有去追：这个 crate 只在运维者一侧，而 08-28 那份笔记的威胁模型本就把恶意运维者放在这条
信任边界上 —— alert #3 自己的 impact 陈述也落在同一处。Low、可达、在被钉扎的图里无法修补，
修复方案与另外两条相同。

接受风险的台账里还有一处缺口，而它正是本 §5 反复撞见的那一类。
`.github/dependabot.yml:26-35` 为 cargo 生态忽略了 `lru` 与 `p3-challenger`，指向那份笔记，
其声明的目的是让 security-update jobs 不再失败。这一点做到了 —— 但同样合理地，任何人都会把那段
配置读成“这两条已经处理了”。它们并没有关闭。今天 Security 标签页里三条告警全部仍是 open，
距其建立已六周，而且再加一条 ignore 也不会关掉它们：`ignore` 抑制的是拉取请求，不是告警。
书面的接受风险决定与可见的告警状态互相矛盾，而读过那份笔记之外的人只能看见后者。

## 6. Medium / Low 发现（本轮新增）

- **`SealedBatch` 丢弃了 batch 的消息那一侧** [E1]。`BatchBuilder.AddWithdrawal`、
  `AddL2ToL1Message`、`AddL2ToL2Message`（`src/Neo.L2.Batch/BatchBuilder.cs:85-106`）把数据暂存进
  `_batch`，但 `SealArtifact`（`:138-170`）返回的 `SealedBatch` 只携带交易、L1 消息与强制包含条目
  （`src/Neo.L2.Batch/SealedBatch.cs:15-17`）。一个交出 `SealedBatch` 的调用方无法重建提取 root
  当初承诺了什么。今天它是潜伏的，因为插件路径使用的是 `BatchExecutionResult` / `ToCommitment`；
  这是一个一旦被当作传输载体就会静默丢数据的 API。
- **`ContractManifest.ToJson()` 的字节进入了 state-root leaf** [E1]。
  `Sp1StateWitnessSource.cs:271` 把每个合约的 manifest 序列化为 UTF-8 JSON 并馈入
  `StateWitnessV1Serializer.ContractBindingHash`（调用点 `:73`），因此规范 root 现在取决于上游
  manifest 的 JSON 顺序。对 `nccs` manifest 发出的任何改动 —— 包括一次不触碰任何 N4 源码的
  编译器同步 commit —— 都会移动每一个 root。
- **一次重组会停止节点而不是回退** [E1]。`RecoverAndProcessCommittedBlocks` 抛出
  `"committed L2 block {index} is missing from the local ledger; recovery cannot skip it"`
  （`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:497-500`），处理器重新抛出（`:479`），而
  `Plugin.ExceptionPolicy` 默认为 `StopNode`
  （`external/neo/src/Neo/Plugins/Plugin.cs:74`），并且**没有**任何第一方覆写 —— 在 `src/` 下做
  源码作用域的 `ExceptionPolicy` grep 完全返回零结果，所以这适用于每一个 L2 插件，不只是 batcher。
- **`WithWriter` 会静默降级 DA profile** [E1]。
  `src/Neo.Plugins.L2DA/L2DAPlugin.cs:163-175` 无条件设置 `_profile = Development`（`:169`）并清除
  `_productionBackendOverridden`（`:174`）。这之所以要紧，是因为该插件其余部分是按 fail-closed
  设计的：`ResolveProfile:218-221` 把每一个非 `Local` 模式默认到 `Production`，而
  `BuildDefaultWriter:134-136` 在 `Production` 下抛异常，而不是去实例化一个模拟 writer。
  一个调用 `WithWriter` 的宿主会一次性绕过这两道守卫 —— `Configure:102-109` 随后以 Development
  profile 运行 `ValidateConfiguredBackend`，于是强制独立 reader 这项要求被豁免，而 Production
  所拒绝的语义模拟 writer 变得可达。doc-comment 把该方法的作用域限定为
  "development and integration environments"，但没有任何东西强制这一点，也没有任何一行日志说明
  该保证已被放弃。
- **提交线程上的 sync-over-async** [E1]。`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs` 中有五处
  `.AsTask().GetAwaiter().GetResult()` —— `:385`、`:387`、`:583`、`:652`、`:655` ——
  从 `Committed` 路径上阻塞在 L1 I/O 上；2026-08-29 报告的健壮性结论已经点到这一点；
  它就是 H1 那个可远程表达的宕服的投递机制。
- **`JsonRpcL1DAWriter.IsAvailableAsync` 把四种状态混成一个 `false`** [E1]
  （`src/Neo.Plugins.L2DA/JsonRpcL1DAWriter.cs:127-158`）：一个与本 writer 的模式不匹配的
  指针/元数据（`:133-136`）、一个非对象响应、`state != "HALT"` —— 也就是 DA 合约自身 FAULT 了 ——
  以及一次真正的不可用，全都返回同一个 `false`，于是配置错误的 DA 层与确实已经消失的数据无法区分，
  而节点会静默地偏向它的回退路径。传输失败是唯一会浮现出来的情形，因为 `CallAsync` 会抛异常，
  而不会被吞成 `false`。
- **每一个内置 DA writer 都是模拟，而这里不交付任何真实后端** [E1]。
  `NeoFsLikeDAWriter` 是一个进程内的 `ConcurrentDictionary`，它自己的文件头就说它
  "does not contact NeoFS or survive process restarts"（`src/Neo.Plugins.L2DA/NeoFsLikeDAWriter.cs:1-27`，
  `ReceiptKind = SemanticSimulation` 在 `:26`）；`CommitteeAttestedDAWriter` 携带同一种 receipt
  类型（`:46`、`:163`）。一个生产级的类型确实存在 —— `MetricsEmittingProductionDAWriter:15`
  实现了 `IProductionDAWriter` —— 但它是套在注入的内部 writer 之上的一个 metrics 装饰器，
  而不是一个后端。因此对 `DAMode.NeoFS`、`.DAC` 与 `.External`，仓库根本不发出任何实现：
  一条宣称 NeoFS 数据可用性的链，在这棵代码树里完全依赖运维者自己提供一个适配器，
  而且没有任何东西校验该适配器的声称是真的、而非仅仅格式良好。这是一条关于组合边界的观察而非缺陷 ——
  fail-closed 的默认值（§9）正是对它的正确回应 —— 但它应当写进 `doc.md` §12，
  而不是留待人们通过读 `L2DAPlugin.cs:134-136` 那处抛出来自己发现。
- **batch 内的 nonce 闸门以执行器对象为作用域，而非以 batch 或状态为作用域** [E1]。
  `_consumedNonces` 在两个执行器上都是一个 `readonly HashSet<(UInt160, uint)>`
  （`src/Neo.L2.Executor/ApplicationEngineTransactionExecutor.cs:60`、`.Add:133`；
  `src/Neo.L2.Executor.RiscV/RiscVTransactionExecutor.cs:52`、`.Add:126`），既从不清空也从不持久化。
  测试之外，唯一生产风格的构造点是 `tools/Neo.L2.Devnet/Program.cs:204`/`:219`，
  即整个进程生命周期内的同一个对象 —— 因此此前 `H10` 的增长项在那里成立，而镜像风险是那个没人会
  察觉的："duplicate sender nonce" 拒绝的持久性只和这个对象一样长，因此任何按 batch 构建执行器的
  宿主都会静默失去重放检测。（track 报告还断言 `:126-128` 处有一条声称 batch 作用域的注释；
  并不存在这样的注释 —— 已在 §8 更正。）修复：把账户 nonce 从状态存储中读出，作为唯一权威来源，
  或者把这道闸门连同 batch checkpoint 一起持久化。
- **一道在防御宿主根本不会产生的场景的守卫** [E1]。
  `L2BatchPlugin.cs:457-460` 让 `_sealer` 跨 `Configure()` 存活，理由是 "if Configure ever runs more
  than once (config-watcher re-fire, host re-init)"。核心的 config watcher 并不会重新调用
  `Configure` —— 它记录 `"File {File} is {ChangeType}, please restart node."`
  （`external/neo/src/Neo/Plugins/Plugin.cs:126`）。于是那条静默忽略已更新设置的分支，
  是为一个永不触发的触发器而存在的，而运维者可见的效果是：编辑 batch 阈值看起来成功了。
  修复：去掉这种臆测，并在读取设置的那个位置明确说明需要重启。
- **未被文档记录的每 batch 状态 witness 上限** [E1]。
  `src/Neo.L2.Batch/StateWitnessV1.cs:133` 设了 `MaxEntries = 65_536`，而 `:305` 拒绝任何超过它的
  witness，因此一个触及超过 64K 个状态键的 batch 会在 durable-artifact 路径上硬性 FAULT，
  而没有任何面向运维者的文档说明这个上限。
- **`OnBlockCommitted` 没有测试** [E1]。`UT_L2BatchPlugin.cs:206`、`:229`、`:304`、`:351`
  只经由 `ProcessCommittedBlock` 钉住重试路径；没有任何测试引用 `OnBlockCommitted`，
  而 `InvokeCommitted` 出现在零个测试中。H1 的修复所依赖的那个恢复行为，
  是关键路径上被测最少的一段代码。
- **强制包含接口文档化了一道任何代码都没有实现的闸门** [E1 counted]。
  `src/Neo.L2.ForcedInclusion/IForcedInclusionSource.cs:36-38` 关于 `HasOverdueEntryAsync` 写着
  "the batcher uses this to decide whether to halt finalization for censorship reasons"。
  没有任何代码这样使用它。两次 grep 分别执行：`git grep -n "HasOverdueEntry" -- src tools`
  只给出两个非测试消费者 —— `CensorshipDetector.cs:79`，它自己的 `:73-74` 就写明检测器
  “does NOT consume the queue”、报告在运维者提交之前只是建议性的；以及
  `LocalHostCompositionBase.cs:510`，一个包装函数。
  随后的 `git grep -n "HasOverdueForcedInclusion\|HasOverdueCachedEntry" -- src tools`
  显示这个包装最终落到哪里：全部是运维者*状态*字段 ——
  `LocalHostCompositionBase.cs:507-520,545,640,717,1707`、`LocalHostOperatorStatus.cs:299,747`、
  `LocalHostHealthProbeDocument.cs:348`、`LocalHostOperatorStatusDocument.cs:252,490`、
  `NeoHubDeployReport.cs:454,511` 与 `InitL2Command.cs:162`。`BatchSealer` 和 `L2BatchPlugin`
  两份名单里都不出现，所以没有任何终局化路径会去读取它。实际行为其实比文档所述更强：
  `BatchSealer.cs:236-240` 在每一个新 batch 的*开头*、在任何区块交易之前把队列排空，
  而 `:338-359` 对一个 null/超限/空值的排空 fail closed，配合 `L2BatchPlugin.cs:642-663`
  在**完全没有**任何截止期检查的情况下排空所有待处理条目。所以一个搞审查的 sequencer 无法跳过一笔
  强制交易，而文档所描述的那个 "halt finalization" 机制既不存在也不必要 —— 但一位读接口的运维者
  会去找一个错的安全属性，而更糟的是，未来某次重构可能“恢复”一道会把健康链停滞住的 halt。
  修复只涉及文档：把那个真实存在的 prepend-and-drain 保证描述清楚。
- **2026-08-29 那条关于 escape hatch "faults without manual pauser registration" 的论断，
  对 live 部署器而言如今已被推翻** [E1]。`ReportCensorship` 只能经由
  `ChainRegistryContract.cs:482-485`（`CheckWitness(owner) || IsPauser(callingScriptHash)`）暂停，
  而 `ForcedInclusion` 没有自助注册路径 —— 但 `tools/Neo.Hub.Deploy/LiveDeployCommand.cs:801-802`
  如今会发出 `registerPauser(ForcedInclusion)` 并带一条 `ChainRegistry.IsPauser` 的读回断言，
  默认就会执行（`:57`），且发生在 `:861-862` 的 `ChainRegistry.LockGovernance` *之前*。
  残余的那一半 —— `RegisterPauser` / `RevokePauser` 在锁之后仍然存活 —— 已经记录在 §7.1 中，
  而且才是值得修的那一项，因为它意味着在部署器已经宣告治理终局之后，暂停权限仍然可由 owner 改写。
- **`docs/zh/CHANGELOG.md` 承诺了一项它并没有执行的同步** [E1 已计数]。它自己的文件头就写着这条规则
  （`:4`：英文文档发生结构、命令、路径、接口、合约数量、测试证据或安全结论变更时，本中文版本必须同步
  更新），而本地化闸门正是为守住这一对文件而写的 —— 但这条闸门断言的只是一个对应文件*存在*：

  ```
  $ grep -c "2026-08" docs/zh/CHANGELOG.md
  0
  $ git log --oneline -1 -- docs/zh/CHANGELOG.md
  a647886d Stabilize the coverage gate (#30) [skip ci]
  ```

  这份中文文件里没有晚于 2026-07-15 的内容，而 `CHANGELOG.md` 在 `master` 上于 2026-08-28 → 2026-08-30
  之间新增了九条带日期的条目。其中的 `C1`、`H12`、`C4`、`H16`、`H17` 这些安全与测试证据记录，恰好就是
  它自己那条规则点名为必须的类别，而本分支新增的两条（`H19`、`V8`）一落地就会把同一个缺口进一步扩大。
  于是中文读者被书面告知：这份文件会跟踪安全结论；而它已经连续六周没有跟踪过任何一条。修复方式是一个
  关于“哪一份产物才是真的”的决策：要么回填内容、并让一条测试在两者之间比对条目标题，要么从文件头删掉
  这句承诺、把这份文件按它本来的样子标注 —— 一份过时的摘要。保持现状是唯一一个选择：它让一条被文档记录
  的不变式始终为假，同时让一个绿色的测试把这一对文件认证为“已同步”。

## 7. 本轮重新核实的既有发现状态

| 既有发现 | 当前状态 | 证据 |
| --- | --- | --- |
| `C1` deposit/router 收件箱相撞 | **已修复**（本分支） | `L1MessageDrain.cs` 中的两段式去重 + 全序，`UT_L1MessageDrain` 回归测试 |
| `C2` `MerkleTree.Verify` 不受位置绑定 | **未修复** —— 而且同一个形状出现在两条合约折叠之中（§5 V5），由于兑付测试把 verifier 做了 stub，它不可被观察 | `SettlementManagerContract.cs:989-1012`、`:1115-1134` |
| `H1` 插件异常会停止节点 | **未修复**，已提升为 [E1] | `L2BatchPlugin.cs:479 throw;`、`Plugin.cs:74` 默认值、`src/` 中零个 `ExceptionPolicy` 覆写 |
| `H6` 装饰性的链下二进制钉扎 | **未修复**，证据等级如今升到 [E1]，其测试的期望摘要由被测二进制自身派生，且没有反向测试（§5 V3） | `UT_Sp1StatefulBatchExecutor.cs:318` |
| `H12` 信任根上的治理锁 | 就本分支覆盖的三根而言**已修复**；该模式在别处仍不完备（§7.1） | `ChainRegistryContract.cs:158-168,172-181,389` |
| `H13` kill-switch 覆盖 3 个资产合约中的 1 个 | 全局标志**未修复**；它的按链变体（§4 H16）**已修复**（当前分支） | 审计时点为 `SubmitBatch:330-331` 对比 `FinalizeBatch:479-533`；`FinalizeBatch` 现在在 `:509-510` 断言 `isActive` |
| `H2` FI 截止期短于它所暂停的挑战窗口 | **重新确认** | `ForcedInclusionContract.cs:195` 界定为 `[60, 86400]`，而 `OptimisticChallengeContract.cs:246` 允许 `[60, 7*86400]` —— 一个 7 天的窗口配上 24 小时的截止期，会让 `ReportCensorship:503` 暂停一个仍可被挑战的 batch。§4 H19 是它的镜像那一半：*部署期*那个字段完全跳过了这条边界 |
| `H3` escape hatch 需要手工接线 | **一半被推翻** | `LiveDeployCommand.cs:801-802` 如今会在 `LockGovernance`（`:861-862`）之前注册该 pauser 并读回校验；只剩 `IsProductionReady()` 这条断言仍未落地（`ForcedInclusionContract.cs:254-266`）—— 见 §6 |
| `§3.1` Windows 自我跳过 | **已修复**（本分支） | 同样 2,893 道测试下全仓库跳过 45 → 5；`tests/Shared/RepoRoot.cs` 在 10 个文件的 33 处替换了那五层上溯，受影响的六个项目现在都报告 `Skipped: 0`（§5 V4） |
| `A4` 不可复现的 VM artifact | **未修复** | 未变；该 artifact 集合仍有两种编译器戳 |
| 治理完备性 | **部分未修复** | 见 §7.1 |

### 7.1 锁模式：已实现之处正确，四个表面仍然缺失

`ChainRegistryContract.RegisterChain:158-168` 是一道堪称典范的守卫 —— 在 `LockGovernance` 之后，
它拒绝改写一条*已存在*的链，同时仍然接受新的 chainId，这正是正确的非对称性。
`SetGovernanceController:172-181` 已被正确地冻结。同样的处理尚未抵达：

- `ChainRegistry.SetOwner:146-153` —— 仅 witness；所有权在锁定之后仍可被转移。
- `ChainRegistry.RegisterPauser:193-199` / `RevokePauser:202-207` —— 仅 witness；锁定之后，
  哪些合约可以暂停链的这个集合（H16 的机制）永远可被 owner 改写。
- `OptimisticChallenge.SetWindowSeconds:243`（下限 60 秒）与 `SetChallengerRewardBps:253` ——
  挑战窗口，也就是攻击者需要被抓场所花的那段时间，在锁之后仍可由 owner 调节。
- `L2NativeContracts.cs` —— `LockGovernance` / `IsGovernanceLocked` 零次出现；十个 native L2 合约
  根本没有锁的概念，而这是一个 core-fork 层面的决策（`r3e/neo-n4-core`），不是 `contracts/` 层面的。

## 8. 更正

在重新核实过程中发现的自我更正与行号漂移。以下每一条都是对照文件核查过的，而不是推断出来的。

1. **2026-08-29 报告中有一条陈述是错的，而我此前口头重复过它**：CI *确实* exercised 了 RISC-V
   native host —— `build.yml:289-300` 构建 `neo-riscv-host`、复制 `libneo_riscv_host.so`，
   并以 `NEO_RISCV_NATIVE_TESTS=1 --minimum-tests 10` 运行 `RealNative_` 测试。成立的事实范围更窄：
   那 10 个测试在其他所有地方都被门控关闭（上方的 H14/C3），而它们运行于其上的 artifact 是陈旧的（C3）。
2. `H1` 为重新抛出引用了 `L2BatchPlugin.cs:478`；`:478` 是那行日志调用，`throw;` 在 `:479`。
3. `H1` 把分派引用为 `Plugin.cs:280-284`；那是 `OnMessage` 路径。`Committed`
   的分派在 `external/neo/src/Neo/Ledger/Blockchain.cs:490-520`。
4. `ffi.rs` 的 panic 分支：此前上报为三处；正确数量是十处（§4 H14）。
5. 此前 §10 的 "CI's Linux-only `sp1-release-gates`" 轻描淡写了：该 job 仅限
   `workflow_dispatch`，而那道必需的聚合检查*断言*它被跳过（§5 V1）。
6. `src/Neo.L2.DA/` 与 `tests/Neo.L2.DA.UnitTests` 并不存在；DA 表面只有
   `src/Neo.Plugins.L2DA/`。任何列出 `Neo.L2.DA` 库的文档都是陈旧的。
7. `AGENTS.md` 与 `docs/zh/AGENTS.md` 描述了两个以 `ChainMode.L2RiscV` 为键的显式执行档。
   `src/Neo.L2.Abstractions/Models/ChainMode.cs:9-21` 只声明了 `L1Mode`、
   `SidechainMode`、`L2RollupMode`、`L2ValidiumMode`。在**代码**中，字符串 `L2RiscV` 在跟踪源码里
   只出现一处 —— `tests/Neo.Stack.Cli.UnitTests/UT_BootstrapGenesisCommand.cs:36`，在一个 JSON
   字面量内部 —— 其余 13 次出现分布在 12 个文档文件中
   （`AGENTS.md`、`docs/zh/AGENTS.md`、`WHITEPAPER.md` 及其中文版、`TASKS.md`、
   `docs/architecture-*`、`docs/tech-stack-coverage.md` 及其中文版），外加一条审计 JSON 标签
   （`docs/audit/riscv-zkvm-local-verify-2026-07-22.json:71`）。
   PolkaVM 路径是真实的；文档所点名的那个枚举成员不是。
8. 2026-08-29 报告中有两处悬空引用在本轮就地修复：§1 的 "compounds with A2 below" → `H1`
   （`A2` 并不存在；§11 从 `A4` 起），以及 §8 安全性那一行的 "custody credit (M)" →
   "§5, Medium, still open"。
9. 一条由 track 提供的对照未能通过复核，因此**没有**以原样进入本报告：它把
   `VerifyWithdrawalLeafWithProof:989-1012` 报告为带有终止条件、受位置绑定的那条折叠，
   与之对照的 `VerifyStateLeafWithProof` 则缺少终止条件。从头到尾读下来，两条折叠形状完全相同，
   而且*都*没有终止条件（§5 V5）。它还报告 `ffi.rs` 中有三处 `catch_unwind` 分支；数量是十处
   （§4 H14）。这两条更正各自降低了一个论断的严重度并抬高了另一个，这正是“重读”属于本方法一部分的原因。
10. `H10` 的 nonce 条目带着一个并不存在的缺陷到来：它报告
    `RiscVTransactionExecutor.cs:126-128` 处有一条断言 batch 作用域的注释，并把这一不符当作 bug 的
    一部分。`:123-128` 是 nonce 键、那把锁以及 `Add` —— 文件里没有任何关于作用域的注释。
    无界集合那一半通过了验证；注释那一半没有，于是 §6 现在改以双侧后果来陈述该发现。
11. 两条关于 DA 的论断是颠倒着到来的，此处把它们反过来。track 报告称
    "no in-scope `IDAWriter` implements `IProductionDAWriter`"，又称 DAC
    "is selectable only under Development"，仿佛默认路径在静默地模拟数据可用性。
    `MetricsEmittingProductionDAWriter:15` 确实实现了该接口，而真实的默认路径比所报告的更严格：
    `ResolveProfile:218-221` 对任何非 `Local` 模式强制 `Production`，而
    `BuildDefaultWriter:134-136` 在那里抛异常。存活下来的结论更窄，并已在 §6 中陈述 ——
    内置 writer 全都是 `SemanticSimulation`，这棵树里不交付任何真实后端，而 `WithWriter` 正是那个孔洞，
    被拒绝的模拟经由它重新变得可达（§9）。
12. 有五个缺陷躲过了我自己的引用复核，只在构造中文镜像时才被发现 —— 那一趟会把每一处行号引用
    重新对照磁盘读一遍，是本报告最严格的审阅者。五处均已在上面就地修正，因此源文与镜像现在一致：
    §3 把强制放出忽略规则的位置写成裸的 `.gitignore:3`，而它实际位于
    `external/neo-riscv-vm/.gitignore:3`（写错了文件，而就在两行之后正是在论证 submodule 的隐形性）；
    §4 H18 写成 `Find`，而 `TemplateCatalog.cs:63` 声明的是 `Resolve(string name)`；
    §5 V1 的围栏范围 `build.yml:563-567` 漏掉了那条断言，正确范围是 `565-569`；
    §5 V4 声称受影响的测试*数量*超过 §3.1 的 "~45"，而被证明的只是项目*分布*更广、
    总数从未重新统计；以及上面第 7 条称 `L2RiscV` "occurs in exactly one place in the repository"，
    而它在 13 个文件中出现 14 次 —— 该说法只对代码成立，如今那句也照此改写。
    我会把镜像保留为后续每一轮的一道审阅步骤，而不只是一道翻译步骤。
13. 由于 CI 已不再能为文档漂移兜底，本报告里每一处 `file.ext:line` 引用随后都被机械地抽取出来，
    并按三个维度校验：路径是否能在跟踪源码中解析、被引用的行号是否落在该文件的行数之内、
    以及那一行是否非空且不是孤零零的收尾大括号。100 处引用通过后两项；有两处未通过第一项，
    而这一类缺陷既躲得过我的手工重读、也躲得过镜像趟 —— 因为 basename 是能解析的，
    丢掉的只有目录：那个 guest 打包脚本位于
    `external/neo-riscv-vm/scripts/package-adapter-plugin.sh`，而不是顶层 `scripts/`
    （本仓库的 `scripts/` 只有 `ci`、`deployment`、`private-network`），
    而 `Committed` 分派位于 `external/neo/src/Neo/Ledger/Blockchain.cs:490-520`，
    不在 core 根目录的 `Blockchain.cs`。两处均已在上面修正。这趟扫描的残余局限：
    像 `:479` 这样的裸第二次提及不会被重新解析，仍然依赖上下文相邻关系本身是对的；
    而行形状检查能抓住落到空行上的 off-by-N，却抓不到落到另一条语句上的。
14. 本报告在写到自己的修复时有一句话是错的，而把它暴露出来的正是修 `V4` 这件事。§3 的 C4 状态段
    把全解决方案的 45 个跳过刻画成 "27 个 `Neo.Plugins.L2Settlement` + 9 个
    `Neo.L2.IntegrationTests` 的 env 门 + 9 个零散"。其中只有 27 那个数字是对的。
    `Neo.L2.IntegrationTests` 在其源码的任何位置都不读取环境变量，所以它那 9 个跳过与另外 31 个一样
    都是 `V4` 的证据文件跳过；而那一段话暗示出的两个变量 `NEO_SDK_LIVE` / `NEO_N4_RPC_URL` 属于
    `Neo.L2.Sdk.UnitTests` —— 一个从未出现在那句话里的另一个项目。测出来的分解是 40 个 `V4`
    加 5 个 env 门，§11 现已把五个逐一点名。值得记下来的是这个归类错误本身：一条消息写着 "not found"
    的跳过是证据问题、不是环境问题，而只读计数不读消息，让我把 40 个被静默停用的测试说成了主动谢绝
    执行的测试。

## 9. 在执行验证下站得住的部分

对于一份会被当作缺陷清单来读的报告而言，平衡是必要的。

- batch/state/DA 各库密布不变式，而随附的那些确实有效：五套测试无失败通过，
  `BatchSealer` 的排序、强制包含的 proof 绑定（`BatchBuilder.cs:146-158`）与连续性检查都是真实的，
  而非装饰。
- `SettlementManager` 的 gateway 前缘重建（`:799-852`）是一次真正受位置绑定的重建，
  它从已终局的记录重新推导两个 root，并在任何 Router 失败时回滚水位线 —— 这是仓库中最强的 merkle
  代码，也是 `VerifyStateLeafWithProof` 应当照着重写的那个模板。
- 治理锁模式，凡已实现之处都完全正确，包括 `RegisterChain` 中那处精妙的非对称，
  以及在 controller 尚未接线之前拒绝加锁。
- DA 插件的默认是 fail-closed，且理由在代码中有记录：
  `L2DAPlugin.ResolveProfile:218-221` 在配置省略 `Profile` 时把每一个非 `Local` 模式提升为
  `Production`，而 `BuildDefaultWriter:134-136` 随后*抛异常*而不是交回一个模拟，其消息点名了那条
  确切要求（"no local or simulated fallback is permitted"）。一个配置错误的 public-DA 节点会拒绝启动，
  而不是静默降级 —— 而这恰恰是 `WithWriter`（§6）本不应被允许绕过的属性。
- v4 fraud-verifier 的作用域限制既诚实又被强制执行：
  `RestrictedExecutionFraudVerifierContract.cs:566` 拒绝除一笔 29 字节的 `IncrementCounter` 交易之外的
  一切，而 `:570-574` 要求 tx index 0 且 depth 为 0 的 proof，也就是单交易 batch。`Reject(…)`
  （`:684-687`）只发出一个事件并返回 `false` —— verifier 内部不会罚没任何 bond，
  因此一次超出作用域的 proof 让挑战者付出的是一笔交易，而不是他们的质押金。
- SP1 Groth16 包装器、BN254 interop parity、充值/提取的 CEI 纪律，以及 state-root 生成器中的
  原子交接，都维持了 2026-08-29 的结论，且它们在本轮所运行的测试下都没有回归。
- 挑战合约没有可供攻击的回合，因为链上根本不存在二分。它全部的按 batch 状态就是三个键 ——
  `:34-36` 那几个前缀，由 `BuildKey:873-881` 拼成 prefix ‖ chainId ‖ batchNumber，
  任何地方都没有回合或分段索引 —— 所以双方都没有可供重新指向的回合转换。分段一致改由 v4 verifier
  里的硬相等来强制（`RestrictedExecutionFraudVerifierContract.cs:510-516` 以
  `ReasonContextMismatch` 拒绝 `disputedTxIndex != 0 || txCount != 1 || lowerBound != 0 || upperBound != 1`），
  而那几个边界会被重新哈希进 transcript（`:699-704`）；claim id 直接绑定链、batch 与 disputed tx
  index（`:736-738`），并靠折叠那个 transcript 哈希（`:739`）传递性地把边界拉进来，
  而 `Challenge:695` 全局消费的正是这个 claim id。过期同样干净：`:704`（`now <= deadline`）与
  `:774`（`now > deadline`）互为精确补集，而 `FinalizeIfPastWindow` 在它的外部调用之前就把这两个键
  删除，所以过期之后它无法被重入。而且这个设计的标注是诚实的：`ChallengeOrchestrator.cs:23-27`
  直白地写着这里的二分是一项链下的 narrowing optimization、"there is currently no on-chain bisection
  contract"，并且 `Challenge` 是 single-shot。
- telemetry 的 name → description → documentation 三角是完备且由机器强制的：39 个常量、
  39 条目录条目、一道双向反射测试，以及零个未被记录的名字（§5 V6 说的是那唯一一处绕过；
  这一条说的是其余一切成立）。
- L2 RPC 表面恰好是 10 个处理器（`getl2batch`、`getl2batchstatus`、`getl2stateroot`、
  `getl2withdrawalproof`、`getl2messageproof`、`getl1depositstatus`、`getbridgedasset`、
  `getcanonicalasset`、`getsecuritylevel`、`getsecuritylabel` —— 每个入口点上的 `Time("…")` 包装
  让它们可被枚举），而这 10 个全部有文档记录。反方向也是干净的：唯一那几个有文档却没有 RPC
  处理器的名字（`getCanonicalStateRoot`、`getGenesisStateRoot`、`getChallengeableBatchHeader`、
  `getproof`）都是合约方法或 Neo-core 方法，而非幽灵 RPC —— `getChallengeableBatchHeader`
  确实存在于 `SettlementManagerContract.cs:739`，并在 `UT_SettlementManager_Vm.cs:610` 被测到。
  注册层同样通过了那趟发现 telemetry 唯一孔洞的扫描（§5 V6）：`L2RpcServerAdapter.cs:25-52`
  恰好带十个 `[RpcMethod]` 特性、每个处理器一个，而 `L2RpcPlugin.cs:150` / `:178-179`
  把这同一个对象交给 `RpcServerPlugin.RegisterMethods` —— 没有任何处理器能经由别的路径被触达。
- 文档指向代码的锚定异常之好：本报告中几乎每一条发现都是循着某条 XML 注释里的 `doc.md` 章节引用
  定位到正确文件的。失效之处更窄 —— 那些活得比其实现更久的声称，以及检查错了东西的门禁。

## 10. 修复顺序

按能否现在落地划分。

**可以落在当前治理分支中的（小、局部、可测）：**

1. `C4` —— **已在本分支完成**：一次挑战成功时那两个窗口键会被清掉，提交 → 挑战 → 重新提交 的 VM
   测试也与它一起落地（见 §3 的 C4 状态段）。
   这是两份报告里最便宜的一个 Critical，而且它门控着下面的 `H18` 修复：不带着这一条就去修
   模板与部署器之间的不匹配，只会把一条坏掉的 optimistic 链变成一条永久卡死的链。
2. `V4` —— **已在本分支完成**：证据文件的向上回溯被 `tests/Shared/RepoRoot.cs` 在全部 33 处替换
   （§5 V4）。重新可见的是 40 个测试、而不是 27 个 —— 27 只是其中一个项目的份额。
3. `H16` —— **已在当前分支完成**：`FinalizeBatch` 在 `SettlementManagerContract.cs:509-510`
   断言 `isActive`，而 `RevertBatch` 刻意保持无守卫，两个 VM 测试随之落地（§4 H16 状态）。
4. `H17` —— **已在本分支完成**：`LiveDeployCommand` 现在在 `SettlementManager.SetMessageRouter` 与
   `ChainRegistry.LockGovernance` 之间执行 `MessageRouter.SetGovernanceController` →
   `SetGlobalRootVerifier` → `LockGlobalRootGovernance`，每步带读回完成检查，冒烟新增六次读回，
   而不是建议里那次端到端发布（§4 H17 状态）。`deploy-testnet` 新增两个必填开关 ——
   `--gateway-program-vkey`、`--gateway-replay-domain` —— 因为 Gateway profile 这组值由运维者提供、
   且没有任何地方持久化。部署测试 115/115、`NeoHub.Contracts.VmTests` 573/573，
   整解决方案 38 个程序集 / 2,897 个测试 / 0 失败 / 5 跳过（H16 那次的 2,895 加两个解析器测试）。
5. `H19` —— **已在本分支完成**：`_deploy` 现在通过 `SetDeadlineSeconds` 所用的同一对常量强制
   `[60, 86400]` 窗口，而该发现拒绝去猜的那个 `uint` 溢出方向是量出来的、不是假设出来的 ——
   在一台会 halt 的 VM 之后是 mod 2³² 截断。`UT_ForcedInclusion_Vm` 17/17、
   `NeoHub.Contracts.VmTests` 575/575、全解决方案 38 个程序集 / 2,899 个测试 / 0 失败 /
   5 跳过（H17 那次的 2,897 加上这两个）。负向对照：只回退被跟踪的产物就能让新的部署测试点名
   截止期 `1` 而失败。见 §4 H19 的状态段。
6. `V6` —— 把 `L2BatchPlugin.cs:477` 的那个字面量提升为一个 `MetricNames` 常量 + 一条目录条目；
   可选地再加上那道本可以捕获它的发射点字面量扫描。
7. §7.1 —— 为 `SetOwner`、`RegisterPauser`/`RevokePauser`、`SetWindowSeconds`、
   `SetChallengerRewardBps` 增加锁守卫（机械性；照 `RegisterChain` 复刻）。
8. `H18` —— 把 `TemplateCatalog.cs:32` 与部署器注册的 verifier 对齐。放在最后，在 `C4` 之后。
9. `V2`（部分）—— 更正 `BatchSerializer.cs:12-14` 与 `AGENTS.md` 中关于
   `ChainMode.L2RiscV` 的陈述，或者补上那个枚举成员。

**需要先决策再写代码的：**

10. `C3` —— guest-blob 新鲜度门禁。需要一个运行 `regenerate-guest-blob.sh`（nightly cargo +
    `polkatool 0.32.0`）并比较 SHA-256 的 CI job，也就是 Rust 通道上新的 CI 容量。
11. `H14` —— 移除 `panic = "abort"` 会改变展开语义，并可能改变 guest 热路径上的吞吐；
    需要一次测量，而且它与 SP1 再执行档相互影响。
12. `V1` —— 决定谁拥有定时的 SP1 dispatch（nightly 还是 merge queue），以及它失败时凭什么阻塞发布。
13. `H15` —— 逐区块上下文的修复会触及 batcher↔executor 接缝，并且如果被持久化的头部馈入任何哈希，
    还会触及 state-root 编码。在“不要破坏字节格式”这条规则之下，它需要一个配套的规范决策。
14. `H1` —— 针对 `Committed` 采用 `StopPlugin` + 重试；需要先补上 `OnBlockCommitted`
    的测试覆盖（§6，“OnBlockCommitted 没有测试”那条）。
15. `C2` / `V5` —— 受位置绑定的验证，外加去掉 `UT_SharedBridge_Vm` 的 mock。
16. `V7` —— 重试惯例在 `AtomicFileQueueTransport.cs:108`/`:147` 已然存在；需要决定的是读路径是否
    获得同样的有界等待重试，*以及*重试耗尽后的 `IOException` 是否包装进协议的
    `InvalidDataException` 家族。一个答案，同时应用到两处读取点
    （`AtomicFileQueueTransport.cs:265`、`Sp1GatewayProofProver.cs:429`）。
17. `V8` —— SP1 的传递依赖公告。要决策的不是“修还是不修”：图内的修复并不存在。要决策的是是否现在就
    排期 SP1 6.2.1 → 6.5.0 的 bump —— 08-28 那份笔记已把它界定为一次 VK 重钉 + guest 重建 +
    `SP1_STATEFUL_NEO_VM_V1` 语义 ID 轮换，横跨两份仓内 manifest 与 `r3e-network/neo-zkvm`
    submodule —— 还是继续维持那份在运维者信任模型下书面接受的姿态。有两个不必等待该决定的小动作，
    也不该等：把 `p3-symmetric` 写成书面评估（今天没人评估过，本节是第一遍），
    以及把 `.github/dependabot.yml:26-35` 与 Security 标签页对齐 —— 那里的 `ignore` 抑制的是更新
    PR、不是告警，所以三条全都还开着，而那段注释读起来像是已解决。
18. `finalizeIfPastWindow` 在整个树内没有任何驱动 —— 既不在 off-chain 侧，也不在合约间调用侧
    （§4 H16 的 blast-radius 那段）。这使得它是 `finalizeBatch` 唯一调用方
    （`OptimisticChallengeContract.cs:791`），而它本身只能由一笔外部手工构造的 L1 交易触达；于是
    一条 Optimistic 链终局化的频率就等于“有人记得去调用它”的频率，而依赖已终局化 root 的提款
    继承这一点。要决策的是归属：由 `Neo.Plugins.L2Settlement` 挂一个截止期定时器、交给既有的
    challenge orchestrator、还是明确写成一条运维手册步骤 —— 最后这一项需要
    `docs/launching-an-l2.md` 把它说清楚。选一个；“合约暴露了这个方法”不是一个能撑住一条有着
    7 天窗口的主网的答案。

## 11. 本轮未验证

- §V2 的 .NET ↔ Rust 那一半：没有任何测试证明 guest 或 host 按 C# writer 发出的方式读取
  `StateWitnessV1` / `MerkleProofSerializer` 字节。Rust 一侧是被阅读的，并未与 .NET 编码器做交叉执行。
- 重新构建 SP1 guest：这里没有 `cargo prove` 工具链，因此 `bridge/neo-zkvm-guest` 的当前
  artifact 未被复现（此前 `A4` 一类风险，对 SP1 未定量）。
- `H10`/`H11` 的真实增长曲线：不存在基准测试脚手架，而创建一个超出范围。记录在案的 Devnet 数字
  （5 个区块 → 5,624 ms，20 → 4,803 ms，40 → 4,161 ms，且 `state entries: 1`）显示每 batch
  的常量成本占主导，且无法把 ≈5·S 的状态扫描与持久化分离开来；`BatchSealer.cs:258-261` 的秒表
  把持久化完全排除在外。
- 经由 `Committed` 钩子的端到端重组：需要一个多节点环境。
- `V4` 修好之后仍剩下五个跳过，它们全都是 env 门控，而这里一个都没有满足，所以那些通道仍未被执行：
  3 个在 `tests/Neo.L2.Sdk.UnitTests/Conformance/UT_SdkConformance_Live.cs`
  （`NEO_SDK_LIVE` / `NEO_N4_RPC_URL` / `NEO_SDK_LIVE_FIXTURE`，也就是 live-L1 路径），
  1 个在 `tests/Neo.L2.Executor.UnitTests/UT_Sp1StatefulBatchExecutor.cs:303-305`
  （`NEO_ZKVM_EXECUTOR` 必须指向一个真实的、被钉扎的执行器二进制），还有 1 个在
  `tests/Neo.Plugins.L2Metrics.UnitTests/UT_L2MetricsPlugin.cs:338-341`，当主机的解析器对
  `does-not-exist.invalid` 给出回答时它会自我跳过。特别要指出的是 `Neo.L2.IntegrationTests`
  **根本没有** env 门控 —— 对该项目 `grep -c Environment.GetEnvironmentVariable` 的结果是 0 ——
  所以它过去报告的那 9 个跳过完全属于 `V4`，而这正是 §3 C4 归错的那一类。
- `C4` 没有 VM 复现：那个死锁是四个被我从头读到尾、横跨两份合约的断言，而
  提交 → 挑战 → 重新提交 这个序列从未被执行过。它正因如此被标为 [E2]。
  **同日作废** —— 这个序列如今会在 `UT_OptimisticChallenge_Vm` 里真实运行，并且抽掉修复就会失败
  （见 §3 的 C4 状态段）。仍然成立的那一半是：没有任何测试把 `SettlementManager` 与
  `OptimisticChallenge` 作为两份真合约一起部署，所以 `SubmitBatch` 的那条重新提交分支依旧只是被读到，
  而非被跑到。
- `ForcedInclusionContract.cs:374` 处那个 `uint` 加法的 NCCS 编译语义 —— 回绕、饱和还是 FAULT ——
  未被确定，所以 `H19` 把两个分支都并列陈述、而不挑定其中之一。一道 VM 测试即可了结。
  **同日了结** —— `EnqueueDeadlineSum_TruncatesModuloTwoTo32InsteadOfFaulting` 量到的是 halt 在后面的
  mod 2³² 截断，而 §4 H19 的状态段记录了这让哪些仍然成立。
- telemetry 与 RPC 表面是通过对源码和文档做计数 grep 验证的（§5 V6、§9），而不是通过从一台运行中的
  节点抓取 live 的 `/metrics` 端点。如果该端点与目录在运行时出现分岔，本轮看不见。
- `docs/telemetry.md:214-226` 那段样例 exposition 是与导出器的渲染规则做比对的，
  比对方式是阅读 `PrometheusExporter.cs`，而不是生成真实输出。
- `V8` 有意停在两件它点得出名、却在此处结不了的事上。`slop-merkle-tree-6.2.1/src/tcs.rs:146` 被哈希的
  claimed-value 数量能否被 SP1 的对抗方操纵 —— 这是唯一能把 `p3-symmetric` 从 Low 抬到“有点意思”的
  前提条件 —— 需要对 SP1 递归的 query 形状做一次阅读，而不是对本仓库的阅读。而一次 SP1 6.5.0 的 bump
  究竟会不会真的关掉 GHSA-vj64-rjf3-w3v7，这里没有尝试：依赖图会解析到 `0.4.3-succinct`，
  它在 semver 里排在 `0.4.3` 之下，所以这条告警有可能活过自己的修复。两件事都是可以回答的，
  但都不是在这里能回答的。
