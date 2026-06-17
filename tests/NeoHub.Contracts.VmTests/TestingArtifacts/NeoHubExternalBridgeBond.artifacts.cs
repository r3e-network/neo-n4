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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeBond"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":208,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":307,""safe"":false},{""name"":""getBondAsset"",""parameters"":[],""returntype"":""Hash160"",""offset"":428,""safe"":true},{""name"":""getMinBond"",""parameters"":[],""returntype"":""Integer"",""offset"":486,""safe"":true},{""name"":""setMinBond"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":533,""safe"":false},{""name"":""registerSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":637,""safe"":false},{""name"":""revokeSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":916,""safe"":false},{""name"":""isSlasher"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1002,""safe"":true},{""name"":""deposit"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1019,""safe"":false},{""name"":""getBalance"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":1817,""safe"":true},{""name"":""hasMinBond"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1855,""safe"":true},{""name"":""slash"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1871,""safe"":false},{""name"":""withdraw"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""member"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2234,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":2473,""safe"":false}],""events"":[{""name"":""BondDeposited"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""BondSlashed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""BondWithdrawn"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""MinBondChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SlasherRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""SlasherRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Per-(externalChainId, committee member) slashable bond escrow."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeBond"",""nef"":{""optimization"":""Basic""}}}");

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
