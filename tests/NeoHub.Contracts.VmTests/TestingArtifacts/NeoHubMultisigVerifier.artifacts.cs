using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubMultisigVerifier(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.MultisigVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""_deploy"",""parameters"":[{""name"":""data"",""type"":""Any""},{""name"":""update"",""type"":""Boolean""}],""returntype"":""Void"",""offset"":0,""safe"":false},{""name"":""getOwner"",""parameters"":[],""returntype"":""Hash160"",""offset"":135,""safe"":true},{""name"":""setOwner"",""parameters"":[{""name"":""newOwner"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":234,""safe"":false},{""name"":""getGovernanceController"",""parameters"":[],""returntype"":""Hash160"",""offset"":355,""safe"":true},{""name"":""setGovernanceController"",""parameters"":[{""name"":""controller"",""type"":""Hash160""}],""returntype"":""Void"",""offset"":413,""safe"":false},{""name"":""getMinQuorum"",""parameters"":[],""returntype"":""Integer"",""offset"":547,""safe"":true},{""name"":""setMinQuorum"",""parameters"":[{""name"":""minQuorum"",""type"":""Integer""}],""returntype"":""Void"",""offset"":609,""safe"":false},{""name"":""verifyMultisigProof"",""parameters"":[{""name"":""commitmentBytes"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":742,""safe"":true}],""events"":[{""name"":""OwnerChanged"",""parameters"":[{""name"":""arg1"",""type"":""Hash160""},{""name"":""arg2"",""type"":""Hash160""}]},{""name"":""GovernanceControllerSet"",""parameters"":[{""name"":""obj"",""type"":""Hash160""}]},{""name"":""MinQuorumSet"",""parameters"":[{""name"":""obj"",""type"":""Integer""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Multisig attestation verifier for Neo Elastic Network."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.MultisigVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErODIxMTdjNDc5OWZkZTYzZThjMjMwZTllOTY5NmI2NmQ3OTQuLi4AAAEb9XWrEYlohBNhCjWhKIbN4LZscg92ZXJpZnlXaXRoRUNEc2EEAAEPAAD9GgpXAQJ5JgQiPXhwaErZKCQGRQkiBsoAFLMkBQkiBmgQs6okEgwNaW52YWxpZCBvd25lcuBoDAH/2zA0HBEMAQLbMDQwQErZKCQGRQkiBsoAFLNAELNAVwACeXhBm/ZnzkHmPxiEQEHmPxiEQEGb9mfOQFcAAnl4QZv2Z85B5j8YhEBB5j8YhEBXAQAMAf/bMDQvcGgLlyYaDBQAAAAAAAAAAAAAAAAAAAAAAAAAACIOaErYJAlKygAUKAM6IgJAVwABeEGb9mfOQZJd6DFAQZJd6DFADBQAAAAAAAAAAAAAAAAAAAAAAAAAAEBXAQE0mkH4J+yMJBMMDm5vdCBhdXRob3JpemVk4HhK2SgkBkUJIgbKABSzJAUJIgZ4ELOqJBYMEWludmFsaWQgbmV3IG93bmVy4DVT////cHgMAf/bMDUV////eGgSwAwMT3duZXJDaGFuZ2VkQZUBb2FAQfgn7IxAVwEADAEB2zA1U////3BoC5cmGgwUAAAAAAAAAAAAAAAAAAAAAAAAAAAiDmhK2CQJSsoAFCgDOiICQFcAATXn/v//Qfgn7IwkEwwObm90IGF1dGhvcml6ZWTgeErZKCQGRQkiBsoAFLMkBQkiBngQs6okIgwdaW52YWxpZCBnb3Zlcm5hbmNlIGNvbnRyb2xsZXLgeAwBAdswNVn+//94EcAMF0dvdmVybmFuY2VDb250cm9sbGVyU2V0QZUBb2FAVwEADAEC2zA1k/7//3BoC5cmBREiHGhK2CYGRRAiBNshShADAAAAAAEAAAC7JAM6IgJAStgmBkUQIgTbIUBXAAE1I/7//0H4J+yMJBMMDm5vdCBhdXRob3JpemVk4HgQtyQcDBdxdW9ydW0gbXVzdCBiZSBwb3NpdGl2ZeB4AQABtiQfDBpxdW9ydW0gZXhjZWVkcyBtYXggc2lnbmVyc+B4DAEC2zA1p/3//3gRwAwMTWluUXVvcnVtU2V0QZUBb2FAVxABeAuXJgUIIgh4ygFBAbUmCAkj2gIAADVk/v//cGgMFAAAAAAAAAAAAAAAAAAAAAAAAAAAlyYICSO0AgAAAT0BeDWsAgAAcWkQtiYFCCIJaQIAABAAtyYICSOTAgAAeMoBQQFpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+1JggJI1UCAABpAUEBeDWAAwAAcmrKE7UmCAkjPgIAAGoQzhGYJggJIzECAAARajXeAwAAc2sQlyYFCCIHawEAAbcmCAkjFAIAABNrAGGgSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfdGrKbJgmCAkjpQEAAAAgARwBeDXPAgAAdRB4NcUDAAB2EHcHE3cIAQABxAB3CRB3CiNlAQAAACFvCGo1pwIAAHcLbwgAIZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfSncIRQBAbwhqNWQCAAB3DG8IAECeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn0p3CEVvC28Hbwk1bgQAACYHI5gAAABvC9soStgkCUrKACEoAzp3DW8NbhLAFQwVaXNTZXF1ZW5jZXJSZWdpc3RlcmVkaEFifVtSdw5vDqomBCJYABdvDNsobw1t2yg3AAB3D28PqiYEIkFvC0pvCW8HUdBFbwdKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ93B0VvCkqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3cKRW8Ka7UlnP7//zVd/P//dwpvB28KuCICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5J4eRKeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84gqEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+SeHkTnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OABioSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5JAVwIDeohwEHEibnh5aZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzkpoaVHQRWlKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9xRWl6tSSRaCICQFcAAnh5znh5EZ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzhioSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn5JKEC4EIgpKAv//AAAyCAL//wAAkUBXAAJ4ec54eRGeSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn84YqEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+SeHkSnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OIKhKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfknh5E55KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzgAYqEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ+SShAuBCIOSgP/////AAAAADIMA/////8AAAAAkUBXBAMQcCOXAAAAeGjOcWkLlyYEIlYIchBzIkRpa856a86YJggJSnJFIjtrSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfc0VrACG1JLpqJgUIIkBoSpxKAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfcEVoebUla////wkiAkDbKErYJAlKygAhKAM6QEFifVtSQDcAAEDbKEDGrdA1").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delGovernanceControllerSet(UInt160? obj);

    [DisplayName("GovernanceControllerSet")]
    public event delGovernanceControllerSet? OnGovernanceControllerSet;

    public delegate void delMinQuorumSet(BigInteger? obj);

    [DisplayName("MinQuorumSet")]
    public event delMinQuorumSet? OnMinQuorumSet;

    public delegate void delOwnerChanged(UInt160? arg1, UInt160? arg2);

    [DisplayName("OwnerChanged")]
    public event delOwnerChanged? OnOwnerChanged;

    #endregion

    #region Properties

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? GovernanceController { [DisplayName("getGovernanceController")] get; [DisplayName("setGovernanceController")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract BigInteger? MinQuorum { [DisplayName("getMinQuorum")] get; [DisplayName("setMinQuorum")] set; }

    /// <summary>
    /// Safe property
    /// </summary>
    public abstract UInt160? Owner { [DisplayName("getOwner")] get; [DisplayName("setOwner")] set; }

    #endregion

    #region Safe methods

    /// <summary>
    /// Safe method
    /// </summary>
    [DisplayName("verifyMultisigProof")]
    public abstract bool? VerifyMultisigProof(byte[]? commitmentBytes);

    #endregion

    #region Unsafe methods

    #endregion
}
