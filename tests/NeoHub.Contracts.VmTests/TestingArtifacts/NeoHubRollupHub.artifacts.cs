using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubRollupHub(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.RollupHub"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":174,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1278,""safe"":false},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1434,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1563,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1689,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1719,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1773,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":1827,""safe"":true},{""name"":""recordBatchDA"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""daCommitment"",""type"":""Hash256""},{""name"":""firstBlock"",""type"":""Integer""},{""name"":""lastBlock"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1898,""safe"":false},{""name"":""getBatchDACommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2399,""safe"":true},{""name"":""isBatchDAAvailable"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2471,""safe"":true},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""transactionBytes"",""type"":""ByteArray""},{""name"":""transactionHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2491,""safe"":false},{""name"":""getNextForcedNonce"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3324,""safe"":true},{""name"":""getPendingForcedCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3331,""safe"":true},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3403,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6188,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5038,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6293,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6569,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":7263,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":7159,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":7557,""safe"":false},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7174,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12283,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12351,""safe"":true},{""name"":""getSharedBridge"",""parameters"":[],""returntype"":""Hash160"",""offset"":7499,""safe"":true},{""name"":""setSharedBridge"",""parameters"":[{""name"":""sharedBridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12419,""safe"":false},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":11014,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4686,""safe"":true},{""name"":""getLatestFinalizedBatchNumber"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4476,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":10124,""safe"":true},{""name"":""getBatchCommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":12550,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":634,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12581,""safe"":false},{""name"":""getVerifierRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":5765,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""controller"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12694,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":2865,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":12968,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":12986,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":13044,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":13733,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainStatusChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""GenesisRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionsConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Consolidated L1 Rollup Hub for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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SgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhhpoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+oShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFKcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpGLUlCP///2giAkBXDQd4ygFBAbgkIAwbY29tbWl0bWVudCBoZWFkZXIgdG9vIHNob3J04HkLmCQFCSIHecoAIJckIwwebDFNZXNzYWdlSGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HoLmCQFCSIHesoAIJckJgwhYmxvY2tDb250ZXh0SGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HlwenF7NY/3//8kEwwOY2hhaW4gaW5hY3RpdmXgezWU+///fRKYJDgMM29wdGltaXN0aWMgYmF0Y2hlcyByZXF1aXJlIGEgd2lyZWQgY2hhbGxlbmdlIHdpbmRvd+B7NfoBAAByfGoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyQaDBViYXRjaCBvdXQgb2Ygc2VxdWVuY2XgACAAHHg19wEAANsoStgkCUrKACAoAzpzezViAgAAdGtslyQcDBdwcmUtc3RhdGUgcm9vdCBtaXNtYXRjaOB7NT70//81mvL//9swdW0AVM52bQBVzncHbwduNYoCAAB9bjV6AwAAJEMMPnByb29mIHR5cGUgaW5jb21wYXRpYmxlIHdpdGggY2hhaW4ncyBhZHZlcnRpc2VkIHNlY3VyaXR5IGxldmVs4AAgAfwAeDVNAQAA2yhK2CQJSsoAICgDOncIbwh8ezX29v//fmloeDVYAwAAdwkAIAEcAXg1HwEAANsoStgkCUrKACAoAzp3Cm8JbwqXJB8MGnB1YmxpYyBpbnB1dCBoYXNoIG1pc21hdGNo4DWdBQAAdwtvCwwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCUMIHZlcmlmaWVyIHJlZ2lzdHJ5IG5vdCBjb25maWd1cmVk4HgRwB8MC3ZlcmlmeVByb29mbwtBYn1bUncMbwwkHgwZcHJvb2YgdmVyaWZpY2F0aW9uIGZhaWxlZOB4fHs1VQUAADV58///fns1bvr//0BXAQF4NDU1L/H//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAF4ACE1nfL//0DbKErYJAlKygAgKAM6QFcCA3qIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVperUkkWgiAkBXAgF4NFk1XfD//3BoC5gmEGhK2CQJSsoAICgDOiI/eDVu8v//NT7w//9xaQuYJCAMG2dlbmVzaXMgcm9vdCBub3QgcmVnaXN0ZXJlZOBpStgkCUrKACAoAzoiAkBXAAF4ACA1p/H//0BXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCYJDIMLVZhbGlkaXVtIHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIG9mZi1jaGFpbiBEQeBAVwACeBCXJgUIIgV4EZcmF3kRlyYFCCIFeRKXJgUIIgV5E5ciKXgSlyYPeRKXJgUIIgV5E5ciF3gTlyYFCCIFeBSXJgd5E5ciBQkiAkBXAgQBYAGIcBBxIj54ac5KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpAfwAtSS/EHEicHlpzkpoAfwAaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSOEHEjpQAAAHgB/ABpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmgBHAFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JVz///8QcSJwemnOSmgBPAFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JI57Af8AkUoQLgQiCEoB/wAyBgH/AJFKaAFcAVHQRXsYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXQFR0EV7IKkB/wCRShAuBCIISgH/ADIGAf8AkUpoAV4BUdBFewAYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXwFR0EVo2yg3AABxaTcAAErYJAlKygAgKAM6IgJANwAAQNsoQFcBAAwBAtswNSTs//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAJ5eAAjNVbx//9AVwEDDAED2zB5eDRlNRDu//95eDXW+v//Ner0//96eDXA+///NaLp//95eDTINbjr//9waAuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOBo2zB5eDQqenl4E8AMDkJhdGNoRmluYWxpemVkQZUBb2FAVwACeXgAIjXa8P//QFcEA3rbKDcAAHBoNwAA2zBxAECIchBzI60AAABpa85KamtR0EV6AdwAa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpqACBrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrACC1JVT///9qeXg0CDXO7P//QFcAAnl4GTX27///QFcEBBB4Nazq//9wFHg1UfX//3F4ATwBznJ7amloenl4NUv2//9paDXo/v//cwwBAdswazWJ7P//ACAAPHg1Zvn//9soStgkCUrKACAoAzppaBPADA5CYXRjaFN1Ym1pdHRlZEGVAW9hQFcFAjXi6f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeDUB7v//JBMMDmNoYWluIGluYWN0aXZl4Hg1BvL//3g1p/j//3B5aBGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGXJB8MGm11c3QgZmluYWxpemUgc2VxdWVudGlhbGx54Hl4NQ7+//9xaTV96f//cmoLmCQFCSIHahDOEZckFgwRYmF0Y2ggbm90IHBlbmRpbmfgeXg1YP3//zVN6f//c2sLmCQXDBJtaXNzaW5nIGNvbW1pdG1lbnTgACAAPGvbMDVE+P//2yhK2CQJSsoAICgDOnRseXg1K/3//0DOQFcFAjVLAgAAJiE1fvH//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4CIfNajo//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB5eDVK/f//cGg1uej//3FpC5gkFAwPYmF0Y2ggbm90IGZvdW5k4Hg1Zvf//3J5apcnDAEAAGkQzhOXJBgME2JhdGNoIG5vdCBmaW5hbGl6ZWTgeXg1wwEAALckJwwiYmF0Y2ggYWxyZWFkeSBwdWJsaXNoZWQgYnkgZ2F0ZXdheeB5eDVN/P//NTro//9zawuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOAAIAAca9swNTH3///bKErYJAlKygAgKAM6dAwBBNswaDUx6v//eXg1Afz//zWHAQAAeXg1Vv3//zV7AQAAeXg1PO3//zVvAQAAbHg1yff//zWr5f//eRGfShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJF4NZn2//81rfD//yO4AAAAeWoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyZCaRDOEZckFgwRYmF0Y2ggbm90IHBlbmRpbmfgDAEE2zBoNWnp//95eDU5+///Nb8AAAB5eDWA7P//NbMAAAAiQgkkPww6b25seSB0aGUgcGVuZGluZyBv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IgJAVwQCEIhwEHEQciMEAQAAeGrOc2vKEJcmByPCAAAAa8oAIJckIAwbR2F0ZXdheSBmcm9udGllciBpcyBjb3JydXB04GjKEJcmD2tKcEVqSnFFI4oAAAB5JkZparUmQWhoNdj+//9KcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUUivmhrNZn+//9KcEVqEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanjKtSX9/v//aMoAIJckHgwZR2F0ZXdheSBmcm9udGllciBpcyBlbXB0eeBoIgJAVwQIAaoAiHBYcRByIj5pas5KaGpR0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqacq1JMBB2/6odNswchBzIm5qa85KaBhrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrABS1JJB/BwAcaDRgeAA8aDXbAAAAeQBEaDRRegBkaDRLewGEAGg15AIAAHxKaAGIAFHQRX1KaAGJAFHQRX4BigBoNCho2yg3AABzazcAANsw2yhK2CQJSsoAICgDOiICQNswQEHb/qh0QFcCA3rbMHAQcSJuaGnOSnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQQNswQFcAA3pKEC4EIghKAf8AMgYB/wCRSnh5UdBFehipShAuBCIISgH/ADIGAf8AkUp4eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXogqUoQLgQiCEoB/wAyBgH/AJFKeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ABipShAuBCIISgH/ADIGAf8AkUp4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAIKlKEC4EIghKAf8AMgYB/wCRSnh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKeHkVnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ADCpShAuBCIISgH/ADIGAf8AkUp4eRaeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSnh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFQFcAA3pKEC4EIghKAf8AMgYB/wCRSnh5UdBFehipShAuBCIISgH/ADIGAf8AkUp4eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXogqUoQLgQiCEoB/wAyBgH/AJFKeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ABipShAuBCIISgH/ADIGAf8AkUp4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRUBXAQEZiHAcSmgQUdBFeBFoNdr8//9oIgJAVwEBeDTjcB1KaBBR0EVoIgJAVwEBeDTRNbDS//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXAQF4NKY1bNL//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAICgDOiICQFcAATVx6///JiE1pNr//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4CIfNc7R//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQaDBVpbnZhbGlkIHNoYXJlZCBicmlkZ2XgeAwBC9swNZLP//9AVwECeXg1tOX//zWh0f//cGgLlyYGEIgiBWjbMCICQFcAATVS0f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNbLq//+qJBYMEWdvdmVybmFuY2UgbG9ja2Vk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBIMDWludmFsaWQgb3duZXLgeAwBAdswNQLP//9AVwABNV7q//8mITWR2f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgIh81u9D//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJCIMHWludmFsaWQgZ292ZXJuYW5jZSBjb250cm9sbGVy4HgMAUPbMDV3zv//QFcCAnl4NWb1//8TmCYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACJWeXg1auT//zVX0P//cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIgaNswcQAgAZwAaTU83///2yhK2CQJSsoAICgDOiICQFcBAng10N7//3B5aHg0BSICQFcBA3l4NWL///9waAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXJgUJIgdoepciAkBXCAV5eDUo////cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN+AgAAewuYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HtxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgetswcnxzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2sQmCYFCSIUaGrbKErYJAlKygAgKAM6lyICQFYBDAhORU80R1dSMtswYEB4NFw7").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delBatchFinalized(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchFinalized")]
    public event delBatchFinalized? OnBatchFinalized;

    public delegate void delBatchReverted(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("BatchReverted")]
    public event delBatchReverted? OnBatchReverted;

    public delegate void delBatchSubmitted(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchSubmitted")]
    public event delBatchSubmitted? OnBatchSubmitted;

    public delegate void delChainRegistered(BigInteger? arg1, byte[]? arg2);

    [DisplayName("ChainRegistered")]
    public event delChainRegistered? OnChainRegistered;

    public delegate void delChainStatusChanged(BigInteger? arg1, bool? arg2);

    [DisplayName("ChainStatusChanged")]
    public event delChainStatusChanged? OnChainStatusChanged;

    public delegate void delForcedTransactionEnqueued(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("ForcedTransactionEnqueued")]
    public event delForcedTransactionEnqueued? OnForcedTransactionEnqueued;

    public delegate void delForcedTransactionsConsumed(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("ForcedTransactionsConsumed")]
    public event delForcedTransactionsConsumed? OnForcedTransactionsConsumed;

    public delegate void delGenesisRootRegistered(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GenesisRootRegistered")]
    public event delGenesisRootRegistered? OnGenesisRootRegistered;

    public delegate void delGlobalRootProofAccepted(BigInteger? arg1, UInt256? arg2, UInt256? arg3);

    [DisplayName("GlobalRootProofAccepted")]
    public event delGlobalRootProofAccepted? OnGlobalRootProofAccepted;

    public delegate void delGlobalRootPublished(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GlobalRootPublished")]
    public event delGlobalRootPublished? OnGlobalRootPublished;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

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
    public abstract UInt160? SharedBridge { [DisplayName("getSharedBridge")] get; [DisplayName("setSharedBridge")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? VerifierRegistry { [DisplayName("getVerifierRegistry")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

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
    [DisplayName("getBatchCommitment")]
    public abstract byte[]? GetBatchCommitment(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchDACommitment")]
    public abstract UInt256? GetBatchDACommitment(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchStatus")]
    public abstract BigInteger? GetBatchStatus(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getCanonicalStateRoot")]
    public abstract UInt256? GetCanonicalStateRoot(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getChainConfig")]
    public abstract byte[]? GetChainConfig(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGatewayFinalizedThrough")]
    public abstract BigInteger? GetGatewayFinalizedThrough(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGenesisStateRoot")]
    public abstract UInt256? GetGenesisStateRoot(BigInteger? chainId);

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
    [DisplayName("getLatestFinalizedBatchNumber")]
    public abstract BigInteger? GetLatestFinalizedBatchNumber(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getNextForcedNonce")]
    public abstract BigInteger? GetNextForcedNonce(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPendingForcedCount")]
    public abstract BigInteger? GetPendingForcedCount(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSecurityLevel")]
    public abstract BigInteger? GetSecurityLevel(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isActive")]
    public abstract bool? IsActive(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isBatchDAAvailable")]
    public abstract bool? IsBatchDAAvailable(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isProofTypeCompatible")]
    public abstract bool? IsProofTypeCompatible(BigInteger? securityLevel, BigInteger? proofType);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeaf")]
    public abstract bool? VerifyWithdrawalLeaf(BigInteger? chainId, UInt256? leafHash);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeafAt")]
    public abstract bool? VerifyWithdrawalLeafAt(BigInteger? chainId, BigInteger? batchNumber, UInt256? leafHash);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeafWithProof")]
    public abstract bool? VerifyWithdrawalLeafWithProof(BigInteger? chainId, BigInteger? batchNumber, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("enqueueForcedTransaction")]
    public abstract BigInteger? EnqueueForcedTransaction(BigInteger? chainId, byte[]? transactionBytes, UInt256? transactionHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeBatch")]
    public abstract void FinalizeBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("pauseChain")]
    public abstract void PauseChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishGatewayGlobalRoot")]
    public abstract bool? PublishGatewayGlobalRoot(BigInteger? batchEpoch, byte[]? constituentReferences, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain, byte[]? aggregatedProof);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("recordBatchDA")]
    public abstract void RecordBatchDA(BigInteger? chainId, BigInteger? batchNumber, UInt256? daCommitment, BigInteger? firstBlock, BigInteger? lastBlock);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerChain")]
    public abstract void RegisterChain(BigInteger? chainId, byte[]? configBytes, UInt256? genesisStateRoot);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("resumeChain")]
    public abstract void ResumeChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revertBatch")]
    public abstract void RevertBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitAndFinalizeBatch")]
    public abstract void SubmitAndFinalizeBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash, BigInteger? forcedInclusionCount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash, BigInteger? forcedInclusionCount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("updateChain")]
    public abstract void UpdateChain(byte[]? configBytes);

    #endregion
}
