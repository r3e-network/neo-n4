using Neo.Json;

namespace Neo.Plugins.L2Rpc.UnitTests;

[TestClass]
public class UT_L2RpcMethods
{
    private static L2BatchCommitment SampleBatch(ulong batchNumber) => new()
    {
        ChainId = 1001,
        BatchNumber = batchNumber,
        FirstBlock = 100, LastBlock = 200,
        PreStateRoot = UInt256.Parse("0x" + new string('1', 64)),
        PostStateRoot = UInt256.Parse("0x" + new string('2', 64)),
        TxRoot = UInt256.Parse("0x" + new string('3', 64)),
        ReceiptRoot = UInt256.Parse("0x" + new string('4', 64)),
        WithdrawalRoot = UInt256.Parse("0x" + new string('5', 64)),
        L2ToL1MessageRoot = UInt256.Parse("0x" + new string('6', 64)),
        L2ToL2MessageRoot = UInt256.Parse("0x" + new string('7', 64)),
        DACommitment = UInt256.Parse("0x" + new string('8', 64)),
        PublicInputHash = UInt256.Parse("0x" + new string('9', 64)),
        ProofType = ProofType.Multisig,
        Proof = new byte[] { 0xAA, 0xBB, 0xCC },
    };

    [TestMethod]
    public void GetL2Batch_ReturnsObject()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var batch = SampleBatch(1);
        store.AddBatch(batch, BatchStatus.Pending);
        var methods = new L2RpcMethods(store);

