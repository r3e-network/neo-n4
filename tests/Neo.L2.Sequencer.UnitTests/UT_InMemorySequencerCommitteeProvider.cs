using Neo.Cryptography.ECC;

namespace Neo.L2.Sequencer.UnitTests;

[TestClass]
public class UT_InMemorySequencerCommitteeProvider
{
    private static ECPoint K(byte seed)
    {
        var priv = new byte[32];
        for (var i = 0; i < 32; i++) priv[i] = (byte)(seed + i);
        return ECCurve.Secp256r1.G * priv;
    }

    private static UInt160 A(byte b)
    {
        var bytes = new byte[20];
        for (var i = 0; i < 20; i++) bytes[i] = b;
        return new UInt160(bytes);
    }

    [TestMethod]
    public async Task Register_AddsActiveMember()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        p.Register(K(1), A(0xAA));

        var committee = await p.GetActiveCommitteeAsync();
        Assert.AreEqual(1, committee.Count);
        Assert.AreEqual(1, committee[0].Status);
        Assert.AreEqual(0u, committee[0].ExitsAtUnixSeconds);
        Assert.IsTrue(await p.IsRegisteredAsync(K(1)));
    }

    [TestMethod]
    public void Register_RejectsNullL1Address()
    {
        // Regression for iter 148: null l1Address would silently propagate into
        // CommitteeMember.L1Address → CensorshipReport.ResponsibleSequencerAddress →
        // either misroutes the slash payout or hard-reverts the slash transaction at L1.
        // Now caught at the API boundary.
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        Assert.ThrowsExactly<ArgumentNullException>(() => p.Register(K(1), null!));
    }

    [TestMethod]
    public void Register_DuplicateThrows()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        p.Register(K(1), A(0xAA));
        Assert.ThrowsExactly<InvalidOperationException>(() => p.Register(K(1), A(0xAA)));
    }

    [TestMethod]
    public void Register_HonorsMaxCommitteeSize()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 2);
        p.Register(K(1), A(0xAA));
        p.Register(K(2), A(0xBB));
        Assert.ThrowsExactly<InvalidOperationException>(() => p.Register(K(3), A(0xCC)));
    }

    [TestMethod]
    public async Task BeginExit_TransitionsStatus()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        p.Register(K(1), A(0xAA));
        p.BeginExit(K(1), 1_700_005_000);

        var committee = await p.GetActiveCommitteeAsync();
        Assert.AreEqual(1, committee.Count);                  // still in committee while exiting
        Assert.AreEqual(2, committee[0].Status);              // 2 = Exiting
        Assert.AreEqual(1_700_005_000u, committee[0].ExitsAtUnixSeconds);
    }

    [TestMethod]
    public void Finalize_WaitsForExitWindow()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        p.Register(K(1), A(0xAA));
        p.BeginExit(K(1), 1_700_005_000);

        // Before window closes — must throw.
        Assert.ThrowsExactly<InvalidOperationException>(() => p.Finalize(K(1), 1_700_004_999));

        // After window — succeeds.
        p.Finalize(K(1), 1_700_005_000);
    }

    [TestMethod]
    public void Finalize_RequiresExitingStatus()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        p.Register(K(1), A(0xAA));
        Assert.ThrowsExactly<InvalidOperationException>(() => p.Finalize(K(1), 1_700_000_000));
    }

    [TestMethod]
    public async Task Finalize_RemovesFromCommittee()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        p.Register(K(1), A(0xAA));
        p.Register(K(2), A(0xBB));
        p.BeginExit(K(1), 1_700_005_000);
        p.Finalize(K(1), 1_700_005_000);

        var committee = await p.GetActiveCommitteeAsync();
        Assert.AreEqual(1, committee.Count);
        Assert.AreEqual(K(2), committee[0].PublicKey);
        Assert.IsFalse(await p.IsRegisteredAsync(K(1)));
    }

    [TestMethod]
    public async Task SetMaxCommitteeSize_UpdatesQuery()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        Assert.AreEqual(7, await p.GetMaxCommitteeSizeAsync());
        p.SetMaxCommitteeSize(21);
        Assert.AreEqual(21, await p.GetMaxCommitteeSizeAsync());
    }

    [TestMethod]
    public void SetMaxCommitteeSize_RangeChecked()
    {
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => p.SetMaxCommitteeSize(0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => p.SetMaxCommitteeSize(65));
    }

    [TestMethod]
    public void Constructor_ValidatesMaxCommitteeSize()
    {
        // Regression for iter 190: previously the ctor accepted maxCommitteeSize = 0
        // (every Register would throw "full" silently), -1 (same), or > 64 (exceeds
        // dBFT 2.0 practical committee bound). Symmetric with SetMaxCommitteeSize.
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new InMemorySequencerCommitteeProvider(1001, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new InMemorySequencerCommitteeProvider(1001, -1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(
            () => new InMemorySequencerCommitteeProvider(1001, 65));
        // Boundary: 1 and 64 must succeed.
        new InMemorySequencerCommitteeProvider(1001, 1);
        new InMemorySequencerCommitteeProvider(1001, 64);
    }

    [TestMethod]
    public void SetMaxCommitteeSize_RejectsShrinkBelowCurrentCount()
    {
        // Regression: previously SetMaxCommitteeSize(2) on a 5-member committee silently
        // succeeded. The provider's count exceeded the cap until members exited
        // organically — a misleading "almost-frozen" state that hid the operator's typo.
        var p = new InMemorySequencerCommitteeProvider(1001, 7);
        for (byte i = 1; i <= 5; i++)
            p.Register(K(i), A(i));

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => p.SetMaxCommitteeSize(2));
        StringAssert.Contains(ex.Message, "max 2");
        StringAssert.Contains(ex.Message, "current committee count 5");
    }
}
