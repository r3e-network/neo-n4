using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Neo.L2.Sdk.UnitTests.Conformance;

[TestClass]
[TestCategory("SdkConformanceLive")]
public sealed partial class UT_SdkConformance_Live
{
    private static readonly string[] RequiredVariables =
    [
        "NEO_SDK_LIVE",
        "NEO_N3_RPC_URL",
        "NEO_N4_RPC_URL",
        "NEO_N4_CHAIN_ID",
        "NEO_SDK_LIVE_FIXTURE",
    ];

    [TestMethod]
    public async Task N3Node_BaseRpcMethodsMatchDeploymentFixture()
    {
        var configuration = RequireConfiguration();
        await AssertBaseNodeAsync(configuration.N3RpcUrl, configuration.N3Fixture);
    }

    [TestMethod]
    public async Task N4Node_BaseRpcMethodsMatchDeploymentFixture()
    {
        var configuration = RequireConfiguration();
        await AssertBaseNodeAsync(configuration.N4RpcUrl, configuration.N4Fixture);
    }

    [TestMethod]
    public async Task N4Node_AllTypedL2QueriesAndWrongChainFailureMatchDeploymentFixture()
    {
        var configuration = RequireConfiguration();
        using var client = new L2RpcClient(configuration.N4RpcUrl, configuration.N4ChainId);

        foreach (var testCase in configuration.N4Fixture.GetProperty("cases").EnumerateArray())
        {
            var actual = await RawRpcAsync(
                configuration.N4RpcUrl,
                testCase.GetProperty("method").GetString()!,
                testCase.GetProperty("params"));
            Assert.IsTrue(
                JsonElement.DeepEquals(testCase.GetProperty("result"), actual),
                $"live RPC result drifted for {testCase.GetProperty("name").GetString()}");
            await AssertTypedCaseAsync(client, testCase);
        }

        var wrongChainId = configuration.N4Fixture.GetProperty("wrongChainId").GetUInt32();
        using var wrongClient = new L2RpcClient(configuration.N4RpcUrl, wrongChainId);
        await Assert.ThrowsExactlyAsync<L2RpcServerException>(
            async () => await wrongClient.GetSecurityLabelAsync());
    }

