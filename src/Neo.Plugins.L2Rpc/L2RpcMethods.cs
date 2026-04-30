using System.Numerics;
using Neo.Json;
using Neo.L2;
using Neo.L2.Batch;

namespace Neo.Plugins.L2Rpc;

/// <summary>
/// All L2 RPC methods listed in doc.md §14.1, implemented as plain methods that take
/// <see cref="JArray"/> params and return <see cref="JToken"/> — exactly the shape Neo's
/// <c>RpcServer</c> dispatcher expects.
/// </summary>
/// <remarks>
/// Held separately from <c>RpcServer</c> integration so the methods can be unit-tested without
/// spinning up a node. The <c>Neo.Plugins.L2Rpc.RpcServerExtensions</c> partial class (added
/// alongside neo's <c>RpcServer</c> when its source is available) wraps these into
/// <c>[RpcMethod]</c>-attributed entry points.
/// </remarks>
public sealed class L2RpcMethods
{
    private readonly IL2RpcStore _store;

    /// <summary>Construct against a backing store.</summary>
    public L2RpcMethods(IL2RpcStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        _store = store;
    }

    /// <summary>RPC: <c>getl2batch</c>.</summary>
    public JToken?GetL2Batch(JArray @params)
    {
        var (chainId, batchNumber) = ReadChainAndBatch(@params);
        AssertOurChain(chainId);
        var batch = _store.GetBatch(batchNumber);
        return batch is null ? JToken.Null : EncodeBatch(batch);
    }

    /// <summary>RPC: <c>getl2batchstatus</c>.</summary>
    public JToken?GetL2BatchStatus(JArray @params)
    {
        var (chainId, batchNumber) = ReadChainAndBatch(@params);
        AssertOurChain(chainId);
        var status = _store.GetBatchStatus(batchNumber);
        var obj = new JObject();
        obj["chainId"] = chainId;
        obj["batchNumber"] = batchNumber;
        obj["status"] = (byte)status;
        obj["statusName"] = status.ToString();
        return obj;
    }

    /// <summary>RPC: <c>getl2stateroot</c>. Optional batch param; otherwise latest.</summary>
    public JToken?GetL2StateRoot(JArray @params)
    {
        var chainId = (uint)ReadULong(@params, 0);
        AssertOurChain(chainId);
        if (@params.Count >= 2 && @params[1] is not null)
        {
            var batchNumber = ReadULong(@params, 1);
            return new JString(_store.GetStateRootAtBatch(batchNumber).ToString());
        }
        return new JString(_store.GetLatestStateRoot().ToString());
    }

    /// <summary>RPC: <c>getl2withdrawalproof</c>.</summary>
    public JToken?GetL2WithdrawalProof(JArray @params)
    {
        var chainId = (uint)ReadULong(@params, 0);
        AssertOurChain(chainId);
        var leaf = ReadUInt256(@params, 1);
        var proof = _store.GetWithdrawalProof(leaf);
        return proof is null ? JToken.Null : new JString(Convert.ToHexString(proof.Value.Span));
    }

    /// <summary>RPC: <c>getl2messageproof</c>.</summary>
    public JToken?GetL2MessageProof(JArray @params)
    {
        var chainId = (uint)ReadULong(@params, 0);
        AssertOurChain(chainId);
        var msgHash = ReadUInt256(@params, 1);
        var proof = _store.GetMessageProof(msgHash);
        return proof is null ? JToken.Null : new JString(Convert.ToHexString(proof.Value.Span));
    }

    /// <summary>RPC: <c>getl1depositstatus</c>.</summary>
    public JToken?GetL1DepositStatus(JArray @params)
    {
        var sourceChainId = (uint)ReadULong(@params, 0);
        var nonce = ReadULong(@params, 1);
        var status = _store.GetL1DepositStatus(sourceChainId, nonce);
        if (status is null) return JToken.Null;
        var s = status.Value;
        var obj = new JObject();
        obj["sourceChainId"] = s.SourceChainId;
        obj["nonce"] = s.Nonce;
        obj["consumedOnL2"] = s.ConsumedOnL2;
        obj["includedInBatch"] = s.IncludedInBatch.HasValue ? (JToken)s.IncludedInBatch.Value : JToken.Null;
        return obj;
    }

