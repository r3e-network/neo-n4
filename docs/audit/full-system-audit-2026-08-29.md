# Neo N4 — Full System Audit (2026-08-29)

Objective: audit the whole repository and verify each system for correctness,
professionalism, consistency, completeness, security, efficiency and robustness.

Method: nine parallel subsystem reviews — including a dedicated pass over the L1 money path
(all 26 `contracts/` projects read in full) — plus an independent executable-gate run
on this workstation (Windows 11 / net10.0 / Git Bash). Every finding below is tagged with
its evidence tier so that measured facts are never mixed with inference.

Evidence tiers:

- **[E1] Executed** — observed in a command result on this machine.
- **[E2] Read-verified** — I opened the cited code and confirmed the mechanism myself.
- **[E3] Reported** — surfaced by a subsystem reviewer, mechanism not independently re-confirmed.

Repository scale measured at HEAD `728e630a`: 103 first-party `.csproj`; 1,138 C# files
(158,547 lines — 72,905 production / 85,642 test); 101 Rust files (22,070 lines);
~119,281 TypeScript lines; 27 `src/` projects, 26 `contracts/` projects, 7 `tools/`,
38 test projects, 3 watcher crates.

---

## 1. Executable gates — what actually ran

| Gate | Result | Evidence |
| --- | --- | --- |
| `dotnet build Neo.L2.sln` | **0 warnings, 0 errors** | [E1] `TreatWarningsAsErrors=true` genuinely holds |
| `dotnet test Neo.L2.sln` | 38 assemblies, **2,814 passed / 0 failed / 55 skipped** | [E1] |
| `dotnet format --verify-no-changes` | **exit 0** | [E1] style discipline is real, not aspirational |
| `mdbook build` | **exit 0** | [E1] |
| Devnet L0 (`-- 5`) | **complete**, audit pass ✅ | [E1] 5 batches sealed, 5 deposits, 5 withdrawals, 5 Multisig proofs verified (391 B each), state-root continuity, DA availability, `alice balance 14850000 (expected 14850000)` |
| 4 sample chain configs | **all complete** | [E1] general-rollup / gaming-rollup / exchange-validium / privacy-sidechain |
| TypeScript SDK (`vitest`) | 23 passed, 3 skipped (live conformance, visible) | [E1] |
| Rust: `neo-execution-core` | 14 passed | [E1] |
| Rust: `neo-bridge-watcher-eth` | 85 passed | [E1] |
| Rust: `neo-bridge-watcher-tron` | 7 passed | [E1] |
| Rust: `neo-bridge-watcher-sol` | 9 passed | [E1] |
| Rust: `neo-n4-sdk` | 17 passed, 3 ignored | [E1] |
| `NeoHub.Sp1Groth16Verifier` unit tests | **12 passed** — real committed SP1 6.2.1 Groth16 proof verified through compiled NEF, plus per-field tamper rejection | [E1] via reviewer; `tests/fixtures/sp1-groth16-positive-vector-v1.json` |
| `dotnet test Neo.L2.Executor.RiscV.UnitTests` | **31 passed / 0 skipped** (was 21/10 skipped) | [E1] — I built `neo_riscv_host` and closed the gate; see §3.1 |
| Python SDK (`unittest`) | **20 passed** (13 client + 7 shared-vector conformance) | [E1] `python -m unittest tests.test_client` and `…test_conformance_offline` under an ephemeral `uv` env (`cryptography` is the only third-party dep; absent from the interpreter, and no `pytest` is installed) |
| `cargo test --workspace` | **FAILS on Windows** | [E1] `sp1-jit 6.2.1` uses Linux-only `libc::ftruncate` / `shm_unlink` / `create_anonymous_file` |

Overall: the system genuinely works end-to-end in its default configuration, and the
byte-level proof-verifier is validated against a real SP1 proof rather than a mock. That
is an unusually strong baseline. The defects below are concentrated in (a) paths that
silently do not execute, and (b) combinations of subsystems that were never composed.

### 1.1 Coverage ledger — what "full" meant here

Every first-party subsystem is accounted for, including the ones where the answer was
"reviewed, nothing material" and the ones that were deliberately not exercised.

| Subsystem | Inventory | Assessed in |
| --- | --- | --- |
| L1 contracts (money path) | 26 `contracts/` projects, read in full | H12, H13, §5 contract group, §6 contract LOWs, §7 cleared list |
| L2 node plugins | 8 `src/Neo.Plugins.L2*` | C1, H1, H10, §5, §7 |
| Core off-chain libs | 19 `src/Neo.L2.*` libs (incl. `Executor`, `Executor.RiscV`, `State`, `Proving`, `Messaging`, `Persistence`, `Telemetry`, 2 `*.Rpc` adapters) | C2, H10, H11, §5 by topic |
| Neo core fork | `external/neo` submodule (10 native L2 contracts, plugin host) | H1 basis (`Plugin.cs:75`), §9 gitlink gate (H9, §3.2) — reviewed at the integration seam, not re-audited as upstream |
| Proving stack (Rust) | 5 `bridge/` crates (`neo-execution-core`, `neo-zkvm-{guest,host}`, `neo-zkvm-gateway-{guest,host}`) | §1 (executed where Windows-possible), §3.3, §7, §10 |
| Bridge watchers | 3 crates (`eth`, `sol`, `tron`) | H4, H5, §1 |
| SDKs | TS, Python, Rust + shared `sdk/conformance/vectors/v1.json` | H7, §1 (all three suites executed), §7 |
| Web explorer | `sdk/web-explorer/index.html` (317 lines) | Read; one `fetch`, zero `innerHTML`, no material finding; **not** exercised in a browser |
| Operator tools | 7 `tools/*` projects (CLI, Devnet, Deploy, Explore, Faucet, Bridge, External-Bridge CLIs) | §5, §6, H13's deploy sequencing, §9 |
| Samples / reference executor | `samples/contracts/` 2 (`CrossChainGreeter`, `WithdrawalDemo`), `samples/executors/` 1 | §5 (semantic-id parity), §7 |
| Test inventory | 38 projects / 38 executed assemblies | §3.1 (the silent-skip defect), §1 |
| Persistence | `Neo.L2.Persistence` (RocksDB) + atomic CAS paths | §5 determinism, §7 (deliberate async-`Put` design, already adjudicated in `TASKS.md`) |
| Docs / spec / book | `doc.md`, `ARCHITECTURE.md`, `docs/` (incl. `telemetry.md`, wire formats), `book/`, `tools/manuscript`, `tools/docs` generators, zh mirrors | Consistency rows in §5/§6, §9 sweep; `mdbook build` clean (§1) |
| CI and release gates | `.github/workflows/*`, `scripts/ci/*`, coverage + audit scripts | §3.2–§3.5, §1 |
| **Not exercised** | L1/L2 live deployment, `nccs` artifact emission, `forge`/Solidity tests, coverage gate (needs absent `pwsh`), `cargo deny` (not installed), SP1 host/guest proving (Windows-incompatible workspace test) | §10, stated as unverified rather than as passing |

Untracked local residue, for the record: `CODEX_DEEP_AUDIT/` (317 MB — a previous agent's
downloaded foundry toolchain plus an empty `screenshots/`), `target/` (31 GB), `coverage/`
(60 MB) and `artifacts/` (547 MB). All four are correctly gitignored (`.gitignore:19,20,25,50`)
and none is repo content; I left them in place. The only hygiene point is that a tool-cache
directory named like an audit deliverable sits in the repo root.

---

## 2. Critical findings

### C1 — Deposits and L1→L2 messages collide in the batcher inbox; enabling both halts the chain [E2 verified]

> **Status — remediated 2026-08-29 (after this report was written).** `L1MessageDrain` now rejects
> exact-duplicate messages by content equality and scopes the `(sourceChainId, nonce)` slot claim to
> `MessageType.Deposit`, which is the only family whose L2 consumer keys on that slot — so the two
> independent nonce spaces coexist instead of halting the batcher. The comparator was widened to a
> total order over every field because the merged sequence feeds `l1MessageHash` and `List.Sort` is
> unstable. `UT_L1MessageDrain` gained the deposit+router combined-drain pin called for in §9 plus
> three regressions. The alternative in the Fix note below (a reserved deposit source-chain id, and
> mirroring it in the L2 consumed-key prefixes) was **not** taken: it would have changed a paired
> off-chain ↔ on-chain byte format for a liveness bug the dedup scoping already closes.

Both L1→L2 channels stamp `SourceChainId = 0` and each keeps an independent per-target-chain
nonce counter that starts at 1:

- `contracts/NeoHub.SharedBridge/SharedBridgeContract.cs:24` `PrefixDepositNonce = 0x01 + chainId(4B)`,
  incremented at `:184`/`:592` (`var next = current + 1`).
- `contracts/NeoHub.MessageRouter/MessageRouterContract.cs:24` `PrefixL1ToL2Nonce = 0x01 + targetChainId(4B)`,
  and `:142` encodes the message with a literal source chain id of `0u`.
- `src/Neo.L2.Bridge/SharedBridgeDepositRecord.cs:127` sets `SourceChainId = 0`.

`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:133-149` wires exactly these two drains together,
and `src/Neo.L2.Messaging/L1MessageDrain.cs:113-121` deduplicates on the tuple
`(SourceChainId, Nonce)` — which omits the message kind. The first deposit and the first
`EnqueueL1ToL2` message for any chain therefore both key to `(0,1)`.

Effect: `Combine` throws `InvalidOperationException`, the batcher stops sealing, and the L2
freezes — all deposits and withdrawals halt. It is reachable by any account for a small GAS
fee. The messages themselves *are* distinguishable (`MessageType.Deposit` vs. router types);
only the dedup key is under-scoped. On-chain consumption uses separate namespaces
(`PrefixDepositConsumed` / `PrefixInboundConsumed`), so no funds are lost or double-credited —
this is a liveness defect, not a theft.

