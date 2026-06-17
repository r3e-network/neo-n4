using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubDAValidator(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.DAValidator"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":168,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":267,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":388,""safe"":true},{""name"":""registerCommittee"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":446,""safe"":false},{""name"":""getCommittee"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1121,""safe"":true},{""name"":""submitAttestation"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1151,""safe"":false},{""name"":""isValidated"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4802,""safe"":true},{""name"":""validate"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4985,""safe"":true},{""name"":""verifyAttestation"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1430,""safe"":true}],""events"":[{""name"":""DACommitteeRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""DAValidated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""L1 data-availability validator for Neo Elastic Network batches."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.DAValidator"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscg92ZXJpZnlXaXRoRUNEc2EEAAEPAAD9yRNXAwJ5JgQidHhwaBDOcWgRznJpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQSDA1pbnZhbGlkIG93bmVy4GpK2SgkBkUJIgbKABSzJAUJIgZqELOqJBgME2ludmFsaWQgREEgcmVnaXN0cnngaQwB/9swNBxqDAH92zA0FEBK2SgkBkUJIgbKABSzQBCzQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBBm/ZnzkBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBXAQE0mkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVT////cHgMAf/bMDUr////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwEADAH92zA1U////3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcDAzXn/v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeBC3JCEMHGNoYWluSWQgMCBpcyByZXNlcnZlZCBmb3IgTDHgeRC3JB8MGnRocmVzaG9sZCBtdXN0IGJlIHBvc2l0aXZl4HrKELckHAwXY29tbWl0dGVlIGJsb2IgaXMgZW1wdHngesoAIaIQlyQ0DC9jb21taXR0ZWUgYmxvYiBtdXN0IGNvbnRhaW4gMzMtYnl0ZSBwdWJsaWMga2V5c+B6ygAhSg8qC0sCAAAAgCoDOqFwaABAtiQYDBNjb21taXR0ZWUgdG9vIGxhcmdl4HlotiQlDCB0aHJlc2hvbGQgZXhjZWVkcyBjb21taXR0ZWUgc2l6ZeASesqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxeUppEFHQRWhKEC4EIghKAf8AMgYB/wCRSmkRUdBFEHIibnpqzkppEmqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp6yrUkkHg0TmlQNDRoShAuBCIISgH/ADIGAf8AkXl4E8AMFURBQ29tbWl0dGVlUmVnaXN0ZXJlZEGVAW9hQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAQEViHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFaCICQFcBAXg0iDV4/P//cGgLlyYGEIgiBWjbMCICQNswQFcDBXx7enl4NQ8BAAAkHAwXREEgYXR0ZXN0YXRpb24gcmVqZWN0ZWTgeXg12QwAADUw/P//C5ckMQwsYXR0ZXN0YXRpb24gYWxyZWFkeSBzdWJtaXR0ZWQgZm9yIHRoaXMgYmF0Y2jgACGIcHtKaBBR0EV62zBxEHIibmlqzkpoEWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkHl4NRUMAABoUDVi/v//e3p5eBTADAtEQVZhbGlkYXRlZEGVAW9hCCICQFcRBXgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HsRlyYFCCIFexKXJgUIIgV7E5ckMgwtYXR0ZXN0YXRpb25zIGFyZSByZXF1aXJlZCBmb3Igb2ZmLUwxIERBIG1vZGVz4HoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okIAwbY29tbWl0bWVudCBtdXN0IGJlIG5vbi16ZXJv4HzKErgkFAwPcHJvb2YgdG9vIHNob3J04Hg1jf3//zV6+v//cGgLmCQeDBlubyBEQSBjb21taXR0ZWUgZm9yIGNoYWlu4GjbMHFpyhK4JBgME2NvbW1pdHRlZSBtYWxmb3JtZWTgaRDOcmkRznNrAEC2JCkMJGNvbW1pdHRlZSB0b28gbGFyZ2UgZm9yIHZlcmlmaWNhdGlvbuBqELckHwwadGhyZXNob2xkIG11c3QgYmUgcG9zaXRpdmXgamu2JCUMIHRocmVzaG9sZCBleGNlZWRzIGNvbW1pdHRlZSBzaXpl4GnKEmsAIaBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+XJB4MGWNvbW1pdHRlZSBsZW5ndGggbWlzbWF0Y2jgfBDOfBHOGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfknRsargkJAwfc2lnbmF0dXJlIGNvdW50IGJlbG93IHRocmVzaG9sZOB8yhJsAEGgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQaDBVwcm9vZiBsZW5ndGggbWlzbWF0Y2jge3p5eDW6AwAAdRiIdhB3BxB3CCN4AwAAEm8IAEGgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwl8bwnOdwpvCmu1JCMMHnNpZ25lciBpbmRleCBvdXRzaWRlIGNvbW1pdHRlZeBvChhKDyoLSwIAAACAKgM6oXcLEW8KGKKoSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0oQLgQiCEoB/wAyBgH/AJF3DG5vC85vDJEQlyQVDBBkdXBsaWNhdGUgc2lnbmVy4G5vC85vDJJKEC4EIghKAf8AMgYB/wCRSm5vC1HQRQAhiHcNEm8KACGgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdw4Qdw8idGlvDm8PnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSm8Nbw9R0EVvD0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cPRW8PACG1JIkAQIh3DxB3ECOnAAAAfG8JEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfbxCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85Kbw9vEFHQRW8QSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdxBFbxAAQLUlWf///wAXbw/bKG8N2yhK2CQJSsoAISgDOm3bKDcAAHcQbxAkIgwdc2lnbmF0dXJlIHZlcmlmaWNhdGlvbiBmYWlsZWTgbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvCEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cIRW8IbLUlifz//28HargiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcEBAAxiHAQcQBOSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRQA0SmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRQBESmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRQBBSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRXhKEC4EIghKAf8AMgYB/wCRSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV5ShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeQAgqUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeQAoqUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeQAwqUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeQA4qUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFetswchBzIm5qa85KaGlrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrACC1JJBpACCeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRXtKaGlR0EVoIgJA2zBANwAAQNsoQNsoStgkCUrKACEoAzpAVwECHYhwEkpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXlKEC4EIghKAf8AMgYB/wCRSmgVUdBFeRipShAuBCIISgH/ADIGAf8AkUpoFlHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXkAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFeQAoqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV5ADCpShAuBCIISgH/ADIGAf8AkUpoG1HQRXkAOKlKEC4EIghKAf8AMgYB/wCRSmgcUdBFaCICQFcEBHl4Nbz+//81E+7//3BoC5cmCAkjoAAAAGjbMHFpygAhmCYFCCIHaRDOe5gmCAkjhQAAAHrbMHIQcyJxaRFrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OamvOmCYFCSI+a0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAgtSSNCCICQFcABHgQlyYFCCIFexO3JgUIIiZ6DCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmBQkiFXsQlyYFCCINe3p5eDUB////IgJAY2Z8hA==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delDACommitteeRegistered(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("DACommitteeRegistered")]
    public event delDACommitteeRegistered? OnDACommitteeRegistered;

    public delegate void delDAValidated(BigInteger? arg1, BigInteger? arg2, UInt256? arg3, BigInteger? arg4);

    [DisplayName("DAValidated")]
    public event delDAValidated? OnDAValidated;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? DARegistry { [DisplayName("getDARegistry")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getCommittee")]
    public abstract byte[]? GetCommittee(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isValidated")]
    public abstract bool? IsValidated(BigInteger? chainId, BigInteger? batchNumber, UInt256? commitment, BigInteger? daMode);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("validate")]
    public abstract bool? Validate(BigInteger? chainId, BigInteger? batchNumber, UInt256? commitment, BigInteger? daMode);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyAttestation")]
    public abstract bool? VerifyAttestation(BigInteger? chainId, BigInteger? batchNumber, UInt256? commitment, BigInteger? daMode, byte[]? proofBytes);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerCommittee")]
    public abstract void RegisterCommittee(BigInteger? chainId, BigInteger? threshold, byte[]? committeeBlob);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitAttestation")]
    public abstract bool? SubmitAttestation(BigInteger? chainId, BigInteger? batchNumber, UInt256? commitment, BigInteger? daMode, byte[]? proofBytes);

    #endregion
}
