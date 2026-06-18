using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubMessageRouter(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MessageRouter"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":175,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":274,""safe"":false},{""name"":""enqueueL1ToL2"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""receiver"",""type"":""Hash160""},{""name"":""messageType"",""type"":""Integer""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":395,""safe"":false},{""name"":""getL1ToL2"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":3509,""safe"":true},{""name"":""setL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""filter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":3543,""safe"":false},{""name"":""clearL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3697,""safe"":false},{""name"":""getL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":759,""safe"":true},{""name"":""publishMessageRoots"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""l2ToL1Root"",""type"":""Hash256""},{""name"":""l2ToL2Root"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":3846,""safe"":false},{""name"":""getL2ToL1Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3992,""safe"":true},{""name"":""getL2ToL2Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4100,""safe"":true},{""name"":""publishGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""verificationKeyId"",""type"":""ByteArray""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":4173,""safe"":false},{""name"":""getGlobalRootVerifier"",""parameters"":[],""returntype"":""Hash160"",""offset"":4867,""safe"":true},{""name"":""getGlobalRootProofSystem"",""parameters"":[],""returntype"":""Integer"",""offset"":4925,""safe"":true},{""name"":""setGlobalRootVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4955,""safe"":false},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":5143,""safe"":true},{""name"":""markConsumed"",""parameters"":[{""name"":""sourceChainId"",""type"":""Integer""},{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":5214,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":5498,""safe"":true}],""events"":[{""name"":""L1ToL2Enqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""L2ToL1Consumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""L1TxFilterSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""MessageRootsPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GlobalRootVerifierChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Cross-chain message queue \u002B message-root registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MessageRouter"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAAAAP2NFVcDAnkmBCJ7eHBoEM5xaBHOcmlK2SgkBkUJIgbKABSzJAUJIgZpELOqJBIMDWludmFsaWQgb3duZXLgakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okHwwaaW52YWxpZCBzZXR0bGVtZW50IG1hbmFnZXLgaQwB/9swNBxqDAH92zA0FEBK2SgkBkUJIgbKABSzQBCzQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBBm/ZnzkBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBXAQE0mkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVT////cHgMAf/bMDUr////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwUEeUrZKCQGRQkiBsoAFLMkBQkiBnkQs6okFQwQaW52YWxpZCByZWNlaXZlcuB4ELckJwwidGFyZ2V0Q2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeBBOVNuPHB7enloeDWzAAAAeDW0AQAAcWk16v7//3JqC5cmBREiUmpK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRc2tpNcsBAAB7enloa3gQNdUBAAB0a3g1/AkAAGxQNd8JAAB5aGt4FMAMDkwxVG9MMkVucXVldWVkQZUBb2FrIgJAQTlTbjxAVwIFeDRRcGgQsyYEIkh8e3p5eBXAFQwMYWNjZXB0TDFUb0wyaEFifVtScWkkKAwjbDEgdG8gbDIgbWVzc2FnZSByZWplY3RlZCBieSBmaWx0ZXLgQFcBAXg0NDXp/f//cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwEBFYhwF0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBBYn1bUkBXAQEViHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQErYJgZFECIE2yFAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcHBwA9fsqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BoiHEQcnhKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeCCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV4ABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV5ShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFekoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFehipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAKKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXvbMHMQdCJua2zOSmlqbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQagAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV82zB0EHUibmxtzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0A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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delGlobalRootPublished(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GlobalRootPublished")]
    public event delGlobalRootPublished? OnGlobalRootPublished;

    public delegate void delGlobalRootVerifierChanged(UInt160? arg1, BigInteger? arg2);

    [DisplayName("GlobalRootVerifierChanged")]
    public event delGlobalRootVerifierChanged? OnGlobalRootVerifierChanged;

    public delegate void delL1ToL2Enqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4);

    [DisplayName("L1ToL2Enqueued")]
    public event delL1ToL2Enqueued? OnL1ToL2Enqueued;

    public delegate void delL1TxFilterSet(BigInteger? arg1, UInt160? arg2);

    [DisplayName("L1TxFilterSet")]
    public event delL1TxFilterSet? OnL1TxFilterSet;

    public delegate void delL2ToL1Consumed(BigInteger? arg1, UInt256? arg2);

    [DisplayName("L2ToL1Consumed")]
    public event delL2ToL1Consumed? OnL2ToL1Consumed;

    public delegate void delMessageRootsPublished(BigInteger? arg1, BigInteger? arg2, UInt256? arg3, UInt256? arg4);

    [DisplayName("MessageRootsPublished")]
    public event delMessageRootsPublished? OnMessageRootsPublished;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? GlobalRootProofSystem { [DisplayName("getGlobalRootProofSystem")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GlobalRootVerifier { [DisplayName("getGlobalRootVerifier")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGlobalRoot")]
    public abstract UInt256? GetGlobalRoot(BigInteger? batchEpoch);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1ToL2")]
    public abstract byte[]? GetL1ToL2(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1TxFilter")]
    public abstract UInt160? GetL1TxFilter(BigInteger? targetChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL1Root")]
    public abstract UInt256? GetL2ToL1Root(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL2Root")]
    public abstract UInt256? GetL2ToL2Root(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isConsumed")]
    public abstract bool? IsConsumed(UInt256? messageHash);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("clearL1TxFilter")]
    public abstract void ClearL1TxFilter(BigInteger? targetChainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("enqueueL1ToL2")]
    public abstract BigInteger? EnqueueL1ToL2(BigInteger? targetChainId, UInt160? receiver, BigInteger? messageType, byte[]? payload);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("markConsumed")]
    public abstract void MarkConsumed(BigInteger? sourceChainId, UInt256? messageHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishGlobalRoot")]
    public abstract void PublishGlobalRoot(BigInteger? batchEpoch, UInt256? globalRoot, byte[]? verificationKeyId, byte[]? aggregatedProof);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishMessageRoots")]
    public abstract void PublishMessageRoots(BigInteger? chainId, BigInteger? batchNumber, UInt256? l2ToL1Root, UInt256? l2ToL2Root);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setGlobalRootVerifier")]
    public abstract void SetGlobalRootVerifier(UInt160? verifier, BigInteger? proofSystem);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setL1TxFilter")]
    public abstract void SetL1TxFilter(BigInteger? targetChainId, UInt160? filter);

    #endregion
}
