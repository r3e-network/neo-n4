using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubExternalBridgeEscrow(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeEscrow"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":165,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":264,""safe"":false},{""name"":""getRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":385,""safe"":true},{""name"":""setRegistry"",""parameters"":[{""name"":""registry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":443,""safe"":false},{""name"":""send"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""calldata"",""type"":""ByteArray""},{""name"":""deadlineUnixSeconds"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":556,""safe"":false},{""name"":""receive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2277,""safe"":false},{""name"":""getLastOutboundNonce"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3952,""safe"":true},{""name"":""getLockedBalance"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":4343,""safe"":true},{""name"":""isInboundConsumed"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4381,""safe"":true},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":4401,""safe"":false}],""events"":[{""name"":""CrossChainSendInitiated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Hash160""},{""name"":""arg6"",""type"":""Integer""},{""name"":""arg7"",""type"":""ByteArray""}]},{""name"":""CrossChainInboundFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""RegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""L1 escrow \u002B dispatch for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeEscrow"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAAAAP3DEVcDAnkmBCJxeHBoEM5xaBHOcmlK2SgkBkUJIgbKABSzJAUJIgZpELOqJBIMDWludmFsaWQgb3duZXLgakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okFQwQaW52YWxpZCByZWdpc3RyeeBpDAH/2zA0HGoMAf7bMDQUQErZKCQGRQkiBsoAFLNAELNAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQEGb9mfOQFcBAAwB/9swNC9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAF4QZv2Z85Bkl3oMUBBkl3oMUAMFAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcBATSaQfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFgwRaW52YWxpZCBuZXcgb3duZXLgNVP///9weAwB/9swNSv///94aBLADAxPd25lckNoYW5nZWRBlQFvYUBB+CfsjEBXAQAMAf7bMDVT////cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABNef+//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQVDBBpbnZhbGlkIHJlZ2lzdHJ54HgMAf7bMDV8/v//eBHADA9SZWdpc3RyeUNoYW5nZWRBlQFvYUBXCgZ4AwAAAP8AAAAAkQMAAADgAAAAAJckSAxDZXh0ZXJuYWxDaGFpbklkIG11c3QgdXNlIHRoZSAweEUwX3h4X3h4X3h4IGZvcmVpZ24tbmFtZXNwYWNlIHByZWZpeOB5StkoJAZFCSIGygAUsyQFCSIGeRCzqiQWDBFpbnZhbGlkIHJlY2lwaWVudOB6StkoJAZFCSIGygAUsyQFCSIGehCzqiQSDA1pbnZhbGlkIGFzc2V04HsQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeB6eDVLAwAAcGg10v3//3FpC5cmBRAiDWlK2CYGRRAiBNshcmp7nmg1IwQAAHg1MwQAAHNrNaf9//90bAuXJgsRSnVFI6QBAABs2zB2bhDObhHOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJuEs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkm4TzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkm4UzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkm4VzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkm4WzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkm4XzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkkp1RW0RnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnVFGIh2bUoQLgQiCEoB/wAyBgH/AJFKbhBR0EVtGKlKEC4EIghKAf8AMgYB/wCRSm4RUdBFbSCpShAuBCIISgH/ADIGAf8AkUpuElHQRW0AGKlKEC4EIghKAf8AMgYB/wCRSm4TUdBFbQAgqUoQLgQiCEoB/wAyBgH/AJFKbhRR0EVtACipShAuBCIISgH/ADIGAf8AkUpuFVHQRW0AMKlKEC4EIghKAf8AMgYB/wCRSm4WUdBFbQA4qUoQLgQiCEoB/wAyBgH/AJFKbhdR0EVuazUgAgAAQTlTbjx3B28HejUtAgAAdwgMAQHbMG8INQMCAAALe0Hb/qh0bwcUwB8MCHRyYW5zZmVyekFifVtSdwlvCSQhDBxhc3NldCB0cmFuc2ZlciBmYWlsZWQgKGxvY2sp4G8INbMCAAB8e3p5bwdteBfADBdDcm9zc0NoYWluU2VuZEluaXRpYXRlZEGVAW9hbSICQFcDAgAZiHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFedswcRByIm5pas5KaBVqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqABS1JJBoIgJA2zBAStgmBkUQIgTbIUBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwEBFYhwEUpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkDbMEBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQTlTbjxAVwQCACmIcBRKaBBR0EV42zBxedswchBzI6sAAABpa85KaBFrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqa85KaAAVa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAUtSVW////aCICQEFifVtSQEHb/qh0QFcAAXhBm/ZnzkEvWMXtQEEvWMXtQFcIA3gDAAAA/wAAAACRAwAAAOAAAAAAlyQtDChleHRlcm5hbENoYWluSWQgbm90IGluIGZvcmVpZ24gbmFtZXNwYWNl4HnKAGa4JBsMFm1lc3NhZ2VCeXRlcyB0b28gc2hvcnTgeRDOeRHOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknkSziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ5E84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknBoeJckQgw9ZXh0ZXJuYWxDaGFpbklkIGFyZ3VtZW50IGRvZXMgbm90IG1hdGNoIHNpZ25lZCBtZXNzYWdlIGRvbWFpbuB5GM55Gc4YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkaziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeRvOABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeRzOACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeR3OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeR7OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeR/OADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGScXkgznJqEpckJwwiZGlyZWN0aW9uIG11c3QgYmUgMiAoRm9yZWlnblRvTmVvKeB5ADnOeQA6zhioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA7ziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA8zgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAPc4AIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AD7OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA/zgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAQM4AOKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJzaxCXJgUIIg1Bt8OIAwHoA6FrtiQkDB9leHRlcm5hbCBicmlkZ2UgbWVzc2FnZSBleHBpcmVk4Gl4NfoAAAB0bDWe8///C5ckLAwnaW5ib3VuZCBub25jZSBhbHJlYWR5IGNvbnN1bWVkIChyZXBsYXkp4DUQ9P//dW0MFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCQXDBJyZWdpc3RyeSBub3Qgd2lyZWTgenl4E8AVDA12ZXJpZnlJbmJvdW5kbUFifVtSdm4kLwwqcmVnaXN0cnkgdmVyaWZpZXIgcmVqZWN0ZWQgaW5ib3VuZCBtZXNzYWdl4AwBAdswbDXg+f//eQBhzncHbwdpeBPADBpDcm9zc0NoYWluSW5ib3VuZEZpbmFsaXplZEGVAW9hQEG3w4gDQFcBAh2IcBJKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV5ShAuBCIISgH/ADIGAf8AkUpoFVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV5ACCpShAuBCIISgH/ADIGAf8AkUpoGVHQRXkAKKlKEC4EIghKAf8AMgYB/wCRSmgaUdBFeQAwqUoQLgQiCEoB/wAyBgH/AJFKaBtR0EV5ADipShAuBCIISgH/ADIGAf8AkUpoHFHQRWgiAkBXAgF4Ne33//81Y/H//3BoC5cmCBAjcQEAAGjbMHFpEM5pEc4YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmkSziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSaRPOABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSaRTOACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSaRXOACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSaRbOADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSaRfOADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSIgJAVwECeXg1UvX//zXb7///cGgLlyYFECINaErYJgZFECIE2yEiAkBXAAJ5eDUP/f//NbXv//8LmCICQFcCA3kQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeBBOVNuPHB4aDWZ9v//cWk1eu///wuYJFMMTmRpcmVjdCB0cmFuc2ZlciByZWplY3RlZCDigJQgY2FsbCBTZW5kIHRvIGxvY2sgYXNzZXRzIGZvciBjcm9zcy1jaGFpbiB0cmFuc2ZlcuBpNRP3//9Ap2/oaA==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delCrossChainInboundFinalized(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("CrossChainInboundFinalized")]
    public event delCrossChainInboundFinalized? OnCrossChainInboundFinalized;

    public delegate void delCrossChainSendInitiated(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4, UInt160? arg5, BigInteger? arg6, byte[]? arg7);

    [DisplayName("CrossChainSendInitiated")]
    public event delCrossChainSendInitiated? OnCrossChainSendInitiated;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delRegistryChanged(UInt160? obj);

    [DisplayName("RegistryChanged")]
    public event delRegistryChanged? OnRegistryChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Registry { [DisplayName("getRegistry")] get; [DisplayName("setRegistry")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLastOutboundNonce")]
    public abstract BigInteger? GetLastOutboundNonce(BigInteger? externalChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLockedBalance")]
    public abstract BigInteger? GetLockedBalance(BigInteger? externalChainId, UInt160? asset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isInboundConsumed")]
    public abstract bool? IsInboundConsumed(BigInteger? externalChainId, BigInteger? nonce);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("onNEP17Payment")]
    public abstract void OnNEP17Payment(UInt160? from, BigInteger? amount, object? data = null);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("receive")]
    public abstract void Receive(BigInteger? externalChainId, byte[]? messageBytes, byte[]? proofBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("send")]
    public abstract BigInteger? Send(BigInteger? externalChainId, UInt160? recipient, UInt160? asset, BigInteger? amount, byte[]? calldata, BigInteger? deadlineUnixSeconds);

    #endregion
}
