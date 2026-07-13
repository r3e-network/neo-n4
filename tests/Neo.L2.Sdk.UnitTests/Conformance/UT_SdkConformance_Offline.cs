using System.Buffers.Binary;
using System.Net;
using System.Text;
using System.Text.Json;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.Extensions;
using Neo.Extensions.IO;
using Neo.Network.P2P;
using Neo.Network.P2P.Payloads;

namespace Neo.L2.Sdk.UnitTests.Conformance;

[TestClass]
[TestCategory("SdkConformanceOffline")]
public sealed class UT_SdkConformance_Offline
{
    private const string VectorRelativePath = "sdk/conformance/vectors/v1.json";

    [TestMethod]
    public async Task RpcVectors_AllMethodShapesAndU64SerializationConform()
    {
        using var vectors = LoadVectors();
        var rpc = vectors.RootElement.GetProperty("rpc");
        var chainId = rpc.GetProperty("chainId").GetUInt32();

        foreach (var testCase in rpc.GetProperty("cases").EnumerateArray())
        {
            var result = testCase.GetProperty("result").Clone();
            using var handler = new CapturingHandler(request => Success(request, result));
            using var http = new HttpClient(handler);
            using var client = new L2RpcClient("http://node.example:30332", chainId, http);

            await InvokeCaseAsync(client, testCase.GetProperty("name").GetString()!);

            Assert.IsTrue(handler.Captured.HasValue);
            var captured = handler.Captured.Value;
            Assert.AreEqual(testCase.GetProperty("method").GetString(), captured.GetProperty("method").GetString());
            Assert.IsTrue(
                JsonElement.DeepEquals(testCase.GetProperty("params"), captured.GetProperty("params")),
                $"request params drifted for {testCase.GetProperty("name").GetString()}");
        }
    }

    [TestMethod]
    public void HashVector_UInt256LittleEndianAndRpcDisplayConform()
    {
        using var vectors = LoadVectors();
        var hash = vectors.RootElement.GetProperty("hash");
        var wire = Convert.FromHexString(hash.GetProperty("wireLittleEndianHex").GetString()!);

        Array.Reverse(wire);

        Assert.AreEqual(hash.GetProperty("rpcDisplay").GetString(), $"0x{Convert.ToHexString(wire).ToLowerInvariant()}");
    }

    [TestMethod]
    public async Task ErrorVectors_ServerIdAndVersionMapToCanonicalTaxonomy()
    {
        using var vectors = LoadVectors();
        var rpc = vectors.RootElement.GetProperty("rpc");
        var chainId = rpc.GetProperty("chainId").GetUInt32();

        foreach (var errorCase in rpc.GetProperty("errors").EnumerateArray())
        {
            using var handler = new CapturingHandler(request => ErrorResponse(request, errorCase));
            using var http = new HttpClient(handler);
            using var client = new L2RpcClient("http://node.example:30332", chainId, http);
            var expected = errorCase.GetProperty("expected").GetString();

            if (expected == "server")
            {
                var error = await Assert.ThrowsExactlyAsync<L2RpcServerException>(
                    async () => await client.GetLatestStateRootAsync());
                Assert.AreEqual(-32000, error.Code);
            }
            else
            {
                await Assert.ThrowsExactlyAsync<L2RpcProtocolException>(
                    async () => await client.GetLatestStateRootAsync());
            }
        }
    }

    [TestMethod]
    public async Task ResponseErrorVectors_ChainShapeAndHexFailuresFailClosed()
    {
        using var vectors = LoadVectors();
        var rpc = vectors.RootElement.GetProperty("rpc");
        var chainId = rpc.GetProperty("chainId").GetUInt32();

        foreach (var errorCase in rpc.GetProperty("responseErrors").EnumerateArray())
        {
            using var handler = new CapturingHandler(request => Success(request, errorCase.GetProperty("result")));
            using var http = new HttpClient(handler);
            using var client = new L2RpcClient("http://node.example:30332", chainId, http);
            var action = InvokeResponseErrorCaseAsync(client, errorCase.GetProperty("name").GetString()!);

            if (errorCase.GetProperty("expected").GetString() == "chain")
                await Assert.ThrowsExactlyAsync<L2RpcMismatchedChainIdException>(async () => await action());
            else
                await Assert.ThrowsExactlyAsync<L2RpcProtocolException>(async () => await action());
        }
    }

