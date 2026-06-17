using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSequencerBond(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SequencerBond"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":516,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":615,""safe"":false},{""name"":""getBondAsset"",""parameters"":[],""returntype"":""Hash160"",""offset"":736,""safe"":true},{""name"":""getMinBond"",""parameters"":[],""returntype"":""Integer"",""offset"":794,""safe"":true},{""name"":""setMinBond"",""parameters"":[{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":841,""safe"":false},{""name"":""registerSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":944,""safe"":false},{""name"":""revokeSlasher"",""parameters"":[{""name"":""slasher"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1064,""safe"":false},{""name"":""isSlasher"",""parameters"":[{""name"":""who"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1150,""safe"":true},{""name"":""deposit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1167,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":1909,""safe"":false},{""name"":""getBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":2043,""safe"":true},{""name"":""hasMinBond"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":2081,""safe"":true},{""name"":""slash"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2097,""safe"":false},{""name"":""withdraw"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""sequencer"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2374,""safe"":false}],""events"":[{""name"":""BondDeposited"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""BondSlashed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""BondWithdrawn"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""MinBondChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""SlasherRegistered"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""SlasherRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Per-(chain, sequencer) slashable bond escrow for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SequencerBond"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAAAAP0pClcIAnkmByMVAQAAeHBoEM5xaBHOcmgSznNpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQSDA1pbnZhbGlkIG93bmVy4GpK2SgkBkUJIgbKABSzJAUJIgZqELOqJBcMEmludmFsaWQgYm9uZCBhc3NldOBryhC3JCQMH3NsYXNoZXJzIGxpc3QgbXVzdCBiZSBub24tZW1wdHngaQwB/9swNY8AAABqDAED2zA1hAAAAAJAQg8ADAEE2zA1kQAAAGtKdMp1EHYiU2xuzncHbwdK2SgkBkUJIgbKABSzJAUJIgdvBxCzqiQkDB9pbnZhbGlkIHNsYXNoZXIgaW4gaW5pdGlhbCBsaXN04G8HNGsMAQHbMFA0TW6cdm5tMK1AStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAwEAFYhwEkpoEFHQRXjbMHEQciJuaWrOSmgRap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAUtSSQaCICQNswQFcBAAwB/9swNC9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAF4QZv2Z85Bkl3oMUBBkl3oMUAMFAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcBATSaQfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFgwRaW52YWxpZCBuZXcgb3duZXLgNVP///9weAwB/9swNXD+//94aBLADAxPd25lckNoYW5nZWRBlQFvYUBB+CfsjEBXAQAMAQPbMDVT////cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwEADAEE2zA1Gf///3BoC5cmBRAiDWhK2CYGRRAiBNshIgJAStgmBkUQIgTbIUBXAQE1uP7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeA0knB4DAEE2zA1uP3//3hoEsAMDk1pbkJvbmRDaGFuZ2VkQZUBb2FAVwABNVH+//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQUDA9pbnZhbGlkIHNsYXNoZXLgeDV5/f//DAEB2zBQNVj9//94EcAMEVNsYXNoZXJSZWdpc3RlcmVkQZUBb2FAVwABNdn9//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NSz9//80G3gRwAwOU2xhc2hlclJldm9rZWRBlQFvYUBXAAF4QZv2Z85BL1jF7UBBL1jF7UBXAAF4NfP8//81tP3//wuYQFcHA3gQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HoQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeB5StkoJAZFCSIGygAUsyQFCSIGeRCzqiQWDBFpbnZhbGlkIHNlcXVlbmNlcuA13v3//3BBOVNuPHF5eDWaAAAAcmo1JP3//3NrC5cmBRAiDWtK2CYGRRAiBNshdGx6nmo1FPz//2loNV0BAAB1DAEB2zBtNRf8//8LekHb/qh0aRTAHwwIdHJhbnNmZXJoQWJ9W1J2biQaDBVhc3NldCB0cmFuc2ZlciBmYWlsZWTgbTXk/v//enl4E8AMDUJvbmREZXBvc2l0ZWRBlQFvYUBBOVNuPEBXAwIAGYhwEUpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXnbMHEQciJuaWrOSmgVap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAUtSSQaCICQFcEAgApiHAVSmgQUdBFeNswcXnbMHIQcyOrAAAAaWvOSmgRa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFamvOSmgAFWueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAFLUlVv///2giAkBBYn1bUkBB2/6odEBXAgN5ELckHAwXYW1vdW50IG11c3QgYmUgcG9zaXRpdmXgQTlTbjxweGg1+v7//3FpNZX6//8LmCRHDEJkaXJlY3QgdHJhbnNmZXIgcmVqZWN0ZWQg4oCUIGNhbGwgRGVwb3NpdCB0byBjcmVkaXQgc2VxdWVuY2VyIGJvbmTgaTV0/P//QFcBAnl4Nar9//81Nvr//3BoC5cmBRAiDWhK2CYGRRAiBNshIgJAVwACeXg01TXy+v//uCICQFcGBEE5U248cGg1Q/z//yQoDCNjYWxsZXIgaXMgbm90IGFuIGF1dGhvcml6ZWQgc2xhc2hlcuB6ELckHAwXYW1vdW50IG11c3QgYmUgcG9zaXRpdmXgeXg1If3//3FpNav5//9yaguXJgUQIg1qStgmBkUQIgTbIXNrergkFgwRaW5zdWZmaWNpZW50IGJvbmTga3qfaTWC+P//e0rZKCQGRQkiBsoAFLMkBQkiBnsQs6omSjX7+f//dAt6e0Hb/qh0FMAfDAh0cmFuc2ZlcmxBYn1bUnVtJCUMIHNsYXNoIHBheW91dCB0byByZWNpcGllbnQgZmFpbGVk4Ht6eXgUwAwLQm9uZFNsYXNoZWRBlQFvYUBXBQM1u/j//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HoQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeB5eDUj/P//cGg1rfj//3FpC5cmBRAiDWlK2CYGRRAiBNshcmp6uCQZDBRpbnN1ZmZpY2llbnQgYmFsYW5jZeBqep9oNYH3//81E/n//3MLenlB2/6odBTAHwwIdHJhbnNmZXJrQWJ9W1J0bCQfDBp3aXRoZHJhd2FsIHRyYW5zZmVyIGZhaWxlZOB6eXgTwAwNQm9uZFdpdGhkcmF3bkGVAW9hQP+WeJw=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delBondDeposited(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("BondDeposited")]
    public event delBondDeposited? OnBondDeposited;

    public delegate void delBondSlashed(BigInteger? arg1, UInt160? arg2, BigInteger? arg3, UInt160? arg4);

    [DisplayName("BondSlashed")]
    public event delBondSlashed? OnBondSlashed;

    public delegate void delBondWithdrawn(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("BondWithdrawn")]
    public event delBondWithdrawn? OnBondWithdrawn;

    public delegate void delMinBondChanged(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("MinBondChanged")]
    public event delMinBondChanged? OnMinBondChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delSlasherRegistered(UInt160? obj);

    [DisplayName("SlasherRegistered")]
    public event delSlasherRegistered? OnSlasherRegistered;

    public delegate void delSlasherRevoked(UInt160? obj);

    [DisplayName("SlasherRevoked")]
    public event delSlasherRevoked? OnSlasherRevoked;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? BondAsset { [DisplayName("getBondAsset")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? MinBond { [DisplayName("getMinBond")] get; [DisplayName("setMinBond")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBalance")]
    public abstract BigInteger? GetBalance(BigInteger? chainId, UInt160? sequencer);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("hasMinBond")]
    public abstract bool? HasMinBond(BigInteger? chainId, UInt160? sequencer);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isSlasher")]
    public abstract bool? IsSlasher(UInt160? who);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("deposit")]
    public abstract void Deposit(BigInteger? chainId, UInt160? sequencer, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("onNEP17Payment")]
    public abstract void OnNEP17Payment(UInt160? from, BigInteger? amount, object? data = null);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerSlasher")]
    public abstract void RegisterSlasher(UInt160? slasher);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeSlasher")]
    public abstract void RevokeSlasher(UInt160? slasher);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("slash")]
    public abstract void Slash(BigInteger? chainId, UInt160? sequencer, BigInteger? amount, UInt160? recipient);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("withdraw")]
    public abstract void Withdraw(BigInteger? chainId, UInt160? sequencer, BigInteger? amount);

    #endregion
}
