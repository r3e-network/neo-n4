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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":554,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":612,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":748,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":806,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":924,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":982,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1102,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5639,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6357,""safe"":false},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3733,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6915,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3062,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":6947,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":6965,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7043,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7765,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":8445,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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aWZ5Q29tbWl0bWVudG8JQWJ9W1J3Cm8KJCEMHHZlcmlmaWVyIHJlamVjdGVkIGNvbW1pdG1lbnTgAfwAeDUfBwAAdwtoajUDDAAAdwxvDG8LaWg1GgwAAHgBPAHOdw1oEcAVDBBnZXRTZWN1cml0eUxldmVsakFifVtSShABAAG7JAM6dw5vDW8OuCQ3DDJwcm9vZiB0eXBlIGJlbG93IGNoYWluJ3MgYWR2ZXJ0aXNlZCBzZWN1cml0eSBsZXZlbOBvDRKXJgUSIgMRdw8RiEoQbw/QbTVLDAAAaWg1WgwAAHhQNT0MAAABnAB4NW8GAAB3EGloNU0MAABvENswUDUhDAAAbw0SlyZoNYL6//93EW8RStkoJAZFCSIGygAUsyQFCSIHbxEQs6okIwweb3B0aW1pc3RpYyBjaGFsbGVuZ2Ugbm90IHdpcmVk4Hg1AwwAAHcSbxJpaBPAHwwKb3BlbldpbmRvd28RQWJ9W1JFADx4NegFAAB3EW8RaWgTwAwOQmF0Y2hTdWJtaXR0ZWRBlQFvYUBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSIgJAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Fp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkiICQEFifVtSQFcBAXg0NTWg9f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcBARWIcBRKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EVoIgJAStgmBkUQIgTbIUBXAAJ5eBE0A0BXAQMdiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFekoQLgQiCEoB/wAyBgH/AJFKaBVR0EV6GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeiCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV6ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJA2zBAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQNsoStgkCUrKACAoAzpAVwEBeDRANQHz//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXAQEViHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwMDAUwBiHAQcRByIm54as5KaGlqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqHLUkkWkcnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUUAHHhpSmBoNdABAAAAPHhYSmBoNcQBAAAAXHhYSmBoNbgBAAAAfHhYSmBoNawBAAABnAB4WEpgaDWfAQAAAbwAeFhKYGg1kgEAAAHcAHhYSmBoNYUBAAAQciJueWrOSmhYap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQWAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEUB/AB4WEpgaDXNAAAAEHIibnpqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFaNsoNwAAcmo3AADbMNsoStgkCUrKACAoAzoiAkBXAQQQcCOhAAAAentonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSnhYaJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaAAgtSVg////WAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEVANwAAQNsoQFcAAnkRwBUMCWdldERBTW9kZXhBYn1bUkoQAQABuyQDOiICQFcBBHoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okIwweREEgY29tbWl0bWVudCBtdXN0IGJlIG5vbi16ZXJv4HsTtiQYDBNkYU1vZGUgbXVzdCBiZSAwLi4z4DV87///cGhK2SgkBkUJIgbKABSzJAUJIgZoELOqJBoMFURBIHJlZ2lzdHJ5IG5vdCB3aXJlZOB7enl4FMAfDAZyZWNvcmRoQWJ9W1JFQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAAJ5eBI13fj//0BXAAJ5eBU10fj//0DbMEBXAgF4ygFBAbgkJAwfY29tbWl0bWVudCBtaXNzaW5nIHByb29mIGxlbmd0aOABPQF4NRT0//9KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcGgAVbgkHwwab3B0aW1pc3RpYyBwcm9vZiB0b28gc21hbGzgaAIAABAAtiQfDBpvcHRpbWlzdGljIHByb29mIHRvbyBsYXJnZeABQQFonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ94ypckJQwgY29tbWl0bWVudCBwcm9vZiBsZW5ndGggbWlzbWF0Y2jgeAFBAc4SlyQpDCR1bnN1cHBvcnRlZCBvcHRpbWlzdGljIHByb29mIHZlcnNpb27gAX4BeDQ/cWlK2SgkBkUJIgbKABSzJAUJIgZpELOqJCEMHGludmFsaWQgb3B0aW1pc3RpYyBzZXF1ZW5jZXLgaSICQFcCAgAUiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAUtSSQaNsoStgkCUrKABQoAzoiAkDbKErYJAlKygAUKAM6QFcFAnl4NaL2//9waDWJ6///cWkLmCQSDA1iYXRjaCB1bmtub3du4GnbMBDOcmoRlyYFCCIFahKXJBoMFWJhdGNoIG5vdCBmaW5hbGl6YWJsZeBqEpcnkwAAADXJ6///c2tK2SgkBkUJIgbKABSzJAUJIgZrELOqJCMMHm9wdGltaXN0aWMgY2hhbGxlbmdlIG5vdCB3aXJlZOBrQfgn7IwkSAxDY2hhbGxlbmdlYWJsZSBiYXRjaCBmaW5hbGl6YXRpb24gbXVzdCBjb21lIGZyb20gT3B0aW1pc3RpY0NoYWxsZW5nZeB5eDXj/P//Nabq//9K2CYURQwOaGVhZGVyIG1pc3Npbmc62zBzeXg14PT//xGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGXJB0MGGZpbmFsaXplIG91dCBvZiBzZXF1ZW5jZeB5EbcmRQAcazWK9v//dGx4NR73//+XJDIMLXByZVN0YXRlUm9vdCBubyBsb25nZXIgbWF0Y2hlcyBjYW5vbmljYWwgaGVhZOBreXg0RwA8azVC9v//dAwBA9swaDX7+///eDUQ9///bNswUDXs+///eXg12AAAAGx5eBPADA5CYXRjaEZpbmFsaXplZEGVAW9hQFcFAwwB/NswNZvp//9K2CYURQwOcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzpweGg1vPr//3EB/AB6NcX1//9yNWLr//9za0rZKCQGRQkiBsoAFLMkBQkiBmsQs6okGwwWREEgdmFsaWRhdG9yIG5vdCB3aXJlZOBpanl4FMAVDAh2YWxpZGF0ZWtBYn1bUnRsJCUMIERBIHZhbGlkYXRvciByZWplY3RlZCBjb21taXRtZW504EBXAAJ4NXrz//95UDQDQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXBwI1jej//0H4J+yMcDVH6f//cWlK2SgkBkUJIgbKABSzJAUJIgZpELOqJAUJIghpQfgn7IxyaCYFCCIDaiQTDA5ub3QgYXV0aG9yaXplZOB5eDWG8///NW/o//9zawuYJBIMDWJhdGNoIHVua25vd27ga9swEM50bBSYJBsMFmJhdGNoIGFscmVhZHkgcmV2ZXJ0ZWTgaiQFCSIEaKomQ2wSlyQ+DDlPcHRpbWlzdGljQ2hhbGxlbmdlIGNhbiBvbmx5IHJldmVydCBjaGFsbGVuZ2VhYmxlIGJhdGNoZXPgbBOXJwkBAAB5eDU18v//lyQ0DC9vbmx5IHRoZSBsYXRlc3QgZmluYWxpemVkIGJhdGNoIGNhbiBiZSByZXZlcnRlZOB5EbcntgAAAHkRn0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACReDWh+f//NWTn//91bQuYJCIMHXByZXZpb3VzIGJhdGNoIGhlYWRlciBtaXNzaW5n4AA8bdswNZHz//92eDVq9P//btswUDVG+f//eRGfShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJF4NQT+//8iEXg1JPT//zQ0EHg18/3//3l4Nenx//8MAQTbMFA17vj//3l4EsAMDUJhdGNoUmV2ZXJ0ZWRBlQFvYUBXAAF4QZv2Z85BL1jF7UBBL1jF7UBXAQJ5eDWm8f//NY/m//9waAuXJgUQIgdo2zAQziICQFcBAng1z/D//3B5aHg0BSICQFcEA3l4NXTx//81Xeb//3BoC5cmBQkiN2jbMBDOcWkTmCYFCSIpeXg1g/j//zU65v//cmoLlyYFCSIUakrYJAlKygAgKAM6c2t6lyICQFcLBXl4NSbx//81D+b//3BoC5cmCAkjuwIAAGjbMBDOcWkTmCYICSOqAgAAeXg1L/j//zXm5f//cmoLlyYICSOSAgAAakrYJAlKygAgKAM6c3sLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7dGzKAEC2JBMMDnByb29mIHRvbyBkZWVw4HrbMHV8dhB3ByMoAgAAbG8HzncIbwjKACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh3CW4RkRCXJ9YAAAAQdwoiQ21vCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuhB3CiJ1bwhvCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIgj0QAAABB3CiJEbwhvCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuRB3CiJ0bW8KzkpvCQAgbwqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkiW8J2yg3AAB3Cm8KNwAA2zBKdUVuEalKdkVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HbMq1Jdj9//9rbdsoStgkCUrKACAoAzqXIgJAVwgEeDU88P//cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN2AgAAeguYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HpxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgedswcntzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2hq2yhK2CQJSsoAICgDOpciAkBWAUC7DF4m").AsSerializable<Neo.SmartContract.NefFile>();

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
    [DisplayName("revertBatch")]
    public abstract void RevertBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    #endregion
}
