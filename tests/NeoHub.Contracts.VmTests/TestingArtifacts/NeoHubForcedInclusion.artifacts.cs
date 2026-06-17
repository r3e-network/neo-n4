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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ForcedInclusion"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":549,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":648,""safe"":false},{""name"":""getDeadlineSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":769,""safe"":true},{""name"":""setDeadlineSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":833,""safe"":false},{""name"":""getFee"",""parameters"":[],""returntype"":""Integer"",""offset"":971,""safe"":true},{""name"":""getFeeRecipient"",""parameters"":[],""returntype"":""Hash160"",""offset"":1007,""safe"":true},{""name"":""getGasToken"",""parameters"":[],""returntype"":""Hash160"",""offset"":1065,""safe"":true},{""name"":""getSequencerBond"",""parameters"":[],""returntype"":""Hash160"",""offset"":1123,""safe"":true},{""name"":""getChainRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":1181,""safe"":true},{""name"":""getCensorshipSlashAmount"",""parameters"":[],""returntype"":""Integer"",""offset"":1239,""safe"":true},{""name"":""setFee"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1275,""safe"":false},{""name"":""setFeeRecipient"",""parameters"":[{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1522,""safe"":false},{""name"":""setGasToken"",""parameters"":[{""name"":""gasContract"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1647,""safe"":false},{""name"":""setSequencerBond"",""parameters"":[{""name"":""sequencerBond"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1767,""safe"":false},{""name"":""setChainRegistry"",""parameters"":[{""name"":""chainRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1891,""safe"":false},{""name"":""setCensorshipSlashAmount"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2015,""safe"":false},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""encodedTx"",""type"":""ByteArray""},{""name"":""txHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2145,""safe"":false},{""name"":""getEntry"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4382,""safe"":true},{""name"":""markConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4416,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4575,""safe"":true},{""name"":""isCensorshipReported"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4592,""safe"":true},{""name"":""reportCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":4621,""safe"":false},{""name"":""slashReportedCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5273,""safe"":false},{""name"":""isCensorshipSlashed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5610,""safe"":true}],""events"":[{""name"":""ForcedTxEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""ForcedTxConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SequencerCensorshipReported"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""SequencerSlashedForCensorship"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""DeadlineSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FeeRecipientChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GasTokenChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""ForcedInclusionFeeCharged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""SequencerBondChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""ChainRegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""CensorshipSlashAmountChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Forced-inclusion queue per L2 chain \u2014 anti-censorship primitive."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ForcedInclusion"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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Z2VkQZUBb2F6aGt4FMAMEEZvcmNlZFR4RW5xdWV1ZWRBlQFvYWsiAkBBOVNuPEBXAQEViHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQEG3w4gDQFcGBAA4esqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BoiHEQcnjbMHMQdCJua2zOSmlqbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQagAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV52zB0EHUibmxtzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkkGoAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFespKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8A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").AsSerializable<Neo.SmartContract.NefFile>();

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
