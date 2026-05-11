# 钱包接入模式

本页讲清楚运维者如何把 Neo 钱包(NEP-6 keystore、Ledger 设备、NeoLine 浏览器扩展、自研
HSM 工具链)接到 Neo Elastic Network 的 L1 交易接口上。

## 为什么这块属于运维侧

钱包接入横跨数百种产品,各自的 UX、安全模型、密钥保管模型都互相矛盾。把框架钉死在某
一款产品(NeoLine、Neon Wallet、NEP-6 纯 JSON、Ledger 硬件、AWS KMS 等)等于强迫所有
下游运维者跟着选。所有 L2 框架 —— ZKsync、Polygon zkEVM、Optimism —— 都因为同样的原因
把这一层留给上层。

`neo4` 给出的方案是:**为每一笔运维者会用到的 L1 调用都输出可粘贴进钱包的规范十六
进制**,以及给生产环境热钱包流程的**委托签名模式**。

## 模式 1 —— 粘贴进钱包(手动签名)

仓库里所有 CLI 都会输出规范的 NeoVM 调用脚本(十六进制)。运维者把十六进制粘到自己挑
的钱包里,钱包请求确认、签名、广播。

```bash
# Bridge: 把 GAS 从 L1 充值到 L2。
neo-bridge deposit \
  --bridge   0xaaaa...aaaa \
  --asset    0xbbbb...bbbb \
  --target-chain 1099 \
  --recipient 0xcccc...cccc \
  --amount   100000000

# 输出:116 字节的 hex blob。
# 粘进 NeoLine / Neon / 你的钱包的 "send invocation" 表单。

# Bridge: 用 Merkle 证明完成提款。
neo-bridge withdraw \
  --bridge ... --chain-id 1099 --batch 7 \
  --leaf 0x...... --leaf-index 3 \
  --asset 0x.... --recipient 0x.... --amount 50000000 \
  --proof-endpoint http://l2-rpc:30332

# Faucet: 限速龙头,沿用同一套粘贴流程。
neo-l2-faucet drip --bridge ... --asset ... --target-chain ... \
                   --recipient ... --amount 100000000

# Hub deploy: 完整的 L1 合约套件。
neo-hub-deploy plan --plan ./deploy-plan.json --output ./bundle.json
# 然后运维者在自己挑的钱包里逐步签名。
```

这一模式对**冷密钥部署**最稳妥 —— L1 私钥永远不接触运维者的 CI、Web 应用、共享主机。
签名由钱包的硬件或 KMS 完成,框架本身只看到规范的未签名调用十六进制。

## 模式 2 —— 委托签名(热钱包自动化)

对于自动化流程(devnet 跑批器、批次结算服务、faucet 服务器)这类需要不经手动就能签名
+ 广播的场景,框架的生产代码接受一个**签名委托**,由运维者来接线:

```csharp
using Neo.L2.Settlement.Rpc;

// RpcSettlementClient 接受一个 SignAndSendAsync 委托。运维者怎么实现都行
// (NEP-6 文件加载、KMS 签名器、Ledger HID 等)。框架本身永远看不到私钥。
var settlement = new RpcSettlementClient(
    rpc: new JsonRpcClient("http://l1-rpc:20332"),
    settlementManagerHash: UInt160.Parse("0x..."),
    signAndSend: async (contractHash, callData, ct) =>
    {
        // 用运维者的签名者 + witness 构造 Neo Transaction。
        var tx = BuildAndSignTransaction(contractHash, callData);
        // 经 Neo RPC 广播,返回 tx hash。
        return await BroadcastViaJsonRpc(tx, ct);
    });
```

其它需要签名的 CLI 用的是同一套模式 —— 都暴露一个委托钩子,让运维者把自己偏好的签名
路径(`AWS-KMS` / `Azure Key Vault` / `Ledger` / NEP-6 等)接进来,框架不需要知道细节。

## 具体钱包产品

### NeoLine(浏览器扩展)

把规范十六进制粘到 NeoLine 的 "Send" → "Smart Contract" 表单。NeoLine 负责构造
witness 脚本并广播。

### Neon Wallet(桌面)

`File → Open invocation script → paste hex`。流程和 NeoLine 一样,签名 UX 由桌面客户端
处理。

### NEP-6(程序化)

用 NEP-6 keystore 做自动签名:

```csharp
using Neo.Wallets.NEP6;

var wallet = new NEP6Wallet("operator-keystore.json");
wallet.Unlock(operatorPassword);
var account = wallet.GetAccounts().First(a => a.IsDefault);

// 通过上文的委托接进 RpcSettlementClient。
async ValueTask<UInt256> SignAndSend(UInt160 hash, byte[] script, CancellationToken ct) {
    var tx = BuildTransaction(account, hash, script);
    var ctx = new ContractParametersContext(snapshot, tx, network);
    if (account.Sign(ctx)) tx.Witnesses = ctx.GetWitnesses();
    var rawTx = tx.ToArray();
    return await rpc.CallAsync(
        "sendrawtransaction",
        new JArray { Convert.ToBase64String(rawTx) },
        ct);
}
```

### Ledger(硬件)

Ledger 的 Neo 应用支持同样的 `signTransaction` 流程。运维者把自己的 HID / WebHID
transport 接好,套进同一种委托形态,框架其它地方原封不动。

### 自研 HSM / KMS

KMS 后备的密钥(AWS-KMS、GCP-KMS、Azure Key Vault、HashiCorp Vault)生产部署的接入完全
一致:委托去调 KMS 的签名 API,返回广播后的 tx hash。其它地方都不用改。

## 为什么不内置签名器

内置签名器会强迫所有运维者在以下互斥选项里选一个:

- 某个**具体的 keystore 格式**(NEP-6vs PKCS#11 vs 纯 JSON vs 自研)
- 某个**具体的签名机制**(冷密钥粘贴 vs 硬件 vs KMS)
- 某个**具体的钱包 UX**(CLI prompt vs 浏览器弹窗 vs 自动签名)

框架刻意不内置任何一种,运维者随合规模型自由接线。每个 CLI 都输出可粘贴进钱包的十六
进制;每条生产热路径都接受签名委托。

## 参考

每个 CLI 的可粘贴输出:

- **`neo-bridge deposit`** —— `SharedBridge.Deposit` 调用 hex。
  粘进钱包 → 签名。
- **`neo-bridge withdraw`** —— `SharedBridge.FinalizeWithdrawalWithProof`
  调用 hex。粘 → 签名。
- **`neo-l2-faucet drip`** —— `SharedBridge.Deposit`(限速版)。
  粘 → 签名。
- **`neo-hub-deploy plan`** —— 13 步顺序合约部署调用。按序逐一签名。
- **`neo-stack register-chain`** —— `ChainRegistry.RegisterChain` 调用 + 91
  字节 configBytes hex。粘 → 签名。
