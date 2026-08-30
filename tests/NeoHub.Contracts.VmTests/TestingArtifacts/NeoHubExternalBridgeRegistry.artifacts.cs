using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubExternalBridgeRegistry(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":540,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":525,""safe"":true},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":741,""safe"":true},{""name"":""registerVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""Void"",""offset"":821,""safe"":false},{""name"":""upgradeVerifierViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1568,""safe"":false},{""name"":""buildUpgradeVerifierAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":2247,""safe"":true},{""name"":""upgradeVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2958,""safe"":false},{""name"":""getVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":2970,""safe"":true},{""name"":""getBridgeKind"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3029,""safe"":true},{""name"":""verifyInbound"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":3060,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":3175,""safe"":false}],""events"":[{""name"":""ExternalVerifierRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Pluggable verifier dispatch table for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delExternalVerifierRegistered(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("ExternalVerifierRegistered")]
    public event delExternalVerifierRegistered? OnExternalVerifierRegistered;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildUpgradeVerifierAction")]
    public abstract byte[]? BuildUpgradeVerifierAction(BigInteger? externalChainId, UInt160? verifier, BigInteger? bridgeKind);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBridgeKind")]
    public abstract BigInteger? GetBridgeKind(BigInteger? externalChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getVerifier")]
    public abstract UInt160? GetVerifier(BigInteger? externalChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyInbound")]
    public abstract bool? VerifyInbound(BigInteger? externalChainId, byte[]? messageBytes, byte[]? proofBytes);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerVerifier")]
    public abstract void RegisterVerifier(BigInteger? externalChainId, UInt160? verifier, BigInteger? bridgeKind);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("upgradeVerifier")]
    public abstract void UpgradeVerifier(BigInteger? externalChainId, UInt160? verifier, BigInteger? bridgeKind);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("upgradeVerifierViaProposal")]
    public abstract void UpgradeVerifierViaProposal(BigInteger? externalChainId, UInt160? verifier, BigInteger? bridgeKind, BigInteger? proposalId);

    #endregion
}
