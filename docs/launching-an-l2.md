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
#    With those four UInt160 hashes plus the reviewed non-zero genesis state root:
#    emits the canonical 91-byte configBytes and immutable root you submit together.
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <hash> --verifier <hash> --bridge <hash> --message <hash> \
    --genesis-state-root <authenticated-non-zero-UInt256>

# 4. Print the bridge adapter deploy plan (one-time per new chain).
neo-stack deploy-bridge-adapter --chain-id 1099 --output ./my-l2

# 5. Put a reviewed Neo.CLI + DBFTPlugin deployment under ./my-l2/node and the
#    release prove-batch binary under ./my-l2/prover. Each command supervises a
#    real child process and remains attached, so start these in separate service
#    units or terminals rather than pasting them sequentially into one shell.
#
# Terminal / service A: dBFT sequencer node.
neo-stack start-sequencer --chain-id 1099 --output ./my-l2 \
    --neo-cli ./my-l2/node/Neo.CLI.dll

# Terminal / service B: separate SP1 prover daemon.
neo-stack start-prover --chain-id 1099 --output ./my-l2 \
    --prover ./my-l2/prover/prove-batch

# Terminal / service C (optional): dedicated batcher follower. Use a separate
# Neo.CLI deployment root and data directory; never point two node processes at
# the same database.
neo-stack start-batcher --chain-id 1099 --output ./my-l2 \
    --neo-cli ./my-l2/batcher-node/Neo.CLI.dll \
    --data-dir ./my-l2/batcher-data
```

Wallet-gated steps (#3, #4, and `submit-batch`) retain deterministic plan mode.
With `--broadcast --rpc <url> --expected-network <magic>`, they sign through the
shared signer boundary, run an `invokescript` preflight, calculate exact fees,
broadcast, and wait for a HALT application log. The built-in CLI signer reads a
WIF from `NEO_N4_OPERATOR_WIF`; production HSM/KMS integrations use the
fail-closed `--signer-command` protocol without changing transaction
construction. See [operator signer-command protocol](./operator-signer-command-protocol.md).

### Production settlement composition

`L2SettlementPlugin.WireProduction(...)` is the production composition root for L1
settlement. It consumes the plugin's `PluginConfiguration` and constructs one shared
`JsonRpcClient`, a network-pinned `RpcTransactionSender`, `RpcSettlementClient`,
`RpcForcedInclusionEventScanner`, `RpcForcedInclusionFinalizationClient`, and
`RpcForcedInclusionSource`. When `SharedBridgeHash` is set, it also constructs an owned
`RpcSharedBridgeDepositSource` (unless the host passes an explicit `depositSource`). When
`MessageRouterHash` is set, it constructs an owned `RpcMessageRouter` with a durable
`L1ToL2Enqueued` event scanner (unless the host passes an explicit `messageRouter`). Both
are installed on the batcher via `WireL1MessageInbox` before the sealed-batch sink.
Forced-inclusion scanner, source, and finalizer are always wired as one production unit;
custom/test DI remains available through `Wire(...)`.

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
    "SharedBridgeHash": "<real non-zero SharedBridge UInt160>",
    "L2BridgeHash": "",
    "MessageRouterHash": "<real non-zero MessageRouter UInt160>",
    "ProofType": 3,
    "Enabled": true
  }
}
```

Empty `L2BridgeHash` defaults to `NativeContract.L2Bridge.Hash` for N4 L2s.
Empty `MessageRouterHash` leaves MessageRouter caller-supplied (or omitted).

The checked-in sample intentionally leaves the network and contract hashes unset.
Production wiring rejects missing or relative/non-HTTP endpoints, missing network magic,
malformed/zero/equal contract hashes, a zero signer account, and any WIF/private-key field
in plugin configuration. Pass an `INeoTransactionSigner` instance from the host instead;
the plugin never reads, stores, logs, or disposes signer key material.

For the SP1 validity profile, build the complete execution/proving dependency stack before
calling `WireProduction`. The state store, signer, DA writer, witness store, and queue directories
remain caller-owned:

