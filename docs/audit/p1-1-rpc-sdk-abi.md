# P1-1 RPC / SDK ABI audit

This audit treats `doc.md` §14.1 as authoritative and compares it with the N4
`RpcServer` registration port, `L2RpcMethods`, and all four typed SDKs.

## Canonical matrix

All `u32` values are JSON numbers. Every `u64` request parameter and response
field is a canonical decimal JSON string. Hashes use Neo RPC display order.

| RPC | `doc.md` §14.1 params | RpcServer adapter | Handler result | .NET SDK | TypeScript SDK | Rust SDK | Python SDK |
|---|---|---|---|---|---|---|---|
| `getl2batch` | `chainId, batchNumber` | `GetL2Batch(JArray)` | `null` or batch object; `batchNumber` / `firstBlock` / `lastBlock` are strings | `GetBatchAsync(ulong)` | `getBatch(bigint)` | `get_batch(u64)` | `get_batch(int)` |
| `getl2batchstatus` | `chainId, batchNumber` | `GetL2BatchStatus(JArray)` | object bound to `chainId` and `batchNumber` | `GetBatchStatusAsync(ulong)` | `getBatchStatus(bigint)` | `get_batch_status(u64)` | `get_batch_status(int)` |
| `getl2stateroot` | `chainId, batchNumber?` | `GetL2StateRoot(JArray)` | UInt256 string; omitted batch means latest | `GetLatestStateRootAsync` / `GetStateRootAtAsync` | `getLatestStateRoot` / `getStateRootAt` | `get_latest_state_root` / `get_state_root_at` | `get_latest_state_root` / `get_state_root_at` |
| `getl2withdrawalproof` | `chainId, withdrawalLeafHash` | `GetL2WithdrawalProof(JArray)` | `null` or proof hex | `GetWithdrawalProofAsync` | `getWithdrawalProof` | `get_withdrawal_proof` | `get_withdrawal_proof` |
| `getl2messageproof` | `chainId, messageHash` | `GetL2MessageProof(JArray)` | `null` or proof hex | `GetMessageProofAsync` | `getMessageProof` | `get_message_proof` | `get_message_proof` |
| `getl1depositstatus` | `sourceChainId, nonce` | `GetL1DepositStatus(JArray)` | `null` or object bound to source chain and nonce; u64 fields are strings | `GetDepositStatusAsync` | `getDepositStatus` | `get_deposit_status` | `get_deposit_status` |
| `getcanonicalasset` | `l2Asset` | `GetCanonicalAsset(JArray)` | `null` or UInt160 string | `GetCanonicalAssetAsync` | `getCanonicalAsset` | `get_canonical_asset` | `get_canonical_asset` |
| `getbridgedasset` | `l1Asset, chainId` | `GetBridgedAsset(JArray)` | `null` or UInt160 string; handler rejects foreign chain | `GetBridgedAssetAsync` | `getBridgedAsset` | `get_bridged_asset` | `get_bridged_asset` |
| `getsecuritylevel` | `chainId` | `GetSecurityLevel(JArray)` | chain-bound single-dimension object | `GetSecurityLevelAsync` | `getSecurityLevel` | `get_security_level` | `get_security_level` |
| `getsecuritylabel` | `chainId` | `GetSecurityLabel(JArray)` | chain-bound §16.2 five-dimension object | `GetSecurityLabelAsync` | `getSecurityLabel` | `get_security_label` | `get_security_label` |

## Resolved conflicts

- §14.1 now makes the state-root batch selector optional because both latest and
  historical reads are required and already share one safe handler.
- Proof queries are chain-bound and withdrawal proofs are keyed by the canonical
  `withdrawalLeafHash` consumed by `SharedBridge`, not by a non-unique transaction hash.
- Deposit identity is the implemented `(sourceChainId, nonce)` pair.
- `getbridgedasset` now follows the original spec and carries `chainId` through
  the server and all SDKs.
- `getsecuritylabel` is canonical because §16.2 requires five dimensions;
  `getsecuritylevel` remains for compatibility.
- JSON-RPC responses require exact `jsonrpc: "2.0"` and numeric request-id echo.
  SDKs reject mismatched or string-coerced ids and cross-check chain/request fields.

## RpcServer evidence and core acceptance

The pinned `external/neo` N4 core exposes
`RpcServerPlugin.RegisterMethods(object handler, uint network)`. It registers
handlers both before and after server startup. The local integration test starts a
real `NeoSystem`, the official Kestrel-backed `RpcServerPlugin`, and HTTP-calls all
ten methods plus the historical state-root form. This is local integration evidence,
not public testnet or deployment evidence.

No core-submodule change is required. A future core update is acceptable only if
an equivalent network-scoped registration port remains available and the real-HTTP
test continues to expose every canonical method before and after plugin lifecycle
transitions, while an unwired store still fails closed with method-not-found.
