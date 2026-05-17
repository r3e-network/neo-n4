using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.L1TxFilter;

/// <summary>
/// Optional policy hook for MessageRouter.EnqueueL1ToL2. The filter supports
/// allow/deny rules for L1 sender contracts, L2 receiver contracts, and message types.
/// Missing rules fall back to the configured default.
/// </summary>
[DisplayName("NeoHub.L1TxFilter")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Per-chain L1-to-L2 transaction filter for NeoHub.MessageRouter.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.L1TxFilter")]
[ContractPermission(Permission.Any, Method.Any)]
public class L1TxFilterContract : SmartContract
{
    private const byte PrefixSenderRule = 0x01;
    private const byte PrefixReceiverRule = 0x02;
    private const byte PrefixMessageTypeRule = 0x03;
    private const byte KeyDefaultAllow = 0x04;
    private const byte KeyOwner = 0xFF;

    private const byte RuleUnset = 0;
    private const byte RuleAllow = 1;
    private const byte RuleDeny = 2;

    /// <summary>Maximum payload accepted by the reference filter: 128 KiB.</summary>
    public const int MaxPayloadBytes = 128 * 1024;

    /// <summary>Emitted when the default allow/deny behavior changes.</summary>
    [DisplayName("DefaultPolicySet")]
    public static event Action<bool> OnDefaultPolicySet = default!;

    /// <summary>Emitted when a sender, receiver, or message-type rule changes.</summary>
    [DisplayName("RuleSet")]
    public static event Action<byte, byte[], byte> OnRuleSet = default!;

    /// <summary>Deploy data: owner. Defaults to allow; call SetDefaultAllow after deployment to harden.</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var owner = (UInt160)data;
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyDefaultAllow }, new byte[] { RuleAllow });
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Default policy used when no explicit rule exists.</summary>
    [Safe]
    public static bool GetDefaultAllow()
    {
        var raw = Storage.Get(new byte[] { KeyDefaultAllow });
        return raw == null || ((byte[])raw)[0] == RuleAllow;
    }

    /// <summary>Set fallback behavior for missing rules.</summary>
    public static void SetDefaultAllow(bool allow)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Put(new byte[] { KeyDefaultAllow }, new byte[] { allow ? RuleAllow : RuleDeny });
        OnDefaultPolicySet(allow);
    }

    /// <summary>Set sender rule: 0=unset, 1=allow, 2=deny.</summary>
    public static void SetSenderRule(UInt160 sender, byte rule)
    {
        ExecutionEngine.Assert(sender.IsValid && !sender.IsZero, "invalid sender");
        SetRule(SenderKey(sender), PrefixSenderRule, (byte[])sender, rule);
    }

    /// <summary>Convenience wrapper for sender allow/deny.</summary>
    public static void SetAllowedSender(UInt160 sender, bool allowed)
    {
        SetSenderRule(sender, allowed ? RuleAllow : RuleDeny);
    }

    /// <summary>Set receiver rule: 0=unset, 1=allow, 2=deny.</summary>
    public static void SetReceiverRule(UInt160 receiver, byte rule)
    {
        ExecutionEngine.Assert(receiver.IsValid && !receiver.IsZero, "invalid receiver");
        SetRule(ReceiverKey(receiver), PrefixReceiverRule, (byte[])receiver, rule);
    }

    /// <summary>Convenience wrapper for receiver allow/deny.</summary>
    public static void SetAllowedReceiver(UInt160 receiver, bool allowed)
    {
        SetReceiverRule(receiver, allowed ? RuleAllow : RuleDeny);
    }

    /// <summary>Set message-type rule: 0=unset, 1=allow, 2=deny.</summary>
    public static void SetMessageTypeRule(byte messageType, byte rule)
    {
        SetRule(new byte[] { PrefixMessageTypeRule, messageType },
            PrefixMessageTypeRule, new byte[] { messageType }, rule);
    }

    /// <summary>Convenience wrapper for message-type allow/deny.</summary>
    public static void SetAllowedMessageType(byte messageType, bool allowed)
    {
        SetMessageTypeRule(messageType, allowed ? RuleAllow : RuleDeny);
    }

    /// <summary>
    /// MessageRouter calls this as a read-only pre-enqueue hook. Returning false
    /// rejects the L1-to-L2 message before nonce allocation and storage writes.
    /// </summary>
    [Safe]
    public static bool AcceptL1ToL2(
        uint targetChainId,
        UInt160 sender,
        UInt160 receiver,
        byte messageType,
        byte[] payload)
    {
        if (targetChainId == 0) return false;
        if (!sender.IsValid || sender.IsZero) return false;
        if (!receiver.IsValid || receiver.IsZero) return false;
        if (payload.Length > MaxPayloadBytes) return false;

        return AcceptRule(SenderKey(sender))
            && AcceptRule(ReceiverKey(receiver))
            && AcceptRule(new byte[] { PrefixMessageTypeRule, messageType });
    }

    private static void SetRule(byte[] key, byte kind, byte[] subject, byte rule)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(rule <= RuleDeny, "rule must be 0=unset, 1=allow, 2=deny");
        if (rule == RuleUnset)
            Storage.Delete(key);
        else
            Storage.Put(key, new byte[] { rule });
        OnRuleSet(kind, subject, rule);
    }

    private static bool AcceptRule(byte[] key)
    {
        var raw = Storage.Get(key);
        if (raw == null) return GetDefaultAllow();
        var rule = ((byte[])raw)[0];
        return rule == RuleAllow;
    }

    private static byte[] SenderKey(UInt160 sender)
    {
        var key = new byte[21];
        key[0] = PrefixSenderRule;
        var bytes = (byte[])sender;
        for (var i = 0; i < 20; i++) key[1 + i] = bytes[i];
        return key;
    }

    private static byte[] ReceiverKey(UInt160 receiver)
    {
        var key = new byte[21];
        key[0] = PrefixReceiverRule;
        var bytes = (byte[])receiver;
        for (var i = 0; i < 20; i++) key[1 + i] = bytes[i];
        return key;
    }
}
