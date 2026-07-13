using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubVerifierRegistry(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.VerifierRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""registerVerifier"",""parameters"":[{""name"":""proofType"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":623,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":452,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":904,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":824,""safe"":true},{""name"":""registerVerifierViaProposal"",""parameters"":[{""name"":""proofType"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1141,""safe"":false},{""name"":""buildRegisterVerifierAction"",""parameters"":[{""name"":""proofType"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":1806,""safe"":true},{""name"":""getVerifier"",""parameters"":[{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":2174,""safe"":true},{""name"":""verifyCommitment"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":2237,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":2337,""safe"":false}],""events"":[{""name"":""VerifierRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Pluggable proof verifier dispatch table for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.VerifierRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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dmVyaWZpZXIpIGFjdGlvbiBhcmdzIChjb3VuY2lsIHZvdGVkIG9uIGRpZmZlcmVudCBieXRlcyngDAEB2zBpNXf8//95eDXR+v//QEFifVtSQFcEAlhwaMoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8AFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHEQciI+aGrOSmlqUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFamjKtSTAeEppaMpR0EV52zByEHMjogAAAGprzkppaMoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9rnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrABS1JV////9pIgJA2zBAVwEBEohKEBHQShF40DUV+P//cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwIBeMoBPAG3JBkMFGNvbW1pdG1lbnQgdG9vIHNtYWxs4HgBPAHOcGg0mHFpELOqJB8MGm5vIHZlcmlmaWVyIGZvciBwcm9vZiB0eXBl4HgRwBUMBnZlcmlmeWlBYn1bUiICQFYBDBluZW80LWdvdjpyZWdpc3RlclZlcmlmaWVy2zBgQET6aOM=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delVerifierRegistered(BigInteger? arg1, UInt160? arg2);

    [DisplayName("VerifierRegistered")]
    public event delVerifierRegistered? OnVerifierRegistered;

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
    [DisplayName("buildRegisterVerifierAction")]
    public abstract byte[]? BuildRegisterVerifierAction(BigInteger? proofType, UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getVerifier")]
    public abstract UInt160? GetVerifier(BigInteger? proofType);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyCommitment")]
    public abstract bool? VerifyCommitment(byte[]? commitmentBytes);

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
    public abstract void RegisterVerifier(BigInteger? proofType, UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerVerifierViaProposal")]
    public abstract void RegisterVerifierViaProposal(BigInteger? proofType, UInt160? verifier, BigInteger? proposalId);

    #endregion
}
