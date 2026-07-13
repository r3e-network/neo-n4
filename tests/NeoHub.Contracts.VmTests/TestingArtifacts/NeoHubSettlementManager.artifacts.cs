using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSettlementManager(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":554,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":612,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":748,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":806,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":924,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":982,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1102,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6001,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7048,""safe"":false},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3728,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7606,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":7638,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":7762,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3057,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":7773,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":7791,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":7869,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8591,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":9271,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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cGF0aWJsZSB3aXRoIGNoYWluJ3MgYWR2ZXJ0aXNlZCBzZWN1cml0eSBsZXZlbOAMAf3bMDXL+v//StgmHUUMF3ZlcmlmaWVyIHJlZ2lzdHJ5IHVuc2V0OkrYJAlKygAUKAM6dwx4EcAVDBB2ZXJpZnlDb21taXRtZW50bwxBYn1bUncNbw0kIQwcdmVyaWZpZXIgcmVqZWN0ZWQgY29tbWl0bWVudOAB/AB4NawGAAB3Dm8Lbw5paDUfDQAAbwkSlyYFEiIDEXcPEYhKEG8P0G01ug0AAGloNckNAAB4UDWsDQAAAZwAeDVvBgAAdxBpaDW8DQAAbxDbMFA1kA0AAG8JEpcmaDWH+v//dxFvEUrZKCQGRQkiBsoAFLMkBQkiB28RELOqJCMMHm9wdGltaXN0aWMgY2hhbGxlbmdlIG5vdCB3aXJlZOB4NXINAAB3Em8SaWgTwB8MCm9wZW5XaW5kb3dvEUFifVtSRQA8eDXoBQAAdxFvEWloE8AMDkJhdGNoU3VibWl0dGVkQZUBb2FAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkiICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRWeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AKKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRaeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AMKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eReeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AOKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZIiAkBBYn1bUkBXAQF4NDU1pfX//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAQEViHAUSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQErYJgZFECIE2yFAVwACeXgRNANAVwEDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXpKEC4EIghKAf8AMgYB/wCRSmgVUdBFehipShAuBCIISgH/ADIGAf8AkUpoFlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFegAoqUoQ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CSIIaUH4J+yMcmgmBQgiA2okEwwObm90IGF1dGhvcml6ZWTgeXg1zvD//zW85f//c2sLmCQSDA1iYXRjaCB1bmtub3du4GvbMBDOdGwUmCQbDBZiYXRjaCBhbHJlYWR5IHJldmVydGVk4GokBQkiBGiqJkNsEpckPgw5T3B0aW1pc3RpY0NoYWxsZW5nZSBjYW4gb25seSByZXZlcnQgY2hhbGxlbmdlYWJsZSBiYXRjaGVz4GwTlycJAQAAeXg1fe///5ckNAwvb25seSB0aGUgbGF0ZXN0IGZpbmFsaXplZCBiYXRjaCBjYW4gYmUgcmV2ZXJ0ZWTgeRG3J7YAAAB5EZ9KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXg1WPj//zWx5P//dW0LmCQiDB1wcmV2aW91cyBiYXRjaCBoZWFkZXIgbWlzc2luZ+AAPG3bMDXZ8P//dng1svH//27bMFA1/ff//3kRn0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACReDUE/v//IhF4NWzx//80NBB4NfP9//95eDUx7///DAEE2zBQNaX3//95eBLADA1CYXRjaFJldmVydGVkQZUBb2FAVwABeEGb9mfOQS9Yxe1AQS9Yxe1AVwECeXg17u7//zXc4///cGgLlyYFECIHaNswEM4iAkBXAAIBvAB5eDQDQFcBA3l4NNATmCYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACJDeXg1LPf//zWF4///cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACILemjbMDWq7///IgJAVwACAdwAeXg0h0BXAQJ4NZDt//9weWh4NAUiAkBXBAN5eDU17v//NSPj//9waAuXJgUJIjdo2zAQznFpE5gmBQkiKXl4NbP2//81AOP//3JqC5cmBQkiFGpK2CQJSsoAICgDOnNrepciAkBXCwV5eDXn7f//NdXi//9waAuXJggJI7sCAABo2zAQznFpE5gmCAkjqgIAAHl4NV/2//81rOL//3JqC5cmCAkjkgIAAGpK2CQJSsoAICgDOnN7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3RsygBAtiQTDA5wcm9vZiB0b28gZGVlcOB62zB1fHYQdwcjKAIAAGxvB853CG8IygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdwluEZEQlyfWAAAAEHcKIkNtbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLoQdwoidW8IbwrOSm8JACBvCp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSSII9EAAAAQdwoiRG8IbwrOSm8JbwpR0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JLkQdwoidG1vCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIlvCdsoNwAAdwpvCjcAANswSnVFbhGpSnZFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvB2zKtSXY/f//a23bKErYJAlKygAgKAM6lyICQFcIBHg1/ez//3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCAkjdgIAAHoLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB6cWnKAEC2JBMMDnByb29mIHRvbyBkZWVw4HnbMHJ7cxB0IxsCAABpbM51bcoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHZrEZEQlyfTAAAAEHcHIkJqbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzbW8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKI84AAAAQdwciQm1vB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNqbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIpu2yg3AAB3B28HNwAA2zBKckVrEalKc0VsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsacq1Jeb9//9oatsoStgkCUrKACAoAzqXIgJAVgFAKZxHIQ==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delBatchFinalized(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchFinalized")]
    public event delBatchFinalized? OnBatchFinalized;

    public delegate void delBatchReverted(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("BatchReverted")]
    public event delBatchReverted? OnBatchReverted;

    public delegate void delBatchSubmitted(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchSubmitted")]
    public event delBatchSubmitted? OnBatchSubmitted;

    public delegate void delDARegistryChanged(UInt160? obj);

    [DisplayName("DARegistryChanged")]
    public event delDARegistryChanged? OnDARegistryChanged;

    public delegate void delDAValidatorChanged(UInt160? obj);

    [DisplayName("DAValidatorChanged")]
    public event delDAValidatorChanged? OnDAValidatorChanged;

    public delegate void delOptimisticChallengeChanged(UInt160? obj);

    [DisplayName("OptimisticChallengeChanged")]
    public event delOptimisticChallengeChanged? OnOptimisticChallengeChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? DARegistry { [DisplayName("getDARegistry")] get; [DisplayName("setDARegistry")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? DAValidator { [DisplayName("getDAValidator")] get; [DisplayName("setDAValidator")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? OptimisticChallenge { [DisplayName("getOptimisticChallenge")] get; [DisplayName("setOptimisticChallenge")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

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
    [DisplayName("getL2ToL1MessageRoot")]
    public abstract UInt256? GetL2ToL1MessageRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL2MessageRoot")]
    public abstract UInt256? GetL2ToL2MessageRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLatestFinalizedBatch")]
    public abstract BigInteger? GetLatestFinalizedBatch(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyStateLeafWithProof")]
    public abstract bool? VerifyStateLeafWithProof(BigInteger? chainId, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);

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
    [DisplayName("finalizeBatch")]
    public abstract void FinalizeBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revertBatch")]
    public abstract void RevertBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    #endregion
}
