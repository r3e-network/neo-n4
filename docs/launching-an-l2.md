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

## Quickest path: the `new-l2` composite

```bash
# Single command: generates chain.config.json, initializes the node working
# directories (data/ logs/ Plugins/), and scaffolds a custom-executor project
# (csproj + executor skeleton + state seam + tx builder + KeyedStateStore
# adapter + README) PLUS a sibling MSTest project with 3 starter tests.
neo-stack new-l2 --name MyChain --chain-id 1099 --template rollup --output ./my-l2

# After the composite runs, the operator's "Next" output points at:
#   1. dotnet build + dotnet test for the executor scaffold
#   2. neo-stack validate for chain.config.json sanity-checks
#   3. dotnet run --project tools/Neo.L2.Devnet -- 5 --config ./my-l2/chain.config.json
#   4. edit MyChainExecutor.cs to replace the placeholder NoOp with real opcodes
```

Use the [`new-l2` composite](#quickest-path-the-new-l2-composite) when you want
the simplest path to a buildable + testable + devnet-previewable starter. Use
the [5-command path below](#quick-path-5-commands-to-a-running-l2) when you
want fine-grained control (e.g. skipping the executor scaffold for chains that
will use `ReferenceTransactionExecutor`).

## Quick path: 5 commands to a running L2

```bash
# 1. Generate config from a template (rollup / zk-rollup / validium / sidechain).
neo-stack create-chain --chain-id 1099 --template rollup --output ./my-l2

# 2. Review a Neo.CLI protocol config whose ValidatorsCount and genesis
#    StandbyCommittee match chain.config.json validators, then initialize the
#    sequencer/batcher/prover state directories without overwriting that config.
neo-stack init-l2 --chain-id 1099 --output ./my-l2 \
    --node-config /secure/reviewed/sequencer-config.json \
    --batcher-node-config /secure/reviewed/batcher-config.json

# 3. Print the L1 registration plan (run during permissioned admission phase
#    or governance-approved semi-permissionless / permissionless modes).
#    Without --operator/--verifier/--bridge/--message: prints plan-only.
#    With those four UInt160 hashes (discovered from neo-hub-deploy bundle):
#    emits the canonical 91-byte configBytes hex you paste into your wallet.
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <hash> --verifier <hash> --bridge <hash> --message <hash>

# 4. Print the bridge adapter deploy plan (one-time per new chain).
neo-stack deploy-bridge-adapter --chain-id 1099 --output ./my-l2

# 5. Put a reviewed Neo.CLI + DBFTPlugin deployment under ./my-l2/node and the
#    release prove-batch binary under ./my-l2/prover. These commands supervise
#    the real processes and remain attached so systemd/k8s sees the child exit.
neo-stack start-sequencer --chain-id 1099 --output ./my-l2 \
    --neo-cli ./my-l2/node/Neo.CLI.dll
neo-stack start-prover --chain-id 1099 --output ./my-l2 \
    --prover ./my-l2/prover/prove-batch

# Optional dedicated batcher follower: use a separate Neo.CLI deployment root
# and data directory. Never point two Neo.CLI processes at the same database.
neo-stack start-batcher --chain-id 1099 --output ./my-l2 \
    --neo-cli ./my-l2/batcher-node/Neo.CLI.dll \
    --data-dir ./my-l2/batcher-data
```

Wallet-gated steps (#3, #4, and `submit-batch`) retain deterministic plan mode.
With `--broadcast --rpc <url> --expected-network <magic>`, they sign through the
shared signer boundary, run an `invokescript` preflight, calculate exact fees,
broadcast, and wait for a HALT application log. The built-in CLI signer reads a
WIF from `NEO_N4_OPERATOR_WIF`; production HSM/KMS integrations implement
`INeoTransactionSigner` without changing transaction construction.

### Production settlement composition

`L2SettlementPlugin.WireProduction(...)` is the production composition root for L1
settlement. It consumes the plugin's `PluginConfiguration` and constructs one shared
`JsonRpcClient`, a network-pinned `RpcTransactionSender`, `RpcSettlementClient`,
`RpcForcedInclusionFinalizationClient`, and `RpcForcedInclusionSource`. The source and
finalizer are always wired as a pair; custom/test dependency injection remains available
through `Wire(...)`, which rejects a forced-inclusion source without a finalizer.

Configure every production identity explicitly in
`Plugins/Neo.Plugins.L2Settlement/config.json` before calling `WireProduction`:

```jsonc
{
  "PluginConfiguration": {
    "ChainId": 1099,
    "L1RpcEndpoint": "https://reviewed-l1-rpc.example:10331",
    "ExpectedNetwork": <l1-network-magic>,
    "SettlementManagerHash": "<real non-zero SettlementManager UInt160>",
    "ForcedInclusionHash": "<real non-zero ForcedInclusion UInt160>",
    "ProofType": 3,
    "Enabled": true
  }
}
```

The checked-in sample intentionally leaves the network and contract hashes unset.
Production wiring rejects missing or relative/non-HTTP endpoints, missing network magic,
malformed/zero/equal contract hashes, a zero signer account, and any WIF/private-key field
in plugin configuration. Pass an `INeoTransactionSigner` instance from the host instead;
the plugin never reads, stores, logs, or disposes signer key material.

```csharp
var forcedSource = settlementPlugin.WireProduction(
    batchPlugin,
    executor,
    daWriter,
    proofWitnessStore,
    prover,
    profile,
    hsmOrKmsSigner,
    knownForcedInclusionNonces,
    maxAutomaticRetries: 3);

forcedTxWatcher.NonceObserved += nonce => forcedSource.RegisterNonce(nonce);
```

Every settlement transaction is preflighted, signed for `ExpectedNetwork`, broadcast, and
confirmed with a `HALT` application log before the durable pipeline records success; zero
transaction hashes are rejected. `L2SettlementPlugin.Dispose()` releases only the RPC stack
created by `WireProduction`. The signer and all dependencies supplied to either wiring API
remain caller-owned and must be disposed by the host in its normal shutdown order.

Settlement reconciliation is strictly ordered by canonical batch number. A missing predecessor,
permanently invalid proof/artifact, reverted batch, unresolved transaction status, DA failure, or
forced-inclusion finalization failure increments a durable per-artifact retry checkpoint. After the
configured bound (default 3, allowed range 1–100), the earliest artifact becomes `Poisoned`; later
batches remain durable but cannot be proved or submitted around it. Restarts preserve the exact
batch number, artifact content hash, retry count, and last bounded error.

Monitor `GetRecoveryStatusAsync()` plus `l2.settlement.pending`,
`l2.settlement.retries`, and `l2.settlement.poisoned`. Recovery is deliberately explicit: correct
the prover/DA/RPC/L1 state first, verify the displayed content hash, reset that exact artifact, then
run reconciliation again:

```csharp
var status = await settlementPlugin.GetRecoveryStatusAsync();
if (status.State == SettlementRecoveryState.Poisoned)
{
    await settlementPlugin.RecoverPoisonedBatchAsync(
        status.BlockedBatchNumber!.Value,
        status.ArtifactContentHash!);
    await settlementPlugin.ReconcileAsync();
}
```

Recovery retains the canonical artifact, proof, known L1 transaction hash, and forced-inclusion
reservations, so normal idempotent L1 reconciliation still decides whether a broadcast must be
observed, replaced, or left untouched. There is no operator "skip" or local terminal-rejection
button: defining a protocol-safe terminal rejection requires an explicit chain-governance policy
for state-root continuity, deposits, messages, withdrawals, and forced inclusions.

### Neo.CLI bundle and dBFT committee state

`r3e-network/neo-node` is not published, so this repository deliberately does
not fabricate or auto-download a mutable node distribution. Build the mature
Neo.CLI and DBFTPlugin sources you have reviewed against the pinned
`r3e-network/neo` core, then deploy each role with plugins adjacent to the
Neo.CLI entry assembly:

```text
my-l2/node/
├── Neo.CLI.dll
├── config.json
└── Plugins/
    └── DBFTPlugin/
        ├── DBFTPlugin.dll
        └── DBFTPlugin.json
```

`config.json` must live beside the Neo.CLI entry assembly. Its
`ApplicationConfiguration.Storage.Path` must resolve to the role's isolated data
directory; the sequencer config must also enable `UnlockWallet.IsActive`, name an
existing wallet, and pair with `DBFTPlugin.json` where
`PluginConfiguration.AutoStart=true`. The batcher has its own deployment root,
`config.json`, database, and `Neo.Plugins.L2Batch` config with the same `ChainId`.
`neo-stack` intentionally does not pass Neo.CLI `--config` or `--db-path`
overrides: current Neo.CLI option binding reconstructs wallet settings and can
drop the auto-unlock flag, leaving DBFTPlugin loaded but consensus stopped.

The DBFTPlugin remains unmodified. Its existing call to
`NativeContract.NEO.GetNextBlockValidators` now reads the canonical validator
set stored by `L2SystemConfigContract`. The genesis committee authorizes initial
configuration; subsequent committee rotation is an owner-authorized native
transaction, not an off-chain callback:

```bash
neo-stack start-sequencer --chain-id 1099 --output ./my-l2 \
    --node-config ./my-l2/node/config.json \
    --sync-only --broadcast --rpc http://existing-l2-rpc:10332 \
    --expected-network <magic>
```

The command parses and canonicalizes `chain.config.json.validators`, checks its
count against the node's `ValidatorsCount`, simulates the native call, signs,
broadcasts, and confirms the pending rotation. The old committee then commits the
new `NextConsensus` at the next deterministic committee-refresh block; only after
that block does native state promote pending to active. `neo-stack` polls
`getSequencerValidators` until that activation (bounded by
`--committee-activation-timeout-seconds`). A genesis-matching set can start without
RPC; a rotated set must either complete this scheduled activation or already match
finalized native state queried through `--rpc`.

For a fully in-process demo without L1, see `tools/Neo.L2.Devnet`:

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5
# 5 batches end-to-end, real KeyedStateStore continuity, post-run audit pass

# Or preview your operator-template config end-to-end (the post-run RPC
# snapshot's getsecuritylabel will reflect the JSON's §16.2 dimensions):
dotnet run --project tools/Neo.L2.Devnet -- 5 --config ./my-l2/chain.config.json

# Sanity-check the JSON before running anything against L1:
neo-stack validate ./my-l2/chain.config.json
# ✅ valid: chainId=1099 securityLevel=Optimistic daMode=NeoFS ...
# (or ❌ pointing at exactly the field that's wrong)
```

---

## Adding custom chain logic (optional)

Most L2 chains specialize how transactions execute on their chain — gaming
rollups want fast counter increments, exchange validiums want orderbook ops,
privacy sidechains want proof-verification opcodes. The framework's seam is
[`ITransactionExecutor`](../src/Neo.L2.Executor/ITransactionExecutor.cs) — one
method, deterministic per [`SPEC.md`](../src/Neo.L2.Executor/SPEC.md), wired
into the standard pipeline so sealing / proving / settlement / fraud-proof
all just work without further plumbing.

```bash
# 1. Scaffold a starter custom-executor project (csproj + executor skeleton +
#    state seam + tx builder + KeyedStateStore adapter + README in one go).
#    Add --with-tests to also emit a sibling tests project that pins the
#    placeholder opcodes (3 starter tests: NoOp success + empty-tx failed +
#    unknown-opcode failed).
neo-stack scaffold-executor --name MyChain --chain-id 1099 --with-tests

# Output (default ./samples/executors/MyChainExecutor):
#   MyChainExecutor.csproj
#   MyChainExecutor.cs              ← ITransactionExecutor with a NoOp placeholder
#   IMyChainState.cs                ← state seam + InMemory impl
#   MyChainTxBuilder.cs             ← canonical tx-byte builders
#   MyChainKeyedStateStoreAdapter.cs← production bridge to KeyedStateStore
#   README.md                       ← 5-step customization checklist
#
# With --with-tests, also: ./samples/executors/MyChainExecutor.UnitTests/
#   MyChainExecutor.UnitTests.csproj
#   Usings.cs
#   UT_MyChainExecutor.cs           ← 3 starter tests (NoOp success + edge cases)

# 2. The scaffold compiles + tests pass as-is. Build + test:
dotnet build samples/executors/MyChainExecutor /p:NuGetAudit=false
dotnet test  samples/executors/MyChainExecutor.UnitTests /p:NuGetAudit=false

# 3. Edit MyChainExecutor.cs — replace Opcode.NoOp with your chain's opcodes
#    (IncrementCounter, EmitWithdrawal, EmitMessage, AppSpecificOp, …).
#    Each opcode is one byte at offset 0; the rest is opcode-specific body.
#    Mirror new opcodes' tests in UT_MyChainExecutor.cs as you add them.
```

Working reference for what a "real" custom executor looks like:
[`samples/executors/Sample.CounterChainExecutor`](../samples/executors/Sample.CounterChainExecutor) —
3 opcodes (IncrementCounter / EmitWithdrawal / EmitMessage), per-sender state
mutation, withdrawal emission, L2→L2 messaging via canonical
`MessageBuilder.Build`, full SPEC.md determinism. End-to-end-tested
through `ReferenceBatchExecutor` + `KeyedStateRootOracle` + multisig
prover/verifier in
[`tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs`](../tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs).

The scaffold's `KeyedStateStoreAdapter` is the bridge that lets your
executor's writes participate in the post-state-root oracle. With it wired,
`BatchExecutionResult.PostStateRoot` reflects the actual mutations (not a
synthetic XOR of the receipt root) — see
[`UT_KeyedStateStoreAdapter.cs`](../tests/Sample.CounterChainExecutor.UnitTests/UT_KeyedStateStoreAdapter.cs)
for the parity pin against direct `KeyedStateStore` writes.

To see a custom executor running through the full devnet pipeline (deposits +
state mutations + receipts + withdrawals + DA + proving + verification + audit):

```bash
# Run the in-process devnet with the Sample.CounterChainExecutor wired in.
# Each batch adds a deposit (as before) PLUS three Counter txs that exercise
# IncrementCounter (state mutation), EmitWithdrawal (withdrawal channel),
# EmitMessage (L2→L2 cross-chain channel).
dotnet run --project tools/Neo.L2.Devnet -- 5 --executor counter

# Look for the "[exec]" line per batch — gas + txRoot + L2-to-L2 root all
# come from the Counter executor's actual outputs. The post-run RPC
# snapshot's "state entries" count includes the Counter writes alongside
# the deposit-induced bridge balance.
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

For ready-to-run sample configs covering distinct use cases (general-purpose
DeFi rollup / gaming chain / DEX validium / privacy sidechain), see
[`samples/`](../samples/README.md). Each sample is verified end-to-end via
`neo-l2-devnet --config samples/<name>.config.json`.

### Optimistic-rollup operators: wire a fraud verifier

`rollup` template chains run with `proofType: Optimistic`, which means
`NeoHub.OptimisticChallenge` enforces a challenge window during which any
party can submit a fraud proof. Submission via `Challenge(chainId,
batchNumber, challenger, fraudProofBytes, fraudVerifier)` delegates the
actual cryptographic check to a contract identified by the
`fraudVerifier` argument.

Four verifier modes are available. The default 24-step production bundle
deploys both reference verifiers and configures the restricted verifier with
`[SettlementManager, replayDomain]`; live deployment therefore requires an
explicit `--fraud-replay-domain`:

  1. **Governance-arbitration mode** (the simplest operator-friendly path):
     deploy `NeoHub.GovernanceFraudVerifier`. It does a structural check
     of the canonical `FraudProofPayload` (v1=101 bytes fixed or v2=105+N
     bytes with disputed-tx witness; length / version /
     claims-a-real-discrepancy) and emits accept/reject events for the
     security council to arbitrate. Pass its deployed hash as
     `fraudVerifier` when filing a challenge.
  2. **Structural v3 governance mode**: legacy custom plans may deploy
     `NeoHub.RestrictedExecutionFraudVerifier` with empty data. V3 re-derives
     challenger-supplied storage roots, but does not bind them to the committed
     batch or execute the transaction; register it through approved-only
     `RegisterFraudVerifier` and require governance co-sign.
  3. **Permissionless restricted v4**: the production bundle deploys
     `NeoHub.RestrictedExecutionFraudVerifier` with
     `[SettlementManager, replayDomain]`, then calls
     `RegisterPermissionlessFraudProfile(chainId, verifier,
     executorSemanticId, replayDomain)`. The shipped semantic id covers exactly
     one existing-key Counter Increment transaction. It binds the canonical
     committed header/roots, tx proof, canonical degenerate `[0,1]` transcript, claim id, and
     old/new storage proofs against the committed pre/post roots, then executes
     that transition. Correct committed execution returns false; a wrong
     committed root returns true. Production smoke checks verify the deployed
     settlement-manager hash, replay domain, semantic id, and exact profile.
     This mode is not general NeoVM; unsupported semantics fail closed.
  4. **Custom verifier**: ship your own fraud verifier (e.g. one that
     re-executes the disputed transaction on L1 with restricted state).
     Skip both reference verifiers from the deploy bundle and register
     your own verifier's hash. The `FraudProofPayload` v2 (DisputedTxBytes)
     and v3 (StorageProofs) wire-format fields carry the disputed-tx
     bytes + storage manifests a re-execution verifier needs.

`neo-hub-deploy`'s post-deploy actions output surfaces the right hash for
each verifier so operators know which to pass as the `fraudVerifier`
argument:

```
# Note: for v1/v2 fraud proofs (governance arbitration),
#       pass GovernanceFraudVerifier.Hash ...
# Note: for v3 fraud proofs (governance-only structural evidence),
#       pass RestrictedExecutionFraudVerifier.Hash ...
```

Do not interpret the legacy deploy-planner v3 note as permissionless
authorization. `RegisterPermissionlessFraudVerifier` is disabled; value-bearing
permissionless challenges require the explicit v4 profile registration above.

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

- **`ITransactionExecutor`** — Default: `ReferenceTransactionExecutor`.
  Swap for domain-specific opcodes; `neo-stack scaffold-executor`
  emits a starter, [`Sample.CounterChainExecutor`](../samples/executors/Sample.CounterChainExecutor)
  is the working reference.
- **`IL2Prover`** / **`IL2ProofVerifier`** — Default: Multisig /
  Optimistic / Mock-RiscV. Swap for Stage 2 (ZK validity):
  `prove-batch daemon` (out-of-process Rust prover at
  `bridge/neo-zkvm-host/`).
- **`IDAWriter`** — Default: NeoFS (`NeoFsLikeDAWriter`) or a
  persistent NeoFS-like store when `--data-dir` is set; `External`
  uses `InMemoryDAWriter` for tests/demos. Swap for a real NeoFS SDK /
  L1 `sendrawtransaction`.
- **`ISequencerCommitteeProvider`** — Registry/source abstraction for discovering
  the desired set. Production consensus does not call an external provider during
  a dBFT round; use `SequencerCommitteeTransactionBuilder` to atomically commit the
  selected set into native consensus state.
- **`IRoundProver`** (Phase 5 only) — Default: `PassThroughRoundProver`.
  Swap for SP1 Compress / Halo2 accumulator / Risc0 fold.

All of these accept ctor injection. The plugin host is the single composition
root; see `Neo.Plugins.L2Metrics.L2MetricsPlugin` for the canonical pattern of
"plugin-A exposes a sink, plugin-B reads it via `WithMetrics(plugin.Metrics)`".

### Concrete customization recipe

```csharp
// 1. Build your custom transaction executor. Either fork the working
//    sample (samples/executors/Sample.CounterChainExecutor — has 3
//    real opcodes already) or scaffold a fresh one:
//
//      neo-stack scaffold-executor --name MyChain --chain-id 1099
//
//    For the canonical N4 path, wire RiscVTransactionExecutor / NeoVM2-RISC-V.
//    ApplicationEngine is only the legacy NeoVM compatibility path.
var stateStore = new KeyedStateStore();              // production: rocksdb-backed
var stateAdapter = new MyChainKeyedStateStoreAdapter(stateStore);
var myExecutor = new MyChainExecutor(
    chainId: 1099,
    state: stateAdapter,                              // executor's writes flow into…
    emittingContract: emittingContractHash);

// 2. Wire it into the batch executor that the plugin host instantiates.
var keyedStateOracle = new KeyedStateRootOracle(stateStore);  // …the same store the oracle hashes
var batchExecutor = new ReferenceBatchExecutor(
    txExecutor: myExecutor,                          // ← injected
    postStateRootOracle: keyedStateOracle,
    l1Processor: depositProcessor);

// 3. Inject your DA writer (e.g. real NeoFS SDK adapter you wrote).
plugin.WithWriter(new MyNeoFsAdapter(neoFsClient));

// 4. Wire metrics so all your custom components emit through the same sink.
plugin.WithMetrics(metricsPlugin.Metrics);
```

Every plug-in point has the same shape: an interface in `Neo.L2.Abstractions`,
a default implementation in `Neo.L2.*` (in-memory / mock), and an injection
hook on the plugin (`WithWriter`, `WithMetrics`, ctor parameter).

End-to-end test for the full custom-executor pipeline (this exact wiring
shape):
[`tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs`](../tests/Neo.L2.IntegrationTests/UT_E2E_CustomExecutor_FullStack.cs).

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

Use the provider to compute the intended active set, then pass those keys through
`SequencerCommitteeTransactionBuilder.BuildSetValidatorsScript` and submit the
result under L2 governance. The dBFT plugin reads only the finalized native state,
which keeps every validator deterministic even if an L1 RPC endpoint is unavailable
or two operators observe registry events at different times. NeoHub's
`SequencerRegistry` remains the admission source; the confirmed L2 transaction is
the consensus activation boundary.

### Worked example: writing a custom `IL2Prover` + `IL2ProofVerifier`

Phase 4 (ZK validity proofs) lets operators bring their own proof system —
SP1 ships as the reference, but a chain that wants Halo2, Plonky3, or Risc0
just implements the prover/verifier pair and registers the verifier with
`NeoHub.VerifierRegistry` on L1.

Both interfaces live in `Neo.L2.Abstractions`. The pair must agree on
the same wire format — what the prover emits in `ProofResult.Proof` is
what the verifier later decodes:

```csharp
using Neo.L2;

// Prover side: runs on the sequencer, produces proofs.
public sealed class Halo2Prover : IL2Prover
{
    private readonly IHalo2BackendClient _backend;

    public Halo2Prover(IHalo2BackendClient backend) => _backend = backend;

    public ProofType Kind => ProofType.Zk;  // shares ProofType.Zk with SP1; on-chain
                                            // dispatch by VerificationKeyId distinguishes them.

    public async ValueTask<ProofResult> ProveAsync(
        ProofRequest request, CancellationToken cancellationToken = default)
    {
        // 1. Serialize public inputs in the canonical format VerifierRegistry expects.
        var publicInputBytes = Neo.L2.Batch.BatchSerializer.EncodePublicInputs(
            request.PublicInputs);

        // 2. Hand off to your proof-system backend with the witness (request.Witness).
        var proofBytes = await _backend.ProveAsync(
            publicInputBytes, request.Witness, cancellationToken);

        // 3. Wrap in the canonical RiscVProofPayload envelope. ProofSystem byte
        //    distinguishes Sp1 / Halo2 / etc. so the verifier knows which decoder to use.
        var payload = new Neo.L2.Proving.RiscVZk.RiscVProofPayload
        {
            ProofSystem = Neo.L2.Proving.RiscVZk.ProofSystem.Halo2,  // or your registered tag
            ProofBytes = proofBytes,
            VerificationKeyId = _backend.VerificationKeyId,
        };

        return new ProofResult
        {
            Proof = payload.Encode(),
            Kind = ProofType.Zk,
            PublicInputHash = Neo.L2.State.StateRootCalculator.HashPublicInputs(
                request.PublicInputs),
        };
    }
}

// Verifier side: runs on L1 (off-chain pre-flight) and is mirrored on-chain in the
// VerifierRegistry-registered NeoVM contract that does the actual cryptographic check.
public sealed class Halo2Verifier : IL2ProofVerifier
{
    private readonly UInt256 _expectedVkId;
    private readonly IHalo2BackendClient _backend;

    public Halo2Verifier(UInt256 expectedVkId, IHalo2BackendClient backend)
    {
        _expectedVkId = expectedVkId;
        _backend = backend;
    }

    public ProofType Kind => ProofType.Zk;

    public async ValueTask<ProofVerificationResult> VerifyAsync(
        PublicInputs publicInputs, ReadOnlyMemory<byte> proof,
        CancellationToken cancellationToken = default)
    {
        // 1. Decode the canonical envelope. Bad bytes → fail with a clear reason.
        Neo.L2.Proving.RiscVZk.RiscVProofPayload payload;
        try
        {
            payload = Neo.L2.Proving.RiscVZk.RiscVProofPayload.Decode(proof.Span);
        }
        catch (Exception ex)
        {
            return ProofVerificationResult.Fail($"decode: {ex.Message}");
        }

        // 2. VK pin: caller's expected vk must match what the prover declared.
        if (!payload.VerificationKeyId.Equals(_expectedVkId))
            return ProofVerificationResult.Fail("vk mismatch");

        // 3. Hand off to your verifier backend.
        var ok = await _backend.VerifyAsync(
            publicInputs, payload.ProofBytes, cancellationToken);
        return ok ? ProofVerificationResult.Ok : ProofVerificationResult.Fail("halo2 reject");
    }
}
```

The reference for Stage-2 ZK validity is `bridge/neo-zkvm-host/` — the
production prover daemon (`prove-batch daemon --watch <dir>`). For
in-process testing, `Neo.L2.Proving.RiscVZk.MockRiscVProver` provides a
deterministic placeholder. Wire the verifier into the chain's boot
sequence; register the matching on-chain verifier contract via
`NeoHub.VerifierRegistry.RegisterVerifier(proofType, verifierHash)` so the
canonical settlement path picks it up.

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
L1:      SettlementManager.SubmitBatch(commitmentBytes, l1MessageHash, blockContextHash)
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

## Going to L1: deploying NeoHub

Before `register-chain` works, the 24 production NeoHub contracts must be
deployed on the target L1. The test-only `ExternalBridgeStubVerifier` is not
part of the default deploy bundle. The `neo-hub-deploy` tool emits a deploy
bundle that names each contract, its dependencies, and the resolved hashes
after a topological sort:

```bash
# 1. Scaffold a starter plan (24 production NeoHub deploy steps in dependency
#    order, including ContractZkVerifier, Sp1Groth16Verifier, and both fraud verifiers).
dotnet run --project tools/Neo.Hub.Deploy -- scaffold \
    --output ./my-l2/deploy-plan.json

# 2. Edit the plan to fill in OWNER_REPLACE_ME / BOND_ASSET_REPLACE_ME
#    placeholders (canonical GAS hash on the target L1, your operator
#    multisig hash, etc.). The plan is JSON — diff-friendly + editable.

# 3. Topo-sort + resolve $step:<name> placeholders against deterministic
#    contract hashes derived from the deploy order. The output bundle is
#    what your wallet feeds to ContractManagement.Deploy in order.
dotnet run --project tools/Neo.Hub.Deploy -- plan \
    --plan ./my-l2/deploy-plan.json \
    --output ./my-l2/deploy-bundle.json
```

The bundle's `Invocations` array is your wallet's deploy script — one
`ContractManagement.Deploy` call per entry, in order. Each entry has a
`Name`, the path to its `.nef` + `.manifest.json`, and the resolved
`DeployData` (with all `$step:<name>` placeholders replaced by the hashes
of contracts deployed earlier in the bundle).

The bundle's "PostDeployActions" section surfaces the wiring steps that
have to run AFTER all contracts are deployed (e.g.
`SequencerBond.RegisterSlasher(OptimisticChallenge)` to break the
bond↔challenge cycle, `ChainRegistry.SetGovernanceController` to enable
§16.1 admission policy, and per-fraud-verifier informational notes
naming which contract hash to pass as the `fraudVerifier` argument to
`OptimisticChallenge.Challenge`).

> **Note on hashes**: the bundle's per-step `Hash` fields are
> *deterministic stubs* derived from the step name (so `plan` is
> reproducible without a wallet). The actual L1 contract hashes only
> exist after your wallet calls `ContractManagement.Deploy`. The wallet
> returns each real hash; capture those into the four `register-chain`
> flags below — NOT the stub hashes from the bundle.

After all 22 deploys + post-deploy wiring complete, capture the
**real on-chain** contract hashes (returned by your wallet from each
`ContractManagement.Deploy` call) into the four `register-chain` flags:

```bash
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <real hash returned by your multisig deploy> \
    --verifier <real hash returned by your VerifierRegistry deploy> \
    --bridge <real hash returned by your bridge-adapter deploy> \
    --message <real hash returned by your MessageRouter deploy>
```

That emits the canonical 91-byte `configBytes` your wallet pastes into
`ChainRegistry.RegisterChain` (admission-mode 0) or
`ChainRegistry.RegisterChainPublic` (admission-modes 1 + 2 — the §16.1
3-phase flow gated by `GovernanceController.GetAdmissionMode`).

Once `RegisterChain` returns, the L2 is alive — the sequencer +
batcher + prover plugins start producing batches (`start-sequencer`
/ `start-batcher` / `start-prover` from the 5-command path above), and
each batch's commitment flows through `SettlementManager.SubmitBatch`
on L1.

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

## Prover deployment (Stage 2 ZK validity)

If your chain runs in Stage-2 (RISC-V ZK validity) mode, the prover should
be a **separate process** from the sequencer — same architecture every
mainstream zk-rollup uses (Optimism's op-batcher/op-proposer split,
Arbitrum's BoLD provers, ZKsync's prover subsystem). Provers are
multi-GB, multi-CPU/GPU workloads with their own SLA; coupling them to
the sequencer process is fragile and doesn't scale.

The framework ships **two** SP1 integrations. Pick one:

### Recommended: out-of-process Rust prover daemon

**This is the production path.** Modern sp1-sdk 6.2.1, simpler dep graph,
matches industry-standard L2 layout.

```bash
# Build the daemon binary (one-time):
cd bridge/neo-zkvm-host
cargo build --release
# → target/release/prove-batch

# Wire the sequencer to drop sealed batches into a queue dir
# (e.g. /var/lib/neo-l2/batches/) — the format is just the canonical
# BatchExecutionRequest bytes written to <batch-number>.batch.bin.
# That format is what `Neo.L2.Batch.BatchExecutionRequest.Encode()` emits.

# Run the daemon (typically under systemd / k8s):
prove-batch daemon \
    --watch /var/lib/neo-l2/batches \
    --archive /var/lib/neo-l2/proven \
    --poll-secs 5

# Equivalent supervised launch from the chain directory:
neo-stack start-prover --chain-id 1099 --output ./my-l2 \
    --prover ./my-l2/prover/prove-batch -- --poll-secs 5
```

The daemon polls `--watch` for `*.batch.bin`, generates a real ZK proof
for each, writes `<name>.proof.bin` (the on-chain submission artifact)
+ `<name>.proof.vk` (the verifying key, stable per guest ELF), and moves
the input to `--archive` so it's not re-processed. Failures leave the
input in place and log loudly so monitoring catches poison-pill batches.

What it actually proves: each tx in the batch is loaded as a Neo N3 VM
script and executed by `neo_vm_guest::execute` (vendored from
`external/neo-zkvm/crates/neo-vm-guest`, which contains the full Neo N3
VM in pure Rust — opcodes, eval stack, gas accounting, native contracts,
storage). The proof attests that each tx halted or faulted at a specific
gas count with a specific top-of-stack result. Tampering with any
execution detail breaks the proof.

The on-chain settlement transaction submits `<name>.proof.bin`,
`<name>.proof.vk`, and the public-input commitment via
`NeoHub.SettlementManager.SubmitBatch`. `VerifierRegistry` dispatches to
the registered verifier and the chain finalizes if the proof verifies.

### Verifying a proof off-chain before submission

The framework ships a public `verify()` so an operator can sanity-check
a proof before paying L1 gas:

```rust
use neo_zkvm_host;

let proof = std::fs::read("00000042.proof.bin")?;
let vk    = std::fs::read("00000042.proof.vk")?;
let expected_pi_hash: [u8; 32] = /* from BatchExecutionRequest */;

neo_zkvm_host::verify(&proof, &vk, &expected_pi_hash)?;
```

Verification is ~42 s on a beefy CPU for the current circuit size.
Fold this into your prover daemon's pre-submission check if you want
belt-and-braces (the daemon doesn't run it by default — the prover
already produced the proof, so verification only catches bugs in the
prover itself).

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
