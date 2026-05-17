# 快速上手

> 从 clone 到本地 devnet 跑通的 5 分钟教程。

## 准备

- .NET 10 SDK（`dotnet --version` ≥ `10.0.0`）。
- [`neo-project/neo`](https://github.com/neo-project/neo) Neo 4 core 已作为 git submodule
  引入到 `external/neo`（`Directory.Build.props` 中的 `NeoCorePath` 默认指向该 submodule
  路径）。请使用 `git clone --recurse-submodules`,或在普通 clone 之后运行
  `git submodule update --init --recursive`。

## 第 1 步 —— 拉取代码并校验工具链

```bash
git clone --recurse-submodules https://github.com/r3e-network/neo-n4
cd neo-n4
dotnet --version            # 期望 10.0.x
ls external/neo/src/Neo     # 确认 neo-project/neo submodule 已就位
```

如果 clone 时忘了带 `--recurse-submodules`：

```bash
git submodule update --init --recursive
```

## 第 2 步 —— 跑测试

```bash
dotnet test Neo.L2.sln /p:NuGetAudit=false
```

预期:**1423 个测试通过、覆盖 34 个工程**,端到端约 10 秒。

如果机器没有外网,`/p:NuGetAudit=false` 会跳过对 nuget.org 的安全审计跳转。

## 第 3 步 —— 跑 devnet 演示

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5
```

应当看到：

```
┌─────────────────────────────────────────────┐
│  Neo Elastic Network — devnet runner v0.2    │
│  chainId = 1001, batches =  5                      │
└─────────────────────────────────────────────┘

[persist] in-memory stores (devnet default — data lost on restart)

[wire] asset registry: 1 mapping (GAS L1=0x11111111…1111 → L2=0x22222222…2222)
[wire] 4 validators, attestation threshold = 3
[wire] sequencer committee: 3 active members
[wire] keyed state store + oracle (0 initial entries)
[wire] DA writer = InMemoryDAWriter (mode=External)

────── batch #1 ──────
  [deposit] minted 1000000 → Alice (nonce=1)
  [withdraw] staged 10000 from Alice → Bob (nonce=1)
  [DA]   layer=External commitment=0xc7a1cb54…7819b6
  [seal] preRoot=0x00000000…000000 postRoot=0xe863d100…d70776 verify=True
[…]
✅ devnet run complete.
```

刚才发生了什么：

- **3 个排序器**注册进委员会（`NeoHub.SequencerRegistry` 的内存后备）。
- **5 个批次**依次跑过 `ReferenceBatchExecutor`,每个批次包含一笔充值 + 一笔提款。
- **`KeyedStateStore`** 存放真实的 (asset, holder) → 余额映射；每个批次的
  `preStateRoot` 等于上一个批次的 `postStateRoot`(状态根连续性得到保证)。
- 每个批次将其负载发布给 **DA writer**,产生的承诺被绑入证明的 public inputs。
- **Stage-0 多签证明器**签名规范 public-input 字节；**`AttestationVerifier`**
  校验 3-of-4 签名。
- **Alice 的净余额**在结尾与预期总和进行对账。

### 持久化 devnet（重启后状态保留）

加上 `--data-dir <path>` 把所有 store 切到 RocksDB：

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/neo-l2-devnet1
# [persist] RocksDB-backed stores at /tmp/neo-l2-devnet1 (data survives restart)

# 用 0 批次再跑 —— 委员会 + 状态会从磁盘上恢复
dotnet run --project tools/Neo.L2.Devnet -- 0 --data-dir /tmp/neo-l2-devnet1
# [wire] sequencer committee: 3 active members  (no re-registration)
# [wire] keyed state store + oracle (5 initial entries)
```

`<path>/` 下的目录结构：`state/`、`rpc-proofs/`、`sequencer/`、`da/`。生产部署的接线请见
[`docs/persistence.md`](./persistence.md)。

## 第 4 步 —— 生成 NeoHub 部署 bundle

```bash
dotnet run --project tools/Neo.Hub.Deploy -- scaffold \
    --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan \
    --plan deploy-plan.json --output bundle.json
```

`bundle.json` 是一份拓扑排序、依赖已解析的合约部署调用序列(共 20 步)——每个
`$step:<name>` 占位符都替换成了确定性的 stub 哈希。生产部署会把 bundle 喂给一个带钱包
的执行器,让它对每一步签名并广播。

`plan` 命令还会打印 bundle 自身无法完成的接线动作,例如:

```
Required post-deploy actions:
  - SequencerBond.RegisterSlasher(OptimisticChallenge)
      # enable Phase-3 challenge slashing
  - ChainRegistry.SetGovernanceController(GovernanceController)
      # enable §16.1 admission policy
  - VerifierRegistry.SetGovernanceController(GovernanceController)
      # enable §16 council-veto path
```

## 第 5 步 —— 构建一个智能合约

```bash
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true
```

`DisableNccs=true` 会跳过基于 `nccs` 的 `.nef`/`.manifest.json` 输出阶段,只保留 C#
类型检查。把
[`nccs`](https://github.com/neo-project/neo-devpack-dotnet) 安装到 `PATH` 上即可关闭该
开关、产出可部署字节码。

## 接下来去哪儿

- **`ARCHITECTURE.md`** —— 主规格(`doc.md`)的英文精炼版。
- **`docs/architecture-walkthrough.md`** —— 把 `doc.md` 各节映射到代码的叙事导览。
- **`IMPLEMENTATION_STATUS.md`** —— 按阶段的覆盖矩阵 + 范围外清单。
- **`AGENTS.md`** —— 给 AI 协作贡献者的指南。
- **`CONTRIBUTING.md`** —— 代码风格、命名约定、PR checklist。
- **源码 XML 文档** —— 每个公开类型都指向 `doc.md` 的某个小节；IDE 提示框就是导航。

## 排错

**构建失败,出现 `NU1900` / nuget audit 报错。**
在构建命令上加 `/p:NuGetAudit=false`。仓库的 `Directory.Build.props` 已经设置了
`NuGetAudit=false`,但少数 restore 路径会重新求值该属性。

**`dotnet test` 报 `Could not find external/neo/src/Neo/Neo.csproj`。**
neo-project/neo submodule 没初始化。在仓库根目录跑
`git submodule update --init --recursive`,或者重新带
`git clone --recurse-submodules` clone。如要指向另一处 checkout,可在命令行覆盖:
`dotnet build /p:NeoCorePath=/path/to/neo/src`。

**合约不输出 `.nef` 文件。**
没有 `nccs` 时这是正常现象。从 `neo-project/neo-devpack-dotnet` 安装 nccs,然后去掉
`DisableNccs=true` 重新 `dotnet build`。

**想要真正的 ZK 证明(Stage-2 有效性)?**
构建 Rust 证明守护进程:`CPATH=~/.local/include cargo build --release -p neo-zkvm-host`
(需要 SP1 工具链 —— 通过 `sp1up` 安装)。然后以
`target/release/prove-batch daemon --watch <queue-dir>` 启动；.NET 排序器把封好的批次扔
进队列目录,守护进程吐出对应的 `*.proof.bin` + `*.proof.vk` 用于 L1 提交。详见
`docs/launching-an-l2.md` 的 "Prover deployment" 一节。
