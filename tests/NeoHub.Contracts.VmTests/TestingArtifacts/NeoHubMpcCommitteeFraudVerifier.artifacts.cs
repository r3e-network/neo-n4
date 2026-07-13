using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubMpcCommitteeFraudVerifier(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MpcCommitteeFraudVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":220,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":319,""safe"":false},{""name"":""getVerifier"",""parameters"":[],""returntype"":""Hash160"",""offset"":440,""safe"":true},{""name"":""getBond"",""parameters"":[],""returntype"":""Hash160"",""offset"":498,""safe"":true},{""name"":""isSlashed"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""signerIdx"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":556,""safe"":true},{""name"":""slash"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""signerIdx"",""type"":""Integer""},{""name"":""message1Bytes"",""type"":""ByteArray""},{""name"":""signature1"",""type"":""ByteArray""},{""name"":""message2Bytes"",""type"":""ByteArray""},{""name"":""signature2"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":696,""safe"":false}],""events"":[{""name"":""CommitteeMemberSlashed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""},{""name"":""arg5"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Slashes equivocating committee members on the cross-foreign-chain bridge."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MpcCommitteeFraudVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAIb9XWrEYlohBNhCjWhKIbN4LZscg92ZXJpZnlXaXRoRUNEc2EEAAEPG/V1qxGJaIQTYQo1oSiGzeC2bHIRdmVyaWZ5V2l0aEVkMjU1MTkDAAEPAAD9mQ5XBAJ5JgcjqAAAAHhwaBDOcWgRznJoEs5zaUrZKCQGRQkiBsoAFLMkBQkiBmkQs6okEgwNaW52YWxpZCBvd25lcuBqStkoJAZFCSIGygAUsyQFCSIGahCzqiQVDBBpbnZhbGlkIHZlcmlmaWVy4GtK2SgkBkUJIgbKABSzJAUJIgZrELOqJBEMDGludmFsaWQgYm9uZOBpDAH/2zA0JGoMAQHbMDQcawwBAtswNBRAStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwEADAH/2zA0L3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAAXhBm/ZnzkGSXegxQEGSXegxQAwUAAAAAAAAAAAAAAAAAAAAAAAAAABAVwEBNJpB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQWDBFpbnZhbGlkIG5ldyBvd25lcuA1U////3B4DAH/2zA1K////3hoEsAMDE93bmVyQ2hhbmdlZEGVAW9hQEH4J+yMQFcBAAwBAdswNVP///9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAQAMAQLbMDUZ////cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwACeXg0DDXg/v//C5giAkBXAQIWiHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFeUpoFVHQRWgiAkBXEQZ5eDSAcGg1Uv7//wuXJDUMMGFscmVhZHkgc2xhc2hlZCBmb3IgKGV4dGVybmFsQ2hhaW5JZCwgc2lnbmVySWR4KeB6C5gkBQkiB3rKAGa4JEEMPG1lc3NhZ2UxQnl0ZXMgdG9vIHNob3J0IGZvciBFeHRlcm5hbENyb3NzQ2hhaW5NZXNzYWdlIGxheW91dOB8C5gkBQkiB3zKAGa4JEEMPG1lc3NhZ2UyQnl0ZXMgdG9vIHNob3J0IGZvciBFeHRlcm5hbENyb3NzQ2hhaW5NZXNzYWdlIGxheW91dOB7C5gkBQkiB3vKAECXJCAMG3NpZ25hdHVyZTEgbXVzdCBiZSA2NCBieXRlc+B9C5gkBQkiB33KAECXJCAMG3NpZ25hdHVyZTIgbXVzdCBiZSA2NCBieXRlc+AQejVTBgAAcRB8NUsGAAByaXiXJCgMI21lc3NhZ2UxIGNoYWluSWQgIT0gZXh0ZXJuYWxDaGFpbklk4Gp4lyQoDCNtZXNzYWdlMiBjaGFpbklkICE9IGV4dGVybmFsQ2hhaW5JZOAYejXtBgAAcxh8NeUGAAB0a2yXJGkMZG1lc3NhZ2VzIGhhdmUgZGlmZmVyZW50IG5vbmNlcyDigJQgbm90IGFuIGVxdWl2b2NhdGlvbiAoYSBtZW1iZXIgaXMgYWxsb3dlZCB0byBzaWduIGRpc3RpbmN0IG5vbmNlcyngeiDOEpckLwwqbWVzc2FnZTEgZGlyZWN0aW9uIG11c3QgYmUgRm9yZWlnblRvTmVvKDIp4HwgzhKXJC8MKm1lc3NhZ2UyIGRpcmVjdGlvbiBtdXN0IGJlIEZvcmVpZ25Ub05lbygyKeB8ejXFCAAAqiQ4DDNtZXNzYWdlcyBhcmUgYnl0ZS1pZGVudGljYWwg4oCUIG5vdCBhbiBlcXVpdm9jYXRpb27gNTv8//91bQwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJBcMEnZlcmlmaWVyIG5vdCB3aXJlZOB4EcAVDAxnZXRDb21taXR0ZWVtQWJ9W1J2bsoTuCQwDCtubyBjb21taXR0ZWUgcmVnaXN0ZXJlZCBmb3IgZXh0ZXJuYWxDaGFpbklk4G4RzncHbhLOdwh5bwe1JCAMG3NpZ25lcklkeCA+PSBjb21taXR0ZWUgc2l6ZeBvCBGXJgYAISIEACB3CW7KE28HbwmgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQjDB5jb21taXR0ZWUgYmxvYiBsZW5ndGggbWlzbWF0Y2jgbwmIdwoQdwsj1wAAAG4TeW8JoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn28LnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSm8KbwtR0EVvC0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cLRW8Lbwm1JSn///9vCBGXJkAAFnvbKG8K2yhK2CQJSsoAISgDOnrbKDcAAEp3C0UAFn3bKG8K2yhK2CQJSsoAISgDOnzbKDcAAEp3DEUiPW8IEpckFQwQdW5rbm93biBjdXJ2ZVRhZ+B72yhvCtsoetsoNwEASncLRX3bKG8K2yh82yg3AQBKdwxFbwskPAw3c2lnbmF0dXJlMSBkb2VzIG5vdCB2ZXJpZnkgYWdhaW5zdCBjb21taXR0ZWVbc2lnbmVySWR4XeBvDCQ8DDdzaWduYXR1cmUyIGRvZXMgbm90IHZlcmlmeSBhZ2FpbnN0IGNvbW1pdHRlZVtzaWduZXJJZHhd4Hl4EsAVDA9nZXRTaWduZXJNZW1iZXJtQWJ9W1J3DW8NDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJglgAAAAAx4bm8gYm9uZC1ob2xkZXIgYm91bmQgdG8gdGhpcyBzaWduZXIgc2xvdCDigJQgb3BlcmF0b3IgbXVzdCBjYWxsIFJlZ2lzdGVyQ29tbWl0dGVlV2l0aE1lbWJlcnMgYmVmb3JlIHNsYXNoaW5nIGNhbiBzdWNjZWVk4DWT+P//dw5vDgwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJBwMF2JvbmQgY29udHJhY3Qgbm90IHdpcmVk4G8NeBLAFQwKZ2V0QmFsYW5jZW8OQWJ9W1J3D28PELckOww2ZXF1aXZvY2F0b3IgaGFzIHplcm8gYm9uZCBiYWxhbmNlIOKAlCBub3RoaW5nIHRvIHNsYXNo4EE5U248dxAMAQHbMGg1gQQAAG8Qbw9vDXgUwB8MBXNsYXNobw5BYn1bUkVvEG8Pbw15eBXADBZDb21taXR0ZWVNZW1iZXJTbGFzaGVkQZUBb2FAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRkiICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRWeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AKKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRaeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AMKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eReeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AOKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZIiAkBXAQJ4ynnKmCYFCSJOEHAiQXhoznlozpgmBQkiPmhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9wRWh4yrUkvQgiAkBBYn1bUkA3AABA2yhA2yhK2CQJSsoAISgDOkA3AQBAQTlTbjxAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQGYBZQE=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delCommitteeMemberSlashed(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, BigInteger? arg4, UInt160? arg5);

    [DisplayName("CommitteeMemberSlashed")]
    public event delCommitteeMemberSlashed? OnCommitteeMemberSlashed;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Bond { [DisplayName("getBond")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Verifier { [DisplayName("getVerifier")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isSlashed")]
    public abstract bool? IsSlashed(BigInteger? externalChainId, BigInteger? signerIdx);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("slash")]
    public abstract void Slash(BigInteger? externalChainId, BigInteger? signerIdx, byte[]? message1Bytes, byte[]? signature1, byte[]? message2Bytes, byte[]? signature2);

    #endregion
}
