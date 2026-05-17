# ZKsync Elastic Chain ↔ neo4 component map

Neo Elastic Network borrows the *shared-bridge + chain-registry + proof-aggregation*
pattern from ZKsync's Elastic Chain (formerly "Hyperchains"). This document maps each
ZKsync component to its neo4 equivalent, calls out where the two diverge intentionally,
and tracks the gaps neo4 still needs to close.

The map is current as of ZKsync's v29 era-contracts release (Q1 2026).

For the 2026-05-17 official-doc revalidation and production-readiness verdict,
see [`docs/audit/zksync-elastic-chain-validation-2026-05-17.md`](audit/zksync-elastic-chain-validation-2026-05-17.md).

---

## Component map

| ZKsync component | neo4 equivalent | Status |
|---|---|---|
| **`Bridgehub.sol`** — chainId → ChainTypeManager registry, L1→L2 entry point | `NeoHub.ChainRegistry` + `NeoHub.MessageRouter` | parity (split into two contracts) |
| **`ChainTypeManager`** (formerly STM) — chain factory + upgrade orchestrator | `NeoHub.VerifierRegistry` + `NeoHub.GovernanceController.PrefixApprovedVerifier` | partial — no per-chain factory contract, no DiamondProxy pattern |
| **`SharedBridge`** — L1 escrow (legacy) | `NeoHub.SharedBridge` | parity |
| **`L1AssetRouter` + `L2AssetRouter`** (v24+) — chain-agnostic asset routing | absent — single `SharedBridge` does both jobs | intentionally different (one Hub) |
| **`L1/L2NativeTokenVault`** — assetId derivation, bridged-token deploy | `NeoHub.TokenRegistry` + `L2Native.L2BridgeContract` (operator-supplied mintable) | partial — no canonical bridged-NEP-17 template |
| **`L1Nullifier`** — withdrawal replay protection | `SharedBridge.PrefixWithdrawalConsumed` + `MessageRouter.PrefixConsumed` | parity |
| **`MessageRoot.sol`** — aggregated L2→L1 root across all chains | `MessageRouter.PublishGlobalRoot` (0x05 slot) + off-chain `Neo.Plugins.L2Gateway.BinaryTreeAggregator` | parity |
| **`ChainAssetHandler`** (per-asset routing rules) | absent | intentionally different (single trust model) |
| **`ValidatorTimelock`** — commit→execute delay | `NeoHub.OptimisticChallenge` + `SettlementManager.StatusChallengeable` | parity (different mechanism) |
| **Governance / `ChainAdmin` / `PermanentRestriction` / `AccessControlRestriction`** | `NeoHub.GovernanceController` (incl. `SetImmutableFlag` for permanent restrictions) | parity |
| **`TransactionFilterer`** (per-chain L1→L2 tx hook) | absent | gap (see below) |
| **`L2AdminFactory` / per-chain `ChainAdmin`** | absent — chain-admin is hub-side `operatorManager` in `ChainRegistry.L2ChainConfig` | intentionally different |
| **`BridgedStandardERC20`** — canonical L2 token | absent — operator supplies | gap (see below) |
| **Boojum / Plonk verifier contracts** | `NeoHub.{MpcCommittee,Governance,RestrictedExecution,ExternalBridgeStub}*Verifier` + pluggable via `VerifierRegistry` | parity |
| **`CalldataDA` / `ValidiumL1DAValidator` / `RollupDAManager` / `RelayedSLDAValidator`** | `NeoHub.DARegistry` + off-chain writers in `Neo.Plugins.L2DA` | partial — no on-L1 contract that verifies DA inclusion proofs (Stage-2 validium) |
| **`BytecodesSupplier` / `*Upgrade` family / `UpgradeStageValidator`** | `GovernanceController` proposal pipeline (council multisig + timelock) | partial — no staged upgrade timer (propose → notice → execute → cool-down) |
| **L2 `Bootloader`** | absent — NeoVM provides native dispatch | intentionally different |
| **L2 `ContractDeployer` / `KnownCodesStorage` / `AccountCodeStorage`** | absent — Neo's `ContractManagement` native handles this | intentionally different |
| **L2 `SystemContext`** (chainId, baseFee, blockhash) | `L2Native.L2BatchInfoContract` + `L2Native.L2SystemConfigContract` | parity |
| **L2 `L2BaseToken`** (ETH balance accounting) | NEP-17 GAS native | intentionally different |
| **L2 `L1Messenger`** (outbox) | `L2Native.L2MessageContract` | parity |
| **L2 `NonceHolder`** | absent — per-tx-signer nonce is implicit in Neo's witness model | intentionally different |
| **L2 `DefaultAccount` + `IAccount` AA** | absent | gap (see below) |
| **L2 `TestnetPaymaster` + `IPaymasterFlow`** | `L2Native.L2PaymasterContract` (top-up model only) | partial — no `approvalBased` flow selector |
| **L2 `L2InteropRootStorage` / `L2MessageVerification` (v29)** | absent on-L2; verification is L1-side in `MessageRouter` | gap (see below) |
| **L2 `L2V29Upgrade` / `ComplexUpgrader` / `L2GenesisUpgrade`** | scaffolded by `Neo.Hub.Deploy` (off-chain) but no on-chain orchestrator | partial |
| **L2 `GasBoundCaller`** | absent — NeoVM gas is per-instruction | intentionally different |
| **ZK Gateway** (settlement-layer proof aggregator) | `Neo.Plugins.L2Gateway` (off-chain) + on-chain `MessageRouter.PublishGlobalRoot` (this release) | parity |
| **Forced inclusion / priority queue** | `NeoHub.ForcedInclusion` + `Neo.L2.ForcedInclusion` | parity |
| **Sequencer staking / slashing** | `NeoHub.SequencerRegistry` + `NeoHub.SequencerBond` | parity |
| **Emergency security upgrade / instant governance** | `NeoHub.EmergencyManager` | parity |
| **Audit module (`ChainAuditor` analog)** | `Neo.L2.Audit` (6 invariant checks) | parity |
| **Foundry tests + invariant + Hardhat specs** | xUnit `tests/Neo.*.UnitTests` + `tests/Neo.L2.IntegrationTests` (E2E series); Foundry tests for `external/foreign-contracts/eth/` | parity |
| **`zksync-cli` + multi-language SDKs** | `Neo.Stack.Cli` + 6 other CLIs; `sdk/typescript`, `sdk/rust`, `src/Neo.L2.Sdk` | partial — no Go / Python SDK |
| **`code.zksync.io` tutorials + zksync-developers samples (~15+)** | `samples/contracts/{CrossChainGreeter,WithdrawalDemo}` + `samples/executors/CounterChainExecutor` | partial — only 3 sample modules |

