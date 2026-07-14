using System.Diagnostics;
using System.Security.Cryptography;

namespace Neo.L2.Proving.RiscVZk;

/// <summary>Atomic, bounded file-queue transport shared by SP1 prover clients.</summary>
/// <remarks>
/// See doc.md §4, §7.5, and §8. Artifacts are immutable and idempotent, result manifests are
/// readiness markers, symbolic links are rejected, and every read is size-bounded.
/// </remarks>
public sealed class AtomicFileQueueTransport
{
    private const long DefaultMaximumQueueBytes = 16L * 1024 * 1024 * 1024;
    private const int DefaultMaximumRequestCount = 64;
    private static readonly TimeSpan MaximumTimeout = TimeSpan.FromHours(24);
    private readonly string _directory;
    private readonly TimeSpan _resultTimeout;
    private readonly TimeSpan _pollInterval;
    private readonly long _maximumQueueBytes;
    private readonly int _maximumRequestCount;

    /// <summary>Create a transport rooted at one dedicated shared queue directory.</summary>
    public AtomicFileQueueTransport(
        string directory,
        TimeSpan resultTimeout,
        TimeSpan pollInterval,
        long maximumQueueBytes = DefaultMaximumQueueBytes,
        int maximumRequestCount = DefaultMaximumRequestCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (resultTimeout <= TimeSpan.Zero || resultTimeout > MaximumTimeout)
            throw new ArgumentOutOfRangeException(
                nameof(resultTimeout), "resultTimeout must be in (0, 24 hours]");
        if (pollInterval <= TimeSpan.Zero || pollInterval > resultTimeout)
            throw new ArgumentOutOfRangeException(
                nameof(pollInterval),
                "pollInterval must be positive and no greater than resultTimeout");
        if (maximumQueueBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumQueueBytes));
        if (maximumRequestCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumRequestCount));

        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
        RejectReparsePoint(_directory, "queue directory");
        EnsurePrivateDirectory(_directory);
        SecureExistingArtifacts(_directory);
        _resultTimeout = resultTimeout;
        _pollInterval = pollInterval;
        _maximumQueueBytes = maximumQueueBytes;
        _maximumRequestCount = maximumRequestCount;
    }

    /// <summary>Absolute queue directory.</summary>
    public string DirectoryPath => _directory;

    /// <summary>Atomically publish immutable bytes or validate an existing idempotent artifact.</summary>
    public async ValueTask PublishExactAsync(
        string fileName,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
        => await PublishExactCoreAsync(
            fileName, bytes, enforceRequestCapacity: false, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Publish one request subject to queue byte and task hard limits.</summary>
    public async ValueTask PublishRequestExactAsync(
        string fileName,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken = default)
        => await PublishExactCoreAsync(
            fileName, bytes, enforceRequestCapacity: true, cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask PublishExactCoreAsync(
        string fileName,
        ReadOnlyMemory<byte> bytes,
        bool enforceRequestCapacity,
        CancellationToken cancellationToken)
    {
        var path = Resolve(fileName);
        await using var publisherLock = await AcquirePublisherLockAsync(cancellationToken)
            .ConfigureAwait(false);
        if (File.Exists(path))
        {
            var existing = await ReadBoundedPathAsync(
                path, bytes.Length, cancellationToken).ConfigureAwait(false);
            if (!existing.AsSpan().SequenceEqual(bytes.Span))
                throw new InvalidDataException(
                    $"existing idempotent artifact differs: {fileName}");
            return;
        }
        if (enforceRequestCapacity) EnsureRequestCapacity(bytes.Length);

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = CreatePrivateWriteStream(temporaryPath))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                var existing = await ReadBoundedPathAsync(
                    path, bytes.Length, cancellationToken).ConfigureAwait(false);
                if (!existing.AsSpan().SequenceEqual(bytes.Span))
                    throw new InvalidDataException(
                        $"existing idempotent artifact differs: {fileName}");
            }
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private async ValueTask<FileStream> AcquirePublisherLockAsync(
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(_directory, ".neo4-publisher.lock");
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var options = new FileStreamOptions
                {
                    Mode = FileMode.OpenOrCreate,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
                };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                var stream = new FileStream(path, options);
                SetPrivateFile(path);
                EnsurePrivateFile(path);
                return stream;
            }
            catch (IOException) when (stopwatch.Elapsed < _resultTimeout)
            {
                var remaining = _resultTimeout - stopwatch.Elapsed;
                await Task.Delay(
                    remaining < _pollInterval ? remaining : _pollInterval,
                    cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private void EnsureRequestCapacity(int incomingBytes)
    {
        long totalBytes = 0;
        var requestCount = 0;
        foreach (var path in Directory.EnumerateFiles(_directory, "*", SearchOption.TopDirectoryOnly))
        {
            RejectReparsePoint(path, "SP1 queue artifact");
            EnsurePrivateFile(path);
            var name = Path.GetFileName(path);
            if (name.EndsWith(Sp1BatchFileQueueProtocol.RequestSuffix, StringComparison.Ordinal)
                || name.EndsWith(
                    Sp1BatchFileQueueProtocol.RequestSuffix + ".done",
                    StringComparison.Ordinal))
                requestCount++;
            totalBytes = checked(totalBytes + new FileInfo(path).Length);
        }
        if (requestCount >= _maximumRequestCount)
            throw new InvalidOperationException(
                $"SP1 queue request limit reached: {requestCount}/{_maximumRequestCount}");
        if (incomingBytes > _maximumQueueBytes - totalBytes)
            throw new InvalidOperationException(
                $"SP1 queue byte limit reached: {totalBytes} + {incomingBytes} > {_maximumQueueBytes}");
    }

    /// <summary>Wait until a readiness manifest is present.</summary>
    public async ValueTask WaitForAsync(
        string fileName,
        CancellationToken cancellationToken = default)
    {
        var path = Resolve(fileName);
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (stopwatch.Elapsed >= _resultTimeout)
                throw new TimeoutException(
                    $"SP1 daemon did not publish {fileName} within {_resultTimeout}");
            var remaining = _resultTimeout - stopwatch.Elapsed;
            await Task.Delay(
                remaining < _pollInterval ? remaining : _pollInterval,
                cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Read an artifact with an exact canonical length.</summary>
    public async ValueTask<byte[]> ReadExactAsync(
        string fileName,
        int exactLength,
        CancellationToken cancellationToken = default)
    {
        var bytes = await ReadBoundedAsync(
            fileName, exactLength, cancellationToken).ConfigureAwait(false);
        if (bytes.Length != exactLength)
            throw new InvalidDataException(
                $"{fileName} must be exactly {exactLength} bytes, got {bytes.Length}");
        return bytes;
    }

    /// <summary>Read an artifact without exceeding its protocol size limit.</summary>
    public ValueTask<byte[]> ReadBoundedAsync(
        string fileName,
        int maximumLength,
        CancellationToken cancellationToken = default)
        => ReadBoundedPathAsync(Resolve(fileName), maximumLength, cancellationToken);

    /// <summary>Validate a lowercase SHA-256 digest.</summary>
    public static void ValidateSha256(
        ReadOnlySpan<byte> bytes,
        string expected,
        string artifact)
    {
        var actual = Hex(SHA256.HashData(bytes));
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"SP1 {artifact} SHA-256 mismatch");
    }

    /// <summary>Validate exact-length lowercase hexadecimal text.</summary>
    public static void ValidateLowerHex(string value, int byteLength, string field)
    {
        if (value is null || value.Length != checked(byteLength * 2))
            throw new InvalidDataException($"{field} must encode exactly {byteLength} bytes");
        foreach (var character in value)
        {
            if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))
                throw new InvalidDataException($"{field} must be lowercase hexadecimal");
        }
    }

    /// <summary>Encode bytes as lowercase hexadecimal text.</summary>
    public static string Hex(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(bytes).ToLowerInvariant();

    private async ValueTask<byte[]> ReadBoundedPathAsync(
        string path,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        if (maximumLength < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLength));
        if (!File.Exists(path))
            throw new InvalidDataException(
                $"SP1 queue artifact is missing: {Path.GetFileName(path)}");
        RejectReparsePoint(path, "SP1 queue artifact");
        EnsurePrivateFile(path);
        var length = new FileInfo(path).Length;
        if (length < 0 || length > maximumLength)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} exceeds the {maximumLength}-byte protocol limit");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.Length > maximumLength || bytes.LongLength != length)
            throw new InvalidDataException(
                $"{Path.GetFileName(path)} changed while being read");
        return bytes;
    }

    private string Resolve(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (!string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal)
            || fileName is "." or "..")
            throw new InvalidDataException("SP1 queue filenames must be single path leaves");
        return Path.Combine(_directory, fileName);
    }

    private static void RejectReparsePoint(string path, string description)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException(
                $"{description} must not be a symbolic link or reparse point");
    }

    private static FileStream CreatePrivateWriteStream(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 4096,
            Options = FileOptions.Asynchronous | FileOptions.WriteThrough,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private static void EnsurePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        const UnixFileMode expected = UnixFileMode.UserRead
            | UnixFileMode.UserWrite
            | UnixFileMode.UserExecute;
        File.SetUnixFileMode(path, expected);
        if (File.GetUnixFileMode(path) != expected)
            throw new InvalidDataException("SP1 queue directory must have mode 0700");
    }

    private static void EnsurePrivateFile(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        const UnixFileMode expected = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        if (File.GetUnixFileMode(path) != expected)
            throw new InvalidDataException(
                $"SP1 queue artifact must have mode 0600: {Path.GetFileName(path)}");
    }

    private static void SetPrivateFile(string path)
    {
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void SecureExistingArtifacts(string directory)
    {
        if (OperatingSystem.IsWindows()) return;
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
        {
            RejectReparsePoint(path, "SP1 queue artifact");
            SetPrivateFile(path);
            EnsurePrivateFile(path);
        }
    }
}
