using System;
using System.Net.Http;
using System.Threading.Tasks;
using Neo;
using Neo.Json;
using Neo.L2;
using Neo.L2.Sdk;

namespace Neo.L2.Sdk.UnitTests;

/// <summary>
/// Happy-path tests for <see cref="L2RpcClient"/> — feeds canned responses through
/// a <see cref="StubHttpHandler"/> and asserts each method
///   (a) calls the documented JSON-RPC method name,
///   (b) builds the expected params array shape,
///   (c) decodes the response into the typed record correctly.
/// Pins the wire-protocol contract between the SDK and Neo.Plugins.L2Rpc.
/// </summary>
[TestClass]
public class UT_L2RpcClient_HappyPath
{
    private const uint TestChainId = 1099;
    private const string Endpoint = "http://node.example:30332";

    private static (StubHttpHandler stub, HttpClient http) NewHttp()
    {
        var stub = new StubHttpHandler();
        var http = new HttpClient(stub);
        return (stub, http);
    }

    [TestMethod]
    public async Task GetLatestStateRoot_ReturnsTypedUInt256()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        var expected = UInt256.Parse("0x" + new string('a', 64));
        stub.EnqueueResult(() => expected.ToString());

        var got = await client.GetLatestStateRootAsync();

        Assert.AreEqual(expected, got);
        Assert.AreEqual(1, stub.Captured.Count);
        Assert.AreEqual("getl2stateroot", stub.Captured[0].Method);
        Assert.AreEqual(1, stub.Captured[0].Params.Count);
        Assert.AreEqual((double)TestChainId, ((JNumber)stub.Captured[0].Params[0]!).AsNumber());
    }

    [TestMethod]
    public async Task GetStateRootAt_PinsBatchNumberInParams()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        var expected = UInt256.Parse("0x" + new string('b', 64));
        stub.EnqueueResult(() => expected.ToString());

        var got = await client.GetStateRootAtAsync(batchNumber: 42);

        Assert.AreEqual(expected, got);
        Assert.AreEqual("getl2stateroot", stub.Captured[0].Method);
        Assert.AreEqual(2, stub.Captured[0].Params.Count);
        Assert.AreEqual("42", stub.Captured[0].Params[1]!.AsString());
    }

    [TestMethod]
    public async Task GetWithdrawalProof_DecodesHexBytes()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        var expected = new byte[] { 0xCA, 0xFE, 0xBA, 0xBE, 0xDE, 0xAD };
        stub.EnqueueResult(() => Convert.ToHexString(expected));

        var leaf = UInt256.Parse("0x" + new string('c', 64));
        var got = await client.GetWithdrawalProofAsync(leaf);

        CollectionAssert.AreEqual(expected, got);
        Assert.AreEqual("getl2withdrawalproof", stub.Captured[0].Method);
        Assert.AreEqual(leaf.ToString(), stub.Captured[0].Params[1]!.AsString());
    }

    [TestMethod]
    public async Task GetWithdrawalProof_ReturnsNullForUnknownLeaf()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        // L2RpcMethods returns JToken.Null (= literal null in the JSON-RPC envelope) when the leaf is unknown.
        stub.EnqueueResult(() => null);

        var got = await client.GetWithdrawalProofAsync(UInt256.Zero);

        Assert.IsNull(got);
    }

    [TestMethod]
    public async Task GetMessageProof_DecodesHexBytes()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        var expected = new byte[] { 0x01, 0x02, 0x03 };
        stub.EnqueueResult(() => Convert.ToHexString(expected));

        var hash = UInt256.Parse("0x" + new string('d', 64));
        var got = await client.GetMessageProofAsync(hash);

        CollectionAssert.AreEqual(expected, got);
        Assert.AreEqual("getl2messageproof", stub.Captured[0].Method);
    }

    [TestMethod]
    public async Task GetCanonicalAsset_ReturnsTypedUInt160()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        var expected = UInt160.Parse("0x" + new string('1', 40));
        stub.EnqueueResult(() => expected.ToString());

        var l2Asset = UInt160.Parse("0x" + new string('2', 40));
        var got = await client.GetCanonicalAssetAsync(l2Asset);

        Assert.AreEqual(expected, got);
        Assert.AreEqual("getcanonicalasset", stub.Captured[0].Method);
        Assert.AreEqual(l2Asset.ToString(), stub.Captured[0].Params[0]!.AsString());
    }

    [TestMethod]
    public async Task GetBridgedAsset_ReturnsTypedUInt160()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        var expected = UInt160.Parse("0x" + new string('3', 40));
        stub.EnqueueResult(() => expected.ToString());

        var l1Asset = UInt160.Parse("0x" + new string('4', 40));
        var got = await client.GetBridgedAssetAsync(l1Asset);

        Assert.AreEqual(expected, got);
        Assert.AreEqual("getbridgedasset", stub.Captured[0].Method);
        Assert.AreEqual(2, stub.Captured[0].Params.Count);
        Assert.AreEqual((double)TestChainId, stub.Captured[0].Params[1]!.AsNumber());
    }

    [TestMethod]
    public async Task GetSecurityLevel_DecodesEnumByByteAndName()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        stub.EnqueueResult(() =>
        {
            var obj = new JObject();
            obj["chainId"] = TestChainId;
            obj["level"] = (byte)SecurityLevel.Optimistic;
            obj["levelName"] = SecurityLevel.Optimistic.ToString();
            return obj;
        });

        var got = await client.GetSecurityLevelAsync();

        Assert.AreEqual(TestChainId, got.ChainId);
        Assert.AreEqual(SecurityLevel.Optimistic, got.Level);
    }

    [TestMethod]
    public async Task GetSecurityLabel_DecodesAllFiveDimensions()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        stub.EnqueueResult(() =>
        {
            var obj = new JObject();
            obj["chainId"] = TestChainId;
            obj["securityLevel"] = (byte)SecurityLevel.Validium;
            obj["securityLevelName"] = SecurityLevel.Validium.ToString();
            obj["daMode"] = (byte)DAMode.NeoFS;
            obj["daModeName"] = DAMode.NeoFS.ToString();
            obj["gatewayEnabled"] = true;
            obj["sequencer"] = (byte)SequencerModel.DbftCommittee;
            obj["sequencerName"] = SequencerModel.DbftCommittee.ToString();
            obj["exit"] = (byte)ExitModel.Delayed;
            obj["exitName"] = ExitModel.Delayed.ToString();
            return obj;
        });

        var got = await client.GetSecurityLabelAsync();

        Assert.AreEqual(TestChainId, got.ChainId);
        Assert.AreEqual(SecurityLevel.Validium, got.SecurityLevel);
        Assert.AreEqual(DAMode.NeoFS, got.DAMode);
        Assert.IsTrue(got.GatewayEnabled);
        Assert.AreEqual(SequencerModel.DbftCommittee, got.Sequencer);
        Assert.AreEqual(ExitModel.Delayed, got.Exit);
    }

    [TestMethod]
    public async Task GetBatchStatus_DecodesEnum()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        stub.EnqueueResult(() =>
        {
            var obj = new JObject();
            obj["chainId"] = TestChainId;
            obj["batchNumber"] = "7";
            obj["status"] = (byte)BatchStatus.Finalized;
            obj["statusName"] = BatchStatus.Finalized.ToString();
            return obj;
        });

        var got = await client.GetBatchStatusAsync(7);

        Assert.AreEqual(7UL, got.BatchNumber);
        Assert.AreEqual(BatchStatus.Finalized, got.Status);
        Assert.AreEqual(nameof(BatchStatus.Finalized), got.StatusName);
    }

    [TestMethod]
    public async Task GetDepositStatus_DecodesNullableIncludedInBatch()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        stub.EnqueueResult(() =>
        {
            var obj = new JObject();
            obj["sourceChainId"] = 1u;
            obj["nonce"] = "42";
            obj["consumedOnL2"] = false;
            obj["includedInBatch"] = JToken.Null;
            return obj;
        });

        var got = await client.GetDepositStatusAsync(1, 42);

        Assert.IsNotNull(got);
        Assert.AreEqual(1u, got!.SourceChainId);
        Assert.AreEqual(42UL, got.Nonce);
        Assert.IsFalse(got.ConsumedOnL2);
        Assert.IsNull(got.IncludedInBatch);
    }

    [TestMethod]
    public async Task GetDepositStatus_ReturnsNullWhenServerSaysNotTracked()
    {
        var (stub, http) = NewHttp();
        using var client = new L2RpcClient(Endpoint, TestChainId, http);
        stub.EnqueueResult(() => null);

        var got = await client.GetDepositStatusAsync(1, 42);

        Assert.IsNull(got);
    }
}
