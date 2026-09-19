# Gateway outbox model — 2026-09-18

Inductive safety model of the Gateway publication outbox state machine in
`src/Neo.Plugins.L2Gateway/GatewayOutbox.cs` and `L2GatewayPlugin.cs`. A handwritten
abstraction, **not** a C#/NeoVM verifier.

## Established properties

- A publication progresses only `Sealed → Proving → Proved → Submitted → Confirmed`,
  and **`Confirmed` is terminal**: a reconciled epoch is never failed, re-proven, or
  re-published.
- **L1 confirmation requires `Submitted`**: an epoch that has not been published to L1
  cannot be marked confirmed.
- `RetryCount` is never negative and is monotone non-decreasing on failure; a failure
  never transitions a publication into `Confirmed`.
- **`Poisoned` is reached only after `RetryCount` reaches `maxAutomaticRetries`** — never
  before exhaustion.
- `RecoverPoisonedPublication` applies only to a `Poisoned` publication, resets retry to 0,
  and returns to an active non-terminal state (`Proving`/`Proved`).
- Four negative controls build witnesses when the confirm-requires-submitted guard, the
  poison-exhaustion guard, the poisoned-only-recovery guard, or the confirmed-is-terminal
  guard is dropped.

`verify_outbox.py` has 17 obligations (10 UNSAT safety, 3 SAT feasibility, 4 SAT negative
controls). All pass. `gateway-outbox-result.json` records solver, source/script/spec hashes
and trusted assumptions.

## Boundary and limits

This models the **outbox protocol state machine** — how an epoch is proved, submitted,
confirmed, poisoned after retry exhaustion, and recovered. It does **not** model RocksDB
internal crash-consistency, the durable write ordering of `SavePublication`/`MarkConfirmed`
(that is argued in the code comments and covered by persistence tests), or the actual L1
RPC confirmation semantics. `maxAutomaticRetries` is an abstract constant; the source
binding pins both `GatewayOutbox.cs` and `L2GatewayPlugin.cs`.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_outbox.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift, line
endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.