    /// <summary>RPC: <c>getcanonicalasset</c>.</summary>
    public JToken?GetCanonicalAsset(JArray @params)
    {
        var l2Asset = ReadUInt160(@params, 0);
        var l1 = _store.GetCanonicalAsset(l2Asset);
        return l1 is null ? JToken.Null : new JString(l1.ToString());
    }

    /// <summary>RPC: <c>getbridgedasset</c>.</summary>
    public JToken?GetBridgedAsset(JArray @params)
    {
        var l1Asset = ReadUInt160(@params, 0);
        var l2 = _store.GetBridgedAsset(l1Asset);
        return l2 is null ? JToken.Null : new JString(l2.ToString());
    }

    /// <summary>RPC: <c>getsecuritylevel</c>.</summary>
    public JToken?GetSecurityLevel(JArray @params)
    {
        var chainId = (uint)ReadULong(@params, 0);
        AssertOurChain(chainId);
        var lvl = _store.SecurityLevel;
        var obj = new JObject();
        obj["chainId"] = chainId;
        obj["level"] = (byte)lvl;
        obj["levelName"] = lvl.ToString();
        return obj;
    }

    private void AssertOurChain(uint chainId)
    {
        if (chainId != _store.ChainId)
            throw new ArgumentException($"chainId {chainId} differs from local {_store.ChainId}");
    }

    private static (uint chainId, ulong batchNumber) ReadChainAndBatch(JArray @params)
        => ((uint)ReadULong(@params, 0), ReadULong(@params, 1));

    private static ulong ReadULong(JArray @params, int idx)
    {
        var token = @params[idx] ?? throw new ArgumentException($"param[{idx}] missing");
        return token switch
        {
            JNumber n => checked((ulong)(BigInteger)n.AsNumber()),
            JString s => ulong.Parse(s.AsString()),
            _ => throw new ArgumentException($"param[{idx}] not a number"),
        };
    }

    private static UInt256 ReadUInt256(JArray @params, int idx)
    {
        var token = @params[idx] ?? throw new ArgumentException($"param[{idx}] missing");
        return UInt256.Parse(token.AsString());
    }

    private static UInt160 ReadUInt160(JArray @params, int idx)
    {
        var token = @params[idx] ?? throw new ArgumentException($"param[{idx}] missing");
        return UInt160.Parse(token.AsString());
    }

    private static JToken? EncodeBatch(L2BatchCommitment batch)
    {
        var bytes = BatchSerializer.Encode(batch);
        var obj = new JObject();
        obj["chainId"] = batch.ChainId;
        obj["batchNumber"] = batch.BatchNumber;
        obj["firstBlock"] = batch.FirstBlock;
        obj["lastBlock"] = batch.LastBlock;
        obj["preStateRoot"] = batch.PreStateRoot.ToString();
        obj["postStateRoot"] = batch.PostStateRoot.ToString();
        obj["txRoot"] = batch.TxRoot.ToString();
        obj["receiptRoot"] = batch.ReceiptRoot.ToString();
        obj["withdrawalRoot"] = batch.WithdrawalRoot.ToString();
        obj["l2ToL1MessageRoot"] = batch.L2ToL1MessageRoot.ToString();
        obj["l2ToL2MessageRoot"] = batch.L2ToL2MessageRoot.ToString();
        obj["daCommitment"] = batch.DACommitment.ToString();
        obj["publicInputHash"] = batch.PublicInputHash.ToString();
        obj["proofType"] = (byte)batch.ProofType;
        obj["proof"] = Convert.ToHexString(batch.Proof.Span);
        obj["encoded"] = Convert.ToHexString(bytes);
        return obj;
    }
}
