# Launching a new L2 chain on Neo Elastic Network

This guide walks an operator from `git clone` to a registered, batch-producing
L2 chain. It also documents every plug-in point where chain-specific logic can
be customized without forking the framework.

The framework treats each L2 as an independent execution kernel + a uniform
NeoHub registration. Custom chains differ in *config* (chain id, DA mode,
proof type, sequencer model) and *injected components* (transaction executor,
DA writer, prover, sequencer source). Everything else — settlement protocol,
message routing, withdrawal verification — is shared.

---

## Quick path: 5 commands to a running L2

```bash
# 1. Generate config from a template (rollup / zk-rollup / validium / sidechain).
neo-stack create-chain --chain-id 1099 --template rollup --output ./my-l2

# 2. Initialize the node working directory (data/ logs/ Plugins/).
neo-stack init-l2 --chain-id 1099 --output ./my-l2

# 3. Print the L1 registration plan (run during permissioned admission phase
#    or governance-approved semi-permissionless / permissionless modes).
#    Without --operator/--verifier/--bridge/--message: prints plan-only.
#    With those four UInt160 hashes (discovered from neo-hub-deploy bundle):
#    emits the canonical 91-byte configBytes hex you paste into your wallet.
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <hash> --verifier <hash> --bridge <hash> --message <hash>

# 4. Print the bridge adapter deploy plan (one-time per new chain).
neo-stack deploy-bridge-adapter --chain-id 1099 --output ./my-l2

# 5. Run sequencer + batcher + prover. Each subcommand prints its preflight
#    checks and exits zero when the chain is ready to accept transactions.
neo-stack start-sequencer --chain-id 1099 --output ./my-l2 &
neo-stack start-batcher  --chain-id 1099 --output ./my-l2 &
neo-stack start-prover   --chain-id 1099 --output ./my-l2 &
```

