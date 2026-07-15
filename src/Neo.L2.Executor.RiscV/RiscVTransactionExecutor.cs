using Neo.Cryptography;
using Neo.Extensions;
using Neo.IO;
using Neo.L2.Executor.Effects;
using Neo.L2.Executor.Receipts;
using Neo.L2.Executor.State;
using Neo.L2.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;

namespace Neo.L2.Executor.RiscV;

/// <summary>
/// Stateful <see cref="ITransactionExecutor"/> backed by the PolkaVM Neo RISC-V guest and
/// <c>neo_riscv_execute_script_with_host</c>.
/// </summary>
/// <remarks>
/// <para>
/// The input is a canonical serialized Neo <see cref="Transaction"/>. Its script executes
/// against a read-through transaction overlay; only native HALT commits the overlay and
/// notifications. FAULT, OOG, callback errors, collector errors, and unsupported syscalls
/// all roll back.
/// </para>
/// <para>
/// Standard cross-contract dispatch remains fail-closed until the guest and C# host can
/// reproduce ApplicationEngine invocation-stack semantics. A resolver may identify a direct
/// deployed-contract script so storage and Runtime.Notify use its id, hash, and manifest.
/// </para>
/// See <c>doc.md</c> §7.2, §7.3, §7.5, §8.1, and §8.4.
/// </remarks>
public sealed class RiscVTransactionExecutor : ITransactionExecutor
{
    /// <summary>Native runner seam used by the production host and deterministic tests.</summary>
    public delegate RiscVExecutionResult ProgramRunner(
        ReadOnlyMemory<byte> script,
        RiscVHostExecutionContext context);

    /// <summary>
    /// Resolve a transaction script to a deployed contract. Returning <c>null</c> keeps the
    /// execution dynamic, where storage contexts and notifications deliberately FAULT.
    /// </summary>
    public delegate ContractState? ContractResolver(
        Transaction transaction,
        BatchBlockContext batchContext);

    private readonly IL2KeyValueStore _state;
    private readonly ProtocolSettings _settings;
    private readonly INotificationCollector _collector;
    private readonly ProgramRunner _runner;
    private readonly ContractResolver? _contractResolver;
    private readonly long _gasLimit;
    private readonly HashSet<(UInt160 Sender, uint Nonce)> _consumedNonces = new();
    private readonly Lock _nonceGate = new();

    /// <inheritdoc />
    public TransactionEffectsProfile EffectsProfile => TransactionEffectsProfile.CanonicalNativeV1;

    /// <summary>
    /// Construct a self-contained devnet executor with bootstrapped in-memory Neo state.
    /// Production operators should use the explicit-state constructor.
    /// </summary>
    public RiscVTransactionExecutor()
        : this(CreateBootstrappedInMemoryState(), NeoVMGenesisBootstrap.DefaultBootstrapSettings)
    {
    }

    /// <summary>Construct an in-memory executor with an explicit runner for deterministic tests.</summary>
    public RiscVTransactionExecutor(ProgramRunner runner)
        : this(
            CreateBootstrappedInMemoryState(),
            NeoVMGenesisBootstrap.DefaultBootstrapSettings,
            runner: runner)
    {
    }

