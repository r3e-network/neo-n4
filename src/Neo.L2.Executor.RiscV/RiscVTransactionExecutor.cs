using Neo.Cryptography;
using Neo.L2.Executor;
using Neo.L2.Executor.Receipts;

namespace Neo.L2.Executor.RiscV;

/// <summary>
/// Preview <see cref="ITransactionExecutor"/> seam backed by the PolkaVM-based NeoVM2/RISC-V host.
/// </summary>
/// <remarks>
/// The serialized transaction payload is expected to be the compiled NeoVM2/RISC-V
/// program bytes for this execution stage. Legacy NeoVM bytecode must use
/// <c>ApplicationEngineTransactionExecutor</c> instead.
/// <para>
/// <b>Not yet a state-bearing production executor.</b> This wrapper binds only the stateless
/// <c>neo_riscv_execute_script</c> FFI (see <see cref="RiscVHost"/>), which runs the program
/// with no host callbacks. Consequently it cannot mutate L2 state: it holds no
/// <c>IL2KeyValueStore</c>, the receipt's <see cref="Receipt.StorageDeltaHash"/> and
/// <see cref="Receipt.EventsHash"/> are always <see cref="UInt256.Zero"/>, and it never emits
/// withdrawals or cross-chain messages. Pairing it with a state-root oracle would compute a
/// post-state root over a store this executor never wrote. Wiring the stateful
/// <c>neo_riscv_execute_script_with_host</c> variants (storage-delta / withdrawal / message
/// extraction and per-tx gas threading) is required before this becomes the production path.
/// </para>
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
        catch (RiscVHostUnavailableException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
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
                // Always Zero: the stateless FFI produces no storage delta or event
                // stream. Real values require the _with_host FFI binding (see class remarks).
                StorageDeltaHash = UInt256.Zero,
                EventsHash = UInt256.Zero,
            },
            // Always empty for the same reason — no host callbacks means no withdrawals
            // or cross-chain messages can be emitted by the guest program.
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
            throw new RiscVHostUnavailableException(
                "NeoVM2/RISC-V executor requested but neo_riscv_host is unavailable. " +
                "Build external/neo-riscv-vm and place neo_riscv_host.dll (Windows) " +
                "or libneo_riscv_host.so (Linux/WSL) on PATH/LD_LIBRARY_PATH. " +
                "On Windows, also ensure dependent runtime DLLs such as libunwind.dll " +
                $"are on PATH.{detail}");
        }

        var network = batchContext.Network == 0 ? RiscVHost.DefaultNetwork : batchContext.Network;
        // Fixed budget for the preview seam: the serialized payload is a raw RISC-V program,
        // not a decoded Neo transaction, so a per-tx gas limit isn't available here. Threading
        // the transaction's own gas requires the decode + _with_host wiring noted in the class
        // remarks; until then OOG/HALT outcomes are evaluated against this constant budget.
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
