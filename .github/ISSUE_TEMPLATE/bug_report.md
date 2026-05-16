---
name: Bug report
about: Report a defect in the framework
labels: bug
---

<!-- For security issues, please follow the disclosure process in
     SECURITY.md instead of filing a public issue. -->

## What happened

<!-- Describe the unexpected behavior. Include error messages, stack traces,
or transaction hashes if relevant. -->

## What you expected

<!-- What should have happened instead, and where in the spec / docs that
behavior is documented. -->

## Reproduction

<!-- Minimum steps to reproduce. Include:
- Commit hash (output of `git rev-parse HEAD`)
- Affected component (e.g., `src/Neo.L2.Batch/BatchBuilder.cs`)
- Exact commands run
- Any config that diverges from the documented defaults
-->

```bash
# steps here
```

## Environment

- OS:
- `dotnet --version`:
- `cargo --version` (if a Rust component is involved):
- `git rev-parse HEAD`:

## Impact

<!-- Who is affected (operators, end users, both), and how severe the
breakage is (e.g., "audit pipeline fails on healthy chain", "withdrawal
proofs no longer verify on L1"). -->
