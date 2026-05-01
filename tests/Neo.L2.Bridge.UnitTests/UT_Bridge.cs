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
