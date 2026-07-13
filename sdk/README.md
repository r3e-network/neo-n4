# SDKs

Typed app-developer SDKs for the Neo Elastic Network L2 RPC surface
(`doc.md` §14.1). Four language bindings + one zero-build web app.
All wire-compatible with any node running `Neo.Plugins.L2Rpc`.

| Path | Language | Test framework |
|------|----------|----------------|
| [`../src/Neo.L2.Sdk/`](../src/Neo.L2.Sdk/) | C# / .NET | MSTest |
| [`typescript/`](./typescript/) | TypeScript | Vitest |
| [`rust/`](./rust/) | Rust | libtest + mockito |
| [`python/`](./python/) | Python | `unittest` |
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

## Shared conformance

All four typed SDKs consume the same canonical vectors in
[`conformance/vectors/v1.json`](./conformance/vectors/v1.json). The shared
runner covers method shape, lossless integer serialization, hash endianness,
error mapping, cursor/page serialization, and a real signed Neo N3 transaction.
It also provides explicit-skip and required-execution modes for real N3/N4
nodes. See [`conformance/README.md`](./conformance/README.md).

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
