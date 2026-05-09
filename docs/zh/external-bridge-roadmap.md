# 外链桥路线图

## 目标

加一个可插拔的外链(Ethereum、Tron、Solana)桥,与 `NeoHub.SharedBridge`(为 Neo
L1 ↔ Neo L2 服务)分开。一套应用 API;底下的验证器在 MPC → Optimistic → ZK 间无缝
切换,不打破上层。和框架已经在 L2 结算用的同一种运营模式
(`NeoHub.VerifierRegistry` 让一条链可以从 Multisig → Optimistic → ZK 升级而无需
应用重建)—— 这就是把同一原语应用到跨外链消息传递上。

## 1. 架构

### 1.1 合约面

3 个新合约 + 1 个接口:

- **`NeoHub.ExternalBridgeRegistry`** —— 把 `externalChainId`(uint32,见 §1.3)
  映射到 `IExternalBridgeVerifier` 的 UInt160。形态同
  `NeoHub.VerifierRegistry`:owner-set + 带重放保护的治理提案路径。新增一字节
  `externalChainId → bridgeKind`(`1=MPC`、`2=Optimistic`、`3=ZK`),让 dApp
  能读到自己实际承担的信任模型。
- **`NeoHub.ExternalBridgeEscrow`** —— 持有锁定的、面向外链的 NEP-17 资产,在
  入站证明通过验证后付款。形态对应 `SharedBridge.Deposit` /
  `FinalizeWithdrawalWithProof`,但按 `externalChainId` 索引,且证明验证路由到
  registry 而非 `SettlementManager`。
- **`L2Native.ExternalBridgeContract`** —— L2 原生对应件,让 L2 dApp 可以直接
  `Send(externalChainId, recipient, asset, amount, calldata)`。形态对应
  `L2Native.L2BridgeContract`,但其提款被发到一个独立的"外部 withdrawal root",
  与现有批次根并列提交。

### 1.2 验证器接缝

```text
interface IExternalBridgeVerifier {
  bool VerifyInboundMessage(uint externalChainId, byte[] messageBytes, byte[] proofBytes);
  byte BridgeKind();   // 1=MPC, 2=Optimistic, 3=ZK
}
```

`ExternalBridgeRegistry.VerifyInbound(externalChainId, msg, proof)` 读出接好的验证
器哈希并 `Contract.Call` `VerifyInboundMessage`。应用代码永远看不到底下是哪一种。
Phase 2 加 Optimistic 的方式是部署一个新验证器、经治理改 registry 指针。Phase 3
加 ZK 同理。同一线协议格式、同一 registry,无应用重建 —— 完全和
`NeoHub.VerifierRegistry` 已经怎么把结算链从 Multisig → Zk 一样。

### 1.3 规范线协议格式

`ExternalCrossChainMessage`(对应 `Neo.L2.Messaging.CrossChainMessage`
+ `MessageHasher`):

```text
4B  externalChainId       (Eth 主网=0xE0_00_00_01,Sepolia=0xE0_00_00_02,
                           Tron=0xE0_00_00_10,Solana=0xE0_00_00_20 —— 高位
                           前缀 0xE0 在与 Neo L2 chainId(从 1 起)互斥的命名
                           空间下保留)
4B  neoChainId            (目标 Neo L2 chainId,L1 用 0)
8B  nonce                 (每方向、每对独立;重放 key)
1B  direction             (1 = Neo→外链, 2 = 外链→Neo)
20B sender                (Neo 侧 UInt160;外链侧取地址末 20B —— Eth/Tron 自然
                           对齐,SOL 打包)
20B recipient
8B  deadlineUnixSeconds   (0 = 无)
32B sourceTxRef           (Eth tx hash / Tron tx hash / Solana sig 截断)
1B  messageType           (0=AssetTransfer, 1=Call, 2=AssetAndCall)
4B+ payload-LE-prefixed bytes
32B messageHash           (上述字节的 Hash256)—— 由 MessageBuilder 填充
```

经新文件 `Neo.L2.Messaging/ExternalMessageBuilder.cs` +
`ExternalMessageHasher.cs` 处理。合约校验哈希一致,然后派发。

## 2. 密码学

Neo 的 `CryptoLib` 已经把 **secp256k1 + Keccak256** 的 `VerifyWithECDsa` 和
`VerifyWithEd25519` 暴露成原生 syscall。所以:

