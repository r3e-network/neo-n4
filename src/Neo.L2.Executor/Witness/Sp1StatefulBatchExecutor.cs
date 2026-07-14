using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Neo.Cryptography;
using Neo.L2.Batch;
using Neo.L2.Persistence;
using Neo.L2.State;

namespace Neo.L2.Executor.ProofWitness;

/// <summary>
/// Production batch executor backed by the host-native build of the exact SP1 stateful guest
/// runtime.
/// </summary>
/// <remarks>
/// See doc.md §7.3, §7.5, and §8. The executable consumes canonical <c>NEO4EXEC</c> and
/// <c>NEO4STW1</c> files and returns one content-addressed <c>NEO4EXR1</c> transition.
/// </remarks>
public sealed class Sp1StatefulBatchExecutor :
    IProofWitnessBatchExecutor,
    IInitialStateRootProvider,
    ICurrentStateRootProvider,
    ICommittedProofWitnessStateSink
{
    private static readonly TimeSpan DefaultExecutionTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaximumExecutionTimeout = TimeSpan.FromHours(24);

    private readonly Sp1StateWitnessSource _stateSource;
    private readonly string _executablePath;
    private readonly byte[] _executableSha256;
    private readonly string _scratchDirectory;
    private readonly TimeSpan _executionTimeout;
    private readonly ISp1NativeExecutionProcess _process;
    private readonly SemaphoreSlim _executionGate = new(1, 1);

    /// <summary>Create a fail-closed executor for one pinned native SP1 runtime binary.</summary>
    public Sp1StatefulBatchExecutor(
        Sp1StateWitnessSource stateSource,
        string executablePath,
        ReadOnlyMemory<byte> executableSha256,
        string? scratchDirectory = null,
        TimeSpan? executionTimeout = null)
        : this(
            stateSource,
            executablePath,
            executableSha256,
            scratchDirectory,
            executionTimeout,
            new SystemSp1NativeExecutionProcess())
    {
    }

    internal Sp1StatefulBatchExecutor(
        Sp1StateWitnessSource stateSource,
        string executablePath,
        ReadOnlyMemory<byte> executableSha256,
        string? scratchDirectory,
        TimeSpan? executionTimeout,
        ISp1NativeExecutionProcess process)
    {
        ArgumentNullException.ThrowIfNull(stateSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentNullException.ThrowIfNull(process);
        if (executableSha256.Length != SHA256.HashSizeInBytes
            || executableSha256.Span.SequenceEqual(stackalloc byte[SHA256.HashSizeInBytes]))
            throw new ArgumentException(
                "executableSha256 must be one non-zero SHA-256 digest",
                nameof(executableSha256));
        var timeout = executionTimeout ?? DefaultExecutionTimeout;
        if (timeout <= TimeSpan.Zero || timeout > MaximumExecutionTimeout)
            throw new ArgumentOutOfRangeException(
                nameof(executionTimeout), "executionTimeout must be in (0, 24 hours]");

        _executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(_executablePath))
            throw new FileNotFoundException(
                "Native SP1 execution binary does not exist", _executablePath);
        RejectReparsePoint(_executablePath, "native SP1 execution binary");
        _scratchDirectory = Path.GetFullPath(scratchDirectory ?? Path.Combine(
            Path.GetTempPath(), "neo-n4-sp1-executor"));
        Directory.CreateDirectory(_scratchDirectory);
        RejectReparsePoint(_scratchDirectory, "native SP1 scratch directory");

        _stateSource = stateSource;
        _executableSha256 = executableSha256.ToArray();
        _executionTimeout = timeout;
        _process = process;
    }

    /// <inheritdoc />
    public ValueTask<UInt256> GetInitialStateRootAsync(
        CancellationToken cancellationToken = default)
        => _stateSource.GetInitialStateRootAsync(cancellationToken);

    /// <inheritdoc />
    public ValueTask<UInt256> GetCurrentStateRootAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = _stateSource.CaptureCurrent().StateRoot;
        return ValueTask.FromResult(new UInt256(root.GetSpan()));
    }

    /// <inheritdoc />
    public ValueTask<BatchExecutionResult> ApplyBatchAsync(
        BatchExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException(
            "SP1 stateful execution requires the complete SealedBatch proof boundary");
    }

    /// <inheritdoc />
    public async ValueTask<ProofWitnessExecutionResult> ApplyBatchWithWitnessAsync(
        SealedBatch batch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(batch);
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var invocationDirectory = Path.Combine(
            _scratchDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            var snapshot = _stateSource.Capture(batch.PreStateRoot);
            var payload = batch.ToExecutionPayload();
            var payloadBytes = ExecutionPayloadSerializer.Encode(payload);
            var output = await ExecuteNativeAsync(
                invocationDirectory,
                payloadBytes,
                snapshot.Witness,
                cancellationToken).ConfigureAwait(false);
            ValidateOutput(
                payload,
                BuildPublicInputs(payload, output.ExecutionResult, payloadBytes),
                payloadBytes,
                snapshot.Witness.Span,
                output);
            return new ProofWitnessExecutionResult
            {
                ExecutionResult = output.ExecutionResult,
                ExecutionSemanticId = output.ExecutionSemanticId,
                WitnessAuthenticated = true,
                StateWitness = snapshot.Witness,
                Effects = output.Effects,
            };
        }
        finally
        {
            TryDeleteDirectory(invocationDirectory);
            _executionGate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask EnsureStateCommittedAsync(
        IProofWitnessStore durableStore,
        ProofWitnessArtifactV1 artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(durableStore);
        ArgumentNullException.ThrowIfNull(artifact);
        await _executionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var invocationDirectory = Path.Combine(
            _scratchDirectory, Guid.NewGuid().ToString("N"));
        try
        {
            var durableArtifact = await durableStore.GetAsync(
                artifact.ChainId, artifact.BatchNumber, cancellationToken)
                .ConfigureAwait(false);
            if (durableArtifact is null
                || !durableArtifact.ContentHash.Equals(artifact.ContentHash)
                || !ProofWitnessArtifactSerializer.Encode(durableArtifact).AsSpan().SequenceEqual(
                    ProofWitnessArtifactSerializer.Encode(artifact)))
                throw new InvalidOperationException(
                    "SP1 state transition requires the exact durable proof artifact");
            var current = _stateSource.CaptureCurrent();
            if (current.StateRoot.Equals(artifact.ExecutionResult.PostStateRoot)) return;
            if (!current.StateRoot.Equals(artifact.ExecutionPayload.PreStateRoot))
                throw new InvalidDataException(
                    "SP1 state root is neither the artifact pre-state nor post-state");
            if (!current.Witness.Span.SequenceEqual(artifact.StateWitness.Span))
                throw new InvalidDataException(
                    "SP1 durable artifact state witness differs from current pre-state");

            var payloadBytes = ExecutionPayloadSerializer.Encode(artifact.ExecutionPayload);
            var output = await ExecuteNativeAsync(
                invocationDirectory,
                payloadBytes,
                artifact.StateWitness,
                cancellationToken).ConfigureAwait(false);
            ValidateOutput(
                artifact.ExecutionPayload,
                artifact.PublicInputs,
                payloadBytes,
                artifact.StateWitness.Span,
                output);
            if (!output.ExecutionResult.Equals(artifact.ExecutionResult)
                || !output.Effects.Span.SequenceEqual(artifact.Effects.Span))
                throw new InvalidDataException(
                    "replayed SP1 execution differs from the durable proof artifact");

            var committedRoot = _stateSource.CommitTransition(
                artifact.ExecutionPayload.PreStateRoot,
                artifact.ExecutionResult.PostStateRoot,
                output.PostStateWitness);
            if (!committedRoot.Equals(artifact.ExecutionResult.PostStateRoot))
                throw new InvalidDataException(
                    "committed SP1 state root differs from the durable proof artifact");
        }
        finally
        {
            TryDeleteDirectory(invocationDirectory);
            _executionGate.Release();
        }
    }

    private static void ValidateOutput(
        ExecutionPayloadV1 payload,
        PublicInputs expectedPublicInputs,
        ReadOnlySpan<byte> payloadBytes,
        ReadOnlySpan<byte> stateWitnessBytes,
        Sp1NativeExecutionOutputV1 output)
    {
        if (!output.RequestPayloadHash.Equals(new UInt256(Crypto.Hash256(payloadBytes))))
            throw new InvalidDataException(
                "Native SP1 result does not bind the exact execution payload");
        if (!output.RequestStateWitnessHash.Equals(
            new UInt256(Crypto.Hash256(stateWitnessBytes))))
            throw new InvalidDataException(
                "Native SP1 result does not bind the exact pre-state witness");
        if (!output.ExecutionSemanticId.Equals(ExecutionSemanticIds.Sp1StatefulNeoVmV1))
            throw new InvalidDataException("Native SP1 result uses a different execution semantic");
        var publicInputs = BuildPublicInputs(payload, output.ExecutionResult, payloadBytes);
        if (!BatchSerializer.EncodePublicInputs(publicInputs).AsSpan().SequenceEqual(
            BatchSerializer.EncodePublicInputs(expectedPublicInputs)))
            throw new InvalidDataException(
                "Native SP1 result differs from the expected canonical public inputs");
        if (!output.PublicInputHash.Equals(StateRootCalculator.HashPublicInputs(publicInputs)))
            throw new InvalidDataException(
                "Native SP1 result public-input hash differs from the canonical batch inputs");
    }

    private static PublicInputs BuildPublicInputs(
        ExecutionPayloadV1 payload,
        BatchExecutionResult executionResult,
        ReadOnlySpan<byte> payloadBytes)
        => new()
        {
            ChainId = payload.ChainId,
            BatchNumber = payload.BatchNumber,
            PreStateRoot = payload.PreStateRoot,
            PostStateRoot = executionResult.PostStateRoot,
            TxRoot = executionResult.TxRoot,
            ReceiptRoot = executionResult.ReceiptRoot,
            WithdrawalRoot = executionResult.WithdrawalRoot,
            L2ToL1MessageRoot = executionResult.L2ToL1MessageRoot,
            L2ToL2MessageRoot = executionResult.L2ToL2MessageRoot,
            L1MessageHash = StateRootCalculator.HashL1Messages(payload.L1Messages),
            DACommitment = ExecutionPayloadSerializer.ComputeCommitment(payloadBytes),
            BlockContextHash = StateRootCalculator.HashBlockContext(payload.BlockContext),
        };

    private async ValueTask<Sp1NativeExecutionOutputV1> ExecuteNativeAsync(
        string invocationDirectory,
        ReadOnlyMemory<byte> payloadBytes,
        ReadOnlyMemory<byte> stateWitness,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(invocationDirectory);
        RejectReparsePoint(invocationDirectory, "native SP1 invocation directory");
        var invocationExecutable = Path.Combine(
            invocationDirectory,
            OperatingSystem.IsWindows()
                ? "neo-zkvm-executor.exe"
                : "neo-zkvm-executor");
        await CopyPinnedExecutableAsync(
            _executablePath,
            invocationExecutable,
            _executableSha256,
            cancellationToken).ConfigureAwait(false);
        var payloadPath = Path.Combine(invocationDirectory, "payload.bin");
        var stateWitnessPath = Path.Combine(invocationDirectory, "pre-state.bin");
        var outputPath = Path.Combine(invocationDirectory, "result.bin");
        await WriteNewAsync(payloadPath, payloadBytes, cancellationToken).ConfigureAwait(false);
        await WriteNewAsync(stateWitnessPath, stateWitness, cancellationToken).ConfigureAwait(false);

        var processResult = await _process.RunAsync(new Sp1NativeExecutionProcessRequest(
            invocationExecutable,
            invocationDirectory,
            payloadPath,
            stateWitnessPath,
            outputPath,
            _executionTimeout), cancellationToken).ConfigureAwait(false);
        if (processResult.ExitCode != 0)
            throw new InvalidOperationException(
                $"Native SP1 executor exited with code {processResult.ExitCode}: "
                + NormalizeDiagnostic(processResult.StandardError));
        var outputBytes = await ReadBoundedAsync(
            outputPath,
            Sp1NativeExecutionOutputSerializer.MaxEncodedBytes,
            cancellationToken).ConfigureAwait(false);
        return Sp1NativeExecutionOutputSerializer.Decode(outputBytes);
    }

    private static async ValueTask WriteNewAsync(
        string path,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        stream.Flush(flushToDisk: true);
    }

    private static async ValueTask CopyPinnedExecutableAsync(
        string sourcePath,
        string destinationPath,
        ReadOnlyMemory<byte> expectedSha256,
        CancellationToken cancellationToken)
    {
        RejectReparsePoint(sourcePath, "native SP1 execution binary");
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(
                buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }
        var actualSha256 = hash.GetHashAndReset();
        if (!actualSha256.AsSpan().SequenceEqual(expectedSha256.Span))
            throw new InvalidDataException(
                "Native SP1 execution binary SHA-256 differs from the pinned operator digest");
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        destination.Flush(flushToDisk: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                destinationPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    private static async ValueTask<byte[]> ReadBoundedAsync(
        string path,
        int maximumLength,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("Native SP1 executor did not produce result.bin");
        RejectReparsePoint(path, "native SP1 execution result");
        var length = new FileInfo(path).Length;
        if (length < 0 || length > maximumLength)
            throw new InvalidDataException(
                $"Native SP1 execution result exceeds {maximumLength} bytes");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        if (bytes.LongLength != length || bytes.Length > maximumLength)
            throw new InvalidDataException(
                "Native SP1 execution result changed while being read");
        return bytes;
    }

    private static string NormalizeDiagnostic(string diagnostic)
    {
        if (string.IsNullOrWhiteSpace(diagnostic)) return "no diagnostic output";
        return diagnostic.ReplaceLineEndings(" ").Trim();
    }

    private static void RejectReparsePoint(string path, string description)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{description} must not be a symbolic link");
    }

    private static void TryDeleteDirectory(string path)
    {
        if (!Directory.Exists(path)) return;
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

internal sealed record Sp1NativeExecutionProcessRequest(
    string ExecutablePath,
    string WorkingDirectory,
    string PayloadPath,
    string StateWitnessPath,
    string OutputPath,
    TimeSpan Timeout);

internal sealed record Sp1NativeExecutionProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

internal interface ISp1NativeExecutionProcess
{
    ValueTask<Sp1NativeExecutionProcessResult> RunAsync(
        Sp1NativeExecutionProcessRequest request,
        CancellationToken cancellationToken);
}

internal sealed class SystemSp1NativeExecutionProcess : ISp1NativeExecutionProcess
{
    private const int MaximumDiagnosticBytes = 64 * 1024;

    public async ValueTask<Sp1NativeExecutionProcessResult> RunAsync(
        Sp1NativeExecutionProcessRequest request,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.ExecutablePath,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("--payload");
        startInfo.ArgumentList.Add(request.PayloadPath);
        startInfo.ArgumentList.Add("--state-witness");
        startInfo.ArgumentList.Add(request.StateWitnessPath);
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(request.OutputPath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
            throw new InvalidOperationException("Failed to start native SP1 executor");
        var standardOutput = ReadBoundedAsync(
            process.StandardOutput.BaseStream, MaximumDiagnosticBytes);
        var standardError = ReadBoundedAsync(
            process.StandardError.BaseStream, MaximumDiagnosticBytes);
        var processExit = process.WaitForExitAsync();
        using var timeout = new CancellationTokenSource(request.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, timeout.Token);
        try
        {
            await Task.WhenAll(processExit, standardOutput, standardError)
                .WaitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException(
                $"Native SP1 execution exceeded {request.Timeout}");
        }
        catch
        {
            Kill(process);
            throw;
        }

        return new Sp1NativeExecutionProcessResult(
            process.ExitCode,
            Encoding.UTF8.GetString(await standardOutput.ConfigureAwait(false)),
            Encoding.UTF8.GetString(await standardError.ConfigureAwait(false)));
    }

    private static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximumLength)
    {
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) return output.ToArray();
            if (output.Length + read > maximumLength)
                throw new InvalidDataException(
                    $"Native SP1 diagnostic output exceeds {maximumLength} bytes");
            output.Write(buffer, 0, read);
        }
    }

    private static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        catch (NotSupportedException) { }
    }
}
