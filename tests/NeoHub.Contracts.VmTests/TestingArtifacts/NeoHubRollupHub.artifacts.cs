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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.RollupHub"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":174,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":970,""safe"":false},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1126,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1255,""safe"":false},{""name"":""registerGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""genesisRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":1381,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1619,""safe"":true},{""name"":""isChainActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1649,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1685,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":1739,""safe"":true},{""name"":""recordBatchDA"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""daCommitment"",""type"":""Hash256""},{""name"":""firstBlock"",""type"":""Integer""},{""name"":""lastBlock"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1810,""safe"":false},{""name"":""getBatchDACommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2311,""safe"":true},{""name"":""isBatchDAAvailable"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2383,""safe"":true},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""transactionBytes"",""type"":""ByteArray""},{""name"":""transactionHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2403,""safe"":false},{""name"":""getNextForcedNonce"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3236,""safe"":true},{""name"":""getPendingForcedCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3243,""safe"":true},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3315,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6092,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4942,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6197,""safe"":false},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":6473,""safe"":false},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":9184,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11325,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11393,""safe"":true},{""name"":""getSharedBridge"",""parameters"":[],""returntype"":""Hash160"",""offset"":8960,""safe"":true},{""name"":""setSharedBridge"",""parameters"":[{""name"":""sharedBridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":11461,""safe"":false},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":10056,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4590,""safe"":true},{""name"":""getLatestFinalizedBatchNumber"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4380,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":9098,""safe"":true},{""name"":""getBatchCommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":11554,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":373,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":11585,""safe"":false},{""name"":""getVerifierRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":5669,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""controller"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":11670,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":2777,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11906,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11924,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":11982,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":12671,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainStatusChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""GenesisRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionsConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Consolidated L1 Rollup Hub for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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IhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXg1CAEAADUnAQAAemh4E8AMGUZvcmNlZFRyYW5zYWN0aW9uRW5xdWV1ZWRBlQFvYWgiAkBXAgE0WXBoDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgmPXgRwB8MDWlzQ2hhaW5QYXVzZWRoQWJ9W1JxaaokHwwaY2hhaW4gcGF1c2VkIGJ5IGdvdmVybmFuY2XgQFcBAAwBQ9swNcv2//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBBYn1bUkBXAQF4NDU1jfb//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAF4AEE12Pf//0BK2CYGRRAiBNshQFcAAnl4AEI1VPz//0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwMCeRCXJgcjygAAAHg1xQAAAHB4NXj///9xaHmeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFptiQnDCJmb3JjZWQgdHJhbnNhY3Rpb24gcXVldWUgdW5kZXJmbG934Gh5nkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRcmp4NGc1Q////3loeBPADBpGb3JjZWRUcmFuc2FjdGlvbnNDb25zdW1lZEGVAW9hQFcBAXg0NTVH9f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcAAXgAQDWS9v//QFcAAXg0t0BXAgF4NLBweDVm/v//cWlouCYzaWifShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJEiAxAiAkBXAgQQeDXg9P//cBR4NDJxe3gBPAHOaWh6eXg1MQEAAAAgADx4NVcEAADbKErYJAlKygAgKAM6aWg1QAkAAEBXAgIQcBBxI/oAAABoeHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGGmgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn6hK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IgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JVz///8QcSJwemnOSmgBPAFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JI57Af8AkUoQLgQiCEoB/wAyBgH/AJFKaAFcAVHQRXsYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXQFR0EV7IKkB/wCRShAuBCIISgH/ADIGAf8AkUpoAV4BUdBFewAYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXwFR0EVo2yg3AABxaTcAAErYJAlKygAgKAM6IgJANwAAQNsoQFcBAAwBAtswNX/r//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAJ5eAAjNV7x//9AVwEDDAED2zB5eDRlNTzt//95eDXW+v//NfL0//96eDXA+///NQLq//95eDTINRPr//9waAuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOBo2zB5eDQqenl4E8AMDkJhdGNoRmluYWxpemVkQZUBb2FAVwACeXgAIjXi8P//QFcEA3rbKDcAAHBoNwAA2zBxAECIchBzI60AAABpa85KamtR0EV6AdwAa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpqACBrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrACC1JVT///9qeXg0CDX66///QFcAAnl4GTX+7///QFcEBBB4NQfq//9wFHg1WfX//3F4ATwBznJ7amloenl4NVP2//9paDXo/v//cwwBAdswazW16///ACAAPHg1Zvn//9soStgkCUrKACAoAzppaBPADA5CYXRjaFN1Ym1pdHRlZEGV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SgP/////AAAAADIMA/////8AAAAAkXcNRW8NfLUli/7//wgiAkBXAAF4EJgkBQkiB3gB/gCYJAUJIgd4Af8AmCICQFcBAAwBC9swNaTe//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAgAdxABwEHEiPRCISmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaWjKtSTBaCICQFcBAnl4NUzz//81GN7//3BoC5cmBRAiBWgQziICQFcCAXg1fN///zX73f//cGgLmCQZDBRjaGFpbiBub3QgcmVnaXN0ZXJlZOBo2zBxaQBWzhCYIgJAVwEBeDQ1Ncbd//9waAuXJgUQIiRoStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6IgJAVwABeBo1Et///0BXAwN5ygAglyQiDB1HYXRld2F5IGxlYWYgbXVzdCBiZSAzMiBieXRlc+B5cHpxEHJpEZERlye/AAAAanjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934HhqzsoAIJckIwweR2F0ZXdheSBmcm9udGllciBpcyBpbmNvbXBsZXRl4Gh4as41lAAAAEpwRRCISnhqUdBFaRGpShAuBCIOSgP/////AAAAADIMA/////8AAAAAkUpxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRSNB////anjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934GhKeGpR0EVAVwICeMoAIJckBQkiB3nKACCXJCIMHUdhdGV3YXkgbm9kZSBtdXN0IGJlIDMyIGJ5dGVz4ABAiHAQcSJ4eGnOSmhpUdBFeWnOSmgAIGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkhmjbKDcAAHFpNwAA2zAiAkBXBAIQiHAQcRByIwQBAAB4as5za8oQlyYHI8IAAABrygAglyQgDBtHYXRld2F5IGZyb250aWVyIGlzIGNvcnJ1cHTgaMoQlyYPa0pwRWpKcUUjigAAAHkmRmlqtSZBaGg12P7//0pwRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRSK+aGs1mf7//0pwRWoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqeMq1Jf3+//9oygAglyQeDBlHYXRld2F5IGZyb250aWVyIGlzIGVtcHR54GgiAkBXBAgBqgCIcFhxEHIiPmlqzkpoalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWppyrUkwEHb/qh02zByEHMibmprzkpoGGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAFLUkkH8HABxoNGB4ADxoNdsAAAB5AERoNFF6AGRoNEt7AYQAaDXkAgAAfEpoAYgAUdBFfUpoAYkAUdBFfgGKAGg0KGjbKDcAAHNrNwAA2zDbKErYJAlKygAgKAM6IgJA2zBAQdv+qHRAVwIDetswcBBxIm5oac5KeHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBA2zBAVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8AMgYB/wCRSnh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeiCpShAuBCIISgH/ADIGAf8AkUp4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSnh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKeHkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ACipShAuBCIISgH/ADIGAf8AkUp4eRWeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAMKlKEC4EIghKAf8AMgYB/wCRSnh5Fp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKeHkXnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVAVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8AMgYB/wCRSnh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeiCpShAuBCIISgH/ADIGAf8AkUp4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSnh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFQFcBARmIcBxKaBBR0EV4EWg12vz//2giAkBXAQF4NONwHUpoEFHQRWgiAkBXAQF4NNE1adX//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAICgDOiICQFcBAXg0pjUl1f//cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwABNa3U//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQaDBVpbnZhbGlkIHNoYXJlZCBicmlkZ2XgeAwBC9swNXbT//9AVwECeXg1OOn//zWA1P//cGgLlyYGEIgiBWjbMCICQFcAATUx1P//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okEgwNaW52YWxpZCBvd25lcuB4DAEB2zA1AtP//0BXAAE13NP//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJCIMHWludmFsaWQgZ292ZXJuYW5jZSBjb250cm9sbGVy4HgMAUPbMDWd0v//QFcCAnl4NYr1//8TmCYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACJWeXg1MOj//zV40///cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIgaNswcQAgAZwAaTUC4///2yhK2CQJSsoAICgDOiICQFcBAng1luL//3B5aHg0BSICQFcBA3l4NWL///9waAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXJgUJIgdoepciAkBXCAV5eDUo////cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN+AgAAewuYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HtxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgetswcnxzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2sQmCYFCSIUaGrbKErYJAlKygAgKAM6lyICQFYBDAhORU80R1dSMtswYEBDUq10").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delBatchFinalized(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchFinalized")]
    public event delBatchFinalized? OnBatchFinalized;

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
    [DisplayName("isBatchDAAvailable")]
    public abstract bool? IsBatchDAAvailable(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isChainActive")]
    public abstract bool? IsChainActive(BigInteger? chainId);

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
    public abstract void RegisterChain(byte[]? configBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerGenesisStateRoot")]
    public abstract void RegisterGenesisStateRoot(BigInteger? chainId, UInt256? genesisRoot);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("resumeChain")]
    public abstract void ResumeChain(BigInteger? chainId);

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
