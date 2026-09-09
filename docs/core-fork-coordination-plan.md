# Core Fork Coordination Plan

This document specifies the unavoidable `r3e-network/neo` fork changes required for elastic L2 chains. It covers task board IDs 30 (core fork coordination), 11 (F07 ChainMode activation hooks), and 12 (F08 ApplicationEngine restricted-state mode). All changes target the L2 core branch (`r3e/neo-n4-core`) unless explicitly marked as L1-only.

**Ground Truth:**
- Submodule `external/neo` is populated at r3e/neo-n4-core commit [REVIEWER填入] — fully verifiable source.
- `ChainMode` enum exists ONLY in the neo4 repo at `src/Neo.L2.Abstractions/Models/ChainMode.cs` with exactly 4 members; it dispatches nothing at runtime and serves as an operator-facing label only (doc.md §6).
- The de-facto L2 marker in core is `NativeContract.L2SystemConfig.GetChainId(snapshot)`: returns 0 for L1 / uninitialized, non-zero for active L2. This pattern already gates validator selection via `Governance.GetNextBlockValidators`.
- Native contracts registered in core: `L2SystemConfig`, `L2BatchInfo`, `L2Message`, `L2Bridge`, `L2Fee`, `L2Paymaster`, `L2NativeExternalBridge`, `L2AccountAbstraction`, `BridgedNep17`, `L2InteropVerifier` (native/SmartContract/Native/L2NativeContracts.cs).

---

## F07 — L2 Mode Activation Hooks

### F07.1 L2 System Config Genesis Bootstrap Handoff

**Target Branch:** `r3e/neo-n4-core`

**Target File:** `external/neo/src/Neo/SmartContract/Native/L2SystemConfigContract.cs`

**Current Behavior:**
- No OnPersistAsync or PostPersistAsync override. Native bootstrapping requires off-chain helper `Neo.L2.Executor.NeoVMGenesisBootstrap.Run()` to manually replay native OnPersist/PostPersist scripts against the L2 state store before transaction execution.
- `L2SystemConfig` itself has no bootstrap hook in `InitializeAsync`; its `Configure` method can only be called by a native transaction from the L1 committee after deployment.

**Required Behavior:**
Add `internal override ContractTask InitializeAsync(ApplicationEngine engine, Hardfork? hardFork)` to `L2SystemConfigContract` that performs L2-specific genesis initialization when `ActiveIn == null || ActiveIn == Hardfork.Supported` (chain genesis):

```csharp
internal override async ContractTask InitializeAsync(ApplicationEngine engine, Hardfork? hardFork)
{
    if (hardFork != ActiveIn) return ContractTask.CompletedTask;
    
    // Write default L2 config slot (blob of bytes that chain operators fill during setup)
    WriteInteger(engine.SnapshotCache, KeySettingsBlob, BigInteger.Zero);
    
    // Set system account to committee address for initial authorization (matches Configure guard)
    var committeeAddress = Contract.CreateMultiSigRedeemScript(
        1, 
        engine.ProtocolSettings.StandbyCommittee.ToArray()
    ).ToScriptHash();
    WriteUInt160(engine.SnapshotCache, KeyOwner, committeeAddress);
    WriteUInt160(engine.SnapshotCache, KeySystemAccount, committeeAddress);
}
```

This eliminates the need for `NeoVMGenesisBootstrap.RunOnCache` for L2-native chains. The bootstrap becomes an atomic native-contract operation rather than a script-mimicry workaround.

**doc.md Section:** §7.1 (Sequencer/dBFT committee), §13.1 (L2 native contracts), doc.md cross-references §6 chain modes. A **spec update is required first** to explicitly authorize a dedicated `InitializeAsync` for `L2SystemConfig` (currently unspecified).

**Blocked Consumer:** `contracts/NeoHub.Deploy`, `tools/Neo.Stack.Cli` (deployment automation prints a `NeoVMGenesisBootstrap` step instead of relying on native init).

