using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSp1Groth16Verifier(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.Sp1Groth16Verifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""getVerifierSelector"",""parameters"":[],""returntype"":""ByteArray"",""offset"":0,""safe"":true},{""name"":""getRecursionVkRoot"",""parameters"":[],""returntype"":""ByteArray"",""offset"":12,""safe"":true},{""name"":""verifyZkProof"",""parameters"":[{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""ByteArray""},{""name"":""publicInputHash"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":52,""safe"":true}],""events"":[]},""permissions"":[{""contract"":""0x726cb6e0cd8628a1350a611384688911ab75f51b"",""methods"":[""bn254Add"",""bn254Deserialize"",""bn254Equal"",""bn254Mul"",""bn254Pairing"",""sha256""]}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""SP1 6.2.1 SDK v6.1-compatible Groth16/BN254 verifier for Neo N4 batch proofs."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.Sp1Groth16Verifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Properties

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
    [DisplayName("verifyZkProof")]
    public abstract bool? VerifyZkProof(BigInteger? proofSystem, byte[]? verificationKeyId, byte[]? publicInputHash, byte[]? proofBytes);

    #endregion

}
