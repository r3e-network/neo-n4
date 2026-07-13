using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubMessageRouter(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MessageRouter"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":175,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":274,""safe"":false},{""name"":""enqueueL1ToL2"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""receiver"",""type"":""Hash160""},{""name"":""messageType"",""type"":""Integer""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":395,""safe"":false},{""name"":""getL1ToL2"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":3508,""safe"":true},{""name"":""setL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""filter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":3542,""safe"":false},{""name"":""clearL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3695,""safe"":false},{""name"":""getL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":758,""safe"":true},{""name"":""publishMessageRoots"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""l2ToL1Root"",""type"":""Hash256""},{""name"":""l2ToL2Root"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":3844,""safe"":false},{""name"":""getL2ToL1Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3988,""safe"":true},{""name"":""getL2ToL2Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4096,""safe"":true},{""name"":""publishGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":4169,""safe"":false},{""name"":""setGlobalRootVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":7186,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7720,""safe"":false},{""name"":""lockGlobalRootGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":7906,""safe"":false},{""name"":""setGlobalRootVerifierViaProposal"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8488,""safe"":false},{""name"":""buildSetGlobalRootVerifierAction"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""ByteArray"",""offset"":8899,""safe"":true},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":5431,""safe"":true},{""name"":""getGlobalRootVerifier"",""parameters"":[],""returntype"":""Hash160"",""offset"":7128,""safe"":true},{""name"":""getGlobalRootProofSystem"",""parameters"":[],""returntype"":""Integer"",""offset"":6958,""safe"":true},{""name"":""getGlobalRootAggregationBackend"",""parameters"":[],""returntype"":""Integer"",""offset"":6928,""safe"":true},{""name"":""getGlobalRootVerificationKeyId"",""parameters"":[],""returntype"":""Hash256"",""offset"":6988,""safe"":true},{""name"":""getGlobalRootReplayDomain"",""parameters"":[],""returntype"":""Hash256"",""offset"":7058,""safe"":true},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":8076,""safe"":true},{""name"":""isGlobalRootGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":6913,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9938,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":10009,""safe"":true},{""name"":""consumeL2ToL1"",""parameters"":[{""name"":""sourceChainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""messageHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":10080,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11277,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":11296,""safe"":false}],""events"":[{""name"":""L1ToL2Enqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""L2ToL1Consumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""L1TxFilterSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""MessageRootsPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GlobalRootVerifierSet"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash256""},{""name"":""arg5"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GlobalRootGovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Cross-chain message queue \u002B message-root registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MessageRouter"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscgZzaGEyNTYBAAEPAAD9UyxXAwJ5JgQie3hwaBDOcWgRznJpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQSDA1pbnZhbGlkIG93bmVy4GpK2SgkBkUJIgbKABSzJAUJIgZqELOqJB8MGmludmFsaWQgc2V0dGxlbWVudCBtYW5hZ2Vy4GkMAf/bMDQcagwB/dswNBRAStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwEADAH/2zA0L3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAAXhBm/ZnzkGSXegxQEGSXegxQAwUAAAAAAAAAAAAAAAAAAAAAAAAAABAVwEBNJpB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQWDBFpbnZhbGlkIG5ldyBvd25lcuA1U////3B4DAH/2zA1K////3hoEsAMDE93bmVyQ2hhbmdlZEGVAW9hQEH4J+yMQFcFBHlK2SgkBkUJIgbKABSzJAUJIgZ5ELOqJBUMEGludmFsaWQgcmVjZWl2ZXLgeBC3JCcMInRhcmdldENoYWluSWQgMCBpcyByZXNlcnZlZCBmb3IgTDHgQTlTbjxwe3p5aHg1sgAAAHg1swEAAHFpNer+//9yaguXJgURIlJqStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6EZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXNraTXKAQAAe3p5aGt4EDXUAQAAdGxreDX6CQAANd8JAAB5aGt4FMAMDkwxVG9MMkVucXVldWVkQZUBb2FrIgJAQTlTbjxAVwIFeDRRcGgQsyYEIkh8e3p5eBXAFQwMYWNjZXB0TDFUb0wyaEFifVtScWkkKAwjbDEgdG8gbDIgbWVzc2FnZSByZWplY3RlZCBieSBmaWx0ZXLgQFcBAXg0NDXq/f//cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwEBFYhwF0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBBYn1bUkBXAQEViHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQErYJgZFECIE2yFAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcHBwA9fsqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BoiHEQcnhKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeCCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV4ABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV5ShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFekoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFehipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAKKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXvbMHMQdCJua2zOSmlqbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQagAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV82zB0EHUibmxtzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/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bi16ZXJv4HsQtyQnDCJjb25zdGl0dWVudCBjb3VudCBtdXN0IGJlIHBvc2l0aXZl4Hw1vwMAACRBDDxwYXNzLXRocm91Z2gvcmVzZXJ2ZWQgYWdncmVnYXRpb24gYmFja2VuZCBpcyBub3QgcHVibGlzaGFibGXgfRG4JAUJIgV9FLYkHQwYcHJvb2ZTeXN0ZW0gbXVzdCBiZSAxLi404H4MIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okKQwkdmVyaWZpY2F0aW9uIGtleSBpZCBtdXN0IGJlIG5vbi16ZXJv4H8HDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJCMMHnJlcGxheSBkb21haW4gbXVzdCBiZSBub24temVyb+B/B359fHt6eXg1zgIAAHF4NaAHAAByajVv7v//c2sLmCZ6eDVpCAAANV7u//90a0rYJAlKygAgKAM6eZckBQkiBWwLmCQFCSIQbErYJAlKygAgKAM6aZckPQw4ZXBvY2ggYWxyZWFkeSBib3VuZCB0byBhIGRpZmZlcmVudCBnbG9iYWwgcm9vdCBzdGF0ZW1lbnTgCSMkAgAANQcIAAAkJgwhZ2xvYmFsIHJvb3QgZ292ZXJuYW5jZSBub3QgbG9ja2Vk4Hw16gcAAJckLQwoZ2xvYmFsIHJvb3QgYWdncmVnYXRpb24gYmFja2VuZCBtaXNtYXRjaOB9NdQHAACXJCYMIWdsb2JhbCByb290IHByb29mIHN5c3RlbSBtaXNtYXRjaOB+NcUHAACXJCoMJWdsb2JhbCByb290IHZlcmlmaWNhdGlvbiBrZXkgbWlzbWF0Y2jgfwc12QcAAJckJwwiZ2xvYmFsIHJvb3QgcmVwbGF5IGRvbWFpbiBtaXNtYXRjaOB/CMoQtyQeDBlhZ2dyZWdhdGVkIHByb29mIHJlcXVpcmVk4H8IygIAABAAtiQfDBphZ2dyZWdhdGVkIHByb29mIHRvbyBsYXJnZeA1pwcAAHRsStkoJAZFCSIGygAUsyQFCSIGbBCzqiQoDCNnbG9iYWwgcm9vdCB2ZXJpZmllciBub3QgY29uZmlndXJlZOB/CGnbMH7bMH0UwBUMDXZlcmlmeVprUHJvb2ZsQWJ9W1J1bSQlDCBnYXRld2F5IGFnZ3JlZ2F0ZSBwcm9vZiByZWplY3RlZOB52zBqNZT3//9p2zB4NSEGAAA1hvf//3l4EsAME0dsb2JhbFJvb3RQdWJsaXNoZWRBlQFvYWl6eBPADBdHbG9iYWxSb290UHJvb2ZBY2NlcHRlZEGVAW9hCCICQFcAAXgQmCQFCSIHeAH+AJgkBQkiB3gB/wCYIgJAVwQIAaoAiHBYcRByIj5pas5KaGpR0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqacq1JMBB2/6odNswchBzIm5qa85KaBhrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrABS1JJB/BwAcaDRdeAA8aDXVAAAAeQBEaDROegBkaDRIewGEAGg13gIAAHxKaAGIAFHQRX1KaAGJAFHQRX4BigBoNCVo2yg3AABzazcAANsw2yhK2CQJSsoAICgDOiICQEHb/qh0QFcCA3rbMHAQcSJuaGnOSnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQQFcAA3pKEC4EIghKAf8AMgYB/wCRSnh5UdBFehipShAuBCIISgH/ADIGAf8AkUp4eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXogqUoQLgQiCEoB/wAyBgH/AJFKeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ABipShAuBCIISgH/ADIGAf8AkUp4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAIKlKEC4EIghKAf8AMgYB/wCRSnh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAoqUoQLgQiCEoB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//9oIgJAVwgFWXBoygAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8AFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnwAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IcRByEHMib2hrzkppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VraMq1JI9B2/6odNswcxB0Im9rbM5KaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSPeNswdBB1Im9sbc5KaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAUtSSPeUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6SmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXvbMHUQdiJvbW7OSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRW5KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ92RW4AILUkj3zbMHYQdwcicm5vB85KaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSLaSICQFcBAXg1FvT//zUL2v//cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwEBeDXz8v//NcTZ//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXBAV4ELckJwwic291cmNlQ2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB6DCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJCIMHW1lc3NhZ2UgaGFzaCBtdXN0IGJlIG5vbi16ZXJv4HsLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7cGjKAEC2JBMMDnByb29mIHRvbyBkZWVw4AwB/dswNdrY//9K2CYORQwIc20gdW5zZXQ6StgkCUrKABQoAzpxeXgSwBUMFGdldEwyVG9MMU1lc3NhZ2VSb290aUFifVtScmoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okNwwyYmF0Y2ggaXMgbm90IGZpbmFsaXplZCBvciBoYXMgbm8gTDItdG8tTDEgbWVzc2FnZXPgfGhqejRuJCMMHmludmFsaWQgTDItdG8tTDEgbWVzc2FnZSBwcm9vZuB6NawCAABzazUK2P//C5ckFQwQYWxyZWFkeSBjb25zdW1lZOAMAQHbMGs1WOP//3p4EsAMDkwyVG9MMUNvbnN1bWVkQZUBb2FAVwYEeNswcHtxEHIjNQIAAHpqznNrC5gkBQkiB2vKACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh0aRGREJcnxwAAABB1Ij5obc5KbG1R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JMAQdSJva23OSmwAIG2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkjyPCAAAAEHUiPmttzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkwBB1Im9obc5KbAAgbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSSPbNsoNwAAdW03AADbMEpwRWkRqUoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanrKtSXM/f//aRCXJAUJIhJ5aNsoStgkCUrKACAoAzqXIgJAVwMBACGIcBZKaBBR0EV42zBxEHIibmlqzkpoEWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkGgiAkBXAAF4NXD///810NT//wuYIgJAVgIMCE5FTzRHV1Iy2zBgDB5uZW80LWdvdjpzZXRHbG9iYWxSb290VmVyaWZpZXLbMGFAU/wMig==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delGlobalRootGovernanceLocked();

    [DisplayName("GlobalRootGovernanceLocked")]
    public event delGlobalRootGovernanceLocked? OnGlobalRootGovernanceLocked;

    public delegate void delGlobalRootProofAccepted(BigInteger? arg1, UInt256? arg2, UInt256? arg3);

    [DisplayName("GlobalRootProofAccepted")]
    public event delGlobalRootProofAccepted? OnGlobalRootProofAccepted;

    public delegate void delGlobalRootPublished(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GlobalRootPublished")]
    public event delGlobalRootPublished? OnGlobalRootPublished;

    public delegate void delGlobalRootVerifierSet(UInt160? arg1, BigInteger? arg2, BigInteger? arg3, UInt256? arg4, UInt256? arg5);

    [DisplayName("GlobalRootVerifierSet")]
    public event delGlobalRootVerifierSet? OnGlobalRootVerifierSet;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delL1ToL2Enqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4);

    [DisplayName("L1ToL2Enqueued")]
    public event delL1ToL2Enqueued? OnL1ToL2Enqueued;

    public delegate void delL1TxFilterSet(BigInteger? arg1, UInt160? arg2);

    [DisplayName("L1TxFilterSet")]
    public event delL1TxFilterSet? OnL1TxFilterSet;

    public delegate void delL2ToL1Consumed(BigInteger? arg1, UInt256? arg2);

    [DisplayName("L2ToL1Consumed")]
    public event delL2ToL1Consumed? OnL2ToL1Consumed;

    public delegate void delMessageRootsPublished(BigInteger? arg1, BigInteger? arg2, UInt256? arg3, UInt256? arg4);

    [DisplayName("MessageRootsPublished")]
    public event delMessageRootsPublished? OnMessageRootsPublished;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? GlobalRootAggregationBackend { [DisplayName("getGlobalRootAggregationBackend")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? GlobalRootProofSystem { [DisplayName("getGlobalRootProofSystem")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt256? GlobalRootReplayDomain { [DisplayName("getGlobalRootReplayDomain")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt256? GlobalRootVerificationKeyId { [DisplayName("getGlobalRootVerificationKeyId")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GlobalRootVerifier { [DisplayName("getGlobalRootVerifier")] get; }

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
    public abstract bool? IsGlobalRootGovernanceLocked { [DisplayName("isGlobalRootGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildGlobalRootProofInputHash")]
    public abstract UInt256? BuildGlobalRootProofInputHash(BigInteger? batchEpoch, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetGlobalRootVerifierAction")]
    public abstract byte[]? BuildSetGlobalRootVerifierAction(UInt160? verifier, BigInteger? proofSystem, BigInteger? aggregationBackendId, UInt256? verificationKeyId, UInt256? replayDomain);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGlobalRoot")]
    public abstract UInt256? GetGlobalRoot(BigInteger? batchEpoch);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGlobalRootProofInputHash")]
    public abstract UInt256? GetGlobalRootProofInputHash(BigInteger? batchEpoch);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1ToL2")]
    public abstract byte[]? GetL1ToL2(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1TxFilter")]
    public abstract UInt160? GetL1TxFilter(BigInteger? targetChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL1Root")]
    public abstract UInt256? GetL2ToL1Root(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL2Root")]
    public abstract UInt256? GetL2ToL2Root(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isConsumed")]
    public abstract bool? IsConsumed(UInt256? messageHash);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("clearL1TxFilter")]
    public abstract void ClearL1TxFilter(BigInteger? targetChainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("consumeL2ToL1")]
    public abstract void ConsumeL2ToL1(BigInteger? sourceChainId, BigInteger? batchNumber, UInt256? messageHash, IList<object>? siblings, BigInteger? leafIndex);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("enqueueL1ToL2")]
    public abstract BigInteger? EnqueueL1ToL2(BigInteger? targetChainId, UInt160? receiver, BigInteger? messageType, byte[]? payload);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGlobalRootGovernance")]
    public abstract void LockGlobalRootGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishGlobalRoot")]
    public abstract bool? PublishGlobalRoot(BigInteger? batchEpoch, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain, byte[]? aggregatedProof);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishMessageRoots")]
    public abstract void PublishMessageRoots(BigInteger? chainId, BigInteger? batchNumber, UInt256? l2ToL1Root, UInt256? l2ToL2Root);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setGlobalRootVerifier")]
    public abstract void SetGlobalRootVerifier(UInt160? verifier, BigInteger? proofSystem, BigInteger? aggregationBackendId, UInt256? verificationKeyId, UInt256? replayDomain);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setGlobalRootVerifierViaProposal")]
    public abstract void SetGlobalRootVerifierViaProposal(UInt160? verifier, BigInteger? proofSystem, BigInteger? aggregationBackendId, UInt256? verificationKeyId, UInt256? replayDomain, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setL1TxFilter")]
    public abstract void SetL1TxFilter(BigInteger? targetChainId, UInt160? filter);

    #endregion
}
