using Neo.Cryptography.ECC;
using Neo.Extensions;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;

#pragma warning disable CS0067

namespace Neo.SmartContract.Testing;

public abstract class NeoHubGovernanceFraudVerifier(Neo.SmartContract.Testing.SmartContractInitialize initialize) : Neo.SmartContract.Testing.SmartContract(initialize), IContractInfo
{
    #region Compiled data

    public static Neo.SmartContract.Manifest.ContractManifest Manifest => Neo.SmartContract.Manifest.ContractManifest.Parse(@"{""name"":""NeoHub.GovernanceFraudVerifier"",""groups"":[],""features"":{},""supportedstandards"":[],""abi"":{""methods"":[{""name"":""verifyFraud"",""parameters"":[{""name"":""chainId"",""type"":""Integer""},{""name"":""batchNumber"",""type"":""Integer""},{""name"":""payload"",""type"":""ByteArray""}],""returntype"":""Boolean"",""offset"":0,""safe"":false}],""events"":[{""name"":""FraudProofAccepted"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Hash256""},{""name"":""arg4"",""type"":""Hash256""}]},{""name"":""FraudProofRejected"",""parameters"":[{""name"":""arg1"",""type"":""Integer""},{""name"":""arg2"",""type"":""Integer""},{""name"":""arg3"",""type"":""Integer""}]}]},""permissions"":[{""contract"":""*"",""methods"":""*""}],""trusts"":[],""extra"":{""Author"":""R3E Network"",""Description"":""Structural fraud verifier for governance-arbitration optimistic rollups."",""Version"":""0.1.0"",""Sourcecode"":""https://github.com/r3e-network/neo-n4/tree/master/contracts/NeoHub.GovernanceFraudVerifier"",""nef"":{""optimization"":""Basic""}}}");

    /// <summary>
    /// Optimization: "Basic"
    /// </summary>
    public static Neo.SmartContract.NefFile Nef => Convert.FromBase64String(@"TkVGM05lby5Db21waWxlci5DU2hhcnAgMy45LjErNWZhOTU2NmU1MTY1ZWRlMjE2NWE5YmUxZjRhMDEyMGMxNzYuLi4AAAAAAP1UA1cDA3rKEbUmJhF5eBPADBJGcmF1ZFByb29mUmVqZWN0ZWRBlQFvYQkj2gEAAHoQznBoEZcmMnrKAGWYJiYReXgTwAwSRnJhdWRQcm9vZlJlamVjdGVkQZUBb2EJI6YBAAAjQAEAAGgSlycXAQAAesoAabUmJhF5eBPADBJGcmF1ZFByb29mUmVqZWN0ZWRBlQFvYQkjbgEAAHoAZc56AGbOGKhKEC4EIg5KA/////8AAAAAMgwD/////wAAAACRknoAZ84gqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGSegBozgAYqEoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGScWkCAAABALcmJhR5eBPADBJGcmF1ZFByb29mUmVqZWN0ZWRBlQFvYQkj0gAAAHrKAGlpnkoQLgQiDkoD/////wAAAAAyDAP/////AAAAAJGYJiYReXgTwAwSRnJhdWRQcm9vZlJlamVjdGVkQZUBb2EJI4kAAAAiIxJ5eBPADBJGcmF1ZFByb29mUmVqZWN0ZWRBlQFvYQkiYwAgAEF6ACF6NFomIxN5eBPADBJGcmF1ZFByb29mUmVqZWN0ZWRBlQFvYQkiNgAhejXnAAAAcQBBejXeAAAAcmppeXgUwAwSRnJhdWRQcm9vZkFjY2VwdGVkQZUBb2EIIgJAVwEFEHAjpAAAAHh5aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfznp7aJ5KAgAAAIAuBCIKSgL///9/Mh4D/////wAAAACRSgL///9/MgwDAAAAAAEAAACfzpgmBQkiQGhKnEoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ9wRWh8tSVe////CCICQFcCAgAgiHAQcSJueHlpnkoCAAAAgC4EIgpKAv///38yHgP/////AAAAAJFKAv///38yDAMAAAAAAQAAAJ/OSmhpUdBFaUqcSgIAAACALgQiCkoC////fzIeA/////8AAAAAkUoC////fzIMAwAAAAABAAAAn3FFaQAgtSSQaNsoStgkCUrKACAoAzoiAkDbKErYJAlKygAgKAM6QNjWl9I=").AsSerializable<Neo.SmartContract.NefFile>();

    #endregion

    #region Events

    public delegate void delFraudProofAccepted(BigInteger? arg1, BigInteger? arg2, UInt256? arg3, UInt256? arg4);

    [DisplayName("FraudProofAccepted")]
    public event delFraudProofAccepted? OnFraudProofAccepted;

    public delegate void delFraudProofRejected(BigInteger? arg1, BigInteger? arg2, BigInteger? arg3);

    [DisplayName("FraudProofRejected")]
    public event delFraudProofRejected? OnFraudProofRejected;

    #endregion

    #region Unsafe methods

    /// <summary>
    /// Unsafe method
    /// </summary>
    [DisplayName("verifyFraud")]
    public abstract bool? VerifyFraud(BigInteger? chainId, BigInteger? batchNumber, byte[]? payload);

    #endregion
}
