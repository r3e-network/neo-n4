# Security Policy

Thank you for taking the time to report a security issue. This document describes
how to disclose vulnerabilities in **Neo Elastic Network (`neo4`)** responsibly.

## Scope

In scope:

- Smart contracts in `contracts/` (NeoHub L1 suite, L2 native contracts).
- Foreign-side on-chain code in `external/foreign-contracts/` (Solidity router,
  Solana Anchor program).
- Off-chain `.NET` libraries in `src/Neo.L2.*/` and plugins in `src/Neo.Plugins.L2*/`.
- Rust crates in `bridge/`, `watchers/`, and `sdk/rust/`.
- Tools in `tools/` (CLI surface that signs / submits transactions).
- Cryptographic primitives and wire-format encoders.
- Cross-chain bridge attack surface (replay protection, signature verification,
  asset accounting, message routing).

Out of scope:

- Unmodified upstream behavior inherited from
  [`neo-project/neo`](https://github.com/neo-project/neo) (report there
  directly). N4-specific deltas maintained in the
  [`r3e-network/neo`](https://github.com/r3e-network/neo) fork are in scope.
- Test fixtures (`tests/`, `samples/`) — these are not run in production paths.
- Third-party dependencies. Report upstream first; we'll bump after a fix lands.
- DoS via excessive resource consumption when the operator has disabled
  rate-limiting / fee gating that's available in the framework.
- Behavior of the `ExternalBridgeStubVerifier` (Phase-A devnet contract,
  `bridgeKind == 0` sentinel, documented as devnet-only and explicitly
  excluded from the production deploy bundle).

## Reporting a vulnerability

Please **do not file a public GitHub issue** for vulnerabilities. Instead:

1. Email **security@reborn.com** with the subject line `neo4 security: <short description>`.
2. Include reproduction steps, affected commit hash, and your assessment of impact
   (e.g., funds at risk, censorship, liveness, data integrity).
3. If your finding requires demonstration code, prefer a small standalone reproducer.

You will receive an acknowledgement within **72 hours**. We aim to provide an
initial impact assessment within **7 calendar days** and a coordinated disclosure
timeline within **14 days**.

## Coordinated disclosure timeline

| Phase | Target |
|-------|--------|
| Acknowledgement | ≤ 72 hours |
| Impact assessment | ≤ 7 calendar days |
| Patch availability | ≤ 90 calendar days (sooner for actively-exploitable bugs) |
| Public disclosure | After patch is broadly deployed, or 90 days, whichever first |

If a reported issue is being actively exploited or trivially exploitable with
funds at risk, we move to expedited remediation (target: ≤ 7 days to patch).

## Severity guidance

We classify reports using the following rubric. The category determines the
remediation timeline and the reporter recognition.

**Critical** — direct theft of user funds; forged inbound messages or batch
commitments; bypass of the SharedBridge / ExternalBridge replay protection;
forged signatures accepted by an on-chain verifier; bypass of the optimistic-
challenge slashing flow.

**High** — denial of withdrawals; bypass of forced-inclusion guarantees;
sequencer-committee impersonation; bypass of `GovernanceController` admission
checks; cryptographic primitive misuse in production paths.

**Medium** — incorrect metric emission that misleads operators; race conditions
in non-financial state; persistence-layer corruption recoverable from L1.

**Low** — documentation drift, error-message confusion, minor information leaks
in non-sensitive surfaces.

## Operator security responsibilities

The framework provides the cryptographic primitives, replay protection, and
auth gates. Operators are responsible for:

- **L1 signer integration** — wire `RpcSettlementClient.SignAndSendAsync` to
  KMS / HSM / hot-wallet-with-thresholds. See `docs/wallet-integration.md`.
- **Key management** — `neo-external-bridge genkey` writes private keys 0600
  on POSIX; rotate via committee-replacement governance proposals.
- **Production deployment refusal** — do NOT register
  `NeoHub.ExternalBridgeStubVerifier` with `ExternalBridgeRegistry`; the stub
  exists for devnet acceptance testing only and is excluded from
  `neo-hub-deploy`'s default bundle. Deploy CI should refuse a registration
  whose `bridgeKind == 0`.
- **`SetFee` / `SetFeeRecipient` configuration** — `NeoHub.ForcedInclusion`
  ships with `fee == 0` (legacy fee-free path). Production deployments
  configure a non-zero anti-spam fee.
- **Verifier-upgrade governance** — use `RegisterVerifierViaProposal` (with
  council threshold + timelock) rather than the owner-only `RegisterVerifier`
  for production upgrades.
- **`*WithProof` variants** — `SettlementManager.VerifyWithdrawalLeafWithProof`
  and `EmergencyManager.EscapeHatchExitWithProof` are the canonical multi-leaf
  verification paths. The non-`*WithProof` variants are single-leaf fast paths
  valid only when the tree collapses to a single leaf (root == leaf).
- **Audit before mainnet** — independent third-party audit of contracts and
  off-chain crypto paths is strongly recommended before any deployment that
  custody user funds.

## Bug bounty

A bounty program is not currently active. We do publicly credit reporters in
the CHANGELOG and offer to coordinate disclosure with `cve.org` and the
`r3e-network` GitHub advisory database. Reporters who wish to remain anonymous
will be credited as "Anonymous" or under a handle of their choosing.

## Verifying releases

All release tags are signed. Verify with:

```bash
git verify-tag v0.1.0
```

Public keys for current maintainers live in `docs/security-model.md`.

## Related documentation

- [`docs/security-model.md`](docs/security-model.md) — full threat model and
  trust-boundary catalogue.
- [`docs/architecture-trust-boundaries.md`](docs/architecture-trust-boundaries.md) —
  per-component trust assumptions.
- [`docs/spec-gap-plan.md`](docs/spec-gap-plan.md) — known limitations
  tracked against the canonical spec.
