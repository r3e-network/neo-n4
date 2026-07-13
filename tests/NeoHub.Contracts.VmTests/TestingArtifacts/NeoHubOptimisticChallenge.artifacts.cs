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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.OptimisticChallenge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":282,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":381,""safe"":false},{""name"":""getWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":502,""safe"":true},{""name"":""getChallengerRewardBps"",""parameters"":[],""returntype"":""Integer"",""offset"":566,""safe"":true},{""name"":""setWindowSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":639,""safe"":false},{""name"":""setChallengerRewardBps"",""parameters"":[{""name"":""bps"",""type"":""Integer""}],""returntype"":""Void"",""offset"":771,""safe"":false},{""name"":""registerFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":897,""safe"":false},{""name"":""registerPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1484,""safe"":false},{""name"":""registerPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":1659,""safe"":false},{""name"":""revokeFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2863,""safe"":false},{""name"":""isApprovedFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":3006,""safe"":true},{""name"":""isPermissionlessFraudVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":3053,""safe"":true},{""name"":""isPermissionlessFraudProfile"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""executorSemanticId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":3060,""safe"":true},{""name"":""isClaimConsumed"",""parameters"":[{""name"":""claimId"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":3428,""safe"":true},{""name"":""openWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":3584,""safe"":false},{""name"":""challenge"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""challenger"",""type"":""Hash160""},{""name"":""fraudProofBytes"",""type"":""ByteArray""},{""name"":""fraudVerifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":4343,""safe"":false},{""name"":""finalizeIfPastWindow"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5669,""safe"":false},{""name"":""isWindowOpen"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nowUnixSeconds"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5937,""safe"":true},{""name"":""getDeadline"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5976,""safe"":true}],""events"":[{""name"":""WindowOpened"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""ChallengeAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""WindowFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""FraudVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""PermissionlessVerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""FraudProfileApproved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""FraudVerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""WindowSecondsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ChallengerRewardBpsChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Versioned optimistic challenge window with profile-bound executable v4 fraud proofs."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.OptimisticChallenge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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VwMCABmIcBdKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV52zBxEHIibmlqzkpoFWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAFLUkkGgiAkBXAAE16PX//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBUMEGludmFsaWQgdmVyaWZpZXLgeDWF+P//RXg1vfn//zQheBHADBRGcmF1ZFZlcmlmaWVyUmV2b2tlZEGVAW9hQFcAAXhBm/ZnzkEvWMXtQEEvWMXtQFcAAXhK2SgkBkUJIgbKABSzqiYFCCIFeBCzJgUJIhF4NWL5//81bvX//wuYIgJAVwABCSICQFcGBHgQlyYFCCIReUrZKCQGRQkiBsoAFLOqJgUIIgV5ELMmCAkjRwEAAHl4NSD+//81LPX//3BoC5cmCAkjLwEAAGjbMHFpygBEmCYICSMeAQAAaQBAzmkAQc4YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSaQBCziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJpAEPOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJyank1svf//5gmCAkjnwAAAHrbMHN72zB0EHUjhAAAAGltzmttzpgmCAkjgQAAAGkAIG2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85sbc6YJgUJIkFtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JX3///8IIgJA2zBAVwABeDQMNefz//8LmCICQFcDAQAhiHAYSmgQUdBFeNswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVq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").AsSerializable<Neo.SmartContract.NefFile>();

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