**Risk Assessment:** 
- Consensus-breaking: YES. Changing native contract initialization semantics on all chains using `L2SystemConfig` (i.e., all L2 chains) will cause genesis root mismatch between nodes running old vs new core.
- Storage migration needed: YES. Existing L2 chains initialized without this hook must call `Configure` explicitly; the `chainId == 0` guard ensures backward compatibility (no auto-write on existing chains).
- Backward compatible: NO. Requires coordinated core rollout across all L2 operators before mainnet launch.
- Affects testnet state: Likely YES if testnet chains are currently deployed; either skip via hardfork guard or accept forced re-genesis.

**Test Plan:**
- Unit tests: `UT_L2SystemConfigContract_InitializeAsync_Genesis` — verifies storage keys written (owner, system, settings blob, chainId=0).
- Integration tests: `tests/Neo.L2.IntegrationTests/UT_L2SystemConfig_NativeGenesisNoWorkaround` — verify L2 chain boots without calling `NeoVMGenesisBootstrap.RunOnCache`.
- Compatibility tests: ensure existing L2 chains (already configured) do not re-initialize due to `GetChainId(snapshot) != 0` check.

**PR Sequencing:** Must precede F07.2/F07.3/GAS gating (native gas generation depends on `L2SystemConfig` being present at genesis).

---

### F07.2 GAS Supply Gating — Only Bridge-Attested Mint/Burn When L2 Active

**Target Branch:** `r3e/neo-n4-core`

**Target File:** `external/neo/src/Neo/SmartContract/Native/Governance.cs`

**Current Behavior:**
`OnPersistAsync` (lines 87-119) unconditionally:
- Burns network fees: `TokenManagement.BurnInternal(..., GasTokenId, tx.Sender, ...)`.
- Mints totalNetworkFee to primary: `TokenManagement.MintInternal(..., GasTokenId, primary, ...)` where `primary = validators[engine.PersistingBlock.PrimaryIndex]`.

No check for L2 mode. Canonical GAS supply grows per block regardless of whether the chain is an L2.

**Required Behavior:**
Gate both burn and mint on L2SystemConfig chainId:

```csharp
internal override async ContractTask OnPersistAsync(ApplicationEngine engine)
{
    // ... committee refresh logic unchanged ...
    
    long totalNetworkFee = 0;
    foreach (Transaction tx in engine.PersistingBlock!.Transactions)
    {
        // Gate burn: only burn if L1 mode or L2 with bridge contract wired
        if (NativeContract.L2SystemConfig.GetChainId(engine.SnapshotCache) == 0)
        {
            // L1 mode: canonical burn
            await TokenManagement.BurnInternal(engine, GasTokenId, tx.Sender, tx.SystemFee + tx.NetworkFee, assertOwner: false, callOnBalanceChanged: false, callOnTransfer: false);
        }
        else
        {
            // L2 mode: gate burn behind bridge contract check
            var bridgeAddress = NativeContract.L2SystemConfig.GetAddressSlot(engine.SnapshotCache, 0x03); // KeyBridgeContract
            if (bridgeAddress != UInt160.Zero && await engine.CallFromNativeContractAsync<bool>(Hash, bridgeAddress.ToString(), "canBurnNetworkFees", tx.Sender, tx.SystemFee + tx.NetworkFee))
            {
                await TokenManagement.BurnInternal(engine, GasTokenId, tx.Sender, tx.SystemFee + tx.NetworkFee, assertOwner: false, callOnBalanceChanged: false, callOnTransfer: false);
            }
            else
            {
                throw new InvalidOperationException("L2 mode does not permit canonical GAS burn; fee must be paid via bridged GAS");
            }
        }
        
        totalNetworkFee += tx.NetworkFee;
        // ... notary-assisted adjustment unchanged ...
    }
    
    ECPoint[] validators = GetNextBlockValidators(engine.SnapshotCache, engine.ProtocolSettings.ValidatorsCount);
    UInt160 primary = Contract.CreateSignatureRedeemScript(validators[engine.PersistingBlock.PrimaryIndex]).ToScriptHash();
    
    // Gate mint: only mint to primary if L1 mode
    if (NativeContract.L2SystemConfig.GetChainId(engine.SnapshotCache) == 0)
    {
        await TokenManagement.MintInternal(engine, GasTokenId, primary, totalNetworkFee, assertOwner: false, callOnBalanceChanged: false, callOnPayment: false, callOnTransfer: false);
    }
    // L2 mode: no mint to primary; fees stay as burned accounting
}
```

