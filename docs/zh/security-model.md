# 安全模型

> 给运维者看的 Neo Elastic Network 安全保证、威胁模型和"标签"摘要 —— 这些标签
> 必须真实反映链的信任假设。完整威胁模型在 `doc.md` §17,正式论述在
> `WHITEPAPER.md` §12。

---

## L1 提供什么保证

对每一条注册到 `NeoHub.ChainRegistry` 的 L2 链:

- **资产托管完整性。** 规范资产(GAS / NEO / USDT / USDC / BTC / NEP-17)托管在
  `NeoHub.SharedBridge` 内。任何 L2 都不能铸造、销毁或挪动被托管资产,除非通过
  注册的验证器最终确认一个 `withdrawalRoot`。
- **结算确定性。** 一个被 `SettlementManager` 接受的 `L2BatchCommitment`,意味着
  它已经被 `VerifierRegistry` 中按该链 `ProofType` 注册的 `IL2ProofVerifier`
  验证过。验证器还会额外校验
  `commitment.PublicInputHash == hash(publicInputs)` —— 阻止恶意证明者用与承诺
  不一致的 inputs 签名。
- **ZK verifier 边界。** `ProofType.Zk` 通过可部署的
  `NeoHub.ContractZkVerifier` router 路由。该合约先校验 commitment/proof envelope
  和已登记 verification-key id，再调用已登记终端验证器的 `verifyZkProof(...)`。
  生产 SP1 路径把 immutable `Sp1Groth16Verifier` 作为可部署验证器合约：它接收精确的 356-byte SP1
  proof，重建 5 个 public inputs，并通过 Neo 当前 BN254 interops 执行完整的、兼容
  SP1 6.2.x 使用的 v6.1-compatible Groth16 wrapper pairing equation。生产部署在启用 `ProofType.Zk` 路由前
  永久关闭 `ProofSystem.Sp1` 的 envelope-only acceptance。
- **重放安全。** 每条跨链消息都带 `(chainId, nonce)`,在 `NeoHub.MessageRouter`
  按 (来源,目标) 配对去重。
- **提款终结性。** 资金离开 `SharedBridge` 必须基于*已最终化的* `withdrawalRoot`
  的 包含证明。最终化前的批次无法释放 L1 资产。
- **逃生通道。** `EmergencyManager.EscapeHatchExit*` 让用户在网络暂停期间凭最后一个
  最终化状态根证明某个状态树叶子,并记录一条一次性、防重放的**退出认领**——它本身
  不转移任何资金。排序器已经最终化的提款经
  `SharedBridge.EmergencyFinalizeWithdrawalWithProof`(对批次 `withdrawalRoot` 验证)
  自主支付。对于从未最终化的提款,此处记录的状态叶认领由治理 / 链下结算来释放托管;
  直接从任意状态叶自主支付属于路线图(通用叶子并不绑定桥可据以自行支付的规范
  资产/金额/收款人三元组)。

## L1 不保证什么(每条链在到达 Phase 4 前)

- **Phase 0–2 链的状态根有效性。** 在 ZK 有效性证明上线前:Phase 0(侧链)是
  信任排序器;Phase 1–2 是信任多签;Phase 3 是信任挑战窗口。
- **排序器活性。** 排序器可以让链停摆。强制纳入 + 保证金罚没让停摆代价高昂;
  逃生通道让停摆最终可解。

正确的思考方式是:**L1 验证什么,取决于注册的验证器验证什么。** 当注册 circuit、VK、
终端验证器和部署 wiring 全部正确时，Phase 4 才能提供密码学状态转换有效性。
Phase 3(乐观)让验证器信任程度等同于二分博弈的挑战窗口。
Phase 0–2 在多签之上叠加治理(Neo Council、排序器保证金)。
生产 SP1 Phase 4 路径中，`VerifierRegistry` 指向 `ContractZkVerifier`，后者固定绑定
immutable `Sp1Groth16Verifier`。两个合约、固定的 SP1 circuit/VK 和 Neo BN254 interops
共同属于 L1 trusted computing base。当前 VM suite 已接受 Rust 生成的正向 SP1 proof
通过 terminal 与 router，并覆盖 artifact integrity、常量、篡改拒绝、pairing 路径和
fee ceiling。