    [TestMethod]
    public void DomainVector_BindsL1ReservationL2ChainAndNetworkMagic()
    {
        using var vectors = LoadVectors();
        var domain = vectors.RootElement.GetProperty("domain");
        var transaction = vectors.RootElement.GetProperty("transaction");
        var network = domain.GetProperty("networkMagic").GetUInt32();

        Assert.AreEqual(0U, domain.GetProperty("l1ReservedChainId").GetUInt32());
        Assert.AreEqual(
            vectors.RootElement.GetProperty("rpc").GetProperty("chainId").GetUInt32(),
            domain.GetProperty("l2ChainId").GetUInt32());
        Assert.AreEqual(transaction.GetProperty("network").GetUInt32(), network);
        Span<byte> networkBytes = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(networkBytes, network);
        Assert.AreEqual(
            domain.GetProperty("networkMagicLittleEndianHex").GetString(),
            Convert.ToHexString(networkBytes).ToLowerInvariant());
        Assert.IsTrue(transaction.GetProperty("signDataHex").GetString()!.StartsWith(
            domain.GetProperty("networkMagicLittleEndianHex").GetString()!,
            StringComparison.Ordinal));
    }

    [TestMethod]
    public void PaginationVector_CursorsAndU64ValuesRoundTripWithoutLoss()
    {
        using var vectors = LoadVectors();
        var pagination = vectors.RootElement.GetProperty("pagination");
        var serialized = JsonSerializer.Serialize(pagination);
        using var roundTrip = JsonDocument.Parse(serialized);
        var numbers = roundTrip.RootElement.GetProperty("pages")
            .EnumerateArray()
            .SelectMany(page => page.GetProperty("items").EnumerateArray())
            .Select(item => item.GetProperty("batchNumber").GetString())
            .ToArray();
        var expected = pagination.GetProperty("expectedBatchNumbers")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();

        CollectionAssert.AreEqual(expected, numbers);
        Assert.AreEqual("batch:9007199254740994", roundTrip.RootElement.GetProperty("pages")[0].GetProperty("nextCursor").GetString());
        Assert.AreEqual(JsonValueKind.Null, roundTrip.RootElement.GetProperty("pages")[1].GetProperty("nextCursor").ValueKind);
    }

    [TestMethod]
    public void TransactionVector_DeserializesHashesSignsAndRoundTrips()
    {
        using var vectors = LoadVectors();
        var vector = vectors.RootElement.GetProperty("transaction");
        var raw = Convert.FromHexString(vector.GetProperty("rawTransactionHex").GetString()!);
        var unsigned = Convert.FromHexString(vector.GetProperty("unsignedTransactionHex").GetString()!);
        var signData = Convert.FromHexString(vector.GetProperty("signDataHex").GetString()!);
        var signature = Convert.FromHexString(vector.GetProperty("signatureHex").GetString()!);
        var publicKey = Convert.FromHexString(vector.GetProperty("publicKeyCompressedHex").GetString()!);
        var transaction = raw.AsSerializable<Transaction>();

        CollectionAssert.AreEqual(raw, transaction.ToArray());
        Assert.AreEqual(vector.GetProperty("txid").GetString(), transaction.Hash.ToString());
        Assert.AreEqual(vector.GetProperty("accountRpcDisplay").GetString(), transaction.Signers[0].Account.ToString());
        CollectionAssert.AreEqual(signData, transaction.GetSignData(vector.GetProperty("network").GetUInt32()));
        CollectionAssert.AreEqual(unsigned, raw[..unsigned.Length]);
        CollectionAssert.AreEqual(
            Convert.FromHexString(vector.GetProperty("verificationScriptHex").GetString()!),
            transaction.Witnesses[0].VerificationScript.ToArray());
        CollectionAssert.AreEqual(signature, transaction.Witnesses[0].InvocationScript[2..].ToArray());
        Assert.IsTrue(Crypto.VerifySignature(signData, signature, publicKey, ECCurve.Secp256r1));
    }

