# Security Policy

Thank you for taking the time to report a security issue. This document describes
how to disclose vulnerabilities in **Neo Elastic Network (`neo4`)** responsibly.

## Scope

In scope:

- Smart contracts in `contracts/` (the deployable NeoHub L1 suite) and N4 L2
  native contracts maintained in the r3e Neo core fork.
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

1. Prefer GitHub's enabled
   [private vulnerability reporting form](https://github.com/r3e-network/neo-n4/security/advisories/new).
   If GitHub reporting is unavailable, email the **R3E Network security mailbox**
   at **security@reborn.com** with the subject line `neo4 security: <short description>`.
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

- **L1 signer integration** — implement `INeoTransactionSigner` with a KMS / HSM
  or threshold wallet and use it through `RpcTransactionSender`. The bundled
  `LocalKeyTransactionSigner` is for controlled local/test deployments, not a
  production key-custody design. Production implementations available:
  `AwsKmsTransactionSigner` (AWS Cloud KMS), 
  `AzureKeyVaultTransactionSigner` (Azure Key Vault HSM), 
  `HsmCliTransactionSigner` (external HSM CLI). See 
  [`docs/wallet-integration.md`](docs/wallet-integration.md) for detailed setup.
- **Key management** — `neo-external-bridge genkey` writes private keys 0600
  on POSIX; rotate via committee-replacement governance proposals.
- **Production deployment refusal** — do NOT register
  `NeoHub.ExternalBridgeStubVerifier` with `ExternalBridgeRegistry`; the stub
  exists for devnet acceptance testing only and is excluded from
  `neo-hub-deploy`'s default bundle. Deploy CI should refuse a registration
  whose `bridgeKind == 0`.
- **Forced-inclusion anti-spam configuration** — development deployments may
  remain fee-free, but production `neo-hub-deploy` requires a positive fee,
  GAS token, accountable fee recipient, bond/slash wiring, and a successful
  `ForcedInclusion.IsProductionReady` post-deploy check. Do not bypass that gate.
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

## KMS and HSM security guidelines

When using cloud KMS or hardware security modules, operators must implement:

- **Least-privilege IAM** — KMS signing permissions should be scoped to exact key ARNs. Never use `*` wildcards in KeyId patterns. Separate signing keys from encryption keys and data decryption operations.
- **Key rotation policies** — Configure automatic rotation for AWS KMS (default 1 year) or Azure Key Vault lifecycle management (recommended 90 days). Verify new key material before deprecating old keys via `CheckKeyStatusAsync()`. Use signature cache invalidation (`InvalidateSignatureCache`) after successful migration.
- **Network partition tolerance** — All signers implement fallback strategies for temporary network unavailability:
  - Cached signatures reduce KMS dependency during transient outages
  - Command timeout configuration prevents indefinite blocking
  - Circuit breaker patterns prevent cascading failures
- **Audit trail requirements** — CloudWatch Logs (AWS) or Azure Monitor (Azure) must capture all KMS API calls. Retain logs for minimum 90 days for compliance review.
- **Disaster recovery procedures** — Document key export paths with proper authorization chains: multi-party approval (3-of-5 or 4-of-7 council) for production key exports. Test restoration procedures quarterly with simulated disaster scenarios.
- **Access control boundaries** — Separate dev/test/staging/production environments with distinct KMS instances or key aliases. Never share keys across accounts. Use separate managed identities for each environment.
- **Secret management hygiene** — Never store private keys in plaintext. WIF-based local testing (`LocalKeyTransactionSigner`) is production-prohibited. Rotate environment variable access tokens every 30 days.
- **Hardware security boundary** — HSM CLI integration must maintain memory-safe external processes. Validate JSON input/output with schema validation. Clear sensitive data from process memory space via secure cleanup routines.

For detailed operational procedures, see [`docs/wallet-integration.md`](docs/wallet-integration.md).

## Verifying releases

No production release tag has been published yet. The repository's `0.1.0`
package version is pre-release metadata, not evidence that a `v0.1.0` tag or
binary release exists. Until the first release, deploy only an explicitly
reviewed commit and record the superproject plus every submodule SHA.

The release policy requires signed tags and published maintainer keys. Once a
release exists, verify its actual tag with:

```bash
git tag --verify <published-tag>
```

Do not infer release authenticity from a version string or an unsigned archive.

## Related documentation

- [`docs/security-model.md`](docs/security-model.md) — full threat model and
  trust-boundary catalogue.
- [`docs/architecture-trust-boundaries.md`](docs/architecture-trust-boundaries.md) —
  per-component trust assumptions.
- [`docs/spec-gap-plan.md`](docs/spec-gap-plan.md) — known limitations
  tracked against the canonical spec.
