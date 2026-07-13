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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ForcedInclusion"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":549,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":648,""safe"":false},{""name"":""getDeadlineSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":769,""safe"":true},{""name"":""setDeadlineSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":833,""safe"":false},{""name"":""getFee"",""parameters"":[],""returntype"":""Integer"",""offset"":971,""safe"":true},{""name"":""getFeeRecipient"",""parameters"":[],""returntype"":""Hash160"",""offset"":1007,""safe"":true},{""name"":""getGasToken"",""parameters"":[],""returntype"":""Hash160"",""offset"":1065,""safe"":true},{""name"":""getSequencerBond"",""parameters"":[],""returntype"":""Hash160"",""offset"":1123,""safe"":true},{""name"":""getChainRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":1181,""safe"":true},{""name"":""getCensorshipSlashAmount"",""parameters"":[],""returntype"":""Integer"",""offset"":1239,""safe"":true},{""name"":""isProductionReady"",""parameters"":[],""returntype"":""Boolean"",""offset"":1275,""safe"":true},{""name"":""setFee"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1433,""safe"":false},{""name"":""setFeeRecipient"",""parameters"":[{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1680,""safe"":false},{""name"":""setGasToken"",""parameters"":[{""name"":""gasContract"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1805,""safe"":false},{""name"":""setSequencerBond"",""parameters"":[{""name"":""sequencerBond"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1925,""safe"":false},{""name"":""setChainRegistry"",""parameters"":[{""name"":""chainRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2049,""safe"":false},{""name"":""setCensorshipSlashAmount"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2173,""safe"":false},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""encodedTx"",""type"":""ByteArray""},{""name"":""txHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2303,""safe"":false},{""name"":""getEntry"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4540,""safe"":true},{""name"":""markConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4574,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4733,""safe"":true},{""name"":""isCensorshipReported"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4750,""safe"":true},{""name"":""reportCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":4779,""safe"":false},{""name"":""slashReportedCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5431,""safe"":false},{""name"":""isCensorshipSlashed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5768,""safe"":true}],""events"":[{""name"":""ForcedTxEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""ForcedTxConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SequencerCensorshipReported"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""SequencerSlashedForCensorship"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""DeadlineSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeRecipientChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GasTokenChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""ForcedInclusionFeeCharged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""SequencerBondChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""ChainRegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""CensorshipSlashAmountChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Forced-inclusion queue per L2 chain \u2014 anti-censorship primitive."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ForcedInclusion"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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HgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFesogqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFesoAGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRRB1Im56bc5KaWptnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtesq1JJBqesqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRXtKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXsYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeyCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV7ABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVpIgJA2zBA2zBAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcAAnl4EjQDQFcBAx2IcHhKaBBR0EV5ShAuBCIISgH/ADIGAf8AkUpoEVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV6ShAuBCIISgH/ADIGAf8AkUpoFVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV6ACCpShAuBCIISgH/ADIGAf8AkUpoGVHQRXoAKKlKEC4EIghKAf8AMgYB/wCRSmgaUdBFegAwqUoQLgQiCEoB/wAyBgH/AJFKaBtR0EV6ADipShAuBCIISgH/ADIGAf8AkUpoHFHQRWgiAkBBYn1bUkBXAQJ5eDWt/v//NZbw//9waAuXJgYQiCIFaNswIgJA2zBAVwICDAH92zA1dvD//0rYJg5FDAhzbSB1bnNldDpK2CQJSsoAFCgDOnBoQfgn7IwkGwwWbm90IHNldHRsZW1lbnQgbWFuYWdlcuB5eDRHcWk1LvD//wuXJBUMEGFscmVhZHkgY29uc3VtZWTgDAEB2zBpNQj+//95eBLADBBGb3JjZWRUeENvbnN1bWVkQZUBb2FAVwACeXgTNQD+//9AVwACeXg07zXY7///C5giAkBXAAJ5eDQMNcfv//8LmCICQFcAAnl4GDXS/f//QFcGA3pK2SgkBkUJIgbKABSzJAUJIgZ6ELOqJBYMEWludmFsaWQgc2VxdWVuY2Vy4Hl4NKCqJBUMEGFscmVhZHkgY29uc3VtZWTgeXg0qHBoNWHv//8LlyQgDBtjZW5zb3JzaGlwIGFscmVhZHkgcmVwb3J0ZWTgeXg1Sv3//zUz7///cWkLmCQUDA9lbnRyeSBub3QgZm91bmTgadswcmrKADy4JBQMD2VudHJ5IG1hbGZvcm1lZOBqasoUn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9QNaAAAABzQbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXRsa7UmBQkibQwBAdswaDWH/P//Ncfw//91bUrZKCQGRQkiBsoAFLMkBQkiBm0Qs6omGXgRwB8MCnBhdXNlQ2hhaW5tQWJ9W1JFenl4E8AMG1NlcXVlbmNlckNlbnNvcnNoaXBSZXBvcnRlZEGVAW9hCCICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAwM16+z//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HpK2SgkBkUJIgbKABSzJAUJIgZ6ELOqJBYMEWludmFsaWQgc2VxdWVuY2Vy4Hl4NRn9//810ez//wuYJBkMFG5vIGNlbnNvcnNoaXAgcmVwb3J04Hl4Nc8AAABwaDWo7P//C5ckFAwPYWxyZWFkeSBzbGFzaGVk4DUI7///cWkQtyQgDBtzbGFzaCBhbW91bnQgbm90IGNvbmZpZ3VyZWTgNWvu//9yakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okGQwUc2VxdWVuY2VyIGJvbmQgdW5zZXTgDAEB2zBoNST6//9BOVNuPGl6eBTAHwwFc2xhc2hqQWJ9W1JFenl4E8AMHVNlcXVlbmNlclNsYXNoZWRGb3JDZW5zb3JzaGlwQZUBb2FAVwACeXgcNfX5//9AVwACeXg07zXN6///C5giAkDr8RR0").AsSerializable<Neo.SmartContract.NefFile>();

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
    [DisplayName("enqueueForcedTransaction")]
    public abstract BigInteger? EnqueueForcedTransaction(BigInteger? chainId, byte[]? encodedTx, UInt256? txHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("markConsumed")]
    public abstract void MarkConsumed(BigInteger? chainId, BigInteger? nonce);

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
