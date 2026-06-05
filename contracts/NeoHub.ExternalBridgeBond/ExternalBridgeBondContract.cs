using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Native;
using Neo.SmartContract.Framework.Services;

namespace NeoHub.ExternalBridgeBond;

/// <summary>
/// Holds slashable collateral per (externalChainId, committeeMember). When a
/// committee member equivocates (signs two distinct messages for the same
/// nonce on the same external chain), an authorized slasher reports it via
/// <see cref="Slash"/> and the bond is forfeited.
/// </summary>
/// <remarks>
/// Mirrors <c>NeoHub.SequencerBond</c> 1:1 with one structural change:
/// indexed by <c>externalChainId</c> (uint, foreign-namespace 0xE0_xx_xx_xx)
/// instead of <c>chainId</c> (Neo L2 id). The economic model is the same:
/// committee members deposit a bond before being added to the verifier's
/// committee; an authorized slasher (Phase C: the optimistic-challenge
/// fraud verifier) burns the bond on equivocation. Withdraw is owner-gated
/// so a member can't pull funds while still in an active committee.
///
/// <para>Slasher set wiring: at deploy time the owner registers the
/// <c>NeoHub.MpcCommitteeFraudVerifier</c> contract (Phase C) as a
/// slasher. Until that exists, slashing is owner-only — usable for
/// devnet but not production-grade economic security on its own.</para>
/// </remarks>
[DisplayName("NeoHub.ExternalBridgeBond")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Per-(externalChainId, committee member) slashable bond escrow.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeBond")]
[ContractPermission(Permission.Any, Method.Any)]
public class ExternalBridgeBondContract : SmartContract
{
    private const byte PrefixBalance = 0x01;          // 0x01 + chainId(4B) + member(20B) → BigInteger
    private const byte PrefixSlasher = 0x02;          // 0x02 + slasher(20B) → 1
    private const byte KeyBondAsset = 0x03;
    private const byte KeyMinBond = 0x04;
    private const byte PrefixPendingTransfer = 0x05;  // 0x05 + asset(20B) + from(20B) → 1
    private const byte KeyOwner = 0xFF;

    /// <summary>Default minimum bond — 10 GAS (committee membership is more
    /// economically consequential than sequencer bonding because a single
    /// equivocation can drain bridged liquidity).</summary>
    public const ulong DefaultMinBond = 10_000_000_000UL; // 10 GAS at 8 decimals

    /// <summary>Emitted when a member deposits (or tops up).</summary>
    [DisplayName("BondDeposited")]
    public static event Action<uint, UInt160, BigInteger> OnBondDeposited = default!;

    /// <summary>Emitted when a member's bond is slashed.</summary>
    [DisplayName("BondSlashed")]
    public static event Action<uint, UInt160, BigInteger, UInt160> OnBondSlashed = default!;

    /// <summary>Emitted when a member withdraws unslashed bond.</summary>
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

    /// <summary>Set wiring on deploy. <c>data</c> = [owner, bondAsset].</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var bondAsset = (UInt160)arr[1];
        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(bondAsset.IsValid && !bondAsset.IsZero, "invalid bond asset");
        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyBondAsset }, bondAsset);
        Storage.Put(new byte[] { KeyMinBond }, (BigInteger)DefaultMinBond);
    }

    /// <summary>Owner — controls slasher set + min bond.</summary>
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

    /// <summary>The NEP-17 asset bonded.</summary>
    [Safe]
    public static UInt160 GetBondAsset()
    {
        var raw = Storage.Get(new byte[] { KeyBondAsset });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Minimum bond required for committee membership.</summary>
    [Safe]
    public static BigInteger GetMinBond()
    {
        var raw = Storage.Get(new byte[] { KeyMinBond });
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>Owner-only: change the minimum bond.</summary>
    public static void SetMinBond(BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(amount > 0, "minBond must be positive");
        var old = GetMinBond();
        Storage.Put(new byte[] { KeyMinBond }, amount);
        OnMinBondChanged(old, amount);
    }

    /// <summary>Owner-only: register a contract authorized to slash.
    /// Typically the Phase-C optimistic-challenge fraud verifier.</summary>
    public static void RegisterSlasher(UInt160 slasher)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(slasher.IsValid && !slasher.IsZero, "invalid slasher");
        Storage.Put(SlasherKey(slasher), new byte[] { 1 });
        OnSlasherRegistered(slasher);
    }

    /// <summary>Owner-only: revoke a slasher.</summary>
    public static void RevokeSlasher(UInt160 slasher)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        Storage.Delete(SlasherKey(slasher));
        OnSlasherRevoked(slasher);
    }

    /// <summary>Is <paramref name="who"/> currently authorized to slash?</summary>
    [Safe]
    public static bool IsSlasher(UInt160 who) => Storage.Get(SlasherKey(who)) != null;

    /// <summary>
    /// Top up a committee member's bond. Caller must have approved this
    /// contract to spend <paramref name="amount"/> of the bond asset.
    /// </summary>
    public static void Deposit(uint externalChainId, UInt160 member, BigInteger amount)
    {
        ExecutionEngine.Assert(
            (externalChainId & 0xFF000000U) == 0xE0000000U,
            "externalChainId must use the 0xE0_xx_xx_xx foreign-namespace prefix");
        ExecutionEngine.Assert(member.IsValid && !member.IsZero, "invalid member");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        var sender = (UInt160)Runtime.CallingScriptHash;

        // Update balance BEFORE external transfer to prevent re-entrant
        // deposit double-counting. NeoVM FAULT on subsequent transfer failure
        // reverts this write.
        var k = BalanceKey(externalChainId, member);
        var prev = Storage.Get(k);
        var newBal = (prev == null ? BigInteger.Zero : (BigInteger)prev) + amount;
        Storage.Put(k, newBal);

        var pendingKey = PendingTransferKey(GetBondAsset(), sender);
        Storage.Put(pendingKey, new byte[] { 1 });
        var ok = (bool)Contract.Call(GetBondAsset(), "transfer", CallFlags.All,
            new object[] { sender, Runtime.ExecutingScriptHash, amount, null! });
        ExecutionEngine.Assert(ok, "bond transfer failed");
        Storage.Delete(pendingKey);
        OnBondDeposited(externalChainId, member, newBal);
    }

    /// <summary>Read a member's current bond balance for a chain.</summary>
    [Safe]
    public static BigInteger GetBalance(uint externalChainId, UInt160 member)
    {
        var raw = Storage.Get(BalanceKey(externalChainId, member));
        return raw == null ? BigInteger.Zero : (BigInteger)raw;
    }

    /// <summary>Does this member currently meet the minimum bond?</summary>
    [Safe]
    public static bool HasMinBond(uint externalChainId, UInt160 member)
    {
        return GetBalance(externalChainId, member) >= GetMinBond();
    }

    /// <summary>
    /// Slash a member's bond. Caller must be a registered slasher OR the owner.
    /// The slashed amount is paid to <paramref name="recipient"/> (typically
    /// the equivocation reporter, providing economic incentive).
    /// </summary>
    public static void Slash(uint externalChainId, UInt160 member, BigInteger amount, UInt160 recipient)
    {
        var caller = (UInt160)Runtime.CallingScriptHash;
        // Either an authorized slasher contract OR the owner (devnet path
        // before MpcCommitteeFraudVerifier is wired) may slash.
        var authorized = IsSlasher(caller) || Runtime.CheckWitness(GetOwner());
        ExecutionEngine.Assert(authorized,
            "not authorized — caller must be a registered slasher contract or the owner");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        ExecutionEngine.Assert(recipient.IsValid && !recipient.IsZero, "invalid recipient");

        var k = BalanceKey(externalChainId, member);
        var raw = Storage.Get(k);
        var bal = raw == null ? BigInteger.Zero : (BigInteger)raw;
        ExecutionEngine.Assert(bal >= amount, "slash amount exceeds bond balance");

        Storage.Put(k, bal - amount);

        // Pay the slashed amount to the recipient.
        var ok = (bool)Contract.Call(GetBondAsset(), "transfer", CallFlags.All,
            new object[] { Runtime.ExecutingScriptHash, recipient, amount, null! });
        ExecutionEngine.Assert(ok, "payout to recipient failed");

        OnBondSlashed(externalChainId, member, amount, recipient);
    }

    /// <summary>
    /// Owner-only: withdraw unslashed bond after a member has exited the
    /// committee. The owner gates this (rather than letting the member
    /// directly withdraw) so a member can't pull funds while still active
    /// — the Phase-C fraud-proof window means equivocation can be detected
    /// after the member has already produced their last signature.
    /// </summary>
    public static void Withdraw(uint externalChainId, UInt160 member, BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");

        var k = BalanceKey(externalChainId, member);
        var raw = Storage.Get(k);
        var bal = raw == null ? BigInteger.Zero : (BigInteger)raw;
        ExecutionEngine.Assert(bal >= amount, "withdraw amount exceeds bond balance");

        Storage.Put(k, bal - amount);
        var ok = (bool)Contract.Call(GetBondAsset(), "transfer", CallFlags.All,
            new object[] { Runtime.ExecutingScriptHash, member, amount, null! });
        ExecutionEngine.Assert(ok, "withdraw transfer failed");

        OnBondWithdrawn(externalChainId, member, amount);
    }

    /// <summary>NEP-17 hook. Accept only transfers initiated by <see cref="Deposit"/>.</summary>
    public static void OnNEP17Payment(UInt160 from, BigInteger amount, object data)
    {
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        var asset = (UInt160)Runtime.CallingScriptHash;
        var pendingKey = PendingTransferKey(asset, from);
        ExecutionEngine.Assert(Storage.Get(pendingKey) != null,
            "direct transfer rejected — call Deposit to credit a member's bond");
        Storage.Delete(pendingKey);
    }

    private static byte[] BalanceKey(uint externalChainId, UInt160 member)
    {
        var k = new byte[1 + 4 + 20];
        k[0] = PrefixBalance;
        k[1] = (byte)externalChainId; k[2] = (byte)(externalChainId >> 8);
        k[3] = (byte)(externalChainId >> 16); k[4] = (byte)(externalChainId >> 24);
        var m = (byte[])member;
        for (var i = 0; i < 20; i++) k[5 + i] = m[i];
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

    private static byte[] PendingTransferKey(UInt160 asset, UInt160 from)
    {
        var key = new byte[1 + 20 + 20];
        key[0] = PrefixPendingTransfer;
        var assetBytes = (byte[])asset;
        var fromBytes = (byte[])from;
        for (var i = 0; i < 20; i++)
        {
            key[1 + i] = assetBytes[i];
            key[21 + i] = fromBytes[i];
        }
        return key;
    }
}
