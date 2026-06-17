using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubL1TxFilter(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.L1TxFilter"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":139,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":238,""safe"":false},{""name"":""getDefaultAllow"",""parameters"":[],""returntype"":""Boolean"",""offset"":359,""safe"":true},{""name"":""setDefaultAllow"",""parameters"":[{""name"":""allow"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":394,""safe"":false},{""name"":""setSenderRule"",""parameters"":[{""name"":""sender"",""type"":""Hash160""},{""name"":""rule"",""type"":""Integer""}],""returntype"":""Void"",""offset"":475,""safe"":false},{""name"":""setAllowedSender"",""parameters"":[{""name"":""sender"",""type"":""Hash160""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":818,""safe"":false},{""name"":""setReceiverRule"",""parameters"":[{""name"":""receiver"",""type"":""Hash160""},{""name"":""rule"",""type"":""Integer""}],""returntype"":""Void"",""offset"":835,""safe"":false},{""name"":""setAllowedReceiver"",""parameters"":[{""name"":""receiver"",""type"":""Hash160""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1037,""safe"":false},{""name"":""setMessageTypeRule"",""parameters"":[{""name"":""messageType"",""type"":""Integer""},{""name"":""rule"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1054,""safe"":false},{""name"":""setAllowedMessageType"",""parameters"":[{""name"":""messageType"",""type"":""Integer""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1081,""safe"":false},{""name"":""acceptL1ToL2"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""sender"",""type"":""Hash160""},{""name"":""receiver"",""type"":""Hash160""},{""name"":""messageType"",""type"":""Integer""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1095,""safe"":true}],""events"":[{""name"":""DefaultPolicySet"",""parameters"":[{""name"":""obj"",""type"":""Boolean""}]},{""name"":""RuleSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Per-chain L1-to-L2 transaction filter for NeoHub.MessageRouter."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.L1TxFilter"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAAAAP3iBFcBAnkmBCJBeHBoStkoJAZFCSIGygAUsyQFCSIGaBCzqiQSDA1pbnZhbGlkIG93bmVy4GgMAf/bMDQgDAEB2zAMAQTbMDQwQErZKCQGRQkiBsoAFLNAELNAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQEGb9mfOQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBXAQE0mkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVT////cHgMAf/bMDUV////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwEADAEE2zA1U////3BoC5cmBQgiCWjbMBDOEZciAkDbMEBXAAE1/v7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4BGIShB4JgURIgMS0AwBBNswNbr+//94EcAMEERlZmF1bHRQb2xpY3lTZXRBlQFvYUBXAAJ4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQTDA5pbnZhbGlkIHNlbmRlcuB4NZoAAAAReNsweVQ0A0BXAAQ1cf7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HsStiQqDCVydWxlIG11c3QgYmUgMD11bnNldCwgMT1hbGxvdywgMj1kZW554HsQlyYHeDQkIg4RiEoQe9B4NQD+//97enkTwAwHUnVsZVNldEGVAW9hQFcAAXhBm/ZnzkEvWMXtQEEvWMXtQFcDAQAViHARSmgQUdBFeNswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqABS1JJBoIgJA2zBAVwACeSYFESIDEng1nv7//0BXAAJ4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQVDBBpbnZhbGlkIHJlY2VpdmVy4Hg0DhJ42zB5VDWc/v//QFcDAQAViHASSmgQUdBFeNswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqABS1JJBoIgJAVwACeSYFESIDEng1K////0BXAAJ5EYhKEHjQExKIShAT0EoReNA15P3//0BXAAJ5JgURIgMSeDTaQFcABXgQlyYFCSJveUrZKCQGRQkiBsoAFLOqJgUIIgV5ELMmBQkiU3pK2SgkBkUJIgbKABSzqiYFCCIFehCzJgUJIjd8ygIAAAIAtyYFCSIqeTUL/v//NCMkBQkiCno13P7//zQWJAUJIg4SiEoQE9BKEXvQNAUiAkBXAgF4Nf77//9waAuXJgk1mPz//yINaNswEM5xaRGXIgJAM77v6A==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delDefaultPolicySet(bool? obj);

    [DisplayName("DefaultPolicySet")]
    public event delDefaultPolicySet? OnDefaultPolicySet;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delRuleSet(BigInteger? arg1, byte[]? arg2, BigInteger? arg3);

    [DisplayName("RuleSet")]
    public event delRuleSet? OnRuleSet;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? DefaultAllow { [DisplayName("getDefaultAllow")] get; [DisplayName("setDefaultAllow")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("acceptL1ToL2")]
    public abstract bool? AcceptL1ToL2(BigInteger? targetChainId, UInt160? sender, UInt160? receiver, BigInteger? messageType, byte[]? payload);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setAllowedMessageType")]
    public abstract void SetAllowedMessageType(BigInteger? messageType, bool? allowed);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setAllowedReceiver")]
    public abstract void SetAllowedReceiver(UInt160? receiver, bool? allowed);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setAllowedSender")]
    public abstract void SetAllowedSender(UInt160? sender, bool? allowed);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setMessageTypeRule")]
    public abstract void SetMessageTypeRule(BigInteger? messageType, BigInteger? rule);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setReceiverRule")]
    public abstract void SetReceiverRule(UInt160? receiver, BigInteger? rule);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setSenderRule")]
    public abstract void SetSenderRule(UInt160? sender, BigInteger? rule);

    #endregion
}
