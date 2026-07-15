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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeEscrow"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":296,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":374,""safe"":false},{""name"":""getRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":495,""safe"":true},{""name"":""getNeoChainId"",""parameters"":[],""returntype"":""Integer"",""offset"":553,""safe"":true},{""name"":""setRegistry"",""parameters"":[{""name"":""registry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":615,""safe"":false},{""name"":""setRegistryViaProposal"",""parameters"":[{""name"":""registry"",""type"":""Hash160""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":827,""safe"":false},{""name"":""buildSetRegistryAction"",""parameters"":[{""name"":""registry"",""type"":""Hash160""}],""returntype"":""ByteArray"",""offset"":1481,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""governanceController"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2279,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":1175,""safe"":true},{""name"":""lockGovernance"",""parameters"":[],""returntype"":""Void"",""offset"":2393,""safe"":false},{""name"":""isGovernanceLocked"",""parameters"":[],""returntype"":""Boolean"",""offset"":772,""safe"":true},{""name"":""setAssetRoute"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""payoutAdapter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":2535,""safe"":false},{""name"":""setAssetRouteActive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":4491,""safe"":false},{""name"":""configureAssetRouteViaProposal"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""payoutAdapter"",""type"":""Hash160""},{""name"":""active"",""type"":""Boolean""},{""name"":""proposalId"",""type"":""Integer""}],""returntype"":""Void"",""offset"":4586,""safe"":false},{""name"":""buildConfigureAssetRouteAction"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""payoutAdapter"",""type"":""Hash160""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""ByteArray"",""offset"":4615,""safe"":true},{""name"":""getRoutedNeoAsset"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":5492,""safe"":true},{""name"":""getPayoutAdapter"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":5555,""safe"":true},{""name"":""getPayoutAdapterUpdateCounter"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":5619,""safe"":true},{""name"":""isAssetRouteActive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":5691,""safe"":true},{""name"":""getRoutedForeignAsset"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""neoAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":5731,""safe"":true},{""name"":""fundLiquidity"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":5791,""safe"":false},{""name"":""send"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""calldata"",""type"":""ByteArray""},{""name"":""deadlineUnixSeconds"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":6980,""safe"":false},{""name"":""receive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":8085,""safe"":false},{""name"":""getLastOutboundNonce"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":12286,""safe"":true},{""name"":""getLockedBalance"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":12684,""safe"":true},{""name"":""isInboundConsumed"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":12722,""safe"":true},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":12749,""safe"":false},{""name"":""_initialize"",""parameters"":[],""returntype"":""Void"",""offset"":12953,""safe"":false}],""events"":[{""name"":""CrossChainSendInitiated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Hash160""},{""name"":""arg6"",""type"":""Integer""},{""name"":""arg7"",""type"":""ByteArray""}]},{""name"":""CrossChainInboundFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""CrossChainAssetPaid"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Hash160""},{""name"":""arg6"",""type"":""Hash160""},{""name"":""arg7"",""type"":""Integer""},{""name"":""arg8"",""type"":""Hash160""}]},{""name"":""AssetRouteConfigured"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Boolean""}]},{""name"":""GovernanceControllerChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""GovernanceLocked"",""parameters"":[]},{""name"":""RegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""L1 escrow \u002B dispatch for cross-foreign-chain messages."",""Version"":""0.2.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeEscrow"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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xe1AVwADekoQLgQiCEoB/wAyBgH/AJFKeHlR0EV6GKlKEC4EIghKAf8AMgYB/wCRSnh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfUdBFQFcBAzUO8f//eXg0HXB4eRBoNQH9//8AFGg1+fz//3oVVTVL+P//QFcBAnl4NTr8//81FO///3BoC5gkGgwVYXNzZXQgcm91dGUgbm90IGZvdW5k4GjbMDWM/P//IgJAVwEGfHt6eXg0FXBofTWB8f//fHt6eXg19vf//0BXBAVZcGjKABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnwAUnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8AFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn4hxEHIQcyJvaGvOSmlqSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfclHQRWtKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWtoyrUkj2lqQdv+qHRTNYTz//9qABSeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0pyRWlqNQju//9TNcnz//9qFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFeGppNY3z//9qFJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSnJFeWppNcry//9qABSe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YW1vdW50IG11c3QgYmUgcG9zaXRpdmXgaHg17gIAAHFpNf/k//9yaguXJgsRSnNFI6QBAABq2zB0bBDObBHOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJsEs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwTzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwUzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwVzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwWzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwXzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkkpzRWsRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnNFGIh0a0oQLgQiCEoB/wAyBgH/AJFKbBBR0EVrGKlKEC4EIghKAf8AMgYB/wCRSmwRUdBFayCpShAuBCIISgH/ADIGAf8AkUpsElHQRWsAGKlKEC4EIghKAf8AMgYB/wCRSmwTUdBFawAgqUoQLgQiCEoB/wAyBgH/AJFKbBRR0EVrACipShAuBCIISgH/ADIGAf8AkUpsFVHQRWsAMKlKEC4EIghKAf8AMgYB/wCRSmwWUdBFawA4qUoQLgQiCEoB/wAyBgH/AJFKbBdR0EVsaTVf5///QTlTbjx1DBxhc3NldCB0cmFuc2ZlciBmYWlsZWQgKGxvY2spe216NeT5//97eng1oPv//3x7enlta3gXwAwXQ3Jvc3NDaGFpblNlbmRJbml0aWF0ZWRBlQFvYWsiAkBXAQIZiHARSmgQUdBFeEoQLgQiCEoB/wAyBgH/AJFKaBFR0EV4GKlKEC4EIghKAf8AMgYB/wCRSmgSUdBFeCCpShAuBCIISgH/ADIGAf8AkUpoE1HQRXgAGKlKEC4EIghKAf8AMgYB/wCRSmgUUdBFeUoQLgQiCEoB/wAyBgH/AJFKaBVR0EV5GKlKEC4EIghKAf8AMgYB/wCRSmgWUdBFeSCpShAuBCIISgH/ADIGAf8AkUpoF1HQRXkAGKlKEC4EIghKAf8AMgYB/wCRSmgYUdBFaCICQEE5U248QFcLA3g19+3//3nKAGa4JBsMFm1lc3NhZ2VCeXRlcyB0b28gc2hvcnTgeRDOeRHOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknkSziCoShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ5E84AGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknBoeJckQgw9ZXh0ZXJuYWxDaGFpbklkIGFyZ3VtZW50IGRvZXMgbm90IG1hdGNoIHNpZ25lZCBtZXNzYWdlIGRvbWFpbuA1u+H//3F5FM55Fc4YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeRbOIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknkXzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGScmpplyQ7DDZzaWduZWQgbWVzc2FnZSBuZW9DaGFpbklkIGRvZXMgbm90IG1hdGNoIGVzY3JvdyBkb21haW7geRjOeRnOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5Gs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkbzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkczgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkdzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkezgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkfzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknN5IM50bBKXJCcMImRpcmVjdGlvbiBtdXN0IGJlIDIgKEZvcmVpZ25Ub05lbyngeQA5znkAOs4YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAO84gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAPM4AGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AD3OACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA+zgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAP84AMKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AEDOADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSdW0QlyYFCCINQbfDiAMB6AOhbbYkJAwfZXh0ZXJuYWwgYnJpZGdlIG1lc3NhZ2UgZXhwaXJlZOB5AGHOdm4QlyYFCCIFbhGXJgUIIgVuEpckKQwkdW5rbm93biBleHRlcm5hbCBicmlkZ2UgbWVzc2FnZSB0eXBl4ABieTXnAQAAdwdvBwIAAAEAtiQmDCFleHRlcm5hbCBicmlkZ2UgcGF5bG9hZCB0b28gbGFyZ2XgecoAZm8HSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACflyQ2DDFtZXNzYWdlQnl0ZXMgbGVuZ3RoIGRvZXMgbm90IG1hdGNoIHBheWxvYWQgbGVuZ3Ro4GtpeDUVAgAAdwhvCDVZ2///C5ckLAwnaW5ib3VuZCBub25jZSBhbHJlYWR5IGNvbnN1bWVkIChyZXBsYXkp4DVF3P//dwlvCQwUAAAAAAAAAAAAAAAAAAAAAAAAAACYJBcMEnJlZ2lzdHJ5IG5vdCB3aXJlZOB6eXgTwBUMDXZlcmlmeUluYm91bmRvCUFifVtSdwpvCiQvDCpyZWdpc3RyeSB2ZXJpZmllciByZWplY3RlZCBpbmJvdW5kIG1lc3NhZ2XgDAEB2zBvCDV83///bhCXJgUIIgVuEpcmD28HeW5ta2l4NdUCAABua3gTwAwaQ3Jvc3NDaGFpbkluYm91bmRGaW5hbGl6ZWRBlQFvYUBBt8OIA0BXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSIgJAVwEDABGIcBJKaBBR0EV4ShAuBCIISgH/ADIGAf8AkUpoEVHQRXgYqUoQLgQiCEoB/wAyBgH/AJFKaBJR0EV4IKlKEC4EIghKAf8AMgYB/wCRSmgTUdBFeAAYqUoQLgQiCEoB/wAyBgH/AJFKaBRR0EV5ShAuBCIISgH/ADIGAf8AkUpoFVHQRXkYqUoQLgQiCEoB/wAyBgH/AJFKaBZR0EV5IKlKEC4EIghKAf8AMgYB/wCRSmgXUdBFeQAYqUoQLgQiCEoB/wAyBgH/AJFKaBhR0EV6ShAuBCIISgH/ADIGAf8AkUpoGVHQRXoYqUoQLgQiCEoB/wAyBgH/AJFKaBpR0EV6IKlKEC4EIghKAf8AMgYB/wCRSmgbUdBFegAYqUoQLgQiCEoB/wAyBgH/AJFKaBxR0EV6ACCpShAuBCIISgH/ADIGAf8AkUpoHVHQRXoAKKlKEC4EIghKAf8AMgYB/wCRSmgeUdBFegAwqUoQLgQiCEoB/wAyBgH/AJFKaB9R0EV6ADipShAuBCIISgH/ADIGAf8AkUpoIFHQRWgiAkBXDwd+ABm4JBwMF2Fzc2V0IHBheWxvYWQgdG9vIHNob3J04ABmfTVR5f//cGhK2SgkBkUJIgbKABSzJAUJIgZoELOqJBoMFWludmFsaWQgZm9yZWlnbiBhc3NldOAAen01/fz//3FpELckBQkiBmkAILYkIAwbYW1vdW50IGxlbmd0aCBvdXQgb2YgYm91bmRz4AAYaUoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3J+argkHAwXYXNzZXQgcGF5bG9hZCB0cnVuY2F0ZWTgfBCXJjh+apckMwwuYXNzZXQtdHJhbnNmZXIgcGF5bG9hZCBjb250YWlucyB0cmFpbGluZyBieXRlc+AAfnN9a2lKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8Rn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OEJgkPAw3YW1vdW50IG11c3QgdXNlIG1pbmltYWwgdW5zaWduZWQgbGl0dGxlLWVuZGlhbiBlbmNvZGluZ+AQdGlKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfEZ9KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdSJxbAEAAaB9a22eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn86eSnRFbUqdSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3VFbRC4JI5sELckHAwXYW1vdW50IG11c3QgYmUgcG9zaXRpdmXgACV9NV7i//91bUrZKCQGRQkiBsoAFLMkBQkiBm0Qs6okGgwVaW52YWxpZCBOZW8gcmVjaXBpZW504Gh4NXnh//81U9T//3ZuC5gkGgwVYXNzZXQgcm91dGUgbm90IGZvdW5k4G7bMDXL4f//dwdvBwAozhGXJBkMFGFzc2V0IHJvdXRlIGluYWN0aXZl4BBvBzXQ4f//dwhvCHg1YuL//zX00///dwlvCQuYJAUJIhFvCUrYJAlKygAUKAM6aJckUgxNYXNzZXQgcm91dGUgcmV2ZXJzZSBtYXBwaW5nIGluY29uc2lzdGVudCDigJQgbWlncmF0ZSByb3V0ZSB0aHJvdWdoIGdvdmVybmFuY2XgABRvBzVM4f//dwpvCgwUAAAAAAAAAAAAAAAAAAAAAAAAAACXJxMBAAB5EJckNwwyTmVvIEwyIGRlc3RpbmF0aW9uIHJvdXRlcyByZXF1aXJlIGEgcGF5b3V0IGFkYXB0ZXLgfmqXJDMMLmFzc2V0LWFuZC1jYWxsIHJvdXRlIHJlcXVpcmVzIGEgcGF5b3V0IGFkYXB0ZXLgbwh4NWzs//93C28LNd7S//93DG8MC5cmBRAiDm8MStgmBkUQIgTbIXcNbw1suCQiDB1pbnN1ZmZpY2llbnQgZXNjcm93IGxpcXVpZGl0eeBvDWyfbws11tL//31sbUHb/qh0FMAfDAh0cmFuc2Zlcm8IQWJ9W1J3Dm8OJCEMHGFzc2V0IHBheW91dCB0cmFuc2ZlciBmYWlsZWTgImdvCmh4NYgAAABQNa3h//8AQX01zAAAAHcLfW8Le2xtbwhoenl4GsAfDAZwYXlvdXRvCkFifVtSdwxvDCQqDCVwYXlvdXQgYWRhcHRlciByZWplY3RlZCBpbmJvdW5kIGFzc2V04G8KbG1vCGh6eXgYwAwTQ3Jvc3NDaGFpbkFzc2V0UGFpZEGVAW9hQFcBAnl4NUbg//81ttH//3BoC5gkLQwocGF5b3V0IGFkYXB0ZXIgdXBkYXRlIGNvdW50ZXIgbm90IHBpbm5lZOBo2zA1q+D//xBQNSPg//8iAkBXAgIAIIhwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkkGjbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBXAwE1KNL//3BoeDWv7v//NcLQ//9xaQuXJggQI3EBAABp2zByahDOahHOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJqEs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoTzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoUzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoVzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoWzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoXzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkiICQFcBAnl4NcTo//81Os///3BoC5cmBRAiDWhK2CYGRRAiBNshIgJAVwECNXTQ//9weWh4NcX1//81Dc///wuYIgJAVwMDeRC3JBwMF2Ftb3VudCBtdXN0IGJlIHBvc2l0aXZl4EE5U248cHhoNWHn//9xaTXSzv//cmoLmCRDDD5kaXJlY3QgdHJhbnNmZXIgcmVqZWN0ZWQg4oCUIHVzZSBhbiBlc2Nyb3cgdHJhbnNmZXIgZW50cnlwb2ludOBqStgmBkUQIgTbIXmXJDsMNk5FUC0xNyBjYWxsYmFjayBhbW91bnQgZG9lcyBub3QgbWF0Y2ggcGVuZGluZyB0cmFuc2ZlcuBpNX/e//9AVgIMJW5lbzQtZ292OnNldEV4dGVybmFsQnJpZGdlUmVnaXN0cnk6djHbMGAMJ25lbzQtZ292OmNvbmZpZ3VyZUV4dGVybmFsQXNzZXRSb3V0ZTp2MdswYUB123x/").AsSerializable<Neo.SmartContract.NefFile>();

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
