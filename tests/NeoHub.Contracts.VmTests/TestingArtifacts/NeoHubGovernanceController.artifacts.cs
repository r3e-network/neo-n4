using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubGovernanceController(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.GovernanceController"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":661,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":760,""safe"":false},{""name"":""isCouncilMember"",""parameters"":[{""name"":""memberKey"",""type"":""PublicKey""}],""returntype"":""Boolean"",""offset"":881,""safe"":true},{""name"":""getCouncilCount"",""parameters"":[],""returntype"":""Integer"",""offset"":1028,""safe"":true},{""name"":""getThreshold"",""parameters"":[],""returntype"":""Integer"",""offset"":1090,""safe"":true},{""name"":""getTimelockSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":1141,""safe"":true},{""name"":""getAdmissionMode"",""parameters"":[],""returntype"":""Integer"",""offset"":1192,""safe"":true},{""name"":""setAdmissionMode"",""parameters"":[{""name"":""mode"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1225,""safe"":false},{""name"":""setAdmissionModeViaProposal"",""parameters"":[{""name"":""mode"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1437,""safe"":false},{""name"":""buildSetAdmissionModeAction"",""parameters"":[{""name"":""mode"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":2507,""safe"":true},{""name"":""createProposal"",""parameters"":[{""name"":""signer"",""type"":""PublicKey""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Integer"",""offset"":2644,""safe"":false},{""name"":""approve"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""},{""name"":""memberKey"",""type"":""PublicKey""}],""returntype"":""Integer"",""offset"":2894,""safe"":false},{""name"":""getProposal"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":3564,""safe"":true},{""name"":""getApprovedAt"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2018,""safe"":true},{""name"":""getApprovalCount"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3594,""safe"":true},{""name"":""isApprovedAndTimelocked"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1853,""safe"":true},{""name"":""cancelProposal"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3647,""safe"":false},{""name"":""isVetoed"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":1997,""safe"":true},{""name"":""setUpgradeWindows"",""parameters"":[{""name"":""noticeSeconds"",""type"":""Integer""},{""name"":""executionWindowSeconds"",""type"":""Integer""},{""name"":""cooldownSeconds"",""type"":""Integer""}],""returntype"":""Void"",""offset"":3865,""safe"":false},{""name"":""getUpgradeNoticeSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":4065,""safe"":true},{""name"":""getUpgradeExecutionWindowSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":4120,""safe"":true},{""name"":""getUpgradeCooldownSeconds"",""parameters"":[],""returntype"":""Integer"",""offset"":4175,""safe"":true},{""name"":""getProposalExecutedAt"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":3803,""safe"":true},{""name"":""getProposalStage"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":4230,""safe"":true},{""name"":""isInExecutionWindow"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":4609,""safe"":true},{""name"":""markProposalExecuted"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4623,""safe"":false},{""name"":""approveVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":4777,""safe"":false},{""name"":""revokeVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5037,""safe"":false},{""name"":""isApprovedVerifier"",""parameters"":[{""name"":""verifier"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5168,""safe"":true},{""name"":""approveBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5187,""safe"":false},{""name"":""revokeBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":5447,""safe"":false},{""name"":""isApprovedBridgeAdapter"",""parameters"":[{""name"":""bridge"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5563,""safe"":true},{""name"":""setImmutableFlag"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5582,""safe"":false},{""name"":""setImmutableFlagViaProposal"",""parameters"":[{""name"":""flagId"",""type"":""Integer""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5682,""safe"":false},{""name"":""buildSetImmutableFlagAction"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""ByteArray"",""offset"":5852,""safe"":true},{""name"":""matchesProposalPayload"",""parameters"":[{""name"":""proposalId"",""type"":""Integer""},{""name"":""expectedAction"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":2176,""safe"":true},{""name"":""isImmutable"",""parameters"":[{""name"":""flagId"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":5989,""safe"":true},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":6008,""safe"":false}],""events"":[{""name"":""ProposalCreated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""ByteArray""}]},{""name"":""ProposalApproved"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""PublicKey""}]},{""name"":""ImmutableFlagSet"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""UpgradeWindowsSet"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""ProposalExecuted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""}]},{""name"":""ProposalVetoed"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""AdmissionModeChanged"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]},{""name"":""VerifierApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""VerifierRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""BridgeAdapterApproved"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""BridgeAdapterRevoked"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""Neo Project"",""Description"":""Governance controller for the Neo Elastic Network: council, timelocks, admission policy."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.GovernanceController"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErYzg3NjZlYTg0OTI5YTA3ZWU3ZmIyOTkxYmM3ODgyMzgzYzkuLi4AAAAAAP23F1cJAnkmByMyAgAAeHBoEM5xaBHOcmgSzkoQAwAAAAABAAAAuyQDOnNoE85KEAMAAAAAAQAAALskAzp0aUrZKCQGRQkiBsoAFLMkBQkiBmkQs6okEgwNaW52YWxpZCBvd25lcuBqyhC3JB4MGWNvdW5jaWwgbXVzdCBiZSBub24tZW1wdHngaxC3JB8MGnRocmVzaG9sZCBtdXN0IGJlIHBvc2l0aXZl4GtqyrYkJQwgdGhyZXNob2xkIGV4Y2VlZHMgY29tbWl0dGVlIHNpemXgbBC3JB4MGXRpbWVsb2NrIG11c3QgYmUgcG9zaXRpdmXgaQwB/9swNVIBAAAMAQLbMGrKUDVhAQAAawwBA9swNVYBAABsDAEE2zA1SwEAAGwMAQ/bMDVAAQAAbAwBENswNTUBAABsDAER2zA1KgEAAAwBANswDAEF2zA1MQEAABEMAQjbMDUQAQAAEHUj0gAAAAAiiHYRSm4QUdBFam3O2zB3BxB3CCJzbwdvCM5KbhFvCJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFbwhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93CEVvCAAhtSSKDAEB2zBuNYUAAABtSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtasq1JS////9AStkoJAZFCSIGygAUs0AQs0BXAAJ5eEGb9mfOQeY/GIRAQeY/GIRAQZv2Z85AVwACeXhBm/ZnzkHmPxiEQEHmPxiEQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEDbMEBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBXAQE0mkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVT////cHgMAf/bMDX8/v//eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwMBACKIcBFKaBBR0EV42zBxEHIibmlqzkpoEWqeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWoAIbUkkGg10v7//wuYIgJAVwEADAEC2zA1wP7//3BoC5cmBRAiHGhK2CYGRRAiBNshShADAAAAAAEAAAC7JAM6IgJAStgmBkUQIgTbIUBXAQAMAQPbMDWC/v//cGgLlyYFECIcaErYJgZFECIE2yFKEAMAAAAAAQAAALskAzoiAkBXAQAMAQTbMDVP/v//cGgLlyYFECIcaErYJgZFECIE2yFKEAMAAAAAAQAAALskAzoiAkBXAQAMAQXbMDUc/v//cGgLlyYFECIHaNswEM4iAkDbMEBXAAF4ErYkGwwWaW52YWxpZCBhZG1pc3Npb24gbW9kZeA1q/3//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4Hg0oLYkYwxeaW5zdGFudCBTZXRBZG1pc3Npb25Nb2RlIG1heSBvbmx5IHRpZ2h0ZW4gYWRtaXNzaW9uOyB1c2UgU2V0QWRtaXNzaW9uTW9kZVZpYVByb3Bvc2FsIHRvIGxvb3NlbuARiEoQeNAMAQXbMDUD/f//eBHADBRBZG1pc3Npb25Nb2RlQ2hhbmdlZEGVAW9hQFcBAngStiQbDBZpbnZhbGlkIGFkbWlzc2lvbiBtb2Rl4HkAEzWgAAAAcGg1BP3//wuXJB4MGXByb3Bvc2FsIGFscmVhZHkgY29uc3VtZWTgeTVPAQAAJCcMInByb3Bvc2FsIG5vdCBhcHByb3ZlZCArIHRpbWVsb2NrZWTgeXg1rwMAAFA1AwIAAAwBAdswaDVP/P//EYhKEHjQDAEF2zA1P/z//3gRwAwUQWRtaXNzaW9uTW9kZUNoYW5nZWRBlQFvYUBXAQIZiHB4SmgQUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBFR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFeQAgqUoQLgQiCEoB/wAyBgH/AJFKaBVR0EV5ACipShAuBCIISgH/ADIGAf8AkUpoFlHQRXkAMKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFeQA4qUoQLgQiCEoB/wAyBgH/AJFKaBhR0EVoIgJAVwMBeDWMAAAAJggJI4MAAAB4NZMAAABwaBCXJgUJInE1GP3//3FpAegDoEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRckG3w4gDaGqeShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJG4IgJAVwABeAAUNY7+//819Pr//wuYIgJAVwEBeBw1ev7//zXg+v//cGgLlyYFECIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOiICQEG3w4gDQFcAAnl4NFYkUwxOcHJvcG9zYWwgcGF5bG9hZCBkb2VzIG5vdCBtYXRjaCBhY3Rpb24gYXJncyAoY291bmNpbCB2b3RlZCBvbiBkaWZmZXJlbnQgYnl0ZXMp4EBXAwJ4NGs1Rvr//3BoC5cmBQkiXGjbMHFpynnKmCYFCSJOEHIiQWlqznlqzpgmBQkiPmpKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9yRWppyrUkvQgiAkBXAQEZiHAWSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFeAAgqUoQLgQiCEoB/wAyBgH/AJFKaBVR0EV4ACipShAuBCIISgH/ADIGAf8AkUpoFlHQRXgAMKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFeAA4qUoQLgQiCEoB/wAyBgH/AJFKaBhR0EVoIgJAVwIBWMoRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IcBBxIj5Yac5KaGlR0EVpSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcUVpWMq1JMB4SmhYylHQRWgiAkBXAgJ4NRn5//8kGQwUbm90IGEgY291bmNpbCBtZW1iZXLgeEH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HnKELckGwwWZW1wdHkgcHJvcG9zYWwgcGF5bG9hZOAMAQjbMDUZ+P//cGgLlyYFESIkaErYJgZFECIE2yFKEAQAAAAAAAAAAAEAAAAAAAAAuyQDOnFpEZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkQwBCNswNU73//9pNdH9//95UDVX9///eWkSwAwPUHJvcG9zYWxDcmVhdGVkQZUBb2FpIgJAQfgn7IxAVwECeTUf+P//JBkMFG5vdCBhIGNvdW5jaWwgbWVtYmVy4HlB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NWX9//81Pff//wuYJBUMEHVua25vd24gcHJvcG9zYWzgeXg0T3BoNRv3//8LlyQVDBBhbHJlYWR5IGFwcHJvdmVk4AwBAdswaDWp9v//eXgSwAwQUHJvcG9zYWxBcHByb3ZlZEGVAW9heDVfAQAAIgJAVwMCACqIcBdKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV4ACCpShAuBCIISgH/ADIGAf8AkUpoFVHQRXgAKKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeAAwqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV4ADipShAuBCIISgH/ADIGAf8AkUpoGFHQRXnbMHEQciJuaWrOSmgZap5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JFagAhtSSQaCICQFcGAXgZNQn5//9waDVt9f//cWkLlyYFECIcaUrYJgZFECIE2yFKEAMAAAAAAQAAALskAzpyahGeShAuBCIOSgP/////AAAAADIMA/////8AAAAAkXNraDW89P//NZP2//90bBC3JAUJIgVqbLUkBQkiBWtsuCYgeBw1lfj//3VtNfn0//8LlyYObUG3w4gDUDWD9P//ayICQFcBAXg1//r//zXX9P//cGgLlyYGEIgiBWjbMCICQFcBAXgZNVL4//81uPT//3BoC5cmBRAiHGhK2CYGRRAiBNshShADAAAAAAEAAAC7JAM6IgJAVwEBNVP0//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOB4NY/6//81Z/T//wuYJBUMEHVua25vd24gcHJvcG9zYWzgeDRZEJckHgwZcHJvcG9zYWwgYWxyZWFkeSBleGVjdXRlZOB4ABQ1uvf//3BoNR70//8LlyYlDAEB2zBoNb/z//94EcAMDlByb3Bvc2FsVmV0b2VkQZUBb2FAVwEBeAASNYD3//815vP//3BoC5cmBRAiJGhK2CYGRRAiBNshShAEAAAAAAAAAAABAAAAAAAAALskAzoiAkBXAAM1efP//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgQtyQcDBdub3RpY2UgbXVzdCBiZSBwb3NpdGl2ZeB5ELckJgwhZXhlY3V0aW9uIHdpbmRvdyBtdXN0IGJlIHBvc2l0aXZl4HoQtyQeDBljb29sZG93biBtdXN0IGJlIHBvc2l0aXZl4HgMAQ/bMDW+8v//eQwBENswNbPy//96DAER2zA1qPL//3p5eBPADBFVcGdyYWRlV2luZG93c1NldEGVAW9hQFcBAAwBD9swNePy//9waAuXJgk1gfT//yIcaErYJgZFECIE2yFKEAMAAAAAAQAAALskAzoiAkBXAQAMARDbMDWs8v//cGgLlyYJNUr0//8iHGhK2CYGRRAiBNshShADAAAAAAEAAAC7JAM6IgJAVwEADAER2zA1dfL//3BoC5cmCTUT9P//IhxoStgmBkUQIgTbIUoQAwAAAAABAAAAuyQDOiICQFcEAXg1WPf//3BoEJcmCBAjagEAAGg1Rf///wHoA6BKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZ5KEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkXFBt8OIA2m1JggRI/cAAAB4Ncz9//9yahC3JnVqNTT///8B6AOgShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8A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").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delAdmissionModeChanged(BigInteger? obj);

    [DisplayName("AdmissionModeChanged")]
    public event delAdmissionModeChanged? OnAdmissionModeChanged;

    public delegate void delBridgeAdapterApproved(UInt160? obj);

    [DisplayName("BridgeAdapterApproved")]
    public event delBridgeAdapterApproved? OnBridgeAdapterApproved;

    public delegate void delBridgeAdapterRevoked(UInt160? obj);

    [DisplayName("BridgeAdapterRevoked")]
    public event delBridgeAdapterRevoked? OnBridgeAdapterRevoked;

    public delegate void delImmutableFlagSet(BigInteger? obj);

    [DisplayName("ImmutableFlagSet")]
    public event delImmutableFlagSet? OnImmutableFlagSet;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delProposalApproved(BigInteger? arg1, ECPoint? arg2);

    [DisplayName("ProposalApproved")]
    public event delProposalApproved? OnProposalApproved;

    public delegate void delProposalCreated(BigInteger? arg1, byte[]? arg2);

    [DisplayName("ProposalCreated")]
    public event delProposalCreated? OnProposalCreated;

    public delegate void delProposalExecuted(BigInteger? arg1, BigInteger? arg2);

    [DisplayName("ProposalExecuted")]
    public event delProposalExecuted? OnProposalExecuted;

    public delegate void delProposalVetoed(BigInteger? obj);

    [DisplayName("ProposalVetoed")]
    public event delProposalVetoed? OnProposalVetoed;

    public delegate void delUpgradeWindowsSet(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("UpgradeWindowsSet")]
    public event delUpgradeWindowsSet? OnUpgradeWindowsSet;

    public delegate void delVerifierApproved(UInt160? obj);

    [DisplayName("VerifierApproved")]
    public event delVerifierApproved? OnVerifierApproved;

    public delegate void delVerifierRevoked(UInt160? obj);

    [DisplayName("VerifierRevoked")]
    public event delVerifierRevoked? OnVerifierRevoked;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? AdmissionMode { [DisplayName("getAdmissionMode")] get; [DisplayName("setAdmissionMode")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? CouncilCount { [DisplayName("getCouncilCount")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? Threshold { [DisplayName("getThreshold")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? TimelockSeconds { [DisplayName("getTimelockSeconds")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? UpgradeCooldownSeconds { [DisplayName("getUpgradeCooldownSeconds")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? UpgradeExecutionWindowSeconds { [DisplayName("getUpgradeExecutionWindowSeconds")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? UpgradeNoticeSeconds { [DisplayName("getUpgradeNoticeSeconds")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetAdmissionModeAction")]
    public abstract byte[]? BuildSetAdmissionModeAction(BigInteger? mode);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetImmutableFlagAction")]
    public abstract byte[]? BuildSetImmutableFlagAction(BigInteger? flagId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getApprovalCount")]
    public abstract BigInteger? GetApprovalCount(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getApprovedAt")]
    public abstract BigInteger? GetApprovedAt(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProposal")]
    public abstract byte[]? GetProposal(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProposalExecutedAt")]
    public abstract BigInteger? GetProposalExecutedAt(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getProposalStage")]
    public abstract BigInteger? GetProposalStage(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedAndTimelocked")]
    public abstract bool? IsApprovedAndTimelocked(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedBridgeAdapter")]
    public abstract bool? IsApprovedBridgeAdapter(UInt160? bridge);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isApprovedVerifier")]
    public abstract bool? IsApprovedVerifier(UInt160? verifier);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isCouncilMember")]
    public abstract bool? IsCouncilMember(ECPoint? memberKey);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isImmutable")]
    public abstract bool? IsImmutable(BigInteger? flagId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isInExecutionWindow")]
    public abstract bool? IsInExecutionWindow(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isVetoed")]
    public abstract bool? IsVetoed(BigInteger? proposalId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("matchesProposalPayload")]
    public abstract bool? MatchesProposalPayload(BigInteger? proposalId, byte[]? expectedAction);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("approve")]
    public abstract BigInteger? Approve(BigInteger? proposalId, ECPoint? memberKey);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("approveBridgeAdapter")]
    public abstract void ApproveBridgeAdapter(UInt160? bridge);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("approveVerifier")]
    public abstract void ApproveVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("cancelProposal")]
    public abstract void CancelProposal(BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("createProposal")]
    public abstract BigInteger? CreateProposal(ECPoint? signer, byte[]? payload);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("markProposalExecuted")]
    public abstract void MarkProposalExecuted(BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeBridgeAdapter")]
    public abstract void RevokeBridgeAdapter(UInt160? bridge);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("revokeVerifier")]
    public abstract void RevokeVerifier(UInt160? verifier);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setAdmissionModeViaProposal")]
    public abstract void SetAdmissionModeViaProposal(BigInteger? mode, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setImmutableFlag")]
    public abstract void SetImmutableFlag(BigInteger? flagId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setImmutableFlagViaProposal")]
    public abstract void SetImmutableFlagViaProposal(BigInteger? flagId, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setUpgradeWindows")]
    public abstract void SetUpgradeWindows(BigInteger? noticeSeconds, BigInteger? executionWindowSeconds, BigInteger? cooldownSeconds);

    #endregion
}