---

## Closed in this iteration (Phase-5 + governance maturity)

### `MessageRouter.PublishGlobalRoot` / `GetGlobalRoot`

The 0x05 storage slot was previously reserved-but-unused. ZKsync's `MessageRoot.sol`
commits an aggregated message root on L1 so any L2 can prove a peer's message via
Merkle inclusion against this single anchored root.

`MessageRouter.PublishGlobalRoot(ulong batchEpoch, UInt256 globalRoot)` now writes the
root, settlement-manager-witness-gated, with publish-once-per-epoch replay protection
and a non-zero root requirement. `OnGlobalRootPublished(epoch, root)` event emitted on
each successful publish.

Off-chain `Neo.Plugins.L2Gateway.BinaryTreeAggregator` continues to perform the actual
log(N) aggregation; the new on-chain entry-point makes the result publicly auditable
and enables cross-L2 message verification against L1 directly.

### `GovernanceController.SetImmutableFlag` / `IsImmutable`

ZKsync's `PermanentRestriction` mechanism: lock certain invariants forever via a flag
that storage write-protects after the first set. Examples: "this chain can never
switch DAMode away from Rollup" or "this verifier hash is permanently retired."

Two entry points:

- `SetImmutableFlag(byte flagId)` — owner-only fast path. Idempotent. Storage is
  write-only; no `ClearImmutableFlag` exists.
- `SetImmutableFlagViaProposal(byte flagId, ulong proposalId)` — council-veto path.
  Requires `IsApprovedAndTimelocked(proposalId)`. Replay-protected per proposalId
  via the new `PrefixConsumedSetImmutable = 0x0E` slot.

`IsImmutable(byte flagId)` is the `[Safe]` reader. `OnImmutableFlagSet(flagId)` event
emitted on first set only (idempotent re-sets are silent).

---

## Open gaps tracked for future iterations

### Gap 1 — No `TransactionFilterer` hook per chain

ZKsync lets each chain plug a contract that vets every L1→L2 priority tx (compliance,
KYC, anti-spam). neo4 currently lets all L1→L2 messages through.

**Recommendation:** Add an optional `IL1TxFilter` extension point in
`ChainRegistry.L2ChainConfig` (one more 20-byte slot in the 91-byte blob), checked
by `MessageRouter` before enqueuing. Tracked in [`TASKS.md`](../TASKS.md).

### Gap 2 — No canonical bridged-NEP-17 template

ZKsync ships `BridgedStandardERC20.sol` so wrapped L1 tokens have a guaranteed
shape. neo4 mappings in `L2BridgeContract.PrefixMapping` rely on operators providing
their own L2 mintable token, with no standard.

**Recommendation:** Publish `L2Native.BridgedNep17Contract` used by default;
`TokenRegistry` deploys it deterministically when registering a new asset.

### Gap 3 — Validium Stage-2 missing (no L1 DA inclusion-verifier contract)

`Neo.Plugins.L2DA` has `JsonRpcL1DAWriter` + `CommitteeAttestedDAWriter` but no L1
contract that *verifies inclusion* the way ZKsync's `ValidiumL1DAValidator` does.
Today neo4 trusts the committee blindly.

**Recommendation:** Add `NeoHub.DAValidator` that consumes committee signatures or
a ZK inclusion proof. Called from `SettlementManager.FinalizeBatch` before
finalization for `DAMode.Committee`.

