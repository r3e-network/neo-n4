using System.Net;
using System.Text;
using System.Text.Json;
using Neo.Cryptography;

namespace Neo.Plugins.L2DA.UnitTests;

[TestClass]
public class UT_NeoFsRestDABackend
{
    private const string ContainerId = "5HZTn5qkRnmgSz9gSrw22CEdPPk6nQhkwf2Mgzyvkikv";
    private const string ObjectId = "8N3o7Dtr6T1xteCt6eRwhpmJ7JhME58Hyu1dvaswuTDd";
    private static readonly byte[] s_evidence = "neo-n4:neofs-rest-gateway:v1"u8.ToArray();

    [TestMethod]
    public async Task Writer_PublishesOfficialAddressAndRequiresIndependentRetrieval()
    {
        var payload = new byte[] { 1, 3, 3, 7 };
        var writerAuth = new TrackingAuthenticator();
        var readerAuth = new TrackingAuthenticator();
        var writerCalls = 0;
        var readerCalls = 0;
        using var writerClient = CreateClient("writer.example", async (request, _) =>
        {
            writerCalls++;
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual($"/v1/objects/{ContainerId}", request.RequestUri!.AbsolutePath);
            Assert.IsTrue(request.Headers.Contains("X-Test-Auth"));
            CollectionAssert.AreEqual(payload, await request.Content!.ReadAsByteArrayAsync());

            var attributesValue = request.Headers.GetValues("X-Attributes-Base64").Single();
            var attributes = JsonSerializer.Deserialize<Dictionary<string, string>>(
                Convert.FromBase64String(attributesValue))!;
            Assert.AreEqual("42", attributes["NeoN4ChainId"]);
            Assert.AreEqual("9", attributes["NeoN4BatchNumber"]);
            Assert.AreEqual(
                Convert.ToHexString(Crypto.Hash256(payload)),
                attributes["NeoN4PayloadHash256"]);
            return UploadResponse(ContainerId, ObjectId);
        });
        using var readerClient = CreateClient("reader.example", (request, _) =>
        {
            readerCalls++;
            Assert.AreEqual(HttpMethod.Get, request.Method);
            Assert.AreEqual(
                $"/v1/objects/{ContainerId}/by_id/{ObjectId}",
                request.RequestUri!.AbsolutePath);
            Assert.IsTrue(request.Headers.Contains("X-Test-Auth"));
            return Task.FromResult(ObjectResponse(payload));
        });
        var reader = new NeoFsRestDAReader(readerClient, readerAuth);
        var writer = new NeoFsRestDAWriter(writerClient, ContainerId, writerAuth, reader);
        using var plugin = new L2DAPlugin();
        plugin.WithProductionBackend(writer, reader);

        var receipt = await plugin.GetWriter().PublishAsync(new DAPublishRequest
        {
            ChainId = 42,
            BatchNumber = 9,
            Payload = payload,
        });

        Assert.AreEqual(DAMode.NeoFS, receipt.Layer);
        Assert.AreEqual(DAReceiptKind.NeoFSObject, receipt.Kind);
        Assert.AreEqual(new UInt256(Crypto.Hash256(payload)), receipt.Commitment);
        Assert.IsTrue(NeoFsObjectLocator.TryParsePointer(receipt.Pointer.Span, out var locator));
        Assert.AreEqual(ContainerId, locator!.ContainerId);
        Assert.AreEqual(ObjectId, locator.ObjectId);
        CollectionAssert.AreEqual(s_evidence, receipt.Evidence.ToArray());
        Assert.AreEqual(DADeploymentProfile.Production, plugin.Profile);
        CollectionAssert.AreEqual(payload, (await plugin.GetReader().ReadAsync(receipt))!.Value.ToArray());
        Assert.AreEqual(1, writerCalls);
        Assert.AreEqual(2, readerCalls);
        Assert.AreEqual(1, writerAuth.CallCount);
        Assert.AreEqual(2, readerAuth.CallCount);
        Assert.IsTrue(await plugin.GetWriter().IsAvailableAsync(receipt));
        Assert.AreEqual(3, readerCalls);
    }

