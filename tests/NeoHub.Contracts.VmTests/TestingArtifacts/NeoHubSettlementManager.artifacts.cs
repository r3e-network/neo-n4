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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":669,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":727,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":868,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":926,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":1049,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1107,""safe"":false},{""name"":""getMessageRouter"",""parameters"":[],""returntype"":""Hash160"",""offset"":1232,""safe"":true},{""name"":""setMessageRouter"",""parameters"":[{""name"":""messageRouter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1290,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1419,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1562,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":1620,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":654,""safe"":true},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2059,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6242,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7062,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8381,""safe"":false},{""name"":""revertBatchViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9188,""safe"":false},{""name"":""buildRevertBatchAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":9785,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4678,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":11038,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11070,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11194,""safe"":true},{""name"":""getFinalizedTxRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11205,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":11218,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4007,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":11486,""safe"":false},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14049,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14067,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14145,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14867,""safe"":true},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8994,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":15547,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MessageRouterChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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DAAAdwtoajUfDQAAdwxvDG8LNTgNAABvCm8LNSYOAAAkQww+cHJvb2YgdHlwZSBpbmNvbXBhdGlibGUgd2l0aCBjaGFpbidzIGFkdmVydGlzZWQgc2VjdXJpdHkgbGV2ZWzgDAH92zA1E/f//0rYJh1FDBd2ZXJpZmllciByZWdpc3RyeSB1bnNldDpK2CQJSsoAFCgDOncNeBHAFQwQdmVyaWZ5Q29tbWl0bWVudG8NQWJ9W1J3Dm8OJCEMHHZlcmlmaWVyIHJlamVjdGVkIGNvbW1pdG1lbnTgAfwAeDWqBgAAdw9vDG8PaWg1og0AAG8KEpcmBRIiAxF3EBGIShBvENBtNc/8//94aWg1NQ4AADXC/P//AZwAeDVuBgAAdxFvEdswaWg1KQ4AADWn/P//bwoSlyZoNUT3//93Em8SStkoJAZFCSIGygAUsyQFCSIHbxIQs6okIwweb3B0aW1pc3RpYyBjaGFsbGVuZ2Ugbm90IHdpcmVk4Hg14Q0AAHcTbxNpaBPAHwwKb3BlbldpbmRvd28SQWJ9W1JFADx4NegFAAB3Em8SaWgTwAwOQmF0Y2hTdWJtaXR0ZWRBlQFvYUBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSIgJAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Fp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkiICQEFifVtSQFcBAXg0NTXv8f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcBARWIcBRKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EVoIgJAStgmBkUQIgTbIUBXAAJ5eBE0A0BXAQMdiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFekoQLgQiCEoB/wAyBgH/AJFKaBVR0EV6GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeiCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV6ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJA2zBAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQNsoStgkCUrKACAoAzpAVwMBeDXFAAAANU3v//9waAuYJhNoStgkCUrKACAoAzojqAAAAAwB/NswNSzv//9K2CYURQwOcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzpxeBHAFQwTZ2V0R2VuZXNpc1N0YXRlUm9vdGlBYn1bUnJqDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJC8MKmNoYWluIGdlbmVzaXMgc3RhdGUgcm9vdCBpcyBub3QgcmVnaXN0ZXJlZOBqIgJAVwEBFYhwE0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcDAwFMAYhwEHEQciJueGrOSmhpap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFahy1JJFpHJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFABx4aUpgaDXQAQAAADx4WEpgaDXEAQAAAFx4WEpgaDW4AQAAAHx4WEpgaDWsAQAAAZwAeFhKYGg1nwEAAAG8AHhYSmBoNZIBAAAB3AB4WEpgaDWFAQAAEHIibnlqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFAfwAeFhKYGg1zQAAABByIm56as5KaFhqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqACC1JJBYACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pgRWjbKDcAAHJqNwAA2zDbKErYJAlKygAgKAM6IgJAVwEEEHAjoQAAAHp7aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkp4WGieSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9wRWgAILUlYP///1gAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFQDcAAEDbKEBXAAJ5EcAVDBBnZXRTZWN1cml0eUxldmVseEFifVtSShABAAG7JAM6IgJAVwACeRHAFQwJZ2V0REFNb2RleEFifVtSShABAAG7JAM6IgJAVwACeBS2JFAMS3NlY3VyaXR5TGV2ZWwgbXVzdCBiZSAwLi40IChTaWRlY2hhaW4vU2V0dGxlZC9PcHRpbWlzdGljL1ZhbGlkaXR5L1ZhbGlkaXVtKeB5E7YkMAwrZGFNb2RlIG11c3QgYmUgMC4uMyAoTDEvTmVvRlMvRXh0ZXJuYWwvREFDKeB4E5cmMHkQlyQrDCZWYWxpZGl0eSBzZWN1cml0eSBsZXZlbCByZXF1aXJlcyBMMSBEQeB4FJcmN3kQmCQy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qHTbMHFoygAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8UnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8YnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IchBzIj5oa85KamtR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VraMq1JMBoynMQdCJuaWzOSmprbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQawAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V4ShAuBCIISgH/ADIGAf8AkUpqa1HQRXgYqUoQLgQiCEoB/wAyBgH/AJFKamsRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV4IKlKEC4EIghKAf8AMgYB/wCRSmprEp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKamsTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFeUoQLgQiCEoB/wAyBgH/AJFKamtR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmprEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeSCpShAuBCIISgH/ADIGAf8AkUpqaxKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmprE55KAgAAAIAuBCIKSgL/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DAAgbw+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85Kbw5vD1HQRW8PSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdw9Fbw8AILUle////25vDWk1cAIAAG5vDmo1ZwIAAG5KnEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF2RW58tSXY/P//CGk1HQQAAHYJajUVBAAAdwd7btsoStgkCUrKACAoAzqXJDEMLEdhdGV3YXkgY29uc3RpdHVlbnQgY29tbWl0bWVudCByb290IG1pc21hdGNo4HpvB9soStgkCUrKACAoAzqXJCkMJEdhdGV3YXkgZ2xvYmFsIG1lc3NhZ2Ugcm9vdCBtaXNtYXRjaOAQdwgj6AAAAG8ISgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxygSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cJbwloNWjZ//93Cm8JFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfaDUu2v//dwtvC28KNWDw//+3JhBvC28KNY3w//818Oz//28ISpxKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdwhFbwh8tSUZ////NczR//93CG8IStkoJAZFCSIGygAUsyQFCSIHbwgQs6okHQwYbWVzc2FnZSByb3V0ZXIgbm90IHdpcmVk4H8Jfwh/B359fHt6eBnAHwwRcHVibGlzaEdsb2JhbFJvb3RvCEFifVtSIgJAVwIAHcQAcBBxIj0QiEpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWloyrUkwWgiAkBXAwN5ygAglyQiDB1HYXRld2F5IGxlYWYgbXVzdCBiZSAzMiBieXRlc+B5cHpxEHJpEZERlye/AAAAanjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934HhqzsoAIJckIwweR2F0ZXdheSBmcm9udGllciBpcyBpbmNvbXBsZXRl4Gh4as41lAAAAEpwRRCISnhqUdBFaRGpShAuBCIOSgP/////AAAAADIMA/////8AAAAAkUpxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRSNB////anjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934GhKeGpR0EVAVwICeMoAIJckBQkiB3nKACCXJCIMHUdhdGV3YXkgbm9kZSBtdXN0IGJlIDMyIGJ5dGVz4ABAiHAQcSJ4eGnOSmhpUdBFeWnOSmgAIGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkhmjbKDcAAHFpNwAA2zAiAkBXBAIQiHAQcRByIwQBAAB4as5za8oQlyYHI8IAAABrygAglyQgDBtHYXRld2F5IGZyb250aWVyIGlzIGNvcnJ1cHTgaMoQlyYPa0pwRWpKcUUjigAAAHkmRmlqtSZBaGg12P7//0pwRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRSK+aGs1mf7//0pwRWoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqeMq1Jf3+//9oygAglyQeDBlHYXRld2F5IGZyb250aWVyIGlzIGVtcHR54GgiAkBXAQJ4NcLY//9weWh4NAUiAkBXBAN5eDVn2f//NZ/K//9waAuXJgUJIjdo2zAQznFpE5gmBQkiKXl4NVfi//81fMr//3JqC5cmBQkiFGpK2CQJSsoAICgDOnNrepciAkBXCwV5eDUZ2f//NVHK//9waAuXJggJI7sCAABo2zAQznFpE5gmCAkjqgIAAHl4NQPi//81KMr//3JqC5cmCAkjkgIAAGpK2CQJSsoAICgDOnN7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3RsygBAtiQTDA5wcm9vZiB0b28gZGVlcOB62zB1fHYQdwcjKAIAAGxvB853CG8IygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdwluEZEQlyfWAAAAEHcKIkNtbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLoQdwoidW8IbwrOSm8JACBvCp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSSII9EAAAAQdwoiRG8IbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLkQdwoidG1vCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIlvCdsoNwAAdwpvCjcAANswSnVFbhGpSnZFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvB2zKtSXY/f//a23bKErYJAlKygAgKAM6lyICQFcIBHg1L9j//3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCAkjdgIAAHoLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB6cWnKAEC2JBMMDnByb29mIHRvbyBkZWVw4HnbMHJ7cxB0IxsCAABpbM51bcoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHZrEZEQlyfTAAAAEHcHIkJqbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzbW8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKI84AAAAQdwciQm1vB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNqbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIpu2yg3AAB3B28HNwAA2zBKckVrEalKc0VsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsacq1Jeb9//9oatsoStgkCUrKACAoAzqXIgJAVgIMFG5lbzQtZ292OnJldmVydEJhdGNo2zBhQHCCG5s=").AsSerializable<Neo.SmartContract.NefFile>();

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
    [DisplayName("isProofTypeCompatible")]
    public abstract bool? IsProofTypeCompatible(BigInteger? securityLevel, BigInteger? proofType);

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
