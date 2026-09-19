# L2 bridge supply-conservation model — 2026-09-18

Inductive safety model of the L2 bridge token-supply accounting in
`external/neo/src/Neo/SmartContract/Native/L2NativeContracts.cs` (`ApplyDeposit`
/ `InitiateWithdrawal`, ~lines 572-610). A handwritten abstraction, **not** a C#/NeoVM
verifier.

## Established properties

- The L2 circulating supply of each mapped asset equals cumulative minted minus cumulative
  burned and is **never negative** — the bridge neither mints out of nothing nor burns more
  than was bridged in.
- `ApplyDeposit` mints (replay-protected by a per-`(sourceChainId, nonce)` dedupe key) and
  `InitiateWithdrawal` burns under the token's own balance guard, so a withdrawal past the
  minted supply is rejected.
- Platform tokens (NEO/GAS) follow the same mint/burn path, so **GAS is not issued on L2**
  outside the bridge mint.
- Four negative controls build witnesses when the over-burn guard, the deposit-credit
  correspondence, the nonce-replay dedupe, or the balance guard is dropped (including a
  replayed deposit doubling supply from a single L1 event).

`verify_l2_bridge.py` has 15 obligations (8 UNSAT safety, 3 SAT feasibility, 4 SAT negative
controls). All pass. `l2-bridge-result.json` records solver, source/spec/script hashes and
trusted assumptions.

## Boundary and limits

This models the **L2 serving-supply ledger conservation** (minted = bridged-in, burned =
bridged-out, supply never negative). It does **not** model the L1-originated deposit proof
verification, the decimal scaling math, the `BridgedNep17` contract internals, or the
per-caller NEP-17 balance semantics (`TokenManagement`); it models the aggregate supply at
the ledger level the bridge maintains. GAS-supply gating (doc.md §13.2) is trusted to follow
the same path; the model does not prove the core's ChainMode-gated mint/burn hooks. This
closes the "L2 mint/burn / GAS" open obligation at the bridge-supply layer.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_l2_bridge.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift, line
endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.