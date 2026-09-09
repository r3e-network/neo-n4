# 批次提交至 L1 结算工作流

本时序图展示从交易收集到 L1 最终结算的完整流程。

```mermaid
sequenceDiagram
    participant User as 终端用户
    participant L2RPC as Neo.Plugins.L2Rpc
    participant Mempool as L2 交易池
    participant Batcher as Neo.L2.Batch
    participant DA as IDAWriter (NeoFS/JsonRpc)
    participant Sequencer as Neo 核心分叉
    participant Aggregator as BinaryTreeAggregator
    participant Prover as SP1 Groth16 证明器
    participant L1 as NeoHub 合约

    Note over User,L1: 阶段 0-1：交易收集

    User->>L2RPC: SubmitTransaction(tx)
    L2RPC->>Mempool: AddToPool(tx, gasLimit)
    MemPool-->>User: TxHash（待接受）

    Note over Batcher,DA: 阶段 2：批次形成

    loop 每 5 秒（可配置）
        Batcher->>Mempool: GetPendingTxs(limit=10000)
        Batcher->>Batcher: BuildBatchCommitment(txs)
        Batcher->>DA: WriteBatchData(batchBytes)
        DA-->>Batcher: ConfirmDAWrite(slotId)
    end

    Note over Sequencer,Prover: 阶段 3：证明流水线

    Sequencer->>Sequencer: ComputeBlockHeader()
    Sequencer->>Aggregator: FeedRoundProverInput()

    alt 多签阶段（Attestation）
        Aggregator->>Aggregator: GenerateMultisigProof(signatures)
        Aggregator->>Prover: 阶段-1 证明输入
    end

    alt 乐观阶段（欺诈窗口）
        Prover->>ChallengeClient: RegisterOptimisticClaim(batchId)
        ChallengeClient-->>Prover: NoChallenge(timeout)
        Prover->>Prover: GenerateOptimisticProof()
    end

    Note over Prover,L1: 阶段 4：ZK 证明生成（SP1）

    Prover->>Prover: 在 SP1 中重放状态
    Prover->>Prover: 编译 RISC-V 见证
    Prover->>Prover: GenerateGroth16Proof(keyPath)
    Prover->>Prover: PackProofIntoEnvelope(l1ContractAddr)

    Note over L1: 阶段 5：链上验证与最终化

    L1->>ZkVerifier: verifyProof(proof, vk)
    ZkVerifier->>ZkVerifier: CheckIsEnvelopeOnlyMode()
    alt 生产模式守卫
        ZkVerifier-->>L1: Assert(!envelopeOnlyMode)
    end
    ZkVerifier->>ZkVerifier: VerifyBN254Interop()

    alt 证明有效
        ZkVerifier->>RollupHub: AcceptStateRoot(newRoot)
        RollupHub->>SharedBridge: UpdateWithdrawalRegistry()
        RollupHub->>GovernanceController: EmitBatchAcceptedEvent(batchId)
        GovernanceController-->>Prover: StateCommitted(blockNum)

        L1-->>Prover: ✓ 已最终化（不可变）
    else 证明无效
        ZkVerifier->>RollupHub: RejectProof(reason)
        RollupHub-->>Prover: ✗ 无效（需重试）
    end

    Note over User,Mempool: 阶段 6：用户可见性

    L1->>L2RPC: NotifySettlementComplete()
    L2RPC->>Mempool: MarkTxFinalized(txHash)
    L2RPC-->>User: TxFinalityReceipt(batchIndex, l1BlockNum)

    rect rgb(232, 245, 233)
        note over User,L1
            **总耗时估计：**
            - 批次形成：约 5 秒
            - DA 写入：约 2 秒
            - ZK 证明生成：约 50 毫秒（SP1 优化）
            - L1 验证：约 15 秒（出块时间）
            - **合计：约 22 秒**
        end note
    end
```

## 关键设计决策说明

### 为何批次上限为 10,000 笔交易？
- **权衡**：更大的批次 → 每笔交易的 L1 Gas 更低，但证明时间更长
- **基准**：10K 笔交易在约 200ms 证明生成下达到最优 Gas 效率（对比 100K 需 2 秒）
- **配置**：通过 `--batch-size` CLI 参数调节该值

### 为何证明流水线分三阶段？
- **阶段 1（多签）**：无需 ZK 的快速证明（attestation），用于触发欺诈证明窗口
- **阶段 2（乐观）**：允许挑战期，任何人可对错误状态提出异议
- **阶段 3（RiscVZk）**：最终 ZK 证明，即使阶段 1-2 失败也能保证正确性

### Gas 限制策略
- 对 `RollupHub.AcceptBatch()` 的外部调用使用**分层 Gas 限制**（参见 ContractGasLimits.cs）：
  - 简单批次：5 万 Gas 单位
  - 复杂批次（含跨链桥操作）：50 万 Gas 单位
  - 紧急暂停：100 万 Gas 单位（上限保护以防 DoS）

## 相关文档
- **[架构导览](../../architecture-walkthrough.md)**（§7.2 Batcher 章节）
- **[技术路线图](../technical-roadmap.md)**（Phase-5 Gateway 里程碑）
- **[实现状态](../../IMPLEMENTATION_STATUS.md)**（当前覆盖矩阵）
