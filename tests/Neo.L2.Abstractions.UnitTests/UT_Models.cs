using System.Numerics;
using System.Reflection;

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

        // doc.md §16.2 — Sequencer + Exit fields default to dBFT committee + permissionless
        // exit (the spec's strongest-guarantee defaults). Pin so a future reorder doesn't
        // accidentally weaken the published security label of every existing chain config.
        Assert.AreEqual(SequencerModel.DbftCommittee, a.Sequencer);
        Assert.AreEqual(ExitModel.Permissionless, a.Exit);

        // Override path: operators on validium / DAC chains can downgrade explicitly.
        var validium = a with { Sequencer = SequencerModel.Centralized, Exit = ExitModel.OperatorAssisted };
        Assert.AreEqual(SequencerModel.Centralized, validium.Sequencer);
        Assert.AreEqual(ExitModel.OperatorAssisted, validium.Exit);
        Assert.AreNotEqual(a, validium);
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
            ChainId = 1U,
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
            ChainId = 1001,
            BatchNumber = 7,
            Payload = payload,
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
            Commitment = UInt256.Zero,
            Pointer = pointer,
            Layer = DAMode.NeoFS,
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
            ChainId = 1001,
            BatchNumber = 1,
            PreStateRoot = UInt256.Zero,
            PostStateRoot = UInt256.Zero,
            TxRoot = UInt256.Zero,
            ReceiptRoot = UInt256.Zero,
            WithdrawalRoot = UInt256.Zero,
            L2ToL1MessageRoot = UInt256.Zero,
            L2ToL2MessageRoot = UInt256.Zero,
            L1MessageHash = UInt256.Zero,
            DACommitment = UInt256.Zero,
            BlockContextHash = UInt256.Zero,
        };
        ProofRequest Mk(byte[] witness) => new()
        {
            PublicInputs = inputs,
            Witness = witness,
            Kind = ProofType.Multisig,
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
            Proof = proof,
            Kind = ProofType.Multisig,
            PublicInputHash = UInt256.Zero,
        };
        var a = Mk([0xAA, 0xBB]);
        var b = Mk([0xAA, 0xBB]);
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, Mk([0xAA, 0xCC]));
    }

    [TestMethod]
    public void BatchStatus_HasExpectedDiscriminants()
    {
        // RpcSettlementClient.GetBatchStatusAsync depends on the 0..4 range to validate
        // the L1 contract's response — pinning here surfaces a future enum renumber as a
        // visible diff rather than silent invalid status acceptance.
        Assert.AreEqual(0, (byte)BatchStatus.Unknown);
        Assert.AreEqual(1, (byte)BatchStatus.Pending);
        Assert.AreEqual(2, (byte)BatchStatus.Challengeable);
        Assert.AreEqual(3, (byte)BatchStatus.Finalized);
        Assert.AreEqual(4, (byte)BatchStatus.Reverted);
    }

    [TestMethod]
    public void SequencerModel_HasExpectedDiscriminants()
    {
        // doc.md §16.2 — security label byte that L2ChainConfig publishes via
        // NeoHub.ChainRegistry. Values are wire-format and must not shift.
        Assert.AreEqual(0, (byte)SequencerModel.Centralized);
        Assert.AreEqual(1, (byte)SequencerModel.DbftCommittee);
        Assert.AreEqual(2, (byte)SequencerModel.Decentralized);
    }

    [TestMethod]
    public void ExitModel_HasExpectedDiscriminants()
    {
        // doc.md §16.2 — security label byte for how users exit the L2.
        Assert.AreEqual(0, (byte)ExitModel.Permissionless);
        Assert.AreEqual(1, (byte)ExitModel.Delayed);
        Assert.AreEqual(2, (byte)ExitModel.OperatorAssisted);
    }

    [TestMethod]
    public void AssetType_HasExpectedDiscriminants()
    {
        // AssetMapping serializes AssetType as a 1-byte field. Pinning the values
        // protects the L1 ↔ L2 wire format across renumbers.
        Assert.AreEqual(0, (byte)AssetType.Gas);
        Assert.AreEqual(1, (byte)AssetType.Neo);
        Assert.AreEqual(2, (byte)AssetType.Nep17);
        Assert.AreEqual(3, (byte)AssetType.Stablecoin);
        Assert.AreEqual(4, (byte)AssetType.Rwa);
        Assert.AreEqual("PlatformUsdt", Enum.GetName(typeof(AssetType), (byte)5));
        Assert.AreEqual("PlatformUsdc", Enum.GetName(typeof(AssetType), (byte)6));
        Assert.AreEqual("PlatformBtc", Enum.GetName(typeof(AssetType), (byte)7));
    }

    [TestMethod]
    public void PlatformAssets_PinNeoAndGasDecimalPolicy()
    {
        Assert.AreEqual((byte)0, PlatformAssets.L1NeoDecimals, "Neo L1 NEO remains indivisible.");
        Assert.AreEqual((byte)8, PlatformAssets.L2NeoDecimals, "Every N4 L2 exposes decimalized native NEO.");
        Assert.AreEqual((byte)8, PlatformAssets.L1GasDecimals);
        Assert.AreEqual((byte)8, PlatformAssets.L2GasDecimals);
        Assert.AreEqual(AssetType.Neo, PlatformAssets.CreateNeoMapping(UInt160.Zero, 1001).AssetType);
        Assert.AreEqual(AssetType.Gas, PlatformAssets.CreateGasMapping(UInt160.Zero, 1001).AssetType);

        Assert.AreEqual("USDT", StaticConst<string>("L2UsdtName"));
        Assert.AreEqual("USDC", StaticConst<string>("L2UsdcName"));
        Assert.AreEqual("BTC", StaticConst<string>("L2BtcName"));
        Assert.AreEqual((byte)6, StaticConst<byte>("L1UsdtDecimals"));
        Assert.AreEqual((byte)6, StaticConst<byte>("L2UsdtDecimals"));
        Assert.AreEqual((byte)6, StaticConst<byte>("L1UsdcDecimals"));
        Assert.AreEqual((byte)6, StaticConst<byte>("L2UsdcDecimals"));
        Assert.AreEqual((byte)8, StaticConst<byte>("L1BtcDecimals"));
        Assert.AreEqual((byte)8, StaticConst<byte>("L2BtcDecimals"));

        var usdt = StaticProperty<UInt160>("L2UsdtAsset");
        var usdc = StaticProperty<UInt160>("L2UsdcAsset");
        var btc = StaticProperty<UInt160>("L2BtcAsset");
        Assert.AreNotEqual(UInt160.Zero, usdt);
        Assert.AreNotEqual(UInt160.Zero, usdc);
        Assert.AreNotEqual(UInt160.Zero, btc);
        Assert.AreEqual(5, new HashSet<UInt160>
        {
            PlatformAssets.L2NeoAsset,
            PlatformAssets.L2GasAsset,
            usdt,
            usdc,
            btc,
        }.Count, "Every platform asset must have a stable, distinct L2 asset id.");

        AssertPlatformMapping("CreateUsdtMapping", "PlatformUsdt", usdt, 6, 6);
        AssertPlatformMapping("CreateUsdcMapping", "PlatformUsdc", usdc, 6, 6);
        AssertPlatformMapping("CreateBtcMapping", "PlatformBtc", btc, 8, 8);

        static T StaticConst<T>(string name)
        {
            var field = typeof(PlatformAssets).GetField(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(field, $"PlatformAssets.{name} must be part of the public platform token catalog.");
            return (T)field.GetRawConstantValue()!;
        }

        static T StaticProperty<T>(string name)
        {
            var property = typeof(PlatformAssets).GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(property, $"PlatformAssets.{name} must expose the canonical L2 token id.");
            return (T)property.GetValue(null)!;
        }

        static void AssertPlatformMapping(string methodName, string assetTypeName, UInt160 l2Asset, byte l1Decimals, byte l2Decimals)
        {
            var method = typeof(PlatformAssets).GetMethod(
                methodName,
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                types: [typeof(UInt160), typeof(uint), typeof(bool)],
                modifiers: null);
            Assert.IsNotNull(method, $"PlatformAssets.{methodName} must create canonical platform mappings.");

            var l1Asset = UInt160.Parse("0x1234567890abcdef1234567890abcdef12345678");
            var mapping = (AssetMapping)method.Invoke(null, [l1Asset, 1001u, true])!;
            var expectedType = (AssetType)Enum.Parse(typeof(AssetType), assetTypeName);
            Assert.AreEqual(l1Asset, mapping.L1Asset);
            Assert.AreEqual(1001u, mapping.L2ChainId);
            Assert.AreEqual(l2Asset, mapping.L2Asset);
            Assert.AreEqual(l1Decimals, mapping.L1Decimals);
            Assert.AreEqual(l2Decimals, mapping.L2Decimals);
            Assert.AreEqual(expectedType, mapping.AssetType);
            Assert.IsTrue(mapping.MintBurn);
            Assert.IsTrue(mapping.LockMint);
            Assert.IsTrue(mapping.Active);
        }
    }

    [TestMethod]
    public void AssetAmount_ScalesBetweenL1AndL2Decimals_AndRejectsLossyDownscale()
    {
        Assert.AreEqual(new BigInteger(100_000_000), AssetAmount.Scale(1, fromDecimals: 0, toDecimals: 8));
        Assert.AreEqual(BigInteger.One, AssetAmount.Scale(100_000_000, fromDecimals: 8, toDecimals: 0));
        Assert.ThrowsExactly<InvalidOperationException>(() => AssetAmount.Scale(1, fromDecimals: 8, toDecimals: 0));
    }

    [TestMethod]
    public void BatchExecutionRequest_DistinguishesByTransactionListContent()
    {
        // BatchExecutionRequest holds IReadOnlyList<ReadOnlyMemory<byte>> and
        // IReadOnlyList<CrossChainMessage>. Default record equality compares both
        // lists by reference. Without the override two requests with identical
        // contents-but-different-list-instances would compare unequal.
        var ctx = new BatchBlockContext
        {
            L1FinalizedHeight = 42,
            FirstBlockTimestamp = 100,
            LastBlockTimestamp = 200,
            SequencerCommitteeHash = UInt256.Zero,
            Network = 5195086u,
        };
        BatchExecutionRequest Mk(byte[][] txs) => new()
        {
            ChainId = 1001,
            BatchNumber = 7,
            PreStateRoot = UInt256.Zero,
            Transactions = txs.Select(t => (ReadOnlyMemory<byte>)t).ToArray(),
            L1MessagesConsumed = Array.Empty<CrossChainMessage>(),
            BlockContext = ctx,
        };
        var a = Mk([[0x01, 0x02], [0x03]]);
        var b = Mk([[0x01, 0x02], [0x03]]);
        Assert.AreEqual(a, b);
        Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        Assert.AreNotEqual(a, Mk([[0x01, 0x02], [0x04]]));
        Assert.AreNotEqual(a, Mk([[0x01, 0x02]]));  // shorter list
    }
}
