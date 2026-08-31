# Sample L2 chains + custom chain logic

End-to-end example chain configs that exercise the four Neo Elastic Network
templates against four distinct use cases, plus a runnable reference custom
transaction executor showing how an operator brings their own chain logic to
the framework.

Each `*.config.json` is the same shape `neo-stack create-chain` writes — drop
into the devnet runner via `--config <path>` to preview the §16.2 security
label end-to-end before deploying to L1.

## The four samples

| Sample | Template | chainId | Use case | Distinguishing parameters |
|--------|----------|--------:|----------|---------------------------|
| [`general-rollup`](./general-rollup.config.json) | `rollup` | 1100 | General-purpose Neo L2 (DeFi, dApp hosting) — the "safe default" | SecurityLevel=Optimistic, proofType=Zk, daMode=NeoFS, sequencer=DbftCommittee, exit=Delayed |
| [`gaming-rollup`](./gaming-rollup.config.json) | `rollup` | 1200 | High-frequency gaming chain (frequent state updates, low-value txs) | sequencer=Centralized for sub-second seal, proofType=Zk, daMode=NeoFS |
| [`exchange-validium`](./exchange-validium.config.json) | `validium` | 1300 | DEX / orderbook / matching engine — ZK validity + off-chain DA | SecurityLevel=Validium, daMode=NeoFS, exit=Delayed, gateway=true |
| [`privacy-sidechain`](./privacy-sidechain.config.json) | `sidechain` | 1400 | Permissioned enterprise / privacy chain — minimal L1 footprint | SecurityLevel=Sidechain, proofType=Multisig, exit=Permissionless |

## Running a sample through the devnet

```bash
# Preview the gaming chain end-to-end (5 batches, RPC snapshot at the bottom).
dotnet run --project tools/Neo.L2.Devnet -- 5 \
    --config samples/gaming-rollup.config.json

# Look for the post-run RPC snapshot's getsecuritylabel line — it should match
# the sample's §16.2 dimensions:
#   getsecuritylabel: securityLevel=Optimistic daMode=NeoFS
#                     sequencer=Centralized exit=Delayed gateway=False
```

A `--config` run routes the sample's declared `proofType` through the shared
`ProofRouting` table (`src/Neo.L2.Abstractions`): an incompatible
`securityLevel`/`proofType` pair aborts the run with exit code 2, and the
matching off-chain prover is wired — committee attestations for `Multisig`, a
sequencer-signed optimistic payload for `Optimistic`, and `MockRiscVProver`
for `Zk` (a preview stand-in; production Zk proofs are produced out-of-process
by `bridge/neo-zkvm-host`). What the devnet still does not do is prove that a
route exists on L1: `neo-stack validate <config>` checks the declared
`proofType` against `SettlementManager.IsProofTypeCompatible` and against the
routes the production bundle freezes.

Each sample includes the `template`, `chainMode`, `vm`, and the §16.2 label
dimensions. The four UInt160 hashes (`operator`, `verifier`, `bridgeAdapter`,
`messageAdapter`) get resolved at deploy time from the
`neo-hub-deploy plan` output — they're not in the template JSON because they
depend on which L1 the operator is targeting.

## When to start from each

**`general-rollup`** is the default. Settlement is an SP1 validity proof, so a
batch is final once the proof verifies — no honest-challenger assumption and no
window to wait out. It still advertises `securityLevel=Optimistic`, which is the
floor the chain promises; `proofType=Zk` over-delivers on that floor and is the
only pairing the shipped production hub can both accept and verify. NeoFS is
the canonical N4 data-availability tier: batches stay Neo-native, retrievable,
and content-addressed without pushing every byte through L1. Pick this unless
one of the others specifically applies.

**`gaming-rollup`** trades off: centralized sequencer (faster seal cadence,
no committee round-trip) while still using NeoFS DA.
Good for a gaming loop where state updates are too frequent to amortize
against L1 fees and the asset-loss radius is low. `permissionlessExit` stays
true so users can always escape if the centralized sequencer goes rogue.

**`exchange-validium`** uses ZK validity (no challenge window — finalization
is the proof) + NeoFS off-chain DA (cheap + retrievable + Neo-native) +
delayed exit (DEX operator gets a window to drain orderbook on shutdown
without users front-running them). Gateway-enabled so the chain participates
in Phase-5 cross-L2 messaging — DEX users can move assets between this and
other Elastic Network L2s without waiting on L1.

