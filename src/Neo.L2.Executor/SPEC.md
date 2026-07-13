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

1. **Apply L1 messages first.** For each `l1Messages[i]`, in the order given, the executor applies the pinned N4 genesis V1 transition below. Replay protection uses the native L2 bridge's exact per-`(sourceChain, nonce)` key. The executor MUST NOT skip or reorder. Inbox validation or adapter failure is fatal to the whole batch; it is not a transaction `FAULT` and cannot produce a synthetic receipt.
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
- Every transaction leaf is `Hash256(encodedTx)` over the exact sealed bytes; executors MUST NOT
  substitute a decoded or witness-stripped transaction hash.
- All Merkle trees use Neo's `Hash256` (double-SHA256) for inner-node combination, with the rightmost-leaf duplicated when the level has odd cardinality (matches `Neo.Cryptography.MerkleTree`).
- `CrossChainMessage` and `WithdrawalRequest` leaf hashes use the encodings in `Neo.L2.State.MessageHasher`.

### N4 genesis V1 receipt and effects

N4 genesis freezes `CanonicalReceiptV1` at exactly 105 bytes:

```text
txHash[32] | success[1] | gasConsumed i64 LE[8]
| storageDeltaHash[32] | eventsHash[32]
```

`StorageDeltaHashV1` is zero for an empty delta set. Otherwise it is `Hash256` over the domain `neo-n4/storage-delta/v1\0`, a `u32 LE` count, and deltas sorted by the complete raw storage key. Each delta binds the key, operation (`Add=1`, `Update=2`, `Delete=3`), and old/new presence bytes plus full length-prefixed values.

```text
storageDeltaV1 = domain | count:u32 | delta...
delta = keyLength:u32 | completeRawKey | operation:u8 | before | after
absent = 0
present = 1 | valueLength:u32 | completeValue
```

`EventsHashV1` is zero for no notifications. Otherwise it is `Hash256` over the domain `neo-n4/events/v1\0`, a `u32 LE` count, and notifications in execution order. Each notification binds the emitting script hash, strict UTF-8 event name, and the complete versioned canonical stack state (`NEO4STK1`, V1). Pointer/interop/iterator values are not serializable and fail closed.

The stack-state header is `NEO4STK1 | version:u16=1 | flags:u16=0`. Recursive tags are Null `00`, Boolean `20`, canonical signed-little-endian Integer `21`, ByteString `28`, Buffer `30`, Array `40`, Struct `41`, and insertion-ordered Map `48`; variable bytes and child counts use `u32 LE`. The format permits at most 16 nested levels, 512 nodes, and 1024 encoded bytes. Integer zero has a zero-length payload.

The post-state root is recomputed from the verified complete pre-state key/value witness plus committed HALT overlays using the keyed-state Merkle algorithm. Receipt folding is not a state-root algorithm.

### N4 genesis V1 inbox and native outbox profile

N4 genesis intentionally proves a restricted native profile, not general native-contract dispatch.
The only supported inbound message is `MessageType.Deposit` (`0`). It MUST have
`sourceChainId == 0`, `targetChainId == payload.chainId`, a non-zero sender,
`receiver == NativeContract.L2Bridge.Hash`, and the exact canonical
`DepositPayload` bytes (`l1Asset[20] | recipient[20] | amountLength i32 LE | unsigned amount`).
The complete message bytes are committed through `MessageHasher.HashMessage` and the
`l1MessageHash` Merkle root; no host-supplied per-message hash is trusted.

The transition reads and writes the actual N4 native layout:

- L2 bridge mapping: contract id `-104`, prefix `0x01`, key suffix `l1Asset`; V1 requires
  the exact 22-byte value `l2Asset[20] | l1Decimals | l2Decimals`.
- Replay marker: contract id `-104`, prefix `0x02`, suffix
  `sourceChainId u32 LE | nonce u64 LE`, value `01`.
- Bridged token authorization: contract id `-109`, either key `fe` containing the L2 bridge
  hash or prefix `0x03 | bridgeHash`.
