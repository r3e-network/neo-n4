# BatchSerializer mutation evidence — 2026-09-17

This is measured test sensitivity, NOT whole-system formal verification.
Stryker.NET 5.0.0 completed both runs with process exit 0. No mutation-score
threshold was supplied; exit 0 alone is not a quality certification.

| BatchSerializer status | Before | After |
| --- | ---: | ---: |
| Killed | 98 | 120 |
| Survived | 29 | 7 |
| NoCoverage | 4 | 4 |
| CompileError | 2 | 2 |
| Ignored | 6 | 6 |
| Timeout | 0 | 0 |
| Score (killed / killed + survived + no coverage) | 74.81% | 91.60% |

Counts come from the JSON statuses for BatchSerializer.cs only. Stryker generated
mutations throughout the project and filtered other files; their totals must not
be reported as BatchSerializer coverage. Compile errors and ignored mutants do not
count as killed. Nothing was excluded to increase the score between these runs.

## Reproduction

Install `dotnet-stryker` version 5.0.0 into a local tool directory. From
`tests/Neo.L2.Batch.UnitTests`, run that executable with:

```sh
dotnet-stryker --project Neo.L2.Batch.csproj \
  --test-project Neo.L2.Batch.UnitTests.csproj \
  --mutate '**/BatchSerializer.cs' --concurrency 2 \
  --reporter Json --reporter ClearText --skip-version-check \
  --break-on-initial-test-failure --output /absolute/local/output
```

Full source snapshots, test metadata and mutant outcomes are preserved in
`evidence/mutation-before.json` and `evidence/mutation-after.json`.
SHA-256:

- Before: `d30204f82984de717bd8f96425678fbde5f45ba2743083c473b05e14caec0989`
- After: `419ca04bc3aba60e77253c59b0dcee2da4844151c390a8e8defd82fba4f4f92d`

## Four new regressions

`UT_BatchSerializer` now tests oversized proof data with a matching overall buffer
length, so a length-mismatch check cannot mask removal of the cap guard; reversed
PublicInputs block ranges; and every null root field in both encoder inputs.
The Batch suite increased from 132 to 136 passing cases. Production logic was not
changed for this coverage improvement.

## Remaining results

Seven survivors are not marked killed or excluded:

- IDs 74, 112, 121, 181 remove diagnostic message contents; 141 changes only the
  expected-length value inside an error message. Diagnostic text is not fully asserted.
- ID 80 removes checked addition. The preceding 1 MiB cap bounds the sum far below
  int32 maximum. This is an equivalent-mutation assessment for the current code,
  not a blanket equivalence classification for future changes.
- ID 191 changes the final decoder cursor increment after ForcedInclusionCount;
  the cursor is not read afterward. This is likewise assessed as behaviorally equivalent.

Four NoCoverage mutants modify the internal encode-length mismatch throws/messages
at lines 147 and 259. These invariant-failure paths are not reached by existing
fixtures; coverage is not claimed. Two mutants failed compilation and six were
ignored by Stryker's filtering. No attempt was made to change production behavior
or test irrelevant diagnostics solely to obtain a 100% score.
