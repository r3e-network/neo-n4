# Neo Elastic Network Smart Contracts

This directory contains the on-chain contracts for the **NeoHub** (L1 contract suite) and
**L2 Native** contracts that ship with every Neo 4 L2 chain.

## Build

Each contract is a plain `Microsoft.NET.Sdk` class library that references
[`Neo.SmartContract.Framework`](https://github.com/neo-project/neo-devpack-dotnet) via
`ProjectReference`. The framework is vendored as a git submodule at
`external/neo-devpack-dotnet/src/Neo.SmartContract.Framework/` (init via
`git submodule update --init --recursive` after cloning). Override `NeoDevpackPath`
in `Directory.Build.props` if you want to point at a local fork.

```bash
dotnet build contracts/                      # type-checks all contracts
nccs contracts/NeoHub.ChainRegistry/         # generates .nef + .manifest.json (per project)
```

The `Directory.Build.props` here also wires `nccs` into the `BeforeBuild` target with
`ContinueOnError=true` — when `nccs` is not on `PATH`, type-checking still succeeds and only
the bytecode artifacts are skipped.

## Layout

```
contracts/
├── NeoHub.ChainRegistry/        # L2 chain admission + config registry
├── NeoHub.SharedBridge/         # canonical asset escrow + deposit/withdraw
├── NeoHub.SettlementManager/    # batch submission + finalization + state-root canon
├── NeoHub.VerifierRegistry/     # pluggable proof verifier dispatch
├── NeoHub.MessageRouter/        # L1↔L2 / L2↔L2 message queues
├── NeoHub.TokenRegistry/        # canonical L1↔L2 asset mappings
├── NeoHub.DARegistry/           # DA layer commitment store
├── NeoHub.GovernanceController/ # L2 admission policy / verifier upgrades
├── NeoHub.EmergencyManager/     # pause / escape hatch
├── L2Native.L2BridgeContract/         # L2-side mint/burn for bridged assets
├── L2Native.L2MessageContract/        # L2-side cross-chain message I/O
├── L2Native.L2BatchInfoContract/      # exposes chainId, batchNumber, L1FinalizedHeight
├── L2Native.L2FeeContract/            # sequencer / prover / DA fee accounting
├── L2Native.L2PaymasterContract/      # stablecoin / sponsored fee paymaster
└── L2Native.L2SystemConfigContract/   # config sync from NeoHub
```

## Storage layout

Each contract reserves single-byte prefixes for each of its persistent maps. Convention:
- `0x01..0x7F` — domain data (keep small to leave room for many maps).
- `0x80..0xFE` — auxiliary indexes / nonces / counters.
- `0xFF` — owner / governance pointer.

Within a key, multi-byte integers are little-endian (matches Neo standard) and addresses /
hashes are their raw 20- or 32-byte payloads.

## See also

- [`../doc.md`](../doc.md) — master architecture spec (§3.2 NeoHub, §13 L2 native contracts).
- [`../ARCHITECTURE.md`](../ARCHITECTURE.md) — English distilled summary.
- [`../src/Neo.L2.Abstractions/`](../src/Neo.L2.Abstractions/) — companion off-chain models;
  on-chain encodings here MUST match the canonical formats in `Neo.L2.Batch.BatchSerializer`,
  `Neo.L2.State.MessageHasher`, and `Neo.L2.Bridge.DepositPayload`.