---

## 每条链的安全标签

<p align="center">
  <img src="../figures/trust-spectrum.svg" alt="按链的安全光谱:0 侧链(完全信任排序器),1 已结算 L2(DA + 状态根承诺),2 乐观 rollup(欺诈证明挑战窗口),3 ZK 有效性(密码学终结性)。每张卡说明用户信任什么、L1 保证什么、提款时间、故障模式、范例部署。" width="900">
</p>

`ChainRegistry` 要求每条 L2 在链上公布自身的安全画像:

| 字段             | 含义                                                                          |
| ---------------- | ----------------------------------------------------------------------------- |
| `securityLevel`   | `0` 侧链 · `1` 已结算 L2 · `2` 乐观 rollup · `3` ZK 有效性                 |
| `daMode`          | `1` NeoFS DA（默认/推荐）· `0` L1 DA · `2` 外部 DA · `3` DAC                   |
| `gatewayEnabled`  | 是否经 Neo Gateway 结算(Phase 5)                                            |
| `permissionlessExit` | 用户能否单方面调用 `EmergencyManager`                                      |

用户通过 `getsecuritylevel` RPC 读取这些字段。UI **必须**显著呈现这些标签 —— 尤其是
DAC 链(就标 DAC,不要营销话术粉饰)。

一条简单的运维守则:**钱包 UX 隐藏安全标签,该链的安全声明就降级为最差标签。**

---

## 威胁与缓解

| #  | 威胁                          | 主缓解措施                                                       | 代码引用                                |
| -- | ---------------------------- | --------------------------------------------------------------- | --------------------------------------- |
| 1  | 排序器审查                    | 强制纳入 + 保证金罚没 + 逃生通道                                | `Neo.L2.ForcedInclusion`、`Neo.L2.Censorship` |
| 2  | 非法状态根                    | ZK 有效性证明(Phase 4)或乐观挑战(Phase 3)                | `Neo.L2.Proving.RiscVZk`、`Neo.L2.Challenge` |
| 3  | 桥被攻破                      | 锁-铸 vs 销-解锁 不变量;速率限制;紧急暂停                     | `NeoHub.SharedBridge`、`EmergencyManager` |
| 4  | 重放攻击(跨链)               | `(chainId, nonce)` 信封 + 按对去重                              | `NeoHub.MessageRouter`、`Neo.L2.Messaging.L1MessageInbox` |
| 5  | DA 不可用                     | `ChainRegistry` 公布 DA 安全标签;不透明触发逃生通道             | `NeoHub.DARegistry`、`EmergencyManager` |
| 6  | 验证人委员会作恶              | 排序器保证金;经 `SequencerRegistry` 轮换出局                    | `NeoHub.SequencerBond`、`NeoHub.SequencerRegistry` |
| 7  | 证明器漏洞                    | `VerifierRegistry` 升级走治理延迟 + 安全委员会否决              | `NeoHub.VerifierRegistry`、`NeoHub.GovernanceController` |
| 8  | 验证器升级攻击                | 同 #7 的治理延迟 + 否决路径                                     | `NeoHub.GovernanceController`           |
| 9  | 消息重复                      | `MessageRouter` 按 (来源,目标) 的 `(chainId, nonce)` 去重      | `NeoHub.MessageRouter`                  |
| 10 | L2 合约漏洞                   | 本地 L2 紧急暂停 + `EmergencyManager` 逃生通道                  | `NeoHub.EmergencyManager`               |

代码库强制执行的不变量远不止威胁模型这 10 条。下面是结构性不变量的一份不完全清单
(每条都有钉死回归测试):

- **跨批次提款 nonce 去重。** 用户不能在 `WithdrawalProcessor.SealBatch` 清空
  in-flight 集合后跨批次复用 `(sender, nonce)` 提款。
- **证明者边界处的 public-input 哈希等价性。** `L2SettlementPlugin` 在提交到 L1
  之前就拒绝 `publicInputHash` 与结算者计算的哈希不一致的证明 —— 避免无谓的 L1
  往返。
