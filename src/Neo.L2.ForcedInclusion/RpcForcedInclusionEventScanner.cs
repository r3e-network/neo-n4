using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Neo.Json;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.ForcedInclusion;

/// <summary>
/// Durable Neo L1 event scanner for <c>NeoHub.ForcedInclusion.ForcedTxEnqueued</c>.
/// </summary>
/// <remarks>
/// See doc.md §15.4. Each observed nonce is synchronously persisted before the block cursor,
/// so a process crash can only replay a block; it cannot advance past an undiscoverable entry.
/// The scanner verifies the persisted block hash before resuming and fails closed on a reorg.
/// </remarks>
public sealed class RpcForcedInclusionEventScanner : IDisposable
{
    private const byte EncodingVersion = 1;
    private const byte CursorTag = 0;
    private const byte NonceTag = 1;
    private const int CursorLength = 1 + sizeof(uint) + UInt256.Length;
    private const string EventName = "ForcedTxEnqueued";
    private static readonly byte[] Namespace = Encoding.ASCII.GetBytes("neo-n4:forced-inclusion-events:v1:");

    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _contractHash;
    private readonly uint _chainId;
    private readonly IL2KeyValueStore _store;
    private readonly bool _ownsStore;
    private readonly uint _startHeight;
    private readonly uint _finalityDepth;
    private readonly int _maximumBlocksPerScan;
    private readonly byte[] _baseKey;
    private readonly byte[] _cursorKey;
    private readonly byte[] _noncePrefix;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private int _disposed;

