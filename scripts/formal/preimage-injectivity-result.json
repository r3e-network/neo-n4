{
  "schema": "neo-n4/preimage-injectivity-model/v1",
  "wholeSystemVerified": false,
  "scope": "structural injectivity of MessageHasher canonical preimages",
  "solver": "4.15.3",
  "obligations": [
    {
      "name": "chainid_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "nonce_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "target_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "sender_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "receiver_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "type_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "payload_length_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "payload_binds_preimage",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "chainid_domain_separates",
      "expected": "unsat",
      "actual": "unsat",
      "passed": true
    },
    {
      "name": "distinct_messages_feasible",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[sc = 4294967295, sc2 = 0]"
    },
    {
      "name": "negative_control_without_length_prefix",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[plen = 4294967295,\n plen2 = 0,\n nonce2 = 0,\n nonce = 0,\n mtype2 = 0,\n mtype = 0,\n sender2 = 0,\n sender = 0,\n sc2 = 0,\n sc = 0,\n receiver2 = 0,\n receiver = 0,\n tc2 = 0,\n tc = 0]"
    },
    {
      "name": "negative_control_without_nonce",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[nonce = 18446744073709551615,\n nonce2 = 0,\n tc2 = 0,\n tc = 0,\n mtype2 = 0,\n mtype = 0,\n sender2 = 0,\n sender = 0,\n receiver2 = 0,\n receiver = 0,\n sc2 = 0,\n sc = 0]"
    },
    {
      "name": "negative_control_without_chainid",
      "expected": "sat",
      "actual": "sat",
      "passed": true,
      "witness": "[sc = 4294967295,\n sc2 = 0,\n nonce2 = 0,\n nonce = 0,\n mtype2 = 0,\n mtype = 0,\n sender2 = 0,\n sender = 0,\n receiver2 = 0,\n receiver = 0,\n tc2 = 0,\n tc = 0]"
    }
  ],
  "status": "passed",
  "trustedAssumptions": [
    "handwritten C#/NeoVM correspondence, not an extracted transition relation",
    "fixed-width fields are little-endian and occupy disjoint positions",
    "the variable payload is length-prefixed (boundary unambiguous)",
    "Hash256 (double-SHA256) is collision-free \u2014 a separate cryptographic trust",
    "this proves distinct preimages, not the absence of SHA-256 collisions",
    "no concurrent writers or reentrancy in the hashing path"
  ],
  "source": "D:\\Git\\neo-n4\\src\\Neo.L2.State\\MessageHasher.cs",
  "scriptSha256": "dd7bbed687f632fb174cd8a80c095ae280bc686346a0f38cff2113feb7fe7849",
  "specSha256": "9d9cd67d5bf568382757704f0ac54e348c79cc373ae354430aeae42b991896b5",
  "sourceSha256": "fa5f4a04350ad076df4a89109c18dbffdf0e171af4b90383c97f553548e4da98",
  "exitCode": 0
}
