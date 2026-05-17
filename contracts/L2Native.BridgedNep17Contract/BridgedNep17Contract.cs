using System;
using System.ComponentModel;
using System.Numerics;
using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Attributes;
using Neo.SmartContract.Framework.Services;

namespace L2Native.BridgedNep17Contract;

/// <summary>
/// Canonical mintable/burnable NEP-17 representation of an L1 asset on an L2.
/// Mint and burn are restricted to the configured L2 bridge contract.
/// </summary>
[DisplayName("L2Native.BridgedNep17Contract")]
[ContractAuthor("Neo Project", "dev@neo.org")]
[ContractDescription("Canonical L2 bridged NEP-17 token for Neo Elastic Network.")]
[ContractVersion("0.1.0")]
[ContractSourceCode("https://github.com/r3e-network/neo-n4/tree/master/contracts/L2Native.BridgedNep17Contract")]
[ContractPermission(Permission.Any, Method.Any)]
[SupportedStandards(NepStandard.Nep17)]
public class BridgedNep17Contract : Nep17Token
{
    private const byte KeyName = 0xF0;
    private const byte KeySymbol = 0xF1;
    private const byte KeyDecimals = 0xF2;
    private const byte KeyL1Asset = 0xF3;
    private const byte KeyBridge = 0xFE;
    private const byte KeyOwner = 0xFF;

    /// <inheritdoc />
    public override string Symbol
    {
        [Safe]
        get => GetSymbol();
    }

    /// <inheritdoc />
    public override byte Decimals
    {
        [Safe]
        get => GetDecimals();
    }

    /// <summary>Deploy data: [owner, bridge, name, symbol, decimals, l1Asset].</summary>
    public static void _deploy(object data, bool update)
    {
        if (update) return;
        var arr = (object[])data;
        var owner = (UInt160)arr[0];
        var bridge = (UInt160)arr[1];
        var name = (string)arr[2];
        var symbol = (string)arr[3];
        var decimals = (byte)(BigInteger)arr[4];
        var l1Asset = (UInt160)arr[5];

        ExecutionEngine.Assert(owner.IsValid && !owner.IsZero, "invalid owner");
        ExecutionEngine.Assert(bridge.IsValid && !bridge.IsZero, "invalid bridge");
        ExecutionEngine.Assert(l1Asset.IsValid && !l1Asset.IsZero, "invalid L1 asset");
        ExecutionEngine.Assert(name.Length > 0, "name required");
        ExecutionEngine.Assert(symbol.Length > 0, "symbol required");
        ExecutionEngine.Assert(decimals <= 18, "decimals too large");

        Storage.Put(new byte[] { KeyOwner }, owner);
        Storage.Put(new byte[] { KeyBridge }, bridge);
        Storage.Put(new byte[] { KeyName }, name);
        Storage.Put(new byte[] { KeySymbol }, symbol);
        Storage.Put(new byte[] { KeyDecimals }, new byte[] { decimals });
        Storage.Put(new byte[] { KeyL1Asset }, l1Asset);
    }

    /// <summary>Governance owner for metadata/bridge rotation.</summary>
    [Safe]
    public static UInt160 GetOwner()
    {
        var raw = Storage.Get(new byte[] { KeyOwner });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Human-readable token name.</summary>
    [Safe]
    public static string GetName()
    {
        var raw = Storage.Get(new byte[] { KeyName });
        return raw == null ? "Bridged NEP-17" : (string)raw;
    }

    /// <summary>NEP-17 symbol.</summary>
    [Safe]
    public static string GetSymbol()
    {
        var raw = Storage.Get(new byte[] { KeySymbol });
        return raw == null ? "bNEP17" : (string)raw;
    }

    /// <summary>NEP-17 decimals.</summary>
    [Safe]
    public static byte GetDecimals()
    {
        var raw = Storage.Get(new byte[] { KeyDecimals });
        return raw == null ? (byte)8 : ((byte[])raw)[0];
    }

    /// <summary>L1 asset represented by this bridged token.</summary>
    [Safe]
    public static UInt160 GetL1Asset()
    {
        var raw = Storage.Get(new byte[] { KeyL1Asset });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Configured L2 bridge contract.</summary>
    [Safe]
    public static UInt160 GetBridge()
    {
        var raw = Storage.Get(new byte[] { KeyBridge });
        return raw == null ? UInt160.Zero : (UInt160)raw;
    }

    /// <summary>Rotate bridge contract under owner control.</summary>
    public static void SetBridge(UInt160 bridge)
    {
        ExecutionEngine.Assert(Runtime.CheckWitness(GetOwner()), "not authorized");
        ExecutionEngine.Assert(bridge.IsValid && !bridge.IsZero, "invalid bridge");
        Storage.Put(new byte[] { KeyBridge }, bridge);
    }

    /// <summary>Bridge-only mint used by L2BridgeContract.ApplyDeposit.</summary>
    public new static void Mint(UInt160 to, BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CallingScriptHash.Equals(GetBridge()), "not bridge");
        ExecutionEngine.Assert(to.IsValid && !to.IsZero, "invalid recipient");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        Nep17Token.Mint(to, amount);
    }

    /// <summary>Bridge-only burn used by L2BridgeContract.InitiateWithdrawal.</summary>
    public new static void Burn(UInt160 account, BigInteger amount)
    {
        ExecutionEngine.Assert(Runtime.CallingScriptHash.Equals(GetBridge()), "not bridge");
        ExecutionEngine.Assert(account.IsValid && !account.IsZero, "invalid account");
        ExecutionEngine.Assert(amount > 0, "amount must be positive");
        Nep17Token.Burn(account, amount);
    }

    /// <summary>Owner witness for wallet tooling.</summary>
    [Safe]
    public static bool Verify()
    {
        return Runtime.CheckWitness(GetOwner());
    }
}