To the authors' credit the guard is fail-closed and its message names this exact hazard
("SharedBridge deposit nonces and MessageRouter nonces must not collide under sourceChainId=0"),
so this was foreseen. But a guard that turns an ordinary usage pattern into a chain halt is a
design gap, not a mitigation. C1 also compounds with A2 below: an unhandled exception on the
`Blockchain.Committed` path stops the node, so the halt can escalate to sequencer outage.

Fix: key on `(SourceChainId, MessageType-domain, Nonce)`, or give the deposit path its own
reserved source-chain id, and mirror the choice in the L2 consumed-key prefixes.

### C2 — `MerkleTree.Verify` is not position-bound [E2 verified]

`src/Neo.L2.State/MerkleTree.cs:143-164`: the fold direction comes entirely from
`proof.PathBitmap`; `proof.LeafIndex` is never read. `GetProof` (`:133-138`) emits
`LeafIndex` and `PathBitmap` as independent fields, and `MerkleProofSerializer` encodes them
separately. A genuine proof for leaf 0 can therefore be re-labelled `LeafIndex=1` and still
verify against the same root. `KeyedStateMerkleTree.Verify` (`:106-120`) derives direction from
`leafIndex` bits instead — so the repo's two verifiers disagree on what is authoritative.

Not directly exploitable for fund theft while every consumer binds the root by value, but any
consumer that trusts the reported leaf position (message index, nonce ordering) is exposed, and
the two conventions drifting apart is itself a correctness hazard. `doc.md` §11 / the withdrawal
path should be audited for such a consumer.

Fix: in `MerkleTree.Verify`, recompute the bitmap from `LeafIndex` and assert equality.

---

## 3. Verification-integrity findings (false confidence)

These do not corrupt state, but they make green results mean less than they appear to. They are
the reason this audit's headline is "2,814 tests pass" *and* "roughly 45 of them did not run".

### 3.1 ~45 tests silently self-skip on Windows; CI can never see it [E1 proven]

55 of 2,869 tests were skipped even though the run printed `Passed!`. Root cause, proven:
`tests/Directory.Build.props:4-5` injects `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`
**only when `'$(OS)' == 'Windows_NT'`**, so test output lands in
`bin/Debug/net10.0/win-x64/` — one directory deeper than on Linux. 40 test methods across 11
files locate the committed evidence file with a hardcoded five-level walk:

```
Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..","..","..","..","..",
    "docs","audit","testnet-deployment-20260716-live.json"))
```

Measured with the tests' own arithmetic:

- 5 × `..` → `D:\Git\neo-n4\**tests**\docs\audit\…json` — `exists=False` (what the code computes)
- 6 × `..` → `D:\Git\neo-n4\docs\audit\…json` — `exists=True` (the real file)

Confirmed end-to-end: copying the JSON to the computed path took
`Neo.Plugins.L2Batch.UnitTests` from 66 tests / 1 skipped to **66 passed / 0 skipped**, with
`FromChainDirectory_LiveDeployReport_LoadsChainId` executing and passing. So the underlying
production wiring is **correct** — only the discovery is broken. `.audit-test-full.log` skip
message, verbatim: `repo evidence file not found at D:\Git\neo-n4\tests\docs\audit\…`.

Why nobody noticed: CI is `ubuntu-latest` only (`.github/workflows/build.yml` — every
`runs-on`), where no RID is injected and the 5-level walk happens to be right. And the primary
`dotnet test` step (`:96-97`) does not use the repo's own zero-skip guard
(`scripts/ci/run_dotnet_filtered_tests.py:85` "expected zero skipped tests"), which *is* applied
to the filtered contract/native gates at `:222,232,240,296,317`.

The repo already contains the correct pattern: `FindRepositoryRoot()`, used by
`tests/Neo.Hub.Deploy.UnitTests/UT_ProductionGapClosure.cs:175+`, which is why that project
reports zero skips.

Fix (any one, in order of preference): migrate the 40 sites to `FindRepositoryRoot()`; or add a
`--reject-skips` run of `run_dotnet_filtered_tests.py` to the main Test step; or drop the
Windows-only RID injection.

### 3.2 The RISC-V "parity" gate was never running — and passes once run [E1]

10 `RealNative_*` tests skipped with `DllNotFoundException: neo_riscv_host`. This is legitimate
environment gating, but it meant the stateful RISC-V executor's byte-for-byte parity with
`ApplicationEngine` was unverified. I built it (`cargo build -p neo-riscv-host` in
`external/neo-riscv-vm`, 21.9s, `crate-type=["rlib","cdylib"]`), dropped the DLL beside the test
assemblies, and got **31 passed / 0 skipped**. New verified surface: `RealNative_RetReceiptMatchesApplicationEngineByteForByte`,
`RealNative_StoragePutCommitsStateAndCanonicalEffects`, `RealNative_CallbackOutOfGasRollsBackStateAndEffects`,
`RealNative_UnsupportedContractCallFaultsClosed` and 6 more. Recommend committing a
`scripts/build-native-host` step so this gate is reproducible.

### 3.3 `cargo test --workspace` cannot run on Windows [E1]

