using System.Runtime.InteropServices;

namespace Neo.L2.Executor.RiscV;

/// <summary>
/// P/Invoke wrapper around <c>libneo_riscv_host</c> — the PolkaVM-backed Neo RISC-V execution
/// engine that lives at <c>external/neo-riscv-vm/crates/neo-riscv-host</c>. Calls cross the
/// stable C ABI defined in <c>neo-riscv-host/src/ffi.rs</c>.
/// </summary>
/// <remarks>
/// Linux: place <c>libneo_riscv_host.so</c> on the dynamic loader path
/// (<c>LD_LIBRARY_PATH</c> or alongside the C# binaries).
/// macOS: <c>libneo_riscv_host.dylib</c>. Windows: <c>neo_riscv_host.dll</c>.
/// <para>
/// This project targets the off-chain L2 batch executor. <c>RiscVTransactionExecutor</c>
/// implements <c>ITransactionExecutor</c> on top of this VM, providing the NeoVM2/RISC-V
/// execution path the spec calls for. The binding keeps an availability gate so callers
/// can fail clearly when the native library is not deployed.
/// </para>
/// </remarks>
public static class RiscVHost
{
    private const string LibraryName = "neo_riscv_host";

    /// <summary>VM execution state byte (matches Neo VMState).</summary>
    public const uint StateNone = 0;

    /// <summary>VM execution halted normally.</summary>
    public const uint StateHalt = 1;

    /// <summary>VM execution faulted (uncaught exception, OOG, etc.).</summary>
    public const uint StateFault = 2;

    /// <summary>VM execution was explicitly broken by the caller.</summary>
    public const uint StateBreak = 4;

    /// <summary>Default mainnet network magic for trigger contexts that don't set one.</summary>
    public const uint DefaultNetwork = 860833102u;  // Neo N3 mainnet "NEO\0"

    /// <summary>Trigger byte: ApplicationTrigger.Application (matches Neo).</summary>
    public const byte TriggerApplication = 0x40;

