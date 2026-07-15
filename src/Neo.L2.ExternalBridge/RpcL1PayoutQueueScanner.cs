using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Neo.Json;
using Neo.L2.Bridge.External;
using Neo.L2.Persistence;
using Neo.L2.Settlement.Rpc;

namespace Neo.L2.ExternalBridge;

/// <summary>Durable finalized-block scanner for one immutable L1 payout adapter.</summary>
/// <remarks>
/// See <c>doc.md</c> §11.3 and §17. An instruction is validated and inserted into the durable
/// outbox before the block cursor advances. A crash can replay a finalized block but cannot skip
/// an instruction. The exact adapter contract hash authenticates the notification source.
/// </remarks>
public sealed class RpcL1PayoutQueueScanner : IDisposable
{
    private const byte EncodingVersion = 1;
    private const int CursorLength = 1 + sizeof(uint) + UInt256.Length;
    private const string EventName = "PayoutEnqueued";
    private static readonly byte[] CursorNamespace =
        Encoding.ASCII.GetBytes("neo4:l2-payout-relay:scan:v1:");
    private readonly JsonRpcClient _rpc;
    private readonly UInt160 _adapter;
    private readonly uint _neoChainId;
    private readonly IL2KeyValueStore _store;
    private readonly PersistentL2PayoutOutbox _outbox;
    private readonly uint _startHeight;
    private readonly uint _finalityDepth;
    private readonly int _maximumBlocksPerScan;
    private readonly byte[] _cursorKey;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private int _disposed;

