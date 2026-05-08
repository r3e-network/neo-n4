using System.Numerics;
using Neo;
using Neo.L2.Faucet.Cli;
using Neo.L2.Persistence;

namespace Neo.L2.Faucet.Cli.UnitTests;

[TestClass]
public class UT_FaucetJournal
{
    private static readonly UInt160 Recipient = UInt160.Parse("0x" + new string('1', 40));

    [TestMethod]
    public void Get_NeverDripped_ReturnsNull()
    {
        using var store = new InMemoryKeyValueStore();
        var journal = new FaucetJournal(store);
        Assert.IsNull(journal.Get(Recipient));
    }

    [TestMethod]
    public void RecordDrip_Then_Get_RoundTripsAllFields()
    {
        using var store = new InMemoryKeyValueStore();
        var journal = new FaucetJournal(store);
        journal.RecordDrip(Recipient, nowUnixSeconds: 1_700_000_000UL, amount: 100_000_000);

        var entry = journal.Get(Recipient);
        Assert.IsNotNull(entry);
        Assert.AreEqual(1_700_000_000UL, entry!.LastDripUnixSeconds);
        Assert.AreEqual(new BigInteger(100_000_000), entry.TotalDripped);
        Assert.AreEqual(1u, entry.TotalCount);
    }

    [TestMethod]
    public void RecordDrip_Multiple_AccumulatesTotalAndCount()
    {
        using var store = new InMemoryKeyValueStore();
        var journal = new FaucetJournal(store);
        journal.RecordDrip(Recipient, 1_700_000_000UL, 100_000_000);
        journal.RecordDrip(Recipient, 1_700_003_600UL, 50_000_000);
        journal.RecordDrip(Recipient, 1_700_007_200UL, 75_000_000);

        var entry = journal.Get(Recipient)!;
        Assert.AreEqual(1_700_007_200UL, entry.LastDripUnixSeconds, "lastDrip is the most recent");
        Assert.AreEqual(new BigInteger(225_000_000), entry.TotalDripped);
        Assert.AreEqual(3u, entry.TotalCount);
    }

    [TestMethod]
    public void RecordDrip_PersistsAcrossInstances()
    {
        // Pin: a faucet process restart preserves rate-limit state. Without
        // persistence, a malicious requester could bounce the process between
        // requests to drain the faucet.
        using var store = new InMemoryKeyValueStore();
        new FaucetJournal(store).RecordDrip(Recipient, 1_700_000_000UL, 100_000_000);
        var entry = new FaucetJournal(store).Get(Recipient);
        Assert.IsNotNull(entry);
        Assert.AreEqual(1_700_000_000UL, entry!.LastDripUnixSeconds);
    }

    [TestMethod]
    public void RecordDrip_ZeroOrNegative_Rejected()
    {
        using var store = new InMemoryKeyValueStore();
        var journal = new FaucetJournal(store);
        Assert.ThrowsExactly<ArgumentException>(() => journal.RecordDrip(Recipient, 0, 0));
        Assert.ThrowsExactly<ArgumentException>(() => journal.RecordDrip(Recipient, 0, -1));
    }
}
