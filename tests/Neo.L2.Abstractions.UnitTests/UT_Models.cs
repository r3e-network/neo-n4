using System.Numerics;

namespace Neo.L2.UnitTests;

[TestClass]
public class UT_Models
{
    [TestMethod]
    public void ChainMode_HasExpectedDiscriminants()
    {
        Assert.AreEqual(0, (byte)ChainMode.L1Mode);
        Assert.AreEqual(1, (byte)ChainMode.SidechainMode);
        Assert.AreEqual(2, (byte)ChainMode.L2RollupMode);
        Assert.AreEqual(3, (byte)ChainMode.L2ValidiumMode);
    }

    [TestMethod]
    public void SecurityLevel_HasExpectedDiscriminants()
    {
        Assert.AreEqual(0, (byte)SecurityLevel.Sidechain);
        Assert.AreEqual(1, (byte)SecurityLevel.Settled);
        Assert.AreEqual(2, (byte)SecurityLevel.Optimistic);
        Assert.AreEqual(3, (byte)SecurityLevel.Validity);
        Assert.AreEqual(4, (byte)SecurityLevel.Validium);
    }

    [TestMethod]
    public void DAMode_HasExpectedDiscriminants()
    {
        Assert.AreEqual(0, (byte)DAMode.L1);
        Assert.AreEqual(1, (byte)DAMode.NeoFS);
        Assert.AreEqual(2, (byte)DAMode.External);
        Assert.AreEqual(3, (byte)DAMode.DAC);
    }

    [TestMethod]
    public void ProofType_HasExpectedDiscriminants()
    {
        Assert.AreEqual(0, (byte)ProofType.None);
        Assert.AreEqual(1, (byte)ProofType.Multisig);
        Assert.AreEqual(2, (byte)ProofType.Optimistic);
        Assert.AreEqual(3, (byte)ProofType.Zk);
    }

    [TestMethod]
    public void ProofTypeExtensions_Resolve_AcceptsAllValidBytes()
    {
        Assert.AreEqual(ProofType.None, ProofTypeExtensions.Resolve(0));
        Assert.AreEqual(ProofType.Multisig, ProofTypeExtensions.Resolve(1));
        Assert.AreEqual(ProofType.Optimistic, ProofTypeExtensions.Resolve(2));
        Assert.AreEqual(ProofType.Zk, ProofTypeExtensions.Resolve(3));
    }

