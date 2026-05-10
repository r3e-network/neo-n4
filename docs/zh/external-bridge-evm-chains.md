# 外链桥接入新 EVM 链

Neo Elastic Network 的外链桥框架把 Ethereum、BSC、Polygon、Arbitrum、Optimism、
Base、Avalanche、Linea、zkSync、Scroll、Mantle、Fantom、Celo 等都视为同一个底层 EVM
链模板的变体。接入一条新 EVM 链只需 5 步,**零行新代码**:仅配置 + 合约部署 +
链上注册。

让这件事可行的两个架构选择:

1. **以太侧 router 合约
   (`external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`)
   通过构造函数参数化 `externalChainId`。** 同一份 Solidity 字节码原样部署到任何
   EVM 链上 —— 以太坊主网、BSC、Polygon、Avalanche、L2,甚至 Tron(TVM 对这些原语
   而言 EVM 风味足够)。构造时传入的 `externalChainId` 进入每个 `Locked` 事件,
   并把 router 绑到 Neo 侧命名空间的对应槽位。

2. **Watcher 守护进程(`neo-bridge-watcher-eth`)完全由 chain id 驱动。**
   `EthRpcEventSource` 在任何说标准 EVM API 的 JSON-RPC 端点上 poll
   `eth_getLogs`;secp256k1 `FileSigner` 不论事件来自哪条 EVM 链都产出相同形态的
   签名;规范 `ExternalCrossChainMessage` encoder 把 chain id 写进固定位置字段。
   **运维者跑同一份守护进程二进制,指向另一个 RPC + 另一份 config 的
   `external_chain_id` 即可。**

## 5 步 runbook

整个例子用 **BNB Smart Chain 主网**(外链 id `0xE000_0030`);把常量替换成你的目
标链即可。

### 第 1 步 —— 选定外链 id

在
[`watchers/neo-bridge-watcher-eth/src/chains.rs`](../../watchers/neo-bridge-watcher-eth/src/chains.rs)
里查你的链。完整精选表见下方 `槽位分配`。如果你的链还没列上,在对应的 16 槽位
家族 bank 加常量并提 PR —— 测试 `family_banks_align_to_16_slots` 会钉死它的位置。

BSC 主网:

```rust
use neo_bridge_watcher_eth::chains::BSC_MAINNET;
// = 0xE000_0030
```

### 第 2 步 —— 在 EVM 链上部署 `NeoExternalBridgeRouter.sol`

```bash
cd external/foreign-contracts/eth
forge build

# 用第 1 步的外链 id + owner 地址部署
# (通常是运维者的 deployer key;后续 transferOwnership 给多签)。
# 委员会成员经后续调用配置(见下)。
forge create src/NeoExternalBridgeRouter.sol:NeoExternalBridgeRouter \
    --rpc-url $BSC_RPC_URL \
    --private-key $DEPLOYER_KEY \
    --constructor-args 0xE0000030 $OWNER_ADDRESS
```

构造参数(定义在
[`NeoExternalBridgeRouter.sol`](../../external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol)):

| 参数               | 类型    | 含义                                                                                       |
|--------------------|---------|--------------------------------------------------------------------------------------------|
| `_externalChainId` | uint32  | 第 1 步的外链 id(BSC 是 `0xE0000030`)。必须带 `0xE0_xx_xx_xx` 命名空间前缀,否则构造时 revert。|
| `_owner`           | address | 初始合约 owner。授权调用 `setCommittee` + `transferOwnership`。必须非零。                    |

记下部署后的 router 地址 —— 第 3 步、第 4 步要用。

部署后,通过 `setCommittee`(owner-only)注册委员会:

```bash
# committee = watcher secp256k1 pubkey 经 keccak256(pubkey)[12:] 派生的
# Eth 地址列表。Neo 侧为同一组 signer 存放 33 字节压缩 pubkey —— 双方
# 引用同一组签名者,只是编码不同。
cast send $ROUTER_ADDRESS \
    "setCommittee(address[],uint8)" \
    "[$ADDR_0,$ADDR_1,$ADDR_2,$ADDR_3,$ADDR_4,$ADDR_5,$ADDR_6]" \
    4 \
    --rpc-url $BSC_RPC_URL \
    --private-key $DEPLOYER_KEY
```

### 第 3 步 —— 跑 watcher 守护进程

