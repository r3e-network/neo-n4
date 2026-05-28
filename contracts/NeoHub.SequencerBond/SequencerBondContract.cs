using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.SequencerBond;

/// <summary>
/// Holds slashable collateral per (chainId, sequencer). When the chain's <c>ForcedInclusion</c>
/// or <c>SettlementManager</c> reports censorship or fraud, the offending sequencer's bond is
/// slashed and either burned or paid to a reporter. See doc.md §15.4 (forced inclusion) and
/// §17 (sequencer-censorship + invalid-state mitigations).
/// </summary>
[DisplayName("NeoHub.SequencerBond")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Per-(chain, sequencer) slashable bond escrow for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SequencerBond")]
[ContractPermission(Permission.Any, Method.Any)]
public class SequencerBondContract : SmartContract
{
    private const byte PrefixBalance = 0x01;          // 0x01 + chainId(4B) + sequencer(20B) → BigInteger
    private const byte PrefixSlasher = 0x02;          // 0x02 + slasher(20B) → 1
    private const byte KeyBondAsset = 0x03;
    private const byte KeyMinBond = 0x04;
    private const byte KeyOwner = 0xFF;

    /// <summary>Default minimum bond amount (in fee-asset smallest units).</summary>
    public const ulong DefaultMinBond = 1_000_000UL; // 1.0 GAS at 8 decimals

    /// <summary>Emitted when a sequencer posts (or tops up) its bond.</summary>
    [DisplayName("BondDeposited")]
    public static event Action<uint, UInt160, BigInteger> OnBondDeposited = default!;

    /// <summary>Emitted when a sequencer's bond is slashed.</summary>
    [DisplayName("BondSlashed")]
    public static event Action<uint, UInt160, BigInteger, UInt160> OnBondSlashed = default!;

    /// <summary>Emitted when a sequencer withdraws unslashed bond after exit.</summary>
    [DisplayName("BondWithdrawn")]
    public static event Action<uint, UInt160, BigInteger> OnBondWithdrawn = default!;

    /// <summary>Emitted when the minimum bond amount changes.</summary>
    [DisplayName("MinBondChanged")]
    public static event Action<BigInteger, BigInteger> OnMinBondChanged = default!;

    /// <summary>Emitted when a slasher is registered.</summary>
    [DisplayName("SlasherRegistered")]
    public static event Action<UInt160> OnSlasherRegistered = default!;

    /// <summary>Emitted when a slasher is revoked.</summary>
    [DisplayName("SlasherRevoked")]
    public static event Action<UInt160> OnSlasherRevoked = default!;

    /// <summary>Emitted when ownership is transferred.</summary>
    [DisplayName("OwnerChanged")]
    public static event Action<UInt160, UInt160> OnOwnerChanged = default!;

    /// <summary>Set wiring on deploy. <c>data</c> = [owner, bondAsset, initialSlashers[]].</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var bondAsset = (UInt160)arr[1];
        var slashers = (UInt160[])arr[2];

        // Validate inputs at the source — without these guards, a typo'd zero owner
        // would deploy successfully but every owner-gated method would later fail with
        // a confusing "not authorized" check. A zero bondAsset would lock every
        // Deposit() at the NEP-17 transfer step. An empty slashers list means no one
        // can ever slash (Phase-3 economic security broken). And a zero slasher
        // entry is a meaningless storage row that confuses IsSlasher() consumers.
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(bondAsset.IsValid && !bondAsset.IsZero, "invalid bond asset");
        ExecutionEngine.Assert(slashers.Length > 0, "slashers list must be non-empty");

        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyBondAsset }, bondAsset);
        Storage.Put(new byte[] { KeyMinBond }, (BigInteger)DefaultMinBond);

        foreach (var s in slashers)
        {
            ExecutionEngine.Assert(s.IsValid && !s.IsZero, "invalid slasher in initial list");
            Storage.Put(SlasherKey(s), new byte[] { 1 });
        }
    }

    /// <summary>Governance owner.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Transfer governance ownership. Owner only.</summary>
    public static void SetOwner(UInt160 newOwner)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(newOwner.IsValid && !newOwner.IsZero, "invalid new owner");
        var oldOwner = GetOwner();
        Storage.Put(new byte[] { KeyOwner }, newOwner);
        OnOwnerChanged(oldOwner, newOwner);
    }

    /// <summary>The NEP-17 asset held as bond collateral.</summary>
    [Safe]
    public static UInt160 GetBondAsset()
    {
        var raw = Storage.Get(new byte[] { KeyBondAsset });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Minimum bond required to be considered eligible to sequence.</summary>
    [Safe]
    public static BigInteger GetMinBond()
    {
        var raw = Storage.Get(new byte[] { KeyMinBond });
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>Update the minimum bond. Owner only.</summary>
    public static void SetMinBond(BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        var old = GetMinBond();
        Storage.Put(new byte[] { KeyMinBond }, amount);
        OnMinBondChanged(old, amount);
    }

    /// <summary>Authorize a contract to slash bonds (typically <c>ForcedInclusion</c> + <c>SettlementManager</c>).</summary>
    public static void RegisterSlasher(UInt160 slasher)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(slasher.IsValid && !slasher.IsZero, "invalid slasher");
        Storage.Put(SlasherKey(slasher), new byte[] { 1 });
        OnSlasherRegistered(slasher);
    }

    /// <summary>Revoke a previously authorized slasher.</summary>
    public static void RevokeSlasher(UInt160 slasher)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Delete(SlasherKey(slasher));
        OnSlasherRevoked(slasher);
    }

    /// <summary>True if <paramref name="who"/> is currently authorized to call <see cref="Slash"/>.</summary>
    [Safe]
    public static bool IsSlasher(UInt160 who) => Storage.Get(SlasherKey(who)) != null;

    /// <summary>
    /// Sequencer (or sponsor) deposits <paramref name="amount"/> of the bond asset on behalf of
    /// <paramref name="sequencer"/> for chain <paramref name="chainId"/>. Caller must have approved
    /// the bond asset's NEP-17 transfer to this contract.
    /// </summary>
    public static void Deposit(uint chainId, UInt160 sequencer, BigInteger amount)
    {
        ExecutionEngine.Assert(chainId > 0, "chainId 0 is reserved for L1");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(sequencer.IsValid && !sequencer.IsZero, "invalid sequencer");

        var asset = GetBondAsset();
        var caller = Runtime.CallingScriptHash;
        var transferred = (bool)Contract.Call(
            asset, "transfer", CallFlags.All,
            new object[] { caller, Runtime.ExecutingScriptHash, amount, null! });
        ExecutionEngine.Assert(transferred, "asset transfer failed");

        var key = BalanceKey(chainId, sequencer);
        var raw = Storage.Get(key);
        var current = raw == null ? BigInteger.Zero : (BigInteger)raw;
        Storage.Put(key, current + amount);

        OnBondDeposited(chainId, sequencer, amount);
    }

    /// <summary>Read a sequencer's current bond balance for a chain.</summary>
    [Safe]
    public static BigInteger GetBalance(uint chainId, UInt160 sequencer)
    {
        var raw = Storage.Get(BalanceKey(chainId, sequencer));
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>True if the sequencer's balance ≥ <see cref="GetMinBond"/>.</summary>
    [Safe]
    public static bool HasMinBond(uint chainId, UInt160 sequencer)
    {
        return GetBalance(chainId, sequencer) >= GetMinBond();
    }

    /// <summary>
    /// Slash <paramref name="amount"/> from <paramref name="sequencer"/>'s bond and pay it to
    /// <paramref name="recipient"/> (typically the reporter who proved censorship/fraud, or the
    /// emergency-manager treasury). Caller must be a registered slasher.
    /// </summary>
    public static void Slash(uint chainId, UInt160 sequencer, BigInteger amount, UInt160 recipient)
    {
        var caller = Runtime.CallingScriptHash;
        ExecutionEngine.Assert(IsSlasher(caller), "caller is not an authorized slasher");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        var key = BalanceKey(chainId, sequencer);
        var raw = Storage.Get(key);
        var current = raw == null ? BigInteger.Zero : (BigInteger)raw;
        ExecutionEngine.Assert(current >= amount, "insufficient bond");

        Storage.Put(key, current - amount);

        if (recipient.IsValid && !recipient.IsZero)
        {
            var asset = GetBondAsset();
            // Check the NEP-17 transfer return — a paused/frozen bond asset would return false
            // and silently leave the accounting decremented without any actual movement. Match
            // ExternalBridgeBond.Slash which captures the bool. recipient == zero burns
            // (no transfer needed) and falls through.
            var ok = (bool)Contract.Call(asset, "transfer", CallFlags.All,
                new object[] { Runtime.ExecutingScriptHash, recipient, amount, null! });
            ExecutionEngine.Assert(ok, "slash payout to recipient failed");
        }

        OnBondSlashed(chainId, sequencer, amount, recipient);
    }

    /// <summary>
    /// Sequencer withdraws unslashed bond after they've exited the validator set.
    /// Owner-gated to enforce a withdrawal-permission flow (e.g. exit-window expired).
    /// </summary>
    public static void Withdraw(uint chainId, UInt160 sequencer, BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        var key = BalanceKey(chainId, sequencer);
        var raw = Storage.Get(key);
        var current = raw == null ? BigInteger.Zero : (BigInteger)raw;
        ExecutionEngine.Assert(current >= amount, "insufficient balance");

        Storage.Put(key, current - amount);
        var asset = GetBondAsset();
        // Capture the NEP-17 transfer return — a paused/frozen asset returns false and
        // we'd otherwise silently lose the sequencer's withdrawn balance from accounting.
        var ok = (bool)Contract.Call(asset, "transfer", CallFlags.All,
            new object[] { Runtime.ExecutingScriptHash, sequencer, amount, null! });
        ExecutionEngine.Assert(ok, "withdrawal transfer failed");

        OnBondWithdrawn(chainId, sequencer, amount);
    }

    private static byte[] BalanceKey(uint chainId, UInt160 sequencer)
    {
        var k = new byte[1 + 4 + 20];
        k[0] = PrefixBalance;
        k[1] = (byte)chainId; k[2] = (byte)(chainId >> 8); k[3] = (byte)(chainId >> 16); k[4] = (byte)(chainId >> 24);
        var s = (byte[])sequencer;
        for (var i = 0; i < 20; i++) k[5 + i] = s[i];
        return k;
    }

    private static byte[] SlasherKey(UInt160 slasher)
    {
        var k = new byte[1 + 20];
        k[0] = PrefixSlasher;
        var s = (byte[])slasher;
        for (var i = 0; i < 20; i++) k[1 + i] = s[i];
        return k;
    }
}
