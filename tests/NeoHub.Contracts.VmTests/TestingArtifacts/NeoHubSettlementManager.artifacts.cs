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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""H" +
        @"ash160"",""offset"":554,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":612,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":748,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":806,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":924,""safe"":true},{""name"":""setDAValidator"",""parameters" +
        @""":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":982,""safe"":false},{""name"":""getMessageRouter"",""parameters"":[],""returntype"":""Hash160"",""offset"":1102,""safe"":true},{""name"":""setMessageRouter"",""parameters"":[{""name"":""messageRouter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1160,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""o" +
        @"ffset"":1284,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6181,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7462,""safe"":false},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3908,""safe"":true},{""name"":""getBatchStatus"",""p" +
        @"arameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8249,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":8281,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":8405,""safe"":true},{""name"":""getFinalizedTxRoot""" +
        @",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":8416,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":8429,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3237,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name""" +
        @":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":8697,""safe"":false},{""name"":""verif" +
        @"yWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11330,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11348,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""lea" +
        @"fHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":11426,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":12148,""safe"":true},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer""" +
        @",""offset"":8055,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":12828,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":" +
        @"""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MessageRouterChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{" +
        @"""Author"":""R3E Network"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscgZzaGEyNTYBAAEPAAD9HzJXBQJ5JgcjGgEAAHhwaBDOcWgRznJoEs5zaMoTtyYHaBPOIhgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAdGlK2SgkBkUJIgbKABSzJAUJIgZpELOqJBIMDWludmFsaWQgb3duZXLgakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okGwwWaW52YWxpZCBjaGFpbiByZWdpc3RyeeBrStkoJAZFCSIGygAUsyQFCSIGaxCzqiQeDBlpbnZhbGlkIHZlcmlmaWVyIHJlZ2lzdHJ54GkMAf/bMDR4agwB/NswNHBrDAH92zA0aGwQs6omOWxK2SgkBkUJIgbKABSzJCEMHGludmFsaWQgb3B0aW1pc3RpYyBjaGFsbGVuZ2XgbAwBBtswNCtADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBK2SgkBkUJIgbKABSzQBCzQFcAAnl4QZv2Z85B5j8Y" +
        @"hEBB5j8YhEBBm/ZnzkBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFAVwEBNLFB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQWDBFpbnZhbGlkIG5ldyBvd25lcuA1av///3B4DAH/2zA1Qv///3hoEsAMDE93bmVyQ2hhbmdlZEGVAW9hQEH4J+yMQFcBAAwBBtswNWr///9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAE1/v7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJCEMHGludmFsaWQgb3B0aW1pc3RpYyBjaGFsbGVuZ2XgeAwBBtswNYf+//94EcAMGk9wdGltaXN0aWNDaGFsbGVuZ2VDaGFuZ2VkQZUBb2FAVwEADAEH2zA1qP7//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK" +
        @"2CQJSsoAFCgDOiICQFcAATU8/v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okGAwTaW52YWxpZCBEQSByZWdpc3RyeeB4DAEH2zA1zv3//3gRwAwRREFSZWdpc3RyeUNoYW5nZWRBlQFvYUBXAQAMAQjbMDX4/f//cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABNYz9//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQZDBRpbnZhbGlkIERBIHZhbGlkYXRvcuB4DAEI2zA1Hf3//3gRwAwSREFWYWxpZGF0b3JDaGFuZ2VkQZUBb2FAVwEADAEL2zA1Rv3//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAATXa/P//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okGwwWaW52YWxpZCBtZXNzYWdlIHJvdXRlcuB4" +
        @"DAEL2zA1afz//3gRwAwUTWVzc2FnZVJvdXRlckNoYW5nZWRBlQFvYUBXEwN4ygFBAbgkGQwUY29tbWl0bWVudCB0b28gc21hbGzgeQuYJAUJIgd5ygAglyQjDB5sMU1lc3NhZ2VIYXNoIG11c3QgYmUgMzIgYnl0ZXPgeguYJAUJIgd6ygAglyQmDCFibG9ja0NvbnRleHRIYXNoIG11c3QgYmUgMzIgYnl0ZXPgEHg1WAMAAHAUeDVQBAAAcQwB/NswNf77//9K2CYURQwOcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzpyaBHAFQwIaXNBY3RpdmVqQWJ9W1JzayQTDA5jaGFpbiBpbmFjdGl2ZeBoNbYGAAB0aWwRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRlyQhDBxiYXRjaCBudW1iZXIgb3V0IG9mIHNlcXVlbmNl4GloNRQHAAB1bTVM+///dm4LlyYFCCIJbtswEM4UlyQcDBdiYXRjaCBhbHJlYWR5IHN1Ym1pdHRlZOBsELcmRAAceDUfCAAAdwdvB2g1sQgAAJckLwwq" +
        @"cHJlU3RhdGVSb290IGRvZXMgbm90IG1hdGNoIGNhbm9uaWNhbCBoZWFk4Hp5eDVUCQAAdwcBHAF4NdIHAAB3CG8HbwiXJDIMLXB1YmxpY0lucHV0SGFzaCBub3QgYm91bmQgdG8gY29tbWl0bWVudCByb290c+B4ATwBzncJaGo1eAwAAHcKaGo1mgwAAHcLbwtvCjWzDAAAbwlvCjWhDQAAJEMMPnByb29mIHR5cGUgaW5jb21wYXRpYmxlIHdpdGggY2hhaW4ncyBhZHZlcnRpc2VkIHNlY3VyaXR5IGxldmVs4AwB/dswNRX6//9K2CYdRQwXdmVyaWZpZXIgcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzp3DHgRwBUMEHZlcmlmeUNvbW1pdG1lbnRvDEFifVtSdw1vDSQhDBx2ZXJpZmllciByZWplY3RlZCBjb21taXRtZW504AH8AHg1qgYAAHcObwtvDmloNR0NAABvCRKXJgUSIgMRdw8RiEoQbw/QbTW4DQAAeGloNcYNAAA1qw0AAAGcAHg1bgYAAHcQbxDbMGloNboNAAA1kA0AAG8JEpcmaDXT+f//dxFv" +
        @"EUrZKCQGRQkiBsoAFLMkBQkiB28RELOqJCMMHm9wdGltaXN0aWMgY2hhbGxlbmdlIG5vdCB3aXJlZOB4NXINAAB3Em8SaWgTwB8MCm9wZW5XaW5kb3dvEUFifVtSRQA8eDXoBQAAdxFvEWloE8AMDkJhdGNoU3VibWl0dGVkQZUBb2FAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkiICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAu" +
        @"BCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRWeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AKKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRaeSgIAAACALgQi" +
        @"CkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AMKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eReeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AOKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZIiAkBBYn1bUkBXAQF4NDU18fT//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAQEViHAUSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQErYJgZFECIE2yFAVwACeXgRNANAVwEDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkg" +
        @"qUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXpKEC4EIghKAf8AMgYB/wCRSmgVUdBFehipShAuBCIISgH/ADIGAf8AkUpoFlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV6ADCpShAuBCIISgH/ADIGAf8AkUpoG1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmgcUdBFaCICQNswQFcCAgAgiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQaNsoStgkCUrKACAoAzoiAkDbKErYJAlKygAgKAM6QFcBAXg0QDVS8v//cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAA" +
        @"AAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwEBFYhwE0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcDAwFMAYhwEHEQciJueGrOSmhpap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFahy1JJFpHJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFABx4aUpgaDXQAQAAADx4WEpgaDXEAQAAAFx4WEpgaDW4AQAAAHx4WEpgaDWsAQAAAZwAeFhKYGg1nwEAAAG8AHhYSmBoNZIBAAAB3AB4WEpgaDWFAQAA" +
        @"EHIibnlqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFAfwAeFhKYGg1zQAAABByIm56as5KaFhqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqACC1JJBYACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pgRWjbKDcAAHJqNwAA2zDbKErYJAlKygAgKAM6IgJAVwEEEHAjoQAAAHp7aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkp4WGie" +
        @"SgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9wRWgAILUlYP///1gAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFQDcAAEDbKEBXAAJ5EcAVDBBnZXRTZWN1cml0eUxldmVseEFifVtSShABAAG7JAM6IgJAVwACeRHAFQwJZ2V0REFNb2RleEFifVtSShABAAG7JAM6IgJAVwACeBS2JFAMS3NlY3VyaXR5TGV2ZWwgbXVzdCBiZSAwLi40IChTaWRlY2hhaW4vU2V0dGxlZC9PcHRpbWlzdGljL1ZhbGlkaXR5L1ZhbGlkaXVtKeB5E7YkMAwrZGFNb2RlIG11c3QgYmUgMC4uMyAoTDEvTmVvRlMvRXh0ZXJuYWwvREFDKeB4E5cmMHkQlyQrDCZWYWxpZGl0eSBzZWN1cml0eSBsZXZlbCByZXF1aXJlcyBMMSBEQeB4FJcmN3kQmCQyDC1W" +
        @"YWxpZGl1bSBzZWN1cml0eSBsZXZlbCByZXF1aXJlcyBvZmYtY2hhaW4gREHgQFcAAngQlyYFCCIFeBGXJhd5EZcmBQgiBXkSlyYFCCIFeROXIil4EpcmD3kSlyYFCCIFeROXIhd4E5cmBQgiBXgUlyYHeROXIgUJIgJAVwEEegwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXqiQjDB5EQSBjb21taXRtZW50IG11c3QgYmUgbm9uLXplcm/gexO2JBgME2RhTW9kZSBtdXN0IGJlIDAuLjPgNV7t//9waErZKCQGRQkiBsoAFLMkBQkiBmgQs6okGgwVREEgcmVnaXN0cnkgbm90IHdpcmVk4Ht6eXgUwB8MBnJlY29yZGhBYn1bUkVAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcAAnl4EjVu9///QNswQFcAAnl4FTVf9///QFcCAXjKAUEBuCQkDB9jb21taXRtZW50IG1pc3NpbmcgcHJvb2YgbGVuZ3Ro4AE9AXg1pfL//0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAA" +
        @"AJ9waABVuCQfDBpvcHRpbWlzdGljIHByb29mIHRvbyBzbWFsbOBoAgAAEAC2JB8MGm9wdGltaXN0aWMgcHJvb2YgdG9vIGxhcmdl4AFBAWieSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3jKlyQlDCBjb21taXRtZW50IHByb29mIGxlbmd0aCBtaXNtYXRjaOB4AUEBzhKXJCkMJHVuc3VwcG9ydGVkIG9wdGltaXN0aWMgcHJvb2YgdmVyc2lvbuABfgF4ND9xaUrZKCQGRQkiBsoAFLMkBQkiBmkQs6okIQwcaW52YWxpZCBvcHRpbWlzdGljIHNlcXVlbmNlcuBpIgJAVwICABSIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpABS1JJBo2yhK2CQJSsoAFCgDOiICQNsoStgkCUrKABQoAzpAVwoCeXg1M/X//3Bo" +
        @"NWvp//9xaQuYJBIMDWJhdGNoIHVua25vd27gadswEM5yahGXJgUIIgVqEpckGgwVYmF0Y2ggbm90IGZpbmFsaXphYmxl4GoSlyeTAAAANavp//9za0rZKCQGRQkiBsoAFLMkBQkiBmsQs6okIwweb3B0aW1pc3RpYyBjaGFsbGVuZ2Ugbm90IHdpcmVk4GtB+CfsjCRIDENjaGFsbGVuZ2VhYmxlIGJhdGNoIGZpbmFsaXphdGlvbiBtdXN0IGNvbWUgZnJvbSBPcHRpbWlzdGljQ2hhbGxlbmdl4Hl4NeP8//81iOj//0rYJhRFDA5oZWFkZXIgbWlzc2luZzrbMHMMAfzbMDVl6P//StgmFEUMDnJlZ2lzdHJ5IHVuc2V0OkrYJAlKygAUKAM6dHhsNTX6//91eGw1WPr//3ZubTV0+v//awE8Ac5tNWD7//8kPgw5cHJvb2YgdHlwZSBpbmNvbXBhdGlibGUgd2l0aCBjdXJyZW50IGNoYWluIHNlY3VyaXR5IGxldmVs4Hl4NeXy//8RnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACR" +
        @"lyQdDBhmaW5hbGl6ZSBvdXQgb2Ygc2VxdWVuY2XgeRG3JkcAHGs1j/T//3cHbwd4NSH1//+XJDIMLXByZVN0YXRlUm9vdCBubyBsb25nZXIgbWF0Y2hlcyBjYW5vbmljYWwgaGVhZOAB/ABrNUn0//93B28HeXg0ZncIbwhtNXP5//9vCG8HeXg1KQEAAAA8azUk9P//dwkMAQPbMGg1S/v//28J2zB4Ne30//81PPv//3l4NXgBAABreXg1kwEAAG8JeXgTwAwOQmF0Y2hGaW5hbGl6ZWRBlQFvYUBXAwM1Gej//3BoStkoJAZFCSIGygAUsyQFCSIGaBCzqiQaDBVEQSByZWdpc3RyeSBub3Qgd2lyZWTgeXgSwBUMDWdldENvbW1pdG1lbnRoQWJ9W1JxaXqXJDcMMkRBIHJlZ2lzdHJ5IGNvbW1pdG1lbnQgZG9lcyBub3QgbWF0Y2ggYmF0Y2ggaGVhZGVy4Hl4EsAVDAdnZXRNb2RlaEFifVtSShABAAG7JAM6cmoTtiQhDBxyZWNvcmRlZCBkYU1vZGUgbXVzdCBiZSAwLi4z4GoiAkBXAgQ19Of//3BoStkoJAZF" +
        @"CSIGygAUsyQFCSIGaBCzqiQbDBZEQSB2YWxpZGF0b3Igbm90IHdpcmVk4Ht6eXgUwBUMCHZhbGlkYXRlaEFifVtScWkkJQwgREEgdmFsaWRhdG9yIHJlamVjdGVkIGNvbW1pdG1lbnTgQFcAAnl4Nbrw//80A0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwQDetsoNwAAcGg3AADbMHEAQIhyEHMjrQAAAGlrzkpqa1HQRXoB3ABrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmoAIGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAILUlVP///2p5eDQINcj4//9AVwACeXgZNUbw//9AVwcCNTzk//9B+CfsjHA19uT//3FpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQFCSIIaUH4J+yMcmgmBQgiA2okEwwObm90IGF1dGhvcml6ZWTg" +
        @"eXg15O///zUe5P//c2sLmCQSDA1iYXRjaCB1bmtub3du4GvbMBDOdGwUmCQbDBZiYXRjaCBhbHJlYWR5IHJldmVydGVk4GokBQkiBGiqJkNsEpckPgw5T3B0aW1pc3RpY0NoYWxsZW5nZSBjYW4gb25seSByZXZlcnQgY2hhbGxlbmdlYWJsZSBiYXRjaGVz4GwTlydCAQAAeXg1k+7//5ckNAwvb25seSB0aGUgbGF0ZXN0IGZpbmFsaXplZCBiYXRjaCBjYW4gYmUgcmV2ZXJ0ZWTgeXg1KQEAALckLwwqR2F0ZXdheS1wdWJsaXNoZWQgYmF0Y2ggY2Fubm90IGJlIHJldmVydGVk4HkRtye1AAAAeRGfShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJF4NTf3//813OL//3VtC5gkIgwdcHJldmlvdXMgYmF0Y2ggaGVhZGVyIG1pc3NpbmfgADxt2zA1uO///3Zu2zB4NY7w//813fb//3kRn0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACReDXr/P//" +
        @"IhR4NUzw//814wAAABB4Ndf8//8MAQTbMHl4NQnu//81g/b//3l4EsAMDUJhdGNoUmV2ZXJ0ZWRBlQFvYUBXAQF4NDU1H+L//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAQEViHAaSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQFcAAXhBm/ZnzkEvWMXtQEEvWMXtQFcBAnl4NR/t//81WeH//3BoC5cmBRAiB2jbMBDOIgJAVwACAbwAeXg0A0BXAQN5eDTQE5gmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiQ3l4NV31//81AuH//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiC3po2zA12+3//yICQFcAAgHcAHl4NIdAVwACAFx5eDV9////QFcFAnl4NWvs//81peD/" +
        @"/3BoC5gkEgwNYmF0Y2ggdW5rbm93buBo2zAQzhKXJB8MGmJhdGNoIGlzIG5vdCBjaGFsbGVuZ2VhYmxl4Hl4Nbj0//81XeD//3FpC5gkGQwUYmF0Y2ggaGVhZGVyIG1pc3NpbmfgadswcmrKAUEBuCQbDBZiYXRjaCBoZWFkZXIgdHJ1bmNhdGVk4GoBPAHOEpckHAwXYmF0Y2ggaXMgbm90IG9wdGltaXN0aWPgAUEBiHMQdCI+amzOSmtsUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAFBAbUkv2siAkBXEAp5C5gkJAwfY29uc3RpdHVlbnQgcmVmZXJlbmNlcyByZXF1aXJlZOB5cHwQtyQFCSIHfAEAELYkJgwhY29uc3RpdHVlbnQgY291bnQgbXVzdCBiZSAxLi40MDk24GjKfEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8coEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+XJCoM" +
        @"JWNvbnN0aXR1ZW50IHJlZmVyZW5jZSBsZW5ndGggbWlzbWF0Y2jgegwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXqiQhDBxnbG9iYWwgcm9vdCBtdXN0IGJlIG5vbi16ZXJv4HsMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okJgwhY29uc3RpdHVlbnQgcm9vdCBtdXN0IGJlIG5vbi16ZXJv4DVZBQAAcTVTBQAAcgwB/NswNRbe//9K2CYURQwOcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzpzEHQQdRB2IyoDAABuSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxygSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHbwdoNcfk//93CG8HFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfaDWN5f//dwlvCBC3JCkMJEdhdGV3YXkgY2hhaW5JZCAwIGlzIHJlc2Vy" +
        @"dmVkIGZvciBMMeBuELcmVG8IbLcmBQgiD28IbJckBQkiBm8JbbckPAw3R2F0ZXdheSBjb25zdGl0dWVudCByZWZlcmVuY2VzIG11c3QgYmUgc3RyaWN0bHkgb3JkZXJlZOBvCEp0RW8JSnVFbwlvCDVK+///E5ckKQwkR2F0ZXdheSBjb25zdGl0dWVudCBpcyBub3QgZmluYWxpemVk4G8IEcAVDBFnZXRHYXRld2F5RW5hYmxlZGtBYn1bUncKbwokKwwmR2F0ZXdheSBkaXNhYmxlZCBmb3IgY29uc3RpdHVlbnQgY2hhaW7gbwlvCDUH+v//tyQuDClHYXRld2F5IGNvbnN0aXR1ZW50IHdhcyBhbHJlYWR5IHB1Ymxpc2hlZOBvCW8INXL3//8179v//3cLbwsLmCQlDCBHYXRld2F5IGZpbmFsaXplZCByZWNvcmQgbWlzc2luZ+BvC9swdwxvDMoAQJckJQwgR2F0ZXdheSBmaW5hbGl6ZWQgcmVjb3JkIGNvcnJ1cHTgACCIdw0AIIh3DhB3DyOFAAAAbwxvD85Kbw1vD1HQRW8MACBvD55KAgAAAIAuBCIKSgL/" +
        @"//9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpvDm8PUdBFbw9KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93D0VvDwAgtSV7////bm8NaTVwAgAAbm8OajVnAgAAbkqcShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXZFbny1Jdj8//8IaTUdBAAAdglqNRUEAAB3B3tu2yhK2CQJSsoAICgDOpckMQwsR2F0ZXdheSBjb25zdGl0dWVudCBjb21taXRtZW50IHJvb3QgbWlzbWF0Y2jgem8H2yhK2CQJSsoAICgDOpckKQwkR2F0ZXdheSBnbG9iYWwgbWVzc2FnZSByb290IG1pc21hdGNo4BB3CCPoAAAAbwhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfHKBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwlvCWg1BeH//3cKbwkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAA" +
        @"AJFKAv///38yDAMAAAAAAQAAAJ9oNcvh//93C28Lbwo1VPf//7cmEG8Lbwo1gff//zX48///bwhKnEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF3CEVvCHy1JRn///816dv//3cIbwhK2SgkBkUJIgbKABSzJAUJIgdvCBCzqiQdDBhtZXNzYWdlIHJvdXRlciBub3Qgd2lyZWTgfwl/CH8Hfn18e3p4GcAfDBFwdWJsaXNoR2xvYmFsUm9vdG8IQWJ9W1IiAkBXAgAdxABwEHEiPRCISmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaWjKtSTBaCICQFcDA3nKACCXJCIMHUdhdGV3YXkgbGVhZiBtdXN0IGJlIDMyIGJ5dGVz4HlwenEQcmkRkRGXJ78AAABqeMq1JB4MGUdhdGV3YXkgZnJvbnRpZXIgb3ZlcmZsb3fgeGrOygAglyQjDB5HYXRld2F5IGZyb250aWVyIGlzIGluY29tcGxldGXgaHhqzjWUAAAASnBFEIhKeGpR0EVpEalKEC4EIg5KA///" +
        @"//8AAAAAMgwD/////wAAAACRSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFI0H///9qeMq1JB4MGUdhdGV3YXkgZnJvbnRpZXIgb3ZlcmZsb3fgaEp4alHQRUBXAgJ4ygAglyQFCSIHecoAIJckIgwdR2F0ZXdheSBub2RlIG11c3QgYmUgMzIgYnl0ZXPgAECIcBBxInh4ac5KaGlR0EV5ac5KaAAgaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSGaNsoNwAAcWk3AADbMCICQFcEAhCIcBBxEHIjBAEAAHhqznNryhCXJgcjwgAAAGvKACCXJCAMG0dhdGV3YXkgZnJvbnRpZXIgaXMgY29ycnVwdOBoyhCXJg9rSnBFakpxRSOKAAAAeSZGaWq1JkFoaDXY/v//SnBFaUqcSgIAAACALgQiCkoC////fzIeA/////8A" +
        @"AAAAkUoC////fzIMAwAAAAABAAAAn3FFIr5oazWZ/v//SnBFahGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp4yrUl/f7//2jKACCXJB4MGUdhdGV3YXkgZnJvbnRpZXIgaXMgZW1wdHngaCICQFcBAng1X+D//3B5aHg0BSICQFcEA3l4NQTh//81PtX//3BoC5cmBQkiN2jbMBDOcWkTmCYFCSIpeXg1hen//zUb1f//cmoLlyYFCSIUakrYJAlKygAgKAM6c2t6lyICQFcLBXl4Nbbg//818NT//3BoC5cmCAkjuwIAAGjbMBDOcWkTmCYICSOqAgAAeXg1Men//zXH1P//cmoLlyYICSOSAgAAakrYJAlKygAgKAM6c3sLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7dGzKAEC2JBMMDnByb29mIHRvbyBkZWVw4HrbMHV8dhB3ByMoAgAAbG8HzncIbwjKACCXJB0MGHNpYmxp" +
        @"bmcgbXVzdCBiZSAzMiBieXRlc+AAQIh3CW4RkRCXJ9YAAAAQdwoiQ21vCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuhB3CiJ1bwhvCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIgj0QAAABB3CiJEbwhvCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuRB3CiJ0bW8KzkpvCQAgbwqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkiW8J" +
        @"2yg3AAB3Cm8KNwAA2zBKdUVuEalKdkVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HbMq1Jdj9//9rbdsoStgkCUrKACAoAzqXIgJAVwgEeDXM3///cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN2AgAAeguYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HpxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgedswcntzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC" +
        @"////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2hq2yhK2CQJSsoAICgDOpciAkBWAUDMg4gW").AsSerializable<Neo.SmartContract.NefFile>();

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
