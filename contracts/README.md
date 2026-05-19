# Neo Elastic Network Smart Contracts

This directory contains the deployable **NeoHub** L1 contract suite. It must
not contain N4 L2 system contracts.

N4 L2 system contracts are not deployed from this directory. They are
registered as Neo core native contracts in the r3e Neo fork at
`../external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` and are
available from genesis on an N4 L2 chain.

## Build

Each deployable contract is a plain `Microsoft.NET.Sdk` class library that
references [`Neo.SmartContract.Framework`](https://github.com/neo-project/neo-devpack-dotnet)
via `ProjectReference`. The framework is vendored as a git submodule at
`external/neo-devpack-dotnet/src/Neo.SmartContract.Framework/` (init via
`git submodule update --init --recursive` after cloning). Override
`NeoDevpackPath` in `Directory.Build.props` if you want to point at a local
fork.

```bash
dotnet build contracts/
nccs contracts/NeoHub.ChainRegistry/
```

`dotnet build` type-checks the C# contract surface. CI and deployment
rehearsals run `nccs` explicitly to emit deployable bytecode artifacts.

## Layout

```text
contracts/
|-- NeoHub.ChainRegistry/                       # L2 chain admission + config registry
|-- NeoHub.SharedBridge/                        # canonical asset escrow + deposit/withdraw
|-- NeoHub.SettlementManager/                   # batch submission + finalization
|-- NeoHub.VerifierRegistry/                    # pluggable proof verifier dispatch
|-- NeoHub.NativeZkVerifier/                    # ProofType.Zk adapter -> L1 native accelerator
|-- NeoHub.MessageRouter/                       # L1<->L2 / L2<->L2 message queues
|-- NeoHub.TokenRegistry/                       # canonical L1<->L2 asset mappings
|-- NeoHub.DARegistry/                          # DA layer commitment store
|-- NeoHub.DAValidator/                         # DA attestation / validation gate
|-- NeoHub.L1TxFilter/                          # optional L1->L2 admission filter
|-- NeoHub.SequencerRegistry/                   # per-chain committee membership
|-- NeoHub.SequencerBond/                       # bonded stake + slashing
|-- NeoHub.ForcedInclusion/                     # anti-censorship forced-tx queue
|-- NeoHub.OptimisticChallenge/                 # Phase-3 fraud-proof challenge window
|-- NeoHub.GovernanceController/                # L2 admission policy / verifier upgrades
|-- NeoHub.GovernanceFraudVerifier/             # v1/v2 structural fraud verifier
|-- NeoHub.RestrictedExecutionFraudVerifier/    # v3 restricted re-execution verifier
|-- NeoHub.EmergencyManager/                    # pause / escape hatch
|-- NeoHub.MpcCommitteeVerifier/                # foreign-chain committee verifier
|-- NeoHub.MpcCommitteeFraudVerifier/           # external-bridge equivocation slasher
|-- NeoHub.ExternalBridgeRegistry/              # externalChainId -> verifier routing
|-- NeoHub.ExternalBridgeEscrow/                # foreign-chain asset escrow
|-- NeoHub.ExternalBridgeBond/                  # foreign-chain committee bonds
`-- NeoHub.ExternalBridgeStubVerifier/          # dev/test only; excluded from production bundle
```

There are 24 `NeoHub.*` projects in this directory. `neo-hub-deploy` emits a
23-step production bundle that excludes `NeoHub.ExternalBridgeStubVerifier`.
`NeoHub.NativeZkVerifier` is part of that production bundle: it validates
`ProofType.Zk` envelopes and registered verification-key ids, then calls the
configured L1 native accelerator for `verifyZkProof(...)`.

The L2 native contract set lives in Neo core under
`../external/neo/src/Neo/SmartContract/Native/`:
`L2SystemConfigContract`, `L2BatchInfoContract`, `L2MessageContract`,
`L2BridgeContract`, `L2FeeContract`, `L2PaymasterContract`,
`L2NativeExternalBridgeContract`, `L2AccountAbstraction`,
`BridgedNep17Contract`, and `L2InteropVerifier`.

## Storage layout

Each deployable NeoHub contract reserves single-byte prefixes for each of its
persistent maps. Convention:

- `0x01..0x7F` - domain data.
- `0x80..0xFE` - auxiliary indexes / nonces / counters.
- `0xFF` - owner / governance pointer.

Within a key, multi-byte integers are little-endian (matches Neo standard) and
addresses / hashes are their raw 20- or 32-byte payloads.

## See also

- [`../doc.md`](../doc.md) - master architecture spec.
- [`../ARCHITECTURE.md`](../ARCHITECTURE.md) - English distilled summary.
- [`../external/neo/tests/Neo.UnitTests/SmartContract/Native/UT_L2NativeContracts.cs`](../external/neo/tests/Neo.UnitTests/SmartContract/Native/UT_L2NativeContracts.cs) - registration and behavior tests for N4 native contracts.
- [`../src/Neo.L2.Abstractions/`](../src/Neo.L2.Abstractions/) - companion off-chain models; on-chain encodings here must match the canonical formats in `Neo.L2.Batch.BatchSerializer`, `Neo.L2.State.MessageHasher`, and `Neo.L2.Bridge.DepositPayload`.
