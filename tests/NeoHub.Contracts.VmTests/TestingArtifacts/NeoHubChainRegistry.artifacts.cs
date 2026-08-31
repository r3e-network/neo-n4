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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ChainRegistry"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":113,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":212,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":336,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1740,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1977,""safe"":true},{""name"":""registerPauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2035,""safe"":false},{""name"":""registerPauserViaProposal"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2391,""safe"":false},{""name"":""revokePauser"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":3400,""safe"":false},{""name"":""revokePauserViaProposal"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3623,""safe"":false},{""name"":""isPauser"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":3653,""safe"":true},{""name"":""registerChainPublic"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":3670,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":4644,""safe"":false},{""name"":""updateChainViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4943,""safe"":false},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":5418,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":592,""safe"":true},{""name"":""buildUpdateChainAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":5136,""safe"":true},{""name"":""buildRegisterPauserAction"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":3053,""safe"":true},{""name"":""buildRevokePauserAction"",""parameters"":[{""name"":""pauser"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":3640,""safe"":true},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5633,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5848,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6038,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":6068,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6139,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6175,""safe"":true},{""name"":""getDAMode"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6207,""safe"":true},{""name"":""getGatewayEnabled"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6239,""safe"":true},{""name"":""getPermissionlessExit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6273,""safe"":true},{""name"":""getSequencerModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6307,""safe"":true},{""name"":""getExitModel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6339,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":6371,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""GenesisStateRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""ChainPaused"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ChainResumed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PauserRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""L2 chain admission and per-chain config registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ChainRegistry"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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").AsSerializable<Neo.SmartContract.NefFile>();

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
    [DisplayName("buildRegisterPauserAction")]
    public abstract byte[]? BuildRegisterPauserAction(UInt160? pauser);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRevokePauserAction")]
    public abstract byte[]? BuildRevokePauserAction(UInt160? pauser);

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
    [DisplayName("registerPauserViaProposal")]
    public abstract void RegisterPauserViaProposal(UInt160? pauser, BigInteger? proposalId);

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
    [DisplayName("revokePauserViaProposal")]
    public abstract void RevokePauserViaProposal(UInt160? pauser, BigInteger? proposalId);

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
