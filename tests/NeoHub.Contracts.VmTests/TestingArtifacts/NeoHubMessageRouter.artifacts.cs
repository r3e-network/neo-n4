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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MessageRouter"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":175,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":274,""safe"":false},{""name"":""enqueueL1ToL2"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""receiver"",""type"":""Hash160""},{""name"":""messageType"",""type"":""Integer""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":395,""safe"":false},{""name"":""getL1ToL2"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":3509,""safe"":true},{""name"":""setL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""filter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":3543,""safe"":false},{""name"":""clearL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3697,""safe"":false},{""name"":""getL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":759,""safe"":true},{""name"":""publishMessageRoots"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""l2ToL1Root"",""type"":""Hash256""},{""name"":""l2ToL2Root"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":3846,""safe"":false},{""name"":""getL2ToL1Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3992,""safe"":true},{""name"":""getL2ToL2Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4100,""safe"":true},{""name"":""publishGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":4173,""safe"":false},{""name"":""setGlobalRootVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":7191,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7725,""safe"":false},{""name"":""lockGlobalRootGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":7911,""safe"":false},{""name"":""setGlobalRootVerifierViaProposal"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8493,""safe"":false},{""name"":""buildSetGlobalRootVerifierAction"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""ByteArray"",""offset"":8904,""safe"":true},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":5436,""safe"":true},{""name"":""getGlobalRootVerifier"",""parameters"":[],""returntype"":""Hash160"",""offset"":7133,""safe"":true},{""name"":""getGlobalRootProofSystem"",""parameters"":[],""returntype"":""Integer"",""offset"":6963,""safe"":true},{""name"":""getGlobalRootAggregationBackend"",""parameters"":[],""returntype"":""Integer"",""offset"":6933,""safe"":true},{""name"":""getGlobalRootVerificationKeyId"",""parameters"":[],""returntype"":""Hash256"",""offset"":6993,""safe"":true},{""name"":""getGlobalRootReplayDomain"",""parameters"":[],""returntype"":""Hash256"",""offset"":7063,""safe"":true},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":8081,""safe"":true},{""name"":""isGlobalRootGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":6918,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9943,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":10014,""safe"":true},{""name"":""consumeL2ToL1"",""parameters"":[{""name"":""sourceChainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""messageHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":10085,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11282,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":11301,""safe"":false}],""events"":[{""name"":""L1ToL2Enqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""L2ToL1Consumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""L1TxFilterSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""MessageRootsPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GlobalRootVerifierSet"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash256""},{""name"":""arg5"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GlobalRootGovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Cross-chain message queue \u002B message-root registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MessageRouter"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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BCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXpKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeiCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ACCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ACipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ADCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ADipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV72zBzEHQibmtszkppamyeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkGoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFfNswdBB1Im5sbc5KaWptnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtABS1JJBqABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRX1KaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFfsp1bUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFbRipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVtIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRW0AGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRRB2Im5+bs5KaWpunkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVuSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdkVubbUkkWkiAkDbMEBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwACeXgSNANAVwEDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXpKEC4EIghKAf8AMgYB/wCRSmgVUdBFehipShAuBCIISgH/ADIGAf8AkUpoFlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV6ADCpShAuBCIISgH/ADIGAf8AkUpoG1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmgcUdBFaCICQFcBAnl4NbP+//81J/P//3BoC5cmBhCIIgVo2zAiAkDbMEBXAAI11fL//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgQtyQnDCJ0YXJnZXRDaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HlK2SgkBkUJIgbKABSzJAUJIgZ5ELOqJBMMDmludmFsaWQgZmlsdGVy4Hg14/T//3lQNUDy//95eBLADA1MMVR4RmlsdGVyU2V0QZUBb2FAVwABNTvy//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4ELckJwwidGFyZ2V0Q2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB4NXP0//80MAwUAAAAAAAAAAAAAAAAAAAAAAAAAAB4EsAMDUwxVHhGaWx0ZXJTZXRBlQFvYUBXAAF4QZv2Z85BL1jF7UBBL1jF7UBXAQQMAf3bMDXY8f//StgmDkUMCHNtIHVuc2V0OkrYJAlKygAUKAM6cGhB+CfsjCQbDBZub3Qgc2V0dGxlbWVudCBtYW5hZ2Vy4Hl4EzUj/f//etswUDX7/P//eXgUNRL9//972zBQNer8//97enl4FMAMFU1lc3NhZ2VSb290c1B1Ymxpc2hlZEGVAW9hQNswQFcBAnl4EzXY/P//NUPx//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcBAnl4FDVs/P//Ndfw//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXBgkMAf3bMDWR8P//StgmDkUMCHNtIHVuc2V0OkrYJAlKygAUKAM6cGhB+CfsjCQbDBZub3Qgc2V0dGxlbWVudCBtYW5hZ2Vy4HkMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okIQwcZ2xvYmFsIHJvb3QgbXVzdCBiZSBub24temVyb+B6DCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJDIMLWNvbnN0aXR1ZW50IGNvbW1pdG1lbnRzIHJvb3QgbXVzdCBiZSBub24temVyb+B7ELckJwwiY29uc3RpdHVlbnQgY291bnQgbXVzdCBiZSBwb3NpdGl2ZeB8NcADAAAkQQw8cGFzcy10aHJvdWdoL3Jlc2VydmVkIGFnZ3JlZ2F0aW9uIGJhY2tlbmQgaXMgbm90IHB1Ymxpc2hhYmxl4H0RuCQFCSIFfRS2JB0MGHByb29mU3lzdGVtIG11c3QgYmUgMS4uNOB+DCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJCkMJHZlcmlmaWNhdGlvbiBrZXkgaWQgbXVzdCBiZSBub24temVyb+B/BwwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXqiQjDB5yZXBsYXkgZG9tYWluIG11c3QgYmUgbm9uLXplcm/gfwd+fXx7enl4Nc8CAABxeDWhBwAAcmo1a+7//3NrC5gmeng1aggAADVa7v//dGtK2CQJSsoAICgDOnmXJAUJIgVsC5gkBQkiEGxK2CQJSsoAICgDOmmXJD0MOGVwb2NoIGFscmVhZHkgYm91bmQgdG8gYSBkaWZmZXJlbnQgZ2xvYmFsIHJvb3Qgc3RhdGVtZW504AkjJQIAADUICAAAJCYMIWdsb2JhbCByb290IGdvdmVybmFuY2Ugbm90IGxvY2tlZOB8NesHAACXJC0MKGdsb2JhbCByb290IGFnZ3JlZ2F0aW9uIGJhY2tlbmQgbWlzbWF0Y2jgfTXVBwAAlyQmDCFnbG9iYWwgcm9vdCBwcm9vZiBzeXN0ZW0gbWlzbWF0Y2jgfjXGBwAAlyQqDCVnbG9iYWwgcm9vdCB2ZXJpZmljYXRpb24ga2V5IG1pc21hdGNo4H8HNdoHAACXJCcMImdsb2JhbCByb290IHJlcGxheSBkb21haW4gbWlzbWF0Y2jgfwjKELckHgwZYWdncmVnYXRlZCBwcm9vZiByZXF1aXJlZOB/CMoCAAAQALYkHwwaYWdncmVnYXRlZCBwcm9vZiB0b28gbGFyZ2XgNagHAAB0bErZKCQGRQkiBsoAFLMkBQkiBmwQs6okKAwjZ2xvYmFsIHJvb3QgdmVyaWZpZXIgbm90IGNvbmZpZ3VyZWTgfwhp2zB+2zB9FMAVDA12ZXJpZnlaa1Byb29mbEFifVtSdW0kJQwgZ2F0ZXdheSBhZ2dyZWdhdGUgcHJvb2YgcmVqZWN0ZWTgedswajWR9///eDUlBgAAadswUDWC9///eXgSwAwTR2xvYmFsUm9vdFB1Ymxpc2hlZEGVAW9haXp4E8AMF0dsb2JhbFJvb3RQcm9vZkFjY2VwdGVkQZUBb2EIIgJAVwABeBCYJAUJIgd4Af4AmCQFCSIHeAH/AJgiAkBXBAgBqgCIcFhxEHIiPmlqzkpoalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWppyrUkwEHb/qh02zByEHMibmprzkpoGGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAFLUkkH8HABxoNF14ADxoNdUAAAB5AERoNE56AGRoNEh7AYQAaDXeAgAAfEpoAYgAUdBFfUpoAYkAUdBFfgGKAGg0JWjbKDcAAHNrNwAA2zDbKErYJAlKygAgKAM6IgJAQdv+qHRAVwIDetswcBBxIm5oac5KeHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBAVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8AMgYB/wCRSnh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeiCpShAuBCIISgH/ADIGAf8AkUp4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSnh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKeHkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ACipShAu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EWg1K/T//2giAkBXCAVZcGjKABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnwAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8RnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8RnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8AIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxEHIQcyJvaGvOSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWtoyrUkj0Hb/qh02zBzEHQib2tszkppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsABS1JI942zB0EHUib2xtzkppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtABS1JI95SmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXpKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFe9swdRB2Im9tbs5KaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFbkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3ZFbgAgtSSPfNswdhB3ByJybm8HzkppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JItpIgJAVwEBeDUW9P//NQba//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXAQF4NfPy//81v9n//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAICgDOiICQFcEBXgQtyQnDCJzb3VyY2VDaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okIgwdbWVzc2FnZSBoYXNoIG11c3QgYmUgbm9uLXplcm/gewuYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HtwaMoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgDAH92zA11dj//0rYJg5FDAhzbSB1bnNldDpK2CQJSsoAFCgDOnF5eBLAFQwUZ2V0TDJUb0wxTWVzc2FnZVJvb3RpQWJ9W1JyagwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXqiQ3DDJiYXRjaCBpcyBub3QgZmluYWxpemVkIG9yIGhhcyBubyBMMi10by1MMSBtZXNzYWdlc+B8aGp6NG4kIwweaW52YWxpZCBMMi10by1MMSBtZXNzYWdlIHByb29m4Ho1rAIAAHNrNQXY//8LlyQVDBBhbHJlYWR5IGNvbnN1bWVk4AwBAdswazVU4///engSwAwOTDJUb0wxQ29uc3VtZWRBlQFvYUBXBgR42zBwe3EQciM1AgAAemrOc2sLmCQFCSIHa8oAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHRpEZEQlyfHAAAAEHUiPmhtzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkwBB1Im9rbc5KbAAgbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSSPI8IAAAAQdSI+a23OSmxtUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSTAEHUib2htzkpsACBtnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JI9s2yg3AAB1bTcAANswSnBFaRGpShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFKcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqesq1Jcz9//9pEJckBQkiEnlo2yhK2CQJSsoAICgDOpciAkBXAwEAIYhwFkpoEFHQRXjbMHEQciJuaWrOSmgRap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQaCICQFcAAXg1cP///zXL1P//C5giAkBWAgwITkVPNEdXUjLbMGAMHm5lbzQtZ292OnNldEdsb2JhbFJvb3RWZXJpZmllctswYUA8+jyR").AsSerializable<Neo.SmartContract.NefFile>();

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