- Token metadata: contract id `-12`, prefix `0x0a | l2Asset`; value is the core
  `BinarySerializer` encoding of `TokenState` (`Struct(7)`, `TokenType.Fungible == 1`).
- Account balance: contract id `-12`, prefix `0x0c | recipient | l2Asset`; value is the core
  `BinarySerializer` encoding of `AccountState` (`Struct(1)`).

The token owner MUST be `NativeContract.BridgedNep17.Hash`, `TokenManagement.GetAssetId(owner,
name)` MUST equal the mapped asset, token and mapping decimals MUST agree, decimal conversion
MUST be exact, and the minted amount MUST be positive and at most `2^128`. Total/max supply and
the recipient balance are updated with checked arbitrary-precision arithmetic. V1 supports only
externally owned recipients; contract recipients fail closed because the `onNEP17Payment`
callback is not yet modeled. Missing native contract state, mapping, authorization, malformed
native state, replay, or any `Call`/`Event`/`Governance` inbox message terminates the batch without
partially applying any inbox write.

For outbound effects, the guest implements only the pinned native methods
`L2Message.emitMessage(targetChainId, receiver, messageType, payload)` and
`L2Bridge.initiateWithdrawal(l2Asset, amount, l1Recipient)`. They update the native nonce,
TokenManagement supply/balance, and emit the same canonical notifications and native fees as the
N4 core profile. Every other native method fails closed.

Committed outbox roots are derived only from successful transactions' canonical `NEO4STK1`
notifications with both the exact native script hash and exact ABI:

- `L2Bridge.WithdrawalEmitted(sender, l1Recipient, l2Asset, amount, nonce)` feeds
  `MessageHasher.HashWithdrawal` and `WithdrawalTree`.
- `L2Message.MessageEmitted(sourceChainId, targetChainId, nonce, sender, receiver,
  messageType, payload)` feeds `MessageHasher.HashMessage` and the appropriate `MessageTree`.

Events are consumed in execution order. Duplicate sender/nonces or malformed reserved native
events terminate the batch. An ordinary user contract may emit the same event name, but its
different script hash cannot create a system withdrawal or message. Empty collections remain
`UInt256.Zero`.

`ApplicationEngineTransactionExecutor` and `RiscVTransactionExecutor` declare the
`CanonicalNativeV1` effects profile. `ReferenceBatchExecutor` therefore re-hashes their canonical
storage/events, requires the hashes to equal the receipt, and ignores any separately projected
withdrawal/message lists. A custom `ITransactionExecutor` uses `ExecutorDeclared` effects by
default so domain-specific chains can commit their own deterministic withdrawal/message model;
such a chain MUST use a matching executor/prover semantic identifier and is not accepted by the
N4 genesis V1 ZK profile.

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
faults. Deployed-contract calls use witnessed code/manifests; native dispatch is limited to the
two methods in the N4 genesis profile above. Other native methods, cryptographic syscalls,
randomness, dynamic script loading, runtime logging, and ledger/blockchain query families remain
fail-closed until implemented with ApplicationEngine parity.

## Error handling

A canonical transaction decode failure, malformed witness scope/rule, invalid contract adapter, invalid pre-state witness, or root mismatch is a **fatal protocol error**. The batch MUST NOT be sealed.

After successful decode, an unknown contract, unsupported consensus syscall, insufficient gas, or any other VM `FAULT` is a **valid result with a failure receipt**. Its storage overlay and notifications are discarded; its actual consumed gas remains in the frozen receipt.

A malformed, replayed, unsupported, or incompletely witnessed L1 message is a **fatal batch error**. Inbox messages execute before transactions and have no transaction receipt in which to encode a recoverable `FAULT`. The inbox transition is atomic, so no replay marker, supply, or balance write survives rejection.

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

## Canonical proof-witness pipeline