- **Contract ZK verifier router。** `ContractZkVerifier` 会在委托给
  `verifyZkProof(...)` 前拒绝非 ZK commitment、畸形 `RiscVProofPayload` envelope、
  未登记 verification key、以及未设置终端验证器合约 hash 的配置。生产 SP1 deploy
  plan 会绑定 immutable `Sp1Groth16Verifier`，并在登记 ZK route 前调用不可逆的
  `DisableEnvelopeOnlyPermanently`。私有 devnet 如需 envelope-only，只能使用独立且
  明确未锁定的部署。
- **`OptimisticChallenge.Challenge` 的 fraud-verifier 白名单。** 关闭一次
  bond-drain 攻击窗口:`Challenge` 只会调用 owner 经 `RegisterFraudVerifier`
  显式登记过的 `fraudVerifier` 合约哈希。否则攻击者可以部署"yes-verifier"
  并把任何 sequencer 的保证金抽干。部署计划器会把对应的
  `RegisterFraudVerifier` 步骤作为 post-deploy 自动列出
  (`GovernanceFraudVerifier`、`RestrictedExecutionFraudVerifier`)。
- **治理 proposal payload 绑定。** 每个 `*ViaProposal` 方法
  (`SetImmutableFlagViaProposal`、`RegisterVerifierViaProposal`、
  `UpgradeVerifierViaProposal`、`RegisterCommitteeViaProposal`)都会把
  action args 做规范字节编码,并通过 `GovernanceController.MatchesProposalPayload`
  与存储的 proposal payload 做按字节相等性比较。council 成员投票的就是执行
  调用实际还原出的同一段字节 —— 已通过的 proposal 不能被改写成不同的 action
  args 重新触发。
- **法定人数仍存活时的委员会密钥丢失恢复。** 完整委员会轮换本身就是绑定 epoch、
  达到阈值并经过 timelock 的 proposal。若 2-of-3 委员会中有一个签名者不可用，
  其余两个签名者提出并批准 `BuildRotateCouncilAction`，等待配置的 timelock，随后
  使用完整旧成员快照和 proposal 中精确绑定的新成员集合调用 `RotateCouncil`。
  丢失的密钥无需参与；轮换后所有旧 epoch proposal 立即失效，被移除的密钥立即
  失去权限。该路径刻意不提供 owner 绕过。若可用签名者少于配置阈值，运维方必须
  停止治理操作，并执行另行审计的紧急治理迁移，而不能削弱链上法定人数。
- **提款 leaf 中的 chainId 域分隔。** `SharedBridge.ComputeWithdrawalLeafHash`
  和链下 `MessageHasher.HashWithdrawal` 的 preimage 都会先拼 4 字节小端 chainId,
  这样某条 L2 的 withdrawal root 上的 inclusion proof 即便其它字段恰好相同也
  绝不能在另一条 L2 上回放。
- **MPC 委员会重复签名拒绝。** `MpcCommitteeVerifier` 用 seenBitmap 追踪
  signer index,攻击者无法让同一个委员会成员被计入阈值两次(同时也是抵御 ECDSA
  签名延展性的关键防线 —— 同一签名者的 low-S/high-S 重签名会在 Neo CryptoLib
  验证之前就被 duplicate-signer 断言拒绝)。
- **证明阶段拒绝空证明。** 非 `None` 的 `ProofType` 配上空 `Proof` 字节会在证明
  边界被拒,而不是审计阶段才被发现。
- **解码器严格长度匹配。** 文档化负载之后的尾随字节会被拒;否则攻击者可以追加
  L1 哈希但 L2 剥离的 padding,制造延展性入口。
- **解码时枚举字节校验。** Proof-type / DA-mode 等判别码在 cast 前做边界检查,
  阻止未定义的枚举值悄悄向下游传播。
- **存储字节的防御式拷贝。** 保留负载的 store(`InMemoryL2RpcStore`、
  `InMemoryMessageRouter`、`KeyedStateStore`、`InMemoryDAWriter`)在插入和迭代时
  都做克隆,避免调用者复用 buffer 静默破坏记录。
- **签名验证之前的签名集合去重。** 否则恶意证明者可以提交 256 份(`MaxSigners`)
  同一份合法签名的副本,迫使在重复检测触发前完成 256 次冗余 ECDSA 验证。
