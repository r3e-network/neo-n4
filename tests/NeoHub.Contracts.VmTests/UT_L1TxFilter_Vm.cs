using System.Numerics;
using Neo;
using Neo.SmartContract.Testing;
using Neo.SmartContract.Testing.Exceptions;

namespace NeoHub.Contracts.VmTests;

/// <summary>
/// VM-level tests for NeoHub.L1TxFilter — the per-chain L1→L2 transaction policy hook that
/// MessageRouter.EnqueueL1ToL2 consults. The contract has no cross-contract dependencies; it is a
/// self-contained allow/deny rule store guarded by an owner witness. These tests execute the contract
/// in a real NeoVM and pin the security-critical invariants:
///   - ownership is witness-gated for every mutating method (positive AND negative paths),
///   - ownership transfer rejects zero/invalid successors (so the contract can't be bricked),
///   - rule writes validate their subject address and the rule code domain (0..2),
///   - the read-only AcceptL1ToL2 hook enforces input validation (chainId!=0, valid non-zero
///     sender/receiver, payload size cap) and applies allow > deny > unset→default precedence so a
///     denied sender/receiver/message-type cannot slip a message past the router.
/// </summary>
[TestClass]
public class UT_L1TxFilter_Vm
{
    private const uint ChainA = 1001;

    private static readonly UInt160 Sender = UInt160.Parse("0x" + new string('a', 40));
    private static readonly UInt160 Receiver = UInt160.Parse("0x" + new string('b', 40));
    private static readonly UInt160 OtherOwner = UInt160.Parse("0x" + new string('1', 40));
    private static readonly UInt160 NewOwner = UInt160.Parse("0x" + new string('2', 40));

    private const byte MsgType = 7;
    private const byte RuleUnset = 0;
    private const byte RuleAllow = 1;
    private const byte RuleDeny = 2;

    /// <summary>Deploy the filter. <paramref name="owner"/> defaults to engine.Sender so the
    /// owner witness checks pass; pass a different principal to exercise the negative auth path.</summary>
    private static NeoHubL1TxFilter Deploy(TestEngine engine, UInt160? owner = null)
    {
        var o = owner ?? engine.Sender;
        // _deploy casts `data` directly to UInt160, so the owner is passed as a single deploy
        // argument (not wrapped in an object[] array — that is only for multi-field deploy data).
        return engine.Deploy<NeoHubL1TxFilter>(
            NeoHubL1TxFilter.Nef, NeoHubL1TxFilter.Manifest, o);
    }

    // ---------------------------------------------------------------------------------------------
    // Deploy / ownership
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void Deploy_RejectsZeroOwner()
    {
        var engine = new TestEngine(true);
        // _deploy asserts owner.IsValid && !owner.IsZero — a zero owner would leave the contract
        // permanently unauthorizable, so it must fault at construction.
        Assert.ThrowsExactly<TestException>(() => engine.Deploy<NeoHubL1TxFilter>(
            NeoHubL1TxFilter.Nef, NeoHubL1TxFilter.Manifest, UInt160.Zero),
            "zero owner must be rejected at deploy");
    }

