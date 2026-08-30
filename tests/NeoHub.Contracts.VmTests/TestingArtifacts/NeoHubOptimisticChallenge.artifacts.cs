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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.OptimisticChallenge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":282,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":381,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":502,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":764,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":822,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":749,""safe"":true},{""name"":""getWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":1048,""safe"":true},{""name"":""getChallengerRewardBps"",""parameters"":[],""returntype"":""Integer"",""offset"":1112,""safe"":true},{""name"":""setWindowSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1185,""safe"":false},{""name"":""setWindowSecondsViaProposal"",""parameters"":[{""name"":""seconds"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1417,""safe"":false},{""name"":""setChallengerRewardBps"",""parameters"":[{""name"":""bps"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2437,""safe"":false},{""name"":""setChallengerRewardBpsViaProposal"",""parameters"":[{""name"":""bps"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2669,""safe"":false},{""name"":""registerFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2750,""safe"":false},{""name"":""registerFraudVerifierViaProposal"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3422,""safe"":false},{""name"":""registerPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":3533,""safe"":false},{""name"":""registerPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":3708,""safe"":false},{""name"":""registerPermissionlessFraudProfileViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5026,""safe"":false},{""name"":""revokeFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5536,""safe"":false},{""name"":""revokeFraudVerifierViaProposal"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5782,""safe"":false},{""name"":""buildRegisterFraudVerifierAction"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":3442,""safe"":true},{""name"":""buildRevokeFraudVerifierAction"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":5799,""safe"":true},{""name"":""buildRegisterPermissionlessFraudProfileAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""ByteArray"",""offset"":5052,""safe"":true},{""name"":""buildSetWindowSecondsAction"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":2076,""safe"":true},{""name"":""buildChallengerRewardBpsAction"",""parameters"":[{""name"":""bps"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":2686,""safe"":true},{""name"":""isApprovedFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5890,""safe"":true},{""name"":""isPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5937,""safe"":true},{""name"":""isPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":5944,""safe"":true},{""name"":""isClaimConsumed"",""parameters"":[{""name"":""claimId"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":6303,""safe"":true},{""name"":""openWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":6459,""safe"":false},{""name"":""challenge"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""challenger"",""type"":""Hash160""},{""name"":""fraudProofBytes"",""type"":""ByteArray""},{""name"":""fraudVerifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7220,""safe"":false},{""name"":""finalizeIfPastWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8601,""safe"":false},{""name"":""isWindowOpen"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nowUnixSeconds"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8887,""safe"":true},{""name"":""getDeadline"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8926,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":8961,""safe"":false}],""events"":[{""name"":""WindowOpened"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""ChallengeAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""WindowFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FraudVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PermissionlessVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""FraudProfileApproved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""FraudVerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""WindowSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ChallengerRewardBpsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Versioned optimistic challenge window with profile-bound executable v4 fraud proofs."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.OptimisticChallenge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsACC1JI8QdCJvamzOSmsAOGyeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAILUkj2tbNfTy//8iAkBXAAE1d+v//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DUt7f//qiRaDFVnb3Zlcm5hbmNlIGxvY2tlZCDigJQgaW5zdGFudCBvd25lciBwYXRoIGRpc2FibGVkOyB1c2UgUmV2b2tlRnJhdWRWZXJpZmllclZpYVByb3Bvc2Fs4Hg0A0BXAAF4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQVDBBpbnZhbGlkIHZlcmlmaWVy4Hg1VPX//0V4NXf2//80IXgRwAwURnJhdWRWZXJpZmllclJldm9rZWRBlQFvYUBXAAF4QZv2Z85BL1jF7UBBL1jF7UBXAAJ5eDQMUDX87v//eDSAQFcDAXjbMHAAFIhxEHIiPmhqzkppalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAFLUkwGlcNZLx//8iAkBXAAF4StkoJAZFCSIGygAUs6omBQgiBXgQsyYFCSIReDWw9f//NSrq//8LmCICQFcAAQkiAkBXBgR4EJcmBQgiEXlK2SgkBkUJIgbKABSzqiYFCCIFeRCzJggJIz4BAAB5eDVP+///Nejp//9waAuXJggJIyYBAABo2zBxacoARJgmCAkjFQEAAGkAQM5pAEHOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkmkAQs4gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSaQBDzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGScmp5NRb0//+YJggJI5YAAAB62zBze9swdBB1In5pbc5rbc6YJgUJIntpACBtnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAA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CmdldEJhbGFuY2VvCUFifVtSdwpvChC3JBUMEG5vIGJvbmQgdG8gc2xhc2jgNdPk//93C28KbwugARAnoXcMeXg19AEAAHpQNUjh//9pNQn5//8MAQHbMFA1UeT//2s1yvb//3l4NWr8//81vvb//wwB/NswNYTh//9K2CYORQwIc20gdW5zZXQ6StgkCUrKABQoAzp3DXl4EsAfDAtyZXZlcnRCYXRjaG8NQWJ9W1JFbwwQtyYaem8Mbwh4FMAfDAVzbGFzaG8JQWJ9W1JFbwpvDJ93Dm8OELcmLwwUAAAAAAAAAAAAAAAAAAAAAAAAAABvDm8IeBTAHwwFc2xhc2hvCUFifVtSRW8Kenl4FMAMEUNoYWxsZW5nZUFjY2VwdGVkQZUBb2FAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQNsoStgkCUrKACAoAzpAVwABeBDOeBHOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkngSziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4E84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkiICQFcAAnl4EjX3+P//QFcEAnl4NeP4//9waDWs3///cWkLmCQTDA5ubyBvcGVuIHdpbmRvd+Bp2zA1Wf///3JBt8OIAwHoA6FKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRarckIAwbY2hhbGxlbmdlIHdpbmRvdyBzdGlsbCBvcGVu4Hl4NXr///81Od///wuXJCoMJWJhdGNoIHdhcyBjaGFsbGVuZ2VkOyBjYW5ub3QgZmluYWxpemXgaDU39P//eXg11/n//zUr9P//DAH82zA18d7//0rYJg5FDAhzbSB1bnNldDpK2CQJSsoAFCgDOnN5eBLAHwwNZmluYWxpemVCYXRjaGtBYn1bUkV5eBLADA9XaW5kb3dGaW5hbGl6ZWRBlQFvYUBXAgN5eDXF9///NZDe//9waAuXJgUJIhBo2zA1S/7//3F6abYiAkBXAQJ5eDWe9///NWne//9waAuXJgUQIgpo2zA1JP7//yICQFYFDB5uZW80LWdvdjpyZWdpc3RlckZyYXVkVmVyaWZpZXLbMGIMHG5lbzQtZ292OnJldm9rZUZyYXVkVmVyaWZpZXLbMGQMK25lbzQtZ292OnJlZ2lzdGVyUGVybWlzc2lvbmxlc3NGcmF1ZFByb2ZpbGXbMGMMGW5lbzQtZ292OnNldFdpbmRvd1NlY29uZHPbMGAMH25lbzQtZ292OnNldENoYWxsZW5nZXJSZXdhcmRCcHPbMGFAC10sSQ==").AsSerializable<Neo.SmartContract.NefFile>();

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
    [DisplayName("buildChallengerRewardBpsAction")]
    public abstract byte[]? BuildChallengerRewardBpsAction(BigInteger? bps);

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
    [DisplayName("buildSetWindowSecondsAction")]
    public abstract byte[]? BuildSetWindowSecondsAction(BigInteger? seconds);

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

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setChallengerRewardBpsViaProposal")]
    public abstract void SetChallengerRewardBpsViaProposal(BigInteger? bps, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setWindowSecondsViaProposal")]
    public abstract void SetWindowSecondsViaProposal(BigInteger? seconds, BigInteger? proposalId);

    #endregion
}
