# Neo Core Fork Policy

Neo N4 uses maintained r3e Neo core fork branches rather than building directly
from `neo-project/neo`. The fork has separate ownership boundaries for the L1
anchor core and the L2 execution core.

## Repositories

| Repository | Role |
| --- | --- |
| `r3e-network/neo-n4` | Elastic-network integration repo. Owns contracts, L2 libraries, plugins, tools, SDKs, docs, and tests. |
| `r3e-network/neo` | Maintained Neo core fork. Owns N4-required native contracts, ChainMode, execution-kernel hooks, and consensus/RPC core deltas. |
| `neo-project/neo` | Read-only upstream source. Used for review and controlled syncs only. Do not push here. |

## Core Branch Matrix

| Layer | Upstream base | r3e maintained branch | Used for |
| --- | --- | --- | --- |
| L1 core | `neo-project/neo` `master-n3` | `r3e/neo-n3-core` | Neo N3 L1 anchor behavior, L1 native-contract work, and any future NeoHub-in-core experiment. |
| L2 core | `neo-project/neo` `master` | `r3e/neo-n4-core` | Neo 4 L2 execution kernel, L2 mode, L2 native contracts, execution hooks, RPC/consensus deltas. |

The `external/neo` submodule in this repo intentionally points at the L2 core
branch:

```text
url    = https://github.com/r3e-network/neo.git
branch = r3e/neo-n4-core
```

The L1 core branch exists in the same `r3e-network/neo` fork, but it is not the
default submodule checkout for `neo-n4`. L1 core changes should be made on
`r3e/neo-n3-core`; L2 core changes should be made on `r3e/neo-n4-core`.

## Dependency Flow

```mermaid
flowchart LR
    upstreamL1["neo-project/neo\nmaster-n3"] --> l1Fork["r3e-network/neo\nr3e/neo-n3-core"]
    upstreamL2["neo-project/neo\nmaster"] --> l2Fork["r3e-network/neo\nr3e/neo-n4-core"]
    l2Fork --> submodule["neo-n4/external/neo\npinned L2 core commit"]
    submodule --> build["Neo.L2.sln\nproject references"]
    build --> tests["unit, integration,\nprivate-network verification"]
    l1Fork --> neohubBoundary["NeoHub L1 anchor\ncore-level changes only"]
```

## Change Placement

```mermaid
flowchart TD
    change["Requested N4 change"] --> l1{"Needs Neo N3 L1 core\nor L1 native-contract behavior?"}
    l1 -- "yes" --> l1Fork["Implement in r3e-network/neo\nr3e/neo-n3-core"]
    l1 -- "no" --> l2{"Needs Neo 4 L2 core,\nL2 mode, or L2 native contracts?"}
    l2 -- "yes" --> l2Fork["Implement in r3e-network/neo\nr3e/neo-n4-core"]
    l2 -- "no" --> n4Repo["Implement in r3e-network/neo-n4"]
    l1Fork --> l1Verify["Run Neo core L1 tests\nand neo-n4 integration checks"]
    l2Fork --> pointer["Update external/neo submodule pointer"]
    pointer --> verify["Run neo-n4 build and test matrix"]
    n4Repo --> verify
```

## Native Contract Boundary

N4 L2 system contracts are L2 core protocol surfaces and must live on
`r3e/neo-n4-core` under
`external/neo/src/Neo/SmartContract/Native/`. They are registered by
`NativeContract` and exist at genesis on every N4 L2 chain. They must not be
reintroduced as DevPack projects under `contracts/L2Native.*`, added to
`Neo.L2.sln`, or deployed later through `Neo.Hub.Deploy`.

The current L2 native set is:

- `L2SystemConfigContract`
- `L2BatchInfoContract`
- `L2MessageContract`
- `L2BridgeContract`
- `L2FeeContract`
- `L2PaymasterContract`
- `L2NativeExternalBridgeContract`
- `L2AccountAbstraction`
- `BridgedNep17Contract`
- `L2InteropVerifier`

NeoHub L1 contracts are a different boundary: they are the L1 anchor contracts
that mirror ZKsync's L1 Bridgehub/shared-bridge ecosystem. The production
target is now native L1 core ownership on `r3e/neo-n3-core`. The
`contracts/NeoHub.*` DevPack projects in `neo-n4` remain as parity/reference
sources and deployment-rehearsal fixtures until their full behavior has been
ported into the L1 core fork and covered by Neo core tests.

Current L1 native migration status in `r3e/neo-n3-core`:

- Native and tested: `NeoHubChainRegistryContract`, `NeoHubTokenRegistryContract`,
  `NeoHubDARegistryContract`, `NeoHubL1TxFilterContract`,
  `NeoHubVerifierRegistryContract`, `NeoHubMessageRouterContract`,
  `NeoHubSettlementManagerContract`, `NeoHubDAValidatorContract`,
  `NeoHubSharedBridgeContract`.
- Still to migrate before claiming full L1-native NeoHub: `EmergencyManager`, `GovernanceController`,
  `SequencerBond`,
  `SequencerRegistry`, `ForcedInclusion`, `OptimisticChallenge`,
  `GovernanceFraudVerifier`, `RestrictedExecutionFraudVerifier`,
  `MpcCommitteeVerifier`, `MpcCommitteeFraudVerifier`,
  `ExternalBridgeRegistry`, `ExternalBridgeEscrow`, `ExternalBridgeBond`.

Do not remove the DevPack `contracts/NeoHub.*` projects or rewrite operator
docs to say "fully native" until every production NeoHub contract has a native
counterpart, registration tests, behavior tests, and neo-n4 parity/integration
coverage.

Use the fork for changes that cannot be implemented cleanly as a plugin,
library, contract, SDK, or operator tool in `neo-n4`. Typical fork-owned work
includes:

- L1 core/native-contract behavior that must exist inside the Neo N3 L1 node.
- L2-aware native contracts and native-contract policy gates.
- `ChainMode` and activation hooks.
- Core execution-kernel hooks needed by deterministic L2 state transition.
- Consensus/RPC behavior that must exist inside Neo core.

Keep the change in `neo-n4` when it can live in L2 plugins, NeoHub contracts,
watchers, SDKs, CLIs, docs, or integration harnesses without modifying Neo core.

## Sync Procedure

Use this flow when refreshing the L2 core branch from upstream:

```bash
cd external/neo
git remote -v
git fetch upstream master
git switch r3e/neo-n4-core
git merge upstream/master
git push origin r3e/neo-n4-core

cd ../..
git add external/neo
dotnet test Neo.L2.sln /p:NuGetAudit=false
```

Use this flow when refreshing the L1 core branch from upstream:

```bash
cd external/neo
git fetch upstream master-n3
git switch r3e/neo-n3-core
git merge upstream/master-n3
git push origin r3e/neo-n3-core
git switch r3e/neo-n4-core
```

If either merge is not fast-forward, resolve it inside `r3e-network/neo`, run
the appropriate Neo core tests there first, then update the `neo-n4` submodule
pointer only for L2-core changes.

## Push Safety

Local `external/neo` should be configured with:

```text
origin   https://github.com/r3e-network/neo.git
upstream https://github.com/neo-project/neo.git
upstream push URL disabled
```

Do not push to `neo-project/neo`. All core commits go to `r3e-network/neo`:
L1 commits on `r3e/neo-n3-core`, and L2 commits on `r3e/neo-n4-core`.
