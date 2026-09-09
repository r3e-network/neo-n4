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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SharedBridge"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":179,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":278,""safe"":false},{""name"":""getSettlementManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":399,""safe"":true},{""name"":""setSettlementManager"",""parameters"":[{""name"":""settlementManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":457,""safe"":false},{""name"":""getTokenRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":501,""safe"":true},{""name"":""setTokenRegistry"",""parameters"":[{""name"":""tokenRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":588,""safe"":false},{""name"":""getEmergencyManager"",""parameters"":[],""returntype"":""Hash160"",""offset"":632,""safe"":true},{""name"":""setEmergencyManager"",""parameters"":[{""name"":""emergencyManager"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":690,""safe"":false},{""name"":""registerMapping"",""parameters"":[{""name"":""mappingBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":833,""safe"":false},{""name"":""setActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":2023,""safe"":false},{""name"":""getMapping"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Array"",""offset"":2156,""safe"":true},{""name"":""getL2Asset"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":2186,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2244,""safe"":true},{""name"":""getL1Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2279,""safe"":true},{""name"":""getL2Decimals"",""parameters"":[{""name"":""l1Asset"",""type"":""Hash160""},{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2312,""safe"":true},{""name"":""deposit"",""parameters"":[{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""targetChainId"",""type"":""Integer""},{""name"":""l2Recipient"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":2345,""safe"":false},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":5088,""safe"":false},{""name"":""getDeposit"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Array"",""offset"":5169,""safe"":true},{""name"":""finalizeWithdrawal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5199,""safe"":false},{""name"":""finalizeWithdrawalAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7679,""safe"":false},{""name"":""finalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7891,""safe"":false},{""name"":""emergencyFinalizeWithdrawalWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""withdrawalLeafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""},{""name"":""emittingContract"",""type"":""Hash160""},{""name"":""l2Sender"",""type"":""Hash160""},{""name"":""l2Asset"",""type"":""Hash160""},{""name"":""withdrawalNonce"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8127,""safe"":false},{""name"":""getLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":8393,""safe"":true},{""name"":""migrateLockedBalance"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8431,""safe"":false},{""name"":""sealLockedBalanceMigration"",""parameters"":[],""returntype"":""Void"",""offset"":8704,""safe"":false},{""name"":""isLockedBalanceMigrationSealed"",""parameters"":[],""returntype"":""Boolean"",""offset"":8672,""safe"":true},{""name"":""sendMessage"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""targetContract"",""type"":""Hash160""},{""name"":""messagePayload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":8833,""safe"":false},{""name"":""getL1ToL2Nonce"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":9227,""safe"":true},{""name"":""getL1ToL2Message"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Array"",""offset"":9284,""safe"":true},{""name"":""publishMessageRoots"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""l2ToL1Root"",""type"":""Hash256""},{""name"":""l2ToL2Root"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":9311,""safe"":false},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9446,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9550,""safe"":true},{""name"":""isL2ToL1MessageConsumed"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":9619,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":9830,""safe"":false}],""events"":[{""name"":""DepositEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Integer""}]},{""name"":""WithdrawalFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""MappingRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""}]},{""name"":""MappingActiveChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Boolean""}]},{""name"":""L1ToL2Enqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""L2ToL1Consumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""MessageRootsPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""EmergencyManagerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""LockedBalanceMigrated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""LockedBalanceMigrationSealed"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Canonical asset escrow \u002B L1\u2194L2 transfer \u002B message router for Neo Elastic Network."",""Version"":""0.2.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SharedBridge"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscgZzaGEyNTYBAAEPAAD9aSZXAwJ5JgQiaXhwaBDOcWlK2SgkBkUJIgbKABSzJAUJIgZpELOqJBIMDWludmFsaWQgb3duZXLgaQwB/9swNERoyhG3Jg5oEc5yagwB/dswNDJoyhK3Jg5oEs5yagwB/tswNCAMAQHbMAwBBtswNDBAStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcBAAwB/9swNC9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAF4QZv2Z85Bkl3oMUBBkl3oMUAMFAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcBATSaQfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okFgwRaW52YWxpZCBuZXcgb3duZXLgNVP///9weAwB/9swNRX///94aBLADAxPd25lckNoYW5nZWRBlQFvYUBB+CfsjEBXAQAMAf3bMDVT////cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABNef+//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4DAH92zA1kv7//0BXAQAMAf7bMDXt/v//cGgLlyYFCCIlaErYJAlKygAUKAM6DBQAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCUHb/qh0Ig5oStgkCUrKABQoAzoiAkBB2/6odEBXAAE1ZP7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgMAf7bMDUP/v//QFcBAAwB/NswNWr+//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAE1/v3//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgMAfzbMDWp/f//eBHADBdFbWVyZ2VuY3lNYW5hZ2VyQ2hhbmdlZEGVAW9hQFcBADV2////cGgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYFCSIXEMAfDAhpc1BhdXNlZGhBYn1bUiICQEFifVtSQFcHATVv/f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeMoAMpckGgwVbWFwcGluZyBzaXplIG1pc21hdGNo4BB4NXoBAABwABR4NQ0CAABxABh4NWgBAAByeAAsznN4AC/OdHgAMM51aErZKCQGRQkiBsoAFLMkBQkiBmgQs6okFQwQaW52YWxpZCBMMSBhc3NldOBqStkoJAZFCSIGygAUsyQFCSIGahCzqiQVDBBpbnZhbGlkIEwyIGFzc2V04GkQtyQhDBxjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4GwAErYkBQkiBm0AErYkFQwQaW52YWxpZCBkZWNpbWFsc+BrEZcmRGwQlyQeDBlMMSBORU8gZGVjaW1hbHMgbXVzdCBiZSAw4G0YlyQeDBlMMiBORU8gZGVjaW1hbHMgbXVzdCBiZSA44GsQlyY+bBiXJBsMFkdBUyBkZWNpbWFscyBtdXN0IGJlIDjgbRiXJBsMFkdBUyBkZWNpbWFscyBtdXN0IGJlIDjgaWg1FwIAAHZ4bjXE+///amloE8AMEU1hcHBpbmdSZWdpc3RlcmVkQZUBb2FAVwICABSIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpABS1JJBo2yhK2CQJSsoAFCgDOiICQNsoStgkCUrKABQoAzpAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfknh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfziCoSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5J4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84AGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfkkoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFAVwECABmIcCBKaBBR0EV4EWg0bXlKEC4EIghKAf8AMgYB/wCRSmgAFVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaAAWUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoABdR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoABhR0EVoIgJAVwIDetswcBBxIm5oac5KeHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpABS1JJBA2zBAVwMDNcn4//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB5eDXf/v//cGg12vj//3FpC5gkFgwRbWFwcGluZyBub3QgZm91bmTgadswcnomBREiAxBKagAxUdBFamg1Wvj//3p5eBPADBRNYXBwaW5nQWN0aXZlQ2hhbmdlZEGVAW9hQNswQFcBAnl4NXf+//81dPj//3BoC5cmBQsiBWjbMCICQFcBAnl4NVn+//81Vvj//3BoC5gmDgAYaNswNVj8//8iGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiAkBXAQJ5eDUf/v//NRz4//9waAuYJgxo2zAAMc4RlyIFCSICQFcBAnl4Nfz9//81+ff//3BoC5cmBQ8iCGjbMAAvziICQFcBAnl4Ndv9//812Pf//3BoC5cmBQ8iCGjbMAAwziICQFcGBDXT+f//qiQTDA5uZXR3b3JrIHBhdXNlZOB4StkoJAZFCSIGygAUsyQFCSIGeBCzqiQSDA1pbnZhbGlkIGFzc2V04HkQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeB7StkoJAZFCSIGygAUsyQFCSIGexCzqiQWDBFpbnZhbGlkIHJlY2lwaWVudOB6ELckJwwidGFyZ2V0Q2hhaW5JZCAwIGlzIHJlc2VydmVkIGZvciBMMeB6eDWk/v//cGhK2SgkBkUJIgbKABSzJAUJIgZoELOqJCYMIWFzc2V0IG5vdCBtYXBwZWQgZm9yIHRhcmdldCBjaGFpbuB6eDWZ/v//JBsMFmFzc2V0IG1hcHBpbmcgaW5hY3RpdmXgQS1RCDATznFpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQfDBppbnZhbGlkIHRyYW5zYWN0aW9uIHNlbmRlcuBpQfgn7IwkKAwjdHJhbnNhY3Rpb24gc2VuZGVyIHdpdG5lc3MgcmVxdWlyZWTgejWdAAAAcmppe3l4NY0BAABza2p6NfYGAAA1zPX//2l4NSYIAAB0DAEB2zBsNbn1//8LeUHb/qh0aRTAHwwIdHJhbnNmZXJ4QWJ9W1J1bSQaDBVhc3NldCB0cmFuc2ZlciBmYWlsZWTgbDUACAAAeXh6NQ0IAAB5e3hqehXADA9EZXBvc2l0RW5xdWV1ZWRBlQFvYWoiAkBBLVEIMEBXAwF4NGtwaDWN9f//cWkLlyYFESJSaUrYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOhGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFyamg0fGoiAkBXAAF4ETQDQFcAAhWIShB40EoReUoQLgQiCEoB/wAyBgH/AJHQShJ5GKlKEC4EIghKAf8AMgYB/wCR0EoTeSCpShAuBCIISgH/ADIGAf8AkdBKFHkAGKlKEC4EIghKAf8AMgYB/wCR0CICQErYJgZFECIE2yFAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcFBXk19QMAAHAASGjKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xaYhyEHMAFGtKYGp42zA1eQQAAAAUWEpganrbMDVrBAAAABRYSmBqe9swNV0EAAB8ShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EV8GKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRXwgqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAYqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAgqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAoqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAAwqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfAA4qUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFaMp0bEoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFbBipShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EVsIKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRWwAGKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRWxYSmBqaDXVAAAAaiICQFcDAXjbMHBoyhG3JAUJIjhoaMoRn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OEJcnggAAAGjKEZ9KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHEQciI+aGrOSmlqUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFamnKtSTAaSIFaCICQFcBBBBwIm54aM5KeVhonkoCAAAA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ABRYSmBqetswNdv4//8AFFhKYGp72zA1zfj//3xKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRXwYqUoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFfCCpShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EV8ABipShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EV8ACCpShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EV8ACipShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EV8ADCpShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EV8ADipShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EUAFFhKYGp92zA1bPb//wAUWEpgan7bMDVe9v//aMp0bEoQLgQiCEoB/wAyBgH/AJFKalhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9gUdBFbBipShAuBCIISgH/ADIGAf8AkUpqWEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2BR0EVsIKlKEC4EIghKAf8AMgYB/wCRSmpYSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfYFHQRWwAGKlKEC4EIghKAf8AMgYB/wCRSmpYSpxK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").AsSerializable<Neo.SmartContract.NefFile>();

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
    public abstract IList<object>? GetDeposit(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1Decimals")]
    public abstract BigInteger? GetL1Decimals(UInt160? l1Asset, BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1ToL2Message")]
    public abstract IList<object>? GetL1ToL2Message(BigInteger? targetChainId, BigInteger? nonce);

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
    public abstract IList<object>? GetMapping(UInt160? l1Asset, BigInteger? chainId);

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
