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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.GovernanceController"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":1169,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1268,""safe"":false},{""name"":""isCouncilMember"",""parameters"":[{""name"":""memberKey"",""type"":""PublicKey""}],""returntype"":""Boolean"",""offset"":1389,""safe"":true},{""name"":""getCouncilCount"",""parameters"":[],""returntype"":""Integer"",""offset"":1408,""safe"":true},{""name"":""getThreshold"",""parameters"":[],""returntype"":""Integer"",""offset"":1470,""safe"":true},{""name"":""getCouncilEpoch"",""parameters"":[],""returntype"":""Integer"",""offset"":1521,""safe"":true},{""name"":""getTimelockSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":1580,""safe"":true},{""name"":""getAdmissionMode"",""parameters"":[],""returntype"":""Integer"",""offset"":1631,""safe"":true},{""name"":""setAdmissionMode"",""parameters"":[{""name"":""mode"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1664,""safe"":false},{""name"":""setAdmissionModeViaProposal"",""parameters"":[{""name"":""mode"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1876,""safe"":false},{""name"":""buildSetAdmissionModeAction"",""parameters"":[{""name"":""mode"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":3165,""safe"":true},{""name"":""rotateCouncil"",""parameters"":[{""name"":""oldMembers"",""type"":""Array""},{""name"":""newMembers"",""type"":""Array""},{""name"":""newThreshold"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3302,""safe"":false},{""name"":""buildRotateCouncilAction"",""parameters"":[{""name"":""newMembers"",""type"":""Array""},{""name"":""newThreshold"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4232,""safe"":true},{""name"":""createProposal"",""parameters"":[{""name"":""signer"",""type"":""PublicKey""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":5444,""safe"":false},{""name"":""approve"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""},{""name"":""memberKey"",""type"":""PublicKey""}],""returntype"":""Integer"",""offset"":5756,""safe"":false},{""name"":""getProposal"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6277,""safe"":true},{""name"":""getProposalEpoch"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2800,""safe"":true},{""name"":""getApprovedAt"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2883,""safe"":true},{""name"":""getApprovalCount"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6307,""safe"":true},{""name"":""isApprovedAndTimelocked"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2636,""safe"":true},{""name"":""cancelProposal"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6360,""safe"":false},{""name"":""isVetoed"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2862,""safe"":true},{""name"":""setUpgradeWindows"",""parameters"":[{""name"":""noticeSeconds"",""type"":""Integer""},{""name"":""executionWindowSeconds"",""type"":""Integer""},{""name"":""cooldownSeconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6578,""safe"":false},{""name"":""getUpgradeNoticeSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":6778,""safe"":true},{""name"":""getUpgradeExecutionWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":6833,""safe"":true},{""name"":""getUpgradeCooldownSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":6888,""safe"":true},{""name"":""getProposalExecutedAt"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6516,""safe"":true},{""name"":""getProposalStage"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6943,""safe"":true},{""name"":""isInExecutionWindow"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7363,""safe"":true},{""name"":""markProposalExecuted"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7377,""safe"":false},{""name"":""approveVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7530,""safe"":false},{""name"":""revokeVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7789,""safe"":false},{""name"":""isApprovedVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":7902,""safe"":true},{""name"":""approveBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7921,""safe"":false},{""name"":""revokeBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":8180,""safe"":false},{""name"":""isApprovedBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":8296,""safe"":true},{""name"":""setImmutableFlag"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8315,""safe"":false},{""name"":""setImmutableFlagViaProposal"",""parameters"":[{""name"":""flagId"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8415,""safe"":false},{""name"":""buildSetImmutableFlagAction"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":8584,""safe"":true},{""name"":""matchesProposalPayload"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""},{""name"":""expectedAction"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":3041,""safe"":true},{""name"":""isImmutable"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8721,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":8740,""safe"":false}],""events"":[{""name"":""ProposalCreated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ProposalApproved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""}]},{""name"":""ImmutableFlagSet"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""UpgradeWindowsSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""ProposalExecuted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ProposalVetoed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""CouncilRotated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Integer""},{""name"":""arg5"",""type"":""Integer""}]},{""name"":""AdmissionModeChanged"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""VerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""VerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""BridgeAdapterApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""BridgeAdapterRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Governance controller for the Neo Elastic Network: council, timelocks, admission policy."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.GovernanceController"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AAAAAQAAAJ9R0EVAVwMBeDWgAAAANZz7//+YJggJI5EAAAB4NcoAAAAmCAkjgwAAAHg10QAAAHBoEJcmBQkicTWs+///cWkB6AOgShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFyQbfDiANoap5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkbgiAkBXAQF4ABY1If3//zXN+f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcAAXgAFDXj/P//NY/5//8LmCICQFcBAXgcNc/8//81e/n//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBBt8OIA0BXAAJ5eDRWJFMMTnByb3Bvc2FsIHBheWxvYWQgZG9lcyBub3QgbWF0Y2ggYWN0aW9uIGFyZ3MgKGNvdW5jaWwgdm90ZWQgb24gZGlmZmVyZW50IGJ5dGVzKeBAVwMCeDRrNeH4//9waAuXJgUJIlxo2zBxacp5ypgmBQkiThByIkFpas55as6YJgUJIj5qSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqacq1JL0IIgJAVwABeBY1wvv//yICQFcCAVjKEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHAQcSI+WGnOSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaVjKtSTAeEpoWMpR0EVoIgJAVwQEewAXNSv7//9waDXV9///C5ckHgwZcHJvcG9zYWwgYWxyZWFkeSBjb25zdW1lZOA1aPj//3F4ymmXJDQML29sZE1lbWJlcnMgbXVzdCBiZSB0aGUgY29tcGxldGUgY3VycmVudCBjb3VuY2ls4HjKELckIgwdb2xkIGNvdW5jaWwgbXVzdCBiZSBub24tZW1wdHngeMoAQLYkJQwgb2xkIGNvdW5jaWwgZXhjZWVkcyBtYXhpbXVtIHNpemXgeDVL9P//EHIiZ3hqzjW69///JCoMJW9sZE1lbWJlcnMgaXMgbm90IHRoZSBjdXJyZW50IGNvdW5jaWzgakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanjKtSSXecoQtyQiDB1uZXcgY291bmNpbCBtdXN0IGJlIG5vbi1lbXB0eeB5ygBAtiQlDCBuZXcgY291bmNpbCBleGNlZWRzIG1heGltdW0gc2l6ZeB6ELckHwwadGhyZXNob2xkIG11c3QgYmUgcG9zaXRpdmXgennKtiQnDCJ0aHJlc2hvbGQgZXhjZWVkcyBuZXcgY291bmNpbCBzaXpl4Hk1OfP//zUz9///cmoE//////////8AAAAAAAAAALUkHAwXY291bmNpbCBlcG9jaCBleGhhdXN0ZWTgezX8+///apckIwwecHJvcG9zYWwgY291bmNpbCBlcG9jaCBleHBpcmVk4Hs1Lfv//yQnDCJwcm9wb3NhbCBub3QgYXBwcm92ZWQgKyB0aW1lbG9ja2Vk4Hp5NTsBAAB7NTP8//8MAQHbMGg1YvT//xBzIkJ4a841bPT//zXABQAAa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa3jKtSS8EHMiRwwBAdsweWvONR30//81AvT//2tKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWt5yrUkt2oRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRc3nKDAEC2zA1d/P//3oMAQPbMDVs8///awwBFdswNWHz//96ecpKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRa2p7FcAMDkNvdW5jaWxSb3RhdGVkQZUBb2FAVwYCeMoQtyQiDB1uZXcgY291bmNpbCBtdXN0IGJlIG5vbi1lbXB0eeB4ygBAtiQlDCBuZXcgY291bmNpbCBleGNlZWRzIG1heGltdW0gc2l6ZeB5ELckHwwadGhyZXNob2xkIG11c3QgYmUgcG9zaXRpdmXgeXjKtiQnDCJ0aHJlc2hvbGQgZXhjZWVkcyBuZXcgY291bmNpbCBzaXpl4Hg1yfD//1nKGJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfeMoAIaBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IcBBxEHIib1lqzkpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqWcq1JI81VPP//3JqaWg1h/X//2kYnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUV5aWg1VgEAAGkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUV4ykoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFpaDX9AAAAaRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRRBzI7UAAAB4a87bMHQQdSJvbG3OSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AIbUkj2tKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWt4yrUlTP///2giAkBXAAN6ShAuBCIISgH/ADIGAf8AkUp4eVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6IKlKEC4EIghKAf8AMgYB/wCRSnh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAA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//9yahC3JnVqNQv///8B6AOgShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFzQbfDiANrtSYFEyIDFCJ1aTVh/v//AegDoEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRc0G3w4gDa7YmBRIiAxUiAkBXAAF4NVj+//8SlyICQFcBATW95///Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeDTRJBwMF3Byb3Bvc2FsIG5vdCBleGVjdXRhYmxl4HgAEjUE6///cGg1ruf//wuXJB4MGXByb3Bvc2FsIGFscmVhZHkgZXhlY3V0ZWTgQbfDiANoNWXm//9Bt8OIA3gSwAwQUHJvcG9zYWxFeGVjdXRlZEGVAW9hQFcAAXhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBUMEGludmFsaWQgdmVyaWZpZXLgNfjm//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOAMAQHbMHg0IjUC5v//eBHADBBWZXJpZmllckFwcHJvdmVkQZUBb2FAVwMBABWIcBpKaBBR0EV42zBxEHIibmlqzkpoEWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAFLUkkGgiAkDbMEBXAAF4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQVDBBpbnZhbGlkIHZlcmlmaWVy4DX15f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeDUk////NXD2//94EcAMD1ZlcmlmaWVyUmV2b2tlZEGVAW9hQFcAAXg1/P7//zXh5f//C5giAkBXAAF4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQTDA5pbnZhbGlkIGJyaWRnZeA1c+X//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4AwBAdsweDQnNX3k//94EcAMFUJyaWRnZUFkYXB0ZXJBcHByb3ZlZEGVAW9hQFcDAQAViHAbSmgQUdBFeNswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqABS1JJBoIgJAVwABeErZKCQGRQkiBsoAFLMkBQkiBngQs6okEwwOaW52YWxpZCBicmlkZ2XgNXDk//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NSn///816/T//3gRwAwUQnJpZGdlQWRhcHRlclJldm9rZWRBlQFvYUBXAAF4Nfz+//81V+T//wuYIgJAVwEBNRPk//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NDNwaDUo5P//C5cmJwwBAdswaDUR4///eBHADBBJbW11dGFibGVGbGFnU2V0QZUBb2FAVwABEohKEB3QShF40CICQFcCAnkeNTPn//9waDXd4///C5ckHgwZcHJvcG9zYWwgYWxyZWFkeSBjb25zdW1lZOB5NTvp//8kJwwicHJvcG9zYWwgbm90IGFwcHJvdmVkICsgdGltZWxvY2tlZOB4NEp5NUXq//8MAQHbMGg1dOL//3g1ff///3FpNW/j//8LlyYnDAEB2zBpNVji//94EcAMEEltbXV0YWJsZUZsYWdTZXRBlQFvYUBXAgFayhGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hwEHEiPlppzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWlayrUkwHhKaFrKUdBFaCICQFcAAXg1uv7//zWu4v//C5giAkBWAwwZbmVvNC1nb3Y6c2V0QWRtaXNzaW9uTW9kZdswYAwZbmVvNC1nb3Y6cm90YXRlQ291bmNpbDp2MdswYQwZbmVvNC1nb3Y6c2V0SW1tdXRhYmxlRmxhZ9swYkAUNj2X").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delAdmissionModeChanged(BigInteger? obj);

    [DisplayName("AdmissionModeChanged")]
    public event delAdmissionModeChanged? OnAdmissionModeChanged;

    public delegate void delBridgeAdapterApproved(UInt160? obj);

    [DisplayName("BridgeAdapterApproved")]
    public event delBridgeAdapterApproved? OnBridgeAdapterApproved;

    public delegate void delBridgeAdapterRevoked(UInt160? obj);

    [DisplayName("BridgeAdapterRevoked")]
    public event delBridgeAdapterRevoked? OnBridgeAdapterRevoked;

    public delegate void delCouncilRotated(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, BigInteger? arg4, BigInteger? arg5);

    [DisplayName("CouncilRotated")]
    public event delCouncilRotated? OnCouncilRotated;

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
    [DisplayName("markProposalExecuted")]
    public abstract void MarkProposalExecuted(BigInteger? proposalId);

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

    #endregion
}
