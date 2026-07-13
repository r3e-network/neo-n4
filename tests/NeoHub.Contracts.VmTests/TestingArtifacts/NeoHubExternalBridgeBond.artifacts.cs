using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubExternalBridgeBond(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeBond"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":208,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":307,""safe"":false},{""name"":""getBondAsset"",""parameters"":[],""returntype"":""Hash160"",""offset"":428,""safe"":true},{""name"":""getMinBond"",""parameters"":[],""returntype"":""Integer"",""offset"":486,""safe"":true},{""name"":""setMinBond"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":533,""safe"":false},{""name"":""registerSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":637,""safe"":false},{""name"":""revokeSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":915,""safe"":false},{""name"":""isSlasher"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1001,""safe"":true},{""name"":""deposit"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1018,""safe"":false},{""name"":""getBalance"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":1815,""safe"":true},{""name"":""hasMinBond"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1853,""safe"":true},{""name"":""slash"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1869,""safe"":false},{""name"":""withdraw"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2232,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":2471,""safe"":false}],""events"":[{""name"":""BondDeposited"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""BondSlashed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""BondWithdrawn"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""MinBondChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SlasherRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""SlasherRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Per-(externalChainId, committee member) slashable bond escrow."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeBond"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delBondDeposited(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("BondDeposited")]
    public event delBondDeposited? OnBondDeposited;

    public delegate void delBondSlashed(BigInteger? arg1, UInt160? arg2, BigInteger? arg3, UInt160? arg4);

    [DisplayName("BondSlashed")]
    public event delBondSlashed? OnBondSlashed;

    public delegate void delBondWithdrawn(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("BondWithdrawn")]
    public event delBondWithdrawn? OnBondWithdrawn;

    public delegate void delMinBondChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("MinBondChanged")]
    public event delMinBondChanged? OnMinBondChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delSlasherRegistered(UInt160? obj);

    [DisplayName("SlasherRegistered")]
    public event delSlasherRegistered? OnSlasherRegistered;

    public delegate void delSlasherRevoked(UInt160? obj);

    [DisplayName("SlasherRevoked")]
    public event delSlasherRevoked? OnSlasherRevoked;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? BondAsset { [DisplayName("getBondAsset")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? MinBond { [DisplayName("getMinBond")] get; [DisplayName("setMinBond")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBalance")]
    public abstract BigInteger? GetBalance(BigInteger? externalChainId, UInt160? member);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("hasMinBond")]
    public abstract bool? HasMinBond(BigInteger? externalChainId, UInt160? member);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isSlasher")]
    public abstract bool? IsSlasher(UInt160? who);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("deposit")]
    public abstract void Deposit(BigInteger? externalChainId, UInt160? member, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("onNEP17Payment")]
    public abstract void OnNEP17Payment(UInt160? from, BigInteger? amount, object? data = null);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerSlasher")]
    public abstract void RegisterSlasher(UInt160? slasher);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeSlasher")]
    public abstract void RevokeSlasher(UInt160? slasher);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("slash")]
    public abstract void Slash(BigInteger? externalChainId, UInt160? member, BigInteger? amount, UInt160? recipient);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("withdraw")]
    public abstract void Withdraw(BigInteger? externalChainId, UInt160? member, BigInteger? amount);

    #endregion
}
