using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubContractZkVerifier(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ContractZkVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""registerVerificationKey"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""registerProofVerifier"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1022,""safe"":false},{""name"":""getProofVerifier"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":1190,""safe"":true},{""name"":""setEnvelopeOnlyAllowed"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1279,""safe"":false},{""name"":""isEnvelopeOnlyAllowed"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1505,""safe"":true},{""name"":""disableEnvelopeOnlyPermanently"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1533,""safe"":false},{""name"":""isEnvelopeOnlyLocked"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1445,""safe"":true},{""name"":""lockProofSystemConfiguration"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":1670,""safe"":false},{""name"":""isProofSystemConfigurationLocked"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":660,""safe"":true},{""name"":""getLockedVerificationKey"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2229,""safe"":true},{""name"":""isVerificationKeyRegistered"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":2108,""safe"":true},{""name"":""verify"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":2345,""safe"":true}],""events"":[{""name"":""VerificationKeyRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""ProofVerifierRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""EnvelopeOnlyModeSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""EnvelopeOnlyPermanentlyDisabled"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""ProofSystemConfigurationLocked"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Deployable ProofType.Zk verifier router backed by ordinary verifier contracts."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ContractZkVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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b2YgbGVuZ3RoIG1pc21hdGNo4AAgARwBeDXgAgAAcmkBQQF4NdUCAABza8oAJrgkGQwUWksgcGF5bG9hZCB0b28gc21hbGzgaxDOEZckIwwedW5zdXBwb3J0ZWQgWksgcGF5bG9hZCB2ZXJzaW9u4GsRznRsNVj3//8SazX8AgAAdW1sNXj9//8kJAwfdmVyaWZpY2F0aW9uIGtleSBub3QgcmVnaXN0ZXJlZOAAIms1SQEAAHZuELckFgwRaW5uZXIgcHJvb2YgZW1wdHngbgIAABAAtiQaDBVpbm5lciBwcm9vZiB0b28gbGFyZ2XgbkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93BwAmbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2vKlyQlDCBaSyBwYXlsb2FkIHByb29mIGxlbmd0aCBtaXNtYXRjaOBvBwAmazV4AQAAdwhsNd34//93CW8JStkoJAZFCSIGygAUsyQFCSIHbwkQs6omJG8Iam3bMGwUwBUMDXZlcmlmeVprUHJvb2ZvCUFifVtSIi1sNdP5//8kIgwdcHJvb2YgdmVyaWZpZXIgbm90IGNvbmZpZ3VyZWTgCCICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAgN6iHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaXq1JJFoIgJAVwECACB5eDV6////cGjbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBBYn1bUkCAfFW8").AsSerializable<Neo.SmartContract.NefFile>();

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

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLockedVerificationKey")]
    public abstract UInt256? GetLockedVerificationKey(BigInteger? proofSystem);

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
    [DisplayName("verify")]
    public abstract bool? Verify(byte[]? commitmentBytes);

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
