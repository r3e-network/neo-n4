# Changelog

All notable changes to **Neo Elastic Network** (`neo4`).
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [Unreleased]

### Fixed — `InMemoryL2RpcStore` ctor: L1-sentinel chainId + SecurityLevel range

- `InMemoryL2RpcStore` ctor accepted `chainId = 0` (the L1 sentinel) silently — every subsequent RPC `AssertOurChain` would later fail with a misleading "differs from local 0" comparison. Now uses `ChainIdValidator.ValidateL2`. Same path also accepted `(SecurityLevel)99` silently — would propagate as `levelName = "99"` in RPC responses. Now range-checked. 2 pinning tests.

Cumulative: 489 tests / 27 projects.

### Fixed — RISC-V prover/verifier ctors null-guard `verificationKeyId`

- `Sp1RiscVProver`, `Sp1RiscVVerifier`, `MockRiscVProver`, `MockRiscVVerifier` ctors all accepted a null `UInt256 verificationKeyId` silently. The null would later surface as a iter-159 `RiscVProofPayload.Encode` "VerificationKeyId is null" — naming the payload field but not the actual producer (the prover/verifier ctor that took the bad value). Surface at the source. No pinning tests (4 trivial ctor guards; existing tests still pass with valid VKs).

Cumulative: 487 tests / 27 projects.

### Fixed — `BisectionGame` ctor per-entry null-guards (defense-in-depth for direct callers)

- `ChallengeOrchestrator.InspectWithBisectionAsync` (iter 196) per-entry-null-checks before invoking, but `BisectionGame` is a public constructor that other callers can hit directly. Without this guard, a null entry would NRE inside the `[0].Equals(...)` / `[^1].Equals(...)` checks or `RunRound`'s mid-comparison. 1 pinning test (had to fix the test on first run — `BuildScenario(N, ...)` returns `N+1` arrays, not `N`; off-by-one caught by length-mismatch firing first).

Cumulative: 487 tests / 27 projects.

### Fixed — `ChallengeOrchestrator.InspectWithBisectionAsync` null-guards (parity with `InspectAsync`)