```csharp
using var state = new RocksDbKeyValueStore("/var/lib/neo-l2/state");

// First deployment only: bootstrap the reviewed Neo protocol settings into this exact store.
NeoVMGenesisBootstrap.Run(state, reviewedProtocolSettings);
var observedInitialRoot =
    Sp1StateWitnessSource.InitializeGenesisContractBindings(state);

// Persist this non-zero root in the signed deployment manifest before batch 1. On every
// restart, load the persisted value and reject any mismatch rather than adopting a new root.
if (!observedInitialRoot.Equals(operatorManifest.InitialStateRoot))
    throw new InvalidDataException("SP1 genesis state root differs from the deployment manifest");

// Register this exact value atomically with the ChainRegistry config. The ZK profile,
// signed deployment manifest, and L1 trust anchor must never use different roots.

var sp1 = Sp1SettlementExecutionStack.Create(
    chainId: 1099,
    state,
    operatorManifest.InitialStateRoot,
    executorPath: "/opt/neo-n4/bin/neo-zkvm-executor",
    executorSha256: Convert.FromHexString(operatorManifest.ExecutorSha256Hex),
    executorScratchDirectory: "/var/lib/neo-l2/executor-scratch",
    proverQueueDirectory: "/var/lib/neo-l2/batches",
    verificationKeyId: operatorManifest.BatchVerificationKeyId);

var forcedSource = settlementPlugin.WireProduction(
    batchPlugin,
    sp1.Executor,
    daWriter,
    proofWitnessStore,
    sp1.Prover,
    sp1.Profile,
    hsmOrKmsSigner,
    forcedInclusionEventRocksDbStore,
    forcedInclusionDeploymentHeight,
    forcedInclusionFinalityDepth: 1,
    knownForcedInclusionNonces: migrationSeed,
    maxAutomaticRetries: 3,
    l1FinalizedHeight: () => operatorL1FinalizedHeight,
    sequencerCommitteeHash: () => operatorCommitteeHash,
    sharedBridgeDepositEventStore: sharedBridgeDepositEventRocksDbStore,
    sharedBridgeDeploymentHeight: sharedBridgeDeployBlock,
    messageRouterEventStore: messageRouterEventRocksDbStore,
    messageRouterDeploymentHeight: messageRouterDeployBlock);
// With SharedBridgeHash configured, batchPlugin.DepositSource is the owned
// RpcSharedBridgeDepositSource (Scan then Drain at seal). With MessageRouterHash
// configured, WireProduction owns RpcMessageRouter + L1ToL2Enqueued scanner.
```

`ExecutorSha256Hex` must come from a reviewed/signed release manifest, not be calculated from the
same mutable path at process startup. Each invocation copies the executable into a private scratch
directory while hashing it, executes only that digest-matched copy, validates the complete
`NEO4EXR1` request/result/post-state/public-input binding, and atomically replaces the full state.
N4 genesis V1 rejects contract descriptor add/remove/replace operations and unsupported native or
consensus behavior. Expanding that profile requires a coordinated guest/VK/verifier protocol
upgrade; it is not a runtime flag.

The prover queue is a security boundary, not a temporary scratch directory. On Unix the queue and
archive directories are forced to `0700`, every artifact is `0600`, and symlinks or foreign-owned
entries fail closed. Defaults cap the combined watch/archive footprint at 16 GiB and 64
content-addressed tasks; operators may lower the limits with `--max-queue-bytes` and
`--max-queue-tasks`. Do not configure a TTL. After the durable proof manifest records
`SettlementFinalized`, the settlement pipeline writes `<content-hash>.proof.ack`; the daemon checks
its 32-byte body and only then prunes the matching request and proof set.

`forcedInclusionDeploymentHeight` is the block that deployed the configured contract. The
caller-owned event store must be durable in production. `knownForcedInclusionNonces` and
`RegisterNonce` are recovery/migration hooks only; normal operation discovers
`ForcedTxEnqueued` from finalized L1 blocks through `getblock` + `getapplicationlog`.

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
`l2.settlement.confirmation_lag_batches`,
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

Two production-target deployment profiles are documented: the bundled restricted-v4
profile and an operator-supplied custom executable-v4 profile. The default 24-step production bundle
excludes the structural v1/v2 advisory contract and configures the restricted verifier with
`[SettlementManager, replayDomain]`; live deployment therefore requires an
explicit `--fraud-replay-domain`:

  1. **Permissionless restricted v4**: the production bundle deploys
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
  2. **Custom executable v4 verifier**: ship your own fraud verifier that
     re-executes the disputed transaction on L1 with restricted state.
     Replace the restricted verifier in the deploy bundle and register an exact
     chain/semantic/replay-domain v4 profile. A custom verifier must consume the
     committed batch header and executable witness; accepting self-asserted roots
     or structural payloads is not a supported production profile.

