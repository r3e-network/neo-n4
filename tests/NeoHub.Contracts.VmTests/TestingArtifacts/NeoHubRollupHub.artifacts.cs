using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubRollupHub(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.RollupHub"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":174,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":971,""safe"":false},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1128,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1258,""safe"":false},{""name"":""registerGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""genesisRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":1385,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1623,""safe"":true},{""name"":""isChainActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1653,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1689,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":1743,""safe"":true},{""name"":""recordBatchDA"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""daCommitment"",""type"":""Hash256""},{""name"":""firstBlock"",""type"":""Integer""},{""name"":""lastBlock"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1814,""safe"":false},{""name"":""getBatchDACommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2315,""safe"":true},{""name"":""isBatchDAAvailable"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2387,""safe"":true},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""transactionBytes"",""type"":""ByteArray""},{""name"":""transactionHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2407,""safe"":false},{""name"":""getNextForcedNonce"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3080,""safe"":true},{""name"":""getPendingForcedCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3087,""safe"":true},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":3159,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":5538,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4773,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5639,""safe"":false},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4421,""safe"":true},{""name"":""getLatestFinalizedBatchNumber"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4211,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5884,""safe"":true},{""name"":""getBatchCommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":5914,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":374,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5945,""safe"":false},{""name"":""getVerifierRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":5377,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":6165,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6183,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6241,""safe"":true}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainStatusChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""GenesisRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionsConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Consolidated L1 Rollup Hub for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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ShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFQNYsAAAB6aHgTwAwZRm9yY2VkVHJhbnNhY3Rpb25FbnF1ZXVlZEGVAW9haCICQFcBAXg0NTUr9///cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcAAXgAQTV2+P//QErYJgZFECIE2yFAVwACeXgAQjX1/P//QFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAwJ5EJcmByPLAAAAeDXGAAAAcHg1eP///3FoeZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkWm2JCcMImZvcmNlZCB0cmFuc2FjdGlvbiBxdWV1ZSB1bmRlcmZsb3fgaHmeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFyeDRpalA1Qv///3loeBPADBpGb3JjZWRUcmFuc2FjdGlvbnNDb25zdW1lZEGVAW9hQFcBAXg0NTXk9f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcAAXgAQDUv9///QFcAAXg0t0BXAgF4NLBweDVl/v//cWlouCYzaWifShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJEiAxAiAkBXAgMQeDV99f//cBR4NDJxeAE8Ac5paHp5eDUyAQAAaGkAIAA8eDVJBAAA2yhK2CQJSsoAICgDOlM1vggAAEBXAgIQcBBxI/oAAABoeHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGGmgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn6hKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkUpwRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkYtSUI////aCICQFcL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AAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkjmjbKDcAAHFpNwAAStgkCUrKACAoAzoiAkA3AABA2yhAVwEADAEC2zA1pOz//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQEFifVtSQFcAAnl4ACM1gPL//0BXAAN5eDRCDAED2zBQNVru//94NUv7//95UDVx9f//eDU0/P//elA1Hev//3p5eBPADA5CYXRjaEZpbmFsaXplZEGVAW9hQFcAAnl4ACI1LPL//0BXBAMQeDUy7P//cBR4Nef2//9xeAE8Ac5yamloenl4NeL3//9paDTNcwwBAdswazXk7f//ACAAPHg16/r//9soStgkCUrKACAoAzppaBPADA5CYXRjaFN1Ym1pdHRlZEGVAW9hQFcFAjVs6///Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeDVL+v//cHloEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZckHwwabXVzdCBmaW5hbGl6ZSBzZXF1ZW50aWFsbHngeXg1Ff///3FpNSbr//9yaguYJAUJIgdqEM4RlyQWDBFiYXRjaCBub3QgcGVuZGluZ+B5eDWP/v//Nfbq//9zawuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOAAIAA8a9swNej5///bKErYJAlKygAgKAM6dGx5eDVa/v//QM5AVwECeXg1lP7//zWn6v//cGgLlyYFECIFaBDOIgJAVwECeXg1Iv7//zWJ6v//cGgLlyYGEIgiBWjbMCICQFcAATU66v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okEgwNaW52YWxpZCBvd25lcuB4DAEB2zA1Cun//0BXAgJ5eDVp////E5gmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiVnl4NX/9//815un//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiIGjbMHEAIAGcAGk1xvj//9soStgkCUrKACAoAzoiAkBXAQJ4NVr4//9weWh4NAUiAkBXAQN5eDVi////cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYFCSIHaHqXIgJAVwgFeXg1KP///3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCAkjdgIAAHsLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7cWnKAEC2JBMMDnByb29mIHRvbyBkZWVw4HrbMHJ8cxB0IxsCAABpbM51bcoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHZrEZEQlyfTAAAAEHcHIkJqbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzbW8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKI84AAAAQdwciQm1vB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNqbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIpu2yg3AAB3B28HNwAA2zBKckVrEalKc0VsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsacq1Jeb9//9oatsoStgkCUrKACAoAzqXIgJA2zBA0w8Lkw==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delBatchFinalized(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchFinalized")]
    public event delBatchFinalized? OnBatchFinalized;

    public delegate void delBatchSubmitted(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchSubmitted")]
    public event delBatchSubmitted? OnBatchSubmitted;

    public delegate void delChainRegistered(BigInteger? arg1, byte[]? arg2);

    [DisplayName("ChainRegistered")]
    public event delChainRegistered? OnChainRegistered;

    public delegate void delChainStatusChanged(BigInteger? arg1, bool? arg2);

    [DisplayName("ChainStatusChanged")]
    public event delChainStatusChanged? OnChainStatusChanged;

    public delegate void delForcedTransactionEnqueued(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("ForcedTransactionEnqueued")]
    public event delForcedTransactionEnqueued? OnForcedTransactionEnqueued;

    public delegate void delForcedTransactionsConsumed(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("ForcedTransactionsConsumed")]
    public event delForcedTransactionsConsumed? OnForcedTransactionsConsumed;

    public delegate void delGenesisRootRegistered(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GenesisRootRegistered")]
    public event delGenesisRootRegistered? OnGenesisRootRegistered;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? VerifierRegistry { [DisplayName("getVerifierRegistry")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchCommitment")]
    public abstract byte[]? GetBatchCommitment(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchDACommitment")]
    public abstract UInt256? GetBatchDACommitment(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchStatus")]
    public abstract BigInteger? GetBatchStatus(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getCanonicalStateRoot")]
    public abstract UInt256? GetCanonicalStateRoot(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getChainConfig")]
    public abstract byte[]? GetChainConfig(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGenesisStateRoot")]
    public abstract UInt256? GetGenesisStateRoot(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLatestFinalizedBatchNumber")]
    public abstract BigInteger? GetLatestFinalizedBatchNumber(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getNextForcedNonce")]
    public abstract BigInteger? GetNextForcedNonce(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPendingForcedCount")]
    public abstract BigInteger? GetPendingForcedCount(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSecurityLevel")]
    public abstract BigInteger? GetSecurityLevel(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isBatchDAAvailable")]
    public abstract bool? IsBatchDAAvailable(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isChainActive")]
    public abstract bool? IsChainActive(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isProofTypeCompatible")]
    public abstract bool? IsProofTypeCompatible(BigInteger? securityLevel, BigInteger? proofType);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeaf")]
    public abstract bool? VerifyWithdrawalLeaf(BigInteger? chainId, UInt256? leafHash);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeafAt")]
    public abstract bool? VerifyWithdrawalLeafAt(BigInteger? chainId, BigInteger? batchNumber, UInt256? leafHash);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeafWithProof")]
    public abstract bool? VerifyWithdrawalLeafWithProof(BigInteger? chainId, BigInteger? batchNumber, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("enqueueForcedTransaction")]
    public abstract BigInteger? EnqueueForcedTransaction(BigInteger? chainId, byte[]? transactionBytes, UInt256? transactionHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeBatch")]
    public abstract void FinalizeBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("pauseChain")]
    public abstract void PauseChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("recordBatchDA")]
    public abstract void RecordBatchDA(BigInteger? chainId, BigInteger? batchNumber, UInt256? daCommitment, BigInteger? firstBlock, BigInteger? lastBlock);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerChain")]
    public abstract void RegisterChain(byte[]? configBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerGenesisStateRoot")]
    public abstract void RegisterGenesisStateRoot(BigInteger? chainId, UInt256? genesisRoot);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("resumeChain")]
    public abstract void ResumeChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitAndFinalizeBatch")]
    public abstract void SubmitAndFinalizeBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("updateChain")]
    public abstract void UpdateChain(byte[]? configBytes);

    #endregion
}
