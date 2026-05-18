# Deterministic Batch Executor Spec

> The function the L2 chain runs to seal a batch and the function the prover proves correct.

This spec defines the `ApplyBatch` contract that doc.md §8.1–§8.2 mandates. Anything that is **not** covered by this spec is outside the proving boundary and **must not** influence the executor's outputs — that includes P2P, RPC, mempool, plugins, logging, wallet, and on-disk DB layout.

## Function signature

```
ApplyBatch(
    preStateRoot:    UInt256,
    orderedTxs:      bytes[],
    l1Messages:      CrossChainMessage[],
    blockContext:    BatchBlockContext
) → (
    postStateRoot:    UInt256,
    txRoot:           UInt256,
    receiptRoot:      UInt256,
    withdrawalRoot:   UInt256,
    l2ToL1MessageRoot:UInt256,
    l2ToL2MessageRoot:UInt256,
    gasConsumed:      i64
)
```

## Determinism contract

The output is a pure function of the inputs. The executor MUST NOT read:

- the wall clock (use `blockContext.FirstBlockTimestamp` / `LastBlockTimestamp`);
- any randomness source (RNG must be derived from `blockContext` + the storage state);
- any environment variable, config file, or CLI argument that is not part of the L2's `L2ChainConfig` or `ProtocolSettings`;
- the file system, network sockets, or any IPC channel;
- mutable state outside `preStateRoot` and the input arrays.

The executor MUST NOT write:

- log files, traces, or telemetry;
- mempool entries;
- DB rows beyond the snapshot it received as `preStateRoot`.

## Execution order

1. **Apply L1 messages first.** For each `l1Messages[i]`, in the order given, the executor calls into the on-L2 bridge / message contracts as if the message came from a trusted source. Replay protection is enforced by the contracts via per-(sourceChain, nonce) bitmaps. The executor MUST NOT skip or reorder.
2. **Apply transactions next.** For each `orderedTxs[i]`, in the order given, the executor decodes the transaction, validates it, and runs it through the configured deterministic VM. Neo N4 L2 production uses NeoVM2/RISC-V; the legacy NeoVM compatibility executor uses Neo's standard `Transaction.DeserializeFrom` + `ApplicationEngine` path. Failed transactions still update fees (matches Neo L1).
3. **Seal outboxes.** After all transactions complete, the executor computes:
   - `txRoot` = MerkleTree(txHash[0], …, txHash[N-1])
   - `receiptRoot` = MerkleTree(receiptHash[0], …, receiptHash[N-1])
   - `withdrawalRoot` = batch-level `WithdrawalTree.Root`
   - `l2ToL1MessageRoot` = batch-level `MessageTree.Root` for messages with `targetChainId == 0`
   - `l2ToL2MessageRoot` = batch-level `MessageTree.Root` for messages with `targetChainId != 0`
4. **Compute postStateRoot.** From the deterministic keyed-state Merkle tree after applying all storage writes.

## Hashing rules

- All multi-byte integers in canonical encodings: little-endian.
- All Merkle trees use Neo's `Hash256` (double-SHA256) for inner-node combination, with the rightmost-leaf duplicated when the level has odd cardinality (matches `Neo.Cryptography.MerkleTree`).
- `CrossChainMessage` and `WithdrawalRequest` leaf hashes use the encodings in `Neo.L2.State.MessageHasher`.

## Error handling

A malformed transaction (bad signature, unknown contract, insufficient gas) is a **valid result with a failure receipt** — it must NOT abort the batch. The receipt's failure flag goes into `receiptRoot`. This matches Ethereum-style and Neo L1 semantics.

A malformed L1 message (corrupted payload, unknown asset) is **also a valid result with a failure receipt**. The deposit is consumed (the `(sourceChain, nonce)` is marked) and the funds remain in the L1 escrow until governance refunds them — the L2 cannot mint to a bogus recipient.

A protocol-level error (invalid `preStateRoot`, witness inconsistency) is **fatal**. The executor returns a hard failure and the calling batcher MUST NOT seal a batch.

## Excluded surfaces

These are explicitly OUTSIDE the proven function and must not be observable in any output:

- Akka actors and any async messaging;
- the RPC server and any of its plugins (`RpcServer`, `RestServer`);
- the `MemoryPool` admit / evict / sort logic;
- the `DBFTPlugin` consensus state machine (the batch only sees its result: an ordered tx list);
- on-disk persistence layer (`LevelDBStore`, `RocksDBStore`) — the prover sees only the MPT root, not the storage engine;
- the `Wallets` namespace and any signing service;
- logging, metrics, telemetry plugins (`OTelPlugin`, `ApplicationLogs`).

## Reference implementation

`Neo.L2.Executor.ReferenceBatchExecutor` is the in-process reference implementation used by tests, the devnet boot path, and the witness-collection code that feeds the RISC-V prover.

A separate `Sp1BatchExecutor` (in a future RISC-V ELF binary, not C#) executes the same function inside the SP1 zkVM. The two implementations MUST produce byte-identical outputs for any given input. Cross-implementation conformance tests live in `tests/Neo.L2.Executor.UnitTests/Conformance/`.

## Witness format

The witness handed to the prover is a serialized record of:

1. The ordered transactions and their decoded form.
2. Storage read set: `(contract, key, value, mptProof)` for every read.
3. Storage write set: `(contract, key, oldValue, newValue, mptProofPre)` for every write.
4. Native contract state delta: per-contract state slot reads and writes.
5. The L1 messages consumed (already in inputs).
6. The ordered list of receipts produced.
7. `BlockContext`.

Witness format is defined in `Neo.L2.Executor.Witness.WitnessRecord`.
