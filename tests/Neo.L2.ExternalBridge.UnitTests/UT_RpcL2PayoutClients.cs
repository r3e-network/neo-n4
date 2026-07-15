using System.Net;
using System.Text;
using Neo.Json;
using Neo.L2.Settlement.Rpc;
using Neo.Network.P2P.Payloads;
using Neo.Wallets;

using static Neo.L2.ExternalBridge.UnitTests.PayoutTestData;

namespace Neo.L2.ExternalBridge.UnitTests;

[TestClass]
public sealed class UT_RpcL2PayoutClients
{
    private const uint Network = 894_710_606;
    private static readonly Uri Endpoint = new("http://neo.example/");

    [TestMethod]
    public async Task CreditClient_ValidatesObservesPreparesAndBroadcasts()
    {
        var instruction = Instruction();
        using var handler = new PayoutRpcHandler(instruction);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        handler.ReportedRelayAccount = signer.Account;
        var sender = CreateSender(rpc, signer);
        var client = new RpcL2PayoutCreditClient(
            rpc, sender, NativeBridge, NeoChainId, signer.Account);

        await client.ValidateConfigurationAsync();
        Assert.AreEqual(
            L2PayoutCreditObservation.Missing,
            await client.ObserveAsync(instruction));
        handler.L2MessageHash = instruction.Message.MessageHash;
        Assert.AreEqual(
            new L2PayoutCreditObservation(instruction.Message.MessageHash, L2Transaction),
            await client.ObserveAsync(instruction));

        var signed = await client.PrepareAsync(instruction);
        Assert.IsFalse(signed.IsEmpty);
        Assert.AreNotEqual(UInt256.Zero, await client.BroadcastAsync(signed));
    }

    [TestMethod]
    public async Task AcknowledgementClient_ValidatesObservesPreparesAndBroadcasts()
    {
        var instruction = Instruction();
        using var handler = new PayoutRpcHandler(instruction);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        handler.ReportedRelayAccount = signer.Account;
        var sender = CreateSender(rpc, signer);
        var client = new RpcL1PayoutAcknowledgementClient(
            rpc, sender, Adapter, NeoChainId, signer.Account);

        await client.ValidateConfigurationAsync();
        Assert.AreEqual(
            L1PayoutAcknowledgementObservation.Missing,
            await client.ObserveAsync(instruction));
        handler.PayoutStatus = 2;
        Assert.AreEqual(
            new L1PayoutAcknowledgementObservation(true, L2Transaction),
            await client.ObserveAsync(instruction));

        var signed = await client.PrepareAsync(instruction, L2Transaction);
        Assert.IsFalse(signed.IsEmpty);
        Assert.AreNotEqual(UInt256.Zero, await client.BroadcastAsync(signed));
    }

