{
  "schema": "neo-n4/forced-inclusion-model/v1",
  "wholeSystemVerified": false,
  "scope": "inductive safety of the RollupHub forced-inclusion FIFO queue abstraction",
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
      "name": "initial_nonce_zero",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "enqueue_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[tail = 0, head = 0]"
    },
    {
      "name": "enqueue_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "enqueue_head_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "enqueue_tail_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "enqueue_pending_nonneg",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "consume_enabled",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[tail = 0, count = 0, head = 0]"
    },
    {
      "name": "consume_preserves_invariant",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "consume_head_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "consume_tail_monotone",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "consume_pending_nonneg",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "consume_bounded_by_tail",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[tail = 0, count = 1, head = 0]"
    },
    {
      "name": "enqueue_tail_strictly_increases",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "negative_control_underflow",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[tail = 0, count = 1, head = 0]"
    },
    {
      "name": "negative_control_tail_wrap",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[head = 0, tail = 18446744073709551615]"
    },
    {
      "name": "negative_control_nonce_reuse",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[tail = 0, head = 0]"
    }
  ],
  "status": "passed",
  "trustedAssumptions": [
    "handwritten C#/NeoVM correspondence, not an extracted transition relation",
    "head/tail counters live in the uint64 domain (BigInteger storage, ulong reads)",
    "enqueue and consume are the only writers of the queue pointers",
    "consume count is a non-negative integer; the 352-byte public-input domain bounds it",
    "atomic fault rollback, no reentrancy, no concurrent enqueue/consume",
    "each batch seals forcedInclusionCount into the public-input hash (replay-bound)"
  ],
  "source": "D:\\Git\\neo-n4\\contracts\\NeoHub.RollupHub\\RollupHubContract.cs",
  "scriptSha256": "10c336de5651c6a1fb6c55e667d667321fab719efb385aff1957f6e9e478239c",
  "specSha256": "9d9cd67d5bf568382757704f0ac54e348c79cc373ae354430aeae42b991896b5",
  "sourceSha256": "f2b4f03648926c5fc29d2d19698ca1ff71a3c9e7b4cb576bc370dc60e4a070aa",
  "exitCode": 0
}
