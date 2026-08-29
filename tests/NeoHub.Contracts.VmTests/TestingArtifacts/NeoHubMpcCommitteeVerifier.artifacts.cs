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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MpcCommitteeVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":540,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":598,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":525,""safe"":true},{""name"":""registerCommittee"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":816,""safe"":false},{""name"":""registerCommitteeWithMembers"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":2009,""safe"":false},{""name"":""registerCommitteeWithMembersViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2717,""safe"":false},{""name"":""registerCommitteeViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5031,""safe"":false},{""name"":""buildRegisterCommitteeAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":5685,""safe"":true},{""name"":""buildRegisterCommitteeWithMembersAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":3322,""safe"":true},{""name"":""getCommittee"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6490,""safe"":true},{""name"":""getCommitteeHeader"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6523,""safe"":true},{""name"":""getSignerMember"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""signerIdx"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":6585,""safe"":true},{""name"":""verifyInboundMessage"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":6645,""safe"":true},{""name"":""bridgeKind"",""parameters"":[],""returntype"":""Integer"",""offset"":9263,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":9265,""safe"":false}],""events"":[{""name"":""CommitteeRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""M-of-N committee verifier for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MpcCommitteeVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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CkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2meSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn2qeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hzEHQQdSJvaG3OSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW1oyrUkj3hKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFeCCpShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EV4ABipShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EV5SmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRXpKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFaUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFaRipShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVpIKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRWkAGKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRRB1Im97bc5Ka2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFbUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbWm1JJBqShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVqGKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRWogqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFagAYqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFEHUib3xtzkprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtarUkkGsiAkBXBQU1cu7//3BoDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkVgxRZ292ZXJuYW5jZSBjb250cm9sbGVyIG5vdCB3aXJlZCDigJQgb3duZXIgbXVzdCBjYWxsIFNldEdvdmVybmFuY2VDb250cm9sbGVyIGZpcnN04BmIcRRKaRBR0EV8ShAuBCIISgH/ADIGAf8AkUppEVHQRXwYqUoQLgQiCEoB/wAyBgH/AJFKaRJR0EV8IKlKEC4EIghKAf8AMgYB/wCRSmkTUdBFfAAYqUoQLgQiCEoB/wAyBgH/AJFKaRRR0EV8ACCpShAuBCIISgH/ADIGAf8AkUppFVHQRXwAKKlKEC4EIghKAf8AMgYB/wCRSmkWUdBFfAAwqUoQLgQiCEoB/wAyBgH/AJFKaRdR0EV8ADipShAuBCIISgH/ADIGAf8AkUppGFHQRWk1rOv//wuXJB4MGXByb3Bvc2FsIGFscmVhZHkgY29uc3VtZWTgfBHAFQwXaXNBcHByb3ZlZEFuZFRpbWVsb2NrZWRoQWJ9W1JyaiQnDCJwcm9wb3NhbCBub3QgYXBwcm92ZWQgKyB0aW1lbG9ja2Vk4Ht6eXg1zAAAAHNrfBLAFQwWbWF0Y2hlc1Byb3Bvc2FsUGF5bG9hZGhBYn1bUnRsJYwAAAAMhHByb3Bvc2FsIHBheWxvYWQgZG9lcyBub3QgbWF0Y2ggKGV4dGVybmFsQ2hhaW5JZCwgdGhyZXNob2xkLCBjdXJ2ZVRhZywgY29tbWl0dGVlQmxvYikgYWN0aW9uIGFyZ3MgKGNvdW5jaWwgdm90ZWQgb24gZGlmZmVyZW50IGJ5dGVzKeAMAQHbMGk19Oz//3t6eXg1hu3//0BXBQRZcHvKcWjKFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfaZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfiHIQcyI+aGvOSmprUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa2jKtSTAaMpzeEoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFeBipShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXlKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFekpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EUQdCJue2zOSmprbJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RFbGm1JJFqIgJAVwEBeDUH7v//NT3n//9waAuXJgYQiCIFaNswIgJA2zBAVwIBeDXm7f//NRzn//9waAuXJgUIIgZoyhO1JgYQiCIcaNswcROIShBpEM7QShFpEc7QShJpEs7QIgJAykBXAQJ5eDUs7f//Nd3m//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIg5oStgkCUrKABQoAzoiAkBXFQN5ygBmuCRADDttZXNzYWdlQnl0ZXMgdG9vIHNob3J0IGZvciBFeHRlcm5hbENyb3NzQ2hhaW5NZXNzYWdlIGxheW91dOB6yhK4JBkMFHByb29mQnl0ZXMgdG9vIHNob3J04HkQznkRzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ5Es4gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeRPOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJwaHiXJEIMPWV4dGVybmFsQ2hhaW5JZCBhcmd1bWVudCBkb2VzIG5vdCBtYXRjaCBzaWduZWQgbWVzc2FnZSBkb21haW7geSDOcWkSlyQnDCJkaXJlY3Rpb24gbXVzdCBiZSAyIChGb3JlaWduVG9OZW8p4HkAOc55ADrOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5ADvOIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5ADzOABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA9zgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAPs4AKKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AD/OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQBAzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknJqEJcmBQgiDUG3w4gDAegDoWq2JCQMH2V4dGVybmFsIGJyaWRnZSBtZXNzYWdlIGV4cGlyZWTgeDWG6v//Nbzj//9zawuYJAUJIgZryhO4JDAMK25vIGNvbW1pdHRlZSByZWdpc3RlcmVkIGZvciBleHRlcm5hbENoYWluSWTga9swdGwQznVsEc52bBLOdwdtELckHwwadGhyZXNob2xkIG11c3QgYmUgcG9zaXRpdmXgbW62JCUMIHRocmVzaG9sZCBleGNlZWRzIGNvbW1pdHRlZSBzaXpl4G8HEZcmBgAhIgQAIHcIbMoTbm8IoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5ckIwweY29tbWl0dGVlIGJsb2IgbGVuZ3RoIG1pc21hdGNo4HoQznoRzhioSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5J3CW8JbbgkJAwfc2lnbmF0dXJlIGNvdW50IGJlbG93IHRocmVzaG9sZOBvCABAnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CnrKEm8JbwqgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQ6DDVwcm9vZkJ5dGVzIGxlbmd0aCBpbmNvbnNpc3RlbnQgd2l0aCBkZWNsYXJlZCBzaWdDb3VudOAYiHcLEHcMEHcNI0gDAAASbw1vCqBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93Dm8IiHcPEHcQInR6bw5vEJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpvD28QUdBFbxBKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93EEVvEG8ItSSJAECIdxAQdxEjqAAAAHpvDm8InkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9vEZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpvEG8RUdBFbxFKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93EUVvEQBAtSVY////bw9vCG5sNeQBAAB3EW8RELgkJQwgc2lnbmF0dXJlIGZyb20gbm9uLWNvbW1pdHRlZSBrZXngbxEYoXcSEW8RGKKoSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0oQLgQiCEoB/wAyBgH/AJF3E28LbxLObxOREJckFQwQZHVwbGljYXRlIHNpZ25lcuBvC28Szm8TkkoQLgQiCEoB/wAyBgH/AJFKbwtvElHQRW8HEZcmIwAWbxDbKG8P2yhK2CQJSsoAISgDOnnbKDcAAEp3FEUiLW8HEpckFQwQdW5rbm93biBjdXJ2ZVRhZ+BvENsobw/bKHnbKDcBAEp3FEVvFCQiDB1zaWduYXR1cmUgdmVyaWZpY2F0aW9uIGZhaWxlZOBvDEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cMRW8NSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdw1Fbw1vCbUluPz//28MbbgkMQwsdmFsaWQgc2lnbmF0dXJlcyBiZWxvdyB0aHJlc2hvbGQgYWZ0ZXIgZGVkdXDgCCICQEG3w4gDQFcEBBBwIx0BAAATaHqgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcQhyEHMidHhpa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzntrzpgmCAlKckUiOmtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWt6tSSLaiYFaCJAaEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3BFaHm1JeX+//8PIgJANwAAQNsoQNsoStgkCUrKACEoAzpANwEAQBFAVgIMJW5lbzQtZ292OnJlZ2lzdGVyQ29tbWl0dGVlV2l0aE1lbWJlcnPbMGAMGm5lbzQtZ292OnJlZ2lzdGVyQ29tbWl0dGVl2zBhQG8Buas=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delCommitteeRegistered(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, BigInteger? arg4);

    [DisplayName("CommitteeRegistered")]
    public event delCommitteeRegistered? OnCommitteeRegistered;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

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

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

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
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance();

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
