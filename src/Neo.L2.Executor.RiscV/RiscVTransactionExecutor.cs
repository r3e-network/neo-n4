using Neo.Cryptography;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;

namespace Neo.L2.Executor.RiscV;

/// <summary>
/// <see cref="ITransactionExecutor"/> backed by the PolkaVM-based NeoVM2/RISC-V host.
/// </summary>
/// <remarks>
/// The serialized transaction payload is expected to be the compiled NeoVM2/RISC-V
/// program bytes for this execution stage. Legacy NeoVM bytecode must use
/// <c>ApplicationEngineTransactionExecutor</c> instead.
/// </remarks>
public sealed class RiscVTransactionExecutor : ITransactionExecutor
{
    /// <summary>Host runner seam used by tests and by the default native-host bridge.</summary>
    public delegate RiscVExecutionResult ProgramRunner(ReadOnlyMemory<byte> program, BatchBlockContext batchContext);

    private readonly ProgramRunner _runner;

    /// <summary>Construct with the default native <see cref="RiscVHost"/> runner.</summary>
    public RiscVTransactionExecutor()
        : this(RunOnNativeHost)
    {
    }

    /// <summary>Construct with an explicit runner, primarily for deterministic tests.</summary>
    public RiscVTransactionExecutor(ProgramRunner runner)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
    }

    /// <inheritdoc />
    public ValueTask<TransactionExecutionResult> ExecuteAsync(
        ReadOnlyMemory<byte> serializedTx,
        BatchBlockContext batchContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batchContext);

        var txHash = new UInt256(Crypto.Hash256(serializedTx.Span));
        if (serializedTx.Length == 0)
            return new ValueTask<TransactionExecutionResult>(Failed(txHash));

        RiscVExecutionResult result;
        try
        {
            result = _runner(serializedTx, batchContext);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DllNotFoundException)
        {
            throw;
        }
        catch (EntryPointNotFoundException)
        {
            throw;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("neo_riscv_host", StringComparison.OrdinalIgnoreCase))
        {
            throw;
        }
        catch
        {
            return new ValueTask<TransactionExecutionResult>(Failed(txHash));
        }

        var success = result.Halted;
        return new ValueTask<TransactionExecutionResult>(new TransactionExecutionResult
        {
            TxHash = txHash,
            Receipt = new Receipt
            {
                TxHash = txHash,
                Success = success,
                GasConsumed = Math.Max(0, result.FeeConsumed),
                StorageDeltaHash = UInt256.Zero,
                EventsHash = UInt256.Zero,
            },
            Withdrawals = Array.Empty<WithdrawalRequest>(),
            Messages = Array.Empty<CrossChainMessage>(),
        });
    }

    private static RiscVExecutionResult RunOnNativeHost(ReadOnlyMemory<byte> program, BatchBlockContext batchContext)
    {
        if (!RiscVHost.IsAvailable)
        {
            var detail = string.IsNullOrWhiteSpace(RiscVHost.LastAvailabilityError)
                ? string.Empty
                : $" Last native load error: {RiscVHost.LastAvailabilityError}";
            throw new InvalidOperationException(
                "NeoVM2/RISC-V executor requested but neo_riscv_host is unavailable. " +
                "Build external/neo-riscv-vm and place neo_riscv_host.dll (Windows) " +
                "or libneo_riscv_host.so (Linux/WSL) on PATH/LD_LIBRARY_PATH. " +
                "On Windows, also ensure dependent runtime DLLs such as libunwind.dll " +
                $"are on PATH.{detail}");
        }

        var network = batchContext.Network == 0 ? RiscVHost.DefaultNetwork : batchContext.Network;
        return RiscVHost.Execute(
            program.Span,
            RiscVHost.TriggerApplication,
            network,
            batchContext.LastBlockTimestamp,
            gasLeft: 1_000_000_000L);
    }

    private static TransactionExecutionResult Failed(UInt256 txHash) => new()
    {
        TxHash = txHash,
        Receipt = new Receipt
        {
            TxHash = txHash,
            Success = false,
            GasConsumed = 0,
            StorageDeltaHash = UInt256.Zero,
            EventsHash = UInt256.Zero,
        },
        Withdrawals = Array.Empty<WithdrawalRequest>(),
        Messages = Array.Empty<CrossChainMessage>(),
    };
}
