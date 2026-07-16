using System.Globalization;
using Neo.Json;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// Production source for the L1 finalized height used by batch sealing and L1 inbox
/// wiring (<c>WireProduction</c> / <c>WireL1MessageInbox</c>).
/// </summary>
/// <remarks>
/// See doc.md §7.2 / §15. Mirrors scanner finality math: <c>safeHeight = getblockcount - 1 - finalityDepth</c>
/// (clamped at 0 when the chain is shallower than the depth).
/// </remarks>
public sealed class RpcL1FinalizedHeightSource
{
    private readonly JsonRpcClient _rpc;
    private readonly uint _finalityDepth;

    /// <summary>L1 blocks that must confirm after tip before a height is treated as finalized.</summary>
    public uint FinalityDepth => _finalityDepth;

    /// <summary>
    /// Construct against an L1 JSON-RPC client.
    /// </summary>
    /// <param name="rpc">Caller-owned L1 client (not disposed by this type).</param>
    /// <param name="finalityDepth">
    /// Confirmation lag. Must match (or exceed) scanner finality depths used for deposits /
    /// forced-inclusion / MessageRouter so seal-time inbox height never races scanners.
    /// Default 1 matches the production scanner default.
    /// </param>
    public RpcL1FinalizedHeightSource(JsonRpcClient rpc, uint finalityDepth = 1)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        _rpc = rpc;
        _finalityDepth = finalityDepth;
    }

    /// <summary>
    /// Read <c>getblockcount</c> and return the highest L1 height considered finalized.
    /// </summary>
    public async ValueTask<uint> GetFinalizedHeightAsync(CancellationToken cancellationToken = default)
    {
        var result = await _rpc.CallAsync("getblockcount", new JArray(), cancellationToken)
            .ConfigureAwait(false);
        var blockCount = ParseUInt32(result, "getblockcount");
        if (blockCount <= _finalityDepth)
            return 0;
        return checked(blockCount - 1 - _finalityDepth);
    }

    /// <summary>
    /// Synchronous provider for seal / WireProduction APIs that take <c>Func&lt;uint&gt;</c>.
    /// Blocks on the underlying RPC; prefer calling from the operator seal path, not hot loops.
    /// </summary>
    public Func<uint> CreateSyncProvider()
    {
        return () => GetFinalizedHeightAsync(CancellationToken.None)
            .AsTask()
            .GetAwaiter()
            .GetResult();
    }

    private static uint ParseUInt32(JToken? token, string name)
    {
        if (token is JNumber number)
        {
            var raw = number.AsNumber();
            if (!double.IsFinite(raw) || raw < uint.MinValue || raw > uint.MaxValue || raw != Math.Truncate(raw))
                throw new InvalidOperationException($"{name} returned a non-uint value");
            return (uint)raw;
        }
        if (token is JString text
            && uint.TryParse(text.AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new InvalidOperationException($"{name} returned a non-uint value");
    }
}