- **插件事件订阅者故障隔离。** 抛异常的 `OnBatchSealed` 订阅者按订阅者隔离,不能
  把异常透出到 Neo 的 `Blockchain.Committed` 进而搞乱区块导入。
- **指标 sink 与业务状态隔离。** 抛异常的 `IL2Metrics` 实现不能让已提交状态在
  调用方看来像出错;每个业务状态调用点都用 `MetricsExtensions.SafeIncrementCounter`
  + try/catch 包裹。已发现的最坏 bug:`SubmitBatchAsync` 成功后 metric 抛错会让
  已上 L1 的承诺被重试循环重新入队 —— 已修。
- **JSON-RPC 响应 id 校验。** 按 JSON-RPC 2.0 §5,`JsonRpcClient.CallAsync` 拒绝
  响应 `id` 与请求 `id` 不匹配的回包。

完整目录(每条防御都附一段 rationale)见 `CHANGELOG.md` 第 67 次迭代起。

---

## 保留链 ID

`chainId = 0` 保留为 **L1 哨兵**。这个约定编码在 `L2Outbox.L1ChainId`,并在合约套件
的每一个外部 mutator 处强制执行 —— `RegisterChain`、`EnqueueForcedTransaction`、
`Deposit`、`FinalizeWithdrawal`、`SequencerRegistry.Register`、
`SequencerBond.Deposit`、`MessageRouter.EnqueueL1ToL2`、
`OptimisticChallenge.OpenWindow`、`EmitMessage`(L2 原生)以及 `L2SystemConfig` /
`L2MessageContract` 的 `_deploy` 路径。链下站点也通过 `ChainIdValidator.ValidateL2`
拒绝 0。

为什么要这么多强制点:L2 路由层把 `chainId == 0` 解读为"此消息发往 L1"。一条注册了
`chainId = 0` 的 L2 会让所有 L2→L2 消息被静默错路成 L2→L1、在 gateway 处丢失,把
网络上其它每一条链都搞坏。这里的纵深防御意味着:既要在 `ChainRegistry` 部署时的
准入门把 0 拒掉,也要在每一个吃 chainId 的合约入口处把 0 拒掉 —— 这样运维者错配
只会在合约入口报清晰错误,绝不会静默错路。

---

## 运维 checklist

上线一条 L2 之前:

- [ ] 老老实实设 `securityLevel`。如果你是 Phase-2 链(还没挑战窗口),不要标成
      `optimistic rollup`。
- [ ] 老老实实设 `daMode`。如果你用 DAC,就标 DAC;UI 应当警告。
- [ ] 接好指标插件。`Neo.Plugins.L2Metrics` 插件托管 `IL2Metrics` sink +
      `MetricsHttpServer`,暴露 `/metrics`、`/healthz`、`/readyz`。挂上 Prometheus,
      面板见 `docs/telemetry.md`。
- [ ] 审 deploy bundle。`Neo.Hub.Deploy plan` 输出确定性、依赖已解析的 L1 部署
      序列;按你打算注册的 `ChainRegistry` 配置逐一比对每一步。
- [ ] 生产 SP1 结算必须核对 post-deploy 顺序：登记 program VK、绑定
      `Sp1Groth16Verifier`、永久关闭 envelope-only、最后路由 `ProofType.Zk`。在精确
      本地正向 proof-vector VM gate 已通过；公共网络仍必须对精确部署的 NEF/VK pair
      记录同一组正向/负向 smoke evidence 后，才可标注 `securityLevel=3`。
- [ ] 运行进程内 devnet(`tools/Neo.L2.Devnet`)做端到端验证,再把插件指向真实
      Neo 网络。
- [ ] 配好审计框架。`Neo.L2.Audit.ChainAuditor` 接受一序列 `IAuditCheck` 实现;
      默认套件(`ContinuityCheck`、`ProofValidityCheck`、`NoZeroProofCheck`、
      `PublicInputHashConsistencyCheck`、`BatchRangeCheck`、
      `DAAvailabilityCheck`)能在坏批次产生几分钟内捕获典型的"状态漂移"故障模
      式。devnet 运行器演示了规范接线。

---

## 上报问题

安全问题请在公开披露前报到 Neo 项目安全邮箱。当前的披露政策见
`neo-project/neo` 主仓库。