`GovernanceFraudVerifier` v1/v2 and the structural v3 decoder remain useful for
offline audits and reason-coded diagnostics. They cannot authorize a revert or
slash: `OptimisticChallenge.Challenge` requires v4 plus an exact registered
executable profile before dispatch, even when the owner/governance witnesses.
The deploy planner therefore never registers these legacy contracts and emits a
fail-closed warning if a custom plan includes one.

```
# Security boundary: only exact registered executable v4 is state-changing;
# v1/v2/v3 and mismatched v4 fail closed even with governance/owner witness.
```

`RegisterPermissionlessFraudVerifier` is disabled; all value-bearing challenges
require the explicit v4 profile registration above.

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
- **`IDAWriter`** — Development default: NeoFS simulation (`NeoFsLikeDAWriter`)
  or a persistent local store when `--data-dir` is set; `External` uses
  `InMemoryDAWriter` for tests/demos. Production requires
  `WithProductionBackend` with `NeoFsRestDAWriter` + `NeoFsRestDAReader`
  (or an equivalent reviewed adapter) and independent retrieval validation;
  L1 mode uses `JsonRpcL1DAWriter` with a signed-transaction confirm path.
- **L1 deposit drain** — Production: prefer
  `L2SettlementPlugin.Wire(..., depositSource:, messageRouter:, l1FinalizedHeight:, sequencerCommitteeHash:)`
  or `WireProduction(...)` with the same optional args; settlement installs the inbox on the batcher
  via `WireL1MessageInbox` before the sealed-batch sink. Lifecycle is **Drain (reserve) → durable
  seal → ConfirmConsumed**; failed persist releases reservations. Local/devnet:
  `InMemorySharedBridgeDepositSource` with the same reserve/confirm contract.
- **`ISequencerCommitteeProvider`** — Registry/source abstraction for discovering
  the desired set. Production consensus does not call an external provider during
  a dBFT round; use `SequencerCommitteeTransactionBuilder` to atomically commit the
  selected set into native consensus state.
- **`IRoundProver`** (Phase 5 only) — Default: `PassThroughRoundProver`.
  Use `MultisigRoundProver` / `MerklePathRoundProver` for non-recursive rounds;
  the bundled recursive terminal path is `bridge/neo-zkvm-gateway-{guest,host}`.
  Halo2/Risc0 remain optional alternatives.

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
- `Neo.Plugins.L2DA.InMemoryDAWriter` — the simplest possible (dev/test only)
- `Neo.Plugins.L2DA.NeoFsLikeDAWriter` — content-addressed local simulator (dev only)
- `Neo.Plugins.L2DA.NeoFsRestDAWriter` / `NeoFsRestDAReader` — production NeoFS REST path
  via `WithProductionBackend` (credentials + independent retrieval required)
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
L1:      locks asset, stores GetDeposit record, emits DepositEnqueued
                                 ↓
L2:      RpcSharedBridgeDepositSource scans DepositEnqueued + getDeposit
         materializes CrossChainMessage(DepositPayload) for L2Bridge
         BatchSealer L1 drain includes the message → DepositProcessor / native mint
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
deployed on the target L1. Advisory `GovernanceFraudVerifier` and test-only
`ExternalBridgeStubVerifier` are not part of the default deploy bundle. The `neo-hub-deploy` tool emits a deploy
bundle that names each contract, its dependencies, and the resolved hashes
after a topological sort:

```bash
# 1. Scaffold a starter plan (24 production NeoHub deploy steps in dependency
#    order, including ContractZkVerifier, Sp1Groth16Verifier, and executable-v4 fraud verifier).
dotnet run --project tools/Neo.Hub.Deploy -- scaffold \
    --output ./my-l2/deploy-plan.json

# 2. Edit the plan to fill in OWNER_REPLACE_ME / BOND_ASSET_REPLACE_ME /
#    GOVERNANCE_COUNCIL_REPLACE_ME / GOVERNANCE_THRESHOLD_REPLACE_ME
#    placeholders (canonical GAS hash on the target L1, your operator
#    multisig hash, etc.). The plan is JSON — diff-friendly + editable.

# 3. Topo-sort + resolve $step:<name> placeholders against deterministic
#    contract hashes derived from the deploy order. The output bundle is
#    what your wallet feeds to ContractManagement.Deploy in order.
dotnet run --project tools/Neo.Hub.Deploy -- plan \
    --plan ./my-l2/deploy-plan.json \
    --output ./my-l2/deploy-bundle.json

# Or run the guarded live testnet deployer. Governance is explicit M-of-N:
# 2..64 distinct compressed secp256r1 keys, threshold >= 2.
dotnet run --project tools/Neo.Hub.Deploy -- deploy-testnet \
    --rpc https://your-reviewed-n3-rpc.example \
    --expected-network <network-magic> \
    --l2-chain-id 1099 \
    --sp1-program-vkey <32-byte-raw-vkey-or-file> \
    --fraud-replay-domain <32-byte-non-zero-domain> \
    --governance-council <pubkey1,pubkey2,pubkey3> \
    --governance-threshold 2 \
    --emergency-council <separate-account-or-script-hash>
```

