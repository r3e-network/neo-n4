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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.DAValidator"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":168,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":267,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":388,""safe"":true},{""name"":""registerCommittee"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":446,""safe"":false},{""name"":""getCommittee"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1107,""safe"":true},{""name"":""submitAttestation"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1137,""safe"":false},{""name"":""isValidated"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4774,""safe"":true},{""name"":""validate"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4957,""safe"":true},{""name"":""verifyAttestation"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""commitment"",""type"":""Hash256""},{""name"":""daMode"",""type"":""Integer""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":1415,""safe"":true}],""events"":[{""name"":""DACommitteeRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""DAValidated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""L1 data-availability validator for Neo Elastic Network batches."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.DAValidator"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscg92ZXJpZnlXaXRoRUNEc2EEAAEPAAD9rRNXAwJ5JgQidHhwaBDOcWgRznJpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQSDA1pbnZhbGlkIG93bmVy4GpK2SgkBkUJIgbKABSzJAUJIgZqELOqJBgME2ludmFsaWQgREEgcmVnaXN0cnngaQwB/9swNBxqDAH92zA0FEBK2SgkBkUJIgbKABSzQBCzQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBBm/ZnzkBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBXAQE0mkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVT////cHgMAf/bMDUr////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwEADAH92zA1U////3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcDAzXn/v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeBC3JCEMHGNoYWluSWQgMCBpcyByZXNlcnZlZCBmb3IgTDHgeRC3JB8MGnRocmVzaG9sZCBtdXN0IGJlIHBvc2l0aXZl4HrKELckHAwXY29tbWl0dGVlIGJsb2IgaXMgZW1wdHngesoAIaIQlyQ0DC9jb21taXR0ZWUgYmxvYiBtdXN0IGNvbnRhaW4gMzMtYnl0ZSBwdWJsaWMga2V5c+B6ygAhoXBoAEC2JBgME2NvbW1pdHRlZSB0b28gbGFyZ2XgeWi2JCUMIHRocmVzaG9sZCBleGNlZWRzIGNvbW1pdHRlZSBzaXpl4BJ6yp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHF5SmkQUdBFaEoQLgQiCEoB/wAyBgH/AJFKaRFR0EUQciJuemrOSmkSap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanrKtSSQaXg0TDQ0aEoQLgQiCEoB/wAyBgH/AJF5eBPADBVEQUNvbW1pdHRlZVJlZ2lzdGVyZWRBlQFvYUBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwEBFYhwEUpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkBXAQF4NIg1hvz//3BoC5cmBhCIIgVo2zAiAkDbMEBXAwV8e3p5eDUOAQAAJBwMF0RBIGF0dGVzdGF0aW9uIHJlamVjdGVk4Hl4NcsMAAA1Pvz//wuXJDEMLGF0dGVzdGF0aW9uIGFscmVhZHkgc3VibWl0dGVkIGZvciB0aGlzIGJhdGNo4AAhiHB7SmgQUdBFetswcRByIm5pas5KaBFqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqACC1JJBoeXg1BgwAADVj/v//e3p5eBTADAtEQVZhbGlkYXRlZEGVAW9hCCICQFcRBXgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HsRlyYFCCIFexKXJgUIIgV7E5ckMgwtYXR0ZXN0YXRpb25zIGFyZSByZXF1aXJlZCBmb3Igb2ZmLUwxIERBIG1vZGVz4HoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okIAwbY29tbWl0bWVudCBtdXN0IGJlIG5vbi16ZXJv4HzKErgkFAwPcHJvb2YgdG9vIHNob3J04Hg1jv3//zWJ+v//cGgLmCQeDBlubyBEQSBjb21taXR0ZWUgZm9yIGNoYWlu4GjbMHFpyhK4JBgME2NvbW1pdHRlZSBtYWxmb3JtZWTgaRDOcmkRznNrAEC2JCkMJGNvbW1pdHRlZSB0b28gbGFyZ2UgZm9yIHZlcmlmaWNhdGlvbuBqELckHwwadGhyZXNob2xkIG11c3QgYmUgcG9zaXRpdmXgamu2JCUMIHRocmVzaG9sZCBleGNlZWRzIGNvbW1pdHRlZSBzaXpl4GnKEmsAIaBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+XJB4MGWNvbW1pdHRlZSBsZW5ndGggbWlzbWF0Y2jgfBDOfBHOGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfknRsargkJAwfc2lnbmF0dXJlIGNvdW50IGJlbG93IHRocmVzaG9sZOB8yhJsAEGgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQaDBVwcm9vZiBsZW5ndGggbWlzbWF0Y2jge3p5eDWtAwAAdRiIdhB3BxB3CCNrAwAAEm8IAEGgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwl8bwnOdwpvCmu1JCMMHnNpZ25lciBpbmRleCBvdXRzaWRlIGNvbW1pdHRlZeBvChihdwsRbwoYoqhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfShAuBCIISgH/ADIGAf8AkXcMbm8Lzm8MkRCXJBUMEGR1cGxpY2F0ZSBzaWduZXLgbm8Lzm8MkkoQLgQiCEoB/wAyBgH/AJFKbm8LUdBFACGIdw0SbwoAIaBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93DhB3DyJ0aW8Obw+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85Kbw1vD1HQRW8PSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdw9Fbw8AIbUkiQBAiHcPEHcQI6cAAAB8bwkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9vEJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpvD28QUdBFbxBKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93EEVvEABAtSVZ////ABdvD9sobw3bKErYJAlKygAhKAM6bdsoNwAAdxBvECQiDB1zaWduYXR1cmUgdmVyaWZpY2F0aW9uIGZhaWxlZOBvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8ISpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwhFbwhstSWW/P//bwdquCICQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwQEADGIcBBxAE5KaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFADRKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFAERKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFAEFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeEoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeBipShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRXlKEC4EIghKAf8AMgYB/wCRSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV5ACCpShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV5ACipShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV5ADCpShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV5ADipShAuBCIISgH/ADIGAf8AkUpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EV62zByEHMibmprzkpoaWueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAILUkkGkAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFe0poaVHQRWgiAkDbMEA3AABA2yhA2yhK2CQJSsoAISgDOkBXAQIdiHASSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBVR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFeQAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV5ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXkAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFeQA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJAVwQEeXg1vP7//zUv7v//cGgLlyYICSOgAAAAaNswcWnKACGYJgUIIgdpEM57mCYICSOFAAAAetswchBzInFpEWueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85qa86YJgUJIj5rSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrACC1JI0IIgJAVwAEeBCXJgUIIgV7E7cmBQgiJnoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYFCSIVexCXJgUIIg17enl4NQH///8iAkCvLlW+").AsSerializable<Neo.SmartContract.NefFile>();

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
