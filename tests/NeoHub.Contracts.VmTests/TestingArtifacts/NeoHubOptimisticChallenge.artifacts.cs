using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubOptimisticChallenge(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.OptimisticChallenge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":282,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":381,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":502,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":764,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":822,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":749,""safe"":true},{""name"":""getWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":1048,""safe"":true},{""name"":""getChallengerRewardBps"",""parameters"":[],""returntype"":""Integer"",""offset"":1112,""safe"":true},{""name"":""setWindowSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1185,""safe"":false},{""name"":""setChallengerRewardBps"",""parameters"":[{""name"":""bps"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1317,""safe"":false},{""name"":""registerFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1443,""safe"":false},{""name"":""registerFraudVerifierViaProposal"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2113,""safe"":false},{""name"":""registerPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":3111,""safe"":false},{""name"":""registerPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":3286,""safe"":false},{""name"":""registerPermissionlessFraudProfileViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4605,""safe"":false},{""name"":""revokeFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5114,""safe"":false},{""name"":""revokeFraudVerifierViaProposal"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5360,""safe"":false},{""name"":""buildRegisterFraudVerifierAction"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":2774,""safe"":true},{""name"":""buildRevokeFraudVerifierAction"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":5376,""safe"":true},{""name"":""buildRegisterPermissionlessFraudProfileAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""ByteArray"",""offset"":4630,""safe"":true},{""name"":""isApprovedFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5467,""safe"":true},{""name"":""isPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5514,""safe"":true},{""name"":""isPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":5521,""safe"":true},{""name"":""isClaimConsumed"",""parameters"":[{""name"":""claimId"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":5889,""safe"":true},{""name"":""openWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":6045,""safe"":false},{""name"":""challenge"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""challenger"",""type"":""Hash160""},{""name"":""fraudProofBytes"",""type"":""ByteArray""},{""name"":""fraudVerifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":6804,""safe"":false},{""name"":""finalizeIfPastWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8165,""safe"":false},{""name"":""isWindowOpen"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nowUnixSeconds"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8451,""safe"":true},{""name"":""getDeadline"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8490,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":8525,""safe"":false}],""events"":[{""name"":""WindowOpened"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""ChallengeAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""WindowFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FraudVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PermissionlessVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""FraudProfileApproved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""FraudVerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""WindowSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ChallengerRewardBpsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Versioned optimistic challenge window with profile-bound executable v4 fraud proofs."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.OptimisticChallenge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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ElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXnbMHEQciJuaWrOSmgVap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAUtSSQaCICQFcABXt6eXg0Enw1Tfb//3t6eXg1XPv//0BXBQR52zBwetswcXvbMHIAWIhzeEoQLgQiCEoB/wAyBgH/AJFKaxBR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmsRUdBFeCCpShAuBCIISgH/ADIGAf8AkUprElHQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmsTUdBFEHQibmhszkprFGyeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkBB0Im9pbM5KawAYbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAgtSSPEHQib2pszkprADhsnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsACC1JI9rWTU89///IgJAVwABNR3t//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA10+7//6okWgxVZ292ZXJuYW5jZSBsb2NrZWQg4oCUIGluc3RhbnQgb3duZXIgcGF0aCBkaXNhYmxlZDsgdXNlIFJldm9rZUZyYXVkVmVyaWZpZXJWaWFQcm9wb3NhbOB4NANAVwABeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFQwQaW52YWxpZCB2ZXJpZmllcuB4Nd7x//9FeDUA8///NCF4EcAMFEZyYXVkVmVyaWZpZXJSZXZva2VkQZUBb2FAVwABeEGb9mfOQS9Yxe1AQS9Yxe1AVwACeDQMeTVd8///eDSBQFcDAXjbMHAAFIhxEHIiPmhqzkppalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAFLUkwGlaNdv1//8iAkBXAAF4StkoJAZFCSIGygAUs6omBQgiBXgQsyYFCSIReDU68v//NdHr//8LmCICQFcAAQkiAkBXBgR4EJcmBQgiEXlK2SgkBkUJIgbKABSzqiYFCCIFeRCzJggJI0cBAAB5eDVR+///NY/r//9waAuXJggJIy8BAABo2zBxacoARJgmCAkjHgEAAGkAQM5pAEHOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkmkAQs4gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSaQBDzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGScmp5NaDw//+YJggJI58AAAB62zBze9swdBB1I4QAAABpbc5rbc6YJggJI4EAAABpACBtnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/ObG3OmCYFCSJBbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSV9////CCICQNswQFcAAXg0DDVK6v//C5giAkBXAwEAIYhwGEpoEFHQRXjbMHEQciJuaWrOSmgRap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQaCICQFcDAwwB/NswNazp//9K2CYORQwIc20gdW5zZXQ6StgkCUrKABQoAzpwaEH4J+yMJBsMFm5vdCBzZXR0bGVtZW50IG1hbmFnZXLgeBC3JCEMHGNoYWluSWQgMCBpcyByZXNlcnZlZCBmb3IgTDHgekrZKCQGRQkiBsoAFLMkBQkiBnoQs6okFgwRaW52YWxpZCBzZXF1ZW5jZXLgQbfDiAMB6AOhShAuBCIOSgP/////AAAA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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delChallengeAccepted(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, BigInteger? arg4);

    [DisplayName("ChallengeAccepted")]
    public event delChallengeAccepted? OnChallengeAccepted;

    public delegate void delChallengerRewardBpsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ChallengerRewardBpsChanged")]
    public event delChallengerRewardBpsChanged? OnChallengerRewardBpsChanged;

    public delegate void delFraudProfileApproved(BigInteger? arg1, UInt160? arg2, UInt256? arg3, UInt256? arg4);

    [DisplayName("FraudProfileApproved")]
    public event delFraudProfileApproved? OnFraudProfileApproved;

    public delegate void delFraudVerifierApproved(UInt160? obj);

    [DisplayName("FraudVerifierApproved")]
    public event delFraudVerifierApproved? OnFraudVerifierApproved;

    public delegate void delFraudVerifierRevoked(UInt160? obj);

    [DisplayName("FraudVerifierRevoked")]
    public event delFraudVerifierRevoked? OnFraudVerifierRevoked;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delPermissionlessVerifierApproved(UInt160? obj);

    [DisplayName("PermissionlessVerifierApproved")]
    public event delPermissionlessVerifierApproved? OnPermissionlessVerifierApproved;

    public delegate void delWindowFinalized(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("WindowFinalized")]
    public event delWindowFinalized? OnWindowFinalized;

    public delegate void delWindowOpened(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, UInt160? arg4);

    [DisplayName("WindowOpened")]
    public event delWindowOpened? OnWindowOpened;

    public delegate void delWindowSecondsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("WindowSecondsChanged")]
    public event delWindowSecondsChanged? OnWindowSecondsChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? ChallengerRewardBps { [DisplayName("getChallengerRewardBps")] get; [DisplayName("setChallengerRewardBps")] set; }

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
    public abstract BigInteger? WindowSeconds { [DisplayName("getWindowSeconds")] get; [DisplayName("setWindowSeconds")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRegisterFraudVerifierAction")]
    public abstract byte[]? BuildRegisterFraudVerifierAction(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRegisterPermissionlessFraudProfileAction")]
    public abstract byte[]? BuildRegisterPermissionlessFraudProfileAction(BigInteger? chainId, UInt160? verifier, UInt256? executorSemanticId, UInt256? replayDomain);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRevokeFraudVerifierAction")]
    public abstract byte[]? BuildRevokeFraudVerifierAction(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getDeadline")]
    public abstract BigInteger? GetDeadline(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedFraudVerifier")]
    public abstract bool? IsApprovedFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isClaimConsumed")]
    public abstract bool? IsClaimConsumed(UInt256? claimId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isPermissionlessFraudProfile")]
    public abstract bool? IsPermissionlessFraudProfile(BigInteger? chainId, UInt160? verifier, UInt256? executorSemanticId, UInt256? replayDomain);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isPermissionlessFraudVerifier")]
    public abstract bool? IsPermissionlessFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isWindowOpen")]
    public abstract bool? IsWindowOpen(BigInteger? chainId, BigInteger? batchNumber, BigInteger? nowUnixSeconds);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("challenge")]
    public abstract void Challenge(BigInteger? chainId, BigInteger? batchNumber, UInt160? challenger, byte[]? fraudProofBytes, UInt160? fraudVerifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeIfPastWindow")]
    public abstract void FinalizeIfPastWindow(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("openWindow")]
    public abstract BigInteger? OpenWindow(BigInteger? chainId, BigInteger? batchNumber, UInt160? sequencer);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerFraudVerifier")]
    public abstract void RegisterFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerFraudVerifierViaProposal")]
    public abstract void RegisterFraudVerifierViaProposal(UInt160? verifier, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerPermissionlessFraudProfile")]
    public abstract void RegisterPermissionlessFraudProfile(BigInteger? chainId, UInt160? verifier, UInt256? executorSemanticId, UInt256? replayDomain);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerPermissionlessFraudProfileViaProposal")]
    public abstract void RegisterPermissionlessFraudProfileViaProposal(BigInteger? chainId, UInt160? verifier, UInt256? executorSemanticId, UInt256? replayDomain, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerPermissionlessFraudVerifier")]
    public abstract void RegisterPermissionlessFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeFraudVerifier")]
    public abstract void RevokeFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeFraudVerifierViaProposal")]
    public abstract void RevokeFraudVerifierViaProposal(UInt160? verifier, BigInteger? proposalId);

    #endregion
}
