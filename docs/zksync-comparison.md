# ZKsync Elastic Chain ↔ neo4 component map

Neo Elastic Network borrows the *shared-bridge + chain-registry + proof-aggregation*
pattern from ZKsync's Elastic Chain (formerly "Hyperchains"). This document maps each
ZKsync component to its neo4 equivalent, calls out where the two diverge intentionally,
and tracks the gaps neo4 still needs to close.

The map is current as of ZKsync's v29 era-contracts release (Q1 2026).

For the latest 2026-05-18 official-doc revalidation and production-readiness verdict,
see [`docs/audit/zksync-elastic-chain-validation-2026-05-18.md`](audit/zksync-elastic-chain-validation-2026-05-18.md).

---

## Component map

| ZKsync component | neo4 equivalent | Status |
|---|---|---|
| **`Bridgehub.sol`** — chainId → ChainTypeManager registry, L1→L2 entry point | `NeoHub.ChainRegistry` + `NeoHub.MessageRouter` | parity (split into two contracts) |
| **`ChainTypeManager`** (formerly STM) — chain factory + upgrade orchestrator | `NeoHub.VerifierRegistry` + `NeoHub.GovernanceController` staged proposal windows | partial — no per-chain factory contract or DiamondProxy pattern |
| **`SharedBridge`** — L1 escrow (legacy) | `NeoHub.SharedBridge` | parity |
| **`L1AssetRouter` + `L2AssetRouter`** (v24+) — chain-agnostic asset routing | absent — single `SharedBridge` does both jobs | intentionally different (one Hub) |
| **`L1/L2NativeTokenVault`** — assetId derivation, bridged-token deploy | `NeoHub.TokenRegistry` + Neo Core native `L2BridgeContract` + native `BridgedNep17Contract` | parity for N4's design — L2 token accounting lives in the core native layer, not operator-deployed templates |
| **`L1Nullifier`** — withdrawal replay protection | `SharedBridge.PrefixWithdrawalConsumed` + `MessageRouter.PrefixConsumed` | parity |
| **`MessageRoot.sol`** — aggregated L2→L1 root across all chains | `MessageRouter.PublishGlobalRoot` (0x05 slot) + off-chain `Neo.Plugins.L2Gateway.BinaryTreeAggregator` | parity |
| **`ChainAssetHandler`** (per-asset routing rules) | absent | intentionally different (single trust model) |
| **`ValidatorTimelock`** — commit→execute delay | `NeoHub.OptimisticChallenge` + `SettlementManager.StatusChallengeable` | parity (different mechanism) |
| **Governance / `ChainAdmin` / `PermanentRestriction` / `AccessControlRestriction`** | `NeoHub.GovernanceController` (immutable flags + staged proposal windows) | parity for shared governance; no ZKsync-style per-chain admin factory |
| **`TransactionFilterer`** (per-chain L1→L2 tx hook) | `MessageRouter.SetL1TxFilter` + `NeoHub.L1TxFilter` | parity for L1→L2 enqueue filtering; L2 mempool filtering remains operator-specific |
| **`L2AdminFactory` / per-chain `ChainAdmin`** | absent — chain-admin is hub-side `operatorManager` in `ChainRegistry.L2ChainConfig` | intentionally different |
| **`BridgedStandardERC20`** — canonical L2 token | Neo Core native `BridgedNep17Contract` | parity at the canonical bridged-token level |
| **Boojum / Plonk verifier contracts** | `NeoHub.{MpcCommittee,Governance,RestrictedExecution,ExternalBridgeStub}*Verifier` + pluggable via `VerifierRegistry` | parity |
| **`CalldataDA` / `ValidiumL1DAValidator` / `RollupDAManager` / `RelayedSLDAValidator`** | `NeoHub.DARegistry` + `NeoHub.DAValidator` + off-chain writers in `Neo.Plugins.L2DA` | partial — DAC attestation gate exists; richer NeoFS/external inclusion adapters remain operator-specific |
| **`BytecodesSupplier` / `*Upgrade` family / `UpgradeStageValidator`** | `GovernanceController` proposal pipeline with notice/execution/cooldown windows | parity for staged timing; no bytecode supplier because NeoVM uses ContractManagement |
| **L2 `Bootloader`** | absent — NeoVM provides native dispatch | intentionally different |
| **L2 `ContractDeployer` / `KnownCodesStorage` / `AccountCodeStorage`** | absent — Neo's `ContractManagement` native handles this | intentionally different |
| **L2 `SystemContext`** (chainId, baseFee, blockhash) | Neo Core native `L2BatchInfoContract` + `L2SystemConfigContract` | parity |
| **L2 `L2BaseToken`** (ETH balance accounting) | NEP-17 GAS native | intentionally different |
| **L2 `L1Messenger`** (outbox) | Neo Core native `L2MessageContract` | parity |
| **L2 `NonceHolder`** | absent — per-tx-signer nonce is implicit in Neo's witness model | intentionally different |
| **L2 `DefaultAccount` + `IAccount` AA** | Neo Core native `L2AccountAbstraction` | partial — validate/execute/paymaster hooks exist; not protocol-native like EraVM |
| **L2 `TestnetPaymaster` + `IPaymasterFlow`** | Neo Core native `L2PaymasterContract` (top-up model only) | partial — no `approvalBased` flow selector |
| **L2 `L2InteropRootStorage` / `L2MessageVerification` (v29)** | Neo Core native `L2InteropVerifier` mirrors global roots and verifies Merkle inclusion locally | parity at helper-contract level |
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