    [TestMethod]
    public async Task Clients_FailClosedOnConfigurationAndDomainMismatch()
    {
        var instruction = Instruction();
        using var handler = new PayoutRpcHandler(instruction)
        {
            ReportedNeoChainId = NeoChainId + 1,
        };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);
        var credit = new RpcL2PayoutCreditClient(
            rpc, sender, NativeBridge, NeoChainId, signer.Account);
        var acknowledgement = new RpcL1PayoutAcknowledgementClient(
            rpc, sender, Adapter, NeoChainId, signer.Account);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await credit.ValidateConfigurationAsync());
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await acknowledgement.ValidateConfigurationAsync());
        var otherDomain = instruction with
        {
            Message = instruction.Message with { NeoChainId = NeoChainId + 1 },
        };
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await credit.ObserveAsync(otherDomain));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await acknowledgement.ObserveAsync(otherDomain));
    }

    [TestMethod]
    public async Task Clients_RejectUnknownOrConflictingOnChainState()
    {
        var instruction = Instruction();
        using var handler = new PayoutRpcHandler(instruction)
        {
            PayoutStatus = 3,
        };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);
        var client = new RpcL1PayoutAcknowledgementClient(
            rpc, sender, Adapter, NeoChainId, signer.Account);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await client.ObserveAsync(instruction));
        handler.PayoutStatus = 0;
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await client.ObserveAsync(instruction));
        handler.PayoutStatus = 2;
        handler.StoredMessageHash = H256(0xEF);
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await client.ObserveAsync(instruction));
    }

    [TestMethod]
    public async Task Clients_RejectUnsafeConstructionAndEmptyBroadcasts()
    {
        var instruction = Instruction();
        using var handler = new PayoutRpcHandler(instruction);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(Endpoint, http);
        using var signer = CreateSigner();
        var sender = CreateSender(rpc, signer);

        Assert.ThrowsExactly<ArgumentException>(() => new RpcL2PayoutCreditClient(
            rpc, sender, UInt160.Zero, NeoChainId, signer.Account));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RpcL2PayoutCreditClient(
            rpc, sender, NativeBridge, 0, signer.Account));
        Assert.ThrowsExactly<ArgumentException>(() => new RpcL1PayoutAcknowledgementClient(
            rpc, sender, UInt160.Zero, NeoChainId, signer.Account));
        var credit = new RpcL2PayoutCreditClient(
            rpc, sender, NativeBridge, NeoChainId, signer.Account);
        var acknowledgement = new RpcL1PayoutAcknowledgementClient(
            rpc, sender, Adapter, NeoChainId, signer.Account);
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await credit.BroadcastAsync(ReadOnlyMemory<byte>.Empty));
        await Assert.ThrowsExactlyAsync<ArgumentException>(async () =>
            await acknowledgement.PrepareAsync(instruction, UInt256.Zero));
    }

    private static LocalKeyTransactionSigner CreateSigner()
    {
        var privateKey = Enumerable.Range(1, 32).Select(value => (byte)value).ToArray();
        return new LocalKeyTransactionSigner(new KeyPair(privateKey));
    }

    private static RpcTransactionSender CreateSender(
        JsonRpcClient rpc,
        INeoTransactionSigner signer) => new(
            rpc,
            signer,
            new RpcTransactionSenderOptions
            {
                ExpectedNetwork = Network,
                ConfirmationTimeout = TimeSpan.FromSeconds(1),
                ConfirmationPollInterval = TimeSpan.FromMilliseconds(1),
            });

    private sealed class PayoutRpcHandler(L2PayoutInstruction instruction) : HttpMessageHandler
    {
        public List<string> ContractMethods { get; } = [];
        public uint ReportedNeoChainId { get; init; } = NeoChainId;
        public UInt160 ReportedRelayAccount { get; set; } = RelayAccount;
        public UInt256 L2MessageHash { get; set; } = UInt256.Zero;
        public byte PayoutStatus { get; set; } = 1;
        public UInt256 StoredMessageHash { get; set; } = instruction.Message.MessageHash;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var envelope = (JObject)JToken.Parse(body)!;
            var method = envelope["method"]!.AsString();
            var response = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = envelope["id"],
                ["result"] = Result(method, (JArray)envelope["params"]!),
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response.ToString(), Encoding.UTF8, "application/json"),
            };
        }

        private JToken Result(string method, JArray parameters) => method switch
        {
            "invokefunction" => InvokeFunction(parameters),
            "getversion" => new JObject
            {
                ["protocol"] = new JObject { ["network"] = Network },
            },
            "invokescript" => new JObject
            {
                ["state"] = "HALT",
                ["gasconsumed"] = "0.10000000",
            },
            "getblockcount" => new JNumber(100),
            "calculatenetworkfee" => new JObject { ["networkfee"] = "0.01000000" },
            "sendrawtransaction" => true,
            "getapplicationlog" => new JObject
            {
                ["executions"] = new JArray
                {
                    new JObject { ["vmstate"] = "HALT" },
                },
            },
            _ => throw new InvalidOperationException($"Unexpected RPC method {method}"),
        };

        private JObject InvokeFunction(JArray parameters)
        {
            var method = parameters[1]!.AsString();
            ContractMethods.Add(method);
            var value = method switch
            {
                "getChainId" or "getNeoChainId" => Integer(ReportedNeoChainId),
                "getSystemAccount" or "getRelayAccount" => Bytes(ReportedRelayAccount.GetSpan()),
                "payoutVersion" => Integer(1),
                "getInboundMessageHash" => Bytes(L2MessageHash.GetSpan()),
                "getInboundTransactionHash" => Bytes(L2Transaction.GetSpan()),
                "getPayoutStatus" => Integer(PayoutStatus),
                "getPayoutMessageHash" => Bytes(StoredMessageHash.GetSpan()),
                "getPayoutL2TransactionHash" => Bytes(L2Transaction.GetSpan()),
                _ => throw new InvalidOperationException($"Unexpected contract method {method}"),
            };
            return new JObject
            {
                ["state"] = "HALT",
                ["stack"] = new JArray { value },
            };
        }

        private static JObject Integer<T>(T value) where T : IFormattable => new()
        {
            ["type"] = "Integer",
            ["value"] = value.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        };

        private static JObject Bytes(ReadOnlySpan<byte> value) => new()
        {
            ["type"] = "ByteString",
            ["value"] = Convert.ToBase64String(value),
        };
    }
}
