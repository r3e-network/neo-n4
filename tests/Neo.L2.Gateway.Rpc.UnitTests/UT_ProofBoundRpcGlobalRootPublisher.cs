using System.Net;
using System.Text;
using System.Text.Json;
using Neo.L2;
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
    private static readonly UInt160 SettlementManager =
        UInt160.Parse("0x" + new string('b', 40));

    [TestMethod]
    public async Task PublishGlobalRootAsync_ForwardsCompleteBindingAndConfirmsOnChainState()
    {
        using var handler = new RouterRpcHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var (binding, aggregate) = Statement();
        var proof = Enumerable.Repeat((byte)0x5A, Sp1GatewayProofProver.Groth16ProofSize).ToArray();
        GatewayRootPublishRequest? captured = null;
        var expectedTransaction = H(0xF1);
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            SettlementManager,
            MessageRouter,
            (
                settlementManager,
                epoch,
                constituentReferences,
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
                Assert.AreEqual(SettlementManager, settlementManager);
                captured = new GatewayRootPublishRequest
                {
                    Binding = new GatewayProofBinding
                    {
                        MessageRouter = MessageRouter,
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
                    ConstituentReferences = constituentReferences.ToArray(),
                    AggregatedProof = forwardedProof.ToArray(),
                };
                handler.GlobalRoot = globalRoot;
                handler.ProofInputHash = GatewayProofBindingSerializer.ComputeHash(binding);
                return ValueTask.FromResult(expectedTransaction);
            });

        var transaction = await publisher.PublishGlobalRootAsync(binding, aggregate, proof);

        Assert.AreEqual(expectedTransaction, transaction);
        Assert.IsNotNull(captured);
        Assert.AreEqual(
            GatewayProofBindingSerializer.ComputeHash(binding),
            GatewayProofBindingSerializer.ComputeHash(captured.Binding));
        Assert.IsTrue(captured.AggregatedProof.Span.SequenceEqual(proof));
        CollectionAssert.AreEqual(
            GatewayFinalityReferenceSerializer.Encode(aggregate.Constituents),
            captured.ConstituentReferences.ToArray());
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
        var (binding, aggregate) = Statement();
        handler.GlobalRoot = binding.GlobalMessageRoot;
        handler.ProofInputHash = GatewayProofBindingSerializer.ComputeHash(binding);
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var submissions = 0;
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            SettlementManager,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });

        var transaction = await publisher.PublishGlobalRootAsync(
            binding,
            aggregate,
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
        var (binding, aggregate) = Statement();
        var submissions = 0;
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            SettlementManager,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });

        await Assert.ThrowsExactlyAsync<GatewayPublicationConflictException>(
            async () => await publisher.PublishGlobalRootAsync(
                binding,
                aggregate,
                new byte[Sp1GatewayProofProver.Groth16ProofSize]));

        Assert.AreEqual(0, submissions);
    }

    [TestMethod]
    public async Task PublishGlobalRootAsync_MissingPostConfirmation_IsRejected()
    {
        using var handler = new RouterRpcHandler();
        using var http = new HttpClient(handler);
        using var rpc = new JsonRpcClient(new Uri("http://l1.example/"), http);
        var (binding, aggregate) = Statement();
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            SettlementManager,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _, _) => ValueTask.FromResult(H(0xF1)));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            async () => await publisher.PublishGlobalRootAsync(
                binding,
                aggregate,
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
            SettlementManager,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });
        var (canonicalBinding, aggregate) = Statement();
        var binding = canonicalBinding with
        {
            MessageRouter = UInt160.Parse("0x" + new string('b', 40)),
        };

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await publisher.PublishGlobalRootAsync(
                binding,
                aggregate,
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
        var (binding, aggregate) = Statement();
        var submissions = 0;
        using var publisher = new ProofBoundRpcGlobalRootPublisher(
            rpc,
            SettlementManager,
            MessageRouter,
            (_, _, _, _, _, _, _, _, _, _, _, _) =>
            {
                submissions++;
                return ValueTask.FromResult(H(0xF1));
            });

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            async () => await publisher.PublishGlobalRootAsync(
                binding,
                aggregate,
                GatewayProofBindingSerializer.ComputeHash(binding).GetSpan().ToArray()));

        Assert.AreEqual(0, submissions);
        Assert.AreEqual(0, handler.ContractMethods.Count);
    }

    private static (GatewayProofBinding Binding, AggregatedCommitment Aggregate) Statement()
    {
        var constituents = new[] { Commitment(1001, 1), Commitment(1002, 2) };
        var aggregate = new AggregatedCommitment
        {
            Constituents = constituents,
            GlobalMessageRoot = H(0x51),
            ConstituentCommitmentsRoot =
                GatewayProofBindingSerializer.ComputeConstituentCommitmentsRoot(constituents),
            AggregatedProof = ReadOnlyMemory<byte>.Empty,
            BackendId = Sp1GatewayProofProver.RecursiveAggregationBackendId,
        };
        var binding = GatewayProofBindingSerializer.Create(
            MessageRouter,
            H(0xD1),
            77,
            aggregate,
            Sp1GatewayProofProver.Sp1ProofSystem,
            H(0xA1));
        return (binding, aggregate);
    }

    private static L2BatchCommitment Commitment(uint chainId, ulong batchNumber) => new()
    {
        ChainId = chainId,
        BatchNumber = batchNumber,
        FirstBlock = batchNumber,
        LastBlock = batchNumber,
        PreStateRoot = H(0x01),
        PostStateRoot = H(0x02),
        TxRoot = H(0x03),
        ReceiptRoot = H(0x04),
        WithdrawalRoot = H(0x05),
        L2ToL1MessageRoot = H(0x06),
        L2ToL2MessageRoot = H((byte)(chainId & 0xFF)),
        DACommitment = H(0x08),
        PublicInputHash = H(0x09),
        ProofType = ProofType.Zk,
        Proof = new byte[] { 0x10 },
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
