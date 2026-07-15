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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":669,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":727,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":868,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":926,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":1049,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1107,""safe"":false},{""name"":""getMessageRouter"",""parameters"":[],""returntype"":""Hash160"",""offset"":1232,""safe"":true},{""name"":""setMessageRouter"",""parameters"":[{""name"":""messageRouter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1290,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1419,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1562,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":1620,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":654,""safe"":true},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2059,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7064,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8343,""safe"":false},{""name"":""revertBatchViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9152,""safe"":false},{""name"":""buildRevertBatchAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":9749,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4680,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":11002,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11034,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11158,""safe"":true},{""name"":""getFinalizedTxRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11169,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":11182,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4009,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":11450,""safe"":false},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14014,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14032,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14110,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14832,""safe"":true},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8958,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":15512,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MessageRouterChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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DAAAdwtoajUhDQAAdwxvDG8LNToNAABvCm8LNSgOAAAkQww+cHJvb2YgdHlwZSBpbmNvbXBhdGlibGUgd2l0aCBjaGFpbidzIGFkdmVydGlzZWQgc2VjdXJpdHkgbGV2ZWzgDAH92zA1E/f//0rYJh1FDBd2ZXJpZmllciByZWdpc3RyeSB1bnNldDpK2CQJSsoAFCgDOncNeBHAFQwQdmVyaWZ5Q29tbWl0bWVudG8NQWJ9W1J3Dm8OJCEMHHZlcmlmaWVyIHJlamVjdGVkIGNvbW1pdG1lbnTgAfwAeDWsBgAAdw9vDG8PaWg1pA0AAG8KEpcmBRIiAxF3EBGIShBvENBtNc/8//9paDU4DgAAeFA1wfz//wGcAHg1bwYAAHcRaWg1Kw4AAG8R2zBQNaX8//9vChKXJmg1Qvf//3cSbxJK2SgkBkUJIgbKABSzJAUJIgdvEhCzqiQjDB5vcHRpbWlzdGljIGNoYWxsZW5nZSBub3Qgd2lyZWTgeDXhDQAAdxNvE2loE8AfDApvcGVuV2luZG93bxJBYn1bUkUAPHg16AUAAHcSbxJpaBPADA5CYXRjaFN1Ym1pdHRlZEGVAW9hQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkVnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkWnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkXnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSIgJAQWJ9W1JAVwEBeDQ1Ne3x//9waAuXJgUQIiRoStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6IgJAVwEBFYhwFEpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBK2CYGRRAiBNshQFcAAnl4ETQDQFcBAx2IcHhKaBBR0EV5ShAuBCIISgH/ADIGAf8AkUpoEVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV6ShAuBCIISgH/ADIGAf8AkUpoFVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV6ACCpShAuBCIISgH/ADIGAf8AkUpoGVHQRXoAKKlKEC4EIghKAf8AMgYB/wCRSmgaUdBFegAwqUoQLgQiCEoB/wAyBgH/AJFKaBtR0EV6ADipShAuBCIISgH/ADIGAf8AkUpoHFHQRWgiAkDbMEBXAgIAIIhwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkkGjbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBXAwF4NcUAAAA1S+///3BoC5gmE2hK2CQJSsoAICgDOiOoAAAADAH82zA1Ku///0rYJhRFDA5yZWdpc3RyeSB1bnNldDpK2CQJSsoAFCgDOnF4EcAVDBNnZXRHZW5lc2lzU3RhdGVSb290aUFifVtScmoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okLwwqY2hhaW4gZ2VuZXNpcyBzdGF0ZSByb290IGlzIG5vdCByZWdpc3RlcmVk4GoiAkBXAQEViHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwMDAUwBiHAQcRByIm54as5KaGlqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqHLUkkWkcnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUUAHHhpSmBoNdABAAAAPHhYSmBoNcQBAAAAXHhYSmBoNbgBAAAAfHhYSmBoNawBAAABnAB4WEpgaDWfAQAAAbwAeFhKYGg1kgEAAAHcAHhYSmBoNYUBAAAQciJueWrOSmhYap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQWAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEUB/AB4WEpgaDXNAAAAEHIibnpqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFaNsoNwAAcmo3AADbMNsoStgkCUrKACAoAzoiAkBXAQQQcCOhAAAAentonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSnhYaJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaAAgtSVg////WAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEVANwAAQNsoQFcAAnkRwBUMEGdldFNlY3VyaXR5TGV2ZWx4QWJ9W1JKEAEAAbskAzoiAkBXAAJ5EcAVDAlnZXREQU1vZGV4QWJ9W1JKEAEAAbskAzoiAkBXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCY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QSB2YWxpZGF0b3Igbm90IHdpcmVk4Ht6eXgUwBUMCHZhbGlkYXRlaEFifVtScWkkJQwgREEgdmFsaWRhdG9yIHJlamVjdGVkIGNvbW1pdG1lbnTgQFcAAng1UPD//3lQNANAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcEA3rbKDcAAHBoNwAA2zBxAECIchBzI60AAABpa85KamtR0EV6AdwAa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpqACBrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrACC1JVT///95eDQKalA1cOf//0BXAAJ5eBk12e///0BXAwI19OH//6okBQkiDDXA4P//Qfgn7IxwNe3h//9xaUrZKCQGRQkiBsoAFLMkBQkiBmkQs6okBQkiCGlB+CfsjHJoJgUIIgNqJBMMDm5vdCBhdXRob3JpemVk4GokBQkiBGiqeXg0A0BXBAN5eDVc7///NZLg//9waAuYJBIMDWJhdGNoIHVua25vd27gaNswEM5xaRSYJBsMFmJhdGNoIGFscmVhZHkgcmV2ZXJ0ZWTgeiZDaRKXJD4MOU9wdGltaXN0aWNDaGFsbGVuZ2UgY2FuIG9ubHkgcmV2ZXJ0IGNoYWxsZW5nZWFibGUgYmF0Y2hlc+BpE5cnQwEAAHl4NRLu//+XJDQML29ubHkgdGhlIGxhdGVzdCBmaW5hbGl6ZWQgYmF0Y2ggY2FuIGJlIHJldmVydGVk4Hl4NSsBAAC3JC8MKkdhdGV3YXktcHVibGlzaGVkIGJhdGNoIGNhbm5vdCBiZSByZXZlcnRlZOB5EbcntgAAAHkRn0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACReDUl9///NVff//9yaguYJCIMHXByZXZpb3VzIGJhdGNoIGhlYWRlciBtaXNzaW5n4AA8atswNTfv//9zeDWV8P//a9swUDVw5f//eRGfShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJF4NdT8//8iFHg1T/D//zXkAAAAEHg1wPz//3l4NYzt//8MAQTbMFA1FeX//3l4EsAMDUJhdGNoUmV2ZXJ0ZWRBlQFvYUBXAQF4NDU1mN7//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAQEViHAaSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQFcAAXhBm/ZnzkEvWMXtQEEvWMXtQFcFAzXL3v//JEIMPWdvdmVybmFuY2Ugbm90IGxvY2tlZCDigJQgYm9vdHN0cmFwIG93bmVyIHBhdGggcmVtYWlucyBhY3RpdmXgNRDi//9waAwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCQMH2dvdmVybmFuY2UgY29udHJvbGxlciBub3Qgd2lyZWTgejXsAAAAcWk1SN3//wuXJB4MGXByb3Bvc2FsIGFscmVhZHkgY29uc3VtZWTgehHAFQwXaXNBcHByb3ZlZEFuZFRpbWVsb2NrZWRoQWJ9W1JyaiQnDCJwcm9wb3NhbCBub3QgYXBwcm92ZWQgKyB0aW1lbG9ja2Vk4Hl4NU4BAABza3oSwBUMFm1hdGNoZXNQcm9wb3NhbFBheWxvYWRoQWJ9W1J0bCQzDC5wcm9wb3NhbCBwYXlsb2FkIGRvZXMgbm90IG1hdGNoIGJhdGNoIHJvbGxiYWNr4AwBAdswaTXK4v//CXl4Nc37//9AVwEBGYhwHkpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXgAIKlKEC4EIghKAf8AMgYB/wCRSmgVUdBFeAAoqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV4ADCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXgAOKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFaCICQFcFAllwQdv+qHTbMHFoygAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8UnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8YnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IchBzIj5oa85KamtR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VraMq1JMBoynMQdCJuaWzOSmprbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQawAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V4ShAuBCIISgH/ADIGAf8AkUpqa1HQRXgYqUoQLgQiCEoB/wAyBgH/AJFKamsRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV4IKlKEC4EIghKAf8AMgYB/wCRSmprEp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKamsTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFeUoQLgQiCEoB/wAyBgH/AJFKamtR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmprEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeSCpShAuBCIISgH/ADIGAf8AkUpqaxKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmprE55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBF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////fzIMAwAAAAABAAAAn85Kbw5vD1HQRW8PSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdw9Fbw8AILUle////25vDWk1cQIAAG5vDmo1aAIAAG5KnEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF2RW58tSXY/P//CGk1HgQAAHYJajUWBAAAdwd7btsoStgkCUrKACAoAzqXJDEMLEdhdGV3YXkgY29uc3RpdHVlbnQgY29tbWl0bWVudCByb290IG1pc21hdGNo4HpvB9soStgkCUrKACAoAzqXJCkMJEdhdGV3YXkgZ2xvYmFsIG1lc3NhZ2Ugcm9vdCBtaXNtYXRjaOAQdwgj6QAAAG8ISgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxygSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cJbwloNY7Z//93Cm8JFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfaDVU2v//dwtvC28KNWDw//+3JhFvCjWP8P//bwtQNezs//9vCEqcShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXcIRW8IfLUlGP///zXv0f//dwhvCErZKCQGRQkiBsoAFLMkBQkiB28IELOqJB0MGG1lc3NhZ2Ugcm91dGVyIG5vdCB3aXJlZOB/CX8Ifwd+fXx7engZwB8MEXB1Ymxpc2hHbG9iYWxSb290bwhBYn1bUiICQFcCAB3EAHAQcSI9EIhKaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpaMq1JMFoIgJAVwMDecoAIJckIgwdR2F0ZXdheSBsZWFmIG11c3QgYmUgMzIgYnl0ZXPgeXB6cRByaRGREZcnvwAAAGp4yrUkHgwZR2F0ZXdheSBmcm9udGllciBvdmVyZmxvd+B4as7KACCXJCMMHkdhdGV3YXkgZnJvbnRpZXIgaXMgaW5jb21wbGV0ZeBoeGrONZQAAABKcEUQiEp4alHQRWkRqUoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFKcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckUjQf///2p4yrUkHgwZR2F0ZXdheSBmcm9udGllciBvdmVyZmxvd+BoSnhqUdBFQFcCAnjKACCXJAUJIgd5ygAglyQiDB1HYXRld2F5IG5vZGUgbXVzdCBiZSAzMiBieXRlc+AAQIhwEHEieHhpzkpoaVHQRXlpzkpoACBpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JIZo2yg3AABxaTcAANswIgJAVwQCEIhwEHEQciMEAQAAeGrOc2vKEJcmByPCAAAAa8oAIJckIAwbR2F0ZXdheSBmcm9udGllciBpcyBjb3JydXB04GjKEJcmD2tKcEVqSnFFI4oAAAB5JkZparUmQWhoNdj+//9KcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUUivmhrNZn+//9KcEVqEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanjKtSX9/v//aMoAIJckHgwZR2F0ZXdheSBmcm9udGllciBpcyBlbXB0eeBoIgJAVwECeDXn2P//cHloeDQFIgJAVwQDeXg1jNn//zXCyv//cGgLlyYFCSI3aNswEM5xaROYJgUJIil5eDV54v//NZ/K//9yaguXJgUJIhRqStgkCUrKACAoAzpza3qXIgJAVwsFeXg1Ptn//zV0yv//cGgLlyYICSO7AgAAaNswEM5xaROYJggJI6oCAAB5eDUl4v//NUvK//9yaguXJggJI5ICAABqStgkCUrKACAoAzpzewuYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4Ht0bMoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgetswdXx2EHcHIygCAABsbwfOdwhvCMoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHcJbhGREJcn1gAAABB3CiJDbW8KzkpvCW8KUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSS6EHcKInVvCG8KzkpvCQAgbwqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkiCPRAAAAEHcKIkRvCG8KzkpvCW8KUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSS5EHcKInRtbwrOSm8JACBvCp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSSJbwnbKDcAAHcKbwo3AADbMEp1RW4RqUp2RW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwdsyrUl2P3//2tt2yhK2CQJSsoAICgDOpciAkBXCAR4NVTY//9waAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXJggJI3YCAAB6C5gkFgwRc2libGluZ3MgcmVxdWlyZWTgenFpygBAtiQTDA5wcm9vZiB0b28gZGVlcOB52zBye3MQdCMbAgAAaWzOdW3KACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh2axGREJcn0wAAABB3ByJCam8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic21vB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkiiPOAAAAEHcHIkJtbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzam8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKbtsoNwAAdwdvBzcAANswSnJFaxGpSnNFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbGnKtSXm/f//aGrbKErYJAlKygAgKAM6lyICQFYCDBRuZW80LWdvdjpyZXZlcnRCYXRjaNswYUAlc4VS").AsSerializable<Neo.SmartContract.NefFile>();

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
