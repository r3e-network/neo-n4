using System.Net;
using System.Text;
using System.Text.Json;
using Neo.L2.Gateway.Rpc;
using Neo.L2.Settlement.Rpc;
using Neo.Plugins.L2Gateway;

namespace Neo.L2.Gateway.Rpc.UnitTests;

/// <summary>Production proof-bound MessageRouter RPC publication tests.</summary>
[TestClass]
public sealed class UT_ProofBoundRpcGlobalRootPublisher
{
    private static readonly UInt160 MessageRouter =
        UInt160.Parse("0x" + new string('a', 40));

    [TestMethod]
    public async Task PublishGlobalRootAsync_ForwardsCompleteBindingAndConfirmsOnChainState()
    {
        using var handler = new RouterRpcHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var binding = Binding();
        var proof = Enumerable.Repeat((byte)0x5A, Sp1GatewayProofProver.Groth16ProofSize).ToArray();
        GatewayRootPublishRequest? captured = null;
        var expectedTransaction = H(0xF1);
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            MessageRouter,
            (
                router,
                epoch,
                globalRoot,
                constituentRoot,
                count,
                backend,
                proofSystem,
                verificationKey,
                replayDomain,
                forwardedProof,
                cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                captured = new GatewayRootPublishRequest
                {
                    Binding = new GatewayProofBinding
                    {
                        MessageRouter = router,
                        ReplayDomain = replayDomain,
                        BatchEpoch = epoch,
                        GlobalMessageRoot = globalRoot,
                        ConstituentCommitmentsRoot = constituentRoot,
                        ConstituentCount = count,
                        AggregationBackendId = backend,
                        ProofSystem = proofSystem,
                        VerificationKeyId = verificationKey,
                    },
                    ProofInputHash = GatewayProofBindingSerializer.ComputeHash(binding),
                    AggregatedProof = forwardedProof.ToArray(),
                };
                handler.GlobalRoot = globalRoot;
                handler.ProofInputHash = GatewayProofBindingSerializer.ComputeHash(binding);
                return ValueTask.FromResult(expectedTransaction);
            });

        var transaction = await publisher.PublishGlobalRootAsync(binding, proof);

        Assert.AreEqual(expectedTransaction, transaction);
        Assert.IsNotNull(captured);
        Assert.AreEqual(
            GatewayProofBindingSerializer.ComputeHash(binding),
            GatewayProofBindingSerializer.ComputeHash(captured.Binding));
        Assert.IsTrue(captured.AggregatedProof.Span.SequenceEqual(proof));
        CollectionAssert.AreEqual(
            new[]
            {
                "getGlobalRoot",
                "getGlobalRootProofInputHash",
                "getGlobalRoot",
                "getGlobalRootProofInputHash",
            },
            handler.ContractMethods);
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_ExactPreflight_IsIdempotentWithoutSubmission()
    {
        using var handler = new RouterRpcHandler();
        var binding = Binding();
        handler.GlobalRoot = binding.GlobalMessageRoot;
        handler.ProofInputHash = GatewayProofBindingSerializer.ComputeHash(binding);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var submissions = 0;
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });

        var transaction = await publisher.PublishGlobalRootAsync(
            binding,
            new byte[Sp1GatewayProofProver.Groth16ProofSize]);

        Assert.AreEqual(UInt256.Zero, transaction);
        Assert.AreEqual(0, submissions);
        CollectionAssert.AreEqual(
            new[] { "getGlobalRoot", "getGlobalRootProofInputHash" },
            handler.ContractMethods);
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_ConflictingPreflight_FailsClosed()
    {
        using var handler = new RouterRpcHandler
        {
            GlobalRoot = H(0xEE),
            ProofInputHash = H(0xEF),
        };
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var submissions = 0;
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });

        await Assert.ThrowsExactlyAsync<GatewayPublicationConflictException>(
            async () => await publisher.PublishGlobalRootAsync(
                Binding(),
                new byte[Sp1GatewayProofProver.Groth16ProofSize]));

        Assert.AreEqual(0, submissions);
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_MissingPostConfirmation_IsRejected()
    {
        using var handler = new RouterRpcHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _) => ValueTask.FromResult(H(0xF1)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await publisher.PublishGlobalRootAsync(
                Binding(),
                new byte[Sp1GatewayProofProver.Groth16ProofSize]));
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_DifferentRouter_FailsBeforeRpcOrSigning()
    {
        using var handler = new RouterRpcHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var submissions = 0;
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });
        var binding = Binding() with
        {
            MessageRouter = UInt160.Parse("0x" + new string('b', 40)),
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await publisher.PublishGlobalRootAsync(
                binding,
                new byte[Sp1GatewayProofProver.Groth16ProofSize]));

        Assert.AreEqual(0, submissions);
        Assert.AreEqual(0, handler.ContractMethods.Count);
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_Sp1HashOnlyProof_FailsBeforeRpcOrSigning()
    {
        using var handler = new RouterRpcHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var submissions = 0;
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await publisher.PublishGlobalRootAsync(
                Binding(),
                GatewayProofBindingSerializer.ComputeHash(Binding()).GetSpan().ToArray()));

        Assert.AreEqual(0, submissions);
        Assert.AreEqual(0, handler.ContractMethods.Count);
    }

    private static GatewayProofBinding Binding() => new()
    {
        MessageRouter = MessageRouter,
        ReplayDomain = H(0xD1),
        BatchEpoch = 77,
        GlobalMessageRoot = H(0x51),
        ConstituentCommitmentsRoot = H(0x61),
        ConstituentCount = 2,
        AggregationBackendId = Sp1GatewayProofProver.RecursiveAggregationBackendId,
        ProofSystem = Sp1GatewayProofProver.Sp1ProofSystem,
        VerificationKeyId = H(0xA1),
    };

    private static UInt256 H(byte value) => new(Enumerable.Repeat(value, 32).ToArray());

    private sealed class RouterRpcHandler : HttpMessageHandler
    {
        public UInt256 GlobalRoot { get; set; } = UInt256.Zero;
        public UInt256 ProofInputHash { get; set; } = UInt256.Zero;
        public List<string> ContractMethods { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var id = root.GetProperty("id").GetInt64();
            Assert.AreEqual("invokefunction", root.GetProperty("method").GetString());
            var rpcParams = root.GetProperty("params");
            Assert.AreEqual(MessageRouter.ToString(), rpcParams[0].GetString());
            var method = rpcParams[1].GetString()
                ?? throw new InvalidOperationException("missing contract method");
            ContractMethods.Add(method);
            var value = method switch
            {
                "getGlobalRoot" => GlobalRoot,
                "getGlobalRootProofInputHash" => ProofInputHash,
                _ => throw new InvalidOperationException($"unexpected contract method {method}"),
            };
            var response = JsonSerializer.Serialize(new
            {
                jsonrpc = "2.0",
                id,
                result = new
                {
                    state = "HALT",
                    stack = new[]
                    {
                        new
                        {
                            type = "ByteString",
                            value = Convert.ToBase64String(value.GetSpan()),
                        },
                    },
                },
            });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, "application/json"),
            };
        }
    }
}
