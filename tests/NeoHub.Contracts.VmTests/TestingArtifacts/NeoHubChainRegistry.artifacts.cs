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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ChainRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":113,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":212,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":336,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1740,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1977,""safe"":true},{""name"":""registerPauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2035,""safe"":false},{""name"":""revokePauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2293,""safe"":false},{""name"":""isPauser"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":2378,""safe"":true},{""name"":""registerChainPublic"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":2395,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":3375,""safe"":false},{""name"":""updateChainViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3674,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":5119,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":592,""safe"":true},{""name"":""buildUpdateChainAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":4519,""safe"":true},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5324,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5539,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":5729,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":5759,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5830,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5866,""safe"":true},{""name"":""getDAMode"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5898,""safe"":true},{""name"":""getGatewayEnabled"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5930,""safe"":true},{""name"":""getPermissionlessExit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5964,""safe"":true},{""name"":""getSequencerModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5998,""safe"":true},{""name"":""getExitModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6030,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":6062,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""GenesisStateRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""ChainPaused"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ChainResumed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""L2 chain admission and per-chain config registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ChainRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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ADIGAf8AkUpoFVHQRXgAKKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeAAwqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV4ADipShAuBCIISgH/ADIGAf8AkUpoGFHQRWgiAkBXAwJYcGjKFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfecqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxEHIiPmhqzkppalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWpoyrUkwHhKEC4EIghKAf8AMgYB/wCRSmloylHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaWjKEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeCCpShAuBCIISgH/ADIGAf8AkUppaMoSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV4ABipShAuBCIISgH/ADIGAf8AkUppaMoTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EUQciOiAAAAeWrOSmloyhSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2qeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp5yrUlX////2kiAkBXAQA1b+z//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DWa8///DBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkXAxXd2lyZSBHb3Zlcm5hbmNlQ29udHJvbGxlciBiZWZvcmUgbG9ja2luZyDigJQgZWxzZSBubyBjaGFpbiBjb25maWcgY291bGQgZXZlciBiZSB1cGRhdGVk4AwBBdswcGg1Cuz//wuXJiYMAQHbMGg1kvH//xDADBBHb3Zlcm5hbmNlTG9ja2VkQZUBb2FAVwYBQTlTbjxwNZzr//9B+CfsjCYFCCIIaDVl9P//JBMMDm5vdCBhdXRob3JpemVk4Hg13uz//3FpNaPr//9yaguYJBkMFGNoYWluIG5vdCByZWdpc3RlcmVk4GrbMHMAW4h0EHUiPmttzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AW7UkwBBKbABaUdBFbGk1wPD//3gRwAwLQ2hhaW5QYXVzZWRBlQFvYUBBOVNuPEDbMEBXBQE1y+r//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hg1GOz//3BoNd3q//9xaQuYJBkMFGNoYWluIG5vdCByZWdpc3RlcmVk4GnbMHIAW4hzEHQiPmpszkprbFHQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAW7UkwBFKawBaUdBFa2g1+u///3gRwAwMQ2hhaW5SZXN1bWVkQZUBb2FAVwEBeDV36///NT7q//9waAuXJgYQiCIFaNswIgJAVwEBeDVI7///NSDq//9waAuXJiYMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKACAoAzoiAkBXAgF4NRLr//812en//3BoC5cmBQkiDmjbMHFpAFrOEZciAkBXAQF4Ne7q//81ten//3BoC5cmBRAiCmjbMABUziICQFcBAXg1zur//zWV6f//cGgLlyYFECIKaNswAFXOIgJAVwEBeDWu6v//NXXp//9waAuXJgUJIgxo2zAAVs4RlyICQFcBAXg1jOr//zVT6f//cGgLlyYFCSIMaNswAFfOEZciAkBXAQF4NWrq//81Men//3BoC5cmBRAiCmjbMABYziICQFcBAXg1Sur//zUR6f//cGgLlyYFECIKaNswAFnOIgJAVgEMFG5lbzQtZ292OnVwZGF0ZUNoYWlu2zBgQPSDRAI=").AsSerializable<Neo.SmartContract.NefFile>();

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

    public delegate void delGenesisStateRootRegistered(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GenesisStateRootRegistered")]
    public event delGenesisStateRootRegistered? OnGenesisStateRootRegistered;

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
    [DisplayName("getGenesisStateRoot")]
    public abstract UInt256? GetGenesisStateRoot(BigInteger? chainId);

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
    public abstract void RegisterChain(BigInteger? chainId, byte[]? configBytes, UInt256? genesisStateRoot);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerChainPublic")]
    public abstract void RegisterChainPublic(BigInteger? chainId, byte[]? configBytes, UInt256? genesisStateRoot);

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
