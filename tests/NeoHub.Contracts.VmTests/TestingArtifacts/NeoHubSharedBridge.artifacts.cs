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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SharedBridge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":256,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":355,""safe"":false},{""name"":""getSettlementManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":476,""safe"":true},{""name"":""getTokenRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":534,""safe"":true},{""name"":""getEmergencyManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":592,""safe"":true},{""name"":""setEmergencyManager"",""parameters"":[{""name"":""emergencyManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":650,""safe"":false},{""name"":""deposit"",""parameters"":[{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""targetChainId"",""type"":""Integer""},{""name"":""l2Recipient"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":794,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":4030,""safe"":false},{""name"":""getDeposit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":4171,""safe"":true},{""name"":""finalizeWithdrawal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4205,""safe"":false},{""name"":""finalizeWithdrawalAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7360,""safe"":false},{""name"":""finalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7572,""safe"":false},{""name"":""emergencyFinalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7808,""safe"":false},{""name"":""getLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":3992,""safe"":true},{""name"":""migrateLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8073,""safe"":false},{""name"":""sealLockedBalanceMigration"",""parameters"":[],""returntype"":""Void"",""offset"":8398,""safe"":false},{""name"":""isLockedBalanceMigrationSealed"",""parameters"":[],""returntype"":""Boolean"",""offset"":8383,""safe"":true}],""events"":[{""name"":""DepositEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Integer""}]},{""name"":""WithdrawalFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""EmergencyManagerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""LockedBalanceMigrated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""LockedBalanceMigrationSealed"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Canonical asset escrow \u002B L1\u2194L2 transfer for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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Av///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtABS1JJBrABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pzRXrbMHUQdiJubW7OSmprbp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3ZFbgAUtSSQawAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V72zB2EHcHInJubwfOSmprbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAFLUki2sAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFfEoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfBipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV8IKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXwAGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXwAIKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXwAKKlKEC4EIghKAf8AMgYB/wCRSmprSpxK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MgwDAAAAAAEAAACfc1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXlrajXr+P//awAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V6a2o1rvj//2sAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFe2tqNXH4//9rABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pzRXxrajU0+P//awAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0VoynRsShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EVsGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRWwgqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFbAAYqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFEHUibmhtzkpqa22eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW1stSSRa2yeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pzRX5KEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRX4YqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFfiCpShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV+ABipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV+ACCpShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV+ACipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV+ADCpShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV+ADipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EVq2yg3AAB1bTcAANsw2yhK2CQJSsoAICgDOiICQFcEAXjbMHBoynFpEbckBQkiN2hpEZ9KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhCXJjdpSp1KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUUijmmIchBzIj5oa85KamtR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrabUkwWoiAkA3AABA2yhA2yhK2CQJSsoAICgDOkBXAwIAJYhwE0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXnbMHEQciJuaWrOSmgVap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQaCICQNswQFcCBQwBAdsweDXR5P//enk1ePP//3BofLgkMAwrd2l0aGRyYXdhbCBleGNlZWRzIGNoYWluJ3MgZXNjcm93ZWQgYmFsYW5jZeBofJ96eTVL8v//Ncnp//8LfHtB2/6odBTAHwwIdHJhbnNmZXJ6QWJ9W1JxaSQaDBVhc3NldCB0cmFuc2ZlciBmYWlsZWTgfHt6eRTADBNXaXRoZHJhd2FsRmluYWxpemVkQZUBb2FAVwMKNRTm//+qJBMMDm5ldHdvcmsgcGF1c2Vk4H8Jfwh/B3g1UvT//38Jfwh/B359fHt6eDXe9P//eng1I/7//3BoNTXk//8LlyQgDBt3aXRoZHJhd2FsIGFscmVhZHkgY29uc3VtZWTgNbPk//9xenl4E8AVDBZ2ZXJpZnlXaXRoZHJhd2FsTGVhZkF0aUFifVtScmokMQwsd2l0aGRyYXdhbCBsZWFmIG5vdCBpbiBuYW1lZCBmaW5hbGl6ZWQgYmF0Y2jgfwl/CH8HeGg1gv7//0BXAww1QOX//6okEwwObmV0d29yayBwYXVzZWTgfwt/Cn8JeDV+8///fwt/Cn8Jfwh/B359eng1CPT//3p4NU39//9waDVf4///C5ckIAwbd2l0aGRyYXdhbCBhbHJlYWR5IGNvbnN1bWVk4DXd4///cXx7enl4FcAVDB12ZXJpZnlXaXRoZHJhd2FsTGVhZldpdGhQcm9vZmlBYn1bUnJqJD4MOXdpdGhkcmF3YWwgbGVhZiBub3QgaW4gYmF0Y2gncyBNZXJrbGUgcm9vdCAocHJvb2YgZmFpbGVkKeB/C38Kfwl4aDWW/f//QFcDDDVU5P//JDEMLGVtZXJnZW5jeSB3aXRoZHJhd2FsIG9ubHkgdmFsaWQgd2hpbGUgcGF1c2Vk4H8Lfwp/CXg1dfL//38Lfwp/CX8Ifwd+fXp4Nf/y//96eDVE/P//cGg1VuL//wuXJCAMG3dpdGhkcmF3YWwgYWxyZWFkeSBjb25zdW1lZOA11OL//3F8e3p5eBXAFQwddmVyaWZ5V2l0aGRyYXdhbExlYWZXaXRoUHJvb2ZpQWJ9W1JyaiQ+DDl3aXRoZHJhd2FsIGxlYWYgbm90IGluIGJhdGNoJ3MgTWVya2xlIHJvb3QgKHByb29mIGZhaWxlZCngfwt/Cn8JeGg1jfz//0BXAAM1dOH//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DUu4///JDAMK21pZ3JhdGlvbiByZXF1aXJlcyB0aGUgbmV0d29yayB0byBiZSBwYXVzZWTgNeEAAACqJCcMImxvY2tlZC1iYWxhbmNlIG1pZ3JhdGlvbiBpcyBzZWFsZWTgeUrZKCQGRQkiBsoAFLMkBQkiBnkQs6okEgwNaW52YWxpZCBhc3NldOB6ELckHAwXYW1vdW50IG11c3QgYmUgcG9zaXRpdmXgeXg1Q+///xCXJDQML2xvY2tlZCBiYWxhbmNlIGFscmVhZHkgc2V0IGZvciAoY2hhaW5JZCwgYXNzZXQp4Hp5eDUW7v//NZTl//96eXgTwAwVTG9ja2VkQmFsYW5jZU1pZ3JhdGVkQZUBb2FADAEG2zA1c+D//wuYIgJANTLg//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOAMAQHbMAwBBtswNfXf//8QwAwcTG9ja2VkQmFsYW5jZU1pZ3JhdGlvblNlYWxlZEGVAW9hQJGq5jI=").AsSerializable<Neo.SmartContract.NefFile>();

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