The live deployer rejects implicit 1-of-1 governance. It writes the exact council count and
threshold into the deployment evidence report and reads both values back during smoke checks.

The bundle's `Invocations` array is your wallet's deploy script — one
`ContractManagement.Deploy` call per entry, in order. Each entry has a
`Name`, the path to its `.nef` + `.manifest.json`, and the resolved
`DeployData` (with all `$step:<name>` placeholders replaced by the hashes
of contracts deployed earlier in the bundle).

The bundle's "PostDeployActions" section surfaces the wiring steps that
have to run AFTER all contracts are deployed (e.g.
`SequencerBond.RegisterSlasher(OptimisticChallenge)` to break the
bond↔challenge cycle, `ChainRegistry.SetGovernanceController` to enable
§16.1 admission policy, `SettlementManager.SetGovernanceController` plus the irreversible
`SettlementManager.LockGovernance` to remove hot-wallet rewiring/direct rollback,
`SettlementManager.SetMessageRouter(MessageRouter)` to close the Gateway contract-witness path,
and per-fraud-verifier informational notes
naming which contract hash to pass as the `fraudVerifier` argument to
`OptimisticChallenge.Challenge`).

After the SettlementManager lock, emergency finalized-head rollback requires an exact
`RevertBatchViaProposal(chainId,batchNumber,proposalId)` action that has reached the configured
council threshold and timelock. `OptimisticChallenge` retains only its immediate
`Challengeable`-batch fraud rollback path.

> **Note on hashes**: the bundle's per-step `Hash` fields are
> *deterministic stubs* derived from the step name (so `plan` is
> reproducible without a wallet). The actual L1 contract hashes only
> exist after your wallet calls `ContractManagement.Deploy`. The wallet
> returns each real hash; combine those four hashes with the signed genesis root in the five
> required `register-chain` flags below — NOT the stub hashes from the bundle.

After all 24 deploys + post-deploy wiring complete, capture the
**real on-chain** contract hashes and the signed genesis root into
`register-chain`. Prefer the deploy evidence report so hashes are not hand-copied:

```bash
# From a neo-hub-deploy evidence JSON (e.g. docs/audit/testnet-deployment-*-live.json):
neo-stack create-chain --chain-id 20260716 --output ./my-l2 --template zk-rollup
neo-stack bootstrap-genesis --chain-id 20260716 --output ./my-l2
# Writes data/state (RocksDB) + genesis-manifest.json with the SP1 initialStateRoot.
neo-stack register-chain --chain-id 20260716 --output ./my-l2 \
    --from-deploy-report docs/audit/testnet-deployment-20260716-live.json \
    --genesis-manifest ./my-l2/genesis-manifest.json
# Writes l1.deployed.json + Plugins/Neo.Plugins.L2Settlement/config.from-deploy.json
# and prints the canonical 91-byte configBytes hex.

# Or pass the four hashes explicitly:
neo-stack register-chain --chain-id 1099 --output ./my-l2 \
    --operator <deployer script hash or multisig> \
    --verifier <VerifierRegistry hash> \
    --bridge <SharedBridge hash> \
    --message <MessageRouter hash> \
    --genesis-state-root <non-zero root from the signed deployment manifest>
```

That emits the canonical 91-byte `configBytes` and immutable genesis root your wallet submits to
`registerChain(chainId, configBytes, genesisStateRoot)`. The root must equal the value observed
after reviewed genesis bootstrap and pinned in the signed deployment manifest; batch 1 is rejected
unless its `preStateRoot` equals this on-chain trust anchor. It cannot be changed by config updates.
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
neo-cli invoke <ChainRegistryHash> getGenesisStateRoot <chainId>
# → returns the immutable non-zero batch-1 pre-state trust anchor

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
# Build the production native executor and both prover daemons (from the repository root):
cargo build --release --locked \
    -p neo-zkvm-guest --bin neo-zkvm-executor \
    -p neo-zkvm-host -p neo-zkvm-gateway-host
