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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.RollupHub"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":174,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1278,""safe"":false},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1434,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1563,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1689,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1719,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1773,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":1827,""safe"":true},{""name"":""recordBatchDA"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""daCommitment"",""type"":""Hash256""},{""name"":""firstBlock"",""type"":""Integer""},{""name"":""lastBlock"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1898,""safe"":false},{""name"":""getBatchDACommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2399,""safe"":true},{""name"":""isBatchDAAvailable"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2471,""safe"":true},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""transactionBytes"",""type"":""ByteArray""},{""name"":""transactionHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2491,""safe"":false},{""name"":""getNextForcedNonce"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3324,""safe"":true},{""name"":""getPendingForcedCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3331,""safe"":true},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3403,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6214,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5051,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6319,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6595,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":7481,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":7199,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":7775,""safe"":false},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7214,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12501,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12569,""safe"":true},{""name"":""getSharedBridge"",""parameters"":[],""returntype"":""Hash160"",""offset"":7717,""safe"":true},{""name"":""setSharedBridge"",""parameters"":[{""name"":""sharedBridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12637,""safe"":false},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":11232,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4699,""safe"":true},{""name"":""getLatestFinalizedBatchNumber"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4489,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":10342,""safe"":true},{""name"":""getBatchCommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":12768,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":634,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12799,""safe"":false},{""name"":""getVerifierRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":5778,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""controller"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12912,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":2865,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":13186,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":13204,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":13262,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":13951,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainStatusChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""GenesisRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionsConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Consolidated L1 Rollup Hub for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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SgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhhpoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+oShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFKcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpGLUlCP///2giAkBXDQd4ygFBAbgkIAwbY29tbWl0bWVudCBoZWFkZXIgdG9vIHNob3J04HkLmCQFCSIHecoAIJckIwwebDFNZXNzYWdlSGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HoLmCQFCSIHesoAIJckJgwhYmxvY2tDb250ZXh0SGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HlwenF7NY/3//8kEwwOY2hhaW4gaW5hY3RpdmXgezWU+///fRKYJDgMM29wdGltaXN0aWMgYmF0Y2hlcyByZXF1aXJlIGEgd2lyZWQgY2hhbGxlbmdlIHdpbmRvd+B7NQcCAAByfGoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyQaDBViYXRjaCBvdXQgb2Ygc2VxdWVuY2XgACAAHHg1BAIAANsoStgkCUrKACAoAzpzezVvAgAAdGtslyQcDBdwcmUtc3RhdGUgcm9vdCBtaXNtYXRjaOB7NT70//81mvL//9swdW0AVM52bQBVzncHbwduNZcCAAB9bjWHAwAAJEMMPnByb29mIHR5cGUgaW5jb21wYXRpYmxlIHdpdGggY2hhaW4ncyBhZHZlcnRpc2VkIHNlY3VyaXR5IGxldmVs4AAgAfwAeDVaAQAA2yhK2CQJSsoAICgDOncIbwh8ezX29v//fmloeDVlAwAAdwkAIAEcAXg1LAEAANsoStgkCUrKACAoAzp3Cm8JbwqXJB8MGnB1YmxpYyBpbnB1dCBoYXNoIG1pc21hdGNo4DWqBQAAdwtvCwwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCUMIHZlcmlmaWVyIHJlZ2lzdHJ5IG5vdCBjb25maWd1cmVk4HgRwB8MC3ZlcmlmeVByb29mbwtBYn1bUncMbwwkHgwZcHJvb2YgdmVyaWZpY2F0aW9uIGZhaWxlZOB4fHs1YgUAADV58///fns1bvr//358ezVbBQAANUv6//9AVwEBeDQ1NSLx//9waAuXJgUQIiRoStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6IgJAVwABeAAhNZDy//9A2yhK2CQJSsoAICgDOkBXAgN6iHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaXq1JJFoIgJAVwIBeDRZNVDw//9waAuYJhBoStgkCUrKACAoAzoiP3g1YfL//zUx8P//cWkLmCQgDBtnZW5lc2lzIHJvb3Qgbm90IHJlZ2lzdGVyZWTgaUrYJAlKygAgKAM6IgJAVwABeAAgNZrx//9AVwACeBS2JFAMS3NlY3VyaXR5TGV2ZWwgbXVzdCBiZSAwLi40IChTaWRlY2hhaW4vU2V0dGxlZC9PcHRpbWlzdGljL1ZhbGlkaXR5L1ZhbGlkaXVtKeB5E7YkMAwrZGFNb2RlIG11c3QgYmUgMC4uMyAoTDEvTmVvRlMvRXh0ZXJuYWwvREFDKeB4E5cmMHkQlyQrDCZWYWxpZGl0eSBzZWN1cml0eSBsZXZlbCByZXF1aXJlcyBMMSBEQeB4FJcmN3kQmCQyDC1WYWxpZGl1bSBzZWN1cml0eSBsZXZlbCByZXF1aXJlcyBvZmYtY2hhaW4gREHgQFcAAngQlyYFCCIFeBGXJhd5EZcmBQgiBXkSlyYFCCIFeROXIil4EpcmD3kSlyYFCCIFeROXIhd4E5cmBQgiBXgUlyYHeROXIgUJIgJAVwIEAWABiHAQcSI+eGnOSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQH8ALUkvxBxInB5ac5KaAH8AGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkjhBxI6UAAAB4AfwAaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoARwBaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSVc////EHEicHppzkpoATwBaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSOewH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXAFR0EV7GKkB/wCRShAuBCIISgH/ADIGAf8AkUpoAV0BUdBFeyCpAf8AkUoQLgQiCEoB/wAyBgH/AJFKaAFeAVHQRXsAGKkB/wCRShAuBCIISgH/ADIGAf8AkUpoAV8BUdBFaNsoNwAAcWk3AABK2CQJSsoAICgDOiICQDcAAEDbKEBXAQAMAQLbMDUX7P//cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwACeXgAIzVJ8f//QFcAAnl4AEU1PPH//0BXAQMMAQPbMHl4NGU19u3//3l4Ncn6//810PT//3p4NbP7//81iOn//3l4NLs1nuv//3BoC5gkFwwSbWlzc2luZyBjb21taXRtZW504GjbMHl4NCp6eXgTwAwOQmF0Y2hGaW5hbGl6ZWRBlQFvYUBXAAJ5eAAiNcDw//9AVwQDetsoNwAAcGg3AADbMHEAQIhyEHMjrQAAAGlrzkpqa1HQRXoB3ABrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmoAIGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAILUlVP///2p5eDQINbTs//9AVwACeXgZNdzv//9AVwQEEHg1kur//3AUeDU39f//cXgBPAHOcntqaWh6eXg1Mfb//2loNej+//9zDAEB2zBrNW/s//8AIAA8eDVZ+f//2yhK2CQJSsoAICgDOmloE8AMDkJhdGNoU3VibWl0dGVkQZUBb2FAVwUCNcjp//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4Neft//8kEwwOY2hhaW4gaW5hY3RpdmXgeDXs8f//eDWa+P//cHloEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZckHwwabXVzdCBmaW5hbGl6ZSBzZXF1ZW50aWFsbHngeXg1Dv7//3FpNWPp//9yaguYJAUJIgdqEM4RlyQWDBFiYXRjaCBub3QgcGVuZGluZ+B5eDVT/f//NTPp//9zawuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOAAIAA8a9swNTf4///bKErYJAlKygAgKAM6dGx5eDUr/f//QM5AVwUCNVkCAAAmITVk8f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgIh81juj//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hl4NUr9//9waDWf6P//cWkLmCQUDA9iYXRjaCBub3QgZm91bmTgeDVZ9///cnlqlycTAQAAaRDOE5ckGAwTYmF0Y2ggbm90IGZpbmFsaXplZOB5eDXRAQAAtyQnDCJiYXRjaCBhbHJlYWR5IHB1Ymxpc2hlZCBieSBnYXRld2F54Hl4NUD8//81IOj//3NrC5gkFwwSbWlzc2luZyBjb21taXRtZW504AAgABxr2zA1JPf//9soStgkCUrKACAoAzp0DAEE2zBoNRfq//95eDX0+///NZUBAAB5eDVW/f//NYkBAAB5eDUi7f//NX0BAAB5eDWLAQAAbHg1tff//zWK5f//eRGfShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJF4NYX2//81jPD//yO/AAAAeWoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyZJaRDOEZckFgwRYmF0Y2ggbm90IHBlbmRpbmfgDAEE2zBoNUjp//95eDUl+///NcYAAAB5eDVf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UHVibGlzaGVkQZUBb2FvCHt4E8AMF0dsb2JhbFJvb3RQcm9vZkFjY2VwdGVkQZUBb2EQdw0jdgEAAG8NSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxygSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cObw5oNf3b//93D28OFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfaDVw5v//dxBvEG8PNQn1//+3JhBvEG8PNTb1//81mOT//28Qbw81je///zVt2///dxFvEQuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOBvEdswdxIAIAG8AG8SNWnq///bKErYJAlKygAgKAM6dxMAIAHcAG8SNU7q///bKErYJAlKygAgKAM6dxRvFG8TbxBvDxTAHwwTcHVibGlzaE1lc3NhZ2VSb290c2lBYn1bUkVvDUqcShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXcNRW8NfLUli/7//wgiAkBXAAF4EJgkBQkiB3gB/gCYJAUJIgd4Af8AmCICQFcCAB3EAHAQcSI9EIhKaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpaMq1JMFoIgJAVwECeXg16u7//zVB2v//cGgLlyYFECIFaBDOIgJAVwIBeDXI2///NSTa//9waAuYJBkMFGNoYWluIG5vdCByZWdpc3RlcmVk4GjbMHFpAFbOEJgiAkBXAwN5ygAglyQiDB1HYXRld2F5IGxlYWYgbXVzdCBiZSAzMiBieXRlc+B5cHpxEHJpEZERlye/AAAAanjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934HhqzsoAIJckIwweR2F0ZXdheSBmcm9udGllciBpcyBpbmNvbXBsZXRl4Gh4as41lAAAAEpwRRCISnhqUdBFaRGpShAuBCIOSgP/////AAAAADIMA/////8AAAAAkUpxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRSNB////anjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934GhKeGpR0EVAVwICeMoAIJckBQkiB3nKACCXJCIMHUdhdGV3YXkgbm9kZSBtdXN0IGJlIDMyIGJ5dGVz4ABAiHAQcSJ4eGnOSmhpUdBFeWnOSmgAIGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkhmjbKDcAAHFpNwAA2zAiAkBXBAIQiHAQcRByIwQBAAB4as5za8oQlyYHI8IAAABrygAglyQgDBtHYXRld2F5IGZyb250aWVyIGlzIGNvcnJ1cHTgaMoQlyYPa0pwRWpKcUUjigAAAHkmRmlqtSZBaGg12P7//0pwRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRSK+aGs1mf7//0pwRWoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqeMq1Jf3+//9oygAglyQeDBlHYXRld2F5IGZyb250aWVyIGlzIGVtcHR54GgiAkBXBAgBqgCIcFhxEHIiPmlqzkpoalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWppyrUkwEHb/qh02zByEHMibmprzkpoGGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAFLUkkH8HABxoNGB4ADxoNdsAAAB5AERoNFF6AGRoNEt7AYQAaDXkAgAAfEpoAYgAUdBFfUpoAYkAUdBFfgGKAGg0KGjbKDcAAHNrNwAA2zDbKErYJAlKygAgKAM6IgJA2zBAQdv+qHRAVwIDetswcBBxIm5oac5KeHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBA2zBAVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8AMgYB/wCRSnh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeiCpShAuBCIISgH/ADIGAf8AkUp4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSnh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKeHkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ACipShAuBCIISgH/ADIGAf8AkUp4eRWeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAMKlKEC4EIghKAf8AMgYB/wCRSnh5Fp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKeHkXnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVAVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8AMgYB/wCRSnh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeiCpShAuBCIISgH/ADIGAf8AkUp4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSnh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFQFcBARmIcBxKaBBR0EV4EWg12vz//2giAkBXAQF4NONwHUpoEFHQRWgiAkBXAQF4NNE11tH//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAICgDOiICQFcBAXg0pjWS0f//cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwABNb/q//8mITXK2f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgIh819ND//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBoMFWludmFsaWQgc2hhcmVkIGJyaWRnZeB4DAEL2zA1uM7//0BXAQJ5eDXn5P//NcfQ//9waAuXJgYQiCIFaNswIgJAVwABNXjQ//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA1AOr//6okFgwRZ292ZXJuYW5jZSBsb2NrZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okEgwNaW52YWxpZCBvd25lcuB4DAEB2zA1KM7//0BXAAE1rOn//yYhNbfY//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOAiHzXhz///Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okIgwdaW52YWxpZCBnb3Zlcm5hbmNlIGNvbnRyb2xsZXLgeAwBQ9swNZ3N//9AVwICeXg1ZvX//xOYJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIlZ5eDWd4///NX3P//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIiBo2zBxACABnABpNW/e///bKErYJAlKygAgKAM6IgJAVwECeDUD3v//cHloeDQFIgJAVwEDeXg1Yv///3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmBQkiB2h6lyICQFcIBXl4NSj///9waAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXJggJI34CAAB7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3FpygBAtiQTDA5wcm9vZiB0b28gZGVlcOB62zByfHMQdCMbAgAAaWzOdW3KACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh2axGREJcn0wAAABB3ByJCam8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic21vB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkiiPOAAAAEHcHIkJtbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzam8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKbtsoNwAAdwdvBzcAANswSnJFaxGpSnNFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbGnKtSXm/f//axCYJgUJIhRoatsoStgkCUrKACAoAzqXIgJAVgEMCE5FTzRHV1Iy2zBgQAwnFVA=").AsSerializable<Neo.SmartContract.NefFile>();

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
