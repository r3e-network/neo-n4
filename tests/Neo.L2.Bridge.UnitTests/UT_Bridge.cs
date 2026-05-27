using System.Numerics;

namespace Neo.L2.Bridge.UnitTests;

[TestClass]
public class UT_Bridge
{
    private const uint LocalChain = 1001;
    private static readonly UInt160 NeoL1 = UInt160.Parse("0x" + new string('9', 40));
    private static readonly UInt160 NeoL2 = UInt160.Parse("0x" + new string('8', 40));
    private static readonly UInt160 GasL1 = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 GasL2 = UInt160.Parse("0x" + new string('2', 40));
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('3', 40));
    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('4', 40));

    private static AssetMapping GasMapping() => new()
    {
        L1Asset = GasL1,
        L2ChainId = LocalChain,
        L2Asset = GasL2,
        L1Decimals = 8,
        L2Decimals = 8,
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
    public void Registry_SetActive_UnknownAsset_ReturnsFalse()
    {
        // SetActive returns bool — true on existing, false on missing. Documents the
        // graceful no-op for unknown L2Asset (vs throwing). Test pins the contract so
        // a future "throw on unknown" refactor is caught.
        var r = new AssetRegistry();
        var unknownL2 = UInt160.Parse("0x" + new string('e', 40));
        Assert.IsFalse(r.SetActive(unknownL2, false));
        Assert.IsFalse(r.SetActive(unknownL2, true));
    }

    [TestMethod]
    public void Registry_Snapshot_ReturnsAllMappings_AndIsImmutable()
    {
        // Snapshot is meant to be a frozen read for inspection / serialization. Pins
        // both that all registered mappings are returned AND that the returned list is
        // an array snapshot — a future Register() doesn't mutate the prior snapshot.
        var r = new AssetRegistry();
        var aL1 = UInt160.Parse("0x" + new string('a', 40));
        var aL2 = UInt160.Parse("0x" + new string('b', 40));
        r.Register(new AssetMapping
        {
            L1Asset = aL1,
            L2ChainId = LocalChain,
            L2Asset = aL2,
            L1Decimals = 8,
            L2Decimals = 8,
            AssetType = AssetType.Nep17,
            MintBurn = true,
            LockMint = true,
            Active = true,
        });

        var snapshot1 = r.Snapshot();
        Assert.AreEqual(1, snapshot1.Count);

        // Add another mapping; the prior snapshot must not change.
        var bL1 = UInt160.Parse("0x" + new string('c', 40));
        var bL2 = UInt160.Parse("0x" + new string('d', 40));
        r.Register(new AssetMapping
        {
            L1Asset = bL1,
            L2ChainId = LocalChain,
            L2Asset = bL2,
            L1Decimals = 8,
            L2Decimals = 8,
            AssetType = AssetType.Nep17,
            MintBurn = true,
            LockMint = true,
            Active = true,
        });

        Assert.AreEqual(1, snapshot1.Count, "prior snapshot is frozen");
        Assert.AreEqual(2, r.Snapshot().Count, "fresh snapshot reflects new state");
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
    public void DepositPayload_ByteLayout_MatchesDocumentedOffsets()
    {
        // Pins the layout claimed in DepositPayload's XML docs:
        // [20B l1Asset] [20B l2Recipient] [4B amountLength LE] [amountLength B amount(unsigned LE)]
        // The L1 SharedBridge contract parses bytes off the wire and depends on these
        // exact offsets — silent reorder would desync L2 mint from L1 lock.
        var amount = new BigInteger(0x1122334455667788UL);
        var amountBytes = amount.ToByteArray(isUnsigned: true, isBigEndian: false);
        var p = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = amount };
        var bytes = p.Encode();

        Assert.AreEqual(20 + 20 + 4 + amountBytes.Length, bytes.Length);
        CollectionAssert.AreEqual(GasL1.GetSpan().ToArray(), bytes[0..20]);
        CollectionAssert.AreEqual(Recipient.GetSpan().ToArray(), bytes[20..40]);
        Assert.AreEqual(amountBytes.Length,
            System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
        CollectionAssert.AreEqual(amountBytes, bytes[44..]);
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
    public void DepositProcessor_ScalesL1NeoWholeUnitsToDecimalizedL2Neo()
    {
        var registry = new AssetRegistry();
        registry.Register(PlatformAssets.CreateNeoMapping(NeoL1, LocalChain) with { L2Asset = NeoL2 });
        var proc = new DepositProcessor(LocalChain, registry);
        var payload = new DepositPayload
        {
            L1Asset = NeoL1,
            L2Recipient = Recipient,
            Amount = new BigInteger(2),
        };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = LocalChain,
            Nonce = 8,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };

        var instr = proc.Process(msg);

        Assert.AreEqual(NeoL2, instr.L2Asset);
        Assert.AreEqual(new BigInteger(200_000_000), instr.Amount);
    }

    [TestMethod]
    public void DepositProcessor_RejectsReplay()
    {
        var registry = RegistryWithGas();
        var proc = new DepositProcessor(LocalChain, registry);
        var payload = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = 1 };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = LocalChain,
            Nonce = 1,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
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
            ChainId = LocalChain,
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
            ChainId = 1U,
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
            ChainId = 1U,
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
    public void WithdrawalProcessor_Stage_RejectsNullRequest()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new WithdrawalProcessor(LocalChain, RegistryWithGas()).Stage(null!));

    [TestMethod]
    public void WithdrawalProcessor_Stage_RejectsNullEmittingContract()
    {
        // Companion to RejectsNullL2Sender / RejectsNullL1Recipient. Pinning
        // WithdrawalProcessor.cs:61. Without it a null EmittingContract surfaces only
        // deep in MessageHasher.HashWithdrawal's GetSpan with no link back to the field.
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas());
        var bad = new WithdrawalRequest
        {
            ChainId = 1U,
            EmittingContract = null!,
            L2Sender = Sender,
            L1Recipient = Recipient,
            L2Asset = GasL2,
            Amount = new BigInteger(100),
            Nonce = 1,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => proc.Stage(bad));
    }

    [TestMethod]
    public void WithdrawalProcessor_Stage_RejectsNullL2Asset()
    {
        // Without WithdrawalProcessor.cs:64, a null L2Asset would slip past the
        // per-field null-guards and fail later inside `_registry.TryGetByL2(null)` —
        // the same iter-148 generic "key" message Register's guard avoided. Pin it
        // at Stage where the bad input first arrives.
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas());
        var bad = new WithdrawalRequest
        {
            ChainId = 1U,
            EmittingContract = UInt160.Zero,
            L2Sender = Sender,
            L1Recipient = Recipient,
            L2Asset = null!,
            Amount = new BigInteger(100),
            Nonce = 1,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => proc.Stage(bad));
    }

    [TestMethod]
    public void WithdrawalProcessor_Constructor_RejectsNullRegistry()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new WithdrawalProcessor(LocalChain, null!));

    [TestMethod]
    public void WithdrawalProcessor_WithMetrics_RejectsNullMetrics()
    {
        // Pinning WithdrawalProcessor.cs:46. The `IL2Metrics?` parameter on the ctor
        // accepts null (defaults to NoOpMetrics), but WithMetrics is the explicit-swap
        // path and must reject null — otherwise a later metric call NREs deep inside.
        var proc = new WithdrawalProcessor(LocalChain, RegistryWithGas());
        Assert.ThrowsExactly<ArgumentNullException>(() => proc.WithMetrics(null!));
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
            L1Decimals = 8,
            L2Decimals = 8,
            AssetType = AssetType.Gas,
            MintBurn = true,
            LockMint = true,
            Active = true,
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
            L1Decimals = 8,
            L2Decimals = 8,
            AssetType = AssetType.Gas,
            MintBurn = true,
            LockMint = true,
            Active = true,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => r.Register(bad));
    }

    [TestMethod]
    public void DepositPayload_Encode_RejectsNullL1Asset()
    {
        // Pin DepositPayload.cs:30. UInt160 is reference-typed; without the guard, a
        // null L1Asset would NRE on GetSpan() inside Encode with no link to the bad
        // field. Same iter-154+ defense-in-depth pattern as Multisig/Optimistic/RiscV
        // proof payloads (iter 220).
        var bad = new DepositPayload { L1Asset = null!, L2Recipient = Recipient, Amount = 1 };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void DepositPayload_Encode_RejectsNullL2Recipient()
    {
        var bad = new DepositPayload { L1Asset = GasL1, L2Recipient = null!, Amount = 1 };
        Assert.ThrowsExactly<ArgumentNullException>(() => bad.Encode());
    }

    [TestMethod]
    public void DepositPayload_Encode_RejectsOversizedAmount()
    {
        // Pin DepositPayload.cs:33-34. The 64-byte amount cap (already a > 256-bit
        // number, well past any plausible token) bounds the buffer alloc against
        // attacker-influenced sizes — same shape as MessageHasher.HashWithdrawal's
        // amount cap pinned in iter 218.
        var huge = BigInteger.One << 600; // ~75 bytes when serialized
        var bad = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = huge };
        Assert.ThrowsExactly<InvalidOperationException>(() => bad.Encode());
    }

    [TestMethod]
    public void DepositPayload_Encode_AcceptsExactly64ByteAmount()
    {
        // Boundary partner of RejectsOversizedAmount: the cap is `> 64`, so 64 must
        // encode without error. 2^504 serializes to exactly 64 bytes (bit 504 = byte 63
        // bit 0). Same boundary-pair pattern as the other proof-payload encoders.
        var atMax = BigInteger.One << 504;  // exactly 64 unsigned-LE bytes
        var p = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = atMax };
        var bytes = p.Encode();
        Assert.AreEqual(64, System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(40, 4)));
        Assert.AreEqual(atMax, DepositPayload.Decode(bytes).Amount);
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
    public void DepositProcessor_Process_RejectsNullMessage()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new DepositProcessor(LocalChain, new AssetRegistry()).Process(null!));

    [TestMethod]
    public void DepositProcessor_Constructor_RejectsNullRegistry()
        => Assert.ThrowsExactly<ArgumentNullException>(
            () => new DepositProcessor(LocalChain, null!));

    [TestMethod]
    public void DepositProcessor_WithMetrics_RejectsNullMetrics()
    {
        // Symmetric to WithdrawalProcessor_WithMetrics_RejectsNullMetrics (iter 223).
        var proc = new DepositProcessor(LocalChain, new AssetRegistry());
        Assert.ThrowsExactly<ArgumentNullException>(() => proc.WithMetrics(null!));
    }

    [TestMethod]
    public void DepositProcessor_Process_RejectsWrongMessageType()
    {
        // Pinning DepositProcessor.cs:49-50. A non-Deposit message reaching Process is
        // a bug: the router shouldn't dispatch them here. Without this guard, downstream
        // DepositPayload.Decode would consume bytes meant for another schema and either
        // succeed by accident or fail with a confusing parse error.
        var proc = new DepositProcessor(LocalChain, new AssetRegistry());
        var payload = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = 1 };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = LocalChain,
            Nonce = 1,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Withdraw,  // ← wrong type
            Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => proc.Process(msg));
        StringAssert.Contains(ex.Message, "Deposit");
    }

    [TestMethod]
    public void DepositProcessor_Process_RejectsWrongTargetChain()
    {
        // Pinning DepositProcessor.cs:51-52. A message targeting a different L2 must not
        // be processed locally — without this guard the deposit would mint on the wrong
        // chain. Same defense-in-depth as the AssertOurChain RPC pattern.
        var proc = new DepositProcessor(LocalChain, new AssetRegistry());
        var payload = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = 1 };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = 9999,  // ← not local
            Nonce = 1,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
            MessageHash = UInt256.Zero,
        };
        var ex = Assert.ThrowsExactly<ArgumentException>(() => proc.Process(msg));
        StringAssert.Contains(ex.Message, "9999");
    }

    [TestMethod]
    public void DepositProcessor_RejectsUnknownAsset()
    {
        var proc = new DepositProcessor(LocalChain, new AssetRegistry());
        var payload = new DepositPayload { L1Asset = GasL1, L2Recipient = Recipient, Amount = 1 };
        var msg = new CrossChainMessage
        {
            SourceChainId = 0,
            TargetChainId = LocalChain,
            Nonce = 1,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
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
            SourceChainId = 0,
            TargetChainId = LocalChain,
            Nonce = 42,
            Sender = Sender,
            Receiver = Recipient,
            MessageType = MessageType.Deposit,
            Payload = payload.Encode(),
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
            ChainId = LocalChain,
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

    [TestMethod]
    public void WithdrawalProcessor_Stage_SurvivesThrowingMetricsSink()
    {
        // Regression for iter 162/163: previously a throwing IL2Metrics would either
        //  (a) leave the staged tree mutation committed but throw to the caller, who
        //      would then assume the operation failed and retry — only to hit
        //      "nonce already used" since _byNonce DID record it; or
        //  (b) double-count (BOTH WithdrawalsStaged and WithdrawalsRejected) because
        //      the broad catch above caught the metrics throw too.
        // The fix: success counter is outside the lock and outside the try; both
        // metric calls are individually try/catch-swallowed via SafeIncrementCounter.
        var registry = RegistryWithGas();
        var proc = new WithdrawalProcessor(LocalChain, registry, new ThrowingMetrics());

        var req = new WithdrawalRequest
        {
            ChainId = 1U,
            EmittingContract = UInt160.Zero,
            L2Sender = Sender,
            L1Recipient = Recipient,
            L2Asset = GasL2,
            Amount = new BigInteger(100),
            Nonce = 1,
        };

        // Must not throw, even though the metrics sink throws on every call.
        var leaf = proc.Stage(req);
        Assert.AreNotEqual(UInt256.Zero, leaf);
        Assert.AreEqual(1, proc.StagedCount);
    }

    private sealed class ThrowingMetrics : Neo.L2.Telemetry.IL2Metrics
    {
        public void IncrementCounter(string name, long delta = 1, params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException($"sink down: {name}");
        public void RecordHistogram(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException($"sink down: {name}");
        public void SetGauge(string name, double value, params ReadOnlySpan<(string Key, string Value)> tags)
            => throw new InvalidOperationException($"sink down: {name}");
    }

    [TestMethod]
    public void Registry_TryGetByL1_RejectsNullL1Asset()
    {
        // Regression for iter 183: Dictionary<UInt160, T>.TryGetValue(null) throws
        // ArgumentNullException with a generic "key" message. Surface at the API
        // boundary so the operator sees the actual argument name.
        var r = new AssetRegistry();
        Assert.ThrowsExactly<ArgumentNullException>(
            () => r.TryGetByL1(null!, LocalChain, out _));
    }

    [TestMethod]
    public void Registry_TryGetByL2_RejectsNullL2Asset()
    {
        var r = new AssetRegistry();
        Assert.ThrowsExactly<ArgumentNullException>(() => r.TryGetByL2(null!, out _));
    }

    [TestMethod]
    public void Registry_SetActive_RejectsNullL2Asset()
    {
        var r = new AssetRegistry();
        Assert.ThrowsExactly<ArgumentNullException>(() => r.SetActive(null!, false));
    }
}
