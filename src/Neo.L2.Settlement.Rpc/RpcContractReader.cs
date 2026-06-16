using System;
using System.Threading;
using System.Threading.Tasks;
using Neo.Json;

namespace Neo.L2.Settlement.Rpc;

/// <summary>
/// Shared helpers for reading Neo N3 contract state via <c>invokefunction</c> RPC calls.
/// Consolidates parameter-encoding, invoke-read, and stack-item-parsing logic that was
/// previously duplicated across <c>RpcForcedInclusionSource</c>, <c>RpcMessageRouter</c>,
/// <c>RpcSequencerCommitteeProvider</c>, and <c>RpcSettlementClient</c>.
/// </summary>
public static class RpcContractReader
{
    /// <summary>
    /// Build a Neo N3 <c>invokefunction</c> parameter array from typed arguments.
    /// </summary>
    /// <remarks>
    /// Supported types: <see cref="uint"/>, <see cref="ulong"/>, <see cref="int"/>,
    /// <see cref="string"/>, <see cref="UInt160"/>, <see cref="UInt256"/>, <see cref="byte"/>[].
    /// </remarks>
    public static JArray BuildParamsArray(object[] args)
    {
        var arr = new JArray();
        foreach (var a in args)
        {
            var entry = new JObject();
            switch (a)
            {
                case uint u: entry["type"] = "Integer"; entry["value"] = u.ToString(); break;
                case ulong ul: entry["type"] = "Integer"; entry["value"] = ul.ToString(); break;
                case int i: entry["type"] = "Integer"; entry["value"] = i.ToString(); break;
                case string s: entry["type"] = "String"; entry["value"] = s; break;
                case UInt160 h160: entry["type"] = "Hash160"; entry["value"] = h160.ToString(); break;
                case UInt256 h256: entry["type"] = "Hash256"; entry["value"] = h256.ToString(); break;
                case byte[] b: entry["type"] = "ByteArray"; entry["value"] = Convert.ToBase64String(b); break;
                default: throw new ArgumentException($"Unsupported RPC param type {a?.GetType()}");
            }
            arr.Add(entry);
        }
        return arr;
    }

    /// <summary>
    /// Call a Neo N3 contract read method via <c>invokefunction</c> and return the top
    /// stack item. Throws <see cref="InvalidOperationException"/> if the VM faults or
    /// the stack is empty.
    /// </summary>
    /// <param name="rpc">L1 JSON-RPC client.</param>
    /// <param name="contractHash">Deployed contract script hash.</param>
    /// <param name="method">Contract method name to invoke.</param>
    /// <param name="args">Typed arguments for the contract method.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The top stack item, or <see langword="null"/>.</returns>
    public static async ValueTask<JToken?> InvokeReadAsync(
        JsonRpcClient rpc,
        UInt160 contractHash,
        string method,
        object[] args,
        CancellationToken ct)
    {
        var paramsArray = new JArray
        {
            contractHash.ToString(),
            method,
            BuildParamsArray(args),
        };
        var result = await rpc.CallAsync("invokefunction", paramsArray, ct).ConfigureAwait(false);
        if (result is not JObject obj)
            throw new InvalidOperationException($"invokefunction({method}) returned non-object");
        var state = obj["state"]?.AsString();
        if (state != "HALT")
            throw new InvalidOperationException($"{method} faulted: state={state}");
        if (obj["stack"] is not JArray stack || stack.Count == 0)
            throw new InvalidOperationException($"{method} returned empty stack");
        return stack[0];
    }

    /// <summary>
    /// Parse a boolean from a Neo N3 RPC stack item. Handles <c>type="Boolean"</c>,
    /// Integer 0/1, and ByteString-encoded values.
    /// </summary>
    public static bool ParseBoolean(JToken? token)
    {
        if (token is not JObject obj) throw new InvalidOperationException("expected JObject");
        var typeStr = obj["type"]?.AsString();
        var value = obj["value"]?.AsString();
        // Neo represents booleans either as type="Boolean" with value="true"/"false" or as
        // an Integer/ByteString 0/1 — handle all shapes.
        if (typeStr == "Boolean") return value == "true";
        return ParseInteger(token) != 0;
    }

    /// <summary>
    /// Parse a byte array from a Neo N3 RPC stack item (Base64-encoded value).
    /// Returns an empty array when the value is null or empty.
    /// </summary>
    public static byte[] ParseByteArray(JToken? token)
    {
        if (token is not JObject obj) throw new InvalidOperationException("expected JObject");
        var value = obj["value"]?.AsString();
        if (string.IsNullOrEmpty(value)) return Array.Empty<byte>();
        return Convert.FromBase64String(value);
    }

    /// <summary>
    /// Parse an integer from a Neo N3 RPC stack item. Tries direct decimal parsing first
    /// (Neo's <c>Integer</c> stack-item shape), then decodes a Base64-encoded ByteString as a
    /// little-endian signed integer (Neo's ByteString integer encoding). Returns 0 for empty
    /// or null values. Throws <see cref="OverflowException"/> if the decoded value does not fit
    /// in <see cref="int"/>, rather than silently truncating to the low byte.
    /// </summary>
    public static int ParseInteger(JToken? token)
    {
        if (token is not JObject obj) throw new InvalidOperationException("expected JObject");
        var value = obj["value"]?.AsString() ?? "0";
        if (int.TryParse(value, out var n)) return n;
        // Neo encodes integers in ByteString form as little-endian two's complement; decode the
        // full value via BigInteger and range-check it for int rather than reading only bytes[0]
        // (which would truncate any value >= 256 or any multi-byte count to its low byte).
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length == 0) return 0;
        var big = new System.Numerics.BigInteger(bytes, isUnsigned: false, isBigEndian: false);
        return checked((int)big);
    }

    /// <summary>
    /// Parse a <see cref="UInt160"/> (20-byte address/hash) from a Neo N3 RPC stack item.
    /// </summary>
    public static UInt160 ParseUInt160(JToken? token)
    {
        if (token is not JObject obj) throw new InvalidOperationException("expected JObject");
        var value = obj["value"]?.AsString() ?? throw new InvalidOperationException("missing value");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length != 20)
            throw new InvalidOperationException($"expected 20 bytes for UInt160, got {bytes.Length}");
        return new UInt160(bytes);
    }

    /// <summary>
    /// Parse a <see cref="UInt256"/> (32-byte hash) from a Neo N3 RPC stack item.
    /// </summary>
    public static UInt256 ParseUInt256(JToken? token)
    {
        if (token is not JObject obj) throw new InvalidOperationException("expected JObject for stack item");
        var value = obj["value"]?.AsString() ?? throw new InvalidOperationException("missing 'value'");
        var bytes = Convert.FromBase64String(value);
        if (bytes.Length != 32) throw new InvalidOperationException($"expected 32 bytes, got {bytes.Length}");
        return new UInt256(bytes);
    }
}
