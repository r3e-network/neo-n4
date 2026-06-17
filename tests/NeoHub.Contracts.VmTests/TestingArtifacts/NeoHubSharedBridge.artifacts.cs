using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSharedBridge(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SharedBridge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":256,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":355,""safe"":false},{""name"":""getSettlementManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":476,""safe"":true},{""name"":""getTokenRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":534,""safe"":true},{""name"":""getEmergencyManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":592,""safe"":true},{""name"":""setEmergencyManager"",""parameters"":[{""name"":""emergencyManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":650,""safe"":false},{""name"":""deposit"",""parameters"":[{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""targetChainId"",""type"":""Integer""},{""name"":""l2Recipient"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":794,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":4031,""safe"":false},{""name"":""getDeposit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4172,""safe"":true},{""name"":""finalizeWithdrawal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4206,""safe"":false},{""name"":""finalizeWithdrawalAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7362,""safe"":false},{""name"":""finalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7574,""safe"":false},{""name"":""emergencyFinalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7810,""safe"":false},{""name"":""getLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":3993,""safe"":true},{""name"":""migrateLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8075,""safe"":false},{""name"":""sealLockedBalanceMigration"",""parameters"":[],""returntype"":""Void"",""offset"":8401,""safe"":false},{""name"":""isLockedBalanceMigrationSealed"",""parameters"":[],""returntype"":""Boolean"",""offset"":8386,""safe"":true}],""events"":[{""name"":""DepositEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Integer""}]},{""name"":""WithdrawalFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""EmergencyManagerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""LockedBalanceMigrated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""LockedBalanceMigrationSealed"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Canonical asset escrow \u002B L1\u2194L2 transfer for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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SgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAUtSSQawAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V62zB1EHYibm1uzkpqa26eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW5KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ92RW4AFLUkkGsAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFe9swdhB3ByJybm8Hzkpqa28HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HABS1JItrABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pzRXxKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXwYqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfCCpShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV8ABipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV8ACCpShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV8ACipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV8ADCpShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV8ADipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EVoyncHbwdKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRW8HGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRW8HIKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRW8HABipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EUQdwgicmhvCM5KamtvCJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CEVvCG8HtSSLaiICQNswQFcBAh2IcBJKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV5ShAuBCIISgH/ADIGAf8AkUpoFVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV5ACCpShAuBCIISgH/ADIGAf8AkUpoGVHQRXkAKKlKEC4EIghKAf8AMgYB/wCRSmgaUdBFeQAw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fzIMAwAAAAABAAAAn3NR0EV4ABipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV5a2o16/j//2sAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFemtqNa74//9rABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pzRXtrajVx+P//awAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V8a2o1NPj//2sAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFaMp0bEoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFbBipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EVsIKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRWwAGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRRB1Im5obc5KamttnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtbLUkkWtsnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V+ShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV+GKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRX4gqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfgAYqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfgAgqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfgAoqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfgAwqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfgA4qUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFatsoNwAAdW03AADbMNsoStgkCUrKACAoAzoiAkBXBAF42zBwaMpxaRG3JAUJIjdoaRGfSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84QlyY3aUqdSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFIo5piHIQcyI+aGvOSmprUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa2m1JMFqIgJANwAAQNsoQNsoStgkCUrKACAoAzpAVwMCACWIcBNKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV52zBxEHIibmlqzkpoFWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkGgiAkDbMEBXAgUMAQHbMHg10OT//3p5NXjz//9waHy4JDAMK3dpdGhkcmF3YWwgZXhjZWVkcyBjaGFpbidzIGVzY3Jvd2VkIGJhbGFuY2Xgenk1TvL//2h8n1A1yOn//wt8e0Hb/qh0FMAfDAh0cmFuc2ZlcnpBYn1bUnFpJBoMFWFzc2V0IHRyYW5zZmVyIGZhaWxlZOB8e3p5FMAME1dpdGhkcmF3YWxGaW5hbGl6ZWRBlQFvYUBXAwo1Eub//6okEwwObmV0d29yayBwYXVzZWTgfwl/CH8HeDVR9P//fwl/CH8Hfn18e3p4Nd30//96eDUi/v//cGg1M+T//wuXJCAMG3dpdGhkcmF3YWwgYWxyZWFkeSBjb25zdW1lZOA1seT//3F6eXgTwBUMFnZlcmlmeVdpdGhkcmF3YWxMZWFmQXRpQWJ9W1JyaiQxDCx3aXRoZHJhd2FsIGxlYWYgbm90IGluIG5hbWVkIGZpbmFsaXplZCBiYXRjaOB/CX8Ifwd4aDWB/v//QFcDDDU+5f//qiQTDA5uZXR3b3JrIHBhdXNlZOB/C38Kfwl4NX3z//9/C38Kfwl/CH8Hfn16eDUH9P//eng1TP3//3BoNV3j//8LlyQgDBt3aXRoZHJhd2FsIGFscmVhZHkgY29uc3VtZWTgNdvj//9xfHt6eXgVwBUMHXZlcmlmeVdpdGhkcmF3YWxMZWFmV2l0aFByb29maUFifVtScmokPgw5d2l0aGRyYXdhbCBsZWFmIG5vdCBpbiBiYXRjaCdzIE1lcmtsZSByb290IChwcm9vZiBmYWlsZWQp4H8Lfwp/CXhoNZX9//9AVwMMNVLk//8kMQwsZW1lcmdlbmN5IHdpdGhkcmF3YWwgb25seSB2YWxpZCB3aGlsZSBwYXVzZWTgfwt/Cn8JeDV08v//fwt/Cn8Jfwh/B359eng1/vL//3p4NUP8//9waDVU4v//C5ckIAwbd2l0aGRyYXdhbCBhbHJlYWR5IGNvbnN1bWVk4DXS4v//cXx7enl4FcAVDB12ZXJpZnlXaXRoZHJhd2FsTGVhZldpdGhQcm9vZmlBYn1bUnJqJD4MOXdpdGhkcmF3YWwgbGVhZiBub3QgaW4gYmF0Y2gncyBNZXJrbGUgcm9vdCAocHJvb2YgZmFpbGVkKeB/C38Kfwl4aDWM/P//QFcAAzVy4f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNSzj//8kMAwrbWlncmF0aW9uIHJlcXVpcmVzIHRoZSBuZXR3b3JrIHRvIGJlIHBhdXNlZOA14gAAAKokJwwibG9ja2VkLWJhbGFuY2UgbWlncmF0aW9uIGlzIHNlYWxlZOB5StkoJAZFCSIGygAUsyQFCSIGeRCzqiQSDA1pbnZhbGlkIGFzc2V04HoQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeB5eDVC7///EJckNAwvbG9ja2VkIGJhbGFuY2UgYWxyZWFkeSBzZXQgZm9yIChjaGFpbklkLCBhc3NldCngeXg1Fu7//3pQNZLl//96eXgTwAwVTG9ja2VkQmFsYW5jZU1pZ3JhdGVkQZUBb2FADAEG2zA1cOD//wuYIgJANS/g//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOAMAQHbMAwBBtswNfLf//8QwAwcTG9ja2VkQmFsYW5jZU1pZ3JhdGlvblNlYWxlZEGVAW9hQCllfbY=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delDepositEnqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4, BigInteger? arg5);

    [DisplayName("DepositEnqueued")]
    public event delDepositEnqueued? OnDepositEnqueued;

    public delegate void delEmergencyManagerChanged(UInt160? obj);

    [DisplayName("EmergencyManagerChanged")]
    public event delEmergencyManagerChanged? OnEmergencyManagerChanged;

    public delegate void delLockedBalanceMigrated(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("LockedBalanceMigrated")]
    public event delLockedBalanceMigrated? OnLockedBalanceMigrated;

    public delegate void delLockedBalanceMigrationSealed();

    [DisplayName("LockedBalanceMigrationSealed")]
    public event delLockedBalanceMigrationSealed? OnLockedBalanceMigrationSealed;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delWithdrawalFinalized(BigInteger? arg1, UInt160? arg2, UInt160? arg3, BigInteger? arg4);

    [DisplayName("WithdrawalFinalized")]
    public event delWithdrawalFinalized? OnWithdrawalFinalized;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? EmergencyManager { [DisplayName("getEmergencyManager")] get; [DisplayName("setEmergencyManager")] set; }

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
    public abstract UInt160? TokenRegistry { [DisplayName("getTokenRegistry")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsLockedBalanceMigrationSealed { [DisplayName("isLockedBalanceMigrationSealed")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getDeposit")]
    public abstract byte[]? GetDeposit(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLockedBalance")]
    public abstract BigInteger? GetLockedBalance(BigInteger? chainId, UInt160? asset);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("deposit")]
    public abstract BigInteger? Deposit(UInt160? asset, BigInteger? amount, BigInteger? targetChainId, UInt160? l2Recipient);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("emergencyFinalizeWithdrawalWithProof")]
    public abstract void EmergencyFinalizeWithdrawalWithProof(BigInteger? chainId, BigInteger? batchNumber, UInt256? withdrawalLeafHash, IList<object>? siblings, BigInteger? leafIndex, UInt160? emittingContract, UInt160? l2Sender, UInt160? l2Asset, BigInteger? withdrawalNonce, UInt160? asset, UInt160? recipient, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeWithdrawal")]
    public abstract void FinalizeWithdrawal(BigInteger? chainId, UInt256? withdrawalLeafHash, UInt160? emittingContract, UInt160? l2Sender, UInt160? l2Asset, BigInteger? withdrawalNonce, UInt160? asset, UInt160? recipient, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeWithdrawalAt")]
    public abstract void FinalizeWithdrawalAt(BigInteger? chainId, BigInteger? batchNumber, UInt256? withdrawalLeafHash, UInt160? emittingContract, UInt160? l2Sender, UInt160? l2Asset, BigInteger? withdrawalNonce, UInt160? asset, UInt160? recipient, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeWithdrawalWithProof")]
    public abstract void FinalizeWithdrawalWithProof(BigInteger? chainId, BigInteger? batchNumber, UInt256? withdrawalLeafHash, IList<object>? siblings, BigInteger? leafIndex, UInt160? emittingContract, UInt160? l2Sender, UInt160? l2Asset, BigInteger? withdrawalNonce, UInt160? asset, UInt160? recipient, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("migrateLockedBalance")]
    public abstract void MigrateLockedBalance(BigInteger? chainId, UInt160? asset, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("onNEP17Payment")]
    public abstract void OnNEP17Payment(UInt160? from, BigInteger? amount, object? data = null);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("sealLockedBalanceMigration")]
    public abstract void SealLockedBalanceMigration();

    #endregion
}