    [DllImport(LibraryName, EntryPoint = "neo_riscv_execute_script", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe bool NativeExecuteScript(
        byte* scriptPtr, nuint scriptLen,
        byte trigger, uint network, ulong timestamp, long gasLeft,
        NativeExecutionResult* output);

    [DllImport(LibraryName, EntryPoint = "neo_riscv_free_execution_result", CallingConvention = CallingConvention.Cdecl)]
    private static extern unsafe void NativeFreeExecutionResult(NativeExecutionResult* result);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeExecutionResult
    {
        public long FeeConsumedPico;
        public uint State;
        public NativeStackItem* StackPtr;
        public nuint StackLen;
        public byte* ErrorPtr;
        public nuint ErrorLen;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct NativeStackItem
    {
        public uint Kind;
        public long IntegerValue;
        public byte* BytesPtr;
        public nuint BytesLen;
    }

    // The bridge's loaded-or-not state is sticky for process lifetime: if the .so is missing
    // at startup it stays missing; if it's present it stays present. Cache so every call
    // doesn't re-pay the DllNotFoundException cost on dev environments where the lib is
    // intentionally absent.
    private static bool? _isAvailableCache;
    private static string? _lastAvailabilityError;

    /// <summary>True if <c>libneo_riscv_host</c> is loadable.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_isAvailableCache is { } cached) return cached;
            try
            {
                // A zero-arg probe: build a 1-byte no-op script and run it. If the library
                // is loadable, this succeeds (or fails with a structured fault, not a
                // DllNotFoundException). Net cost is one VM init on first access; subsequent
                // accesses hit the cache.
                Span<byte> script = stackalloc byte[1] { 0x40 };  // RET opcode
                unsafe
                {
                    fixed (byte* scriptPtr = script)
                    {
                        NativeExecutionResult output = default;
                        var ok = NativeExecuteScript(
                            scriptPtr, (nuint)script.Length,
                            TriggerApplication, DefaultNetwork, 0, 1_000_000_000L,
                            &output);
                        // Free whatever the native side allocated, regardless of ok.
                        NativeFreeExecutionResult(&output);
                        _isAvailableCache = true;
                        _lastAvailabilityError = null;
                        return true;
                    }
                }
            }
            catch (DllNotFoundException ex)
            {
                _lastAvailabilityError = $"{ex.GetType().Name}: {ex.Message}";
                _isAvailableCache = false;
                return false;
            }
            catch (Exception ex)
            {
                // Any other exception (e.g. EntryPointNotFoundException for the wrong .so)
                // also counts as unavailable — the wrapper can't proceed safely.
                _lastAvailabilityError = $"{ex.GetType().Name}: {ex.Message}";
                _isAvailableCache = false;
                return false;
            }
        }
    }

    /// <summary>Most recent native library load/probe error, when <see cref="IsAvailable"/>
    /// returned false.</summary>
    public static string? LastAvailabilityError => _lastAvailabilityError;

    /// <summary>Reset the cached availability flag (test-only — production callers see a
    /// stable value over process lifetime).</summary>
    public static void ResetAvailabilityCache()
    {
        _isAvailableCache = null;
        _lastAvailabilityError = null;
    }

    /// <summary>
    /// Execute a RISC-V program on the PolkaVM-backed engine. Returns the execution outcome
    /// including the final state, fee consumed, and an optional fault message. Throws
    /// <see cref="DllNotFoundException"/> when the native library is missing — gate calls
    /// behind <see cref="IsAvailable"/>.
    /// </summary>
    /// <param name="script">Compiled RISC-V program bytes (typically a Neo guest program ELF;
    /// raw NeoVM opcodes are <em>not</em> accepted — those go through Neo's standard NeoVM
    /// engine, not this RISC-V host).</param>
    /// <param name="trigger">Trigger byte (Application = 0x40, Verification = 0x20, etc.).</param>
    /// <param name="network">Network magic (use <see cref="DefaultNetwork"/> if unsure).</param>
    /// <param name="timestamp">Block timestamp; 0 = unset.</param>
    /// <param name="gasLeft">Initial gas budget in fee units.</param>
    public static unsafe RiscVExecutionResult Execute(
        ReadOnlySpan<byte> script,
        byte trigger = TriggerApplication,
        uint network = DefaultNetwork,
        ulong timestamp = 0,
        long gasLeft = 1_000_000_000L)
    {
        if (script.Length == 0)
            throw new ArgumentException("script cannot be empty", nameof(script));

        fixed (byte* scriptPtr = script)
        {
            NativeExecutionResult output = default;
            var ok = NativeExecuteScript(
                scriptPtr, (nuint)script.Length,
                trigger, network, timestamp, gasLeft,
                &output);

            try
            {
                if (!ok)
                    return new RiscVExecutionResult { State = StateFault, FeeConsumed = 0, ErrorMessage = "native execute returned false" };

                string? errorMessage = null;
                if (output.ErrorPtr != null && output.ErrorLen > 0)
                {
                    var errBytes = new byte[(int)output.ErrorLen];
                    Marshal.Copy((IntPtr)output.ErrorPtr, errBytes, 0, errBytes.Length);
                    errorMessage = System.Text.Encoding.UTF8.GetString(errBytes);
                }

                return new RiscVExecutionResult
                {
                    State = output.State,
                    FeeConsumed = output.FeeConsumedPico,
                    ErrorMessage = errorMessage,
                };
            }
            finally
            {
                NativeFreeExecutionResult(&output);
            }
        }
    }
}

/// <summary>Outcome of a single RISC-V VM script execution.</summary>
public sealed record RiscVExecutionResult
{
    /// <summary>Execution state — one of <c>RiscVHost.State*</c>.</summary>
    public required uint State { get; init; }

    /// <summary>Fee consumed in pico-units (matches Neo's pricing convention).</summary>
    public required long FeeConsumed { get; init; }

    /// <summary>Optional fault message; non-null only on <c>StateFault</c>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>True if the script halted normally (state = halt).</summary>
    public bool Halted => State == RiscVHost.StateHalt;
}