```bash
# 生成 watcher 私钥(一次性,按链或共享):
dotnet run --project tools/Neo.External.Bridge.Cli -- \
    genkey --out bsc-watcher.priv

cat > bsc-watcher.toml <<TOML
external_chain_id   = 0xE0000030                              # BSC 主网
eth_rpc_url         = "https://bsc-dataseed.binance.org"      # 任何 BSC RPC
eth_router_address  = "0xDEPLOYED_ROUTER_FROM_STEP_2"
neo_rpc_url         = "https://rpc.testnet.neo.org"
neo_escrow_address  = "0xNEO_ESCROW_DEPLOYED_BY_NEO_HUB_DEPLOY"
neo_signer_address  = "0xWATCHER_NEO_ACCOUNT"
signer_key_path     = "bsc-watcher.priv"
journal_dir         = "./journal-bsc"

[poll]
poll_interval_secs    = 6              # BSC ~3s 出块;2 块节奏
backoff_initial_secs  = 5
backoff_max_secs      = 300
eth_chunk_size        = 5000
request_timeout_secs  = 30
min_confirmations     = 15             # BSC reorg 缓冲(见 chains.rs)
start_block           = 38_400_000     # 可选 —— 中途 bootstrap
                                       # (省略/设 0 则从创世扫起)

[health]                               # 可选 —— 给 k8s probe / Prometheus
bind                  = "0.0.0.0:9090"
threshold_secs        = 120
TOML

# 带 live-rpc 编译:
CPATH=~/.local/include cargo build --release \
    -p neo-bridge-watcher-eth --features live-rpc

# 启动前先 preflight,验证配置 + signer + journal + RPC 可达性:
./target/release/neo-bridge-watcher-eth --config bsc-watcher.toml --preflight
# 退出 0 = 可安全启动。会跑 6 项检查,失败时定位到具体组件
# (例如 eth_blockNumber on http://...: connection refused)。

# 跑:
./target/release/neo-bridge-watcher-eth --config bsc-watcher.toml
```

守护进程启动 log 会回显由 `name_for_chain_id(...)` 给出的人类可读链名 —— 运维者一
眼即可确认链是对的,再让它继续跑。如果 `min_confirmations = 0` 但
`chains::recommended_confirmations` 给出非零推荐值,守护进程会在启动时发出
`WARNING` 指向推荐值。

**`start_block` 用于中途 bootstrap**(上面高亮字段):当守护进程的 journal 游标
低于 `start_block` 时,启动时会把游标推进。把一个已经跑了几个月的链对上 watcher
时很有用 —— 不带 `start_block` 时,守护进程会从 0 块扫起,反复砸 RPC 提供方
扫一年的空块。把 `start_block` 设为该链当前头减几千块(给任何在途的入站事件
留余量)。后续重启正常从 journal 读;`start_block` 单调 —— 只有第一次发现
journal 游标 < start_block 的运行才会推进。

### 第 4 步 —— 在 Neo 侧注册委员会 + 验证器

运维 CLI 在 `tools/Neo.External.Bridge.Cli/`(简称构建为 `neo-external-bridge`,
经 `dotnet run --project tools/Neo.External.Bridge.Cli` 调用,或者用已发布二进制)。

```bash
# 4a —— 把 watcher 的 33B 压缩 pubkey 转成两种编码:
#       Neo 侧 `committeeBlob`(hex)+ 第 2 步 setCommittee 已接受的对应 Eth 侧
#       address[]。CLI 从 pubkey 交叉派生地址,二者不会跑偏。
dotnet run --project tools/Neo.External.Bridge.Cli -- committee-blob \
    --pubs-file watchers.pubs   # 一行一个 pub33 hex

# stdout:委员会规模、Neo blob(0x...)、按 index 的 Eth 地址列表。

# 4b —— 产生链上部署 bundle。这会输出一份 step-by-step 的 runbook(打印到
#       stdout),由运维者钱包执行;它本身不直接调用合约。
dotnet run --project tools/Neo.External.Bridge.Cli -- deploy-bundle \
    --external-chain-id 0xE0000030 \
    --verifier 0xMPC_VERIFIER_FROM_NEO_HUB_DEPLOY \
    --registry 0xREGISTRY_FROM_NEO_HUB_DEPLOY \
    --escrow   0xESCROW_FROM_NEO_HUB_DEPLOY \
    --eth-router 0xROUTER_FROM_STEP_2 \
    --threshold 4 \
    --committee-blob 0xBLOB_HEX_FROM_4A \
    --eth-addresses 0xADDR0,0xADDR1,0xADDR2,0xADDR3,0xADDR4,0xADDR5,0xADDR6
```