        var result = methods.GetL2Batch(new JArray { 1001, 1UL });
        var obj = (JObject)result!;
        Assert.AreEqual(1001U, (uint)obj["chainId"]!.AsNumber());
        Assert.AreEqual(1UL, (ulong)obj["batchNumber"]!.AsNumber());
        Assert.AreEqual((byte)ProofType.Multisig, (byte)obj["proofType"]!.AsNumber());
    }

    [TestMethod]
    public void GetL2Batch_NullForUnknown()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store);
        var result = methods.GetL2Batch(new JArray { 1001, 999UL });
        Assert.AreEqual(JToken.Null, result);
    }

    [TestMethod]
    public void GetL2BatchStatus_ReturnsCorrectName()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        store.AddBatch(SampleBatch(7), BatchStatus.Challengeable);
        var methods = new L2RpcMethods(store);

        var obj = (JObject)methods.GetL2BatchStatus(new JArray { 1001, 7UL })!;
        Assert.AreEqual((byte)BatchStatus.Challengeable, (byte)obj["status"]!.AsNumber());
        Assert.AreEqual("Challengeable", obj["statusName"]!.AsString());
    }

    [TestMethod]
    public void GetL2StateRoot_BatchSpecificAndLatest()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var b1 = SampleBatch(1);
        var b2 = SampleBatch(2) with { PostStateRoot = UInt256.Parse("0x" + new string('a', 64)) };
        store.AddBatch(b1, BatchStatus.Pending);
        store.AddBatch(b2, BatchStatus.Pending);
        store.Finalize(2);
        var methods = new L2RpcMethods(store);

        // Specific batch.
        var rootAt1 = methods.GetL2StateRoot(new JArray { 1001, 1UL })!;
        Assert.AreEqual(b1.PostStateRoot.ToString(), rootAt1.AsString());

        // Latest (only batch 2 was finalized).
        var latest = methods.GetL2StateRoot(new JArray { 1001 })!;
        Assert.AreEqual(b2.PostStateRoot.ToString(), latest.AsString());
    }

    [TestMethod]
    public void GetL2WithdrawalProof_HexEncoded()
    {
        var leaf = UInt256.Parse("0x" + new string('e', 64));
        var bytes = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        store.RecordWithdrawalProof(leaf, bytes);
        var methods = new L2RpcMethods(store);

        var result = methods.GetL2WithdrawalProof(new JArray { 1001, leaf.ToString() })!;
        Assert.AreEqual("DEADBEEF", result.AsString());
    }

    [TestMethod]
    public void GetCanonicalAndBridgedAsset_AreInverse()
    {
        var l1 = UInt160.Parse("0x" + new string('1', 40));
        var l2 = UInt160.Parse("0x" + new string('2', 40));
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        store.RegisterAsset(l1, l2);
        var methods = new L2RpcMethods(store);

        Assert.AreEqual(l1.ToString(), methods.GetCanonicalAsset(new JArray { l2.ToString() })!.AsString());
        Assert.AreEqual(l2.ToString(), methods.GetBridgedAsset(new JArray { l1.ToString() })!.AsString());
    }

    [TestMethod]
    public void GetSecurityLevel_PropagatesStoreLabel()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Validity);
        var methods = new L2RpcMethods(store);
        var obj = (JObject)methods.GetSecurityLevel(new JArray { 1001 })!;
        Assert.AreEqual((byte)SecurityLevel.Validity, (byte)obj["level"]!.AsNumber());
        Assert.AreEqual("Validity", obj["levelName"]!.AsString());
    }

    [TestMethod]
    public void GetSecurityLabel_ExposesAllFiveDimensions_FromDefaults()
    {
        // doc.md §16.2 mandates 5 security label dimensions. Without explicitly setting
        // the new init properties, callers should see the documented sane defaults
        // (matches the L2ChainConfig record's defaults + the on-chain "0" sentinel).
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store);
        var obj = (JObject)methods.GetSecurityLabel(new JArray { 1001 })!;

        Assert.AreEqual(1001U, (uint)obj["chainId"]!.AsNumber());
        Assert.AreEqual((byte)SecurityLevel.Optimistic, (byte)obj["securityLevel"]!.AsNumber());
        Assert.AreEqual("Optimistic", obj["securityLevelName"]!.AsString());
        Assert.AreEqual((byte)DAMode.External, (byte)obj["daMode"]!.AsNumber());
        Assert.AreEqual("External", obj["daModeName"]!.AsString());
        Assert.IsFalse(obj["gatewayEnabled"]!.AsBoolean());
        Assert.AreEqual((byte)SequencerModel.DbftCommittee, (byte)obj["sequencer"]!.AsNumber());
        Assert.AreEqual("DbftCommittee", obj["sequencerName"]!.AsString());
        Assert.AreEqual((byte)ExitModel.Permissionless, (byte)obj["exit"]!.AsNumber());
        Assert.AreEqual("Permissionless", obj["exitName"]!.AsString());
    }

    [TestMethod]
    public void GetSecurityLabel_ReflectsOverrides()
    {
        // A validium operator overrides DA + Exit; a centralized-sequencer operator
        // overrides Sequencer. The label RPC must surface those.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Validity)
        {
            DAMode = DAMode.NeoFS,
            GatewayEnabled = true,
            Sequencer = SequencerModel.Centralized,
            Exit = ExitModel.Delayed,
        };
        var methods = new L2RpcMethods(store);
        var obj = (JObject)methods.GetSecurityLabel(new JArray { 1001 })!;

        Assert.AreEqual((byte)DAMode.NeoFS, (byte)obj["daMode"]!.AsNumber());
        Assert.IsTrue(obj["gatewayEnabled"]!.AsBoolean());
        Assert.AreEqual((byte)SequencerModel.Centralized, (byte)obj["sequencer"]!.AsNumber());
        Assert.AreEqual("Centralized", obj["sequencerName"]!.AsString());
        Assert.AreEqual((byte)ExitModel.Delayed, (byte)obj["exit"]!.AsNumber());
    }

    [TestMethod]
    public void GetSecurityLabel_RejectsForeignChainId()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store);
        Assert.ThrowsExactly<ArgumentException>(() => methods.GetSecurityLabel(new JArray { 9999 }));
    }

    [TestMethod]
    public void RejectsForeignChainId()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store);
        Assert.ThrowsExactly<ArgumentException>(() => methods.GetL2BatchStatus(new JArray { 9999, 1UL }));
    }

    [TestMethod]
    public void RejectsTooFewParams_ClearMessage()
    {
        // Regression: previously the JArray indexer threw raw ArgumentOutOfRangeException
        // ("Index was out of range. Must be non-negative and less than the size of the
        // collection.") on a missing param. RPC clients had no clue which param was missing.
        // Now: ArgumentException("param[N] missing").
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store);

        // GetL2Batch wants chainId + batchNumber; only chainId provided.
        var ex = Assert.ThrowsExactly<ArgumentException>(() => methods.GetL2Batch(new JArray { 1001 }));
        StringAssert.Contains(ex.Message, "param[1]");
    }

    [TestMethod]
    public void RecordWithdrawalProof_TakesDefensiveCopy()
    {
        // Regression: previously the store retained the caller's byte[] reference. A
        // caller who reused a scratch buffer across many records (or mutated it after
        // passing it in) would silently corrupt the previously stored proof.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var leaf = UInt256.Parse("0x" + new string('e', 64));
        var bytes = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE };
        store.RecordWithdrawalProof(leaf, bytes);

        // Mutate the caller's array — the stored copy must NOT change.
        bytes[0] = 0x00;
        bytes[1] = 0x00;
        var stored = store.GetWithdrawalProof(leaf);
        Assert.IsNotNull(stored);
        Assert.AreEqual(0xCA, stored.Value.Span[0]);
        Assert.AreEqual(0xFE, stored.Value.Span[1]);
    }

    [TestMethod]
    public void RegisterAsset_RepointL2_RemovesOrphan()
    {
        // Regression: previously RegisterAsset wrote both indexes without cleanup. Re-
        // registering an L1 asset against a different L2 token left the prior L2 entry
        // as an orphan in _l1ByL2 — GetCanonicalAsset(oldL2) still returned the L1 asset
        // even though GetBridgedAsset(L1) now returned the new L2. Mirrors the iter-100
        // fix in AssetRegistry.
        var l1 = UInt160.Parse("0x" + new string('1', 40));
        var oldL2 = UInt160.Parse("0x" + new string('2', 40));
        var newL2 = UInt160.Parse("0x" + new string('5', 40));
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        store.RegisterAsset(l1, oldL2);
        store.RegisterAsset(l1, newL2);

        Assert.IsNull(store.GetCanonicalAsset(oldL2), "stale L2 → L1 entry must be removed");
        Assert.AreEqual(l1, store.GetCanonicalAsset(newL2));
        Assert.AreEqual(newL2, store.GetBridgedAsset(l1));
    }

    [TestMethod]
    public void Finalize_OutOfOrder_DoesNotRegressLatestStateRoot()
    {
        // Regression: Finalize(N) blindly overwrote _latestStateRoot with batch N's
        // post-state root regardless of N. Finalize(5) then Finalize(3) would set the
        // latest to batch 3's older root — an apparent state-root regression that a
        // downstream relayer treats as a chain reorg signal.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var b3 = SampleBatch(3) with { PostStateRoot = UInt256.Parse("0x" + new string('3', 64)) };
        var b5 = SampleBatch(5) with { PostStateRoot = UInt256.Parse("0x" + new string('5', 64)) };
        store.AddBatch(b3, BatchStatus.Pending);
        store.AddBatch(b5, BatchStatus.Pending);

        store.Finalize(5);
        Assert.AreEqual(b5.PostStateRoot, store.GetLatestStateRoot());

        store.Finalize(3);
        Assert.AreEqual(b5.PostStateRoot, store.GetLatestStateRoot(),
            "out-of-order Finalize must not regress latest root");
    }

    [TestMethod]
    public void RejectsOversizedChainId_OverflowException()
    {
        // Regression: previously chainId was read as ulong then cast `(uint)` — silently
        // truncating. Caller passes 0x100000001 → reduced to 1 → AssertOurChain compares 1
        // vs 1001 with a misleading "differs from local" message. Now: OverflowException.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store);
        ulong oversized = 0x1_0000_0000UL;  // UInt32.MaxValue + 1
        Assert.ThrowsExactly<OverflowException>(() => methods.GetL2BatchStatus(new JArray { oversized, 1UL }));
    }

    [TestMethod]
    public void Constructor_RejectsNullStore()
    {
        // Pin L2RpcMethods.cs:28's ArgumentNullException.ThrowIfNull(store). Without it
        // every RPC method would NRE on the first store.GetX call with no link to ctor.
        Assert.ThrowsExactly<ArgumentNullException>(() => new L2RpcMethods(null!));
    }

    [TestMethod]
    public void RejectsNegativeNumberForULongParam_OverflowException()
    {
        // Companion pin to RejectsOversizedChainId: the OTHER half of L2RpcMethods.cs:182
        // — `checked((ulong)(BigInteger)n.AsNumber())`. Without `checked`, a negative
        // JSON-RPC number would silently wrap into a large positive ulong (e.g. -1
        // → 18446744073709551615) — a batchNumber lookup by that value would then miss,
        // returning a "not found" rather than the more diagnostic "param[idx] must be ≥ 0".
        // OverflowException at least surfaces the bad input shape clearly.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        var methods = new L2RpcMethods(store);
        Assert.ThrowsExactly<OverflowException>(
            () => methods.GetL2BatchStatus(new JArray { 1001UL, -5L }));
    }

    [TestMethod]
    public void Store_Constructor_RejectsL1ChainIdSentinel()
    {
        // Regression for iter 199: chainId = 0 is the L1 sentinel and never valid for
        // an L2 store. Without this guard, every RPC AssertOurChain would later reject
        // with a misleading "differs from local 0" comparison.
        Assert.ThrowsExactly<System.IO.InvalidDataException>(
            () => new InMemoryL2RpcStore(0, SecurityLevel.Optimistic));
    }

    [TestMethod]
    public void Store_Constructor_RejectsOutOfRangeSecurityLevel()
    {
        // SecurityLevel range-check at ctor — without it `(SecurityLevel)99` would
        // silently propagate as `levelName = "99"` in RPC responses.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new InMemoryL2RpcStore(1001, (SecurityLevel)99));
    }

    [TestMethod]
    public void Store_AddBatch_RejectsNullCommitment()
    {
        // Pin InMemoryL2RpcStore.cs:47.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => store.AddBatch(null!, BatchStatus.Pending));
    }

    [TestMethod]
    public void Store_RegisterAsset_RejectsNullL1Asset()
    {
        // Pin InMemoryL2RpcStore.cs:83.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => store.RegisterAsset(null!, UInt160.Zero));
    }

    [TestMethod]
    public void Store_RegisterAsset_RejectsNullL2Asset()
    {
        // Pin InMemoryL2RpcStore.cs:84.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => store.RegisterAsset(UInt160.Zero, null!));
    }

    [TestMethod]
    public void Store_RecordWithdrawalProof_RejectsNullProofBytes()
    {
        // Pin InMemoryL2RpcStore.cs:110. Companion to the existing leafHash null pin.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => store.RecordWithdrawalProof(UInt256.Zero, null!));
    }

    [TestMethod]
    public void Store_RecordMessageProof_RejectsNullProofBytes()
    {
        // Pin InMemoryL2RpcStore.cs:121.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        Assert.ThrowsExactly<ArgumentNullException>(
            () => store.RecordMessageProof(UInt256.Zero, null!));
    }

    [TestMethod]
    public void Store_RejectsNullKey_AcrossEntryPoints()
    {
        // Regression for iter 184: Dictionary<UInt256/UInt160, T>.TryGetValue(null) /
        // setter[null] throws ArgumentNullException with a generic "key" message. Surface
        // null at the API boundary with the actual parameter name. Same iter-148/183 pattern.
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);

        // Setters
        Assert.ThrowsExactly<ArgumentNullException>(
            () => store.RecordWithdrawalProof(null!, new byte[] { 0x01 }));
        Assert.ThrowsExactly<ArgumentNullException>(
            () => store.RecordMessageProof(null!, new byte[] { 0x01 }));

        // Getters
        Assert.ThrowsExactly<ArgumentNullException>(() => store.GetWithdrawalProof(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => store.GetMessageProof(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => store.GetCanonicalAsset(null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => store.GetBridgedAsset(null!));
    }

    [TestMethod]
    public void GetL1DepositStatus_RoundTrips()
    {
        var store = new InMemoryL2RpcStore(1001, SecurityLevel.Optimistic);
        store.RecordDeposit(new DepositStatus(0, 5, ConsumedOnL2: true, IncludedInBatch: 3));
        var methods = new L2RpcMethods(store);
        var obj = (JObject)methods.GetL1DepositStatus(new JArray { 0, 5UL })!;
        Assert.AreEqual(0U, (uint)obj["sourceChainId"]!.AsNumber());
        Assert.AreEqual(5UL, (ulong)obj["nonce"]!.AsNumber());
        Assert.IsTrue(obj["consumedOnL2"]!.AsBoolean());
        Assert.AreEqual(3UL, (ulong)obj["includedInBatch"]!.AsNumber());
    }

    /// <summary>
    /// Minimal third-party <see cref="IL2RpcStore"/> implementation that only implements
    /// the required interface members — the §16.2 dimension properties (DAMode /
    /// GatewayEnabled / Sequencer / Exit) are covered by the interface's default-method
    /// bodies. Pinning these defaults explicitly catches a refactor that changes the
    /// "strongest-default" (e.g. flipping DAMode default to L1, which would mislead
    /// every third-party implementation that didn't override).
    /// </summary>
    private sealed class MinimalRpcStore : IL2RpcStore
    {
        public uint ChainId => 1001;
        public SecurityLevel SecurityLevel => SecurityLevel.Optimistic;
        public L2BatchCommitment? GetBatch(ulong batchNumber) => null;
        public BatchStatus GetBatchStatus(ulong batchNumber) => BatchStatus.Unknown;
        public UInt256 GetLatestStateRoot() => UInt256.Zero;
        public UInt256 GetStateRootAtBatch(ulong batchNumber) => UInt256.Zero;
        public ReadOnlyMemory<byte>? GetWithdrawalProof(UInt256 leafHash) => null;
        public ReadOnlyMemory<byte>? GetMessageProof(UInt256 messageHash) => null;
        public DepositStatus? GetL1DepositStatus(uint sourceChainId, ulong nonce) => null;
        public UInt160? GetCanonicalAsset(UInt160 l2Asset) => null;
        public UInt160? GetBridgedAsset(UInt160 l1Asset) => null;
    }

    [TestMethod]
    public void IL2RpcStore_DefaultInterfaceMethods_ReturnDocumentedDefaults()
    {
        // Pin the §16.2 dimension default values exposed via IL2RpcStore default-method
        // bodies. Third-party stores that don't override see these — a future change to
        // any default would silently shift every external operator's getsecuritylabel
        // output. Defaults match L2ChainConfig record's init defaults.
        IL2RpcStore store = new MinimalRpcStore();

        Assert.AreEqual(DAMode.External, store.DAMode);
        Assert.IsFalse(store.GatewayEnabled);
        Assert.AreEqual(SequencerModel.DbftCommittee, store.Sequencer);
        Assert.AreEqual(ExitModel.Permissionless, store.Exit);
    }

    [TestMethod]
    public void Constructor_AcceptsValidium()
    {
        // Regression: the original SecurityLevel range-check predated Validium being
        // added (which has byte value 4, outside the 0..3 range the original logic
        // checked). The devnet hit this when fed a `--config` JSON from
        // `neo-stack create-chain --template validium`. Pin so a future refactor
        // doesn't reintroduce the range gap.
        using var store = new InMemoryL2RpcStore(1001, SecurityLevel.Validium);
        Assert.AreEqual(SecurityLevel.Validium, store.SecurityLevel);
    }

    [TestMethod]
    public void Constructor_RejectsOutOfRangeSecurityLevel()
    {
        // Pin the upper bound: bytes outside 0..4 (the Validium-inclusive range)
        // surface as ArgumentOutOfRangeException at construction.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new InMemoryL2RpcStore(1001, (SecurityLevel)99));
    }

    [TestMethod]
    public void GetSecurityLabel_OnMinimalRpcStore_ReflectsDefaults()
    {
        // End-to-end: a third-party IL2RpcStore that only implements required members
        // surfaces the documented defaults through getsecuritylabel without any extra
        // wiring. This is the operator-friendly path — third parties don't need to know
        // about the new properties to deploy the L2RpcMethods.
        IL2RpcStore store = new MinimalRpcStore();
        var methods = new L2RpcMethods(store);
        var obj = (JObject)methods.GetSecurityLabel(new JArray { 1001 })!;

        Assert.AreEqual("Optimistic", obj["securityLevelName"]!.AsString());
        Assert.AreEqual("External", obj["daModeName"]!.AsString());
        Assert.IsFalse(obj["gatewayEnabled"]!.AsBoolean());
        Assert.AreEqual("DbftCommittee", obj["sequencerName"]!.AsString());
        Assert.AreEqual("Permissionless", obj["exitName"]!.AsString());
    }
}
