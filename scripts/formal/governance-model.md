# Governance authorization model — 2026-09-18

Inductive safety model of the GovernanceController proposal-authorization gate.
A handwritten abstraction, **not** a C#/NeoVM verifier. It models the council-vote /
timelock / veto / epoch gate behind `IsApprovedAndTimelocked`
(GovernanceControllerContract lines 548-558).

## Established properties

The gate is sound: an action guarded by `IsApprovedAndTimelocked` can execute only when
all of the following hold — the proposal reached the M-of-N approval threshold
(`approvedAt` recorded), the configured timelock has elapsed since first
threshold-reach, the proposal is not vetoed, and its epoch matches the current council
epoch. Every transition (approve / veto / time-advance) preserves the invariant and the
gate's implications (threshold reached, not vetoed, timelock elapsed, epoch match).

`approve` records approval only on the first crossing of the threshold (a later vote
cannot reset the timer), `veto` is permanent, and time only moves forward. A vetoed
proposal can never satisfy the gate again. Four negative controls build witnesses when
any single gate conjunct is dropped (unapproved proposal, vetoed proposal, pre-timelock
execution, stale epoch) — proving every conjunct is load-bearing.

`verify_governance.py` has 26 obligations: initial checks, three transitions with five
gate-soundness checks each, the veto-blocks-gate obligation, and four SAT negative
controls. All pass. `governance-result.json` records solver, source/script/spec hashes
and trusted assumptions.

## Boundary and limits

The model proves the authorization gate's logic, not the cryptographic signature checks
behind `CheckWitness`/council-membership, not proposal payload encoding/binding, and not
the `Runtime.Time` source. It abstracts epoch and timelock to booleans/integers as the
contract reads them; the wall-clock monotonicity of `Runtime.Time` is trusted. Council
rotation is modeled only insofar as it changes the epoch match flag. These are separate
obligations and are not claimed here.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_governance.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift, line
endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.