    /// <summary>Construct a stateful RISC-V executor.</summary>
    /// <param name="state">Bootstrapped Neo-compatible L2 state store.</param>
    /// <param name="settings">Protocol settings shared with ApplicationEngine execution.</param>
    /// <param name="collector">Optional notification decoder.</param>
    /// <param name="gasLimit">Per-transaction gas ceiling in datoshi.</param>
    /// <param name="contractResolver">Optional direct deployed-contract resolver.</param>
    /// <param name="runner">Optional native runner replacement.</param>
    public RiscVTransactionExecutor(
        IL2KeyValueStore state,
        ProtocolSettings settings,
        INotificationCollector? collector = null,
        long gasLimit = ApplicationEngine.TestModeGas,
        ContractResolver? contractResolver = null,
        ProgramRunner? runner = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settings);
        if (gasLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(gasLimit), "gas limit must be positive");
        _state = state;
        _settings = settings;
        _collector = collector ?? NoEffectsCollector.Instance;
        _gasLimit = gasLimit;
        _contractResolver = contractResolver;
        _runner = runner ?? RunOnNativeHost;
    }

    /// <inheritdoc />
    public async ValueTask<TransactionExecutionResult> ExecuteAsync(
        ReadOnlyMemory<byte> serializedTx,
        BatchBlockContext batchContext,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(batchContext);
        var rawHash = new UInt256(Crypto.Hash256(serializedTx.Span));

        Transaction transaction;
        try
        {
            transaction = serializedTx.ToArray().AsSerializable<Transaction>();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failed(rawHash, $"deserialize failed: {ex.Message}");
        }

        var nonceKey = (transaction.Sender, transaction.Nonce);
        lock (_nonceGate)
        {
            if (!_consumedNonces.Add(nonceKey))
                return Failed(transaction.Hash, "duplicate sender nonce");
        }

        using var stateTransaction = new ExecutionStateTransaction(_state);
        ContractState? contract;
        try
        {
            contract = _contractResolver?.Invoke(transaction, batchContext);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Failed(transaction.Hash, $"contract resolution failed: {ex.Message}");
        }

        RiscVHostExecutionContext hostContext;
        try
        {
            hostContext = new RiscVHostExecutionContext(
                stateTransaction,
                transaction,
                batchContext,
                _settings,
                contract,
                _gasLimit,
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            stateTransaction.Rollback();
            return Failed(transaction.Hash, $"host context setup failed: {ex.Message}");
        }

        try
        {
            RiscVExecutionResult execution;
            try
            {
                execution = _runner(transaction.Script, hostContext);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or RiscVHostUnavailableException)
            {
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                stateTransaction.Rollback();
                return Failed(transaction.Hash, $"RISC-V execution failed: {ex.Message}", hostContext.HostFeeConsumed);
            }

            long gasConsumed;
            try
            {
                if (execution.HostFeeConsumed != hostContext.HostFeeConsumed)
                    throw new InvalidOperationException("runner host fee does not match callback context");
                gasConsumed = execution.FeeConsumed;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                stateTransaction.Rollback();
                return Failed(transaction.Hash, $"invalid native fee: {ex.Message}", hostContext.HostFeeConsumed);
            }

            if (!execution.Halted || gasConsumed > _gasLimit)
            {
                stateTransaction.Rollback();
                return Failed(
                    transaction.Hash,
                    execution.ErrorMessage ?? (gasConsumed > _gasLimit ? "Insufficient GAS." : "native execution fault"),
                    gasConsumed);
            }

            try
            {
                hostContext.StageSuccessfulExecution();
                var effects = CanonicalExecutionEffects.Create(
                    stateTransaction.GetChanges(),
                    hostContext.Notifications);
                var (withdrawals, messages) = await _collector
                    .CollectAsync(transaction, effects.Notifications)
                    .ConfigureAwait(false);
                stateTransaction.Commit();

                return new TransactionExecutionResult
                {
                    TxHash = transaction.Hash,
                    Receipt = new Receipt
                    {
                        TxHash = transaction.Hash,
                        Success = true,
                        GasConsumed = gasConsumed,
                        StorageDeltaHash = effects.StorageHash,
                        EventsHash = effects.EventsHash,
                    },
                    Withdrawals = withdrawals,
                    Messages = messages,
                    Effects = effects,
                };
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                stateTransaction.Rollback();
                return Failed(transaction.Hash, $"effect commit failed: {ex.Message}", gasConsumed);
            }
        }
        finally
        {
            hostContext.Dispose();
        }
    }

    private static RiscVExecutionResult RunOnNativeHost(
        ReadOnlyMemory<byte> script,
        RiscVHostExecutionContext context)
    {
        if (!RiscVHost.IsAvailable)
        {
            var detail = string.IsNullOrWhiteSpace(RiscVHost.LastAvailabilityError)
                ? string.Empty
                : $" Last native load error: {RiscVHost.LastAvailabilityError}";
            throw new RiscVHostUnavailableException(
                "Neo RISC-V executor requested but the stateful neo_riscv_host ABI is unavailable. " +
                "Build external/neo-riscv-vm and place the platform library on the dynamic loader path." +
                detail);
        }
        return RiscVHost.Execute(script.Span, context);
    }

    private static IL2KeyValueStore CreateBootstrappedInMemoryState()
    {
        var state = new InMemoryKeyValueStore();
        try
        {
            NeoVMGenesisBootstrap.Run(state, NeoVMGenesisBootstrap.DefaultBootstrapSettings);
            return state;
        }
        catch
        {
            state.Dispose();
            throw;
        }
    }

    private static TransactionExecutionResult Failed(
        UInt256 txHash,
        string reason,
        long gasConsumed = 0)
    {
        return new TransactionExecutionResult
        {
            TxHash = txHash,
            Receipt = new Receipt
            {
                TxHash = txHash,
                Success = false,
                GasConsumed = Math.Max(0, gasConsumed),
                StorageDeltaHash = UInt256.Zero,
                EventsHash = UInt256.Zero,
            },
            Withdrawals = System.Array.Empty<WithdrawalRequest>(),
            Messages = System.Array.Empty<CrossChainMessage>(),
            Effects = CanonicalExecutionEffects.Empty,
            FailureReason = reason,
        };
    }
}
