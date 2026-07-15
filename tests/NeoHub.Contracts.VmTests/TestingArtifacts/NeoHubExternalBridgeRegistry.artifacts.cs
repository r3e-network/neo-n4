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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":463,""safe"":true},{""name"":""registerVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""Void"",""offset"":521,""safe"":false},{""name"":""upgradeVerifierViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1198,""safe"":false},{""name"":""buildUpgradeVerifierAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1877,""safe"":true},{""name"":""upgradeVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2588,""safe"":false},{""name"":""getVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":2600,""safe"":true},{""name"":""getBridgeKind"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2659,""safe"":true},{""name"":""verifyInbound"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":2690,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":2805,""safe"":false}],""events"":[{""name"":""ExternalVerifierRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Pluggable verifier dispatch table for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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YWRoQWJ9W1J0bCR7DHZwcm9wb3NhbCBwYXlsb2FkIGRvZXMgbm90IG1hdGNoIChleHRlcm5hbENoYWluSWQsIHZlcmlmaWVyLCBicmlkZ2VLaW5kKSBhY3Rpb24gYXJncyAoY291bmNpbCB2b3RlZCBvbiBkaWZmZXJlbnQgYnl0ZXMp4AwBAdswaTXd/P//enl4NeD6//9AVwUDWHBoyhSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnwAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8RnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IcRByIj5oas5KaWpR0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqaMq1JMBoynJ4ShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFedswcxB0Im5rbM5KaWpsnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsABS1JJBqABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRXpKaWpR0EVpIgJA2zBAVwADenl4Nef3//9AVwEBeDWE+f//NW/2//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAQF4NdP5//81NPb//3BoC5cmBRAiBWgQziICQM5AVwEDeDSicGgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCQvDCpubyB2ZXJpZmllciByZWdpc3RlcmVkIGZvciBleHRlcm5hbENoYWluSWTgenl4E8AVDBR2ZXJpZnlJbmJvdW5kTWVzc2FnZWhBYn1bUiICQFYBDBhuZW80LWdvdjp1cGdyYWRlVmVyaWZpZXLbMGBAREqdHA==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delExternalVerifierRegistered(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("ExternalVerifierRegistered")]
    public event delExternalVerifierRegistered? OnExternalVerifierRegistered;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

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
