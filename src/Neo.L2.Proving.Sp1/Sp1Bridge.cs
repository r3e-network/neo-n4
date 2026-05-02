using System.Runtime.InteropServices;

namespace Neo.L2.Proving.Sp1;

/// <summary>
/// P/Invoke wrapper around <c>libneo_zkvm_bridge</c>. Calls into the bridge's stable C ABI
/// (see <c>bridge/neo-zkvm-bridge/src/lib.rs</c>) which then talks to neo-zkvm's NeoProver.
/// </summary>
/// <remarks>
/// Linux: place <c>libneo_zkvm_bridge.so</c> on the dynamic loader path.
/// macOS:  <c>libneo_zkvm_bridge.dylib</c>.
/// Windows: <c>neo_zkvm_bridge.dll</c>.
/// <para>
/// Method calls return <see cref="Sp1BridgeStatus"/>. The wrapper validates the bridge's
/// reported ABI version against <see cref="ExpectedAbiVersion"/> at construction time.
/// </para>
/// </remarks>
public static class Sp1Bridge
{
    private const string LibraryName = "neo_zkvm_bridge";

    /// <summary>The ABI version this binding is built against. Must match the bridge's report.</summary>
    public const uint ExpectedAbiVersion = 1;

    /// <summary>
    /// Defensive cap on proof bytes returned by the native bridge. SP1 proofs are typically
    /// &lt;1 MB; this 1 GiB ceiling guards against a misbehaving FFI return that declares a
    /// >2 GB length, which would wrap the <c>(int)</c> cast in <see cref="Prove"/> and feed
    /// a wrapped length into <see cref="Marshal.Copy(IntPtr, byte[], int, int)"/> — a heap-
    /// overflow shape.
    /// </summary>
    public const long MaxProofBytes = 1L * 1024 * 1024 * 1024;

    [DllImport(LibraryName, EntryPoint = "neo_zkvm_abi_version", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint NativeAbiVersion();

    [DllImport(LibraryName, EntryPoint = "neo_zkvm_prove", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeProve(
        IntPtr inputPtr, nuint inputLen,
        out IntPtr outputPtr, out nuint outputLen);

    [DllImport(LibraryName, EntryPoint = "neo_zkvm_verify", CallingConvention = CallingConvention.Cdecl)]
    private static extern int NativeVerify(IntPtr proofPtr, nuint proofLen);

    [DllImport(LibraryName, EntryPoint = "neo_zkvm_free_buffer", CallingConvention = CallingConvention.Cdecl)]
    private static extern void NativeFreeBuffer(IntPtr ptr, nuint len);

    // Cache the lib-loadable + ABI-match result. The bridge's loaded-or-not state is
    // sticky for process lifetime: if the .so is missing at startup, it stays missing;
    // if it's present and version-matched, it stays so. Without caching, every Prove /
    // Verify call re-attempts the P/Invoke and re-pays the DllNotFoundException cost
    // in dev environments where the lib is intentionally absent (~10× per batch).
    private static bool? _isAvailableCache;

    /// <summary>True if the native library is loadable AND its ABI matches.</summary>
    public static bool IsAvailable
    {
        get
        {
            if (_isAvailableCache is { } cached) return cached;
            bool result;
            try { result = NativeAbiVersion() == ExpectedAbiVersion; }
            catch (DllNotFoundException) { result = false; }
            catch (BadImageFormatException) { result = false; }
            catch (EntryPointNotFoundException) { result = false; }
            _isAvailableCache = result;
            return result;
        }
    }

    /// <summary>
    /// Reset the <see cref="IsAvailable"/> cache. Test-only — production deployments don't
    /// hot-load native libraries, so the cached result is correct for process lifetime.
    /// </summary>
    public static void ResetAvailableCache() => _isAvailableCache = null;

    /// <summary>
    /// Generate a proof. Returns the proof bytes on success or an error status. The caller
    /// passes the canonical-encoded <c>ProofInput</c> from neo-zkvm.
    /// </summary>
    public static (Sp1BridgeStatus status, byte[]? proof) Prove(ReadOnlySpan<byte> proofInput)
    {
        if (!IsAvailable) return (Sp1BridgeStatus.NotImplemented, null);

        unsafe
        {
            fixed (byte* inputPtr = proofInput)
            {
                IntPtr outputPtr = IntPtr.Zero;
                nuint outputLen = 0;
                var status = NativeProve(
                    (IntPtr)inputPtr, (nuint)proofInput.Length,
                    out outputPtr, out outputLen);

                if (status != 0 || outputPtr == IntPtr.Zero)
                {
                    return ((Sp1BridgeStatus)status, null);
                }

                try
                {
                    if (outputLen > (nuint)MaxProofBytes)
                        return (Sp1BridgeStatus.InvalidInput, null);

                    var bytes = new byte[(int)outputLen];
                    Marshal.Copy(outputPtr, bytes, 0, (int)outputLen);
                    return (Sp1BridgeStatus.Ok, bytes);
                }
                finally
                {
                    NativeFreeBuffer(outputPtr, outputLen);
                }
            }
        }
    }

    /// <summary>Verify a previously generated proof. <c>true</c> if valid.</summary>
    public static Sp1BridgeStatus Verify(ReadOnlySpan<byte> proofBytes)
    {
        if (!IsAvailable) return Sp1BridgeStatus.NotImplemented;

        unsafe
        {
            fixed (byte* proofPtr = proofBytes)
            {
                var status = NativeVerify((IntPtr)proofPtr, (nuint)proofBytes.Length);
                return (Sp1BridgeStatus)status;
            }
        }
    }
}

/// <summary>Status codes returned by the SP1 bridge.</summary>
public enum Sp1BridgeStatus
{
    /// <summary>Operation succeeded.</summary>
    Ok = 0,

    /// <summary>One of the input parameters was malformed.</summary>
    InvalidInput = -1,

    /// <summary>Proof generation failed inside the prover.</summary>
    ProveFailed = -2,

    /// <summary>Verification rejected the proof.</summary>
    VerifyRejected = -3,

    /// <summary>The bridge is loaded but compiled without the real-prover feature; call falls back to a mock prover.</summary>
    NotImplemented = -9,
}
