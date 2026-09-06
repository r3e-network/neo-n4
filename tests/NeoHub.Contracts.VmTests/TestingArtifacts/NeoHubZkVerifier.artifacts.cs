using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubZkVerifier(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ZkVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""registerVerificationKey"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""isVerificationKeyRegistered"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":774,""safe"":true},{""name"":""registerProofVerifier"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":794,""safe"":false},{""name"":""getProofVerifier"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":1011,""safe"":true},{""name"":""setEnvelopeOnlyAllowed"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1067,""safe"":false},{""name"":""isEnvelopeOnlyAllowed"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1258,""safe"":true},{""name"":""disableEnvelopeOnlyPermanently"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1288,""safe"":false},{""name"":""isEnvelopeOnlyLocked"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1197,""safe"":true},{""name"":""lockProofSystemConfiguration"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":1384,""safe"":false},{""name"":""isProofSystemConfigurationLocked"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":534,""safe"":true},{""name"":""verifyProof"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1566,""safe"":true},{""name"":""getVerifierSelector"",""parameters"":[],""returntype"":""ByteArray"",""offset"":3657,""safe"":true},{""name"":""getRecursionVkRoot"",""parameters"":[],""returntype"":""ByteArray"",""offset"":3663,""safe"":true},{""name"":""verifyZkProof"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""ByteArray""},{""name"":""publicInputHash"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":2473,""safe"":true}],""events"":[{""name"":""VerificationKeyRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""ProofVerifierRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""EnvelopeOnlyModeSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""EnvelopeOnlyPermanentlyDisabled"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ProofSystemConfigurationLocked"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Unified ZK validity verifier \u002B BN254 Groth16 math for Neo Elastic Network."",""Version"":""0.2.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ZkVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AGkQzhGYJggJIyIBAABpEc5yACASaTVKAgAA2yhK2CQJSsoAICgDOnNqNUD7//81tfn//3RsC5gmG2tsStgkCUrKACAoAzqXqiYICSPfAAAAIhJrajX1+///qiYICSPNAAAAACJpNcYAAAB1bRC1JgUIIjdpygAmbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACftSYFCSJ/bQAmaTWuAQAAdgAgARwBeDWiAQAAdwdqNWj9//8mBQgiXWoRlyYQbm8Ha9swajURAgAAIkpqNVP8//93CG8IStkoJAZFCSIGygAUsyQFCSIHbwgQs6omJG5vB2vbMGoUwBUMDXZlcmlmeVprUHJvb2ZvCEFifVtSIgUJIgJAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5J4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfkkBXAgN6iHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaXq1JJFoIgJA2yhK2CQJSsoAICgDOkBXDwR4EZgmCAkjKwMAAHkLlyYFCCIHecoAIJgmCAkjFgMAAHoLlyYFCCIHesoAIJgmCAkjAQMAAHsLlyYFCCIIe8oBZAGYJggJI+sCAAA15wIAAHAQcSJEe2nOaGnOmCYICSPSAgAAaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaRS1JLsQcSJyexRpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OEJgmCAkjWQIAAGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkjDUlAgAAcRByInV7ACRqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OaWrOmCYICSPWAQAAakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSJAEAAZHs10v3//3IBgAABpAB7NcX9//9zAEABJAF7Nbn9//90NaMBAAB1NaEBAAB2NaABAAB3BzWeAQAAdwg1nAEAAHcJenkAIABEezWL/f//ACAAJHs1gf3//wAgFHs1eP3//xXAdwoQdwsj0gAAAG8KbwvO2yhvCxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnzU+AQAAEsAQDAhibjI1NE11bAwUG/V1qxGJaIQTYQo1oSiGzeC2bHJBYn1bUncMbwxvCRLAEAwIYm4yNTRBZGQMFBv1dasRiWiEE2EKNaEohs3gtmxyQWJ9W1JKdwlFbwtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93C0VvCxW1JS////9qNb4AAAB3C2xvCW1vCxTAdwxvCG8HbmsUwHcNbw1vDBLAEAwMYm4yNTRQYWlyaW5nDBQb9XWrEYlohBNhCjWhKIbN4LZsckFifVtSdw5vDiICQAwEYREiM9swQAwgAAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh/bMEAAQIhAAYAAiEABgACIQAGAAIhAAECIQEFifVtSQNsoQFcAAQBAiEAMFBv1dasRiWiEE2EKNaEohs3gtmxyQFcCAQBAiHAQcSI+eGnOSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSTAEHEjowAAAHgAIGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaAAgaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSVe////aCICQDWV/v//QDWY/v//QPYqGl8=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delEnvelopeOnlyModeSet(BigInteger? arg1, bool? arg2);

    [DisplayName("EnvelopeOnlyModeSet")]
    public event delEnvelopeOnlyModeSet? OnEnvelopeOnlyModeSet;

    public delegate void delEnvelopeOnlyPermanentlyDisabled(BigInteger? obj);

    [DisplayName("EnvelopeOnlyPermanentlyDisabled")]
    public event delEnvelopeOnlyPermanentlyDisabled? OnEnvelopeOnlyPermanentlyDisabled;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delProofSystemConfigurationLocked(BigInteger? arg1, UInt256? arg2, UInt160? arg3);

    [DisplayName("ProofSystemConfigurationLocked")]
    public event delProofSystemConfigurationLocked? OnProofSystemConfigurationLocked;

    public delegate void delProofVerifierRegistered(BigInteger? arg1, UInt160? arg2, bool? arg3);

    [DisplayName("ProofVerifierRegistered")]
    public event delProofVerifierRegistered? OnProofVerifierRegistered;

    public delegate void delVerificationKeyRegistered(BigInteger? arg1, UInt256? arg2, bool? arg3);

    [DisplayName("VerificationKeyRegistered")]
    public event delVerificationKeyRegistered? OnVerificationKeyRegistered;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract byte[]? RecursionVkRoot { [DisplayName("getRecursionVkRoot")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract byte[]? VerifierSelector { [DisplayName("getVerifierSelector")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProofVerifier")]
    public abstract UInt160? GetProofVerifier(BigInteger? proofSystem);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isEnvelopeOnlyAllowed")]
    public abstract bool? IsEnvelopeOnlyAllowed(BigInteger? proofSystem);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isEnvelopeOnlyLocked")]
    public abstract bool? IsEnvelopeOnlyLocked(BigInteger? proofSystem);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isProofSystemConfigurationLocked")]
    public abstract bool? IsProofSystemConfigurationLocked(BigInteger? proofSystem);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isVerificationKeyRegistered")]
    public abstract bool? IsVerificationKeyRegistered(BigInteger? proofSystem, UInt256? verificationKeyId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyProof")]
    public abstract bool? VerifyProof(byte[]? commitmentBytes);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyZkProof")]
    public abstract bool? VerifyZkProof(BigInteger? proofSystem, byte[]? verificationKeyId, byte[]? publicInputHash, byte[]? proofBytes);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("disableEnvelopeOnlyPermanently")]
    public abstract void DisableEnvelopeOnlyPermanently(BigInteger? proofSystem);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockProofSystemConfiguration")]
    public abstract void LockProofSystemConfiguration(BigInteger? proofSystem, UInt256? verificationKeyId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerProofVerifier")]
    public abstract void RegisterProofVerifier(BigInteger? proofSystem, UInt160? verifier, bool? allowed);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerVerificationKey")]
    public abstract void RegisterVerificationKey(BigInteger? proofSystem, UInt256? verificationKeyId, bool? allowed);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setEnvelopeOnlyAllowed")]
    public abstract void SetEnvelopeOnlyAllowed(BigInteger? proofSystem, bool? allowed);

    #endregion
}