### Gap 4 — No staged upgrade timer

`GovernanceController` has a timelock but no separation of *propose → notice →
execute → cool-down* like ZKsync's `UpgradeStageValidator`.

**Recommendation:** Add a notice window in `PrefixProposal` (proposed-at, notice-end,
execute-end) so council members and downstream contracts see the upgrade coming
before it can fire.

### Gap 5 — No `IAccount`-style programmable AA at L2

Even without EraVM, the AA *pattern* — validate / pay / execute hooks — is portable
and pairs naturally with `L2Native.L2PaymasterContract`. Today only the top-up
sponsor model is available.

**Recommendation:** Add `L2Native.L2AccountAbstraction` with a validate-hook spec
+ magic-value return, mirroring ZKsync's `IAccount` ABI as closely as Neo's signer
model allows.

### Gap 6 — L2-side message verification

ZKsync's v29 release added `L2InteropRootStorage` + `L2MessageVerification`,
letting an L2 verify another L2's messages directly without round-tripping L1.
neo4 verifies all cross-chain messages via L1 today.

**Recommendation:** Once the off-chain Gateway aggregation matures, add an
L2-side helper that reads the L1-committed `MessageRouter.GetGlobalRoot` via the
`L2BatchInfoContract` and verifies inclusion locally.

### Gap 7 — Sample coverage is thin

ZKsync's `code.zksync.io` ships ~15+ tutorials (multisig AA, paymaster ERC20,
gated NFT mint, L1→L2 deposit). neo4 has 3 sample modules total.

**Recommendation:** Add at least `Sample.Erc20PaymasterClient`,
`Sample.MultisigAccount`, `Sample.GatedMint`, `Sample.CrossChainSwap`.

### Gap 8 — No Python / Go SDK

`sdk/rust` and `sdk/typescript` mirror the 10 RPC methods, but ZKsync ships
`zksync2-go` and `zksync2-python` (high indexer / exchange demand).

**Recommendation:** Generate community-tier SDKs from the same `L2RpcClient.cs`
surface.

---

## Intentional divergences (not gaps)

These exist in ZKsync because of EVM/EraVM peculiarities. NeoVM's design either
makes them moot or provides native equivalents:

- **EraVM L2 system contracts** (`Bootloader`, `ContractDeployer`, `KnownCodesStorage`,
  `AccountCodeStorage`, `NonceHolder`, `MsgValueSimulator`, `Compressor`,
  `EvmEmulator.yul`, `EvmGasManager.yul`, `EventWriter.yul`, precompiles like
  `EcAdd.yul` / `EcPairing.yul` / `Modexp.yul` / `P256Verify.yul`) — NeoVM provides
  `ContractManagement`, native NEP-17 GAS, native cryptography, and implicit signer
  nonces.
- **Diamond proxy + facet pattern** (`DiamondProxy.sol`, `Admin.sol`, `Executor.sol`,
  `Getters.sol`, `Mailbox.sol`) — exists to work around Ethereum's 24KB contract
  size limit. NeoVM has no 24KB bound; the per-concern split in NeoHub's 21
  contracts is equivalent in effect.
- **`CTMDeploymentTracker` + `ChainAssetHandler`** — ZKsync needs these to support
  *competing* chain types and asset routers run by third parties. neo4 has one
  canonical Hub.
- **`L2BaseToken` + `L2WrappedBaseToken` / `L2WrappedBaseTokenStore`** — solves the
  ETH/WETH non-NEP-17 unwrap dance. GAS is already NEP-17 in Neo.
- **`GasBoundCaller`** — EraVM gas semantics differ from EVM. NeoVM gas is
  per-instruction and deterministic.
- **`zksolc` / `zkvyper` compilers** — neo4 reuses upstream `neo-devpack-dotnet` +
  `Neo.SmartContract.Framework`; no need for an EVM-to-zkVM transpiler.
- **Per-chain `ChainAdmin` contract** (`ChainAdmin.sol`, `ChainAdminOwnable.sol`,
  `L2AdminFactory.sol`) — neo4's chain-admin surface is the per-chain `operatorManager`
  in `ChainRegistry.L2ChainConfig` + sequencer/verifier records.
- **`EvmEmulator` / `EvmPredeploysManager`** — ZKsync runs unmodified EVM bytecode
  inside EraVM for migration ease. neo4's audience is Neo developers.
- **`Airbender` / Boojum-specific verifier crates** — neo4 already vendors
  `external/neo-zkvm` (Neo VM in pure Rust) + SP1 prover. Adopting Airbender wire
  formats would mean refactoring without a pulling audience.

---

## See also

- [`ARCHITECTURE.md`](../ARCHITECTURE.md) — neo4's own layered architecture
- [`docs/architecture-l1-vs-l2.md`](architecture-l1-vs-l2.md) — concern-by-concern
  L1/L2 split
- [`docs/spec-gap-plan.md`](spec-gap-plan.md) — full gap-tracking against `doc.md`
- [`TASKS.md`](../TASKS.md) — actionable checklist by repo (core / this-repo /
  cross-repo)
