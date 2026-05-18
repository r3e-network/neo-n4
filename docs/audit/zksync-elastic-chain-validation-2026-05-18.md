# ZKsync Elastic Chain Alignment Validation - 2026-05-18

This pass revalidates Neo N4 against the current ZKsync Elastic Chain design
using official ZKsync docs and the local implementation state in `D:\Git\neo-n4`.

## Official ZKsync Baseline

Sources checked on 2026-05-18:

- ZKsync L1 ecosystem contracts:
  https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/l1-ecosystem-contracts
- ZKsync shared bridges:
  https://docs.zksync.io/zksync-protocol/contracts/l1-contracts/shared-bridges
- ZKsync EraVM system contracts:
  https://docs.zksync.io/zksync-protocol/era-vm/contracts/system-contracts

The relevant ZKsync pattern is still: shared L1 contracts for chain registry,
CTM governance, bridge routing, proof validation, and shared liquidity; L2
system contracts are present from genesis and hold privileged protocol state.

## Alignment Verdict

| ZKsync design point | Neo N4 implementation | Verdict |
|---|---|---|
| Bridgehub as shared L1 entry point and chain registry | `NeoHub.ChainRegistry`, `NeoHub.MessageRouter`, `NeoHub.SharedBridge` | Aligned; split by concern instead of one Bridgehub facade |
| CTM-managed chain type and upgrade control | `NeoHub.VerifierRegistry`, `NeoHub.GovernanceController`, per-chain `operatorManager` | Aligned for N4; no DiamondProxy/CTM factory because NeoVM does not need EVM facet splitting |
| Shared bridge and canonical wrapped assets | `NeoHub.TokenRegistry`, `NeoHub.SharedBridge`, Neo core native `L2BridgeContract`, `BridgedNep17Contract` | Aligned; wrapped asset accounting is in the native layer |
| L2 system contracts at genesis | 10 native contracts in `external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` | Aligned; no `contracts/L2Native.*` deployable suite remains in active build paths |
| Gateway/global message root and interop verification | `MessageRouter.PublishGlobalRoot`, `Neo.Plugins.L2Gateway`, native `L2InteropVerifier` | Aligned |
| L1-to-L2 transaction filtering | `MessageRouter.SetL1TxFilter`, `NeoHub.L1TxFilter` | Aligned |
| DA commitment validation | `NeoHub.DARegistry`, `NeoHub.DAValidator`, `Neo.Plugins.L2DA` | Aligned for current rollup/validium modes |
| Account abstraction and paymaster hooks | Native `L2AccountAbstraction`, native `L2PaymasterContract` | Aligned at protocol-hook level; intentionally Neo-style, not EIP-4337 byte-for-byte |
| Base token model | Neo native GAS / TokenManagement | Intentional divergence; Neo does not need EraVM `L2BaseToken` and `MsgValueSimulator` |

## Security Fixes Applied

1. `L2BridgeContract.ApplyDeposit` and `InitiateWithdrawal` now call the native
   `BridgedNep17Contract` facade with `(assetId, account, amount)`. Previously
   the bridge treated the `TokenManagement` asset id as a contract hash.
2. `L2NativeExternalBridgeContract.Send` now burns canonical wrapped assets and
   `ApplyInbound` now mints them through `BridgedNep17Contract`. It no longer
   transfers to or from `UInt160.Zero` / bridge balances.
3. `BridgedNep17Contract` now supports an owner/committee-managed authorized
   bridge set, so both canonical L1-L2 bridge and external-chain bridge can mint
   or burn without weakening the bridge-only gate.
4. `L2NativeExternalBridgeContract` now records reverse `(externalChainId,
   l2Asset)` mapping and rejects outbound/inbound asset flows for unregistered
   L2 assets.
5. `L2FeeContract.Distribute` now throws if any fee-asset transfer returns
   `false`; it cannot emit `FeesDistributed` after a failed token movement.

## Documentation Fixes Applied

- English and Chinese architecture glossaries now describe 23 NeoHub L1
  deployable contracts and 10 Neo core L2 native contracts.
- CI documentation now states the current split: 23 `NeoHub.*` + 2 `Sample.*`
  deployable contracts compile to artifacts, and the 10 L2 native contracts are
  verified through `external/neo` unit tests.
- Architecture SVGs now show "10 native contracts".
- Historical 2026-05-17 rehearsal notes now carry a supersession note explaining
  that L2 system contracts moved into the r3e Neo core native layer.
- `samples/contracts/README.md` no longer implies samples deploy alongside
  `L2Native.*` contracts.

## Verification Evidence

Commands run locally on Windows + WSL2:

| Command | Result |
|---|---|
| `dotnet test tests\Neo.UnitTests\Neo.UnitTests.csproj /p:NuGetAudit=false --nologo --filter FullyQualifiedName~UT_L2NativeContracts` in `external/neo` | Passed: 6, Failed: 0 |
| `dotnet test tests\Neo.UnitTests\Neo.UnitTests.csproj /p:NuGetAudit=false --nologo` in `external/neo` | Passed: 921, Failed: 0 |
| `dotnet build Neo.L2.sln /p:NuGetAudit=false --nologo` | Build succeeded, 0 warnings, 0 errors |
| `dotnet test Neo.L2.sln /p:NuGetAudit=false --nologo --no-build` | All test projects passed, 0 failed |
| `.\scripts\private-network\Test-PrivateNetwork.ps1` | Passed |
| `dotnet list Neo.L2.sln package --vulnerable --include-transitive` | No vulnerable packages reported for all projects using nuget.org + local SDK sources |
| `git diff --check` in main repo | Exit 0; CRLF normalization warnings only |
| `git diff --check` in `external/neo` | Exit 0 |

Private-network run summary:

- Run id: `20260518-131438`
- Summary: `D:\Git\neo-n4\artifacts\private-network\20260518-131438\summary.json`
- Status: `passed`
- Covered: solution build/test, 25 deployable contract artifacts, four chain
  templates, local devnet smoke runs, RISC-V host check, zkVM guest build/test,
  real SP1 proof generation/verification, Rust workspace fmt/clippy/test,
  RustSec audit, TypeScript SDK test/build/audit, Foundry foreign EVM tests,
  Solana router tests, and `mdbook build docs`.

## Residual Scope Boundary

This pass completes the local and private-network validation gates. It does not
claim public Neo N4 testnet/mainnet finality, because that requires public RPC
endpoints, funded accounts, governance/multisig wallets, and external testnet
routes. The repository behavior and private-network deployment path are verified
with local evidence above.
