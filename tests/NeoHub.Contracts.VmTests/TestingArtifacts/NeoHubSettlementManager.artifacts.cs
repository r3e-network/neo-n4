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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":347,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":411,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":532,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":590,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":726,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":784,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":902,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":960,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1080,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5961,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6993,""safe"":false},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3704,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7537,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3033,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":7569,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":7587,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7665,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8387,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":9067,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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ZHZlcnRpc2VkIHNlY3VyaXR5IGxldmVs4AwB/dswQdWNXuhK2CYdRQwXdmVyaWZpZXIgcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzp3DHgRwBUMEHZlcmlmeUNvbW1pdG1lbnRvDEFifVtSdw1vDSQhDBx2ZXJpZmllciByZWplY3RlZCBjb21taXRtZW504AH8AHg1qgYAAHcObwtvDmloNR0NAABvCRKXJgUSIgMRdw8RiEoQbw/QbUE5DOMKeGloNbYNAABBOQzjCgGcAHg1bgYAAHcQbxDbMGloNaoNAABBOQzjCm8JEpcmaDWJ+v//dxFvEUrZKCQGRQkiBsoAFLMkBQkiB28RELOqJCMMHm9wdGltaXN0aWMgY2hhbGxlbmdlIG5vdCB3aXJlZOB4NWINAAB3Em8SaWgTwB8MCm9wZW5XaW5kb3dvEUFifVtSRQA8eDXoBQAAdxFvEWloE8AMDkJhdGNoU3VibWl0dGVkQZUBb2FAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkiICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRWeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AKKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRaeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AMKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eReeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AOKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZIiAkBBYn1bUkBXAQF4NDVB1Y1e6HBoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAQEViHAUSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQErYJgZFECIE2yFAVwACeXgRNANAVwEDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXpKEC4EIghKAf8AMgYB/wCRSmgVUdBFehipShAuBCIISgH/ADIGAf8AkUpoFlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV6ADCpShAuBCIISgH/ADIGAf8AkUpoG1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmgcUdBFaCICQNswQFcCAgAgiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQaNsoStgkCUrKACAoAzoiAkDbKErYJAlKygAgKAM6QFcBAXg0QEHVjV7ocGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwEBFYhwE0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcDAwFMAYhwEHEQciJueGrOSmhpap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFahy1JJFpHJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFABx4aUpgaDXQAQAAADx4WEpgaDXEAQAAAFx4WEpgaDW4AQAAAHx4WEpgaDWsAQAAAZwAeFhKYGg1nwEAAAG8AHhYSmBoNZIBAAAB3AB4WEpgaDWFAQAAEHIibnlqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFAfwAeFhKYGg1zQAAABByIm56as5KaFhqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqACC1JJBYACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pgRWjbKDcAAHJqNwAA2zDbKErYJAlKygAgKAM6IgJAVwEEEHAjoQAAAHp7aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkp4WGieSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9wRWgAILUlYP///1gAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFQDcAAEDbKEBXAAJ5EcAVDBBnZXRTZWN1cml0eUxldmVseEFifVtSShABAAG7JAM6IgJAVwACeRHAFQwJZ2V0REFNb2RleEFifVtSShABAAG7JAM6IgJAVwACeBS2JFAMS3NlY3VyaXR5TGV2ZWwgbXVzdCBiZSAwLi40IChTaWRlY2hhaW4vU2V0dGxlZC9PcHRpbWlzdGljL1ZhbGlkaXR5L1ZhbGlkaXVtKeB5E7YkMAwrZGFNb2RlIG11c3QgYmUgMC4uMyAoTDEvTmVvRlMvRXh0ZXJuYWwvREFDKeB4E5cmMHkQlyQrDCZWYWxpZGl0eSBzZWN1cml0eSBsZXZlbCByZXF1aXJlcyBMMSBEQeB4FJcmN3kQmCQyDC1WYWxpZGl1bSBzZWN1cml0eSBsZXZlbCByZXF1aXJlcyBvZmYtY2hhaW4gREHgQFcAAngQlyYFCCIFeBGXJhd5EZcmBQgiBXkSlyYFCCIFeROXIil4EpcmD3kSlyYFCCIFeROXIhd4E5cmBQgiBXgUlyYHeROXIgUJIgJAVwEEegwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXqiQjDB5EQSBjb21taXRtZW50IG11c3QgYmUgbm9uLXplcm/gexO2JBgME2RhTW9kZSBtdXN0IGJlIDAuLjPgNRTu//9waErZKCQGRQkiBsoAFLMkBQkiBmgQs6okGgwVREEgcmVnaXN0cnkgbm90IHdpcmVk4Ht6eXgUwB8MBnJlY29yZGhBYn1bUkVAQTkM4wpAVwACeXgSNX73//9A2zBAVwACeXgVNW/3//9AVwIBeMoBQQG4JCQMH2NvbW1pdG1lbnQgbWlzc2luZyBwcm9vZiBsZW5ndGjgAT0BeDW18v//SgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BoAFW4JB8MGm9wdGltaXN0aWMgcHJvb2YgdG9vIHNtYWxs4GgCAAAQALYkHwwab3B0aW1pc3RpYyBwcm9vZiB0b28gbGFyZ2XgAUEBaJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfeMqXJCUMIGNvbW1pdG1lbnQgcHJvb2YgbGVuZ3RoIG1pc21hdGNo4HgBQQHOEpckKQwkdW5zdXBwb3J0ZWQgb3B0aW1pc3RpYyBwcm9vZiB2ZXJzaW9u4AF+AXg0P3FpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQhDBxpbnZhbGlkIG9wdGltaXN0aWMgc2VxdWVuY2Vy4GkiAkBXAgIAFIhwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAFLUkkGjbKErYJAlKygAUKAM6IgJA2yhK2CQJSsoAFCgDOkBXCgJ5eDVD9f//cGhB1Y1e6HFpC5gkEgwNYmF0Y2ggdW5rbm93buBp2zAQznJqEZcmBQgiBWoSlyQaDBViYXRjaCBub3QgZmluYWxpemFibGXgahKXJ5MAAAA1cer//3NrStkoJAZFCSIGygAUsyQFCSIGaxCzqiQjDB5vcHRpbWlzdGljIGNoYWxsZW5nZSBub3Qgd2lyZWTga0H4J+yMJEgMQ2NoYWxsZW5nZWFibGUgYmF0Y2ggZmluYWxpemF0aW9uIG11c3QgY29tZSBmcm9tIE9wdGltaXN0aWNDaGFsbGVuZ2XgeXg14/z//0HVjV7oStgmFEUMDmhlYWRlciBtaXNzaW5nOtswcwwB/NswQdWNXuhK2CYURQwOcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzp0eGw1Rfr//3V4bDVo+v//dm5tNYT6//9rATwBzm01cPv//yQ+DDlwcm9vZiB0eXBlIGluY29tcGF0aWJsZSB3aXRoIGN1cnJlbnQgY2hhaW4gc2VjdXJpdHkgbGV2ZWzgeXg19fL//xGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGXJB0MGGZpbmFsaXplIG91dCBvZiBzZXF1ZW5jZeB5EbcmRwAcazWf9P//dwdvB3g1MfX//5ckMgwtcHJlU3RhdGVSb290IG5vIGxvbmdlciBtYXRjaGVzIGNhbm9uaWNhbCBoZWFk4AH8AGs1WfT//3cHbwd5eDRedwhvCG01g/n//28Ibwd5eDUhAQAAADxrNTT0//93CQwBA9swaEE5DOMKbwnbMHg1/fT//0E5DOMKeXg1cAEAAG8JeXgTwAwOQmF0Y2hGaW5hbGl6ZWRBlQFvYUBXAwM15+j//3BoStkoJAZFCSIGygAUsyQFCSIGaBCzqiQaDBVEQSByZWdpc3RyeSBub3Qgd2lyZWTgeXgSwBUMDWdldENvbW1pdG1lbnRoQWJ9W1JxaXqXJDcMMkRBIHJlZ2lzdHJ5IGNvbW1pdG1lbnQgZG9lcyBub3QgbWF0Y2ggYmF0Y2ggaGVhZGVy4Hl4EsAVDAdnZXRNb2RlaEFifVtSShABAAG7JAM6cmoTtiQhDBxyZWNvcmRlZCBkYU1vZGUgbXVzdCBiZSAwLi4z4GoiAkBXAgQ1wuj//3BoStkoJAZFCSIGygAUsyQFCSIGaBCzqiQbDBZEQSB2YWxpZGF0b3Igbm90IHdpcmVk4Ht6eXgUwBUMCHZhbGlkYXRlaEFifVtScWkkJQwgREEgdmFsaWRhdG9yIHJlamVjdGVkIGNvbW1pdG1lbnTgQFcAAnl4NdLw//9BOQzjCkBBOQzjCkBXBwI1B+b//0H4J+yMcDW15v//cWlK2SgkBkUJIgbKABSzJAUJIgZpELOqJAUJIghpQfgn7IxyaCYFCCIDaiQTDA5ub3QgYXV0aG9yaXplZOB5eDXt8P//QdWNXuhzawuYJBIMDWJhdGNoIHVua25vd27ga9swEM50bBSYJBsMFmJhdGNoIGFscmVhZHkgcmV2ZXJ0ZWTgaiQFCSIEaKomQ2wSlyQ+DDlPcHRpbWlzdGljQ2hhbGxlbmdlIGNhbiBvbmx5IHJldmVydCBjaGFsbGVuZ2VhYmxlIGJhdGNoZXPgbBOXJwsBAAB5eDWc7///lyQ0DC9vbmx5IHRoZSBsYXRlc3QgZmluYWxpemVkIGJhdGNoIGNhbiBiZSByZXZlcnRlZOB5EbcntQAAAHkRn0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACReDVn+P//QdWNXuh1bQuYJCIMHXByZXZpb3VzIGJhdGNoIGhlYWRlciBtaXNzaW5n4AA8bdswNfjw//92btsweDXO8f//QTkM4wp5EZ9KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXg1E/7//yIUeDWM8f//QXVU9ZQQeDX//f//DAEE2zB5eDVJ7///QTkM4wp5eBLADA1CYXRjaFJldmVydGVkQZUBb2FAQXVU9ZRAVwECeXg1G+///0HVjV7ocGgLlyYFECIHaNswEM4iAkBXAQJ4NUTu//9weWh4NAUiAkBXBAN5eDXp7v//QdWNXuhwaAuXJgUJIjdo2zAQznFpE5gmBQkiKXl4NVr3//9B1Y1e6HJqC5cmBQkiFGpK2CQJSsoAICgDOnNrepciAkBXCwV5eDWb7v//QdWNXuhwaAuXJggJI7sCAABo2zAQznFpE5gmCAkjqgIAAHl4NQb3//9B1Y1e6HJqC5cmCAkjkgIAAGpK2CQJSsoAICgDOnN7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3RsygBAtiQTDA5wcm9vZiB0b28gZGVlcOB62zB1fHYQdwcjKAIAAGxvB853CG8IygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdwluEZEQlyfWAAAAEHcKIkNtbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLoQdwoidW8IbwrOSm8JACBvCp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSSII9EAAAAQdwoiRG8IbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLkQdwoidG1vCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIlvCdsoNwAAdwpvCjcAANswSnVFbhGpSnZFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvB2zKtSXY/f//a23bKErYJAlKygAgKAM6lyICQFcIBHg1se3//3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCAkjdgIAAHoLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB6cWnKAEC2JBMMDnByb29mIHRvbyBkZWVw4HnbMHJ7cxB0IxsCAABpbM51bcoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHZrEZEQlyfTAAAAEHcHIkJqbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzbW8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKI84AAAAQdwciQm1vB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNqbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIpu2yg3AAB3B28HNwAA2zBKckVrEalKc0VsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsacq1Jeb9//9oatsoStgkCUrKACAoAzqXIgJAVgFAdv0GTA==").AsSerializable<Neo.SmartContract.NefFile>();

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
