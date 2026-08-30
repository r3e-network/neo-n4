using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubForcedInclusion(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ForcedInclusion"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":601,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":700,""safe"":false},{""name"":""getDeadlineSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":821,""safe"":true},{""name"":""setDeadlineSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":885,""safe"":false},{""name"":""getFee"",""parameters"":[],""returntype"":""Integer"",""offset"":1023,""safe"":true},{""name"":""getFeeRecipient"",""parameters"":[],""returntype"":""Hash160"",""offset"":1059,""safe"":true},{""name"":""getGasToken"",""parameters"":[],""returntype"":""Hash160"",""offset"":1117,""safe"":true},{""name"":""getSequencerBond"",""parameters"":[],""returntype"":""Hash160"",""offset"":1175,""safe"":true},{""name"":""getChainRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":1233,""safe"":true},{""name"":""getCensorshipSlashAmount"",""parameters"":[],""returntype"":""Integer"",""offset"":1291,""safe"":true},{""name"":""isProductionReady"",""parameters"":[],""returntype"":""Boolean"",""offset"":1327,""safe"":true},{""name"":""setFee"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1486,""safe"":false},{""name"":""setFeeRecipient"",""parameters"":[{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1736,""safe"":false},{""name"":""setGasToken"",""parameters"":[{""name"":""gasContract"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1861,""safe"":false},{""name"":""setSequencerBond"",""parameters"":[{""name"":""sequencerBond"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1994,""safe"":false},{""name"":""setChainRegistry"",""parameters"":[{""name"":""chainRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2118,""safe"":false},{""name"":""setCensorshipSlashAmount"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2242,""safe"":false},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""encodedTx"",""type"":""ByteArray""},{""name"":""txHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2372,""safe"":false},{""name"":""getEntry"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4789,""safe"":true},{""name"":""consume"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4820,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6120,""safe"":true},{""name"":""isCensorshipReported"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6140,""safe"":true},{""name"":""reportCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":6169,""safe"":false},{""name"":""slashReportedCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":6858,""safe"":false},{""name"":""isCensorshipSlashed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7201,""safe"":true}],""events"":[{""name"":""ForcedTxEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""ForcedTxConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SequencerCensorshipReported"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""SequencerSlashedForCensorship"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""DeadlineSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeRecipientChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GasTokenChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""ForcedInclusionFeeCharged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""SequencerBondChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""ChainRegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""CensorshipSlashAmountChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Forced-inclusion queue per L2 chain \u2014 anti-censorship primitive."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ForcedInclusion"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AAABAAAAAAAAALskAzoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRc2tpNcz3//810Pj//3RBt8OIAwHoA6FKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdW1snkoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF5emg1zQEAAHZreDWsBgAAblA1jwYAADU4+f//dwdvBxC3J9UAAAA1TPn//3cINX/5//93CW8IStkoJAZFCSIGygAUsyQFCSIHbwgQs6okGAwTZmVlIHJlY2lwaWVudCB1bnNldOBvCQwUz3bii9AGLEpHjuNVYQETGfPPpNKXJBsMFm5hdGl2ZSBHQVMgdG9rZW4gdW5zZXTgC28HbwhoFMAfDAh0cmFuc2Zlcm8JQWJ9W1J3Cm8KJBgME2ZlZSB0cmFuc2ZlciBmYWlsZWTgbwdvCGgTwAwZRm9yY2VkSW5jbHVzaW9uRmVlQ2hhcmdlZEGVAW9hemhreBTADBBGb3JjZWRUeEVucXVldWVkQZUBb2FrIgJAVwEBeNsoNwAAcGg3AADbMNsoStgkCUrKACAoAzoiAkA3AABA2yhA2yhK2CQJSsoAICgDOkDbMEBBLVEIMEBXAQEViHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQEG3w4gDQFcGBAA4esqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BoiHEQcnjbMHMQdCJua2zOSmlqbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQagAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV52zB0EHUibmxtzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkkGoAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFespKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EUQdSJuem3OSmlqbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbXrKtSSQanrKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV7ShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV7GKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXsgqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFewAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFaSICQNswQNswQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAAJ5eBI0A0BXAQMdiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFekoQLgQiCEoB/wAyBgH/AJFKaBVR0EV6GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeiCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV6ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJAQWJ9W1JAVwECeXg1rf7//zXR7///cGgLlyYGEIgiBWjbMCICQFcHBXgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HoQtyQbDBZub25jZSBtdXN0IGJlIHBvc2l0aXZl4HsLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7cGjKAEC2JBMMDnByb29mIHRvbyBkZWVw4Hp4NRn+//81Pe///3FpC5gkFAwPZW50cnkgbm90IGZvdW5k4GnbMHJqygA8uCQUDA9lbnRyeSBtYWxmb3JtZWTgABRqNR0BAABzeng1owEAAHRsNfHu//8LlyQVDBBhbHJlYWR5IGNvbnN1bWVk4AwBAdswbDWQ/f//DAH92zA1xe7//0rYJg5FDAhzbSB1bnNldDpK2CQJSsoAFCgDOnV5eBLAFQwSZ2V0RmluYWxpemVkVHhSb290bUFifVtSdm4MIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okMgwtYmF0Y2ggaXMgbm90IGZpbmFsaXplZCBvciBoYXMgbm8gdHJhbnNhY3Rpb25z4Hxobms1AwEAACQlDCBpbnZhbGlkIGZvcmNlZC10cmFuc2FjdGlvbiBwcm9vZuB6eBLADBBGb3JjZWRUeENvbnN1bWVkQZUBb2FAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQFcAAnl4EzUv/P//QAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwYEeNswcHtxEHIjTwIAAHpqznNrdGwLlyYdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXM6a8oAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHRpEZEQlyfHAAAAEHUiPmhtzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkwBB1Im9rbc5KbAAgbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSSPI8IAAAAQdSI+a23OSmxtUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSTAEHUib2htzkpsACBtnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JI9s2yg3AAB1bTcAANswSnBFaRGpShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFKcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqesq1JbL9//9pEJckBQkiEnlo2yhK2CQJSsoAICgDOpciAkBXAAJ5eDVO/f//NZ7q//8LmCICQFcAAnl4NAw1jer//wuYIgJAVwACeXgYNV35//9AVwYDehCzJDcMMnBlcm1pc3Npb25sZXNzIHJlcG9ydCBjYW5ub3QgYXR0cmlidXRlIGEgc2VxdWVuY2Vy4Hl4NJCqJBUMEGFscmVhZHkgY29uc3VtZWTgeXg0m3BoNRrq//8LlyQgDBtjZW5zb3JzaGlwIGFscmVhZHkgcmVwb3J0ZWTgeXg1yPj//zXs6f//cWkLmCQUDA9lbnRyeSBub3QgZm91bmTgadswcmrKADy4JBQMD2VudHJ5IG1hbGZvcm1lZOBqasoUn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9QNbgAAABzQbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXRsa7UmCAkjhQAAAAwBAdswaDUC+P//NX3r//91bUrZKCQGRQkiBsoAFLMkBQkiBm0Qs6omGXgRwB8MCnBhdXNlQ2hhaW5tQWJ9W1JFDBQAAAAAAAAAAAAAAAAAAAAAAAAAAHl4E8AMG1NlcXVlbmNlckNlbnNvcnNoaXBSZXBvcnRlZEGVAW9hCCICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAwM1jOf//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HpK2SgkBkUJIgbKABSzJAUJIgZ6ELOqJBYMEWludmFsaWQgc2VxdWVuY2Vy4Hl4NfT8//81cuf//wuYJBkMFG5vIGNlbnNvcnNoaXAgcmVwb3J04Hl4Nc8AAABwaDVJ5///C5ckFAwPYWxyZWFkeSBzbGFzaGVk4DWp6f//cWkQtyQgDBtzbGFzaCBhbW91bnQgbm90IGNvbmZpZ3VyZWTgNQzp//9yakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okGQwUc2VxdWVuY2VyIGJvbmQgdW5zZXTgDAEB2zBoNYr1//9BOVNuPGl6eBTAHwwFc2xhc2hqQWJ9W1JFenl4E8AMHVNlcXVlbmNlclNsYXNoZWRGb3JDZW5zb3JzaGlwQZUBb2FAVwACeXgcNVv1//9AQTlTbjxAVwACeXg06TVo5v//C5giAkDyw3oF").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delCensorshipSlashAmountChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("CensorshipSlashAmountChanged")]
    public event delCensorshipSlashAmountChanged? OnCensorshipSlashAmountChanged;

    public delegate void delChainRegistryChanged(UInt160? obj);

    [DisplayName("ChainRegistryChanged")]
    public event delChainRegistryChanged? OnChainRegistryChanged;

    public delegate void delDeadlineSecondsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("DeadlineSecondsChanged")]
    public event delDeadlineSecondsChanged? OnDeadlineSecondsChanged;

    public delegate void delFeeChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("FeeChanged")]
    public event delFeeChanged? OnFeeChanged;

    public delegate void delFeeRecipientChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("FeeRecipientChanged")]
    public event delFeeRecipientChanged? OnFeeRecipientChanged;

    public delegate void delForcedInclusionFeeCharged(UInt160? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("ForcedInclusionFeeCharged")]
    public event delForcedInclusionFeeCharged? OnForcedInclusionFeeCharged;

    public delegate void delForcedTxConsumed(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ForcedTxConsumed")]
    public event delForcedTxConsumed? OnForcedTxConsumed;

    public delegate void delForcedTxEnqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt256? arg4);

    [DisplayName("ForcedTxEnqueued")]
    public event delForcedTxEnqueued? OnForcedTxEnqueued;

    public delegate void delGasTokenChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("GasTokenChanged")]
    public event delGasTokenChanged? OnGasTokenChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delSequencerBondChanged(UInt160? obj);

    [DisplayName("SequencerBondChanged")]
    public event delSequencerBondChanged? OnSequencerBondChanged;

    public delegate void delSequencerCensorshipReported(BigInteger? arg1, BigInteger? arg2, UInt160? arg3);

    [DisplayName("SequencerCensorshipReported")]
    public event delSequencerCensorshipReported? OnSequencerCensorshipReported;

    public delegate void delSequencerSlashedForCensorship(BigInteger? arg1, BigInteger? arg2, UInt160? arg3);

    [DisplayName("SequencerSlashedForCensorship")]
    public event delSequencerSlashedForCensorship? OnSequencerSlashedForCensorship;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? CensorshipSlashAmount { [DisplayName("getCensorshipSlashAmount")] get; [DisplayName("setCensorshipSlashAmount")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? ChainRegistry { [DisplayName("getChainRegistry")] get; [DisplayName("setChainRegistry")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? DeadlineSeconds { [DisplayName("getDeadlineSeconds")] get; [DisplayName("setDeadlineSeconds")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? Fee { [DisplayName("getFee")] get; [DisplayName("setFee")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? FeeRecipient { [DisplayName("getFeeRecipient")] get; [DisplayName("setFeeRecipient")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GasToken { [DisplayName("getGasToken")] get; [DisplayName("setGasToken")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? SequencerBond { [DisplayName("getSequencerBond")] get; [DisplayName("setSequencerBond")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsProductionReady { [DisplayName("isProductionReady")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getEntry")]
    public abstract byte[]? GetEntry(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isCensorshipReported")]
    public abstract bool? IsCensorshipReported(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isCensorshipSlashed")]
    public abstract bool? IsCensorshipSlashed(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isConsumed")]
    public abstract bool? IsConsumed(BigInteger? chainId, BigInteger? nonce);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("consume")]
    public abstract void Consume(BigInteger? chainId, BigInteger? batchNumber, BigInteger? nonce, IList<object>? siblings, BigInteger? leafIndex);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("enqueueForcedTransaction")]
    public abstract BigInteger? EnqueueForcedTransaction(BigInteger? chainId, byte[]? encodedTx, UInt256? txHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("reportCensorship")]
    public abstract bool? ReportCensorship(BigInteger? chainId, BigInteger? nonce, UInt160? sequencer);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("slashReportedCensorship")]
    public abstract void SlashReportedCensorship(BigInteger? chainId, BigInteger? nonce, UInt160? sequencer);

    #endregion
}
