using System.Numerics;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.DARegistry — the per-batch DA commitment registry. These execute the
/// deploy / record / read / owner-transfer paths in a real NeoVM and pin the security-critical
/// invariants:
///   * deploy rejects a zero/invalid owner or settlement-manager (typo'd wiring surfaced eagerly),
///   * Record is settlement-manager-gated (witness OR calling-script identity) and a non-SM caller
///     cannot write a commitment,
///   * the daMode range guard (0..3) rejects out-of-range modes so no GetMode reader sees garbage,
///   * recorded (chainId, batchNumber) tuples are isolated keys — one batch's commitment/mode never
///     bleeds into another's, and absent keys read back as the zero sentinel,
///   * SetOwner is owner-witness-gated (positive AND negative) and rejects a zero/invalid new owner.
/// </summary>
[TestClass]
public class UT_DARegistry_Vm
{
    private const uint ChainA = 1001;
    private const uint ChainB = 2002;

    private static readonly UInt256 CommitA = UInt256.Parse("0x" + new string('1', 64));
    private static readonly UInt256 CommitB = UInt256.Parse("0x" + new string('2', 64));
    private static readonly UInt160 Stranger = UInt160.Parse("0x" + new string('a', 40));

    /// <summary>
    /// Deploy the registry. owner/settlementManager default to engine.Sender so the owner and
    /// settlement-manager witness checks pass; pass an explicit principal to exercise the negative
    /// authorization paths (CheckWitness fails for an account the test signer does not control).
    /// </summary>
    private static NeoHubDARegistry Deploy(TestEngine engine, UInt160? owner = null, UInt160? settlementManager = null)
    {
        var o = owner ?? engine.Sender;
        var sm = settlementManager ?? engine.Sender;
        return engine.Deploy<NeoHubDARegistry>(
            NeoHubDARegistry.Nef, NeoHubDARegistry.Manifest, new object[] { o, sm });
    }

    [TestMethod]
    public void Deploy_RejectsZeroOwner_AndZeroSettlementManager()
    {
        var engine = new TestEngine(true);

        // Zero owner -> _deploy assert "invalid owner" -> VM FAULT.
        Assert.ThrowsExactly<TestException>(() => Deploy(engine, owner: UInt160.Zero),
            "deploy must reject a zero owner");

        // Zero settlement manager -> _deploy assert "invalid settlement manager" -> VM FAULT.
        var engine2 = new TestEngine(true);
        Assert.ThrowsExactly<TestException>(() => Deploy(engine2, settlementManager: UInt160.Zero),
            "deploy must reject a zero settlement manager");
    }

    [TestMethod]
    public void Deploy_StoresOwner_Readable()
    {
        var engine = new TestEngine(true);
        var da = Deploy(engine);
        Assert.AreEqual(engine.Sender, da.Owner!, "deploy must persist the owner");
    }

    [TestMethod]
    public void Record_BySettlementManager_StoresCommitmentAndMode()
    {
        var engine = new TestEngine(true);
        var da = Deploy(engine); // settlementManager == engine.Sender -> CheckWitness(sm) passes

        // Absent before recording: zero sentinels.
        Assert.AreEqual(UInt256.Zero, da.GetCommitment(ChainA, 1)!, "no commitment before Record");
        Assert.AreEqual((BigInteger)0, da.GetMode(ChainA, 1)!, "no mode before Record");

        da.Record(ChainA, 1, CommitA, 2); // daMode=External(2)

        Assert.AreEqual(CommitA, da.GetCommitment(ChainA, 1)!, "commitment must read back");
        Assert.AreEqual((BigInteger)2, da.GetMode(ChainA, 1)!, "mode must read back");
    }

    [TestMethod]
    public void Record_NonSettlementManager_Faults_NoStateWritten()
    {
        var engine = new TestEngine(true);
        // SM is an account the test signer does not control: neither CheckWitness nor a matching
        // CallingScriptHash holds, so the gate must reject.
        var da = Deploy(engine, settlementManager: Stranger);

        Assert.ThrowsExactly<TestException>(() => da.Record(ChainA, 1, CommitA, 0),
            "Record is settlement-manager-gated");
        // The rejected write must not leak any state.
        Assert.AreEqual(UInt256.Zero, da.GetCommitment(ChainA, 1)!, "rejected Record must not store a commitment");
        Assert.AreEqual((BigInteger)0, da.GetMode(ChainA, 1)!, "rejected Record must not store a mode");
    }

