using Neo.IO;
using Neo.Extensions;
using Neo.L2.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Iterators;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using Array = Neo.VM.Types.Array;
using Buffer = Neo.VM.Types.Buffer;

namespace Neo.L2.Executor.RiscV;

/// <summary>
/// Stateful host context passed through the PolkaVM callback ABI for one transaction.
/// It owns the isolated Neo snapshot, notification stream, interop handles, and syscall fees.
/// </summary>
/// <remarks>See <c>doc.md</c> §7.3, §8.1, and §8.4.</remarks>
public sealed class RiscVHostExecutionContext : IDisposable
{
    private readonly HostApplicationEngine _engine;
    private readonly L2DataCacheAdapter _rootCache;
    private readonly ContractState? _contract;
    private readonly Dictionary<ulong, object> _handles = new();
    private readonly Dictionary<object, ulong> _reverseHandles = new(ReferenceEqualityComparer.Instance);
    private readonly object _gate = new();
    private ulong _nextHandle = 1;
    private bool _staged;

    internal RiscVHostExecutionContext(
        IL2KeyValueStore state,
        Transaction transaction,
        BatchBlockContext batchContext,
        ProtocolSettings settings,
        ContractState? contract,
        long gasLimit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(batchContext);
        ArgumentNullException.ThrowIfNull(settings);
        if (gasLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(gasLimit), "gas limit must be positive");
        if (contract is not null && !contract.Script.Span.SequenceEqual(transaction.Script.Span))
            throw new InvalidOperationException("resolved RISC-V contract script does not match the transaction script");

        Transaction = transaction;
        BatchContext = batchContext;
        Settings = settings;
        GasLimit = gasLimit;
        CancellationToken = cancellationToken;
        _contract = contract;
        _rootCache = new L2DataCacheAdapter(state);
        PersistingBlock = BuildPersistingBlock(batchContext);
        ExecFeeFactor = PersistingBlock.Index == 0
            ? PolicyContract.DefaultExecFeeFactor
            : NativeContract.Policy.GetExecFeeFactor(_rootCache);
        ExecFeeFactorPico = checked((long)ExecFeeFactor * 10_000L);
        _engine = new HostApplicationEngine(
            transaction,
            _rootCache,
            PersistingBlock,
            settings,
            contract,
            gasLimit);
    }

    /// <summary>Canonical Neo transaction serving as the script container.</summary>
    public Transaction Transaction { get; }

    /// <summary>Batch-provided block and network context.</summary>
    public BatchBlockContext BatchContext { get; }

    /// <summary>Neo protocol settings used for hardfork and address-version checks.</summary>
    public ProtocolSettings Settings { get; }

    /// <summary>Synthetic persisting block shared with the ApplicationEngine executor.</summary>
    public Block PersistingBlock { get; }

    /// <summary>Per-transaction gas ceiling in datoshi.</summary>
    public long GasLimit { get; }

    /// <summary>Fixed and variable host syscall fees consumed in datoshi.</summary>
    public long HostFeeConsumed => _engine.FeeConsumed;

    /// <summary>Current notifications emitted by safely implemented runtime syscalls.</summary>
    public IReadOnlyList<NotifyEventArgs> Notifications => _engine.Notifications;

    internal CancellationToken CancellationToken { get; }
    internal uint ExecFeeFactor { get; }
    internal long ExecFeeFactorPico { get; }
    internal IReferenceCounter? ReferenceCounter => _engine.ReferenceCounter;
    internal Script PointerScript => _engine.PointerScript;
    internal byte Trigger => RiscVHost.TriggerApplication;
    internal uint Network => BatchContext.Network == 0 ? RiscVHost.DefaultNetwork : BatchContext.Network;
    internal byte AddressVersion => Settings.AddressVersion;
    internal ulong Timestamp => PersistingBlock.Timestamp;

    /// <summary>
    /// Invoke one host syscall using the same stack replacement convention as the native ABI.
    /// This is also the deterministic runner seam used by callback-focused tests.
    /// </summary>
    /// <param name="api">Neo interop descriptor hash.</param>
    /// <param name="inputStack">Complete evaluation stack before the syscall.</param>
    /// <param name="nativeGasLeft">
    /// Remaining datoshi reported by the native VM after opcode charging. When omitted,
    /// the transaction gas ceiling is used.
    /// </param>
    /// <returns>Complete replacement evaluation stack.</returns>
    public IReadOnlyList<StackItem> InvokeSyscall(
        uint api,
        IReadOnlyList<StackItem> inputStack,
        long? nativeGasLeft = null)
    {
        ArgumentNullException.ThrowIfNull(inputStack);
        var stack = new StackItem[inputStack.Count];
        for (var index = 0; index < stack.Length; index++)
            stack[index] = inputStack[index] ?? throw new ArgumentException("stack cannot contain null", nameof(inputStack));

        lock (_gate)
        {
            return InvokeSyscallCore(api, nativeGasLeft ?? GasLimit, stack);
        }
    }

