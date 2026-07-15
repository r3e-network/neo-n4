# Persistence

Neo Elastic Network L2 components persist their durability-critical state through
the `Neo.L2.Persistence` abstraction. **Production deployments use RocksDB**;
the in-memory backing is for tests and devnets only.

## Why this matters

L2 state isn't all the same kind. Some of it can be re-derived from L1 on startup
(asset registries, batch metadata) — losing it is a slow restart, not a bug. But
some state, if lost, breaks security or correctness invariants:

| State                                  | What breaks if lost                                                  |
| -------------------------------------- | -------------------------------------------------------------------- |
| Finalized message-inclusion proofs     | RPC clients can't query proofs after node bounce; bridge stalls.     |
| Withdrawal-inclusion proofs            | Users can't claim finalized withdrawals on L1.                       |
| Forced-inclusion consumed-nonce set    | L2 may re-execute or refuse a forced tx — breaks at-most-once.       |
| Deterministic state-store contents     | Post-state root after restart != pre-state root; commitments diverge.|
| DA blob bytes (when L2 owns them)      | Verifiers can't reconstruct what was claimed.                        |
| Sequencer committee + exit windows     | Mid-exit deadlines lost; bad-actor sequencer re-admitted or stuck.   |

Every store backed by RocksDB is one less source of restart-induced incidents.

## The abstraction

`IL2KeyValueStore` (in `Neo.L2.Persistence`) is the minimal byte-keyed,
byte-valued store interface every persistable component delegates to:

```csharp
public interface IL2KeyValueStore : IDisposable
{
    void Put(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
    bool TryPut(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
    bool CompareExchange(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> expectedValue,
        ReadOnlySpan<byte> newValue);
    byte[]? Get(ReadOnlySpan<byte> key);
    bool Delete(ReadOnlySpan<byte> key);
    bool Contains(ReadOnlySpan<byte> key);
    IEnumerable<(byte[] Key, byte[] Value)> EnumeratePrefix(ReadOnlySpan<byte> prefix);
    long Count { get; }
}

public interface IAtomicL2KeyValueStore : IL2KeyValueStore
{
    bool CompareExchangeBatch(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? ExpectedValue)> conditions,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte>? Value)> mutations);
    void ReplaceAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> entries);
    bool CompareExchangeAll(
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> expectedEntries,
        IEnumerable<(ReadOnlyMemory<byte> Key, ReadOnlyMemory<byte> Value)> replacementEntries);
}
```

Two implementations ship in the box:

| Class                       | Use for           | Backing                     | Survives restart |
| --------------------------- | ----------------- | --------------------------- | ---------------- |
| `InMemoryKeyValueStore`     | Tests, devnets    | `SortedDictionary<byte[], byte[]>` | No        |
| `RocksDbKeyValueStore`      | Production        | RocksDB 10.10 (Snappy compression) | Yes       |

Restart durability is an explicit capability, not a class-name convention:
`RocksDbKeyValueStore` implements `IDurableL2KeyValueStore`, while the in-memory backend does
not. `KeyValueProofWitnessStore.IsDurable` forwards this capability. Production settlement
wiring rejects a volatile proof-witness store or forced-inclusion event store before constructing
any RPC/process-owned resources; tests and custom devnets continue to use the generic `Wire` path.

Both built-in backends implement `IAtomicL2KeyValueStore`. `CompareExchangeBatch`
first verifies that every condition is absent or byte-equal as requested, then applies
all puts and deletes as one linearizable commit. RocksDB uses one synchronous WAL-backed
`WriteBatch`, so a reported success is the durable recovery boundary rather than an
in-memory observation.

A shared `KeyValueStoreContractTests` suite runs against both backends — same
behavior either way; future LevelDB / SQLite / cloud backends bolt on by adding
a TestClass with a `Create()` factory.

## Per-component wiring