    /// <summary>
    /// Construct a durable scanner over a key-value store.
    /// When <paramref name="ownsStore"/> is true, <see cref="Dispose"/> disposes the store.
    /// </summary>
    public RpcForcedInclusionEventScanner(
        JsonRpcClient rpc,
        UInt160 contractHash,
        uint chainId,
        IL2KeyValueStore store,
        uint startHeight,
        uint finalityDepth = 1,
        int maximumBlocksPerScan = 256,
        bool ownsStore = false)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(contractHash);
        ArgumentNullException.ThrowIfNull(store);
        if (contractHash.Equals(UInt160.Zero))
            throw new ArgumentException("forced-inclusion contract hash must not be zero", nameof(contractHash));
        if (chainId == 0)
            throw new ArgumentOutOfRangeException(nameof(chainId), "chain id must be non-zero");
        if (maximumBlocksPerScan <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumBlocksPerScan), "maximum blocks per scan must be positive");

        _rpc = rpc;
        _contractHash = contractHash;
        _chainId = chainId;
        _store = store;
        _ownsStore = ownsStore;
        _startHeight = startHeight;
        _finalityDepth = finalityDepth;
        _maximumBlocksPerScan = maximumBlocksPerScan;
        _baseKey = BuildBaseKey(contractHash, chainId);
        _cursorKey = Append(_baseKey, CursorTag);
        _noncePrefix = Append(_baseKey, NonceTag);
    }

    /// <summary>Load all discovered, not-yet-confirmed-consumed nonces from durable state.</summary>
    public IReadOnlyList<ulong> LoadTrackedNonces()
    {
        ThrowIfDisposed();
        return _store.EnumeratePrefix(_noncePrefix)
            .Select(pair => DecodeNonceKey(pair.Key))
            .OrderBy(nonce => nonce)
            .ToArray();
    }

    /// <summary>Persist a nonce before exposing it to the in-memory batch source.</summary>
    public void TrackNonce(ulong nonce)
    {
        ThrowIfDisposed();
        _store.TryPut(BuildNonceKey(nonce), new byte[] { EncodingVersion });
    }

    /// <summary>Forget a nonce only after L1 confirms it consumed.</summary>
    public void ForgetNonce(ulong nonce)
    {
        ThrowIfDisposed();
        _store.Delete(BuildNonceKey(nonce));
    }

    /// <summary>
    /// Scan finalized blocks from the durable cursor and register matching nonces.
    /// Returns the number of blocks committed to the cursor during this call.
    /// </summary>
    public async ValueTask<int> ScanAsync(
        Action<ulong> registerNonce,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registerNonce);
        ThrowIfDisposed();
        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cursor = ReadCursor();
            await VerifyResumeHashAsync(cursor, cancellationToken).ConfigureAwait(false);

            var blockCount = ParseUInt32(
                await _rpc.CallAsync("getblockcount", new JArray(), cancellationToken)
                    .ConfigureAwait(false),
                "getblockcount");
            if (blockCount <= _finalityDepth)
                return 0;

            var safeHeight = checked(blockCount - 1 - _finalityDepth);
            if (cursor.NextHeight > safeHeight)
                return 0;

            var scanned = 0;
            var nextHeight = cursor.NextHeight;
            var previousHash = cursor.LastHash;
            while (nextHeight <= safeHeight && scanned < _maximumBlocksPerScan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = await ReadBlockAsync(nextHeight, cancellationToken).ConfigureAwait(false);
                if (previousHash is not null && !block.PreviousHash.Equals(previousHash))
                    throw new InvalidDataException(
                        $"L1 block {nextHeight} previous hash {block.PreviousHash} does not match durable cursor hash {previousHash}");

                foreach (var transactionHash in block.TransactionHashes)
                {
                    var nonces = await ReadMatchingNoncesAsync(transactionHash, cancellationToken)
                        .ConfigureAwait(false);
                    foreach (var nonce in nonces)
                    {
                        TrackNonce(nonce);
                        registerNonce(nonce);
                    }
                }

                nextHeight = checked(nextHeight + 1);
                WriteCursor(new ScannerCursor(nextHeight, block.Hash));
                previousHash = block.Hash;
                scanned++;
            }
            return scanned;
        }
        finally
        {
            _scanGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _scanGate.Dispose();
        if (_ownsStore) _store.Dispose();
    }

    private async ValueTask VerifyResumeHashAsync(
        ScannerCursor cursor,
        CancellationToken cancellationToken)
    {
        if (cursor.NextHeight == _startHeight || cursor.LastHash is null)
            return;
        var priorHeight = checked(cursor.NextHeight - 1);
        var actual = ParseUInt256(
            await _rpc.CallAsync(
                "getblockhash", new JArray { priorHeight }, cancellationToken)
                .ConfigureAwait(false),
            $"getblockhash({priorHeight})");
        if (!actual.Equals(cursor.LastHash))
            throw new InvalidDataException(
                $"L1 finalized history changed at height {priorHeight}: durable hash {cursor.LastHash}, RPC hash {actual}");
    }

    private async ValueTask<ScannedBlock> ReadBlockAsync(
        uint height,
        CancellationToken cancellationToken)
    {
        var result = await _rpc.CallAsync(
            "getblock", new JArray { height, true }, cancellationToken).ConfigureAwait(false);
        if (result is not JObject block)
            throw new InvalidDataException($"getblock({height}) returned a non-object");
        var actualHeight = ParseUInt32(block["index"], $"getblock({height}).index");
        if (actualHeight != height)
            throw new InvalidDataException(
                $"getblock({height}) returned block index {actualHeight}");
        var hash = ParseUInt256(block["hash"], $"getblock({height}).hash");
        var previousHash = ParseUInt256(
            block["previousblockhash"], $"getblock({height}).previousblockhash");
        if (block["tx"] is not JArray transactions)
            throw new InvalidDataException($"getblock({height}) returned no transaction array");
        var hashes = new List<UInt256>(transactions.Count);
        foreach (var token in transactions)
        {
            if (token is not JObject transaction)
                throw new InvalidDataException($"getblock({height}) returned a non-object transaction");
            hashes.Add(ParseUInt256(transaction["hash"], $"getblock({height}).tx.hash"));
        }
        return new ScannedBlock(hash, previousHash, hashes);
    }

    private async ValueTask<IReadOnlyList<ulong>> ReadMatchingNoncesAsync(
        UInt256 transactionHash,
        CancellationToken cancellationToken)
    {
        var result = await _rpc.CallAsync(
            "getapplicationlog", new JArray { transactionHash.ToString() }, cancellationToken)
            .ConfigureAwait(false);
        if (result is not JObject log || log["executions"] is not JArray executions)
            throw new InvalidDataException(
                $"getapplicationlog({transactionHash}) returned no executions");

        var nonces = new List<ulong>();
        foreach (var executionToken in executions)
        {
            if (executionToken is not JObject execution)
                throw new InvalidDataException(
                    $"getapplicationlog({transactionHash}) returned an invalid execution");
            if (!string.Equals(execution["vmstate"]?.AsString(), "HALT", StringComparison.Ordinal))
                continue;
            if (execution["notifications"] is not JArray notifications)
                continue;
            foreach (var notificationToken in notifications)
            {
                if (notificationToken is not JObject notification
                    || !string.Equals(
                        notification["eventname"]?.AsString(), EventName, StringComparison.Ordinal)
                    || !string.Equals(
                        notification["contract"]?.AsString(), _contractHash.ToString(), StringComparison.OrdinalIgnoreCase))
                    continue;

                if (notification["state"] is not JObject state
                    || !string.Equals(state["type"]?.AsString(), "Array", StringComparison.Ordinal)
                    || state["value"] is not JArray values
                    || values.Count != 4)
                    throw new InvalidDataException(
                        $"{EventName} in {transactionHash} has a non-canonical state payload");
                var eventChainId = ParseStackUInt32(values[0], "ForcedTxEnqueued.chainId");
                var nonce = ParseStackUInt64(values[1], "ForcedTxEnqueued.nonce");
                if (eventChainId == _chainId)
                    nonces.Add(nonce);
            }
        }
        return nonces;
    }

    private ScannerCursor ReadCursor()
    {
        var bytes = _store.Get(_cursorKey);
        if (bytes is null)
            return new ScannerCursor(_startHeight, null);
        if (bytes.Length != CursorLength || bytes[0] != EncodingVersion)
            throw new InvalidDataException("forced-inclusion scan cursor has an unsupported encoding");
        var nextHeight = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1, sizeof(uint)));
        if (nextHeight < _startHeight)
            throw new InvalidDataException(
                $"forced-inclusion scan cursor {nextHeight} precedes configured start height {_startHeight}");
        return new ScannerCursor(nextHeight, new UInt256(bytes.AsSpan(1 + sizeof(uint), UInt256.Length)));
    }

    private void WriteCursor(ScannerCursor cursor)
    {
        if (cursor.LastHash is null)
            throw new InvalidOperationException("a committed scan cursor requires a block hash");
        Span<byte> encoded = stackalloc byte[CursorLength];
        encoded[0] = EncodingVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(encoded.Slice(1, sizeof(uint)), cursor.NextHeight);
        cursor.LastHash.GetSpan().CopyTo(encoded.Slice(1 + sizeof(uint), UInt256.Length));
        _store.Put(_cursorKey, encoded);
    }

    private byte[] BuildNonceKey(ulong nonce)
    {
        var key = new byte[_noncePrefix.Length + sizeof(ulong)];
        _noncePrefix.CopyTo(key, 0);
        BinaryPrimitives.WriteUInt64LittleEndian(key.AsSpan(_noncePrefix.Length), nonce);
        return key;
    }

    private ulong DecodeNonceKey(byte[] key)
    {
        if (key.Length != _noncePrefix.Length + sizeof(ulong)
            || !key.AsSpan().StartsWith(_noncePrefix))
            throw new InvalidDataException("forced-inclusion nonce key has an invalid encoding");
        return BinaryPrimitives.ReadUInt64LittleEndian(key.AsSpan(_noncePrefix.Length));
    }

    private static byte[] BuildBaseKey(UInt160 contractHash, uint chainId)
    {
        var key = new byte[Namespace.Length + UInt160.Length + sizeof(uint)];
        Namespace.CopyTo(key, 0);
        contractHash.GetSpan().CopyTo(key.AsSpan(Namespace.Length, UInt160.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            key.AsSpan(Namespace.Length + UInt160.Length, sizeof(uint)), chainId);
        return key;
    }

    private static byte[] Append(byte[] prefix, byte suffix)
    {
        var result = new byte[prefix.Length + 1];
        prefix.CopyTo(result, 0);
        result[^1] = suffix;
        return result;
    }

    private static uint ParseUInt32(JToken? token, string field)
    {
        if (token is not JNumber number)
            throw new InvalidDataException($"{field} returned a non-number");
        var value = number.AsNumber();
        if (!double.IsFinite(value)
            || value < uint.MinValue
            || value > uint.MaxValue
            || value != Math.Truncate(value))
            throw new InvalidDataException($"{field} returned a non-uint value");
        return (uint)value;
    }

    private static UInt256 ParseUInt256(JToken? token, string field)
    {
        if (token is not JString text)
            throw new InvalidDataException($"{field} returned a non-string hash");
        try
        {
            return UInt256.Parse(text.AsString());
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            throw new InvalidDataException($"{field} returned an invalid UInt256 hash", exception);
        }
    }

    private static uint ParseStackUInt32(JToken? token, string field)
    {
        var value = ParseStackInteger(token, field);
        if (value < uint.MinValue || value > uint.MaxValue)
            throw new InvalidDataException($"{field} is outside uint range");
        return (uint)value;
    }

    private static ulong ParseStackUInt64(JToken? token, string field)
    {
        var value = ParseStackInteger(token, field);
        if (value < ulong.MinValue || value > ulong.MaxValue)
            throw new InvalidDataException($"{field} is outside ulong range");
        return (ulong)value;
    }

    private static BigInteger ParseStackInteger(JToken? token, string field)
    {
        if (token is not JObject item
            || !string.Equals(item["type"]?.AsString(), "Integer", StringComparison.Ordinal)
            || item["value"] is not JString value
            || !BigInteger.TryParse(value.AsString(), out var parsed))
            throw new InvalidDataException($"{field} is not a canonical Integer stack item");
        return parsed;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    private sealed record ScannerCursor(uint NextHeight, UInt256? LastHash);

    private sealed record ScannedBlock(
        UInt256 Hash,
        UInt256 PreviousHash,
        IReadOnlyList<UInt256> TransactionHashes);
}
