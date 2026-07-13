using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubChainRegistry(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ChainRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":94,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":181,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":305,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1236,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1374,""safe"":true},{""name"":""registerPauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1432,""safe"":false},{""name"":""revokePauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1689,""safe"":false},{""name"":""isPauser"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1762,""safe"":true},{""name"":""registerChainPublic"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1779,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2692,""safe"":false},{""name"":""updateChainViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2990,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":4434,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":560,""safe"":true},{""name"":""buildUpdateChainAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":3834,""safe"":true},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4639,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4854,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":5044,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5074,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5110,""safe"":true},{""name"":""getDAMode"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5142,""safe"":true},{""name"":""getGatewayEnabled"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5174,""safe"":true},{""name"":""getPermissionlessExit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5208,""safe"":true},{""name"":""getSequencerModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5242,""safe"":true},{""name"":""getExitModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5274,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":5306,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainPaused"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ChainResumed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""L2 chain admission and per-chain config registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ChainRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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YXBwcm92ZWQgc2V0IChzZW1pLXBlcm1pc3Npb25sZXNzIG1vZGUp4Hl4NdX3//9AQWJ9W1JA2yhK2CQJSsoAFCgDOkBXAAI11/X//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DWM9///qiRSDE1nb3Zlcm5hbmNlIGxvY2tlZCDigJQgaW5zdGFudCBvd25lciBwYXRoIGRpc2FibGVkOyB1c2UgVXBkYXRlQ2hhaW5WaWFQcm9wb3NhbOB4ELckIQwcY2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB5ygBblyQZDBRjb25maWcgc2l6ZSBtaXNtYXRjaOB5Nar3//94lyQVDBBjaGFpbklkIG1pc21hdGNo4Hk1/ff//3g1Wvb//0HVjV7oC5gkGQwUY2hhaW4gbm90IHJlZ2lzdGVyZWTgeXg1M/b//0E5DOMKeXgSwAwPQ2hhaW5SZWdpc3RlcmVkQZUBb2FAVwUDNa35//9waAwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJFYMUWdvdmVybmFuY2UgY29udHJvbGxlciBub3Qgd2lyZWQg4oCUIG93bmVyIG11c3QgY2FsbCBTZXRHb3Zlcm5hbmNlQ29udHJvbGxlciBmaXJzdOB4ELckIQwcY2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB5ygBblyQZDBRjb25maWcgc2l6ZSBtaXNtYXRjaOB5NYH2//94lyQVDBBjaGFpbklkIG1pc21hdGNo4Hk11Pb//3g1MfX//0HVjV7oC5gkGQwUY2hhaW4gbm90IHJlZ2lzdGVyZWTgejVtAQAAcWlB1Y1e6AuXJB4MGXByb3Bvc2FsIGFscmVhZHkgY29uc3VtZWTgehHAFQwXaXNBcHByb3ZlZEFuZFRpbWVsb2NrZWRoQWJ9W1JyaiRTDE5wcm9wb3NhbCBub3QgYXBwcm92ZWQgKyB0aW1lbG9ja2VkIChjb3VuY2lsIG11bHRpc2lnICsgdGltZWxvY2sgbm90IHNhdGlzZmllZCngeXg1owEAAHNrehLAFQwWbWF0Y2hlc1Byb3Bvc2FsUGF5bG9hZGhBYn1bUnRsJGoMZXByb3Bvc2FsIHBheWxvYWQgZG9lcyBub3QgbWF0Y2ggKGNoYWluSWQsIGNvbmZpZ0J5dGVzKSBhY3Rpb24gYXJncyAoY291bmNpbCB2b3RlZCBvbiBkaWZmZXJlbnQgYnl0ZXMp4AwBAdswaUE5DOMKeXg1w/P//0E5DOMKeXgSwAwPQ2hhaW5SZWdpc3RlcmVkQZUBb2FAVwEBGYhwFkpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXgAIKlKEC4EIghKAf8AMgYB/wCRSmgVUdBFeAAoqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV4ADCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXgAOKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFaCICQFcDAlhwaMoUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ95yp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHEQciI+aGrOSmlqUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFamjKtSTAeEoQLgQiCEoB/wAyBgH/AJFKaWjKUdBFeBipShAuBCIISgH/ADIGAf8AkUppaMoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV4IKlKEC4EIghKAf8AMgYB/wCRSmloyhKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmloyhOeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRRByI6IAAAB5as5KaWjKFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFannKtSVf////aSICQFcBADUJ7///Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNezz//8MFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCRcDFd3aXJlIEdvdmVybmFuY2VDb250cm9sbGVyIGJlZm9yZSBsb2NraW5nIOKAlCBlbHNlIG5vIGNoYWluIGNvbmZpZyBjb3VsZCBldmVyIGJlIHVwZGF0ZWTgDAEF2zBwaEHVjV7oC5cmJgwBAdswaEE5DOMKEMAMEEdvdmVybmFuY2VMb2NrZWRBlQFvYUBXBgFBOVNuPHA1Nu7//0H4J+yMJgUIIghoNar0//8kEwwObm90IGF1dGhvcml6ZWTgeDVr7///cWlB1Y1e6HJqC5gkGQwUY2hhaW4gbm90IHJlZ2lzdGVyZWTgatswcwBbiHQQdSI+a23OSmxtUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQBbtSTAEEpsAFpR0EVsaUE5DOMKeBHADAtDaGFpblBhdXNlZEGVAW9hQEE5U248QNswQFcFATVl7f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeDWl7v//cGhB1Y1e6HFpC5gkGQwUY2hhaW4gbm90IHJlZ2lzdGVyZWTgadswcgBbiHMQdCI+amzOSmtsUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbABbtSTAEUprAFpR0EVraEE5DOMKeBHADAxDaGFpblJlc3VtZWRBlQFvYUBXAQF4NQTu//9B1Y1e6HBoC5cmBhCIIgVo2zAiAkBXAgF4Nebt//9B1Y1e6HBoC5cmBQkiDmjbMHFpAFrOEZciAkBXAQF4NcLt//9B1Y1e6HBoC5cmBRAiCmjbMABUziICQFcBAXg1ou3//0HVjV7ocGgLlyYFECIKaNswAFXOIgJAVwEBeDWC7f//QdWNXuhwaAuXJgUJIgxo2zAAVs4RlyICQFcBAXg1YO3//0HVjV7ocGgLlyYFCSIMaNswAFfOEZciAkBXAQF4NT7t//9B1Y1e6HBoC5cmBRAiCmjbMABYziICQFcBAXg1Hu3//0HVjV7ocGgLlyYFECIKaNswAFnOIgJAVgEMFG5lbzQtZ292OnVwZGF0ZUNoYWlu2zBgQDasOAI=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delChainPaused(BigInteger? obj);

    [DisplayName("ChainPaused")]
    public event delChainPaused? OnChainPaused;

    public delegate void delChainRegistered(BigInteger? arg1, byte[]? arg2);

    [DisplayName("ChainRegistered")]
    public event delChainRegistered? OnChainRegistered;

    public delegate void delChainResumed(BigInteger? obj);

    [DisplayName("ChainResumed")]
    public event delChainResumed? OnChainResumed;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delPauserRegistered(UInt160? obj);

    [DisplayName("PauserRegistered")]
    public event delPauserRegistered? OnPauserRegistered;

    public delegate void delPauserRevoked(UInt160? obj);

    [DisplayName("PauserRevoked")]
    public event delPauserRevoked? OnPauserRevoked;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildUpdateChainAction")]
    public abstract byte[]? BuildUpdateChainAction(BigInteger? chainId, byte[]? configBytes);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getChainConfig")]
    public abstract byte[]? GetChainConfig(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getDAMode")]
    public abstract BigInteger? GetDAMode(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getExitModel")]
    public abstract BigInteger? GetExitModel(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGatewayEnabled")]
    public abstract bool? GetGatewayEnabled(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPermissionlessExit")]
    public abstract bool? GetPermissionlessExit(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSecurityLevel")]
    public abstract BigInteger? GetSecurityLevel(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSequencerModel")]
    public abstract BigInteger? GetSequencerModel(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isActive")]
    public abstract bool? IsActive(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isPauser")]
    public abstract bool? IsPauser(UInt160? who);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("pauseChain")]
    public abstract void PauseChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerChain")]
    public abstract void RegisterChain(BigInteger? chainId, byte[]? configBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerChainPublic")]
    public abstract void RegisterChainPublic(BigInteger? chainId, byte[]? configBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerPauser")]
    public abstract void RegisterPauser(UInt160? pauser);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("resumeChain")]
    public abstract void ResumeChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokePauser")]
    public abstract void RevokePauser(UInt160? pauser);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("updateChain")]
    public abstract void UpdateChain(BigInteger? chainId, byte[]? configBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("updateChainViaProposal")]
    public abstract void UpdateChainViaProposal(BigInteger? chainId, byte[]? configBytes, BigInteger? proposalId);

    #endregion
}