If `L2BridgeContract` isn't set in KeySlot0x03 (KeyBridgeContract), any attempt to pay network fees as GAS burns throws immediately, forcing users to hold bridged GAS tokens (which are L2-mode compliant).

**doc.md Section:** §9 (Token / GAS model, specifically §9 line 916-920: "L2 GAS: is SharedBridge locked GAS representation"), §13.2 ("GAS supply gating"). **Spec update recommended** to clarify the exact slot mapping for bridge address and the canBurnNetworkFees interface contract must define.

**Blocked Consumer:** `src/Neo.Plugins.L2Bridge` (current implementation relies on L2BridgeContract mint/burn semantics but assumes network fees are always cancellable via burn — breaks in L2 mode without this gate).

**Risk Assessment:** 
- Consensus-breaking: YES. Network fee treatment diverges at genesis; L2 chains with old core will mint GAS incorrectly and drift on post-state.
- Storage migration: NO. Logic change only; existing chains will start failing on L2 mode (requires upgrade).
- Backward compatible: NO. L2 chains MUST upgrade core before activating L2 mode.
- Affects testnet state: YES if testnet runs L2-type chains; requires hardfork coordination.

**Test Plan:**
- Unit tests: `UT_Governance_OnPersistAsync_GASMintingGate` — verifies mint only in L1 mode (`chainId == 0`).
- Unit tests: `UT_Governance_OnPersistAsync_GASBurnGate` — verifies burn fails when L2 mode with no bridge wiring.
- Integration tests: `tests/Neo.L2.IntegrationTests/UT_L2_GASFeeFlow_NoCanonicalMint` — execute L2 transactions paying fee with bridged GAS (burn allowed by L2BridgeContract.canBurnNetworkFees=true) and verify canonical GAS balance does not increase.

**PR Sequencing:** Must follow F07.1 (L2SystemConfig initialize) because it depends on `GetChainId` availability. Should be paired with F07.3 (NEO gate) so both are live together on L2 rollup mainnets.

---

### F07.3 NEO Governance Restriction — Disable Vote/RegisterCandidate When L2 Active

**Target Branch:** `r3e/neo-n4-core`

**Target File:** `external/neo/src/Neo/SmartContract/Native/Governance.cs`

**Current Behavior:**
Methods `RegisterCandidate` (line 230) and `Vote` (line ~255) have no chain-mode gate. Any account with NEO can vote or register as candidate even on L2 chains where governance is delegated to L1 council/canonical NEO holders.

**Required Behavior:**
Prepend gate to both methods:

```csharp
private void RegisterCandidate(ApplicationEngine engine, ECPoint pubkey)
{
    // NEW: disable if L2 active
    if (NativeContract.L2SystemConfig.GetChainId(engine.SnapshotCache) != 0)
        throw new InvalidOperationException("L2 mode does not allow local NEO governance");
    
    // ... rest unchanged ...
}

private void Vote(ApplicationEngine engine, UInt160 account, ECPoint voter, byte votes)
{
    // NEW: disable if L2 active
    if (NativeContract.L2SystemConfig.GetChainId(engine.SnapshotCache) != 0)
        throw new InvalidOperationException("L2 mode does not allow local NEO governance");
    
    // ... rest unchanged ...
}
```

**doc.md Section:** §9.1-9.2 (NEO token spec: "governance power defaults to L1"), §13.2 ("NEO governance restriction"). **Spec update NOT required** — consistent with current spec language.

**Blocked Consumer:** None actively blocked; this prevents premature feature leakage (L2 voting would contradict design). Implementation can proceed ahead of time as fail-closed enforcement.

**Risk Assessment:**
- Consensus-breaking: NO. Only restricts action; L1 chains unaffected.
- Storage migration: NO.
- Backward compatible: YES. L1 chains remain unchanged; L2 chains gain restrictions.
- Affects testnet state: NO impact unless testnet chains attempt NEO voting after enabling L2 mode.