- **Eth/Tron**:链上 secp256k1+Keccak256 验证便宜且现成。
- **Solana**:链上 ed25519 验证可行。痛点不是签名验证 —— 是 *Tower BFT 轻客户端*。
  Solana 验证人集合约 1500 个,每 epoch 轮换,终结性需要推理 lockout。一个完全
  无信任的 Solana 轻客户端真的很贵(Helius、Pyth、Wormhole 都没硬啃下来)。
  因此:**Solana 在 Phase 3 之前一直只走 MPC 委员会**;ZK Solana 是 Phase 4 的
  R&D 项目。
- **Eth ZK 轻客户端**:BLS12-381 sync-committee 验证需要 pairing precompile 或
  SNARK-of-SNARK 设计 —— Phase 3 用 Succinct 的 SP1 证明(我们已经经
  `bridge/neo-zkvm-host/` 托管了 SP1),由通用的 Groth16/Plonky 风格验证器去验,
  而不是新增 pairing syscall。复用现有的 zkVM 接缝就是赢点。

## 3. 链下组件(`watchers/`)

新顶层目录 `watchers/`;每个 watcher 是一个 Rust 二进制。

- `watchers/neo-bridge-watcher-eth/` —— ethers-rs RPC 客户端。订阅 Eth 侧
  `NeoExternalBridgeRouter.sol`(经 `external/foreign-contracts/` 部署)的事件。
  每个事件,构造规范 `ExternalCrossChainMessage`,用 secp256r1 签名,发到 Neo 上
  `NeoHub.MpcCommitteeVerifier` 的 `Attest` 端点。形态对应
  `Neo.L2.Sequencer.RpcSequencerCommitteeProvider`。
- `watchers/neo-bridge-watcher-tron/` —— 用 `tron-rust` 同形态。
- `watchers/neo-bridge-watcher-sol/` —— 用 `solana-client` 同形态。

**质押/罚没。** 每个 watcher 通过新的 `NeoHub.ExternalBridgeBond`
(把 `NeoHub.SequencerBond` 1:1 克隆,以 `(externalChainId, operator)` 为 key)
进行质押。罚没条件:
1. 对同一 `(externalChainId, nonce)` 签了两条不同消息(可在链上证明的 equivocation)。
2. 外链已最终化源 tx 超过 `livenessTimeout` 之后仍未产出 quorum 签名(由另一个
   watcher 提交外链 inclusion 证明来证明)。

Phase C 的 optimistic 验证器直接把 (2) 当作其 fraud proof。

## 4. 分阶段计划

### 阶段 A —— 基础设施(4–6 周)

`Neo.L2.Messaging/ExternalMessageBuilder.cs`、`ExternalMessageHasher.cs`、3 个
合约、`IExternalBridgeVerifier` 接口、registry 接线、治理钩子。还没验证器 ——
只是接缝。

**验收:** 部署 + 注册一个永远返回 `true` 的 stub 验证器;在 devnet 上把消息
经 `Send` → registry → noop 验证器 → escrow 付款走通。

### 阶段 B —— MPC 委员会 + Eth(6–8 周)

`NeoHub.MpcCommitteeVerifier`(克隆 `AttestationVerifier` 形态)、
`NeoHub.ExternalBridgeBond`、`watchers/neo-bridge-watcher-eth`、Eth 侧
`NeoExternalBridgeRouter.sol`(经 `external/foreign-contracts/` 中的 Hardhat 脚本
部署)。Tron watcher 沿用同一形态(边际 ~2 周)。Solana watcher 同理(边际 ~3
周 —— RPC 古怪)。

**验收:** Sepolia 用户 <2 分钟内桥 1 USDC 到 chainId=1 的 L2;反向通顺;
equivocation 罚没在 devnet 上有单元测试。

### 阶段 C —— 乐观挑战(4 周)

`NeoHub.ExternalOptimisticChallenge` 克隆 `NeoHub.OptimisticChallenge` + 一个
`NeoHub.MpcCommitteeFraudVerifier` 验证"委员会签了消息 X,但外链 tx ref 与之
不符"。ETH 的 registry 指针从 MPC 翻到 Optimistic;Solana 仍是 MPC。

**验收:** 模拟 equivocation 经挑战路径被罚;窗口过期后完成最终化。

### 阶段 D —— 按链 ZK 轻客户端(每条 12+ 周)

先 Eth,经 Succinct/SP1(`watchers/neo-bridge-prover-eth/` 在
`bridge/neo-zkvm-host/` 的 SP1 容器里跑 sync-committee 证明,发给一个
`NeoHub.EthSyncCommitteeVerifier`,后者调用一个坐落在
`Neo.L2.Proving/RiscVZk/` 的 Groth16 验证器)。再到 Tron(DPOS 更简单 —— ~6 周)。
Solana 标记为只走委员会。

