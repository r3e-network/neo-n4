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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ForcedInclusion"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":549,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":648,""safe"":false},{""name"":""getDeadlineSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":769,""safe"":true},{""name"":""setDeadlineSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":833,""safe"":false},{""name"":""getFee"",""parameters"":[],""returntype"":""Integer"",""offset"":971,""safe"":true},{""name"":""getFeeRecipient"",""parameters"":[],""returntype"":""Hash160"",""offset"":1007,""safe"":true},{""name"":""getGasToken"",""parameters"":[],""returntype"":""Hash160"",""offset"":1065,""safe"":true},{""name"":""getSequencerBond"",""parameters"":[],""returntype"":""Hash160"",""offset"":1123,""safe"":true},{""name"":""getChainRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":1181,""safe"":true},{""name"":""getCensorshipSlashAmount"",""parameters"":[],""returntype"":""Integer"",""offset"":1239,""safe"":true},{""name"":""isProductionReady"",""parameters"":[],""returntype"":""Boolean"",""offset"":1275,""safe"":true},{""name"":""setFee"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1433,""safe"":false},{""name"":""setFeeRecipient"",""parameters"":[{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1680,""safe"":false},{""name"":""setGasToken"",""parameters"":[{""name"":""gasContract"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1805,""safe"":false},{""name"":""setSequencerBond"",""parameters"":[{""name"":""sequencerBond"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1925,""safe"":false},{""name"":""setChainRegistry"",""parameters"":[{""name"":""chainRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2049,""safe"":false},{""name"":""setCensorshipSlashAmount"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2173,""safe"":false},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""encodedTx"",""type"":""ByteArray""},{""name"":""txHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2303,""safe"":false},{""name"":""getEntry"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4640,""safe"":true},{""name"":""consume"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4671,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5945,""safe"":true},{""name"":""isCensorshipReported"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5962,""safe"":true},{""name"":""reportCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5991,""safe"":false},{""name"":""slashReportedCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":6643,""safe"":false},{""name"":""isCensorshipSlashed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6980,""safe"":true}],""events"":[{""name"":""ForcedTxEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""ForcedTxConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SequencerCensorshipReported"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""SequencerSlashedForCensorship"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""DeadlineSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeRecipientChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GasTokenChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""ForcedInclusionFeeCharged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""SequencerBondChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""ChainRegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""CensorshipSlashAmountChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Forced-inclusion queue per L2 chain \u2014 anti-censorship primitive."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ForcedInclusion"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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kXl6aDXGAQAAdmt4NaUGAABuUDWIBgAANZL5//93B28HELcnzgAAADWm+f//dwg12fn//3cJbwhK2SgkBkUJIgbKABSzJAUJIgdvCBCzqiQYDBNmZWUgcmVjaXBpZW50IHVuc2V04G8JStkoJAZFCSIGygAUsyQFCSIHbwkQs6okFAwPZ2FzIHRva2VuIHVuc2V04AtvB28IaBTAHwwIdHJhbnNmZXJvCUFifVtSdwpvCiQYDBNmZWUgdHJhbnNmZXIgZmFpbGVk4G8HbwhoE8AMGUZvcmNlZEluY2x1c2lvbkZlZUNoYXJnZWRBlQFvYXpoa3gUwAwQRm9yY2VkVHhFbnF1ZXVlZEGVAW9hayICQFcBAXjbKDcAAHBoNwAA2zDbKErYJAlKygAgKAM6IgJANwAAQNsoQNsoStgkCUrKACAoAzpA2zBAQTlTbjxAVwEBFYhwEUpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBBt8OIA0BXBgQAOHrKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8UnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9waIhxEHJ42zBzEHQibmtszkppamyeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkGoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFedswdBB1Im5sbc5KaWptnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JJBqACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRXrKShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6yhipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6yiCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ygAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFEHUibnptzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW16yrUkkGp6yp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFe0oQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFexipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV7IKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXsAGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWkiAkDbMEDbMEBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwACeXgSNANAVwEDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXpKEC4EIghKAf8AMgYB/wCRSmgVUdBFehipShAuBCIISgH/ADIGAf8AkUpoFlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV6ADCpShAuBCIISgH/ADIGAf8AkUpoG1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmgcUdBFaCICQEFifVtSQFcBAnl4Na3+//81MvD//3BoC5cmBhCIIgVo2zAiAkBXBwV4ELckIQwcY2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB6ELckGwwWbm9uY2UgbXVzdCBiZSBwb3NpdGl2ZeB7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3BoygBAtiQTDA5wcm9vZiB0b28gZGVlcOB6eDUZ/v//NZ7v//9xaQuYJBQMD2VudHJ5IG5vdCBmb3VuZOBp2zByasoAPLgkFAwPZW50cnkgbWFsZm9ybWVk4AAUajUdAQAAcwwB/dswNVbv//9K2CYORQwIc20gdW5zZXQ6StgkCUrKABQoAzp0eXgSwBUMEmdldEZpbmFsaXplZFR4Um9vdGxBYn1bUnVtDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJDIMLWJhdGNoIGlzIG5vdCBmaW5hbGl6ZWQgb3IgaGFzIG5vIHRyYW5zYWN0aW9uc+B8aG1rNScBAAAkJQwgaW52YWxpZCBmb3JjZWQtdHJhbnNhY3Rpb24gcHJvb2bgeng1XwMAAHZuNYfu//8LlyQVDBBhbHJlYWR5IGNvbnN1bWVk4AwBAdswbjXF/P//engSwAwQRm9yY2VkVHhDb25zdW1lZEGVAW9hQFcCAgAgiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQaNsoStgkCUrKACAoAzoiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcGBHjbMHB7cRByIzUCAAB6as5zawuYJAUJIgdrygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdGkRkRCXJ8cAAAAQdSI+aG3OSmxtUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSTAEHUib2ttzkpsACBtnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JI8jwgAAABB1Ij5rbc5KbG1R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JMAQdSJvaG3OSmwAIG2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkj2zbKDcAAHVtNwAA2zBKcEVpEalKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkUpxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp6yrUlzP3//2kQlyQFCSISeWjbKErYJAlKygAgKAM6lyICQFcAAnl4EzWo+f//QFcAAnl4NO81HOv//wuYIgJAVwACeXg0DDUL6///C5giAkBXAAJ5eBg1evn//0BXBgN6StkoJAZFCSIGygAUsyQFCSIGehCzqiQWDBFpbnZhbGlkIHNlcXVlbmNlcuB5eDSgqiQVDBBhbHJlYWR5IGNvbnN1bWVk4Hl4NKhwaDWl6v//C5ckIAwbY2Vuc29yc2hpcCBhbHJlYWR5IHJlcG9ydGVk4Hl4NfL4//81d+r//3FpC5gkFAwPZW50cnkgbm90IGZvdW5k4GnbMHJqygA8uCQUDA9lbnRyeSBtYWxmb3JtZWTgamrKFJ9KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUDWgAAAAc0G3w4gDAegDoUoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF0bGu1JgUJIm0MAQHbMGg1L/j//zUL7P//dW1K2SgkBkUJIgbKABSzJAUJIgZtELOqJhl4EcAfDApwYXVzZUNoYWlubUFifVtSRXp5eBPADBtTZXF1ZW5jZXJDZW5zb3JzaGlwUmVwb3J0ZWRBlQFvYQgiAkBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSIgJAVwMDNS/o//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB6StkoJAZFCSIGygAUsyQFCSIGehCzqiQWDBFpbnZhbGlkIHNlcXVlbmNlcuB5eDUZ/f//NRXo//8LmCQZDBRubyBjZW5zb3JzaGlwIHJlcG9ydOB5eDXPAAAAcGg17Of//wuXJBQMD2FscmVhZHkgc2xhc2hlZOA1TOr//3FpELckIAwbc2xhc2ggYW1vdW50IG5vdCBjb25maWd1cmVk4DWv6f//cmpK2SgkBkUJIgbKABSzJAUJIgZqELOqJBkMFHNlcXVlbmNlciBib25kIHVuc2V04AwBAdswaDXM9f//QTlTbjxpengUwB8MBXNsYXNoakFifVtSRXp5eBPADB1TZXF1ZW5jZXJTbGFzaGVkRm9yQ2Vuc29yc2hpcEGVAW9hQFcAAnl4HDWd9f//QFcAAnl4NO81Eef//wuYIgJA/uR1+g==").AsSerializable<Neo.SmartContract.NefFile>();

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
