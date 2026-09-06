using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubSettlementManager(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":669,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":727,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":868,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":926,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":1049,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1107,""safe"":false},{""name"":""getMessageRouter"",""parameters"":[],""returntype"":""Hash160"",""offset"":1232,""safe"":true},{""name"":""setMessageRouter"",""parameters"":[{""name"":""messageRouter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1290,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1419,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1562,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":1620,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":654,""safe"":true},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2059,""safe"":false},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":7064,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6244,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7270,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8589,""safe"":false},{""name"":""revertBatchViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9396,""safe"":false},{""name"":""buildRevertBatchAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":9993,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4678,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":11246,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11278,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11402,""safe"":true},{""name"":""getFinalizedTxRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11413,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":11426,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4007,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":11694,""safe"":false},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14257,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14275,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14353,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":15083,""safe"":true},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":9202,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":15771,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MessageRouterChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscgZzaGEyNTYBAAEPAAD9tz1XBQJ5JgcjGgEAAHhwaBDOcWgRznJoEs5zaMoTtyYHaBPOIhgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAdGlK2SgkBkUJIgbKABSzJAUJIgZpELOqJBIMDWludmFsaWQgb3duZXLgakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okGwwWaW52YWxpZCBjaGFpbiByZWdpc3RyeeBrStkoJAZFCSIGygAUsyQFCSIGaxCzqiQeDBlpbnZhbGlkIHZlcmlmaWVyIHJlZ2lzdHJ54GkMAf/bMDR4agwB/NswNHBrDAH92zA0aGwQs6omOWxK2SgkBkUJIgbKABSzJCEMHGludmFsaWQgb3B0aW1pc3RpYyBjaGFsbGVuZ2XgbAwBBtswNCtADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBK2SgkBkUJIgbKABSzQBCzQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBBm/ZnzkBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFAVwEBNLFB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA0XnhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVo////cHgMAf/bMDVA////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxANGKqJF4MWWdvdmVybmFuY2UgbG9ja2VkIOKAlCBib290c3RyYXAgb3duZXIgcGF0aCBkaXNhYmxlZDsgZGVwbG95IGEgdmVyc2lvbmVkIFNldHRsZW1lbnRNYW5hZ2Vy4EAMAQ3bMDUJ////C5giAkBXAQAMAQbbMDX3/v//cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABNYv+//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA1Nf///3hK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJCEMHGludmFsaWQgb3B0aW1pc3RpYyBjaGFsbGVuZ2XgeAwBBtswNQ/+//94EcAMGk9wdGltaXN0aWNDaGFsbGVuZ2VDaGFuZ2VkQZUBb2FAVwEADAEH2zA1MP7//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAATXE/f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNW7+//94StkoJAZFCSIGygAUsyQFCSIGeBCzqiQYDBNpbnZhbGlkIERBIHJlZ2lzdHJ54HgMAQfbMDVR/f//eBHADBFEQVJlZ2lzdHJ5Q2hhbmdlZEGVAW9hQFcBAAwBCNswNXv9//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAE1D/3//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DW5/f//eErZKCQGRQkiBsoAFLMkBQkiBngQs6okGQwUaW52YWxpZCBEQSB2YWxpZGF0b3LgeAwBCNswNZv8//94EcAMEkRBVmFsaWRhdG9yQ2hhbmdlZEGVAW9hQFcBAAwBC9swNcT8//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAE1WPz//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DUC/f//eErZKCQGRQkiBsoAFLMkBQkiBngQs6okGwwWaW52YWxpZCBtZXNzYWdlIHJvdXRlcuB4DAEL2zA14vv//3gRwAwUTWVzc2FnZVJvdXRlckNoYW5nZWRBlQFvYUBXAAE11/v//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DWB/P//eErZKCQGRQkiBsoAFLMkBQkiBngQs6okIgwdaW52YWxpZCBnb3Zlcm5hbmNlIGNvbnRyb2xsZXLgeAwBDNswNVr7//94EcAMG0dvdmVybmFuY2VDb250cm9sbGVyQ2hhbmdlZEGVAW9hQFcBAAwBDNswNXr7//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAQA1Dvv//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DSmDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkLQwod2lyZSBHb3Zlcm5hbmNlQ29udHJvbGxlciBiZWZvcmUgbG9ja2luZ+A14/v//wwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCwMJ3dpcmUgT3B0aW1pc3RpY0NoYWxsZW5nZSBiZWZvcmUgbG9ja2luZ+A1Yvz//wwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCMMHndpcmUgREFSZWdpc3RyeSBiZWZvcmUgbG9ja2luZ+A12Pz//wwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCQMH3dpcmUgREFWYWxpZGF0b3IgYmVmb3JlIGxvY2tpbmfgNU/9//8MFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCQmDCF3aXJlIE1lc3NhZ2VSb3V0ZXIgYmVmb3JlIGxvY2tpbmfgDAEN2zBwaDXS+f//C5cmIwwBAdswaDQcEMAMEEdvdmVybmFuY2VMb2NrZWRBlQFvYUBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVxQDeMoBQQG4JBkMFGNvbW1pdG1lbnQgdG9vIHNtYWxs4HkLmCQFCSIHecoAIJckIwwebDFNZXNzYWdlSGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HoLmCQFCSIHesoAIJckJgwhYmxvY2tDb250ZXh0SGFzaCBtdXN0IGJlIDMyIGJ5dGVz4BB4NVMDAABwFHg1SwQAAHEMAfzbMDX3+P//StgmFEUMDnJlZ2lzdHJ5IHVuc2V0OkrYJAlKygAUKAM6cmgRwBUMCGlzQWN0aXZlakFifVtSc2skEwwOY2hhaW4gaW5hY3RpdmXgaDWxBgAAdGlsEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZckIQwcYmF0Y2ggbnVtYmVyIG91dCBvZiBzZXF1ZW5jZeBpaDUPBwAAdW01Rfj//3ZuC5cmBQgiCW7bMBDOFJckHAwXYmF0Y2ggYWxyZWFkeSBzdWJtaXR0ZWTgABx4NR8IAAB3B28HaDWxCAAAlyQvDCpwcmVTdGF0ZVJvb3QgZG9lcyBub3QgbWF0Y2ggY2Fub25pY2FsIGhlYWTgenl4NdkJAAB3CAEcAXg10gcAAHcJbwhvCZckMgwtcHVibGljSW5wdXRIYXNoIG5vdCBib3VuZCB0byBjb21taXRtZW50IHJvb3Rz4HgBPAHOdwpoajX/DAAAdwtoajUhDQAAdwxvDG8LNToNAABvCm8LNSgOAAAkQww+cHJvb2YgdHlwZSBpbmNvbXBhdGlibGUgd2l0aCBjaGFpbidzIGFkdmVydGlzZWQgc2VjdXJpdHkgbGV2ZWzgDAH92zA1E/f//0rYJh1FDBd2ZXJpZmllciByZWdpc3RyeSB1bnNldDpK2CQJSsoAFCgDOncNeBHAFQwQdmVyaWZ5Q29tbWl0bWVudG8NQWJ9W1J3Dm8OJCEMHHZlcmlmaWVyIHJlamVjdGVkIGNvbW1pdG1lbnTgAfwAeDWqBgAAdw9vDG8PaWg1pA0AAG8KEpcmBRIiAxF3EBGIShBvENBtNc/8//94aWg1Nw4AADXC/P//AZwAeDVuBgAAdxFvEdswaWg1Kw4AADWn/P//bwoSlyZoNUT3//93Em8SStkoJAZFCSIGygAUsyQFCSIHbxIQs6okIwweb3B0aW1pc3RpYyBjaGFsbGVuZ2Ugbm90IHdpcmVk4Hg14w0AAHcTbxNpaBPAHwwKb3BlbldpbmRvd28SQWJ9W1JFADx4NegFAAB3Em8SaWgTwAwOQmF0Y2hTdWJtaXR0ZWRBlQFvYUBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSIgJAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Fp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkiICQEFifVtSQFcBAXg0NTXv8f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcBARWIcBRKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EVoIgJAStgmBkUQIgTbIUBXAAJ5eBE0A0BXAQMdiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFekoQLgQiCEoB/wAyBgH/AJFKaBVR0EV6GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeiCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV6ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJA2zBAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQNsoStgkCUrKACAoAzpAVwMBeDXFAAAANU3v//9waAuYJhNoStgkCUrKACAoAzojqAAAAAwB/NswNSzv//9K2CYURQwOcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzpxeBHAFQwTZ2V0R2VuZXNpc1N0YXRlUm9vdGlBYn1bUnJqDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJC8MKmNoYWluIGdlbmVzaXMgc3RhdGUgcm9vdCBpcyBub3QgcmVnaXN0ZXJlZOBqIgJAVwEBFYhwE0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcDAwFcAYhwEHEQciJueGrOSmhpap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagActSSQaQAcnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUUAHHhpSmBoNdABAAAAPHhYSmBoNcQBAAAAXHhYSmBoNbgBAAAAfHhYSmBoNawBAAABnAB4WEpgaDWfAQAAAbwAeFhKYGg1kgEAAAHcAHhYSmBoNYUBAAAQciJueWrOSmhYap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQWAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEUB/AB4WEpgaDXNAAAAEHIibnpqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFaNsoNwAAcmo3AADbMNsoStgkCUrKACAoAzoiAkBXAQQQcCOhAAAAentonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSnhYaJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaAAgtSVg////WAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEVANwAAQNsoQFcAAnkRwBUMEGdldFNlY3VyaXR5TGV2ZWx4QWJ9W1JKEAEAAbskAzoiAkBXAAJ5EcAVDAlnZXREQU1vZGV4QWJ9W1JKEAEAAbskAzoiAkBXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCY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SgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV4ACCpShAuBCIISgH/ADIGAf8AkUpoFVHQRXgAKKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeAAwqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV4ADipShAuBCIISgH/ADIGAf8AkUpoGFHQRWgiAkBXBQJZcEHb/qh02zBxaMoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfGJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHIQcyI+aGvOSmprUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa2jKtSTAaMpzEHQibmlszkpqa2yeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkGsAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnNFeEoQLgQiCEoB/wAyBgH/AJFKamtR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmprEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeCCpShAuBCIISgH/ADIGAf8AkUpqaxKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmprE55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pz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dGl0dWVudCBjaGFpbuBvCW8INRPz//+3JC4MKUdhdGV3YXkgY29uc3RpdHVlbnQgd2FzIGFscmVhZHkgcHVibGlzaGVk4G8Jbwg1avD//zWA0P//dwtvCwuYJCUMIEdhdGV3YXkgZmluYWxpemVkIHJlY29yZCBtaXNzaW5n4G8L2zB3DG8MygBAlyQlDCBHYXRld2F5IGZpbmFsaXplZCByZWNvcmQgY29ycnVwdOAAIIh3DQAgiHcOEHcPI4UAAABvDG8PzkpvDW8PUdBFbwwAIG8PnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSm8Obw9R0EVvD0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cPRW8PACC1JXv///9ubw1pNXACAABubw5qNWcCAABuSpxKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdkVufLUl2Pz//whpNR0EAAB2CWo1FQQAAHcHe27bKErYJAlKygAgKAM6lyQxDCxHYXRld2F5IGNvbnN0aXR1ZW50IGNvbW1pdG1lbnQgcm9vdCBtaXNtYXRjaOB6bwfbKErYJAlKygAgKAM6lyQpDCRHYXRld2F5IGdsb2JhbCBtZXNzYWdlIHJvb3QgbWlzbWF0Y2jgEHcII+gAAABvCEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8coEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CW8JaDWY2P//dwpvCRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2g1Xtn//3cLbwtvCjVg8P//tyYQbwtvCjWN8P//NfDs//9vCEqcShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXcIRW8IfLUlGf///zX80P//dwhvCErZKCQGRQkiBsoAFLMkBQkiB28IELOqJB0MGG1lc3NhZ2Ugcm91dGVyIG5vdCB3aXJlZOB/CX8Ifwd+fXx7engZwB8MEXB1Ymxpc2hHbG9iYWxSb290bwhBYn1bUiICQFcCAB3EAHAQcSI9EIhKaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpaMq1JMFoIgJAVwMDecoAIJckIgwdR2F0ZXdheSBsZWFmIG11c3QgYmUgMzIgYnl0ZXPgeXB6cRByaRGREZcnvwAAAGp4yrUkHgwZR2F0ZXdheSBmcm9udGllciBvdmVyZmxvd+B4as7KACCXJCMMHkdhdGV3YXkgZnJvbnRpZXIgaXMgaW5jb21wbGV0ZeBoeGrONZQAAABKcEUQiEp4alHQRWkRqUoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJFKcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckUjQf///2p4yrUkHgwZR2F0ZXdheSBmcm9udGllciBvdmVyZmxvd+BoSnhqUdBFQFcCAnjKACCXJAUJIgd5ygAglyQiDB1HYXRld2F5IG5vZGUgbXVzdCBiZSAzMiBieXRlc+AAQIhwEHEieHhpzkpoaVHQRXlpzkpoACBpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JIZo2yg3AABxaTcAANswIgJAVwQCEIhwEHEQciMEAQAAeGrOc2vKEJcmByPCAAAAa8oAIJckIAwbR2F0ZXdheSBmcm9udGllciBpcyBjb3JydXB04GjKEJcmD2tKcEVqSnFFI4oAAAB5JkZparUmQWhoNdj+//9KcEVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUUivmhrNZn+//9KcEVqEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanjKtSX9/v//aMoAIJckHgwZR2F0ZXdheSBmcm9udGllciBpcyBlbXB0eeBoIgJAVwECeDXy1///cHloeDQFIgJAVwQDeXg1l9j//zXPyf//cGgLlyYFCSI3aNswEM5xaROYJgUJIil5eDWJ4f//NazJ//9yaguXJgUJIhRqStgkCUrKACAoAzpza3qXIgJAVwsFeXg1Sdj//zWByf//cGgLlyYICSPDAgAAaNswEM5xaROYJggJI7ICAAB5eDU14f//NVjJ//9yaguXJggJI5oCAABqStgkCUrKACAoAzpzewuYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4Ht0bMoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgetswdXx2EHcHIygCAABsbwfOdwhvCMoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHcJbhGREJcn1gAAABB3CiJDbW8KzkpvCW8KUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSS6EHcKInVvCG8KzkpvCQAgbwqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkiCPRAAAAEHcKIkRvCG8KzkpvCW8KUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSS5EHcKInRtbwrOSm8JACBvCp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CkVvCgAgtSSJbwnbKDcAAHcKbwo3AADbMEp1RW4RqUp2RW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwdsyrUl2P3//24QmCYFCSIUa23bKErYJAlKygAgKAM6lyICQFcIBHg1V9f//3BoDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJcmCAkjfgIAAHoLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB6cWnKAEC2JBMMDnByb29mIHRvbyBkZWVw4HnbMHJ7cxB0IxsCAABpbM51bcoAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHZrEZEQlyfTAAAAEHcHIkJqbwfOSm5vB1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkuxB3ByJzbW8HzkpuACBvB55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSSKI84AAAAQdwciQm1vB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNqbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIpu2yg3AAB3B28HNwAA2zBKckVrEalKc0VsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsacq1Jeb9//9rEJgmBQkiFGhq2yhK2CQJSsoAICgDOpciAkBWAgwUbmVvNC1nb3Y6cmV2ZXJ0QmF0Y2jbMGFAsFWSmw==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delBatchFinalized(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchFinalized")]
    public event delBatchFinalized? OnBatchFinalized;

    public delegate void delBatchReverted(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("BatchReverted")]
    public event delBatchReverted? OnBatchReverted;

    public delegate void delBatchSubmitted(BigInteger? arg1, BigInteger? arg2, UInt256? arg3);

    [DisplayName("BatchSubmitted")]
    public event delBatchSubmitted? OnBatchSubmitted;

    public delegate void delDARegistryChanged(UInt160? obj);

    [DisplayName("DARegistryChanged")]
    public event delDARegistryChanged? OnDARegistryChanged;

    public delegate void delDAValidatorChanged(UInt160? obj);

    [DisplayName("DAValidatorChanged")]
    public event delDAValidatorChanged? OnDAValidatorChanged;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delMessageRouterChanged(UInt160? obj);

    [DisplayName("MessageRouterChanged")]
    public event delMessageRouterChanged? OnMessageRouterChanged;

    public delegate void delOptimisticChallengeChanged(UInt160? obj);

    [DisplayName("OptimisticChallengeChanged")]
    public event delOptimisticChallengeChanged? OnOptimisticChallengeChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? DARegistry { [DisplayName("getDARegistry")] get; [DisplayName("setDARegistry")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? DAValidator { [DisplayName("getDAValidator")] get; [DisplayName("setDAValidator")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? MessageRouter { [DisplayName("getMessageRouter")] get; [DisplayName("setMessageRouter")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? OptimisticChallenge { [DisplayName("getOptimisticChallenge")] get; [DisplayName("setOptimisticChallenge")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRevertBatchAction")]
    public abstract byte[]? BuildRevertBatchAction(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchStatus")]
    public abstract BigInteger? GetBatchStatus(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getCanonicalStateRoot")]
    public abstract UInt256? GetCanonicalStateRoot(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getChallengeableBatchHeader")]
    public abstract byte[]? GetChallengeableBatchHeader(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getFinalizedTxRoot")]
    public abstract UInt256? GetFinalizedTxRoot(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGatewayFinalizedThrough")]
    public abstract BigInteger? GetGatewayFinalizedThrough(BigInteger? chainId);

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
    [DisplayName("getLatestFinalizedBatch")]
    public abstract BigInteger? GetLatestFinalizedBatch(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isProofTypeCompatible")]
    public abstract bool? IsProofTypeCompatible(BigInteger? securityLevel, BigInteger? proofType);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyStateLeafWithProof")]
    public abstract bool? VerifyStateLeafWithProof(BigInteger? chainId, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeaf")]
    public abstract bool? VerifyWithdrawalLeaf(BigInteger? chainId, UInt256? leafHash);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeafAt")]
    public abstract bool? VerifyWithdrawalLeafAt(BigInteger? chainId, BigInteger? batchNumber, UInt256? leafHash);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyWithdrawalLeafWithProof")]
    public abstract bool? VerifyWithdrawalLeafWithProof(BigInteger? chainId, BigInteger? batchNumber, UInt256? leafHash, IList<object>? siblings, BigInteger? leafIndex);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("finalizeBatch")]
    public abstract void FinalizeBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishGatewayGlobalRoot")]
    public abstract bool? PublishGatewayGlobalRoot(BigInteger? batchEpoch, byte[]? constituentReferences, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain, byte[]? aggregatedProof);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revertBatch")]
    public abstract void RevertBatch(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revertBatchViaProposal")]
    public abstract void RevertBatchViaProposal(BigInteger? chainId, BigInteger? batchNumber, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitAndFinalizeBatch")]
    public abstract void SubmitAndFinalizeBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash);

    #endregion
}