    private static async Task InvokeCaseAsync(L2RpcClient client, string name)
    {
        switch (name)
        {
            case "batch-missing-max-u64":
                Assert.IsNull(await client.GetBatchAsync(ulong.MaxValue));
                break;
            case "batch-complete-large-u64":
                {
                    var batch = await client.GetBatchAsync(9_007_199_254_740_993UL);
                    Assert.IsNotNull(batch);
                    Assert.AreEqual(9_007_199_254_740_993UL, batch.BatchNumber);
                    Assert.AreEqual("AABBCC", Convert.ToHexString(batch.Proof));
                    break;
                }
            case "batch-status":
                Assert.AreEqual(
                    9_007_199_254_740_993UL,
                    (await client.GetBatchStatusAsync(9_007_199_254_740_993UL)).BatchNumber);
                break;
            case "latest-state-root":
                Assert.AreEqual("0x" + new string('2', 64), (await client.GetLatestStateRootAsync()).ToString());
                break;
            case "state-root-max-u64":
                Assert.AreEqual("0x" + new string('3', 64), (await client.GetStateRootAtAsync(ulong.MaxValue)).ToString());
                break;
            case "withdrawal-proof":
                CollectionAssert.AreEqual(
                    Convert.FromHexString("CAFEBABE"),
                    await client.GetWithdrawalProofAsync(UInt256.Parse("0x" + new string('4', 64))));
                break;
            case "message-proof":
                CollectionAssert.AreEqual(
                    Convert.FromHexString("DEADBEEF"),
                    await client.GetMessageProofAsync(UInt256.Parse("0x" + new string('5', 64))));
                break;
            case "deposit-status-max-u64":
                {
                    var deposit = await client.GetDepositStatusAsync(1, ulong.MaxValue);
                    Assert.IsNotNull(deposit);
                    Assert.AreEqual(ulong.MaxValue, deposit.Nonce);
                    Assert.AreEqual(9_007_199_254_740_993UL, deposit.IncludedInBatch);
                    break;
                }
            case "canonical-asset":
                Assert.AreEqual("0x" + new string('b', 40), (await client.GetCanonicalAssetAsync(UInt160.Parse("0x" + new string('a', 40))))!.ToString());
                break;
            case "bridged-asset":
                Assert.AreEqual("0x" + new string('a', 40), (await client.GetBridgedAssetAsync(UInt160.Parse("0x" + new string('b', 40))))!.ToString());
                break;
            case "security-level":
                Assert.AreEqual(SecurityLevel.Validity, (await client.GetSecurityLevelAsync()).Level);
                break;
            case "security-label":
                Assert.AreEqual(SecurityLevel.Validium, (await client.GetSecurityLabelAsync()).SecurityLevel);
                break;
            default:
                Assert.Fail($"unknown conformance case {name}");
                break;
        }
    }

    private static Func<Task> InvokeResponseErrorCaseAsync(L2RpcClient client, string name)
        => name switch
        {
            "mismatched-chain-id" => async () => await client.GetSecurityLabelAsync(),
            "invalid-withdrawal-proof-hex" => async () => await client.GetWithdrawalProofAsync(
                UInt256.Parse("0x" + new string('4', 64))),
            "wrong-state-root-type" => async () => await client.GetLatestStateRootAsync(),
            "unsafe-numeric-u64" => async () => await client.GetDepositStatusAsync(1, 9_007_199_254_740_992UL),
            _ => throw new AssertFailedException($"unknown response-error conformance case {name}"),
        };

    private static JsonDocument LoadVectors()
    {
        var configured = Environment.GetEnvironmentVariable("NEO_SDK_CONFORMANCE_VECTORS");
        if (!string.IsNullOrWhiteSpace(configured))
            return JsonDocument.Parse(File.ReadAllText(configured));

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, VectorRelativePath);
            if (File.Exists(candidate))
                return JsonDocument.Parse(File.ReadAllText(candidate));
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"cannot locate {VectorRelativePath}");
    }

    private static string Success(JsonElement request, JsonElement result)
        => JsonSerializer.Serialize(new
        {
            jsonrpc = "2.0",
            id = request.GetProperty("id").GetInt64(),
            result,
        });

    private static string ErrorResponse(JsonElement request, JsonElement errorCase)
    {
        var id = request.GetProperty("id").GetInt64() + (errorCase.TryGetProperty("idOffset", out var offset) ? offset.GetInt64() : 0);
        var jsonrpc = errorCase.GetProperty("jsonrpc").GetString();
        if (errorCase.TryGetProperty("error", out var error))
            return JsonSerializer.Serialize(new { jsonrpc, id, error });
        return JsonSerializer.Serialize(new { jsonrpc, id, result = errorCase.GetProperty("result") });
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly Func<JsonElement, string> _response;

        public CapturingHandler(Func<JsonElement, string> response)
        {
            _response = response;
        }

        public JsonElement? Captured { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var document = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Captured = document.RootElement.Clone();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_response(Captured.Value), Encoding.UTF8, "application/json"),
            };
        }
    }
}
