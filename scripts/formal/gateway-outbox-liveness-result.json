{
  "schema": "neo-n4/gateway-outbox-liveness-model/v1",
  "wholeSystemVerified": false,
  "scope": "CTL reachability-liveness of the Gateway publication outbox (pyModelChecking)",
  "tool": "pyModelChecking 1.3.4",
  "obligations": [
    {
      "name": "sealed_eventually_confirmable",
      "expected": true,
      "actual": true,
      "passed": true
    },
    {
      "name": "all_states_can_reach_confirmed",
      "expected": true,
      "actual": true,
      "passed": true
    },
    {
      "name": "poisoned_is_recoverable",
      "expected": true,
      "actual": true,
      "passed": true
    },
    {
      "name": "submitted_eventually_confirmable",
      "expected": true,
      "actual": true,
      "passed": true
    },
    {
      "name": "proved_eventually_confirmable",
      "expected": true,
      "actual": true,
      "passed": true
    },
    {
      "name": "proving_eventually_confirmable",
      "expected": true,
      "actual": true,
      "passed": true
    },
    {
      "name": "no_deadlock_trap",
      "expected": true,
      "actual": true,
      "passed": true
    },
    {
      "name": "negative_control_no_recovery",
      "expected": false,
      "actual": false,
      "passed": true
    },
    {
      "name": "negative_control_no_confirm",
      "expected": false,
      "actual": false,
      "passed": true
    },
    {
      "name": "negative_control_no_prove",
      "expected": false,
      "actual": false,
      "passed": true
    }
  ],
  "status": "passed",
  "trustedAssumptions": [
    "the Kripke model is a handwritten abstraction of the outbox states",
    "states: Sealed, Proving, Proved, Submitted, Confirmed, Poisoned",
    "retry exhaustion moves Proving to Poisoned; recovery returns Poisoned to Proving",
    "Confirmed is terminal (self-loop only)",
    "reachability-liveness (EF/AG EF) is the correct notion: unconditional AF would",
    "assume operator-free termination, which the real outbox does not guarantee",
    "liveness here models the state machine, not RocksDB internals or L1 RPC"
  ],
  "source": "D:\\Git\\neo-n4\\scripts\\formal\\verify_outbox_liveness.py",
  "exitCode": 0
}
