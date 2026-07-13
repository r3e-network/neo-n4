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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ChainRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":113,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":212,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":336,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1283,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1421,""safe"":true},{""name"":""registerPauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1479,""safe"":false},{""name"":""revokePauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1736,""safe"":false},{""name"":""isPauser"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1821,""safe"":true},{""name"":""registerChainPublic"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1838,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2751,""safe"":false},{""name"":""updateChainViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3049,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":4493,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":591,""safe"":true},{""name"":""buildUpdateChainAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":3893,""safe"":true},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4698,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4913,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":5103,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5133,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5169,""safe"":true},{""name"":""getDAMode"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5201,""safe"":true},{""name"":""getGatewayEnabled"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5233,""safe"":true},{""name"":""getPermissionlessExit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5267,""safe"":true},{""name"":""getSequencerModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5301,""safe"":true},{""name"":""getExitModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5333,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":5365,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainPaused"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ChainResumed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""L2 chain admission and per-chain config registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ChainRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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ZXJoQWJ9W1J3B28HJFcMUmJyaWRnZSBhZGFwdGVyIG5vdCBpbiBHb3Zlcm5hbmNlQ29udHJvbGxlciBhcHByb3ZlZCBzZXQgKHNlbWktcGVybWlzc2lvbmxlc3MgbW9kZSngeXg1uff//0BBYn1bUkDbKErYJAlKygAUKAM6QFcAAjWv9f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNXD3//+qJFIMTWdvdmVybmFuY2UgbG9ja2VkIOKAlCBpbnN0YW50IG93bmVyIHBhdGggZGlzYWJsZWQ7IHVzZSBVcGRhdGVDaGFpblZpYVByb3Bvc2Fs4HgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HnKAFuXJBkMFGNvbmZpZyBzaXplIG1pc21hdGNo4Hk1jvf//3iXJBUMEGNoYWluSWQgbWlzbWF0Y2jgeTXh9///eDU+9v//NQb1//8LmCQZDBRjaGFpbiBub3QgcmVnaXN0ZXJlZOB5eDUX9v//NbD4//95eBLADA9DaGFpblJlZ2lzdGVyZWRBlQFvYUBXBQM1ofn//3BoDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkVgxRZ292ZXJuYW5jZSBjb250cm9sbGVyIG5vdCB3aXJlZCDigJQgb3duZXIgbXVzdCBjYWxsIFNldEdvdmVybmFuY2VDb250cm9sbGVyIGZpcnN04HgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HnKAFuXJBkMFGNvbmZpZyBzaXplIG1pc21hdGNo4Hk1Zfb//3iXJBUMEGNoYWluSWQgbWlzbWF0Y2jgeTW49v//eDUV9f//Nd3z//8LmCQZDBRjaGFpbiBub3QgcmVnaXN0ZXJlZOB6NW0BAABxaTW18///C5ckHgwZcHJvcG9zYWwgYWxyZWFkeSBjb25zdW1lZOB6EcAVDBdpc0FwcHJvdmVkQW5kVGltZWxvY2tlZGhBYn1bUnJqJFMMTnByb3Bvc2FsIG5vdCBhcHByb3ZlZCArIHRpbWVsb2NrZWQgKGNvdW5jaWwgbXVsdGlzaWcgKyB0aW1lbG9jayBub3Qgc2F0aXNmaWVkKeB5eDWjAQAAc2t6EsAVDBZtYXRjaGVzUHJvcG9zYWxQYXlsb2FkaEFifVtSdGwkagxlcHJv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").AsSerializable<Neo.SmartContract.NefFile>();

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
