using System.Numerics;
using Neo.L2.Faucet.Cli;

namespace Neo.L2.Faucet.Cli.UnitTests;

[TestClass]
public class UT_FaucetPolicy
{
    private static readonly FaucetPolicy Tight = new()
    {
        CooldownSeconds = 100,
        MaxPerDrip = 1000,
        MaxPerRecipientLifetime = 10_000,
    };

    [TestMethod]
    public void NeverDripped_AllowsAnyAmountUpToMaxPerDrip()
    {
        Assert.IsTrue(Tight.Decide(amount: 500, nowUnixSeconds: 1000, entry: null).Allowed);
        Assert.IsTrue(Tight.Decide(amount: 1000, nowUnixSeconds: 1000, entry: null).Allowed);
    }

    [TestMethod]
    public void AmountAboveMaxPerDrip_Rejected()
    {
        var d = Tight.Decide(amount: 1001, nowUnixSeconds: 1000, entry: null);
        Assert.IsFalse(d.Allowed);
        StringAssert.Contains(d.RejectReason!, "exceeds per-drip cap");
    }

    [TestMethod]
    public void NonPositiveAmount_Rejected()
    {
        Assert.IsFalse(Tight.Decide(0, 1000, null).Allowed);
        Assert.IsFalse(Tight.Decide(-1, 1000, null).Allowed);
    }

    [TestMethod]
    public void Cooldown_DripsTooSoon_Rejected()
    {
        // last drip 50s ago, cooldown is 100s — should reject with remaining time.
        var entry = new FaucetEntry { LastDripUnixSeconds = 1000, TotalDripped = 100, TotalCount = 1 };
        var d = Tight.Decide(amount: 500, nowUnixSeconds: 1050, entry: entry);
        Assert.IsFalse(d.Allowed);
        StringAssert.Contains(d.RejectReason!, "cooldown active");
        StringAssert.Contains(d.RejectReason!, "50s ago");
        StringAssert.Contains(d.RejectReason!, "another 50s");
    }

    [TestMethod]
    public void Cooldown_DripsAfterCooldownElapsed_Allowed()
    {
        var entry = new FaucetEntry { LastDripUnixSeconds = 1000, TotalDripped = 100, TotalCount = 1 };
        Assert.IsTrue(Tight.Decide(500, 1100, entry).Allowed);
    }

    [TestMethod]
    public void LifetimeCap_Exceeded_Rejected()
    {
        // Already dripped 9500; trying to drip 1000 would push to 10500 > 10000.
        var entry = new FaucetEntry { LastDripUnixSeconds = 1000, TotalDripped = 9500, TotalCount = 19 };
        var d = Tight.Decide(amount: 1000, nowUnixSeconds: 2000, entry: entry);
        Assert.IsFalse(d.Allowed);
        StringAssert.Contains(d.RejectReason!, "lifetime cap exceeded");
    }

    [TestMethod]
    public void LifetimeCap_ExactlyAtCap_Allowed()
    {
        // Already dripped 9000; trying to drip 1000 exactly hits 10000 == cap.
        // The test pins inclusive vs exclusive: cap of 10000 means total <= 10000 is OK.
        var entry = new FaucetEntry { LastDripUnixSeconds = 1000, TotalDripped = 9000, TotalCount = 18 };
        Assert.IsTrue(Tight.Decide(1000, 2000, entry).Allowed,
            "amount that brings total exactly to MaxPerRecipientLifetime is allowed");
    }

    [TestMethod]
    public void DefaultPolicy_HasReasonableValues()
    {
        // Pin defaults so a refactor that changes them surfaces here.
        Assert.AreEqual(3600u, FaucetPolicy.Default.CooldownSeconds);
        Assert.AreEqual(new BigInteger(100_000_000), FaucetPolicy.Default.MaxPerDrip);  // 1 GAS
        Assert.AreEqual(new BigInteger(10_000_000_000), FaucetPolicy.Default.MaxPerRecipientLifetime);  // 100 GAS
    }
}
