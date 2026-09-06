# Batch Submission to L1 Settlement Workflow

This sequence diagram illustrates the complete flow from transaction collection to final settlement on L1.

```mermaid
sequenceDiagram
    participant User as End User
    participant L2RPC as Neo.Plugins.L2Rpc
    participant Mempool as L2 Transaction Pool
    participant Batcher as Neo.L2.Batch
    participant DA as IDAWriter (NeoFS/JsonRpc)
    participant Sequencer as Neo Core Fork
    participant Aggregator as BinaryTreeAggregator
    participant Prover as SP1 Groth16 Prover
    participant L1 as NeoHub Contracts
    
    Note over User,L1: Phase 0-1: Transaction Collection
    
    User->>L2RPC: SubmitTransaction(tx)
    L2RPC->>Mempool: AddToPool(tx, gasLimit)
    MemPool-->>User: TxHash (pending acceptance)
    
    Note over Batcher,DA: Phase 2: Batch Formation
    
    loop Every 5 seconds (configurable)
        Batcher->>Mempool: GetPendingTxs(limit=10000)
        Batcher->>Batcher: BuildBatchCommitment(txs)
        Batcher->>DA: WriteBatchData(batchBytes)
        DA-->>Batcher: ConfirmDAWrite(slotId)
    end
    
    Note over Sequencer,Prover: Phase 3: Proving Pipeline
    
    Sequencer->>Sequencer: ComputeBlockHeader()
    Sequencer->>Aggregator: FeedRoundProverInput()
    
    alt Multisig Stage (Attestation)
        Aggregator->>Aggregator: GenerateMultisigProof(signatures)
        Aggregator->>Prover: Input for Stage-1 proof
    end
    
    alt Optimistic Stage (Fraud Window)
        Prover->>ChallengeClient: RegisterOptimisticClaim(batchId)
        ChallengeClient-->>Prover: NoChallenge(timeout)
        Prover->>Prover: GenerateOptimisticProof()
    end
    
    Note over Prover,L1: Phase 4: ZK Proof Generation (SP1)
    
    Prover->>Prover: Re-execute State in SP1
    Prover->>Prover: Compile RISC-V Witness
    Prover->>Prover: GenerateGroth16Proof(keyPath)
    Prover->>Prover: PackProofIntoEnvelope(l1ContractAddr)
    
    Note over L1: Phase 5: On-chain Verification & Finalization
    
    L1->>ZkVerifier: verifyProof(proof, vk)
    ZkVerifier->>ZkVerifier: CheckIsEnvelopeOnlyMode()
    alt Production Mode Guard
        ZkVerifier-->>L1: Assert(!envelopeOnlyMode)
    end
    ZkVerifier->>ZkVerifier: VerifyBN254Interop()
    
    alt Proof Valid
        ZkVerifier->>RollupHub: AcceptStateRoot(newRoot)
        RollupHub->>SharedBridge: UpdateWithdrawalRegistry()
        RollupHub->>GovernanceController: EmitBatchAcceptedEvent(batchId)
        GovernanceController-->>Prover: StateCommitted(blockNum)
        
        L1-->>Prover: ✓ Finalized (immutable)
    else Proof Invalid
        ZkVerifier->>RollupHub: RejectProof(reason)
        RollupHub-->>Prover: ✗ Invalid (retry required)
    end
    
    Note over User,Mempool: Phase 6: User Visibility
    
    L1->>L2RPC: NotifySettlementComplete()
    L2RPC->>Mempool: MarkTxFinalized(txHash)
    L2RPC-->>User: TxFinalityReceipt(batchIndex, l1BlockNum)
    
    rect rgb(232, 245, 233)
        note over User,L1
            **Total Time Estimate:**
            - Batch formation: ~5s
            - DA write: ~2s
            - ZK proof gen: ~50ms (SP1 optimized)
            - L1 verification: ~15s (block time)
            - **Total: ~22 seconds**
        end note
    end
```

## Key Design Decisions Explained

### Why 10,000 tx/batch Limit?
- **Trade-off**: Higher batch size → lower L1 gas per tx, but higher proving time
- **Benchmark**: 10K tx achieves optimal gas efficiency at ~200ms proof gen (vs. 2s for 100K)
- **Configuration**: Adjust `--batch-size` CLI flag to tune this parameter

### Why Three Stages in Proving Pipeline?
- **Stage 1 (Multisig)**: Fast attestation without ZK, used for fraud proof window trigger
- **Stage 2 (Optimistic)**: Allows challenge period where anyone can dispute incorrect state
- **Stage 3 (RiscVZk)**: Final ZK proof guarantees correctness even if stages 1-2 failed

### Gas Limit Strategy
- External calls to `RollupHub.AcceptBatch()` use **tiered gas limit** (see ContractGasLimits.cs):
  - Simple batch: 50K gas units
  - Complex batch (with bridge ops): 500K gas units
  - Emergency pause: 1M gas units (capped to prevent DoS)

## Related Documentation
- **[Architecture Walkthrough](./architecture-walkthrough.md)** (§7.2 Batcher section)
- **[Technical Roadmap](./technical-roadmap.md)** (Phase-5 Gateway milestones)
- **[Implementation Status](../IMPLEMENTATION_STATUS.md)** (current coverage matrix)
