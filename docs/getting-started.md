# Getting Started

> 5-minute walkthrough from clone to running devnet.

## Prerequisites

- .NET 10 SDK (`dotnet --version` ≥ `10.0.0`).
- The [`neo-project/neo`](https://github.com/neo-project/neo) Neo 4 core, vendored as a git
  submodule at `external/neo` (the `NeoCorePath` property in `Directory.Build.props` defaults
  to the submodule path). Run `git clone --recurse-submodules` or
  `git submodule update --init --recursive` after a regular clone.

## Step 1 — Clone and check the toolchain

```bash
git clone --recurse-submodules https://github.com/r3e-network/neo-n4
cd neo-n4
dotnet --version            # expect 10.0.x
ls external/neo/src/Neo     # confirm the neo-project/neo submodule
```

If you forgot `--recurse-submodules` at clone time:

```bash
git submodule update --init --recursive
```

## Step 2 — Run the test suite

```bash
dotnet test Neo.L2.sln /p:NuGetAudit=false
```

Expected: **1013 tests pass across 29 projects**, ~10 seconds end-to-end.

If your machine doesn't have network access, `/p:NuGetAudit=false` is what suppresses the
audit hop to nuget.org.

## Step 3 — Run the devnet demo

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5
```

You should see:

```
┌─────────────────────────────────────────────┐
│  Neo Elastic Network — devnet runner v0.2    │
│  chainId = 1001, batches =  5                      │
└─────────────────────────────────────────────┘

[persist] in-memory stores (devnet default — data lost on restart)

[wire] asset registry: 1 mapping (GAS L1=0x11111111…1111 → L2=0x22222222…2222)
[wire] 4 validators, attestation threshold = 3
[wire] sequencer committee: 3 active members
[wire] keyed state store + oracle (0 initial entries)
[wire] DA writer = InMemoryDAWriter (mode=External)

────── batch #1 ──────
  [deposit] minted 1000000 → Alice (nonce=1)
  [withdraw] staged 10000 from Alice → Bob (nonce=1)
  [DA]   layer=External commitment=0xc7a1cb54…7819b6
  [seal] preRoot=0x00000000…000000 postRoot=0xe863d100…d70776 verify=True
[…]
✅ devnet run complete.
```

What just happened:

- **3 sequencers** registered into a committee (in-memory backing for `NeoHub.SequencerRegistry`).
- **5 batches** ran — each containing a deposit + withdrawal — through `ReferenceBatchExecutor`.
- The **`KeyedStateStore`** held real (asset, holder) → balance entries; each batch's
  `preStateRoot` equals the previous `postStateRoot` (state-root continuity guaranteed).
- Each batch published its payload to the **DA writer**; the resulting commitment was
  bound into the proof's public inputs.
- **Stage-0 multisig prover** signed canonical public-input bytes; **`AttestationVerifier`**
  validated 3-of-4 signatures.
- **Alice's net balance** was checked against the expected sum at the end.

### Persistent devnet (state survives restart)

Add `--data-dir <path>` to swap every store to RocksDB:

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir /tmp/neo-l2-devnet1
# [persist] RocksDB-backed stores at /tmp/neo-l2-devnet1 (data survives restart)

# Re-run with 0 batches — committee + state are rehydrated from disk
dotnet run --project tools/Neo.L2.Devnet -- 0 --data-dir /tmp/neo-l2-devnet1
# [wire] sequencer committee: 3 active members  (no re-registration)
# [wire] keyed state store + oracle (5 initial entries)
```

Layout under `<path>/`: `state/`, `rpc-proofs/`, `sequencer/`, `da/`. See
[`docs/persistence.md`](./persistence.md) for the production wiring story.

## Step 4 — Generate a NeoHub deploy bundle

```bash
dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan --plan deploy-plan.json --output bundle.json
```

`bundle.json` is a topologically-sorted, dependency-resolved sequence of 13 contract
deploy invocations — every `$step:<name>` placeholder substituted with deterministic
stub hashes. Production deployments feed the bundle to a wallet-equipped runner that
signs + sends each invocation.

The `plan` command also prints required post-deploy actions when the bundle does not
fully wire the system on its own — e.g.:

```
Required post-deploy actions:
  - SequencerBond.RegisterSlasher(OptimisticChallenge)  # enable Phase-3 challenge slashing
  - ChainRegistry.SetGovernanceController(GovernanceController)  # enable §16.1 admission policy
  - VerifierRegistry.SetGovernanceController(GovernanceController)  # enable §16 council-veto path
```

## Step 5 — Build a smart contract

```bash
dotnet build contracts/NeoHub.ChainRegistry /p:NuGetAudit=false /p:DisableNccs=true
```

The `DisableNccs=true` toggle skips the `nccs`-based `.nef`/`.manifest.json` emission step
and keeps just the C# type-check. Install
[`nccs`](https://github.com/neo-project/neo-devpack-dotnet) on `PATH` to flip the toggle off
and emit deployable bytecode.

## Where to go next

- **`ARCHITECTURE.md`** — English distillation of the master spec (`doc.md`).
- **`docs/architecture-walkthrough.md`** — narrative tour mapping `doc.md` sections to code.
- **`IMPLEMENTATION_STATUS.md`** — per-phase coverage matrix and out-of-scope list.
- **`AGENTS.md`** — guide for AI-assisted contributors.
- **`CONTRIBUTING.md`** — code style, naming, PR checklist.
- **Source XML docs** — every public type points at a `doc.md` section; IDE tooltips
  become navigation aids.

## Troubleshooting

**Build fails with `NU1900` / nuget audit errors.**
Add `/p:NuGetAudit=false` to your build command. The repo's `Directory.Build.props` already
sets `NuGetAudit=false`, but some restore code paths re-evaluate the property.

**`dotnet test` reports `Could not find external/neo/src/Neo/Neo.csproj`.**
The neo-project/neo submodule isn't initialized. Run
`git submodule update --init --recursive` from the repo root, or re-clone with
`git clone --recurse-submodules`. To point at a different checkout, override on the
command line: `dotnet build /p:NeoCorePath=/path/to/neo/src`.

**Contracts don't emit `.nef` files.**
That's expected without `nccs`. Install nccs from `neo-project/neo-devpack-dotnet` and run
`dotnet build` without `DisableNccs=true`.

**SP1 prover returns `NotImplemented`.**
The `bridge/neo-zkvm-bridge` Rust crate defaults to a feature-gated stub. Build with
`cargo build --release --features real-prover` (and have `../neo-zkvm` available) to enable
the real prover.
