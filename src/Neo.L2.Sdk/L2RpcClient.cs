using System;
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
        var p = new JArray { ChainId, batchNumber };
        var result = await CallAsync("getl2batch", p, ct).ConfigureAwait(false);
        if (result is null) return null;
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getl2batch", "expected object response");
        AssertResponseChainId(obj, "getl2batch");
        return ParseBatch(obj);
    }

    /// <summary>
    /// <c>getl2batchstatus</c> — pending / finalized / challenged / etc.
    /// </summary>
    public async Task<BatchStatusResponse> GetBatchStatusAsync(ulong batchNumber, CancellationToken ct = default)
    {
        var p = new JArray { ChainId, batchNumber };
        var result = await CallAsync("getl2batchstatus", p, ct).ConfigureAwait(false);
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getl2batchstatus", "expected object response");
        AssertResponseChainId(obj, "getl2batchstatus");
        var statusByte = (byte)(double)((JNumber)obj["status"]!).AsNumber();
        return new BatchStatusResponse(
            ChainId: (uint)(double)((JNumber)obj["chainId"]!).AsNumber(),
            BatchNumber: (ulong)(double)((JNumber)obj["batchNumber"]!).AsNumber(),
            Status: (BatchStatus)statusByte,
            StatusName: obj["statusName"]?.AsString() ?? ((BatchStatus)statusByte).ToString());
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
        return UInt256.Parse(result.AsString());
    }

    /// <summary>
    /// <c>getl2stateroot &lt;batch&gt;</c> — historical state root pinned to a specific batch number.
    /// </summary>
    public async Task<UInt256> GetStateRootAtAsync(ulong batchNumber, CancellationToken ct = default)
    {
        var p = new JArray { ChainId, batchNumber };
        var result = await CallAsync("getl2stateroot", p, ct).ConfigureAwait(false)
            ?? throw new L2RpcProtocolException("getl2stateroot", "null result");
        return UInt256.Parse(result.AsString());
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
        if (result is null) return null;
        return Convert.FromHexString(result.AsString());
    }

    /// <summary>
    /// <c>getl2messageproof</c> — Merkle proof bytes for a cross-chain message; <c>null</c> if unknown.
    /// </summary>
    public async Task<byte[]?> GetMessageProofAsync(UInt256 messageHash, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(messageHash);
        var p = new JArray { ChainId, messageHash.ToString() };
        var result = await CallAsync("getl2messageproof", p, ct).ConfigureAwait(false);
        if (result is null) return null;
        return Convert.FromHexString(result.AsString());
    }

    /// <summary>
    /// <c>getl1depositstatus</c> — has an L1 deposit (sourceChain, nonce) been consumed on this L2?
    /// Returns <c>null</c> when the deposit isn't tracked at all.
    /// </summary>
    public async Task<DepositStatusResponse?> GetDepositStatusAsync(uint sourceChainId, ulong nonce, CancellationToken ct = default)
    {
        var p = new JArray { sourceChainId, nonce };
        var result = await CallAsync("getl1depositstatus", p, ct).ConfigureAwait(false);
        if (result is null) return null;
        if (result is not JObject obj)
            throw new L2RpcProtocolException("getl1depositstatus", "expected object response");
        ulong? includedInBatch = obj["includedInBatch"] is JNumber inb ? (ulong)(double)inb.AsNumber() : null;
        return new DepositStatusResponse(
            SourceChainId: (uint)(double)((JNumber)obj["sourceChainId"]!).AsNumber(),
            Nonce: (ulong)(double)((JNumber)obj["nonce"]!).AsNumber(),
            ConsumedOnL2: ((JBoolean)obj["consumedOnL2"]!).AsBoolean(),
            IncludedInBatch: includedInBatch);
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
        var lvlByte = (byte)(double)((JNumber)obj["level"]!).AsNumber();
        return new SecurityLevelResponse(
            ChainId: (uint)(double)((JNumber)obj["chainId"]!).AsNumber(),
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
        return new SecurityLabelResponse(
            ChainId: (uint)(double)((JNumber)obj["chainId"]!).AsNumber(),
            SecurityLevel: (SecurityLevel)(byte)(double)((JNumber)obj["securityLevel"]!).AsNumber(),
            DAMode: (DAMode)(byte)(double)((JNumber)obj["daMode"]!).AsNumber(),
            GatewayEnabled: ((JBoolean)obj["gatewayEnabled"]!).AsBoolean(),
            Sequencer: (SequencerModel)(byte)(double)((JNumber)obj["sequencer"]!).AsNumber(),
            Exit: (ExitModel)(byte)(double)((JNumber)obj["exit"]!).AsNumber());
    }

    private static L2BatchView ParseBatch(JObject obj)
    {
        // The full encoded batch bytes round-trip via BatchSerializer if a caller wants
        // the canonical wire format. Decoding the structured fields here gives the SDK's
        // typed surface; the encoded bytes blob stays exposed for re-publish flows.
        var encoded = Convert.FromHexString(obj["encoded"]!.AsString());
        var proof = Convert.FromHexString(obj["proof"]!.AsString());
        return new L2BatchView(
            ChainId: (uint)(double)((JNumber)obj["chainId"]!).AsNumber(),
            BatchNumber: (ulong)(double)((JNumber)obj["batchNumber"]!).AsNumber(),
            FirstBlock: (ulong)(double)((JNumber)obj["firstBlock"]!).AsNumber(),
            LastBlock: (ulong)(double)((JNumber)obj["lastBlock"]!).AsNumber(),
            PreStateRoot: UInt256.Parse(obj["preStateRoot"]!.AsString()),
            PostStateRoot: UInt256.Parse(obj["postStateRoot"]!.AsString()),
            TxRoot: UInt256.Parse(obj["txRoot"]!.AsString()),
            ReceiptRoot: UInt256.Parse(obj["receiptRoot"]!.AsString()),
            WithdrawalRoot: UInt256.Parse(obj["withdrawalRoot"]!.AsString()),
            L2ToL1MessageRoot: UInt256.Parse(obj["l2ToL1MessageRoot"]!.AsString()),
            L2ToL2MessageRoot: UInt256.Parse(obj["l2ToL2MessageRoot"]!.AsString()),
            DACommitment: UInt256.Parse(obj["daCommitment"]!.AsString()),
            PublicInputHash: UInt256.Parse(obj["publicInputHash"]!.AsString()),
            ProofType: (ProofType)(byte)(double)((JNumber)obj["proofType"]!).AsNumber(),
            Proof: proof,
            EncodedWireFormat: encoded);
    }

    private void AssertResponseChainId(JObject obj, string method)
    {
        if (obj["chainId"] is not JNumber n)
            return; // method's response shape doesn't include chainId — fine.
        var serverChainId = (uint)(double)n.AsNumber();
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

        return responseObj["result"];
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsHttp)
            _http.Dispose();
    }
}