Wallet-gated steps (#3, #4, and `submit-batch`) print the structured operator
plan — target contract, args, signed-transaction template, numbered next-steps —
rather than auto-signing. Operators feed the plan into their wallet of choice
(NEP-6 keystore, Ledger, etc.).

For a fully in-process demo without L1, see `tools/Neo.L2.Devnet`:

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5
# 5 batches end-to-end, real KeyedStateStore continuity, post-run audit pass
```

---

## Templates

`neo-stack create-chain --template <name>` picks one of four starting points.
Each writes a different `chain.config.json` (chainMode + daMode + proofType +
security label set per `doc.md` §6 + §16.2):

| Template     | chainMode        | daMode    | proofType  | SecurityLevel | Exit             |
|--------------|------------------|-----------|------------|---------------|------------------|
| `rollup`     | L2RollupMode     | L1        | Optimistic | Optimistic    | Delayed          |
| `zk-rollup`  | L2RollupMode     | L1        | Zk         | Validity      | Permissionless   |
| `validium`   | L2ValidiumMode   | NeoFS     | Zk         | Validium      | Delayed          |
| `sidechain`  | SidechainMode    | External  | None       | Sidechain     | Permissionless   |

All templates default to `sequencerModel: DbftCommittee` (Neo-native one-block
finality). All can be edited post-`create-chain` — the JSON is operator
property.

---

## Architecture: where custom logic plugs in

The framework's extension surface is a set of interfaces that each L2 wires
to its own implementation. The sample wiring lives in
`tools/Neo.L2.Devnet/Program.cs`; production deployments substitute their own
classes at the same call sites.

```
┌─────────────────────────────────────────────────────────────────┐
│ Settlement (NeoHub)                — operator-shared, immutable │
│   ChainRegistry · SharedBridge · SettlementManager · ...        │
└──────────────────┬──────────────────────────────────────────────┘
                   │  IL2BatchExecutor.ApplyBatchAsync
                   │  ITransactionExecutor.ExecuteAsync
                   │  IL2Prover.ProveAsync       ──► IL2ProofVerifier
                   │  IDAWriter.PublishAsync     ──► IDAWriter.IsAvailableAsync
                   │  ISequencerCommitteeProvider.GetActiveCommitteeAsync
                   │  IForcedInclusionSource.DequeueOverdueAsync
                   │  IL2Metrics.IncrementCounter / RecordHistogram / SetGauge
                   ▼
┌─────────────────────────────────────────────────────────────────┐
│ Per-L2 plug-in (you implement / configure)                      │
└─────────────────────────────────────────────────────────────────┘
```

### Five extension points operators commonly customize

| Interface                                    | Default                           | When to swap                                 |
|----------------------------------------------|-----------------------------------|----------------------------------------------|
| `ITransactionExecutor`                       | `ReferenceTransactionExecutor`    | Wire to a real NeoVM `ApplicationEngine`     |
| `IL2Prover` / `IL2ProofVerifier`             | Multisig / Optimistic / Mock-RiscV| Phase 4: SP1 prover via `Sp1RiscVProver`     |
| `IDAWriter`                                  | InMemory / NeoFsLike / Persistent | Real NeoFS SDK / L1 sendrawtransaction       |
| `ISequencerCommitteeProvider`                | `InMemorySequencerCommitteeProvider`| Wire to neo's `DBFTPlugin` consensus selector|
| `IRoundProver` (Phase 5 only)                | `PassThroughRoundProver`          | SP1 Compress / Halo2 accumulator / Risc0 fold|

All of these accept ctor injection. The plugin host is the single composition
root; see `Neo.Plugins.L2Metrics.L2MetricsPlugin` for the canonical pattern of
"plugin-A exposes a sink, plugin-B reads it via `WithMetrics(plugin.Metrics)`".

### Concrete customization recipe

```csharp
// 1. Build your custom transaction executor (e.g. backed by a real NeoVM).
var myExecutor = new MyNeoVmTransactionExecutor(myNeoSystem);

// 2. Wire it into the batch executor that the plugin host instantiates.
var batchExecutor = new ReferenceBatchExecutor(
    txExecutor: myExecutor,                        // ← injected
    stateRootOracle: keyedStateOracle,
    l1MessageProcessor: depositProcessor);

// 3. Inject your DA writer (e.g. real NeoFS SDK adapter you wrote).
plugin.WithWriter(new MyNeoFsAdapter(neoFsClient));

// 4. Wire metrics so all your custom components emit through the same sink.
myExecutor.WithMetrics(metricsPlugin.Metrics);
plugin.WithMetrics(metricsPlugin.Metrics);
```

Every plug-in point has the same shape: an interface in `Neo.L2.Abstractions`,
a default implementation in `Neo.L2.*` (in-memory / mock), and an injection
hook on the plugin (`WithWriter`, `WithMetrics`, ctor parameter).

### Worked example: writing a custom `IDAWriter`

To support a new DA tier (e.g. Celestia, Avail, or your own off-chain blob
service), implement `IDAWriter` and pass it to `L2DAPlugin.WithWriter()`.
Anatomy of the smallest viable implementation:

```csharp
using Neo.L2;

public sealed class CelestiaLikeDAWriter : IDAWriter
{
    private readonly ICelestiaClient _client;

    public CelestiaLikeDAWriter(ICelestiaClient client) => _client = client;

    // Pick the DAMode discriminant that matches your tier (External=2 for
    // generic third-party DA layers per Neo.L2.DAMode).
    public DAMode Mode => DAMode.External;

    public async ValueTask<DAReceipt> PublishAsync(
        DAPublishRequest request,
        CancellationToken cancellationToken = default)
    {
        // 1. Submit the payload to the DA layer; capture the layer's pointer
        //    (Celestia: namespace + height + index; NeoFS: container+object id).
        var pointer = await _client.SubmitBlobAsync(
            request.Payload, cancellationToken);

        // 2. Compute the cross-tier commitment. Always Hash256(payload) so
        //    DAAvailabilityCheck can compare across tiers without knowing
        //    the underlying layer's native commitment scheme.
        var commitment = Crypto.Hash256(request.Payload.Span);

        // 3. Return the receipt — Pointer must round-trip through your
        //    own layer's "fetch by pointer" RPC.
        return new DAReceipt
        {
            Commitment = new UInt256(commitment),
            Layer = Mode,
            Pointer = pointer.AsMemory(),
        };
    }

    public async ValueTask<bool> IsAvailableAsync(
        DAReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        // Your layer's "is this still retrievable" check. Many layers expose
        // a HEAD-style endpoint that returns 200 if the blob's still pinned.
        return await _client.HeadAsync(receipt.Pointer, cancellationToken);
    }
}
```

Then wire it at the plugin host:

```csharp
var dapw = new L2DAPlugin();
dapw.WithWriter(new CelestiaLikeDAWriter(myCelestiaClient));
dapw.WithMetrics(metricsPlugin.Metrics);  // emits l2.da.published / l2.da.errors
```

The framework's `MetricsEmittingDAWriter` (composed automatically when
`WithMetrics` is called) wraps whatever writer you passed in, so your
custom layer gets the same telemetry as the built-in writers without any
extra plumbing.

Reference implementations that follow this exact shape:
- `Neo.Plugins.L2DA.InMemoryDAWriter` — the simplest possible
- `Neo.Plugins.L2DA.NeoFsLikeDAWriter` — content-addressed blob store
- `Neo.Plugins.L2DA.JsonRpcL1DAWriter` — submits to an L1 NEP-17-style contract
- `Neo.Plugins.L2DA.PersistentDAWriter` — RocksDB-backed local store
- `Neo.Plugins.L2DA.CommitteeAttestedDAWriter` — DAC committee multisig

### Worked example: writing a custom `ISequencerCommitteeProvider`

If your chain uses a non-dBFT sequencer model (centralized, PoS-rotated,
oracle-selected, etc.), implement `ISequencerCommitteeProvider` to feed
your selection logic into the L2 node:

```csharp
using Neo.Cryptography.ECC;
using Neo.L2.Sequencer;

public sealed class StakeWeightedSequencerProvider : ISequencerCommitteeProvider
{
    private readonly IStakeOracle _stakes;
    private readonly int _maxSize;

    public uint ChainId { get; }

    public StakeWeightedSequencerProvider(uint chainId, IStakeOracle stakes, int maxSize)
    {
        ChainId = chainId;
        _stakes = stakes;
        _maxSize = maxSize;
    }

    public async ValueTask<IReadOnlyList<CommitteeMember>> GetActiveCommitteeAsync(
        CancellationToken cancellationToken = default)
    {
        // Pull the current top-N stakers from your stake oracle. The framework
        // doesn't care HOW you select — it just expects a list of CommitteeMember
        // records, each with PublicKey + L1Address + Status (1=Active) + ExitsAt.
        var top = await _stakes.GetTopByStakeAsync(_maxSize, cancellationToken);
        return top.Select(s => new CommitteeMember
        {
            PublicKey = s.PublicKey,
            L1Address = s.L1Address,
            Status = 1,                 // Active
            ExitsAtUnixSeconds = 0,     // Active members have no exit window
        }).ToList();
    }

    public ValueTask<int> GetMaxCommitteeSizeAsync(CancellationToken cancellationToken = default)
        => new ValueTask<int>(_maxSize);

    public ValueTask<bool> IsRegisteredAsync(ECPoint sequencerKey,
        CancellationToken cancellationToken = default)
        => _stakes.HasStakeAsync(sequencerKey, cancellationToken);
}
```

Wire it through whatever component owns the sequencer reference (typically
the L2 node's consensus selector — the existing `InMemorySequencerCommitteeProvider`
in `Neo.L2.Sequencer` shows the production-ready persistence + lifecycle
pattern your custom provider can follow if you also need restart-survival).

The L2 node's dBFT plugin polls this interface before each round, so switching
the provider is the only on-chain-visible step needed to swap sequencer models —
NeoHub's `SequencerRegistry` continues to track *who registered* but the
*selection policy* is the L2's call.

---

## Lifecycle in one diagram

```
User:    ──► Deposit on L1 (SharedBridge.Deposit)
                                 ↓
L2:      ── L1MessageInbox dequeues → DepositProcessor mints L2 GAS
                                 ↓
         User submits L2 tx → Sequencer orders → Batcher accumulates
                                 ↓
         Batch sealed → StateRootGenerator computes 7 roots
                                 ↓
         DAWriter publishes batch payload → DAReceipt → daCommitment
                                 ↓
         Prover produces proof (Multisig / Optimistic / ZK)
                                 ↓
L1:      SettlementManager.SubmitBatch(commitment, publicInputs, proof)
                                 ↓
         Verifier dispatches → finalize → updates canonical state root
                                 ↓
User:    Withdrawal: SharedBridge.FinalizeWithdrawalWithProof(...)
         (or EmergencyManager.EscapeHatchExitWithProof if L2 stalled)
```

Each arrow is a contract or a plugin method already present in the codebase.
For the spec mapping see `AGENTS.md` "Mapping doc.md to code" or
`docs/architecture-walkthrough.md`.

---

## Extending vs forking

This framework is designed for *extension*, not forking. Patterns:

- **New chain type** (e.g. a privacy chain) → add a `--template` entry in
  `CreateChainCommand.cs` with the right defaults; everything else reuses the
  shared NeoHub.
- **New proof system** (e.g. Halo2) → implement `IL2Prover` + `IL2ProofVerifier`,
  register with `VerifierRegistry`, point the L2's chain config at it.
- **New DA tier** (e.g. Celestia, Avail) → implement `IDAWriter`, wire via
  `L2DAPlugin.WithWriter()`, document the `daMode` byte you claim.
- **New sequencer model** (e.g. PoS-rotated) → implement
  `ISequencerCommitteeProvider`, wire via `L2BridgePlugin` ctor.

Each path keeps the chain inside the Neo Elastic Network — same SharedBridge,
same settlement, same message routing — while letting the chain's *internals*
be whatever the operator needs.

---

## Verifying your chain registered

```bash
# Devnet
dotnet run --project tools/Neo.L2.Devnet -- 3 --metrics-port 9090
curl http://127.0.0.1:9090/metrics | grep l2_batch_sealed

# After register-chain on L1, query NeoHub:
neo-cli invoke <ChainRegistryHash> getChainConfig <chainId>
# → returns 91 bytes (encoded L2ChainConfig per §16.2); empty = not registered

# Or query the 5-dimension §16.2 security label as a single object:
neo-cli invoke <ChainRegistryHash> getSecurityLevel <chainId>
neo-cli invoke <ChainRegistryHash> getSequencerModel <chainId>
neo-cli invoke <ChainRegistryHash> getExitModel <chainId>
neo-cli invoke <ChainRegistryHash> getDAMode <chainId>
neo-cli invoke <ChainRegistryHash> getGatewayEnabled <chainId>
neo-cli invoke <ChainRegistryHash> getPermissionlessExit <chainId>
```

A registered, batch-producing chain emits `l2.batch.sealed`,
`l2.settlement.submitted`, `l2.proving.generated`, and `l2.bridge.deposits`.
The audit framework (`Neo.L2.Audit.ChainAuditor`) runs 6 invariant checks
post-run; the devnet runs them automatically and prints a final ✅ / ❌.

---

## Reference

- Spec: [`doc.md`](../doc.md) (Chinese, authoritative)
- Architecture: [`ARCHITECTURE.md`](../ARCHITECTURE.md) (English distillation)
- Per-component: [`IMPLEMENTATION_STATUS.md`](../IMPLEMENTATION_STATUS.md)
- Walkthroughs: [`docs/architecture-walkthrough.md`](architecture-walkthrough.md)
- Telemetry: [`docs/telemetry.md`](telemetry.md)
- Persistence: [`docs/persistence.md`](persistence.md)
- Security model: [`docs/security-model.md`](security-model.md)
- Spec-gap plan: [`docs/spec-gap-plan.md`](spec-gap-plan.md)
