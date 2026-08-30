using System.Buffers.Binary;
using System.Numerics;
using Moq;
using Neo;
using Neo.SmartContract.Testing;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.ChainRegistry. These pin the fix for the ByteString-mutation fault in
/// PauseChain/ResumeChain (which mutated a storage-read <c>byte[]</c> — an immutable NeoVM
/// ByteString — and FAULTed at runtime, silently breaking the censorship/emergency pause path).
/// </summary>
[TestClass]
public class UT_ChainRegistry_Vm
{
    private const int ConfigSize = 91;
    private const int OffsetSecurityLevel = 84;
    private const int OffsetDAMode = 85;
    private const int OffsetActive = 90;
    private static readonly UInt256 GenesisStateRoot = new(Enumerable.Repeat((byte)0xA5, 32).ToArray());

    private static byte[] BuildConfig(uint chainId, byte daMode = 0, byte securityLevel = 0)
    {
        // 91-byte L2ChainConfig. The UInt160 fields are not relevant to these registration
        // compatibility tests, so they may stay zero.
        var c = new byte[ConfigSize];
        BinaryPrimitives.WriteUInt32LittleEndian(c.AsSpan(0, 4), chainId);
        c[OffsetSecurityLevel] = securityLevel;
        c[OffsetDAMode] = daMode;
        c[OffsetActive] = 1; // active
        return c;
    }

    private static NeoHubChainRegistry Deploy() => Deploy(new TestEngine(true));

    private static NeoHubChainRegistry Deploy(TestEngine engine)
    {
        var owner = engine.Sender; // default tx sender is an auto-witnessed signer
        return engine.Deploy<NeoHubChainRegistry>(NeoHubChainRegistry.Nef, NeoHubChainRegistry.Manifest, owner);
    }

    [TestMethod]
    public void RegisterChain_PauseChain_ResumeChain_TogglesActive()
    {
        var reg = Deploy();
        BigInteger chainId = 1001;

        reg.RegisterChain(chainId, BuildConfig(1001, daMode: 0), GenesisStateRoot);
        Assert.IsTrue(reg.IsActive(chainId), "a freshly registered chain must be active");
        Assert.AreEqual(GenesisStateRoot, reg.GetGenesisStateRoot(chainId));

        // PauseChain rewrites the stored config's active byte. Before the fix this FAULTed
        // (SETITEM on a ByteString), so the censorship/emergency pause was a no-op-that-throws.
        reg.PauseChain(chainId);
        Assert.IsFalse(reg.IsActive(chainId), "PauseChain must deactivate the chain");

        reg.ResumeChain(chainId);
        Assert.IsTrue(reg.IsActive(chainId), "ResumeChain must reactivate the chain");
    }

