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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ForcedInclusion"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":579,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":678,""safe"":false},{""name"":""getDeadlineSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":799,""safe"":true},{""name"":""setDeadlineSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":863,""safe"":false},{""name"":""getFee"",""parameters"":[],""returntype"":""Integer"",""offset"":1001,""safe"":true},{""name"":""getFeeRecipient"",""parameters"":[],""returntype"":""Hash160"",""offset"":1037,""safe"":true},{""name"":""getGasToken"",""parameters"":[],""returntype"":""Hash160"",""offset"":1095,""safe"":true},{""name"":""getSequencerBond"",""parameters"":[],""returntype"":""Hash160"",""offset"":1153,""safe"":true},{""name"":""getChainRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":1211,""safe"":true},{""name"":""getCensorshipSlashAmount"",""parameters"":[],""returntype"":""Integer"",""offset"":1269,""safe"":true},{""name"":""isProductionReady"",""parameters"":[],""returntype"":""Boolean"",""offset"":1305,""safe"":true},{""name"":""setFee"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1464,""safe"":false},{""name"":""setFeeRecipient"",""parameters"":[{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1714,""safe"":false},{""name"":""setGasToken"",""parameters"":[{""name"":""gasContract"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1839,""safe"":false},{""name"":""setSequencerBond"",""parameters"":[{""name"":""sequencerBond"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1972,""safe"":false},{""name"":""setChainRegistry"",""parameters"":[{""name"":""chainRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2096,""safe"":false},{""name"":""setCensorshipSlashAmount"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2220,""safe"":false},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""encodedTx"",""type"":""ByteArray""},{""name"":""txHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2350,""safe"":false},{""name"":""getEntry"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4741,""safe"":true},{""name"":""consume"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4772,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6072,""safe"":true},{""name"":""isCensorshipReported"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6092,""safe"":true},{""name"":""reportCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":6121,""safe"":false},{""name"":""slashReportedCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":6809,""safe"":false},{""name"":""isCensorshipSlashed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7152,""safe"":true}],""events"":[{""name"":""ForcedTxEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""ForcedTxConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SequencerCensorshipReported"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""SequencerSlashedForCensorship"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""DeadlineSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeRecipientChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GasTokenChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""ForcedInclusionFeeCharged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""SequencerBondChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""ChainRegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""CensorshipSlashAmountChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Forced-inclusion queue per L2 chain \u2014 anti-censorship primitive."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ForcedInclusion"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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/////wAAAAAAAAAAkXNraTXl9///Nen4//90QbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXVtbJ5KEC4EIg5KA/////8AAAAAMgwD/////wAAAACReXpoNcwBAAB2bmt4NaoGAAA1jwYAADVS+f//dwdvBxC3J9UAAAA1Zvn//3cINZn5//93CW8IStkoJAZFCSIGygAUsyQFCSIHbwgQs6okGAwTZmVlIHJlY2lwaWVudCB1bnNldOBvCQwUz3bii9AGLEpHjuNVYQETGfPPpNKXJBsMFm5hdGl2ZSBHQVMgdG9rZW4gdW5zZXTgC28HbwhoFMAfDAh0cmFuc2Zlcm8JQWJ9W1J3Cm8KJBgME2ZlZSB0cmFuc2ZlciBmYWlsZWTgbwdvCGgTwAwZRm9yY2VkSW5jbHVzaW9uRmVlQ2hhcmdlZEGVAW9hemhreBTADBBGb3JjZWRUeEVucXVldWVkQZUBb2FrIgJAVwEBeNsoNwAAcGg3AADbMNsoStgkCUrKACAoAzoiAkA3AABA2yhA2yhK2CQJSsoAICgDOkDbMEBBLVEIMEBXAQEViHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQEG3w4gDQFcGBAA4esqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BoiHEQcnjbMHMQdCJua2zOSmlqbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQagAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV52zB0EHUibmxtzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkkGoAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFespKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EUQdSJuem3OSmlqbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbXrKtSSQanrKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV7ShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV7GKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXsgqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFewAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFaSICQNswQNswQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAAJ5eBI0A0BXAQMdiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFekoQLgQiCEoB/wAyBgH/AJFKaBVR0EV6GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeiCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV6ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJAQWJ9W1JAVwECeXg1rf7//zXr7///cGgLlyYGEIgiBWjbMCICQFcHBXgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HoQtyQbDBZub25jZSBtdXN0IGJlIHBvc2l0aXZl4HsLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7cGjKAEC2JBMMDnByb29mIHRvbyBkZWVw4Hp4NRn+//81V+///3FpC5gkFAwPZW50cnkgbm90IGZvdW5k4GnbMHJqygA8uCQUDA9lbnRyeSBtYWxmb3JtZWTgABRqNR0BAABzeng1owEAAHRsNQvv//8LlyQVDBBhbHJlYWR5IGNvbnN1bWVk4AwBAdswbDWQ/f//DAH92zA13+7//0rYJg5FDAhzbSB1bnNldDpK2CQJSsoAFCgDOnV5eBLAFQwSZ2V0RmluYWxpemVkVHhSb290bUFifVtSdm4MIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okMgwtYmF0Y2ggaXMgbm90IGZpbmFsaXplZCBvciBoYXMgbm8gdHJhbnNhY3Rpb25z4Hxobms1AwEAACQlDCBpbnZhbGlkIGZvcmNlZC10cmFuc2FjdGlvbiBwcm9vZuB6eBLADBBGb3JjZWRUeENvbnN1bWVkQZUBb2FAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQFcAAnl4EzUv/P//QAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwYEeNswcHtxEHIjTwIAAHpqznNrdGwLlyYdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXM6a8oAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHRpEZEQlyfHAAAAEHUiPmhtzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkwBB1Im9rbc5KbAAgbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSSPI8IAAAAQdSI+a23OSmxtUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSTAEHUib2htzkpsACBtnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JI9s2yg3AAB1bTcAANswSnBFaRGpShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFKcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqesq1JbL9//9pEJckBQkiEnlo2yhK2CQJSsoAICgDOpciAkBXAAJ5eDVO/f//Nbjq//8LmCICQFcAAnl4NAw1p+r//wuYIgJAVwACeXgYNV35//9AVwYDehCzJDcMMnBlcm1pc3Npb25sZXNzIHJlcG9ydCBjYW5ub3QgYXR0cmlidXRlIGEgc2VxdWVuY2Vy4Hl4NJCqJBUMEGFscmVhZHkgY29uc3VtZWTgeXg0m3BoNTTq//8LlyQgDBtjZW5zb3JzaGlwIGFscmVhZHkgcmVwb3J0ZWTgeXg1yPj//zUG6v//cWkLmCQUDA9lbnRyeSBub3QgZm91bmTgadswcmrKADy4JBQMD2VudHJ5IG1hbGZvcm1lZOBqyhSfSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2o1uAAAAHNBt8OIAwHoA6FKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdGxrtSYICSOFAAAADAEB2zBoNQP4//81mOv//3VtStkoJAZFCSIGygAUsyQFCSIGbRCzqiYZeBHAHwwKcGF1c2VDaGFpbm1BYn1bUkUMFAAAAAAAAAAAAAAAAAAAAAAAAAAAeXgTwAwbU2VxdWVuY2VyQ2Vuc29yc2hpcFJlcG9ydGVkQZUBb2EIIgJAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkiICQFcDAzWn5///Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgekrZKCQGRQkiBsoAFLMkBQkiBnoQs6okFgwRaW52YWxpZCBzZXF1ZW5jZXLgeXg19fz//zWN5///C5gkGQwUbm8gY2Vuc29yc2hpcCByZXBvcnTgeXg1zwAAAHBoNWTn//8LlyQUDA9hbHJlYWR5IHNsYXNoZWTgNcTp//9xaRC3JCAMG3NsYXNoIGFtb3VudCBub3QgY29uZmlndXJlZOA1J+n//3JqStkoJAZFCSIGygAUsyQFCSIGahCzqiQZDBRzZXF1ZW5jZXIgYm9uZCB1bnNldOAMAQHbMGg1i/X//0E5U248aXp4FMAfDAVzbGFzaGpBYn1bUkV6eXgTwAwdU2VxdWVuY2VyU2xhc2hlZEZvckNlbnNvcnNoaXBBlQFvYUBXAAJ5eBw1XPX//0BBOVNuPEBXAAJ5eDTpNYPm//8LmCICQO0xJCI=").AsSerializable<Neo.SmartContract.NefFile>();

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
