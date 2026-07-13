# Shared SDK conformance vectors

`vectors/v1.json` is the language-neutral contract consumed by the .NET,
Rust, TypeScript, and Python SDK conformance suites. A change to RPC wire
semantics must update this file and all four consumers in one review.

The vectors pin:

- all ten public L2 RPC methods plus the historical state-root overload;
- JSON string encoding for every `u64`, including `u64::MAX`;
- strict JSON-RPC 2.0 envelopes and the common error taxonomy;
- numeric response-id echo plus chain and request-identity binding;
- Neo `UInt256` little-endian bytes versus reversed RPC display text;
- lossless cursor/page serialization above JavaScript's safe-integer limit;
- a complete Neo N3 P-256 single-signature transaction, including unsigned
  bytes, txid, network-bound sign data, witness scripts, and raw transaction.
- explicit L1/L2 chain-domain values and fail-closed malformed-response cases.

Real-node execution additionally consumes one operator-owned fixture matching
[`live-fixture.example.json`](./live-fixture.example.json). The fixture pins the
N3/N4 network magic and genesis hashes plus exact, non-empty results for batch,
proof, bridge, message, state, and security queries. It is deployment evidence,
so the checked-in example is a schema template and must never be reported as a
successful live run.

The transaction uses the public test key for private scalar `1`. It is a
deterministic interoperability fixture, not an account for holding funds.

Run the complete matrix through
[`scripts/ci/run_sdk_conformance.py`](../../scripts/ci/run_sdk_conformance.py).

## Commands

```bash
python3 scripts/ci/run_sdk_conformance.py \
  --mode offline \
  --require-all-languages \
  --output artifacts/sdk-conformance-offline.json
```

Live execution requires an explicit switch, both endpoints, the N4 chain ID,
and an operator-owned deployment fixture. The checked-in example is never
selected automatically:

```bash
export NEO_SDK_LIVE=1
export NEO_N3_RPC_URL='https://n3-node.example'
export NEO_N4_RPC_URL='https://n4-node.example'
export NEO_N4_CHAIN_ID='1099'
export NEO_SDK_LIVE_FIXTURE='/secure/path/neo-sdk-live-fixture.json'

python3 scripts/ci/run_sdk_conformance.py \
  --mode live \
  --require-live \
  --require-all-languages \
  --output artifacts/sdk-conformance-live.json
```

Without that complete environment, local live suites report all discovered
tests as skipped. `--require-live` converts missing configuration into a
non-zero failure. Scheduled runs, version tags, and manual dispatches in
`.github/workflows/sdk-conformance.yml` always use required-live mode and
materialize `NEO_SDK_LIVE_FIXTURE_JSON` from repository secrets.

## Specification alignment

The vectors now pin the canonical `doc.md` §14.1 surface: optional historical
state-root selection, chain-bound proof queries, `(sourceChainId, nonce)` deposit
identity, chain-bound bridged-asset lookup, and the §16.2 `getsecuritylabel`
method. All four SDKs send the same parameters, require decimal-string `u64`
fields, and fail closed on JSON-RPC version/id, chain-id, batch-number, and
deposit-identity mismatches. See
[`docs/audit/p1-1-rpc-sdk-abi.md`](../../docs/audit/p1-1-rpc-sdk-abi.md).
