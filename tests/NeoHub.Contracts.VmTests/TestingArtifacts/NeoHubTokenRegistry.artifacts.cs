using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubTokenRegistry(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.TokenRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""registerMapping"",""parameters"":[{""name"":""mappingBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""setActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1644,""safe"":false},{""name"":""getMapping"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1872,""safe"":true},{""name"":""getL2Asset"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":1903,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1963,""safe"":true},{""name"":""getL1Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2000,""safe"":true},{""name"":""getL2Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2035,""safe"":true}],""events"":[{""name"":""MappingRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""MappingActiveChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Canonical L1 \u2194 L2 asset mapping registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.TokenRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delMappingActiveChanged(UInt160? arg1, BigInteger? arg2, bool? arg3);

    [DisplayName("MappingActiveChanged")]
    public event delMappingActiveChanged? OnMappingActiveChanged;

    public delegate void delMappingRegistered(UInt160? arg1, BigInteger? arg2, UInt160? arg3);

    [DisplayName("MappingRegistered")]
    public event delMappingRegistered? OnMappingRegistered;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1Decimals")]
    public abstract BigInteger? GetL1Decimals(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2Asset")]
    public abstract UInt160? GetL2Asset(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2Decimals")]
    public abstract BigInteger? GetL2Decimals(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getMapping")]
    public abstract byte[]? GetMapping(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isActive")]
    public abstract bool? IsActive(UInt160? l1Asset, BigInteger? chainId);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerMapping")]
    public abstract void RegisterMapping(byte[]? mappingBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setActive")]
    public abstract void SetActive(UInt160? l1Asset, BigInteger? chainId, bool? active);

    #endregion
}