`sp1-jit 6.2.1` fails on `libc::ftruncate`, `libc::shm_unlink`, `create_anonymous_file`. The
documented L1 gate in `docs/system-verification-plan.md` ("`cd bridge/neo-zkvm-guest && cargo
build && cargo test`") is therefore Linux-only. Consequence: all SP1 proving Rust code is
`#[cfg(unix)]` and completely unexercised on Windows dev boxes. `docs/getting-started.md` should
state that Windows contributors cannot run the proving gates.

### 3.4 NuGet advisory gate fails open [E1 measured]

`.github/workflows/build.yml:87-94`, under a comment reading "Non-overridable supply-chain gate":

```bash
dotnet list Neo.L2.sln package --vulnerable --include-transitive | tee /tmp/nuget-vuln.log
if grep -qi "is affected by" /tmp/nuget-vuln.log; then exit 1; fi
echo "OK: no vulnerable NuGet packages"
```

With `bash -e` and no `pipefail`, a failed `dotnet list` yields pipeline status 0 and an empty
log. Measured directly: `bash -e -c 'false | tee /dev/null; echo exit=$?'` → `exit=0`. Any
restore or network error therefore prints `OK: no vulnerable NuGet packages`. (For the record the
gate currently finds nothing: 107 projects, 5 distinct packages, all exactly pinned, `api.nuget.org`
clean.) Fix: `set -o pipefail` plus an explicit exit-code and expected-project-count assertion.

### 3.5 `cargo audit --ignore` is blanket-scoped, and the policy table reports the filtered result [E2]

`.github/workflows/build.yml:600-607` applies `--ignore RUSTSEC-2026-0258` in a loop over **all
five** lockfiles, but the advisory belongs to `h2 0.4.14`, which is only in
`external/neo-zkvm/Cargo.lock` (verified: root lock is `h2 0.4.19`). A future regression of the
*root* graph to a vulnerable `h2` would be silently accepted.
`docs/rust-supply-chain-policy.md:19-24` records `Vulnerabilities: 0` for `external/neo-zkvm`,
which is true only because the same RUSTSEC id is ignored. Fix: scope the ignore per lockfile and
run a second unignored pass; label the table column "as gated" rather than "0".

---

## 4. High findings

**H1 — Plugin exceptions stop the node** [E2]. `L2BatchPlugin.cs:478` rethrows into the
`Blockchain.Committed` handler, and no first-party plugin overrides `ExceptionPolicy`;
`external/neo/src/Neo/Plugins/Plugin.cs:75` defaults to `UnhandledExceptionPolicy.StopNode`
(dispatch at `:280-284`). A transient RocksDB/DA/SP1 fault in `PersistAndAcknowledge`
(`:580-597`) or a ledger gap in `RecoverAndProcessCommittedBlocks` (`:489-500`) therefore kills
the node — a remotely-expressible sequencer outage. `TryRetryPendingSealedBatch()` (`:565`)
already exists as the graceful path. Fix: `StopPlugin` + retry.

**H2 — Forced-inclusion deadline is shorter than the challenge window it pauses** [E3].
`contracts/NeoHub.ForcedInclusion/ForcedInclusionContract.cs:195,495-503` caps the deadline at
86,400 s while `contracts/NeoHub.OptimisticChallenge/OptimisticChallengeContract.cs:170` permits
`7*86400`. After 24 h, `ReportCensorship` can pause batches that are still legitimately
challengeable.

**H3 — Censorship escape hatch faults unless wiring is done by hand** [E3].
`ForcedInclusionContract.cs:503` calls `ChainRegistry.PauseChain`, which requires
`CheckWitness(owner) || IsPauser(caller)` (`ChainRegistryContract.cs:485`); `ForcedInclusion` is
not auto-registered as a pauser, so the anti-censorship path faults exactly when needed. Assert
it in `IsProductionReady()`.

**H4 — ETH watcher accepts RPC logs with no header cross-check and 0 confirmations** [E3].
`watchers/neo-bridge-watcher-eth/src/live/eth_rpc/eth_rpc_event_source.rs:109-128` validates only
the router address and a block-number range — no `blockHash` ↔ header binding, no `logIndex`
check — and `min_confirmations` defaults to 0
(`eth_rpc_event_source_builder.rs:33`, `poll_config.rs:52`; the binary only warns at
`bin/neo-bridge-watcher-eth.rs:454`). A hostile or reorging HTTP provider can fabricate one
`Locked` log, and committee signatures would then drain escrow. Highest-severity item in the
external-bridge surface. Fix: `eth_getBlockByNumber` → require `log.blockHash == header.hash`
and the emitting transaction's presence; enforce a non-zero floor inside `build()`.

**H5 — Stub signer marks funds as submitted** [E3]. The only shipped ETH signer returns
`SHA256(script)` as a fake tx hash (`bin/neo-bridge-watcher-eth.rs:744`,
`stub_sign_and_send.rs:11-28`), yet `NeoRpcSubmitter` persists it as submitted and advances the
cursor. After switching to a real signer, those `(chain,nonce)` pairs are never retried, so funds
sit locked in the foreign escrow. Mitigating control confirmed present: startup hard-refuses
without `--allow-stub-signer`. Fix: never persist a submitted-set entry for a synthetic hash.

**H6 — `neo-zkvm-executor` SHA-256 pin is caller-supplied** [E3].
`src/Neo.Plugins.L2Settlement/Sp1SettlementExecutionStack.cs:46,128` takes the digest as a
constructor argument. It fails closed on mismatch (`Sp1StatefulBatchExecutor.cs:391-393`) but only
self-consistently, so "pinned binary" is a decorative anchor off-chain. Contrast the genuinely
enforced on-chain VK lock. Fix: embed the reviewed digest as a constant (as
`bridge/neo-zkvm-guest/vk_manifest.rs` does) and assert equality.

**H7 — SDKs do not re-encode the commitment the .NET client verifies** [E3].
`docs/sdk-conformance.md:26` / `docs/audit/p1-1-rpc-sdk-abi.md:26` claim the `encoded` field is
decoded, re-encoded and compared. Only `src/Neo.L2.Sdk/L2RpcClient.cs:310-337` does;
`sdk/typescript/src/index.ts:397-416`, `sdk/python/neo_n4_sdk/__init__.py:400-410` and
`sdk/rust/src/l2_batch_view.rs:41` (which also accepts any `proofType` u8) take it verbatim. A
hostile or buggy node returns structured fields disagreeing with the wire bytes. This means the
previously-closed P1-1 finding is only closed for one of four SDKs.

**H8 — Faucet disbursement caps are silently inert by default** [E3].
`tools/Neo.L2.Faucet.Cli/Program.cs:156-162` defaults the journal to
`InMemoryKeyValueStore` in a one-shot process, so `FaucetPolicy.Decide` always sees `entry == null`
(`FaucetPolicy.cs:44`) and cooldown/lifetime caps never fire; `--max-per-drip` /
`--max-lifetime` / `--cooldown-seconds` (`:107-109`) have no floor.
`IMPLEMENTATION_STATUS.md:220-221` describes this as "production drip with rate limiting +
RocksDB-persisted journal". Contradicts the "one protocol contract across four SDKs" style claim
of production readiness.

**H9 — Submodules pinned to force-pushable `codex/*` branches; the gitlink gate covers only one** [E1+E2].
`git -C <sm> branch -r --contains HEAD` on this checkout:

| Submodule | Pinned commit reachable only from |
| --- | --- |
| `external/neo` | `origin/r3e/neo-n4-core` ✅ canonical |
| `external/neo-devpack-dotnet` | `origin/codex/system-audit-devpack` |
| `external/neo-riscv-vm` | `origin/codex/rustsec-2026-refresh` |
| `external/neo-vm-rs` | `origin/codex/rustsec-2026-refresh` |
| `external/neo-zkvm` | `origin/codex/rustsec-2026-refresh` |

These are agent-authored scratch branches, and they include the **nccs contract compiler**
(`build.yml:151` "Build pinned nccs from the audited devpack submodule" — the tool that emits the
bytecode of every fund-holding NeoHub contract) and the whole proving/VM stack. A force-push to
any of them silently changes what every artifact means, with no review boundary. Independently
found by two reviewers. `scripts/ci/verify_neo_core_gitlink.py:61` checks **only**
`Path("external/neo")`, so CI prints a green "Verify Neo gitlink is published on the official core
branch" while 4 of 5 build-critical gitlinks are ungated. Fix: fast-forward these commits onto
protected branches, repoint the gitlinks, and generalize the gate to every submodule.

**H10 — Unbounded growth on production hot paths** [E2 for H10a].
- (a) `src/Neo.Plugins.L2Rpc/InMemoryL2RpcStore.cs:18-25` — `_batches`, `_statuses`,
  `_stateRoots`, `_deposits` are `ConcurrentDictionary` keyed by monotonic batch number with no
  eviction anywhere in the file (the only `TryRemove`, `:205-207`, maintains the L1↔L2 address
  map). I confirmed this is the production path: `OpenFromChainDirectory` (`:92`/`:125`) is called
  from `src/Neo.Plugins.L2Settlement/MultisigLocalHostComposition.cs:130`. Only withdrawal/message
  proofs reach RocksDB.
- (b) `src/Neo.L2.Messaging/RpcMessageRouter.cs:52,282` — `_locallyConsumed` never pruned, unlike
  `_knownNonces` (`:349`). [E3]
- (c) `src/Neo.L2.Executor/ApplicationEngineTransactionExecutor.cs:60` — `_consumedNonces` is an
  instance-level `HashSet`, only ever `.Add`ed (`:133`), never cleared or persisted, while its
  comment at `:126-128` claims "at most once **per batch**". Either the comment or the scope is
  wrong; re-executing a batch on a warm executor (witness regeneration, crash re-seal,
  `TryRetryPendingSealedBatch`) yields spurious `duplicate nonce` → `Success=false`, `Gas=0` →
  a different `ReceiptRoot`/`PostStateRoot` for identical input, which would make an honest
  sequencer look fraudulent. Unbounded memory growth too. Same field in `RiscVTransactionExecutor.cs:53`.

**H11 — Full-state re-root per batch, on the commit thread** [E3].
`src/Neo.L2.Executor/MerkleStatePostStateRootOracle.cs:58-60` enumerates the entire KV store,
re-sorts and re-hashes every leaf each batch; reached synchronously from `Blockchain.Committed`
via `L2BatchPlugin.cs:582-583` sync-over-async. Per-batch cost grows with total state keys and
eventually exceeds the block interval. `Sp1StateWitnessSource.cs:238-254` additionally clones the
whole state a second time (`entries.Select(e => (e.Key.ToArray(), e.Value.ToArray()))`), so peak
memory ≈2-3× full state in the LOH per batch. Fix: incremental dirty-key root; hash in place.

**H12 — The irreversible-lock pattern is applied to 4 of 7 governance surfaces, so a
"locked" production deployment is still owner-rewritable at its trust roots** [E2 verified].
> **Status — remediated 2026-08-29 (after this report was written).** `OptimisticChallenge`,
> `ExternalBridgeRegistry` and `MpcCommitteeVerifier` each gained the one-way `LockGovernance()`
> (GC-wired precondition, freezes `SetGovernanceController` too, `GovernanceLocked` event) and every
> instant owner `Register*`/`Revoke*` path named below now asserts `!IsGovernanceLocked()`.
> `OptimisticChallenge` had no §16 surface at all, so it gained one — `SetGovernanceController` plus
> proposal-gated `RegisterFraudVerifier` / `RegisterPermissionlessFraudProfile` / `RevokeFraudVerifier`
> twins (revocation is council-gated because an instant revoke is itself a denial-of-fraud-proof
> path). `LiveDeployCommand` wires the OC controller, calls all three locks, and read-back-asserts
> them in both the resumable call list and the smoke checks; `ScaffoldPlan` and
> `docs/launching-an-l2.md` no longer sell the escrow pointer lock as full coverage. Post-lock
> administration-only behaviour is pinned in VM tests (fraud proving still slashes; a valid committee
> attestation still verifies). Line numbers below are pre-fix HEAD `728e630a`.
`LockGovernance()` (one-way, freezes the instant-owner path) exists in exactly four contracts —
`ChainRegistryContract.cs:389`, `VerifierRegistryContract.cs:108`, `SettlementManagerContract.cs:268`,
`ExternalBridgeEscrowContract.cs:239` — and `LiveDeployCommand.cs:833,853,855` calls three of them.
The three contracts that decide *who may prove a fraud* and *who may attest a foreign deposit* have
no such gate:
- `OptimisticChallengeContract.cs:206` `RegisterFraudVerifier` and `:235`
  `RegisterPermissionlessFraudProfile` are owner-witness-only immediate writes. The v4 introspection
  (`:247-270`) only re-reads `getSettlementManager` / `getExecutorSemanticId` / `getReplayDomain` off
  the candidate contract, which a purpose-built contract returns trivially, so it is anti-typo, not
  anti-malicious. Once registered, `Challenge` (`:386`) slashes the whole open-window bond and drives
  `SettlementManager.RevertBatch` (`SettlementManagerContract.cs:542`, whose `challengeAuthorized`
  branch survives `LockGovernance` by design — see the doc-comment at `:263-266`).
- `ExternalBridgeRegistryContract.cs:122` `RegisterVerifier` writes the
  `externalChainId → verifier` dispatch table immediately; the only added checks are the
  `0xE0_xx_xx_xx` namespace and that the candidate's own `bridgeKind()` matches the requested one
  (`:240-256`). `ExternalBridgeEscrow.Receive` then routes through that table
  (`ExternalBridgeEscrowContract.cs:553-557`), so pointing one foreign chain at a permissive verifier
  makes `Receive` pay arbitrary amounts.
- `MpcCommitteeVerifierContract.cs:129` `RegisterCommittee` / `:148`
  `RegisterCommitteeWithMembers` replace the signing set the same way.

Each of the three *does* also expose a proposal-gated variant
(`UpgradeVerifierViaProposal` `:132`, `RegisterCommittee*ViaProposal` `:162`/`:199`) and
`LiveDeployCommand.cs:845-851` wires their `GovernanceController` — so the safe path exists but can
never be *enforced*, because nothing stops the instant path afterwards. `Escrow.LockGovernance`
freezes only the escrow's *pointer to* the registry, which is precisely the "locked door, unlocked
hinge" case. This is a design-consistency defect as much as a security one: the repo's own
`ScaffoldPlan.cs:417,460,501,505` calls `LockGovernance` "the irreversible production gate", so an
operator following the scaffold reasonably believes the whole set is frozen. It is not.
Fix: add the same one-way `LockGovernance` to `OptimisticChallenge`, `ExternalBridgeRegistry` and
`MpcCommitteeVerifier`, have it disable direct `Register*`, and call it from `LiveDeployCommand`.

**H13 — The emergency kill-switch covers one of the three asset-bearing contracts, contradicting the
invariant its own header states** [E2 verified].
`EmergencyManagerContract.cs:11-13` documents "Other NeoHub contracts consult `IsPaused` before
mutating state." A repo-wide search for `IsPaused` finds exactly one consumer —
`SharedBridgeContract.cs:131-135`, guarding `:152` (deposit), `:244`, `:280`, `:324`, plus the
pause-only escape hatches at `:361` and `:551`. Grepping `paused|Paused|emergency` in
`ExternalBridgeEscrowContract.cs` and `SettlementManagerContract.cs` returns no pause guard at all:
`ExternalBridgeEscrow.Receive` (`:484`) keeps releasing foreign-bridge funds and
`SettlementManager.SubmitBatch`/`FinalizeBatch` keep accepting and settling batches while
`IsPaused()` is true. So the operator action documented as "the global pause flag"
(`EmergencyManagerContract.cs:103`, `doc.md:133` "提供 emergency pause") does not stop two of the
three things an incident responder would need stopped, and `EmergencyManager`'s own escape hatch
(`:143,:185`) moves nothing from either. Fix: add the guard to `Receive`, `SubmitBatch` and
`FinalizeBatch` (or narrow the doc-comment and `doc.md` §15.5 to "SharedBridge pause only" and say
explicitly what stays live).

---

## 5. Medium findings (grouped)

**Determinism / correctness**
- `MerkleStatePostStateRootOracle.ResolveAsync` ignores `preStateRoot`, `receiptRoot` and
  `blockContext` (acknowledged in the comment at `:52-56`) and roots the **live** store;
  `RocksDbKeyValueStore.EnumerateInternal:268` opens its iterator with no RocksDB snapshot and does
  not take `_writeGate`, so a concurrent writer is observable mid-iteration. In the default
  single-commit-thread topology this does not fire, which is why I rate it Medium rather than the
  Critical proposed; it is a latent hazard that needs a snapshot or the same gate, plus a
  fail-closed check that the derived pre-root matches `request.PreStateRoot`. [E2]
- `BatchSealer.cs:387-394` builds **one** `BatchBlockContext` for up to 50 blocks and
  `BuildPersistingBlock:237-238` sets `Index = L1FinalizedHeight` and a shared timestamp, so every
  transaction in a multi-block batch sees the same `Runtime.BlockIndex`/`Timestamp` — multi-block
  batches are not reproducible. [E3]
- `ExecutionStateTransaction.Commit:197-201` applies changes with individual `Put` calls and
  hand-rolled compensation, although `IAtomicL2KeyValueStore.CompareExchangeBatch` (atomic and
  synced) already exists and is used correctly elsewhere (`ProofWitnessStore`). [E3]
- `ApplicationEngineTransactionExecutor.cs:157-161,197-201` converts *environmental* faults
  (`DllNotFoundException`, transient IO during `Commit`) into a consensus-visible failed receipt
  that feeds `ReceiptRoot`; `:165-168` already aborts correctly for runner faults. Same shape in
  `RiscVTransactionExecutor.cs:155-159,225-230`. [E3]
- `StateWitnessV1.MaxEntries = 65_536` (`:133`) caps the *complete* state snapshot, contradicting
  multi-GB RocksDB state; and `MaxKeyBytes = 4096` (`:136`) vs `KeyedStateMerkleTree.MaxKeySize =
  1024` (`:53`) means a 2 KiB key passes witness validation then throws inside `ComputeRoot`. [E3]
- `BatchBuilder.SealArtifact:138-170` drops `Batch.Withdrawals`, `L2ToL1Messages`,
  `L2ToL2Messages`, making `AddWithdrawal`/`AddL2ToL1Message` silent no-ops for the sealed artifact
  and their sealer gauges (`BatchSealer.cs:139-151`) permanently 0. `BatchSerializer.Encode` lacks
  the `lastBlock >= firstBlock` check its own `Decode:145` enforces;
  `MessageHasher.EncodeMessage:38` has no payload cap while `DecodeMessage:75` rejects >1 MiB. [E3]
- `SettlementManagerContract.cs:1012` — `return storedRoot.Equals((UInt256)current);` omits the
  `index == 0` terminator that both sibling verifiers enforce (cf.
  `MessageRouterContract.cs:603` which has it), so a proof need only reach *some* ancestor equal to
  the stored root. [E3 — worth confirming against the on-chain fold]
- `SettlementManagerContract.cs:444-466` — `firstBlock`/`lastBlock` are absent from the 332-byte
  public-input preimage, so proven roots do not bind the claimed block range. [E3]
- `StateRootCalculator.cs:88-99` / `hashing.rs:297-314` — the public-input preimage has no
  version/profile/domain byte and attestation payloads carry no epoch or expiry, so a Stage-0
  attestation is byte-identical and valid forever across future profiles. Threshold math itself is
  correct (dedup-before-verify, `seen.Count < Threshold`, ctor rejects `threshold > N`). [E3]
- `RiscVProofPayload.cs:93-96` accepts `ProofSystem` byte 0 where the router requires 1..4
  (`ContractZkVerifier:355-358`) — divergent parse, liveness risk. [E3]
- `AssetRegistry.cs:50-52` — re-pointing an L2 asset silently evicts the other
  `(L1Asset, L2ChainId)` mapping, orphaning an L1 asset that keeps accepting deposits. [E3]
- `ContractZkVerifierContract.cs:334-346` envelope-only mode returns `true` with no cryptographic
  check. Default off, one-way locks exist, and `LiveDeployCommand.cs:819-836` does run all three
  locks — but production soundness then rests on an operator not skipping a step, with
  `VerifierRegistry:90-96` giving an instant owner `registerVerifier` until `lockGovernance`.
  Fix: make `SubmitBatch` fail closed unless both locks are reported set. [E3]
- `tools/Neo.Hub.Deploy` registers only `ProofType.Zk`, so Multisig/Optimistic commitments fault
  at `VerifierRegistry:256` — fail-closed, but §7.5 Stage-0/1 settlement and the whole
  OptimisticChallenge/v4 path are unwired in production, which contradicts "All phases ✅". [E3]
- `L2BatchPlugin.cs:392-393,485` — `_sink`/`_sealer` published without a barrier (a reader can see
  `_sealer != null, _sink == null`), and both `ProcessCommittedBlock` and
  `TryRetryPendingSealedBatch` are public entry points driving a non-thread-safe `BatchSealer`. [E3]
- `BatchSealer.cs:360-373` bounds batch *tx count* but never byte size, so `MaxBlocksPerBatch=50` ×
  large txs can exceed `MaxEncodedBytes`/DA blob limits; `L2Batch.cs:80` permits
  `blockIndex == LastBlock` on `AddBlock` while still absorbing that block's txs. [E3]
- `RpcSharedBridgeDepositScanner.cs:46,110,165` — `finalityDepth = 1` by default and
  `VerifyResumeHashAsync` *throws* on finalized-history change with no rollback of already-credited
  nonces, so a 2-deep L1 reorg hard-stops the pipeline and manual recovery is unsafe. [E3]
- `RpcForcedInclusionSource.cs:182,192,249` and `RpcMessageRouter.cs:257,267` use raw
  `DateTime.UtcNow` for cache expiry while the rest of the codebase injects `IClock`/`FakeClock`; a
  clock rollback can pin a stale drained set — dropping forced txs, i.e. the censorship the queue
  exists to prevent — and it is untestable. Related: `BatchSealer.cs:340`
  (`if (_forcedDrain is null) return;`) makes forced inclusion purely opt-in, so an unconfigured
  sequencer collects `EnqueueForcedTransaction` fees and never includes them. [E3]

**Sync-over-async / blocking on the commit path** — `L2BatchPlugin.cs:385,387,583,652,655` and
`L1MessageDrain.cs:53,74` (including cross-network L1 RPC), `MultisigRoundProver.cs:94-95`,
`L1MessageDrain.cs:652` `DrainAsync(int.MaxValue)` unbounded. A slow L1 parks L2 commit, and via
H1 becomes node death. [E3 for most; the `:583` and `:652` sites are E2-visible]

**RPC/metrics hygiene** — `InMemoryMetrics.cs:95-103` + `PrometheusExporter.cs:132-152`: the
internal `name{k=v,k2=v}` key is not injective and is re-parsed by naive `,`/`=` splitting, so a
tag value containing `,` or `=` can drop or duplicate a series, and `ToPromName:129` does not strip
CR/LF → exposition-line injection; `L2SettlementPlugin.cs:791-794` tags
`("exception", ex.GetType().Name)`; `_counters`/`_gauges` have no key-count cap. [E3]
`L2BatchPlugin.cs:477` emits `l2_batch_on_block_committed_error` as a raw literal, escaping the
`MetricCatalog` reflection test that `docs/telemetry.md:126-128` says is enforced. [E3]

**Unauthenticated operator endpoints** — `MetricsRequestHandler.cs:82-95` serves `/operatorstatus`
and `/healthprobe` unauthenticated and verb-agnostic, exposing contract hashes, L1 endpoints,
heights and queue depth. Loopback by default (`src/Neo.Plugins.L2Metrics/Settings.cs:21,45`) but
no warning on non-loopback bind; and `external/neo/src/Plugins/RpcServer/RpcServer.json:13` ships
`EnableCors: true` with `AllowOrigins: []` → `AllowAnyOrigin()` (`RpcServer.cs:193-205`), so any
web page can read loopback RPC, while `neo-stack init-l2` never writes a tightening RpcServer
config. [E3]

**CLI safety** — `tools/Neo.Stack.Cli/Commands/ArgUtil.cs:7-15` silently ignores unknown options and
returns the default for a trailing flag with no value; on `--broadcast` paths
(`OperatorPlanCommands.cs:182,400,568`) a typo'd `--chain-id`/`--output`/`--settlement-manager`
produces a signed, broadcast L1 transaction from an unintended config with no diagnostic. [E3]

**Windows secret hardening** — `tools/Neo.External.Bridge.Cli/Commands/GenKeyCommand.cs:88-97`:
0600 hardening is POSIX-only, so on Windows `watcher.priv` inherits `BUILTIN\Users:R` with no
`FileSecurity` attempted; `priv` is never zeroized (`:56`) and `--print-priv` (`:100`) echoes the
key without confirmation. Same Unix-only asymmetry noted at
`AtomicFileQueueTransport.cs:303-321`. [E3] — materially relevant because this audit ran on
Windows.

**Lifecycle** — `L2RpcPlugin.cs:97-118` disposes the adapter (`L2RpcServerAdapter.cs:55-62`) but
never removes it from the process-static `RpcServerPlugin.handlers`
(`external/neo/.../RpcServer.cs:25,137-143`), so a later RpcServer on the same process permanently
re-registers a disposed adapter and all 10 L2 methods fail. [E3]

**Efficiency** — `RocksDbKeyValueStore.Count:78-90` is a full-DB iteration surfaced as
`LeafCount`; `L2DataCacheAdapter.cs:126-133` "seeks" by enumerating from key 0 (backward seeks
materialize then `.Reverse()`), making native-contract enumerators O(N²)/block;
`ExecutionStateTransaction.GetCore:240` allocates `key.ToArray()` per read;
`ProofWitnessStore.QuarantineRevertedTailAsync:888-964` copies the whole store under a lock, up to
8× on CAS retry; `Sp1StateWitnessSource.CommitTransition` calls `Capture` ≥5× and
`CompareExchangeAll` rewrites the entire DB per batch. [E3]

**Supply chain** — no root `rust-toolchain.toml` while `external/neo-riscv-vm` pins
`channel = "stable"`; Actions version-tag-pinned not SHA-pinned (`actions/checkout@v7`,
`dtolnay/rust-toolchain@1.88.0`, `foundry-rs/foundry-toolchain@v1` mutable major);
`forge install --no-git foundry-rs/forge-std` pulls a default branch (`lib/` gitignored); Python
extras resolve `cryptography>=44,<47` with no lockfile; `build-watcher-image.yml:51-53,113-115`
publishes to GHCR with `packages: write` and no cosign signature or SBOM; no repo-level
`NuGet.config`; `external/neo-riscv-vm` ships no LICENSE though
`src/Neo.L2.Executor.RiscV` builds against it. [E3]

**Doc consistency** — `repository-coverage-ledger.md:11` (R3, status `closed`) asserts
`ExternalBridgeRegistry` bridge kinds "gate production registration"; the contract contains only a
self-consistency `Assert(declaredKind == bridgeKind)` at `:252` and no CI enforcement, while
`SECURITY.md:97-98` words it correctly as "Deploy CI *should* refuse". A shipped control is
described where an aspiration exists. [E3]
`IMPLEMENTATION_STATUS.md` — 21 of 36 per-project test-count rows are stale, e.g. L2Settlement
`:348` claims 73 / actual 159, L2Batch `:345` 48→66, Sequencer `:337` 35→48; two **overstate**:
`L2.Persistence` `:351` claims 70 / actual 53 (no `[DataRow]` expansion explains it) and
`L2.State` `:330` 120→89. Aggregate claimed ~2,041 vs actual 2,724. [E3]
Bilingual parity: all 39 `docs/*.md` have a `docs/zh/` file, but four root mirrors are abstracts —
`docs/zh/IMPLEMENTATION_STATUS.md` is 9% of the English size, `AGENTS.md` 20% (no canonical-encoding
section, no Don'ts, zero hits for `little-endian`/`91`), `SECURITY.md` 32%, `CHANGELOG.md` 2.3% —
while their own header mandates non-divergence, and the enforcing test
(`UT_ProductionGapClosure.cs:175-235`) only checks phrase presence in 5 named files, making
`docs/test-coverage.md:13` materially misleading. [E3]
`docs/architecture-wire-formats.md:220-221` cites dead paths
`src/Neo.L2.Proving/MultisigProofPayload.cs` / `RiscVProofPayload.cs` (actually under
`Attestation/` and `RiscVZk/`). [E3]
Stale counts: `AGENTS.md:58` "16 core off-chain libs" → actual 17 (27 `src/` dirs, 8 plugins, 2
`*.Rpc`); `AGENTS.md:168` "12 CLI subcommands" → 13 wired (`bootstrap-genesis` uncounted), and "the
3 wallet commands print plans rather than performing the wallet-side submission" is now **false** —
all three broadcast and verify on-chain (`OperatorPlanCommands.cs:182-217,400-460,568-596`).
`repository-coverage-ledger.md:22` references nonexistent `external/upstream`. [E3]

**L1 contracts — asset-loss / liveness (26 projects reviewed; items below verified in source)**
- **Deposit over-credit on a delta-less transfer.** `SharedBridgeContract.cs:184-203` pulls tokens with
  `asset.transfer(...)` and then calls `IncrementLocked(targetChainId, asset, amount)` on the *claimed*
  `amount`; no `balanceOf` before/after, and `OnNEP17Payment` (`:210`) asserts a positive amount but
  never compares it to the credited value. `ExternalBridgeEscrowContract.TransferIntoCustody` (`:770-791`)
  already does exactly the right thing (`balanceAfter == balanceBefore + amount`), so the repo knows the
  pattern and the bridge that guards per-chain accounting is the one that omits it. A fee-on-transfer or
  rebasing asset therefore credits `locked` above real custody → over-withdrawal that drains that chain's
  escrow. Mitigant found and kept in view: `Deposit` refuses unmapped/inactive assets
  (`:164-172`, `getL2Asset` + `isActive` on the token registry), so exploitation needs an operator to map
  such an asset — MEDIUM, not Critical. Same shape at `SequencerBondContract.cs:187` and
  `ExternalBridgeBondContract.cs:176-179`. Fix: reuse the custody-delta check. [E2]
- **Envelope-only stays reachable for proof systems 2-4.** `ContractZkVerifierContract.cs:344-346` returns
  `true` with zero proof math when no verifier contract is configured for a proof system and the
  envelope-only flag is set. The flag is off by default (`IsEnvelopeOnlyAllowed:196-200`), is documented
  as devnet-only (`:169-174`), and there is a one-way `DisableEnvelopeOnlyPermanently` +
  `LockProofSystemConfiguration` — but `LiveDeployCommand.cs:827-831` applies them **only to SP1**, and
  `SettlementManager` (`:369-375`) never requires either lock before accepting `ProofTypeZk` for a
  `SecurityLevelValidity` chain. So Risc0/Halo2/Axiom stay one owner call away from no-proof settlement.
  Fix: assert `isEnvelopeOnlyLocked && isProofSystemConfigurationLocked` in SM when `securityLevel ≥ 3`,
  and lock all four systems in the deploy. [E2]
- **`MessageTypeCall` is consumed and dropped.** `ExternalBridgeEscrowContract.cs:533-537` accepts
  message types 0/1/2, `:561-565` writes the replay key before effects, and `:567-571` pays out only for
  types 0 and 2 — a pure call burns `(externalChainId, neoChainId, nonce)` and delivers nothing, with no
  refund path. Fix: reject type 1 until dispatch exists. [E2]
- **`Send` has no route check and no refund.** `ExternalBridgeEscrowContract.cs:425` validates neither the
  asset route nor an active mapping, where `Receive`'s symmetric enforcement does exist and
  `SharedBridgeContract.cs:156-172` enforces it deposit-side; outbound funds on a stale route are stranded.
  [E2]
- **Attested messages bind networks but not the redeeming contract.** Correcting the reviewer's framing:
  the signed preimage *does* carry the destination domain — `ExternalMessageHasher.cs:46-57` serializes
  `ExternalChainId`, `NeoChainId`, `Nonce`, `Direction`, `Sender`, `Recipient`, deadline, `SourceTxRef`,
  type and payload length, and `ExternalBridgeEscrowContract.cs:499-508` asserts the embedded
  `neoChainId` equals the escrow's own. The real residual is that nothing mixes the *redeeming contract
  identity or L1 network* into the preimage (`MpcCommitteeVerifierContract.cs:439` verifies over the raw
  bytes; the consumed key lives in one escrow's storage at `:547`), so one M-of-N attestation is redeemable
  once per deployment that shares committee keys — i.e. testnet→mainnet replay under key reuse, or a
  re-deployed escrow. That is an operator key-management hazard plus a missing domain tag, not a
  signature-forgery path. Fix: prefix `Runtime.ExecutingScriptHash` and the L1 chain magic into the signed
  bytes. [E2]
- **Unpriced attacker-keyed storage growth.** `MessageRouterContract.cs:126-146` `EnqueueL1ToL2` is
  permissionless and writes up to 128 KiB with no protocol fee (contrast `ForcedInclusionContract.cs:365-400`,
  which charges); the contract pays the storage GAS, so the balance can be exhausted and honest traffic
  FAULTs. `SettlementManager` similarly stores a full ≤1 MiB proof-bearing header per batch
  (`:383`) with no pruning. Not free for the attacker — they pay normal per-tx GAS — but the cost imposed
  on the shared contract exceeds the cost to the caller. [E2]
- **`ForcedInclusionContract.cs:481-504` griefing (downgraded from the reviewer's MEDIUM).**
  `ReportCensorship` cannot act on an arbitrary chain: it requires a prior paid
  `EnqueueForcedTransaction` entry, refuses a caller-supplied sequencer attribution (`:483-485`), and only
  pauses once `nowSec ≥ deadline` (`:496-498`) — and the sequencer defuses it simply by including the
  forced transaction. Real residual is repeatable fee-priced pause/resume churn after
  `ResumeChain`; that is LOW, and the permissionless-pause authority itself deserves the design comment it
  already carries. [E2]
- Contract test surface is thin exactly where the Highs live: `tests/NeoHub.Contracts.VmTests/UT_SharedBridge_Vm.cs`
  has 3 `[TestMethod]`s and `UT_L2PayoutAdapter_Vm.cs` 1, with no replay/double-finalize, forged-leaf,
  pause-gating or `MigrateLockedBalance` authz cases, and nothing anywhere tests an owner-installed hostile
  verifier or committee (H12), `MessageTypeCall` loss, or envelope-only acceptance under
  `SecurityLevelValidity`. [E1 counted]

---

## 6. Low / informational

Environmental-failure→receipt conflation aside (§5), the remaining items are hygiene:
`SharedBridgeContract.cs:68` asserts `settlementManager.IsValid` but not `!IsZero` unlike its
neighbour `:67`; `eth_rpc_event_source.rs:47` `self.chunk_size - 1` underflows for
`eth_chunk_size = 0` with no validation in `build()`; `poll_config.rs` `poll_interval_secs = 0`
busy-loops and `backoff_initial_secs = 0` never grows (`*2`); `FaucetPolicy.cs:46` `ulong`
underflow on a future-dated journal timestamp bypasses cooldown;
`OperatorPlanCommands.cs:541` unbounded `File.ReadAllBytes`; `StartOperatorCommands.cs:56,78` dead
`GetAwaiter().GetResult()` wrappers and `:371` swallowing `NullReferenceException`;
`ExternalCommandTransactionSigner.cs:178` discards adapter stderr contradicting
`docs/operator-signer-command-protocol.md:53`; `L2MetricsPlugin.cs:113-143` "must be installed
before Start" unenforced; `bridge/neo-zkvm-host/src/lib.rs:120-121` `prove()` verifies against a VK
derived from its own `setup()` rather than `NEO_ZKVM_GUEST_VK_BYTES32`, closing that loop only via
the `build.rs:123-126` ELF hash; `Sp1BatchProofProver.cs:151-153` accepts a pre-existing result file
and `:242-252` performs no local proof sanity/pairing check, so malformed proofs cost L1 gas;
`ReadBoundedPathAsync:261-265` has a `FileInfo.Length`→`ReadAllBytesAsync` TOCTOU allocation window;
`NeoExternalBridgeRouter.sol:417` raw `ecrecover` with no high-`s` and no `v∈{27,28}` check
(signature malleability; replay is nonce-keyed at `:326,:356` and `MAX_COMMITTEE_SIZE=64` keeps
`seenBitmap` in range, so no fund impact); `Sp1StateWitnessSource.cs:271` commits
`Manifest.ToJson().ToString()` (Newtonsoft ordering/version sensitivity) into the state root and
`:71,76` use a per-process-random `Dictionary<byte[],…>` comparer (harmless only because roots
re-sort); `KeyedStateMerkleTree.cs:26-35`'s no-domain-tag justification understates a ~2⁶⁴ grinding
path; `docs/architecture-wire-formats.md:59` alt-text says "16 fields"/"9 shared" vs 14/9 in code;
dated audit reports present "1,430 passing tests" (now 2,724+ method-declarations / 2,869 executed).

Contract-tier LOWs, none of which break soundness: `firstBlock`/`lastBlock` are excluded from
`publicInputHash` (`SettlementManagerContract.cs:444-466`, `StateRootCalculator.cs:88-97`), leaving header
offsets 12-27 unauthenticated by the proof while `ContinuityCheck.cs:66` treats them as canonical; SM
`SubmitBatch` bounds the commitment only as `Length >= ProofBytesOffset` (`:321`) — the exact
`321+proofLen == length` check exists on the optimistic (`:1343`) and ZK (`ContractZkVerifier:311`) paths
but not for Multisig, so an attested commitment can carry unbound trailing bytes; SM's leaf-proof loop
(`:989-1012`) omits the `index == 0` canonicity check `MessageRouter:603` has;
`SequencerRegistry:142` and `ForcedInclusion:354` accept unregistered `chainId`s;
`ForcedInclusion:537` pays the slash reward to `CallingScriptHash`. [E2]

---

## 7. What is demonstrably sound (and this is unusually strong)

**Encoding discipline.** All 9 pinned encoders exist and every documented fixed size matches code
exactly: 91-byte `L2ChainConfig` is byte-identical in three places (off-chain
`L2ChainConfigSerializer.ConfigSize = 4 + 20*4 + 7`, on-chain `ChainRegistry.ConfigSize`,
`docs/architecture-wire-formats.md:97`) including field order and the 7 §16.2 tail bytes;
`PublicInputsSize` 332, `CommitmentFixedSize` 321 (with 1 MiB proof cap),
`ExternalMessageHasher.FixedPrefixSize` 102, `DepositPayload` 44-byte minimum. Little-endian
throughout; every variable-length field 4-byte-LE length-prefixed; leaf preimages are
`[4B klen][k][4B vlen][v]` so `a|b` cannot collide with `ab`; `HashEntry`/`HashLeaf`/witness-tree
produce byte-identical leaves across three call sites; all decoders reject trailing bytes;
`BigInteger` amounts minimal-length unsigned LE (no malleability). No off-chain↔on-chain layout
mismatch was found anywhere.

**Record equality.** Every in-scope record with a `ReadOnlyMemory<byte>` field
(`L2BatchCommitment`, `CrossChainMessage`, `DAPublishRequest`, `DAReceipt`, `ProofRequest`,
`ProofResult`, `ExecutionPayloadV1`, `ProofWitnessArtifactV1`, `StateWitnessEntryV1`,
`ContractWitnessV1`, `CanonicalStorageChange`, `CanonicalExecutionEvent`,
`Sp1StateWitnessSnapshot`) correctly overrides `Equals`/`GetHashCode` with
`SequenceEqual`/`AddBytes`, as `AGENTS.md` demands.

**Determinism hygiene in the root paths.** No `Dictionary<,>` enumeration feeds a root; no
`DateTime.Now`, unseeded `Random`, `Parallel.*` or `Task.WhenAll`-ordering dependency in any root
path; no locale-dependent formatting; `UInt256`/`UInt160` compare and hash by content;
`LexicographicByteArrayComparer` uses `SequenceCompareTo`, matching RocksDB's `BytewiseComparator`,
so state sorting is sound; `MessageTree`/`WithdrawalTree` correctly invalidate their cached tree on
`Add`; `ExecutionStateTransaction` overlay isolation and lock discipline show no order inversion.

**Proof soundness (the parts that matter most).** All 12 public inputs are bound at
`SettlementManagerContract.cs:446-462` including post-state root, batch number, chainId and
previous root, with continuity at `:349-351` and re-hash equality at `:357-360`; the off-chain
mirror is `VerifierRegistry.cs:76-105`. The guest re-executes and *rejects* claims
(`batch.rs:90` pre-root, `:236-243` `ClaimMismatch`, `main.rs:19` commits the computed hash). VK
routing is locked (`ContractZkVerifier:322-324`, terminal `:86-91`, and `:104-109` folds
vk + pv-digest + exit + vkroot + nonce into the linear combination the pairing actually consumes at
`:115-121`, with canonical-Fr checks at `:140-150`). Optimistic window clamped to `[60s, 7d]`,
single-shot, `claimId` replay-guarded, CEI-correct, verifier-allowlisted, malformed proofs
`return false`, and the v4 verifier genuinely re-executes and demands
`expectedPostRoot != committedPostRoot`. Attestation threshold math has no off-by-one.

**Atomicity is real, not marketing.** Single `WriteBatch` + `SetSync(true)` + full-snapshot CAS
(`RocksDbKeyValueStore.cs:32,237-256,291-304`); artifact bytes verified before commit
(`Sp1StatefulBatchExecutor.cs:170-178`); post-commit re-root (`Sp1StateWitnessSource.cs:163-172`);
`ProofWitnessStore` correctly uses the atomic synced paths. The unsynced `Put`/`Delete` default is a
**deliberate, documented** choice — `RocksDbKeyValueStore.cs:20-26` states it and
`TASKS.md` closed "RocksDB doc/code drift" as resolved with an operator override path. Not a defect.

**Untrusted-input hardening.** `ManifestWireReader.ReadLengthPrefixedBytes`/`EnsureAvailable:1822-1855`
bounds attacker lengths by both max *and* remaining buffer before allocating; `StateWitnessV1` caps
128 MiB / 1 MiB; `wire.rs:1114-1129` rejects inflated counts before `with_capacity` (no
length-prefix allocation DoS); `NeoFsRestDABackend.ReadBoundedAsync:301-337` pre-checks
content-length then streams with `ArrayPool`; `AtomicFileQueueTransport.cs:272-286` has
path-traversal/reparse guards with bounded reads.

**RPC boundary.** All 10 L2 RPC methods are read-only — the only `[RpcMethod]`s in the repo are
`L2RpcServerAdapter.cs:25-53`; there is no submit/prove/settle/sign/mint/faucet/debug/admin method.
Every parameter is bounds- and type-checked (`L2RpcMethods.cs:197-230`: NaN/Inf/fractional rejected,
leading zeros/signs/whitespace rejected), cross-chain reads blocked by `AssertOurChain:188-192`,
no SQL, no shell (`UseShellExecute=false` + `ArgumentList`), no path built from an RPC parameter,
`NormalizePath` + exact-switch blocks `..` traversal, fail-closed `method-not-found` with no store
(`L2RpcPlugin.cs:87-93,144-145`), `MetricsHttpServer` caps concurrency at 32 with a 5 s deadline and
503 on saturation, and no `0.0.0.0` bind anywhere in first-party code or configs. `doc.md` §14.1's
ten-method ABI holds exactly.

**Cryptography.** No MD5/SHA1/`RijndaelManaged`/DES/TripleDES/`BinaryFormatter`/MessagePack
untrusted/`TypeNameHandling`/implicit-curve ECDSA/`SkipCertificateCheck`/TLS-verify-off in any
first-party C#/Rust/TS/Python. Double-SHA256 applied consistently
(`Crypto.Hash256` at `MerkleTree.cs:172`, `MessageHasher.cs:24,102`; `hash256()` at
`hashing.rs:18-24`) and all domain-separated hashes feed the double pass, so length-extension is
unreachable; the single-pass `SHA256.HashData` sites are integrity-vs-pinned-value, not MACs.
`RandomNumberGenerator` for all keygen/nonce work; `new Random`/`Random.Shared` only in seeded
tests; `Guid.NewGuid()` only for temp paths and trace ids. No committed `.env`/PEM/WIF/NEP2/token;
the only hit is `"Wif": "Kxshouldnotbehere"` in a redaction test. Key material flows only over
stdin, is zeroized (`LocalKeyTransactionSigner.cs:54-62,118-123`; `FileSigner` uses `Zeroizing` +
low-S normalisation), and settlement config actively rejects `Wif`/`SignerWif`/`OperatorWif`/
`PrivateKey` keys (`src/Neo.Plugins.L2Settlement/Settings.cs:270-286`).

**Rust discipline.** Prover daemons contain **zero** `.unwrap()`/`.expect()` outside `#[cfg(test)]`
(all 4 are ≥ line 1323 in test modules); no `std::sync::Mutex` guard held across `.await`; `unsafe`
is only `sigaction`/`flock`/`geteuid`; iterators `using`-disposed; every `CancellationTokenSource` in
`src/` is `using`-scoped; no lock-across-await; `BatchSealer` caps batch size, forced txs (256) and
L1 msgs (1024); no socket exhaustion (all `HttpClient`s instance-held with 30-45 s timeouts).

**Bridge invariants.** `ValidateWithdrawalLeafBinding` recomputes the leaf hash with chainId as the
first domain separator and `WithdrawalKey = 0x03+chainId+leafHash` de-duplicates, roots come only
from the registered SettlementManager with `CheckWitness(sm)`, and every `VerifyWithdrawalLeaf*` is
gated on `StatusFinalized` — no inclusion/settlement confusion, cross-chain replay closed.
`PrefixLockedBalance` caps per-chain escrow; `Deposit` is CEI-correct; `OnNEP17Payment`'s
pending-marker blocks unsolicited transfers; `AssetAmount.Scale` **throws** on any non-exact
down-scale and `TokenRegistryContract.cs:94-114` pins decimals per asset type, so no silent
decimal truncation; inbound external-bridge claims are gated by an `0xE0` namespace, signed
chainIds, `direction==2`, deadline, type whitelist, payload cap, exact length, with the consumed key
written *before* payout; MPC committee enforces `0 < threshold <= size` with `seenBitmap` signer
dedup, and advisory verifiers reach only `Slash` behind governance witness; the ETH watcher cursor
semantics are correct (`set_cursor(block_number)` not +1, `(chain,nonce)` submitted-set,
`AlreadyConsumed` re-confirmation requires an on-chain proof, `amount_be_to_le_minimal` ≡ C# minimal
LE, `AssetAndCall` refused rather than guessed) and it hard-refuses startup without
`--allow-stub-signer`. ~71 attack-named tests exist (`Replay|Reorg|DoubleMint|Collision|Tamper|
Spoof|Unauth`, incl. `UT_BridgeInvariants_PropertyBased`, `UT_External_RealCrypto`,
`UT_MpcFraudProof_RealCrypto`).

**Professionalism.** 0 warnings under `TreatWarningsAsErrors`; `dotnet format` clean; `mdbook`
clean; zero `TODO`/`FIXME`/`NotImplementedException` in `src/`+`tools/`; zero banned
`// added for X` comments; no committed `bin`/`obj`/`target`/`*.trx`/`*.dll` (the 9 `src/bin` hits
are Rust binary-target source dirs); `__pycache__` correctly gitignored; all 117 class-shaped
symbols named across the 83 KB `IMPLEMENTATION_STATUS.md` resolve to real declarations — zero
hallucinated types; 0 broken relative links and 0 missing figures across 138 tracked markdown files;
one unit-test project per `src/` project and per `tools/` project with **no exceptions**; CI ships
self-testing gate helpers (`scripts/ci/tests/test_ci_gates.py`) and a real positive-vector fixture
for the Groth16 verifier.

**L1 contracts cleared.** Off-chain ↔ on-chain encodings match byte-for-byte on every seam checked:
the withdrawal leaf (`MessageHasher.cs:121-133` ↔ `SharedBridgeContract.cs:440-476`, chainId-first domain
separation, double-SHA256), the 344-byte `publicInputHash` preimage and root order
(`SettlementManagerContract.cs` binds the declared header value at offset 284 to the proof and re-checks
pre-state continuity at `:350,516-519`), the 321-byte header offsets, the 38-byte RISC-V payload, and the
Gateway roots — including that `duplicateOdd:true` at `SettlementManager:847-848` exactly reproduces
`MerkleTree.ComputeRoot`'s odd-leaf self-pairing versus the raw promotion in `BinaryTreeAggregator` /
`MerklePathRoundProver.Combine`. On-chain `Neo.Crypto.Hash256` is a faithful `Sha256(Sha256(x))`. The BN254
interop rejects non-canonical coordinates and enforces the G2 subgroup check, `Bn254Add`'s GT multiplication
is legitimate, so the verifier's 3-pairing Groth16 equation is valid and its own `IsCanonicalScalar` guards
are load-bearing; the `TryDeserializeScalar` reduction risk is closed. Checks-effects-interaction holds on
every payout path (consumed key before transfer: `SharedBridge:496-514`, `Escrow:547-559`,
`PayoutAdapter:121-131`) and all external verifier calls use `CallFlags.ReadOnly` (`Escrow:553`,
`Challenge:437`). `GovernanceController` payload binding (`MatchesProposalPayload`, distinct `neo4-gov:`
tags), council-epoch expiry, veto and `RotateCouncil` completeness are sound, and `SetAdmissionMode` can
only tighten. MPC threshold is enforced after member-index dedup, so committee blobs are not malleable.
v4 fraud proofs are profile-, batch-, chain- and `claimId`-bound, and the advisory v1/v2 and the verifier's
v3 path are unreachable from `Challenge` (`:403-422`). `unchecked(previousValue + amount)` at
`RestrictedExecutionFraudVerifier:644` is matched by `CounterChainExecutor.cs:126`. Zero storage-prefix
collisions across all 26 projects.

This is a materially more disciplined repo than typical for its size.

---

## 8. Dimension verdicts

| Dimension | Verdict | Basis |
| --- | --- | --- |
| **Correctness** | Good core, gaps at composition | Encodings, roots, proof binding verified exact (E1/E2). C1 and C2 are real defects; 6 further Medium correctness items are cross-subsystem seams never composed (multi-block batch context, dropped withdrawals, env→receipt conflation). |
| **Professionalism** | Excellent | E1 clean build with warnings-as-errors, clean format, clean mdbook, zero TODOs, universal test-project pairing, self-testing CI helpers. |
| **Consistency** | Good, quantified prose drifting | All fixed byte sizes match in code+contract+doc, and every off-chain↔on-chain encoding seam checked is byte-exact (§7). But 21/36 test-count rows stale, lib/subcommand counts off, one control overstated as shipped, zh root mirrors abstracts under a sync mandate their test cannot enforce — and two doc-vs-code contradictions in the contracts: `EmergencyManagerContract.cs:11-13` states an invariant only 1 of 3 asset-bearing contracts honours (H13), and `ScaffoldPlan.cs:417,460,501,505` sells `LockGovernance` as *the* irreversible production gate while 3 of 7 governance surfaces never implemented it (H12). |
| **Completeness** | Weakest dimension | "All phases ✅" is not true of the production wiring: only `ProofType.Zk` is registered by `Neo.Hub.Deploy`, Stage-0/1 settlement and the OptimisticChallenge/v4 path are unwired, and the forced-inclusion escape hatch faults without manual pauser registration. ~45 tests silently do not execute (§3.1). On the money path the adversarial depth is uneven: SettlementManager/Challenge/DAValidator/MPC/Sp1Groth16 suites are genuinely tamper-and-forgery testing, but `UT_SharedBridge_Vm.cs` is 3 methods and `UT_L2PayoutAdapter_Vm.cs` 1, leaving replay, forged-leaf, pause-gating and hostile-owner cases untested. |
| **Security** | Strong design, bounded by operator privilege rather than by code | Crypto hygiene clean, RPC surface read-only and well-bounded, atomicity real, proof soundness well-bound (E2), Groth16/BN254 path verified, CEI and `ReadOnly` discipline hold on every payout path (§7). No unprivileged theft path was found in any of the 26 contracts. The residual is that central-owner power is not irrevocably bounded: H12 leaves the fraud-verifier allowlist, the foreign-bridge dispatch table and the MPC committee replaceable forever, and proof systems 2-4 stay one owner call from no-proof settlement; H13 means the kill-switch does not stop 2 of 3 asset-bearing contracts. Plus a delta-less custody credit (M), and H4 unvalidated RPC logs, H9 compiler/prover gitlinks on force-pushable branches with a gate covering 1 of 5, H6 decorative off-chain binary pin; plus fail-open advisory gates (§3.4/3.5). |
| **Efficiency** | Adequate at MVP scale, not at target scale | Full-state re-root and 2-3× state materialization per batch (H11), O(N) `Seek` (`L2DataCacheAdapter.cs:126`), O(N) `Count`, whole-store copies on revert; unbounded dictionaries (H10). All are O(state-size)-per-batch, which is precisely what an L2 must not be. On L1, unpruned per-batch ≤1 MiB headers plus fee-less ≤128 KiB inbox writes put the GAS cost on shared contracts rather than the caller. |
| **Robustness** | Needs work — availability is the soft spot | H1 makes transient faults fatal to the node; C1 makes an ordinary usage pattern halt the chain; H2/H3 leave the anti-censorship path broken or dangerous; reorg handling throws instead of rewinding; sync-over-async puts L1 latency on the L2 commit thread; and the incident-response primitive itself is partial (H13), so "pause the network" leaves foreign-bridge payouts and batch settlement running. |

These rows are the as-written scores. C1 and H12 have since been remediated (see their status
notes, §9 items 1–2 and the §11 addendum), which lifts part of the Security and Robustness
residuals but none of the other rows.

---

## 9. Recommended remediation order

Recorded as written; items 1 and 2 landed on 2026-08-29 (see the status notes at C1 and H12).

1. ✅ **C1** — namespace the inbox dedup key; add the deposit+router combined-drain test that does not exist today.
2. ✅ **H12** — add one-way `LockGovernance` to `OptimisticChallenge`, `ExternalBridgeRegistry` and
   `MpcCommitteeVerifier` that disables the instant `Register*` paths, and call it from
   `LiveDeployCommand`. Small, mechanical, and it is the difference between "owner is honest today" and
   "owner cannot be evil later" on both fraud-proofing and the foreign-bridge dispatch table.
3. **H13 + custody delta** — either enforce the `IsPaused` invariant in `ExternalBridgeEscrow.Receive`,
   `SettlementManager.SubmitBatch`/`FinalizeBatch` or restate the invariant to match reality; and port
   `ExternalBridgeEscrow.TransferIntoCustody`'s `balanceOf` before/after check to `SharedBridge.Deposit`,
   `SequencerBond` and `ExternalBridgeBond`.
4. **H1** — `ExceptionPolicy => StopPlugin` on the L2 plugins + use the existing retry path.
5. **H4 / H5** — watcher header cross-check, non-zero confirmation floor in `build()`, and no submitted-persistence under the stub signer.
6. **H9** — move the four `codex/*` gitlinks onto protected branches; generalize `verify_neo_core_gitlink.py` to every submodule.
7. **§3.1–3.5** — restore verification integrity: `FindRepositoryRoot()`, zero-skip guard on the main Test step, `set -o pipefail`, per-lockfile audit ignore, committed native-host build step. Cheap, and it makes every other claim trustworthy.
8. **H2 / H3** — clamp the forced-inclusion deadline above the max challenge window and self-register the pauser, asserted in `IsProductionReady()`.
9. **C2** — position-bind `MerkleTree.Verify`; add randomized Merkle parity tests (today only fixed `{1,2,3,4,5,7,8,15,16}` shapes and 4 hand-written vectors exist, and nothing asserts `siblings.Count == depth(totalLeaves)` or bitmap/`LeafIndex` consistency).
10. **Contract proof/liveness hardening** — require `isEnvelopeOnlyLocked` + `isProofSystemConfigurationLocked`
    in `SettlementManager` for `securityLevel ≥ 3` and lock all four proof systems in the deploy; reject
    `MessageTypeCall` in `Receive` until dispatch exists; add route validation and a refund path to `Send`;
    mix `ExecutingScriptHash` + L1 chain magic into the attestation preimage; fee-gate `EnqueueL1ToL2`.
11. **H10 / H11** — eviction on the RPC store and router consumed-set; scope `_consumedNonces` per batch (or make the comment match reality); incremental dirty-key state root.
12. **H7 / H8** — port the commitment re-encode check to the TS/Python/Rust SDKs into the shared vectors; make the faucet journal mandatory.
13. **Consistency sweep** — regenerate the `IMPLEMENTATION_STATUS.md` counts from TRX, fix the two stale counts in `AGENTS.md` (16→17 libs, 12→13 subcommands and the now-false "prints plans only" sentence), reclassify ledger R3, correct the two dead wire-format paths, add "superseded" banners to the dated audit reports, narrow the `EmergencyManager` and `ScaffoldPlan` lock claims until H12/H13 land, and either genuinely mirror the four zh root docs or retitle them as abstracts.

## 10. Residual risk / not verified in this pass

`cargo test --workspace` cannot run on Windows (§3.3), so the SP1 host/guest proving crates and the
two `#[ignore]`d real-proof Rust tests were never exercised here; the C#↔Rust executor equivalence
rests on the committed Groth16 positive vector plus CI's Linux-only `sp1-release-gates`. The
hardcoded Groth16 VK points (Alpha/Beta/Gamma/Delta/IC0-5) and `digest[0] & 0x1F` reduction were not
independently derived — the passing 12-test verifier suite is the only evidence.
`contracts/` is now reviewed at source level across all 26 projects (§5, §6, §7, H12, H13), but three
things about it stay unproven here: H12's owner-hostile scenarios now have VM pins and were executed
(568 `NeoHub.Contracts.VmTests` cases green, including the post-lock challenge/slash and post-lock
attestation regressions) whereas H13's still have none; the `LiveDeployCommand` lock sequencing was
read and unit-pinned at the call-list/smoke-check level, but never executed, so no deploy actually ran
end-to-end against a chain; and the
Solidity side (`NeoExternalBridgeRouter.sol`) was read, not compiled or `forge test`ed. No L1/L2 deployment,
`forge test`, `cargo deny`
(not installed; no root `deny.toml`), coverage gate (`scripts/test-coverage.ps1` needs `pwsh`,
absent here), or NuGet/npm advisory-freshness run was performed — `cargo audit` used the locally
cached advisory DB via `--no-fetch`, and `npm audit` needed a registry override.
L2 contract artifact emission (`nccs`) was not run.

## 11. Addendum — findings surfaced while remediating C1 / H12 (2026-08-29)

These were produced by the E2 read + E1 execution needed to land §9 items 1–2, and were not part of
the original pass. §10's final line ("`nccs` was not run") is superseded by A4, which *did* run it.

**A4 — 20 of 25 committed VM contract artifacts cannot be reproduced from the pinned devpack, and 2
of them were stale against their own unchanged source** [E1 measured].
Decoding the NEF header of every `tests/NeoHub.Contracts.VmTests/TestingArtifacts/*.artifacts.cs`
gives two compiler provenances: **20 files** `Neo.Compiler.CSharp 3.9.1+5fa9566e5165ede2165a9be1f4a0120c17602697`
and **5 files** `3.9.1+82117c4799fde63e8c230e9e9696b66d794c6ed7` (`ChainRegistry`,
`ExternalBridgeEscrow`, `L2PayoutAdapter`, `MessageRouter`, `SettlementManager`). The submodule
gitlink is `git ls-tree HEAD external/neo-devpack-dotnet` → `82117c47…`, so only the *minority* was
emitted by the pinned compiler; the majority came from a machine-global `~/.dotnet/tools/nccs`
(`nccs --version` → `3.9.1+5fa9566e…`), which is not a build of the tracked tree and does not exist
in a clean clone. `AGENTS.md` presents `external/neo-devpack-dotnet` as the compiler source of
record, so the artifact set disagrees with the stated toolchain policy.
Independently, rebuilding two contracts whose C# is **untouched** at `728e630a`
(`NeoHub.ForcedInclusion`, `NeoHub.SharedBridge` — neither appears modified in `git status`) still
rewrote their committed artifacts: NEF `7,280 → 7,305` B and `8,591 → 8,702` B, manifest text
different, method and event *name* sets identical (offsets shifted). Both sides carry the same
compiler stamp, so the delta is source drift, not toolchain — those artifacts were generated from
older source and the repo never noticed. It would notice: the manifest-invariant tests compare
committed against freshly emitted, but they self-skip when no fresh build is present (§3.1), and they
only run under `NEO_N4_REQUIRE_FRESH_MANIFESTS=1` (that mode is green here: 19/19, matching CI's
`--minimum-tests 19`). So a stale artifact is invisible by default, and a "green" default run is not
evidence that `contracts/` and `TestingArtifacts/` agree.
Fix: emit artifacts only from the pinned submodule build (stop relying on the global tool), re-emit
all 25 in one pass so the set has a single provenance, and make fresh-manifest mode mandatory in any
CI job where a `contracts/**.cs` file changed.

**A5 — C1's fix removes the batcher halt; the router still has no V1 execution path, so enabling it
end-to-end remains blocked one stage later** [E2 verified].
`CanonicalL1MessageProcessor.ApplyBatchAsync` throws `NotSupportedException` for any non-`Deposit`
message (`src/Neo.L2.Executor/CanonicalNativeExecutionAdapter.cs:85-86`: "not supported by N4 genesis
V1"), while `ProofWitnessSerializers.Validate` accepts every *known* `MessageType` into the witness
(`src/Neo.L2.Batch/ProofWitnessSerializers.cs:292`) and `L2BatchPlugin` wires the router drain into
the production composition root whenever a router is supplied
(`src/Neo.Plugins.L2Batch/L2BatchPlugin.cs:140-146`). `ReferenceBatchExecutor` requires a canonical
processor for a non-empty inbox (`src/Neo.L2.Executor/ReferenceBatchExecutor.cs:84`), and the only
construction site in the repo passes none (`tools/Neo.L2.Devnet/Program.cs:227`).
Net effect: after C1 a combined deposit+router inbox seals instead of throwing, but a batch that
actually carries a `Call` / `Event` / governance entry still cannot be executed by the shipped V1
profile — the failure moved from "cannot seal" to "cannot execute", which is the same boundary §9
item 10 describes as "reject `MessageTypeCall` in `Receive` until dispatch exists". This is
recorded, not fixed, because closing it means specifying router application semantics in
`doc.md` §10 / `SPEC.md` (a spec change), not a patch. Until then the honest operator guidance is
"deposits + router may be drained together; router messages are not yet executable on L2".

**A6 — one gateway test's 5-second fake-daemon deadline is not parallel-safe, and CI runs the same
solution-wide invocation** [E1 observed].
The post-remediation `dotnet test Neo.L2.sln` run (38 assemblies, 2,844 passed / 1 failed / 45
skipped) failed exactly one case: `UT_Sp1GatewayProofProver.ProveAsync_RequestUsesCanonicalBindingAndBatchEncodings`
threw `TimeoutException: Gateway SP1 daemon did not publish …gateway-result.json within 00:00:05`
(`src/Neo.Plugins.L2Gateway/Sp1GatewayProofProver.cs:337`). Re-running
`tests/Neo.Plugins.L2Gateway.UnitTests` alone gives 103/103 green, so this is scheduling starvation
under the parallel run, not a regression — and it is unrelated to C1/H12. The 5 s bound is purely
test-local (`tests/Neo.Plugins.L2Gateway.UnitTests/UT_Sp1GatewayProofProver.cs:198-202` passes
`resultTimeout: 5 s` / `pollInterval: 10 ms` to an in-test fake responder; the production default at
`Sp1GatewayProofProver.cs:34` is 30 minutes), and CI's Test step uses the same solution-wide
`dotnet test Neo.L2.sln` (`.github/workflows/build.yml:97`), so any loaded runner can turn it red.
Fix: give that fake round-trip a load-insensitive budget (tens of seconds) or mark the class
`[DoNotParallelize]`.
