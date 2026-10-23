using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubRollupHub(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.RollupHub"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""registerChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""genesisStateRoot"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":174,""safe"":false},{""name"":""updateChain"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1275,""safe"":false},{""name"":""pauseChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1432,""safe"":false},{""name"":""resumeChain"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1562,""safe"":false},{""name"":""getChainConfig"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":1689,""safe"":true},{""name"":""isActive"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1719,""safe"":true},{""name"":""getSecurityLevel"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":1773,""safe"":true},{""name"":""getGenesisStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":1827,""safe"":true},{""name"":""recordBatchDA"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""daCommitment"",""type"":""Hash256""},{""name"":""firstBlock"",""type"":""Integer""},{""name"":""lastBlock"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1898,""safe"":false},{""name"":""getBatchDACommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":2399,""safe"":true},{""name"":""isBatchDAAvailable"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":2471,""safe"":true},{""name"":""enqueueForcedTransaction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""transactionBytes"",""type"":""ByteArray""},{""name"":""transactionHash"",""type"":""Hash256""}],""returntype"":""Integer"",""offset"":2491,""safe"":false},{""name"":""consume"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4651,""safe"":false},{""name"":""reportCensorship"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5893,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6265,""safe"":true},{""name"":""isCensorshipReported"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":6298,""safe"":true},{""name"":""getFee"",""parameters"":[],""returntype"":""Integer"",""offset"":4605,""safe"":true},{""name"":""setFee"",""parameters"":[{""name"":""fee"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6315,""safe"":false},{""name"":""getDeadlineSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":3204,""safe"":true},{""name"":""setDeadlineSeconds"",""parameters"":[{""name"":""seconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6391,""safe"":false},{""name"":""getNextForcedNonce"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6837,""safe"":true},{""name"":""getPendingForcedCount"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6844,""safe"":true},{""name"":""submitAndFinalizeBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":6916,""safe"":false},{""name"":""submitBatch"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""},{""name"":""l1MessageHash"",""type"":""ByteArray""},{""name"":""blockContextHash"",""type"":""ByteArray""},{""name"":""forcedInclusionCount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9571,""safe"":false},{""name"":""isProofTypeCompatible"",""parameters"":[{""name"":""securityLevel"",""type"":""Integer""},{""name"":""proofType"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8431,""safe"":true},{""name"":""finalizeBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9676,""safe"":false},{""name"":""revertBatch"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Void"",""offset"":9950,""safe"":false},{""name"":""revertBatchViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":10841,""safe"":false},{""name"":""executeGovernanceProposal"",""parameters"":[{""name"":""action"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":12171,""safe"":false},{""name"":""updateChainViaProposal"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":14145,""safe"":false},{""name"":""setChainActiveViaProposal"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""active"",""type"":""Boolean""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":14485,""safe"":false},{""name"":""buildRevertBatchAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":11086,""safe"":true},{""name"":""buildUpdateChainAction"",""parameters"":[{""name"":""configBytes"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":13843,""safe"":true},{""name"":""buildSetChainActiveAction"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""ByteArray"",""offset"":13540,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":14802,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":10046,""safe"":true},{""name"":""publishGatewayGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""constituentReferences"",""type"":""ByteArray""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":15096,""safe"":false},{""name"":""getGatewayFinalizedThrough"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":10576,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":18989,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":19057,""safe"":true},{""name"":""getSharedBridge"",""parameters"":[],""returntype"":""Hash160"",""offset"":15038,""safe"":true},{""name"":""setSharedBridge"",""parameters"":[{""name"":""sharedBridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":19125,""safe"":false},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":18522,""safe"":true},{""name"":""getCanonicalStateRoot"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":8079,""safe"":true},{""name"":""getLatestFinalizedBatchNumber"",""parameters"":[{""name"":""chainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":8010,""safe"":true},{""name"":""getBatchStatus"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":5291,""safe"":true},{""name"":""getBatchCommitment"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":19256,""safe"":true},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":631,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":19287,""safe"":false},{""name"":""getVerifierRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":9151,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""controller"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":19400,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":3093,""safe"":true},{""name"":""verifyWithdrawalLeaf"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":19674,""safe"":true},{""name"":""verifyWithdrawalLeafAt"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":19692,""safe"":true},{""name"":""verifyWithdrawalLeafWithProof"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""leafHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":19750,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":20439,""safe"":false}],""events"":[{""name"":""ChainRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ChainStatusChanged"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Boolean""}]},{""name"":""GenesisRootRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""BatchSubmitted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""ForcedTransactionEnqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""ForcedTxConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""CensorshipReported"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ForcedTransactionsConsumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""BatchReverted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Consolidated L1 Rollup Hub for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.RollupHub"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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MHMQdCJua2zOSmlqbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbAAUtSSQagAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV52zB0EHUibmxtzkppam2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkkGoAIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFespKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXrKABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EUQdSJuem3OSmlqbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbXrKtSSQanrKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV7ShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV7GKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXsgqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFewAYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFaSICQNswQNswQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAQAMAUjbMDWp8P//cGgLlyYFECINaErYJgZFECIE2yEiAkA3AQBAQdv+qHRAVwcFeBC3JCEMHGNoYWluSWQgMCBpcyByZXNlcnZlZCBmb3IgTDHgewuYJBYMEXNpYmxpbmdzIHJlcXVpcmVk4HtwaMoAQLYkEwwOcHJvb2YgdG9vIGRlZXDgeng1gvr//zUi8P//cWkLmCQUDA9lbnRyeSBub3QgZm91bmTgadswcmrKADy4JBQMD2VudHJ5IG1hbGZvcm1lZOAAFGo1IgEAAHN6eDXAAQAAdGw11u///wuXJBUMEGFscmVhZHkgY29uc3VtZWTgDAEB2zBsNevx//95eDWdAQAANajv//91bQuYJBQMD2JhdGNoIG5vdCBmb3VuZOB5eDWGAQAAE5ckGAwTYmF0Y2ggbm90IGZpbmFsaXplZOAAIABcbdswNccAAADbKErYJAlKygAgKAM6dm4MIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okHgwZYmF0Y2ggaGFzIG5vIHRyYW5zYWN0aW9uc+B8aG5rNTABAAAkJQwgaW52YWxpZCBmb3JjZWQtdHJhbnNhY3Rpb24gcHJvb2bgengSwAwQRm9yY2VkVHhDb25zdW1lZEGVAW9hQFcAAgAgeXg0INsoStgkCUrKACAoAzoiAkDbKErYJAlKygAgKAM6QFcCA3qIcBBxIm54eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVperUkkWgiAkBXAAJ5eABGNYTz//9AVwACeXgAIzV38///QFcBAnl4NBY1/O3//3BoC5cmBRAiBWgQziICQFcAAnl4ACI1T/P//0DOQFcGBHjbMHB7cRByIwECAAB6as5za8oAIJckHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVz4ABAiHRpEZEQlyfHAAAAEHUiPmhtzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkwBB1Im9rbc5KbAAgbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSSPI8IAAAAQdSI+a23OSmxtUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSTAEHUib2htzkpsACBtnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JI9s2yg3AAB1bTcAANswSnBFaRGpSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanrKtSUA/v//aRCYJgUJIhR5aNsoStgkCUrKACAoAzqXIgJAVwcCeXg1bwEAAKokFQwQYWxyZWFkeSBjb25zdW1lZOB5eDVmAQAAcGg1gOv//wuXJCAMG2NlbnNvcnNoaXAgYWxyZWFkeSByZXBvcnRlZOB5eDWy9f//NVLr//9xaQuYJBQMD2VudHJ5IG5vdCBmb3VuZOBp2zByasoAPLgkFAwPZW50cnkgbWFsZm9ybWVk4GpqyhSfSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1A1EOv//3NBt8OIAwHoA6FKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdGxrtSYFCSJ8DAEB2zBoNeHs//95eBLADBJDZW5zb3JzaGlwUmVwb3J0ZWRBlQFvYTXv8///dW1K2SgkBkUJIgbKABSzJAUJIgZtELOqJjI1Mur//0H4J+yMJgUIIghtQfgn7Ix2biYZeBHAHwwKcGF1c2VDaGFpbm1BYn1bUkUIIgJAVwACeXg1E/z//zUr6v//C5giAkBXAAJ5eABHNYjv//9AVwACeXg07jUN6v//C5giAkBXAAE1yen//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgQuCQdDBhmZWUgbXVzdCBiZSBub24tbmVnYXRpdmXgeAwBSNswNfb4//9AVwABNX3p//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4ADy4JAUJIgl4AoBRAQC2JBoMFWRlYWRsaW5lIG91dCBvZiByYW5nZeB4DAFJ2zA1oPj//0BXBAJ5EJcmByMaAQAAeDUVAQAAcHg1YvP//3FoeZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkWm2JCcMImZvcmNlZCB0cmFuc2FjdGlvbiBxdWV1ZSB1bmRlcmZsb3fgaHmeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJFyeDW4AAAAalA17/f//2hzIkVreDWO+v//DAEB2zBQNdfq//9rSpxKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXNFa2q1JLp5aHgTwAwaRm9yY2VkVHJhbnNhY3Rpb25zQ29uc3VtZWRBlQFvYUBXAQF4NDU1OOj//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAF4AEA1pun//0BXAAF4NLdAVwIBeDSwcHg1APL//3FpaLgmM2lon0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRIgMQIgJAVwIEEHg10ef//3AUeDQzcXt4ATwBzmloenl4NTIBAABoaQAgADx4Nef4///bKErYJAlKygAgKAM6UzXICAAAQFcCAhBwEHEj+gAAAGh4eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YaaBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaRi1JQj///9oIgJAVw0HeMoBQQG4JCAMG2NvbW1pdG1lbnQgaGVhZGVyIHRvbyBzaG9ydOB5C5gkBQkiB3nKACCXJCMMHmwxTWVzc2FnZUhhc2ggbXVzdCBiZSAzMiBieXRlc+B6C5gkBQkiB3rKACCXJCYMIWJsb2NrQ29udGV4dEhhc2ggbXVzdCBiZSAzMiBieXRlc+B5cHpxezXV6f//JBMMDmNoYWluIGluYWN0aXZl4Hs1vu7//30SmCQ4DDNvcHRpbWlzdGljIGJhdGNoZXMgcmVxdWlyZSBhIHdpcmVkIGNoYWxsZW5nZSB3aW5kb3fgezUOAgAAcnxqEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZckGgwVYmF0Y2ggb3V0IG9mIHNlcXVlbmNl4AAgABx4NYD2///bKErYJAlKygAgKAM6c3s16QEAAHRrbJckHAwXcHJlLXN0YXRlIHJvb3QgbWlzbWF0Y2jgezWB5v//Nd3k///bMHVtAFTOdm0AVc53B28HbjURAgAAfW41AQMAACRDDD5wcm9vZiB0eXBlIGluY29tcGF0aWJsZSB3aXRoIGNoYWluJ3MgYWR2ZXJ0aXNlZCBzZWN1cml0eSBsZXZlbOAAIAH8AHg11vX//9soStgkCUrKACAoAzp3CG8IfHs1POn//35paHg13wIAAHcJACABHAF4Naj1///bKErYJAlKygAgKAM6dwpvCW8KlyQfDBpwdWJsaWMgaW5wdXQgaGFzaCBtaXNtYXRjaOA1HQUAAHcLbwsMFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCQlDCB2ZXJpZmllciByZWdpc3RyeSBub3QgY29uZmlndXJlZOB4EcAfDAt2ZXJpZnlQcm9vZm8LQWJ9W1J3DG8MJB4MGXByb29mIHZlcmlmaWNhdGlvbiBmYWlsZWTgfHs1e/X//3hQNbvl//9+ezUc+v//fhC3JhB8ezW8BAAAflA1o/L//0BXAQF4NDU1XuP//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAF4ACE1zOT//0BXAgF4NFk1GeP//3BoC5gmEGhK2CQJSsoAICgDOiI/eDUq5f//Nfri//9xaQuYJCAMG2dlbmVzaXMgcm9vdCBub3QgcmVnaXN0ZXJlZOBpStgkCUrKACAoAzoiAkBXAAF4ACA1Y+T//0BXAAJ4FLYkUAxLc2VjdXJpdHlMZXZlbCBtdXN0IGJlIDAuLjQgKFNpZGVjaGFpbi9TZXR0bGVkL09wdGltaXN0aWMvVmFsaWRpdHkvVmFsaWRpdW0p4HkTtiQwDCtkYU1vZGUgbXVzdCBiZSAwLi4zIChMMS9OZW9GUy9FeHRlcm5hbC9EQUMp4HgTlyYweRCXJCsMJlZhbGlkaXR5IHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIEwxIERB4HgUlyY3eRCYJDIMLVZhbGlkaXVtIHNlY3VyaXR5IGxldmVsIHJlcXVpcmVzIG9mZi1jaGFpbiBEQeBAVwACeBCXJgUIIgV4EZcmF3kRlyYFCCIFeRKXJgUIIgV5E5ciKXgSlyYPeRKXJgUIIgV5E5ciF3gTlyYFCCIFeBSXJgd5E5ciBQkiAkBXAgQBYAGIcBBxIj54ac5KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpAfwAtSS/EHEicHlpzkpoAfwAaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSOEHEjpQAAAHgB/ABpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmgBHAFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JVz///8QcSJwemnOSmgBPAFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpACC1JI57Af8AkUoQLgQiCEoB/wAyBgH/AJFKaAFcAVHQRXsYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXQFR0EV7IKkB/wCRShAuBCIISgH/ADIGAf8AkUpoAV4BUdBFewAYqQH/AJFKEC4EIghKAf8AMgYB/wCRSmgBXwFR0EVo2yg3AABxaTcAAErYJAlKygAgKAM6IgJAVwEADAEC2zA1597//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAAnl4AEU1HOT//0BXAQN5eDW78P//DAED2zBQNc/g//94NWf7//95UDXE7f//eDXD+///elA1Ytz//3l4NWfw//81ct7//3BoC5gkFwwSbWlzc2luZyBjb21taXRtZW504GjbMHl4NB16eXgTwAwOQmF0Y2hGaW5hbGl6ZWRBlQFvYUBXBAN62yg3AABwaDcAANswcQBAiHIQcyOtAAAAaWvOSmprUdBFegHcAGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn85KagAga55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAgtSVU////eXg0CmpQNZTf//9AVwACeXgZNb/i//9AVwQEEHg1ct3//3AUeDXU9f//cXgBPAHOcntqaWh6eXg1zvb//2loNTzv//9zDAEB2zBrNU/f//8AIAA8eDVy7v//2yhK2CQJSsoAICgDOmloE8AMDkJhdGNoU3VibWl0dGVkQZUBb2FAVwUCNajc//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4Ncrg//8kEwwOY2hhaW4gaW5hY3RpdmXgeDWz5f//eDU++f//cHloEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZckHwwabXVzdCBmaW5hbGl6ZSBzZXF1ZW50aWFsbHngeXg1Yu7//3FpNUPc//9yaguYJAUJIgdqEM4RlyQWDBFiYXRjaCBub3QgcGVuZGluZ+B5eDUI7v//NRPc//9zawuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOAAIAA8a9swNVDt///bKErYJAlKygAgKAM6dGx5eDUu/f//QFcAAjRdJiE1MOX//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4CIfNXPb//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB5eDQqeXgSwAwNQmF0Y2hSZXZlcnRlZEGVAW9hQAwBRNswNWvb//8LmCICQFcFAnl4NXTt//9waDVV2///cWkLmCQUDA9iYXRjaCBub3QgZm91bmTgeDXT9///cnlqlycVAQAAaRDOE5ckGAwTYmF0Y2ggbm90IGZpbmFsaXplZOB5eDWsAQAAtyQnDCJiYXRjaCBhbHJlYWR5IHB1Ymxpc2hlZCBieSBnYXRld2F54Hl4Ncvs//811tr//3NrC5gkFwwSbWlzc2luZyBjb21taXRtZW504AAgABxr2zA1E+z//9soStgkCUrKACAoAzp0DAEE2zBoNc3c//95eDV/7P//NXABAAB5eDUs/f//NWQBAAB5eDXb3///NVgBAAB5eDVmAQAAeDWj9///bFA1Qtj//3g1Lff//3kRn0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRUDVc6f//I78AAAB5ahGeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGXJklpEM4RlyQWDBFiYXRjaCBub3QgcGVuZGluZ+AMAQTbMGg1/Nv//3l4Na7r//81nwAAAHl4NRbf//81kwAAAHl4NaEAAAAiQgkkPww6b25seSB0aGUgcGVuZGluZyBvciBsYXRlc3QgZmluYWxpemVkIGJhdGNoIGNhbiBiZSByZXZlcnRlZOBAVwEBeDQ1NVjZ//9waAuXJgUQIiRoStgmBkUQIgTbIUoQBAAAAAAAAAAAAQAAAAAAAAC7JAM6IgJAVwABeBo1x9r//0BXAAF4QZv2Z85BL1jF7UBBL1jF7UBXBAJ5eDVL+v//cGg1+dj//3FoNNhpC5cmByOVAAAAaUrYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOnJqEJcmBCJoeDV98P//c2tquCQhDBxmb3JjZWQgaGVhZCByZXdpbmQgdW5kZXJmbG934Hg1i/D//2tqn0oQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRUDWU5///QFcEAzXi/P//JBoMFWdvdmVybmFuY2Ugbm90IGxvY2tlZOA1muH//3BoQfgn7IwkEwwObm90IGF1dGhvcml6ZWTgehHAFQwXaXNBcHByb3ZlZEFuZFRpbWVsb2NrZWRoQWJ9W1JxaSQnDCJwcm9wb3NhbCBub3QgYXBwcm92ZWQgKyB0aW1lbG9ja2Vk4Hl4NGZyanoSwBUMFm1hdGNoZXNQcm9wb3NhbFBheWxvYWRoQWJ9W1JzayQeDBlwcm9wb3NhbCBwYXlsb2FkIG1pc21hdGNo4Hl4NR38//95eBLADA1CYXRjaFJldmVydGVkQZUBb2FAVwQCWHBoyhSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxieSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxEHIQcyJvaGvOSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWtoyrUkj3hqaTRCahSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRXlqaTUAAQAAaSICQFcAA3pKEC4EIghKAf8AMgYB/wCRSnh5UdBFehipShAuBCIISgH/ADIGAf8AkUp4eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXogqUoQLgQiCEoB/wAyBgH/AJFKeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ABipShAuBCIISgH/ADIGAf8AkUp4eROeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRUBXAAN6ShAuBCIISgH/ADIGAf8AkUp4eVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6IKlKEC4EIghKAf8AMgYB/wCRSnh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ACCpShAuBCIISgH/ADIGAf8AkUp4eRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAKKlKEC4EIghKAf8AMgYB/wCRSnh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAwqUoQLgQiCEoB/wAyBgH/AJFKeHkWnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ADipShAuBCIISgH/ADIGAf8AkUp4eReeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRUBXCAJ4C5gkFAwPYWN0aW9uIHJlcXVpcmVk4HhwNZf3//8kGgwVZ292ZXJuYW5jZSBub3QgbG9ja2Vk4DVP3P//cWlB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB5EcAVDBdpc0FwcHJvdmVkQW5kVGltZWxvY2tlZGlBYn1bUnJqJCcMInByb3Bvc2FsIG5vdCBhcHByb3ZlZCArIHRpbWVsb2NrZWTgWGg1egMAACcmAQAAWMoUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8YnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zaMprlyQkDB9pbnZhbGlkIHJldmVydEJhdGNoIGFjdGlvbiBzaXpl4GhYylA1DtL//3RoWMoUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9QNT7q//91aG1sNT/6//9QNfICAAB5AEo1rgMAADV/AwAAeQBKNaEDAAAMAQHbMFA1sNP//21sNRH2//9tbBLADA1CYXRjaFJldmVydGVkQZUBb2EjUwIAAFloNU0CAAAnbgEAAFnKFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc2jKa5ckJwwiaW52YWxpZCBzZXRDaGFpbkFjdGl2ZSBhY3Rpb24gc2l6ZeBoWcpQNd7Q//90aFnKFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhCYdWhtbDWoAgAAUDXFAQAAeQBLNYECAAA1UgIAAHkASzV0AgAADAEB2zBQNYPS//9sNeXR//81QdD//3ZuC5gkGQwUY2hhaW4gbm90IHJlZ2lzdGVyZWTgbtswdwdtJgURIgMQSm8HAFpR0EVsNanR//9vB1A1OdL//21sEsAMEkNoYWluU3RhdHVzQ2hhbmdlZEGVAW9hI94AAABaaDXYAAAAJ7MAAABoSspQWspRS5+Mc2vKAFuXJBgME2ludmFsaWQgY29uZmlnIHNpemXgaGs1CgMAAFA1+AAAAHkATDW0AQAANYUBAAB5AEw1pwEAAAwBAdswUDW20f//EGs1pM///3RsNRDR//81bM///3VtC5gkGQwUY2hhaW4gbm90IHJlZ2lzdGVyZWTgbDXo0P//a1A1edH//2tsEsAMD0NoYWluUmVnaXN0ZXJlZEGVAW9hIiEJJB4MGXVua25vd24gZ292ZXJuYW5jZSBhY3Rpb27gQFcBAnjKecq1JgUJIk4QcCJBeGjOeWjOmCYFCSI+aEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaHnKtSS9CCICQFcBAnjKecqXJCYMIWdvdmVybmFuY2UgYWN0aW9uIGxlbmd0aCBtaXNtYXRjaOAQcCJjeGjOeWjOlyQnDCJnb3Zlcm5hbmNlIGFjdGlvbiBwYXlsb2FkIG1pc21hdGNo4GhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9wRWh5yrUkm0BXAAF4NQnO//8LlyQeDBlwcm9wb3NhbCBhbHJlYWR5IGNvbnN1bWVk4EBXAQIZiHB4SmgQUdBFeRFoNZH4//9oIgJAVwQCWXBoyhSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxEHIQcyJvaGvOSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWtoyrUkj3hqaTWs9v//ahSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRXkmBREiAxBKaWpR0EVpIgJAVwQBWnBoynjKnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IcRByEHMib2hrzkppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VraMq1JI8QcyJveGvOSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWt4yrUkj2kiAkBXBgI1+u///yQaDBVnb3Zlcm5hbmNlIG5vdCBsb2NrZWTgNbLU//9waEH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HkRwBUMF2lzQXBwcm92ZWRBbmRUaW1lbG9ja2VkaEFifVtScWkkJwwicHJvcG9zYWwgbm90IGFwcHJvdmVkICsgdGltZWxvY2tlZOB4NUT+//9yankSwBUMFm1hdGNoZXNQcm9wb3NhbFBheWxvYWRoQWJ9W1JzayQeDBlwcm9wb3NhbCBwYXlsb2FkIG1pc21hdGNo4HgLmCQFCSIHeMoAW5ckGAwTaW52YWxpZCBjb25maWcgc2l6ZeAQeDWbyv//dGw1B8z//zVjyv//dW0LmCQZDBRjaGFpbiBub3QgcmVnaXN0ZXJlZOBsNd/L//94UDVwzP//eGwSwAwPQ2hhaW5SZWdpc3RlcmVkQZUBb2FAVwYDNabu//8kGgwVZ292ZXJuYW5jZSBub3QgbG9ja2Vk4DVe0///cGhB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB6EcAVDBdpc0FwcHJvdmVkQW5kVGltZWxvY2tlZGhBYn1bUnFpJCcMInByb3Bvc2FsIG5vdCBhcHByb3ZlZCArIHRpbWVsb2NrZWTgeXg1wPv//3JqehLAFQwWbWF0Y2hlc1Byb3Bvc2FsUGF5bG9hZGhBYn1bUnNrJB4MGXByb3Bvc2FsIHBheWxvYWQgbWlzbWF0Y2jgeDXfyv//NTvJ//90bAuYJBkMFGNoYWluIG5vdCByZWdpc3RlcmVk4GzbMHV5JgURIgMQSm0AWlHQRXg1pcr//21QNTbL//95eBLADBJDaGFpblN0YXR1c0NoYW5nZWRBlQFvYUA1pcj//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4DUm0v//DBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkKQwkZ292ZXJuYW5jZSBjb250cm9sbGVyIG5vdCBjb25maWd1cmVk4DWKAAAADBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkIQwcc2hhcmVkIGJyaWRnZSBub3QgY29uZmlndXJlZOA1zez//6okHgwZZ292ZXJuYW5jZSBhbHJlYWR5IGxvY2tlZOAMAQHbMAwBRNswNUbK//8QwAwQR292ZXJuYW5jZUxvY2tlZEGVAW9hQFcBAAwBC9swNejH//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXFQp5C5gkJAwfY29uc3RpdHVlbnQgcmVmZXJlbmNlcyByZXF1aXJlZOB5cHwQtyQFCSIHfAEAELYkJgwhY29uc3RpdHVlbnQgY291bnQgbXVzdCBiZSAxLi40MDk24GjKfEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8coEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+XJCoMJWNvbnN0aXR1ZW50IHJlZmVyZW5jZSBsZW5ndGggbWlzbWF0Y2jgewwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXqiQmDCFjb25zdGl0dWVudCByb290IG11c3QgYmUgbm9uLXplcm/gfTViCAAAJEEMPHBhc3MtdGhyb3VnaC9yZXNlcnZlZCBhZ2dyZWdhdGlvbiBiYWNrZW5kIGlzIG5vdCBwdWJsaXNoYWJsZeB+EbgkBQkiBX4UtiQdDBhwcm9vZlN5c3RlbSBtdXN0IGJlIDEuLjTgfwcMIAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAl6okKQwkdmVyaWZpY2F0aW9uIGtleSBpZCBtdXN0IGJlIG5vbi16ZXJv4H8IDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJCMMHnJlcGxheSBkb21haW4gbXVzdCBiZSBub24temVyb+B/CQuYJAUJIgd/CcoQtyQeDBlhZ2dyZWdhdGVkIHByb29mIHJlcXVpcmVk4H8JygIAABAAtiQfDBphZ2dyZWdhdGVkIHByb29mIHRvbyBsYXJnZeA1Nf3//3FpStkoJAZFCSIGygAUsyQFCSIGaRCzqiQcDBdzaGFyZWQgYnJpZGdlIG5vdCB3aXJlZOA17AYAAHI15gYAAHMQdBB1EHYjDwMAAG5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfHKBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdvB2g1ncT//3cIbwcUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9oNc3c//93CW8IELckKQwkR2F0ZXdheSBjaGFpbklkIDAgaXMgcmVzZXJ2ZWQgZm9yIEwx4G4QtyZUbwhstyYFCCIPbwhslyQFCSIGbwlttyQ8DDdHYXRld2F5IGNvbnN0aXR1ZW50IHJlZmVyZW5jZXMgbXVzdCBiZSBzdHJpY3RseSBvcmRlcmVk4G8ISnRFbwlKdUVvCW8INZvV//8TlyQpDCRHYXRld2F5IGNvbnN0aXR1ZW50IGlzIG5vdCBmaW5hbGl6ZWTgbwg1vAUAACQrDCZHYXRld2F5IGRpc2FibGVkIGZvciBjb25zdGl0dWVudCBjaGFpbuBvCW8INdrp//+3JC4MKUdhdGV3YXkgY29uc3RpdHVlbnQgd2FzIGFscmVhZHkgcHVibGlzaGVk4G8Jbwg1qeX//zX7wv//dwpvCguYJCUMIEdhdGV3YXkgZmluYWxpemVkIHJlY29yZCBtaXNzaW5n4G8K2zB3C28LygBAlyQlDCBHYXRld2F5IGZpbmFsaXplZCByZWNvcmQgY29ycnVwdOAAIIh3DAAgiHcNEHcOI4UAAABvC28OzkpvDG8OUdBFbwsAIG8OnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSm8Nbw5R0EVvDkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cORW8OACC1JXv///9ubwxqNYIEAABubw1rNXkEAABuSpxKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRdkVufLUl8/z//whqNS8GAAB2CWs1JwYAAHcHe27bKErYJAlKygAgKAM6lyQxDCxHYXRld2F5IGNvbnN0aXR1ZW50IGNvbW1pdG1lbnQgcm9vdCBtaXNtYXRjaOB6bwfbKErYJAlKygAgKAM6lyQpDCRHYXRld2F5IGdsb2JhbCBtZXNzYWdlIHJvb3QgbWlzbWF0Y2jgfwh/B359fHt6eDXZBgAAdwh4NXkIAAB3CW8JNRzB//93Cm8KC5gmf3g1ewgAADUJwf//dwtvCkrYJAlKygAgKAM6epckBQkiBm8LC5gkBQkiEm8LStgkCUrKACAoAzpvCJckPQw4ZXBvY2ggYWxyZWFkeSBib3VuZCB0byBhIGRpZmZlcmVudCBnbG9iYWwgcm9vdCBzdGF0ZW1lbnTgCSN5AgAANaPh//93C28LStkoJAZFCSIGygAUsyQFCSIHbwsQs6okJQwgdmVyaWZpZXIgcmVnaXN0cnkgbm90IGNvbmZpZ3VyZWTgfwlvCNswfwfbMH4UwB8MDXZlcmlmeVprUHJvb2ZvC0FifVtSdwxvDCQlDCBnYXRld2F5IGFnZ3JlZ2F0ZSBwcm9vZiByZWplY3RlZOB62zBvCTUywv//eDViBwAAbwjbMFA1IsL//3p4EsAME0dsb2JhbFJvb3RQdWJsaXNoZWRBlQFvYW8Ie3gTwAwXR2xvYmFsUm9vdFByb29mQWNjZXB0ZWRBlQFvYRB3DSN3AQAAbw1KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfHKBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdw5vDmg1Y7///3cPbw4UnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9oNZPX//93EG8Qbw81lOX//7cmEW8PNcPl//9vEFA1Gc7//28Qbw81x9D//zXSvv//dxFvEQuYJBcMEm1pc3NpbmcgY29tbWl0bWVudOBvEdswdxIAIAG8AG8SNQfQ///bKErYJAlKygAgKAM6dxMAIAHcAG8SNezP///bKErYJAlKygAgKAM6dxRvFG8TbxBvDxTAHwwTcHVibGlzaE1lc3NhZ2VSb290c2lBYn1bUkVvDUqcShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXcNRW8NfLUliv7//wgiAkBXAAF4EJgkBQkiB3gB/gCYJAUJIgd4Af8AmCICQFcCAB3EAHAQcSI9EIhKaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpaMq1JMFoIgJAVwIBeDVLv///Nae9//9waAuYJBkMFGNoYWluIG5vdCByZWdpc3RlcmVk4GjbMHFpAFbOEJgiAkBXAwN5ygAglyQiDB1HYXRld2F5IGxlYWYgbXVzdCBiZSAzMiBieXRlc+B5cHpxEHJpEZERlye/AAAAanjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934HhqzsoAIJckIwweR2F0ZXdheSBmcm9udGllciBpcyBpbmNvbXBsZXRl4Gh4as41lAAAAEpwRRCISnhqUdBFaRGpShAuBCIOSgP/////AAAAADIMA/////8AAAAAkUpxRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRSNB////anjKtSQeDBlHYXRld2F5IGZyb250aWVyIG92ZXJmbG934GhKeGpR0EVAVwICeMoAIJckBQkiB3nKACCXJCIMHUdhdGV3YXkgbm9kZSBtdXN0IGJlIDMyIGJ5dGVz4ABAiHAQcSJ4eGnOSmhpUdBFeWnOSmgAIGmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkhmjbKDcAAHFpNwAA2zAiAkBXBAIQiHAQcRByIwQBAAB4as5za8oQlyYHI8IAAABrygAglyQgDBtHYXRld2F5IGZyb250aWVyIGlzIGNvcnJ1cHTgaMoQlyYPa0pwRWpKcUUjigAAAHkmRmlqtSZBaGg12P7//0pwRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRSK+aGs1mf7//0pwRWoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KcUVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqeMq1Jf3+//9oygAglyQeDBlHYXRld2F5IGZyb250aWVyIGlzIGVtcHR54GgiAkBXBAgBqgCIcFtxEHIiPmlqzkpoalHQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWppyrUkwEHb/qh02zByEHMibmprzkpoGGueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAFLUkkH8HABxoNFd4ADxoNTnk//95AERoNEh6AGRoNEJ7AYQA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").AsSerializable<Neo.SmartContract.NefFile>();

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

    public delegate void delCensorshipReported(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("CensorshipReported")]
    public event delCensorshipReported? OnCensorshipReported;

    public delegate void delChainRegistered(BigInteger? arg1, byte[]? arg2);

    [DisplayName("ChainRegistered")]
    public event delChainRegistered? OnChainRegistered;

    public delegate void delChainStatusChanged(BigInteger? arg1, bool? arg2);

    [DisplayName("ChainStatusChanged")]
    public event delChainStatusChanged? OnChainStatusChanged;

    public delegate void delForcedTransactionEnqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt256? arg4);

    [DisplayName("ForcedTransactionEnqueued")]
    public event delForcedTransactionEnqueued? OnForcedTransactionEnqueued;

    public delegate void delForcedTransactionsConsumed(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("ForcedTransactionsConsumed")]
    public event delForcedTransactionsConsumed? OnForcedTransactionsConsumed;

    public delegate void delForcedTxConsumed(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ForcedTxConsumed")]
    public event delForcedTxConsumed? OnForcedTxConsumed;

    public delegate void delGenesisRootRegistered(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GenesisRootRegistered")]
    public event delGenesisRootRegistered? OnGenesisRootRegistered;

    public delegate void delGlobalRootProofAccepted(BigInteger? arg1, UInt256? arg2, UInt256? arg3);

    [DisplayName("GlobalRootProofAccepted")]
    public event delGlobalRootProofAccepted? OnGlobalRootProofAccepted;

    public delegate void delGlobalRootPublished(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GlobalRootPublished")]
    public event delGlobalRootPublished? OnGlobalRootPublished;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? DeadlineSeconds { [DisplayName("getDeadlineSeconds")] get; [DisplayName("setDeadlineSeconds")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? Fee { [DisplayName("getFee")] get; [DisplayName("setFee")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? SharedBridge { [DisplayName("getSharedBridge")] get; [DisplayName("setSharedBridge")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? VerifierRegistry { [DisplayName("getVerifierRegistry")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildGlobalRootProofInputHash")]
    public abstract UInt256? BuildGlobalRootProofInputHash(BigInteger? batchEpoch, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRevertBatchAction")]
    public abstract byte[]? BuildRevertBatchAction(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetChainActiveAction")]
    public abstract byte[]? BuildSetChainActiveAction(BigInteger? chainId, bool? active);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildUpdateChainAction")]
    public abstract byte[]? BuildUpdateChainAction(byte[]? configBytes);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchCommitment")]
    public abstract byte[]? GetBatchCommitment(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getBatchDACommitment")]
    public abstract UInt256? GetBatchDACommitment(BigInteger? chainId, BigInteger? batchNumber);

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
    [DisplayName("getChainConfig")]
    public abstract byte[]? GetChainConfig(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGatewayFinalizedThrough")]
    public abstract BigInteger? GetGatewayFinalizedThrough(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGenesisStateRoot")]
    public abstract UInt256? GetGenesisStateRoot(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGlobalRoot")]
    public abstract UInt256? GetGlobalRoot(BigInteger? batchEpoch);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getGlobalRootProofInputHash")]
    public abstract UInt256? GetGlobalRootProofInputHash(BigInteger? batchEpoch);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLatestFinalizedBatchNumber")]
    public abstract BigInteger? GetLatestFinalizedBatchNumber(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getNextForcedNonce")]
    public abstract BigInteger? GetNextForcedNonce(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPendingForcedCount")]
    public abstract BigInteger? GetPendingForcedCount(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSecurityLevel")]
    public abstract BigInteger? GetSecurityLevel(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isActive")]
    public abstract bool? IsActive(BigInteger? chainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isBatchDAAvailable")]
    public abstract bool? IsBatchDAAvailable(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isCensorshipReported")]
    public abstract bool? IsCensorshipReported(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isConsumed")]
    public abstract bool? IsConsumed(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isProofTypeCompatible")]
    public abstract bool? IsProofTypeCompatible(BigInteger? securityLevel, BigInteger? proofType);

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
    [DisplayName("consume")]
    public abstract void Consume(BigInteger? chainId, BigInteger? batchNumber, BigInteger? nonce, IList<object>? siblings, BigInteger? leafIndex);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("enqueueForcedTransaction")]
    public abstract BigInteger? EnqueueForcedTransaction(BigInteger? chainId, byte[]? transactionBytes, UInt256? transactionHash);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("executeGovernanceProposal")]
    public abstract void ExecuteGovernanceProposal(byte[]? action, BigInteger? proposalId);

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
    [DisplayName("pauseChain")]
    public abstract void PauseChain(BigInteger? chainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishGatewayGlobalRoot")]
    public abstract bool? PublishGatewayGlobalRoot(BigInteger? batchEpoch, byte[]? constituentReferences, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain, byte[]? aggregatedProof);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("recordBatchDA")]
    public abstract void RecordBatchDA(BigInteger? chainId, BigInteger? batchNumber, UInt256? daCommitment, BigInteger? firstBlock, BigInteger? lastBlock);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerChain")]
    public abstract void RegisterChain(BigInteger? chainId, byte[]? configBytes, UInt256? genesisStateRoot);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("reportCensorship")]
    public abstract bool? ReportCensorship(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("resumeChain")]
    public abstract void ResumeChain(BigInteger? chainId);

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
    [DisplayName("setChainActiveViaProposal")]
    public abstract void SetChainActiveViaProposal(BigInteger? chainId, bool? active, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitAndFinalizeBatch")]
    public abstract void SubmitAndFinalizeBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash, BigInteger? forcedInclusionCount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("submitBatch")]
    public abstract void SubmitBatch(byte[]? commitmentBytes, byte[]? l1MessageHash, byte[]? blockContextHash, BigInteger? forcedInclusionCount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("updateChain")]
    public abstract void UpdateChain(byte[]? configBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("updateChainViaProposal")]
    public abstract void UpdateChainViaProposal(byte[]? configBytes, BigInteger? proposalId);

    #endregion
}
