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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ContractZkVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""registerVerificationKey"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""registerProofVerifier"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verifier"",""type"":""Hash160""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":814,""safe"":false},{""name"":""getProofVerifier"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":974,""safe"":true},{""name"":""setEnvelopeOnlyAllowed"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1063,""safe"":false},{""name"":""isEnvelopeOnlyAllowed"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1284,""safe"":true},{""name"":""disableEnvelopeOnlyPermanently"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1312,""safe"":false},{""name"":""isEnvelopeOnlyLocked"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1224,""safe"":true},{""name"":""isVerificationKeyRegistered"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":1444,""safe"":true},{""name"":""verify"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1517,""safe"":true}],""events"":[{""name"":""VerificationKeyRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""ProofVerifierRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""EnvelopeOnlyModeSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""EnvelopeOnlyPermanentlyDisabled"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Deployable ProofType.Zk verifier router backed by ordinary verifier contracts."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ContractZkVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AUEBaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfeMqXJCUMIGNvbW1pdG1lbnQgcHJvb2YgbGVuZ3RoIG1pc21hdGNo4AAgARwBeDXgAgAAcmkBQQF4NdUCAABza8oAJrgkGQwUWksgcGF5bG9hZCB0b28gc21hbGzgaxDOEZckIwwedW5zdXBwb3J0ZWQgWksgcGF5bG9hZCB2ZXJzaW9u4GsRznRsNYT6//8SazX8AgAAdW1sNRz+//8kJAwfdmVyaWZpY2F0aW9uIGtleSBub3QgcmVnaXN0ZXJlZOAAIms1SQEAAHZuELckFgwRaW5uZXIgcHJvb2YgZW1wdHngbgIAABAAtiQaDBVpbm5lciBwcm9vZiB0b28gbGFyZ2XgbkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93BwAmbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2vKlyQlDCBaSyBwYXlsb2FkIHByb29mIGxlbmd0aCBtaXNtYXRjaOBvBwAmazV4AQAAdwhsNUH7//93CW8JStkoJAZFCSIGygAUsyQFCSIHbwkQs6omJG8Iam3bMGwUwBUMDXZlcmlmeVprUHJvb2ZvCUFifVtSIi1sNTL8//8kIgwdcHJvb2YgdmVyaWZpZXIgbm90IGNvbmZpZ3VyZWTgCCICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAgN6iHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaXq1JJFoIgJAVwECACB5eDV6////cGjbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBBYn1bUkBOC2Eu").AsSerializable<Neo.SmartContract.NefFile>();

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