Seven components currently delegate their durability-critical state to
`IL2KeyValueStore`. Each has a default ctor that uses an in-memory backing
(suitable for tests) and an alternate ctor that takes a caller-supplied store:

### 1. DA writer (`Neo.Plugins.L2DA.PersistentDAWriter`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/da");
plugin.WithWriter(new PersistentDAWriter(rocks, DAMode.NeoFS, ownsStore: true));
```

Or, simpler — set `DataDirectory` in the plugin's config section and the
`L2DAPlugin` opens a RocksDB at that path automatically:

```json
{
  "PluginConfiguration": {
    "DAMode": 1,
    "DataDirectory": "/var/lib/neo-l2/da"
  }
}
```

### 2. State oracle (`Neo.L2.Executor.State.KeyedStateStore`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/state");
var store = new KeyedStateStore(rocks);
var oracle = new KeyedStateRootOracle(store);
```

The `ComputeRoot()` Merkle root is identical across InMemory and RocksDB
backends for the same content — drop-in replacement.

### 3. Message router finalized proofs (`Neo.L2.Messaging.InMemoryMessageRouter`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/messages");
var router = new InMemoryMessageRouter(
    inbox: null, outbox: null,
    finalized: rocks, ownsFinalized: true);
```

Inbox + outbox stay transient — they're re-drained from L1 on startup. Only
the finalized-proof map needs durability.

### 4. RPC store proofs (`Neo.Plugins.L2Rpc.InMemoryL2RpcStore`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/rpc-proofs");
var store = new InMemoryL2RpcStore(
    chainId: 1001,
    level: SecurityLevel.Optimistic,
    proofs: rocks,
    ownsProofs: true);
```

Withdrawal + message proofs share a single RocksDB (1-byte key prefixes
disambiguate). Other RPC state (batches, asset mappings, deposits) is still
in-memory — it's rebuildable from L1.

### 5. Forced-inclusion consumed-nonce set (`Neo.L2.ForcedInclusion.InMemoryForcedInclusionSource`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/forced-inclusion");
var src = new InMemoryForcedInclusionSource(
    chainId: 1001,
    consumed: rocks,
    ownsConsumed: true);
```

The simple in-memory source keeps its pending queue transient and persists the
consumed-nonce set. Production additionally wires `RpcForcedInclusionEventScanner`:
it durably records every finalized L1 enqueue nonce before advancing its block cursor,
and `RpcForcedInclusionSource` rebuilds the hot known-nonce set from those records on
restart. A failed block is replayed because its cursor is not committed; a confirmed
consumption removes the tracked nonce only after L1 reports it consumed.

### 6. Sequencer committee membership (`Neo.L2.Sequencer.InMemorySequencerCommitteeProvider`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/sequencer");
using var p = new InMemorySequencerCommitteeProvider(
    chainId: 1001,
    store: rocks,
    ownsStore: true);
```

In-memory dictionary stays as the hot path; all writes (Register / BeginExit /
Finalize) shadow-write to the KV store. On construction, members are hydrated
from the store back into the dict so a restart picks up where the previous
process left off — including mid-exit windows. Without persistence, a node
bounce mid-exit could lose the `ExitsAtUnixSeconds` deadline and either
re-admit a sequencer that was supposed to be in cooldown or refuse to finalize
an exit whose window already passed.

### 7. Proof witness and settlement recovery (`Neo.L2.Persistence.KeyValueProofWitnessStore`)

```csharp
using var rocks = new RocksDbKeyValueStore("/var/lib/neo-l2/proof-witness");
using var witnesses = new KeyValueProofWitnessStore(rocks, ownsStore: true);
```

This store is the production commit log for canonical proof-witness artifacts,
proofs, result manifests, retry checkpoints, and rollback checkpoints. It provides:

