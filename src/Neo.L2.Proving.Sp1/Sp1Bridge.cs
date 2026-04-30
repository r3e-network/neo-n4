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

    /// <summary>True if the native library is loadable AND its ABI matches.</summary>
    public static bool IsAvailable
    {
        get
        {
            try
            {
                return NativeAbiVersion() == ExpectedAbiVersion;
            }
            catch (DllNotFoundException) { return false; }
            catch (BadImageFormatException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

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