`deploy-bundle` 输出按序列出每个合约方法 + 参数。用你的 Neo 钱包执行 ——
比如 `neo-cli` 调用:

```bash
# bundle 里的第 1 步:
neo-cli invoke <verifier> RegisterCommittee \
    0xE0000030 4 1 0xBLOB_HEX
# bundle 里的第 2 步:
neo-cli invoke <registry> RegisterVerifier \
    0xE0000030 <verifier> 1
# ...(deploy-bundle 输出的更多步骤)
```

### 第 5 步 —— 烟雾测试

终端用户在已部署的 BSC router 上调 `lockETHAndSend`(或 `lockERC20AndSend`);
watcher 把事件中继给 Neo 的 `ExternalBridgeEscrow.Receive`;接收方在 Neo 上的
包装余额到账。反向:Neo `ExternalBridgeEscrow` 发出 `WithdrawalReady` 事件 →
委员会联签 → 用户带证明字节调 BSC router 的 `finalizeWithdrawal`。

## 槽位分配

`0xE0_00_FF_FF` 以下的 24 位外链 id 空间按 16 槽位家族 bank 组织。每个 bank 给该
家族的主网 + 1–3 个测试网 + 未来变体留余量:

- **`0xE000_0001..000F`** —— Ethereum:`ETH_MAINNET`、`ETH_SEPOLIA`、
  `ETH_HOLESKY`。
- **`0xE000_0010..001F`** —— Tron:`TRON_MAINNET`、
  `TRON_NILE_TESTNET`、`TRON_SHASTA_TESTNET`。
- **`0xE000_0020..002F`** —— Solana:`SOLANA_MAINNET`、`SOLANA_DEVNET`、
  `SOLANA_TESTNET`。
- **`0xE000_0030..003F`** —— BSC:`BSC_MAINNET`、`BSC_TESTNET`。
- **`0xE000_0040..004F`** —— Polygon:`POLYGON_MAINNET`、
  `POLYGON_AMOY_TESTNET`、`POLYGON_ZKEVM`、`POLYGON_ZKEVM_CARDONA`。
- **`0xE000_0050..005F`** —— Arbitrum:`ARBITRUM_ONE`、
  `ARBITRUM_SEPOLIA`、`ARBITRUM_NOVA`。
- **`0xE000_0060..006F`** —— Optimism:`OPTIMISM_MAINNET`、
  `OPTIMISM_SEPOLIA`。
- **`0xE000_0070..007F`** —— Base:`BASE_MAINNET`、`BASE_SEPOLIA`。
- **`0xE000_0080..008F`** —— Avalanche:`AVALANCHE_C_MAINNET`、
  `AVALANCHE_FUJI`。
- **`0xE000_0090..009F`** —— Linea:`LINEA_MAINNET`、`LINEA_SEPOLIA`。
- **`0xE000_00A0..00AF`** —— zkSync Era:`ZKSYNC_ERA_MAINNET`、
  `ZKSYNC_SEPOLIA`。
- **`0xE000_00B0..00BF`** —— Scroll:`SCROLL_MAINNET`、
  `SCROLL_SEPOLIA`。
- **`0xE000_00C0..00CF`** —— Mantle:`MANTLE_MAINNET`、
  `MANTLE_SEPOLIA`。
- **`0xE000_00D0..00DF`** —— Fantom / Sonic:`FANTOM_OPERA`、
  `SONIC_MAINNET`。
- **`0xE000_00E0..00EF`** —— Celo:`CELO_MAINNET`、`CELO_ALFAJORES`。
- **`0xE000_00F0..00FF`** —— 保留:未使用(未来分配)。

超出此精选集合的链,在 `..F0..FF` 槽里取下一个空闲槽,或在 `0xE000_00FF` 之上的下
一个空闲 16 槽位 bank 中安排。提 PR 加常量 + 一个 `name_for_chain_id` 分支。

## 按链确认 buffer(`min_confirmations`)

每条 EVM 链 reorg 特征不同。watcher 的 `[poll]` config 暴露 `min_confirmations`
—— source 不会从距链头 `min_confirmations` 之内的浅块发出事件。设对它是运维
者抵御短 reorg 引发幻象铸造的防线。

