using System.Collections.Concurrent;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Neo.SmartContract.Iterators;
using Neo.VM.Types;
using Array = Neo.VM.Types.Array;
using Buffer = Neo.VM.Types.Buffer;

namespace Neo.L2.Executor.RiscV;

/// <summary>Thrown when the stateful PolkaVM host library cannot be loaded.</summary>
/// <remarks>See <c>doc.md</c> §7.5 and §8.4.</remarks>
public sealed class RiscVHostUnavailableException : InvalidOperationException
{
    /// <summary>Create an availability error.</summary>
    public RiscVHostUnavailableException(string message) : base(message) { }

    /// <summary>Create an availability error with its native loader cause.</summary>
    public RiscVHostUnavailableException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Production stateful binding for <c>neo_riscv_execute_script_with_host</c>. Every callback
/// receives a transaction-scoped <see cref="RiscVHostExecutionContext"/> through a
/// <see cref="GCHandle"/>; callback delegates are process-lifetime rooted, and all callback
/// output allocations are released by the ABI free callback or the owning call's finalizer path.
/// </summary>
/// <remarks>See <c>doc.md</c> §7.3, §7.5, §8.1, and §8.4.</remarks>
public static class RiscVHost
{
    private const string LibraryName = "neo_riscv_host";
    private const int MaxNativeStackItems = 10_000;
    private const int MaxNativeStackDepth = 64;
    private const int MaxNativeByteLength = 16 * 1024 * 1024;
    private static readonly ConcurrentDictionary<IntPtr, NativeAllocationLease> CallbackAllocations = new();

    /// <summary>Native execution halted normally.</summary>
    public const uint StateHalt = 0;

    /// <summary>Native execution faulted, including OOG and callback errors.</summary>
    public const uint StateFault = 1;

    /// <summary>Native execution was explicitly broken.</summary>
    public const uint StateBreak = 2;

    /// <summary>Neo N3 mainnet network magic used only when the batch omits one.</summary>
    public const uint DefaultNetwork = 860833102u;

