using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubGovernanceController(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.GovernanceController"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":1171,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1270,""safe"":false},{""name"":""isCouncilMember"",""parameters"":[{""name"":""memberKey"",""type"":""PublicKey""}],""returntype"":""Boolean"",""offset"":1391,""safe"":true},{""name"":""getCouncilCount"",""parameters"":[],""returntype"":""Integer"",""offset"":1410,""safe"":true},{""name"":""getThreshold"",""parameters"":[],""returntype"":""Integer"",""offset"":1472,""safe"":true},{""name"":""getCouncilEpoch"",""parameters"":[],""returntype"":""Integer"",""offset"":1523,""safe"":true},{""name"":""getTimelockSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":1582,""safe"":true},{""name"":""getAdmissionMode"",""parameters"":[],""returntype"":""Integer"",""offset"":1633,""safe"":true},{""name"":""setAdmissionMode"",""parameters"":[{""name"":""mode"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1666,""safe"":false},{""name"":""setAdmissionModeViaProposal"",""parameters"":[{""name"":""mode"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1878,""safe"":false},{""name"":""buildSetAdmissionModeAction"",""parameters"":[{""name"":""mode"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":3168,""safe"":true},{""name"":""rotateCouncil"",""parameters"":[{""name"":""oldMembers"",""type"":""Array""},{""name"":""newMembers"",""type"":""Array""},{""name"":""newThreshold"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3305,""safe"":false},{""name"":""buildRotateCouncilAction"",""parameters"":[{""name"":""newMembers"",""type"":""Array""},{""name"":""newThreshold"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4238,""safe"":true},{""name"":""createProposal"",""parameters"":[{""name"":""signer"",""type"":""PublicKey""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":5451,""safe"":false},{""name"":""approve"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""},{""name"":""memberKey"",""type"":""PublicKey""}],""returntype"":""Integer"",""offset"":5765,""safe"":false},{""name"":""getProposal"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6287,""safe"":true},{""name"":""getProposalEpoch"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2803,""safe"":true},{""name"":""getApprovedAt"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2886,""safe"":true},{""name"":""getApprovalCount"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6317,""safe"":true},{""name"":""isApprovedAndTimelocked"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2639,""safe"":true},{""name"":""cancelProposal"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6370,""safe"":false},{""name"":""isVetoed"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2865,""safe"":true},{""name"":""setUpgradeWindows"",""parameters"":[{""name"":""noticeSeconds"",""type"":""Integer""},{""name"":""executionWindowSeconds"",""type"":""Integer""},{""name"":""cooldownSeconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6588,""safe"":false},{""name"":""getUpgradeNoticeSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":6788,""safe"":true},{""name"":""getUpgradeExecutionWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":6843,""safe"":true},{""name"":""getUpgradeCooldownSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":6898,""safe"":true},{""name"":""getProposalExecutedAt"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6526,""safe"":true},{""name"":""getProposalStage"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6953,""safe"":true},{""name"":""isInExecutionWindow"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7373,""safe"":true},{""name"":""markProposalExecuted"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7387,""safe"":false},{""name"":""approveVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7541,""safe"":false},{""name"":""revokeVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7801,""safe"":false},{""name"":""isApprovedVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":7914,""safe"":true},{""name"":""approveBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7933,""safe"":false},{""name"":""revokeBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":8193,""safe"":false},{""name"":""isApprovedBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":8309,""safe"":true},{""name"":""setImmutableFlag"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8328,""safe"":false},{""name"":""setImmutableFlagViaProposal"",""parameters"":[{""name"":""flagId"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8428,""safe"":false},{""name"":""buildSetImmutableFlagAction"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":8598,""safe"":true},{""name"":""matchesProposalPayload"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""},{""name"":""expectedAction"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":3044,""safe"":true},{""name"":""isImmutable"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8735,""safe"":true},{""name"":""setEmergencyCouncil"",""parameters"":[{""name"":""council"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":8754,""safe"":false},{""name"":""getEmergencyCouncil"",""parameters"":[],""returntype"":""Hash160"",""offset"":8874,""safe"":true},{""name"":""pause"",""parameters"":[],""returntype"":""Void"",""offset"":8932,""safe"":false},{""name"":""unpause"",""parameters"":[],""returntype"":""Void"",""offset"":9058,""safe"":false},{""name"":""isPaused"",""parameters"":[],""returntype"":""Boolean"",""offset"":9135,""safe"":true},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9150,""safe"":false},{""name"":""unpauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9381,""safe"":false},{""name"":""isChainPaused"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":9465,""safe"":true},{""name"":""registerSequencer"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""},{""name"":""sequencerAddress"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":9494,""safe"":false},{""name"":""unregisterSequencer"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Void"",""offset"":9899,""safe"":false},{""name"":""isSequencerRegistered"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Boolean"",""offset"":10015,""safe"":true},{""name"":""getSequencerAddress"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Hash160"",""offset"":10035,""safe"":true},{""name"":""depositBond"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":10095,""safe"":false},{""name"":""withdrawBond"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":10619,""safe"":false},{""name"":""slashSequencer"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":10958,""safe"":false},{""name"":""getSequencerBond"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":11191,""safe"":true},{""name"":""hasMinBond"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":11229,""safe"":true},{""name"":""getMinBond"",""parameters"":[],""returntype"":""Integer"",""offset"":10918,""safe"":true},{""name"":""setMinBond"",""parameters"":[{""name"":""minBond"",""type"":""Integer""}],""returntype"":""Void"",""offset"":11249,""safe"":false},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":11330,""safe"":false}],""events"":[{""name"":""ProposalCreated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ProposalApproved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""}]},{""name"":""ImmutableFlagSet"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""UpgradeWindowsSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""ProposalExecuted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ProposalVetoed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""CouncilRotated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Integer""},{""name"":""arg5"",""type"":""Integer""}]},{""name"":""AdmissionModeChanged"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""VerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""VerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""BridgeAdapterApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""BridgeAdapterRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""EmergencyPaused"",""parameters"":[]},{""name"":""EmergencyUnpaused"",""parameters"":[]},{""name"":""ChainPaused"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ChainUnpaused"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""EmergencyCouncilChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""SequencerRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""SequencerUnregistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""}]},{""name"":""BondDeposited"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""BondSlashed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""BondWithdrawn"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Governance controller for the Neo Elastic Network: council, timelocks, admission policy."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.GovernanceController"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AAAAn1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSnh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFQFcDAXg1oAAAADWb+///mCYICSORAAAAeDXKAAAAJggJI4MAAAB4NdEAAABwaBCXJgUJInE1q/v//3FpAegDoEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRckG3w4gDaGqeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJG4IgJAVwEBeAAWNSH9//81zPn//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAF4ABQ14/z//zWO+f//C5giAkBXAQF4HDXP/P//NXr5//9waAuXJgUQIiRoStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6IgJAQbfDiANAVwACeXg0ViRTDE5wcm9wb3NhbCBwYXlsb2FkIGRvZXMgbm90IG1hdGNoIGFjdGlvbiBhcmdzIChjb3VuY2lsIHZvdGVkIG9uIGRpZmZlcmVudCBieXRlcyngQFcDAng0azXg+P//cGgLlyYFCSJcaNswcWnKecqYJgUJIk4QciJBaWrOeWrOmCYFCSI+akqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFamnKtSS9CCICQFcAAXgWNcL7//8iAkBXAgFYyhGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hwEHEiPlhpzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWlYyrUkwHhKaFjKUdBFaCICQFcEBHsAFzUr+///cGg11Pf//wuXJB4MGXByb3Bvc2FsIGFscmVhZHkgY29uc3VtZWTgNWf4//9xeMpplyQ0DC9vbGRNZW1iZXJzIG11c3QgYmUgdGhlIGNvbXBsZXRlIGN1cnJlbnQgY291bmNpbOB4yhC3JCIMHW9sZCBjb3VuY2lsIG11c3QgYmUgbm9uLWVtcHR54HjKAEC2JCUMIG9sZCBjb3VuY2lsIGV4Y2VlZHMgbWF4aW11bSBzaXpl4Hg1SvT//xByImd4as41uff//yQqDCVvbGRNZW1iZXJzIGlzIG5vdCB0aGUgY3VycmVudCBjb3VuY2ls4GpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp4yrUkl3nKELckIgwdbmV3IGNvdW5jaWwgbXVzdCBiZSBub24tZW1wdHngecoAQLYkJQwgbmV3IGNvdW5jaWwgZXhjZWVkcyBtYXhpbXVtIHNpemXgehC3JB8MGnRocmVzaG9sZCBtdXN0IGJlIHBvc2l0aXZl4Hp5yrYkJwwidGhyZXNob2xkIGV4Y2VlZHMgbmV3IGNvdW5jaWwgc2l6ZeB5NTjz//81Mvf//3JqBP//////////AAAAAAAAAAC1JBwMF2NvdW5jaWwgZXBvY2ggZXhoYXVzdGVk4Hs1/Pv//2qXJCMMHnByb3Bvc2FsIGNvdW5jaWwgZXBvY2ggZXhwaXJlZOB7NS37//8kJwwicHJvcG9zYWwgbm90IGFwcHJvdmVkICsgdGltZWxvY2tlZOB7enk1PQEAAFA1Mvz//wwBAdswaDVg9P//EHMiQnhrzjVq9P//NcMFAABrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VreMq1JLwQcyJIeWvONSD0//8MAQHbMFA1//P//2tKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWt5yrUktmoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRcwwBAtswecpQNXPz//96DAED2zA1aPP//2sMARXbMDVd8///ennKShAuBCIOSgP/////AAAAADIMA/////8AAAAAkWtqexXADA5Db3VuY2lsUm90YXRlZEGVAW9hQFcGAnjKELckIgwdbmV3IGNvdW5jaWwgbXVzdCBiZSBub24tZW1wdHngeMoAQLYkJQwgbmV3IGNvdW5jaWwgZXhjZWVkcyBtYXhpbXVtIHNpemXgeRC3JB8MGnRocmVzaG9sZCBtdXN0IGJlIHBvc2l0aXZl4Hl4yrYkJwwidGhyZXNob2xkIGV4Y2VlZHMgbmV3IGNvdW5jaWwgc2l6ZeB4NcXw//9ZyhieSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3jKACGgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHAQcRByIm9Zas5KaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFalnKtSSPNVDz//9yamloNYT1//9pGJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFeWloNVcBAABpFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFaGl4ykoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFTNf0AAABpFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFEHMjtQAAAHhrztswdBB1Im9sbc5KaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAhtSSPa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa3jKtSVM////aCICQFcAA3pKEC4EIghKAf8AMgYB/wCRSnh5UdBFehipShAuBCIISgH/ADIGAf8AkUp4eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXogqUoQLgQiCEoB/wAyBgH/AJFKeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ABipShAuBCIISgH/ADIGAf8AkUp4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRUBXAAF4QZv2Z85BL1jF7UBBL1jF7UBXAgJ4NSDw//8kGQwUbm90IGEgY291bmNpbCBtZW1iZXLgeEH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HnKELckGwwWZW1wdHkgcHJvcG9zYWwgcGF5bG9hZOAMAQjbMDUg7///cGgLlyYFESIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOnFpBP//////////AAAAAAAAAAC1JBoMFXByb3Bvc2FsIGlkIGV4aGF1c3RlZOBpEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkQwBCNswNXDt//9pNRH2//95UDV57f//aQAWNcnx//81ne///1A1UO3//3lpEsAMD1Byb3Bvc2FsQ3JlYXRlZEGVAW9haSICQEH4J+yMQFcBAng1yvX//zU87v//C5gkFQwQdW5rbm93biBwcm9wb3NhbOB4NUj0//81Q+///5ckIwwecHJvcG9zYWwgY291bmNpbCBlcG9jaCBleHBpcmVk4Hk1le7//yQZDBRub3QgYSBjb3VuY2lsIG1lbWJlcuB5Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeXg0T3BoNbPt//8LlyQVDBBhbHJlYWR5IGFwcHJvdmVk4AwBAdswaDWJ7P//eXgSwAwQUHJvcG9zYWxBcHByb3ZlZEGVAW9heDWcAAAAIgJAVwMCACqIcBdKaBBR0EV4EWg1vfD//3nbMHEQciJuaWrOSmgZap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAhtSSQaCICQFcGAXgZNR/w//9waDXI7P//cWkLlyYFECIcaUrYJgZFECIE2yFKEAMAAAAAAQAAALskAzpyahGeShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXNraDVf6///NW7t//90bBC3JAUJIgVqbLUkBQkiBWtsuCYgeBw1q+///3VtNVTs//8LlyYObUG3w4gDUDUm6///ayICQFcBAXg1wPP//zUy7P//cGgLlyYGEIgiBWjbMCICQFcBAXgZNWjv//81E+z//3BoC5cmBRAiHGhK2CYGRRAiBNshShADAAAAAAEAAAC7JAM6IgJAVwEBNa7r//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NVDz//81wuv//wuYJBUMEHVua25vd24gcHJvcG9zYWzgeDRZEJckHgwZcHJvcG9zYWwgYWxyZWFkeSBleGVjdXRlZOB4ABQ10O7//3BoNXnr//8LlyYlDAEB2zBoNWLq//94EcAMDlByb3Bvc2FsVmV0b2VkQZUBb2FAVwEBeAASNZbu//81Qev//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAM11Or//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgQtyQcDBdub3RpY2UgbXVzdCBiZSBwb3NpdGl2ZeB5ELckJgwhZXhlY3V0aW9uIHdpbmRvdyBtdXN0IGJlIHBvc2l0aXZl4HoQtyQeDBljb29sZG93biBtdXN0IGJlIHBvc2l0aXZl4HgMAQ/bMDVh6f//eQwBENswNVbp//96DAER2zA1S+n//3p5eBPADBFVcGdyYWRlV2luZG93c1NldEGVAW9hQFcBAAwBD9swNT7q//9waAuXJgk1l+v//yIcaErYJgZFECIE2yFKEAMAAAAAAQAAALskAzoiAkBXAQAMARDbMDUH6v//cGgLlyYJNWDr//8iHGhK2CYGRRAiBNshShADAAAAAAEAAAC7JAM6IgJAVwEADAER2zA10On//3BoC5cmCTUp6///IhxoStgmBkUQIgTbIUoQAwAAAAABAAAAuyQDOiICQFcEAXg1JvH//zWY6f//C5cmCBAjkAEAAHg1se///zWs6v//mCYIFSN8AQAAeDXw7///cGgQlyYIECNqAQAAaDUc////AegDoEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRcUG3w4gDabUmCBEj9wAAAHg1o/3//3JqELcmdWo1C////wHoA6BKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXNBt8OIA2u1JgUTIgMUInVpNWH+//8B6AOgShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFzQbfDiANrtiYFEiIDFSICQFcAAXg1WP7//xKXIgJAVwEBNbXn//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NNEkHAwXcHJvcG9zYWwgbm90IGV4ZWN1dGFibGXgeAASNf3q//9waDWm5///C5ckHgwZcHJvcG9zYWwgYWxyZWFkeSBleGVjdXRlZOBoQbfDiANQNVzm//9Bt8OIA3gSwAwQUHJvcG9zYWxFeGVjdXRlZEGVAW9hQFcAAXhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBUMEGludmFsaWQgdmVyaWZpZXLgNe/m//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NCgMAQHbMFA1+OX//3gRwAwQVmVyaWZpZXJBcHByb3ZlZEGVAW9hQFcDAQAViHAaSmgQUdBFeNswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqABS1JJBoIgJA2zBAVwABeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFQwQaW52YWxpZCB2ZXJpZmllcuA16+X//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hg1JP///zVr9v//eBHADA9WZXJpZmllclJldm9rZWRBlQFvYUBXAAF4Nfz+//811+X//wuYIgJAVwABeErZKCQGRQkiBsoAFLMkBQkiBngQ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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delAdmissionModeChanged(BigInteger? obj);

    [DisplayName("AdmissionModeChanged")]
    public event delAdmissionModeChanged? OnAdmissionModeChanged;

    public delegate void delBondDeposited(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("BondDeposited")]
    public event delBondDeposited? OnBondDeposited;

    public delegate void delBondSlashed(BigInteger? arg1, UInt160? arg2, BigInteger? arg3, UInt160? arg4);

    [DisplayName("BondSlashed")]
    public event delBondSlashed? OnBondSlashed;

    public delegate void delBondWithdrawn(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("BondWithdrawn")]
    public event delBondWithdrawn? OnBondWithdrawn;

    public delegate void delBridgeAdapterApproved(UInt160? obj);

    [DisplayName("BridgeAdapterApproved")]
    public event delBridgeAdapterApproved? OnBridgeAdapterApproved;

    public delegate void delBridgeAdapterRevoked(UInt160? obj);

    [DisplayName("BridgeAdapterRevoked")]
    public event delBridgeAdapterRevoked? OnBridgeAdapterRevoked;

    public delegate void delChainPaused(BigInteger? obj);

    [DisplayName("ChainPaused")]
    public event delChainPaused? OnChainPaused;

    public delegate void delChainUnpaused(BigInteger? obj);

    [DisplayName("ChainUnpaused")]
    public event delChainUnpaused? OnChainUnpaused;

    public delegate void delCouncilRotated(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, BigInteger? arg4, BigInteger? arg5);

    [DisplayName("CouncilRotated")]
    public event delCouncilRotated? OnCouncilRotated;

    public delegate void delEmergencyCouncilChanged(UInt160? obj);

    [DisplayName("EmergencyCouncilChanged")]
    public event delEmergencyCouncilChanged? OnEmergencyCouncilChanged;

    public delegate void delEmergencyPaused();

    [DisplayName("EmergencyPaused")]
    public event delEmergencyPaused? OnEmergencyPaused;

    public delegate void delEmergencyUnpaused();

    [DisplayName("EmergencyUnpaused")]
    public event delEmergencyUnpaused? OnEmergencyUnpaused;

    public delegate void delImmutableFlagSet(BigInteger? obj);

    [DisplayName("ImmutableFlagSet")]
    public event delImmutableFlagSet? OnImmutableFlagSet;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delProposalApproved(BigInteger? arg1, ECPoint? arg2);

    [DisplayName("ProposalApproved")]
    public event delProposalApproved? OnProposalApproved;

    public delegate void delProposalCreated(BigInteger? arg1, byte[]? arg2);

    [DisplayName("ProposalCreated")]
    public event delProposalCreated? OnProposalCreated;

    public delegate void delProposalExecuted(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ProposalExecuted")]
    public event delProposalExecuted? OnProposalExecuted;

    public delegate void delProposalVetoed(BigInteger? obj);

    [DisplayName("ProposalVetoed")]
    public event delProposalVetoed? OnProposalVetoed;

    public delegate void delSequencerRegistered(BigInteger? arg1, ECPoint? arg2, UInt160? arg3);

    [DisplayName("SequencerRegistered")]
    public event delSequencerRegistered? OnSequencerRegistered;

    public delegate void delSequencerUnregistered(BigInteger? arg1, ECPoint? arg2);

    [DisplayName("SequencerUnregistered")]
    public event delSequencerUnregistered? OnSequencerUnregistered;

    public delegate void delUpgradeWindowsSet(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("UpgradeWindowsSet")]
    public event delUpgradeWindowsSet? OnUpgradeWindowsSet;

    public delegate void delVerifierApproved(UInt160? obj);

    [DisplayName("VerifierApproved")]
    public event delVerifierApproved? OnVerifierApproved;

    public delegate void delVerifierRevoked(UInt160? obj);

    [DisplayName("VerifierRevoked")]
    public event delVerifierRevoked? OnVerifierRevoked;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? AdmissionMode { [DisplayName("getAdmissionMode")] get; [DisplayName("setAdmissionMode")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? CouncilCount { [DisplayName("getCouncilCount")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? CouncilEpoch { [DisplayName("getCouncilEpoch")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? EmergencyCouncil { [DisplayName("getEmergencyCouncil")] get; [DisplayName("setEmergencyCouncil")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? MinBond { [DisplayName("getMinBond")] get; [DisplayName("setMinBond")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? Threshold { [DisplayName("getThreshold")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? TimelockSeconds { [DisplayName("getTimelockSeconds")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? UpgradeCooldownSeconds { [DisplayName("getUpgradeCooldownSeconds")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? UpgradeExecutionWindowSeconds { [DisplayName("getUpgradeExecutionWindowSeconds")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? UpgradeNoticeSeconds { [DisplayName("getUpgradeNoticeSeconds")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsPaused { [DisplayName("isPaused")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRotateCouncilAction")]
    public abstract byte[]? BuildRotateCouncilAction(IList<object>? newMembers, BigInteger? newThreshold);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetAdmissionModeAction")]
    public abstract byte[]? BuildSetAdmissionModeAction(BigInteger? mode);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetImmutableFlagAction")]
    public abstract byte[]? BuildSetImmutableFlagAction(BigInteger? flagId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getApprovalCount")]
    public abstract BigInteger? GetApprovalCount(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getApprovedAt")]
    public abstract BigInteger? GetApprovedAt(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProposal")]
    public abstract byte[]? GetProposal(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProposalEpoch")]
    public abstract BigInteger? GetProposalEpoch(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProposalExecutedAt")]
    public abstract BigInteger? GetProposalExecutedAt(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProposalStage")]
    public abstract BigInteger? GetProposalStage(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSequencerAddress")]
    public abstract UInt160? GetSequencerAddress(BigInteger? chainId, ECPoint? sequencerKey);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSequencerBond")]
    public abstract BigInteger? GetSequencerBond(BigInteger? chainId, UInt160? sequencer);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("hasMinBond")]
    public abstract bool? HasMinBond(BigInteger? chainId, UInt160? sequencer);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedBridgeAdapter")]
    public abstract bool? IsApprovedBridgeAdapter(UInt160? bridge);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedVerifier")]
    public abstract bool? IsApprovedVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isChainPaused")]
    public abstract bool? IsChainPaused(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isCouncilMember")]
    public abstract bool? IsCouncilMember(ECPoint? memberKey);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isImmutable")]
    public abstract bool? IsImmutable(BigInteger? flagId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isInExecutionWindow")]
    public abstract bool? IsInExecutionWindow(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isSequencerRegistered")]
    public abstract bool? IsSequencerRegistered(BigInteger? chainId, ECPoint? sequencerKey);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isVetoed")]
    public abstract bool? IsVetoed(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedAction);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("approve")]
    public abstract BigInteger? Approve(BigInteger? proposalId, ECPoint? memberKey);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("approveBridgeAdapter")]
    public abstract void ApproveBridgeAdapter(UInt160? bridge);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("approveVerifier")]
    public abstract void ApproveVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("cancelProposal")]
    public abstract void CancelProposal(BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("createProposal")]
    public abstract BigInteger? CreateProposal(ECPoint? signer, byte[]? payload);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("depositBond")]
    public abstract void DepositBond(BigInteger? chainId, UInt160? sequencer, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("markProposalExecuted")]
    public abstract void MarkProposalExecuted(BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("pause")]
    public abstract void Pause();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("pauseChain")]
    public abstract void PauseChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerSequencer")]
    public abstract void RegisterSequencer(BigInteger? chainId, ECPoint? sequencerKey, UInt160? sequencerAddress);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeBridgeAdapter")]
    public abstract void RevokeBridgeAdapter(UInt160? bridge);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeVerifier")]
    public abstract void RevokeVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("rotateCouncil")]
    public abstract void RotateCouncil(IList<object>? oldMembers, IList<object>? newMembers, BigInteger? newThreshold, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setAdmissionModeViaProposal")]
    public abstract void SetAdmissionModeViaProposal(BigInteger? mode, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setImmutableFlag")]
    public abstract void SetImmutableFlag(BigInteger? flagId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setImmutableFlagViaProposal")]
    public abstract void SetImmutableFlagViaProposal(BigInteger? flagId, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setUpgradeWindows")]
    public abstract void SetUpgradeWindows(BigInteger? noticeSeconds, BigInteger? executionWindowSeconds, BigInteger? cooldownSeconds);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("slashSequencer")]
    public abstract void SlashSequencer(BigInteger? chainId, UInt160? sequencer, BigInteger? amount, UInt160? recipient);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("unpause")]
    public abstract void Unpause();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("unpauseChain")]
    public abstract void UnpauseChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("unregisterSequencer")]
    public abstract void UnregisterSequencer(BigInteger? chainId, ECPoint? sequencerKey);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("withdrawBond")]
    public abstract bool? WithdrawBond(BigInteger? chainId, UInt160? sequencer, BigInteger? amount);

    #endregion
}
