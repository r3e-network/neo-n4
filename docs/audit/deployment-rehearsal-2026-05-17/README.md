# Deployment Rehearsal Report - 2026-05-17

> Superseded implementation note, 2026-05-18: L2 system contracts now live in
> the r3e Neo core fork as native contracts registered at genesis. Current CI
> compiles deployable `NeoHub.*` and `Sample.*` contracts, and verifies the 10
> L2 native contracts through `external/neo` unit tests.

This report records the Windows + WSL2 deployment rehearsal performed on
2026-05-17. It is a local, reproducible rehearsal of the Neo N4 operator path:
devnet execution, NeoHub deploy-plan generation, contract artifact generation,
external-bridge committee setup, local EVM foreign-router deployment, and test
verification.

## Result

Status: local rehearsal passed.

The remaining deployment gate is a funded public Neo N4 devnet/testnet run with
real RPC endpoints, operator wallets, governance/multisig accounts, and foreign
testnet routes. Those credentials and funded accounts were not present in the
workspace, so this rehearsal did not claim public-network finality.

## One-command Local Rehearsal

The repeatable no-credential rehearsal is automated in:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\deployment\run-local-rehearsal.ps1
```

The script writes ignored scratch output under
`artifacts/local-deployment-rehearsal/<timestamp>` by default. It generates
local watcher keys, deploy-plan artifacts, compiled contract artifacts, EVM
Anvil router state, raw logs, and a machine-readable `summary.json` without
requiring public RPC endpoints, funded wallets, faucet access, or governance
accounts.

The latest local run in this workspace completed with `status: passed` at
`D:\Git\neo-n4\artifacts\local-deployment-rehearsal\20260517-200257`.

## Environment

- Host: Windows PowerShell, repo at `D:\Git\neo-n4`.
- Foreign-chain rehearsal: WSL2 with Foundry/Anvil.
- .NET: `dotnet` for the Neo L2 solution and CLIs.
- Neo compiler: `Neo.Compiler.CSharp` / `nccs` 3.9.1.
- Rust: Cargo for the Solana foreign-router program.

## Committed Artifacts

| Artifact | Purpose |
| --- | --- |
| `hub/deploy-plan.json` | 22-step NeoHub production deploy scaffold. |
| `hub/deploy-bundle.json` | Resolved deploy bundle with post-deploy actions. |
| `external-bridge/watchers.pubs` | Public watcher keys used for the rehearsal committee. |
| `external-bridge/watchers.eth-addresses.txt` | EVM addresses derived from the watcher public keys. |
| `external-bridge/committee-blob.out` | Neo-side committee blob and EVM-side member list. |
| `external-bridge/deploy-bundle.out` | Dual-side Neo + EVM external-bridge registration checklist. |

Local raw logs were retained in the working tree under `logs/*.log` and
`external-bridge/anvil-*.log`, but `.gitignore` intentionally excludes raw logs
from commits. This report copies the relevant pass/fail evidence instead.

Private watcher keys were generated only under the scratch directory outside the
repo and are not committed.

## NeoHub Deploy Planner

Commands executed:

```bash
dotnet run --project tools/Neo.Hub.Deploy -- scaffold \
    --output docs/audit/deployment-rehearsal-2026-05-17/hub/deploy-plan.json

dotnet run --project tools/Neo.Hub.Deploy -- plan \
    --plan docs/audit/deployment-rehearsal-2026-05-17/hub/deploy-plan.json \
    --output docs/audit/deployment-rehearsal-2026-05-17/hub/deploy-bundle.json
```

Outcome:

- 22 NeoHub production deployment steps resolved.
- Post-deploy actions were surfaced for slasher registration, governance
  controller wiring, verifier registry setup, MPC committee verifier setup,
  external bridge registry setup, and external bridge slashing identity.
- The bundle is deterministic and operator-readable; real deployment still
  requires a wallet to submit each `ContractManagement.Deploy` in order.

## Contract Artifacts

The rehearsal found that direct `dotnet build` type-checks the smart contracts,
but does not reliably emit `.nef` and `.manifest.json` artifacts through the
old MSBuild prebuild hook. The corrected production path is now explicit:

```bash
dotnet build <contract.csproj> /p:NuGetAudit=false --nologo
nccs <contract.csproj> --output <contract>/bin/sc
```

All 22 production contracts in the NeoHub deploy plan were type-checked and compiled with
`nccs`. `neo-hub-deploy verify` then reported:

```text
Contract artifact check: 22 ok / 0 missing of 22 total.
```

Note: `neo-hub-deploy verify --rpc http://127.0.0.1:20332` was used as an
artifact gate in this rehearsal. No local Neo RPC node was running, so this does
not constitute an on-chain public-network deployment.

Follow-up fixed in this commit:

- Removed the stale automatic `nccs` MSBuild hook from
  `contracts/Directory.Build.props`.
- Updated CI at the time to run `dotnet build` and `nccs` explicitly for every
  deployable contract project. The current pipeline compiles `NeoHub.*` and
  `Sample.*`, then verifies native contracts through `external/neo` tests.
- Updated English and Chinese getting-started docs to describe the explicit
  artifact pipeline.

## Local Devnet

Commands executed:

```bash
dotnet run --project tools/Neo.L2.Devnet -- 5
dotnet run --project tools/Neo.L2.Devnet -- 5 --data-dir <scratch>/persistent-default
dotnet run --project tools/Neo.L2.Devnet -- 0 --data-dir <scratch>/persistent-default
dotnet run --project tools/Neo.L2.Devnet -- 3 --executor counter --data-dir <scratch>/counter
dotnet publish tools/Neo.L2.Devnet/Neo.L2.Devnet.csproj -c Release -r linux-x64 --self-contained true \
    /p:NuGetAudit=false /p:PublishSingleFile=false -o <scratch>/devnet-linux-x64
cd external/neo-riscv-vm && cargo build -p neo-riscv-host --release
LD_LIBRARY_PATH=<repo>/external/neo-riscv-vm/target/release:<scratch>/devnet-linux-x64 \
    <scratch>/devnet-linux-x64/neo-l2-devnet 1 --executor riscv
dotnet run --project tools/Neo.L2.Devnet -- 3 --executor neovm --data-dir <scratch>/neovm  # legacy compatibility
dotnet run --project tools/Neo.L2.Devnet -- 2 \
    --config samples/general-rollup.config.json \
    --data-dir <scratch>/general-rollup-config
```

Outcome:

| Mode | Evidence |
| --- | --- |
| Default in-memory, 5 batches | Audit passed, state-root continuity held, `alice balance` matched `14850000`. |
| Persistent default, 5 batches | Audit passed and state survived to disk. |
| Persistent rehydrate, 0 batches | State store rehydrated with 1 initial entry; committee active. |
| Counter executor, 3 batches | Audit passed, 3 state entries, `alice balance` matched `5940000`. |
| NeoVM2/RISC-V executor, 1 batch | WSL2 loaded `libneo_riscv_host.so`; audit passed through `RiscVTransactionExecutor`. |
| Legacy NeoVM executor, 3 batches | Native-contract bootstrap succeeded for compatibility coverage. |
| Sample general-rollup config, 2 batches | Config label reflected `Optimistic`, `L1` DA, delayed exit; audit passed. |

## External Bridge and EVM Router

Commands executed:

```bash
dotnet run --project tools/Neo.External.Bridge.Cli -- genkey --out <scratch>/watcher-N.priv
dotnet run --project tools/Neo.External.Bridge.Cli -- committee-blob \
    --pubs-file docs/audit/deployment-rehearsal-2026-05-17/external-bridge/watchers.pubs
dotnet run --project tools/Neo.External.Bridge.Cli -- deploy-bundle ...
```

Committee output:

- Committee size: 3.
- Threshold used for the rehearsal bundle: 2.
- Neo committee blob:
  `0x02da022c86171bbad8799ea58c71a7cb1c42a975351ca64dc1e8576d464768eba102f7cab0d6865ae6f54e9b96940bb31721faccffc7d38ba812a62e81ccaf3e22bd02dea8dd9b7e953337f1b9e4ce5037f626de63ae5cf155ea6fe7976f3054ec7cdf`.
- EVM committee members:
  `0x1ba6c6d6ce04a5b403a69fd6dbf3b0154bbed10e`,
  `0x2b1036849fdaa17d5bd1d9e1e8709f442c211f7d`,
  `0xbcc1b39b251d1e571c034348d659e55adb772b95`.

Anvil rehearsal:

- Deployed `NeoExternalBridgeRouter` to
  `0x5FbDB2315678afecb367f032d93F642f64180aa3`.
- Registered the 3-member committee with threshold `2`.
- Verified `threshold()` returned `2`.
- Verified `committee(0)` returned
  `0x1bA6C6d6Ce04a5B403A69fd6Dbf3b0154bbED10E`.
- Called `lockETHAndSend(...)` with `1` wei.
- Verified `lockedBalances(native)` returned `1`.

The EVM deploy bundle file uses dummy Neo addresses for checklist generation;
it is not a real public-network deployment bundle.

## Verification

The following verification commands passed during the rehearsal:

```bash
dotnet test Neo.L2.sln --no-restore --nologo /p:NuGetAudit=false
dotnet test tests/Neo.Hub.Deploy.UnitTests/Neo.Hub.Deploy.UnitTests.csproj --no-restore --nologo /p:NuGetAudit=false
dotnet test tests/Neo.External.Bridge.Cli.UnitTests/Neo.External.Bridge.Cli.UnitTests.csproj --no-restore --nologo /p:NuGetAudit=false
dotnet test tests/Neo.L2.Devnet.UnitTests/Neo.L2.Devnet.UnitTests.csproj --no-restore --nologo /p:NuGetAudit=false
dotnet test tests/Neo.L2.IntegrationTests/Neo.L2.IntegrationTests.csproj --no-restore --nologo /p:NuGetAudit=false
dotnet test tests/Neo.L2.Bridge.UnitTests/Neo.L2.Bridge.UnitTests.csproj --no-restore --nologo /p:NuGetAudit=false
wsl.exe bash -lc "cd /mnt/d/Git/neo-n4/external/foreign-contracts/eth && ~/.foundry/bin/forge test -vv"
wsl.exe bash -lc "cd /mnt/d/Git/neo-n4/external/foreign-contracts/sol && cargo test"
```

Observed results:

- Full .NET solution test pass completed successfully.
- Hub deploy unit tests: 43 passed.
- External bridge CLI unit tests: 16 passed.
- Devnet unit tests: 29 passed.
- L2 integration tests: 25 passed.
- L2 bridge unit tests: 88 passed.
- Foundry EVM router tests: 21 passed.
- Solana foreign-router cargo tests: 4 passed.
- Explicit `dotnet build` + `nccs` pipeline verified deployable contract
  artifacts. Current native-contract verification is handled by the r3e Neo
  core unit tests because N4 L2 system contracts are built in at genesis.

## Public-Network Gate

To close the final production deployment gate, run the same flow against real
funded networks:

1. Provide Neo N4 devnet/testnet RPC endpoints and funded operator accounts.
2. Submit all NeoHub deploy-bundle invocations in order through the production
   wallet or multisig process.
3. Record real contract hashes returned by `ContractManagement.Deploy`.
4. Run all post-deploy actions from `hub/deploy-bundle.json`.
5. Deploy at least one foreign-router instance on an EVM testnet.
6. Register a real watcher committee on both sides.
7. Exercise deposit, withdrawal, replay rejection, duplicate-signer rejection,
   wrong-chain rejection, watcher restart, and emergency-path drills.
8. Attach GitHub Actions run URLs after the pushed CI workflow executes remotely.
