{
  "schema": "neo-n4/bridge-conservation-model/v1",
  "wholeSystemVerified": false,
  "scope": "inductive safety of SharedBridge per-(chain,asset) escrow accounting",
  "solver": "4.15.3",
  "obligations": [
    {
      "name": "initial_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "initial_empty",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "deposit_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[amount = 1, locked = 0, paid = 0, deposited = 0]"
    },
    {
      "name": "deposit_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "deposit_payout_leq_deposited",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "deposit_locked_nonneg",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[amount = 1, locked = 1, paid = 0, deposited = 1]"
    },
    {
      "name": "withdraw_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_payout_leq_deposited",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_locked_nonneg",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_drains_to_zero",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[amount = 1, paid = 0, deposited = 1, locked = 1]"
    },
    {
      "name": "negative_control_over_payout",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[amount = 1, locked = 0, paid = 0, deposited = 0]"
    },
    {
      "name": "negative_control_double_payout",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[amount = 1, paid = 0, locked = 1, deposited = 1]"
    },
    {
      "name": "negative_control_credit_without_deposit",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[amount = 1, locked = 0, paid = 0, deposited = 0]"
    }
  ],
  "status": "passed",
  "trustedAssumptions": [
    "handwritten C#/NeoVM correspondence, not an extracted transition relation",
    "deposit transfers the asset in and credits; withdraw debits and pays out",
    "withdrawal replay protection: a consumed withdrawal leaf is paid at most once",
    "per-(chainId,asset) escrow is the only balance mutated by deposit/withdraw",
    "atomic fault rollback, no reentrancy, no concurrent deposit/withdraw",
    "bridge conservation here covers L1 escrow accounting, not L2 mint/burn or GAS"
  ],
  "source": "D:\\Git\\neo-n4\\contracts\\NeoHub.SharedBridge\\SharedBridgeContract.cs",
  "scriptSha256": "ce8d9010f8d09c74bd4ac9bd43ac42d4c14aa7a4c2534877108c2147cc124020",
  "specSha256": "9d9cd67d5bf568382757704f0ac54e348c79cc373ae354430aeae42b991896b5",
  "sourceSha256": "906945c4115aad7885436862b7dbfbff01c17e20bad7436defd22004062e1b85",
  "exitCode": 0
}