    private static async Task AssertTypedCaseAsync(L2RpcClient client, JsonElement testCase)
    {
        var name = testCase.GetProperty("name").GetString()!;
        var parameters = testCase.GetProperty("params");
        var expected = testCase.GetProperty("result");
        switch (name)
        {
            case "batch":
                {
                    var batch = await client.GetBatchAsync(ReadUInt64(parameters[1]));
                    Assert.IsNotNull(batch);
                    Assert.AreEqual(expected.GetProperty("chainId").GetUInt32(), batch.ChainId);
                    Assert.AreEqual(ReadUInt64(expected.GetProperty("batchNumber")), batch.BatchNumber);
                    Assert.AreEqual(ReadUInt64(expected.GetProperty("firstBlock")), batch.FirstBlock);
                    Assert.AreEqual(ReadUInt64(expected.GetProperty("lastBlock")), batch.LastBlock);
                    Assert.AreEqual(expected.GetProperty("preStateRoot").GetString(), batch.PreStateRoot.ToString());
                    Assert.AreEqual(expected.GetProperty("postStateRoot").GetString(), batch.PostStateRoot.ToString());
                    Assert.AreEqual(expected.GetProperty("txRoot").GetString(), batch.TxRoot.ToString());
                    Assert.AreEqual(expected.GetProperty("receiptRoot").GetString(), batch.ReceiptRoot.ToString());
                    Assert.AreEqual(expected.GetProperty("withdrawalRoot").GetString(), batch.WithdrawalRoot.ToString());
                    Assert.AreEqual(expected.GetProperty("l2ToL1MessageRoot").GetString(), batch.L2ToL1MessageRoot.ToString());
                    Assert.AreEqual(expected.GetProperty("l2ToL2MessageRoot").GetString(), batch.L2ToL2MessageRoot.ToString());
                    Assert.AreEqual(expected.GetProperty("daCommitment").GetString(), batch.DACommitment.ToString());
                    Assert.AreEqual(expected.GetProperty("publicInputHash").GetString(), batch.PublicInputHash.ToString());
                    Assert.AreEqual(expected.GetProperty("proofType").GetByte(), (byte)batch.ProofType);
                    Assert.AreEqual(expected.GetProperty("proof").GetString(), Convert.ToHexString(batch.Proof));
                    Assert.AreEqual(expected.GetProperty("encoded").GetString(), Convert.ToHexString(batch.EncodedWireFormat));
                    break;
                }
            case "batch-status":
                {
                    var status = await client.GetBatchStatusAsync(ReadUInt64(parameters[1]));
                    Assert.AreEqual(expected.GetProperty("chainId").GetUInt32(), status.ChainId);
                    Assert.AreEqual(ReadUInt64(expected.GetProperty("batchNumber")), status.BatchNumber);
                    Assert.AreEqual(expected.GetProperty("status").GetByte(), (byte)status.Status);
                    Assert.AreEqual(expected.GetProperty("statusName").GetString(), status.StatusName);
                    break;
                }
            case "latest-state-root":
                Assert.AreEqual(expected.GetString(), (await client.GetLatestStateRootAsync()).ToString());
                break;
            case "historical-state-root":
                Assert.AreEqual(
                    expected.GetString(),
                    (await client.GetStateRootAtAsync(ReadUInt64(parameters[1]))).ToString());
                break;
            case "withdrawal-proof":
                Assert.AreEqual(
                    expected.GetString(),
                    Convert.ToHexString((await client.GetWithdrawalProofAsync(UInt256.Parse(parameters[1].GetString()!)))!));
                break;
            case "message-proof":
                Assert.AreEqual(
                    expected.GetString(),
                    Convert.ToHexString((await client.GetMessageProofAsync(UInt256.Parse(parameters[1].GetString()!)))!));
                break;
            case "deposit-status":
                {
                    var deposit = await client.GetDepositStatusAsync(parameters[0].GetUInt32(), ReadUInt64(parameters[1]));
                    Assert.IsNotNull(deposit);
                    Assert.AreEqual(expected.GetProperty("sourceChainId").GetUInt32(), deposit.SourceChainId);
                    Assert.AreEqual(ReadUInt64(expected.GetProperty("nonce")), deposit.Nonce);
                    Assert.AreEqual(expected.GetProperty("consumedOnL2").GetBoolean(), deposit.ConsumedOnL2);
                    var included = expected.GetProperty("includedInBatch");
                    Assert.AreEqual(
                        included.ValueKind is JsonValueKind.Null ? null : ReadUInt64(included),
                        deposit.IncludedInBatch);
                    break;
                }
            case "canonical-asset":
                Assert.AreEqual(
                    expected.GetString(),
                    (await client.GetCanonicalAssetAsync(UInt160.Parse(parameters[0].GetString()!)))?.ToString());
                break;
            case "bridged-asset":
                Assert.AreEqual(
                    expected.GetString(),
                    (await client.GetBridgedAssetAsync(UInt160.Parse(parameters[0].GetString()!)))?.ToString());
                break;
            case "security-level":
                {
                    var level = await client.GetSecurityLevelAsync();
                    Assert.AreEqual(expected.GetProperty("chainId").GetUInt32(), level.ChainId);
                    Assert.AreEqual(expected.GetProperty("level").GetByte(), (byte)level.Level);
                    break;
                }
            case "security-label":
                {
                    var label = await client.GetSecurityLabelAsync();
                    Assert.AreEqual(expected.GetProperty("chainId").GetUInt32(), label.ChainId);
                    Assert.AreEqual(expected.GetProperty("securityLevel").GetByte(), (byte)label.SecurityLevel);
                    Assert.AreEqual(expected.GetProperty("daMode").GetByte(), (byte)label.DAMode);
                    Assert.AreEqual(expected.GetProperty("gatewayEnabled").GetBoolean(), label.GatewayEnabled);
                    Assert.AreEqual(expected.GetProperty("sequencer").GetByte(), (byte)label.Sequencer);
                    Assert.AreEqual(expected.GetProperty("exit").GetByte(), (byte)label.Exit);
                    break;
                }
            default:
                Assert.Fail($"unknown live conformance case {name}");
                break;
        }
    }

