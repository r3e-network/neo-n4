<!--
Thank you for contributing to Neo Elastic Network.

Before submitting, please confirm:

- [ ] `dotnet build Neo.L2.sln /p:NuGetAudit=false` passes (0 errors, 0 warnings)
- [ ] `dotnet test Neo.L2.sln /p:NuGetAudit=false` is green locally
- [ ] Affected Rust crates pass `cargo test --release`
- [ ] If you touched any on-chain `*.cs` contract, the matching parity test
      (UT_OnChainMerkleVerifyParity, UT_RestrictedExecutionFraudVerifierParity,
      UT_GovernanceFraudVerifierParity, UT_MpcFraudProof_RealCrypto) still passes
- [ ] If you touched a wire-format encoder/decoder, the cross-language parity
      test (`canonical_bytes_match_csharp_vector` in the Eth watcher) still passes
- [ ] CHANGELOG.md `[Unreleased]` section updated for user-visible changes
- [ ] No new `TODO` / `FIXME` / placeholder strings in production code paths
      (template-output strings in `tools/Neo.Stack.Cli/Commands/ScaffoldExecutorCommand.cs`
      are the documented exception, pinned by `UT_ScaffoldExecutorCommand`)

Security-sensitive changes (smart contracts, cryptographic primitives, bridge
logic, governance flows) require an additional reviewer from the relevant
team — see `.github/CODEOWNERS`.
-->

## Summary

<!-- One or two sentences describing what changes and why. Reference the
related doc.md section, spec-gap-plan item, or issue number when applicable. -->

## Test plan

<!-- A bulleted checklist of how you verified the change locally.
Include the test project names you ran and any non-test verification
(devnet runs, forge tests, etc.). -->

- [ ]
- [ ]
- [ ]

## Risk

<!-- For consensus-affecting changes (contracts, wire formats, cryptographic
primitives), describe rollback strategy and any operator coordination required.
For doc-only or test-only changes, mark as "low — no production impact". -->

## Related

<!-- Links to the doc.md section, spec-gap-plan item, issue, or prior PR
this change builds on. -->
