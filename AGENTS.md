# AGENTS.md — Guide for AI Agents

This file orients agents working on `neo4`. Read it before making changes.

## Authority order
1. **`doc.md`** — the master architecture spec (Chinese). Treat as the implementation contract. Section numbers in this file refer to `doc.md` sections.
2. **`ARCHITECTURE.md`** — English distillation; for reference only, not authoritative.
3. **Code-level conventions** — neo-project/neo C# style; net10.0; nullable enabled; warnings as errors.

If `doc.md` and code disagree, the spec wins. If `doc.md` and reality disagree (e.g. a `neo-project/neo` API has changed), surface the conflict and propose an update before silently diverging.

## Working scope

`neo4` is a **consolidation layer**, not a fork. It adds the L2 / NeoHub / Gateway / Stack components on top of pre-existing Neo ecosystem repos in `/home/neo/git/`:

- `neo` — official Neo 4 core (net10.0). Reference as a project sibling (see `Directory.Build.props` `NeoCorePath`).
- `neo-riscv-{vm,core,node}`, `neo-llvm` — RISC-V VM stack (NeoVM2 candidate).
- `neo-zkvm`, `neo-axiom` — ZK proof systems.
- `neo-execution-specs` — deterministic state-transition spec (proving target).
- `neo-sovereign-rollup`, `neo-matrix`, `neo-nexus` — existing rollup/orchestration/portal work.
- `neo-devpack-dotnet` — compile C# to NeoVM (used for `contracts/`).

**Before writing a new component, search the relevant repo above for an existing implementation and prefer reuse.**

## Mapping `doc.md` to code

| `doc.md` § | Code location                          |
| ---------- | -------------------------------------- |
| §3.2 NeoHub               | `contracts/NeoHub.*/`             |
| §4 Neo Gateway            | `src/Neo.Plugins.L2Gateway/`      |
| §5 L2 node internals      | `src/Neo.Plugins.L2*/`            |
| §7.1 Sequencer            | reuse `Neo.Plugins.DBFTPlugin`    |
| §7.2 Batcher              | `src/Neo.L2.Batch/` + plugin      |
| §7.3 StateRootGenerator   | `src/Neo.L2.State/`               |
| §7.4 DAWriter             | `src/Neo.Plugins.L2DA/`           |
| §7.5 ProverAdapter        | `src/Neo.L2.Proving/`             |
| §10 Neo Connect           | `src/Neo.L2.Messaging/`           |
| §11 SharedBridge          | `contracts/NeoHub.SharedBridge/` + `src/Neo.L2.Bridge/` |
| §13 L2 native contracts   | `contracts/L2Native.*/`           |
| §14.1 L2 RPC              | extends `Neo.Plugins.RpcServer`   |
| §14.2 neo-stack CLI       | `tools/Neo.Stack.Cli/`            |
| §19 code modules          | `src/Neo.L2.*/`, `src/Neo.Plugins.L2*/` |

## Conventions

- **Namespaces:** `Neo.L2.*`, `Neo.Plugins.L2.*`, `NeoHub.*`, `L2Native.*`, `Neo.Stack.*`.
- **Pluggable, not hard-coded:** verifier, prover, DA writer, message adapter must be interface-driven so phases can swap implementations (multisig → optimistic → ZK; L1 DA → NeoFS DA).
- **Don't reinvent Neo.MPTTrie / dBFT / NEP-17** — reuse from `neo` core.
- **Smart contracts** live under `contracts/`. C# .NET source compiled to `.nef`/`.manifest.json` via devpack.
- **Test parity:** every L2 component has a unit test project under `tests/`.

## Phased work

Don't try to build phases 4–6 before phase 0–2 land. The MVP target (`doc.md` §20) is:
1. Deposit GAS N3 → L2 (one chain only)
2. Deploy / call a contract on L2
3. Generate batch commitment
4. Submit batch to NeoHub
5. Withdraw GAS L2 → N3 with `withdrawalRoot` proof

Stage-0 attestation proof (validator multisig) is enough for MVP. Optimistic and ZK come later.

## Don'ts

- **Don't fork** `neo-project/neo`. Extend via plugins and references.
- **Don't issue canonical GAS on L2** outside the bridge mint path.
- **Don't bypass `ChainRegistry`** — every L2 must register before submitting batches.
- **Don't write `// added for X` or `// TODO once Y` style comments** — keep code clean; track followups in tasks.
- **Don't add config fields not in `doc.md`** without proposing a spec update first.
