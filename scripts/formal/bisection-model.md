# Optimistic-challenge bisection game model — 2026-09-18

Inductive model of `src/Neo.L2.Challenge/BisectionGame.cs`: the interactive dispute
bisection that narrows a disputed batch to a single transaction for re-execution. A
handwritten abstraction, **not** verification of C#/NeoVM.

## Established properties

The game is a pure interval-narrowing state machine over per-tx checkpoint agreement
(`lo` = last known-agree index, `hi` = first known-disagree index). Modeled with an abstract
`diff[i]` predicate (do the two parties' checkpoint roots differ at index i), the induction
proves:

- **Preconditions imply the initial invariant**: preState (index 0) agrees, postState (index
  n) disagrees, so `lo=0` is an agreement index and `hi=n` a disagreement index.
- **Invariant preserved**: each round keeps `lo` an agreement index and `hi` a disagreement
  index (`¬diff[lo] ∧ diff[hi]`), so the dispute never escapes the interval.
- **Monotone narrowing**: each round strictly shrinks `hi-lo` (by at least 1) and increments
  `rounds`, so the game terminates.
- **Bounded rounds**: `rounds + (hi-lo) ≤ n` is a reachability invariant, so rounds ≤ n.
- **Settlement**: adjacent `hi-lo ≤ 1` settles to a single atomic disputed index at `lo`;
  the midpoint always advances (never stuck) and a round never widens the interval.

`verify_bisection.py` has 16 obligations (11 UNSAT safety/progress, 2 SAT feasibility, 3 SAT
negative controls: a reversed branch breaks the invariant, a disagreeing preState is
reachable if the constructor guard is dropped, and a growing interval is a bad state the
shrink-by-≥1 property excludes). All pass. `bisection-result.json` records solver,
source/script/spec hashes and trusted assumptions.

## Boundary and limits

This models the **bisection state machine** — interval invariants, monotone convergence and
settlement — not the on-chain recording, round deadlines, fraud-proof payload semantics, or
signature/witness verification (those live in ChallengeOrchestrator / the settlement contract
and are separate). The two parties' checkpoint sequences are abstracted to an agreement
predicate; the cryptographic identity of the checkpoints is out of scope.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_bisection.py
```

On Windows use `.venv-formal/Scripts/python`. Four self-tests cover source drift, line
endings, UNKNOWN, and the induction obligations.