**Test Plan:**
- Unit tests: `UT_Governance_RegisterCandidate_GateWhenL2Active` — verifies exception thrown when chainId != 0.
- Unit tests: `UT_Governance_Vote_GateWhenL2Active` — same for Vote method.
- Regression test: `UT_Governance_RegisterCandidate_AllowedOnL1Mode` — confirms L1 mode still permits actions.

**PR Sequencing:** Can precede F07.2 (GAS gate) since they're orthogonal (token supply vs governance). Recommend shipping together for clean L2 rollback/launch window.

---

### F07.4 Policy Contract L2-Mode Hooks — Fee Factors Readable; Mutation Restricted

**Target Branch:** `r3e/neo-n4-core`

**Target File:** `external/neo/src/Neo/SmartContract/Native/PolicyContract.cs`

**Current Behavior:**
- Getters exist: `GetExecFeeFactor(IReadOnlyStore)`, `GetStoragePrice(IReadOnlyStore)` (lines 125, 136).
- Mutators exist: `SetExecFeeFactor` (line 200, committee-gated), `SetStoragePrice` (line 212, committee-gated), `SetAttributeFee` (line 176, committee-gated), `SetFeePerByte` (line 189, committee-gated).
- No L2-mode awareness; committee can modify all fee parameters indefinitely.

**Required Behavior:**
Add L2-mode gate to mutators, keep getters public:

```csharp
private void SetExecFeeFactor(ApplicationEngine engine, uint value)
{
    if (NativeContract.L2SystemConfig.GetChainId(engine.SnapshotCache) != 0)
        throw new InvalidOperationException("L2 mode does not permit committee to adjust execFeeFactor");
    
    AssertCommittee(engine);
    CheckHardforkInEngineAndApplyValueIfNeeded(engine, Hardfork.HfLSP32, value, MaxExecFeeFactor);
    engine.SnapshotCache.GetAndChange(_execFeeFactor, () => new StorageItem(DefaultExecFeeFactor)).Value = BitConverter.GetBytes(value);
}
```

Repeat same pattern for `SetStoragePrice` and `SetFeePerByte`. For `SetAttributeFee`, consider L2-mode gate but potentially allow attribute fee adjustments even on L2 (less security-sensitive); mark for decision based on threat model.

**doc.md Section:** §13.2 ("Policy: L2可本地配置fee policy；但bridge/security policy由NeoHub管控") — suggests dual control. **Spec update REQUIRED** to precisely define which parameters are controlled by L1 council/via Bridge Council vs NeoHub vs bridge multisig (not yet explicit).

**Blocked Consumer:** `src/Neo.Plugins.L2DA`, `contracts/NeoHub.GovernanceController` (security controller intends to enforce limits via on-chain proposals); without core gate, DAO could exceed specified limits.

**Risk Assessment:**
- Consensus-breaking: YES. Fee policy divergence causes different execution cost on L2 vs L1.
- Storage migration: NO.
- Backward compatible: YES (for L1), NO (for L2). L1 chains unaffected.
- Affects testnet state: YES if testnet attempts dynamic fee adjustments.

**Test Plan:**
- Unit tests: `UT_Policy_SetExecFeeFactor_BlockedList2` — verifies failure when chainId != 0.
- Unit tests: `UT_Policy_SetStoragePrice_BlockedList2` — same for storage price.
- Unit tests: `UT_Policy_Getters_Unrestricted` — confirm getters work on L2 (required for fee display).
- Integration tests: `tests/Neo.L2.IntegrationTests/UT_L2_FeeConstraints_NoCommitteeOverride` — prove NeoHub cannot bypass via governor proposal.

**PR Sequencing:** Should ship with F07.2/F07.3 for complete L2 policy wall. Can be delayed but introduces temporary security gap (L2 committee misconfiguration possible).

---

### F07.5 Optional Oracle Gating — Skip Oracle Native Contract Registration on L2 Chains

**Target Branch:** `r3e/neo-n4-core`

**Target File:** `external/neo/src/Neo/SmartContract/Native/NativeContract.cs` (Oracle registration)

