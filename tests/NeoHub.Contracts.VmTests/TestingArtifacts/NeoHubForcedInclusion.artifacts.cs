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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ForcedInclusion"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":579,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":678,""safe"":false},{""name"":""getDeadlineSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":799,""safe"":true},{""name"":""setDeadlineSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":863,""safe"":false},{""name"":""getFee"",""parameters"":[],""returntype"":""Integer"",""offset"":1001,""safe"":true},{""name"":""getFeeRecipient"",""parameters"":[],""returntype"":""Hash160"",""offset"":1037,""safe"":true},{""name"":""getGasToken"",""parameters"":[],""returntype"":""Hash160"",""offset"":1095,""safe"":true},{""name"":""getSequencerBond"",""parameters"":[],""returntype"":""Hash160"",""offset"":1153,""safe"":true},{""name"":""getChainRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":1211,""safe"":true},{""name"":""getCensorshipSlashAmount"",""parameters"":[],""returntype"":""Integer"",""offset"":1269,""safe"":true},{""name"":""isProductionReady"",""parameters"":[],""returntype"":""Boolean"",""offset"":1305,""safe"":true},{""name"":""setFee"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1464,""safe"":false},{""name"":""setFeeRecipient"",""parameters"":[{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1714,""safe"":false},{""name"":""setGasToken"",""parameters"":[{""name"":""gasContract"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1839,""safe"":false},{""name"":""setSequencerBond"",""parameters"":[{""name"":""sequencerBond"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1972,""safe"":false},{""name"":""setChainRegistry"",""parameters"":[{""name"":""chainRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2096,""safe"":false},{""name"":""setCensorshipSlashAmount"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2220,""safe"":false},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""encodedTx"",""type"":""ByteArray""},{""name"":""txHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2350,""safe"":false},{""name"":""getEntry"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4766,""safe"":true},{""name"":""consume"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4797,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6097,""safe"":true},{""name"":""isCensorshipReported"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6117,""safe"":true},{""name"":""reportCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":6146,""safe"":false},{""name"":""slashReportedCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":6834,""safe"":false},{""name"":""isCensorshipSlashed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7177,""safe"":true}],""events"":[{""name"":""ForcedTxEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""ForcedTxConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SequencerCensorshipReported"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""SequencerSlashedForCensorship"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""DeadlineSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeRecipientChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GasTokenChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""ForcedInclusionFeeCharged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""SequencerBondChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""ChainRegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""CensorshipSlashAmountChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Forced-inclusion queue per L2 chain \u2014 anti-censorship primitive."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ForcedInclusion"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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SgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFza2k1zPf//zXQ+P//dEG3w4gDAegDoUoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF1bWyeShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXl6aDXMAQAAdm5reDWqBgAANY8GAAA1Ofn//3cHbwcQtyfVAAAANU35//93CDWA+f//dwlvCErZKCQGRQkiBsoAFLMkBQkiB28IELOqJBgME2ZlZSByZWNpcGllbnQgdW5zZXTgbwkMFM924ovQBixKR47jVWEBExnzz6TSlyQbDBZuYXRpdmUgR0FTIHRva2VuIHVuc2V04AtvB28IaBTAHwwIdHJhbnNmZXJvCUFifVtSdwpvCiQYDBNmZWUgdHJhbnNmZXIgZmFpbGVk4G8HbwhoE8AMGUZvcmNlZEluY2x1c2lvbkZlZUNoYXJnZWRBlQFvYXpoa3gUwAwQRm9yY2VkVHhFbnF1ZXVlZEGVAW9hayICQFcBAXjbKDcAAHBoNwAA2zDbKErYJAlKygAgKAM6IgJANwAAQNsoQNsoStgkCUrKACAoAzpA2zBAQS1RCDBAVwEBFYhwEUpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBBt8OIA0BXBgQAOHrKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8UnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9waIhxEHJ42zBzEHQibmtszkppamyeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkGoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFedswdBB1Im5sbc5KaWptnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JJBqACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRXrKShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6yhipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6yiCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ygAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFEHUibnptzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW16yrUkkGp6yp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFe0oQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFexipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV7IKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXsAGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWkiAkDbMEDbMEBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwACeXgSNANAVwEDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXpKEC4EIghKAf8AMgYB/wCRSmgVUdBFehipShAuBCIISgH/ADIGAf8AkUpoFlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV6ADCpShAuBCIISgH/ADIGAf8AkUpoG1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmgcUdBFaCICQEFifVtSQFcBAnl4Na3+//810u///3BoC5cmBhCIIgVo2zAiAkBXBwV4ELckIQwcY2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB6ELckGwwWbm9uY2UgbXVzdCBiZSBwb3NpdGl2ZeB7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3BoygBAtiQTDA5wcm9vZiB0b28gZGVlcOB6eDUZ/v//NT7v//9xaQuYJBQMD2VudHJ5IG5vdCBmb3VuZOBp2zByasoAPLgkFAwPZW50cnkgbWFsZm9ybWVk4AAUajUdAQAAc3p4NaMBAAB0bDXy7v//C5ckFQwQYWxyZWFkeSBjb25zdW1lZOAMAQHbMGw1kP3//wwB/dswNcbu//9K2CYORQwIc20gdW5zZXQ6StgkCUrKABQoAzp1eXgSwBUMEmdldEZpbmFsaXplZFR4Um9vdG1BYn1bUnZuDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJDIMLWJhdGNoIGlzIG5vdCBmaW5hbGl6ZWQgb3IgaGFzIG5vIHRyYW5zYWN0aW9uc+B8aG5rNQMBAAAkJQwgaW52YWxpZCBmb3JjZWQtdHJhbnNhY3Rpb24gcHJvb2bgengSwAwQRm9yY2VkVHhDb25zdW1lZEGVAW9hQFcCAgAgiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQaNsoStgkCUrKACAoAzoiAkBXAAJ5eBM1L/z//0AMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcGBHjbMHB7cRByI08CAAB6as5za3RsC5cmHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVzOmvKACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh0aRGREJcnxwAAABB1Ij5obc5KbG1R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JMAQdSJva23OSmwAIG2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkjyPCAAAAEHUiPmttzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkwBB1Im9obc5KbAAgbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSSPbNsoNwAAdW03AADbMEpwRWkRqUoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanrKtSWy/f//aRCXJAUJIhJ5aNsoStgkCUrKACAoAzqXIgJAVwACeXg1Tv3//zWf6v//C5giAkBXAAJ5eDQMNY7q//8LmCICQFcAAnl4GDVd+f//QFcGA3oQsyQ3DDJwZXJtaXNzaW9ubGVzcyByZXBvcnQgY2Fubm90IGF0dHJpYnV0ZSBhIHNlcXVlbmNlcuB5eDSQqiQVDBBhbHJlYWR5IGNvbnN1bWVk4Hl4NJtwaDUb6v//C5ckIAwbY2Vuc29yc2hpcCBhbHJlYWR5IHJlcG9ydGVk4Hl4Ncj4//817en//3FpC5gkFAwPZW50cnkgbm90IGZvdW5k4GnbMHJqygA8uCQUDA9lbnRyeSBtYWxmb3JtZWTgasoUn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9qNbgAAABzQbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXRsa7UmCAkjhQAAAAwBAdswaDUD+P//NX/r//91bUrZKCQGRQkiBsoAFLMkBQkiBm0Qs6omGXgRwB8MCnBhdXNlQ2hhaW5tQWJ9W1JFDBQAAAAAAAAAAAAAAAAAAAAAAAAAAHl4E8AMG1NlcXVlbmNlckNlbnNvcnNoaXBSZXBvcnRlZEGVAW9hCCICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAwM1juf//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HpK2SgkBkUJIgbKABSzJAUJIgZ6ELOqJBYMEWludmFsaWQgc2VxdWVuY2Vy4Hl4NfX8//81dOf//wuYJBkMFG5vIGNlbnNvcnNoaXAgcmVwb3J04Hl4Nc8AAABwaDVL5///C5ckFAwPYWxyZWFkeSBzbGFzaGVk4DWr6f//cWkQtyQgDBtzbGFzaCBhbW91bnQgbm90IGNvbmZpZ3VyZWTgNQ7p//9yakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okGQwUc2VxdWVuY2VyIGJvbmQgdW5zZXTgDAEB2zBoNYv1//9BOVNuPGl6eBTAHwwFc2xhc2hqQWJ9W1JFenl4E8AMHVNlcXVlbmNlclNsYXNoZWRGb3JDZW5zb3JzaGlwQZUBb2FAVwACeXgcNVz1//9AQTlTbjxAVwACeXg06TVq5v//C5giAkCWxOOX").AsSerializable<Neo.SmartContract.NefFile>();

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
