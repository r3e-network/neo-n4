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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SharedBridge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":179,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":278,""safe"":false},{""name"":""getSettlementManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":399,""safe"":true},{""name"":""setSettlementManager"",""parameters"":[{""name"":""settlementManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":457,""safe"":false},{""name"":""getTokenRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":501,""safe"":true},{""name"":""setTokenRegistry"",""parameters"":[{""name"":""tokenRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":588,""safe"":false},{""name"":""getEmergencyManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":632,""safe"":true},{""name"":""setEmergencyManager"",""parameters"":[{""name"":""emergencyManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":690,""safe"":false},{""name"":""registerMapping"",""parameters"":[{""name"":""mappingBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":833,""safe"":false},{""name"":""setActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":2023,""safe"":false},{""name"":""getMapping"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":2156,""safe"":true},{""name"":""getL2Asset"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":2186,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2244,""safe"":true},{""name"":""getL1Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2279,""safe"":true},{""name"":""getL2Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2312,""safe"":true},{""name"":""deposit"",""parameters"":[{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""targetChainId"",""type"":""Integer""},{""name"":""l2Recipient"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":2345,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":5086,""safe"":false},{""name"":""getDeposit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":5167,""safe"":true},{""name"":""finalizeWithdrawal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5197,""safe"":false},{""name"":""finalizeWithdrawalAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7677,""safe"":false},{""name"":""finalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7889,""safe"":false},{""name"":""emergencyFinalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8125,""safe"":false},{""name"":""getLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":8391,""safe"":true},{""name"":""migrateLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8429,""safe"":false},{""name"":""sealLockedBalanceMigration"",""parameters"":[],""returntype"":""Void"",""offset"":8703,""safe"":false},{""name"":""isLockedBalanceMigrationSealed"",""parameters"":[],""returntype"":""Boolean"",""offset"":8671,""safe"":true},{""name"":""sendMessage"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""targetContract"",""type"":""Hash160""},{""name"":""messagePayload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":8832,""safe"":false},{""name"":""getL1ToL2Nonce"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":9226,""safe"":true},{""name"":""getL1ToL2Message"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":9283,""safe"":true},{""name"":""publishMessageRoots"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""l2ToL1Root"",""type"":""Hash256""},{""name"":""l2ToL2Root"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":9310,""safe"":false},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9447,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9551,""safe"":true},{""name"":""isL2ToL1MessageConsumed"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":9620,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":9831,""safe"":false}],""events"":[{""name"":""DepositEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Integer""}]},{""name"":""WithdrawalFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""MappingRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""MappingActiveChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""L1ToL2Enqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""L2ToL1Consumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""MessageRootsPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""EmergencyManagerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""LockedBalanceMigrated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""LockedBalanceMigrationSealed"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Canonical asset escrow \u002B L1\u2194L2 transfer \u002B message router for Neo Elastic Network."",""Version"":""0.2.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErODIxMTdjNDc5OWZkZTYzZThjMjMwZTllOTY5NmI2NmQ3OTQuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscgZzaGEyNTYBAAEPAAD9aiZXAwJ5JgQiaXhwaBDOcWlK2SgkBkUJIgbKABSzJAUJIgZpELOqJBIMDWludmFsaWQgb3duZXLgaQwB/9swNERoyhG3Jg5oEc5yagwB/dswNDJoyhK3Jg5oEs5yagwB/tswNCAMAQHbMAwBBtswNDBAStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcBAAwB/9swNC9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAF4QZv2Z85Bkl3oMUBBkl3oMUAMFAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcBATSaQfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFgwRaW52YWxpZCBuZXcgb3duZXLgNVP///9weAwB/9swNRX///94aBLADAxPd25lckNoYW5nZWRBlQFvYUBB+CfsjEBXAQAMAf3bMDVT////cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABNef+//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4DAH92zA1kv7//0BXAQAMAf7bMDXt/v//cGgLlyYFCCIlaErYJAlKygAUKAM6DBQAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCUHb/qh0Ig5oStgkCUrKABQoAzoiAkBB2/6odEBXAAE1ZP7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgMAf7bMDUP/v//QFcBAAwB/NswNWr+//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAE1/v3//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgMAfzbMDWp/f//eBHADBdFbWVyZ2VuY3lNYW5hZ2VyQ2hhbmdlZEGVAW9hQFcBADV2////cGgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYFCSIXEMAfDAhpc1BhdXNlZGhBYn1bUiICQEFifVtSQFcHATVv/f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeMoAMpckGgwVbWFwcGluZyBzaXplIG1pc21hdGNo4BB4NXoBAABwABR4NQ0CAABxABh4NWgBAAByeAAsznN4AC/OdHgAMM51aErZKCQGRQkiBsoAFLMkBQkiBmgQs6okFQwQaW52YWxpZCBMMSBhc3NldOBqStkoJAZFCSIGygAUsyQFCSIGahCzqiQVDBBpbnZhbGlkIEwyIGFzc2V04GkQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4GwAErYkBQkiBm0AErYkFQwQaW52YWxpZCBkZWNpbWFsc+BrEZcmRGwQlyQeDBlMMSBORU8gZGVjaW1hbHMgbXVzdCBiZSAw4G0YlyQeDBlMMiBORU8gZGVjaW1hbHMgbXVzdCBiZSA44GsQlyY+bBiXJBsMFkdBUyBkZWNpbWFscyBtdXN0IGJlIDjgbRiXJBsMFkdBUyBkZWNpbWFscyBtdXN0IGJlIDjgaWg1FwIAAHZ4bjXE+///amloE8AMEU1hcHBpbmdSZWdpc3RlcmVkQZUBb2FAVwICABSIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpABS1JJBo2yhK2CQJSsoAFCgDOiICQNsoStgkCUrKABQoAzpAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5J4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhK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Af8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRXwAIKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRXwAKKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRXwAMKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRXwAOKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRWjKdGxKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRWwYqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFbCCpShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EVsABipShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EVsWEpgamg10gAAAGoiAkBXAwF42zBwaMoRtyQFCSI4aGjKEZ9KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhCXJn9oyhGfSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxEHIiPmhqzkppalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWppyrUkwGkiBWgiAkBXAQQQcCJueGjOSnlYaJ5KAgAAAIAu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WEpganrbMDXb+P//ABRYSmBqe9swNc34//98ShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EV8GKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRXwgqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAYqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAgqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAoqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAwqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAA4qUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFABRYSmBqfdswNWz2//8AFFhKYGp+2zA1Xvb//2jKdGxKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRWwYqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFbCCpShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EVsABipShAuBCIISgH/ADIGAf8AkUpqWEqcSgIA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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delDepositEnqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4, BigInteger? arg5);

    [DisplayName("DepositEnqueued")]
    public event delDepositEnqueued? OnDepositEnqueued;

    public delegate void delEmergencyManagerChanged(UInt160? obj);

    [DisplayName("EmergencyManagerChanged")]
    public event delEmergencyManagerChanged? OnEmergencyManagerChanged;

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