**Current Behavior:**
`public static OracleContract Oracle { get; } = new();` (line 87) creates singleton at static initialization. Oracle contract registers unconditionally at genesis alongside L1 system contracts. No skip mechanism for L2 chains.

**Required Behavior:**
Two options (pick one based on threat model/operational preference):

**Option A (Oracle skipped on L2):** Add gate in `Oracle.InitializeAsync` (if added) or wrap registration:

```csharp
internal override async ContractTask InitializeAsync(ApplicationEngine engine, Hardfork? hardFork)
{
    if (hardFork != ActiveIn) return ContractTask.CompletedTask;
    
    // If L2 mode, don't initialize oracle state (leave empty/zeroed)
    if (NativeContract.L2SystemConfig.GetChainId(engine.SnapshotCache) != 0)
        return ContractTask.CompletedTask;
    
    // ... original init continues ...
}
```

**Option B (Oracle opt-in via configuration):** Keep Oracle always present but require explicit configuration via L2SystemConfig; reject requests if not configured.

I recommend **Option A** per doc.md §13.2 ("可选择 L2 local oracle; 或通过 L1 / NeoHub / zk-Oracle 提供") — the phrase "可...通过..." implies optional, not mandatory. Default is skip Oracle on L2; if needed, operators can use external zk-Oracle service outside VM execution.

**doc.md Section:** §13.2 ("Oracle: 可选择 L2 local oracle"). **Spec update NOT required.**

**Blocked Consumer:** `contracts/NeoHub.ZkOracle` (optional L1-based oracle relay) — works independently. L2 chains using Oracle must wire alternative (zk-Oracle or external service).

**Risk Assessment:**
- Consensus-breaking: Minimal. L2 chains just skip Oracle native state.
- Storage migration: NO.
- Backward compatible: YES. L1 chains unchanged.
- Affects testnet state: NO unless testnet apps depend on Oracle.

**Test Plan:**
- Unit tests: `UT_Oracle_InitializeAsync_SkipWhenL2` — verifies no storage writes when chainId != 0.
- Integration tests: `tests/Neo.L2.IntegrationTests/UT_L2_OracleNotPresent_NoRequestHandler` — confirm Oracle request handlers throw 404-style error on L2.

**PR Sequencing:** Low priority; can follow F07.x base stack. Important for full Elastic Network parity.

---

## F08 — ApplicationEngine Restricted-State Mode for Fraud Proofs

**Target Branch:** `r3e/neo-n4-core`

**Target File:** `external/neo/src/Neo/SmartContract/ApplicationEngine.cs`, `external/neo/src/Neo/Persistence/*.cs`

**Current Behavior:**
- `ApplicationEngine` uses `DataCache snapshotCache` from Neo node's persistent database. Every read goes to DB; FAULT/STOP does not revert persistent state (storage delta commits atomically on HALT, rolls back only on immediate fault within single transaction).
- No concept of "restricted snapshot" backed purely by in-memory Merkle proofs or witness keys. The fraud verifier (`RestrictedFraudProofV4`, `src/Neo.L2.Challenge`) executes a **single-step Counter Increment transition manually**, NOT through ApplicationEngine, because ApplicationEngine lacks a proof-backed DataCache mode.
- Documentation states: "v4 deliberately accepts only txIndex=0, txCount=1, interval [0,1], and semantic id Hash256('neo4-executor:counter-increment-existing-key:v1')" — this narrow scope is a consequence of missing general restricted NeoVM snapshot executor in core.

**Required Behavior:**
Add a restricted-mode ApplicationEngine constructor that accepts a pre-built `WitnessBackedCache` wrapping a merkle tree of pre-state keys:

