using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubOptimisticChallenge(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.OptimisticChallenge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":282,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":381,""safe"":false},{""name"":""getWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":502,""safe"":true},{""name"":""getChallengerRewardBps"",""parameters"":[],""returntype"":""Integer"",""offset"":566,""safe"":true},{""name"":""setWindowSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":639,""safe"":false},{""name"":""setChallengerRewardBps"",""parameters"":[{""name"":""bps"",""type"":""Integer""}],""returntype"":""Void"",""offset"":771,""safe"":false},{""name"":""registerFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":897,""safe"":false},{""name"":""registerPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1484,""safe"":false},{""name"":""registerPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":1659,""safe"":false},{""name"":""revokeFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2863,""safe"":false},{""name"":""isApprovedFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":3006,""safe"":true},{""name"":""isPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":3053,""safe"":true},{""name"":""isPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":3060,""safe"":true},{""name"":""isClaimConsumed"",""parameters"":[{""name"":""claimId"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":3428,""safe"":true},{""name"":""openWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":3584,""safe"":false},{""name"":""challenge"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""challenger"",""type"":""Hash160""},{""name"":""fraudProofBytes"",""type"":""ByteArray""},{""name"":""fraudVerifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":4343,""safe"":false},{""name"":""finalizeIfPastWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5704,""safe"":false},{""name"":""isWindowOpen"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nowUnixSeconds"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5990,""safe"":true},{""name"":""getDeadline"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6029,""safe"":true}],""events"":[{""name"":""WindowOpened"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""ChallengeAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""WindowFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FraudVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PermissionlessVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""FraudProfileApproved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""FraudVerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""WindowSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ChallengerRewardBpsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Versioned optimistic challenge window with profile-bound executable v4 fraud proofs."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.OptimisticChallenge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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VwMCABmIcBdKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV52zBxEHIibmlqzkpoFWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAFLUkkGgiAkBXAAE16PX//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBUMEGludmFsaWQgdmVyaWZpZXLgeDWF+P//RXg1vfn//zQheBHADBRGcmF1ZFZlcmlmaWVyUmV2b2tlZEGVAW9hQFcAAXhBm/ZnzkEvWMXtQEEvWMXtQFcAAXhK2SgkBkUJIgbKABSzqiYFCCIFeBCzJgUJIhF4NWL5//81bvX//wuYIgJAVwABCSICQFcGBHgQlyYFCCIReUrZKCQGRQkiBsoAFLOqJgUIIgV5ELMmCAkjRwEAAHl4NSD+//81LPX//3BoC5cmCAkjLwEAAGjbMHFpygBEmCYICSMeAQAAaQBAzmkAQc4YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSaQBCziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJpAEPOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJyank1svf//5gmCAkjnwAAAHrbMHN72zB0EHUjhAAAAGltzmttzpgmCAkjgQAAAGkAIG2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85sbc6YJgUJIkFtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JX3///8IIgJA2zBAVwABeDQMNefz//8LmCICQFcDAQAhiHAYSmgQUdBFeNswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVq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c2hvCUFifVtSRW8Kbwyfdw5vDhC3Ji8MFAAAAAAAAAAAAAAAAAAAAAAAAAAAbw5vCHgUwB8MBXNsYXNobwlBYn1bUkVvCnp5eBTADBFDaGFsbGVuZ2VBY2NlcHRlZEGVAW9hQFcCAgAgiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQaNsoStgkCUrKACAoAzoiAkDbKErYJAlKygAgKAM6QFcAAXgQzngRzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4Es4gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeBPOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAAJ5eBI1C/n//0BXBAJ5eDX3+P//cGg1/er//3FpC5gkEwwObm8gb3BlbiB3aW5kb3fgadswNVn///9yQbfDiAMB6AOhShAuBCIOSgP/////AAAAADIMA/////8AAAAAkWq3JCAMG2NoYWxsZW5nZSB3aW5kb3cgc3RpbGwgb3BlbuB5eDV6////NYrq//8LlyQqDCViYXRjaCB3YXMgY2hhbGxlbmdlZDsgY2Fubm90IGZpbmFsaXpl4Gg1sPT//3l4Nev5//81pPT//wwB/NswNULq//9K2CYORQwIc20gdW5zZXQ6StgkCUrKABQoAzpzeXgSwB8MDWZpbmFsaXplQmF0Y2hrQWJ9W1JFeXgSwAwPV2luZG93RmluYWxpemVkQZUBb2FAVwIDeXg12ff//zXh6f//cGgLlyYFCSIQaNswNUv+//9xemm2IgJAVwECeXg1svf//zW66f//cGgLlyYFECIKaNswNST+//8iAkCfdrz7").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delChallengeAccepted(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, BigInteger? arg4);

    [DisplayName("ChallengeAccepted")]
    public event delChallengeAccepted? OnChallengeAccepted;

    public delegate void delChallengerRewardBpsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ChallengerRewardBpsChanged")]
    public event delChallengerRewardBpsChanged? OnChallengerRewardBpsChanged;

    public delegate void delFraudProfileApproved(BigInteger? arg1, UInt160? arg2, UInt256? arg3, UInt256? arg4);

    [DisplayName("FraudProfileApproved")]
    public event delFraudProfileApproved? OnFraudProfileApproved;

    public delegate void delFraudVerifierApproved(UInt160? obj);

    [DisplayName("FraudVerifierApproved")]
    public event delFraudVerifierApproved? OnFraudVerifierApproved;

    public delegate void delFraudVerifierRevoked(UInt160? obj);

    [DisplayName("FraudVerifierRevoked")]
    public event delFraudVerifierRevoked? OnFraudVerifierRevoked;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delPermissionlessVerifierApproved(UInt160? obj);

    [DisplayName("PermissionlessVerifierApproved")]
    public event delPermissionlessVerifierApproved? OnPermissionlessVerifierApproved;

    public delegate void delWindowFinalized(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("WindowFinalized")]
    public event delWindowFinalized? OnWindowFinalized;

    public delegate void delWindowOpened(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, UInt160? arg4);

    [DisplayName("WindowOpened")]
    public event delWindowOpened? OnWindowOpened;

    public delegate void delWindowSecondsChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("WindowSecondsChanged")]
    public event delWindowSecondsChanged? OnWindowSecondsChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? ChallengerRewardBps { [DisplayName("getChallengerRewardBps")] get; [DisplayName("setChallengerRewardBps")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? WindowSeconds { [DisplayName("getWindowSeconds")] get; [DisplayName("setWindowSeconds")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getDeadline")]
    public abstract BigInteger? GetDeadline(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedFraudVerifier")]
    public abstract bool? IsApprovedFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isClaimConsumed")]
    public abstract bool? IsClaimConsumed(UInt256? claimId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isPermissionlessFraudProfile")]
    public abstract bool? IsPermissionlessFraudProfile(BigInteger? chainId, UInt160? verifier, UInt256? executorSemanticId, UInt256? replayDomain);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isPermissionlessFraudVerifier")]
    public abstract bool? IsPermissionlessFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isWindowOpen")]
    public abstract bool? IsWindowOpen(BigInteger? chainId, BigInteger? batchNumber, BigInteger? nowUnixSeconds);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("challenge")]
    public abstract void Challenge(BigInteger? chainId, BigInteger? batchNumber, UInt160? challenger, byte[]? fraudProofBytes, UInt160? fraudVerifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeIfPastWindow")]
    public abstract void FinalizeIfPastWindow(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("openWindow")]
    public abstract BigInteger? OpenWindow(BigInteger? chainId, BigInteger? batchNumber, UInt160? sequencer);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerFraudVerifier")]
    public abstract void RegisterFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerPermissionlessFraudProfile")]
    public abstract void RegisterPermissionlessFraudProfile(BigInteger? chainId, UInt160? verifier, UInt256? executorSemanticId, UInt256? replayDomain);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerPermissionlessFraudVerifier")]
    public abstract void RegisterPermissionlessFraudVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeFraudVerifier")]
    public abstract void RevokeFraudVerifier(UInt160? verifier);

    #endregion
}
