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
}