    /// <summary><see cref="Neo.SmartContract.TriggerType.Application"/> ABI byte.</summary>
    public const byte TriggerApplication = 0x40;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeExecutionResult
    {
        public long FeeConsumedPico;
        public uint State;
        public IntPtr StackPtr;
        public nuint StackLen;
        public IntPtr ErrorPtr;
        public nuint ErrorLen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeHostResult
    {
        public IntPtr StackPtr;
        public nuint StackLen;
        public IntPtr ErrorPtr;
        public nuint ErrorLen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeStackItem
    {
        public uint Kind;
        public long IntegerValue;
        public IntPtr BytesPtr;
        public nuint BytesLen;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private delegate bool HostCallbackDelegate(
        IntPtr userData,
        uint api,
        nuint instructionPointer,
        byte trigger,
        uint network,
        byte addressVersion,
        ulong timestamp,
        long gasLeft,
        IntPtr inputStackPtr,
        nuint inputStackLen,
        out NativeHostResult output);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void HostFreeCallbackDelegate(IntPtr userData, ref NativeHostResult result);

    private static readonly HostCallbackDelegate PinnedHostCallback = StaticHostCallback;
    private static readonly HostFreeCallbackDelegate PinnedHostFreeCallback = StaticHostFreeCallback;
    private static readonly IntPtr HostCallbackPointer = Marshal.GetFunctionPointerForDelegate(PinnedHostCallback);
    private static readonly IntPtr HostFreeCallbackPointer = Marshal.GetFunctionPointerForDelegate(PinnedHostFreeCallback);

    [DllImport(
        LibraryName,
        EntryPoint = "neo_riscv_execute_script_with_host",
        CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool NativeExecuteScriptWithHost(
        IntPtr scriptPtr,
        nuint scriptLen,
        nuint initialInstructionPointer,
        byte trigger,
        uint network,
        byte addressVersion,
        ulong timestamp,
        long gasLeft,
        long execFeeFactorPico,
        IntPtr initialStackPtr,
        nuint initialStackLen,
        IntPtr userData,
        IntPtr callback,
        IntPtr freeCallback,
        out NativeExecutionResult output);

    [DllImport(
        LibraryName,
        EntryPoint = "neo_riscv_free_execution_result",
        CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeFreeExecutionResult(ref NativeExecutionResult result);

    private static readonly object AvailabilityLock = new();
    private static bool? _isAvailableCache;
    private static string? _lastAvailabilityError;

    /// <summary>True only when the stateful FFI entry point can execute a minimal RET script.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_isAvailableCache is { } cached) return cached;
            lock (AvailabilityLock)
            {
                if (_isAvailableCache is { } innerCached) return innerCached;
                var script = new byte[] { 0x40 };
                var scriptHandle = GCHandle.Alloc(script, GCHandleType.Pinned);
                NativeExecutionResult output = default;
                var nativeReturned = false;
                try
                {
                    var invoked = NativeExecuteScriptWithHost(
                        scriptHandle.AddrOfPinnedObject(),
                        (nuint)script.Length,
                        0,
                        TriggerApplication,
                        DefaultNetwork,
                        Neo.ProtocolSettings.Default.AddressVersion,
                        0,
                        1_000_000,
                        300_000,
                        IntPtr.Zero,
                        0,
                        IntPtr.Zero,
                        HostCallbackPointer,
                        HostFreeCallbackPointer,
                        out output);
                    nativeReturned = true;
                    _isAvailableCache = invoked;
                    _lastAvailabilityError = invoked ? null : "stateful native execute returned false";
                    return invoked;
                }
                catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
                {
                    _lastAvailabilityError = $"{ex.GetType().Name}: {ex.Message}";
                    _isAvailableCache = false;
                    return false;
                }
                finally
                {
                    try
                    {
                        if (nativeReturned) NativeFreeExecutionResult(ref output);
                    }
                    finally
                    {
                        scriptHandle.Free();
                        GC.KeepAlive(PinnedHostCallback);
                        GC.KeepAlive(PinnedHostFreeCallback);
                    }
                }
            }
        }
    }

    /// <summary>Most recent stateful native-library probe error.</summary>
    public static string? LastAvailabilityError => _lastAvailabilityError;

    /// <summary>Reset the process-local availability probe cache.</summary>
    public static void ResetAvailabilityCache()
    {
        lock (AvailabilityLock)
        {
            _isAvailableCache = null;
            _lastAvailabilityError = null;
        }
    }

    /// <summary>
    /// Execute one Neo script through PolkaVM with stateful host callbacks. Native result buffers
    /// are always released before this method returns or throws.
    /// </summary>
    /// <param name="script">Neo script bytes executed by the RISC-V guest.</param>
    /// <param name="context">Transaction-scoped state, runtime, witness, and effect context.</param>
    /// <param name="initialInstructionPointer">Initial Neo instruction offset.</param>
    public static RiscVExecutionResult Execute(
        ReadOnlySpan<byte> script,
        RiscVHostExecutionContext context,
        nuint initialInstructionPointer = 0)
    {
        if (script.IsEmpty)
            throw new ArgumentException("script cannot be empty", nameof(script));
        ArgumentNullException.ThrowIfNull(context);

        var scriptBytes = script.ToArray();
        var scriptHandle = GCHandle.Alloc(scriptBytes, GCHandleType.Pinned);
        var contextHandle = GCHandle.Alloc(context, GCHandleType.Normal);
        var userData = GCHandle.ToIntPtr(contextHandle);
        NativeExecutionResult output = default;
        var nativeReturned = false;
        try
        {
            var invoked = NativeExecuteScriptWithHost(
                scriptHandle.AddrOfPinnedObject(),
                (nuint)scriptBytes.Length,
                initialInstructionPointer,
                context.Trigger,
                context.Network,
                context.AddressVersion,
                context.Timestamp,
                context.GasLimit,
                context.ExecFeeFactorPico,
                IntPtr.Zero,
                0,
                userData,
                HostCallbackPointer,
                HostFreeCallbackPointer,
                out output);
            nativeReturned = true;

            if (!invoked)
            {
                return new RiscVExecutionResult
                {
                    State = StateFault,
                    FeeConsumedPico = 0,
                    HostFeeConsumed = context.HostFeeConsumed,
                    ErrorMessage = "stateful native execute returned false",
                };
            }

            var result = new RiscVExecutionResult
            {
                State = output.State,
                FeeConsumedPico = output.FeeConsumedPico,
                HostFeeConsumed = context.HostFeeConsumed,
                ErrorMessage = ReadUtf8(output.ErrorPtr, output.ErrorLen),
            };
            context.CancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        finally
        {
            try
            {
                if (nativeReturned) NativeFreeExecutionResult(ref output);
            }
            finally
            {
                ReleaseOutstandingCallbackAllocations(userData);
                contextHandle.Free();
                scriptHandle.Free();
                GC.KeepAlive(PinnedHostCallback);
                GC.KeepAlive(PinnedHostFreeCallback);
            }
        }
    }

    private static bool StaticHostCallback(
        IntPtr userData,
        uint api,
        nuint instructionPointer,
        byte trigger,
        uint network,
        byte addressVersion,
        ulong timestamp,
        long gasLeft,
        IntPtr inputStackPtr,
        nuint inputStackLen,
        out NativeHostResult output)
    {
        output = default;
        if (userData == IntPtr.Zero) return false;

        RiscVHostExecutionContext? context = null;
        try
        {
            context = GCHandle.FromIntPtr(userData).Target as RiscVHostExecutionContext;
            if (context is null) return false;
            var input = ReadStack(inputStackPtr, inputStackLen, context, depth: 0);
            var result = context.InvokeNativeSyscall(
                api,
                instructionPointer,
                trigger,
                network,
                addressVersion,
                timestamp,
                gasLeft,
                input);
            output = CreateHostStackResult(result, context, userData);
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (context is null) return false;
            output = CreateHostErrorResult(ex, userData);
            return true;
        }
    }

    private static void StaticHostFreeCallback(IntPtr userData, ref NativeHostResult result)
    {
        var key = result.StackPtr != IntPtr.Zero ? result.StackPtr : result.ErrorPtr;
        if (key != IntPtr.Zero && CallbackAllocations.TryRemove(key, out var lease))
            lease.Dispose();
        result = default;
    }

    private static NativeHostResult CreateHostStackResult(
        IReadOnlyList<StackItem> stack,
        RiscVHostExecutionContext context,
        IntPtr owner)
    {
        if (stack.Count == 0) return default;
        var lease = new NativeAllocationLease(owner);
        try
        {
            var stackPtr = WriteStack(stack, context, lease, depth: 0);
            var result = new NativeHostResult
            {
                StackPtr = stackPtr,
                StackLen = (nuint)stack.Count,
            };
            RegisterAllocation(stackPtr, lease);
            return result;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static NativeHostResult CreateHostErrorResult(Exception exception, IntPtr owner)
    {
        var payload = Encoding.UTF8.GetBytes(
            $"{exception.GetType().FullName}\n{exception.Message}");
        var lease = new NativeAllocationLease(owner);
        try
        {
            var errorPtr = lease.Allocate(payload.Length);
            Marshal.Copy(payload, 0, errorPtr, payload.Length);
            RegisterAllocation(errorPtr, lease);
            return new NativeHostResult
            {
                ErrorPtr = errorPtr,
                ErrorLen = (nuint)payload.Length,
            };
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private static void RegisterAllocation(IntPtr key, NativeAllocationLease lease)
    {
        if (key == IntPtr.Zero || !CallbackAllocations.TryAdd(key, lease))
            throw new InvalidOperationException("failed to register native callback allocation");
    }

    private static void ReleaseOutstandingCallbackAllocations(IntPtr owner)
    {
        foreach (var pair in CallbackAllocations)
        {
            if (pair.Value.Owner != owner) continue;
            if (CallbackAllocations.TryRemove(pair.Key, out var lease))
                lease.Dispose();
        }
    }

    private static StackItem[] ReadStack(
        IntPtr stackPtr,
        nuint stackLen,
        RiscVHostExecutionContext context,
        int depth)
    {
        if (depth > MaxNativeStackDepth)
            throw new InvalidOperationException("native stack nesting exceeds the supported maximum");
        if (stackLen == 0) return System.Array.Empty<StackItem>();
        if (stackPtr == IntPtr.Zero)
            throw new InvalidOperationException("native stack pointer is null for a non-empty stack");
        var length = checked((int)stackLen);
        if (length > MaxNativeStackItems)
            throw new InvalidOperationException("native stack exceeds the supported item count");

        var output = new StackItem[length];
        var itemSize = Marshal.SizeOf<NativeStackItem>();
        for (var index = 0; index < length; index++)
        {
            var native = Marshal.PtrToStructure<NativeStackItem>(IntPtr.Add(stackPtr, checked(index * itemSize)));
            output[index] = native.Kind switch
            {
                0 => new Integer(native.IntegerValue),
                1 => new ByteString(ReadBytes(native)),
                2 => StackItem.Null,
                3 => native.IntegerValue == 0 ? StackItem.False : StackItem.True,
                4 => new Array(context.ReferenceCounter, ReadStack(native.BytesPtr, native.BytesLen, context, depth + 1)),
                5 => new Integer(new BigInteger(ReadBytes(native))),
                6 => StackItem.FromInterface(ResolveIterator(context, native.IntegerValue)),
                7 => new Struct(context.ReferenceCounter, ReadStack(native.BytesPtr, native.BytesLen, context, depth + 1)),
                8 => ReadMap(native, context, depth + 1),
                9 => StackItem.FromInterface(context.ResolveHandle(checked((ulong)native.IntegerValue))),
                10 => new Pointer(context.PointerScript, checked((int)native.IntegerValue)),
                11 => new Buffer(ReadBytes(native)),
                _ => throw new InvalidOperationException($"unsupported native stack kind {native.Kind}"),
            };
        }
        return output;
    }

    private static Map ReadMap(NativeStackItem native, RiscVHostExecutionContext context, int depth)
    {
        var children = ReadStack(native.BytesPtr, native.BytesLen, context, depth);
        if ((children.Length & 1) != 0)
            throw new InvalidOperationException("native map contains an odd flattened item count");
        var map = new Map(context.ReferenceCounter);
        for (var index = 0; index < children.Length; index += 2)
        {
            if (children[index] is not PrimitiveType key)
                throw new InvalidOperationException("native map contains a non-primitive key");
            map[key] = children[index + 1];
        }
        return map;
    }

    private static IIterator ResolveIterator(RiscVHostExecutionContext context, long rawHandle)
    {
        var value = context.ResolveHandle(checked((ulong)rawHandle));
        return value as IIterator
            ?? throw new InvalidOperationException("native iterator handle has the wrong type");
    }

    private static byte[] ReadBytes(NativeStackItem native)
    {
        var length = checked((int)native.BytesLen);
        if (length > MaxNativeByteLength)
            throw new InvalidOperationException("native stack byte payload exceeds the supported maximum");
        if (length == 0) return System.Array.Empty<byte>();
        if (native.BytesPtr == IntPtr.Zero)
            throw new InvalidOperationException("native byte pointer is null for a non-empty payload");
        var bytes = new byte[length];
        Marshal.Copy(native.BytesPtr, bytes, 0, length);
        return bytes;
    }

    private static IntPtr WriteStack(
        IReadOnlyList<StackItem> stack,
        RiscVHostExecutionContext context,
        NativeAllocationLease lease,
        int depth)
    {
        if (depth > MaxNativeStackDepth)
            throw new InvalidOperationException("managed stack nesting exceeds the supported maximum");
        if (stack.Count > MaxNativeStackItems)
            throw new InvalidOperationException("managed stack exceeds the supported item count");
        if (stack.Count == 0) return IntPtr.Zero;

        var itemSize = Marshal.SizeOf<NativeStackItem>();
        var stackPtr = lease.Allocate(checked(itemSize * stack.Count));
        for (var index = 0; index < stack.Count; index++)
        {
            var native = WriteItem(stack[index], context, lease, depth);
            Marshal.StructureToPtr(native, IntPtr.Add(stackPtr, checked(index * itemSize)), fDeleteOld: false);
        }
        return stackPtr;
    }

    private static NativeStackItem WriteItem(
        StackItem item,
        RiscVHostExecutionContext context,
        NativeAllocationLease lease,
        int depth)
    {
        ArgumentNullException.ThrowIfNull(item);
        return item switch
        {
            Integer integer when integer.Size > sizeof(long) => WriteBytesItem(
                kind: 5,
                integer.GetInteger().ToByteArray(),
                lease),
            Integer integer => new NativeStackItem { Kind = 0, IntegerValue = checked((long)integer.GetInteger()) },
            ByteString bytes => WriteBytesItem(1, bytes.GetSpan().ToArray(), lease),
            Buffer bytes => WriteBytesItem(11, bytes.GetSpan().ToArray(), lease),
            Struct structure => WriteCollectionItem(7, structure, context, lease, depth + 1),
            Array array => WriteCollectionItem(4, array, context, lease, depth + 1),
            Map map => WriteMapItem(map, context, lease, depth + 1),
            Neo.VM.Types.Boolean boolean => new NativeStackItem
            {
                Kind = 3,
                IntegerValue = boolean.GetBoolean() ? 1 : 0,
            },
            InteropInterface interop when interop.GetInterface<object>() is IIterator iterator => new NativeStackItem
            {
                Kind = 6,
                IntegerValue = checked((long)context.RegisterHandle(iterator)),
            },
            InteropInterface interop => new NativeStackItem
            {
                Kind = 9,
                IntegerValue = checked((long)context.RegisterHandle(
                    interop.GetInterface<object>()
                    ?? throw new InvalidOperationException("interop stack item contains null"))),
            },
            Null => new NativeStackItem { Kind = 2 },
            Pointer pointer => new NativeStackItem { Kind = 10, IntegerValue = pointer.Position },
            _ => throw new InvalidOperationException($"unsupported managed stack type {item.GetType().Name}"),
        };
    }

    private static NativeStackItem WriteBytesItem(uint kind, byte[] bytes, NativeAllocationLease lease)
    {
        if (bytes.Length > MaxNativeByteLength)
            throw new InvalidOperationException("managed byte payload exceeds the supported maximum");
        var pointer = lease.Allocate(bytes.Length);
        if (bytes.Length > 0) Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new NativeStackItem { Kind = kind, BytesPtr = pointer, BytesLen = (nuint)bytes.Length };
    }

    private static NativeStackItem WriteCollectionItem(
        uint kind,
        IReadOnlyList<StackItem> items,
        RiscVHostExecutionContext context,
        NativeAllocationLease lease,
        int depth)
    {
        return new NativeStackItem
        {
            Kind = kind,
            BytesPtr = WriteStack(items, context, lease, depth),
            BytesLen = (nuint)items.Count,
        };
    }

    private static NativeStackItem WriteMapItem(
        Map map,
        RiscVHostExecutionContext context,
        NativeAllocationLease lease,
        int depth)
    {
        var flattened = new StackItem[checked(map.Count * 2)];
        var index = 0;
        foreach (var entry in map)
        {
            flattened[index++] = entry.Key;
            flattened[index++] = entry.Value;
        }
        return new NativeStackItem
        {
            Kind = 8,
            BytesPtr = WriteStack(flattened, context, lease, depth),
            BytesLen = (nuint)flattened.Length,
        };
    }

    private static string? ReadUtf8(IntPtr pointer, nuint rawLength)
    {
        if (pointer == IntPtr.Zero || rawLength == 0) return null;
        var length = checked((int)rawLength);
        if (length > MaxNativeByteLength)
            throw new InvalidOperationException("native error payload exceeds the supported maximum");
        var bytes = new byte[length];
        Marshal.Copy(pointer, bytes, 0, length);
        return Encoding.UTF8.GetString(bytes);
    }

    private sealed class NativeAllocationLease(IntPtr owner) : IDisposable
    {
        private readonly List<IntPtr> _allocations = new();
        private int _disposed;

        public IntPtr Owner { get; } = owner;

        public IntPtr Allocate(int length)
        {
            if (length == 0) return IntPtr.Zero;
            var pointer = Marshal.AllocHGlobal(length);
            _allocations.Add(pointer);
            return pointer;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            for (var index = _allocations.Count - 1; index >= 0; index--)
                Marshal.FreeHGlobal(_allocations[index]);
            _allocations.Clear();
        }
    }
}

/// <summary>Outcome returned by the stateful PolkaVM execution ABI.</summary>
/// <remarks>See <c>doc.md</c> §7.5 and §8.1.</remarks>
public sealed record RiscVExecutionResult
{
    /// <summary>Native ABI outcome ordinal.</summary>
    public required uint State { get; init; }

    /// <summary>Raw opcode fee returned by the stateful FFI, in 1/10,000 datoshi.</summary>
    public required long FeeConsumedPico { get; init; }

    /// <summary>ApplicationEngine-compatible fixed and variable host fees, in datoshi.</summary>
    public required long HostFeeConsumed { get; init; }

    /// <summary>Optional native or callback fault message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Total transaction fee in datoshi, rounded exactly as the native host charges gas.</summary>
    public long FeeConsumed
    {
        get
        {
            if (FeeConsumedPico < 0 || HostFeeConsumed < 0)
                throw new InvalidOperationException("native execution returned a negative fee");
            var nativeDatoshi = FeeConsumedPico / 10_000;
            if (FeeConsumedPico % 10_000 != 0) nativeDatoshi = checked(nativeDatoshi + 1);
            return checked(nativeDatoshi + HostFeeConsumed);
        }
    }

    /// <summary>True only for the ABI's zero-valued HALT outcome.</summary>
    public bool Halted => State == RiscVHost.StateHalt;
}
