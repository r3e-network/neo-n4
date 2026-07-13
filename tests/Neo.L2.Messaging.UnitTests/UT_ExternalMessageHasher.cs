namespace Neo.L2.Messaging.UnitTests;

[TestClass]
public class UT_ExternalMessageHasher
{
    private static ExternalCrossChainMessage SampleEthDeposit() => ExternalMessageBuilder.Build(
        externalChainId: 0xE000_0001,                                  // Eth mainnet
        neoChainId: 1099,
        nonce: 42,
        direction: ExternalBridgeDirection.ForeignToNeo,
        sender: UInt160.Parse("0x" + new string('1', 40)),             // foreign sender (low 20B)
        recipient: UInt160.Parse("0x" + new string('a', 40)),          // L2 recipient
        deadlineUnixSeconds: 1_900_000_000UL,
        sourceTxRef: UInt256.Parse("0x" + new string('e', 64)),
        messageType: ExternalMessageType.AssetTransfer,
        payload: new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });

    [TestMethod]
    public void Build_PopulatesCanonicalHash()
    {
        var msg = SampleEthDeposit();
        Assert.AreNotEqual(UInt256.Zero, msg.MessageHash, "MessageHash must be populated");
        // Hash is deterministic — recomputing on the same record yields the same value.
        var recomputed = ExternalMessageHasher.HashMessage(msg with { MessageHash = UInt256.Zero });
        Assert.AreEqual(recomputed, msg.MessageHash);
    }

    [TestMethod]
    public void Build_RejectsNonForeignNamespacePrefix()
    {
        // Neo L2 chainIds start at 1; passing one as externalChainId would silently
        // route to the wrong verifier. The builder must catch the misconfig.
        Assert.ThrowsExactly<ArgumentException>(() => ExternalMessageBuilder.Build(
            externalChainId: 1099u,
            neoChainId: 1099u,
            nonce: 1,
            direction: ExternalBridgeDirection.NeoToForeign,
            sender: UInt160.Parse("0x" + new string('1', 40)),
            recipient: UInt160.Parse("0x" + new string('2', 40)),
            deadlineUnixSeconds: 0,
            sourceTxRef: UInt256.Zero,
            messageType: ExternalMessageType.AssetTransfer,
            payload: ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public void Build_RejectsInvalidDirection()
    {
        Assert.ThrowsExactly<ArgumentException>(() => ExternalMessageBuilder.Build(
            externalChainId: 0xE000_0001U,
            neoChainId: 1099u,
            nonce: 1,
            direction: (ExternalBridgeDirection)0,         // invalid: not 1 or 2
            sender: UInt160.Parse("0x" + new string('1', 40)),
            recipient: UInt160.Parse("0x" + new string('2', 40)),
            deadlineUnixSeconds: 0,
            sourceTxRef: UInt256.Zero,
            messageType: ExternalMessageType.AssetTransfer,
            payload: ReadOnlyMemory<byte>.Empty));
    }

    [TestMethod]
    public void Hash_DiffersWhenAnyFieldChanges()
    {
        var baseline = SampleEthDeposit();

        // Each independent field-change should produce a different hash.
        var changedNonce = ExternalMessageBuilder.Build(
            baseline.ExternalChainId, baseline.NeoChainId, baseline.Nonce + 1, baseline.Direction,
            baseline.Sender, baseline.Recipient, baseline.DeadlineUnixSeconds, baseline.SourceTxRef,
            baseline.MessageType, baseline.Payload);
        Assert.AreNotEqual(baseline.MessageHash, changedNonce.MessageHash, "nonce change must shift hash");

        var changedExternalChainId = ExternalMessageBuilder.Build(
            0xE000_0010U /* Tron */, baseline.NeoChainId, baseline.Nonce, baseline.Direction,
            baseline.Sender, baseline.Recipient, baseline.DeadlineUnixSeconds, baseline.SourceTxRef,
            baseline.MessageType, baseline.Payload);
        Assert.AreNotEqual(baseline.MessageHash, changedExternalChainId.MessageHash,
            "externalChainId change must shift hash");

        var changedNeoChainId = ExternalMessageBuilder.Build(
            baseline.ExternalChainId, baseline.NeoChainId + 1, baseline.Nonce, baseline.Direction,
            baseline.Sender, baseline.Recipient, baseline.DeadlineUnixSeconds, baseline.SourceTxRef,
            baseline.MessageType, baseline.Payload);
        Assert.AreNotEqual(baseline.MessageHash, changedNeoChainId.MessageHash,
            "neoChainId change must shift hash so a signed message cannot cross Neo L2 domains");

        var changedDirection = ExternalMessageBuilder.Build(
            baseline.ExternalChainId, baseline.NeoChainId, baseline.Nonce,
            ExternalBridgeDirection.NeoToForeign, baseline.Sender, baseline.Recipient,
            baseline.DeadlineUnixSeconds, baseline.SourceTxRef,
            baseline.MessageType, baseline.Payload);
        Assert.AreNotEqual(baseline.MessageHash, changedDirection.MessageHash,
            "direction change must shift hash");

        var changedPayload = ExternalMessageBuilder.Build(
            baseline.ExternalChainId, baseline.NeoChainId, baseline.Nonce, baseline.Direction,
            baseline.Sender, baseline.Recipient, baseline.DeadlineUnixSeconds, baseline.SourceTxRef,
            baseline.MessageType, new byte[] { 0xDE, 0xAD, 0xBE, 0xEE });
        Assert.AreNotEqual(baseline.MessageHash, changedPayload.MessageHash,
            "payload byte change must shift hash");
    }

    [TestMethod]
    public void Hash_DistinctFromInternalCrossChainMessage()
    {
        // The two hashers serve disjoint message universes — same field values
        // must NOT collide between an ExternalCrossChainMessage and a
        // CrossChainMessage. This pins the format-separation invariant.
        var external = SampleEthDeposit();
        var internalMsg = MessageBuilder.Build(
            sourceChainId: 0xE000_0001U,
            targetChainId: 1099u,
            nonce: 42,
            sender: UInt160.Parse("0x" + new string('1', 40)),
            receiver: UInt160.Parse("0x" + new string('a', 40)),
            messageType: MessageType.Deposit,
            payload: new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        Assert.AreNotEqual(external.MessageHash, internalMsg.MessageHash,
            "external + internal cross-chain message hashes must not collide");
    }

    [TestMethod]
    public void Hash_RejectsNullCriticalFields()
    {
        // Manually construct a record with null Sender — bypasses the builder
        // null-checks. The hasher must still throw rather than NRE-ing inside
        // GetSpan().
        var bad = new ExternalCrossChainMessage
        {
            ExternalChainId = 0xE000_0001U,
            NeoChainId = 1099u,
            Nonce = 1,
            Direction = ExternalBridgeDirection.ForeignToNeo,
            Sender = null!,
            Recipient = UInt160.Parse("0x" + new string('a', 40)),
            DeadlineUnixSeconds = 0,
            SourceTxRef = UInt256.Zero,
            MessageType = ExternalMessageType.AssetTransfer,
            Payload = ReadOnlyMemory<byte>.Empty,
            MessageHash = UInt256.Zero,
        };
        Assert.ThrowsExactly<ArgumentNullException>(() => ExternalMessageHasher.HashMessage(bad));
    }
}
