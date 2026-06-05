# neo-n4-sdk for Python

Typed Python client for the Neo Elastic Network L2 RPC surface
(`doc.md` §14.1). It mirrors the .NET, TypeScript, and Rust SDKs: same
10 JSON-RPC methods, same four-way error taxonomy, same chain-id
cross-checks.

The runtime uses only Python's standard library so operators can embed it in
deployment, health-check, and smoke-test scripts without carrying a dependency
stack.

## Install

```bash
cd sdk/python
python3 -m pip install .
python3 -m unittest discover -s tests
```

For source-tree development without installation:

```bash
PYTHONPATH=sdk/python python3 -m unittest discover -s sdk/python/tests
```

## Usage

```python
from neo_n4_sdk import L2RpcClient

client = L2RpcClient("http://node.example:30332", 1099)

root = client.get_latest_state_root()
batch = client.get_batch(7)
label = client.get_security_label()
proof = client.get_withdrawal_proof("0x...")
```

## RPC surface

All methods are synchronous and map directly to `doc.md` §14.1:

- `get_batch(batch_number)` → `getl2batch`
- `get_batch_status(batch_number)` → `getl2batchstatus`
- `get_latest_state_root()` → `getl2stateroot`
- `get_state_root_at(batch_number)` → `getl2stateroot`
- `get_withdrawal_proof(leaf)` → `getl2withdrawalproof`
- `get_message_proof(message_hash)` → `getl2messageproof`
- `get_deposit_status(source_chain_id, nonce)` → `getl1depositstatus`
- `get_canonical_asset(l2_asset)` → `getcanonicalasset`
- `get_bridged_asset(l1_asset)` → `getbridgedasset`
- `get_security_level()` / `get_security_label()`

Batch numbers and nonces are encoded as JSON strings to preserve full u64
precision in every runtime and to match the TypeScript SDK's precision-safe
wire format.

## Error handling

Each call raises one of four classes:

| Class | Retry? | When |
|-------|:------:|------|
| `L2RpcTransportError` | yes | HTTP timeout, connection refused, non-2xx |
| `L2RpcProtocolError` | no | Bad JSON, bad envelope, mismatched response id |
| `L2RpcServerError` | conditional | Server returned a JSON-RPC `error` field |
| `L2RpcMismatchedChainIdError` | no | Server returned another chain's data |

Tests inject a callable transport, so production HTTP behavior and in-process
unit tests use the same JSON-RPC envelope validation path.