**`privacy-sidechain`** is the lightest-touch variant: SidechainMode
+ proofType=Multisig + permissionlessExit. Useful for permissioned consortia
or enterprise networks where the L1 anchor isn't a trust anchor — it's just
a discovery + asset-bridge endpoint. Settlement is a committee attestation
rather than a validity proof. `proofType=None` is not available at any layer:
`SettlementManager.IsProofTypeCompatible` has no None row, `VerifierRegistry`
refuses to register a route for byte 0, and the batcher cannot build a None
proof artifact. Caveat: the shipped production bundle registers only the Zk
route before locking `VerifierRegistry`, so a sidechain that must settle
batches on such a hub needs a Multisig verifier registered before that lock.

## Custom chain logic — `executors/`

[`samples/executors/`](./executors/) is the directory the
`neo-stack scaffold-executor` helper writes to by default + the home of the
working reference sample. See [`executors/README.md`](./executors/README.md)
for the full overview of "scaffold your own" + "see it run end-to-end."

[`samples/executors/Sample.CounterChainExecutor`](./executors/Sample.CounterChainExecutor)
is a runnable, fully-tested reference for the
`Neo.L2.Executor.ITransactionExecutor` seam — the framework's plug-in point
for "what happens when a transaction lands on this L2." The reference handles
three opcodes: `IncrementCounter` (per-sender u64 counter), `EmitWithdrawal`
(L2→L1 with replay-protected nonce), and `EmitMessage` (L2→L2 via canonical
`MessageBuilder.Build`).

### What the sample shows

- **Custom transaction wire format** — opcode byte + opcode-specific body,
  decoded straight from `ReadOnlyMemory<byte>`. No need to inherit from a
  framework base class.
- **Determinism contract** — receipts are derivable from
  `(serializedTx, batchContext, preStateRoot)` alone. No clock reads, no
  RNG, no I/O. The `Execute_Determinism_SameInputSameOutput` test pins this.
- **Failed-receipt path** — malformed transactions produce
  `Receipt.Success = false` instead of crashing the batch. The
  `ReferenceBatchExecutor` requires this so one bad tx can't take down the
  whole batch's proving pipeline.
- **State seam** — the executor takes an `ICounterChainState` interface so
  tests inject `InMemoryCounterChainState` and production wires
  `Neo.L2.Executor.State.KeyedStateStore`.
- **Withdrawal + message emission** — withdrawals build a `WithdrawalRequest`
  with txHash-derived nonces; messages route through `MessageBuilder.Build`
  to inherit the canonical hash composition and self-routed-rejection.
- **Per-opcode gas schedule** — fixed gas per opcode keeps `GasConsumed`
  reproducible by any verifier (each opcode declares a const).

### How to fork it for your own chain

1. Copy `samples/executors/Sample.CounterChainExecutor/` to
   `your-org/MyChainExecutor/`.
2. Replace the three opcodes + their decoders with your chain's transaction
   types. Keep the opcode-byte + opcode-specific-body shape (or define your
   own — only `ITransactionExecutor.ExecuteAsync` is the contract).
3. Replace `ICounterChainState` with your own state-mutation seam, or wire
   directly to `KeyedStateStore` if that's enough.
4. Hand the executor to `ReferenceBatchExecutor.WithExecutor(yourExec)` —
   the rest of the pipeline (sealing, proving, settlement, fraud-proof) is
   already wired by the Neo Elastic Network plug-ins.
5. Mirror the test shape in `tests/Sample.CounterChainExecutor.UnitTests/`:
   per-opcode happy path + edge cases + determinism pin + mixed-batch smoke.

## See also

- [`contracts/`](./contracts/README.md) — sample L2-aware app contracts
  (`Sample.CrossChainGreeter`, `Sample.WithdrawalDemo`) showing standard
  patterns for integrating with N4 L2 native system contracts.

## Reference

- Template defaults: [`tools/Neo.Stack.Cli/Commands/CreateChainCommand.cs`](../tools/Neo.Stack.Cli/Commands/CreateChainCommand.cs)
- Devnet `--config` parser: [`tools/Neo.L2.Devnet/Program.cs`](../tools/Neo.L2.Devnet/Program.cs) (`ReadLabelOverrides`)
- Custom chain logic: [`docs/launching-an-l2.md`](../docs/launching-an-l2.md)
- Tech-stack coverage: [`docs/tech-stack-coverage.md`](../docs/tech-stack-coverage.md)
- Spec: [`doc.md`](../doc.md) §6 (chain modes), §12 (DA tiers), §16.2 (security label)