## Closed in this iteration

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

### `NeoHub.DAValidator` + `SettlementManager` DA gate

`SettlementManager.SubmitBatch` now records each batch's DA commitment and active
`DAMode` into `DARegistry`. `FinalizeBatch` calls `DAValidator.Validate` before
canonicalizing a batch. DAC mode requires a prior M-of-N secp256r1 committee
attestation submitted through `DAValidator.SubmitAttestation`.

### Neo Core native `BridgedNep17Contract`

The repo now ships a canonical mint/burn NEP-17 template for bridged L1 assets.
`L2BridgeContract.ApplyDeposit` can mint it and `InitiateWithdrawal` can burn it
using the existing bridge-only `mint` / `burn` ABI.

### `GovernanceController` staged upgrade windows

Governance now exposes explicit notice, execution, and cool-down windows through
`SetUpgradeWindows`, `GetProposalStage`, `IsInExecutionWindow`, and
`MarkProposalExecuted`. Existing `IsApprovedAndTimelocked` remains for backwards
compatibility.

### `MessageRouter.SetL1TxFilter` + `NeoHub.L1TxFilter`

`MessageRouter.EnqueueL1ToL2` now checks an optional per-chain read-only filter
before allocating the nonce or writing the message. The default filter supports
sender, receiver, message-type, default allow/deny, and payload-size policy.

### Neo Core native `L2AccountAbstraction`

The L2 now has a programmable AA entry contract with per-account validator and
paymaster binding, nonce checks, `validateTx`, `validateTransaction` magic return,
`executeTx`, and system-account nonce consumption.

### Neo Core native `L2InteropVerifier`

L2 dApps can now verify peer-chain messages locally against mirrored Gateway
global roots. The helper stores publish-once roots, checks the mirrored L1
finalized height through `L2BatchInfoContract`, verifies Neo-style Hash256 Merkle
proofs, and provides local replay protection.

---

## Open gaps tracked for future iterations

### Gap 1 — Sample coverage is thin

ZKsync's `code.zksync.io` ships ~15+ tutorials (multisig AA, paymaster ERC20,
gated NFT mint, L1→L2 deposit). neo4 has 3 sample modules total.

**Recommendation:** Add at least `Sample.Erc20PaymasterClient`,
`Sample.MultisigAccount`, `Sample.GatedMint`, `Sample.CrossChainSwap`.

### Gap 2 — No Python / Go SDK

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
  size limit. NeoVM has no 24KB bound; the per-concern split in NeoHub's 23
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
