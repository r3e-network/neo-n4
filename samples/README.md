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
| [`general-rollup`](./general-rollup.config.json) | `rollup` | 1100 | General-purpose Neo L2 (DeFi, dApp hosting) — the "safe default" | SecurityLevel=Optimistic, daMode=L1, sequencer=DbftCommittee, exit=Delayed |
| [`gaming-rollup`](./gaming-rollup.config.json) | `rollup` | 1200 | High-frequency gaming chain (frequent state updates, low-value txs) | sequencer=Centralized for sub-second seal, daMode=External (cheap blob DA) |
| [`exchange-validium`](./exchange-validium.config.json) | `validium` | 1300 | DEX / orderbook / matching engine — ZK validity + off-chain DA | SecurityLevel=Validium, daMode=NeoFS, exit=Delayed, gateway=true |
| [`privacy-sidechain`](./privacy-sidechain.config.json) | `sidechain` | 1400 | Permissioned enterprise / privacy chain — minimal L1 footprint | SecurityLevel=Sidechain, proofType=None, exit=Permissionless |

## Running a sample through the devnet

```bash
# Preview the gaming chain end-to-end (5 batches, RPC snapshot at the bottom).
dotnet run --project tools/Neo.L2.Devnet -- 5 \
    --config samples/gaming-rollup.config.json

# Look for the post-run RPC snapshot's getsecuritylabel line — it should match
# the sample's §16.2 dimensions:
#   getsecuritylabel: securityLevel=Optimistic daMode=External
#                     sequencer=Centralized exit=Delayed gateway=False
```

Each sample includes the `template`, `chainMode`, `vm`, and the §16.2 label
dimensions. The four UInt160 hashes (`operator`, `verifier`, `bridgeAdapter`,
`messageAdapter`) get resolved at deploy time from the
`neo-hub-deploy plan` output — they're not in the template JSON because they
depend on which L1 the operator is targeting.

## When to start from each

**`general-rollup`** is the default. Inherits the Optimistic challenge window
(§17 mitigation #2) so a faulty proof is contestable. L1 DA matches the
strongest data-availability tier; everyone can independently re-derive the
state by replaying batches from L1. Pick this unless one of the others
specifically applies.

**`gaming-rollup`** trades off: centralized sequencer (faster seal cadence,
no committee round-trip) + External DA (cheaper than L1, slightly weaker).
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
+ proofType=None + permissionlessExit. Useful for permissioned consortia
or enterprise networks where the L1 anchor isn't a trust anchor — it's just
a discovery + asset-bridge endpoint. No prover plugin needed; settlement
happens via attestation alone.

## Custom chain logic — `executors/Sample.CounterChainExecutor`

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
  patterns for integrating with the `L2Native.*` system contracts.

## Reference

- Template defaults: [`tools/Neo.Stack.Cli/Commands/CreateChainCommand.cs`](../tools/Neo.Stack.Cli/Commands/CreateChainCommand.cs)
- Devnet `--config` parser: [`tools/Neo.L2.Devnet/Program.cs`](../tools/Neo.L2.Devnet/Program.cs) (`ReadLabelOverrides`)
- Custom chain logic: [`docs/launching-an-l2.md`](../docs/launching-an-l2.md)
- Tech-stack coverage: [`docs/tech-stack-coverage.md`](../docs/tech-stack-coverage.md)
- Spec: [`doc.md`](../doc.md) §6 (chain modes), §12 (DA tiers), §16.2 (security label)