    internal StackItem[] InvokeNativeSyscall(
        uint api,
        nuint instructionPointer,
        byte trigger,
        uint network,
        byte addressVersion,
        ulong timestamp,
        long nativeGasLeft,
        StackItem[] inputStack)
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (trigger != Trigger || network != Network || addressVersion != AddressVersion || timestamp != Timestamp)
        {
            throw new InvalidOperationException(
                $"native callback context mismatch at instruction {instructionPointer}");
        }

        lock (_gate)
        {
            return InvokeSyscallCore(api, nativeGasLeft, inputStack);
        }
    }

    internal ulong RegisterHandle(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            if (_reverseHandles.TryGetValue(value, out var existing)) return existing;
            var handle = _nextHandle++;
            _handles.Add(handle, value);
            _reverseHandles.Add(value, handle);
            return handle;
        }
    }

    internal object ResolveHandle(ulong handle)
    {
        lock (_gate)
        {
            return _handles.TryGetValue(handle, out var value)
                ? value
                : throw new InvalidOperationException($"unknown RISC-V host handle {handle}");
        }
    }

    internal void StageSuccessfulExecution()
    {
        lock (_gate)
        {
            if (_staged)
                throw new InvalidOperationException("RISC-V execution was already staged");
            _engine.CommitCurrentSnapshot();
            _rootCache.Commit();
            _staged = true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var disposable in _handles.Values.OfType<IDisposable>())
                disposable.Dispose();
            _handles.Clear();
            _reverseHandles.Clear();
            _engine.Dispose();
        }
    }

    private StackItem[] InvokeSyscallCore(uint api, long nativeGasLeft, StackItem[] inputStack)
    {
        CancellationToken.ThrowIfCancellationRequested();
        var descriptor = ApplicationEngine.GetInteropDescriptor(api);
        if (descriptor.Hardfork is not null &&
            !Settings.IsHardforkEnabled(descriptor.Hardfork.Value, PersistingBlock.Index))
        {
            throw new KeyNotFoundException($"syscall {descriptor.Name} is not active");
        }
        if ((CallFlags.All & descriptor.RequiredCallFlags) != descriptor.RequiredCallFlags)
            throw new InvalidOperationException($"syscall {descriptor.Name} requires unavailable call flags");
        if (!IsSafelyImplemented(api))
            throw new NotSupportedException($"unsupported consensus syscall {descriptor.Name} (0x{api:x8})");

        if (descriptor.FixedPrice != 0)
            _engine.Charge(checked(descriptor.FixedPrice * ExecFeeFactor));
        EnsureCombinedGas(nativeGasLeft);

        var output = api switch
        {
            uint hash when hash == ApplicationEngine.System_Runtime_Platform =>
                Append(inputStack, new ByteString("NEO"u8.ToArray())),
            uint hash when hash == ApplicationEngine.System_Runtime_GetTrigger =>
                Append(inputStack, new Integer(Trigger)),
            uint hash when hash == ApplicationEngine.System_Runtime_GetNetwork =>
                Append(inputStack, new Integer(Network)),
            uint hash when hash == ApplicationEngine.System_Runtime_GetAddressVersion =>
                Append(inputStack, new Integer(AddressVersion)),
            uint hash when hash == ApplicationEngine.System_Runtime_GasLeft =>
                Append(inputStack, new Integer(checked(nativeGasLeft - HostFeeConsumed))),
            uint hash when hash == ApplicationEngine.System_Runtime_GetScriptContainer =>
                Append(inputStack, _engine.GetScriptContainerItem()),
            uint hash when hash == ApplicationEngine.System_Runtime_GetTime =>
                Append(inputStack, new Integer(_engine.GetBlockTime())),
            uint hash when hash == ApplicationEngine.System_Runtime_GetExecutingScriptHash =>
                Append(inputStack, new ByteString(_engine.ExecutingScriptHash.GetSpan().ToArray())),
            uint hash when hash == ApplicationEngine.System_Runtime_GetCallingScriptHash =>
                AppendHashOrNull(inputStack, _engine.CallingHash),
            uint hash when hash == ApplicationEngine.System_Runtime_GetEntryScriptHash =>
                Append(inputStack, new ByteString(_engine.EntryHash.GetSpan().ToArray())),
            uint hash when hash == ApplicationEngine.System_Runtime_GetInvocationCounter =>
                Append(inputStack, new Integer(_engine.InvocationCounter())),
            uint hash when hash == ApplicationEngine.System_Runtime_CurrentSigners =>
                Append(inputStack, _engine.CurrentSignersItem()),
            uint hash when hash == ApplicationEngine.System_Runtime_CheckWitness =>
                HandleCheckWitness(inputStack),
            uint hash when hash == ApplicationEngine.System_Runtime_BurnGas =>
                HandleBurnGas(inputStack),
            uint hash when hash == ApplicationEngine.System_Runtime_Notify =>
                HandleRuntimeNotify(inputStack),
            uint hash when hash == ApplicationEngine.System_Runtime_GetNotifications =>
                HandleGetNotifications(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_GetContext =>
                Append(inputStack, CreateStorageContext(isReadOnly: false)),
            uint hash when hash == ApplicationEngine.System_Storage_GetReadOnlyContext =>
                Append(inputStack, CreateStorageContext(isReadOnly: true)),
            uint hash when hash == ApplicationEngine.System_Storage_AsReadOnly =>
                HandleStorageAsReadOnly(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Get =>
                HandleStorageGet(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Put =>
                HandleStoragePut(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Delete =>
                HandleStorageDelete(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Find =>
                HandleStorageFind(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Local_Get =>
                HandleStorageLocalGet(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Local_Put =>
                HandleStorageLocalPut(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Local_Delete =>
                HandleStorageLocalDelete(inputStack),
            uint hash when hash == ApplicationEngine.System_Storage_Local_Find =>
                HandleStorageLocalFind(inputStack),
            uint hash when hash == ApplicationEngine.System_Iterator_Next =>
                HandleIteratorNext(inputStack),
            uint hash when hash == ApplicationEngine.System_Iterator_Value =>
                HandleIteratorValue(inputStack),
            _ => throw new NotSupportedException($"unsupported consensus syscall 0x{api:x8}"),
        };

        EnsureCombinedGas(nativeGasLeft);
        return output;
    }

    private static bool IsSafelyImplemented(uint api)
    {
        return api == ApplicationEngine.System_Runtime_Platform
            || api == ApplicationEngine.System_Runtime_GetTrigger
            || api == ApplicationEngine.System_Runtime_GetNetwork
            || api == ApplicationEngine.System_Runtime_GetAddressVersion
            || api == ApplicationEngine.System_Runtime_GasLeft
            || api == ApplicationEngine.System_Runtime_GetScriptContainer
            || api == ApplicationEngine.System_Runtime_GetTime
            || api == ApplicationEngine.System_Runtime_GetExecutingScriptHash
            || api == ApplicationEngine.System_Runtime_GetCallingScriptHash
            || api == ApplicationEngine.System_Runtime_GetEntryScriptHash
            || api == ApplicationEngine.System_Runtime_GetInvocationCounter
            || api == ApplicationEngine.System_Runtime_CurrentSigners
            || api == ApplicationEngine.System_Runtime_CheckWitness
            || api == ApplicationEngine.System_Runtime_BurnGas
            || api == ApplicationEngine.System_Runtime_Notify
            || api == ApplicationEngine.System_Runtime_GetNotifications
            || api == ApplicationEngine.System_Storage_GetContext
            || api == ApplicationEngine.System_Storage_GetReadOnlyContext
            || api == ApplicationEngine.System_Storage_AsReadOnly
            || api == ApplicationEngine.System_Storage_Get
            || api == ApplicationEngine.System_Storage_Put
            || api == ApplicationEngine.System_Storage_Delete
            || api == ApplicationEngine.System_Storage_Find
            || api == ApplicationEngine.System_Storage_Local_Get
            || api == ApplicationEngine.System_Storage_Local_Put
            || api == ApplicationEngine.System_Storage_Local_Delete
            || api == ApplicationEngine.System_Storage_Local_Find
            || api == ApplicationEngine.System_Iterator_Next
            || api == ApplicationEngine.System_Iterator_Value;
    }

    private void EnsureCombinedGas(long nativeGasLeft)
    {
        if (nativeGasLeft < 0 || HostFeeConsumed > nativeGasLeft)
            throw new InvalidOperationException("Insufficient GAS.");
    }

    private StackItem[] HandleCheckWitness(StackItem[] stack)
    {
        if (stack.Length == 0 || !TryGetBytes(stack[^1], out var value))
            throw new InvalidOperationException("Runtime.CheckWitness requires a byte-like argument");
        return ReplaceSuffix(stack, 1, _engine.CheckWitnessItem(value) ? StackItem.True : StackItem.False);
    }

    private StackItem[] HandleBurnGas(StackItem[] stack)
    {
        if (stack.Length == 0 || stack[^1] is not Integer value)
            throw new InvalidOperationException("Runtime.BurnGas requires an integer argument");
        _engine.Burn(checked((long)value.GetInteger()));
        return RemoveSuffix(stack, 1);
    }

    private StackItem[] HandleRuntimeNotify(StackItem[] stack)
    {
        if (stack.Length < 2)
            throw new InvalidOperationException("Runtime.Notify requires event name and state");

        ByteString eventName;
        Array state;
        if (stack[^1] is ByteString topName && stack[^2] is Array bottomState)
        {
            eventName = topName;
            state = bottomState;
        }
        else if (stack[^2] is ByteString bottomName && stack[^1] is Array topState)
        {
            eventName = bottomName;
            state = topState;
        }
        else
        {
            throw new InvalidOperationException("Runtime.Notify requires a byte-string name and array state");
        }

        _engine.EmitNotification(eventName.GetSpan().ToArray(), state);
        return RemoveSuffix(stack, 2);
    }

    private StackItem[] HandleGetNotifications(StackItem[] stack)
    {
        if (stack.Length == 0)
            throw new InvalidOperationException("Runtime.GetNotifications requires one argument");
        UInt160? hash = stack[^1] switch
        {
            Null => null,
            ByteString bytes when bytes.GetSpan().Length == UInt160.Length => new UInt160(bytes.GetSpan()),
            Buffer bytes when bytes.GetSpan().Length == UInt160.Length => new UInt160(bytes.GetSpan()),
            _ => throw new InvalidOperationException("Runtime.GetNotifications requires null or UInt160 bytes"),
        };
        return ReplaceSuffix(stack, 1, _engine.GetNotificationsItem(hash));
    }

    private StackItem CreateStorageContext(bool isReadOnly)
    {
        if (_contract is null)
            throw new InvalidOperationException("storage contexts require an explicitly resolved deployed contract");
        return StackItem.FromInterface(new StorageContext { Id = _contract.Id, IsReadOnly = isReadOnly });
    }

    private static StackItem[] HandleStorageAsReadOnly(StackItem[] stack)
    {
        if (stack.Length == 0)
            throw new InvalidOperationException("Storage.AsReadOnly requires one argument");
        var context = ParseStorageContext(stack[^1]);
        var readOnly = new StorageContext { Id = context.Id, IsReadOnly = true };
        return ReplaceSuffix(stack, 1, StackItem.FromInterface(readOnly));
    }

    private StackItem[] HandleStorageGet(StackItem[] stack)
    {
        ParseContextAndKey(stack, out var context, out var key);
        var value = _engine.StorageGet(context, key);
        return ReplaceSuffix(stack, 2, value.HasValue ? new ByteString(value.Value) : StackItem.Null);
    }

    private StackItem[] HandleStoragePut(StackItem[] stack)
    {
        if (stack.Length < 3)
            throw new InvalidOperationException("Storage.Put requires context, key, and value");

        StorageContext context;
        byte[] key;
        byte[] value;
        if (TryParseStorageContext(stack[^3], out context) &&
            TryGetBytes(stack[^2], out var forwardKey) &&
            TryGetBytes(stack[^1], out var forwardValue))
        {
            key = forwardKey;
            value = forwardValue;
        }
        else if (TryParseStorageContext(stack[^1], out context) &&
                 TryGetBytes(stack[^2], out var reverseKey) &&
                 TryGetBytes(stack[^3], out var reverseValue))
        {
            key = reverseKey;
            value = reverseValue;
        }
        else
        {
            throw new InvalidOperationException("Storage.Put requires a context plus byte-like key and value");
        }

        _engine.StoragePut(context, key, value);
        return RemoveSuffix(stack, 3);
    }

    private StackItem[] HandleStorageDelete(StackItem[] stack)
    {
        ParseContextAndKey(stack, out var context, out var key);
        _engine.StorageDelete(context, key);
        return RemoveSuffix(stack, 2);
    }

    private StackItem[] HandleStorageFind(StackItem[] stack)
    {
        if (stack.Length < 3)
            throw new InvalidOperationException("Storage.Find requires context, prefix, and options");

        StorageContext context;
        byte[] prefix;
        Integer options;
        if (TryParseStorageContext(stack[^3], out context) &&
            TryGetBytes(stack[^2], out var forwardPrefix) &&
            stack[^1] is Integer forwardOptions)
        {
            prefix = forwardPrefix;
            options = forwardOptions;
        }
        else if (TryParseStorageContext(stack[^1], out context) &&
                 TryGetBytes(stack[^2], out var reversePrefix) &&
                 stack[^3] is Integer reverseOptions)
        {
            prefix = reversePrefix;
            options = reverseOptions;
        }
        else
        {
            throw new InvalidOperationException("Storage.Find requires a context, byte-like prefix, and integer options");
        }

        var iterator = _engine.StorageFind(context, prefix, (FindOptions)checked((byte)options.GetInteger()));
        return ReplaceSuffix(stack, 3, StackItem.FromInterface(iterator));
    }

    private StackItem[] HandleStorageLocalGet(StackItem[] stack)
    {
        if (stack.Length == 0 || !TryGetBytes(stack[^1], out var key))
            throw new InvalidOperationException("Storage.Local.Get requires a byte-like key");
        var value = _engine.StorageGet(ParseStorageContext(CreateStorageContext(true)), key);
        return ReplaceSuffix(stack, 1, value.HasValue ? new ByteString(value.Value) : StackItem.Null);
    }

    private StackItem[] HandleStorageLocalPut(StackItem[] stack)
    {
        if (stack.Length < 2 || !TryGetBytes(stack[^2], out var key) || !TryGetBytes(stack[^1], out var value))
            throw new InvalidOperationException("Storage.Local.Put requires byte-like key and value");
        _engine.StoragePut(ParseStorageContext(CreateStorageContext(false)), key, value);
        return RemoveSuffix(stack, 2);
    }

    private StackItem[] HandleStorageLocalDelete(StackItem[] stack)
    {
        if (stack.Length == 0 || !TryGetBytes(stack[^1], out var key))
            throw new InvalidOperationException("Storage.Local.Delete requires a byte-like key");
        _engine.StorageDelete(ParseStorageContext(CreateStorageContext(false)), key);
        return RemoveSuffix(stack, 1);
    }

    private StackItem[] HandleStorageLocalFind(StackItem[] stack)
    {
        if (stack.Length < 2 || !TryGetBytes(stack[^2], out var prefix) || stack[^1] is not Integer options)
            throw new InvalidOperationException("Storage.Local.Find requires a byte-like prefix and integer options");
        var iterator = _engine.StorageFind(
            ParseStorageContext(CreateStorageContext(true)),
            prefix,
            (FindOptions)checked((byte)options.GetInteger()));
        return ReplaceSuffix(stack, 2, StackItem.FromInterface(iterator));
    }

    private static StackItem[] HandleIteratorNext(StackItem[] stack)
    {
        var iterator = ParseIterator(stack);
        return ReplaceSuffix(stack, 1, iterator.Next() ? StackItem.True : StackItem.False);
    }

    private StackItem[] HandleIteratorValue(StackItem[] stack)
    {
        var iterator = ParseIterator(stack);
        return ReplaceSuffix(stack, 1, iterator.Value(_engine.ReferenceCounter));
    }

    private static IIterator ParseIterator(StackItem[] stack)
    {
        if (stack.Length == 0 ||
            stack[^1] is not InteropInterface interop ||
            interop.GetInterface<object>() is not IIterator iterator)
        {
            throw new InvalidOperationException("iterator syscall requires an iterator handle");
        }
        return iterator;
    }

    private static void ParseContextAndKey(
        StackItem[] stack,
        out StorageContext context,
        out byte[] key)
    {
        if (stack.Length < 2)
            throw new InvalidOperationException("storage syscall requires context and key");
        if (TryParseStorageContext(stack[^2], out context) && TryGetBytes(stack[^1], out key))
            return;
        if (TryParseStorageContext(stack[^1], out context) && TryGetBytes(stack[^2], out key))
            return;
        throw new InvalidOperationException("storage syscall requires a context and byte-like key");
    }

    private static StorageContext ParseStorageContext(StackItem item)
    {
        return TryParseStorageContext(item, out var context)
            ? context
            : throw new InvalidOperationException("storage context must be an opaque interop handle");
    }

    private static bool TryParseStorageContext(StackItem item, out StorageContext context)
    {
        if (item is InteropInterface interop && interop.GetInterface<object>() is StorageContext value)
        {
            context = value;
            return true;
        }
        context = new StorageContext();
        return false;
    }

    private static bool TryGetBytes(StackItem item, out byte[] bytes)
    {
        if (item is ByteString or Buffer)
        {
            bytes = item.GetSpan().ToArray();
            return true;
        }
        bytes = System.Array.Empty<byte>();
        return false;
    }

    private static StackItem[] Append(StackItem[] stack, StackItem value)
    {
        var output = new StackItem[stack.Length + 1];
        System.Array.Copy(stack, output, stack.Length);
        output[^1] = value;
        return output;
    }

    private static StackItem[] AppendHashOrNull(StackItem[] stack, UInt160? hash)
        => Append(stack, hash is null ? StackItem.Null : new ByteString(hash.GetSpan().ToArray()));

    private static StackItem[] RemoveSuffix(StackItem[] stack, int count)
    {
        var output = new StackItem[stack.Length - count];
        System.Array.Copy(stack, output, output.Length);
        return output;
    }

    private static StackItem[] ReplaceSuffix(StackItem[] stack, int count, StackItem value)
    {
        var output = new StackItem[stack.Length - count + 1];
        if (output.Length > 1)
            System.Array.Copy(stack, output, output.Length - 1);
        output[^1] = value;
        return output;
    }

    private static Block BuildPersistingBlock(BatchBlockContext context)
    {
        return new Block
        {
            Header = new Header
            {
                Index = context.L1FinalizedHeight,
                Timestamp = context.FirstBlockTimestamp,
                MerkleRoot = UInt256.Zero,
                PrevHash = UInt256.Zero,
                NextConsensus = UInt160.Zero,
                Nonce = 0,
                Witness = new Witness
                {
                    InvocationScript = ReadOnlyMemory<byte>.Empty,
                    VerificationScript = ReadOnlyMemory<byte>.Empty,
                },
            },
            Transactions = System.Array.Empty<Transaction>(),
        };
    }

    private sealed class HostApplicationEngine : ApplicationEngine
    {
        public HostApplicationEngine(
            Transaction transaction,
            DataCache snapshot,
            Block persistingBlock,
            ProtocolSettings settings,
            ContractState? contract,
            long gasLimit)
            : base(TriggerType.Application, transaction, snapshot, persistingBlock, settings, gasLimit)
        {
            var context = LoadScript(
                new Script(transaction.Script, strictMode: true),
                configureState: state =>
                {
                    state.CallFlags = CallFlags.All;
                    state.Contract = contract;
                    state.ScriptHash = contract?.Hash ?? transaction.Script.Span.ToScriptHash();
                });
            PointerScript = context.Script;
        }

        public Script PointerScript { get; }
        public UInt160 ExecutingScriptHash => CurrentScriptHash!;
        public UInt160 EntryHash => CurrentScriptHash!;
        public UInt160? CallingHash => CallingScriptHash;

        public void Charge(long amount) => AddFee(amount);
        public void Burn(long amount) => BurnGas(amount);
        public bool CheckWitnessItem(byte[] value) => CheckWitness(value);
        public StackItem GetScriptContainerItem() => GetScriptContainer();
        public ulong GetBlockTime() => GetTime();
        public int InvocationCounter() => GetInvocationCounter();
        public StackItem CurrentSignersItem() => Convert(GetCurrentSigners());
        public void EmitNotification(byte[] eventName, Array state) => RuntimeNotify(eventName, state);
        public Array GetNotificationsItem(UInt160? hash) => GetNotifications(hash);
        public ReadOnlyMemory<byte>? StorageGet(StorageContext context, byte[] key) => Get(context, key);
        public void StoragePut(StorageContext context, byte[] key, byte[] value) => Put(context, key, value);
        public void StorageDelete(StorageContext context, byte[] key) => Delete(context, key);
        public IIterator StorageFind(StorageContext context, byte[] prefix, FindOptions options) => Find(context, prefix, options);
        public void CommitCurrentSnapshot() => SnapshotCache.Commit();
    }
}