    [TestMethod]
    public void RegisterChain_RejectsOutOfRangeDaMode()
    {
        var reg = Deploy();
        // daMode 99 is out of the 0..3 range — registration must abort (VM FAULT → throw).
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(1001, BuildConfig(1001, daMode: 99), GenesisStateRoot));
    }

    [TestMethod]
    [DataRow(0, 0, true)]
    [DataRow(0, 1, true)]
    [DataRow(0, 2, true)]
    [DataRow(0, 3, true)]
    [DataRow(1, 0, true)]
    [DataRow(1, 1, true)]
    [DataRow(1, 2, true)]
    [DataRow(1, 3, true)]
    [DataRow(2, 0, true)]
    [DataRow(2, 1, true)]
    [DataRow(2, 2, true)]
    [DataRow(2, 3, true)]
    [DataRow(3, 0, true)]
    [DataRow(3, 1, false)]
    [DataRow(3, 2, false)]
    [DataRow(3, 3, false)]
    [DataRow(4, 0, false)]
    [DataRow(4, 1, true)]
    [DataRow(4, 2, true)]
    [DataRow(4, 3, true)]
    public void RegisterChain_EnforcesSecurityAndDaCompatibility(
        int securityLevel,
        int daMode,
        bool expectedCompatible)
    {
        var reg = Deploy();
        var config = BuildConfig(1001, (byte)daMode, (byte)securityLevel);

        if (!expectedCompatible)
        {
            Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
                () => reg.RegisterChain(1001, config, GenesisStateRoot));
            Assert.IsFalse(reg.IsActive(1001), "rejected config must not be persisted");
            return;
        }

        reg.RegisterChain(1001, config, GenesisStateRoot);
        CollectionAssert.AreEqual(config, reg.GetChainConfig(1001));
    }

    [TestMethod]
    public void RegisterChain_RejectsOutOfRangeSecurityLevel()
    {
        var reg = Deploy();

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(1001, BuildConfig(1001, securityLevel: 99), GenesisStateRoot));
    }

    [TestMethod]
    public void UpdateChain_RejectsContradictorySecurityAndDaWithoutChangingStoredConfig()
    {
        var reg = Deploy();
        var original = BuildConfig(1001, daMode: 0, securityLevel: 3);
        reg.RegisterChain(1001, original, GenesisStateRoot);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.UpdateChain(1001, BuildConfig(1001, daMode: 1, securityLevel: 3)));

        CollectionAssert.AreEqual(original, reg.GetChainConfig(1001));
    }

    [TestMethod]
    public void RegisterChain_GenesisStateRoot_IsRequiredAndImmutable()
    {
        var reg = Deploy();
        var config = BuildConfig(1001);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(1001, config, UInt256.Zero));
        Assert.IsFalse(reg.IsActive(1001));
        Assert.AreEqual(UInt256.Zero, reg.GetGenesisStateRoot(1001));

        reg.RegisterChain(1001, config, GenesisStateRoot);
        reg.RegisterChain(1001, config, GenesisStateRoot);
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChain(
                1001,
                config,
                new UInt256(Enumerable.Repeat((byte)0x5A, 32).ToArray())));
        Assert.AreEqual(GenesisStateRoot, reg.GetGenesisStateRoot(1001));
    }

    [TestMethod]
    public void RegisterChainPublic_RequiresAndPersistsImmutableGenesisStateRoot()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);
        var governanceHash = UInt160.Parse("0x" + new string('7', 40));
        engine.FromHash<NeoHubGovernanceController>(
            governanceHash,
            governance => governance
                .Setup(controller => controller.AdmissionMode)
                .Returns((BigInteger?)2),
            checkExistence: false);
        reg.GovernanceController = governanceHash;
        var config = BuildConfig(1001);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChainPublic(1001, config, UInt256.Zero));
        Assert.AreEqual(UInt256.Zero, reg.GetGenesisStateRoot(1001));

        reg.RegisterChainPublic(1001, config, GenesisStateRoot);
        Assert.AreEqual(GenesisStateRoot, reg.GetGenesisStateRoot(1001));
        CollectionAssert.AreEqual(config, reg.GetChainConfig(1001));
    }

    [TestMethod]
    [DataRow(-1)]
    [DataRow(3)]
    [DataRow(258)]
    public void RegisterChainPublic_RejectsInvalidAdmissionModeWithoutPersistingState(int admissionMode)
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);
        var governanceHash = UInt160.Parse("0x" + new string('7', 40));
        engine.FromHash<NeoHubGovernanceController>(
            governanceHash,
            governance => governance
                .Setup(controller => controller.AdmissionMode)
                .Returns((BigInteger?)admissionMode),
            checkExistence: false);
        reg.GovernanceController = governanceHash;

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterChainPublic(1001, BuildConfig(1001), GenesisStateRoot));

        Assert.IsFalse(reg.IsActive(1001));
        Assert.AreEqual(UInt256.Zero, reg.GetGenesisStateRoot(1001));
        CollectionAssert.AreEqual(Array.Empty<byte>(), reg.GetChainConfig(1001));
    }

    [TestMethod]
    public void SetGovernanceController_AfterGovernanceLock_RejectsReplacement()
    {
        var reg = Deploy();
        var original = UInt160.Parse("0x" + new string('7', 40));
        var replacement = UInt160.Parse("0x" + new string('8', 40));
        reg.GovernanceController = original;
        reg.LockGovernance();

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.GovernanceController = replacement);

        Assert.IsTrue(reg.IsGovernanceLocked);
        Assert.AreEqual(original, reg.GovernanceController);
    }

    // ── §7.1 pauser surface: instant paths close at the lock, council paths stay open ──────────

    private static readonly UInt160 PauserA = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 PauserB = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 GovHash = UInt160.Parse("0x" + new string('7', 40));

    /// <summary>Wire a GovernanceController mock whose two read-only proposal checks answer
    /// <paramref name="approved"/> and <paramref name="payloadMatches"/> independently, so a test can
    /// pin the council-approval gate and the payload-binding gate separately.</summary>
    private static void WireGc(TestEngine engine, bool approved = true, bool payloadMatches = true) =>
        engine.FromHash<NeoHubGovernanceController>(GovHash, m =>
        {
            m.Setup(g => g.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(approved);
            m.Setup(g => g.MatchesProposalPayload(It.IsAny<BigInteger?>(), It.IsAny<byte[]?>()))
                .Returns(payloadMatches);
        }, checkExistence: false);

    /// <summary>Wire a GovernanceController mock that approves only the listed action bytes. This is
    /// what makes a payload-binding test mean something: any other action faults the gate.</summary>
    private static void WireGcBound(TestEngine engine, params byte[][] votedActions) =>
        engine.FromHash<NeoHubGovernanceController>(GovHash, m =>
        {
            m.Setup(g => g.IsApprovedAndTimelocked(It.IsAny<BigInteger?>())).Returns(true);
            m.Setup(g => g.MatchesProposalPayload(It.IsAny<BigInteger?>(),
                    It.Is<byte[]?>(a => MatchesAny(a, votedActions))))
                .Returns(true);
        }, checkExistence: false);

    // Outside the Moq predicate, which is compiled as an expression tree and cannot carry a loop.
    private static bool MatchesAny(byte[]? actual, byte[][] votedActions)
    {
        if (actual is null) return false;
        foreach (var voted in votedActions)
            if (actual.SequenceEqual(voted)) return true;
        return false;
    }

    private static void LockWithGovernance(NeoHubChainRegistry reg)
    {
        reg.GovernanceController = GovHash;
        reg.LockGovernance();
    }

    [TestMethod]
    public void PauserSurface_RevertsOnceGovernanceLocked()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine); // owner == engine.Sender
        reg.RegisterPauser(PauserA);
        Assert.IsTrue(reg.IsPauser(PauserA)!.Value, "instant path is open before the lock");

        LockWithGovernance(reg);

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterPauser(PauserB),
            "a locked owner must not be able to add a chain pauser");
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RevokePauser(PauserA),
            "a locked owner must not be able to revoke the honest pauser either");
        Assert.IsFalse(reg.IsPauser(PauserB)!.Value, "rejected register must not touch the set");
        Assert.IsTrue(reg.IsPauser(PauserA)!.Value, "rejected revoke must not touch the set");
    }

    [TestMethod]
    public void RegisterPauserViaProposal_RequiresGcWired()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine); // GC not wired

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterPauserViaProposal(PauserA, 1),
            "the council path needs a GovernanceController to consult");
        Assert.IsFalse(reg.IsPauser(PauserA)!.Value);
    }

    [TestMethod]
    public void RegisterPauserViaProposal_NotApproved_Faults()
    {
        var engine = new TestEngine(true);
        WireGc(engine, approved: false, payloadMatches: true);
        var reg = Deploy(engine);
        reg.GovernanceController = GovHash;

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterPauserViaProposal(PauserA, 1),
            "an un-approved / un-timelocked proposal must not authorize a pauser");
        Assert.IsFalse(reg.IsPauser(PauserA)!.Value, "rejected proposal must not set state");
    }

    [TestMethod]
    public void RegisterPauserViaProposal_PayloadMismatch_Faults()
    {
        // Approved + timelocked, but the council voted on different bytes: the blank-check defense.
        var engine = new TestEngine(true);
        WireGc(engine, approved: true, payloadMatches: false);
        var reg = Deploy(engine);
        reg.GovernanceController = GovHash;

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterPauserViaProposal(PauserA, 1),
            "a proposal whose payload does not match the action args must be rejected");
        Assert.IsFalse(reg.IsPauser(PauserA)!.Value);
    }

    [TestMethod]
    public void PauserViaProposal_BindsVotedPauser_Replays_AndSurvivesLock()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);
        WireGcBound(engine,
            reg.BuildRegisterPauserAction(PauserA)!,
            reg.BuildRevokePauserAction(PauserA)!,
            reg.BuildRegisterPauserAction(PauserB)!);
        reg.GovernanceController = GovHash;

        // A vote bound to PauserA cannot admit PauserC, and a fresh id grants nothing by itself.
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterPauserViaProposal(UInt160.Parse("0x" + new string('c', 40)), 10),
            "an id the council never spent on this pauser is not authority");

        reg.RegisterPauserViaProposal(PauserA, 11);
        Assert.IsTrue(reg.IsPauser(PauserA)!.Value,
            "an approved + bound proposal must authorize the pauser");

        // One proposal, one application — and the consumed namespace is shared across the pauser
        // actions, so a spent register vote cannot be re-spent as a revoke. Both calls below present
        // payloads the council did approve, so only consumption can fault them.
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterPauserViaProposal(PauserA, 11), "a consumed proposal cannot be replayed");
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RevokePauserViaProposal(PauserA, 11),
            "a consumed proposal cannot be re-spent on a different action");

        // The lock closes the instant paths but must not strand incident response: retiring a
        // compromised pauser is exactly what the council path is for.
        LockWithGovernance(reg);
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RevokePauser(PauserA), "instant revoke stays closed after locking");
        reg.RevokePauserViaProposal(PauserA, 12);
        Assert.IsFalse(reg.IsPauser(PauserA)!.Value,
            "the council must be able to retire a pauser once governance is locked");
        reg.RegisterPauserViaProposal(PauserB, 13);
        Assert.IsTrue(reg.IsPauser(PauserB)!.Value,
            "the council must be able to admit a replacement pauser once governance is locked");
    }

    [TestMethod]
    public void UpdateChainViaProposal_StillApplies_AndSharesTheConsumedNamespace()
    {
        var engine = new TestEngine(true);
        var reg = Deploy(engine);
        var original = BuildConfig(1001, daMode: 0);
        var updated = BuildConfig(1001, daMode: 1);
        reg.RegisterChain(1001, original, GenesisStateRoot);
        WireGcBound(engine, reg.BuildUpdateChainAction(1001, updated)!);
        reg.GovernanceController = GovHash;

        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.UpdateChainViaProposal(1001, original, 20),
            "the refactor must not have loosened payload binding");
        CollectionAssert.AreEqual(original, reg.GetChainConfig(1001));

        reg.UpdateChainViaProposal(1001, updated, 21);
        CollectionAssert.AreEqual(updated, reg.GetChainConfig(1001),
            "an approved + bound config proposal must apply");

        // 21 was spent by the config path. The shared consumed namespace is what stops that same
        // council vote from also authorizing an unrelated pauser admission.
        Assert.ThrowsExactly<Neo.SmartContract.Testing.Exceptions.TestException>(
            () => reg.RegisterPauserViaProposal(PauserA, 21),
            "one proposal id must be spendable exactly once across every *ViaProposal path");
        Assert.IsFalse(reg.IsPauser(PauserA)!.Value);
    }

    [TestMethod]
    public void BuildPauserActions_UseCanonicalTagEncoding()
    {
        var reg = Deploy();
        var pauserBytes = PauserA.GetSpan().ToArray();

        // The tags are hand-built byte arrays on-chain, so the only thing that catches a mistyped
        // spelling is comparing them against the off-chain ASCII the council actually signs.
        CollectionAssert.AreEqual(Concat(
            System.Text.Encoding.ASCII.GetBytes("neo4-gov:registerPauser"), pauserBytes),
            reg.BuildRegisterPauserAction(PauserA)!,
            "registerPauser action must be tag||pauser (43B)");
        CollectionAssert.AreEqual(Concat(
            System.Text.Encoding.ASCII.GetBytes("neo4-gov:revokePauser"), pauserBytes),
            reg.BuildRevokePauserAction(PauserA)!,
            "revokePauser action must be tag||pauser (41B)");

        Assert.IsFalse(reg.BuildRegisterPauserAction(PauserA)!.AsSpan()
                .SequenceEqual(reg.BuildRevokePauserAction(PauserA)!),
            "a vote to add must never be executable as a vote to remove");
        Assert.IsFalse(reg.BuildRegisterPauserAction(PauserA)!.AsSpan()
                .SequenceEqual(reg.BuildRegisterPauserAction(PauserB)!),
            "the voted pauser must participate in the binding");
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var total = 0;
        foreach (var part in parts) total += part.Length;
        var buf = new byte[total];
        var pos = 0;
        foreach (var part in parts)
        {
            Array.Copy(part, 0, buf, pos, part.Length);
            pos += part.Length;
        }
        return buf;
    }
}