    [TestMethod]
    public void Deploy_SetsOwnerAndDefaultAllowTrue()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.AreEqual(engine.Sender, c.Owner, "deploy records the supplied owner");
        Assert.IsTrue(c.DefaultAllow!.Value, "deploy defaults to allow (open) policy");
    }

    [TestMethod]
    public void SetOwner_ByOwner_TransfersOwnership()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (witnessed)

        c.Owner = NewOwner;
        Assert.AreEqual(NewOwner, c.Owner, "owner can transfer ownership");
    }

    [TestMethod]
    public void SetOwner_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        // Owner is a different account than the test signer -> CheckWitness(owner) fails.
        var c = Deploy(engine, owner: OtherOwner);

        Assert.ThrowsExactly<TestException>(() => c.Owner = NewOwner,
            "setOwner is owner-gated");
        Assert.AreEqual(OtherOwner, c.Owner, "rejected transfer must not mutate owner");
    }

    [TestMethod]
    public void SetOwner_RejectsZeroSuccessor()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (witnessed)

        // Even the legitimate owner cannot hand ownership to the zero address (would brick governance).
        Assert.ThrowsExactly<TestException>(() => c.Owner = UInt160.Zero,
            "zero successor must be rejected");
        Assert.AreEqual(engine.Sender, c.Owner, "rejected transfer leaves owner unchanged");
    }

    // ---------------------------------------------------------------------------------------------
    // Default policy
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void SetDefaultAllow_ByOwner_TogglesFallback()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.IsTrue(c.DefaultAllow!.Value, "starts open");
        c.DefaultAllow = false;
        Assert.IsFalse(c.DefaultAllow!.Value, "owner can harden to deny-by-default");
        c.DefaultAllow = true;
        Assert.IsTrue(c.DefaultAllow!.Value, "owner can re-open");
    }

    [TestMethod]
    public void SetDefaultAllow_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner);

        Assert.ThrowsExactly<TestException>(() => c.DefaultAllow = false,
            "setDefaultAllow is owner-gated");
        Assert.IsTrue(c.DefaultAllow!.Value, "rejected change must not alter the default policy");
    }

    // ---------------------------------------------------------------------------------------------
    // Sender rules
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void SetSenderRule_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner);

        Assert.ThrowsExactly<TestException>(() => c.SetSenderRule(Sender, RuleAllow),
            "setSenderRule is owner-gated");
        Assert.ThrowsExactly<TestException>(() => c.SetAllowedSender(Sender, true),
            "setAllowedSender is owner-gated");
    }

    [TestMethod]
    public void SetSenderRule_RejectsZeroSender()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (witnessed)

        // Subject-address validation runs even for the legitimate owner.
        Assert.ThrowsExactly<TestException>(() => c.SetSenderRule(UInt160.Zero, RuleAllow),
            "zero sender is not a valid rule subject");
    }

    [TestMethod]
    public void SetSenderRule_RejectsOutOfRangeRuleCode()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (witnessed)

        // Rule domain is {0=unset,1=allow,2=deny}; anything above RuleDeny must fault.
        Assert.ThrowsExactly<TestException>(() => c.SetSenderRule(Sender, 3),
            "rule code > 2 must be rejected");
    }

    // ---------------------------------------------------------------------------------------------
    // Receiver rules
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void SetReceiverRule_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner);

        Assert.ThrowsExactly<TestException>(() => c.SetReceiverRule(Receiver, RuleDeny),
            "setReceiverRule is owner-gated");
        Assert.ThrowsExactly<TestException>(() => c.SetAllowedReceiver(Receiver, false),
            "setAllowedReceiver is owner-gated");
    }

    [TestMethod]
    public void SetReceiverRule_RejectsZeroReceiver()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine);

        Assert.ThrowsExactly<TestException>(() => c.SetReceiverRule(UInt160.Zero, RuleAllow),
            "zero receiver is not a valid rule subject");
    }

    // ---------------------------------------------------------------------------------------------
    // Message-type rules
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void SetMessageTypeRule_NonOwner_Faults()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine, owner: OtherOwner);

        Assert.ThrowsExactly<TestException>(() => c.SetMessageTypeRule(MsgType, RuleDeny),
            "setMessageTypeRule is owner-gated");
        Assert.ThrowsExactly<TestException>(() => c.SetAllowedMessageType(MsgType, false),
            "setAllowedMessageType is owner-gated");
    }

    [TestMethod]
    public void SetMessageTypeRule_RejectsOutOfRangeRuleCode()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (witnessed)

        Assert.ThrowsExactly<TestException>(() => c.SetMessageTypeRule(MsgType, 5),
            "rule code > 2 must be rejected");
    }

    // ---------------------------------------------------------------------------------------------
    // AcceptL1ToL2 — input validation (read-only hook, no witness required)
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void AcceptL1ToL2_RejectsReservedChainZero()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // open by default

        Assert.IsFalse(c.AcceptL1ToL2(0, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "targetChainId 0 is the reserved L1 sentinel and must be rejected");
    }

    [TestMethod]
    public void AcceptL1ToL2_RejectsZeroSenderAndZeroReceiver()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // open by default

        Assert.IsFalse(c.AcceptL1ToL2(ChainA, UInt160.Zero, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "zero sender must be rejected");
        Assert.IsFalse(c.AcceptL1ToL2(ChainA, Sender, UInt160.Zero, MsgType, new byte[] { 0x01 })!.Value,
            "zero receiver must be rejected");
    }

    [TestMethod]
    public void AcceptL1ToL2_AcceptsLargePayloadWithinCap()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // open by default

        // The contract cap MaxPayloadBytes (128 KiB) sits at/above the NeoVM maximum stack-item size,
        // so an oversized byte[] cannot even be passed as an argument — the VM rejects it before the
        // contract's size guard runs, making that reject branch unreachable for a direct call. Pin the
        // reachable behavior: a large (64 KiB) payload, comfortably within the cap, is accepted.
        Assert.IsTrue(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[64 * 1024])!.Value,
            "a large payload within the cap is accepted");
    }

    // ---------------------------------------------------------------------------------------------
    // AcceptL1ToL2 — rule precedence (default open vs explicit deny/allow)
    // ---------------------------------------------------------------------------------------------

    [TestMethod]
    public void AcceptL1ToL2_DefaultOpen_AcceptsUnsetSubjects()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // default allow, no rules set

        Assert.IsTrue(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "with default-allow and no rules, a valid message passes");
    }

    [TestMethod]
    public void AcceptL1ToL2_DefaultDeny_RejectsUntilExplicitlyAllowed()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // owner == engine.Sender (witnessed)
        c.DefaultAllow = false; // harden: deny-by-default

        // Nothing is whitelisted yet -> sender rule falls back to deny.
        Assert.IsFalse(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "under deny-by-default an unlisted message is rejected");

        // Allow-list every leg of the conjunction; only then does the message pass.
        c.SetAllowedSender(Sender, true);
        Assert.IsFalse(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "allowing only the sender is insufficient — receiver still defaults to deny");

        c.SetAllowedReceiver(Receiver, true);
        c.SetAllowedMessageType(MsgType, true);
        Assert.IsTrue(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "with sender, receiver, and message type all allowed the message passes");
    }

    [TestMethod]
    public void AcceptL1ToL2_ExplicitSenderDeny_OverridesDefaultAllow()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // default allow

        c.SetAllowedSender(Sender, false); // explicit deny
        Assert.IsFalse(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "an explicit sender deny blocks the message even under default-allow");

        // A different (unlisted) sender still rides the default-allow path.
        Assert.IsTrue(c.AcceptL1ToL2(ChainA, OtherOwner, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "deny is scoped to the listed sender, not global");
    }

    [TestMethod]
    public void AcceptL1ToL2_ExplicitReceiverDeny_OverridesDefaultAllow()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // default allow

        c.SetAllowedReceiver(Receiver, false);
        Assert.IsFalse(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "an explicit receiver deny blocks the message even under default-allow");
    }

    [TestMethod]
    public void AcceptL1ToL2_ExplicitMessageTypeDeny_OverridesDefaultAllow()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // default allow

        c.SetAllowedMessageType(MsgType, false);
        Assert.IsFalse(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "an explicit message-type deny blocks the message even under default-allow");

        // A different message type is unaffected.
        Assert.IsTrue(c.AcceptL1ToL2(ChainA, Sender, Receiver, 9, new byte[] { 0x01 })!.Value,
            "deny is scoped to the listed message type, not global");
    }

    [TestMethod]
    public void AcceptL1ToL2_UnsetRule_ClearsBackToDefault()
    {
        var engine = new TestEngine(true);
        var c = Deploy(engine); // default allow

        c.SetAllowedSender(Sender, false); // deny
        Assert.IsFalse(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "denied sender is blocked");

        c.SetSenderRule(Sender, RuleUnset); // clear the rule -> falls back to default-allow
        Assert.IsTrue(c.AcceptL1ToL2(ChainA, Sender, Receiver, MsgType, new byte[] { 0x01 })!.Value,
            "clearing the rule restores the default-allow fallback");
    }
}
