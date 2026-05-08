# Neo Elastic Network — Web Explorer

Single-page web app (zero build tooling) that exercises the L2 RPC surface
through an inlined JS SDK. Drop `index.html` onto any static-file host
(GitHub Pages, S3, plain Apache, etc.) — no Node, no bundler, no compile step.

## Tabs

| Tab | What it does |
|-----|--------------|
| **Explore** | `getsecuritylabel`, `getl2stateroot`, `getl2batch` — terminal-equivalent of `neo-l2-explore` |
| **Bridge** | `getl1depositstatus` query + pointer to `neo-bridge` CLI for the actual L1 invocation-script generation |
| **Faucet** | Per-recipient rate-limit UI in `localStorage` (mirrors `FaucetPolicy`'s 1h cooldown / 1 GAS default), plus copy-paste command for the .NET `neo-l2-faucet` CLI |
| **Audit** | State-root continuity check across N batches — same invariant as `neo-l2-explore audit` |

## What it doesn't do

- **Sign or broadcast L1 transactions** — the L1 SharedBridge calls are operator-territory; every L2 framework defers wallet integration (NEP-6, Ledger, NeoLine, custom HSM) to the operator's deployment. The bridge tab points at `neo-bridge deposit` / `neo-bridge withdraw` for the canonical paste-into-wallet hex.
- **Encode L1 invocation scripts** — Neo VM script encoding requires the Neo SDK's ScriptBuilder; the .NET `neo-bridge` CLI handles that path. The web UI surfaces the L2-side queries that run cleanly without wallet integration.

## Architecture

```
[browser]
  ├─ index.html     ← single file, zero dependencies
  ├─ inline JS SDK  ← distilled from sdk/typescript/
  └─ direct fetch() to L2 node's RPC endpoint
```

The inlined SDK uses the same wire format + same 4-class error taxonomy as
`@neo-n4/sdk` (TypeScript) and `Neo.L2.Sdk` (.NET): `L2RpcTransportError`,
`L2RpcProtocolError`, `L2RpcServerError`, `L2RpcMismatchedChainIdError`.

## Local preview

```bash
cd sdk/web-explorer
python3 -m http.server 8080  # any static-file server works
# → http://localhost:8080
```

Point the **Endpoint** field at any L2 node running `Neo.Plugins.L2Rpc`
(e.g. `http://localhost:30332` for a local devnet).
