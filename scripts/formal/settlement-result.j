{
  "schema": "neo-n4/settlement-model/v1",
  "wholeSystemVerified": false,
  "scope": "inductive safety of handwritten registered-chain settlement abstraction",
  "solver": "4.15.3",
  "obligations": [
    {
      "name": "initial_domain_nonempty",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[genesis = 115792089237316195423570985008687907853269984665640564039457584007913129639935]"
    },
    {
      "name": "initial_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "submit_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[genesis = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n index = 1,\n number = 1,\n verified = K(Int, False),\n pre = K(Int,\n         115792089237316195423570985008687907853269984665640564039457584007913129639935),\n post = K(Int, 0),\n proof_ok = True,\n root = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n da_fresh = True,\n operational = True,\n input_pre = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n pending = False,\n height = 0]"
    },
    {
      "name": "submit_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "submit_preserves_finalized_history",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "submit_height_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "finalize_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[index = 1,\n number = 1,\n verified = K(Int, False),\n genesis = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n pre = K(Int,\n         115792089237316195423570985008687907853269984665640564039457584007913129639935),\n post = K(Int, 0),\n root = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n pending_pre = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n owner = True,\n operational = True,\n pending = True,\n height = 0,\n pending_verified = True,\n pending_number = 1]"
    },
    {
      "name": "finalize_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "finalize_preserves_finalized_history",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "finalize_height_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "atomic_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[index = 0,\n verified = K(Int, False),\n genesis = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n number = 1,\n pre = K(Int, 0),\n post = K(Int, 0),\n proof_ok = True,\n root = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n da_fresh = True,\n operational = True,\n input_pre = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n pending = False,\n height = 0]"
    },
    {
      "name": "atomic_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "atomic_preserves_finalized_history",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "atomic_height_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "fault_stutter_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[height = 0,\n pending = False,\n index = 1,\n verified = K(Int, False),\n genesis = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n pending_verified = False,\n pre = K(Int,\n         115792089237316195423570985008687907853269984665640564039457584007913129639935),\n post = K(Int, 0),\n pending_number = 1,\n pending_pre = 115792089237316195423570985008687907853269984665640564039457584007913129639935,\n root = 115792089237316195423570985008687907853269984665640564039457584007913129639935]"
    },
    {
      "name": "fault_stutter_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "fault_stutter_preserves_finalized_history",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "fault_stutter_height_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "negative_control_missing_pre_root_guard",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[input_pre = 115792089237316195423570985008687907853269984665640564039457584007913129639934,\n index = 1,\n genesis = 1,\n post = K(Int, 0),\n da_fresh = True,\n root = 1,\n number = 1,\n operational = True,\n pending = False,\n proof_ok = True,\n height = 0]"
    },
    {
      "name": "negative_control_missing_canonical_root_update",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[verified = K(Int, False),\n post = Store(Store(K(Int, 0),\n                    0,\n                    5444517870735015415413993718908425601024),\n              -25907,\n              3618502788666131106986593281521497120414687020801267626233049500247285301248),\n index = -25906,\n genesis = 3379722536327712998346051398801894848683711660032,\n pre = K(Int, 0),\n number = 1,\n input_post = 28269553036454149273332760011886696253239742350009903329945699220681916416,\n proof_ok = True,\n root = 3379722536327712998346051398801894848683711660032,\n da_fresh = True,\n operational = True,\n input_pre = 3379722536327712998346051398801894848683711660032,\n pending = False,\n height = 0]"
    },
    {
      "name": "negative_control_unverified_finalization",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[index = 1,\n genesis = 1,\n root = 1,\n operational = True,\n input_pre = 1,\n pending = False,\n height = 0,\n proof_ok = True,\n number = 1,\n da_fresh = True]"
    }
  ],
  "status": "passed",
  "trustedAssumptions": [
    "handwritten C#/NeoVM correspondence, not an extracted transition relation",
    "chain and nonzero immutable genesis already registered",
    "atomic fault rollback, no reentrancy or hidden storage writers",
    "height arithmetic does not wrap; terminal height has no successor",
    "proof_ok models successful external verification, not cryptographic soundness",
    "no governance rollback, reconfiguration or concurrency transitions"
  ],
  "source": "D:\\Git\\neo-n4\\contracts\\NeoHub.RollupHub\\RollupHubContract.cs",
  "scriptSha256": "5da14141aed3f5ee20a782ff76cb101e9fee1514419ba847f6ebcc1e1e0c13ee",
  "specSha256": "9d9cd67d5bf568382757704f0ac54e348c79cc373ae354430aeae42b991896b5",
  "sourceSha256": "f2b4f03648926c5fc29d2d19698ca1ff71a3c9e7b4cb576bc370dc60e4a070aa",
  "exitCode": 0
}