    [TestMethod]
    public async Task Writer_RejectsObjectThatIndependentReaderCannotVerify()
    {
        var payload = new byte[] { 1, 2, 3 };
        using var writerClient = CreateClient(
            "writer.example",
            (_, _) => Task.FromResult(UploadResponse(ContainerId, ObjectId)));
        using var readerClient = CreateClient(
            "reader.example",
            (_, _) => Task.FromResult(ObjectResponse(new byte[] { 9, 9, 9 })));
        var reader = new NeoFsRestDAReader(readerClient, NeoFsRestAnonymousAuthenticator.Instance);
        var writer = new NeoFsRestDAWriter(
            writerClient,
            ContainerId,
            NeoFsRestAnonymousAuthenticator.Instance,
            reader);

        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await writer.PublishAsync(new DAPublishRequest
            {
                ChainId = 1,
                BatchNumber = 2,
                Payload = payload,
            }));

        StringAssert.Contains(error.Message, "read-after-write");
    }

    [TestMethod]
    public async Task Writer_SnapshotsCallerPayloadBeforeHashAndUpload()
    {
        var callerBuffer = new byte[] { 1, 2, 3 };
        var expectedPayload = callerBuffer.ToArray();
        using var writerClient = CreateClient("writer.example", async (request, _) =>
        {
            callerBuffer[0] = 9;
            CollectionAssert.AreEqual(
                expectedPayload,
                await request.Content!.ReadAsByteArrayAsync());
            return UploadResponse(ContainerId, ObjectId);
        });
        using var readerClient = CreateClient(
            "reader.example",
            (_, _) => Task.FromResult(ObjectResponse(expectedPayload)));
        var reader = new NeoFsRestDAReader(readerClient, NeoFsRestAnonymousAuthenticator.Instance);
        var writer = new NeoFsRestDAWriter(
            writerClient,
            ContainerId,
            NeoFsRestAnonymousAuthenticator.Instance,
            reader);

        var receipt = await writer.PublishAsync(new DAPublishRequest
        {
            ChainId = 1,
            BatchNumber = 1,
            Payload = callerBuffer,
        });

        Assert.AreEqual(9, callerBuffer[0]);
        Assert.AreEqual(new UInt256(Crypto.Hash256(expectedPayload)), receipt.Commitment);
    }

    [TestMethod]
    public async Task Writer_RejectsMismatchedUploadContainerBeforeRetrieval()
    {
        var readerCalled = false;
        using var writerClient = CreateClient(
            "writer.example",
            (_, _) => Task.FromResult(UploadResponse(ObjectId, ObjectId)));
        using var readerClient = CreateClient("reader.example", (_, _) =>
        {
            readerCalled = true;
            return Task.FromResult(ObjectResponse(Array.Empty<byte>()));
        });
        var reader = new NeoFsRestDAReader(readerClient, NeoFsRestAnonymousAuthenticator.Instance);
        var writer = new NeoFsRestDAWriter(
            writerClient,
            ContainerId,
            NeoFsRestAnonymousAuthenticator.Instance,
            reader);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await writer.PublishAsync(new DAPublishRequest
            {
                ChainId = 1,
                BatchNumber = 1,
                Payload = new byte[] { 1 },
            }));
        Assert.IsFalse(readerCalled);
    }

    [TestMethod]
    public async Task Writer_PropagatesFailedUploadStatusWithoutRetrieval()
    {
        var readerCalled = false;
        using var writerClient = CreateClient(
            "writer.example",
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        using var readerClient = CreateClient("reader.example", (_, _) =>
        {
            readerCalled = true;
            return Task.FromResult(ObjectResponse(Array.Empty<byte>()));
        });
        var reader = new NeoFsRestDAReader(readerClient, NeoFsRestAnonymousAuthenticator.Instance);
        var writer = new NeoFsRestDAWriter(
            writerClient,
            ContainerId,
            NeoFsRestAnonymousAuthenticator.Instance,
            reader);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await writer.PublishAsync(new DAPublishRequest
            {
                ChainId = 1,
                BatchNumber = 1,
                Payload = new byte[] { 1 },
            }));
        Assert.IsFalse(readerCalled);
    }

    [TestMethod]
    public async Task Writer_RejectsPayloadAboveConfiguredLimitWithoutNetwork()
    {
        var networkCalled = false;
        using var writerClient = CreateClient("writer.example", (_, _) =>
        {
            networkCalled = true;
            return Task.FromResult(UploadResponse(ContainerId, ObjectId));
        });
        var reader = new StubProductionReader();
        var writer = new NeoFsRestDAWriter(
            writerClient,
            ContainerId,
            NeoFsRestAnonymousAuthenticator.Instance,
            reader,
            maxObjectBytes: 2);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await writer.PublishAsync(new DAPublishRequest
            {
                ChainId = 1,
                BatchNumber = 1,
                Payload = new byte[] { 1, 2, 3 },
            }));
        Assert.IsFalse(networkCalled);
    }

    [TestMethod]
    public async Task Reader_RejectsMismatchedLocatorHeaders()
    {
        var payload = new byte[] { 4, 5, 6 };
        using var client = CreateClient("reader.example", (_, _) =>
        {
            var response = ObjectResponse(payload);
            response.Headers.Remove("X-Object-Id");
            response.Headers.Add("X-Object-Id", ContainerId);
            return Task.FromResult(response);
        });
        var reader = new NeoFsRestDAReader(client, NeoFsRestAnonymousAuthenticator.Instance);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await reader.ReadAsync(Receipt(payload)));
    }

    [TestMethod]
    public async Task Reader_ReturnsNullForNotFoundAndContentTampering()
    {
        var calls = 0;
        using var client = CreateClient("reader.example", (_, _) =>
        {
            calls++;
            return Task.FromResult(calls == 1
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : ObjectResponse(new byte[] { 0xFF }));
        });
        var reader = new NeoFsRestDAReader(client, NeoFsRestAnonymousAuthenticator.Instance);
        var receipt = Receipt(new byte[] { 1, 2 });

        Assert.IsNull(await reader.ReadAsync(receipt));
        Assert.IsNull(await reader.ReadAsync(receipt));
    }

    [TestMethod]
    public async Task Reader_PropagatesAuthorizationFailure()
    {
        using var client = CreateClient(
            "reader.example",
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)));
        var reader = new NeoFsRestDAReader(client, NeoFsRestAnonymousAuthenticator.Instance);

        await Assert.ThrowsExactlyAsync<HttpRequestException>(async () =>
            await reader.ReadAsync(Receipt(new byte[] { 1 })));
    }

    [TestMethod]
    public async Task Reader_RejectsOversizedResponseAndMalformedReceipt()
    {
        var calls = 0;
        using var client = CreateClient("reader.example", (_, _) =>
        {
            calls++;
            return Task.FromResult(ObjectResponse(new byte[] { 1, 2, 3 }));
        });
        var reader = new NeoFsRestDAReader(
            client,
            NeoFsRestAnonymousAuthenticator.Instance,
            maxObjectBytes: 2);
        var receipt = Receipt(new byte[] { 1, 2, 3 });

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await reader.ReadAsync(receipt));
        Assert.IsNull(await reader.ReadAsync(receipt with { Evidence = new byte[] { 1 } }));
        Assert.IsNull(await reader.ReadAsync(receipt with { Pointer = "bad"u8.ToArray() }));
        Assert.AreEqual(1, calls);
    }

    [TestMethod]
    public void Locator_RequiresCanonicalNonZeroNeoFsIdentifiers()
    {
        var locator = new NeoFsObjectLocator(ContainerId, ObjectId);
        Assert.AreEqual($"{ContainerId}/{ObjectId}", locator.ProtocolAddress);
        Assert.IsTrue(NeoFsObjectLocator.TryParsePointer(locator.ToPointer(), out var decoded));
        Assert.AreEqual(locator, decoded);

        Assert.ThrowsExactly<ArgumentException>(() => new NeoFsObjectLocator("not-base58", ObjectId));
        Assert.ThrowsExactly<ArgumentException>(() => new NeoFsObjectLocator("1", ObjectId));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new NeoFsObjectLocator(new string('1', 45), ObjectId));
        Assert.IsFalse(NeoFsObjectLocator.TryParsePointer(
            Encoding.UTF8.GetBytes($"{ContainerId}/{ObjectId}/extra"),
            out _));
    }

    [TestMethod]
    public async Task SessionAuthenticator_UsesRotatingBearerTokenAndFailsClosed()
    {
        var token = "session-token-1";
        var authenticator = new NeoFsRestSessionTokenAuthenticator(
            _ => ValueTask.FromResult(token));
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://gateway.example/");

        await authenticator.AuthenticateAsync(request);
        Assert.AreEqual("Bearer", request.Headers.Authorization!.Scheme);
        Assert.AreEqual(token, request.Headers.Authorization.Parameter);

        var invalid = new NeoFsRestSessionTokenAuthenticator(
            _ => ValueTask.FromResult("\r\n"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await invalid.AuthenticateAsync(request));
    }

    [TestMethod]
    public void Constructors_RequireHttpsAndNeoFsProductionReader()
    {
        using var insecureClient = new HttpClient(new DelegateHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("http://gateway.example/"),
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            new NeoFsRestDAReader(insecureClient, NeoFsRestAnonymousAuthenticator.Instance));

        using var credentialClient = new HttpClient(new DelegateHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))))
        {
            BaseAddress = new Uri("https://user:password@gateway.example/"),
        };
        Assert.ThrowsExactly<ArgumentException>(() =>
            new NeoFsRestDAReader(credentialClient, NeoFsRestAnonymousAuthenticator.Instance));

        using var secureClient = CreateClient(
            "gateway.example",
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        Assert.ThrowsExactly<ArgumentException>(() =>
            new NeoFsRestDAWriter(
                secureClient,
                ContainerId,
                NeoFsRestAnonymousAuthenticator.Instance,
                new WrongModeProductionReader()));
    }

    private static HttpClient CreateClient(
        string host,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new DelegateHandler(send))
        {
            BaseAddress = new Uri($"https://{host}/"),
        };

    private static HttpResponseMessage UploadResponse(string containerId, string objectId)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    container_id = containerId,
                    object_id = objectId,
                }),
                Encoding.UTF8,
                "application/json"),
        };

    private static HttpResponseMessage ObjectResponse(byte[] payload)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(payload),
        };
        response.Headers.Add("X-Container-Id", ContainerId);
        response.Headers.Add("X-Object-Id", ObjectId);
        return response;
    }

    private static DAReceipt Receipt(byte[] payload)
        => new()
        {
            Commitment = new UInt256(Crypto.Hash256(payload)),
            Pointer = new NeoFsObjectLocator(ContainerId, ObjectId).ToPointer(),
            Evidence = s_evidence,
            Kind = DAReceiptKind.NeoFSObject,
            Layer = DAMode.NeoFS,
        };

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class TrackingAuthenticator : INeoFsRestRequestAuthenticator
    {
        public int CallCount { get; private set; }

        public ValueTask AuthenticateAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            request.Headers.Add("X-Test-Auth", "authenticated");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StubProductionReader : IProductionDAReader
    {
        public DAMode Mode => DAMode.NeoFS;

        public DAReceiptKind ReceiptKind => DAReceiptKind.NeoFSObject;

        public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
    }

    private sealed class WrongModeProductionReader : IProductionDAReader
    {
        public DAMode Mode => DAMode.L1;

        public DAReceiptKind ReceiptKind => DAReceiptKind.L1Transaction;

        public ValueTask<ReadOnlyMemory<byte>?> ReadAsync(
            DAReceipt receipt,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<ReadOnlyMemory<byte>?>(null);
    }
}