    [TestMethod]
    public void Record_RejectsOutOfRangeDaMode()
    {
        var engine = new TestEngine(true);
        var da = Deploy(engine);

        // daMode 4 and 99 are outside the documented L1/NeoFS/External/DAC (0..3) range.
        // The audit-hardening guard rejects them so no later GetMode reader interprets garbage.
        Assert.ThrowsExactly<TestException>(() => da.Record(ChainA, 1, CommitA, 4),
            "daMode 4 is out of range (0..3)");
        Assert.ThrowsExactly<TestException>(() => da.Record(ChainA, 1, CommitA, 99),
            "daMode 99 is out of range (0..3)");

        // Boundary values 0 and 3 must be accepted.
        da.Record(ChainA, 2, CommitA, 0);
        Assert.AreEqual((BigInteger)0, da.GetMode(ChainA, 2)!, "daMode 0 (L1) is valid");
        da.Record(ChainA, 3, CommitB, 3);
        Assert.AreEqual((BigInteger)3, da.GetMode(ChainA, 3)!, "daMode 3 (DAC) is valid");
        // The rejected writes for batch 1 left no state behind.
        Assert.AreEqual(UInt256.Zero, da.GetCommitment(ChainA, 1)!, "out-of-range Record must not persist");
    }

    [TestMethod]
    public void Record_KeysAreIsolatedPerChainAndBatch()
    {
        var engine = new TestEngine(true);
        var da = Deploy(engine);

        da.Record(ChainA, 1, CommitA, 1); // NeoFS
        da.Record(ChainB, 1, CommitB, 3); // DAC, same batch number, different chain
        da.Record(ChainA, 2, CommitB, 0); // L1, same chain, different batch

        // Each (chainId, batchNumber) tuple is an independent key.
        Assert.AreEqual(CommitA, da.GetCommitment(ChainA, 1)!);
        Assert.AreEqual((BigInteger)1, da.GetMode(ChainA, 1)!);

        Assert.AreEqual(CommitB, da.GetCommitment(ChainB, 1)!, "same batch number on a different chain is a distinct slot");
        Assert.AreEqual((BigInteger)3, da.GetMode(ChainB, 1)!);

        Assert.AreEqual(CommitB, da.GetCommitment(ChainA, 2)!, "same chain, different batch is a distinct slot");
        Assert.AreEqual((BigInteger)0, da.GetMode(ChainA, 2)!);

        // An entirely unrecorded tuple still reads back as the zero sentinel.
        Assert.AreEqual(UInt256.Zero, da.GetCommitment(ChainB, 99)!);
        Assert.AreEqual((BigInteger)0, da.GetMode(ChainB, 99)!);
    }

    [TestMethod]
    public void Record_OverwritesSameKey_LatestWins()
    {
        var engine = new TestEngine(true);
        var da = Deploy(engine);

        da.Record(ChainA, 5, CommitA, 0);
        Assert.AreEqual(CommitA, da.GetCommitment(ChainA, 5)!);
        Assert.AreEqual((BigInteger)0, da.GetMode(ChainA, 5)!);

        // Re-recording the same tuple replaces both commitment and mode (sealing is SM-authoritative).
        da.Record(ChainA, 5, CommitB, 2);
        Assert.AreEqual(CommitB, da.GetCommitment(ChainA, 5)!, "latest commitment wins on the same key");
        Assert.AreEqual((BigInteger)2, da.GetMode(ChainA, 5)!, "latest mode wins on the same key");
    }

    [TestMethod]
    public void SetOwner_ByOwner_TransfersOwnership()
    {
        var engine = new TestEngine(true);
        var da = Deploy(engine); // owner == engine.Sender -> owner witness present

        da.Owner = Stranger;
        Assert.AreEqual(Stranger, da.Owner!, "owner transfer must persist the new owner");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is an account the test signer does not control -> the owner gate must reject.
        var da = Deploy(engine, owner: Stranger);

        Assert.ThrowsExactly<TestException>(() => da.Owner = engine.Sender,
            "SetOwner is owner-witness-gated");
        Assert.AreEqual(Stranger, da.Owner!, "a rejected transfer must leave the owner unchanged");
    }

    [TestMethod]
    public void SetOwner_RejectsZeroNewOwner()
    {
        var engine = new TestEngine(true);
        var da = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => da.Owner = UInt160.Zero,
            "SetOwner must reject a zero new owner");
        Assert.AreEqual(engine.Sender, da.Owner!, "a rejected transfer must leave the owner unchanged");
    }

    [TestMethod]
    public void SetOwner_NewOwner_CanThenTransferAgain_OldOwnerLocked()
    {
        var engine = new TestEngine(true);
        // Deploy with the test signer as the initial owner so the first transfer is authorized.
        var da = Deploy(engine);

        // Transfer to a stranger the signer does not control.
        da.Owner = Stranger;
        Assert.AreEqual(Stranger, da.Owner!);

        // The old owner (engine.Sender) no longer holds the gate: a further transfer must fault.
        Assert.ThrowsExactly<TestException>(() => da.Owner = engine.Sender,
            "the previous owner must lose authority after transfer");
        Assert.AreEqual(Stranger, da.Owner!, "ownership stays with the new owner");
    }
}
