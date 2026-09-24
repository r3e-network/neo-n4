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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.RollupHub"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":174,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1275,""safe"":false},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1432,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1562,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1689,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1719,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1773,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":1827,""safe"":true},{""name"":""recordBatchDA"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""daCommitment"",""type"":""Hash256""},{""name"":""firstBlock"",""type"":""Integer""},{""name"":""lastBlock"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1898,""safe"":false},{""name"":""getBatchDACommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2399,""safe"":true},{""name"":""isBatchDAAvailable"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2471,""safe"":true},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""transactionBytes"",""type"":""ByteArray""},{""name"":""transactionHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2491,""safe"":false},{""name"":""getNextForcedNonce"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3326,""safe"":true},{""name"":""getPendingForcedCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3333,""safe"":true},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3405,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6228,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5061,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6333,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6609,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":7495,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":7215,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":7789,""safe"":false},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7230,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12514,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12582,""safe"":true},{""name"":""getSharedBridge"",""parameters"":[],""returntype"":""Hash160"",""offset"":7731,""safe"":true},{""name"":""setSharedBridge"",""parameters"":[{""name"":""sharedBridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12650,""safe"":false},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":11245,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4709,""safe"":true},{""name"":""getLatestFinalizedBatchNumber"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4499,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":10355,""safe"":true},{""name"":""getBatchCommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":12781,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":631,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12812,""safe"":false},{""name"":""getVerifierRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":5788,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""controller"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12925,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":2866,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":13199,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":13217,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":13275,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":13964,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainStatusChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""GenesisRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionsConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Consolidated L1 Rollup Hub for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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BCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhhpoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+oShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFKcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpGLUlCP///2giAkBXDQd4ygFBAbgkIAwbY29tbWl0bWVudCBoZWFkZXIgdG9vIHNob3J04HkLmCQFCSIHecoAIJckIwwebDFNZXNzYWdlSGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HoLmCQFCSIHesoAIJckJgwhYmxvY2tDb250ZXh0SGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HlwenF7NYz3//8kEwwOY2hhaW4gaW5hY3RpdmXgezWS+///fRKYJDgMM29wdGltaXN0aWMgYmF0Y2hlcyByZXF1aXJlIGEgd2lyZWQgY2hhbGxlbmdlIHdpbmRvd+B7NQ4CAAByfGoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyQaDBViYXRjaCBvdXQgb2Ygc2VxdWVuY2XgACAAHHg1CwIAANsoStgkCUrKACAoAzpzezV2AgAAdGtslyQcDBdwcmUtc3RhdGUgcm9vdCBtaXNtYXRjaOB7NTj0//81lPL//9swdW0AVM52bQBVzncHbwduNZ4CAAB9bjWOAwAAJEMMPnByb29mIHR5cGUgaW5jb21wYXRpYmxlIHdpdGggY2hhaW4ncyBhZHZlcnRpc2VkIHNlY3VyaXR5IGxldmVs4AAgAfwAeDVhAQAA2yhK2CQJSsoAICgDOncIbwh8ezXz9v//fmloeDVsAwAAdwkAIAEcAXg1MwEAANsoStgkCUrKACAoAzp3Cm8JbwqXJB8MGnB1YmxpYyBpbnB1dCBoYXNoIG1pc21hdGNo4DWxBQAAdwtvCwwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCUMIHZlcmlmaWVyIHJlZ2lzdHJ5IG5vdCBjb25maWd1cmVk4HgRwB8MC3ZlcmlmeVByb29mbwtBYn1bUncMbwwkHgwZcHJvb2YgdmVyaWZpY2F0aW9uIGZhaWxlZOB8ezVqBQAAeFA1cvP//357NWv6//9+ELcmEHx7NV0FAAB+UDVC+v//QFcBAXg0NTUV8f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcAAXgAITWD8v//QNsoStgkCUrKACAoAzpAVwIDeohwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWl6tSSRaCICQFcCAXg0WTVD8P//cGgLmCYQaErYJAlKygAgKAM6Ij94NVTy//81JPD//3FpC5gkIAwbZ2VuZXNpcyByb290IG5vdCByZWdpc3RlcmVk4GlK2CQJSsoAICgDOiICQFcAAXgAIDWN8f//QFcAAngUtiRQDEtzZWN1cml0eUxldmVsIG11c3QgYmUgMC4uNCAoU2lkZWNoYWluL1NldHRsZWQvT3B0aW1pc3RpYy9WYWxpZGl0eS9WYWxpZGl1bSngeRO2JDAMK2RhTW9kZSBtdXN0IGJlIDAuLjMgKEwxL05lb0ZTL0V4dGVybmFsL0RBQyngeBOXJjB5EJckKwwmVmFsaWRpdHkgc2VjdXJpdHkgbGV2ZWwgcmVxdWlyZXMgTDEgREHgeBSXJjd5EJgkMgwtVmFsaWRpdW0gc2VjdXJpdHkgbGV2ZWwgcmVxdWlyZXMgb2ZmLWNoYWluIERB4EBXAAJ4EJcmBQgiBXgRlyYXeRGXJgUIIgV5EpcmBQgiBXkTlyIpeBKXJg95EpcmBQgiBXkTlyIXeBOXJgUIIgV4FJcmB3kTlyIFCSICQFcCBAFgAYhwEHEiPnhpzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkB/AC1JL8QcSJweWnOSmgB/ABpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JI4QcSOlAAAAeAH8AGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaAEcAWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUlXP///xBxInB6ac5KaAE8AWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkjnsB/wCRShAuBCIISgH/ADIGAf8AkUpoAVwBUdBFexipAf8AkUoQLgQiCEoB/wAyBgH/AJFKaAFdAVHQRXsgqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXgFR0EV7ABipAf8AkUoQLgQiCEoB/wAyBgH/AJFKaAFfAVHQRWjbKDcAAHFpNwAAStgkCUrKACAoAzoiAkA3AABA2yhAVwEADAEC2zA1Cuz//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAAnl4ACM1P/H//0BXAAJ5eABFNTLx//9AVwEDeXg0bQwBA9swUDXo7f//eDXJ+v//eVA1xfT//3g1svv//3pQNXvp//95eDS4NY7r//9waAuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOBo2zB5eDQqenl4E8AMDkJhdGNoRmluYWxpemVkQZUBb2FAVwACeXgAIjWz8P//QFcEA3rbKDcAAHBoNwAA2zBxAECIchBzI60AAABpa85KamtR0EV6AdwAa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpqACBrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrACC1JVT///95eDQKalA1o+z//0BXAAJ5eBk1zu///0BXBAQQeDWB6v//cBR4NSz1//9xeAE8Ac5ye2ppaHp5eDUm9v//aWg15/7//3MMAQHbMGs1Xuz//wAgADx4NVX5///bKErYJAlKygAgKAM6aWgTwAwOQmF0Y2hTdWJtaXR0ZWRBlQFvYUBXBQI1t+n//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hg12e3//yQTDA5jaGFpbiBpbmFjdGl2ZeB4Nd/x//94NZb4//9weWgRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyQfDBptdXN0IGZpbmFsaXplIHNlcXVlbnRpYWxseeB5eDUN/v//cWk1Uun//3JqC5gkBQkiB2oQzhGXJBYMEWJhdGNoIG5vdCBwZW5kaW5n4Hl4NU/9//81Iun//3NrC5gkFwwSbWlzc2luZyBjb21taXRtZW504AAgADxr2zA1M/j//9soStgkCUrKACAoAzp0bHl4NSf9//9AzkBXBQI1WwIAACYhNVfx//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOAiHzV96P//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeXg1Sf3//3BoNY7o//9xaQuYJBQMD2JhdGNoIG5vdCBmb3VuZOB4NVX3//9yeWqXJxUBAABpEM4TlyQYDBNiYXRjaCBub3QgZmluYWxpemVk4Hl4NdMBAAC3JCcMImJhdGNoIGFscmVhZHkgcHVibGlzaGVkIGJ5IGdhdGV3YXngeXg1PPz//zUP6P//c2sLmCQXDBJtaXNzaW5nIGNvbW1pdG1lbnTgACAAHGvbMDUg9///2yhK2CQJSsoAICgDOnQMAQTbMGg1Bur//3l4NfD7//81lwEAAHl4NVb9//81iwEAAHl4NRTt//81fwEAAHl4NY0BAAB4NbL3//9sUDV75f//eDWv9v//eRGfShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFQNX3w//8jvwAAAHlqEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZcmSWkQzhGXJBYMEWJhdGNoIG5vdCBwZW5kaW5n4AwBBNswaDU16f//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DBNHbG9iYWxSb290UHVibGlzaGVkQZUBb2FvCHt4E8AMF0dsb2JhbFJvb3RQcm9vZkFjY2VwdGVkQZUBb2EQdw0jdwEAAG8NSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxygSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cObw5oNe7b//93D28OFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfaDVn5v//dxBvEG8PNQ31//+3JhFvDzU89f//bxBQNYzk//9vEG8PNYrv//81Xdv//3cRbxELmCQXDBJtaXNzaW5nIGNvbW1pdG1lbnTgbxHbMHcSACABvABvEjVm6v//2yhK2CQJSsoAICgDOncTACAB3ABvEjVL6v//2yhK2CQJSsoAICgDOncUbxRvE28Qbw8UwB8ME3B1Ymxpc2hNZXNzYWdlUm9vdHNpQWJ9W1JFbw1KnEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF3DUVvDXy1JYr+//8IIgJAVwABeBCYJAUJIgd4Af4AmCQFCSIHeAH/AJgiAkBXAgAdxABwEHEiPRCISmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaWjKtSTBaCICQFcBAnl4Neru//81Mdr//3BoC5cmBRAiBWgQziICQFcCAXg1uNv//zUU2v//cGgLmCQZDBRjaGFpbiBub3QgcmVnaXN0ZXJlZOBo2zBxaQBWzhCYIgJAVwMDecoAIJckIgwdR2F0ZXdheSBsZWFmIG11c3QgYmUgMzIgYnl0ZXPgeXB6cRByaRGREZcnvwAAAGp4yrUkHgwZR2F0ZXdheSBmcm9udGllciBvdmVyZmxvd+B4as7KACCXJCMMHkdhdGV3YXkgZnJvbnRpZXIgaXMgaW5jb21wbGV0ZeBoeGrONZQAAABKcEUQiEp4alHQRWkRqUoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFKcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckUjQf///2p4yrUkHgwZR2F0ZXdheSBmcm9udGllciBvdmVyZmxvd+BoSnhqUdBFQFcCAnjKACCXJAUJIgd5ygAglyQiDB1HYXRld2F5IG5vZGUgbXVzdCBiZSAzMiBieXRlc+AAQIhwEHEieHhpzkpoaVHQRXlpzkpoACBpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JIZo2yg3AABxaTcAANswIgJAVwQCEIhwEHEQciMEAQAAeGrOc2vKEJcmByPCAAAAa8oAIJckIAwbR2F0ZXdheSBmcm9udGllciBpcyBjb3JydXB04GjKEJcmD2tKcEVqSnFFI4oAAAB5JkZparUmQWhoNdj+//9KcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUUivmhrNZn+//9KcEVqEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanjKtSX9/v//aMoAIJckHgwZR2F0ZXdheSBmcm9udGllciBpcyBlbXB0eeBoIgJAVwQIAaoAiHBYcRByIj5pas5KaGpR0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqacq1JMBB2/6odNswchBzIm5qa85KaBhrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrABS1JJB/BwAcaDRgeAA8aDXbAAAAeQBEaDRRegBkaDRLewGEAGg15AIAAHxKaAGIAFHQRX1KaAGJAFHQRX4BigBoNCho2yg3AABzazcAANsw2yhK2CQJSsoAICgDOiICQNswQEHb/qh0QFcCA3rbMHAQcSJuaGnOSnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQQNswQFcAA3pKEC4EIghKAf8AMgYB/wCRSnh5UdBFehipShAuBCIISgH/ADIGAf8AkUp4eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXogqUoQLgQiCEoB/wAyBgH/AJFKeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ABipShAuBCIISgH/ADIGAf8AkUp4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAIKlKEC4EIghKAf8AMgYB/wCRSnh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKeHkVnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ADCpShAuBCIISgH/ADIGAf8AkUp4eRaeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSnh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFQFcAA3pKEC4EIghKAf8AMgYB/wCRSnh5UdBFehipShAuBCIISgH/ADIGAf8AkUp4eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXogqUoQLgQiCEoB/wAyBgH/AJFKeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ABipShAuBCIISgH/ADIGAf8AkUp4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRUBXAQEZiHAcSmgQUdBFeBFoNdr8//9oIgJAVwEBeDTjcB1KaBBR0EVoIgJAVwEBeDTRNcbR//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXAQF4NKY1gtH//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAICgDOiICQFcAATXC6v//JiE1vtn//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4CIfNeTQ//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQaDBVpbnZhbGlkIHNoYXJlZCBicmlkZ2XgeAwBC9swNavO//9AVwECeXg15OT//zW30P//cGgLlyYGEIgiBWjbMCICQFcAATVo0P//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNQPq//+qJBYMEWdvdmVybmFuY2UgbG9ja2Vk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBIMDWludmFsaWQgb3duZXLgeAwBAdswNRvO//9AVwABNa/p//8mITWr2P//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgIh810c///0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJCIMHWludmFsaWQgZ292ZXJuYW5jZSBjb250cm9sbGVy4HgMAUPbMDWQzf//QFcCAnl4NWb1//8TmCYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACJWeXg1muP//zVtz///cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIgaNswcQAgAZwAaTVs3v//2yhK2CQJSsoAICgDOiICQFcBAng1AN7//3B5aHg0BSICQFcBA3l4NWL///9waAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXJgUJIgdoepciAkBXCAV5eDUo////cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN+AgAAewuYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HtxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgetswcnxzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2sQmCYFCSIUaGrbKErYJAlKygAgKAM6lyICQFYBDAhORU80R1dSMtswYEDCxwg4").AsSerializable<Neo.SmartContract.NefFile>();

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
