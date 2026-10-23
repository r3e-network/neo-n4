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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SharedBridge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":179,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":278,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":424,""safe"":true},{""name"":""lockGovernance"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":459,""safe"":false},{""name"":""getSettlementManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":738,""safe"":true},{""name"":""executeGovernanceProposal"",""parameters"":[{""name"":""action"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":796,""safe"":false},{""name"":""buildSetSettlementManagerAction"",""parameters"":[{""name"":""manager"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":2114,""safe"":true},{""name"":""buildRegisterMappingAction"",""parameters"":[{""name"":""mappingBytes"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":2737,""safe"":true},{""name"":""buildSetMappingActiveAction"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""ByteArray"",""offset"":3946,""safe"":true},{""name"":""setSettlementManager"",""parameters"":[{""name"":""settlementManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":4702,""safe"":false},{""name"":""getTokenRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":4746,""safe"":true},{""name"":""setTokenRegistry"",""parameters"":[{""name"":""tokenRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":4833,""safe"":false},{""name"":""getEmergencyManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":4877,""safe"":true},{""name"":""setEmergencyManager"",""parameters"":[{""name"":""emergencyManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":4935,""safe"":false},{""name"":""registerMapping"",""parameters"":[{""name"":""mappingBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":5072,""safe"":false},{""name"":""setActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":5111,""safe"":false},{""name"":""getMapping"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":5152,""safe"":true},{""name"":""getL2Asset"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":5182,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5240,""safe"":true},{""name"":""getL1Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5275,""safe"":true},{""name"":""getL2Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5308,""safe"":true},{""name"":""deposit"",""parameters"":[{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""targetChainId"",""type"":""Integer""},{""name"":""l2Recipient"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":5341,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":8082,""safe"":false},{""name"":""getDeposit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":8163,""safe"":true},{""name"":""finalizeWithdrawal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8193,""safe"":false},{""name"":""finalizeWithdrawalAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":10673,""safe"":false},{""name"":""finalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":10885,""safe"":false},{""name"":""emergencyFinalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":11121,""safe"":false},{""name"":""getLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":11387,""safe"":true},{""name"":""migrateLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":11425,""safe"":false},{""name"":""sealLockedBalanceMigration"",""parameters"":[],""returntype"":""Void"",""offset"":11699,""safe"":false},{""name"":""isLockedBalanceMigrationSealed"",""parameters"":[],""returntype"":""Boolean"",""offset"":11667,""safe"":true},{""name"":""sendMessage"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""targetContract"",""type"":""Hash160""},{""name"":""messagePayload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":11828,""safe"":false},{""name"":""getL1ToL2Nonce"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":12222,""safe"":true},{""name"":""getL1ToL2Message"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":12279,""safe"":true},{""name"":""publishMessageRoots"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""l2ToL1Root"",""type"":""Hash256""},{""name"":""l2ToL2Root"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":12306,""safe"":false},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12443,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":12547,""safe"":true},{""name"":""isL2ToL1MessageConsumed"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":12616,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":12827,""safe"":false}],""events"":[{""name"":""DepositEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Integer""}]},{""name"":""WithdrawalFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""MappingRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""MappingActiveChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""L1ToL2Enqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""L2ToL1Consumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""MessageRootsPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""EmergencyManagerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""LockedBalanceMigrated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""LockedBalanceMigrationSealed"",""parameters"":[]},{""name"":""GovernanceLocked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""SettlementManagerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Canonical asset escrow \u002B L1\u2194L2 transfer \u002B message router for Neo Elastic Network."",""Version"":""0.2.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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cm5hbmNlIGFjdGlvbuBAQWJ9W1JAVwECeMp5yrUmBQkiThBwIkF4aM55aM6YJgUJIj5oSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcEVoecq1JL0IIgJAVwICABSIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpABS1JJBo2yhK2CQJSsoAFCgDOiICQNsoStgkCUrKABQoAzpAVwECeMp5ypckJgwhZ292ZXJuYW5jZSBhY3Rpb24gbGVuZ3RoIG1pc21hdGNo4BBwImN4aM55aM6XJCcMImdvdmVybmFuY2UgYWN0aW9uIHBheWxvYWQgbWlzbWF0Y2jgaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaHnKtSSbQFcDAVjKABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hwEHEQciJvWGrOSmhpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcVHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWpYyrUkj3hpaDQ7aQAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUVoIgJAVwIDetswcBBxIm5oac5KeHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpABS1JJBA2zBAVwABeDU09///C5ckHgwZcHJvcG9zYWwgYWxyZWFkeSBjb25zdW1lZOBAVwECGYhweEpoEFHQRXkRaDQGaCICQFcBAxBwI7EAAAB6GGigSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn6lKEC4EIghKAf8AMgYB/wCRSnh5aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaBi1JVH///9AVwMBeAuYJBsMFm1hcHBpbmcgYnl0ZXMgcmVxdWlyZWTgWcp4yp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHAQcRByIm9Zas5KaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFalnKtSSPEHIib3hqzkpoaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FR0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqeMq1JI9oIgJAVwcBeMoAMpckGgwVbWFwcGluZyBzaXplIG1pc21hdGNo4BB4Ne36//9wABR4NXEBAABxABh4Ndv6//9yeAAsznN4AC/OdHgAMM51aErZKCQGRQkiBsoAFLMkBQkiBmgQs6okFQwQaW52YWxpZCBMMSBhc3NldOBqStkoJAZFCSIGygAUsyQFCSIGahCzqiQVDBBpbnZhbGlkIEwyIGFzc2V04GkQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4GwAErYkBQkiBm0AErYkFQwQaW52YWxpZCBkZWNpbWFsc+BrEZcmRGwQlyQeDBlMMSBORU8gZGVjaW1hbHMgbXVzdCBiZSAw4G0YlyQeDBlMMiBORU8gZGVjaW1hbHMgbXVzdCBiZSA44GsQlyY+bBiXJBsMFkdBUyBkZWNpbWFscyBtdXN0IGJlIDjgbRiXJBsMFkdBUyBkZWNpbWFscyBtdXN0IGJlIDjgaWg1ewEAAHZ4bjUn8///amloE8AMEU1hcHBpbmdSZWdpc3RlcmVkQZUBb2FAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5J4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfkkoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFAVwECABmIcCBKaBBR0EV4EWg1N/r//3lKEC4EIghKAf8AMgYB/wCRSmgAFVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaAAWUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoABdR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoABhR0EVoIgJAVwMDWsoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHAQcRByIm9aas5KaGlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFalrKtSSPeGloNbP4//9pABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRXlpaDRHaRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRXomBREiAxBKaGlR0EVoIgJAVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8A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Av///38yDAMAAAAAAQAAAJ9xaYhyEHMAFGtKY2p42zA1dgQAAAAUW0pjanrbMDVoBAAAABRbSmNqe9swNVoEAAB8ShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EV8GKlKEC4EIghKAf8AMgYB/wCRSmpbSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfY1HQRXwgqUoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFfAAYqUoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFfAAgqUoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFfAAoqUoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFfAAwqUoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFfAA4qUoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFaMp0bEoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFbBipShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EVsIKlKEC4EIghKAf8AMgYB/wCRSmpbSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfY1HQRWwAGKlKEC4EIghKAf8AMgYB/wCRSmpbSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfY1HQRWxbSmNqaDXSAAAAaiICQFcDAXjbMHBoyhG3JAUJIjhoaMoRn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OEJcmf2jKEZ9KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHEQciI+aGrOSmlqUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFamnKtSTAaSIFaCICQFcBBBBwIm54aM5KeVtonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVoSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcEVoe7UkkVt7nkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KY0VAVwACeXgSNANAVwIDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRRBxI7UAAAB6GGmgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn6kB/wCRShAuBCIISgH/ADIGAf8AkUpoFWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkYtSVN////aCICQFcBAgApiHAUSmgQUdBFeBFoNXDq//95ABVoNWfq//9oIgJAVwABeEGb9mfOQS9Yxe1AQS9Yxe1AVwMDeXg0KHBoNfnh//9xaQuXJgUQIg1pStgmBkUQIgTbIXJqep5oNeD4//9AVwECABmIcBVKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV5FWg1qOn//2giAkBXAgNBOVNuPHB4aDUT////cWk1RuH//wuYJDEMLHVuc29saWNpdGVkIHRyYW5zZmVyIHJlamVjdGVkOyB1c2UgZGVwb3NpdCgp4EBBOVNuPEBXAQJ5eDWM/f//Nf3g//9waAuXJgULIgVo2zAiAkBXAwk1kPP//6okEwwObmV0d29yayBwYXVzZWTgfwh/B354NaYAAAB/CH8Hfn18e3p5eDUzAQAAeXg1iwcAAHBoNang//8LlyQgDBt3aXRoZHJhd2FsIGFscmVhZHkgY29uc3VtZWTgNXri//9xeXgSwB8MFHZlcmlmeVdpdGhkcmF3YWxMZWFmaUFifVtScmokKwwmd2l0aGRyYXdhbCBsZWFmIG5vdCBpbiBmaW5hbGl6ZWQgYmF0Y2jgfwh/B354aDX0BwAAQFcABHgQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4HlK2SgkBkUJIgbKABSzJAUJIgZ5ELOqJBIMDWludmFsaWQgYXNzZXTgekrZKCQGRQkiBsoAFLMkBQkiBnoQs6okFgwRaW52YWxpZCByZWNpcGllbnTgexC3JBwMF2Ftb3VudCBtdXN0IGJlIHBvc2l0aXZl4EBXAgl6StkoJAZFCSIGygAUsyQFCSIGehCzqiQeDBlpbnZhbGlkIGVtaXR0aW5nIGNvbnRyYWN04HtK2SgkBkUJIgbKABSzJAUJIgZ7ELOqJBUMEGludmFsaWQgbDJTZW5kZXLgfErZKCQGRQkiBsoAFLMkBQkiBnwQs6okFAwPaW52YWxpZCBsMkFzc2V04Hh+NUfy//9waHyXJC0MKGwyQXNzZXQgbWlzbWF0Y2ggd2l0aCByZWdpc3RlcmVkIG1hcHBpbmfgfwh/B359fHt6eDQzcWl5lyQsDCdsZWFmIGhhc2ggbWlzbWF0Y2ggd2l0aCBzdXBwbGllZCBmaWVsZHPgQFcHCH8HNY/5//9wAHRoyp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcWmIchBzeEoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFeBipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRQAUa0pjannbMDXp+P//ABRbSmNqetswNdv4//8AFFtKY2p72zA1zfj//3xKEC4EIghKAf8AMgYB/wCRSmpbSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfY1HQRXwYqUoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFfCCpShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EV8ABipShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EV8ACCpShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EV8ACipShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EV8ADCpShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EV8ADipShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EUAFFtKY2p92zA1bPb//wAUW0pjan7bMDVe9v//aMp0bEoQLgQiCEoB/wAyBgH/AJFKaltKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9jUdBFbBipShAuBCIISgH/ADIGAf8AkUpqW0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2NR0EVsIKlKEC4EIghKAf8AMgYB/wCRSmpbSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfY1HQRWwAGKlKEC4EIghKAf8AMgYB/wCRSmpbSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfY1HQRWxbSmNqaDUp9f//atsoNwAAdW03AAB2bkrYJAlKygAgKAM6IgJANwAAQNsoQFcDAgAliHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFedswcRByIm5pas5KaBVqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqACC1JJBoIgJA2zBAVwEFfHp5NGMMAQHbMHg12Nf//wt8e0Hb/qh0FMAfDAh0cmFuc2ZlcnpBYn1bUnBoJBgME2Fzc2V0IHBheW91dCBmYWlsZWTgfHt6eRTADBNXaXRoZHJhd2FsRmluYWxpemVkQZUBb2FAVwMDeXg18PX//3BoNb7X//9xaQuYJDMMLm5vIGxvY2tlZCBiYWxhbmNlIHJlY29yZGVkIGZvciBjaGFpbiBhbmQgYXNzZXTgaUrYJgZFECIE2yFyanq4JDAMK3dpdGhkcmF3YWwgZXhjZWVkcyBjaGFpbidzIGVzY3Jvd2VkIGJhbGFuY2XganqfaDVE7v//QFcDCjXg6f//qiQTDA5uZXR3b3JrIHBhdXNlZOB/CX8Ifwd4NfX2//9/CX8Ifwd+fXx7eng1gff//3p4Ndn9//9waDX31v//C5ckIAwbd2l0aGRyYXdhbCBhbHJlYWR5IGNvbnN1bWVk4DXI2P//cXp5eBPAHwwWdmVyaWZ5V2l0aGRyYXdhbExlYWZBdGlBYn1bUnJqJDEMLHdpdGhkcmF3YWwgbGVhZiBub3QgaW4gbmFtZWQgZmluYWxpemVkIGJhdGNo4H8Jfwh/B3hoNTj+//9AVwMMNQzp//+qJBMMDm5ldHdvcmsgcGF1c2Vk4H8Lfwp/CXg1Ifb//38Lfwp/CX8Ifwd+fXp4Nav2//96eDUD/f//cGg1Idb//wuXJCAMG3dpdGhkcmF3YWwgYWxyZWFkeSBjb25zdW1lZOA18tf//3F8e3p5eBXAHwwddmVyaWZ5V2l0aGRyYXdhbExlYWZXaXRoUHJvb2ZpQWJ9W1JyaiQ+DDl3aXRoZHJhd2FsIGxlYWYgbm90IGluIGJhdGNoJ3MgTWVya2xlIHJvb3QgKHByb29mIGZhaWxlZCngfwt/Cn8JeGg1TP3//0BXAww1IOj//yQyDC1uZXR3b3JrIG11c3QgYmUgcGF1c2VkIGZvciBlbWVyZ2VuY3kgZmluYWxpemXgfwt/Cn8JeDUX9f//fwt/Cn8Jfwh/B359eng1ofX//3p4Nfn7//9waDUX1f//C5ckIAwbd2l0aGRyYXdhbCBhbHJlYWR5IGNvbnN1bWVk4DXo1v//cXx7enl4FcAfDB12ZXJpZnlXaXRoZHJhd2FsTGVhZldpdGhQcm9vZmlBYn1bUnJqJD4MOXdpdGhkcmF3YWwgbGVhZiBub3QgaW4gYmF0Y2gncyBNZXJrbGUgcm9vdCAocHJvb2YgZmFpbGVkKeB/C38Kfwl4aDVC/P//QFcBAnl4NZXy//81ZdT//3BoC5cmBRAiDWhK2CYGRRAiBNshIgJAVwADNQ/U//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA10gAAAKokJAwfbG9ja2VkLWJhbGFuY2UgbWlncmF0aW9uIHNlYWxlZOB4ELckIQwcY2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB5StkoJAZFCSIGygAUsyQFCSIGeRCzqiQSDA1pbnZhbGlkIGFzc2V04HoQuCQoDCNtaWdyYXRpb24gYW1vdW50IGNhbm5vdCBiZSBuZWdhdGl2ZeB5eDWw8f//elA1g+r//3p5eBPADBVMb2NrZWRCYWxhbmNlTWlncmF0ZWRBlQFvYUBXAQAMAQbbMDVP0///cGgLmCQFCSIJaNswEM4RlyICQDUA0///Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNMOqJCwMJ2xvY2tlZC1iYWxhbmNlIG1pZ3JhdGlvbiBhbHJlYWR5IHNlYWxlZOAMAQHbMAwBBtswNZTS//8QwAwcTG9ja2VkQmFsYW5jZU1pZ3JhdGlvblNlYWxlZEGVAW9hQFcDAzVd5f//qiQTDA5uZXR3b3JrIHBhdXNlZOB4ELckJwwidGFyZ2V0Q2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB5StkoJAZFCSIGygAUsyQFCSIGeRCzqiQcDBdpbnZhbGlkIHRhcmdldCBjb250cmFjdOBBLVEIMBPOcGhK2SgkBkUJIgbKABSzJAUJIgZoELOqJBMMDmludmFsaWQgc2VuZGVy4GhB+CfsjCQcDBdzZW5kZXIgd2l0bmVzcyByZXF1aXJlZOB4NDFxaXg1qgAAAHJ6ajWO0f//eWhpeBTADA5MMVRvTDJFbnF1ZXVlZEGVAW9haSICQFcDAXg0bnBoNa/R//9xaQuXJgURIlJpStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6EZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXJqaDVT6P//aiICQFcAAXgAIDXW5///QFcAAnl4ACE1xe3//0BXAQF4NOM1JtH//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAQJ5eDS1NezQ//9waAuXJgULIgVo2zAiAkBXAQQ1zdL//3BBOVNuPGiXJgUIIgw1jND//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hl4NDt62zBQNVHQ//95eDQ7e9swUDVE0P//e3p5eBTADBVNZXNzYWdlUm9vdHNQdWJsaXNoZWRBlQFvYUBXAAJ5eAAiNfXs//9AVwACeXgAIzXo7P//QFcBAnl4NOE1SND//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAICgDOiICQAwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAVwECeXg0hjXgz///cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwABeDQMNZzP//8LmCICQFcAAXjbMAAkNANAVwICEXnKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IcHhKaBBR0EUQcSJueWnOSmgRaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaXnKtSSQaCICQFYEDCBuZW80LWJyaWRnZTpzZXRTZXR0bGVtZW50TWFuYWdlctswYAwbbmVvNC1icmlkZ2U6cmVnaXN0ZXJNYXBwaW5n2zBhDBxuZW80LWJyaWRnZTpzZXRNYXBwaW5nQWN0aXZl2zBiQPs+hU0=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delDepositEnqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4, BigInteger? arg5);

    [DisplayName("DepositEnqueued")]
    public event delDepositEnqueued? OnDepositEnqueued;

    public delegate void delEmergencyManagerChanged(UInt160? obj);

    [DisplayName("EmergencyManagerChanged")]
    public event delEmergencyManagerChanged? OnEmergencyManagerChanged;

    public delegate void delGovernanceLocked(UInt160? obj);

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delL1ToL2Enqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4);

    [DisplayName("L1ToL2Enqueued")]
    public event delL1ToL2Enqueued? OnL1ToL2Enqueued;

    public delegate void delL2ToL1Consumed(BigInteger? arg1, UInt256? arg2);

    [DisplayName("L2ToL1Consumed")]
    public event delL2ToL1Consumed? OnL2ToL1Consumed;

    public delegate void delLockedBalanceMigrated(BigInteger? arg1, UInt160? arg2, BigInteger? arg3);

    [DisplayName("LockedBalanceMigrated")]
    public event delLockedBalanceMigrated? OnLockedBalanceMigrated;

    public delegate void delLockedBalanceMigrationSealed();

    [DisplayName("LockedBalanceMigrationSealed")]
    public event delLockedBalanceMigrationSealed? OnLockedBalanceMigrationSealed;

    public delegate void delMappingActiveChanged(UInt160? arg1, BigInteger? arg2, bool? arg3);

    [DisplayName("MappingActiveChanged")]
    public event delMappingActiveChanged? OnMappingActiveChanged;

    public delegate void delMappingRegistered(UInt160? arg1, BigInteger? arg2, UInt160? arg3);

    [DisplayName("MappingRegistered")]
    public event delMappingRegistered? OnMappingRegistered;

    public delegate void delMessageRootsPublished(BigInteger? arg1, BigInteger? arg2, UInt256? arg3, UInt256? arg4);

    [DisplayName("MessageRootsPublished")]
    public event delMessageRootsPublished? OnMessageRootsPublished;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delSettlementManagerChanged(UInt160? obj);

    [DisplayName("SettlementManagerChanged")]
    public event delSettlementManagerChanged? OnSettlementManagerChanged;

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
    public abstract UInt160? SettlementManager { [DisplayName("getSettlementManager")] get; [DisplayName("setSettlementManager")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? TokenRegistry { [DisplayName("getTokenRegistry")] get; [DisplayName("setTokenRegistry")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsLockedBalanceMigrationSealed { [DisplayName("isLockedBalanceMigrationSealed")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRegisterMappingAction")]
    public abstract byte[]? BuildRegisterMappingAction(byte[]? mappingBytes);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetMappingActiveAction")]
    public abstract byte[]? BuildSetMappingActiveAction(UInt160? l1Asset, BigInteger? chainId, bool? active);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetSettlementManagerAction")]
    public abstract byte[]? BuildSetSettlementManagerAction(UInt160? manager);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getDeposit")]
    public abstract byte[]? GetDeposit(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1Decimals")]
    public abstract BigInteger? GetL1Decimals(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1ToL2Message")]
    public abstract byte[]? GetL1ToL2Message(BigInteger? targetChainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1ToL2Nonce")]
    public abstract BigInteger? GetL1ToL2Nonce(BigInteger? targetChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2Asset")]
    public abstract UInt160? GetL2Asset(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2Decimals")]
    public abstract BigInteger? GetL2Decimals(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL1MessageRoot")]
    public abstract UInt256? GetL2ToL1MessageRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL2MessageRoot")]
    public abstract UInt256? GetL2ToL2MessageRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLockedBalance")]
    public abstract BigInteger? GetLockedBalance(BigInteger? chainId, UInt160? asset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getMapping")]
    public abstract byte[]? GetMapping(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isActive")]
    public abstract bool? IsActive(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isL2ToL1MessageConsumed")]
    public abstract bool? IsL2ToL1MessageConsumed(UInt256? messageHash);

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
    [DisplayName("executeGovernanceProposal")]
    public abstract void ExecuteGovernanceProposal(byte[]? action, BigInteger? proposalId);

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
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance(UInt160? governanceController);

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
    [DisplayName("publishMessageRoots")]
    public abstract void PublishMessageRoots(BigInteger? chainId, BigInteger? batchNumber, UInt256? l2ToL1Root, UInt256? l2ToL2Root);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerMapping")]
    public abstract void RegisterMapping(byte[]? mappingBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("sealLockedBalanceMigration")]
    public abstract void SealLockedBalanceMigration();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("sendMessage")]
    public abstract BigInteger? SendMessage(BigInteger? targetChainId, UInt160? targetContract, byte[]? messagePayload);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setActive")]
    public abstract void SetActive(UInt160? l1Asset, BigInteger? chainId, bool? active);

    #endregion
}
