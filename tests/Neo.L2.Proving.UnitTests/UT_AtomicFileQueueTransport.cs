using System.Security.Cryptography;
using Neo.L2.Proving.RiscVZk;

namespace Neo.L2.Proving.UnitTests;

/// <summary>Security-boundary tests for atomic, bounded SP1 queue transport.</summary>
[TestClass]
public sealed class UT_AtomicFileQueueTransport
{
    [TestMethod]
    public void Constructor_RejectsInvalidTimeouts()
    {
        using var directory = new TemporaryDirectory();

        Assert.ThrowsExactly<ArgumentException>(() =>
            new AtomicFileQueueTransport("", TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AtomicFileQueueTransport(directory.Path, TimeSpan.Zero, TimeSpan.FromMilliseconds(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AtomicFileQueueTransport(directory.Path, TimeSpan.FromDays(2), TimeSpan.FromSeconds(1)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AtomicFileQueueTransport(directory.Path, TimeSpan.FromSeconds(1), TimeSpan.Zero));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AtomicFileQueueTransport(directory.Path, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AtomicFileQueueTransport(
                directory.Path, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), 0, 1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            new AtomicFileQueueTransport(
                directory.Path, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(1), 1, 0));
    }

    [TestMethod]
    public async Task PublishExactAsync_IsAtomicIdempotentAndRejectsConflict()
    {
        using var directory = new TemporaryDirectory();
        var transport = CreateTransport(directory.Path);
        byte[] expected = [1, 2, 3, 4];

        await transport.PublishExactAsync("artifact.bin", expected);
        await transport.PublishExactAsync("artifact.bin", expected);

        CollectionAssert.AreEqual(expected, await transport.ReadExactAsync("artifact.bin", 4));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await transport.PublishExactAsync("artifact.bin", new byte[] { 4, 3, 2, 1 }));
        Assert.AreEqual(0, Directory.GetFiles(directory.Path, "*.tmp-*").Length);
        if (!OperatingSystem.IsWindows())
        {
            Assert.AreEqual(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(directory.Path, "artifact.bin")));
            Assert.AreEqual(
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(directory.Path));
        }
    }

    [TestMethod]
    public async Task PublishRequestExactAsync_EnforcesTaskAndByteBackpressure()
    {
        using var taskDirectory = new TemporaryDirectory();
        var taskLimited = new AtomicFileQueueTransport(
            taskDirectory.Path,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(5),
            maximumQueueBytes: 1024,
            maximumRequestCount: 1);
        await taskLimited.PublishRequestExactAsync("a.batch.bin", new byte[] { 1 });
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            taskLimited.PublishRequestExactAsync("b.batch.bin", new byte[] { 2 }).AsTask());

        using var byteDirectory = new TemporaryDirectory();
        var byteLimited = new AtomicFileQueueTransport(
            byteDirectory.Path,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromMilliseconds(5),
            maximumQueueBytes: 3,
            maximumRequestCount: 4);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            byteLimited.PublishRequestExactAsync(
                "large.batch.bin", new byte[] { 1, 2, 3, 4 }).AsTask());
    }

    [TestMethod]
    public async Task ReadBoundedAsync_RejectsMissingOversizedAndNoncanonicalPaths()
    {
        using var directory = new TemporaryDirectory();
        var transport = CreateTransport(directory.Path);
        await WritePrivateAsync(
            Path.Combine(directory.Path, "short.bin"), new byte[] { 1, 2 });
        await WritePrivateAsync(
            Path.Combine(directory.Path, "large.bin"), new byte[] { 1, 2, 3 });

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
            await transport.ReadBoundedAsync("short.bin", -1));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await transport.ReadBoundedAsync("missing.bin", 4));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await transport.ReadBoundedAsync("large.bin", 2));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await transport.ReadExactAsync("short.bin", 3));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await transport.ReadBoundedAsync("../short.bin", 2));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await transport.ReadBoundedAsync(".", 2));
    }

    [TestMethod]
    public async Task WaitForAsync_HandlesReadyAndTimeoutStates()
    {
        using var directory = new TemporaryDirectory();
        var transport = new AtomicFileQueueTransport(
            directory.Path,
            TimeSpan.FromMilliseconds(40),
            TimeSpan.FromMilliseconds(5));
        await WritePrivateAsync(
            Path.Combine(directory.Path, "ready.json"), new byte[] { 1 });

        await transport.WaitForAsync("ready.json");
        await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
            await transport.WaitForAsync("missing.json"));
    }

    [TestMethod]
    public void Validators_RequireExactDigestAndLowercaseHex()
    {
        byte[] bytes = [0xA5, 0x5A];
        var digest = AtomicFileQueueTransport.Hex(SHA256.HashData(bytes));

        AtomicFileQueueTransport.ValidateSha256(bytes, digest, "proof");
        AtomicFileQueueTransport.ValidateLowerHex("00af", 2, "field");

        Assert.ThrowsExactly<InvalidDataException>(() =>
            AtomicFileQueueTransport.ValidateSha256(bytes, new string('0', 64), "proof"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            AtomicFileQueueTransport.ValidateLowerHex("00", 2, "field"));
        Assert.ThrowsExactly<InvalidDataException>(() =>
            AtomicFileQueueTransport.ValidateLowerHex("00AF", 2, "field"));
    }

    [TestMethod]
    public async Task ReadBoundedAsync_RejectsSymbolicLinks()
    {
        if (OperatingSystem.IsWindows()) return;

        using var directory = new TemporaryDirectory();
        var transport = CreateTransport(directory.Path);
        var target = Path.Combine(directory.Path, "target.bin");
        var link = Path.Combine(directory.Path, "link.bin");
        await WritePrivateAsync(target, new byte[] { 1 });
        File.CreateSymbolicLink(link, target);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(async () =>
            await transport.ReadBoundedAsync("link.bin", 1));
    }

    private static AtomicFileQueueTransport CreateTransport(string directory) =>
        new(directory, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(5));

    private static async Task WritePrivateAsync(string path, ReadOnlyMemory<byte> bytes)
    {
        await File.WriteAllBytesAsync(path, bytes);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "neo-n4-atomic-queue-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
        }
    }
}
