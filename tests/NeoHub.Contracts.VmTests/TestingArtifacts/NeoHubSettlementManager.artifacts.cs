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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.SettlementManager"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":357,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":433,""safe"":false},{""name"":""getOptimisticChallenge"",""parameters"":[],""returntype"":""Hash160"",""offset"":669,""safe"":true},{""name"":""setOptimisticChallenge"",""parameters"":[{""name"":""optimisticChallenge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":727,""safe"":false},{""name"":""getDARegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":868,""safe"":true},{""name"":""setDARegistry"",""parameters"":[{""name"":""daRegistry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":926,""safe"":false},{""name"":""getDAValidator"",""parameters"":[],""returntype"":""Hash160"",""offset"":1049,""safe"":true},{""name"":""setDAValidator"",""parameters"":[{""name"":""daValidator"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1107,""safe"":false},{""name"":""getMessageRouter"",""parameters"":[],""returntype"":""Hash160"",""offset"":1232,""safe"":true},{""name"":""setMessageRouter"",""parameters"":[{""name"":""messageRouter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1290,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":1419,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1562,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":1620,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":654,""safe"":true},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2059,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6244,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":7064,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8383,""safe"":false},{""name"":""revertBatchViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9190,""safe"":false},{""name"":""buildRevertBatchAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":9787,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4678,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":11040,""safe"":true},{""name"":""getL2ToL1MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11072,""safe"":true},{""name"":""getL2ToL2MessageRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11196,""safe"":true},{""name"":""getFinalizedTxRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":11207,""safe"":true},{""name"":""getChallengeableBatchHeader"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":11220,""safe"":true},{""name"":""getLatestFinalizedBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4007,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":11488,""safe"":false},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14051,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":14069,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14147,""safe"":true},{""name"":""verifyStateLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":14869,""safe"":true},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8996,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":15549,""safe"":false}],""events"":[{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""OptimisticChallengeChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DARegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""DAValidatorChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MessageRouterChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Batch settlement \u002B canonical state root tracking for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.SettlementManager"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscgZzaGEyNTYBAAEPAAD92TxXBQJ5JgcjGgEAAHhwaBDOcWgRznJoEs5zaMoTtyYHaBPOIhgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAdGlK2SgkBkUJIgbKABSzJAUJIgZpELOqJBIMDWludmFsaWQgb3duZXLgakrZKCQGRQkiBsoAFLMkBQkiBmoQs6okGwwWaW52YWxpZCBjaGFpbiByZWdpc3RyeeBrStkoJAZFCSIGygAUsyQFCSIGaxCzqiQeDBlpbnZhbGlkIHZlcmlmaWVyIHJlZ2lzdHJ54GkMAf/bMDR4agwB/NswNHBrDAH92zA0aGwQs6omOWxK2SgkBkUJIgbKABSzJCEMHGludmFsaWQgb3B0aW1pc3RpYyBjaGFsbGVuZ2XgbAwBBtswNCtADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBK2SgkBkUJIgbKABSzQBCzQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBBm/ZnzkBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFAVwEBNLFB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA0XnhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVo////cHgMAf/bMDVA////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxANGKqJF4MWWdvdmVybmFuY2UgbG9ja2VkIOKAlCBib290c3RyYXAgb3duZXIgcGF0aCBkaXNhYmxlZDsgZGVwbG95IGEgdmVyc2lvbmVkIFNldHRsZW1lbnRNYW5hZ2Vy4EAMAQ3bMDUJ////C5giAkBXAQAMAQbbMDX3/v//cGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABNYv+//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA1Nf///3hK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJCEMHGludmFsaWQgb3B0aW1pc3RpYyBjaGFsbGVuZ2XgeAwBBtswNQ/+//94EcAMGk9wdGltaXN0aWNDaGFsbGVuZ2VDaGFuZ2VkQZUBb2FAVwEADAEH2zA1MP7//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAATXE/f//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgNW7+//94StkoJAZFCSIGygAUsyQFCSIGeBCzqiQYDBNpbnZhbGlkIERBIHJlZ2lzdHJ54HgMAQfbMDVR/f//eBHADBFEQVJlZ2lzdHJ5Q2hhbmdlZEGVAW9hQFcBAAwBCNswNXv9//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAE1D/3//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DW5/f//eErZKCQGRQkiBsoAFLMkBQkiBngQs6okGQwUaW52YWxpZCBEQSB2YWxpZGF0b3LgeAwBCNswNZv8//94EcAMEkRBVmFsaWRhdG9yQ2hhbmdlZEGVAW9hQFcBAAwBC9swNcT8//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAAE1WPz//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DUC/f//eErZKCQGRQkiBsoAFLMkBQkiBngQs6okGwwWaW52YWxpZCBtZXNzYWdlIHJvdXRlcuB4DAEL2zA14vv//3gRwAwUTWVzc2FnZVJvdXRlckNoYW5nZWRBlQFvYUBXAAE11/v//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DWB/P//eErZKCQGRQkiBsoAFLMkBQkiBngQs6okIgwdaW52YWxpZCBnb3Zlcm5hbmNlIGNvbnRyb2xsZXLgeAwBDNswNVr7//94EcAMG0dvdmVybmFuY2VDb250cm9sbGVyQ2hhbmdlZEGVAW9hQFcBAAwBDNswNXr7//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXAQA1Dvv//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DSmDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkLQwod2lyZSBHb3Zlcm5hbmNlQ29udHJvbGxlciBiZWZvcmUgbG9ja2luZ+A14/v//wwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCwMJ3dpcmUgT3B0aW1pc3RpY0NoYWxsZW5nZSBiZWZvcmUgbG9ja2luZ+A1Yvz//wwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCMMHndpcmUgREFSZWdpc3RyeSBiZWZvcmUgbG9ja2luZ+A12Pz//wwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCQMH3dpcmUgREFWYWxpZGF0b3IgYmVmb3JlIGxvY2tpbmfgNU/9//8MFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCQmDCF3aXJlIE1lc3NhZ2VSb3V0ZXIgYmVmb3JlIGxvY2tpbmfgDAEN2zBwaDXS+f//C5cmIwwBAdswaDQcEMAMEEdvdmVybmFuY2VMb2NrZWRBlQFvYUBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVxQDeMoBQQG4JBkMFGNvbW1pdG1lbnQgdG9vIHNtYWxs4HkLmCQFCSIHecoAIJckIwwebDFNZXNzYWdlSGFzaCBtdXN0IGJlIDMyIGJ5dGVz4HoLmCQFCSIHesoAIJckJgwhYmxvY2tDb250ZXh0SGFzaCBtdXN0IGJlIDMyIGJ5dGVz4BB4NVMDAABwFHg1SwQAAHEMAfzbMDX3+P//StgmFEUMDnJlZ2lzdHJ5IHVuc2V0OkrYJAlKygAUKAM6cmgRwBUMCGlzQWN0aXZlakFifVtSc2skEwwOY2hhaW4gaW5hY3RpdmXgaDWxBgAAdGlsEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZckIQwcYmF0Y2ggbnVtYmVyIG91dCBvZiBzZXF1ZW5jZeBpaDUPBwAAdW01Rfj//3ZuC5cmBQgiCW7bMBDOFJckHAwXYmF0Y2ggYWxyZWFkeSBzdWJtaXR0ZWTgABx4NR8IAAB3B28HaDWxCAAAlyQvDCpwcmVTdGF0ZVJvb3QgZG9lcyBub3QgbWF0Y2ggY2Fub25pY2FsIGhlYWTgenl4NdkJAAB3CAEcAXg10gcAAHcJbwhvCZckMgwtcHVibGljSW5wdXRIYXNoIG5vdCBib3VuZCB0byBjb21taXRtZW50IHJvb3Rz4HgBPAHOdwpoajX/DAAAdwtoajUhDQAAdwxvDG8LNToNAABvCm8LNSgOAAAkQww+cHJvb2YgdHlwZSBpbmNvbXBhdGlibGUgd2l0aCBjaGFpbidzIGFkdmVydGlzZWQgc2VjdXJpdHkgbGV2ZWzgDAH92zA1E/f//0rYJh1FDBd2ZXJpZmllciByZWdpc3RyeSB1bnNldDpK2CQJSsoAFCgDOncNeBHAFQwQdmVyaWZ5Q29tbWl0bWVudG8NQWJ9W1J3Dm8OJCEMHHZlcmlmaWVyIHJlamVjdGVkIGNvbW1pdG1lbnTgAfwAeDWqBgAAdw9vDG8PaWg1pA0AAG8KEpcmBRIiAxF3EBGIShBvENBtNc/8//94aWg1Nw4AADXC/P//AZwAeDVuBgAAdxFvEdswaWg1Kw4AADWn/P//bwoSlyZoNUT3//93Em8SStkoJAZFCSIGygAUsyQFCSIHbxIQs6okIwweb3B0aW1pc3RpYyBjaGFsbGVuZ2Ugbm90IHdpcmVk4Hg14w0AAHcTbxNpaBPAHwwKb3BlbldpbmRvd28SQWJ9W1JFADx4NegFAAB3Em8SaWgTwAwOQmF0Y2hTdWJtaXR0ZWRBlQFvYUBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSIgJAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5Fp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknh5F55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkiICQEFifVtSQFcBAXg0NTXv8f//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQFcBARWIcBRKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EVoIgJAStgmBkUQIgTbIUBXAAJ5eBE0A0BXAQMdiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFekoQLgQiCEoB/wAyBgH/AJFKaBVR0EV6GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeiCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXoAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFegAgqUoQLgQiCEoB/wAyBgH/AJFKaBlR0EV6ACipShAuBCIISgH/ADIGAf8AkUpoGlHQRXoAMKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegA4qUoQLgQiCEoB/wAyBgH/AJFKaBxR0EVoIgJA2zBAVwICACCIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JJBo2yhK2CQJSsoAICgDOiICQNsoStgkCUrKACAoAzpAVwMBeDXFAAAANU3v//9waAuYJhNoStgkCUrKACAoAzojqAAAAAwB/NswNSzv//9K2CYURQwOcmVnaXN0cnkgdW5zZXQ6StgkCUrKABQoAzpxeBHAFQwTZ2V0R2VuZXNpc1N0YXRlUm9vdGlBYn1bUnJqDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJC8MKmNoYWluIGdlbmVzaXMgc3RhdGUgcm9vdCBpcyBub3QgcmVnaXN0ZXJlZOBqIgJAVwEBFYhwE0poEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRWgiAkAMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQFcDAwFcAYhwEHEQciJueGrOSmhpap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagActSSQaQAcnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUUAHHhpSmBoNdABAAAAPHhYSmBoNcQBAAAAXHhYSmBoNbgBAAAAfHhYSmBoNawBAAABnAB4WEpgaDWfAQAAAbwAeFhKYGg1kgEAAAHcAHhYSmBoNYUBAAAQciJueWrOSmhYap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAgtSSQWAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEUB/AB4WEpgaDXNAAAAEHIibnpqzkpoWGqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkFgAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSmBFaNsoNwAAcmo3AADbMNsoStgkCUrKACAoAzoiAkBXAQQQcCOhAAAAentonkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSnhYaJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaAAgtSVg////WAAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KYEVANwAAQNsoQFcAAnkRwBUMEGdldFNlY3VyaXR5TGV2ZWx4QWJ9W1JKEAEAAbskAzoiAkBXAAJ5EcAVDAlnZXREQU1vZGV4QWJ9W1JKEAEAAbskAzoiAkBXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCY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2/6odNswcWjKABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxieSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hyEHMiPmhrzkpqa1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWtoyrUkwGjKcxB0Im5pbM5KamtsnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsABS1JJBrABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pzRXhKEC4EIghKAf8AMgYB/wCRSmprUdBFeBipShAuBCIISgH/ADIGAf8AkUpqaxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXggqUoQLgQiCEoB/wAyBgH/AJFKamsSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV4ABipShAuBCIISgH/ADIGAf8AkUpqaxOeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWsUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9Kc0V5ShAuBCIISgH/ADIGAf8AkUpqa1HQRXkYqUoQLgQiCEoB/wAyBgH/AJFKamsRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV5IKlKEC4EIghKAf8AMgYB/wCRSmprEp5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKamsTnkoCAAAAgC4EIgpK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RW8MACBvD55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpvDm8PUdBFbw9KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93D0VvDwAgtSV7////bm8NaTVwAgAAbm8OajVnAgAAbkqcShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXZFbny1Jdj8//8IaTUdBAAAdglqNRUEAAB3B3tu2yhK2CQJSsoAICgDOpckMQwsR2F0ZXdheSBjb25zdGl0dWVudCBjb21taXRtZW50IHJvb3QgbWlzbWF0Y2jgem8H2yhK2CQJSsoAICgDOpckKQwkR2F0ZXdheSBnbG9iYWwgbWVzc2FnZSByb290IG1pc21hdGNo4BB3CCPoAAAAbwhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfHKBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwlvCWg1Ztn//3cKbwkUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9oNSza//93C28Lbwo1YPD//7cmEG8Lbwo1jfD//zXw7P//bwhKnEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJF3CEVvCHy1JRn///81ytH//3cIbwhK2SgkBkUJIgbKABSzJAUJIgdvCBCzqiQdDBhtZXNzYWdlIHJvdXRlciBub3Qgd2lyZWTgfwl/CH8Hfn18e3p4GcAfDBFwdWJsaXNoR2xvYmFsUm9vdG8IQWJ9W1IiAkBXAgAdxABwEHEiPRCISmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaWjKtSTBaCICQFcDA3nKACCXJCIMHUdhdGV3YXkgbGVhZiBtdXN0IGJlIDMyIGJ5dGVz4HlwenEQcmkRkRGXJ78AAABqeMq1JB4MGUdhdGV3YXkgZnJvbnRpZXIgb3ZlcmZsb3fgeGrOygAglyQjDB5HYXRld2F5IGZyb250aWVyIGlzIGluY29tcGxldGXgaHhqzjWUAAAASnBFEIhKeGpR0EVpEalKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFI0H///9qeMq1JB4MGUdhdGV3YXkgZnJvbnRpZXIgb3ZlcmZsb3fgaEp4alHQRUBXAgJ4ygAglyQFCSIHecoAIJckIgwdR2F0ZXdheSBub2RlIG11c3QgYmUgMzIgYnl0ZXPgAECIcBBxInh4ac5KaGlR0EV5ac5KaAAgaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSGaNsoNwAAcWk3AADbMCICQFcEAhCIcBBxEHIjBAEAAHhqznNryhCXJgcjwgAAAGvKACCXJCAMG0dhdGV3YXkgZnJvbnRpZXIgaXMgY29ycnVwdOBoyhCXJg9rSnBFakpxRSOKAAAAeSZGaWq1JkFoaDXY/v//SnBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFIr5oazWZ/v//SnBFahGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWp4yrUl/f7//2jKACCXJB4MGUdhdGV3YXkgZnJvbnRpZXIgaXMgZW1wdHngaCICQFcBAng1wNj//3B5aHg0BSICQFcEA3l4NWXZ//81ncr//3BoC5cmBQkiN2jbMBDOcWkTmCYFCSIpeXg1V+L//zV6yv//cmoLlyYFCSIUakrYJAlKygAgKAM6c2t6lyICQFcLBXl4NRfZ//81T8r//3BoC5cmCAkjuwIAAGjbMBDOcWkTmCYICSOqAgAAeXg1A+L//zUmyv//cmoLlyYICSOSAgAAakrYJAlKygAgKAM6c3sLmCQWDBFzaWJsaW5ncyByZXF1aXJlZOB7dGzKAEC2JBMMDnByb29mIHRvbyBkZWVw4HrbMHV8dhB3ByMoAgAAbG8HzncIbwjKACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh3CW4RkRCXJ9YAAAAQdwoiQ21vCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuhB3CiJ1bwhvCs5KbwkAIG8KnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8KACC1JIgj0QAAABB3CiJEbwhvCs5KbwlvClHQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkuRB3CiJ0bW8KzkpvCQAgbwqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8KSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwpFbwoAILUkiW8J2yg3AAB3Cm8KNwAA2zBKdUVuEalKdkVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HbMq1Jdj9//9rbdsoStgkCUrKACAoAzqXIgJAVwgEeDUt2P//cGgMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSN2AgAAeguYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HpxacoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgedswcntzEHQjGwIAAGlsznVtygAglyQdDBhzaWJsaW5nIG11c3QgYmUgMzIgYnl0ZXPgAECIdmsRkRCXJ9MAAAAQdwciQmpvB85Kbm8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvBwAgtSS7EHcHInNtbwfOSm4AIG8HnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JIojzgAAABB3ByJCbW8HzkpubwdR0EVvB0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cHRW8HACC1JLsQdwcic2pvB85KbgAgbweeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUkim7bKDcAAHcHbwc3AADbMEpyRWsRqUpzRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWxpyrUl5v3//2hq2yhK2CQJSsoAICgDOpciAkBWAgwUbmVvNC1nb3Y6cmV2ZXJ0QmF0Y2jbMGFAx34cFw==").AsSerializable<Neo.SmartContract.NefFile>();

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

    #endregion
}
