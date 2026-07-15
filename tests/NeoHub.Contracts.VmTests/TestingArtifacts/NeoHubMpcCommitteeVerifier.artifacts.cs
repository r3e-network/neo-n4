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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MpcCommitteeVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":105,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":204,""safe"":false},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":325,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":463,""safe"":true},{""name"":""registerCommittee"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":521,""safe"":false},{""name"":""registerCommitteeWithMembers"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":1642,""safe"":false},{""name"":""registerCommitteeWithMembersViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":2245,""safe"":false},{""name"":""registerCommitteeViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4559,""safe"":false},{""name"":""buildRegisterCommitteeAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":5213,""safe"":true},{""name"":""buildRegisterCommitteeWithMembersAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""threshold"",""type"":""Integer""},{""name"":""curveTag"",""type"":""Integer""},{""name"":""committeeBlob"",""type"":""ByteArray""},{""name"":""memberBlob"",""type"":""ByteArray""}],""returntype"":""ByteArray"",""offset"":2850,""safe"":true},{""name"":""getCommittee"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6018,""safe"":true},{""name"":""getCommitteeHeader"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":6051,""safe"":true},{""name"":""getSignerMember"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""signerIdx"",""type"":""Integer""}],""returntype"":""Hash160"",""offset"":6113,""safe"":true},{""name"":""verifyInboundMessage"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":6173,""safe"":true},{""name"":""bridgeKind"",""parameters"":[],""returntype"":""Integer"",""offset"":8791,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":8793,""safe"":false}],""events"":[{""name"":""CommitteeRegistered"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Integer""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""M-of-N committee verifier for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MpcCommitteeVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AAAAkUoC////fzIMAwAAAAABAAAAn3RR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRXlKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFekprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVpShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVpGKlKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRWkgqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFaQAYqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFEHUib3ttzkprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtabUkkGpKEC4EIghKAf8AMgYB/wCRSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRWoYqUoQLgQiCEoB/wAyBgH/AJFKa2xKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ90UdBFaiCpShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EVqABipShAuBCIISgH/ADIGAf8AkUprbEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3RR0EUQdSJvfG3OSmtsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdFHQRW1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91RW1qtSSQayICQFcFBTX97///cGgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAmCRWDFFnb3Zlcm5hbmNlIGNvbnRyb2xsZXIgbm90IHdpcmVkIOKAlCBvd25lciBtdXN0IGNhbGwgU2V0R292ZXJuYW5jZUNvbnRyb2xsZXIgZmlyc3TgGYhxFEppEFHQRXxKEC4EIghKAf8AMgYB/wCRSmkRUdBFfBipShAuBCIISgH/ADIGAf8AkUppElHQRXwgqUoQLgQiCEoB/wAyBgH/AJFKaRNR0EV8ABipShAuBCIISgH/ADIGAf8AkUppFFHQRXwAIKlKEC4EIghKAf8AMgYB/wCRSmkVUdBFfAAoqUoQLgQiCEoB/wAyBgH/AJFKaRZR0EV8ADCpShAuBCIISgH/ADIGAf8AkUppF1HQRXwAOKlKEC4EIghKAf8AMgYB/wCRSmkYUdBFaTWE7f//C5ckHgwZcHJvcG9zYWwgYWxyZWFkeSBjb25zdW1lZOB8EcAVDBdpc0FwcHJvdmVkQW5kVGltZWxvY2tlZGhBYn1bUnJqJCcMInByb3Bvc2FsIG5vdCBhcHByb3ZlZCArIHRpbWVsb2NrZWTge3p5eDXMAAAAc2t8EsAVDBZtYXRjaGVzUHJvcG9zYWxQYXlsb2FkaEFifVtSdGwljAAAAAyEcHJvcG9zYWwgcGF5bG9hZCBkb2VzIG5vdCBtYXRjaCAoZXh0ZXJuYWxDaGFpbklkLCB0aHJlc2hvbGQsIGN1cnZlVGFnLCBjb21taXR0ZWVCbG9iKSBhY3Rpb24gYXJncyAoY291bmNpbCB2b3RlZCBvbiBkaWZmZXJlbnQgYnl0ZXMp4AwBAdswaTWS8f//e3p5eDXZ7f//QFcFBFlwe8pxaMoUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8RnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8RnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9pnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IchBzIj5oa85KamtR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VraMq1JMBoynN4ShAuBCIISgH/ADIGAf8AkUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRXggqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKamtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zUdBFeUpqa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NR0EV6SmprSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc1HQRRB0Im57bM5KamtsnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVsSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdEVsabUkkWoiAkBXAQF4NXDu//81Fen//3BoC5cmBhCIIgVo2zAiAkDbMEBXAgF4NU/u//819Oj//3BoC5cmBQgiBmjKE7UmBhCIIhxo2zBxE4hKEGkQztBKEWkRztBKEmkSztAiAkDKQFcBAnl4NX/t//81tej//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcVA3nKAGa4JEAMO21lc3NhZ2VCeXRlcyB0b28gc2hvcnQgZm9yIEV4dGVybmFsQ3Jvc3NDaGFpbk1lc3NhZ2UgbGF5b3V04HrKErgkGQwUcHJvb2ZCeXRlcyB0b28gc2hvcnTgeRDOeRHOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknkSziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ5E84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknBoeJckQgw9ZXh0ZXJuYWxDaGFpbklkIGFyZ3VtZW50IGRvZXMgbm90IG1hdGNoIHNpZ25lZCBtZXNzYWdlIGRvbWFpbuB5IM5xaRKXJCcMImRpcmVjdGlvbiBtdXN0IGJlIDIgKEZvcmVpZ25Ub05lbyngeQA5znkAOs4YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAO84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAPM4AGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AD3OACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA+zgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAP84AMKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AEDOADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGScmoQlyYFCCINQbfDiAMB6AOharYkJAwfZXh0ZXJuYWwgYnJpZGdlIG1lc3NhZ2UgZXhwaXJlZOB4Ne/q//81lOX//3NrC5gkBQkiBmvKE7gkMAwrbm8gY29tbWl0dGVlIHJlZ2lzdGVyZWQgZm9yIGV4dGVybmFsQ2hhaW5JZOBr2zB0bBDOdWwRznZsEs53B20QtyQfDBp0aHJlc2hvbGQgbXVzdCBiZSBwb3NpdGl2ZeBtbrYkJQwgdGhyZXNob2xkIGV4Y2VlZHMgY29tbWl0dGVlIHNpemXgbwcRlyYGACEiBAAgdwhsyhNubwigSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQjDB5jb21taXR0ZWUgYmxvYiBsZW5ndGggbWlzbWF0Y2jgehDOehHOGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfkncJbwltuCQkDB9zaWduYXR1cmUgY291bnQgYmVsb3cgdGhyZXNob2xk4G8IAECeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKesoSbwlvCqBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+XJDoMNXByb29mQnl0ZXMgbGVuZ3RoIGluY29uc2lzdGVudCB3aXRoIGRlY2xhcmVkIHNpZ0NvdW504BiIdwsQdwwQdw0jSAMAABJvDW8KoEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cObwiIdw8QdxAidHpvDm8QnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSm8PbxBR0EVvEEqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cQRW8Qbwi1JIkAQIh3EBB3ESOoAAAAem8ObwieSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn28RnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSm8QbxFR0EVvEUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cRRW8RAEC1JVj///9vD28Ibmw15AEAAHcRbxEQuCQlDCBzaWduYXR1cmUgZnJvbSBub24tY29tbWl0dGVlIGtleeBvERihdxIRbxEYoqhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfShAuBCIISgH/ADIGAf8AkXcTbwtvEs5vE5EQlyQVDBBkdXBsaWNhdGUgc2lnbmVy4G8LbxLObxOSShAuBCIISgH/ADIGAf8AkUpvC28SUdBFbwcRlyYjABZvENsobw/bKErYJAlKygAhKAM6edsoNwAASncURSItbwcSlyQVDBB1bmtub3duIGN1cnZlVGFn4G8Q2yhvD9soedsoNwEASncURW8UJCIMHXNpZ25hdHVyZSB2ZXJpZmljYXRpb24gZmFpbGVk4G8MSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdwxFbw1KnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93DUVvDW8JtSW4/P//bwxtuCQxDCx2YWxpZCBzaWduYXR1cmVzIGJlbG93IHRocmVzaG9sZCBhZnRlciBkZWR1cOAIIgJAQbfDiANAVwQEEHAjHQEAABNoeqBKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xCHIQcyJ0eGlrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/Oe2vOmCYICUpyRSI6a0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFa3q1JItqJgVoIkBoSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcEVoebUl5f7//w8iAkA3AABA2yhA2yhK2CQJSsoAISgDOkA3AQBAEUBWAgwlbmVvNC1nb3Y6cmVnaXN0ZXJDb21taXR0ZWVXaXRoTWVtYmVyc9swYAwabmVvNC1nb3Y6cmVnaXN0ZXJDb21taXR0ZWXbMGFA9hRpMQ==").AsSerializable<Neo.SmartContract.NefFile>();

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
