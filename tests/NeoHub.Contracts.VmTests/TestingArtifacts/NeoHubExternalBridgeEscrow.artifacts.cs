using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubExternalBridgeEscrow(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeEscrow"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":296,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":374,""safe"":false},{""name"":""getRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":495,""safe"":true},{""name"":""getNeoChainId"",""parameters"":[],""returntype"":""Integer"",""offset"":553,""safe"":true},{""name"":""setRegistry"",""parameters"":[{""name"":""registry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":615,""safe"":false},{""name"":""setRegistryViaProposal"",""parameters"":[{""name"":""registry"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":827,""safe"":false},{""name"":""buildSetRegistryAction"",""parameters"":[{""name"":""registry"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":1480,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2276,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1174,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":2390,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":772,""safe"":true},{""name"":""setAssetRoute"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""payoutAdapter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2532,""safe"":false},{""name"":""setAssetRouteActive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":4500,""safe"":false},{""name"":""configureAssetRouteViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""payoutAdapter"",""type"":""Hash160""},{""name"":""active"",""type"":""Boolean""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4593,""safe"":false},{""name"":""buildConfigureAssetRouteAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""payoutAdapter"",""type"":""Hash160""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""ByteArray"",""offset"":4622,""safe"":true},{""name"":""getRoutedNeoAsset"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":5497,""safe"":true},{""name"":""getPayoutAdapter"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":5559,""safe"":true},{""name"":""getPayoutAdapterUpdateCounter"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":5622,""safe"":true},{""name"":""isAssetRouteActive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5693,""safe"":true},{""name"":""getRoutedForeignAsset"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""neoAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":5733,""safe"":true},{""name"":""fundLiquidity"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5793,""safe"":false},{""name"":""send"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""calldata"",""type"":""ByteArray""},{""name"":""deadlineUnixSeconds"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6783,""safe"":false},{""name"":""receive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":7935,""safe"":false},{""name"":""getLastOutboundNonce"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":12013,""safe"":true},{""name"":""getLockedBalance"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":12411,""safe"":true},{""name"":""isInboundConsumed"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":12449,""safe"":true},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":12476,""safe"":false},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":12696,""safe"":false}],""events"":[{""name"":""CrossChainSendInitiated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Hash160""},{""name"":""arg6"",""type"":""Integer""},{""name"":""arg7"",""type"":""ByteArray""}]},{""name"":""CrossChainInboundFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""CrossChainAssetPaid"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Hash160""},{""name"":""arg6"",""type"":""Hash160""},{""name"":""arg7"",""type"":""Integer""},{""name"":""arg8"",""type"":""Hash160""}]},{""name"":""AssetRouteConfigured"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Boolean""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]},{""name"":""RegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""L1 escrow \u002B dispatch for cross-foreign-chain messages."",""Version"":""0.2.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeEscrow"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AAABAAAAn3JR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VraMq1JI9B2/6odGppNYMAAABqABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRTUM+///amk1yQAAAGoUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV4amk0BmkiAkBXAgN62zBwEHEibmhpzkp4eWmeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn1HQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAFLUkkEDbMEBB2/6odEBXAAN6ShAuBCIISgH/ADIGAf8AkUp4eVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EV6IKlKEC4EIghKAf8AMgYB/wCRSnh5Ep5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVAVwABNbX5//94StkoJAZFCSIGygAUsyQFCSIGeBCzqiQiDB1pbnZhbGlkIGdvdmVybmFuY2UgY29udHJvbGxlcuB4DAH82zA10ff//3gRwAwbR292ZXJuYW5jZUNvbnRyb2xsZXJDaGFuZ2VkQZUBb2FANdL3//9B+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA1I/v//wwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCQMH2dvdmVybmFuY2UgY29udHJvbGxlciBub3Qgd2lyZWTgNVH5//+qJioMAQHbMAwB+9swNe37//8QwAwQR292ZXJuYW5jZUxvY2tlZEGVAW9hQFcABDW1+P//CHt6eXg0A0BXCwV4NaEDAAB5StkoJAZFCSIGygAUsyQFCSIGeRCzqiQaDBVpbnZhbGlkIGZvcmVpZ24gYXNzZXTgekrZKCQGRQkiBsoAFLMkBQkiBnoQs6okFgwRaW52YWxpZCBOZW8gYXNzZXTgewwUAAAAAAAAAAAAAAAAAAAAAAAAAACXJgUIIhB7StkoJAZFCSIGygAUsyQbDBZpbnZhbGlkIHBheW91dCBhZGFwdGVy4HyqJgUIIhp7DBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgmBQgiCTVk9///EJckPgw5YWN0aXZlIE5lbyBMMiBkZXN0aW5hdGlvbiByb3V0ZXMgcmVxdWlyZSBhIHBheW91dCBhZGFwdGVy4Hl4Ne4CAABwaDW99f//cQwUAAAAAAAAAAAAAAAAAAAAAAAAAAByaQuYJkZp2zA1PwMAAHMQazVmAwAAepckJwwiZm9yZWlnbiBhc3NldCBtYXBwaW5nIGlzIGltbXV0YWJsZeAAFGs1NQMAAEpyRXp4NccDAABzazVO9f//dHyqJgUIIgVsC5cmBQgiEGxK2CQJSsoAFCgDOnmXJDYMMU5lbyBhc3NldCBhbHJlYWR5IG1hcHBlZCB0byBhbm90aGVyIGZvcmVpZ24gYXNzZXTgEHV5eDWFAwAAdm416vT//3cHCXcIewwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJ9UAAABpC5gkBQkiBWp7lyZ4bwcLmCYgEG8H2zA14gMAADVcAwAASnVFfCYJbXs1DQQAACJSfCZPezVWBAAASnVFbRCXJDYMMXBheW91dCBhZGFwdGVyIG11c3QgYmUgYSBuZXZlci11cGRhdGVkIGRlcGxveW1lbnTgezVPBAAACEp3CEUiT3s1BwQAAEp1RW0QlyQ2DDFwYXlvdXQgYWRhcHRlciBtdXN0IGJlIGEgbmV2ZXItdXBkYXRlZCBkZXBsb3ltZW504Hs1AAQAAAhKdwhFACmIdwl6EG8JNYD6//97ABRvCTV2+v//fCYFESIDEEoQLgQiCEoB/wAyBgH/AJFKbwkAKFHQRW8JaDWb+P//ewwUAAAAAAAAAAAAAAAAAAAAAAAAAACXJgpuNeQDAAAiG28IJhcSiHcKbRBvCjXmAwAAbwpuNWD4//9sC5cmBQgiEGxK2CQJSsoAFCgDOnmXJgl5azWL8///fHt6eXgVwAwUQXNzZXRSb3V0ZUNvbmZpZ3VyZWRBlQFvYUBXAAF4AwAAAP8AAAAAkQMAAADgAAAAAJckSAxDZXh0ZXJuYWxDaGFpbklkIG11c3QgdXNlIHRoZSAweEUwX3h4X3h4X3h4IGZvcmVpZ24tbmFtZXNwYWNlIHByZWZpeOBAVwECABmIcBVKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV5FWg1+Pj//2giAkBXAAF4ygAplyQgDBthc3NldCByb3V0ZSBzdG9yYWdlIGNvcnJ1cHTgeCICQNswQFcCAgAUiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAUtSSQaNsoStgkCUrKABQoAzoiAkDbKErYJAlKygAUKAM6QFcBAgAZiHAXSmgQUdBFeBFoNZr4//95FWg1C/j//2giAkBXAQIAGYhwGEpoEFHQRXgRaDV4+P//eRVoNen3//9oIgJAVwACeHnOeHkRnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OGKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfkkoQLgQiCkoC//8AADIIAv//AACRIgJAVwABeMoSlyQyDC1wYXlvdXQgYWRhcHRlciB1cGRhdGUtY291bnRlciBzdG9yYWdlIGNvcnJ1cHTgeCICQFcAAng0UHmXJEgMQ3BheW91dCBhZGFwdGVyIHdhcyB1cGdyYWRlZCBpbiBwbGFjZSDigJQgY29uZmlndXJlIGEgbmV3IGRlcGxveW1lbnTgeDQ+QFcBAXg3AABwaAuYJCYMIXBheW91dCBhZGFwdGVyIGNvbnRyYWN0IG5vdCBmb3VuZOBoEc4iAkA3AABAVwEBEMQAFQwNcGF5b3V0VmVyc2lvbnhBYn1bUnBoEZckJwwidW5zdXBwb3J0ZWQgcGF5b3V0IGFkYXB0ZXIgdmVyc2lvbuBAVwABeEGb9mfOQS9Yxe1AQS9Yxe1AVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8AMgYB/wCRSnh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFQFcBAzUF8f//eXg0G3B6ABRoNQH9//8QaDX6/P//eXg1Qfj//0BXAQJ5eDU8/P//NQ3v//9waAuYJBoMFWFzc2V0IHJvdXRlIG5vdCBmb3VuZOBo2zA1jvz//yICQFcBBnx7enl4NBVwaH01efH//3x7enl4Nez3//9AVwQFWXBoygAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8UnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8UnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8AFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnwAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8RnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+IcRByEHMib2hrzkppakqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3JR0EVrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VraMq1JI9B2/6odGppNXvz//9qABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRTUE7v//amk1wfP//2oUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV4amk1hfP//2oUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV5amk1wvL//2oAFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFemppNYXy//9qABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRXtqaTVI8v//agAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9KckV8JgURIgMQShAuBCIISgH/ADIGAf8AkUppalHQRWkiAkBXAQJ5eDV8+P//NU3r//9waAuXJhoMFAAAAAAAAAAAAAAAAAAAAAAAAAAAIhAQaNswNc34//819/j//yICQFcBAnl4NT74//81D+v//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiEQAUaNswNY74//81uPj//yICQFcCAnl4Nf/3//810Or//3BoC5cmBRAiMGjbMDVm+P//RXl4NUv5//81sur//3FpC5cmBRAiEBBp2zA11/n//zVR+f//IgJAVwECeXg1uPf//zWJ6v//cGgLmCQFCSIPaNswNR/4//8AKM4RlyICQFcBAnl4Ndj4//81Yer//3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcFAzWE6v//cGhB+CfsjCQTDA5ub3QgYXV0aG9yaXplZOA1Zuv//xCXJEwMR2RpcmVjdCBlc2Nyb3cgbGlxdWlkaXR5IGlzIG9ubHkgdmFsaWQgZm9yIHRoZSBOZW8gTDEgZGVzdGluYXRpb24gZG9tYWlu4Hg1gvb//3lK2SgkBkUJIgbKABSzJAUJIgZ5ELOqJBIMDWludmFsaWQgYXNzZXTgehC3JBwMF2Ftb3VudCBtdXN0IGJlIHBvc2l0aXZl4Hl4Nf/+//9xaQwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJCEMHGRpcmVjdCBhc3NldCByb3V0ZSBub3QgZm91bmTgaXg1Evr//3JqACjOEZckIAwbZGlyZWN0IGFzc2V0IHJvdXRlIGluYWN0aXZl4AAUajXQ9v//DBQAAAAAAAAAAAAAAAAAAAAAAAAAAJckPgw5bGlxdWlkaXR5IGZ1bmRpbmcgaXMgb25seSB2YWxpZCBmb3IgZGlyZWN0LXJlbGVhc2Ugcm91dGVz4Hp5eDRgaHk1eAEAAHN6azXT6P//C3pB2/6odGgUwB8MCHRyYW5zZmVyeUFifVtSdGwkKwwmYXNzZXQgdHJhbnNmZXIgZmFpbGVkIChmdW5kIGxpcXVpZGl0eSngazWO+P//QFcDA3l4NChwaDU06P//cWkLlyYFECINaUrYJgZFECIE2yFyanqeaDVY6P//QFcDAgAZiHATSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFedswcRByIm5pas5KaBVqnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfckVqABS1JJBoIgJAVwQCACmIcBRKaBBR0EV42zBxedswchBzI6sAAABpa85KaBFrnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9R0EVqa85KaAAVa55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFa0qcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3NFawAUtSVW////aCICQFcIBjWn5///cHg1EPP//3lK2SgkBkUJIgbKABSzJAUJIgZ5ELOqJBYMEWludmFsaWQgcmVjaXBpZW504HpK2SgkBkUJIgbKABSzJAUJIgZ6ELOqJBIMDWludmFsaWQgYXNzZXTgexC3JBwMF2Ftb3VudCBtdXN0IGJlIHBvc2l0aXZl4Ht6eDWN/f//aHg1FQMAAHFpNbzl//9yaguXJgsRSnNFI6QBAABq2zB0bBDObBHOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJsEs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwTzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwUzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwVzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwWzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwXzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkkpzRWsRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnNFGIh0a0oQLgQiCEoB/wAyBgH/AJFKbBBR0EVrGKlKEC4EIghKAf8AMgYB/wCRSmwRUdBFayCpShAuBCIISgH/ADIGAf8AkUpsElHQRWsAGKlKEC4EIghKAf8AMgYB/wCRSmwTUdBFawAgqUoQLgQiCEoB/wAyBgH/AJFKbBRR0EVrACipShAuBCIISgH/ADIGAf8AkUpsFVHQRWsAMKlKEC4EIghKAf8AMgYB/wCRSmwWUdBFawA4qUoQLgQiCEoB/wAyBgH/AJFKbBdR0EVsaTUb6P//QTlTbjx1bXo1C/z//3Z7bjVm4///C3tB2/6odG0UwB8MCHRyYW5zZmVyekFifVtSdwdvByQhDBxhc3NldCB0cmFuc2ZlciBmYWlsZWQgKGxvY2sp4G41KfP//3x7enlta3gXwAwXQ3Jvc3NDaGFpblNlbmRJbml0aWF0ZWRBlQFvYWsiAkBXAQIZiHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBVR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFaCICQEE5U248QFcLA3g1lu7//3nKAGa4JBsMFm1lc3NhZ2VCeXRlcyB0b28gc2hvcnTgeRDOeRHOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknkSziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ5E84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknBoeJckQgw9ZXh0ZXJuYWxDaGFpbklkIGFyZ3VtZW50IGRvZXMgbm90IG1hdGNoIHNpZ25lZCBtZXNzYWdlIGRvbWFpbuA1UeL//3F5FM55Fc4YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeRbOIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknkXzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGScmpplyQ7DDZzaWduZWQgbWVzc2FnZSBuZW9DaGFpbklkIGRvZXMgbm90IG1hdGNoIGVzY3JvdyBkb21haW7geRjOeRnOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5Gs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkbzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkczgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkdzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkezgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkfzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknN5IM50bBKXJCcMImRpcmVjdGlvbiBtdXN0IGJlIDIgKEZvcmVpZ25Ub05lbyngeQA5znkAOs4YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAO84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAPM4AGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AD3OACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA+zgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAP84AMKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AEDOADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSdW0QlyYFCCINQbfDiAMB6AOhbbYkJAwfZXh0ZXJuYWwgYnJpZGdlIG1lc3NhZ2UgZXhwaXJlZOB5AGHOdm4QlyYFCCIFbhGXJgUIIgVuEpckKQwkdW5rbm93biBleHRlcm5hbCBicmlkZ2UgbWVzc2FnZSB0eXBl4ABieTXnAQAAdwdvBwIAAAEAtiQmDCFleHRlcm5hbCBicmlkZ2UgcGF5bG9hZCB0b28gbGFyZ2XgecoAZm8HSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQ2DDFtZXNzYWdlQnl0ZXMgbGVuZ3RoIGRvZXMgbm90IG1hdGNoIHBheWxvYWQgbGVuZ3Ro4GtpeDUVAgAAdwhvCDXv2///C5ckLAwnaW5ib3VuZCBub25jZSBhbHJlYWR5IGNvbnN1bWVkIChyZXBsYXkp4DXb3P//dwlvCQwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJBcMEnJlZ2lzdHJ5IG5vdCB3aXJlZOB6eXgTwBUMDXZlcmlmeUluYm91bmRvCUFifVtSdwpvCiQvDCpyZWdpc3RyeSB2ZXJpZmllciByZWplY3RlZCBpbmJvdW5kIG1lc3NhZ2XgDAEB2zBvCDUR4P//bhCXJgUIIgVuEpcmD28HeW5ta2l4NdUCAABua3gTwAwaQ3Jvc3NDaGFpbkluYm91bmRGaW5hbGl6ZWRBlQFvYUBBt8OIA0BXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiDkoD/////wAAAAAyDAP/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ZmVybwhBYn1bUncNbw0kIQwcYXNzZXQgcGF5b3V0IHRyYW5zZmVyIGZhaWxlZOAiZmh4NYkAAABvCTXG4v//AEF9NcsAAAB3Cn1vCntsbW8IaHp5eBrAHwwGcGF5b3V0bwlBYn1bUncLbwskKgwlcGF5b3V0IGFkYXB0ZXIgcmVqZWN0ZWQgaW5ib3VuZCBhc3NldOBvCWxtbwhoenl4GMAME0Nyb3NzQ2hhaW5Bc3NldFBhaWRBlQFvYUBXAQJ5eDVf4f//NcbS//9waAuYJC0MKHBheW91dCBhZGFwdGVyIHVwZGF0ZSBjb3VudGVyIG5vdCBwaW5uZWTgEGjbMDXD4f//NT3h//8iAkBXAgIAIIhwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkkGjbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBXAwE1OdP//3BoeDUq7///NdPR//9xaQuXJggQI3EBAABp2zByahDOahHOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJqEs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoTzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoUzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoVzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoWzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoXzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkiICQFcBAnl4NUDo//81S9D//3BoC5cmBRAiDWhK2CYGRRAiBNshIgJAVwECNYXR//9weWh4NUD2//81HtD//wuYIgJAVwMDeRC3JBwMF2Ftb3VudCBtdXN0IGJlIHBvc2l0aXZl4EE5U248cHhoNcno//9xaTXjz///cmoLmCRTDE5kaXJlY3QgdHJhbnNmZXIgcmVqZWN0ZWQg4oCUIGNhbGwgU2VuZCB0byBsb2NrIGFzc2V0cyBmb3IgY3Jvc3MtY2hhaW4gdHJhbnNmZXLgakrYJgZFECIE2yF5lyQ7DDZORVAtMTcgY2FsbGJhY2sgYW1vdW50IGRvZXMgbm90IG1hdGNoIHBlbmRpbmcgdHJhbnNmZXLgaTWJ3///QFYCDCVuZW80LWdvdjpzZXRFeHRlcm5hbEJyaWRnZVJlZ2lzdHJ5OnYx2zBgDCduZW80LWdvdjpjb25maWd1cmVFeHRlcm5hbEFzc2V0Um91dGU6djHbMGFATC48TQ==").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delAssetRouteConfigured(BigInteger? arg1, UInt160? arg2, UInt160? arg3, UInt160? arg4, bool? arg5);

    [DisplayName("AssetRouteConfigured")]
    public event delAssetRouteConfigured? OnAssetRouteConfigured;

    public delegate void delCrossChainAssetPaid(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3, UInt160? arg4, UInt160? arg5, UInt160? arg6, BigInteger? arg7, UInt160? arg8);

    [DisplayName("CrossChainAssetPaid")]
    public event delCrossChainAssetPaid? OnCrossChainAssetPaid;

    public delegate void delCrossChainInboundFinalized(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("CrossChainInboundFinalized")]
    public event delCrossChainInboundFinalized? OnCrossChainInboundFinalized;

    public delegate void delCrossChainSendInitiated(BigInteger? arg1, BigInteger? arg2, UInt160? arg3, UInt160? arg4, UInt160? arg5, BigInteger? arg6, byte[]? arg7);

    [DisplayName("CrossChainSendInitiated")]
    public event delCrossChainSendInitiated? OnCrossChainSendInitiated;

    public delegate void delGovernanceControllerChanged(UInt160? obj);

    [DisplayName("GovernanceControllerChanged")]
    public event delGovernanceControllerChanged? OnGovernanceControllerChanged;

    public delegate void delGovernanceLocked();

    [DisplayName("GovernanceLocked")]
    public event delGovernanceLocked? OnGovernanceLocked;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    public delegate void delRegistryChanged(UInt160? obj);

    [DisplayName("RegistryChanged")]
    public event delRegistryChanged? OnRegistryChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? NeoChainId { [DisplayName("getNeoChainId")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Registry { [DisplayName("getRegistry")] get; [DisplayName("setRegistry")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract bool? IsGovernanceLocked { [DisplayName("isGovernanceLocked")] get; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildConfigureAssetRouteAction")]
    public abstract byte[]? BuildConfigureAssetRouteAction(BigInteger? externalChainId, UInt160? foreignAsset, UInt160? neoAsset, UInt160? payoutAdapter, bool? active);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("buildSetRegistryAction")]
    public abstract byte[]? BuildSetRegistryAction(UInt160? registry);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLastOutboundNonce")]
    public abstract BigInteger? GetLastOutboundNonce(BigInteger? externalChainId);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getLockedBalance")]
    public abstract BigInteger? GetLockedBalance(BigInteger? externalChainId, UInt160? asset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPayoutAdapter")]
    public abstract UInt160? GetPayoutAdapter(BigInteger? externalChainId, UInt160? foreignAsset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getPayoutAdapterUpdateCounter")]
    public abstract BigInteger? GetPayoutAdapterUpdateCounter(BigInteger? externalChainId, UInt160? foreignAsset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getRoutedForeignAsset")]
    public abstract UInt160? GetRoutedForeignAsset(BigInteger? externalChainId, UInt160? neoAsset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("getRoutedNeoAsset")]
    public abstract UInt160? GetRoutedNeoAsset(BigInteger? externalChainId, UInt160? foreignAsset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isAssetRouteActive")]
    public abstract bool? IsAssetRouteActive(BigInteger? externalChainId, UInt160? foreignAsset);

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("isInboundConsumed")]
    public abstract bool? IsInboundConsumed(BigInteger? externalChainId, BigInteger? nonce);

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("configureAssetRouteViaProposal")]
    public abstract void ConfigureAssetRouteViaProposal(BigInteger? externalChainId, UInt160? foreignAsset, UInt160? neoAsset, UInt160? payoutAdapter, bool? active, BigInteger? proposalId);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("fundLiquidity")]
    public abstract void FundLiquidity(BigInteger? externalChainId, UInt160? asset, BigInteger? amount);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("lockGovernance")]
    public abstract void LockGovernance();

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("onNEP17Payment")]
    public abstract void OnNEP17Payment(UInt160? from, BigInteger? amount, object? data = null);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("receive")]
    public abstract void Receive(BigInteger? externalChainId, byte[]? messageBytes, byte[]? proofBytes);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("send")]
    public abstract BigInteger? Send(BigInteger? externalChainId, UInt160? recipient, UInt160? asset, BigInteger? amount, byte[]? calldata, BigInteger? deadlineUnixSeconds);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setAssetRoute")]
    public abstract void SetAssetRoute(BigInteger? externalChainId, UInt160? foreignAsset, UInt160? neoAsset, UInt160? payoutAdapter);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setAssetRouteActive")]
    public abstract void SetAssetRouteActive(BigInteger? externalChainId, UInt160? foreignAsset, bool? active);

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("setRegistryViaProposal")]
    public abstract void SetRegistryViaProposal(UInt160? registry, BigInteger? proposalId);

    #endregion
}