- `InspectAsync` got iter-171 null-guards on `claimedCommitment.PreStateRoot`/`PostStateRoot`/`inputs.PreStateRoot` (UInt256 reference type, `required` doesn't prevent null). `InspectWithBisectionAsync` was missed at that time. Brought to parity. Additionally added per-entry null guards on `challengerCheckpoints[i]` / `sequencerCheckpoints[i]` so a null entry surfaces with the bad index instead of NREing inside `[^1].Equals(...)` or BisectionGame's loop. 1 pinning test.

Cumulative: 486 tests / 27 projects.

### Fixed — `BinaryTreeAggregator.Aggregate` validates `IRoundProver.Combine` return

- A buggy `IRoundProver.Combine` returning null would propagate to the next round's `current[i*2]` as null, causing a confusing NRE deep inside `Combine` on the next round. Now caught at the source with round/slot index. Same iter-171/172/173 callee-contract pattern. 1 pinning test using a `NullReturningRoundProver` test double.

Cumulative: 485 tests / 27 projects.

### Fixed — `MessageTree.GetMessage` / `WithdrawalTree.GetWithdrawal` clearer out-of-range errors

- Both methods previously delegated index-validation to `List<T>`'s indexer, which throws a generic `ArgumentOutOfRangeException` with `"Index was out of range"`. Now both surface a clearer `"index N not in [0, count)"` so an operator chasing a stale or out-of-band index sees the actual valid range. 2 pinning tests covering negative-index and beyond-count cases.

Cumulative: 484 tests / 27 projects.

### Fixed — `ReferenceBatchExecutor.ApplyBatchAsync` per-entry null-guards

- Per-entry null-guards added at three foreach sites: `request.L1MessagesConsumed[i]` (would propagate as a generic NRE inside `_l1Processor.ApplyAsync`), `result.Withdrawals[i]` and `result.Messages[i]` (would surface as `WithdrawalTree.Add` / `L2Outbox.Add` "X is null" without naming the misbehaving executor or the bad index). Now surfaces at the source with the index AND the executor's tx hash. Same iter-181 `BatchSealer.OnBlockCommit` per-entry pattern. 1 pinning test for the L1-message case.

Cumulative: 482 tests / 27 projects.

### Fixed — `L2SettlementPlugin.Wire` revalidates `_settings.ProofType`

- `L2SettlementSettings.From` validates `ProofType` byte range at config-parse time, but `init` setters bypass that path. Same iter-191 ctor-symmetry pattern: revalidate in `Wire` (the latest fail-fast point before the first `OnBatchSealed → SubmitNextAsync`). Without this, an invalid byte would surface only deep inside the broad-catch as a generic `AttestationProver expects ProofType.Multisig, got (ProofType)X` ArgumentException tagged as `("exception", "ArgumentException")`. Now: clear `InvalidDataException` at Wire time. No pinning test (Plugin's `Configure` is protected and `_settings` private — the validation is reachable only through direct internal construction; existing tests still pass with the default `ProofType=1`).

Cumulative: 481 tests / 27 projects.

### Fixed — `BatchSealer` ctor revalidates `L2BatchSettings` positivity

- `L2BatchSettings.From` validates `Max*` positivity at config-parse time, but `init` setters allow direct construction (tests / programmatic wiring) to bypass that path. A caller writing `new L2BatchSettings { MaxBlocksPerBatch = 0 }` would slip past the parser, then `BatchSealer.ShouldSeal` returns true on every block — degenerate per-block batches surface only as a runaway L1 submission rate hours later. Now `BatchSealer` ctor calls `L2BatchSettings.ValidatePositive` for all three `Max*` fields. Same pattern as iter-190's sequencer ctor symmetry. 1 pinning test covers the three Max* misconfigs + default-settings boundary.

Cumulative: 481 tests / 27 projects.

### Fixed — `InMemorySequencerCommitteeProvider` ctor validates `maxCommitteeSize`

- `SetMaxCommitteeSize` validates the range `[1..64]`; the constructor did not. Operator-supplied `0` silently accepted every `Register` call as "committee full", `-1` same, and `> 64` exceeded the dBFT 2.0 practical bound. Now symmetric: surface the misconfig at construction time, not at first `Register`. 1 pinning test covers `0` / `-1` / `65` reject and `1` / `64` boundary accept.

Cumulative: 480 tests / 27 projects.

### Fixed — `RpcSettlementClient.SubmitBatchAsync` validates `SignAndSendAsync` return

- A buggy `SignAndSendAsync` delegate returning null `UInt256` would propagate as a NRE further downstream — typically an L1-tracker dereferencing the tx hash. Same iter-171/172/173 callee-contract pattern: surface the bad return as `InvalidOperationException` naming the delegate. Made `SubmitBatchAsync` async to enable awaiting and validating the result. 1 pinning test using a delegate that returns `(UInt256)null!`.

Cumulative: 479 tests / 27 projects.

### Fixed — `NeoFsLikeDAWriter.TryGet` defensive copy

- Same iter-176 defensive-copy pattern as `KeyedStateStore.EnumerateSorted`. Previously returned the raw stored `byte[]` wrapped in `ReadOnlyMemory<byte>?`; a debug consumer that mutated the returned bytes would silently corrupt the store. Now returns a per-call `Clone()`. 1 pinning test mutates the first read and asserts the second read sees the original bytes.

Cumulative: 478 tests / 27 projects.

### Fixed — `BatchSerializer.Encode` rejects out-of-range `ProofType` (Encode/Decode symmetry)

- `Decode` rejects `ProofType` bytes outside `[0..Zk]` (iter 103). `Encode` did not — an out-of-range cast (e.g. `(ProofType)99`) produced bytes the round-trip `Decode` would refuse, masking the producer-side bug at the consumer. Same iter-159 Encode/Decode-symmetry pattern as `OptimisticProofPayload.MaxSignatureBytes` and `RiscVProofPayload.MaxProofBytes`. 1 pinning test asserts the throw + index in message.

Cumulative: 477 tests / 27 projects.

### Fixed — `MetricsRequestHandler.HandleMetrics` returns 500 on snapshot/format failure

- Previously a buggy `IMetricsSource` that returned null or threw from `Snapshot()` (or a downstream `PrometheusExporter.Format` regression) would surface as a closed connection to the scraper — no diagnostic, no alertable HTTP status. Wrapped the snapshot/format pipeline in `try/catch` so the failure becomes a `500` with a generic body, which flips most Prometheus servers into an "exporter down" alert state. Generic-on-purpose body; operators chase the actual exception in logs. 2 pinning tests using `NullSnapshotSource` and `ThrowingSnapshotSource`.

Cumulative: 476 tests / 27 projects.

### Fixed — `MessageTree` + DA writers null-key sweep

- Closed the iter-148/183/184 Dictionary-key null-guard pattern in 4 more entry points: `MessageTree.Add` (now guards `message.MessageHash`; without this `_byHash[null]` throws with generic message and `_leaves` accumulates a null that iter-179's `ComputeRoot` would catch only later), `MessageTree.TryGetIndex` (UInt256 key), `InMemoryDAWriter.IsAvailableAsync` (`receipt.Commitment`), and `NeoFsLikeDAWriter.IsAvailableAsync` + `TryGet`. 2 pinning tests for the `MessageTree` cases.

Cumulative: 474 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore` null-key sweep (6 entry points)

- Closed the iter-148/183 Dictionary-key null-guard pattern in 6 more entry points: `RecordWithdrawalProof`/`RecordMessageProof` (setters add explicit guards on `leafHash`/`messageHash` keys; the byte-payload guards already existed), and `GetWithdrawalProof`/`GetMessageProof`/`GetCanonicalAsset`/`GetBridgedAsset` (getters now null-guard their `UInt256`/`UInt160` keys instead of relying on `Dictionary<,>.TryGetValue(null)`'s generic message). 1 pinning test asserts all 6 entry points reject null with a clear `ArgumentNullException`.

Cumulative: 472 tests / 27 projects.

### Fixed — `AssetRegistry` lookup/setter null-guard sweep

- `TryGetByL1`, `TryGetByL2`, and `SetActive` previously delegated null-key handling to `Dictionary<UInt160, T>` whose `TryGetValue(null)` throws `ArgumentNullException` with a generic `"key"` message. Surface the bad arg at the API boundary so the operator sees which parameter is wrong. Same iter-148 pattern as `Register`. 3 pinning tests.

Cumulative: 471 tests / 27 projects.

### Fixed — `JsonRpcClient.CallAsync` rejects mismatched response id

- JSON-RPC 2.0 §5 mandates the response's `id` field match the request's. A buggy server, misconfigured proxy, or confused upstream that interleaves streams could silently send a response correlating to a different request — previously accepted as if it answered ours. Although we rely on HTTP one-request-per-connection correlation here (so a mismatch can't actually misroute a response on the happy path), the missing check masked server bugs that an operator would otherwise want to catch. Now throws `JsonRpcException(-32603, "response id N does not match request id M")`. 1 pinning test using a `StubHandler` that returns `id=999` for a request sent with `id=1`.

Cumulative: 468 tests / 27 projects.

### Fixed — `BatchSealer.OnBlockCommit` silently treated null tx as empty (`byte[]→ReadOnlyMemory<byte>`)

- The implicit `byte[]` → `ReadOnlyMemory<byte>` conversion in C# accepts null and produces an empty `ReadOnlyMemory` rather than throwing. So a null entry in the `IEnumerable<byte[]> rawTransactions` argument was silently folded into the batch's tx tree as an empty leaf — a deterministic-replay nightmare because the commitment wouldn't match what re-execution produces, and a sequencer's batch could quietly diverge from any honest replay. Now caught at the foreach with the bad index named. 1 pinning test.

Cumulative: 467 tests / 27 projects.

### Fixed — `PassThroughRoundProver.Combine` null-guard on `MessageRootContribution`

- The hashing primitive at `Combine` dereferenced `left.MessageRootContribution.GetSpan()` and `right.MessageRootContribution.GetSpan()` without null-checking the `UInt256` references. Same iter-156 hashing-primitive defense pattern. 1 pinning test that asserts both `left`-bad and `right`-bad cases throw.

Cumulative: 466 tests / 27 projects.

### Fixed — `MerkleTree` constructor + `ComputeRoot` null-leaf-entry guard

- Both the constructor and the static `ComputeRoot` accepted `IReadOnlyList<UInt256>` but didn't per-entry null-check. A null leaf would NRE deep in `CombineHash`'s `GetSpan()` (or, for `ComputeRoot` with a single null leaf, return null `UInt256` to the caller). Added the same iter-158/168 per-entry index-naming guard as `MerkleProofSerializer.Encode` and `MerkleTree.Verify`. 2 pinning tests.

Cumulative: 465 tests / 27 projects.

### Fixed — `L2BridgePlugin.DepositProcessor`/`WithdrawalProcessor` accessor null-handling

- The two accessors used the `!` null-forgiving operator (`_depositProcessor!`), so a caller who accessed them before `Configure()` had run got the underlying `null` and NRE'd on the next field access. Replaced with `?? throw new InvalidOperationException("... accessed before Configure() — wire the L2BridgePlugin into the host first")` so the cause is named at the source. No pinning test (would require adding a new `Neo.Plugins.L2Bridge.UnitTests` project; the change is small and the throw message is self-explanatory).

Cumulative: 463 tests / 27 projects.

### Fixed — `L2SettlementPlugin`: fail-fast on empty `proofResult.Proof`

- A prover that returned empty `Proof` bytes paired with a non-None `ProofType` would otherwise produce a soft-sealed commitment that `NoZeroProofCheck` catches hours later at audit time, with no link back to the prover bug. Now caught at the prove boundary in `SubmitNextAsync` — same iter-128/140-style "fail close to the contract" pattern as the Kind/PublicInputHash mismatch checks. The exception flows through the iter-175 catch tagging so dashboards see `SubmitFailures{exception=InvalidOperationException}`. 1 pinning test with `EmptyProofProver` test double.

Cumulative: 463 tests / 27 projects.

### Fixed — `KeyedStateStore.EnumerateSorted` defensive copy

- The "test/debug helper" `EnumerateSorted` yielded the raw `byte[]` references stored in the `SortedDictionary`, so a debug consumer that mutated the yielded keys/values would silently corrupt the store's internal state. `Put` already copies (iter-167 pattern) and `Get` returns immutable `ReadOnlyMemory`, but this iterator was the lone hole. Now each yielded entry is a fresh `Clone()` — caller mutations are isolated. Pinning test mutates the yielded buffers and asserts the stored entry is unchanged.
- Spent earlier in this iteration trying to broaden the iter-175 exception-tagging pattern across 5 more failure-counter sites (`WithdrawalsRejected`, `DepositsRejected`, `RpcFailures`, `DAPublishFailures`, `BatchSealedSubscriberFailures`) — broke 12 tests that asserted untagged metrics. Reverted; the per-site choice was deliberate, broadening it requires a coordinated test update done in a focused refactor PR rather than a defensive-sweep iter.

Cumulative: 462 tests / 27 projects.

### Changed — `L2SettlementPlugin`: prover-contract assertion + exception-tagged failure metric

- `L2SettlementPlugin.SubmitNextAsync` now asserts the prover's contract: `IL2Prover.ProveAsync` returning null surfaces as `InvalidOperationException`, and `proofResult.PublicInputHash` is null-guarded (was previously dereferenced on the next `.Equals(hash)`). Same iter-171/172/173/174 callee-contract pattern.
- The broad `catch (Exception)` block now tags `MetricNames.SubmitFailures` with `("exception", typeName)` so an operator's dashboard can separate contract violations (`InvalidOperationException`) from network failures (`HttpRequestException`) from L1-side rejections. Previously the counter was a single number that hid the failure mode.
- 1 new pinning test (`Submit_BuggyProverReturnsNull_TaggedAsContractViolation`) + 2 existing tests updated to assert the new tag.

Cumulative: 461 tests / 27 projects.

### Fixed — `AttestationProver` callee-contract assertion on `ISignerSet.SignAsync`

- A buggy `ISignerSet` returning null from `SignAsync` would propagate as a NRE inside `MultisigProofPayload.Encode`'s iter-159 null-guard (which would name `Signatures` rather than the actual root cause). Now surfaced at the prover boundary as `InvalidOperationException` naming `SignAsync`. 1 pinning test with `NullReturningSigners` test double.

Cumulative: 460 tests / 27 projects.

### Fixed — Two more callee-contract assertions: `MetricsEmittingDAWriter` + `ReferenceBatchExecutor`

- Continued the iter-171/172 sweep. (1) `MetricsEmittingDAWriter.PublishAsync` now asserts the inner writer's contract: a null `DAReceipt` return surfaces as `InvalidOperationException` naming `PublishAsync` (with the mode tag) and bumps the failure metric. Previously a downstream consumer would NRE on `receipt.Commitment`. (2) `ReferenceBatchExecutor.ApplyBatchAsync` now asserts `ITransactionExecutor.ExecuteAsync` returns non-null AND that `result.Receipt`/`result.TxHash` are non-null (would NRE inside `result.Receipt.Hash()` or `MerkleTree.ComputeRoot(txHashes)` containing nulls).
- 2 pinning tests: `Publish_BuggyInnerReturnsNull_SurfacesContractViolation` (asserts the failure metric still ticks but the success metric does not) and `ApplyBatchAsync_BuggyTxExecutorReturnsNull_SurfacesContractViolation`.

Cumulative: 459 tests / 27 projects.

### Fixed — Callee-returns-null contract assertions in `CensorshipDetector` + `ProofValidityCheck`

- Extended the iter-171 callee-contract pattern. (1) `CensorshipDetector.DetectOverdueAsync` now asserts the buggy-source contract: a null return from `IForcedInclusionSource.DrainAsync` or `ISequencerCommitteeProvider.GetActiveCommitteeAsync` surfaces as `InvalidOperationException` naming the contract method, instead of NREing inside the foreach / `.Count` access. (2) `ProofValidityCheck.RunAsync` now asserts `_publicInputsResolver(batch) != null` (was previously dereferenced on the next line); the existing `ProofVerificationResult` is `record struct` so it can't be null and doesn't need a guard.
- 2 pinning tests with `BuggySource` and `BuggyCommittee` test doubles in `UT_CensorshipDetector.cs`.

Cumulative: 457 tests / 27 projects.

### Fixed — `ChallengeOrchestrator.InspectAsync` null-guards + replayer-contract assertion

- Three new defensive guards in `ChallengeOrchestrator.InspectAsync`. (1) `claimedCommitment.PreStateRoot`/`PostStateRoot` and `inputs.PreStateRoot` are `UInt256` reference types — `required` doesn't force non-null. The chain-id/batch-number/pre-state validation that follows would NRE on `.Equals(...)`. Same iter-156 hashing-primitive defense pattern. (2) After `_replayer.ReplayAsync(...)`, the `replayedRoot` is now checked for null — a buggy `IFraudProofGenerator` returning null would otherwise NRE inside `replayedRoot.Equals(...)` with no link to the replayer's contract violation; surfaced as `InvalidOperationException` naming `ReplayAsync`.
- Two pinning tests in `UT_Challenge.cs`: `Inspect_RejectsBuggyReplayerReturningNull` (asserts `ReplayAsync` appears in the message) and `Inspect_RejectsNullPreStateRootInCommitment`.

Cumulative: 455 tests / 27 projects.

### Fixed — `L2BatchPlugin.OnBatchSealed` subscriber-failure isolation

- A throwing `OnBatchSealed` subscriber would propagate its exception back to Neo's `Blockchain.Committed` via standard .NET event dispatch (first-throw aborts further dispatch + exception rethrows to event source), making a buggy downstream listener (e.g. `L2SettlementPlugin`) potentially destabilize block import. Refactored to iterate `GetInvocationList()` and try/catch each subscriber individually; failures bump the new `MetricNames.BatchSealedSubscriberFailures` counter (+catalog entry).
- Extracted the dispatch into an `internal static DispatchSealed` so it can be unit-tested without spinning up a `NeoSystem`. Added `InternalsVisibleTo` for the test project. New `UT_L2BatchPlugin.cs` with 3 pinning tests: one-throws-others-still-fire, no-subscribers, and multi-throw-counter.

Cumulative: 453 tests / 27 projects.

### Fixed — Two more null-guard surfaces: `DerivedPostStateRootOracle` + `ChainAuditor` per-batch

- `DerivedPostStateRootOracle.ResolveAsync` now null-guards `preStateRoot`, `receiptRoot`, and `blockContext` at the API boundary. Without these, a null `UInt256` would NRE inside `GetSpan()` with no link to the caller.
- `ChainAuditor.AuditAsync` now per-entry null-checks `batches[i]` BEFORE any field access (the chainId/sort scan touches `.ChainId`/`.BatchNumber`). Without this, a null entry would surface as a confusing NRE deep inside an audit check (e.g. `ContinuityCheck`'s `cur.PreStateRoot.Equals(...)`); the audit-level message names the bad index so the operator sees which batch is missing.
- 4 pinning tests: 3 for the oracle's reject-null-input cases, 1 for the auditor's null-batch-entry case.

Cumulative: 450 tests / 27 projects.

### Fixed — `MerkleTree.Verify` null-guards on proof.Leaf and Siblings entries

- `MerkleTree.Verify` only null-checked the proof object itself; `proof.Leaf` (UInt256, reference type) and individual `proof.Siblings[d]` entries could still be null and would NRE inside `CombineHash`'s `GetSpan()` with no link to the bad caller. Same iter-158 pattern from `MerkleProofSerializer.Encode`. Now: top-level `ArgumentNullException.ThrowIfNull` on `proof.Leaf` and `proof.Siblings`, and the per-sibling check fires inside the loop with `Siblings[i]` index in the message.
- 2 pinning tests in `UT_MerkleTree.cs`: `Verify_RejectsNullLeaf` and `Verify_RejectsNullSiblingEntry` (asserts the bad index appears in the exception).

Cumulative: 446 tests / 27 projects.

### Fixed — `InMemoryMessageRouter`: null-hash NRE + caller-mutation corruption

- `RecordFinalized(messageHash, proofBytes)` and `GetMessageProofAsync(messageHash, ...)` didn't null-guard `messageHash`. UInt256 is a reference type — null would NRE inside `ConcurrentDictionary`'s hash lookup with no link to the bad caller.
- `RecordFinalized` didn't make a defensive copy of `proofBytes`. `ReadOnlyMemory<byte>` provides immutability for the *view*, but the underlying array can still be mutated through other references. A caller who reused a scratch buffer or mutated their array after passing it in would silently corrupt the stored proof. Now does `proofBytes.ToArray()`, mirroring `InMemoryL2RpcStore.RecordWithdrawalProof`.
- 3 pinning tests in `UT_Messaging.cs`: 2 null-guard tests + `Router_RecordFinalized_DefensiveCopyProtectsAgainstCallerMutation` that mutates the source bytes after calling and asserts the stored proof is unchanged.

Cumulative: 444 tests / 27 projects.

### Fixed — `BatchBuilder.ToCommitment` null-guards before sealing

- `ToCommitment` would NRE on the first `executionResult.PostStateRoot` access if `executionResult` was null. Worse, `daCommitment` and `publicInputHash` were `UInt256` (reference type) — null in either would slip through here, get assembled into the commitment, and be caught only later in `BatchSerializer.Encode`'s iter-156/157 null-guards — but by then `_batch.Seal()` had already mutated state irreversibly, so the operator couldn't simply retry. Now all three are guarded BEFORE `_batch.Seal()` runs.
- 3 pinning tests in `UT_BatchBuilder.cs` assert the throw AND that `b.Batch.IsSealed` is false after the failed call (proves the guard fires before sealing so a retry can succeed).

Cumulative: 441 tests / 27 projects.

### Added — Pin iter-164 worst-case + sweep last 2 metric sites

- New regression test `Submit_ThrowingMetrics_DoesNotReQueueAlreadySubmittedBatch` in `UT_L2SettlementPlugin_Metrics.cs` uses a `ThrowingMetrics` test double to assert that a metrics-sink failure after `SubmitBatchAsync` returns does NOT re-queue the batch (which would loop indefinitely against L1's duplicate-rejection). Verifies `client.SubmitCount == 1` and `settlement.PendingCount == 0`.
- Swept the final two metric call sites: `L2RpcMethods.Time` (would surface a successful RPC body as an `RpcFailures` to the caller, prompting a retry) and `CensorshipDetector` (less severe — pure compute path, but consistent with the rest). Metric-sink-isolation sweep is now complete across the L2 stack.

Cumulative: 438 tests / 27 projects.

### Fixed — Metric-induced re-submission of already-on-L1 batches + sweep across 5 more sites

- Worst-case bug fixed: in `L2SettlementPlugin.SubmitNextAsync`, a metrics-sink throw between the success of `SubmitBatchAsync` (line 175) and either of the post-submit metric calls (177/178) was caught by the broad `catch (Exception)` block, which re-queues the batch. The L1 contract would reject the duplicate commitment, the plugin would treat the rejection as another submit failure, and the batch would loop indefinitely — paying L1 gas every retry.
- Same pattern swept across 4 more sites: `BatchSealer.OnBlockCommit` (would leave `_builder` non-null pointing at the just-sealed builder so the next call adds blocks to a sealed batch), `MetricsEmittingDAWriter.PublishAsync` (would re-publish an already-on-DA blob), `BinaryTreeAggregator`, `ChainAuditor`, `ChallengeOrchestrator`. All converted to `SafeIncrementCounter`/`SafeRecordHistogram`/`SafeSetGauge`.

Cumulative: 437 tests / 27 projects.

### Added — `MetricsExtensions.SafeIncrementCounter`/`SafeSetGauge`/`SafeRecordHistogram` + sweep

- New `Neo.L2.Telemetry.MetricsExtensions` (3 helpers) wraps `IL2Metrics` calls in `try/catch` swallows so a defective sink can never affect business logic. Refactored 4 state-mutating sites to use it: `InMemorySequencerCommitteeProvider.Register`/`BeginExit`/`Finalize`, `InMemoryForcedInclusionSource.Enqueue`, `L2Outbox.Add`. Each was previously vulnerable to the iter-162 defect: state committed under the lock, then a metric throw outside the lock would surface as a caller-visible "operation failed" while the state had already mutated. Added `WithdrawalProcessor_Stage_SurvivesThrowingMetricsSink` pinning test using a `ThrowingMetrics` test double that throws on every call.

Cumulative: 437 tests / 27 projects.

### Fixed — Metrics-sink exception corrupts `WithdrawalProcessor`/`DepositProcessor` state

- Both processors had the same defect: a defective `IL2Metrics` implementation that throws would leave business state committed (`_byNonce` / `_tree` / `_consumed`) while the caller saw an exception and assumed the operation failed. Worse, the broad `catch { _metrics.IncrementCounter(*Rejected); throw; }` block would then fire too — double-counting. The interface contract doesn't promise `IL2Metrics.IncrementCounter` is non-throwing (any HTTP-pushing implementation absolutely could throw), so business code can't trust it. Fix: success counter is now outside the lock and outside the try block, both metric calls are individually try/catch-swallowed, and the rejection counter no longer competes with the success path.

Cumulative: 436 tests / 27 projects.

### Added — Pinning tests for `OptimisticProofPayload.Encode`/`RiscVProofPayload.Encode` size caps

- Two new tests in `UT_OptimisticAndRiscV.cs` lock down the iter-159 Encode-side cap rejection: `OptimisticProofPayload_Encode_RejectsOversizedSig` (`MaxSignatureBytes + 1`) and `RiscVProofPayload_Encode_RejectsOversizedProof` (`MaxProofBytes + 1`). Both assert the exception message names the cap constant so a future refactor that drops the validation is caught here, not at the next consumer.

Cumulative: 436 tests / 27 projects.

### Added — Pinning tests for `MultisigProofPayload.Encode` validation

- Three new tests in `UT_Attestation.cs` lock down the iter-159 Encode-side checks: `Encode_RejectsBadSignatureLength` (boundary 63 / 65 vs. the silent-zero-pad at length < 64), `Encode_RejectsNullSignerEntry` (asserts the null index appears in the exception message), `Encode_RejectsOversizedSignerCount` (Encode/Decode symmetry pin). Without these, future refactors could regress the silent-padding bug undetected.

Cumulative: 434 tests / 27 projects.

### Changed — Proof/deposit payload encoders: null-guards + Encode/Decode-symmetry checks

- Swept the four remaining payload encoders. Added null-guards on the reference-type `UInt160`/`UInt256` fields (`OptimisticProofPayload`, `RiscVProofPayload`, `MultisigProofPayload`, `DepositPayload`). Added Encode-side cap checks where the matching `Decode` already rejects oversized inputs (`MaxSignatureBytes`, `MaxProofBytes`, `MaxSigners`) — without these you could `Encode` bytes the round-trip `Decode` would refuse, masking the producer-side bug at the next consumer. `MultisigProofPayload.Encode` additionally validates each signer's `Signature.Length == 64` (a shorter source silently zero-pads via `Span.CopyTo`, producing a structurally-valid but semantically-wrong encoding).
- This **does** finish the encoder sweep — all eight `Encode` methods (3 in `BatchSerializer`/`StateRootCalculator`, 1 in `MessageHasher`, 1 in `Receipt`, 1 in `MerkleProofSerializer`, 1 in `FraudProofPayload`, 4 in payload encoders) now uniformly null-guard their reference-type fields.

Cumulative: 431 tests / 27 projects.

### Changed — `FraudProofPayload.Encode` and `MerkleProofSerializer.Encode` defense-in-depth null-guards

- Extended the iter-154…157 null-guard pattern to the remaining encoders. `FraudProofPayload.Encode` now guards its 3 `UInt256` roots; `MerkleProofSerializer.Encode` guards `Leaf`, the `Siblings` collection, and each sibling entry inside the loop (`Siblings[i]` could be a null reference even with the collection itself non-null). The previous claim that iter-157 "finished" the sweep was premature — these were missed.

Cumulative: 431 tests / 27 projects.

### Changed — `BatchSerializer.Encode`/`EncodePublicInputs` defense-in-depth null-guards

- Continued the iter-154/155/156 hashing-primitive null-guard pattern through the wire serializer. `BatchSerializer.Encode` now null-checks all 9 commitment `UInt256` fields before reaching `WriteUInt256`'s `GetSpan()`. `EncodePublicInputs` mirrors `StateRootCalculator.HashPublicInputs` with all 10 root fields. This finishes the cryptographic-primitive null-guard sweep across the codebase.

Cumulative: 431 tests / 27 projects.

### Changed — `StateRootCalculator` defense-in-depth null-guards

- Continued the iter-154/155 hashing-primitive null-guard pattern through `StateRootCalculator.HashBlockContext` (one `UInt256`) and `StateRootCalculator.HashPublicInputs` (ten `UInt256` fields). Each null-checked before reaching the `WriteRoot` / `GetSpan()` boundary.

Cumulative: 431 tests / 27 projects.

### Changed — `Receipt.Hash` defense-in-depth null-guards

- Same pattern as iter 154's `MessageHasher` fix, applied to `Receipt.Hash()`. The three `UInt256` fields (`TxHash`, `StorageDeltaHash`, `EventsHash`) are reference types; `required` only forces "must be set," not "non-null." A null field would crash inside `GetSpan()` with no link back to the bad caller. Now `ArgumentNullException.ThrowIfNull` each before hitting the buffer copy.

Cumulative: 431 tests / 27 projects.

### Changed — `MessageHasher` defense-in-depth null-guards

- The iter-146/147 fixes added null-guards at the API boundaries (`MessageBuilder.Build`, `WithdrawalProcessor.Stage`). Added the same guards at the cryptographic-primitive boundary too — `MessageHasher.HashMessage` / `HashWithdrawal` — covers any direct caller (tests, future helpers) that bypasses the higher-level boundaries. Defense in depth.
- Both methods now `ArgumentNullException.ThrowIfNull` each `UInt160` field they read (Sender/Receiver for HashMessage; EmittingContract/L2Sender/L1Recipient/L2Asset for HashWithdrawal) before hitting `GetSpan()`.

Cumulative: 431 tests / 27 projects.

### Tests — Regression coverage for iter-148 `Register` null-guards

- Pinned the iter-148 null-guards on `AssetRegistry.Register` and `InMemorySequencerCommitteeProvider.Register` with regression tests so a future refactor can't silently drop them.
- **3 new tests**:
  - `AssetRegistry_Register_RejectsNullL1Asset`
  - `AssetRegistry_Register_RejectsNullL2Asset`
  - `Sequencer.Register_RejectsNullL1Address`

Cumulative: 431 tests / 27 projects.

### Tests — Regression coverage for iter-147 `WithdrawalProcessor.Stage` null-guards

- The iter-147 fix added `ArgumentNullException.ThrowIfNull` for the four `UInt160` fields on `WithdrawalRequest`. Pinned with regression tests so a future refactor can't silently drop them.
- **2 new tests**: `WithdrawalProcessor_Stage_RejectsNullL2Sender` and `WithdrawalProcessor_Stage_RejectsNullL1Recipient` — both pass `null!` for the respective field and assert `ArgumentNullException`.

Cumulative: 428 tests / 27 projects.

### Tests — Regression coverage for iter-146 `MessageBuilder.Build` null-guards

- The iter-146 fix added `ArgumentNullException.ThrowIfNull` to `sender` / `receiver`. This iter pins the contract with two regression tests so a future "trust the caller" refactor can't silently drop the guards.
- **2 new tests**: `MessageBuilder_RejectsNullSender` and `MessageBuilder_RejectsNullReceiver` — both pass `null!` for the respective field and assert `ArgumentNullException`.

Cumulative: 426 tests / 27 projects.

### Changed — `BatchBuilder` reject-null guards on all reference-type append methods

- Continuation of the iter-146–149 null-guard sweep, applied to the batch-builder API surface.
- `ConsumeL1Message(CrossChainMessage)`, `AddWithdrawal(WithdrawalRequest)`, `AddL2ToL1Message(CrossChainMessage)`, `AddL2ToL2Message(CrossChainMessage)`: all four now `ArgumentNullException.ThrowIfNull` their reference-type input. Previously a null arg silently added a null entry to the underlying `L2Batch._{l1Messages,withdrawals,l2ToL1,l2ToL2}` list, which would then crash hours later inside the per-batch hashing pass with no link back to the bad caller.

Cumulative: 424 tests / 27 projects.

### Changed — `InMemoryL2RpcStore.AddBatch` + `RegisterAsset` reject null inputs

- Continuation of the iter-146/147/148 null-guard sweep.
- `AddBatch(L2BatchCommitment commitment, ...)`: a null commitment crashes deep in `commitment.BatchNumber` access.
- `RegisterAsset(UInt160 l1Asset, UInt160 l2Asset)`: a null UInt160 throws from `ConcurrentDictionary.TryGetValue(null)` deep in the hash path.
- Both call sites now `ArgumentNullException.ThrowIfNull` up front.
- (Note: `RecordDeposit(DepositStatus)` doesn't need a guard — `DepositStatus` is a `record struct` (value type), so null is impossible at the type level.)

Cumulative: 424 tests / 27 projects.

### Changed — `AssetRegistry.Register` + `Sequencer.Register` reject null `UInt160` fields

- Continuation of the iter-146/147 null-guard sweep.
- `AssetRegistry.Register`: a null `mapping.L1Asset` creates the tuple key `(null, chainId)` (Dictionary tolerates null inside a tuple) — lookups would interpret it oddly. A null `mapping.L2Asset` throws deep in `_byL2[null]`.
- `InMemorySequencerCommitteeProvider.Register`: a null `l1Address` propagates into `CommitteeMember.L1Address` → `CensorshipReport.ResponsibleSequencerAddress` → either misroutes the slash payout or hard-reverts the slash transaction at L1.
- Both call sites now `ArgumentNullException.ThrowIfNull` the relevant fields up front.

Cumulative: 424 tests / 27 projects.

### Changed — `WithdrawalProcessor.Stage` rejects null `UInt160` fields

- Same defensive shape as the iter-146 `MessageBuilder.Build` fix, applied to the withdrawal-staging boundary. `WithdrawalRequest`'s `EmittingContract` / `L2Sender` / `L1Recipient` / `L2Asset` are `required UInt160` (reference type) — the `required` keyword forces them to be set but doesn't prevent null. A null field would crash deep in `MessageHasher.HashWithdrawal`'s `GetSpan()` with no link back to the bad field.
- Catch all four at the API boundary with `ArgumentNullException.ThrowIfNull`.

Cumulative: 424 tests / 27 projects.

### Changed — `MessageBuilder.Build` rejects null `sender` / `receiver`

- `UInt160` is a reference type in Neo's library, so a null `sender` or `receiver` slipped past the C# nullable analysis (the `required` keyword on `CrossChainMessage` just means "must be set," not "non-null"). The null then crashed inside `MessageHasher.HashMessage`'s `GetSpan()` with a `NullReferenceException` and no link back to the bad argument.
- Added `ArgumentNullException.ThrowIfNull(sender)` + same for `receiver` at the API boundary so the operator sees which arg was wrong.

Cumulative: 424 tests / 27 projects.

### Changed — `L2BatchPlugin.Configure` no longer resets `_sealer` (preserves batch-numbering state)

- Companion to iter-144's bridge fix. `Configure` explicitly set `_sealer = null` after parsing settings — so a re-fired Configure (config-watcher, host re-init) would discard `BatchSealer._nextBatchNumber` and the in-progress `_builder`. Next `OnBlockCommitted` would lazy-create a fresh sealer starting from BatchNumber=1, potentially producing duplicate batch numbers if any batches were already submitted.
- Removed the explicit reset. The existing `_sealer ??= new BatchSealer(...)` lazy-init handles first-time creation; subsequent Configures only refresh the `_settings` field (which the existing sealer ignores — settings are captured at construction). Mid-flight reconfiguration of batch thresholds isn't supported anyway.

Cumulative: 424 tests / 27 projects.

### Changed — `L2BridgePlugin.Configure` lazily inits processors to preserve cross-batch state

- `Configure` unconditionally recreated `DepositProcessor` + `WithdrawalProcessor`. If `Configure` ever ran twice (config-watcher re-fire, host re-init), the new processors started fresh — discarding `DepositProcessor._consumed` and `WithdrawalProcessor._consumedAcrossBatches` (iter 133), allowing already-processed deposits and closed-batch withdrawals to be replayed on L2 with the duplicate only catching hours later at L1 settlement.
- Switched to `_depositProcessor ??= new …` / `_withdrawalProcessor ??= new …` lazy init. Subsequent Configure calls are now no-ops for the processor instances; their replay-protection sets persist for the plugin's lifetime. Matches the iter-70/71 in-place-WithMetrics-rewire pattern that was added for the same reason.

Cumulative: 424 tests / 27 projects.

### Fixed — `ReferenceBatchExecutor` skips effects from failed transactions

- A failed transaction's emitted withdrawals + L2→* messages were still added to the batch trees. Per L2 semantics, a failed tx reverts all its state changes — including emitted effects. The `ITransactionExecutor` contract should already filter on the executor side, but a buggy executor that leaks effects from a failed tx silently produces a withdrawal-tree commitment that doesn't match the (correct) ReceiptRoot — surfacing only at L1 settlement when the inclusion proof for the leaked withdrawal is checked against the user's actual state (which never debited the funds).
- Defense in depth at the batch level: `if (!result.Receipt.Success) continue;` skips the `withdrawalTree.Add` / `outbox.Add` calls.
- **1 new test**: `FailingExecutor` returns a leaked withdrawal on a failed tx → batch's `WithdrawalRoot` is still `Zero`.

Cumulative: 424 tests / 27 projects.

### Changed — Documented `ProofResult` prover contract

- The iter-139 (Kind match) and iter-140 (PublicInputHash match) settlement-plugin assertions are now also documented in the `ProofResult` record's XML doc — making the contract explicit at the API surface where prover authors will see it. No behavior change; just makes the invariants visible to anyone implementing `IL2Prover` without having to read the settlement plugin to discover what's checked.

Cumulative: 423 tests / 27 projects.

### Changed — `BisectionGame.RunRound` emits the BisectionRounds metric on its dead-branch settle path

- The `mid == _lo` branch in `RunRound` is normally unreachable (TrySettle at construction + every prior round keeps `_hi - _lo > 1` while `_settled == false`). But if a future refactor ever breaks that invariant, the branch settles the game without emitting `MetricNames.BisectionRounds` — a silent metric drop. Defense-in-depth: emit the same metric `TrySettle` would.
- No behavior change in the non-degenerate path; just makes the dead-branch fallback consistent with `TrySettle`.

Cumulative: 423 tests / 27 projects.

### Fixed — `L2SettlementPlugin` also checks `ProofResult.PublicInputHash` matches settlement's hash

- Companion to the iter-139 Kind check. The settlement plugin computes its own `hash = StateRootCalculator.HashPublicInputs(publicInputs)` and the prover returns its own `proofResult.PublicInputHash`. If the two disagree, the prover proved a different set of inputs than settlement built — the iter-128 verifier-side `PublicInputHash` check catches it later, but it costs a wasted `SubmitBatch` round-trip and surfaces as a confusing remote rejection.
- Now the settlement plugin also asserts `proofResult.PublicInputHash.Equals(hash)` at the prove boundary; the catch-and-requeue path takes care of the rest.
- Updated `FakeProver` test fixture to honor the prover contract (compute the actual hash from `request.PublicInputs` instead of returning Zero).

Cumulative: 423 tests / 27 projects (same count; the iter-139 LiarProver test now exercises both Kind and PublicInputHash via the same plugin assertion path).

### Fixed — `L2SettlementPlugin` checks `ProofResult.Kind` matches requested kind

- A buggy prover that returned `ProofResult.Kind = None` (or any other mismatch) silently produced a commitment with whatever the prover claimed. The mismatch would only surface at audit time as `NoZeroProofCheck` failing with "ProofType.None — soft-sealed but never proved" — confusing, with no direct link back to the prover bug.
- The settlement plugin now asserts the prover's contract at the prove boundary: `proofResult.Kind != requestedKind` throws `InvalidOperationException`. The exception is caught by the existing `try/catch` around prove+submit, increments `SubmitFailures`, and re-queues the batch — so a buggy prover doesn't block the queue but its bug is at least visible in counters.
- **1 new test**: `LiarProver` returns `Kind = None` for a `Multisig` request → caught, no submission, failure counter ticks.

Cumulative: 423 tests / 27 projects.

### Fixed — `CensorshipDetector` rejects negative `baseSlashAmount`

- The constructor accepted any `BigInteger` for `baseSlashAmount`. A negative slash amount, embedded in the resulting `CensorshipReport.SlashAmount`, would either get silently flipped on L1 (rewarding the offending sequencer instead of penalizing) or revert at the slash transaction — either way the operator only finds out after submitting reports.
- Now: reject negative at construction with `ArgumentOutOfRangeException`. Zero is still allowed for warning-only modes (operator wants detection without enforcement).
- **2 new tests**: negative → rejected; zero → accepted (warning-only mode).

Cumulative: 422 tests / 27 projects.

### Changed — `Sp1Bridge.IsAvailable` caches the lib-loadable result

- Every `Prove`/`Verify` call invoked `IsAvailable`, which re-attempted the `NativeAbiVersion()` P/Invoke and re-paid the `DllNotFoundException` cost in dev environments where the bridge is intentionally absent. The result is sticky for process lifetime — the lib is either there at startup or it isn't.
- Cached the result in a `static bool? _isAvailableCache`. Added `ResetAvailableCache()` for test scenarios that want to re-probe.
- **1 new test**: pin the cache behavior (first/second/post-reset calls all return the same result, no exception).

Cumulative: 420 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore.Record*Proof` takes a defensive copy

- `RecordWithdrawalProof` and `RecordMessageProof` retained the caller's `byte[]` reference. A caller who reused a scratch buffer across many records — or who mutated their copy after passing it in — would silently corrupt the previously stored proof. The corruption would only surface much later when an RPC client scraped `/getl2withdrawalproof` and got back garbage.
- Both setters now `Clone()` the input. The `IReadOnlyList<byte>` style API (`ReadOnlyMemory<byte>?`) on the read side already guards against caller mutation; this aligns the write side.
- **1 new test**: store, mutate caller's buffer, read — assert stored bytes unchanged.

Cumulative: 419 tests / 27 projects.

### Changed — `L2ProverPlugin.Wire` gives a clear error for `ProofType.None`

- `ProofType.None` is a legitimate enum value (used for genesis / operator-trusted flows in the wire format) but the prover plugin can't produce a proof for it. The switch's `_ => throw NotSupportedException($"Unknown ProofType {_kind}")` arm fired with `"Unknown ProofType None"` — misleading, since None is defined.
- Added an explicit `ProofType.None => …` case with a message explaining what the operator should do: configure Multisig/Optimistic/Zk to enable settlement.

Cumulative: 418 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore.RegisterAsset` removes orphan index entries

- Same bug pattern as iter 100's `AssetRegistry.Register` fix, in the RPC-side asset cache. Re-registering an L1 asset against a different L2 token (or vice versa) overwrote one index but left the stale entry in the other. `GetCanonicalAsset(oldL2)` still returned the L1 asset while `GetBridgedAsset(L1)` returned the new L2 — silent inconsistency between the two RPC lookups.
- Now: detect both repoint cases and `TryRemove` the orphaned entry from the opposite index before writing the new mapping.
- **1 new test**: `RegisterAsset_RepointL2_RemovesOrphan` — re-register, assert old L2 → L1 lookup returns null.

Cumulative: 418 tests / 27 projects.

### Fixed — `WithdrawalProcessor` enforces nonce uniqueness across batches

- `_byNonce` was cleared on `SealBatch`, so a user could re-stage the same `(sender, nonce)` withdrawal in the next batch — L2 silently accepted; the duplicate was only caught hours later at L1 settlement. The `WithdrawalRequest.Nonce` field's docstring is explicit: "per-(chain, sender) monotonic for replay protection," so L2 must enforce uniqueness across the chain's lifetime, not just per-batch.
- Added `_consumedAcrossBatches` set populated on `SealBatch` (just before clearing `_byNonce`). `Stage` now rejects duplicates from prior batches with a clear "already used by sender X in a prior batch" message.
- **1 new test**: stage nonce 1, seal, attempt to stage nonce 1 again → rejected.

Cumulative: 417 tests / 27 projects.

### Changed — `checked` arithmetic sweep for unbounded buffer-size sums

- Continued the iter-130/131 pattern across the remaining unbounded `var size = X + caller.Length` sites:
  - `BatchSerializer.Encode` — `CommitmentFixedSize + commitment.Proof.Length`.
  - `MessageHasher.HashMessage` — `61 + payload.Length`.
  - `KeyedStateStore.HashEntry` — `4 + key.Length + 4 + value.Length`.
- Each sum is now wrapped in `checked(…)` so a pathological caller-supplied length near `int.MaxValue` surfaces an `OverflowException` at the sum site rather than later as a confusing `OverflowException` from `new byte[wrappedNeg]` deep in the allocator. Bounded payloads (signature/proof bytes already capped by validators) are unchanged.

Cumulative: 416 tests / 27 projects.

### Changed — `Sp1RiscVProver.ProveAsync` uses `checked` arithmetic for combined-buffer size

- `4 + publicInputBytes.Length + 4 + request.Witness.Length` summed in plain `int`. A pathological witness near `int.MaxValue` would wrap to negative and surface as `OverflowException` from `new byte[wrappedNeg]` with no link to the offending sum.
- Wrapped the size computation in `checked(…)` (matches the iter-130 pattern in the gateway aggregators). Behavior under realistic witness sizes is unchanged.

Cumulative: 416 tests / 27 projects.

### Changed — Gateway aggregators use `checked` arithmetic for size sums

- `PassThroughAggregator.ConcatenateProofs` and `PassThroughRoundProver.Combine` summed `4 + proofLen` accumulators in plain `int` arithmetic. A pathological N × proofLen combination near `int.MaxValue` (~2 GiB) would silently wrap to negative; the next `new byte[wrappedNeg]` then threw `OverflowException` deep in the allocator with no link back to the offending sum.
- Wrapped both sites in `checked(…)` so an overflow surfaces with the operation in scope. Behavior under realistic loads is unchanged.
- No new tests — behavior preserved; existing 19 gateway tests still pass.

Cumulative: 416 tests / 27 projects.

### Changed — `L1MessageInbox.Enqueue` duplicate-pending check is O(1)

- The check was `foreach (var existing in _pending) if (...) throw` — O(n) per enqueue. Under bursty inbound traffic with thousands of pending messages, the cost compounds: a flood of N legitimate messages is O(N²). Not a security bug, but a performance cliff.
- Added a `HashSet<(uint, ulong)> _pendingKeys` mirror that's kept in sync with `_pending`. Enqueue uses `Add` (returns false on duplicate), Dequeue uses `Remove`. Behavior is identical; the hot path is now O(1).
- No new tests — behavior unchanged, existing 10 messaging tests still pass.

Cumulative: 416 tests / 27 projects (no test count change).

### Fixed — `VerifierRegistry.VerifyAsync` checks `commitment.PublicInputHash` matches `publicInputs`

- The registry compared 10 duplicated fields between `commitment` and `publicInputs` but never re-derived the actual hash of `publicInputs` and matched it against `commitment.PublicInputHash`. A malicious submission could set `PublicInputHash` to any value (planning a forged future replay against the consensus-recorded hash) while supplying real `publicInputs` that the verifier accepts. The audit-time `PublicInputHashConsistencyCheck` (iter 96) caught this after-the-fact, but verify-time is the right boundary.
- Added `StateRootCalculator.HashPublicInputs(publicInputs).Equals(commitment.PublicInputHash)` check; failure → `ProofVerificationResult.Fail("commitment.PublicInputHash != hash(publicInputs)")`.
- **1 new test**: `Registry_FailsWhenPublicInputHashIsForged` — commitment with arbitrary `PublicInputHash` but valid 10-field-aligned `publicInputs` → rejected with the right reason.

Cumulative: 416 tests / 27 projects.

### Fixed — `L2SettlementPlugin.Dispose` no longer races in-flight `SubmitNextAsync`

- `Dispose` called `_submitGate.Dispose()` unconditionally. If `SubmitNextAsync` was mid-flight (very plausible since it's invoked fire-and-forget from `OnBatchSealed`), the inevitable `Release()` in its `finally` threw `ObjectDisposedException` — which surfaces only via `TaskScheduler.UnobservedTaskException` (invisible by default) and aborts the in-flight submit's metric accounting.
- Both ends of the gate now swallow `ObjectDisposedException`: the entry `WaitAsync` returns quietly when shutdown wins the race; the exit `Release` is wrapped in a try/catch. Either way the in-flight task completes cleanly.
- **1 new test**: submit parked inside a blocking client, `Dispose()` while in-flight, then unblock the client — assert no exception escapes.

Cumulative: 415 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` per-check exception isolation

- A buggy custom check that threw aborted the entire audit — subsequent checks were skipped, and the operator saw only the exception (no findings from anything that ran before it). For an audit framework whose value is "every registered check runs and reports its result," this is the wrong default.
- Now: per-check try/catch converts a thrown exception into a single failure finding (`Check = check.Name`, `Detail = "check threw {Type}: {Message}"`), and the audit continues. Caller cancellation still propagates verbatim as `OperationCanceledException` — that's a control-flow signal, not a check failure.
- **2 new tests**: throwing-check produces a failure finding while a sibling `ContinuityCheck` still runs to completion; pre-cancelled token surfaces `OperationCanceledException` instead of being swallowed.

Cumulative: 414 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` empty-batches still emits run + failure metrics

- The empty-batches early return short-circuited before the metric increments. The failing finding showed up in the returned `AuditReport`, but `AuditsRun` and `AuditFailures` counters never ticked. An operator watching only dashboards would see the audit as "didn't happen" — invisible to monitoring even though we returned a failed report.
- Now: increment `AuditsRun` + `AuditFailures` on the empty-batches branch too, before returning. The report-side and metrics-side observability paths are now consistent.
- **1 new test**: empty batches → both counters tick + report still fails.

Cumulative: 412 tests / 27 projects.

### Fixed — `neo-stack` exception handling + `init-l2 --chain-id` validation

- Two related gaps in the launcher CLI:
  1. `init-l2` parsed `--chain-id` with `uint.Parse` (raw `FormatException` on bad input) and never validated against the L1-reserved 0. Aligned with `create-chain`'s iter-123 fix: `uint.TryParse` + `ChainIdValidator.ValidateL2(value, "--chain-id")`.
  2. `Program.Main` had no top-level try/catch. Any subcommand exception (`FormatException`, `IOException`, `InvalidDataException` from the validators) leaked as a raw stack trace. Added the wrap that `neo-hub-deploy` already uses: catch → `Console.Error.WriteLine($"Error: {ex.Message}")` + exit 1.

Cumulative: 411 tests / 27 projects (CLI changes aren't unit-tested; the underlying validator is covered in `UT_Models`).

### Fixed — `neo-stack create-chain --chain-id` validates against L1-reserved 0

- `uint.Parse(...)` accepted any value, including the L1-reserved `0` (matches `L2Outbox.L1ChainId`). An operator who typo'd `--chain-id 0` would generate a chain config that misroutes L2→L2 messages as L2→L1 — silently broken from the genesis block. Also: malformed input like `--chain-id abc` threw `FormatException` with a raw stack trace.
- Now: `uint.TryParse` for clean error on non-numeric, plus `ChainIdValidator.ValidateL2(value, "--chain-id")` (the shared helper from iter 114) for the reserved-id check. Each surfaces a clear single-line error and exit code 1.

Cumulative: 411 tests / 27 projects (no new test; the validator is already covered in `UT_Models`).

### Fixed — `InMemoryMessageRouter._finalized` is thread-safe

- `_finalized` was a plain `Dictionary<UInt256, FinalizedEntry>`. `RecordFinalized` (settlement-pipeline thread) writes; `GetMessageProofAsync` (RPC-handler threads) reads. A concurrent read while writing had a small but real chance of corruption / `NullReferenceException` deep in `Dictionary.FindEntry`.
- Swapped to `ConcurrentDictionary<UInt256, FinalizedEntry>`. The cost is negligible for the test/devnet backend; production wires a different impl.
- **1 new test**: 8 threads × 500 iterations alternating writes / reads → no exceptions.

Cumulative: 411 tests / 27 projects.

### Fixed — `InMemorySequencerCommitteeProvider.SetMaxCommitteeSize` rejects shrink below current count

- The setter accepted any `max ∈ [1, 64]` regardless of how many members were already registered. Calling `SetMaxCommitteeSize(2)` on a 5-member committee silently succeeded; the count then exceeded the cap until members organically exited — a misleading "almost-frozen" state that hides the operator's typo (registrations would be rejected with no clear pointer back to the misconfigured cap).
- Now: rejects with `InvalidOperationException("max N < current committee count M — exit members before shrinking")` so the operator sees both the proposed and actual values immediately.
- **1 new test**: 5-member committee, `SetMaxCommitteeSize(2)` → rejected with both numbers in the message.

Cumulative: 410 tests / 27 projects.

### Fixed — `ProofValidityCheck.RunAsync` matches null-guard convention

- Sister checks (`ContinuityCheck`, `NoZeroProofCheck`, `PublicInputHashConsistencyCheck`) all begin with `cancellationToken.ThrowIfCancellationRequested()` + `ArgumentNullException.ThrowIfNull(batches)`. `ProofValidityCheck.RunAsync` was missing both — a null-batches caller hit the `foreach` and got a `NullReferenceException` with no link back to the bad input.
- Added the standard prelude.
- **1 new test**: `RunAsync(null)` → `ArgumentNullException`.

Cumulative: 409 tests / 27 projects.

### Added — Shared `Neo.L2.Telemetry.PortValidator.Validate(int, label)`

- The devnet runner's `--metrics-port` parser had no bounds check — a typo like `--metrics-port 99999` propagated to `IPEndPoint` construction and surfaced an opaque "value must be between 0..65535" deep in the wiring path.
- Promoted iter-111's `L2MetricsSettings.ValidatePort` to a shared helper in `Neo.L2.Telemetry` so CLI tools can reuse the check without taking a plugin dependency. Caller-supplied `contextLabel` ("L2Metrics Port" vs `--metrics-port`) appears in the error message so the operator sees which input was bad. `L2MetricsSettings.ValidatePort` now delegates to it.
- **4 new tests**: boundary values 0/9090/65535 → accepted; negative → rejected; >65535 → rejected; context label appears in error message.

Cumulative: 408 tests / 27 projects.

### Fixed — `Sp1RiscVProver` only falls back to mock on `NotImplemented`

- The fallback condition was `status != Ok || proofBytes is null`, which silently substituted a trivially-valid mock proof on **any** non-OK status — including real bridge errors like `InvalidInput` (malformed witness) or `ProveFailed` (prover crashed). The downstream verifier then ran the mock proof through the real bridge, got `VerifyRejected`, and surfaced only as a confusing "verify rejected" message hours later — disconnecting the cause (bad input on the prover side) from the symptom (failed verify).
- Now: fallback fires only on `NotImplemented` (the genuine "bridge missing" signal). Other non-OK statuses throw `InvalidOperationException("SP1 bridge {status} for proof generation — verify input shape, witness, or bridge state")` so the operator sees the bridge's actual status at the failure site.

Cumulative: 404 tests / 27 projects.

### Fixed — `Sp1Bridge.Prove` bounds-checks native return length

- The Native FFI returned `nuint outputLen` was cast `(int)outputLen` for `new byte[len]` and `Marshal.Copy(..., len)` without bounds checking. A misbehaving native bridge or corrupted FFI return that declared > 2 GB would wrap the cast and feed a wrapped length into `Marshal.Copy` — a heap-overflow shape on a process boundary that crosses .NET ↔ unmanaged.
- Added `Sp1Bridge.MaxProofBytes = 1 GiB` defensive cap (well above realistic SP1 proof sizes, well below `int.MaxValue`). Cast is now guarded — anything above the cap returns `Sp1BridgeStatus.InvalidInput` and the buffer is freed via the existing `finally`.
- **1 new test**: pins `MaxProofBytes > 0 && < int.MaxValue` so a future "trust the bridge" refactor can't silently drop the guard.

Cumulative: 404 tests / 27 projects.

### Fixed — `MetricsHttpServer.StatusText` returns the right reason phrase for 503

- The switch only knew about 200/404/500. The readiness-probe failure path (`HandleReady` → `MetricsHttpResponse(503, ...)`) fell through to the default `=> "OK"`, sending `"HTTP/1.0 503 OK"` on the wire — status code says error, reason phrase says OK. Strict HTTP parsers (load-balancer health-check libraries, Kubernetes probes) would reject this as malformed.
- Added the 503 case → `"Service Unavailable"`.
- **1 new test**: real HTTP scrape against `/readyz` with a failing readiness check → status 503, reason phrase "Service Unavailable".

Cumulative: 403 tests / 27 projects.

### Fixed — `AttestationVerifier` deduplicates signers before signature verification

- The verify loop sequence was validator-set → length → ECDSA-verify → dedup. A malicious prover could fill the wire payload with `MaxSigners` (256) copies of the same valid signature and force the verifier to perform 256 redundant ECDSA verifications before the duplicate-signer check fired. Not a DoS at production scale, but a wasted-cost vector that ramps with the cap.
- Reordered: dedup-on-first-occurrence runs before signature verification. Cost is now bounded by the number of *distinct* keys submitted, not the wire-payload count. Correctness unchanged: duplicates still fail with the same error.
- **1 new test**: payload with one signer repeated twice → `"duplicate signer"` failure.

Cumulative: 402 tests / 27 projects.

### Added — `ChainIdValidator.ValidateL2(uint)` plugin-config validator

- `ChainId = 0` is reserved for Neo L1 (matches `L2Outbox.L1ChainId` sentinel). An L2 chain that adopts it would misroute L2→L2 messages as L2→L1, sending them out the wrong outbox subtree. The default `uint` value is 0, so an operator who omits `ChainId` from `config.json` silently lands on the reserved value.
- Added `Neo.L2.ChainIdValidator.ValidateL2(uint, settingName?)` (in `Neo.L2.Abstractions`) that throws `InvalidDataException("<settingName> 0 is reserved for Neo L1")` on the reserved id. Wired into `L2BridgePlugin.Configure`, `L2BatchSettings.From`, and `L2SettlementSettings.From`.
- Each call site reads via `GetValue<uint?>("ChainId")` to distinguish "key missing" (test mode, no config — leave at default 0) from "explicitly set to 0" (operator misconfig — reject). Real production configs always set the key, so the validator fires whenever it's actually wrong.
- **3 new tests**: ValidateL2(0) → rejected; ValidateL2(1, 1001, MaxValue) → accepted; setting-name parameter included in error message.

Cumulative: 401 tests / 27 projects.

### Fixed — `AuditReport.Passed` requires non-empty `Findings`

- `Passed` was `Findings.All(f => f.Passed)`, which returns `true` vacuously on an empty list. A caller who constructed the report directly (bypassing `ChainAuditor`'s no-checks-registered guard) would see "passed" with no checks run — a silent observability regression for hand-built reports. Sister case to iter 85's `ChainAuditor` empty-checks fix, applied at the report-API layer.
- Now: `Findings.Count > 0 && Findings.All(...)`. `ChainAuditor` already guarantees at least one finding via the iter-85 guard, so this is purely defense for hand-built reports.
- **1 new test**: report with empty findings → `Passed = false`.

Cumulative: 398 tests / 27 projects.

### Fixed — `L2BatchSettings.From` rejects zero / negative thresholds at parse time

- `MaxBlocksPerBatch`, `MaxTransactionsPerBatch`, and `MaxBatchAgeMillis` accepted any int. A misconfig like `MaxBlocksPerBatch: 0` made `BatchSealer.ShouldSeal` return `true` on every block — every block became its own batch, producing degenerate per-block batches that each carry full settlement / proving overhead. Operators saw the misconfig as a runaway L1 submission rate hours later, not at plugin load.
- Added `L2BatchSettings.ValidatePositive(int, name)` that throws `InvalidDataException("L2Batch <name> must be > 0, got N — fix config")` at parse time. `From` calls it on each Max* setting.
- **3 new tests**: zero → rejected with name; negative → rejected with value+name; boundary `1` → accepted (per-block sealing is degenerate but legal for test/devnet diagnostics).

Cumulative: 397 tests / 27 projects.

### Fixed — `L2MetricsSettings.From` validates port range at parse time

- `Port = s.GetValue("Port", 9090)` accepted any int. A typo like `Port: 90909` (six digits) propagated to `IPEndPoint` construction at `Start()`, where the resulting `ArgumentOutOfRangeException("value must be between 0..65535")` is real but the operator has to dig through a stack trace to map it back to a config typo.
- Added `L2MetricsSettings.ValidatePort(int)` that throws `InvalidDataException("L2Metrics Port N out of range — must be 0 (any free) or 1..65535")` at parse time, citing both the bad value and the config-key name. `From` calls it on the parsed value.
- **3 new tests**: out-of-range high → rejected; negative → rejected; boundary values 0, 9090, 65535 → accepted.

Cumulative: 394 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` rejects duplicate batch numbers (strict ascending)

- The sort check was `batches[i].BatchNumber < batches[i - 1].BatchNumber` (strict less-than), which silently allowed equal batch numbers. A duplicate-batch-number list is a precondition violation (a chain can't carry two distinct commitments at the same height) but the auditor would let it pass; the duplicate would then surface downstream as a confusing `ContinuityCheck` failure ("batch N does not follow N").
- Tightened to `<=` so duplicates throw `ArgumentException("batches must be sorted strictly ascending by batchNumber")` at the input-validation step.
- **1 new test**: two batches at batch number 5 → throws with "strictly ascending" in message.

Cumulative: 391 tests / 27 projects.

### Added — `ProofTypeExtensions.Resolve(byte)` plugin-config validator

- `L2ProverPlugin.Configure` did `_kind = (ProofType)section.GetValue<byte>(...)` without bounds-checking. A misconfigured `ProofType: 99` would only surface much later at `Wire()` time (when the operator sees `NotSupportedException("Unknown ProofType 99")`).
- `L2SettlementSettings.From` did the same — the bad byte propagated into `_settings.ProofType` and only failed at first `SubmitNextAsync`.
- Added `ProofTypeExtensions.Resolve(byte)` (in `Neo.L2.Abstractions`, alongside the enum) that throws `InvalidDataException` with a clear message listing valid values. Both plugins now use it at config-parse time, so misconfiguration surfaces at plugin load.
- **2 new tests**: every valid byte 0..3 → resolves; byte 99 → rejected with the byte in the message.

Cumulative: 390 tests / 27 projects.

### Fixed — `RiscVProofPayload.Decode` rejects unknown `ProofSystem` discriminants

- Same enum-discriminant gap as iter 103's `BatchSerializer` fix, but in the inner Stage-2 ZK proof wrapper. `bytes[1]` was cast `(ProofSystem)` without bounds-checking. A corrupted or replayed-from-future payload with a discriminant > 4 slipped through as an undefined enum value, and a downstream verifier dispatcher's `==` comparison would silently treat it as "not the expected one" — selecting the wrong backend or skipping verification entirely.
- Now bounds-checks against `(byte)ProofSystem.Axiom` and throws `InvalidDataException` with a clear message.
- **2 new tests**: byte 99 → rejected; every valid byte 0..4 → round-trips.

Cumulative: 388 tests / 27 projects.

### Fixed — `BatchSerializer.Decode` rejects trailing bytes (strict length match)

- Same trailing-byte malleability surface as `DepositPayload` (iter 106) but in the master commitment wire format. The check was `data.Length < pos + proofLen` — only caught buffers shorter than declared. Trailing bytes after the proof were silently ignored, opening a divergence between the L1 contract (hashes full calldata) and the L2 decoder (strips trailing). Same logical commitment, two different hashes.
- Strengthened to `pos + proofLen != data.Length`. The four boundary-pair tests around proof length (zero, at-cap, oversized-claim, trailing-bytes) now form a complete defensive set.
- **1 new test**: commitment with trailing bytes → `InvalidDataException("length mismatch")`.

Cumulative: 386 tests / 27 projects.

### Fixed — `DepositPayload.Decode` rejects trailing bytes (strict length match)

- The length check was `pos + amountLen > bytes.Length`, which only caught buffers shorter than declared. Trailing bytes after the amount were silently ignored. An attacker could append padding that the L1 hashes (full bytes) but the L2 decoder strips — a malleability surface where the same logical deposit produces different leaves on either side.
- Strengthened to `pos + amountLen != bytes.Length` (matches the `OptimisticProofPayload.Decode` pattern).
- **1 new test**: payload with trailing bytes → `InvalidDataException`.

Cumulative: 385 tests / 27 projects.

### Fixed — `L2DAPlugin.Configure` rejects unknown `DAMode` bytes

- The `DAMode` switch fell through to `InMemoryDAWriter` for any byte outside `0..3`. An operator who misconfigured `DAMode = 99` would silently end up with the in-memory test backend — they'd think batch payloads were going to External DA, but actually they vanish into a process-local hash table that disappears at restart. The kind of failure that only surfaces hours later when something downstream tries to fetch the data.
- Added `L2DAPlugin.ResolveDAMode(byte)` that validates against the defined enum range and throws `InvalidOperationException` with a clear message listing the valid values. The switch's `_` arm is now an internal-defense `InvalidOperationException("unhandled DAMode N")` since `ResolveDAMode` already filters.
- **2 new tests**: every valid byte 0..3 → resolves to its enum; byte 99 → rejected with the byte in the message.

Cumulative: 384 tests / 27 projects.

### Fixed — `DeployPlanner` surfaces clear errors for duplicate / empty step names

- The name index was built via `steps.ToDictionary(s => s.Name)`, which throws a generic `ArgumentException("An item with the same key has already been added. Key: foo")` on duplicates. Operators with a typo in their plan JSON had to map the message back to the offending entry by hand.
- Empty / whitespace step names slipped through silently — a `DependsOn: [""]` reference would happen-to-resolve and the operator's typo would only show up at runtime.
- Now: explicit foreach with `string.IsNullOrWhiteSpace` rejection (`"deploy step name must not be empty or whitespace"`) and `TryAdd` for clearer duplicate messages (`"duplicate deploy step name '<name>'"`).
- **2 new tests**: duplicate-name rejection with name in message; empty-name rejection.

Cumulative: 382 tests / 27 projects.

### Fixed — `BatchSerializer.Decode` rejects unknown `ProofType` discriminants

- The decoder cast `(ProofType)data[pos++]` without validating the byte was within the defined enum range (0..3). A corrupted L1 calldata payload, a replay from a future-version chain, or a hand-crafted attack could carry a discriminant > 3, producing an undefined enum value that downstream `==` comparisons would silently treat as "not the expected one" — a silent verification skip.
- Now bounds-checks the byte against `(byte)ProofType.Zk` and throws `InvalidDataException` with a clear message.
- **2 new tests**: byte 99 → rejected; every valid byte 0..3 → round-trips.

Cumulative: 380 tests / 27 projects.

### Fixed — `InMemoryL2RpcStore.Finalize` keeps `_latestStateRoot` monotonic

- `Finalize(N)` blindly overwrote `_latestStateRoot` with batch N's post-state root regardless of N. Finalizing batch 5 then batch 3 left the latest root at batch 3's older value — an apparent state-root regression that a downstream relayer treats as a chain reorg signal.
- Now tracks `_latestFinalizedBatch` under a lock; `Finalize` only updates `_latestStateRoot` when the new batch number exceeds the prior latest. `GetLatestStateRoot` reads under the same lock so a concurrent reader never observes a torn `UInt256`.
- **1 new test**: `Finalize(5)` then `Finalize(3)` → latest root stays at batch 5.

Cumulative: 378 tests / 27 projects.

### Fixed — `L2RpcMethods` parameter parsing surfaces clear errors

- An RPC call with too few params (e.g. `getl2batch [1001]` — missing the batch number) hit `JArray`'s underlying `List<T>` indexer and surfaced `ArgumentOutOfRangeException` with the unhelpful `"Index was out of range..."` message. RPC clients had no way to tell which param was missing.
- A `chainId` value above `UInt32.MaxValue` was read as `ulong` then cast `(uint)` — silently truncating. Caller passing `0x100000001` got reduced to `1`; `AssertOurChain` then compared `1` vs the local id with a misleading "differs from local" message instead of the actual overflow.
- Added `RequireParam` helper that bounds-checks before indexing, plus `ReadUInt` helper that uses `checked((uint)…)` so oversized chain ids surface `OverflowException` at the parsing boundary.
- **2 new tests**: too-few-params → `ArgumentException("param[N] missing")`; oversized chainId → `OverflowException`.

Cumulative: 377 tests / 27 projects.

### Fixed — `AssetRegistry.Register` removes orphan index entries on re-point

- Re-registering an L1 asset to a different L2 token (or vice versa) overwrote one index but left the stale entry in the other. `TryGetByL2(oldL2Asset)` would still return the prior mapping while `TryGetByL1` returned the new one — a silent registry inconsistency that could route a deposit through the old L2 token long after the operator thought they had repointed it.
- `Register` now detects both repoint cases — `(L1Asset, L2ChainId)` mapped to a new `L2Asset`, and `L2Asset` mapped to a new `(L1Asset, L2ChainId)` — and removes the orphaned entry from the opposite index before writing the new mapping.
- **2 new tests**: repoint L2Asset → orphan L2 index removed; repoint L1Asset → orphan L1 index removed.

Cumulative: 375 tests / 27 projects.

### Fixed — `DepositProcessor.Process` claims nonce only after validation succeeds

- The consumed-set was populated BEFORE the asset-registry lookup, so a transient validation failure (e.g. "asset not yet registered" — the L2 cross-chain pipeline can deliver a deposit before the asset is registered on L2) permanently locked the `(SourceChainId, Nonce)` pair. When the operator later registered the missing asset, retry hit the consumed-set first and threw `"already processed"` — the L1 message stayed in NeoHub's "delivered" state but the L2 could never mint the funds.
- Now: decode + asset-registry lookup happens first, then the atomic claim-and-add to `_consumed`. Replay protection still covers the success path (subsequent identical-nonce calls fail), and the concurrency window for two callers passing validation simultaneously is still safe (only one wins the `_consumed.Add`).
- **1 new test**: process with unknown asset → fails, nonce NOT consumed; register the asset; retry → succeeds, nonce now consumed.

Cumulative: 373 tests / 27 projects.

### Fixed — `L2SettlementPlugin.SubmitNextAsync` serializes parallel submits

- `OnBatchSealed` did `_ = SubmitNextAsync()` fire-and-forget. When N batches sealed in quick succession, N submit tasks ran in parallel — racing each other into `_client.SubmitBatchAsync`. NeoHub then sees out-of-order `BatchNumber` values and rejects the late ones; the retry path tries to re-queue the loser, but the winner's batch is already on-chain — the L1 expects `lastSubmitted+1`, so the loser stays stuck and retries forever.
- Added a `SemaphoreSlim(1, 1)` gate around the dequeue + prove + submit path. Concurrent calls now wait their turn; submission order matches enqueue order.
- **1 new test**: 4 concurrent `SubmitNextAsync` calls against a tracking client → all 4 batches submitted, peak in-flight = 1.

Cumulative: 372 tests / 27 projects.

### Fixed — `ChallengeOrchestrator.InspectWithBisectionAsync` validates checkpoint shapes

- The agreement-at-end short-circuit (`challengerCheckpoints[^1].Equals(sequencerCheckpoints[^1])`) ran *before* any length validation. An empty array crashed with raw `IndexOutOfRangeException`; mismatched-length arrays silently compared incompatible last elements (could wrongly return "no fraud" when the last entries happened to match).
- Now the orchestrator validates `length match` and `length ≥ 2` upfront, throwing `ArgumentException` at its boundary. The `BisectionGame` constructor's existing checks become defensive duplicates.
- **3 new tests**: empty arrays → throws; mismatched lengths → throws; single-checkpoint (length 1) → throws.

Cumulative: 371 tests / 27 projects.

### Added — `PublicInputHashConsistencyCheck` audit

- New `IAuditCheck` that re-derives `PublicInputHash` from each batch's stored fields (via `StateRootCalculator.HashPublicInputs`) and compares against the stored value. Catches commitments where the hash was set to a value derived from different public inputs than the commitment claims — a tamper that would otherwise verify against the wrong proof.
- MVP assumes `L1MessageHash = Zero` and `BlockContextHash = Zero`, matching the Phase 0-3 settlement plugin's `BuildPublicInputs`. When future phases populate those fields, this check needs an augmenting resolver (same shape as `ProofValidityCheck`'s `publicInputsResolver`).
- **3 new tests**: consistent hash → pass with summary; tampered hash → fail with batch number; empty list → vacuous pass.

Cumulative: 368 tests / 27 projects. Audit pluggability now covers continuity, proof validity, no-zero-proof, and public-input-hash consistency.

### Fixed — `JsonRpcClient` wraps network-level failures as `JsonRpcException`

- Iters 93 + 94 wrapped malformed JSON and non-2xx HTTP. This completes the picture by wrapping `HttpRequestException` (connection refused / DNS / TLS) and `TaskCanceledException` from a timeout. Caller-driven `OperationCanceledException` (via the supplied `CancellationToken`) is preserved verbatim so the caller distinguishes their own cancel from a server-side issue.
- All wrap to `JsonRpcException(-32603)` with a descriptive message.
- **2 new tests**: network error → wrapped; caller cancellation → `OperationCanceledException` propagates.

Cumulative: 365 tests / 27 projects. The RPC client now exposes exactly one exception type for every failure mode except caller-cancellation.

### Fixed — `JsonRpcClient` wraps non-2xx HTTP responses as `JsonRpcException`

- `EnsureSuccessStatusCode` threw `HttpRequestException` on a non-2xx status (e.g. proxy 502, server 500, gateway 504), inconsistent with the parse-error wrapping fixed in iter 93. Callers had to handle two different exception types depending on whether the failure was at the HTTP or JSON layer.
- Now wraps as `JsonRpcException(-32603)` ("Internal error" per JSON-RPC 2.0 spec) with the status + reason phrase + body snippet.
- **1 new test** stubs an HTTP 502 response and asserts `JsonRpcException(-32603)`.

Cumulative: 363 tests / 27 projects. The RPC client now produces exactly one exception type for all failure modes (RPC error, parse error, HTTP error).

### Fixed — `JsonRpcClient` wraps malformed-JSON as `JsonRpcException`

- `JToken.Parse` exceptions on malformed RPC responses (e.g. proxy returning HTML 502, gateway truncated body) leaked as raw parser exceptions instead of `JsonRpcException`. Callers had to write disparate exception handlers depending on the failure origin.
- Wraps in `JsonRpcException` with code `-32700` (JSON-RPC 2.0 spec code for "Parse error") so callers see one exception type regardless of failure source.
- **1 new test** stubs an HTML 502 response and asserts `JsonRpcException(-32700)`.

Cumulative: 362 tests / 27 projects.

### Fixed — `L2MetricsPlugin.ResolveBindAddress` rejects empty / null input

- Empty `""` BindAddress fell through `IPAddress.TryParse` to `Dns.GetHostAddresses("")`, which on Linux returns ALL local interface addresses non-deterministically. The plugin would bind to a random interface depending on machine config — exactly the opaque-failure-mode an operator hates.
- Now `ArgumentException.ThrowIfNullOrEmpty` rejects both upfront with a clear error.
- **2 new tests**: empty + null both throw.

Cumulative: 361 tests / 27 projects.

### Fixed — `DeployPlan.FromJson` rejects unsupported version

- `FromJson` read the `version` field but didn't validate it. A future v2 plan with renamed fields (e.g. `nefPath` → `wasmPath`) would silently parse with the v1 reader and produce garbage. Same defensive pattern as the proof-payload decoders' version checks.
- Added `DeployPlan.CurrentVersion = 1` constant + a hard check in `FromJson` that throws `InvalidDataException` on mismatch.
- **1 new test** verifies a `version=99` plan is rejected.

Cumulative: 359 tests / 27 projects.

### Added — `AssetRegistry` overwrite-semantics test

- `Register` silently overwrites an existing entry under the same `(L1Asset, L2ChainId)` key. Documented now via a test that registers twice with different `Active` flags and asserts the second registration wins. Pins overwrite as a deliberate API choice — a future refactor to "throw on duplicate" would break the test instead of silently breaking governance flows that re-register assets.
- **1 new test**.

Cumulative: 358 tests / 27 projects.

### Fixed — `L2SettlementPlugin.SubmitNextAsync` no longer drains queue when un-wired

- Real silent-data-loss bug: `SubmitNextAsync` dequeued an item BEFORE checking whether `_prover` / `_client` had been wired. If `Wire()` hadn't been called yet (operator setup error, or `OnBatchSealed` event firing before wiring completed), every batch flowing through this plugin would be silently dropped — no exception, no failure metric, just gone.
- Fixed by moving the wiring check before the dequeue. Items stay in the queue until `Wire()` is called.
- **1 new test** asserts pending count stays at 1 after `SubmitNextAsync` is called without `Wire`.

Cumulative: 357 tests / 27 projects.

### Added — `MetricsEmittingDAWriter` unwrap/rewrap state-preservation test

- `L2DAPlugin.WithMetrics` unwraps the existing decorator and rewraps with the new sink. The inner `InMemoryDAWriter`'s content store is preserved through this swap, but no test pinned that property — a future refactor that re-allocated the inner could silently lose published content.
- **1 new test** publishes to a decorated writer, unwraps + rewraps with a different sink, then asserts `IsAvailableAsync(receipt)` still returns true on the new wrapper.

Cumulative: 356 tests / 27 projects.

### Added — `Plan_DetectsSelfCycle` test in `Neo.Hub.Deploy.UnitTests`

- The existing 2-step cycle test (`A→B→A`) didn't cover the degenerate length-1 self-cycle (`A→A`). Adding it pins the trivial case so a future refactor of the recursion-path check can't regress on it.
- **1 new test**.

Cumulative: 355 tests / 27 projects.

### Added — `MetricCatalog` non-blank-description check

- Existing tests enforced "every metric has an entry" + "no orphan entries" + "no trailing period". Missing: empty/whitespace descriptions, which would silently produce a useless Prometheus HELP line (`# HELP foo_total ` with nothing after).
- **1 new test** asserts every catalog value passes `string.IsNullOrWhiteSpace` → false.

Cumulative: 354 tests / 27 projects.

### Fixed — `ChainAuditor.AuditAsync` fails on zero-checks-registered

- An audit with no checks registered used to silently report `Passed = true` because `Findings.All(...)` returns true on an empty collection. A misconfigured production deployment that registered zero checks would get a green report despite proving nothing.
- Surfaces as a failure now with a `"no audit checks registered"` finding before any per-check work; feeds into the existing `l2.audit.failures` counter so an alert fires on the misconfiguration.
- **1 new test** registers no checks, runs an audit, asserts `Passed=false` and the failure counter increments.

Cumulative: 353 tests / 27 projects.

### Added — Boundary tests for `Optimistic`/`RiscV`/`Multisig` proof payload caps

- Iter 76-77 added max-length caps to all three inner proof-payload decoders, with reject-at-`Max+1` tests. Now paired with accept-at-exactly-`Max` tests on each (matches the symmetric pattern from iters 82-83 for `BatchSerializer` and `MerkleProofSerializer`):
  - `OptimisticProofPayload`: 4096-byte signature accepts.
  - `RiscVProofPayload`: 1 MiB inner-proof accepts.
  - `MultisigProofPayload`: 256-signer payload accepts.
- Each pair locks the boundary on both sides — an off-by-one in any direction now fails the build.
- **3 new tests**.

Cumulative: 352 tests / 27 projects. Every length-prefix decoder in the stack now has paired accept-at-`Max` + reject-at-`Max+1` tests.

### Added — `MerkleProofSerializer` boundary test at exactly `MaxDepth`

- Same shape as iter 82's `BatchSerializer` fix: the reject-at-`MaxDepth+1` test (iter 47) lacked a paired accept-at-exactly-`MaxDepth=64` test. An off-by-one could either reject a depth-64 proof (too strict) or admit depth-65 (too loose). Pinning both directions makes the boundary explicit.
- **1 new test** encodes + decodes a depth-64 proof end-to-end.

Cumulative: 349 tests / 27 projects.

### Added — `BatchSerializer` boundary test at exactly `ProofMaxBytes`

- The reject-at-1MiB+1 test (iter 75) didn't have a paired accept-at-exactly-1MiB test. An off-by-one in the limit check could either reject the boundary (too strict) or accept 1MiB+1 (too loose). Pinning both directions makes the boundary explicit.
- **1 new test** encodes + decodes a commitment with a 1MiB proof and verifies round-trip identity.

Cumulative: 348 tests / 27 projects.

### Added — Bidirectional `MetricCatalog` ↔ `MetricNames` consistency check

- The existing completeness test enforced "every `MetricNames` constant has a catalog entry." The reverse — "every catalog entry references a real constant" — was uncovered. An orphan description surviving a metric rename or removal would silently bloat the exposition without triggering any test.
- **1 new test** reflects over `MetricNames` constants and asserts every catalog key matches one. Both directions are now pinned.

Cumulative: 347 tests / 27 projects.

### Added — `MetricsHttpServer.IsRunning` diagnostic

- Public `IsRunning` property returns `true` after `Start()` and `false` after `Dispose()`. Useful for integration-test assertions, operator diagnostics, and host-side health checks that need to verify the metrics endpoint is up before reporting the node ready.
- **1 new test** verifies the lifecycle: `false` → `true` after `Start()` → `false` after `Dispose()`.

Cumulative: 346 tests / 27 projects.

### Added — Dispose-without-Start regression test for `MetricsHttpServer`

- All existing tests called `Start()` after construction; the constructed-but-never-Started case was uncovered. Future refactor that assumes `_loop` is non-null at Dispose would silently NPE only in deployments where Start was deferred or never called.
- **1 new test** constructs a server, calls Dispose without Start, then calls Dispose a second time — both must be no-throw.

Cumulative: 345 tests / 27 projects.

### Docs — AGENTS.md catches up to current state

- Component counts updated: 11 → 15 off-chain libs, 7 → 8 plugins (the new `Neo.Plugins.L2Metrics` was added in iter 50 but the `AGENTS.md` summary still said the old numbers).
- §5 row in the doc.md→code mapping now includes `L2Metrics` alongside the other plugins.
- New "Cross-cutting / Telemetry" row points at `Neo.L2.Telemetry` + `Neo.Plugins.L2Metrics` and `docs/telemetry.md`, mirroring the same row in `architecture-walkthrough.md`.
- Quick-commands test-count refreshed (194 → 344).

### Fixed — Hard cap on `MultisigProofPayload` signer count

- `signerCount` is uint16, naturally capped at 65535. The decoder validated buffer-size match but had no upper limit on count itself — a 65535-signer header forced allocation of `SignerSignature[65535]` (~7 MB) before any sanity check. Production multisigs run 7-21 signers; cap at `MaxSigners = 256` is generous but defensive.
- **1 new test** verifies a 257-signer header is rejected.

Cumulative: 344 tests / 27 projects. All four proof-payload decoders (Multisig, Optimistic, RiscV, plus the outer `BatchSerializer` wrap) now have explicit bounds.

### Fixed — Hard caps on `OptimisticProofPayload` + `RiscVProofPayload` decode

- The decoders accepted any non-negative length matching the buffer size — no upper bound. A hostile peer feeding a 4 GiB length-prefix would crash the verifier on `OutOfMemory` instead of getting a clean `InvalidDataException`. The outer `BatchSerializer.Decode` already caps at 1 MiB, but a caller decoding these payloads directly (skipping `BatchSerializer`) inherits no protection.
- Added defense-in-depth caps: `OptimisticProofPayload.MaxSignatureBytes = 4096` (real signatures are 64), `RiscVProofPayload.MaxProofBytes = 1 MiB` (matches `BatchSerializer`).
- **2 new tests** craft a header claiming oversized length; assert decoder throws `InvalidDataException`.

Cumulative: 343 tests / 27 projects.

### Added — `BatchSerializer` proof-size-limit tests

- `BatchSerializer.Encode` validates `Proof.Length <= ProofMaxBytes` (1 MiB) and `Decode` validates the same in the header. Both checks were uncovered by tests.
- **2 new tests**: encoding a 1 MiB+1 byte proof throws `ArgumentException`; decoding a header claiming oversized proof throws `InvalidDataException`. Closes a defense-in-depth gap — these limits are the safety net against a hostile peer dumping a 4 GiB proof at the L1 settlement contract.

Cumulative: 341 tests / 27 projects.

### Fixed — `PrometheusExporter` formats `±Infinity` per spec

- .NET's `double.ToString` returns `Infinity` / `-Infinity` for non-finite values, but Prometheus exposition format requires `+Inf` / `-Inf`. A scraper rejects the bad form. None of the current emit sites produce infinity, so this is defensive — but a misbehaving plugin (e.g. division by zero in a gauge) would silently corrupt the scrape.
- Helper `FormatDouble` handles `NaN`, `+Inf`, `-Inf` as Prometheus specifies; finite values still go through `G17` invariant.
- **3 new tests** for each non-finite case.

Cumulative: 339 tests / 27 projects.

### Fixed — `MetricsHttpServer.Start` is now idempotent

- Previously a second `Start` call would overwrite `_loop` with a new `Task.Run`, leaving the first accept loop dangling on the same `TcpListener`. Both loops would race on `AcceptTcpClientAsync`, only the latest got awaited at Dispose. Defensive fix consistent with iter 67's plugin-level lock.
- **1 new test** asserts `Start()` twice still serves a clean scrape.

Cumulative: 336 tests / 27 projects.

### Added — `BinaryTreeAggregator.WithMetrics` for consistency

- Previously the aggregator only accepted metrics via constructor — an operator wanting to swap the sink mid-flight had to construct a new aggregator and lose the pending submission list. Adds an in-place `WithMetrics(IL2Metrics)` setter consistent with the same-named methods now on `BatchSealer`, `DepositProcessor`, `WithdrawalProcessor`, and the `L2DAPlugin` decorator-based path.
- **1 new test** verifies the pending list survives a mid-flight sink swap.

Cumulative: 335 tests / 27 projects. The "in-place metric swap on long-lived stateful components" pattern is now uniform across every plugin / library that holds state between metric emissions.

### Fixed — `L2BatchPlugin.WithMetrics` preserves sealer state

- Same shape as the `L2BridgePlugin` fix in iter 70: calling `WithMetrics` set `_sealer = null`, which silently dropped the sealer's `_nextBatchNumber`, `_lastPostStateRoot`, and any in-progress builder. A mid-flight rewire would have caused the next batch to be numbered 1 again — colliding with whatever the chain had already submitted.
- New `BatchSealer.WithMetrics(IL2Metrics)` setter swaps the sink in-place. Plugin's `WithMetrics` calls it instead of nulling.
- **1 new regression test**: post-rewire batch is numbered 2, not 1; old sink keeps batch-1 counter, new sink gets batch-2 counter.

Cumulative: 334 tests / 27 projects.

### Fixed — `L2BridgePlugin.WithMetrics` preserves processor state

- Calling `WithMetrics` on the bridge plugin used to re-construct `DepositProcessor` and `WithdrawalProcessor`, dropping their consumed-nonce dedup sets and the in-progress withdrawal tree. A replay deposit submitted after a re-wire would silently slip through; an in-progress withdrawal batch would lose accumulated leaves. Now `WithMetrics` swaps the sink in-place via new `Processor.WithMetrics(IL2Metrics)` setters, preserving every piece of state.
- **2 new tests**: replay-deposit-after-rewire still rejects + emits to the new sink; same for withdrawal duplicate-nonce.

Cumulative: 333 tests / 27 projects.

### Fixed — `/readyz` returns 503 when the readiness predicate throws

- A readiness predicate that threw (e.g. checking a missing dependency) propagated the exception out of `Handle`, dropped the connection, and gave the scraper a TCP-level error rather than a clean 503. Predicate is wrapped in try/catch now; any throw produces 503 with body `predicate threw\n`. Operators chase the underlying exception in their logs, not the HTTP response.
- **1 new test** asserts a throwing predicate produces 503.

Cumulative: 331 tests / 27 projects.

### Fixed — `MetricsHttpServer` slow-client deadline

- `NetworkStream.ReadTimeout` doesn't apply to async reads, so a client that connected but never sent a request line could pin a worker thread indefinitely (slow-loris-style). Each connection now runs under a `CancellationTokenSource` linked to a 5-second deadline + the server's shutdown token. Both the read and write paths receive the linked token so they cancel together.
- **1 new test** opens a slow-client TCP connection and never sends; verifies the server stays responsive to a parallel real scrape.

Cumulative: 330 tests / 27 projects.

### Fixed — `L2MetricsPlugin.Start` is now thread-safe

- `Start` was idempotent under serial calls but had a race window between the `_server is null` check and the assignment. Two threads calling `Start` concurrently could both observe `_server == null` and bind two servers, leaking one. Now guarded by a `Lock _startGate` so only the first call binds.
- **1 new test** spawns 8 threads that all call `Start(portOverride: 0)` past a `Barrier`; asserts only one server is bound and the port stays stable across a follow-up `Start` call.

Cumulative: 329 tests / 27 projects.

### Docs — CONTRIBUTING.md test count + new wire-format guidance

- Updated stale "162 tests" → 328 in the Quick Start.
- Added a new "Adding a new wire format" section that codifies the byte-layout-table-in-docs + byte-layout-test pattern established by iter 47 / 58–60. Closes the contributor onboarding gap where someone introducing a new canonical encoding wouldn't know the convention.

### Fixed — `PrometheusExporter` escapes label values per spec

- Label values containing `"`, `\`, or newline were emitted verbatim, producing malformed Prometheus exposition that would break a real scraper. No current `MetricNames` emit those characters in tags, so this is defensive — but if anyone ever adds e.g. a `("user_agent", request.UserAgent)` tag, the exporter is now safe.
- Escapes per the Prometheus exposition format: `\` → `\\`, `"` → `\"`, newline → `\n`.
- **3 new tests** cover each escape case + the regression where a raw newline in a label used to make `line2` look like the start of a new metric line to the parser.

Cumulative: 328 tests / 27 projects.

### Fixed — `L2MetricsPlugin` accepts hostnames in BindAddress (e.g. `localhost`)

- `Start` previously crashed if `BindAddress` wasn't a numeric IP — `IPAddress.TryParse` rejects hostnames. A reasonable operator config like `"BindAddress": "localhost"` would throw `InvalidOperationException` at startup. Now falls back to `Dns.GetHostAddresses` for hostnames; raises a clear error only if DNS resolution fails or returns zero addresses.
- New public helper `L2MetricsPlugin.ResolveBindAddress(string)` so the resolution logic is directly testable + reusable.
- **5 new tests**: numeric IPv4, IPv6, any-address (`0.0.0.0`), `localhost` regression, bogus hostname → throw.

Cumulative: 325 tests / 27 projects.

### Fixed — `L2MetricsPlugin.Start(portOverride)` removes test fragility

- Tests for `L2MetricsPlugin` and `UT_E2E_L2MetricsPlugin_CompositionRoot` previously fell back to `Assert.Inconclusive` when port 9090 was in use on the test machine — fragile on shared CI runners. `Start` now accepts an optional `portOverride` parameter (`0` = "any free port") so tests bind deterministically. Production callers leave it null and let the JSON config drive.
- Removed 4 `Assert.Inconclusive` paths across 5 tests; all now run unconditionally.

Cumulative: 320 tests / 27 projects (no count change; tests are just now reliable).

### Docs — Refresh README counts to match current state

- README's "What ships" section was stale at 194 tests / 19 test projects / 11 off-chain libs / 7 plugins. Now: 320 tests / 21 test projects / 15 off-chain libs / 8 plugins, with the new bullets calling out the production-grade telemetry stack (Prometheus + `/metrics` + `/healthz` + `/readyz`) and that every canonical wire format has a byte-layout test.
- Quick-start gets a `--metrics-port 9090` example for the live HTTP scrape.

### Docs — Walk #4 covers telemetry in `docs/architecture-walkthrough.md`

- Added a "Walk #4: telemetry — emit, snapshot, scrape" section with an ASCII diagram of the metrics pipeline + the catalog of every component → metric family mapping. Cross-references `docs/telemetry.md` for the operator detail.
- Added a "Cross-cutting / Telemetry" row to the doc.md→code mapping table so a contributor scanning the table for the observability path finds it.
- No code changes; 320 tests / 27 projects.

### Added — `BatchSerializer` byte layouts in XML docs + tests

- `BatchSerializer`'s XML doc now includes full offset tables for both `L2BatchCommitment` (321 + proofLen bytes) and `PublicInputs` (332 bytes). This is THE format `NeoHub.SettlementManager` reads on-chain — having the layout in the doc means a contract author parsing the bytes doesn't need to read the encoder source.
- **2 new tests** in `Neo.L2.Batch.UnitTests` pin every documented offset.

Cumulative: 320 tests / 27 projects.

### Added — Documented byte layouts for `OptimisticProofPayload` + `RiscVProofPayload`

- Both payload types previously had `<remarks>See doc.md §X</remarks>` but no actual byte layout written down. A contract author parsing them off the wire had to read the source to know offsets. Layouts now spelled out as offset/size tables matching the format used in other canonical encoders.
- **2 new tests** in `Neo.L2.Proving.UnitTests` pin every byte range so future encoder reorders fail the build.

Cumulative: 318 tests / 27 projects.

### Fixed — `FraudProofPayload` doc-comment layout matches the encoder

- The XML doc-comment listed fields in a different order than `Encode` actually produced. Real layout (101 bytes, all little-endian): version (1B) + preStateRoot (32B) + claimedPostStateRoot (32B) + replayedPostStateRoot (32B) + disputedTxIndex (uint32, 4B). Doc updated and a new byte-layout test pins the offsets so future reorders fail the build.
- **1 new test** in `Neo.L2.Challenge.UnitTests` asserts each byte range matches the documented offsets.

Cumulative: 316 tests / 27 projects.

### Added — `ChallengeOrchestrator.InspectWithBisectionAsync`

- New overload that takes per-tx checkpoint sequences from both parties, runs `BisectionGame` internally, and emits a `FraudProofPayload` with `DisputedTxIndex` set to the single narrowed tx index. Pulls the bisection step inside the orchestrator so the caller doesn't have to wire it manually.
- Returns `null` when checkpoints agree at the final index (no fraud). Otherwise emits `l2.challenge.fraud_proofs` (counter) and `l2.challenge.bisection_rounds` (histogram via `BisectionGame`).
- **3 new tests**: agreement returns null, log-N narrowing produces the right disputed index region, arg validation matches `InspectAsync`.

Cumulative: 315 tests / 27 projects.

### Added — `ChainAuditor` self-emits audit metrics

- **`ChainAuditor`** accepts an optional `IL2Metrics` constructor parameter and emits `l2.audit.runs` (counter, +1 per `AuditAsync` call) and `l2.audit.failures` (counter, delta = number of failed findings — not 1 per failed audit) automatically. Devnet's manual emission of these metrics is removed; the auditor handles it now.
- **`NoZeroProofCheck`** registered in the devnet's auditor pipeline alongside `Continuity` + `ProofValidity`.
- **4 new tests** in `Neo.L2.Audit.UnitTests`: passing-audit increments runs only, failing-audit increments runs + failures by failed-finding count, repeated audits accumulate, NoOp default safety.

Cumulative: 312 tests / 27 projects.

### Added — `L2Outbox` messaging telemetry

- **`L2Outbox`** emits `l2.messaging.emitted` (counter) on every `Add`. Optional `IL2Metrics` constructor param. The metric was declared in iter 33's `MetricNames` but never emitted by any component.
- **2 new tests** in `Neo.L2.Messaging.UnitTests`: counter increments across L1 + L2 destinations, NoOp default safety.

Cumulative: 308 tests / 27 projects.

### Added — Sequencer registry telemetry

- **`InMemorySequencerCommitteeProvider`** emits `l2.sequencer.registered` (counter) on Register, `l2.sequencer.exits_started` (counter) on BeginExit, `l2.sequencer.exits_finalized` (counter) on Finalize, and `l2.sequencer.committee_size` (gauge) on every Register / Finalize. Optional `IL2Metrics` constructor param. Lets operators alert on unexpected committee shrinkage or rapid churn.
- **4 new `MetricNames`** constants + matching catalog entries.
- **4 new tests**: counter+gauge on Register, exits_started on BeginExit (size unchanged), exits_finalized + size decremented on Finalize, NoOp default safety.

Cumulative: 306 tests / 27 projects.

### Added — Forced-inclusion / censorship / challenge telemetry

The four `MetricNames` constants for these subsystems were declared in iter 33 but not actually emitted. Closing that gap:

- **`InMemoryForcedInclusionSource`** emits `l2.forced_inclusion.observed` on every `Enqueue`. Optional `IL2Metrics` constructor param.
- **`CensorshipDetector`** emits `l2.censorship.reports` (incremented by report count) when `DetectOverdueAsync` returns a non-empty list. Optional `IL2Metrics` constructor param.
- **`ChallengeOrchestrator`** emits `l2.challenge.fraud_proofs` when `InspectAsync` returns a non-null payload. Optional `IL2Metrics` constructor param.
- **`BisectionGame`** records `l2.challenge.bisection_rounds` (histogram) when the game settles, value = number of rounds taken. Optional `IL2Metrics` constructor param.
- **6 new tests** across the three lib test projects.

Cumulative: 302 tests / 27 projects. Every metric in `MetricCatalog` now has at least one emitter in source.

### Added — Misc polish

- **`Neo.Plugins.L2Metrics/config.json`** — config template so operators can drop the plugin into a Neo node and have it work. Mirrors the file shape every other L2 plugin uses (`PluginConfiguration` block).
- **`MerkleProof.Verify(root)`** — instance-method convenience; delegates to the existing `MerkleTree.Verify(proof, root)`. Lets call sites read `proof.Verify(root)` without the static dispatch boilerplate.

Cumulative: 296 tests / 27 projects.

### Added — Composition-root integration test

- **`UT_E2E_L2MetricsPlugin_CompositionRoot`** — wires every instrumented component (`BatchSealer`, `MetricsEmittingDAWriter`, `DepositProcessor`, `WithdrawalProcessor`, `BinaryTreeAggregator`, `L2RpcMethods`) to one shared sink hosted by `L2MetricsPlugin`, drives activity, scrapes `/metrics` through the plugin's HTTP server, and asserts every component's metric family is present in the response. Locks in that the composition root in `docs/telemetry.md` actually works end-to-end as advertised.

Cumulative: 295 tests / 27 projects.

### Added — `Neo.Plugins.L2Metrics` composition root

- New plugin **`L2MetricsPlugin`** owns the shared `InMemoryMetrics` sink the rest of the L2 plugin set wires its `WithMetrics()` calls to, and stands up the `MetricsHttpServer` based on settings (BindAddress, Port, Enabled). Pulls everything together — operators register this plugin first, then call `plugin.Metrics` from each other plugin's `WithMetrics()`.
- **Optional readiness predicate** via `WithReadinessCheck(Func<bool>)` — gates `/readyz` 200 vs 503.
- **Idempotent `Start()`** — extra calls are no-ops, simplifying host startup.
- **`L2MetricsSettings`** — `Enabled` (kill switch), `BindAddress` (default `127.0.0.1`), `Port` (default 9090, use 0 for any free port). Loaded from the standard plugin `config.json` `PluginConfiguration` section.
- **7 new tests** in `Neo.Plugins.L2Metrics.UnitTests`: bound-port-zero-before-Start, default-settings, idempotent Start, real HTTP scrape with emitted counter, readiness predicate gating 200 ↔ 503, null-arg validation.

Cumulative: 294 tests / 27 projects.

### Added — Devnet `--metrics-port` flag (live HTTP demo)

- `neo-l2-devnet <N> --metrics-port <P>` (or `--metrics-port 0` for "any free port") now stands up a real `MetricsHttpServer` after the batch run, self-scrapes `/metrics`, `/healthz`, and `/readyz` over real HTTP, and prints the round-trip status + content-type + body summary. Promotes the previously static "Prometheus text format" devnet section to a live demonstration of the production scrape path.

Cumulative: 287 tests / 26 projects (no new tests; the e2e telemetry integration test already covers this code path).

### Added — `NoZeroProofCheck` audit

- New `IAuditCheck` implementation flags batches that were soft-sealed but never had a real proof attached: `ProofType.None`, or non-`None` discriminator paired with empty `Proof` bytes. Cheap and fast — does not re-verify the proof (that's `ProofValidityCheck`'s job), it just catches the "soft-sealed but never proved" failure mode that would otherwise need full verification cost to detect.
- 5 new tests in `Neo.L2.Audit.UnitTests`: all-proved happy path, `ProofType.None`, empty proof bytes, multiple failures all reported, empty batch list.

Cumulative: 287 tests / 26 projects.

### Added — Canonical `MerkleProof` wire format

- **`MerkleProofSerializer`** (`Neo.L2.State`) — fixed-layout encoding of `MerkleProof` consumed by L1 NeoHub.SharedBridge for withdrawal verification. Closes a real gap: prior to this, off-chain code could `MerkleTree.GetProof` + `Verify`, but there was no canonical byte format for sending a proof across the off-chain ↔ on-chain boundary.
- Layout (48 + 32 × siblingCount bytes, all little-endian):
  - 0 .. 32  — Leaf hash
  - 32 .. 36 — LeafIndex (uint32)
  - 36 .. 44 — PathBitmap (uint64)
  - 44 .. 48 — SiblingCount (uint32)
  - 48 ..    — Siblings, 32 bytes each, leaf-to-root order
- **`MaxDepth = 64`** matches `MerkleTree.Verify`'s existing depth limit.
- **10 new tests**: round-trip 4-leaf, depth-0 single-leaf (header-only), exact byte-layout assertion, truncated-header rejection, truncated-siblings rejection, extra-trailing-bytes rejection, oversized-depth-on-encode rejection, header-claims-too-many-siblings rejection, null-arg, all-positions in 7-leaf tree round-trip.
- Listed in `AGENTS.md` "Canonical encodings" so future contributors don't reinvent.

Cumulative: 282 tests / 26 projects.

### Added — `/healthz` + `/readyz` endpoints

- **`MetricsRequestHandler`** now answers `/healthz` (always 200) and `/readyz` (200 or 503 based on optional predicate) in addition to `/metrics`. Standard Kubernetes-style liveness / readiness probes for load-balancer integration without bringing in an additional HTTP framework.
- **`MetricsRequestHandler` constructor** gets an optional `Func<bool>? readinessCheck` parameter. When unwired, `/readyz` always returns 200; when wired, the predicate is evaluated on every scrape.
- **6 new tests** covering: `/healthz` always-200, `/readyz` no-predicate-200, predicate-true-200, predicate-false-503, predicate evaluated per-request, trailing-slash + query-string tolerance on `/healthz`.
- `docs/telemetry.md` gets an "Endpoints" table and a `/readyz` predicate example.

Cumulative: 272 tests / 26 projects.

### Added — Gateway aggregation telemetry

- **`BinaryTreeAggregator`** accepts an optional `IL2Metrics` constructor parameter (default `NoOpMetrics`). On every successful `Aggregate()` it emits `l2.gateway.aggregations` (counter), `l2.gateway.batches_aggregated` (counter, +N constituents), `l2.gateway.aggregation_rounds` (histogram = tree depth), and `l2.gateway.aggregation_latency_ms` (histogram). Empty-pending case emits nothing.
- **4 new `MetricNames`** + matching catalog entries.
- **5 new tests** in `Neo.Plugins.L2Gateway.UnitTests`: 4-batch case verifies rounds = log2(4) = 2; 1-batch case verifies rounds = 0; empty case verifies no emission; repeated aggregations verify accumulation; default NoOp safety. **Last plugin without telemetry is now wired.**

Cumulative: 266 tests / 26 projects. Telemetry coverage matrix complete.

### Added — RPC telemetry

- **`L2RpcMethods`** wraps each of its 9 RPC methods through a private `Time` helper that emits `l2.rpc.calls` (counter) + `l2.rpc.latency_ms` (histogram) on success and `l2.rpc.failures` (counter) on exception, all tagged by `method` name (e.g. `getl2stateroot`, `getl2batch`). Optional `IL2Metrics` constructor parameter; default `NoOpMetrics`.
- **3 new `MetricNames`**: `RpcCalls`, `RpcLatencyMs`, `RpcFailures` + matching catalog entries.
- **4 new tests** in `Neo.Plugins.L2Rpc.UnitTests`: per-method tag isolation, repeated-call accumulation, foreign-chain rejection ↑ failure counter, no-metrics-default safety.

Cumulative: 261 tests / 26 projects.

### Added — Bridge processor telemetry

- **`DepositProcessor`** + **`WithdrawalProcessor`** now accept an optional `IL2Metrics` constructor parameter and emit:
  - `l2.bridge.deposits` (counter) on successful `Process`, `l2.bridge.deposits_rejected` on validation failure (replay, unknown asset, inactive mapping).
  - `l2.bridge.withdrawals` (counter) on successful `Stage`, `l2.bridge.withdrawals_rejected` on validation failure (unknown asset, duplicate nonce, non-positive amount).
- **`L2BridgePlugin.WithMetrics(IL2Metrics)`** — re-creates the processors with the new sink. Default is `NoOpMetrics`.
- **2 new `MetricNames`**: `DepositsRejected`, `WithdrawalsRejected` + matching catalog entries.
- **7 new tests** in `Neo.L2.Bridge.UnitTests`: success path, replay, unknown asset, withdrawal success, duplicate nonce, negative amount, default-NoOp safety. Closes the gap where bridge counters were only emitted manually in the devnet's inline path; production plugin path now emits them too.

Cumulative: 257 tests / 26 projects.

### Added — `MetricCatalog` (operator-facing HELP strings)

- **`MetricCatalog`** — single source of truth for the operator-facing description of every canonical metric. `GetHelp(name)` returns a sentence-form description; `IsKnown(name)` answers whether the catalog has an entry. `Descriptions` exposes the full map.
- **`PrometheusExporter`** now consults `MetricCatalog.GetHelp(baseName)` so HELP lines read e.g. `# HELP l2_batch_sealed_total Number of L2 batches sealed by the local sequencer` instead of the previous generic `L2 telemetry counter (l2.batch.sealed)`.
- **6 new tests** in `Neo.L2.Telemetry.UnitTests`: catalog-completeness check (reflects over `MetricNames` constants and asserts every one has an entry — guards against future drift), unknown-name fallback, expected-description spot-checks, null-arg validation, no-trailing-period convention check, end-to-end exporter integration.

Cumulative: 250 tests / 26 projects.

### Added — End-to-end telemetry integration test

- **`UT_E2E_Telemetry_Pipeline`** — drives the full telemetry pipeline (single shared `InMemoryMetrics` + `BatchSealer` + `MetricsEmittingDAWriter` + synthesized settlement/proving/bridge counters), stands up a `MetricsHttpServer` on a free port, scrapes `/metrics` over real HTTP, and asserts on the resulting Prometheus exposition. Covers both success path (4 batches → counters at 4) and failure path (DA write throws → `l2_da_publish_failures_total` incremented, success counter absent).
- Locks the metric contract end-to-end: every metric the production stack emits has a regression test.

Cumulative: 244 tests / 26 projects.

### Added — `/metrics` HTTP endpoint

- **`MetricsRequestHandler`** — framework-agnostic pure handler. Takes a request path, returns `MetricsHttpResponse` (status / content-type / body). Routes <c>/metrics</c> (with tolerance for trailing slash and query string) to a fresh `PrometheusExporter.Format(snapshot)`; everything else returns 404. Drop into any HTTP host (ASP.NET, Kestrel, RpcServer plugin) by routing GET <c>/metrics</c> through `Handle()`.
- **`IMetricsSource`** — read-side companion to `IL2Metrics`. `InMemoryMetrics` implements both. Decouples the snapshot read from the exporter so future sources (e.g. an OpenTelemetry-backed cache) plug in cleanly.
- **`MetricsHttpServer`** — minimal in-process HTTP server. Uses `TcpListener` + raw HTTP/1.0 framing instead of `HttpListener` (which is unreliable on Linux). Binds to a configurable IP/port (use port 0 for "any free port"; the resolved port is exposed via `Endpoint`). No third-party deps.
- **13 new tests**: 8 handler tests (status routing, query/trailing-slash tolerance, fresh-snapshot-per-call, null-arg validation, `IMetricsSource` round-trip), 5 server tests (real HTTP scrape, 404 on bad path, sequential requests, null-arg, port-zero binding). HttpClient explicitly bypasses ambient proxy env vars.

Cumulative: 242 tests / 26 projects.

### Added — Prometheus exporter

- **`MetricsSnapshot`** — frozen point-in-time read of every counter / gauge / histogram. Decouples accumulation (`IL2Metrics`) from export so future exporters (OpenTelemetry, StatsD, …) reuse the same shape.
- **`InMemoryMetrics.Snapshot()`** — produces a `MetricsSnapshot` immune to subsequent emissions.
- **`PrometheusExporter.Format(snapshot)`** — emits standards-compliant Prometheus exposition text: counters get `_total` suffix and `counter` type, gauges stay as-is with `gauge` type, histograms produce `_count` + `_sum` + `_max` aggregates with `summary` type. Tag pairs become labels with proper quoting; `.` and `-` in metric names sanitize to `_` per Prometheus naming rules. HELP/TYPE preambles emitted once per metric family.
- **`PrometheusExporter.ContentType`** — the canonical `text/plain; version=0.0.4; charset=utf-8` HTTP header value.
- **Devnet** now prints a `───── /metrics (Prometheus text format) ─────` section after each run, demonstrating the same data viewed through both the human summary and the production exporter.
- **10 new tests** in `Neo.L2.Telemetry.UnitTests`: empty-snapshot, counter, tagged counter, gauge, histogram, mixed kinds, content-type constant, name sanitization with dots+dashes, tagged histogram, snapshot frozenness.

Cumulative: 229 tests / 26 projects.

### Added — DA telemetry decorator

- **`MetricsEmittingDAWriter`** (`Neo.Plugins.L2DA`) — composition-pattern decorator that wraps any `IDAWriter` and emits `l2.da.published` (counter), `l2.da.publish_latency_ms` (histogram), and `l2.da.publish_failures` (counter), all tagged by `mode`. New writers automatically participate in the metric contract by being wrapped at plugin configure time.
- **`L2DAPlugin.WithMetrics(IL2Metrics)`** — wraps the chosen raw writer in the decorator. Idempotent: re-wiring a different metrics sink unwraps and re-wraps. NoOp metrics (the default) skip wrapping entirely.
- **6 new tests** in `Neo.Plugins.L2DA.UnitTests`: success path, propagating-throw failure path, accumulation across multiple publishes, `IsAvailableAsync` pass-through, null-arg validation, mode mirroring.
- **2 new `MetricNames`** entries: `DAPublishLatencyMs`, `DAPublishFailures`.

Cumulative: 219 tests / 26 projects.

### Refactor — extract testable `BatchSealer`

- **`BatchSealer`** (`Neo.Plugins.L2Batch`) — pure batch-accumulation state machine extracted from `L2BatchPlugin`. Owns `BatchBuilder` lifecycle, the three seal triggers (block-count, tx-count, age), block-context construction, and metric emission. Takes an injectable `Func<long>` clock so age-based seal can be exercised without sleeping. The plugin shrinks to ~70 lines whose only job is forwarding `Blockchain.Committed` to the sealer.
- **`Neo.Plugins.L2Batch.UnitTests`** — 7 unit tests that drive `BatchSealer` directly: seal-on-block-count, seal-on-tx-count, seal-on-age (with fake clock), batch-number monotonicity across seals, builder reset post-seal, NoOp metrics default safety, gauge-replace semantics for `l2.batch.tx_count`. Locks down the sealer's contract so future plugin refactors can't silently break the seal triggers.

Cumulative: 213 tests / 26 projects.

### Added — Plugin telemetry wiring

- **`L2BatchPlugin.WithMetrics(IL2Metrics)`** — emits `l2.batch.sealed` (counter), `l2.batch.seal_latency_ms` (histogram), and `l2.batch.tx_count` (gauge) on every seal. Default sink is `NoOpMetrics.Instance` so the metric path is opt-in.
- **`L2SettlementPlugin.WithMetrics(IL2Metrics)`** — emits `l2.proving.generated{kind=…}` (counter), `l2.proving.latency_ms{kind=…}` (histogram), `l2.settlement.submitted` (counter), `l2.settlement.submit_latency_ms` (histogram), and `l2.settlement.submit_failures` (counter on exception). Failed submits re-queue at the head, exactly matching the prior retry semantics.
- **`L2SettlementPlugin.Enqueue(L2BatchCommitment)`** — public hot-path entry that's also useful for backfilling missed batches after a node restart. Replaces the previously private event handler.
- **`Neo.Plugins.L2Settlement.UnitTests`** — 4 new unit tests exercising submit-success / submit-failure / default-NoOp paths with mocked `IL2Prover` + `ISettlementClient` + `InMemoryMetrics`. First plugin-level test project that drives an actual Plugin subclass.

Cumulative: 206 tests / 25 projects.

### Added — Auditor + devnet v0.3

- **`Neo.L2.Audit`** — chain auditor: pluggable `IAuditCheck`, built-in `ContinuityCheck` (sequential batch numbers + state-root linking + non-overlapping block ranges) and `ProofValidityCheck` (re-runs each batch's proof through `VerifierRegistry`). `ChainAuditor` aggregates findings into `AuditReport` with human-readable `Summarize()`. 9 unit tests.
- **Devnet v0.3** — after the per-batch loop, runs the full `ChainAuditor` pass (continuity + proof validity) and prints the `AuditReport.Summarize()` output. The devnet is now a complete end-to-end demonstration: state-root continuity, real multisig proofs, balance arithmetic, and an explicit auditor pass.
- **`UT_Mvp_AllPhases_FullStack`** integration test — single readable scenario that runs Phase-1 deploy planner → Phase-0/2 batch lifecycle with state continuity → Phase-3 BisectionGame → Phase-5 Gateway aggregation, all in one test.

Cumulative: 194 tests / 23 projects.

### Added — Phase 3 completion (optimistic challenge window + bisection)

- **`NeoHub.OptimisticChallenge`** L1 contract — accepts fraud proofs against pending `Challengeable` batches; on accepted challenge, reads sequencer's full bond, splits per `ChallengerRewardBps` (default 50%), pays challenger via `SequencerBond.Slash`, treasures the rest, and calls `SettlementManager.RevertBatch`. `FinalizeIfPastWindow` for unchallenged batches. Owner-gated `SetWindowSeconds` (60s..7d).
- **`Neo.L2.Challenge.FraudProofPayload`** — 101-byte canonical wire format (1B version + 32B preStateRoot + 32B claimedPostStateRoot + 32B replayedPostStateRoot + 4B disputedTxIndex).
- **`Neo.L2.Challenge.ChallengeOrchestrator`** — pluggable `IFraudProofGenerator`; `InspectAsync` takes claimedCommitment + reconstructed inputs and emits a `FraudProofPayload` only when challenger's deterministic replay disagrees with the sequencer's claim.
- **`Neo.L2.Challenge.BisectionGame`** — pure state machine that converges challenger and sequencer to a single disputed tx index in `O(log N)` rounds (standard optimistic-rollup design). Makes on-chain fraud verification O(1) instead of O(N).
- **Phase-3 end-to-end integration test** (`UT_Mvp_Phase3_OptimisticChallenge`) demonstrates: 8-tx batch with `KeyedStateStore`-backed honest checkpoints → sequencer lies from index 5 → `ChallengeOrchestrator` detects → `BisectionGame` narrows to disputed tx index 4 in ≤ 3 rounds.

Phase 3 → ✅. Cumulative: 184 tests / 21 projects.

### Added — Phase 2 / 3 / 5 wave (real state, slashing, recursive aggregation)

- **`KeyedStateStore` + `KeyedStateRootOracle`** (`Neo.L2.Executor.State`) — replace the XOR-mix stub with a sorted-leaf Merkle root computed over `Hash256(4B keyLen || key || 4B valueLen || value)` leaves; deterministic + insert-order-independent. The devnet now runs with real state-root continuity (`postRoot[N] == preRoot[N+1]`).
- **`NeoHub.SequencerBond` + `NeoHub.SequencerRegistry`** — slashable bond escrow (`Deposit` / `Slash` / `Withdraw`) and per-chain dBFT pubkey registry (`Register` / `Unregister` / `Finalize` with exit window). `Register` gates on `SequencerBond.HasMinBond` via inter-contract call.
- **`Neo.L2.Sequencer`** — L2-side `ISequencerCommitteeProvider` + `InMemorySequencerCommitteeProvider`. Models the L1 contract semantics so L2 nodes can test their committee-aware code paths.
- **`Neo.L2.Censorship`** — off-chain `CensorshipDetector` that polls `IForcedInclusionSource` for overdue entries, uses `ISequencerCommitteeProvider` to identify the responsible signer, and emits `CensorshipReport[]` ready for `NeoHub.ForcedInclusion.ReportCensorship` + `NeoHub.SequencerBond.Slash`.
- **`BinaryTreeAggregator` + `IRoundProver`** (`Neo.Plugins.L2Gateway`) — log(N)-round pairwise reduction with pluggable round prover. Default `PassThroughRoundProver` (Hash256 + length-prefixed proof concat). Production swaps for SP1 Compress / Halo2 accumulator / Risc0 fold.
- **`NeoFsLikeDAWriter`** — content-addressed in-process DA writer with NeoFS object semantics (object id = SHA256(payload), per-chain container, 36-byte pointer = 4B chainId LE + 32B objectId).
- **`Neo.L2.Devnet` v0.2** — upgraded to use `KeyedStateRootOracle` and 3-member `InMemorySequencerCommitteeProvider`; verified end-to-end across 5 batches with Alice's balance arithmetic check.
- **Phase-2 full-stack integration test** (`UT_Mvp_Phase2_FullStack`) stitches `KeyedStateStore` continuity + sequencer committee + forced-inclusion + censorship detection + multi-chain `BinaryTreeAggregator` together.

Cumulative: 162 tests / 19 projects, all green.

### Added — Phase 0 / 1 / 2 substantial scaffolding

- **Off-chain libraries** (`src/Neo.L2.*`): `Abstractions`, `Batch`, `State`, `Bridge`, `Messaging`, `Proving`, `Executor`. 7 interfaces, 14 model records, deterministic batch executor + spec, Merkle tree matching Neo's `Hash256` convention, Stage 0 multisig prover (production-usable) + verifier, Stage 1 optimistic verifier, Stage 2 RISC-V mock backend.
- **neo-node plugins** (`src/Neo.Plugins.L2*`): `L2Batch`, `L2Settlement`, `L2Bridge`, `L2DA`, `L2Prover`, `L2Rpc`, `L2Gateway`. All extend `Neo.Plugins.Plugin` and type-check against the locally-vendored `neo-project/neo` master branch.
- **Smart contracts** (`contracts/`): 9 NeoHub L1 contracts (`ChainRegistry`, `SharedBridge`, `SettlementManager`, `VerifierRegistry`, `MessageRouter`, `TokenRegistry`, `DARegistry`, `GovernanceController`, `EmergencyManager`) and 6 L2 native contracts (`L2BridgeContract`, `L2MessageContract`, `L2BatchInfoContract`, `L2FeeContract`, `L2PaymasterContract`, `L2SystemConfigContract`). Compile via `Neo.SmartContract.Framework`.
- **Tooling** (`tools/`): `neo-stack` CLI (8 subcommands) and `neo-l2-devnet` runnable Phase 0 demo.
- **Tests**: 88 unit tests across 10 projects (incl. 1 MVP integration test that walks deposit → batch → prove → verify → withdraw end-to-end in-process).
- **Documentation**: `README.md`, `ARCHITECTURE.md` (English distillation of `doc.md`), `AGENTS.md` (agent guide), `IMPLEMENTATION_STATUS.md` (per-phase coverage matrix), `CHANGELOG.md` (this file). Each L2 module's interfaces are XML-doc'd so IDE tooltips trace back to `doc.md` section numbers.

### Architecture decisions locked in

- **Plugin-based extension over fork**: neo4 references `neo-project/neo` master via `ProjectReference` (offline-friendly). Every L2 capability is a separate `Plugin` subclass loaded at runtime, preserving upstream compatibility.
- **Deterministic batch executor as the proving boundary**: `SPEC.md` enumerates excluded surfaces (P2P, RPC, mempool, plugins, logging, wallet, on-disk DB) so the prover only commits to pure state-transition behavior.
- **Pluggable verifier registry**: `VerifierRegistry` dispatches by `ProofType`, mirroring NeoHub's L1 contract — the same wire-format moves from off-chain to on-chain unchanged.
- **Canonical encodings**: `L2BatchCommitment`, `PublicInputs`, `MessageHasher`, `DepositPayload` all serialize little-endian with deterministic byte layouts; the same encoding is what NeoHub's contracts decode.

### Added — Phase 1 / 4 acceleration

- **`NeoHub.ForcedInclusion` + `Neo.L2.ForcedInclusion`** (doc.md §15.4): anti-censorship primitive — L1 enqueue, L2 drain with deadline tracking, replay protection. 8 unit tests.
- **`Neo.L2.Settlement.Rpc`**: JSON-RPC 2.0 client over `HttpClient` + `Neo.Json` (no third-party deps). `RpcSettlementClient` implements `ISettlementClient` for read-only methods; submit-batch delegates to a caller-supplied signer. 6 unit tests with in-memory `HttpMessageHandler` mocks.
- **`Neo.Hub.Deploy`** (`neo-hub-deploy` CLI): declarative deploy planner with topological sort, `$step:<name>` placeholder resolution, cycle/unknown-dep detection, canonical 10-step NeoHub scaffold. 8 unit tests.
- **`bridge/neo-zkvm-bridge`** (Rust cdylib) + **`Neo.L2.Proving.Sp1`** (C#): Phase-4 SP1 FFI scaffold. Stable 4-symbol C ABI; default features = NOT_IMPLEMENTED so the C# side falls back to `MockRiscVProver`; `--features real-prover` links the actual `neo-zkvm-prover` crate. 6 unit tests.
- **Phase-1 cross-component integration test** that walks: deploy-planner topological resolve → forced-inclusion enqueue/drain → SP1 fallback prover → multi-chain Gateway aggregation. 5 new tests.

### Out of MVP scope (still deferred)

- **Live L1 signer for `RpcSettlementClient.SubmitBatchAsync`** — interface in place; concrete wallet integration is operator-specific.
- **One-shot deploy runner** — `Neo.Hub.Deploy` emits the bundle JSON; the consumer (signer + chain bookkeeper) lives outside this repo.
- **`nccs` artifact generation** — `Directory.Build.props` calls `nccs` with `ContinueOnError=true`; users install nccs separately.
- **RpcServer plugin integration partial** that registers `L2RpcMethods` as `[RpcMethod]`-attributed entry points (needs neo's RpcServer plugin source).
- **Real SP1 prover linkage** — flip `--features real-prover` on the bridge crate to enable.
- **Phase 5 recursive proof aggregation** — `PassThroughAggregator` is a non-ZK reference impl.
- **Forced-inclusion bond/slashing** — contract emits the report event; actual sequencer slashing depends on `SettlementManager` integration.
- **NeoFS DA writer** — stub class throws; production wires NeoFS client.
- **dBFT sequencer-committee selection per Neo Elastic** (doc.md §7.1) — defaults to neo's existing `DBFTPlugin` consensus.
