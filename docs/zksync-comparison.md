# ZKsync Elastic Chain ↔ neo4 component map

Neo Elastic Network borrows the *shared-bridge + chain-registry + proof-aggregation*
pattern from ZKsync's Elastic Chain (formerly "Hyperchains"). This document maps each
ZKsync component to its neo4 equivalent, calls out where the two diverge intentionally,
and tracks the gaps neo4 still needs to close.

The map is current as of ZKsync's v29 era-contracts release (Q1 2026).

For the latest 2026-05-18 official-doc revalidation and production-readiness verdict,
see [`docs/audit/zksync-elastic-chain-validation-2026-05-18.md`](audit/zksync-elastic-chain-validation-2026-05-18.md).

---

## Neo-native 1:1 replica policy

In this repository, "1:1 with ZKsync Elastic Chain" means: preserve the
**component role**, **security invariant**, **operator workflow**, and **user-facing
contract**, while substituting the substrate-specific parts that are inseparable from
Ethereum / EVM / EraVM. It does **not** mean copying Solidity bytecode, EraVM system
contracts, Boojum circuit layouts, or Ethereum gas/accounting semantics verbatim.

The official ZKsync Gateway docs define Gateway as an optional shared proof aggregation
layer: chains remain anchored to Ethereum, assets stay locked on Ethereum, and Gateway
does not become a general custody / execution layer. The Neo equivalent keeps that
invariant but swaps the root layer from Ethereum to Neo L1: assets are locked in
NeoHub, global roots are anchored by NeoHub contracts, and Gateway remains middleware.
Reference docs: [Gateway overview](https://docs.zksync.io/zksync-protocol/gateway),
[Gateway features](https://docs.zksync.io/zksync-protocol/gateway/features), and
[shared bridges](https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/shared-bridges).

| ZKsync Elastic Chain invariant | Neo-native replacement | Acceptance gate |
|---|---|---|
| Ethereum is the root of trust and final verifier | Neo L1 / NeoHub is the root of trust and final verifier | Batch roots, message roots, withdrawals, and emergency exits are anchored by NeoHub contracts; Gateway never becomes the custody layer |
| Bridgehub is the chain registry + L1 entry point | `NeoHub.ChainRegistry` + `SettlementManager` + `MessageRouter` | Chain registration, settlement, L1→L2 messages, and global roots are all reachable from NeoHub |
| Chain Type Manager shares verifier / upgrade policy for a chain family | `VerifierRegistry` + `GovernanceController` + proof/DA mode in `L2ChainConfig` | Chains of the same Neo-native type share verifier policy, staged upgrades, immutable flags, and DA gates |
| Shared Bridge provides canonical ecosystem liquidity | `NeoHub.SharedBridge` + `TokenRegistry` + L2 native `L2BridgeContract` / `BridgedNep17Contract` | One canonical bridged representation per asset; replay protection via withdrawal/message nullifiers |
| Gateway is optional proof aggregation middleware | `Neo.Plugins.L2Gateway` + `SettlementManager.PublishGatewayGlobalRoot` → `MessageRouter.PublishGlobalRoot` | Chains can use direct NeoHub settlement or an atomic, finalized-constituent-bound Gateway path without moving asset custody to Gateway |
| Rollup / validium DA choices are explicit | `DARegistry` + `DAValidator` + NeoFS / L1 / DAC writers | Batch finalization checks the active DA policy; NeoFS is the default Neo-native external DA layer |
| Forced inclusion protects users from sequencer censorship | `NeoHub.ForcedInclusion` + `SequencerBond` + `ChainRegistry` pauser wiring | Overdue forced txs can trigger at-most-once report, slashing, and chain pause when production wiring is enabled |
| L2 system contracts expose bridge, messaging, fee, AA, and interop primitives | Neo core native L2 contracts under `external/neo` | Neo-native contracts expose equivalent primitives without EraVM bytecode/deployer/nonce-holder machinery |
| ZK validity proof is the trustless settlement target | RISC-V/NeoVM execution receipt + SP1/Neo zkVM proof boundary + `ContractZkVerifier` route | Production ZK chains must register a real verifier and permanently disable `envelope-only`; devnet-only shortcuts must stay explicit |
| `zkstack` / `zksync-cli` make the operator flow reproducible | `neo-stack`, `neo-hub-deploy`, SDKs, devnet runner | Operators get deterministic config bytes, deploy plans, post-deploy wiring checks, smoke tests, and wallet-owned signing |

### Direct-copy boundary

The following ZKsync ideas should be copied as closely as Neo permits:

- shared bridge liquidity model and replay/nullifier discipline;
- Bridgehub / CTM / Gateway topology;
- explicit rollup-vs-validium DA selection;
- global message-root aggregation for L2↔L2 interop;
- staged governance, immutable restrictions, and emergency path separation;
- forced inclusion with sequencer accountability;
- operator CLI ergonomics and reproducible deployment plans.

The following must remain Neo-native substitutions, not direct copies:

- **EraVM / EVM system contracts** → NeoVM2/RISC-V runtime plus Neo core native L2 contracts;
- **Boojum / Airbender circuit stack** → Neo execution proof adapter (currently SP1 over
  the vendored Neo zkVM path, with RISC-V execution receipts as the N4 target);
- **Ethereum ETH/ERC20 accounting** → GAS / NEO / NEP-17 accounting and UInt160 addresses;
- **calldata-centric DA** → NeoFS / L1 / DAC DA modes;
- **Solidity Diamond/facet upgrade mechanics** → deployed NeoHub contracts plus
  `GovernanceController` staged upgrade windows.

This is the architectural contract for "ZKsync Elastic Chain, but for Neo": maximum
parity at the protocol boundary, deliberate divergence only where Ethereum-specific
mechanics would be incorrect or inefficient on Neo.

---

## L1 trust model (read this first)

The property that *defines* ZKsync's Elastic Chain is that L1 contracts verify a
**validity proof** (a Boojum/Plonk SNARK) for every settled batch, so the L1 never has to
trust the sequencer. neo4 is **topologically** aligned with the Elastic Chain — shared
bridge, chain registry, aggregated message root, DA gate, forced inclusion, escape hatch —
and now ships an in-repo SP1 Groth16/BN254 terminal verifier. What an L1 batch settlement
actually trusts depends on the `ProofType` the chain is configured for:

- **`ProofType.Multisig` (Stage 0)** — the L1 trusts a registered secp256r1 committee
  (`MpcCommitteeVerifier`). The signature checks are real and fully on-chain; security is an
  honest-majority assumption on the committee.
- **`ProofType.Optimistic` (Stage 1)** — validity is *assumed* and the L1 relies on a
  fraud-proof challenge window (`OptimisticChallenge`). This is an **optimistic-rollup
  divergence** from ZKsync, which is a pure validity rollup. V1/v2/v3 remain
  governance-arbitrated structural evidence. The separate v4 profile binds the committed
  batch and executes exactly one existing-key Counter Increment; general NeoVM and
  multi-transaction fraud proofs fail closed.
- **`ProofType.Zk` (Stage 2)** — `ContractZkVerifier` validates the canonical batch/proof
  envelope and routes SP1 proofs to the in-repo immutable `Sp1Groth16Verifier`, which executes
  the complete pinned SP1 Groth16/BN254 pairing equation through Neo Core native interops.
  The production plan registers the exact program VK and permanently disables SP1
  `envelope-only` before exposing the ZK settlement route. Explicit envelope-only mode remains
  available only for private devnets and proof systems whose terminal verifier is not yet wired.

In short: neo4 now ships both the Elastic Chain-style proof-routing topology and an SP1
Groth16 on-chain validity verifier. This is not bytecode parity with ZKsync's Boojum/Plonk
verifier; it is the corresponding trustless settlement boundary for Neo's SP1/RISC-V path. Rows
below that read "parity" describe structural/topological parity unless stated otherwise; the
proof-verification rows are explicitly marked **partial**.

---

## Component map

| ZKsync component | neo4 equivalent | Status |
|---|---|---|
| **`Bridgehub.sol`** — chainId → ChainTypeManager registry, L1→L2 entry point | `NeoHub.ChainRegistry` + `NeoHub.MessageRouter` | parity (split into two contracts) |
| **`ChainTypeManager`** (formerly STM) — chain factory + upgrade orchestrator | `NeoHub.VerifierRegistry` + `NeoHub.GovernanceController` staged proposal windows | partial — no per-chain factory contract or DiamondProxy pattern |
| **`SharedBridge`** — L1 escrow (legacy) | `NeoHub.SharedBridge` | parity |
| **`L1AssetRouter` + `L2AssetRouter`** (v24+) — chain-agnostic asset routing | absent — single `SharedBridge` does both jobs | intentionally different (one Hub) |
| **`L1/L2NativeTokenVault`** — assetId derivation, bridged-token deploy | `NeoHub.TokenRegistry` + Neo Core native `L2BridgeContract` + native `BridgedNep17Contract` | parity for N4's design — L2 token accounting lives in the core native layer, not operator-deployed templates; platform assets now include NEO 0→8, GAS 8→8, USDT/USDC 6→6, and BTC 8→8 as chain-invariant L2 catalog entries |
| **`L1Nullifier`** — withdrawal replay protection | `SharedBridge.PrefixWithdrawalConsumed` + `MessageRouter.PrefixConsumed` | parity |
| **`MessageRoot.sol`** — aggregated L2→L1 root across all chains | `MessageRouter.PublishGlobalRoot` (0x05 slot) + off-chain `Neo.Plugins.L2Gateway.BinaryTreeAggregator` | parity |
| **`ChainAssetHandler`** (per-asset routing rules) | absent | intentionally different (single trust model) |
| **`ValidatorTimelock`** — commit→execute delay on **already-proven** batches | `NeoHub.OptimisticChallenge` + `SettlementManager.StatusChallengeable` | **different security model, not parity** — ZKsync delays execution of batches whose validity proof already verified on L1; neo4's window is instead an *optimistic* fraud-proof game where validity is assumed absent a challenge (N4 `ProofType.Optimistic`, Stage 1). See **L1 trust model** above. |
| **Governance / `ChainAdmin` / `PermanentRestriction` / `AccessControlRestriction`** | `NeoHub.GovernanceController` (immutable flags + staged proposal windows) | parity for shared governance; no ZKsync-style per-chain admin factory |
| **`TransactionFilterer`** (per-chain L1→L2 tx hook) | `MessageRouter.SetL1TxFilter` + `NeoHub.L1TxFilter` | parity for L1→L2 enqueue filtering; L2 mempool filtering remains operator-specific |
| **`L2AdminFactory` / per-chain `ChainAdmin`** | absent — chain-admin is hub-side `operatorManager` in `ChainRegistry.L2ChainConfig` | intentionally different |
| **`BridgedStandardERC20`** — canonical L2 token | Neo Core native `BridgedNep17Contract` | parity at the canonical bridged-token level |
| **Boojum / Plonk verifier contracts** — on-chain validity-proof math | `NeoHub.ContractZkVerifier` routes `ProofType.Zk` to immutable `NeoHub.Sp1Groth16Verifier`; the latter pins the SP1 wrapper VK and executes Groth16/BN254 math through Neo Core | equivalent security boundary, different proof stack — SP1 Groth16 replaces Boojum/Plonk; production permanently disables SP1 `envelope-only`. See **L1 trust model** above. |
| **`CalldataDA` / `ValidiumL1DAValidator` / `RollupDAManager` / `RelayedSLDAValidator`** | `NeoHub.DARegistry` + `NeoHub.DAValidator` + off-chain writers in `Neo.Plugins.L2DA` | partial — DAC attestation gate exists; richer NeoFS/external inclusion adapters remain operator-specific |
| **`BytecodesSupplier` / `*Upgrade` family / `UpgradeStageValidator`** | `GovernanceController` proposal pipeline with notice/execution/cooldown windows | parity for staged timing; no bytecode supplier because NeoVM uses ContractManagement |
| **L2 `Bootloader`** | absent — NeoVM2/RISC-V runtime provides native dispatch | intentionally different |
| **L2 `ContractDeployer` / `KnownCodesStorage` / `AccountCodeStorage`** | absent — Neo's `ContractManagement` native handles this | intentionally different |
| **L2 `SystemContext`** (chainId, baseFee, blockhash) | Neo Core native `L2BatchInfoContract` + `L2SystemConfigContract` | parity |
| **L2 `L2BaseToken`** (ETH balance accounting) | NEP-17 GAS native | intentionally different |
| **L2 `L1Messenger`** (outbox) | Neo Core native `L2MessageContract` | parity |
| **L2 `NonceHolder`** | absent — per-tx-signer nonce is implicit in Neo's witness model | intentionally different |
| **L2 `DefaultAccount` + `IAccount` AA** | Neo Core native `L2AccountAbstraction` | partial — validate/execute/paymaster hooks exist; not protocol-native like EraVM |
| **L2 `TestnetPaymaster` + `IPaymasterFlow`** | Neo Core native `L2PaymasterContract` (top-up model only) | partial — no `approvalBased` flow selector |
| **L2 `L2InteropRootStorage` / `L2MessageVerification` (v29)** | Neo Core native `L2InteropVerifier` mirrors global roots and verifies Merkle inclusion locally | parity at helper-contract level |
| **L2 `L2V29Upgrade` / `ComplexUpgrader` / `L2GenesisUpgrade`** | scaffolded by `Neo.Hub.Deploy` (off-chain) but no on-chain orchestrator | partial |
| **L2 `GasBoundCaller`** | absent — NeoVM2/RISC-V gas is instruction/runtime-metered | intentionally different |
| **ZK Gateway** (settlement-layer proof aggregator) | `Neo.Plugins.L2Gateway` (off-chain) + on-chain `SettlementManager.PublishGatewayGlobalRoot` → `MessageRouter.PublishGlobalRoot` | parity at protocol/code level; external audit and executed production deployment evidence remain |
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

These exist in ZKsync because of EVM/EraVM peculiarities. NeoVM2/RISC-V's design either
makes them moot or provides native equivalents:

- **EraVM L2 system contracts** (`Bootloader`, `ContractDeployer`, `KnownCodesStorage`,
  `AccountCodeStorage`, `NonceHolder`, `MsgValueSimulator`, `Compressor`,
  `EvmEmulator.yul`, `EvmGasManager.yul`, `EventWriter.yul`, precompiles like
  `EcAdd.yul` / `EcPairing.yul` / `Modexp.yul` / `P256Verify.yul`) — NeoVM2/RISC-V provides
  `ContractManagement`, native NEP-17 GAS, native cryptography, and implicit signer
  nonces.
- **Diamond proxy + facet pattern** (`DiamondProxy.sol`, `Admin.sol`, `Executor.sol`,
  `Getters.sol`, `Mailbox.sol`) — exists to work around Ethereum's 24KB contract
  size limit. NeoVM2/RISC-V has no 24KB bound; the per-concern split in NeoHub's 23
  contracts is equivalent in effect.
- **`CTMDeploymentTracker` + `ChainAssetHandler`** — ZKsync needs these to support
  *competing* chain types and asset routers run by third parties. neo4 has one
  canonical Hub.
- **`L2BaseToken` + `L2WrappedBaseToken` / `L2WrappedBaseTokenStore`** — solves the
  ETH/WETH non-NEP-17 unwrap dance. GAS is already NEP-17 in Neo.
- **`GasBoundCaller`** — EraVM gas semantics differ from EVM. NeoVM2/RISC-V gas is
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
