# SP1 6.2.1 transitive-dependency advisories — 2026-08-28

Two GitHub Advisory Database entries currently flag transitive dependencies of the
pinned SP1 6.2.1 toolchain. Neither has a matching RUSTSEC record as of this date,
so the CI `cargo audit` gate (which scans RUSTSEC) is green against every production
lockfile. Both Dependabot security-update jobs fail on master commits because the
pinned dependency graph cannot express the fix. This note records the assessment and
the accepted remediation path.

## GHSA-vj64-rjf3-w3v7 / CVE-2026-46654 — p3-challenger (high)

- **Advisory**: Plonky3 `MultiField32Challenger` transcript malleability and challenge
  entropy loss — the Fiat-Shamir sponge does not fully bind challenges to the observed
  field-element stream (absorption/squeezing injectivity, absorbed-bit coverage).
- **Locked version**: `p3-challenger 0.3.3-succinct` (SP1's Plonky3 fork line), inside
  `>= 0.4.3` → flagged. Upstream fix: 0.4.3 / 0.5.3.
- **Reachability**: only via the SP1 prover stack
  (`slop-whir → sp1-hypercube → sp1-core-* → sp1-prover → sp1-sdk 6.2.1`), consumed by
  `bridge/neo-zkvm-host` (operator-side prover) and as a build-dependency of the
  gateway host. No on-chain code links these crates.
- **Impact**: weakens the theoretical soundness of SP1 proofs the operator-side prover
  produces; the L1 `NeoHub.Sp1Groth16Verifier` trusts proof-system soundness when it
  verifies against the pinned VK. A malicious operator is the threat actor this would
  empower — the same trust boundary Stage-2 proving already places on the operator.
  Whether SP1's `0.3.3-succinct` fork backports the upstream fix is not publicly
  recorded; the conservative posture treats it as affected.
- **Remediation**: coordinated SP1 toolchain upgrade to a release whose dependency
  graph pins `p3-challenger >= 0.4.3` (or an SP1 line with a backported fix). This is
  a VK re-pin + guest rebuild + `SP1_STATEFUL_NEO_VM_V1` semantic-ID rotation, not a
  lockfile bump. Precedent: Dependabot's sp1 6.3.1 attempt (PR #23) was closed as
  conflicting with 4 failing checks; the 6.2.1 pin is deliberate (see AGENTS.md /
  IMPLEMENTATION_STATUS.md Phase 4). Until that migration lands, this is a documented
  accepted-risk item under the operator-trust model.

## GHSA-qqmc-hwqp-8g2w — lru (high, no patched release on the 0.12 line)

- **Advisory**: use-after-free — holding an iterator over the cache while calling
  `pop()` frees entries the iterator still references. Dependabot additionally
  matched the unpatched `>= 0.9.0, < 0.16.3` range for this pattern class.
- **Locked version**: `lru 0.12.5`, reached only via `sp1-prover 6.2.1 → sp1-sdk`.
- **Impact**: memory-safety bug inside the operator-side prover process; not on-chain,
  not in verification code. Requires the vulnerable iterator+`pop` interleaving;
  upstream has published no fixed 0.12.x release to move to.
- **Remediation**: none available at the pinned toolchain version; resolves with the
  same coordinated SP1 upgrade above.

## Actions taken

1. `dependabot.yml` ignores `lru` + `p3-challenger` (cargo ecosystem) with pointers to
   this note, so security-update jobs stop failing on every master commit while the
   upgrade is coordinated. The ignore is for these two names only; every other
   advisory path stays active.
2. The CI `cargo audit` gate is unchanged: if/when RUSTSEC publishes matching records,
   the gate will fail loudly. At that point either add a justified `--ignore` (the
   RUSTSEC-2026-0258/h2 precedent in `.github/workflows/build.yml`) or complete the
   SP1 upgrade — the failure is the reminder, not something to pre-silence.

## Verification snapshot (2026-08-28, master @ 0a40ee5e)

- `cargo audit --file Cargo.lock --ignore RUSTSEC-2026-0258` → 0 vulnerabilities,
  9 allowed warnings (yanked crates only).
- `cargo tree -i lru` / `cargo tree -i p3-challenger` → reachable only through
  sp1-sdk 6.2.1 paths listed above.
