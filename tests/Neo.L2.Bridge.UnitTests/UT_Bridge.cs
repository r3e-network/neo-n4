using System.Numerics;

namespace Neo.L2.Bridge.UnitTests;

[TestClass]
public class UT_Bridge
{
    private const uint LocalChain = 1001;
    private static readonly UInt160 GasL1 = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 GasL2 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('3', 40));
    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('4', 40));

    private static AssetMapping GasMapping() => new()
    {
        L1Asset = GasL1,
        L2ChainId = LocalChain,
        L2Asset = GasL2,
        AssetType = AssetType.Gas,
        MintBurn = true,
        LockMint = true,
        Active = true,
    };

    private static AssetRegistry RegistryWithGas()
    {
        var r = new AssetRegistry();
        r.Register(GasMapping());
        return r;
    }

    [TestMethod]
    public void Registry_LookupBothDirections()
    {
        var r = RegistryWithGas();
        Assert.IsTrue(r.TryGetByL1(GasL1, LocalChain, out var byL1));
        Assert.AreEqual(GasL2, byL1!.L2Asset);

        Assert.IsTrue(r.TryGetByL2(GasL2, out var byL2));
        Assert.AreEqual(GasL1, byL2!.L1Asset);
    }

    [TestMethod]
    public void Registry_SetActive()
    {
        var r = RegistryWithGas();
        Assert.IsTrue(r.SetActive(GasL2, false));
        r.TryGetByL2(GasL2, out var m);
        Assert.IsFalse(m!.Active);
    }

    [TestMethod]
    public void Registry_Register_SameKey_Overwrites()
    {
        // Documents the overwrite behavior: re-registering a mapping with the same
        // (L1Asset, L2ChainId) replaces the prior entry. Same L2Asset keeps a single
        // _byL2 entry pointing at the latest mapping.
        var r = new AssetRegistry();
        r.Register(GasMapping() with { Active = true });
        r.Register(GasMapping() with { Active = false });

        Assert.AreEqual(1, r.Count);
        r.TryGetByL2(GasL2, out var m);
        Assert.IsFalse(m!.Active, "second Register replaces the first");
    }

    [TestMethod]
    public void Registry_Register_RepointL2Asset_RemovesOrphan()
    {
        // Regression: previously when (L1Asset, L2ChainId) was repointed to a different
        // L2Asset, the old _byL2 entry leaked — TryGetByL2(oldL2) would still return the
        // stale mapping while TryGetByL1(L1) returned the new one. Silent inconsistency.
        var newL2 = UInt160.Parse("0x" + new string('5', 40));
        var r = new AssetRegistry();
        r.Register(GasMapping());
        r.Register(GasMapping() with { L2Asset = newL2 });

        Assert.AreEqual(1, r.Count, "exactly one mapping should remain");
        Assert.IsFalse(r.TryGetByL2(GasL2, out _), "old L2Asset entry must be removed");
        Assert.IsTrue(r.TryGetByL2(newL2, out var byNewL2));
        Assert.AreEqual(GasL1, byNewL2!.L1Asset);
        Assert.IsTrue(r.TryGetByL1(GasL1, LocalChain, out var byL1));
        Assert.AreEqual(newL2, byL1!.L2Asset, "L1 lookup must point at the new L2Asset");
    }

    [TestMethod]
    public void Registry_Register_RepointL1Asset_RemovesOrphan()
    {
        // Symmetric: same L2Asset re-pointed to a different L1Asset must clean up the
        // old _byL1 entry. Otherwise a deposit on the old L1 asset would still resolve
        // to the L2 token that is now mapped elsewhere.
        var newL1 = UInt160.Parse("0x" + new string('6', 40));
        var r = new AssetRegistry();
        r.Register(GasMapping());
        r.Register(GasMapping() with { L1Asset = newL1 });

        Assert.AreEqual(1, r.Count, "exactly one mapping should remain");
        Assert.IsFalse(r.TryGetByL1(GasL1, LocalChain, out _), "old L1 entry must be removed");
        Assert.IsTrue(r.TryGetByL1(newL1, LocalChain, out var byNewL1));
        Assert.AreEqual(GasL2, byNewL1!.L2Asset);
    }

    [TestMethod]
    public void DepositPayload_RoundTrips()
    {
        var p = new DepositPayload
        {
            L1Asset = GasL1,
            L2Recipient = Recipient,
            Amount = new BigInteger(1_234_567_890),
        };
        var bytes = p.Encode();
        var decoded = DepositPayload.Decode(bytes);
        Assert.AreEqual(p.L1Asset, decoded.L1Asset);
        Assert.AreEqual(p.L2Recipient, decoded.L2Recipient);
        Assert.AreEqual(p.Amount, decoded.Amount);
    }

    [TestMethod]
    public void DepositProcessor_MintsForKnownAsset()
    {
        var registry = RegistryWithGas();
        var proc = new DepositProcessor(LocalChain, registry);

        var payload = new DepositPayload
        {
            L1Asset = GasL1,
            L2Recipient = Recipient,
            Amount = new BigInteger(1_000_000),
        };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = LocalChain,
            Nonce = 7,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
            MessageHash = UInt256.Parse("0x" + new string('a', 64)),
        };

        var instr = proc.Process(msg);
        Assert.AreEqual(GasL2, instr.L2Asset);
        Assert.AreEqual(Recipient, instr.Recipient);
        Assert.AreEqual(new BigInteger(1_000_000), instr.Amount);
        Assert.IsTrue(proc.HasConsumed(0, 7));
    }

    [TestMethod]
    public void DepositProcessor_RejectsReplay()
    {
        var registry = RegistryWithGas();
        var proc = new DepositProcessor(LocalChain, registry);
        var payload = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = 1 };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0, TargetChainId = LocalChain, Nonce = 1,
            Sender = Sender, Receiver = Recipient,
            MessageType = MessageType.Deposit, Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };
        proc.Process(msg);
        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Process(msg));
    }

    [TestMethod]
    public void WithdrawalProcessor_RejectsCrossBatchDuplicateNonce()
    {
        // Regression: previously SealBatch cleared _byNonce, which meant a user could
        // re-stage the same (sender, nonce) in the next batch — the L2 accepted, the
        // duplicate was caught only at L1 settlement hours later. The Nonce field is
        // documented as "per-(chain, sender) monotonic for replay protection," so the
        // L2 must enforce uniqueness across the chain's lifetime, not just per-batch.
        var registry = RegistryWithGas();
        var proc = new WithdrawalProcessor(LocalChain, registry);

        WithdrawalRequest Mk(ulong nonce) => new()
        {
            EmittingContract = UInt160.Zero,
            L2Sender = Sender,
            L1Recipient = Recipient,
            L2Asset = GasL2,
            Amount = new BigInteger(100),
            Nonce = nonce,
        };

        proc.Stage(Mk(1));
        proc.SealBatch();

        // Nonce 1 from the same sender in the *next* batch must be rejected.
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => proc.Stage(Mk(1)));
        StringAssert.Contains(ex.Message, "prior batch");
    }

    [TestMethod]
    public void WithdrawalProcessor_Stage_RejectsNullL2Sender()
    {
        // Regression for iter 147: null UInt160 fields slipped past `required` (which only
        // forces "must be set," not "non-null") and crashed deep in
        // MessageHasher.HashWithdrawal's GetSpan(). Now caught at the API boundary.
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas());
        var bad = new WithdrawalRequest
        {
            EmittingContract = UInt160.Zero,
            L2Sender = null!,  // ← null
            L1Recipient = Recipient,
            L2Asset = GasL2,
            Amount = new BigInteger(100),
            Nonce = 1,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => proc.Stage(bad));
    }

    [TestMethod]
    public void WithdrawalProcessor_Stage_RejectsNullL1Recipient()
    {
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas());
        var bad = new WithdrawalRequest
        {
            EmittingContract = UInt160.Zero,
            L2Sender = Sender,
            L1Recipient = null!,  // ← null
            L2Asset = GasL2,
            Amount = new BigInteger(100),
            Nonce = 1,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => proc.Stage(bad));
    }

    [TestMethod]
    public void AssetRegistry_Register_RejectsNullL1Asset()
    {
        // Regression for iter 148: null UInt160 fields in AssetMapping would either
        // create a tuple key (null, chainId) (Dictionary tolerates null inside a tuple)
        // or throw deep in `_byL2[null]`. Both are surfaced clearly at the API boundary.
        var r = new AssetRegistry();
        var bad = new AssetMapping
        {
            L1Asset = null!,  // ← null
            L2ChainId = LocalChain,
            L2Asset = GasL2,
            AssetType = AssetType.Gas,
            MintBurn = true, LockMint = true, Active = true,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => r.Register(bad));
    }

    [TestMethod]
    public void AssetRegistry_Register_RejectsNullL2Asset()
    {
        var r = new AssetRegistry();
        var bad = new AssetMapping
        {
            L1Asset = GasL1,
            L2ChainId = LocalChain,
            L2Asset = null!,  // ← null
            AssetType = AssetType.Gas,
            MintBurn = true, LockMint = true, Active = true,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => r.Register(bad));
    }

    [TestMethod]
    public void DepositPayload_Decode_RejectsTrailingBytes()
    {
        // Regression: previously the length check was `pos + amountLen > bytes.Length`,
        // which silently accepted trailing bytes. An attacker could append padding that
        // the L1 hashes (full bytes) but the L2 decoder ignored — a malleability surface.
        var p = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = new BigInteger(1) };
        var bytes = p.Encode().ToList();
        bytes.AddRange(new byte[] { 0xFF, 0xFF }); // trailing padding
        Assert.ThrowsExactly<InvalidDataException>(() => DepositPayload.Decode(bytes.ToArray()));
    }

    [TestMethod]
    public void DepositProcessor_RejectsUnknownAsset()
    {
        var proc = new DepositProcessor(LocalChain, new AssetRegistry());
        var payload = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = 1 };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0, TargetChainId = LocalChain, Nonce = 1,
            Sender = Sender, Receiver = Recipient,
            MessageType = MessageType.Deposit, Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Process(msg));
    }

    [TestMethod]
    public void DepositProcessor_AssetUnknownThenRegistered_RetrySucceeds()
    {
        // Regression: previously the consumed-set was populated BEFORE asset lookup, so a
        // transient "asset not yet registered" failure permanently locked the (source, nonce)
        // pair. After the operator registers the missing asset, retry would fail with
        // "already processed" instead of succeeding. The fix moves the consumed-claim to
        // AFTER all validation passes.
        var registry = new AssetRegistry();
        var proc = new DepositProcessor(LocalChain, registry);
        var payload = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = 1 };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0, TargetChainId = LocalChain, Nonce = 42,
            Sender = Sender, Receiver = Recipient,
            MessageType = MessageType.Deposit, Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };

        // Attempt 1 — asset not registered yet.
        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Process(msg));
        Assert.IsFalse(proc.HasConsumed(0, 42), "nonce must NOT be consumed when validation failed");

        // Operator registers the missing asset.
        registry.Register(GasMapping());

        // Attempt 2 — should succeed now.
        var instr = proc.Process(msg);
        Assert.AreEqual(GasL2, instr.L2Asset);
        Assert.IsTrue(proc.HasConsumed(0, 42), "nonce must be consumed after success");
    }

    [TestMethod]
    public void WithdrawalProcessor_Stages_RejectsDuplicateNonce()
    {
        var registry = RegistryWithGas();
        var proc = new WithdrawalProcessor(LocalChain, registry);

        WithdrawalRequest Mk(ulong nonce) => new()
        {
            EmittingContract = UInt160.Zero,
            L2Sender = Sender,
            L1Recipient = Recipient,
            L2Asset = GasL2,
            Amount = new BigInteger(100),
            Nonce = nonce,
        };

        proc.Stage(Mk(1));
        proc.Stage(Mk(2));
        Assert.AreEqual(2, proc.StagedCount);
        Assert.ThrowsExactly<InvalidOperationException>(() => proc.Stage(Mk(1)));

        var (root, tree) = proc.SealBatch();
        Assert.AreNotEqual(UInt256.Zero, root);
        Assert.AreEqual(2, tree.Count);
        Assert.AreEqual(0, proc.StagedCount); // sealed → fresh tree
    }
}