    private static async Task AssertBaseNodeAsync(string endpoint, JsonElement expected)
    {
        var emptyParameters = JsonSerializer.SerializeToElement(Array.Empty<object>());
        var version = await RawRpcAsync(endpoint, "getversion", emptyParameters);
        Assert.AreEqual(
            expected.GetProperty("networkMagic").GetUInt32(),
            version.GetProperty("protocol").GetProperty("network").GetUInt32());

        var blockCount = await RawRpcAsync(endpoint, "getblockcount", emptyParameters);
        Assert.IsTrue(blockCount.GetInt64() >= expected.GetProperty("minimumBlockCount").GetInt64());

        var genesisParameters = JsonSerializer.SerializeToElement(new object[] { 0 });
        var genesisHash = await RawRpcAsync(endpoint, "getblockhash", genesisParameters);
        Assert.IsTrue(HashRegex().IsMatch(genesisHash.GetString()!));
        Assert.AreEqual(expected.GetProperty("genesisHash").GetString(), genesisHash.GetString());
    }

    private static async Task<JsonElement> RawRpcAsync(string endpoint, string method, JsonElement parameters)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var response = await http.PostAsJsonAsync(endpoint, new
        {
            jsonrpc = "2.0",
            method,
            @params = parameters,
            id = 1,
        });
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.AreEqual("2.0", root.GetProperty("jsonrpc").GetString());
        Assert.AreEqual(1L, root.GetProperty("id").GetInt64());
        Assert.IsFalse(root.TryGetProperty("error", out var error) && error.ValueKind is not JsonValueKind.Null);
        return root.GetProperty("result").Clone();
    }

    private static LiveConfiguration RequireConfiguration()
    {
        var missing = RequiredVariables
            .Where(name => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
            .ToArray();
        if (missing.Length > 0)
            Assert.Inconclusive($"SKIP live SDK conformance: missing {string.Join(", ", missing)}");
        if (Environment.GetEnvironmentVariable("NEO_SDK_LIVE") != "1")
            Assert.Fail("NEO_SDK_LIVE must equal 1");

        var chainIdText = Environment.GetEnvironmentVariable("NEO_N4_CHAIN_ID")!;
        if (!uint.TryParse(chainIdText, NumberStyles.None, CultureInfo.InvariantCulture, out var chainId) || chainId == 0)
            Assert.Fail("NEO_N4_CHAIN_ID must be an unsigned non-zero integer");

        using var fixtureDocument = JsonDocument.Parse(File.ReadAllText(
            Environment.GetEnvironmentVariable("NEO_SDK_LIVE_FIXTURE")!));
        var fixture = fixtureDocument.RootElement;
        Assert.AreEqual("neo-n4-sdk-live-fixture/v1", fixture.GetProperty("schema").GetString());
        var n4 = fixture.GetProperty("n4");
        Assert.AreEqual(chainId, n4.GetProperty("chainId").GetUInt32());

        return new LiveConfiguration(
            Environment.GetEnvironmentVariable("NEO_N3_RPC_URL")!,
            Environment.GetEnvironmentVariable("NEO_N4_RPC_URL")!,
            chainId,
            fixture.GetProperty("n3").Clone(),
            n4.Clone());
    }

    private static ulong ReadUInt64(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.Number => value.GetUInt64(),
            JsonValueKind.String => ulong.Parse(value.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture),
            _ => throw new AssertFailedException("fixture u64 must be a number or decimal string"),
        };

    [GeneratedRegex("^0x[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashRegex();

    private sealed record LiveConfiguration(
        string N3RpcUrl,
        string N4RpcUrl,
        uint N4ChainId,
        JsonElement N3Fixture,
        JsonElement N4Fixture);
}
