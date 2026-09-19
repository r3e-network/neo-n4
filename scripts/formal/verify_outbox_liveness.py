"""CTL reachability-liveness model of the Gateway publication outbox.

Uses the pyModelChecking temporal-logic model checker (CTL) over a Kripke structure of
the Gateway outbox states (GatewayOutbox.cs / L2GatewayPlugin.cs). Proves
reachability-liveness properties that the real system does guarantee, and shows the
negative controls (a dropped transition flips the property).

Honest liveness semantics: the real outbox, on retry exhaustion, moves to Poisoned and
waits for an operator to call RecoverPoisonedPublication before it can proceed. So the
correct liveness obligations are reachability-liveness (EF / AG EF): from every reachable
state there is a path to Confirmed, and a Poisoned publication is recoverable. We do NOT
claim unconditional termination (AF) under arbitrary re-entry into recovery, which the
implementation does not guarantee. See gateway-outbox-liveness-model.md.
"""
import argparse
import json
from pathlib import Path
import sys

import pyModelChecking
from pyModelChecking.CTL import *
from pyModelChecking.kripke import Kripke
import pyModelChecking.CTL.model_checking as mc

# State names.
SEALED, PROVING, PROVED, SUBMITTED, CONFIRMED, POISONED = "Sealed", "Proving", "Proved", "Submitted", "Confirmed", "Poisoned"
STATES = [SEALED, PROVING, PROVED, SUBMITTED, CONFIRMED, POISONED]


def label(state):
    return {state}


def kripke(with_recover=True, with_confirm=True, with_prove=True, with_submit=True):
    # Total transition relation: every state has at least one successor (Kripke totality).
    edges = []
    edges.append((SEALED, PROVING))
    if with_prove:
        edges.append((PROVING, PROVED))
        edges.append((PROVING, POISONED))   # retry exhaustion -> poison
        edges.append((PROVING, PROVING))    # a retry attempt not yet exhausted
    else:
        edges.append((PROVING, POISONED))   # no success path -> only poison
        edges.append((PROVING, PROVING))
    if with_submit:
        edges.append((PROVED, SUBMITTED))
        edges.append((PROVED, PROVED))
    else:
        edges.append((PROVED, PROVED))
    if with_confirm:
        edges.append((SUBMITTED, CONFIRMED))
        edges.append((SUBMITTED, SUBMITTED))
    else:
        edges.append((SUBMITTED, SUBMITTED))
    if with_recover:
        edges.append((POISONED, PROVING))
    edges.append((POISONED, POISONED))
    edges.append((CONFIRMED, CONFIRMED))
    return Kripke(S=STATES, S0=[SEALED], R=edges, L={s: label(s) for s in STATES})


def formula_holds(kr, formula):
    sat = mc.modelcheck(kr, formula)
    return kr.S0.issubset(sat)


def obligations():
    base = kripke()
    P, C, S, Po, Prov = (AtomicProposition(x) for x in
                         (PROVING, CONFIRMED, SUBMITTED, POISONED, PROVED))

    # --- reachability-liveness (hold) ---
    yield "sealed_eventually_confirmable", base, \
        AG(Imply(AtomicProposition(SEALED), EF(C))), True
    yield "all_states_can_reach_confirmed", base, AG(EF(C)), True
    yield "poisoned_is_recoverable", base, AG(Imply(Po, EF(P))), True
    yield "submitted_eventually_confirmable", base, AG(Imply(S, EF(C))), True
    yield "proved_eventually_confirmable", base, AG(Imply(Prov, EF(C))), True
    yield "proving_eventually_confirmable", base, AG(Imply(P, EF(C))), True
    # No deadlock trap: every state can reach a terminal (Confirmed) or a recoverable state.
    yield "no_deadlock_trap", base, AG(EF(Or(C, Po))), True

    # --- negative controls: drop a transition and the liveness property flips ---
    # Without recovery, a poisoned publication can never return to an active state.
    yield "negative_control_no_recovery", kripke(with_recover=False), \
        AG(Imply(Po, EF(P))), False
    # Without a confirm edge, submitted can never reach Confirmed.
    yield "negative_control_no_confirm", kripke(with_confirm=False), \
        AG(Imply(S, EF(C))), False
    # Without a prove-success edge, proving can never reach Confirmed.
    yield "negative_control_no_prove", kripke(with_prove=False), \
        AG(Imply(P, EF(C))), False


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=Path(__file__).with_name("gateway-outbox-liveness-result.json"))
    args = parser.parse_args()
    report = {"schema": "neo-n4/gateway-outbox-liveness-model/v1", "wholeSystemVerified": False,
              "scope": "CTL reachability-liveness of the Gateway publication outbox (pyModelChecking)",
              "tool": f"pyModelChecking {getattr(pyModelChecking, '__version__', '?')}",
              "obligations": [], "status": "failed",
              "trustedAssumptions": [
                  "the Kripke model is a handwritten abstraction of the outbox states",
                  "states: Sealed, Proving, Proved, Submitted, Confirmed, Poisoned",
                  "retry exhaustion moves Proving to Poisoned; recovery returns Poisoned to Proving",
                  "Confirmed is terminal (self-loop only)",
                  "reachability-liveness (EF/AG EF) is the correct notion: unconditional AF would",
                  "assume operator-free termination, which the real outbox does not guarantee",
                  "liveness here models the state machine, not RocksDB internals or L1 RPC"],
              "source": str(Path(__file__).resolve())}
    try:
        for name, kr, formula, expected in obligations():
            actual = formula_holds(kr, formula)
            report["obligations"].append({
                "name": name, "expected": expected, "actual": actual,
                "passed": actual == expected})
        if report["obligations"] and all(o["passed"] for o in report["obligations"]):
            report["status"] = "passed"
    except Exception as error:
        report["error"] = f"{type(error).__name__}: {error}"
    report["exitCode"] = 0 if report["status"] == "passed" else 1
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    for o in report["obligations"]:
        print(f"{o['name']}: actual={o['actual']} expected={o['expected']} passed={o['passed']}")
    print(f"status={report['status']}; exit={report['exitCode']}; report={args.output}")
    if "error" in report:
        print(report["error"])
    return report["exitCode"]


if __name__ == "__main__":
    sys.exit(main())