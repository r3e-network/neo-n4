using System.Globalization;
using Neo.L2.Settlement.Rpc;

namespace Neo.Stack.Cli.Commands;

/// <summary>Shared signed-transaction execution path for neo-stack operator commands.</summary>
internal static class OperatorTransactionBroadcaster
{
    public static async Task<int> BroadcastAsync(
        string[] args,
        byte[] script,
        string operation,
        HttpClient? httpClient = null,
        string optionPrefix = "",
        CancellationToken cancellationToken = default,
        IExternalSignerCommandRunner? externalSignerCommandRunner = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(script);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        var rpcOption = Option(optionPrefix, "rpc");
        var networkOption = Option(optionPrefix, "expected-network");
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

        if (!OperatorTransactionSignerFactory.TryCreate(
                args,
                optionPrefix,
                externalSignerCommandRunner,
                out var signer,
                out var signerError))
        {
            Console.Error.WriteLine(signerError);
            return 12;
        }

        try
        {
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
        finally
        {
            (signer as IDisposable)?.Dispose();
        }
    }

    private static string Option(string prefix, string name)
    {
        return string.IsNullOrEmpty(prefix) ? $"--{name}" : $"--{prefix}-{name}";
    }
}
