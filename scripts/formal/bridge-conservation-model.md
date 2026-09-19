# Bridge conservation model — 2026-09-18

Inductive safety model of the SharedBridge per-(chainId, asset) escrow accounting.
A handwritten abstraction, **not** a C#/NeoVM verifier. It models the locked-balance
ledger against `Deposit`, `FinalizeWithdrawal*` / `ConsumeAndPayout` and
`Credit/DebitLockedBalance` (NeoHub.SharedBridgeContract lines 265–297, 538–575).

## Established properties

- Escrow is never negative: `locked >= 0` at every reachable state.
- Conservation holds: `locked == deposited - paid`, so payouts never exceed deposits
  (no asset is minted out of nothing and no chain pays more than it escrowed).
- `deposit(amount>0)` transfers the asset in and credits; `withdraw` debits under the
  `currentBal >= amount` guard and pays out, so a withdrawal past the escrow is rejected.
- A withdraw exactly equal to the escrow drains it to zero (valid, not a fault).
- Three negative controls build witnesses when the balance guard, replay protection, or
  the deposit-credit correspondence is dropped (over-payout, double payout, mint-out-of-nothing).

`verify_bridge.py` has 14 obligations: initial checks, two transition families with
invariant/conservation/payout and non-negative-locked checks, a drain-to-zero reachability,
and 3 SAT negative controls. All pass. `bridge-conservation-result.json` records solver,
source/script/spec hashes and trusted assumptions.

## Boundary and limits

Conservation here covers the **L1 escrow ledger** (per-chain locked balance = deposits −
payouts). It does not prove L2 mint/burn or the GAS supply model (those live in the L2 core
native contracts and are separate obligations), nor message hashing, withdrawal-leaf
Merkle binding, or the settlement manager's verifyWithdrawalLeaf path (covered by the
settlement model and VM tests). Replay protection is modeled at the ledger level as
"payout ≤ deposited"; the per-leaf consumed-key bit is trusted as the mechanism that
prevents a single leaf from paying twice, which the double-payout negative control
illustrates would otherwise break conservation.

## Reproduce

```sh
.venv-formal/bin/python -m unittest discover -s scripts/formal -p 'test_*.py' -v
.venv-formal/bin/python scripts/formal/verify_bridge.py
```

On Windows use `.venv-formal/Scripts/python`. Five self-tests cover source drift, line
endings, UNKNOWN, counterexample rejection, and the normal model/negative controls.