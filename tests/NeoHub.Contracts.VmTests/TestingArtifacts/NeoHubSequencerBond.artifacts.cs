using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSequencerBond(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SequencerBond"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":515,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":614,""safe"":false},{""name"":""getBondAsset"",""parameters"":[],""returntype"":""Hash160"",""offset"":735,""safe"":true},{""name"":""getMinBond"",""parameters"":[],""returntype"":""Integer"",""offset"":793,""safe"":true},{""name"":""setMinBond"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":840,""safe"":false},{""name"":""registerSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":943,""safe"":false},{""name"":""revokeSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1062,""safe"":false},{""name"":""isSlasher"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1148,""safe"":true},{""name"":""deposit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1165,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":1907,""safe"":false},{""name"":""getBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":2041,""safe"":true},{""name"":""hasMinBond"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":2079,""safe"":true},{""name"":""slash"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2095,""safe"":false},{""name"":""withdraw"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2372,""safe"":false}],""events"":[{""name"":""BondDeposited"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""BondSlashed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""BondWithdrawn"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""MinBondChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SlasherRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""SlasherRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Per-(chain, sequencer) slashable bond escrow for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SequencerBond"",""nef"":{""optimization"":""Basic""}}}");

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
    public abstract BigInteger? GetBalance(BigInteger? chainId, UInt160? sequencer);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("hasMinBond")]
    public abstract bool? HasMinBond(BigInteger? chainId, UInt160? sequencer);

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
    public abstract void Deposit(BigInteger? chainId, UInt160? sequencer, BigInteger? amount);

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
    public abstract void Slash(BigInteger? chainId, UInt160? sequencer, BigInteger? amount, UInt160? recipient);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("withdraw")]
    public abstract void Withdraw(BigInteger? chainId, UInt160? sequencer, BigInteger? amount);

    #endregion
}
