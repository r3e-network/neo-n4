namespace Neo.Plugins.L2DA.UnitTests;

[TestClass]
public class UT_FailoverDAWriter
{
    private static DAPublishRequest Request() => new() { ChainId = 1001, BatchNumber = 1, Payload = new byte[] { 1, 2, 3 } };
    private static FailoverDAWriter Wrap(params IDAWriter[] endpoints) => new(endpoints, new InMemoryMetrics());

    [TestMethod]
    public async Task Publish_PrimarySucceeds_DoesNotCallStandby()
    {
        var primary = new Endpoint();
        var standby = new Endpoint();
        var writer = Wrap(primary, standby);
        var receipt = await writer.PublishAsync(Request());
        Assert.AreEqual(DAMode.Local, writer.Mode);
        Assert.AreEqual(DAReceiptKind.LocalPersistence, writer.ReceiptKind);
        Assert.IsTrue(await writer.IsAvailableAsync(receipt));
        Assert.AreEqual(1, primary.Calls);
        Assert.AreEqual(0, standby.Calls);
    }

    [TestMethod]
    public async Task Publish_TransientFailure_UsesSameProfileStandby()
    {
        var metrics = new InMemoryMetrics();
        var primary = new Endpoint { Error = new IOException("temporarily unavailable") };
        var standby = new Endpoint();
        var writer = new FailoverDAWriter([primary, standby], metrics);
        var receipt = await writer.PublishAsync(Request());
        Assert.IsTrue(await standby.IsAvailableAsync(receipt));
        Assert.AreEqual(1, primary.Calls);
        Assert.AreEqual(1, standby.Calls);
        Assert.AreEqual(1L, metrics.GetCounter(MetricNames.DAPublishFailures, ("mode", "Local")));
        Assert.AreEqual(1L, metrics.GetCounter(MetricNames.DAPublished, ("mode", "Local")));
    }

    [TestMethod]
    public async Task Publish_AllEndpointsFail_PreservesLastFailure()
    {
        var error = new IOException("last failure");
        var first = new Endpoint { Error = new IOException() };
        var last = new Endpoint { Error = error };
        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await Wrap(first, last).PublishAsync(Request()));
        Assert.AreSame(error, ex.InnerException);
        Assert.AreEqual(1, last.Calls);
    }

    [TestMethod]
    public async Task Publish_CanceledToken_DoesNotCallEndpoints()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var endpoint = new Endpoint();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () => await Wrap(endpoint).PublishAsync(Request(), cts.Token));
        Assert.AreEqual(0, endpoint.Calls);
    }

    [TestMethod]
    public async Task Publish_InnerCancellation_DoesNotFailOver()
    {
        var standby = new Endpoint();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
            await Wrap(new Endpoint { Error = new OperationCanceledException() }, standby).PublishAsync(Request()));
        Assert.AreEqual(0, standby.Calls);
    }

    [TestMethod]
    public async Task Publish_InvalidData_DoesNotFailOver()
    {
        var standby = new Endpoint();
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await Wrap(new Endpoint { Error = new InvalidDataException() }, standby).PublishAsync(Request()));
        Assert.AreEqual(0, standby.Calls);
    }

    [TestMethod]
    public async Task Publish_InvalidReceipts_DoNotFailOver()
    {
        foreach (var invalid in new[] { 1, 2, 3 })
        {
            var standby = new Endpoint();
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
                await Wrap(new Endpoint { InvalidReceipt = invalid }, standby).PublishAsync(Request()));
            Assert.AreEqual(0, standby.Calls);
        }
    }

    [TestMethod]
    public async Task IsAvailable_SameProfileReceipt_ProbesStandby()
    {
        var primary = new Endpoint();
        var standby = new Endpoint();
        var receipt = await standby.PublishAsync(Request());
        Assert.IsTrue(await Wrap(primary, standby).IsAvailableAsync(receipt));
        Assert.AreEqual(1, primary.Probes);
        Assert.AreEqual(1, standby.Probes);
    }

    [TestMethod]
    public async Task IsAvailable_UnknownProfile_DoesNotProbe()
    {
        var endpoint = new Endpoint();
        var receipt = await endpoint.PublishAsync(Request());
        Assert.IsFalse(await Wrap(endpoint).IsAvailableAsync(receipt with { Layer = DAMode.NeoFS }));
        Assert.IsFalse(await Wrap(endpoint).IsAvailableAsync(receipt with { Kind = DAReceiptKind.NeoFSObject }));
        Assert.AreEqual(0, endpoint.Probes);
    }

    [TestMethod]
    public void Constructor_RejectsInvalidProfilesAndNulls()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Wrap());
        Assert.ThrowsExactly<ArgumentException>(() => Wrap(new Endpoint(), null!));
        Assert.ThrowsExactly<ArgumentNullException>(() => new FailoverDAWriter(null!, new InMemoryMetrics()));
        Assert.ThrowsExactly<ArgumentNullException>(() => new FailoverDAWriter([new Endpoint()], null!));
        Assert.ThrowsExactly<ArgumentException>(() => Wrap(new Endpoint(), new Endpoint { Mode = DAMode.NeoFS }));
        Assert.ThrowsExactly<ArgumentException>(() => Wrap(new Endpoint(), new Endpoint { ReceiptKind = DAReceiptKind.NeoFSObject }));
        Assert.ThrowsExactly<ArgumentException>(() => Wrap(new Endpoint { ReceiptKind = DAReceiptKind.Unspecified }));
    }

    [TestMethod]
    public void Constructor_RejectsNeoFsToLocalDowngrade_BeforePublication()
    {
        var primary = new Endpoint { Mode = DAMode.NeoFS, ReceiptKind = DAReceiptKind.NeoFSObject };
        var standby = new Endpoint();
        Assert.ThrowsExactly<ArgumentException>(() => Wrap(primary, standby));
        Assert.AreEqual(0, primary.Calls);
        Assert.AreEqual(0, standby.Calls);
    }

    private sealed class Endpoint : IDAWriter
    {
        private readonly InMemoryDAWriter _inner = new();
        public DAMode Mode { get; init; } = DAMode.Local;
        public DAReceiptKind ReceiptKind { get; init; } = DAReceiptKind.LocalPersistence;
        public Exception? Error { get; init; }
        public int InvalidReceipt { get; init; }
        public int Calls { get; private set; }
        public int Probes { get; private set; }
        public async ValueTask<DAReceipt> PublishAsync(DAPublishRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Error is not null) throw Error;
            var receipt = await _inner.PublishAsync(request, cancellationToken);
            return InvalidReceipt switch
            {
                1 => null!,
                2 => receipt with { Pointer = ReadOnlyMemory<byte>.Empty },
                3 => receipt with { Commitment = UInt256.Zero },
                _ => receipt
            };
        }
        public ValueTask<bool> IsAvailableAsync(DAReceipt receipt, CancellationToken cancellationToken = default)
        {
            Probes++;
            return _inner.IsAvailableAsync(receipt, cancellationToken);
        }
    }
}
