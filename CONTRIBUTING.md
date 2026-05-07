# Contributing to Neo Elastic Network

> Thanks for your interest. This guide covers the layout, conventions, and the path from
> "I cloned the repo" to "my change is ready for review."

## Prerequisites

- **.NET 10 SDK** (`dotnet --version` must report `10.0.x`).
- **Rust toolchain** (1.75+) — only required if you touch `bridge/neo-zkvm-bridge`.
- The [`neo-project/neo`](https://github.com/neo-project/neo) Neo 4 core, vendored as a git
  submodule at `external/neo`. Run `git submodule update --init --recursive` after cloning
  (or use `git clone --recurse-submodules`). `Directory.Build.props` defaults `NeoCorePath`
  to the submodule path; override with `dotnet build /p:NeoCorePath=/path/to/neo/src` if
  you want to point at a local fork.
- (Optional) [`nccs`](https://github.com/neo-project/neo-devpack-dotnet) on `PATH` if you want
  the contract build step to emit `.nef` + `.manifest.json`. Without it, contracts still
  type-check.

## Quick start

```bash
# Type-check everything + run all 1156 tests
dotnet test Neo.L2.sln /p:NuGetAudit=false

# Run the in-process devnet demo
dotnet run --project tools/Neo.L2.Devnet -- 5

# Generate a NeoHub deploy bundle
dotnet run --project tools/Neo.Hub.Deploy -- scaffold --output deploy-plan.json
dotnet run --project tools/Neo.Hub.Deploy -- plan --plan deploy-plan.json --output bundle.json
```

`docs/getting-started.md` walks through more.

## Repo layout

```
neo4/
├── doc.md                           # master architecture spec (Chinese)
├── ARCHITECTURE.md                  # English distillation
├── IMPLEMENTATION_STATUS.md         # what's built vs what's deferred
├── CHANGELOG.md
├── Directory.Build.props            # NeoCorePath, MSTest version, NuGetAudit=false
├── Neo.L2.sln                       # the master solution
├── src/
│   ├── Neo.L2.Abstractions/         # interfaces + model records (the contract surface)
│   ├── Neo.L2.{Batch,State,Bridge,Messaging,Proving,Executor,…}/
│   ├── Neo.L2.Proving.Sp1/          # SP1 P/Invoke wrapper
│   ├── Neo.L2.{ForcedInclusion,Sequencer,Censorship}/
│   ├── Neo.L2.Settlement.Rpc/       # JSON-RPC client for L1
│   └── Neo.Plugins.L2*/             # neo-node Plugin subclasses
├── contracts/                       # 19 Neo SmartContract.Framework projects
│   ├── NeoHub.*/                    # 14 L1 contracts
│   └── L2Native.*/                  # 6 on-L2 native contracts
├── tools/
│   ├── Neo.Stack.Cli/               # neo-stack CLI
│   ├── Neo.L2.Devnet/               # neo-l2-devnet runnable demo
│   └── Neo.Hub.Deploy/              # declarative deploy planner
├── bridge/
│   └── neo-zkvm-bridge/             # Rust cdylib for SP1 prover P/Invoke
├── tests/                           # one *.UnitTests project per src/* and tools/* item
└── docs/
    ├── getting-started.md
    └── architecture-walkthrough.md
```

## Conventions

### Code style

- **`net10.0`**, `nullable enable`, `ImplicitUsings enable` for runtime libs (contracts and
  test projects keep implicit usings off because they have stricter framework needs).
- `TreatWarningsAsErrors = true`. Two exceptions: `CS1591` (missing XML docs on public APIs)
  and the `NU190x` NuGet-audit family (kept off for offline-friendly builds).
- Records over classes for data carriers (`ProofRequest`, `BatchExecutionResult`, etc.).
  When a record contains `ReadOnlyMemory<byte>`, **override `Equals` + `GetHashCode`** so
  byte-content participates in equality (see `L2BatchCommitment`).
- All public types get XML docs that **trace back to `doc.md` section numbers** in their
  `<remarks>`. IDE tooltips become navigation aids into the spec.

### Naming

- Off-chain library projects: `Neo.L2.<Component>` (`Batch`, `State`, `Bridge`, …).
- Plugin projects: `Neo.Plugins.L2<Component>` (the namespace is `Neo.Plugins.L2`, the
  assembly name follows the project name).
- Smart contracts: `NeoHub.<Component>` (L1) or `L2Native.<Component>` (on-L2). The contract
  class always ends in `Contract` (e.g. `ChainRegistryContract`).

### Canonical encodings

- Multi-byte integers are **little-endian** everywhere.
- Hashes are **`Hash256`** (double-SHA256), matching Neo's existing `MerkleTree` convention.
- `BigInteger` payloads are unsigned little-endian (`bi.ToByteArray(isUnsigned: true,
  isBigEndian: false)`).
- Cross-component encodings are pinned in one place: `BatchSerializer`, `MessageHasher`,
  `DepositPayload`. **Don't reinvent these in new components**; reuse or extend.

### Tests

- One `tests/<Project>.UnitTests/` for each `src/<Project>/` (and one for each `tools/<Tool>/`).
- MSTest with `Assert.ThrowsExactly<T>` (the analyzer flags `Assert.ThrowsException<T>`).
- Test methods named `<Subject>_<Behavior>` — e.g. `BatchBuilder_RejectsOutOfOrderBlocks`.
- Use `FakeClock` (in `Neo.L2.Censorship`) when you need controllable time; never let real
  `DateTime.UtcNow` leak into a test that asserts ordering.
- Integration tests live under `tests/Neo.L2.IntegrationTests/`. They wire multiple
  components together to lock in cross-cutting invariants.

## Adding a new component

1. **Source.** Create `src/Neo.L2.<MyComponent>/Neo.L2.<MyComponent>.csproj`. Reference the
   minimum upstream pieces it needs (typically `Neo.L2.Abstractions`).
2. **Public XML docs.** Every public type / method has at least one sentence and a `<remarks>`
   pointing at the `doc.md` section that motivates it.
3. **Tests.** Mirror under `tests/Neo.L2.<MyComponent>.UnitTests/`. Add a corresponding
   `Neo.L2.sln` entry.
4. **`AGENTS.md`.** Add a row to the §"Mapping `doc.md` to code" table so future agents can
   find your component.
5. **`IMPLEMENTATION_STATUS.md`.** Update the per-component table and the test count.
6. **`CHANGELOG.md`.** Add a bullet under `[Unreleased]`. Keep it terse — the diff is the
   spec; the changelog is the highlight reel.

## Adding a new wire format

If your component adds a canonical byte encoding (anything that crosses the off-chain ↔
on-chain boundary, e.g. proof payloads, message envelopes, Merkle proofs):

1. **Document the layout in the type's XML docs** as an offset/size table inside `<remarks>`.
   Match the format used by `BatchSerializer`, `FraudProofPayload`, `MerkleProofSerializer`, etc.
   A contract author parsing the bytes off the wire should be able to read the table without
   reading the C# encoder.
2. **Add a byte-layout test** that pins each documented offset. Future encoder reorders fail
   the test instead of silently breaking on-chain verifiers. See
   `UT_MerkleProofSerializer.Encode_LayoutMatchesSpec` for the pattern.
3. **Add the encoder to `AGENTS.md`'s "Canonical encodings" list.** This is the registry
   future agents consult before reinventing one.
4. **Add round-trip + rejection tests.** Verify Encode → Decode is identity, and that Decode
   throws on truncated / oversized / wrong-version input.

## Adding a smart contract

1. Create `contracts/<Domain>.<Name>/<Domain>.<Name>.csproj` (one-line wrapper that inherits
   `contracts/Directory.Build.props`).
2. Class under `<Domain>.<Name>` namespace, ending in `Contract`. Decorate with
   `[DisplayName]`, `[ContractAuthor]`, `[ContractDescription]`, `[ContractVersion]`,
   `[ContractSourceCode]`, `[ContractPermission(Permission.Any, Method.Any)]`.
3. Storage prefixes: pick a single byte per logical map. Keep the table at the top of the
   file as `private const byte Prefix<Foo> = 0x__;`.
4. Use `[Safe]` on every read-only public method.
5. Wire `_deploy(object data, bool update)` for one-shot init.
6. Encode events through `[DisplayName("…")] public static event Action<…> On<…> = default!;`.

The Neo `nccs` compiler picks up the `.csproj` and emits `.nef` + `.manifest.json`. Without
`nccs` on `PATH`, the C# still type-checks; you just don't get bytecode.

## PR checklist

- [ ] `dotnet test Neo.L2.sln /p:NuGetAudit=false` is green.
- [ ] If you added a public API, the XML doc points to a `doc.md` section.
- [ ] If you added a component, the per-component tables in `IMPLEMENTATION_STATUS.md` and
      `AGENTS.md` are updated.
- [ ] If a phase moved (🔴 → 🟡 → ✅), `IMPLEMENTATION_STATUS.md`'s phase matrix reflects it.
- [ ] `CHANGELOG.md` has a one-line entry under `[Unreleased]`.

## License

MIT — see [`LICENSE`](LICENSE). All contributions are accepted under the same terms.
