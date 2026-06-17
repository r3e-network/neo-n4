using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubEmergencyManager(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.EmergencyManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getSettlementManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":267,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":366,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":421,""safe"":false},{""name"":""setEmergencyCouncil"",""parameters"":[{""name"":""council"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":542,""safe"":false},{""name"":""isPaused"",""parameters"":[],""returntype"":""Boolean"",""offset"":653,""safe"":true},{""name"":""pause"",""parameters"":[],""returntype"":""Void"",""offset"":688,""safe"":false},{""name"":""resume"",""parameters"":[],""returntype"":""Void"",""offset"":799,""safe"":false},{""name"":""escapeHatchExit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sender"",""type"":""Hash160""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":876,""safe"":false},{""name"":""escapeHatchExitWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sender"",""type"":""Hash160""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1550,""safe"":false}],""events"":[{""name"":""PauseStateChanged"",""parameters"":[{""name"":""obj"",""type"":""Boolean""}]},{""name"":""EscapeHatchExit"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""CouncilChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Emergency pause \u002B escape hatch for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.EmergencyManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAAAAP1YB1cEAnkmByPBAAAAeHBoEM5xaBHOcmgSznNpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQSDA1pbnZhbGlkIG93bmVy4GpK2SgkBkUJIgbKABSzJAUJIgZqELOqJBQMD2ludmFsaWQgY291bmNpbOBrStkoJAZFCSIGygAUsyQFCSIGaxCzqiQfDBppbnZhbGlkIHNldHRsZW1lbnQgbWFuYWdlcuBpDAH/2zA0MGoMAQLbMDQoawwBBNswNCAMAQDbMAwBAdswNDBAStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcBAAwBBNswNC9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAF4QZv2Z85Bkl3oMUBBkl3oMUAMFAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcBAAwB/9swNMxwaAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAQE0xkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DV/////cHgMAf/bMDXe/v//eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwABNU3///9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQUDA9pbnZhbGlkIGNvdW5jaWzgeAwBAtswNWr+//94EcAMDkNvdW5jaWxDaGFuZ2VkQZUBb2FAVwEADAEB2zA1rf7//3BoC5gkBQkiCWjbMBDOEZciAkDbMEBXAQAMAQLbMDWK/v//StgmE0UMDWNvdW5jaWwgdW5zZXQ6StgkCUrKABQoAzpwaEH4J+yMJBAMC25vdCBjb3VuY2ls4AwBAdswDAEB2zA19/3//wgRwAwRUGF1c2VTdGF0ZUNoYW5nZWRBlQFvYUBXAQA1TP7//3BoQfgn7IwkEwwObm90IGF1dGhvcml6ZWTgDAEA2zAMAQHbMDWq/f//CRHADBFQYXVzZVN0YXRlQ2hhbmdlZEGVAW9hQFcDAzUe////JCkMJGVzY2FwZSBoYXRjaCBvbmx5IHZhbGlkIHdoaWxlIHBhdXNlZOB5Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeng1KwEAAHBoNYP9//8LlyQhDBxlc2NhcGUgbGVhZiBhbHJlYWR5IGNvbnN1bWVk4DUk/f//cWkMFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCQdDBhzZXR0bGVtZW50IG1hbmFnZXIgdW5zZXTgeBHAFQwVZ2V0Q2Fub25pY2FsU3RhdGVSb290aUFifVtScmoMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okHAwXbm8gZmluYWxpemVkIHN0YXRlIHJvb3TganqXJDQML2xlYWYgZG9lcyBub3QgbWF0Y2ggbGF0ZXN0IGZpbmFsaXplZCBzdGF0ZSByb2904AwBAdswaDUz/P//enl4E8AMD0VzY2FwZUhhdGNoRXhpdEGVAW9hQFcDAgAliHATSmgQUdBFeAH/AJFKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipAf8AkUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKkB/wCRShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKkB/wCRShAuBCIISgH/ADIGAf8AkUpoFFHQRXnbMHEQciJuaWrOSmgVap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQaCICQNswQEFifVtSQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwMFNXz8//8kKQwkZXNjYXBlIGhhdGNoIG9ubHkgdmFsaWQgd2hpbGUgcGF1c2Vk4HlB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB6eDWJ/v//cGg14fr//wuXJCEMHGVzY2FwZSBsZWFmIGFscmVhZHkgY29uc3VtZWTgNYL6//9xaQwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJB0MGHNldHRsZW1lbnQgbWFuYWdlciB1bnNldOB8e3p4FMAVDBh2ZXJpZnlTdGF0ZUxlYWZXaXRoUHJvb2ZpQWJ9W1JyaiREDD9sZWFmIGRvZXMgbm90IE1lcmtsZS12ZXJpZnkgYWdhaW5zdCBsYXRlc3QgZmluYWxpemVkIHN0YXRlIHJvb3TgDAEB2zBoNb75//96eXgTwAwPRXNjYXBlSGF0Y2hFeGl0QZUBb2FAWWOxFg==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delCouncilChanged(UInt160? obj);

    [DisplayName("CouncilChanged")]
    public event delCouncilChanged? OnCouncilChanged;

    public delegate void delEscapeHatchExit(BigInteger? arg1, UInt160? arg2, UInt256? arg3);

    [DisplayName("EscapeHatchExit")]
    public event delEscapeHatchExit? OnEscapeHatchExit;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delPauseStateChanged(bool? obj);

    [DisplayName("PauseStateChanged")]
    public event delPauseStateChanged? OnPauseStateChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? SettlementManager { [DisplayName("getSettlementManager")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsPaused { [DisplayName("isPaused")] get; }

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("escapeHatchExit")]
    public abstract void EscapeHatchExit(BigInteger? chainId, UInt160? sender, UInt256? leafHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("escapeHatchExitWithProof")]
    public abstract void EscapeHatchExitWithProof(BigInteger? chainId, UInt160? sender, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("pause")]
    public abstract void Pause();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("resume")]
    public abstract void Resume();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setEmergencyCouncil")]
    public abstract void SetEmergencyCouncil(UInt160? council);

    #endregion
}
