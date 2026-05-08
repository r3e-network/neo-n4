# @neo-n4/sdk

Typed TypeScript client for the Neo Elastic Network L2 RPC surface
(`doc.md` §14.1). Mirrors the .NET reference SDK in `src/Neo.L2.Sdk/` —
same method names, same 4-way exception taxonomy, same chainId cross-check.

## Install

```bash
npm install
npm run build
npm test
```

## Usage

```ts
import { L2RpcClient } from "@neo-n4/sdk";

const client = new L2RpcClient({
  endpoint: "http://node.example:30332",
  chainId: 1099,
});

// Read methods (10 total, doc.md §14.1):
const root = await client.getLatestStateRoot();
const batch = await client.getBatch(7n);
const label = await client.getSecurityLabel();
const proof = await client.getWithdrawalProof("0x...");
```

## Error handling

Each call throws one of four classes; pick the right `catch` based on whether
the operation is retry-safe:

| Class | Retry? | When |
|-------|:------:|------|
| `L2RpcTransportError` | ✅ | HTTP timeout, connection refused, non-2xx |
| `L2RpcProtocolError` | ❌ | Bad JSON, mismatched response id |
| `L2RpcServerError` | conditional | Server returned `error` field with code |
| `L2RpcMismatchedChainIdError` | ❌ | Server's chainId ≠ client's (config error) |

## Tests

`npm test` runs vitest against an in-process stub fetch — same pattern
as the .NET SDK's `StubHttpHandler` tests.
