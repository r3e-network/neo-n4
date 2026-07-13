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

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.ExternalBridgeEscrow"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":391,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":469,""safe"":false},{""name"":""getRegistry"",""parameters"":[],""returntype"":""Hash160"",""offset"":590,""safe"":true},{""name"":""getNeoChainId"",""parameters"":[],""returntype"":""Integer"",""offset"":244,""safe"":true},{""name"":""setRegistry"",""parameters"":[{""name"":""registry"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":648,""safe"":false},{""name"":""setAssetRoute"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""neoAsset"",""type"":""Hash160""},{""name"":""payoutAdapter"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":761,""safe"":false},{""name"":""setAssetRouteActive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""},{""name"":""active"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":1413,""safe"":false},{""name"":""getRoutedNeoAsset"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":1805,""safe"":true},{""name"":""getPayoutAdapter"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Hash160"",""offset"":1862,""safe"":true},{""name"":""isAssetRouteActive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""foreignAsset"",""type"":""Hash160""}],""returntype"":""Boolean"",""offset"":1920,""safe"":true},{""name"":""fundLiquidity"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""}],""returntype"":""Void"",""offset"":1955,""safe"":false},{""name"":""send"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""recipient"",""type"":""Hash160""},{""name"":""asset"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""calldata"",""type"":""ByteArray""},{""name"":""deadlineUnixSeconds"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":2695,""safe"":false},{""name"":""receive"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""messageBytes"",""type"":""ByteArray""},{""name"":""proofBytes"",""type"":""ByteArray""}],""returntype"":""Void"",""offset"":3889,""safe"":false},{""name"":""getLastOutboundNonce"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""}],""returntype"":""Integer"",""offset"":7894,""safe"":true},{""name"":""getLockedBalance"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""asset"",""type"":""Hash160""}],""returntype"":""Integer"",""offset"":8300,""safe"":true},{""name"":""isInboundConsumed"",""parameters"":[{""name"":""externalChainId"",""type"":""Integer""},{""name"":""nonce"",""type"":""Integer""}],""returntype"":""Boolean"",""offset"":8338,""safe"":true},{""name"":""onNEP17Payment"",""parameters"":[{""name"":""from"",""type"":""Hash160""},{""name"":""amount"",""type"":""Integer""},{""name"":""data"",""type"":""Any""}],""returntype"":""Void"",""offset"":8373,""safe"":false}],""events"":[{""name"":""CrossChainSendInitiated"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Hash160""},{""name"":""arg6"",""type"":""Integer""},{""name"":""arg7"",""type"":""ByteArray""}]},{""name"":""CrossChainInboundFinalized"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]},{""name"":""CrossChainAssetPaid"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Hash160""},{""name"":""arg6"",""type"":""Hash160""},{""name"":""arg7"",""type"":""Integer""},{""name"":""arg8"",""type"":""Hash160""}]},{""name"":""AssetRouteConfigured"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Hash160""},{""name"":""arg3"",""type"":""Hash160""},{""name"":""arg4"",""type"":""Hash160""},{""name"":""arg5"",""type"":""Boolean""}]},{""name"":""RegistryChanged"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""L1 escrow \u002B dispatch for cross-foreign-chain messages."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.ExternalBridgeEscrow"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"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AAAAAJFKAv///38yDAMAAAAAAQAAAJ9zRWsAFLUlVv///2giAkBBYn1bUkBB2/6odEBXAAF4QZv2Z85BL1jF7UBBL1jF7UBXCAY1avb//3BoEJgkIwweZXNjcm93IE5lbyBMMiBkb21haW4gbm90IGJvdW5k4Hg1Wfn//3lK2SgkBkUJIgbKABSzJAUJIgZ5ELOqJBYMEWludmFsaWQgcmVjaXBpZW504HpK2SgkBkUJIgbKABSzJAUJIgZ6ELOqJBIMDWludmFsaWQgYXNzZXTgexC3JBwMF2Ftb3VudCBtdXN0IGJlIHBvc2l0aXZl4Ht6eDVG/f//aHg1GQMAAHFpNeL1//9yaguXJgsRSnNFI6QBAABq2zB0bBDObBHOGKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJsEs4gqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwTzgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwUzgAgqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwVzgAoqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwWzgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmwXzgA4qEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkkpzRWsRnkoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRSnNFGIh0a0oQLgQiCEoB/wAyBgH/AJFKbBBR0EVrGKlKEC4EIghKAf8AMgYB/wCRSmwRUdBFayCpShAuBCIISgH/ADIGAf8AkUpsElHQRWsAGKlKEC4EIghKAf8AMgYB/wCRSmwTUdBFawAgqUoQLgQiCEoB/wAyBgH/AJFKbBRR0EVrACipShAuBCIISgH/ADIGAf8AkUpsFVHQRWsAMKlKEC4EIghKAf8AMgYB/wCRSmwWUdBFawA4qUoQLgQiCEoB/wAyBgH/AJFKbBdR0EVsaTUt9///QTlTbjx1bXo1xPv//3YMAQHbMG41FPf//wt7Qdv+qHRtFMAfDAh0cmFuc2ZlcnpBYn1bUncHbwckIQwcYXNzZXQgdHJhbnNmZXIgZmFpbGVkIChsb2NrKeBuNU78//98e3p5bWt4F8AMF0Nyb3NzQ2hhaW5TZW5kSW5pdGlhdGVkQZUBb2FrIgJAVwECGYhwEUpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXlKEC4EIghKAf8AMgYB/wCRSmgVUdBFeRipShAuBCIISgH/ADIGAf8AkUpoFlHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRWgiAkBBOVNuPEBXCwN4Ndv0//95ygBmuCQbDBZtZXNzYWdlQnl0ZXMgdG9vIHNob3J04HkQznkRzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ5Es4gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeRPOABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJwaHiXJEIMPWV4dGVybmFsQ2hhaW5JZCBhcmd1bWVudCBkb2VzIG5vdCBtYXRjaCBzaWduZWQgbWVzc2FnZSBkb21haW7gNerw//9xaRCYJCMMHmVzY3JvdyBOZW8gTDIgZG9tYWluIG5vdCBib3VuZOB5FM55Fc4YqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeRbOIKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknkXzgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGScmoQmCQvDCpzaWduZWQgbWVzc2FnZSBuZW9DaGFpbklkIG11c3QgYmUgbm9uLXplcm/gammXJDsMNnNpZ25lZCBtZXNzYWdlIG5lb0NoYWluSWQgZG9lcyBub3QgbWF0Y2ggZXNjcm93IGRvbWFpbuB5GM55Gc4YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkaziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeRvOABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeRzOACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeR3OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeR7OADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeR/OADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSc3kgznRsEpckJwwiZGlyZWN0aW9uIG11c3QgYmUgMiAoRm9yZWlnblRvTmVvKeB5ADnOeQA6zhioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA7ziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA8zgAYqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAPc4AIKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ5AD7OACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSeQA/zgAwqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRknkAQM4AOKhKEC4EIhZKBP//////////AAAAAAAAAAAyFAT//////////wAAAAAAAAAAkZJ1bRCXJgUIIg1Bt8OIAwHoA6FttiQkDB9leHRlcm5hbCBicmlkZ2UgbWVzc2FnZSBleHBpcmVk4HkAYc52bhCXJgUIIgVuEZcmBQgiBW4SlyQpDCR1bmtub3duIGV4dGVybmFsIGJyaWRnZSBtZXNzYWdlIHR5cGXgAGJ5NecBAAB3B28HAgAAAQC2JCYMIWV4dGVybmFsIGJyaWRnZSBwYXlsb2FkIHRvbyBsYXJnZeB5ygBmbwdKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+XJDYMMW1lc3NhZ2VCeXRlcyBsZW5ndGggZG9lcyBub3QgbWF0Y2ggcGF5bG9hZCBsZW5ndGjga2l4NRUCAAB3CG8INbnr//8LlyQsDCdpbmJvdW5kIG5vbmNlIGFscmVhZHkgY29uc3VtZWQgKHJlcGxheSngNbDs//93CW8JDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJgkFwwScmVnaXN0cnkgbm90IHdpcmVk4Hp5eBPAFQwNdmVyaWZ5SW5ib3VuZG8JQWJ9W1J3Cm8KJC8MKnJlZ2lzdHJ5IHZlcmlmaWVyIHJlamVjdGVkIGluYm91bmQgbWVzc2FnZeAMAQHbMG8INcfu//9uEJcmBQgiBW4SlyYPbwd5bm1raXg11QIAAG5reBPADBpDcm9zc0NoYWluSW5ib3VuZEZpbmFsaXplZEGVAW9hQEG3w4gDQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZJ4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioShAuBCIOSgP/////AAAAADIMA/////8AAAAAkZIiAkBXAQMAEYhwEkpoEFHQRXhKEC4EIghKAf8AMgYB/wCRSmgRUdBFeBipShAuBCIISgH/ADIGAf8AkUpoElHQRXggqUoQLgQiCEoB/wAyBgH/AJFKaBNR0EV4ABipShAuBCIISgH/ADIGAf8AkUpoFFHQRXlKEC4EIghKAf8AMgYB/wCRSmgVUdBFeRipShAuBCIISgH/ADIGAf8AkUpoFlHQRXkgqUoQLgQiCEoB/wAyBgH/AJFKaBdR0EV5ABipShAuBCIISgH/ADIGAf8AkUpoGFHQRXpKEC4EIghKAf8AMgYB/wCRSmgZUdBFehipShAuBCIISgH/ADIGAf8AkUpoGlHQRXogqUoQLgQiCEoB/wAyBgH/AJFKaBtR0EV6ABipShAuBCIISgH/ADIGAf8AkUpoHFHQRXoAIKlKEC4EIghKAf8AMgYB/wCRSmgdUdBFegAoqUoQLgQiCEoB/wAyBgH/AJFKaB5R0EV6ADCpShAuBCIISgH/ADIGAf8AkUpoH1HQRXoAOKlKEC4EIghKAf8AMgYB/wCRSmggUdBFaCICQFcOB34AGbgkHAwXYXNzZXQgcGF5bG9hZCB0b28gc2hvcnTgAGZ9NTHt//9waErZKCQGRQkiBsoAFLMkBQkiBmgQs6okGgwVaW52YWxpZCBmb3JlaWduIGFzc2V04AB6fTX9/P//cWkQtyQFCSIGaQAgtiQgDBthbW91bnQgbGVuZ3RoIG91dCBvZiBib3VuZHPgABhpSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcn5quCQcDBdhc3NldCBwYXlsb2FkIHRydW5jYXRlZOB8EJcmOH5qlyQzDC5hc3NldC10cmFuc2ZlciBwYXlsb2FkIGNvbnRhaW5zIHRyYWlsaW5nIGJ5dGVz4AB+c31raUoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+eSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAnxGfSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84QmCQ8DDdhbW91bnQgbXVzdCB1c2UgbWluaW1hbCB1bnNpZ25lZCBsaXR0bGUtZW5kaWFuIGVuY29kaW5n4BB0aUoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ8Rn0oCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ91InFsAQABoH1rbZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzp5KdEVtSp1KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdUVtELgkjmwQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeAAJX01Pur//3VtStkoJAZFCSIGygAUsyQFCSIGbRCzqiQaDBVpbnZhbGlkIE5lbyByZWNpcGllbnTgaHg1nOj//zWz5P//dm4LmCQaDBVhc3NldCByb3V0ZSBub3QgZm91bmTgbtswdwdvBwAozhGXJBkMFGFzc2V0IHJvdXRlIGluYWN0aXZl4BBvBzW16f//dwgAFG8HNarp//93CW8JDBQAAAAAAAAAAAAAAAAAAAAAAAAAAJcn2QAAAH5qlyQzDC5hc3NldC1hbmQtY2FsbCByb3V0ZSByZXF1aXJlcyBhIHBheW91dCBhZGFwdGVy4G8IeDWC6///dwpvCjX24///dwtvCwuXJgUQIg5vC0rYJgZFECIE2yF3DG8MbLgkIgwdaW5zdWZmaWNpZW50IGVzY3JvdyBsaXF1aWRpdHngbwxsn28KNfnj//99bG1B2/6odBTAHwwIdHJhbnNmZXJvCEFifVtSdw1vDSQhDBxhc3NldCBwYXlvdXQgdHJhbnNmZXIgZmFpbGVk4CJVAEF9NHd3Cn1vCntsbW8IaHp5eBrAHwwGcGF5b3V0bwlBYn1bUncLbwskKgwlcGF5b3V0IGFkYXB0ZXIgcmVqZWN0ZWQgaW5ib3VuZCBhc3NldOBvCWxtbwhoenl4GMAME0Nyb3NzQ2hhaW5Bc3NldFBhaWRBlQFvYUBXAgIAIIhwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWkAILUkkGjbKErYJAlKygAgKAM6IgJA2yhK2CQJSsoAICgDOkBXAwE1G+L//3BoEJcmBQsiDmh4NWvv//81NuL//3FpC5cmCBAjcQEAAGnbMHJqEM5qEc4YqEoQLgQiFkoE//////////8AAAAAAAAAADIUBP//////////AAAAAAAAAACRkmoSziCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSahPOABioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSahTOACCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSahXOACioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSahbOADCoShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSahfOADioShAuBCIWSgT//////////wAAAAAAAAAAMhQE//////////8AAAAAAAAAAJGSIgJAVwECeXg1Nuj//zWu4P//cGgLlyYFECINaErYJgZFECIE2yEiAkBXAQI1X+D//3BoEJgkBQkiEXloeDXR9v//NXng//8LmCICQFcCA3kQtyQcDBdhbW91bnQgbXVzdCBiZSBwb3NpdGl2ZeBBOVNuPHB4aDW36P//cWk1PuD//wuYJFMMTmRpcmVjdCB0cmFuc2ZlciByZWplY3RlZCDigJQgY2FsbCBTZW5kIHRvIGxvY2sgYXNzZXRzIGZvciBjcm9zcy1jaGFpbiB0cmFuc2ZlcuBpNTHp//9AFcSi4Q==").AsSerializable<Neo.SmartContract.NefFile>();

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
    public abstract BigInteger? NeoChainId { [DisplayName("getNeoChainId")] get; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Registry { [DisplayName("getRegistry")] get; [DisplayName("setRegistry")] set; }

    #endregion

    #region Safe methods

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
    [DisplayName("fundLiquidity")]
    public abstract void FundLiquidity(BigInteger? externalChainId, UInt160? asset, BigInteger? amount);

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

    #endregion
}
