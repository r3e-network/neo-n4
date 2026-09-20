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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.RollupHub"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":174,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1278,""safe"":false},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1434,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1563,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1689,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1719,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1773,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":1827,""safe"":true},{""name"":""recordBatchDA"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""daCommitment"",""type"":""Hash256""},{""name"":""firstBlock"",""type"":""Integer""},{""name"":""lastBlock"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1898,""safe"":false},{""name"":""getBatchDACommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2399,""safe"":true},{""name"":""isBatchDAAvailable"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2471,""safe"":true},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""transactionBytes"",""type"":""ByteArray""},{""name"":""transactionHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2491,""safe"":false},{""name"":""getNextForcedNonce"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3324,""safe"":true},{""name"":""getPendingForcedCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3331,""safe"":true},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3403,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6219,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5056,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6324,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6600,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":7483,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":7204,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":7777,""safe"":false},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7219,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12503,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12571,""safe"":true},{""name"":""getSharedBridge"",""parameters"":[],""returntype"":""Hash160"",""offset"":7719,""safe"":true},{""name"":""setSharedBridge"",""parameters"":[{""name"":""sharedBridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12639,""safe"":false},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":11234,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4704,""safe"":true},{""name"":""getLatestFinalizedBatchNumber"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4494,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":10344,""safe"":true},{""name"":""getBatchCommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":12770,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":634,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12801,""safe"":false},{""name"":""getVerifierRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":5783,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""controller"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":12914,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":2865,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":13188,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":13206,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":13264,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":13953,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainStatusChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""GenesisRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionsConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Consolidated L1 Rollup Hub for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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SgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhhpoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+oShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFKcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpGLUlCP///2giAkBXDQd4ygFBAbgkIAwbY29tbWl0bWVudCBoZWFkZXIgdG9vIHNob3J04HkLmCQFCSIHecoAIJckIwwebDFNZXNzYWdlSGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HoLmCQFCSIHesoAIJckJgwhYmxvY2tDb250ZXh0SGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HlwenF7NY/3//8kEwwOY2hhaW4gaW5hY3RpdmXgezWU+///fRKYJDgMM29wdGltaXN0aWMgYmF0Y2hlcyByZXF1aXJlIGEgd2lyZWQgY2hhbGxlbmdlIHdpbmRvd+B7NQwCAAByfGoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyQaDBViYXRjaCBvdXQgb2Ygc2VxdWVuY2XgACAAHHg1CQIAANsoStgkCUrKACAoAzpzezV0AgAAdGtslyQcDBdwcmUtc3RhdGUgcm9vdCBtaXNtYXRjaOB7NT70//81mvL//9swdW0AVM52bQBVzncHbwduNZwCAAB9bjWMAwAAJEMMPnByb29mIHR5cGUgaW5jb21wYXRpYmxlIHdpdGggY2hhaW4ncyBhZHZlcnRpc2VkIHNlY3VyaXR5IGxldmVs4AAgAfwAeDVfAQAA2yhK2CQJSsoAICgDOncIbwh8ezX29v//fmloeDVqAwAAdwkAIAEcAXg1MQEAANsoStgkCUrKACAoAzp3Cm8JbwqXJB8MGnB1YmxpYyBpbnB1dCBoYXNoIG1pc21hdGNo4DWvBQAAdwtvCwwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCUMIHZlcmlmaWVyIHJlZ2lzdHJ5IG5vdCBjb25maWd1cmVk4HgRwB8MC3ZlcmlmeVByb29mbwtBYn1bUncMbwwkHgwZcHJvb2YgdmVyaWZpY2F0aW9uIGZhaWxlZOB4fHs1ZwUAADV58///fns1bvr//34QtyYPfnx7NVsFAAA1Rvr//0BXAQF4NDU1HfH//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAF4ACE1i/L//0DbKErYJAlKygAgKAM6QFcCA3qIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVperUkkWgiAkBXAgF4NFk1S/D//3BoC5gmEGhK2CQJSsoAICgDOiI/eDVc8v//NSzw//9xaQuYJCAMG2dlbmVzaXMgcm9vdCBub3QgcmVnaXN0ZXJlZOBpStgkCUrKACAoAzoiAkBXAAF4ACA1lfH//0BXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCYJDIMLVZhbGlkaXVtIHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIG9mZi1jaGFpbiBEQeBAVwACeBCXJgUIIgV4EZcmF3kRlyYFCCIFeRKXJgUIIgV5E5ciKXgSlyYPeRKXJgUIIgV5E5ciF3gTlyYFCCIFeBSXJgd5E5ciBQkiAkBXAgQBYAGIcBBxIj54ac5KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpAfwAtSS/EHEicHlpzkpoAfwAaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSOEHEjpQAAAHgB/ABpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmgBHAFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JVz///8QcSJwemnOSmgBPAFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JI57Af8AkUoQLgQiCEoB/wAyBgH/AJFKaAFcAVHQRXsYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXQFR0EV7IKkB/wCRShAuBCIISgH/ADIGAf8AkUpoAV4BUdBFewAYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXwFR0EVo2yg3AABxaTcAAErYJAlKygAgKAM6IgJANwAAQNsoQFcBAAwBAtswNRLs//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAJ5eAAjNUTx//9AVwACeXgARTU38f//QFcBAwwBA9sweXg0ZTXx7f//eXg1yfr//zXL9P//eng1s/v//zWD6f//eXg0uzWZ6///cGgLmCQXDBJtaXNzaW5nIGNvbW1pdG1lbnTgaNsweXg0Knp5eBPADA5CYXRjaEZpbmFsaXplZEGVAW9hQFcAAnl4ACI1u/D//0BXBAN62yg3AABwaDcAANswcQBAiHIQcyOtAAAAaWvOSmprUdBFegHcAGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KagAga55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAgtSVU////anl4NAg1r+z//0BXAAJ5eBk11+///0BXBAQQeDWN6v//cBR4NTL1//9xeAE8Ac5ye2ppaHp5eDUs9v//aWg16P7//3MMAQHbMGs1auz//wAgADx4NVn5///bKErYJAlKygAgKAM6aWgTwAwOQmF0Y2hTdWJtaXR0ZWRBlQFvYUBXBQI1w+n//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hg14u3//yQTDA5jaGFpbiBpbmFjdGl2ZeB4Nefx//94NZr4//9weWgRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyQfDBptdXN0IGZpbmFsaXplIHNlcXVlbnRpYWxseeB5eDUO/v//cWk1Xun//3JqC5gkBQkiB2oQzhGXJBYMEWJhdGNoIG5vdCBwZW5kaW5n4Hl4NVP9//81Lun//3NrC5gkFwwSbWlzc2luZyBjb21taXRtZW504AAgADxr2zA1N/j//9soStgkCUrKACAoAzp0bHl4NSv9//9AzkBXBQI1WQIAACYhNV/x//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOAiHzWJ6P//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeXg1Sv3//3BoNZro//9xaQuYJBQMD2JhdGNoIG5vdCBmb3VuZOB4NVn3//9yeWqXJxMBAABpEM4TlyQYDBNiYXRjaCBub3QgZmluYWxpemVk4Hl4NdEBAAC3JCcMImJhdGNoIGFscmVhZHkgcHVibGlzaGVkIGJ5IGdhdGV3YXngeXg1QPz//zUb6P//c2sLmCQXDBJtaXNzaW5nIGNvbW1pdG1lbnTgACAAHGvbMDUk9///2yhK2CQJSsoAICgDOnQMAQTbMGg1Eur//3l4NfT7//81lQEAAHl4NVb9//81iQEAAHl4NR3t//81fQEAAHl4NYsBAABseDW19///NYXl//95EZ9KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXg1hfb//zWH8P//I78AAAB5ahGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGXJklpEM4RlyQWDBFiYXRjaCBub3QgcGVuZGluZ+AMAQTbMGg1Q+n//3l4NSX7//81xgAA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bnRpZXIgb3ZlcmZsb3fgaEp4alHQRUBXAgJ4ygAglyQFCSIHecoAIJckIgwdR2F0ZXdheSBub2RlIG11c3QgYmUgMzIgYnl0ZXPgAECIcBBxInh4ac5KaGlR0EV5ac5KaAAgaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSGaNsoNwAAcWk3AADbMCICQFcEAhCIcBBxEHIjBAEAAHhqznNryhCXJgcjwgAAAGvKACCXJCAMG0dhdGV3YXkgZnJvbnRpZXIgaXMgY29ycnVwdOBoyhCXJg9rSnBFakpxRSOKAAAAeSZGaWq1JkFoaDXY/v//SnBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFIr5oazWZ/v//SnBFahGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp4yrUl/f7//2jKACCXJB4MGUdhdGV3YXkgZnJvbnRpZXIgaXMgZW1wdHngaCICQFcECAGqAIhwWHEQciI+aWrOSmhqUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFamnKtSTAQdv+qHTbMHIQcyJuamvOSmgYa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAUtSSQfwcAHGg0YHgAPGg12wAAAHkARGg0UXoAZGg0S3sBhABoNeQCAAB8SmgBiABR0EV9SmgBiQBR0EV+AYoAaDQoaNsoNwAAc2s3AADbMNsoStgkCUrKACAoAzoiAkDbMEBB2/6odEBXAgN62zBwEHEibmhpzkp4eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkkEDbMEBXAAN6ShAuBCIISgH/ADIGAf8AkUp4eVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6IKlKEC4EIghKAf8AMgYB/wCRSnh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ACCpShAuBCIISgH/ADIGAf8AkUp4eRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAKKlKEC4EIghKAf8AMgYB/wCRSnh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAwqUoQLgQiCEoB/wAyBgH/AJFKeHkWnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ADipShAuBCIISgH/ADIGAf8AkUp4eReeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRUBXAAN6ShAuBCIISgH/ADIGAf8AkUp4eVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6IKlKEC4EIghKAf8AMgYB/wCRSnh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVAVwEBGYhwHEpoEFHQRXgRaDXa/P//aCICQFcBAXg043AdSmgQUdBFaCICQFcBAXg00TXU0f//cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwEBeDSmNZDR//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXAAE1wur//yYhNcjZ//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOAiHzXy0P//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okGgwVaW52YWxpZCBzaGFyZWQgYnJpZGdl4HgMAQvbMDW2zv//QFcBAnl4Nerk//81xdD//3BoC5cmBhCIIgVo2zAiAkBXAAE1dtD//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DUD6v//qiQWDBFnb3Zlcm5hbmNlIGxvY2tlZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQSDA1pbnZhbGlkIG93bmVy4HgMAQHbMDUmzv//QFcAATWv6f//JiE1tdj//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4CIfNd/P//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQiDB1pbnZhbGlkIGdvdmVybmFuY2UgY29udHJvbGxlcuB4DAFD2zA1m83//0BXAgJ5eDVm9f//E5gmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiVnl4NaDj//81e8///3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiIGjbMHEAIAGcAGk1ct7//9soStgkCUrKACAoAzoiAkBXAQJ4NQbe//9weWh4NAUiAkBXAQN5eDVi////cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYFCSIHaHqXIgJAVwgFeXg1KP///3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCAkjfgIAAHsLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7cWnKAEC2JBMMDnByb29mIHRvbyBkZWVw4HrbMHJ8cxB0IxsCAABpbM51bcoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHZrEZEQlyfTAAAAEHcHIkJqbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzbW8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKI84AAAAQdwciQm1vB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNqbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIpu2yg3AAB3B28HNwAA2zBKckVrEalKc0VsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsacq1Jeb9//9rEJgmBQkiFGhq2yhK2CQJSsoAICgDOpciAkBWAQwITkVPNEdXUjLbMGBAJkB6PQ==").AsSerializable<Neo.SmartContract.NefFile>();

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
