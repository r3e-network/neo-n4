# Neo N4 Gateway SP1 Guest

This independent SP1 6.2.1 guest proves the Phase-5 Gateway statement from `doc.md` §4.1.

- Input is exactly `NEO4GWP1 || binding170 || countLE32 || commitments`.
- Every commitment is the canonical .NET `BatchSerializer` encoding, strictly ordered by
  `(chainId, batchNumber)`, with `ProofType.Zk` and roots recomputed inside the guest.
- Every deferred child proof uses the compile-time-locked batch guest VK and public values
  `0x00 || batch.PublicInputHash`.
- The only committed output is `0x00 || Hash256(binding170)`.

Standalone host tests require the explicit non-production feature:

```bash
cargo test -p neo-zkvm-gateway-guest --features test-only-vk
```

Production release artifacts must go through `neo-zkvm-gateway-host`. The checked-in
`vk_manifest.rs` pins the SP1 version, batch/Gateway verification keys, and both guest ELF
digests; the host build re-derives and compares every pin. Production builds fail closed when a
pin is missing, zero, or different. The `test-only-vk` feature is the only explicit host-test
bypass.