# → target/release/neo-zkvm-executor + prove-batch + prove-gateway

# Record neo-zkvm-executor's SHA-256 in the signed operator release manifest.
# Linux: sha256sum target/release/neo-zkvm-executor
# macOS: shasum -a 256 target/release/neo-zkvm-executor

# Wire the sequencer to drop sealed batches into a queue dir
# (e.g. /var/lib/neo-l2/batches/) — each file is the canonical
# ProofWitnessArtifactV1 written to <batch-number>.batch.bin.

# Run the daemon (typically under systemd / k8s):
prove-batch daemon \
    --watch /var/lib/neo-l2/batches \
    --archive /var/lib/neo-l2/proven \
    --gateway-sidecars /var/lib/neo-l2/gateway-children \
    --poll-secs 5 \
    --max-queue-bytes 17179869184 \
    --max-queue-tasks 64

# Run only when Gateway aggregation is enabled. The .NET Sp1GatewayProofProver
# writes canonical request + readiness manifests into --queue.
prove-gateway daemon \
    --queue /var/lib/neo-l2/gateway-queue \
    --child-proofs /var/lib/neo-l2/gateway-children \
    --poll-ms 1000

# Equivalent supervised launch from the chain directory:
neo-stack start-prover --chain-id 1099 --output ./my-l2 \
    --prover ./my-l2/prover/prove-batch -- --poll-secs 5
```

The batch daemon polls `--watch` for `*.batch.bin`, generates a real ZK proof
for each, writes `<name>.proof.bin` (the on-chain submission artifact)
+ `<name>.proof.vk` (the verifying key, stable per guest ELF), and atomically
publishes a canonical compressed child sidecar when `--gateway-sidecars` is set.
`--watch`, `--archive`, and the sidecar directory must reside on filesystems that
support hard links; the archive path must be on the same filesystem as `--watch`.
Failures leave the input in place and log loudly so monitoring catches poison-pill batches.
Successful proof generation also retains all content-addressed evidence. Pruning starts only after
the .NET settlement pipeline has durably observed L1 settlement and published the matching
`*.proof.ack`; deletion is crash-idempotent across both watch and archive directories.

The Gateway daemon accepts only the tuple-derived sidecar filename, reconstructs the full
332-byte public inputs from the commitment plus the sidecar's two missing hashes, recursively
verifies every compressed batch proof against the compiled batch VK, and emits a host-verified
356-byte Gateway Groth16 proof with the result manifest published last. On restart, a complete
result is skipped only after exact manifest/artifact checks and terminal Groth16 re-verification;
without a result marker, only regular non-symlink orphan outputs are removed before re-proving.
The batch daemon applies the same fail-closed policy to its proof/VK/public-values triplet.

The proof-bound RPC publisher queries `MessageRouter.GetGlobalRoot*` for reconciliation but signs
and submits `SettlementManager.PublishGatewayGlobalRoot(epoch,references,globalRoot,
constituentRoot,count,backend,proofSystem,vkId,replayDomain,proof)`. `references` is the exact
strictly ordered packed list of `chainId:uint32 LE || batchNumber:uint64 LE` entries (1..4096).
SettlementManager revalidates current finality/Gateway admission, reconstructs both roots from
stored finalized records, advances non-revertible per-chain watermarks, and atomically invokes
Router. Verify post-deploy readback of both MessageRouter's SettlementManager constructor binding
and `SettlementManager.GetMessageRouter`; a direct Router call is expected to fail witness checks.

What it actually proves: the sequencer first invokes the SHA-256-pinned
`neo-zkvm-executor` with canonical `NEO4EXEC` and complete pre-state `NEO4STW1`.
That binary and the SP1 guest call the same `neo-execution-core` and vendored
`neo-vm-rs` stateful N4 V1 runtime. The native result is canonical `NEO4EXR1`; C#
validates its exact request hashes, semantic id, roots, gas, effects, complete post-state,
and public-input hash before atomic state commit. The resulting `NEO4PWIT` is then
re-executed inside SP1. The proof therefore binds HALT/FAULT behavior, gas, receipts,
storage/event effects, post-state, withdrawal/message roots, and settlement public inputs.
Tampering with any bound byte breaks native validation or proof verification.

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
