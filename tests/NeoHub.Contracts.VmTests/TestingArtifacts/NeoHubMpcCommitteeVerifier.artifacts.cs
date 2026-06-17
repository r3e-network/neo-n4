using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubMpcCommitteeVerifier(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MpcCommitteeVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":463,""safe"":true},{""name"":""registerCommittee"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":521,""safe"":false},{""name"":""registerCommitteeWithMembers"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1656,""safe"":false},{""name"":""registerCommitteeWithMembersViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2273,""safe"":false},{""name"":""registerCommitteeViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4587,""safe"":false},{""name"":""buildRegisterCommitteeAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":5241,""safe"":true},{""name"":""buildRegisterCommitteeWithMembersAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":2878,""safe"":true},{""name"":""getCommittee"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6046,""safe"":true},{""name"":""getCommitteeHeader"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6079,""safe"":true},{""name"":""getSignerMember"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""signerIdx"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":6141,""safe"":true},{""name"":""verifyInboundMessage"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":6201,""safe"":true},{""name"":""bridgeKind"",""parameters"":[],""returntype"":""Integer"",""offset"":8832,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":8834,""safe"":false}],""events"":[{""name"":""CommitteeRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""M-of-N committee verifier for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MpcCommitteeVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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BgH/AJFKaRZR0EV9ADCpShAuBCIISgH/ADIGAf8AkUppF1HQRX0AOKlKEC4EIghKAf8AMgYB/wCRSmkYUdBFaTVy9v//C5ckHgwZcHJvcG9zYWwgYWxyZWFkeSBjb25zdW1lZOB9EcAVDBdpc0FwcHJvdmVkQW5kVGltZWxvY2tlZGhBYn1bUnJqJCcMInByb3Bvc2FsIG5vdCBhcHByb3ZlZCArIHRpbWVsb2NrZWTgfHt6eXg1mgAAAHNrfRLAFQwWbWF0Y2hlc1Byb3Bvc2FsUGF5bG9hZGhBYn1bUnRsJFMMTnByb3Bvc2FsIHBheWxvYWQgZG9lcyBub3QgbWF0Y2ggYWN0aW9uIGFyZ3MgKGNvdW5jaWwgdm90ZWQgb24gZGlmZmVyZW50IGJ5dGVzKeAMAQHbMGk1xvr//3x7enl4NW77//9AQWJ9W1JAVwYFWHB7ynF8ynJoyhSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2meSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2qeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hzEHQQdSJvaG3OSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW1oyrUkj3hKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFeCCpShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EV4ABipShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EV5SmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRXpKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFaUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFaRipShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVpIKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRWkAGKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRRB1Im97bc5Ka2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbWm1JJBqShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVqGKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRWogqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFagAYqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFEHUib3xtzkprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtarUkkGsiAkBXBQU14e///3BoDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkVgxRZ292ZXJuYW5jZSBjb250cm9sbGVyIG5vdCB3aXJlZCDigJQgb3duZXIgbXVzdCBjYWxsIFNldEdvdmVybmFuY2VDb250cm9sbGVyIGZpcnN04BmIcRRKaRBR0EV8ShAuBCIISgH/ADIGAf8AkUppEVHQRXwYqUoQLgQiCEoB/wAyBgH/AJFKaRJR0EV8IKlKEC4EIghKAf8AMgYB/wCRSmkTUdBFfAAYqUoQLgQiCEoB/wAyBgH/AJFKaRRR0EV8ACCpShAuBCIISgH/ADIGAf8AkUppFVHQRXwAKKlKEC4EIghKAf8AMgYB/wCRSmkWUdBFfAAwqUoQLgQiCEoB/wAyBgH/AJFKaRdR0EV8ADipShAuBCIISgH/ADIGAf8AkUppGFHQRWk1aO3//wuXJB4MGXByb3Bvc2FsIGFscmVhZHkgY29uc3VtZWTgfBHAFQwXaXNBcHByb3ZlZEFuZFRpbWVsb2NrZWRoQWJ9W1JyaiQnDCJwcm9wb3NhbCBub3QgYXBwcm92ZWQgKyB0aW1lbG9ja2Vk4Ht6eXg1zAAAAHNrfBLAFQwWbWF0Y2hlc1Byb3Bvc2FsUGF5bG9hZGhBYn1bUnRsJYwAAAAMhHByb3Bvc2FsIHBheWxvYWQgZG9lcyBub3QgbWF0Y2ggKGV4dGVybmFsQ2hhaW5JZCwgdGhyZXNob2xkLCBjdXJ2ZVRhZywgY29tbWl0dGVlQmxvYikgYWN0aW9uIGFyZ3MgKGNvdW5jaWwgdm90ZWQgb24gZGlmZmVyZW50IGJ5dGVzKeAMAQHbMGk1hPH//3t6eXg1ve3//0BXBQRZcHvKcWjKFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHIQcyI+aGvOSmprUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa2jKtSTAaMpzeEoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFeBipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXlKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFekpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EUQdCJue2zOSmprbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbGm1JJFqIgJAVwEBeDVi7v//Nfno//9waAuXJgYQiCIFaNswIgJA2zBAVwIBeDVB7v//Ndjo//9waAuXJgUIIgZoyhO1JgYQiCIcaNswcROIShBpEM7QShFpEc7QShJpEs7QIgJAykBXAQJ5eDVx7f//NZno//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXFQN5ygBmuCRADDttZXNzYWdlQnl0ZXMgdG9vIHNob3J0IGZvciBFeHRlcm5hbENyb3NzQ2hhaW5NZXNzYWdlIGxheW91dOB6yhK4JBkMFHByb29mQnl0ZXMgdG9vIHNob3J04HkQznkRzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ5Es4gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeRPOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJwaHiXJEIMPWV4dGVybmFsQ2hhaW5JZCBhcmd1bWVudCBkb2VzIG5vdCBtYXRjaCBzaWduZWQgbWVzc2FnZSBkb21haW7geSDOcWkSlyQnDCJkaXJlY3Rpb24gbXVzdCBiZSAyIChGb3JlaWduVG9OZW8p4HkAOc55ADrOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5ADvOIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5ADzOABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA9zgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAPs4AKKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AD/OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQBAzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknJqEJcmBQgiDUG3w4gDAegDoWq2JCQMH2V4dGVybmFsIGJyaWRnZSBtZXNzYWdlIGV4cGlyZWTgeDXh6v//NXjl//9zawuYJAUJIgZryhO4JDAMK25vIGNvbW1pdHRlZSByZWdpc3RlcmVkIGZvciBleHRlcm5hbENoYWluSWTga9swdGwQznVsEc52bBLOdwdtELckHwwadGhyZXNob2xkIG11c3QgYmUgcG9zaXRpdmXgbW62JCUMIHRocmVzaG9sZCBleGNlZWRzIGNvbW1pdHRlZSBzaXpl4G8HEZcmBgAhIgQAIHcIbMoTbm8IoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5ckIwweY29tbWl0dGVlIGJsb2IgbGVuZ3RoIG1pc21hdGNo4HoQznoRzhioSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5J3CW8JbbgkJAwfc2lnbmF0dXJlIGNvdW50IGJlbG93IHRocmVzaG9sZOBvCABAnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CnrKEm8JbwqgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQ6DDVwcm9vZkJ5dGVzIGxlbmd0aCBpbmNvbnNpc3RlbnQgd2l0aCBkZWNsYXJlZCBzaWdDb3VudOAYiHcLEHcMEHcNI1UDAAASbw1vCqBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93Dm8IiHcPEHcQInR6bw5vEJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpvD28QUdBFbxBKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93EEVvEG8ItSSJAECIdxAQdxEjqAAAAHpvDm8InkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9vEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpvEG8RUdBFbxFKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93EUVvEQBAtSVY////bw9vCG5sNfEBAAB3EW8RELgkJQwgc2lnbmF0dXJlIGZyb20gbm9uLWNvbW1pdHRlZSBrZXngbxEYSg8qC0sCAAAAgCoDOqF3EhFvERiiqEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KEC4EIghKAf8AMgYB/wCRdxNvC28Szm8TkRCXJBUMEGR1cGxpY2F0ZSBzaWduZXLgbwtvEs5vE5JKEC4EIghKAf8AMgYB/wCRSm8LbxJR0EVvBxGXJiMAFm8Q2yhvD9soStgkCUrKACEoAzp52yg3AABKdxRFIi1vBxKXJBUMEHVua25vd24gY3VydmVUYWfgbxDbKG8P2yh52yg3AQBKdxRFbxQkIgwdc2lnbmF0dXJlIHZlcmlmaWNhdGlvbiBmYWlsZWTgbwxKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93DEVvDUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cNRW8Nbwm1Jav8//9vDG24JDEMLHZhbGlkIHNpZ25hdHVyZXMgYmVsb3cgdGhyZXNob2xkIGFmdGVyIGRlZHVw4AgiAkBBt8OIA0BXBAQQcCMdAQAAE2h6oEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3EIchBzInR4aWueSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn857a86YJggJSnJFIjprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrerUki2omBWgiQGhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9wRWh5tSXl/v//DyICQDcAAEDbKEDbKErYJAlKygAhKAM6QDcBAEARQFYCDCVuZW80LWdvdjpyZWdpc3RlckNvbW1pdHRlZVdpdGhNZW1iZXJz2zBgDBpuZW80LWdvdjpyZWdpc3RlckNvbW1pdHRlZdswYUDsOKdU").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delCommitteeRegistered(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, BigInteger? arg4);

    [DisplayName("CommitteeRegistered")]
    public event delCommitteeRegistered? OnCommitteeRegistered;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? BridgeKind { [DisplayName("bridgeKind")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRegisterCommitteeAction")]
    public abstract byte[]? BuildRegisterCommitteeAction(BigInteger? externalChainId, BigInteger? threshold, BigInteger? curveTag, byte[]? committeeBlob);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildRegisterCommitteeWithMembersAction")]
    public abstract byte[]? BuildRegisterCommitteeWithMembersAction(BigInteger? externalChainId, BigInteger? threshold, BigInteger? curveTag, byte[]? committeeBlob, byte[]? memberBlob);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getCommittee")]
    public abstract byte[]? GetCommittee(BigInteger? externalChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getCommitteeHeader")]
    public abstract byte[]? GetCommitteeHeader(BigInteger? externalChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getSignerMember")]
    public abstract UInt160? GetSignerMember(BigInteger? externalChainId, BigInteger? signerIdx);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyInboundMessage")]
    public abstract bool? VerifyInboundMessage(BigInteger? externalChainId, byte[]? messageBytes, byte[]? proofBytes);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerCommittee")]
    public abstract void RegisterCommittee(BigInteger? externalChainId, BigInteger? threshold, BigInteger? curveTag, byte[]? committeeBlob);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerCommitteeViaProposal")]
    public abstract void RegisterCommitteeViaProposal(BigInteger? externalChainId, BigInteger? threshold, BigInteger? curveTag, byte[]? committeeBlob, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerCommitteeWithMembers")]
    public abstract void RegisterCommitteeWithMembers(BigInteger? externalChainId, BigInteger? threshold, BigInteger? curveTag, byte[]? committeeBlob, byte[]? memberBlob);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("registerCommitteeWithMembersViaProposal")]
    public abstract void RegisterCommitteeWithMembersViaProposal(BigInteger? externalChainId, BigInteger? threshold, BigInteger? curveTag, byte[]? committeeBlob, byte[]? memberBlob, BigInteger? proposalId);

    #endregion
}
