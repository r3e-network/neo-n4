using System.Globalization;
using System.Security.Cryptography;
using System.Diagnostics;
using Neo.Extensions.IO;
using Neo.Json;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;

namespace Neo.L2.Settlement.Rpc;

/// <summary>Configuration for signed Neo invocation transactions.</summary>
/// <remarks>See doc.md §14.2 (production operator transactions).</remarks>
public sealed record RpcTransactionSenderOptions
{
    /// <summary>Required Neo network magic. A mismatched RPC endpoint is rejected.</summary>
    public required uint ExpectedNetwork { get; init; }

    /// <summary>Number of blocks for which the transaction remains valid.</summary>
    public uint ValidUntilBlockDelta { get; init; } = 100;

    /// <summary>Additional system-fee margin in basis points.</summary>
    public uint SystemFeeMarginBasisPoints { get; init; } = 2_000;

    /// <summary>Minimum additional system-fee margin in datoshi.</summary>
    public long MinimumSystemFeeMargin { get; init; } = 100_000;

    /// <summary>Maximum time to wait for an application log.</summary>
    public TimeSpan ConfirmationTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>Polling interval while waiting for an application log.</summary>
    public TimeSpan ConfirmationPollInterval { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Confirmed execution result for a signed Neo transaction.</summary>
/// <remarks>See doc.md §14.2 (operator transaction evidence).</remarks>
public sealed record RpcTransactionReceipt(
    UInt256 TransactionHash,
    string VmState,
    string? Exception,
    long SystemFee,
    long NetworkFee);

/// <summary>
/// Builds, preflights, signs, broadcasts, and confirms Neo invocation transactions over JSON-RPC.
/// </summary>
/// <remarks>
/// See doc.md §14.2. The signer boundary supports local wallets and remote HSM/KMS adapters,
/// while all fee calculation and confirmation behavior remains canonical and shared.
/// </remarks>
public sealed class RpcTransactionSender
{
    private readonly JsonRpcClient _rpc;
    private readonly INeoTransactionSigner _signer;
    private readonly RpcTransactionSenderOptions _options;

    /// <summary>Constructs a transaction sender.</summary>
    public RpcTransactionSender(
        JsonRpcClient rpc,
        INeoTransactionSigner signer,
        RpcTransactionSenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ValidUntilBlockDelta == 0)
            throw new ArgumentOutOfRangeException(nameof(options), "ValidUntilBlockDelta must be positive.");
        if (options.SystemFeeMarginBasisPoints > 100_000)
            throw new ArgumentOutOfRangeException(nameof(options), "SystemFeeMarginBasisPoints must not exceed 100000.");
        if (options.MinimumSystemFeeMargin < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "MinimumSystemFeeMargin must not be negative.");
        if (options.ConfirmationTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "ConfirmationTimeout must be positive.");
        if (options.ConfirmationPollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "ConfirmationPollInterval must be positive.");
        _rpc = rpc;
        _signer = signer;
        _options = options;
    }

    /// <summary>Preflights and signs an invocation without broadcasting it.</summary>
    public async ValueTask<Transaction> BuildSignedInvocationAsync(
        ReadOnlyMemory<byte> script,
        CancellationToken cancellationToken = default)
    {
        if (script.IsEmpty) throw new ArgumentException("Invocation script must not be empty.", nameof(script));

        var network = await GetNetworkAsync(cancellationToken).ConfigureAwait(false);
        var invoke = await InvokeScriptAsync(script, cancellationToken).ConfigureAwait(false);
        var systemFee = AddSystemFeeMargin(NeoGas.ParseRpcValue(GetRequiredString(invoke, "gasconsumed")));
        var blockCount = await GetBlockCountAsync(cancellationToken).ConfigureAwait(false);

        var transaction = new Transaction
        {
            Version = 0,
            Nonce = RandomNonce(),
            SystemFee = systemFee,
            NetworkFee = 0,
            ValidUntilBlock = checked(blockCount + _options.ValidUntilBlockDelta),
            Signers =
            [
                new Signer
                {
                    Account = _signer.Account,
                    Scopes = _signer.Scope,
                },
            ],
            Attributes = [],
            Script = script.ToArray(),
            Witnesses = [_signer.CreatePlaceholderWitness()],
        };

        ValidateWitness(transaction.Witnesses[0]);
        transaction.NetworkFee = await CalculateNetworkFeeAsync(transaction, cancellationToken).ConfigureAwait(false);
        transaction.Witnesses =
        [
            await _signer.SignAsync(transaction, network, cancellationToken).ConfigureAwait(false),
        ];
        ValidateWitness(transaction.Witnesses[0]);
        return transaction;
    }

    /// <summary>Broadcasts a signed transaction and waits for its application log.</summary>
    public async ValueTask<RpcTransactionReceipt> BroadcastAndWaitAsync(
        Transaction transaction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Witnesses is null || transaction.Witnesses.Length == 0)
            throw new ArgumentException("Transaction must contain at least one witness.", nameof(transaction));
        if (transaction.Signers is null
            || transaction.Signers.Length != 1
            || transaction.Signers[0].Account != _signer.Account)
        {
            throw new ArgumentException(
                "Transaction must contain exactly the configured account signer.",
                nameof(transaction));
        }
        ValidateWitness(transaction.Witnesses[0]);
        await GetNetworkAsync(cancellationToken).ConfigureAwait(false);

        var result = await _rpc.CallAsync(
            "sendrawtransaction",
            new JArray { Convert.ToBase64String(transaction.ToArray()) },
            cancellationToken).ConfigureAwait(false);
        ValidateBroadcastResult(result, transaction.Hash);

        var execution = await WaitForApplicationLogAsync(transaction.Hash, cancellationToken).ConfigureAwait(false);
        var receipt = new RpcTransactionReceipt(
            transaction.Hash,
            execution.VmState,
            execution.Exception,
            transaction.SystemFee,
            transaction.NetworkFee);
        if (!string.Equals(receipt.VmState, "HALT", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"transaction {receipt.TransactionHash} faulted: {receipt.Exception ?? "unknown VM fault"}");
        return receipt;
    }

    /// <summary>Builds, signs, broadcasts, and confirms an invocation transaction.</summary>
    public async ValueTask<RpcTransactionReceipt> SendInvocationAsync(
        ReadOnlyMemory<byte> script,
        CancellationToken cancellationToken = default)
    {
        var transaction = await BuildSignedInvocationAsync(script, cancellationToken).ConfigureAwait(false);
        return await BroadcastAndWaitAsync(transaction, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<uint> GetNetworkAsync(CancellationToken cancellationToken)
    {
        var result = await _rpc.CallAsync("getversion", new JArray(), cancellationToken).ConfigureAwait(false);
        if (result is not JObject version || version["protocol"] is not JObject protocol)
            throw new InvalidOperationException("getversion returned no protocol object");
        var network = ParseUInt32(protocol["network"], "getversion.protocol.network");
        if (network != _options.ExpectedNetwork)
            throw new InvalidOperationException(
                $"RPC network mismatch: expected {_options.ExpectedNetwork}, endpoint reports {network}");
        return network;
    }

    private async ValueTask<JObject> InvokeScriptAsync(
        ReadOnlyMemory<byte> script,
        CancellationToken cancellationToken)
    {
        var signer = new JObject();
        signer["account"] = _signer.Account.ToString();
        signer["scopes"] = _signer.Scope.ToString();
        var result = await _rpc.CallAsync(
            "invokescript",
            new JArray
            {
                Convert.ToBase64String(script.Span),
                new JArray { signer },
            },
            cancellationToken).ConfigureAwait(false);
        if (result is not JObject invoke)
            throw new InvalidOperationException("invokescript returned non-object");
        var state = GetRequiredString(invoke, "state");
        if (!string.Equals(state, "HALT", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"transaction preflight faulted: {invoke["exception"]?.AsString() ?? "unknown VM fault"}");
        return invoke;
    }

    private async ValueTask<uint> GetBlockCountAsync(CancellationToken cancellationToken)
    {
        var result = await _rpc.CallAsync("getblockcount", new JArray(), cancellationToken).ConfigureAwait(false);
        return ParseUInt32(result, "getblockcount");
    }

    private async ValueTask<long> CalculateNetworkFeeAsync(
        Transaction transaction,
        CancellationToken cancellationToken)
    {
        var result = await _rpc.CallAsync(
            "calculatenetworkfee",
            new JArray { Convert.ToBase64String(transaction.ToArray()) },
            cancellationToken).ConfigureAwait(false);
        if (result is not JObject fee)
            throw new InvalidOperationException("calculatenetworkfee returned non-object");
        var value = GetRequiredString(fee, "networkfee");
        var parsed = NeoGas.ParseRpcValue(value);
        if (parsed < 0) throw new InvalidOperationException("calculatenetworkfee returned a negative fee");
        return parsed;
    }

    private async ValueTask<(string VmState, string? Exception)> WaitForApplicationLogAsync(
        UInt256 transactionHash,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < _options.ConfirmationTimeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var result = await _rpc.CallAsync(
                    "getapplicationlog",
                    new JArray { transactionHash.ToString() },
                    cancellationToken).ConfigureAwait(false);
                if (result is not JObject log || log["executions"] is not JArray executions || executions.Count == 0)
                    throw new InvalidOperationException("getapplicationlog returned no executions");
                if (executions[0] is not JObject execution)
                    throw new InvalidOperationException("getapplicationlog returned an invalid execution");
                return (
                    GetRequiredString(execution, "vmstate"),
                    execution["exception"]?.AsString());
            }
            catch (JsonRpcException ex) when (IsPendingTransaction(ex))
            {
                await Task.Delay(_options.ConfirmationPollInterval, cancellationToken).ConfigureAwait(false);
            }
        }
        throw new TimeoutException($"timed out waiting for application log for {transactionHash}");
    }

    private static bool IsPendingTransaction(JsonRpcException exception)
    {
        return exception.Code is -100 or -105
            || exception.Message.Contains("Unknown transaction", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private void ValidateWitness(Witness witness)
    {
        ArgumentNullException.ThrowIfNull(witness);
        if (witness.VerificationScript.IsEmpty)
            throw new InvalidOperationException("Signer returned an empty verification script.");
        var account = witness.VerificationScript.ToArray().ToScriptHash();
        if (account != _signer.Account)
            throw new InvalidOperationException(
                $"Signer witness account {account} does not match configured account {_signer.Account}.");
    }

    private long AddSystemFeeMargin(long fee)
    {
        if (fee < 0) throw new InvalidOperationException("invokescript returned a negative gas value");
        var proportional = checked(fee * _options.SystemFeeMarginBasisPoints / 10_000L);
        return checked(fee + Math.Max(proportional, _options.MinimumSystemFeeMargin));
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
        if (token is JString text && uint.TryParse(text.AsString(), NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new InvalidOperationException($"{name} returned a non-uint value");
    }

    private static string GetRequiredString(JObject value, string property)
    {
        return value[property]?.AsString()
            ?? throw new InvalidOperationException($"RPC response is missing '{property}'");
    }

    private static uint RandomNonce()
    {
        Span<byte> bytes = stackalloc byte[sizeof(uint)];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt32(bytes);
    }

    private static void ValidateBroadcastResult(JToken? result, UInt256 expectedHash)
    {
        if (result is JBoolean accepted)
        {
            if (!accepted.AsBoolean())
                throw new InvalidOperationException($"sendrawtransaction returned false for {expectedHash}");
            return;
        }
        if (result is JString hashText
            && UInt256.TryParse(hashText.AsString(), out var returnedHash)
            && returnedHash is not null
            && returnedHash == expectedHash)
        {
            return;
        }
        throw new InvalidOperationException(
            $"sendrawtransaction returned an unexpected result for {expectedHash}");
    }
}
