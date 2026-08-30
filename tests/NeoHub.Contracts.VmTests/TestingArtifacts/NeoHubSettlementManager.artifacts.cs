using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSettlementManager(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":669,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":727,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":868,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":926,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":1049,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1107,""safe"":false},{""name"":""getMessageRouter"",""parameters"":[],""returntype"":""Hash160"",""offset"":1232,""safe"":true},{""name"":""setMessageRouter"",""parameters"":[{""name"":""messageRouter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1290,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1419,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1562,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":1620,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":654,""safe"":true},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2059,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7064,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8386,""safe"":false},{""name"":""revertBatchViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9195,""safe"":false},{""name"":""buildRevertBatchAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":9792,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4680,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":11045,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11077,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11201,""safe"":true},{""name"":""getFinalizedTxRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11212,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":11225,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4009,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":11493,""safe"":false},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14057,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14075,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14153,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14875,""safe"":true},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":9001,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":15555,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MessageRouterChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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DAAAdwtoajUhDQAAdwxvDG8LNToNAABvCm8LNSgOAAAkQww+cHJvb2YgdHlwZSBpbmNvbXBhdGlibGUgd2l0aCBjaGFpbidzIGFkdmVydGlzZWQgc2VjdXJpdHkgbGV2ZWzgDAH92zA1E/f//0rYJh1FDBd2ZXJpZmllciByZWdpc3RyeSB1bnNldDpK2CQJSsoAFCgDOncNeBHAFQwQdmVyaWZ5Q29tbWl0bWVudG8NQWJ9W1J3Dm8OJCEMHHZlcmlmaWVyIHJlamVjdGVkIGNvbW1pdG1lbnTgAfwAeDWsBgAAdw9vDG8PaWg1pA0AAG8KEpcmBRIiAxF3EBGIShBvENBtNc/8//9paDU4DgAAeFA1wfz//wGcAHg1bwYAAHcRaWg1Kw4AAG8R2zBQNaX8//9vChKXJmg1Qvf//3cSbxJK2SgkBkUJIgbKABSzJAUJIgdvEhCzqiQjDB5vcHRpbWlzdGljIGNoYWxsZW5nZSBub3Qgd2lyZWTgeDXhDQAAdxNvE2loE8AfDApvcGVuV2luZG93bxJBYn1bUkUAPHg16AUAAHcSbxJpaBPADA5CYXRjaFN1Ym1pdHRlZEGVAW9hQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkVnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkWnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkXnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSIgJAQWJ9W1JAVwEBeDQ1Ne3x//9waAuXJgUQIiRoStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6IgJAVwEBFYhwFEpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBK2CYGRRAiBNshQFcAAnl4ETQDQFcBAx2IcHhKaBBR0EV5ShAuBCIISgH/ADIGAf8AkUpoEVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV6ShAuBCIISgH/ADIGAf8AkUpoFVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV6ACCpShAuBCIISgH/ADIGAf8AkUpoGVHQRXoAKKlKEC4EIghKAf8AMgYB/wCRSmgaUdBFegAwqUoQLgQiCEoB/wAyBgH/AJFKaBtR0EV6ADipShAuBCIISgH/ADIGAf8AkUpoHFHQRWgiAkDbMEBXAgIAIIhwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkkGjbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBXAwF4NcUAAAA1S+///3BoC5gmE2hK2CQJSsoAICgDOiOoAAAADAH82zA1Ku///0rYJhRFDA5yZWdpc3RyeSB1bnNldDpK2CQJSsoAFCgDOnF4EcAVDBNnZXRHZW5lc2lzU3RhdGVSb290aUFifVtScmoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okLwwqY2hhaW4gZ2VuZXNpcyBzdGF0ZSByb290IGlzIG5vdCByZWdpc3RlcmVk4GoiAkBXAQEViHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwMDAUwBiHAQcRByIm54as5KaGlqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqHLUkkWkcnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUUAHHhpSmBoNdABAAAAPHhYSmBoNcQBAAAAXHhYSmBoNbgBAAAAfHhYSmBoNawBAAABnAB4WEpgaDWfAQAAAbwAeFhKYGg1kgEAAAHcAHhYSmBoNYUBAAAQciJueWrOSmhYap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQWAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEUB/AB4WEpgaDXNAAAAEHIibnpqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFaNsoNwAAcmo3AADbMNsoStgkCUrKACAoAzoiAkBXAQQQcCOhAAAAentonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSnhYaJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaAAgtSVg////WAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEVANwAAQNsoQFcAAnkRwBUMEGdldFNlY3VyaXR5TGV2ZWx4QWJ9W1JKEAEAAbskAzoiAkBXAAJ5EcAVDAlnZXREQU1vZGV4QWJ9W1JKEAEAAbskAzoiAkBXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCY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BQJZcEHb/qh02zBxaMoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfGJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHIQcyI+aGvOSmprUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa2jKtSTAaMpzEHQibmlszkpqa2yeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkGsAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFeEoQLgQiCEoB/wAyBgH/AJFKamtR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmprEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeCCpShAuBCIISgH/ADIGAf8AkUpqaxKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmprE55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pzRXlKEC4EIghKAf8AMgYB/wCRSmprUdBFeRipShAuBCIISgH/ADIGAf8AkUpqaxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXkgqUoQLgQiCEoB/wAyBgH/AJFKamsSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV5ABipShAuBCIISgH/ADIGAf8AkUpqaxOeSgIAAACA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DW8PUdBFbwwAIG8PnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSm8Obw9R0EVvD0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cPRW8PACC1JXv///9ubw1pNXECAABubw5qNWgCAABuSpxKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdkVufLUl2Pz//whpNR4EAAB2CWo1FgQAAHcHe27bKErYJAlKygAgKAM6lyQxDCxHYXRld2F5IGNvbnN0aXR1ZW50IGNvbW1pdG1lbnQgcm9vdCBtaXNtYXRjaOB6bwfbKErYJAlKygAgKAM6lyQpDCRHYXRld2F5IGdsb2JhbCBtZXNzYWdlIHJvb3QgbWlzbWF0Y2jgEHcII+kAAABvCEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8coEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CW8JaDVj2f//dwpvCRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2g1Kdr//3cLbwtvCjVg8P//tyYRbwo1j/D//28LUDXs7P//bwhKnEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF3CEVvCHy1JRj///81xNH//3cIbwhK2SgkBkUJIgbKABSzJAUJIgdvCBCzqiQdDBhtZXNzYWdlIHJvdXRlciBub3Qgd2lyZWTgfwl/CH8Hfn18e3p4GcAfDBFwdWJsaXNoR2xvYmFsUm9vdG8IQWJ9W1IiAkBXAgAdxABwEHEiPRCISmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaWjKtSTBaCICQFcDA3nKACCXJCIMHUdhdGV3YXkgbGVhZiBtdXN0IGJlIDMyIGJ5dGVz4HlwenEQcmkRkRGXJ78AAABqeMq1JB4MGUdhdGV3YXkgZnJvbnRpZXIgb3ZlcmZsb3fgeGrOygAglyQjDB5HYXRld2F5IGZyb250aWVyIGlzIGluY29tcGxldGXgaHhqzjWUAAAASnBFEIhKeGpR0EVpEalKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFI0H///9qeMq1JB4MGUdhdGV3YXkgZnJvbnRpZXIgb3ZlcmZsb3fgaEp4alHQRUBXAgJ4ygAglyQFCSIHecoAIJckIgwdR2F0ZXdheSBub2RlIG11c3QgYmUgMzIgYnl0ZXPgAECIcBBxInh4ac5KaGlR0EV5ac5KaAAgaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSGaNsoNwAAcWk3AADbMCICQFcEAhCIcBBxEHIjBAEAAHhqznNryhCXJgcjwgAAAGvKACCXJCAMG0dhdGV3YXkgZnJvbnRpZXIgaXMgY29ycnVwdOBoyhCXJg9rSnBFakpxRSOKAAAAeSZGaWq1JkFoaDXY/v//SnBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFIr5oazWZ/v//SnBFahGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp4yrUl/f7//2jKACCXJB4MGUdhdGV3YXkgZnJvbnRpZXIgaXMgZW1wdHngaCICQFcBAng1vNj//3B5aHg0BSICQFcEA3l4NWHZ//81l8r//3BoC5cmBQkiN2jbMBDOcWkTmCYFCSIpeXg1TuL//zV0yv//cmoLlyYFCSIUakrYJAlKygAgKAM6c2t6lyICQFcLBXl4NRPZ//81Scr//3BoC5cmCAkjuwIAAGjbMBDOcWkTmCYICSOqAgAAeXg1+uH//zUgyv//cmoLlyYICSOSAgAAakrYJAlKygAgKAM6c3sLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7dGzKAEC2JBMMDnByb29mIHRvbyBkZWVw4HrbMHV8dhB3ByMoAgAAbG8HzncIbwjKACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh3CW4RkRCXJ9YAAAAQdwoiQ21vCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuhB3CiJ1bwhvCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIgj0QAAABB3CiJEbwhvCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuRB3CiJ0bW8KzkpvCQAgbwqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkiW8J2yg3AAB3Cm8KNwAA2zBKdUVuEalKdkVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HbMq1Jdj9//9rbdsoStgkCUrKACAoAzqXIgJAVwgEeDUp2P//cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN2AgAAeguYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HpxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgedswcntzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2hq2yhK2CQJSsoAICgDOpciAkBWAgwUbmVvNC1nb3Y6cmV2ZXJ0QmF0Y2jbMGFAfA2q6Q==").AsSerializable<Neo.SmartContract.NefFile>();

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

    public delegate void delDARegistryChanged(UInt160? obj);

    [DisplayName("DARegistryChanged")]
    public event delDARegistryChanged? OnDARegistryChanged;

    public delegate void delDAValidatorChanged(UInt160? obj);

    [DisplayName("DAValidatorChanged")]
    public event delDAValidatorChanged? OnDAValidatorChanged;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delMessageRouterChanged(UInt160? obj);

    [DisplayName("MessageRouterChanged")]
    public event delMessageRouterChanged? OnMessageRouterChanged;

    public delegate void delOptimisticChallengeChanged(UInt160? obj);

    [DisplayName("OptimisticChallengeChanged")]
    public event delOptimisticChallengeChanged? OnOptimisticChallengeChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? DARegistry { [DisplayName("getDARegistry")] get; [DisplayName("setDARegistry")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? DAValidator { [DisplayName("getDAValidator")] get; [DisplayName("setDAValidator")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? MessageRouter { [DisplayName("getMessageRouter")] get; [DisplayName("setMessageRouter")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? OptimisticChallenge { [DisplayName("getOptimisticChallenge")] get; [DisplayName("setOptimisticChallenge")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRevertBatchAction")]
    public abstract byte[]? BuildRevertBatchAction(BigInteger? chainId, BigInteger? batchNumber);

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
    [DisplayName("getChallengeableBatchHeader")]
    public abstract byte[]? GetChallengeableBatchHeader(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getFinalizedTxRoot")]
    public abstract UInt256? GetFinalizedTxRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGatewayFinalizedThrough")]
    public abstract BigInteger? GetGatewayFinalizedThrough(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL1MessageRoot")]
    public abstract UInt256? GetL2ToL1MessageRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL2MessageRoot")]
    public abstract UInt256? GetL2ToL2MessageRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLatestFinalizedBatch")]
    public abstract BigInteger? GetLatestFinalizedBatch(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyStateLeafWithProof")]
    public abstract bool? VerifyStateLeafWithProof(BigInteger? chainId, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);

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
    [DisplayName("publishGatewayGlobalRoot")]
    public abstract bool? PublishGatewayGlobalRoot(BigInteger? batchEpoch, byte[]? constituentReferences, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain, byte[]? aggregatedProof);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revertBatch")]
    public abstract void RevertBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revertBatchViaProposal")]
    public abstract void RevertBatchViaProposal(BigInteger? chainId, BigInteger? batchNumber, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    #endregion
}