The prover input is the existing `ProofWitnessArtifactV1` (`NEO4PWIT`); no parallel outer witness envelope is permitted. Its `ExecutionPayloadV1` carries ordered full transaction bytes, block/L1 context, and DA bytes. Its non-empty `StateWitness` section uses `NEO4STW1` V1 and carries the complete sorted pre-state key/value set, frozen protocol settings, and deployed contract IDs, hashes, scripts, and Neo manifests. Every contract descriptor is bound into the pre-state root through `0xff || "neo-n4/contract-binding/v1/" || scriptHash` and the domain `neo-n4/contract-binding/v1\0`.

The artifact's execution result, `NEO4EFX1` effects, and 332-byte public inputs are claims, not trusted inputs. The guest recomputes all of them and requires exact equality. N4 genesis V1 accepts only the restricted deposit and native outbox profile above; all other inbox types, native methods, and unavailable consensus syscalls fail closed rather than returning fabricated state or zero roots.

The executor-specific state and effects bytes are canonical outputs supplied through
`IProofWitnessBatchExecutor`; the settlement pipeline treats them as opaque and MUST NOT create a
second effects encoding or wrap the artifact in a competing outer witness format.

The production order is:

1. `BatchSealer` creates an immutable `SealedBatch` containing exact transaction bytes, canonical
   L1 messages, `BatchBlockContext`, `preStateRoot`, and any forced-inclusion nonce plus transaction
   position and Merkle siblings. Entering an open builder never consumes a forced nonce. After
   sealing, the immutable batch remains pending and no later block is accepted until
   `ISealedBatchSink` durably persists it and execution is acknowledged. On restart, the sink derives
   `(batchNumber,lastBlock,postStateRoot)` from the continuous committed artifact chain before the
   first block is processed. An empty store starts at the first non-genesis block (index 1); missing
   blocks are replayed from the local ledger, and an unavailable block, duplicate, block gap,
   batch-number gap, or state-root gap fails closed.
2. `IProofWitnessBatchExecutor` executes the sealed inputs and returns roots plus the canonical
   authenticated state/effects bytes produced by the matching execution profile.
3. `IDAWriter` publishes the exact versioned `ExecutionPayloadV1`. Its receipt commitment, layer,
   receipt kind, pointer, evidence, and availability are checked against the payload bytes.
4. The coordinator computes `L1MessageHash` and `BlockContextHash` from the real sealed input,
   constructs exactly one `ProofWitnessArtifactV1`, validates every payload/result/public-input/DA
   cross-binding, and atomically commits it through `IProofWitnessStore`.
5. `IL2Prover` receives the complete, non-empty serialized artifact bytes. The proof manifest is
   durably committed before settlement submission. The store, not an in-memory queue, is the source
   of truth for proving, submission, reconciliation, and restart recovery.
   The manifest state machine is `ProofReady -> Submitted -> SettlementObserved`: `Submitted`
   requires a persisted non-zero L1 transaction hash, while broadcast success alone never implies
   observation. Recovery always queries the batch first. A persisted pending transaction is not
   duplicated; dropped/reverted transactions may be replaced only after explicit transaction-status
   evidence. Unknown or inconsistent transaction state fails closed. If a process stops before the
   transaction hash is persisted, retry is allowed only through the settlement client's required
   `(chainId,batchNumber)` idempotency after another batch-status query.
6. After settlement is observed, a forced nonce remains tracked until the batch is finalized and an
   `IForcedInclusionFinalizationClient` verifies `SettlementManager.getFinalizedTxRoot`, submits
   permissionless `ForcedInclusion.consume(chainId,batchNumber,nonce,siblings,leafIndex)`, and confirms
   consumption on L1. Failures remain retryable from the persisted artifact and proof manifest.

For a ZK profile, executor and prover execution semantic identifiers MUST match exactly. The
executor witness MUST be authenticated and non-empty, and the prover MUST declare a cryptographic
backend. Mock, preview, legacy, unauthenticated, empty, or semantic-mismatched combinations fail
closed. Multisig and optimistic compatibility paths are permitted only through an explicit non-ZK
profile.