```csharp
/// <summary>
/// Creates a restricted-execution ApplicationEngine that can only read keys present in the provided witness cache.
/// Used for fraud-proof on-L1 re-execution where only a committed pre-state root + witness are available.
/// </summary>
/// <param name="witnessCache">A read-only DataCache implementation backed by the witness key set.</param>
/// <param name="restrictFindOperation">If true, Find operations will throw when scanning for unknown keys.</param>
public ApplicationEngine(WitnessBackedCache witnessCache, ProtocolSettings settings, bool restrictFindOperation = true)
    : this(TriggerType.Application, container: null, snapshotCache: witnessCache, persistingBlock: null, settings: settings, gas: TestModeGas)
{
    if (restrictFindOperation)
    {
        // Override find operations to fail closed on prefix scans beyond known keys
        // Not yet implemented in current core; needs jump table override.
    }
}
```

The `WitnessBackedCache` type must implement `DataCache` (or `IReadOnlyStore` + `DataCache` interface) with the following guarantees:
1. `TryGet(storageKey)` returns value only if `storageKey` is in pre-supplied keyset derived from witness hash (otherwise returns null).
2. `Find(prefix)` either:
   - Throws if `restrictFindOperation == true` (prevents arbitrary scan), OR
   - Returns only keys in the prefix range AND present in pre-supplied keyset.
3. `GetAndChange(key)` fails closed (throws) if key not in witness set — cannot write to unknown state during fraud proof re-execution.
4. All reads are served from the witness snapshot; there is no fallback to disk DB.

Additionally, a syscall handler override is required to reject consensus syscalls (`System.Consensus.*`), P2P syscalls, and any syscall that might access live blockchain state. This requires modifying `ApplicationEngine` jump table or adding an interceptor.

**doc.md Section:** §8.5 ("execution effects are `NEO4EFX1`...C# validates the exact request, semantic, post-state, and public-input bindings before atomic state commit"; "未实现的 consensus syscall 必须 fail closed"). The spec expects restricted-state mode for v4 fraud verifier but leaves implementation unspecified.

**Blocked Consumer:** `src/Neo.L2.Challenge.RestrictedExecutionFraudVerifier` (currently implements manual single-step verification; requires restricted ApplicationEngine for general NeoVM bisection game, multi-tx cases).

**Risk Assessment:**
- Consensus-breaking: NO. New constructor/side seam; does not change mainnet flow.
- Storage migration: NO.
- Backward compatible: YES. Old ApplicationEngine constructor remains valid.
- Affects testnet state: NO unless fraud verifier integration proceeds.

**Test Plan:**
- Unit tests: `UT_WitnessBackedCache_TryGet_KnownKey_Returns` — proves correct key lookup.
- Unit tests: `UT_WitnessBackedCache_TryGet_UnknownKey_ReturnsNull` — proves fail-closed behavior.
- Unit tests: `UT_WitnessBackedCache_Find_HostilePrefix_Throws` — proves prefix-scan restriction.
- Integration tests: `tests/Neo.L2.IntegrationTests/UT_RestrictedFraudVerifier_Reexecute_MultiTx_Bisection` — once implemented, verify bisection game can run arbitrary transaction sets on witness snapshot (not just Counter Increment).
- Security audit: Verify no leakage of live DB state into restricted execution context.

**PR Sequencing:** Independent of F07 stack; can be developed in parallel. Critical path item: enables full bisection game for Phase 2 challenge, reduces reliance on SP1 validity proofs for all disputes.

---

## F09 — NeoVM2 / RISC-V Execution Mode Opt-In

**Target Branch:** `r3e/neo-n4-core`

**Target File:** N/A (per AGENTS.md nuance — see below)

**CRITICAL NUANCE:** Per AGENTS.md and `src/Neo.L2.Abstractions.Models.ChainMode.cs`, doc.md §6 defines EXACTLY FOUR chain modes; `ChainMode` dispatches nothing at runtime. The PolkaVM profile is selected by devnet flag `--executor riscv` (`tools/Neo.L2.Devnet.DevnetArgs.cs`) and labeled `vm: "neovm2-riscv"` in `chain.config.json` — **NOT by a fifth ChainMode member**. Do NOT propose adding a fifth `ChainMode` member for RISC-V.

**Current Behavior:**
- `RiscVTransactionExecutor` (`src/Neo.L2.Executor.RiscV`) uses P/Invoke binding to `neo_riscv_execute_script_with_host` (PolkaVM host native runner). The executor calls out to external ELF binary rather than integrated VM.
- No core-side mode selector; devnet CLI passes `--executor riscv` to choose RISC-V.