    /// <summary>Construct a finalized scanner over the same durable store as the outbox.</summary>
    public RpcL1PayoutQueueScanner(
        JsonRpcClient rpc,
        UInt160 adapter,
        uint neoChainId,
        IL2KeyValueStore store,
        PersistentL2PayoutOutbox outbox,
        uint startHeight,
        uint finalityDepth = 1,
        int maximumBlocksPerScan = 256)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(outbox);
        if (adapter == UInt160.Zero)
            throw new ArgumentException("Adapter hash must not be zero.", nameof(adapter));
        if (neoChainId == 0) throw new ArgumentOutOfRangeException(nameof(neoChainId));
        if (maximumBlocksPerScan <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBlocksPerScan));
        _rpc = rpc;
        _adapter = adapter;
        _neoChainId = neoChainId;
        _store = store;
        _outbox = outbox;
        _startHeight = startHeight;
        _finalityDepth = finalityDepth;
        _maximumBlocksPerScan = maximumBlocksPerScan;
        _cursorKey = BuildCursorKey(adapter, neoChainId);
    }

    /// <summary>Scan finalized L1 blocks and durably enqueue every exact adapter event.</summary>
    public async ValueTask<int> ScanAsync(CancellationToken cancellationToken = default)
    {
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
            if (blockCount <= _finalityDepth) return 0;
            var safeHeight = checked(blockCount - 1 - _finalityDepth);
            if (cursor.NextHeight > safeHeight) return 0;

            var scanned = 0;
            var nextHeight = cursor.NextHeight;
            var previousHash = cursor.LastHash;
            while (nextHeight <= safeHeight && scanned < _maximumBlocksPerScan)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var block = await ReadBlockAsync(nextHeight, cancellationToken).ConfigureAwait(false);
                if (previousHash is not null && block.PreviousHash != previousHash)
                    throw new InvalidDataException(
                        $"L1 block {nextHeight} does not extend the durable payout cursor.");
                foreach (var transactionHash in block.TransactionHashes)
                {
                    var instructions = await ReadInstructionsAsync(
                        transactionHash, cancellationToken).ConfigureAwait(false);
                    foreach (var instruction in instructions) _outbox.Enqueue(instruction);
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
    }

    private async ValueTask VerifyResumeHashAsync(
        ScannerCursor cursor,
        CancellationToken cancellationToken)
    {
        if (cursor.NextHeight == _startHeight || cursor.LastHash is null) return;
        var priorHeight = checked(cursor.NextHeight - 1);
        var actual = ParseUInt256(
            await _rpc.CallAsync(
                "getblockhash", new JArray { priorHeight }, cancellationToken)
                .ConfigureAwait(false),
            $"getblockhash({priorHeight})");
        if (actual != cursor.LastHash)
            throw new InvalidDataException(
                $"L1 finalized payout history changed at height {priorHeight}.");
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
            throw new InvalidDataException($"getblock({height}) returned index {actualHeight}");
        var hash = ParseUInt256(block["hash"], $"getblock({height}).hash");
        var previousHash = ParseUInt256(
            block["previousblockhash"], $"getblock({height}).previousblockhash");
        if (block["tx"] is not JArray transactions)
            throw new InvalidDataException($"getblock({height}) returned no transaction array");
        var transactionHashes = new List<UInt256>(transactions.Count);
        foreach (var token in transactions)
        {
            if (token is not JObject transaction)
                throw new InvalidDataException($"getblock({height}) returned an invalid transaction");
            transactionHashes.Add(ParseUInt256(
                transaction["hash"], $"getblock({height}).tx.hash"));
        }
        return new ScannedBlock(hash, previousHash, transactionHashes);
    }

    private async ValueTask<IReadOnlyList<L2PayoutInstruction>> ReadInstructionsAsync(
        UInt256 transactionHash,
        CancellationToken cancellationToken)
    {
        var result = await _rpc.CallAsync(
            "getapplicationlog", new JArray { transactionHash.ToString() }, cancellationToken)
            .ConfigureAwait(false);
        if (result is not JObject log || log["executions"] is not JArray executions)
            throw new InvalidDataException(
                $"getapplicationlog({transactionHash}) returned no executions");
        var instructions = new List<L2PayoutInstruction>();
        foreach (var executionToken in executions)
        {
            if (executionToken is not JObject execution)
                throw new InvalidDataException("Application log contains an invalid execution.");
            if (!string.Equals(execution["vmstate"]?.AsString(), "HALT", StringComparison.Ordinal))
                continue;
            if (execution["notifications"] is not JArray notifications) continue;
            foreach (var notificationToken in notifications)
            {
                if (notificationToken is not JObject notification
                    || !string.Equals(notification["eventname"]?.AsString(), EventName,
                        StringComparison.Ordinal)
                    || !string.Equals(notification["contract"]?.AsString(), _adapter.ToString(),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                instructions.Add(ParseInstruction(notification, transactionHash));
            }
        }
        return instructions;
    }

    private L2PayoutInstruction ParseInstruction(JObject notification, UInt256 transactionHash)
    {
        if (notification["state"] is not JObject state
            || !string.Equals(state["type"]?.AsString(), "Array", StringComparison.Ordinal)
            || state["value"] is not JArray values
            || values.Count != 12)
        {
            throw new InvalidDataException(
                $"{EventName} in {transactionHash} has a non-canonical state payload");
        }

        var sequence = ParseStackUInt64(values[0], "PayoutEnqueued.sequence");
        var messageHash = ParseStackUInt256(values[1], "PayoutEnqueued.messageHash");
        var externalChainId = ParseStackUInt32(values[2], "PayoutEnqueued.externalChainId");
        var neoChainId = ParseStackUInt32(values[3], "PayoutEnqueued.neoChainId");
        var nonce = ParseStackUInt64(values[4], "PayoutEnqueued.nonce");
        var foreignAsset = ParseStackExternalAssetId(
            values[5], "PayoutEnqueued.foreignAsset");
        var neoAsset = ParseStackUInt160(values[6], "PayoutEnqueued.neoAsset");
        var recipient = ParseStackUInt160(values[7], "PayoutEnqueued.recipient");
        var amount = ParseStackInteger(values[8], "PayoutEnqueued.amount");
        var deadline = ParseStackUInt64(values[9], "PayoutEnqueued.deadline");
        var sourceTransaction = ParseStackUInt256(values[10], "PayoutEnqueued.sourceTransaction");
        var messageBytes = ParseStackBytes(values[11], "PayoutEnqueued.messageBytes");
        var instruction = L2PayoutInstruction.Decode(
            sequence, _adapter, neoAsset, messageHash, messageBytes, _neoChainId);

        if (instruction.Message.ExternalChainId != externalChainId
            || instruction.Message.NeoChainId != neoChainId
            || instruction.Message.Nonce != nonce
            || instruction.ForeignAsset != foreignAsset
            || instruction.Message.Recipient != recipient
            || instruction.Amount != amount
            || instruction.Message.DeadlineUnixSeconds != deadline
            || instruction.Message.SourceTxRef != sourceTransaction)
        {
            throw new InvalidDataException(
                $"{EventName} in {transactionHash} fields do not match canonical message bytes");
        }
        return instruction;
    }

    private ScannerCursor ReadCursor()
    {
        var bytes = _store.Get(_cursorKey);
        if (bytes is null) return new ScannerCursor(_startHeight, null);
        if (bytes.Length != CursorLength || bytes[0] != EncodingVersion)
            throw new InvalidDataException("Payout scan cursor has an unsupported encoding.");
        var nextHeight = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(1, sizeof(uint)));
        if (nextHeight < _startHeight)
            throw new InvalidDataException("Payout scan cursor precedes configured start height.");
        return new ScannerCursor(
            nextHeight, new UInt256(bytes.AsSpan(1 + sizeof(uint), UInt256.Length)));
    }

    private void WriteCursor(ScannerCursor cursor)
    {
        if (cursor.LastHash is null)
            throw new InvalidOperationException("Committed payout cursor requires a block hash.");
        Span<byte> bytes = stackalloc byte[CursorLength];
        bytes[0] = EncodingVersion;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.Slice(1, sizeof(uint)), cursor.NextHeight);
        cursor.LastHash.GetSpan().CopyTo(bytes.Slice(1 + sizeof(uint), UInt256.Length));
        _store.Put(_cursorKey, bytes);
    }

    private static byte[] BuildCursorKey(UInt160 adapter, uint neoChainId)
    {
        var key = new byte[CursorNamespace.Length + UInt160.Length + sizeof(uint)];
        CursorNamespace.CopyTo(key, 0);
        adapter.GetSpan().CopyTo(key.AsSpan(CursorNamespace.Length, UInt160.Length));
        BinaryPrimitives.WriteUInt32LittleEndian(
            key.AsSpan(CursorNamespace.Length + UInt160.Length, sizeof(uint)), neoChainId);
        return key;
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
            throw new InvalidDataException($"{field} returned an invalid UInt256", exception);
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

    private static UInt160 ParseStackUInt160(JToken? token, string field)
    {
        var bytes = ParseStackBytes(token, field);
        if (bytes.Length != UInt160.Length)
            throw new InvalidDataException($"{field} is not 20 bytes");
        return new UInt160(bytes);
    }

    private static ExternalAssetId ParseStackExternalAssetId(JToken? token, string field)
    {
        var bytes = ParseStackBytes(token, field);
        try
        {
            return new ExternalAssetId(bytes);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidDataException(
                $"{field} is not a canonical foreign asset identifier", exception);
        }
    }

    private static UInt256 ParseStackUInt256(JToken? token, string field)
    {
        var bytes = ParseStackBytes(token, field);
        if (bytes.Length != UInt256.Length)
            throw new InvalidDataException($"{field} is not 32 bytes");
        return new UInt256(bytes);
    }

    private static byte[] ParseStackBytes(JToken? token, string field)
    {
        if (token is not JObject item
            || item["value"] is not JString value
            || item["type"]?.AsString() is not ("ByteString" or "Buffer"))
            throw new InvalidDataException($"{field} is not a canonical byte string");
        try
        {
            return Convert.FromBase64String(value.AsString());
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"{field} is not canonical base64", exception);
        }
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
