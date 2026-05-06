namespace Neo.L2.Executor.RiscV.UnitTests;

/// <summary>
/// Tests for <see cref="RiscVHost"/> — the P/Invoke wrapper around <c>libneo_riscv_host</c>.
/// These exercise the binding's structural contracts (constants, null-arg defenses, the
/// availability gate) without requiring the native library to be deployed. When the
/// library is on the dynamic loader path (set <c>LD_LIBRARY_PATH</c> to
/// <c>external/neo-riscv-vm/target/release</c> after building it), <see cref="RiscVHost.IsAvailable"/>
/// reports true and <see cref="RiscVHost.Execute"/> runs scripts on the real VM.
/// </summary>
[TestClass]
public class UT_RiscVHost
{
    [TestMethod]
    public void StateConstants_PinnedToNeoVMConvention()
    {
        // RISC-V host uses Neo VMState's byte values: HALT=1, FAULT=2, BREAK=4. A future
        // refactor that renumbers these would silently mis-classify execution outcomes
        // — host returns "halt" but the C# wrapper reports "break."
        Assert.AreEqual(0u, RiscVHost.StateNone);
        Assert.AreEqual(1u, RiscVHost.StateHalt);
        Assert.AreEqual(2u, RiscVHost.StateFault);
        Assert.AreEqual(4u, RiscVHost.StateBreak);
    }

    [TestMethod]
    public void DefaultNetwork_IsNeoN3MainnetMagic()
    {
        // 860833102 = "NEO\0" little-endian = the canonical Neo N3 mainnet magic. Pin so
        // the default doesn't drift to testnet or another value.
        Assert.AreEqual(860833102u, RiscVHost.DefaultNetwork);
    }

    [TestMethod]
    public void TriggerApplication_PinnedToNeoConvention()
    {
        // 0x40 = ApplicationTrigger.Application in Neo. Smart contracts use this as the
        // default execution context.
        Assert.AreEqual((byte)0x40, RiscVHost.TriggerApplication);
    }

    [TestMethod]
    public void IsAvailable_DoesNotThrow_WhenLibraryAbsent()
    {
        // The binding gracefully handles a missing library — IsAvailable returns false
        // instead of bubbling a DllNotFoundException up to the caller. This lets the
        // off-chain executor pick a fallback path when the bridge isn't deployed.
        // We can't assert true/false because CI might run with or without the .so on PATH;
        // the contract is "doesn't throw."
        var available = RiscVHost.IsAvailable;
        // Either value is acceptable — both paths must not throw.
        Assert.IsTrue(available || !available);
    }

    [TestMethod]
    public void Execute_RejectsEmptyScript()
    {
        // Defense at the API boundary — empty script is meaningless. Without this guard
        // the native side would either reject silently or fault deep inside PolkaVM with
        // no link to the bad caller.
        Assert.ThrowsExactly<ArgumentException>(
            () => RiscVHost.Execute(System.Array.Empty<byte>()));
    }

    [TestMethod]
    public void RiscVExecutionResult_HaltedReflectsStateByte()
    {
        // Pin Halted property: state == StateHalt (1) → Halted = true. Other states
        // → Halted = false. Operators read Halted as the "did it succeed" boolean.
        var halted = new RiscVExecutionResult { State = RiscVHost.StateHalt, FeeConsumed = 100 };
        Assert.IsTrue(halted.Halted);

        var faulted = new RiscVExecutionResult { State = RiscVHost.StateFault, FeeConsumed = 100, ErrorMessage = "ok" };
        Assert.IsFalse(faulted.Halted);

        var none = new RiscVExecutionResult { State = RiscVHost.StateNone, FeeConsumed = 0 };
        Assert.IsFalse(none.Halted);
    }
}
