using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubMessageRouter(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MessageRouter"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":175,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":274,""safe"":false},{""name"":""enqueueL1ToL2"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""receiver"",""type"":""Hash160""},{""name"":""messageType"",""type"":""Integer""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":395,""safe"":false},{""name"":""getL1ToL2"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":3509,""safe"":true},{""name"":""setL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""},{""name"":""filter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":3543,""safe"":false},{""name"":""clearL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3697,""safe"":false},{""name"":""getL1TxFilter"",""parameters"":[{""name"":""targetChainId"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":759,""safe"":true},{""name"":""publishMessageRoots"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""l2ToL1Root"",""type"":""Hash256""},{""name"":""l2ToL2Root"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":3846,""safe"":false},{""name"":""getL2ToL1Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":3992,""safe"":true},{""name"":""getL2ToL2Root"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":4100,""safe"":true},{""name"":""publishGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""aggregatedProof"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":4173,""safe"":false},{""name"":""setGlobalRootVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Void"",""offset"":7121,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":7655,""safe"":false},{""name"":""lockGlobalRootGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":7841,""safe"":false},{""name"":""setGlobalRootVerifierViaProposal"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":8423,""safe"":false},{""name"":""buildSetGlobalRootVerifierAction"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""ByteArray"",""offset"":8834,""safe"":true},{""name"":""buildGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""},{""name"":""globalRoot"",""type"":""Hash256""},{""name"":""constituentCommitmentsRoot"",""type"":""Hash256""},{""name"":""constituentCount"",""type"":""Integer""},{""name"":""aggregationBackendId"",""type"":""Integer""},{""name"":""proofSystem"",""type"":""Integer""},{""name"":""verificationKeyId"",""type"":""Hash256""},{""name"":""replayDomain"",""type"":""Hash256""}],""returntype"":""Hash256"",""offset"":5366,""safe"":true},{""name"":""getGlobalRootVerifier"",""parameters"":[],""returntype"":""Hash160"",""offset"":7063,""safe"":true},{""name"":""getGlobalRootProofSystem"",""parameters"":[],""returntype"":""Integer"",""offset"":6893,""safe"":true},{""name"":""getGlobalRootAggregationBackend"",""parameters"":[],""returntype"":""Integer"",""offset"":6863,""safe"":true},{""name"":""getGlobalRootVerificationKeyId"",""parameters"":[],""returntype"":""Hash256"",""offset"":6923,""safe"":true},{""name"":""getGlobalRootReplayDomain"",""parameters"":[],""returntype"":""Hash256"",""offset"":6993,""safe"":true},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":8011,""safe"":true},{""name"":""isGlobalRootGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":6848,""safe"":true},{""name"":""getGlobalRootProofInputHash"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9873,""safe"":true},{""name"":""getGlobalRoot"",""parameters"":[{""name"":""batchEpoch"",""type"":""Integer""}],""returntype"":""Hash256"",""offset"":9944,""safe"":true},{""name"":""consumeL2ToL1"",""parameters"":[{""name"":""sourceChainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""messageHash"",""type"":""Hash256""},{""name"":""siblings"",""type"":""Array""},{""name"":""leafIndex"",""type"":""Integer""}],""returntype"":""Void"",""offset"":10015,""safe"":false},{""name"":""isConsumed"",""parameters"":[{""name"":""messageHash"",""type"":""Hash256""}],""returntype"":""Boolean"",""offset"":11238,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":11257,""safe"":false}],""events"":[{""name"":""L1ToL2Enqueued"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""}]},{""name"":""L2ToL1Consumed"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""GlobalRootPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""}]},{""name"":""L1TxFilterSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""MessageRootsPublished"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GlobalRootVerifierSet"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash256""},{""name"":""arg5"",""type"":""Hash256""}]},{""name"":""GlobalRootProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash256""},{""name"":""arg3"",""type"":""Hash256""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GlobalRootGovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Cross-chain message queue \u002B message-root registry for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MessageRouter"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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BCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXpKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFeiCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ABipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ACCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ACipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ADCpShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV6ADipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV72zBzEHQibmtszkppamyeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkkGoAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFfNswdBB1Im5sbc5KaWptnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtABS1JJBqABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRX1KaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFfsp1bUoQLgQiCEoB/wAyBgH/AJFKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFbRipShAuBCIISgH/ADIGAf8AkUppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVtIKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRW0AGKlKEC4EIghKAf8AMgYB/wCRSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRRB2Im5+bs5KaWpunkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVuSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdkVubbUkkWkiAkDbMEBXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAVwACeXgSNANAVwEDHYhweEpoEFHQRXlKEC4EIghKAf8AMgYB/wCRSmgRUdBFeRipShAuBCIISgH/ADIGAf8AkUpoElHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXpKEC4EIghKAf8AMgYB/wCRSmgVUdBFehipShAuBCIISgH/ADIGAf8AkUpoFlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgZUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV6ADCpShAuBCIISgH/ADIGAf8AkUpoG1HQRXoAOKlKEC4EIghKAf8A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QZUBb2FpengTwAwXR2xvYmFsUm9vdFByb29mQWNjZXB0ZWRBlQFvYQgiAkBXAAF4EJgkBQkiB3gB/gCYJAUJIgd4Af8AmCICQFcECAGqAIhwWHEQciI+aWrOSmhqUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFamnKtSTAQdv+qHTbMHIQcyJuamvOSmgYa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAUtSSQfwcAHGg0XXgAPGg11QAAAHkARGg0TnoAZGg0SHsBhABoNd4CAAB8SmgBiABR0EV9SmgBiQBR0EV+AYoAaDQlaNsoNwAAc2s3AADbMNsoStgkCUrKACAoAzoiAkBB2/6odEBXAgN62zBwEHEibmhpzkp4eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkkEBXAAN6ShAuBCIISgH/ADIGAf8AkUp4eVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6IKlKEC4EIghKAf8AMgYB/wCRSnh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6ACCpShAuBCIISgH/ADIGAf8AkUp4eRSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRXoAKKlKEC4EIghKAf8AMgYB/wCRSnh5FZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAw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SgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnwAgnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8AIJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHEQchBzIm9oa85KaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa2jKtSSPQdv+qHTbMHMQdCJva2zOSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90RWwAFLUkj3jbMHQQdSJvbG3OSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AFLUkj3lKaWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yUdBFekppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EV72zB1EHYib21uzkppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVuSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdkVuACC1JI982zB2EHcHInJubwfOSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRW8HSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwdFbwcAILUki2kiAkBXAQF4NRb0//81TNr//3BoC5cmJgwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAICgDOiICQFcBAXg18/L//zUF2v//cGgLlyYmDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAgKAM6IgJAVwQFeBC3JCcMInNvdXJjZUNoYWluSWQgMCBpcyByZXNlcnZlZCBmb3IgTDHgegwgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAACXqiQiDB1tZXNzYWdlIGhhc2ggbXVzdCBiZSBub24temVyb+B7C5gkFgwRc2libGluZ3MgcmVxdWlyZWTge3BoygBAtiQTDA5wcm9vZiB0b28gZGVlcOAMAf3bMDUb2f//StgmDkUMCHNtIHVuc2V0OkrYJAlKygAUKAM6cXl4EsAVDBRnZXRMMlRvTDFNZXNzYWdlUm9vdGlBYn1bUnJqDCAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAJeqJDcMMmJhdGNoIGlzIG5vdCBmaW5hbGl6ZWQgb3IgaGFzIG5vIEwyLXRvLUwxIG1lc3NhZ2Vz4Hxoano0biQjDB5pbnZhbGlkIEwyLXRvLUwxIG1lc3NhZ2UgcHJvb2bgejXGAgAAc2s1S9j//wuXJBUMEGFscmVhZHkgY29uc3VtZWTgDAEB2zBrNZrj//96eBLADA5MMlRvTDFDb25zdW1lZEGVAW9hQFcGBHjbMHB7cRByI08CAAB6as5za3RsC5cmHQwYc2libGluZyBtdXN0IGJlIDMyIGJ5dGVzOmvKACCXJB0MGHNpYmxpbmcgbXVzdCBiZSAzMiBieXRlc+AAQIh0aRGREJcnxwAAABB1Ij5obc5KbG1R0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtACC1JMAQdSJva23OSmwAIG2eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkjyPCAAAAEHUiPmttzkpsbVHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW0AILUkwBB1Im9obc5KbAAgbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbQAgtSSPbNsoNwAAdW03AADbMEpwRWkRqUoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnFFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFanrKtSWy/f//aRCXJAUJIhJ5aNsoStgkCUrKACAoAzqXIgJAVwMBACGIcBZKaBBR0EV42zBxEHIibmlqzkpoEWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAILUkkGgiAkBXAAF4NXD///8199T//wuYIgJAVgIMCE5FTzRHV1Iy2zBgDB5uZW80LWdvdjpzZXRHbG9iYWxSb290VmVyaWZpZXLbMGFAmILErA==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delGlobalRootGovernanceLocked();

    [DisplayName("GlobalRootGovernanceLocked")]
    public event delGlobalRootGovernanceLocked? OnGlobalRootGovernanceLocked;

    public delegate void delGlobalRootProofAccepted(BigInteger? arg1, UInt256? arg2, UInt256? arg3);

    [DisplayName("GlobalRootProofAccepted")]
    public event delGlobalRootProofAccepted? OnGlobalRootProofAccepted;

    public delegate void delGlobalRootPublished(BigInteger? arg1, UInt256? arg2);

    [DisplayName("GlobalRootPublished")]
    public event delGlobalRootPublished? OnGlobalRootPublished;

    public delegate void delGlobalRootVerifierSet(UInt160? arg1, BigInteger? arg2, BigInteger? arg3, UInt256? arg4, UInt256? arg5);

    [DisplayName("GlobalRootVerifierSet")]
    public event delGlobalRootVerifierSet? OnGlobalRootVerifierSet;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delL1ToL2Enqueued(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4);

    [DisplayName("L1ToL2Enqueued")]
    public event delL1ToL2Enqueued? OnL1ToL2Enqueued;

    public delegate void delL1TxFilterSet(BigInteger? arg1, UInt160? arg2);

    [DisplayName("L1TxFilterSet")]
    public event delL1TxFilterSet? OnL1TxFilterSet;

    public delegate void delL2ToL1Consumed(BigInteger? arg1, UInt256? arg2);

    [DisplayName("L2ToL1Consumed")]
    public event delL2ToL1Consumed? OnL2ToL1Consumed;

    public delegate void delMessageRootsPublished(BigInteger? arg1, BigInteger? arg2, UInt256? arg3, UInt256? arg4);

    [DisplayName("MessageRootsPublished")]
    public event delMessageRootsPublished? OnMessageRootsPublished;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? GlobalRootAggregationBackend { [DisplayName("getGlobalRootAggregationBackend")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? GlobalRootProofSystem { [DisplayName("getGlobalRootProofSystem")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt256? GlobalRootReplayDomain { [DisplayName("getGlobalRootReplayDomain")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt256? GlobalRootVerificationKeyId { [DisplayName("getGlobalRootVerificationKeyId")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GlobalRootVerifier { [DisplayName("getGlobalRootVerifier")] get; }

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
    public abstract bool? IsGlobalRootGovernanceLocked { [DisplayName("isGlobalRootGovernanceLocked")] get; }

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
    [DisplayName("buildSetGlobalRootVerifierAction")]
    public abstract byte[]? BuildSetGlobalRootVerifierAction(UInt160? verifier, BigInteger? proofSystem, BigInteger? aggregationBackendId, UInt256? verificationKeyId, UInt256? replayDomain);

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
    [DisplayName("getL1ToL2")]
    public abstract byte[]? GetL1ToL2(BigInteger? chainId, BigInteger? nonce);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL1TxFilter")]
    public abstract UInt160? GetL1TxFilter(BigInteger? targetChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL1Root")]
    public abstract UInt256? GetL2ToL1Root(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getL2ToL2Root")]
    public abstract UInt256? GetL2ToL2Root(BigInteger? chainId, BigInteger? batchNumber);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isConsumed")]
    public abstract bool? IsConsumed(UInt256? messageHash);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("clearL1TxFilter")]
    public abstract void ClearL1TxFilter(BigInteger? targetChainId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("consumeL2ToL1")]
    public abstract void ConsumeL2ToL1(BigInteger? sourceChainId, BigInteger? batchNumber, UInt256? messageHash, IList<object>? siblings, BigInteger? leafIndex);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("enqueueL1ToL2")]
    public abstract BigInteger? EnqueueL1ToL2(BigInteger? targetChainId, UInt160? receiver, BigInteger? messageType, byte[]? payload);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGlobalRootGovernance")]
    public abstract void LockGlobalRootGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishGlobalRoot")]
    public abstract bool? PublishGlobalRoot(BigInteger? batchEpoch, UInt256? globalRoot, UInt256? constituentCommitmentsRoot, BigInteger? constituentCount, BigInteger? aggregationBackendId, BigInteger? proofSystem, UInt256? verificationKeyId, UInt256? replayDomain, byte[]? aggregatedProof);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("publishMessageRoots")]
    public abstract void PublishMessageRoots(BigInteger? chainId, BigInteger? batchNumber, UInt256? l2ToL1Root, UInt256? l2ToL2Root);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setGlobalRootVerifier")]
    public abstract void SetGlobalRootVerifier(UInt160? verifier, BigInteger? proofSystem, BigInteger? aggregationBackendId, UInt256? verificationKeyId, UInt256? replayDomain);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setGlobalRootVerifierViaProposal")]
    public abstract void SetGlobalRootVerifierViaProposal(UInt160? verifier, BigInteger? proofSystem, BigInteger? aggregationBackendId, UInt256? verificationKeyId, UInt256? replayDomain, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setL1TxFilter")]
    public abstract void SetL1TxFilter(BigInteger? targetChainId, UInt160? filter);

    #endregion
}