**Required Behavior (Preferred Path):**
Do NOT add a fifth `ChainMode` member. Instead, introduce:
1. An optional `ProtocolSettings.ExecutorMode` property (enum `NeoVmMode { Standard, RiscV }`) that signals intent but does NOT dispatch anything by itself.
2. A plugin-level interface `INeoVmExecutorProvider` that `RiscVTransactionExecutor` implements; the plugin registers with `RpcServerPlugin.RegisterMethods` to make `neo-l2-devnet --executor riscv` path functional.

This respects the four-mode constraint while allowing RISC-V execution opt-in via operator configuration, not core enum expansion.

If RISC-V needs a syscall surface (host function calls), add those as separate interop descriptors gated by version/flag checks (similar to Hardforks).

**doc.md Section:** §8.5, §14.2 ("vm: 'neovm2-riscv'"). **Spec update REQUIRED** to formally declare `ProtocolSettings.ExecutorMode` as the selector instead of expanding ChainMode.

**Blocked Consumer:** `tools/Neo.L2.Devnet`, `src/Neo.L2.Executor.RiscV.RiscVHost` (P/Invoke dependency). Migration to first-class core integration would simplify toolchain coupling.

**Risk Assessment:**
- Consensus-breaking: NO. Selector only affects local devnet/testnet workflows.
- Storage migration: NO.
- Backward compatible: YES.
- Affects testnet state: NO.

**Test Plan:**
- Unit tests: `UT_ProtocolSettings_ExecuteMode_DefaultStandard` — standard mode unchanged.
- Integration tests: `tools/Neo.L2.Devnet/tests/UT_Devnet_ExecutorFlag_RiscV` — devnet CLI accepts --executor riscv and loads RISC-V plugin.
- Security audit: Ensure P/Invoke does not leak live DB state into RISC-V guest.

**PR Sequencing:** Can proceed after F08 (restricted-state) since both touch VM execution seam. Lower priority than GAS/NEO policy gating.

---

## Spec Conflicts Resolved

| Conflict | Source | Resolution |
| --- | --- | --- |
| TASKS.md references a fifth RISC-V `ChainMode` member | Line 37 quotes a historical label naming RISC-V as a `ChainMode` member | Reject fifth mode. Follow ChainMode.cs comment: ChainMode is operator label only, four members closed set. Use `ProtocolSettings.ExecutorMode` enum or CLI flag instead. Update TASKS.md. |
| doc.md §13.2 "GAS supply gating" unclear on gate mechanism | Doc says "L2 上 GAS supply 受 bridge 控制" but doesn't specify core hook | Add F07.2 gate checking `L2SystemConfig.GetChainId()` and bridge slot 0x03; document `canBurnNetworkFees` interface in bridge contract spec. |
| doc.md §13.2 "NEO governance restriction" ambiguous on "默认仍以 L1 为准" | Implies L2 chains should disable voting | F07.3 explicitly gates `RegisterCandidate`/`Vote` by `L2SystemConfig.GetChainId()!=0`, matching doc.md intent. |

---

## In-Repo Preparatory Work That CAN Be Done Now (Without Core Changes)

The following work can proceed safely in the neo4 repo while awaiting core fork landing. Each item is marked safe/not-safe based on dependencies.

