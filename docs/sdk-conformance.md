# Four-language SDK conformance

The Neo N4 SDKs share one executable protocol contract rather than four sets
of handwritten expectations:

| SDK | Production client | Offline suite | Live suite |
|---|---|---|---|
| .NET | `src/Neo.L2.Sdk/` | MSTest category `SdkConformanceOffline` | MSTest category `SdkConformanceLive` |
| Rust | `sdk/rust/` | `conformance_offline` | ignored target `conformance_live` |
| TypeScript | `sdk/typescript/` | Vitest `tests/conformance.offline.test.ts` | Vitest `tests/conformance.live.test.ts` |
| Python | `sdk/python/` | `unittest` `test_conformance_offline.py` | `unittest` `test_conformance_live.py` |

All suites read [`sdk/conformance/vectors/v1.json`](../sdk/conformance/vectors/v1.json).
The CI runner rejects an empty selection, a partial execution, any skipped
offline test, or a configured live lane that does not execute every discovered
test. It writes `neo-n4-sdk-conformance-summary/v1` JSON with per-language
discovered, executed, passed, failed, and skipped counts, plus vector/fixture
SHA-256 digests and the GitHub commit SHA when available.

## Coverage matrix

| Area | Canonical assertion |
|---|---|
| RPC method shape | All ten currently deployed L2 methods and the historical state-root overload use identical names, parameter order, and result semantics. |
| Integer encoding | Every `u64` request value is a decimal JSON string; `u64::MAX` and values above `2^53` exercise lossless response decoding and reject unsafe JSON numbers. |
| Canonical batch | A complete 324-byte commitment is decoded and re-encoded; the live fixture validator rejects an `encoded` value that differs from its structured fields. |
| Hash endianness | `UInt256` wire bytes are little-endian and RPC display is the reversed 32-byte lowercase hex value with `0x`. |
| Error mapping | Server error, response-id mismatch, and non-2.0 envelope map to the common server/protocol taxonomy. |
| Pagination/serialization | Cursor nullability, page order, and identifiers above `2^53` survive a JSON round trip without loss. The current ten-method API is not paginated; this pins the generic envelope contract for future paginated methods. |
| Signature/transaction | A real secp256r1 signature verifies over `networkMagicLE || txHash`; unsigned bytes, txid, invocation script, verification script, and full raw transaction round-trip. |
| Live N3/N4 | Both nodes must satisfy `getversion`, `getblockcount`, and `getblockhash(0)` shapes; N4 additionally satisfies typed security-label/state-root calls and wrong-chain server-error mapping. |

The live suite is read-only. It never broadcasts the transaction fixture or
calls `sendrawtransaction`.

## Local commands

Install the TypeScript and Python test dependencies first:

```bash
npm --prefix sdk/typescript ci --no-audit --no-fund
python3 -m pip install --editable 'sdk/python[test]'
```

Run every offline suite and write the machine-readable report:

```bash
python3 scripts/ci/run_sdk_conformance.py \
  --mode offline \
  --require-all-languages \
  --output artifacts/sdk-conformance-offline.json
```

The .NET project normally resolves the pinned `external/neo` submodule. A
reviewer using an already checked-out r3e Neo fork can set `NEO_CORE_PATH` to
its `src` directory without changing project files.

## Live-node contract

Live execution requires an explicit enable switch, both endpoints, the N4
chain ID, and an operator-owned fixture based on
`sdk/conformance/live-fixture.example.json`:

```bash
export NEO_SDK_LIVE='1'
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

Without the complete environment, every language reports its three live tests
as skipped and the summary status is `skipped`. `--require-live` converts that
state into a failure. When configured, each language verifies the fixture's
network magic, genesis hash, minimum height, all eleven N4 RPC results, and the
typed client/error path. This prevents a release lane from appearing green
after running zero live assertions or from testing the wrong deployment.

## CI and release gate

`.github/workflows/sdk-conformance.yml` always runs the offline job. Its live
job is absent from ordinary pull-request and branch-push execution. Scheduled
runs, `v*` tag runs, and manual dispatches always require
`NEO_N3_RPC_URL`, `NEO_N4_RPC_URL`, and
`NEO_N4_CHAIN_ID` secrets plus `NEO_SDK_LIVE_FIXTURE_JSON`, whose content must
match the example fixture schema. The workflow materializes it as a private
temporary file and requires real execution in all four SDKs; missing secrets
fail rather than producing a green empty job.

Protect releases with both workflow checks and retain both JSON summaries as
release evidence. The runner validates test discovery and execution counts;
the workflow artifact is therefore evidence of work performed, not only a
test-process exit code.

## Specification alignment

The authoritative `doc.md` §14.1, `Neo.Plugins.L2Rpc`, the official RpcServer
registration adapter, and all four SDKs now share the same ten-method ABI. It
uses optional historical state-root selection, chain-bound proof queries,
`(sourceChainId, nonce)` deposit identity, chain-bound bridged-asset lookup,
and the §16.2 `getsecuritylabel` method. The shared vectors fail closed on
parameter order, decimal-string `u64` encoding, JSON-RPC envelope/id, chain,
batch, and deposit-identity drift. The decision matrix and local real-HTTP
evidence are recorded in
[`audit/p1-1-rpc-sdk-abi.md`](./audit/p1-1-rpc-sdk-abi.md).