| 链                    | `min_confirmations` | 理由                                                                |
|----------------------|---------------------|---------------------------------------------------------------------|
| Ethereum 主网          | **12**(或 32)      | 12 ≈ 99.9% 终结性;32 ≈ Casper 终结(治理推荐)                     |
| Ethereum 测试网        | 5                   | 加快开发反馈;testnet reorg 常见但代价小                            |
| BSC 主网              | 15                  | Parlia 共识;~15 块跨验证人确认                                    |
| Polygon PoS          | 256                 | 启发式终结性;CheckpointManager ~30 分钟终结一次                  |
| Polygon zkEVM        | 0                   | ZK 有效性证明在 L1 batch 提交时把 L2 终结性把住                 |
| Arbitrum One/Nova    | 0                   | 运维者另行等待 L1 batch 终结信号                                   |
| Optimism / Base      | 0                   | 同上 —— 在 L1 上结算                                              |
| Avalanche C-Chain    | 1                   | Snowman++ 接近瞬时终结;1 个确认就够                              |
| Linea / Scroll       | 0                   | ZK rollup;终结性跟随 L1 batch 提交                                |
| zkSync Era           | 0                   | 同                                                                 |
| Mantle / Mode        | 0                   | OP Stack 衍生                                                     |
| Fantom / Sonic       | 5                   | Lachesis aBFT;~5 块安全                                           |
| Celo 主网            | 1                   | IBFT;接近瞬时终结                                                 |
| Tron 主网            | 19                  | DPoS Super-Representative 已确认(KSR/SR2 round)                  |
| Tron Nile/Shasta     | 1                   | 测试网 —— 加快反馈                                                |

`min_confirmations` 为 0 的 L2,运维者必须叠加自己的信号 —— 通常是在处理 L2 事件
之前 poll L1 结算合约的 batch 终结性。

## 框架在各 EVM 链上的保证

同一组 trait 抽象(`Signer`、`EventSource`、`NeoSubmitter`、`Journal`)原样适用。
EVM 家族分类辅助:

```rust
use neo_bridge_watcher_eth::chains::is_evm_family;

assert!(is_evm_family(BSC_MAINNET));
assert!(is_evm_family(POLYGON_MAINNET));
assert!(is_evm_family(TRON_MAINNET));        // EVM 风味
assert!(!is_evm_family(SOLANA_MAINNET));     // ed25519 —— 另一个栈
```

…告诉运维工具 Eth watcher 二进制是否适用。Solana 切到 `neo-bridge-watcher-sol`
(ed25519 signer,同一编排器,同一 `WatcherCore::tick` 循环)。

## 框架**不**保证什么

- **块终结性语义。** 每条 EVM 链 reorg 特征不同 —— Ethereum 等 ~12 个确认到
  ~99.9% 终结性,BSC ~15,L2 在其 rollup batch 提交时结算。watcher 的 `[poll]`
  config 暴露 `poll_interval_ms`,但**不**实现按链的确认策略。运维者依赖快速
  结算前必须了解目标链的 reorg 风险。
- **MEV / 抢先交易保护。** router 的 `lockETHAndSend(...)` 是 public 函数;
  想要保护免受 MEV 机器人冲击的用户,在其上叠加自己的方案(Flashbots / 私
  内存池 / commit-reveal)。
- **按链 gas / 费用逻辑。** 部分 EVM 链有自己的 gas 模型(Arbitrum 的
  L1+L2 费用拆分,Optimism 的 data fee)。router 的 gas 计费走标准 EVM 模型 ——
  在实际成本不同的链上,运维者要预留更多预算。

## 另请参阅

- [`watchers/neo-bridge-watcher-eth/src/chains.rs`](../../watchers/neo-bridge-watcher-eth/src/chains.rs) —— 规范 chain-id 表。
- [`external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol`](../../external/foreign-contracts/eth/src/NeoExternalBridgeRouter.sol) —— EVM router 合约(原样部署到任何 EVM 链)。
- [`watchers/neo-bridge-watcher-eth/README.md`](../../watchers/neo-bridge-watcher-eth/README.md) —— 守护进程完整 config schema + 启动说明。
- [`docs/external-bridge-roadmap.md`](external-bridge-roadmap.md) —— Phase A → B → C 交付计划 + 未来 zk 轻客户端 R&D。
