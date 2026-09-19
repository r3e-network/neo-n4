{
  "schema": "neo-n4/l2-bridge-model/v1",
  "wholeSystemVerified": false,
  "scope": "inductive safety of L2 bridge token-supply conservation (mint/burn)",
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
      "witness": "[amount = 1, supply = 0, burned = 0, minted = 0]"
    },
    {
      "name": "deposit_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "deposit_mint_tracks_deposit",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "deposit_supply_nonneg",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[burned = 0, supply = 1, amount = 1, minted = 1]"
    },
    {
      "name": "withdraw_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_mint_tracks_deposit",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_supply_nonneg",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "withdraw_drains_to_zero",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[burned = 0, amount = 1, supply = 1, minted = 1]"
    },
    {
      "name": "negative_control_over_burn",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[burned = 0, supply = 0, amount = 1, minted = 0]"
    },
    {
      "name": "negative_control_mint_without_deposit",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[burned = 0, supply = 0, amount = 1, minted = 0]"
    },
    {
      "name": "negative_control_nonce_replay_double_mint",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[burned = 0, supply = 0, amount = 1, minted = 0]"
    },
    {
      "name": "negative_control_withdraw_unguarded",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[burned = 0, supply = 0, amount = 1, minted = 0]"
    }
  ],
  "status": "passed",
  "trustedAssumptions": [
    "handwritten C#/NeoVM correspondence, not an extracted transition relation",
    "ApplyDeposit mints and InitiateWithdrawal burns per mapped asset",
    "deposits are replay-protected by a per-(sourceChainId, nonce) dedupe key",
    "withdrawal burns under the token's balance guard (no over-burn)",
    "platform tokens (GAS/NEO) follow the same mint/burn path; GAS is not issued on L2",
    "no in-place governance override or concurrency in the bridge ledger"
  ],
  "source": "D:\\Git\\neo-n4\\external\\neo\\src\\Neo\\SmartContract\\Native\\L2NativeContracts.cs",
  "scriptSha256": "54a10281d8bdb79e41139ed6d04ed0510efabfc7391466ec9df7054dd58163c6",
  "specSha256": "9d9cd67d5bf568382757704f0ac54e348c79cc373ae354430aeae42b991896b5",
  "sourceSha256": "526d03c6cfec375319a5f13e00532c10442e4cbe562a93a6a3303e7a8a6bd210",
  "exitCode": 0
}
