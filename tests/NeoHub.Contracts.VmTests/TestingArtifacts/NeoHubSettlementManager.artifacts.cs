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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":554,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":612,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":748,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":806,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":924,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":982,""safe"":false},{""name"":""getMessageRouter"",""parameters"":[],""returntype"":""Hash160"",""offset"":1102,""safe"":true},{""name"":""setMessageRouter"",""parameters"":[{""name"":""messageRouter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1160,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1284,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6183,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7467,""safe"":false},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3910,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8256,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":8288,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":8412,""safe"":true},{""name"":""getFinalizedTxRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":8423,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":8436,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3239,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":8704,""safe"":false},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11268,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11286,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":11364,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":12086,""safe"":true},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8062,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":12766,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MessageRouterChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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EC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Fp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkiICQEFifVtSQFcBAXg0NTXv9P//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcBARWIcBRKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EVoIgJAStgmBkUQIgTbIUBXAAJ5eBE0A0BXAQMdiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFekoQLgQiCEoB/wAyBgH/AJFKaBVR0EV6GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeiCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV6ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJA2zBAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQNsoStgkCUrKACAoAzpAVwEBeDRANVDy//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXAQEViHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwMDAUwBiHAQcRByIm54as5KaGlqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqHLUkkWkcnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUUAHHhpSmBoNdABAAAAPHhYSmBoNcQBAAAAXHhYSmBoNbgBAAAAfHhYSmBoNawBAAABnAB4WEpgaDWfAQAAAbwAeFhKYGg1kgEAAAHcAHhYSmBoNYUBAAAQciJueWrOSmhYap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQWAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEUB/AB4WEpgaDXNAAAAEHIibnpqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFaNsoNwAAcmo3AADbMNsoStgkCUrKACAoAzoiAkBXAQQQcCOhAAAAentonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSnhYaJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaAAgtSVg////WAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEVANwAAQNsoQFcAAnkRwBUMEGdldFNlY3VyaXR5TGV2ZWx4QWJ9W1JKEAEAAbskAzoiAkBXAAJ5EcAVDAlnZXREQU1vZGV4QWJ9W1JKEAEAAbskAzoiAkBXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCYJDIM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HgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93D0VvDwAgtSV7////bm8NaTVxAgAAbm8OajVoAgAAbkqcShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXZFbny1Jdj8//8IaTUeBAAAdglqNRYEAAB3B3tu2yhK2CQJSsoAICgDOpckMQwsR2F0ZXdheSBjb25zdGl0dWVudCBjb21taXRtZW50IHJvb3QgbWlzbWF0Y2jgem8H2yhK2CQJSsoAICgDOpckKQwkR2F0ZXdheSBnbG9iYWwgbWVzc2FnZSByb290IG1pc21hdGNo4BB3CCPpAAAAbwhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfHKBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwlvCWg1RuH//3cKbwkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9oNQzi//93C28Lbwo1mvf//7cmEW8KNcn3//9vC1A1OvT//28ISpxKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdwhFbwh8tSUY////NSfc//93CG8IStkoJAZFCSIGygAUsyQFCSIHbwgQs6okHQwYbWVzc2FnZSByb3V0ZXIgbm90IHdpcmVk4H8Jfwh/B359fHt6eBnAHwwRcHVibGlzaEdsb2JhbFJvb3RvCEFifVtSIgJAVwIAHcQAcBBxIj0QiEpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWloyrUkwWgiAkBXAwN5ygAglyQiDB1HYXRld2F5IGxlYWYgbXVzdCBiZSAzMiBieXRlc+B5cHpxEHJpEZERlye/AAAAanjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934HhqzsoAIJckIwweR2F0ZXdheSBmcm9udGllciBpcyBpbmNvbXBsZXRl4Gh4as41lAAAAEpwRRCISnhqUdBFaRGpShAuBCIOSgP/////AAAAADIMA/////8AAAAAkUpxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRSNB////anjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934GhKeGpR0EVAVwICeMoAIJckBQkiB3nKACCXJCIMHUdhdGV3YXkgbm9kZSBtdXN0IGJlIDMyIGJ5dGVz4ABAiHAQcSJ4eGnOSmhpUdBFeWnOSmgAIGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkhmjbKDcAAHFpNwAA2zAiAkBXBAIQiHAQcRByIwQBAAB4as5za8oQlyYHI8IAAABrygAglyQgDBtHYXRld2F5IGZyb250aWVyIGlzIGNvcnJ1cHTgaMoQlyYPa0pwRWpKcUUjigAAAHkmRmlqtSZBaGg12P7//0pwRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRSK+aGs1mf7//0pwRWoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqeMq1Jf3+//9oygAglyQeDBlHYXRld2F5IGZyb250aWVyIGlzIGVtcHR54GgiAkBXAQJ4NZ/g//9weWh4NAUiAkBXBAN5eDVE4f//NXzV//9waAuXJgUJIjdo2zAQznFpE5gmBQkiKXl4NcLp//81WdX//3JqC5cmBQkiFGpK2CQJSsoAICgDOnNrepciAkBXCwV5eDX24P//NS7V//9waAuXJggJI7sCAABo2zAQznFpE5gmCAkjqgIAAHl4NW7p//81BdX//3JqC5cmCAkjkgIAAGpK2CQJSsoAICgDOnN7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3RsygBAtiQTDA5wcm9vZiB0b28gZGVlcOB62zB1fHYQdwcjKAIAAGxvB853CG8IygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdwluEZEQlyfWAAAAEHcKIkNtbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLoQdwoidW8IbwrOSm8JACBvCp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSSII9EAAAAQdwoiRG8IbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLkQdwoidG1vCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIlvCdsoNwAAdwpvCjcAANswSnVFbhGpSnZFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvB2zKtSXY/f//a23bKErYJAlKygAgKAM6lyICQFcIBHg1DOD//3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCAkjdgIAAHoLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB6cWnKAEC2JBMMDnByb29mIHRvbyBkZWVw4HnbMHJ7cxB0IxsCAABpbM51bcoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHZrEZEQlyfTAAAAEHcHIkJqbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzbW8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKI84AAAAQdwciQm1vB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNqbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIpu2yg3AAB3B28HNwAA2zBKckVrEalKc0VsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsacq1Jeb9//9oatsoStgkCUrKACAoAzqXIgJAVgFAFjiZbQ==").AsSerializable<Neo.SmartContract.NefFile>();

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
    public abstract UInt160? MessageRouter { [DisplayName("getMessageRouter")] get; [DisplayName("setMessageRouter")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? OptimisticChallenge { [DisplayName("getOptimisticChallenge")] get; [DisplayName("setOptimisticChallenge")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

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
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    #endregion
}
