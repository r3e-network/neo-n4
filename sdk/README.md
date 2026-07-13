# SDKs

Typed app-developer SDKs for the Neo Elastic Network L2 RPC surface
(`doc.md` §14.1). Four language bindings + one zero-build web app.
All wire-compatible with any node running `Neo.Plugins.L2Rpc`.

| Path | Language | Tests |
|------|----------|-------|
| [`../src/Neo.L2.Sdk/`](../src/Neo.L2.Sdk/) | C# / .NET | 25 (`Neo.L2.Sdk.UnitTests`) |
| [`typescript/`](./typescript/) | TypeScript | 16 (vitest) |
| [`rust/`](./rust/) | Rust | 10 (mockito) |
| [`python/`](./python/) | Python | 12 (`unittest`) |
| [`web-explorer/`](./web-explorer/) | static HTML + inlined JS | manual / smoke |

All four typed SDKs follow the same shape:

- Constructor takes `(endpoint, chainId)`. ChainId is cross-checked against
  every response field that includes one — surfaces config errors early
  rather than silently consuming cross-chain data.
- 10 RPC methods matching `doc.md §14.1`: `getl2batch`, `getl2batchstatus`,
  `getl2stateroot`, `getl2withdrawalproof`, `getl2messageproof`,
  `getl1depositstatus`, `getcanonicalasset`, `getbridgedasset`,
  `getsecuritylevel`, `getsecuritylabel`.
- 4-class error taxonomy:
  - `Transport` — HTTP timeout, connection refused, non-2xx (retry-safe)
  - `Protocol` — bad envelope, parse error, mismatched id (don't retry)
  - `Server` — JSON-RPC `error` field with int code (caller decides)
  - `MismatchedChainId` — server's chainId ≠ client's (config error)

The web app vendors the JS SDK inline (zero build tooling) so operators
can drop `web-explorer/index.html` onto any static-file host with no
build step.

## Why four typed languages

dApp developers building on top of an L2 chain use whatever they're
already using. Forcing them onto one language stack costs adoption.
.NET / TS / Rust / Python covers the majority of contemporary dApp and
operator ecosystems; the wire format is fixed (JSON-RPC 2.0) so additional
languages are straightforward operator-supplied wrappers.

## Wallet integration

See [`../docs/wallet-integration.md`](../docs/wallet-integration.md)
for the patterns each SDK + the CLIs follow when actually signing +
broadcasting L1 transactions. None of the SDKs hold private keys; they
either emit canonical paste-into-wallet hex or accept a signing
delegate the operator wires.
