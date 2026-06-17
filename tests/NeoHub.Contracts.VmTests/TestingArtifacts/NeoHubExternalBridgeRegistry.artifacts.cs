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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":463,""safe"":true},{""name"":""registerVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""Void"",""offset"":521,""safe"":false},{""name"":""upgradeVerifierViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1200,""safe"":false},{""name"":""buildUpgradeVerifierAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1879,""safe"":true},{""name"":""upgradeVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""bridgeKind"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2590,""safe"":false},{""name"":""getVerifier"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":2602,""safe"":true},{""name"":""getBridgeKind"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2661,""safe"":true},{""name"":""verifyInbound"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":2692,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":2807,""safe"":false}],""events"":[{""name"":""ExternalVerifierRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Pluggable verifier dispatch table for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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bG9hZGhBYn1bUnRsJHsMdnByb3Bvc2FsIHBheWxvYWQgZG9lcyBub3QgbWF0Y2ggKGV4dGVybmFsQ2hhaW5JZCwgdmVyaWZpZXIsIGJyaWRnZUtpbmQpIGFjdGlvbiBhcmdzIChjb3VuY2lsIHZvdGVkIG9uIGRpZmZlcmVudCBieXRlcyngDAEB2zBpNd38//96eXg13vr//0BXBQNYcGjKFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxEHIiPmhqzkppalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWpoyrUkwGjKcnhKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeCCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV4ABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV52zBzEHQibmtszkppamyeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkGoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFekppalHQRWkiAkDbMEBXAAN6eXg15ff//0BXAQF4NYT5//81bfb//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcBAXg10/n//zUy9v//cGgLlyYFECIFaBDOIgJAzkBXAQN4NKJwaAwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJC8MKm5vIHZlcmlmaWVyIHJlZ2lzdGVyZWQgZm9yIGV4dGVybmFsQ2hhaW5JZOB6eXgTwBUMFHZlcmlmeUluYm91bmRNZXNzYWdlaEFifVtSIgJAVgEMGG5lbzQtZ292OnVwZ3JhZGVWZXJpZmllctswYEDLhDrv").AsSerializable<Neo.SmartContract.NefFile>();

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