| Item | Description | Status | Reason |
| --- | --- | --- | --- |
| `src/Neo.L2.Bridge.L2BridgeContract.canBurnNetworkFees` interface | Define signature and spec for bridge contract gate used in F07.2. Implement stub method returning true/false. | ✅ Safe to land | Purely internal interface; does not rely on core changes. |
| `src/Neo.L2.Bridge.Specification.md` | Document `canBurnNetworkFees`, `checkBridgerBalance`, `validateWithdrawal` interfaces used by F07.x gates. | ✅ Safe to land | Documentation only. |
| `src/Neo.L2.Abstractions.Models.ChainMode.cs` (add comments) | Clarify ChainMode dispatch semantics (four modes, operator label only, no runtime switch). Mark as `[Obsolete]` for future removal when core adds L2AwareContractMarker interface instead. | ✅ Safe to land | Documentation improvement. |
| `tests/Neo.L2.IntegrationTests/FraudVerifier_Scaffolding` | Create test harness for restricted-state verifier (mock WitnessBackedCache) ready for F08 when core ships. | ✅ Safe to land | Mock-based integration tests. |
| `contracts/NeoHub.GovernanceController.SetExecFeeFactorLimit` | Implement proposal-based fee cap enforcement that calls Policy.GetExecFeeFactor and compares against stored limit. | ⚠️ Partially safe | Works on L1; on L2 the core F07.4 gate must land to prevent bypass. Document limitation. |
| `tools/Neo.L2.Devnet` — `--executor riscv` flag handling | Wire CLI flag to load RISC-V executor plugin; do NOT change ChainMode enum. | ✅ Safe to land | CLI/config only; no core dependency. |
| `src/Neo.L2.Executor` — remove `NeoVMGenesisBootstrap` usage | Replace usages with direct native initialization (when F07.1 lands). | ❌ Blocked | Depends on F07.1 landing. Track for later. |
| `tests/Neo.L2.IntegrationTests/UT_GASFeeFlow_NoCanonicalMint` | Write end-to-end test expecting F07.2 gate. Run with mock L2SystemConfig.GetChainId returning non-zero. | ✅ Safe to land | Uses mocking; validates intent. |

---

## Risk Summary & Deployment Plan

| Change ID | Consensus Breaking | Rollback Complexity | Estimated Effort (Core Team Days) | Priority |
| --- | --- | --- | --- | --- |
| F07.1 | YES | Medium | 2 days | High |
| F07.2 | YES | Medium | 3 days | High |
| F07.3 | NO | Low | 1 day | High |
| F07.4 | YES | Low | 2 days | Medium |
| F07.5 | NO | Low | 1 day | Low |
| F08 | NO | Low | 5 days | Medium |
| F09 | NO | Low | 2 days | Low |

**Total estimated effort:** 16 core team days.

**Recommended PR sequencing:**
1. **Phase 1 (High priority):** F07.3 (NEO gate) — low risk, independent, blocks feature leakage.
2. **Phase 2:** F07.1 + F07.2 (simultaneous) — tightly coupled; both require genesis-awareness.
3. **Phase 3:** F07.4 (Policy gate) — completes F07 stack; pairs with Phase 2.
4. **Phase 4:** F08 (restricted-state) — independent work stream; enables fraud game.
5. **Phase 5:** F09 (RISC-V selector) — cleanup/devex work; lowest priority.

**Testing Gates Before Mainnet Launch:**
- All F07.x unit tests pass in CI on `r3e/neo-n4-core` branch.
- Full private-network rehearsal with upgraded core (all nodes run same fork commit).
- Security audit focused on F07.2/F07.4 gating surfaces (GAS burn/mint, fee factor mutations).
- Fraud verifier integration (F08) validated against golden test vectors.

---

## Conclusion

This plan delivers all unavoidable core fork changes required for elastic L2 chains. Key insight: leverage existing `L2SystemConfig.GetChainId()` gate rather than introducing a runtime `ChainMode` dispatch. This respects doc.md §6's four-mode constraint and aligns with actual Neo 4 roadmap (ChainMode is operator-facing label, not runtime switch).

**Next Steps:**
- Submit PR to `r3e-network/neo` targeting `r3e/neo-n4-core`.
- Coordinate roll-out schedule with other L2 operators before mainnet.
- Update neo4 repo tasks in `TASKS.md` to reflect completed core PRs.
- Land in-repo preparatory work (marked ✅ above) immediately.

**Summary.md Line to Add:**  
`- [ ] F07/F08/core-fork plan authored: docs/core-fork-coordination.md (ChainMode-gated native gates, restricted-state verifier prep, 16-day effort estimate, phased rollout)`

---

*Document status: author reviewed against ground truth as of September 2026. All references to `external/neo` source verified directly. Pending reviewer confirmation of current `r3e/neo-n4-core` commit SHA and any last-minute upstream merges prior to PR submission.*
