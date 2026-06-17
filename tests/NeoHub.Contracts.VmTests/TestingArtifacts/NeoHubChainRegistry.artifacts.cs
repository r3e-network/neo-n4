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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ChainRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":113,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":212,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":336,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1091,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1229,""safe"":true},{""name"":""registerPauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1287,""safe"":false},{""name"":""revokePauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1545,""safe"":false},{""name"":""isPauser"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1630,""safe"":true},{""name"":""registerChainPublic"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1647,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2560,""safe"":false},{""name"":""updateChainViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2859,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":4304,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":591,""safe"":true},{""name"":""buildUpdateChainAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":3704,""safe"":true},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4509,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4724,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4914,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4944,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4980,""safe"":true},{""name"":""getDAMode"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5012,""safe"":true},{""name"":""getGatewayEnabled"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5044,""safe"":true},{""name"":""getPermissionlessExit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5078,""safe"":true},{""name"":""getSequencerModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5112,""safe"":true},{""name"":""getExitModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5144,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":5176,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainPaused"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ChainResumed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""L2 chain admission and per-chain config registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ChainRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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aW5zdGFudCBvd25lciBwYXRoIGRpc2FibGVkOyB1c2UgVXBkYXRlQ2hhaW5WaWFQcm9wb3NhbOB4ELckIQwcY2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB5ygBblyQZDBRjb25maWcgc2l6ZSBtaXNtYXRjaOB5NU74//94lyQVDBBjaGFpbklkIG1pc21hdGNo4Hk1ofj//3g1/fb//zXF9f//C5gkGQwUY2hhaW4gbm90IHJlZ2lzdGVyZWTgeDXX9v//eVA1rvj//3l4EsAMD0NoYWluUmVnaXN0ZXJlZEGVAW9hQFcFAzWf+f//cGgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCRWDFFnb3Zlcm5hbmNlIGNvbnRyb2xsZXIgbm90IHdpcmVkIOKAlCBvd25lciBtdXN0IGNhbGwgU2V0R292ZXJuYW5jZUNvbnRyb2xsZXIgZmlyc3TgeBC3JCEMHGNoYWluSWQgMCBpcyByZXNlcnZlZCBmb3IgTDHgecoAW5ckGQwUY29uZmlnIHNpemUgbWlzbWF0Y2jgeTUk9///eJckFQwQY2hhaW5JZCBtaXNtYXRjaOB5NXf3//94NdP1//81m/T//wuYJBkMFGNoYWluIG5vdCByZWdpc3RlcmVk4Ho1bgEAAHFpNXP0//8LlyQeDBlwcm9wb3NhbCBhbHJlYWR5IGNvbnN1bWVk4HoRwBUMF2lzQXBwcm92ZWRBbmRUaW1lbG9ja2VkaEFifVtScmokUwxOcHJvcG9zYWwgbm90IGFwcHJvdmVkICsgdGltZWxvY2tlZCAoY291bmNpbCBtdWx0aXNpZyArIHRpbWVsb2NrIG5vdCBzYXRpc2ZpZWQp4Hl4NaQBAABza3oSwBUMFm1hdGNoZXNQcm9wb3NhbFBheWxvYWRoQWJ9W1J0bCRqDGVwcm9wb3NhbCBwYXlsb2FkIGRvZXMgbm90IG1hdGNoIChjaGFpbklkLCBjb25maWdCeXRlcykgYWN0aW9uIGFyZ3MgKGNvdW5jaWwgdm90ZWQgb24gZGlmZmVyZW50IGJ5dGVzKeAMAQHbMGk1Svb//3g1ZvT//3lQNT32//95eBLADA9DaGFpblJlZ2lzdGVyZWRBlQFvYUBXAQEZiHAWSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlK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").AsSerializable<Neo.SmartContract.NefFile>();

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
