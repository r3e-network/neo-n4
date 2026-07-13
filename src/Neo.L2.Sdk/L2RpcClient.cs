using System;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Neo.Json;
using Neo.L2;
using Neo.L2.Batch;

namespace Neo.L2.Sdk;

/// <summary>
/// App-developer-facing typed client for an L2 node's RPC endpoint. Wraps the 10
/// RPC methods listed in <c>doc.md §14.1</c> + implemented by
/// <c>Neo.Plugins.L2Rpc.L2RpcMethods</c>. Each method returns a strongly-typed
/// record (no <see cref="JArray"/> / <see cref="JObject"/> in the public API).
/// </summary>
/// <remarks>
/// <para>
/// Construction is cheap; thread-safe for concurrent calls (each call gets its own
/// monotonic JSON-RPC <c>id</c>). Disposes the internal <see cref="HttpClient"/>
/// when constructed without a caller-supplied one.
/// </para>
/// <para>
/// The <see cref="ChainId"/> the client carries is checked against every RPC response
/// that includes a <c>chainId</c> field; a mismatch surfaces as
/// <see cref="L2RpcMismatchedChainIdException"/> rather than a silently-wrong response.
/// </para>
/// </remarks>
public sealed class L2RpcClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly bool _ownsHttp;
    private long _nextId;

    /// <summary>The L2 chain id this client is bound to. Server responses must match.</summary>
    public uint ChainId { get; }

    /// <summary>The endpoint URL this client targets.</summary>
    public Uri Endpoint => _endpoint;

    /// <summary>Construct against an HTTP/HTTPS endpoint URL + the L2 chain id this node serves.</summary>
    /// <param name="endpoint">RPC endpoint URL (e.g. <c>http://node.example:30332</c>).</param>
    /// <param name="chainId">Expected chain id the endpoint serves; cross-checked on every response with a <c>chainId</c> field.</param>
    /// <param name="httpClient">
    /// Optional caller-owned <see cref="HttpClient"/>. When supplied, the SDK does not dispose it on
    /// <see cref="Dispose"/> — caller manages the lifetime. Leave <c>null</c> for the default behavior.
    /// </param>
    public L2RpcClient(string endpoint, uint chainId, HttpClient? httpClient = null)
        : this(ParseEndpoint(endpoint), chainId, httpClient)
    { }

    /// <summary>Construct with a parsed <see cref="Uri"/> endpoint.</summary>
    public L2RpcClient(Uri endpoint, uint chainId, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ChainIdValidator.ValidateL2(chainId, nameof(chainId));
        if (!endpoint.IsAbsoluteUri)
            throw new ArgumentException($"endpoint must be an absolute URI; got '{endpoint}'", nameof(endpoint));
        if (endpoint.Scheme is not ("http" or "https"))
            throw new ArgumentException($"endpoint scheme '{endpoint.Scheme}' must be http or https", nameof(endpoint));
        _endpoint = endpoint;
        ChainId = chainId;
        if (httpClient is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
            _ownsHttp = false;
        }
    }

    private static Uri ParseEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrEmpty(endpoint);
        return new Uri(endpoint);
    }

    /// <summary>
    /// <c>getl2batch</c> — fetch the full canonical batch commitment for <paramref name="batchNumber"/>.
    /// Returns <c>null</c> when the batch has not been sealed yet.
    /// </summary>
    public async Task<L2BatchView?> GetBatchAsync(ulong batchNumber, CancellationToken ct = default)
    {
        var p = new JArray { ChainId, ULongWire(batchNumber) };
        var result = await CallAsync("getl2batch", p, ct).ConfigureAwait(false);
        if (result is null) return null;
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getl2batch", "expected object response");
        AssertResponseChainId(obj, "getl2batch");
        var batch = ParseBatch(obj, "getl2batch");
        if (batch.BatchNumber != batchNumber)
            throw new L2RpcProtocolException(
                "getl2batch",
                $"response batchNumber {batch.BatchNumber} does not match request {batchNumber}");
        return batch;
    }

    /// <summary>
    /// <c>getl2batchstatus</c> — pending / finalized / challenged / etc.
    /// </summary>
    public async Task<BatchStatusResponse> GetBatchStatusAsync(ulong batchNumber, CancellationToken ct = default)
    {
        var p = new JArray { ChainId, ULongWire(batchNumber) };
        var result = await CallAsync("getl2batchstatus", p, ct).ConfigureAwait(false);
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getl2batchstatus", "expected object response");
        AssertResponseChainId(obj, "getl2batchstatus");
        var statusByte = ReadByteField(obj, "status", "getl2batchstatus");
        if (statusByte > (byte)BatchStatus.Reverted)
            throw new L2RpcProtocolException("getl2batchstatus", $"unknown batch status {statusByte}");
        var response = new BatchStatusResponse(
            ChainId: ReadUInt32Field(obj, "chainId", "getl2batchstatus"),
            BatchNumber: ReadUInt64Field(obj, "batchNumber", "getl2batchstatus"),
            Status: (BatchStatus)statusByte,
            StatusName: obj["statusName"]?.AsString() ?? ((BatchStatus)statusByte).ToString());
        if (response.BatchNumber != batchNumber)
            throw new L2RpcProtocolException(
                "getl2batchstatus",
                $"response batchNumber {response.BatchNumber} does not match request {batchNumber}");
        return response;
    }

    /// <summary>
    /// <c>getl2stateroot</c> — latest sealed state root. See
    /// <see cref="GetStateRootAtAsync(ulong, CancellationToken)"/> for a pinned-batch variant.
    /// </summary>
    public async Task<UInt256> GetLatestStateRootAsync(CancellationToken ct = default)
    {
        var p = new JArray { ChainId };
        var result = await CallAsync("getl2stateroot", p, ct).ConfigureAwait(false)
            ?? throw new L2RpcProtocolException("getl2stateroot", "null result");
        return ParseUInt256("getl2stateroot", result);
    }

    /// <summary>
    /// <c>getl2stateroot &lt;batch&gt;</c> — historical state root pinned to a specific batch number.
    /// </summary>
    public async Task<UInt256> GetStateRootAtAsync(ulong batchNumber, CancellationToken ct = default)
    {
        var p = new JArray { ChainId, ULongWire(batchNumber) };
        var result = await CallAsync("getl2stateroot", p, ct).ConfigureAwait(false)
            ?? throw new L2RpcProtocolException("getl2stateroot", "null result");
        return ParseUInt256("getl2stateroot", result);
    }

    /// <summary>
    /// <c>getl2withdrawalproof</c> — canonical Merkle proof bytes for a withdrawal leaf;
    /// <c>null</c> if the leaf is unknown to this node. Pass the raw bytes to
    /// <c>NeoHub.SharedBridge.FinalizeWithdrawalWithProof</c> on L1.
    /// </summary>
    public async Task<byte[]?> GetWithdrawalProofAsync(UInt256 leaf, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(leaf);
        var p = new JArray { ChainId, leaf.ToString() };
        var result = await CallAsync("getl2withdrawalproof", p, ct).ConfigureAwait(false);
        return ParseOptionalHex("getl2withdrawalproof", result);
    }

    /// <summary>
    /// <c>getl2messageproof</c> — Merkle proof bytes for a cross-chain message; <c>null</c> if unknown.
    /// </summary>
    public async Task<byte[]?> GetMessageProofAsync(UInt256 messageHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messageHash);
        var p = new JArray { ChainId, messageHash.ToString() };
        var result = await CallAsync("getl2messageproof", p, ct).ConfigureAwait(false);
        return ParseOptionalHex("getl2messageproof", result);
    }

    /// <summary>
    /// <c>getl1depositstatus</c> — has an L1 deposit (sourceChain, nonce) been consumed on this L2?
    /// Returns <c>null</c> when the deposit isn't tracked at all.
    /// </summary>
    public async Task<DepositStatusResponse?> GetDepositStatusAsync(uint sourceChainId, ulong nonce, CancellationToken ct = default)
    {
        var p = new JArray { sourceChainId, ULongWire(nonce) };
        var result = await CallAsync("getl1depositstatus", p, ct).ConfigureAwait(false);
        if (result is null) return null;
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getl1depositstatus", "expected object response");
        var serverSourceChainId = ReadUInt32Field(obj, "sourceChainId", "getl1depositstatus");
        // Cross-check the requested source-chain matches what came back — a misbehaving
        // server returning another L1's deposit would otherwise sail through and the caller
        // would consume the wrong consumed/included status.
        if (serverSourceChainId != sourceChainId)
            throw new L2RpcMismatchedChainIdException("getl1depositstatus", sourceChainId, serverSourceChainId);
        ulong? includedInBatch = obj["includedInBatch"] is null
            ? null
            : ReadUInt64Field(obj, "includedInBatch", "getl1depositstatus");
        var response = new DepositStatusResponse(
            SourceChainId: serverSourceChainId,
            Nonce: ReadUInt64Field(obj, "nonce", "getl1depositstatus"),
            ConsumedOnL2: ((JBoolean)obj["consumedOnL2"]!).AsBoolean(),
            IncludedInBatch: includedInBatch);
        if (response.Nonce != nonce)
            throw new L2RpcProtocolException(
                "getl1depositstatus",
                $"response nonce {response.Nonce} does not match request {nonce}");
        return response;
    }

    /// <summary>
    /// <c>getcanonicalasset</c> — given an L2-side asset hash, return the canonical L1 asset; <c>null</c> if not bridged.
    /// </summary>
    public async Task<UInt160?> GetCanonicalAssetAsync(UInt160 l2Asset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(l2Asset);
        var p = new JArray { l2Asset.ToString() };
        var result = await CallAsync("getcanonicalasset", p, ct).ConfigureAwait(false);
        if (result is null) return null;
        return UInt160.Parse(result.AsString());
    }

    /// <summary>
    /// <c>getbridgedasset</c> — given an L1 asset hash, return its L2-side bridged hash; <c>null</c> if not bridged.
    /// </summary>
    public async Task<UInt160?> GetBridgedAssetAsync(UInt160 l1Asset, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(l1Asset);
        var p = new JArray { l1Asset.ToString() };
        var result = await CallAsync("getbridgedasset", p, ct).ConfigureAwait(false);
        if (result is null) return null;
        return UInt160.Parse(result.AsString());
    }

    /// <summary>
    /// <c>getsecuritylevel</c> — single-dimension §16.2 chain-type label.
    /// Use <see cref="GetSecurityLabelAsync"/> for the full 5-dimension label.
    /// </summary>
    public async Task<SecurityLevelResponse> GetSecurityLevelAsync(CancellationToken ct = default)
    {
        var p = new JArray { ChainId };
        var result = await CallAsync("getsecuritylevel", p, ct).ConfigureAwait(false);
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getsecuritylevel", "expected object response");
        AssertResponseChainId(obj, "getsecuritylevel");
        var lvlByte = ReadByteField(obj, "level", "getsecuritylevel");
        if (lvlByte > (byte)SecurityLevel.Validium)
            throw new L2RpcProtocolException("getsecuritylevel", $"unknown security level {lvlByte}");
        return new SecurityLevelResponse(
            ChainId: ReadUInt32Field(obj, "chainId", "getsecuritylevel"),
            Level: (SecurityLevel)lvlByte);
    }

    /// <summary>
    /// <c>getsecuritylabel</c> — full §16.2 5-dimension label
    /// (securityLevel / daMode / gatewayEnabled / sequencer / exit).
    /// </summary>
    public async Task<SecurityLabelResponse> GetSecurityLabelAsync(CancellationToken ct = default)
    {
        var p = new JArray { ChainId };
        var result = await CallAsync("getsecuritylabel", p, ct).ConfigureAwait(false);
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getsecuritylabel", "expected object response");
        AssertResponseChainId(obj, "getsecuritylabel");
        var securityLevel = ReadByteField(obj, "securityLevel", "getsecuritylabel");
        var daMode = ReadByteField(obj, "daMode", "getsecuritylabel");
        var sequencer = ReadByteField(obj, "sequencer", "getsecuritylabel");
        var exit = ReadByteField(obj, "exit", "getsecuritylabel");
        if (securityLevel > (byte)SecurityLevel.Validium)
            throw new L2RpcProtocolException("getsecuritylabel", $"unknown security level {securityLevel}");
        if (daMode > (byte)DAMode.DAC)
            throw new L2RpcProtocolException("getsecuritylabel", $"unknown public DA mode {daMode}");
        if (sequencer > (byte)SequencerModel.Decentralized)
            throw new L2RpcProtocolException("getsecuritylabel", $"unknown sequencer model {sequencer}");
        if (exit > (byte)ExitModel.OperatorAssisted)
            throw new L2RpcProtocolException("getsecuritylabel", $"unknown exit model {exit}");
        return new SecurityLabelResponse(
            ChainId: ReadUInt32Field(obj, "chainId", "getsecuritylabel"),
            SecurityLevel: (SecurityLevel)securityLevel,
            DAMode: (DAMode)daMode,
            GatewayEnabled: ((JBoolean)obj["gatewayEnabled"]!).AsBoolean(),
            Sequencer: (SequencerModel)sequencer,
            Exit: (ExitModel)exit);
    }

    private static L2BatchView ParseBatch(JObject obj, string method)
    {
        // The full encoded batch bytes round-trip via BatchSerializer if a caller wants
        // the canonical wire format. Decoding the structured fields here gives the SDK's
        // typed surface; the encoded bytes blob stays exposed for re-publish flows.
        var encoded = ParseOptionalHex(method, obj["encoded"])
            ?? throw new L2RpcProtocolException(method, "encoded batch is null");
        var proof = ParseOptionalHex(method, obj["proof"])
            ?? throw new L2RpcProtocolException(method, "proof is null");
        var view = new L2BatchView(
            ChainId: ReadUInt32Field(obj, "chainId", method),
            BatchNumber: ReadUInt64Field(obj, "batchNumber", method),
            FirstBlock: ReadUInt64Field(obj, "firstBlock", method),
            LastBlock: ReadUInt64Field(obj, "lastBlock", method),
            PreStateRoot: UInt256.Parse(obj["preStateRoot"]!.AsString()),
            PostStateRoot: UInt256.Parse(obj["postStateRoot"]!.AsString()),
            TxRoot: UInt256.Parse(obj["txRoot"]!.AsString()),
            ReceiptRoot: UInt256.Parse(obj["receiptRoot"]!.AsString()),
            WithdrawalRoot: UInt256.Parse(obj["withdrawalRoot"]!.AsString()),
            L2ToL1MessageRoot: UInt256.Parse(obj["l2ToL1MessageRoot"]!.AsString()),
            L2ToL2MessageRoot: UInt256.Parse(obj["l2ToL2MessageRoot"]!.AsString()),
            DACommitment: UInt256.Parse(obj["daCommitment"]!.AsString()),
            PublicInputHash: UInt256.Parse(obj["publicInputHash"]!.AsString()),
            ProofType: (ProofType)ReadProofType(obj, method),
            Proof: proof,
            EncodedWireFormat: encoded);
        byte[] canonical;
        try
        {
            canonical = BatchSerializer.Encode(new L2BatchCommitment
            {
                ChainId = view.ChainId,
                BatchNumber = view.BatchNumber,
                FirstBlock = view.FirstBlock,
                LastBlock = view.LastBlock,
                PreStateRoot = view.PreStateRoot,
                PostStateRoot = view.PostStateRoot,
                TxRoot = view.TxRoot,
                ReceiptRoot = view.ReceiptRoot,
                WithdrawalRoot = view.WithdrawalRoot,
                L2ToL1MessageRoot = view.L2ToL1MessageRoot,
                L2ToL2MessageRoot = view.L2ToL2MessageRoot,
                DACommitment = view.DACommitment,
                PublicInputHash = view.PublicInputHash,
                ProofType = view.ProofType,
                Proof = view.Proof,
            });
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new L2RpcProtocolException(method, $"invalid canonical batch: {ex.Message}");
        }
        if (!canonical.AsSpan().SequenceEqual(encoded))
            throw new L2RpcProtocolException(method, "encoded batch does not match structured response fields");
        return view;
    }

    private void AssertResponseChainId(JObject obj, string method)
    {
        if (obj["chainId"] is not JNumber)
            throw new L2RpcProtocolException(method, "response chainId is missing or not numeric");
        var serverChainId = ReadUInt32Field(obj, "chainId", method);
        if (serverChainId != ChainId)
            throw new L2RpcMismatchedChainIdException(method, expected: ChainId, got: serverChainId);
    }

    private async Task<JToken?> CallAsync(string method, JArray @params, CancellationToken ct)
    {
        var id = Interlocked.Increment(ref _nextId);
        var envelope = new JObject();
        envelope["jsonrpc"] = "2.0";
        envelope["method"] = method;
        envelope["params"] = @params;
        envelope["id"] = id;

        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(envelope.ToString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        HttpResponseMessage response;
        try { response = await _http.SendAsync(request, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (OperationCanceledException ex) { throw new L2RpcTransportException(method, $"timeout: {ex.Message}"); }
        catch (HttpRequestException ex) { throw new L2RpcTransportException(method, $"http send failed: {ex.Message}"); }

        using var _scoped = response;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var snippet = body.Length <= 200 ? body : body[..200];
            throw new L2RpcTransportException(method, $"http {(int)response.StatusCode} {response.ReasonPhrase}: {snippet}");
        }

        JToken? parsed;
        try { parsed = JToken.Parse(body); }
        catch (Exception ex) { throw new L2RpcProtocolException(method, $"parse error: {ex.Message}"); }
        if (parsed is not JObject responseObj)
            throw new L2RpcProtocolException(method, "non-object response");

        if (responseObj["jsonrpc"] is not JString version || version.AsString() != "2.0")
            throw new L2RpcProtocolException(method, "response jsonrpc must be '2.0'");

        var responseIdToken = responseObj["id"];
        var responseId = responseIdToken is JNumber rn ? (long)rn.AsNumber()
            : responseIdToken is JString rs && long.TryParse(rs.AsString(), out var rsi) ? rsi
            : -1L;
        if (responseId != id)
            throw new L2RpcProtocolException(method, $"response id {responseId} does not match request id {id}");

        if (responseObj["error"] is JObject err)
        {
            var code = -32603;
            if (err["code"] is JNumber cn)
            {
                var d = cn.AsNumber();
                if (d >= int.MinValue && d <= int.MaxValue) code = (int)d;
            }
            var message = err["message"]?.AsString() ?? "rpc error";
            throw new L2RpcServerException(method, code, message);
        }

        if (!responseObj.ContainsProperty("result"))
            throw new L2RpcProtocolException(method, "response is missing result");

        return responseObj["result"];
    }

    private static JString ULongWire(ulong value)
        => new(value.ToString(CultureInfo.InvariantCulture));

    private static byte[]? ParseOptionalHex(string method, JToken? result)
    {
        if (result is null)
            return null;
        if (result is not JString textToken)
            throw new L2RpcProtocolException(method, "expected hex string");
        var text = textToken.AsString();
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            text = text[2..];
        try
        {
            return Convert.FromHexString(text);
        }
        catch (FormatException ex)
        {
            throw new L2RpcProtocolException(method, $"invalid hex: {ex.Message}");
        }
    }

    private static UInt256 ParseUInt256(string method, JToken result)
    {
        if (result is not JString textToken)
            throw new L2RpcProtocolException(method, "expected UInt256 string");
        try
        {
            return UInt256.Parse(textToken.AsString());
        }
        catch (FormatException ex)
        {
            throw new L2RpcProtocolException(method, $"invalid UInt256: {ex.Message}");
        }
    }

    private static byte ReadByteField(JObject obj, string field, string method)
    {
        var value = ReadUInt64Field(obj, field, method);
        if (value > byte.MaxValue)
            throw new L2RpcProtocolException(method, $"field {field} exceeds byte range");
        return (byte)value;
    }

    private static byte ReadProofType(JObject obj, string method)
    {
        var value = ReadByteField(obj, "proofType", method);
        if (value > (byte)ProofType.Zk)
            throw new L2RpcProtocolException(method, $"unknown proof type {value}");
        return value;
    }

    private static uint ReadUInt32Field(JObject obj, string field, string method)
    {
        var value = ReadUInt64Field(obj, field, method);
        if (value > uint.MaxValue)
            throw new L2RpcProtocolException(method, $"field {field} exceeds u32 range");
        return (uint)value;
    }

    private static ulong ReadUInt64Field(JObject obj, string field, string method)
    {
        var token = obj[field];
        if (token is JString text && ulong.TryParse(
                text.AsString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsed))
            return parsed;
        if (token is JNumber number)
        {
            var value = number.AsNumber();
            const double MaximumSafeInteger = 9_007_199_254_740_991d;
            if (double.IsFinite(value) && value >= 0 && value <= MaximumSafeInteger && Math.Truncate(value) == value)
                return (ulong)value;
        }
        throw new L2RpcProtocolException(method, $"field {field} must be a lossless u64");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