- guarded artifact publication that cannot race a rollback-absent marker;
- quarantine of the exact reverted tail, including its proof bytes;
- authenticated state-snapshot restoration before rollback completion;
- crash-idempotent checkpoint completion and same-number resubmission;
- separate observed and finalized settlement states: `Pending` and `Challengeable`
  are observed but not final, so proof-queue acknowledgement and pruning happen only
  after L1 reports `Finalized`;
- startup reconciliation that queries L1 for every local artifact, requires the local
  proof manifest, validates contiguous finality and the canonical state root, and only
  then permits recovery side effects.

## Operator config recipe

A production L2 node typically carves out a single base directory and gives
each store its own subdirectory:

```
/var/lib/neo-l2/
├── da/                  # PersistentDAWriter
├── state/               # KeyedStateStore
├── messages/            # InMemoryMessageRouter (finalized proofs)
├── rpc-proofs/          # InMemoryL2RpcStore (withdrawal + message)
├── forced-inclusion/    # InMemoryForcedInclusionSource (consumed)
├── sequencer/           # InMemorySequencerCommitteeProvider (membership + exit windows)
└── proof-witness/       # KeyValueProofWitnessStore (artifact, proof, finality + rollback)
```

Each RocksDB instance is independent — they're not column families of one
database. That's intentional: one corrupt database doesn't take down the
others, and operators can back up + restore them individually.

## Devnet runner

The in-process devnet runner (`tools/Neo.L2.Devnet`) supports a `--data-dir`
flag that wires four of these stores under one root automatically — the
quickest way to see the persistence story end-to-end:

```bash
# First run — writes to RocksDB
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/devnet1

# Second run — same dir, state survives
dotnet run --project tools/Neo.L2.Devnet -- 0 --data-dir /tmp/devnet1
# → "[wire] sequencer committee: 3 active members"  (rehydrated, not re-registered)
# → "[wire] keyed state store + oracle (5 initial entries)"  (Alice's balance restored)
```

When `--data-dir <path>` is passed, the devnet creates these subdirectories:

```
<path>/
├── state/        # KeyedStateStore
├── rpc-proofs/   # InMemoryL2RpcStore (withdrawal + message proofs)
├── sequencer/    # InMemorySequencerCommitteeProvider
└── da/           # PersistentDAWriter (DAMode.NeoFS + dataDir)
```

Without `--data-dir`, every store is in-memory — fine for tests, but everything
is lost on restart. Production deployments should always set `--data-dir` (or
the equivalent config keys for plugin-based deployments).

## Operator checklist

- [ ] Every L2 component that needs durability uses the `(IL2KeyValueStore)` ctor
      overload. The bare default ctor is for tests only — do not ship it.
- [ ] The directory passed to `RocksDbKeyValueStore` is on durable storage
      (not `tmpfs` or an ephemeral container volume).
- [ ] Backups capture all seven subdirectories above. A point-in-time backup of
      one without the others can leave the L2 in an inconsistent state on
      restore (e.g., consumed nonces but no corresponding finalized proofs).
- [ ] The process running the L2 node has write access to each directory.
- [ ] On planned shutdown, dispose stores explicitly (`using` blocks) so
      RocksDB flushes its memtable.

## Adding a new persistence backend

To add (e.g.) LevelDB or a cloud KV like DynamoDB:

1. Implement `IL2KeyValueStore`.
2. Add a TestClass to `tests/Neo.L2.Persistence.UnitTests/UT_KeyValueStore.cs`
   that inherits from `KeyValueStoreContractTests` and supplies a `Create()`
   factory pointing at your backend. The shared contract tests run as-is.
3. Wire it into the relevant plugin's `WithWriter` / ctor injection point.

No changes needed to the consumers — the abstraction makes them agnostic.

## See also

- `src/Neo.L2.Persistence/IL2KeyValueStore.cs` — the interface, with full XML docs.
- `src/Neo.L2.Persistence/RocksDbKeyValueStore.cs` — the production backend.
- `tests/Neo.L2.Persistence.UnitTests/UT_KeyValueStore.cs` — contract tests.
