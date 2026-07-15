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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.L1TxFilter"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":139,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":238,""safe"":false},{""name"":""getDefaultAllow"",""parameters"":[],""returntype"":""Boolean"",""offset"":359,""safe"":true},{""name"":""setDefaultAllow"",""parameters"":[{""name"":""allow"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":394,""safe"":false},{""name"":""setSenderRule"",""parameters"":[{""name"":""sender"",""type"":""Hash160""},{""name"":""rule"",""type"":""Integer""}],""returntype"":""Void"",""offset"":475,""safe"":false},{""name"":""setAllowedSender"",""parameters"":[{""name"":""sender"",""type"":""Hash160""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":817,""safe"":false},{""name"":""setReceiverRule"",""parameters"":[{""name"":""receiver"",""type"":""Hash160""},{""name"":""rule"",""type"":""Integer""}],""returntype"":""Void"",""offset"":834,""safe"":false},{""name"":""setAllowedReceiver"",""parameters"":[{""name"":""receiver"",""type"":""Hash160""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1035,""safe"":false},{""name"":""setMessageTypeRule"",""parameters"":[{""name"":""messageType"",""type"":""Integer""},{""name"":""rule"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1052,""safe"":false},{""name"":""setAllowedMessageType"",""parameters"":[{""name"":""messageType"",""type"":""Integer""},{""name"":""allowed"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1079,""safe"":false},{""name"":""acceptL1ToL2"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""sender"",""type"":""Hash160""},{""name"":""receiver"",""type"":""Hash160""},{""name"":""messageType"",""type"":""Integer""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1093,""safe"":true}],""events"":[{""name"":""DefaultPolicySet"",""parameters"":[{""name"":""obj"",""type"":""Boolean""}]},{""name"":""RuleSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Per-chain L1-to-L2 transaction filter for NeoHub.MessageRouter."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.L1TxFilter"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAAAAP3gBFcBAnkmBCJBeHBoStkoJAZFCSIGygAUsyQFCSIGaBCzqiQSDA1pbnZhbGlkIG93bmVy4GgMAf/bMDQgDAEB2zAMAQTbMDQwQErZKCQGRQkiBsoAFLNAELNAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQEGb9mfOQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBXAQE0mkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVT////cHgMAf/bMDUV////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwEADAEE2zA1U////3BoC5cmBQgiCWjbMBDOEZciAkDbMEBXAAE1/v7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4BGIShB4JgURIgMS0AwBBNswNbr+//94EcAMEERlZmF1bHRQb2xpY3lTZXRBlQFvYUBXAAJ4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQTDA5pbnZhbGlkIHNlbmRlcuB5eNswEXg1lwAAADQDQFcABDVy/v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgexK2JCoMJXJ1bGUgbXVzdCBiZSAwPXVuc2V0LCAxPWFsbG93LCAyPWRlbnngexCXJgd4NCQiDhGIShB70Hg1Af7//3t6eRPADAdSdWxlU2V0QZUBb2FAVwABeEGb9mfOQS9Yxe1AQS9Yxe1A2zBAVwMBABWIcBFKaBBR0EV42zBxEHIibmlqzkpoEWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAFLUkkGgiAkBXAAJ5JgURIgMSeDWf/v//QFcAAnhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBUMEGludmFsaWQgcmVjZWl2ZXLgeXjbMBJ4NAg1nf7//0BXAwEAFYhwEkpoEFHQRXjbMHEQciJuaWrOSmgRap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAUtSSQaCICQFcAAnkmBREiAxJ4NSz///9AVwACeRGIShB40BMSiEoQE9BKEXjQNeX9//9AVwACeSYFESIDEng02kBXAAV4EJcmBQkib3lK2SgkBkUJIgbKABSzqiYFCCIFeRCzJgUJIlN6StkoJAZFCSIGygAUs6omBQgiBXoQsyYFCSI3fMoCAAACALcmBQkiKnk1D/7//zQjJAUJIgp6Ndz+//80FiQFCSIOEohKEBPQShF70DQFIgJAVwIBeDUA/P//cGgLlyYJNZr8//8iDWjbMBDOcWkRlyICQNJI3ec=").AsSerializable<Neo.SmartContract.NefFile>();

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