**验收:** Eth 桥不依赖委员会签名也能无信任运行;每个入站证明 gas 成本 <5 GAS。

## 5. 仓库布局

```text
contracts/
  NeoHub.ExternalBridgeRegistry/          # 阶段 A
  NeoHub.ExternalBridgeEscrow/            # 阶段 A
  NeoHub.ExternalBridgeBond/              # 阶段 B
  NeoHub.MpcCommitteeVerifier/            # 阶段 B
  NeoHub.MpcCommitteeFraudVerifier/       # 阶段 C
  NeoHub.ExternalOptimisticChallenge/     # 阶段 C
  NeoHub.EthSyncCommitteeVerifier/        # 阶段 D
  L2Native.ExternalBridgeContract/        # 阶段 A
src/
  Neo.L2.Messaging/ExternalMessageBuilder.cs        # 阶段 A
  Neo.L2.Messaging/ExternalMessageHasher.cs         # 阶段 A
  Neo.L2.Messaging/ExternalCrossChainMessage.cs     # 阶段 A
  Neo.L2.Bridge/ExternalDepositPayload.cs           # 阶段 A
  Neo.L2.Bridge/ExternalWithdrawalProcessor.cs      # 阶段 A
  Neo.L2.Proving/External/MpcCommitteePayload.cs    # 阶段 B
  Neo.L2.Proving/External/MpcCommitteeVerifier.cs   # 阶段 B
watchers/
  neo-bridge-watcher-eth/                 # 阶段 B(Rust)
  neo-bridge-watcher-tron/                # 阶段 B(Rust)
  neo-bridge-watcher-sol/                 # 阶段 B(Rust)
  neo-bridge-prover-eth/                  # 阶段 D(Rust,封装 bridge/neo-zkvm-host)
external/
  foreign-contracts/eth/NeoExternalBridgeRouter.sol    # 阶段 B
  foreign-contracts/tron/NeoExternalBridgeRouter.sol   # 阶段 B
  foreign-contracts/sol/                                # 阶段 B(Anchor)
```

## 6. 面向用户的 API

**L2 dApp → 外链:**

```csharp
ExternalBridge.Send(
    externalChainId: 0xE000_0001,           // Eth 主网
    recipient:       new Bytes20("0xabc..."),
    asset:           usdcL2Hash,            // 或 UInt160.Zero 表示原生
    amount:          1_000_000,             // 1 USDC
    calldata:        Array.Empty<byte>(),   // 或任意字节
    deadline:        Runtime.Time + 86400);
```

返回一个用户可追踪的 `(externalChainId, nonce)`。应用永远不指名是哪种验证器。

**外链 → Neo(以 Ethereum 为例):**

```solidity
NeoExternalBridgeRouter.lockAndSend(
    uint32 neoChainId,             // 1 = 第一条 L2
    bytes20 neoRecipient,
    address asset,                  // 0x0 = ETH
    uint256 amount,
    bytes calldata payload,
    uint64 deadline);
```

Watcher 观察 `Locked` 事件、做证明;Neo 验证器接受;`ExternalBridgeEscrow` 在目标
L2 铸出锚定资产。

接缝保证这两个面在 registry 翻 MPC → Optimistic → ZK 时字节一致;只有底下的验证
器合约换。

## 7. 可参照的实现样板

实现每一阶段时,把以下现有合约直接当形态参照 —— 同模式、同错误处理、同测试姿态:

- `contracts/NeoHub.VerifierRegistry/` —— 给 `ExternalBridgeRegistry` 用
  (治理介入的验证器派发,带重放保护)。
- `contracts/NeoHub.SharedBridge/` —— 给 `ExternalBridgeEscrow` 用
  (escrow + finalize-with-proof 流程,只是路由经 registry 而非 SettlementManager)。
- `contracts/NeoHub.OptimisticChallenge/` —— 给 `ExternalOptimisticChallenge` 用
  (挑战窗口 + 二分博弈,形态同现有欺诈证明系统)。
- `src/Neo.L2.Proving/Attestation/AttestationVerifier.cs` —— 给
  `MpcCommitteeVerifier` 用(对规范哈希做 M-of-N secp256r1)。外链 op 会经 Neo
  CryptoLib syscall 把曲线换成 secp256k1 / ed25519。
- `src/Neo.L2.State/MessageHasher.cs` —— 给 `ExternalMessageHasher.cs` 用
  (在规范线协议字节上做 Hash256)。
