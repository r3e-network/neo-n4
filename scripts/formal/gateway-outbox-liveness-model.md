# Gateway outbox reachability-liveness model — 2026-09-18

CTL temporal-logic model of the Gateway publication outbox state machine, checked with
the **pyModelChecking** model checker (`pyModelChecking.CTL` + Kripke). It answers the
"composition / liveness" open obligation for the outbox at the finite-state level.

## What is checked

A Kripke structure over the outbox states (`Sealed, Proving, Proved, Submitted, Confirmed,
Poisoned`) with the real transitions: retry exhaustion moves `Proving → Poisoned`,
`Poisoned → Proving` on operator recovery, `Confirmed` is terminal, and prove/submit/confirm
edges are present. Reachability-liveness properties hold:

- `AG(Sealed → EF Confirmed)` — an initial publication is eventually confirmable.
- `AG EF Confirmed` — from every reachable state there is a path to `Confirmed`.
- `AG(Poisoned → EF Proving)` — a poisoned publication is recoverable.
- `AG(Submitted → EF Confirmed)` / `AG(Proved → EF Confirmed)` / `AG(Proving → EF Confirmed)`.
- `AG EF(Confirmed ∨ Poisoned)` — no deadlock trap; every state reaches a terminal.

Three negative controls drop a transition and the property flips to false: without recovery,
`Poisoned` can never return to an active state; without the confirm edge, `Submitted` can
never reach `Confirmed`; without the prove-success edge, `Proving` can never reach `Confirmed`.

## Honest liveness semantics

The real outbox moves to `Poisoned` on retry exhaustion and waits for an operator to call
`RecoverPoisonedPublication` before proceeding. So the correct liveness obligations are
**reachability-liveness** (`EF` / `AG EF`) — from every state a confirming or recoverable
path exists. We do **not** claim unconditional termination (`AF Confirmed`) under arbitrary
re-entry into recovery, which the implementation does not guarantee (a poisoned publication
stays until the operator acts). This is stated in the model's trusted assumptions.

## Facilities

Requires `pyModelChecking==1.3.4` (added to `scripts/formal/requirements.txt`). This is a
genuine temporal-logic model checker; the CTL obligations are solved exhaustively over the
finite Kripke structure, not approximated.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox_liveness.py
```

On Windows use `.venv-formal/Scripts/python`. Two self-tests verify all obligations pass and
that the tool is importable.

## Boundary

This covers the outbox **state machine's liveness** (eventual-confirmability, recoverability,
no-deadlock). It does not model full cross-component composition, network-time liveness, or
SP1 proof liveness; those remain separate obligations. The Kripke abstraction is handwritten;
the transition set mirrors GatewayOutbox.cs / L2GatewayPlugin.cs semantics.
