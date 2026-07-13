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
2. **Apply transactions next.** Before execution starts, every `orderedTxs[i]` is decoded as a complete canonical Neo `Transaction` (unsigned fields, signers/scopes/rules, attributes, script, and witnesses). A decode or adapter error is fatal to the batch. Decoded transactions then execute `tx.Script` in order. Neo N4 L2 production uses NeoVM2/RISC-V; the NeoVM compatibility path uses the same opcode prices, bounded gas, block context, signer scopes, deployed contract code/manifests, and stateful syscalls as `ApplicationEngine`. A VM `FAULT` produces a failure receipt and rolls back that transaction's storage and notifications.
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

### N4 genesis V1 receipt and effects

N4 genesis freezes `CanonicalReceiptV1` at exactly 105 bytes:

```text
txHash[32] | success[1] | gasConsumed i64 LE[8]
| storageDeltaHash[32] | eventsHash[32]
```

`StorageDeltaHashV1` is zero for an empty delta set. Otherwise it is `Hash256` over the domain `neo-n4/storage-delta/v1\0`, a `u32 LE` count, and deltas sorted by the complete raw storage key. Each delta binds the key, operation (`Add=1`, `Update=2`, `Delete=3`), and old/new presence bytes plus full length-prefixed values.

`EventsHashV1` is zero for no notifications. Otherwise it is `Hash256` over the domain `neo-n4/events/v1\0`, a `u32 LE` count, and notifications in execution order. Each notification binds the emitting script hash, strict UTF-8 event name, and the complete versioned canonical stack state (`NEO4STK1`, V1). Pointer/interop/iterator values are not serializable and fail closed.

The post-state root is recomputed from the verified complete pre-state key/value witness plus committed HALT overlays using the keyed-state Merkle algorithm. Receipt folding is not a state-root algorithm.

### Stateful RISC-V host boundary

The PolkaVM path calls `neo_riscv_execute_script_with_host` with a transaction-scoped
managed host context. Native opcode fees are returned in pico-datoshi and converted by
ceiling division (`ceil(pico / 10,000)`); safely implemented host syscall fees are added
in datoshi using Neo's active execution-fee factor. Fixed-zero fee substitution is invalid.

Storage reads use a read-through overlay. A Put or Delete changes only the transaction
overlay until the VM returns `HALT`, canonical effects are built, and downstream effect
collection succeeds. Only then may the overlay commit. `FAULT`, `BREAK`, out-of-gas,
callback/marshalling error, collector failure, or commit conflict MUST leave storage and
effects unchanged.

The host implements only syscalls whose Neo consensus behavior can be reproduced safely.
An unknown or unsupported descriptor MUST make the callback fail so native execution
faults. In particular, cross-contract/native-contract invocation, cryptographic syscalls,
randomness, dynamic script loading, runtime logging, and ledger/blockchain query families
remain fail-closed until implemented with ApplicationEngine parity.

## Error handling

A canonical transaction decode failure, malformed witness scope/rule, invalid contract adapter, invalid pre-state witness, or root mismatch is a **fatal protocol error**. The batch MUST NOT be sealed.

After successful decode, an unknown contract, unsupported consensus syscall, insufficient gas, or any other VM `FAULT` is a **valid result with a failure receipt**. Its storage overlay and notifications are discarded; its actual consumed gas remains in the frozen receipt.

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

`bridge/neo-zkvm-guest` executes this function inside the SP1 zkVM. It uses the vendored `neo-vm-rs` interpreter with the stateful N4 V1 syscall provider and rejects every host-supplied result/effect/root that it cannot recompute. The native and proven implementations MUST produce byte-identical outputs for any supported input.

## Witness format

The prover input is the existing `ProofWitnessArtifactV1` (`NEO4PWIT`); no parallel outer witness envelope is permitted. Its `ExecutionPayloadV1` carries ordered full transaction bytes, block/L1 context, and DA bytes. Its non-empty `StateWitness` section uses `NEO4STW1` V1 and carries the complete sorted pre-state key/value set, frozen protocol settings, and deployed contract IDs, hashes, scripts, and Neo manifests. Every contract descriptor is bound into the pre-state root through `0xff || "neo-n4/contract-binding/v1/" || scriptHash` and the domain `neo-n4/contract-binding/v1\0`.

The artifact's execution result, `NEO4EFX1` effects, and 332-byte public inputs are claims, not trusted inputs. The guest recomputes all of them and requires exact equality. N4 genesis V1 currently rejects non-empty L1 inbox batches and consensus syscalls without a state adapter; unsupported behavior fails closed rather than returning fabricated values.
