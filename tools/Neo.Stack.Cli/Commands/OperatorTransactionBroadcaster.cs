using System.Globalization;
using Neo.Cryptography;
using Neo.Extensions.IO;
using Neo.L2.Settlement.Rpc;
using Neo.Wallets;

namespace Neo.Stack.Cli.Commands;

/// <summary>Shared signed-transaction execution path for neo-stack operator commands.</summary>
internal static class OperatorTransactionBroadcaster
{
    private const string DefaultWifEnvironmentVariable = "NEO_N4_OPERATOR_WIF";

    public static async Task<int> BroadcastAsync(
        string[] args,
        byte[] script,
        string operation,
        HttpClient? httpClient = null,
        string optionPrefix = "",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var rpcOption = Option(optionPrefix, "rpc");
        var networkOption = Option(optionPrefix, "expected-network");
        var wifOption = Option(optionPrefix, "wif-env");
        var rpcValue = ArgUtil.Get(args, rpcOption, "");
        if (!Uri.TryCreate(rpcValue, UriKind.Absolute, out var rpcEndpoint)
            || rpcEndpoint.Scheme is not ("http" or "https"))
        {
            Console.Error.WriteLine($"{rpcOption} must be an absolute HTTP(S) Neo JSON-RPC endpoint");
            return 10;
        }

        var networkValue = ArgUtil.Get(args, networkOption, "");
        if (!uint.TryParse(networkValue, NumberStyles.None, CultureInfo.InvariantCulture, out var expectedNetwork))
        {
            Console.Error.WriteLine($"{networkOption} <uint> is required for broadcast safety");
            return 11;
        }

        var wifEnvironmentVariable = ArgUtil.Get(args, wifOption, DefaultWifEnvironmentVariable);
        var wif = Environment.GetEnvironmentVariable(wifEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(wif))
        {
            Console.Error.WriteLine($"{wifEnvironmentVariable} is required; pass {wifOption} <name> to use another environment variable");
            return 12;
        }

        try
        {
            using var signer = ImportSigner(wif);
            using var rpc = new JsonRpcClient(rpcEndpoint, httpClient);
            var sender = new RpcTransactionSender(
                rpc,
                signer,
                new RpcTransactionSenderOptions { ExpectedNetwork = expectedNetwork });
            var receipt = await sender.SendInvocationAsync(script, cancellationToken).ConfigureAwait(false);
            Console.WriteLine();
            Console.WriteLine($"{operation} confirmed:");
            Console.WriteLine($"  transactionHash : {receipt.TransactionHash}");
            Console.WriteLine($"  vmState         : {receipt.VmState}");
            Console.WriteLine($"  systemFee       : {receipt.SystemFee} datoshi");
            Console.WriteLine($"  networkFee      : {receipt.NetworkFee} datoshi");
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"{operation} failed: {exception.Message}");
            return 13;
        }
    }

    private static LocalKeyTransactionSigner ImportSigner(string wif)
    {
        var payload = wif.Base58CheckDecode();
        try
        {
            if (payload.Length != 34 || payload[0] != 0x80 || payload[33] != 0x01)
                throw new FormatException("WIF payload is not a compressed Neo private key");
            var key = new KeyPair(payload[1..33]);
            try
            {
                return new LocalKeyTransactionSigner(key);
            }
            finally
            {
                key.PrivateKey.AsSpan().Clear();
            }
        }
        finally
        {
            payload.AsSpan().Clear();
        }
    }

    private static string Option(string prefix, string name)
    {
        return string.IsNullOrEmpty(prefix) ? $"--{name}" : $"--{prefix}-{name}";
    }
}
