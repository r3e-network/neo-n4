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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":554,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":612,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":748,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":806,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":924,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":982,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1102,""safe"":false},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5999,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7044,""safe"":false},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3726,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7600,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":7632,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":7756,""safe"":true},{""name"":""getFinalizedTxRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":7767,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":7780,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3055,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":8048,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":8066,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8144,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8866,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":9546,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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cGF0aWJsZSB3aXRoIGNoYWluJ3MgYWR2ZXJ0aXNlZCBzZWN1cml0eSBsZXZlbOAMAf3bMDXL+v//StgmHUUMF3ZlcmlmaWVyIHJlZ2lzdHJ5IHVuc2V0OkrYJAlKygAUKAM6dwx4EcAVDBB2ZXJpZnlDb21taXRtZW50bwxBYn1bUncNbw0kIQwcdmVyaWZpZXIgcmVqZWN0ZWQgY29tbWl0bWVudOAB/AB4NaoGAAB3Dm8Lbw5paDUdDQAAbwkSlyYFEiIDEXcPEYhKEG8P0G01uA0AAHhpaDXGDQAANasNAAABnAB4NW4GAAB3EG8Q2zBpaDW6DQAANZANAABvCRKXJmg1ifr//3cRbxFK2SgkBkUJIgbKABSzJAUJIgdvERCzqiQjDB5vcHRpbWlzdGljIGNoYWxsZW5nZSBub3Qgd2lyZWTgeDVyDQAAdxJvEmloE8AfDApvcGVuV2luZG93bxFBYn1bUkUAPHg16AUAAHcRbxFpaBPADA5CYXRjaFN1Ym1pdHRlZEGVAW9hQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkVnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkWnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkXnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSIgJAQWJ9W1JAVwEBeDQ1Naf1//9waAuXJgUQIiRoStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6IgJAVwEBFYhwFEpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBK2CYGRRAiBNshQFcAAnl4ETQDQFcBAx2IcHhKaBBR0EV5ShAuBCIISgH/ADIGAf8AkUpoEVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV6ShAuBCIISgH/ADIGAf8AkUpoFVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV6ACCpShAuBCIISgH/ADIGAf8AkUpoGVHQRXoAKKlKEC4E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AIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsAUEBtSS/ayICQFcBAng1e+z//3B5aHg0BSICQFcEA3l4NSDt//81EOL//3BoC5cmBQkiN2jbMBDOcWkTmCYFCSIpeXg1ofX//zXt4f//cmoLlyYFCSIUakrYJAlKygAgKAM6c2t6lyICQFcLBXl4NdLs//81wuH//3BoC5cmCAkjuwIAAGjbMBDOcWkTmCYICSOqAgAAeXg1TfX//zWZ4f//cmoLlyYICSOSAgAAakrYJAlKygAgKAM6c3sLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7dGzKAEC2JBMMDnByb29mIHRvbyBkZWVw4HrbMHV8dhB3ByMoAgAAbG8HzncIbwjKACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh3CW4RkRCXJ9YAAAAQdwoiQ21vCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuhB3CiJ1bwhvCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIgj0QAAABB3CiJEbwhvCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuRB3CiJ0bW8KzkpvCQAgbwqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkiW8J2yg3AAB3Cm8KNwAA2zBKdUVuEalKdkVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HbMq1Jdj9//9rbdsoStgkCUrKACAoAzqXIgJAVwgEeDXo6///cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN2AgAAeguYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HpxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgedswcntzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2hq2yhK2CQJSsoAICgDOpciAkBWAUCN1sQU").AsSerializable<Neo.SmartContract.NefFile>();

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
    [DisplayName("getChallengeableBatchHeader")]
    public abstract byte[]? GetChallengeableBatchHeader(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getFinalizedTxRoot")]
    public abstract UInt256? GetFinalizedTxRoot(BigInteger? chainId, BigInteger? batchNumber);

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
