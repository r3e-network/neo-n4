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

# Or preview your operator-template config end-to-end (the post-run RPC
# snapshot's getsecuritylabel will reflect the JSON's §16.2 dimensions):
dotnet run --project tools/Neo.L2.Devnet -- 5 --config ./my-l2/chain.config.json

# Sanity-check the JSON before running anything against L1:
neo-stack validate ./my-l2/chain.config.json
# ✅ valid: chainId=1099 securityLevel=Optimistic daMode=L1 ...
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

Two paths:

  1. **Governance-arbitration mode** (default operator-friendly path):
     deploy `NeoHub.GovernanceFraudVerifier` (it's in the default
     `neo-hub-deploy plan` output as the 14th NeoHub contract). It does
     a structural check of the canonical `FraudProofPayload` (v1=101 bytes
     fixed or v2=105+N bytes with disputed-tx witness; length / version /
     claims-a-real-discrepancy) and emits accept/reject events for the
     security council to arbitrate. Pass its deployed hash as
     `fraudVerifier` when filing a challenge.
  2. **Trustless re-execution mode**: ship your own fraud verifier that
     re-executes the disputed transaction on L1. The `FraudProofPayload`
     v2 wire format (`DisputedTxBytes` field) carries the disputed tx
     bytes the verifier needs to re-execute. **Still missing**: the
     storage-read + storage-write manifests with Merkle proofs against
     the pre / post state roots — that's a v3 wire format extension
     tracked in `IMPLEMENTATION_STATUS.md`. A re-execution verifier today
     must supply that data through its own side-channel (e.g. operator-
     run state proof server) or wait for v3. Operators choosing this
     path skip `GovernanceFraudVerifier` from the deploy bundle and
     register their own verifier's hash.

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
| `ITransactionExecutor`                       | `ReferenceTransactionExecutor`    | Domain-specific opcodes — `neo-stack scaffold-executor` emits a starter, [`Sample.CounterChainExecutor`](../samples/executors/Sample.CounterChainExecutor) is the working reference |
| `IL2Prover` / `IL2ProofVerifier`             | Multisig / Optimistic / Mock-RiscV| Phase 4: SP1 prover via `Sp1RiscVProver`     |
| `IDAWriter`                                  | InMemory / NeoFsLike / Persistent | Real NeoFS SDK / L1 sendrawtransaction       |
| `ISequencerCommitteeProvider`                | `InMemorySequencerCommitteeProvider`| Wire to neo's `DBFTPlugin` consensus selector|
| `IRoundProver` (Phase 5 only)                | `PassThroughRoundProver`          | SP1 Compress / Halo2 accumulator / Risc0 fold|

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
//    For a NeoVM-backed real chain, wrap ApplicationEngine instead.
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

Wire it through whatever component owns the sequencer reference (typically
the L2 node's consensus selector — the existing `InMemorySequencerCommitteeProvider`
in `Neo.L2.Sequencer` shows the production-ready persistence + lifecycle
pattern your custom provider can follow if you also need restart-survival).

The L2 node's dBFT plugin polls this interface before each round, so switching
the provider is the only on-chain-visible step needed to swap sequencer models —
NeoHub's `SequencerRegistry` continues to track *who registered* but the
*selection policy* is the L2's call.

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

The reference is `Neo.L2.Proving.Sp1.Sp1RiscVProver` / `Sp1RiscVVerifier` — same
shape, with a `MockRiscVProver` fallback when the native bridge isn't loaded
(see the `Sp1Bridge.IsAvailable` gate). Wire the verifier into the chain's
boot sequence; register the matching on-chain verifier contract via
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
