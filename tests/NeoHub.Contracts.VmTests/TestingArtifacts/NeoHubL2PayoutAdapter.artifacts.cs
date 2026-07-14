using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubL2PayoutAdapter(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.L2PayoutAdapter"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""payoutVersion"",""parameters"":[],""returntype"":""Integer"",""offset"":308,""safe"":true},{""name"":""getEscrow"",""parameters"":[],""returntype"":""Hash160"",""offset"":310,""safe"":true},{""name"":""getNeoChainId"",""parameters"":[],""returntype"":""Integer"",""offset"":416,""safe"":true},{""name"":""getRelayAccount"",""parameters"":[],""returntype"":""Hash160"",""offset"":482,""safe"":true},{""name"":""getLastSequence"",""parameters"":[],""returntype"":""Integer"",""offset"":491,""safe"":true},{""name"":""payout"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""neoChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""deadlineUnixSeconds"",""type"":""Integer""},{""name"":""sourceTxRef"",""type"":""Hash256""},{""name"":""messageBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":520,""safe"":false},{""name"":""acknowledge"",""parameters"":[{""name"":""sequence"",""type"":""Integer""},{""name"":""messageHash"",""type"":""Hash256""},{""name"":""l2TransactionHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":4583,""safe"":false},{""name"":""getPayoutStatus"",""parameters"":[{""name"":""sequence"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4963,""safe"":true},{""name"":""getPayoutMessage"",""parameters"":[{""name"":""sequence"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4994,""safe"":true},{""name"":""getPayoutMessageHash"",""parameters"":[{""name"":""sequence"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":5067,""safe"":true},{""name"":""getPayoutNeoAsset"",""parameters"":[{""name"":""sequence"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":5085,""safe"":true},{""name"":""getPayoutL2TransactionHash"",""parameters"":[{""name"":""sequence"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":5102,""safe"":true},{""name"":""getSequenceForMessageHash"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":5120,""safe"":true}],""events"":[{""name"":""PayoutEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Integer""},{""name"":""arg5"",""type"":""Integer""},{""name"":""arg6"",""type"":""Hash160""},{""name"":""arg7"",""type"":""Hash160""},{""name"":""arg8"",""type"":""Hash160""},{""name"":""arg9"",""type"":""Integer""},{""name"":""arg10"",""type"":""Integer""},{""name"":""arg11"",""type"":""Hash256""},{""name"":""arg12"",""type"":""ByteArray""}]},{""name"":""PayoutAcknowledged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]}]},""permissions"":[{""contract"":""0x726cb6e0cd8628a1350a611384688911ab75f51b"",""methods"":[""sha256""]}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Authenticated durable L1-to-L2 external-bridge payout queue."",""Version"":""1.0.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.L2PayoutAdapter"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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Av///38yDAMAAAAAAQAAAJ/OnkpzRWxKnUoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwQuCSNa32XJBsMFnNpZ25lZCBhbW91bnQgbWlzbWF0Y2jgfwg14wQAACICQErZKCQGRQkiBsoAILNAELNAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkkBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkVnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkWnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkXnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSQFcBAgAUiHB4eWgQaMoVVTQTaNsoStgkCUrKABQoAzoiAkBXAQUQcCOhAAAAeHlonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSnp7aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaHy1JWH///9A2yhK2CQJSsoAFCgDOkBXAQIAIIhweHloEGjKFVU1NP///2jbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBXAQF42yg3AABwaDcAANsw2yhK2CQJSsoAICgDOiICQDcAAEDbKEDbMEBXAQEAIYhwEkpoEFHQRXgRaDQGaCICQFcAAwAgeXgQetswNcT+//9A2zBAVwIBeDWMAAAANaTx//9waAuYJCEMHHBheW91dCBxdWV1ZSBlbnRyeSBub3QgZm91bmTgaNswcWnKAbsAuCQgDBtwYXlvdXQgcXVldWUgcmVjb3JkIGNvcnJ1cHTgaRDOEZcmBQgiB2kQzhKXJCAMG3BheW91dCBxdWV1ZSBzdGF0dXMgY29ycnVwdOBpIgJAVwEBGYhwEUpoEFHQRXgRaDQGaCICQFcBAxBwI7EAAAB6aBigSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn6lKEC4EIghKAf8AMgYB/wCRSnh5aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaBi1JVH///9AVwECeMp5ypgmBQkiThBwIkF4aM55aM6YJgUJIj5oSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcEVoeMq1JL0IIgJAVwEDeohwehBoeXg18fz//2giAkBXAAMAFHl4EHrbMDXd/P//QNswQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcBAzX47///Qfgn7IwkFgwRbm90IHJlbGF5IGFjY291bnTgeUrZKCQGRQkiBsoAILMkBQkiBnkQs6okGQwUaW52YWxpZCBtZXNzYWdlIGhhc2jgekrZKCQGRQkiBsoAILMkBQkiBnoQs6okIAwbaW52YWxpZCBMMiB0cmFuc2FjdGlvbiBoYXNo4Hg1Vf3//3AAFWg1xPz//3mXJCoMJWFja25vd2xlZGdlbWVudCBtZXNzYWdlIGhhc2ggbWlzbWF0Y2jgaBDOEpcmSQA1aDWJ/P//epckOgw1cGF5b3V0IGFscmVhZHkgYWNrbm93bGVkZ2VkIGJ5IGFub3RoZXIgTDIgdHJhbnNhY3Rpb27gCCJkaBDOEZckHwwaaW52YWxpZCBwYXlvdXQgcXVldWUgc3RhdGXgEkpoEFHQRXoANWg1ivz//3g1KP3//2hQNZv+//96eXgTwAwSUGF5b3V0QWNrbm93bGVkZ2VkQZUBb2EIIgJAQfgn7IxAVwEBeDXw/P//NQju//9waAuXJgUQIgdo2zAQziICQFcBAXg1Qfz//3BoAFVoygBVn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9TNcP9//8iAkBXAAF4Nfj7//8AFVA1aPv//0BXAAF4Neb7//8RUDV4+v//QFcAAXg11fv//wA1UDVF+///QFcBAXg1mPv//zVr7f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQHoyjQ4=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delPayoutAcknowledged(BigInteger? arg1, UInt256? arg2, UInt256? arg3);

    [DisplayName("PayoutAcknowledged")]
    public event delPayoutAcknowledged? OnPayoutAcknowledged;

    public delegate void delPayoutEnqueued(BigInteger? arg1, UInt256? arg2, BigInteger? arg3, BigInteger? arg4, BigInteger? arg5, UInt160? arg6, UInt160? arg7, UInt160? arg8, BigInteger? arg9, BigInteger? arg10, UInt256? arg11, byte[]? arg12);

    [DisplayName("PayoutEnqueued")]
    public event delPayoutEnqueued? OnPayoutEnqueued;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Escrow { [DisplayName("getEscrow")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? LastSequence { [DisplayName("getLastSequence")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? NeoChainId { [DisplayName("getNeoChainId")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? RelayAccount { [DisplayName("getRelayAccount")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? PayoutVersion { [DisplayName("payoutVersion")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPayoutL2TransactionHash")]
    public abstract UInt256? GetPayoutL2TransactionHash(BigInteger? sequence);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPayoutMessage")]
    public abstract byte[]? GetPayoutMessage(BigInteger? sequence);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPayoutMessageHash")]
    public abstract UInt256? GetPayoutMessageHash(BigInteger? sequence);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPayoutNeoAsset")]
    public abstract UInt160? GetPayoutNeoAsset(BigInteger? sequence);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPayoutStatus")]
    public abstract BigInteger? GetPayoutStatus(BigInteger? sequence);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSequenceForMessageHash")]
    public abstract BigInteger? GetSequenceForMessageHash(UInt256? messageHash);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("acknowledge")]
    public abstract bool? Acknowledge(BigInteger? sequence, UInt256? messageHash, UInt256? l2TransactionHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("payout")]
    public abstract bool? Payout(BigInteger? externalChainId, BigInteger? neoChainId, BigInteger? nonce, UInt160? foreignAsset, UInt160? neoAsset, UInt160? recipient, BigInteger? amount, BigInteger? deadlineUnixSeconds, UInt256? sourceTxRef, byte[]? messageBytes);

    #endregion
}