    [TestMethod]
    public void ProofTypeExtensions_Resolve_RejectsUnknownByte()
    {
        // Used by L2ProverPlugin.Configure and L2SettlementSettings.From to validate
        // operator-supplied ProofType bytes at plugin-load time. Without this the
        // misconfiguration only surfaces at first proof generation.
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() => ProofTypeExtensions.Resolve(99));
        StringAssert.Contains(ex.Message, "ProofType 99");
    }

    [TestMethod]
    public void ChainIdValidator_ValidateL2_RejectsZero()
    {
        // Regression: ChainId=0 is reserved for Neo L1 (L2Outbox.L1ChainId). An L2 chain
        // adopting it would misroute L2→L2 messages as L2→L1. Default-uint config (when
        // ChainId is omitted) lands on 0, so plugin Configure must surface this clearly
        // at load time, not let it slip into runtime where a misrouted message is silent.
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() => ChainIdValidator.ValidateL2(0));
        StringAssert.Contains(ex.Message, "reserved for Neo L1");
    }

    [TestMethod]
    public void ChainIdValidator_ValidateL2_AcceptsNonZero()
    {
        Assert.AreEqual(1u, ChainIdValidator.ValidateL2(1));
        Assert.AreEqual(1001u, ChainIdValidator.ValidateL2(1001));
        Assert.AreEqual(uint.MaxValue, ChainIdValidator.ValidateL2(uint.MaxValue));
    }

    [TestMethod]
    public void ChainIdValidator_ValidateL2_NamesSettingInError()
    {
        // The optional setting-name parameter lets each call site identify which config
        // key was bad — useful when the same plugin reads multiple chain ids.
        var ex = Assert.ThrowsExactly<System.IO.InvalidDataException>(() =>
            ChainIdValidator.ValidateL2(0, "BridgeChainId"));
        StringAssert.Contains(ex.Message, "BridgeChainId");
    }

    [TestMethod]
    public void MessageType_HasExpectedDiscriminants()
    {
        Assert.AreEqual(0, (byte)MessageType.Deposit);
        Assert.AreEqual(1, (byte)MessageType.Withdraw);
        Assert.AreEqual(2, (byte)MessageType.Call);
        Assert.AreEqual(3, (byte)MessageType.Event);
        Assert.AreEqual(4, (byte)MessageType.Governance);
    }

    [TestMethod]
    public void L2ChainConfig_RoundtripsByValue()
    {
        var verifier = UInt160.Parse("0x0000000000000000000000000000000000000001");
        var bridge = UInt160.Parse("0x0000000000000000000000000000000000000002");
        var msg = UInt160.Parse("0x0000000000000000000000000000000000000003");
        var op = UInt160.Parse("0x0000000000000000000000000000000000000004");

        var a = new L2ChainConfig
        {
            ChainId = 1001,
            OperatorManager = op,
            Verifier = verifier,
            BridgeAdapter = bridge,
            MessageAdapter = msg,
            SecurityLevel = SecurityLevel.Optimistic,
            DAMode = DAMode.NeoFS,
            GatewayEnabled = true,
            PermissionlessExit = false,
            Active = true,
        };

        var b = a with { ChainId = 1001 };

        Assert.AreEqual(a, b);
    }

    [TestMethod]
    public void L2BatchCommitment_DistinguishesByContent()
    {
        var z = UInt256.Zero;

        L2BatchCommitment Mk(uint chainId) => new()
        {
            ChainId = chainId,
            BatchNumber = 1,
            FirstBlock = 100,
            LastBlock = 200,
            PreStateRoot = z,
            PostStateRoot = z,
            TxRoot = z,
            ReceiptRoot = z,
            WithdrawalRoot = z,
            L2ToL1MessageRoot = z,
            L2ToL2MessageRoot = z,
            DACommitment = z,
            PublicInputHash = z,
            ProofType = ProofType.Multisig,
            Proof = ReadOnlyMemory<byte>.Empty,
        };

        Assert.AreNotEqual(Mk(1001), Mk(1002));
        Assert.AreEqual(Mk(1001), Mk(1001));
    }

    [TestMethod]
    public void WithdrawalRequest_HoldsBigInteger()
    {
        var w = new WithdrawalRequest
        {
            EmittingContract = UInt160.Zero,
            L2Sender = UInt160.Zero,
            L1Recipient = UInt160.Zero,
            L2Asset = UInt160.Zero,
            Amount = BigInteger.Parse("123456789012345678901234567890"),
            Nonce = 42,
        };

        Assert.AreEqual(BigInteger.Parse("123456789012345678901234567890"), w.Amount);
    }

    [TestMethod]
    public void ProofVerificationResult_OkAndFail()
    {
        Assert.IsTrue(ProofVerificationResult.Ok.Valid);
        Assert.IsNull(ProofVerificationResult.Ok.FailureReason);

        var f = ProofVerificationResult.Fail("bad multiset");
        Assert.IsFalse(f.Valid);
        Assert.AreEqual("bad multiset", f.FailureReason);
    }

    [TestMethod]
    public void DAPublishRequest_DistinguishesByPayloadContent()
    {
        // Per AGENTS.md "When a record contains ReadOnlyMemory<byte>, override Equals
        // + GetHashCode so byte-content participates." Without the override, default
        // record equality compares ReadOnlyMemory<byte> by reference: two records with
        // identical bytes (constructed independently) compare unequal.
        DAPublishRequest Mk(byte[] payload) => new()
        {
            ChainId = 1001, BatchNumber = 7, Payload = payload,
        };
        var a = Mk([0x01, 0x02, 0x03]);
        var b = Mk([0x01, 0x02, 0x03]);
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, Mk([0x01, 0x02, 0xFF]));
    }

    [TestMethod]
    public void DAReceipt_DistinguishesByPointerContent()
    {
        DAReceipt Mk(byte[] pointer) => new()
        {
            Commitment = UInt256.Zero, Pointer = pointer, Layer = DAMode.NeoFS,
        };
        var a = Mk([0xCA, 0xFE]);
        var b = Mk([0xCA, 0xFE]);
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, Mk([0xDE, 0xAD]));
    }

    [TestMethod]
    public void ProofRequest_DistinguishesByWitnessContent()
    {
        var inputs = new PublicInputs
        {
            ChainId = 1001, BatchNumber = 1,
            PreStateRoot = UInt256.Zero, PostStateRoot = UInt256.Zero, TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero, WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero, L2ToL2MessageRoot = UInt256.Zero,
            L1MessageHash = UInt256.Zero, DACommitment = UInt256.Zero,
            BlockContextHash = UInt256.Zero,
        };
        ProofRequest Mk(byte[] witness) => new()
        {
            PublicInputs = inputs, Witness = witness, Kind = ProofType.Multisig,
        };
        var a = Mk([0x10, 0x20]);
        var b = Mk([0x10, 0x20]);
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, Mk([0x10, 0x21]));
    }

    [TestMethod]
    public void ProofResult_DistinguishesByProofContent()
    {
        ProofResult Mk(byte[] proof) => new()
        {
            Proof = proof, Kind = ProofType.Multisig, PublicInputHash = UInt256.Zero,
        };
        var a = Mk([0xAA, 0xBB]);
        var b = Mk([0xAA, 0xBB]);
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, Mk([0xAA, 0xCC]));
    }
}
