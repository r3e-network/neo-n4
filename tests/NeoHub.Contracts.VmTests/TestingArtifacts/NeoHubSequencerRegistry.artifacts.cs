using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSequencerRegistry(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SequencerRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":241,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":340,""safe"":false},{""name"":""getMaxCommitteeSize"",""parameters"":[],""returntype"":""Integer"",""offset"":461,""safe"":true},{""name"":""getExitWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":495,""safe"":true},{""name"":""setMaxCommitteeSize"",""parameters"":[{""name"":""size"",""type"":""Integer""}],""returntype"":""Void"",""offset"":561,""safe"":false},{""name"":""setExitWindowSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":690,""safe"":false},{""name"":""register"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""},{""name"":""sequencerAddress"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":831,""safe"":false},{""name"":""unregister"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Integer"",""offset"":1817,""safe"":false},{""name"":""finalize"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Void"",""offset"":2217,""safe"":false},{""name"":""getActiveCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1635,""safe"":true},{""name"":""isRegistered"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Boolean"",""offset"":2554,""safe"":true},{""name"":""getStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Integer"",""offset"":2574,""safe"":true},{""name"":""getSequencerAddress"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencerKey"",""type"":""PublicKey""}],""returntype"":""Hash160"",""offset"":2606,""safe"":true}],""events"":[{""name"":""SequencerRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""}]},{""name"":""SequencerExiting"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""SequencerRemoved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""}]},{""name"":""MaxCommitteeSizeChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ExitWindowSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Per-chain dBFT sequencer pubkey registry for Neo Elastic Network L2s."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SequencerRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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EC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQNswQFcAAng0hXlQNcj5//9AVwYCeUH4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hl4NTr+//9waDXq+f//cWkLmCQTDA5ub3QgcmVnaXN0ZXJlZOBp2zByahDOEZckGQwUbm90IGN1cnJlbnRseSBhY3RpdmXgQbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkTVO+v//nkoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFzABmIdBB1Ij5qbc5KbG1R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtbMq1JMASSmwQUdBFa0oQLgQiCEoB/wAyBgH/AJFKbAAVUdBFaxipShAuBCIISgH/ADIGAf8AkUpsABZR0EVrIKlKEC4EIghKAf8AMgYB/wCRSmwAF1HQRWsAGKlKEC4EIghKAf8AMgYB/wCRSmwAGFHQRWxoNUf4//9reXgTwAwQU2VxdWVuY2VyRXhpdGluZ0GVAW9hayICQEG3w4gDQFcEAnl4NcP8//9waDVz+P//cWkLmCQTDA5ub3QgcmVnaXN0ZXJlZOBp2zByahDOEpckEAwLbm90IGV4aXRpbmfgagAVzmoAFs4YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSagAXziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJqABjOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJzQbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkWu4JBsMFmV4aXQgd2luZG93IHN0aWxsIG9wZW7gaDRJeHg1w/z//xGfShAuBCIOSgP/////AAAAADIMA/////8AAAAAkVA1R/3//3l4EsAMEFNlcXVlbmNlclJlbW92ZWRBlQFvYUBXAAF4QZv2Z85BL1jF7UBBL1jF7UBXAAJ5eDVy+///NST3//8LmCICQFcBAnl4NV77//81EPf//3BoC5cmBRAiB2jbMBDOIgJAVwQCeXg1Pvv//zXw9v//cGgLlyYdDBQAAAAAAAAAAAAAAAAAAAAAAAAAACOTAAAAaNswcQAUiHIQcyJuaRFrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmprUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAUtSSQatsoStgkCUrKABQoAzoiAkDbKErYJAlKygAUKAM6QD+4wPU=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delExitWindowSecondsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ExitWindowSecondsChanged")]
    public event delExitWindowSecondsChanged? OnExitWindowSecondsChanged;

    public delegate void delMaxCommitteeSizeChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("MaxCommitteeSizeChanged")]
    public event delMaxCommitteeSizeChanged? OnMaxCommitteeSizeChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delSequencerExiting(BigInteger? arg1, ECPoint? arg2, BigInteger? arg3);

    [DisplayName("SequencerExiting")]
    public event delSequencerExiting? OnSequencerExiting;

    public delegate void delSequencerRegistered(BigInteger? arg1, ECPoint? arg2);

    [DisplayName("SequencerRegistered")]
    public event delSequencerRegistered? OnSequencerRegistered;

    public delegate void delSequencerRemoved(BigInteger? arg1, ECPoint? arg2);

    [DisplayName("SequencerRemoved")]
    public event delSequencerRemoved? OnSequencerRemoved;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? ExitWindowSeconds { [DisplayName("getExitWindowSeconds")] get; [DisplayName("setExitWindowSeconds")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? MaxCommitteeSize { [DisplayName("getMaxCommitteeSize")] get; [DisplayName("setMaxCommitteeSize")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getActiveCount")]
    public abstract BigInteger? GetActiveCount(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSequencerAddress")]
    public abstract UInt160? GetSequencerAddress(BigInteger? chainId, ECPoint? sequencerKey);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getStatus")]
    public abstract BigInteger? GetStatus(BigInteger? chainId, ECPoint? sequencerKey);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isRegistered")]
    public abstract bool? IsRegistered(BigInteger? chainId, ECPoint? sequencerKey);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalize")]
    public abstract void Finalize(BigInteger? chainId, ECPoint? sequencerKey);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("register")]
    public abstract void Register(BigInteger? chainId, ECPoint? sequencerKey, UInt160? sequencerAddress);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("unregister")]
    public abstract BigInteger? Unregister(BigInteger? chainId, ECPoint? sequencerKey);

    #endregion
}
