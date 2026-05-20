# Getting Started

> 5-minute walkthrough from clone to running devnet.

## Prerequisites

- .NET 10 SDK (`dotnet --version` ≥ `10.0.0`).
- The [`r3e-network/neo`](https://github.com/r3e-network/neo) Neo core fork, vendored
  as a git submodule at `external/neo` on branch `r3e/neo-n4-core` (the
  `NeoCorePath` property in `Directory.Build.props` defaults to the submodule path).
  L1 core work uses the same fork's `r3e/neo-n3-core` branch, based on upstream
  `master-n3`; do not replace the default L2 submodule pointer for normal `neo-n4`
  builds.
  Run `git clone --recurse-submodules` or `git submodule update --init --recursive`
  after a regular clone.

## Step 1 — Clone and check the toolchain

```bash
git clone --recurse-submodules https://github.com/r3e-network/neo-n4
cd neo-n4
dotnet --version            # expect 10.0.x
ls external/neo/src/Neo     # confirm the r3e-network/neo submodule
```

If you forgot `--recurse-submodules` at clone time:

```bash
git submodule update --init --recursive
```

## Step 2 — Run the test suite

```bash
dotnet test Neo.L2.sln /p:NuGetAudit=false
```

Expected: **1455 tests pass across 34 projects**, ~10 seconds end-to-end.

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

[wire] asset registry: 5 platform mappings (NEO 0→8, GAS/USDT/USDC/BTC fixed decimals; sample GAS L1=0x11111111…1111 → L2=0xf684fdbd…ee28)
[wire] 4 validators, attestation threshold = 3
[wire] sequencer committee: 3 active members
[wire] keyed state store + oracle (0 initial entries)
[wire] DA writer = NeoFsLikeDAWriter (mode=NeoFS)

────── batch #1 ──────
  [deposit] minted 1000000 → Alice (nonce=1)
  [withdraw] staged 10000 from Alice → Bob (nonce=1)
  [DA]   layer=NeoFS commitment=0xc7a1cb54…7819b6
  [seal] preRoot=0x00000000…000000 postRoot=0xe863d100…d70776 verify=True
[…]
✅ devnet run complete.
```

What just happened:

- **3 sequencers** registered into a committee (in-memory backing for `NeoHub.SequencerRegistry`).
- **5 batches** ran — each containing a deposit + withdrawal — through `ReferenceBatchExecutor`.
- **5 platform asset mappings** were registered: NEO maps 0→8, GAS maps 8→8,
  USDT/USDC map 6→6, and BTC maps 8→8. The L2 asset ids for this catalog are
  built into the r3e N4 core fork so L1↔L2 and L2↔L2 routes use the same symbols
  and decimal policy.
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
dotnet run --project tools/Neo.Hub.Deploy -- scaffold \
    --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan \
    --plan deploy-plan.json --output bundle.json
```

`bundle.json` is a topologically-sorted, dependency-resolved sequence of 23 contract
deploy invocations — every `$step:<name>` placeholder substituted with deterministic
stub hashes. Production deployments feed the bundle to a wallet-equipped runner that
signs + sends each invocation.

The `plan` command also prints required post-deploy actions when the bundle does not
fully wire the system on its own — e.g.:

```
Required post-deploy actions:
  - SequencerBond.RegisterSlasher(OptimisticChallenge)
      # enable Phase-3 challenge slashing
  - ChainRegistry.SetGovernanceController(GovernanceController)
      # enable §16.1 admission policy
  - VerifierRegistry.SetGovernanceController(GovernanceController)
      # enable §16 council-veto path
```

## Step 5 — Build a smart contract

```bash
dotnet build contracts/NeoHub.ChainRegistry/NeoHub.ChainRegistry.csproj /p:NuGetAudit=false
nccs contracts/NeoHub.ChainRegistry/NeoHub.ChainRegistry.csproj \
    --output contracts/NeoHub.ChainRegistry/bin/sc
```

`dotnet build` type-checks the C# contract surface. `nccs` emits the deployable
`.nef` and `.manifest.json` artifacts consumed by deploy tooling and CI. Keep
the two steps explicit so a local rehearsal matches the production pipeline.

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
The r3e-network/neo submodule isn't initialized. Run
`git submodule update --init --recursive` from the repo root, or re-clone with
`git clone --recurse-submodules`. To point at a different checkout, override on the
command line: `dotnet build /p:NeoCorePath=/path/to/neo/src`.

**Contracts don't emit `.nef` files.**
Run `nccs <contract.csproj> --output <contract>/bin/sc` explicitly after the
contract type-check. If `nccs` is unavailable, install it from
`r3e-network/neo-devpack-dotnet` or with `dotnet tool install -g Neo.Compiler.CSharp`.

**Want real ZK proofs (Stage-2 validity)?**
Build the Rust prover daemon: `CPATH=~/.local/include cargo build --release -p neo-zkvm-host`
(needs the SP1 toolchain — install via `sp1up`). Run it as
`target/release/prove-batch daemon --watch <queue-dir>`; the .NET sequencer drops sealed
batches into the queue dir and the daemon emits matching `*.proof.bin` + `*.proof.vk` for
L1 submission. See `docs/launching-an-l2.md` § "Prover deployment".
