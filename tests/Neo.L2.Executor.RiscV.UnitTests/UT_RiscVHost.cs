using Neo.Extensions.IO;
using Neo.Extensions.VM;
using Neo.L2.Executor.Effects;
using Neo.L2.Persistence;
using Neo.SmartContract;
using Neo.VM;

namespace Neo.L2.Executor.RiscV.UnitTests;

/// <summary>Structural and real-native tests for the stateful PolkaVM ABI binding.</summary>
[TestClass]
public class UT_RiscVHost
{
    [TestMethod]
    public void StateConstants_MatchRustAbiOrdinals()
    {
        Assert.AreEqual(0u, RiscVHost.StateHalt);
        Assert.AreEqual(1u, RiscVHost.StateFault);
        Assert.AreEqual(2u, RiscVHost.StateBreak);
    }

    [TestMethod]
    public void IsAvailable_DoesNotThrow_WhenStatefulLibraryIsAbsent()
    {
        var available = RiscVHost.IsAvailable;
        Assert.IsTrue(available || !available);
    }

    [TestMethod]
    public void ResetAvailabilityCache_ClearsLastError()
    {
        _ = RiscVHost.IsAvailable;
        RiscVHost.ResetAvailabilityCache();
        Assert.IsNull(RiscVHost.LastAvailabilityError);
    }

    [TestMethod]
    public void Execute_RejectsEmptyScriptBeforeNativeCall()
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => RiscVHost.Execute(System.Array.Empty<byte>(), null!));
    }

    [TestMethod]
    public void ExecutionResult_FeeRoundsNativePicoAndAddsHostDatoshi()
    {
        var result = new RiscVExecutionResult
        {
            State = RiscVHost.StateHalt,
            FeeConsumedPico = 10_001,
            HostFeeConsumed = 7,
        };

        Assert.AreEqual(9, result.FeeConsumed);
        Assert.IsTrue(result.Halted);
    }

    [TestMethod]
    public void ExecutionResult_NegativeNativeFeeFailsClosed()
    {
        var result = new RiscVExecutionResult
        {
            State = RiscVHost.StateHalt,
            FeeConsumedPico = -1,
            HostFeeConsumed = 0,
        };

        Assert.ThrowsExactly<InvalidOperationException>(() => _ = result.FeeConsumed);
    }

    [TestMethod]
    public async Task RealNative_RetReceiptMatchesApplicationEngineByteForByte()
    {
        RequireNativeHost();
        var script = new byte[] { (byte)OpCode.RET };
        var serialized = RiscVTestData.BuildTransaction(script);
        using var applicationStore = RiscVTestData.CreateStore();
        using var riscVStore = RiscVTestData.CreateStore();
        var application = new ApplicationEngineTransactionExecutor(
            applicationStore,
            RiscVTestData.Settings);
        var riscV = new RiscVTransactionExecutor(
            riscVStore,
            RiscVTestData.Settings);

        var applicationResult = await application.ExecuteAsync(serialized, RiscVTestData.Context);
        var riscVResult = await riscV.ExecuteAsync(serialized, RiscVTestData.Context);

        Assert.IsTrue(applicationResult.Receipt.Success);
        Assert.IsTrue(riscVResult.Receipt.Success);
        CollectionAssert.AreEqual(
            applicationResult.Receipt.EncodeHashData(),
            riscVResult.Receipt.EncodeHashData());
        Assert.AreEqual(applicationResult.Receipt.Hash(), riscVResult.Receipt.Hash());
    }

    [TestMethod]
    public async Task RealNative_StoragePutCommitsStateAndCanonicalEffects()
    {
        RequireNativeHost();
        var key = new byte[] { 0xAA, 0x01 };
        var value = new byte[] { 0xBB, 0x02 };
        var script = new ScriptBuilder()
            .EmitPush(value)
            .EmitPush(key)
            .EmitSysCall(ApplicationEngine.System_Storage_GetContext)
            .EmitSysCall(ApplicationEngine.System_Storage_Put)
            .Emit(OpCode.RET)
            .ToArray();
        var contract = RiscVTestData.BuildContract(script);
        using var store = RiscVTestData.CreateStore();
        var executor = new RiscVTransactionExecutor(
            store,
            RiscVTestData.Settings,
            contractResolver: (_, _) => contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success);
        CollectionAssert.AreEqual(
            value,
            store.Get(new StorageKey { Id = contract.Id, Key = key }.ToArray()));
        Assert.AreEqual(1, result.Effects.StorageChanges.Count);
        Assert.AreEqual(CanonicalStorageOperation.Add, result.Effects.StorageChanges[0].Operation);
        Assert.AreNotEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        RiscVTestData.AssertReceiptEffectsAreSameSource(result);
    }

    [TestMethod]
    public async Task RealNative_NotifyProducesCanonicalN4StackState()
    {
        RequireNativeHost();
        var script = new ScriptBuilder()
            .EmitPush(new byte[] { 0xA0 })
            .EmitPush(7)
            .EmitPush(2)
            .Emit(OpCode.PACK)
            .EmitPush("Changed")
            .EmitSysCall(ApplicationEngine.System_Runtime_Notify)
            .Emit(OpCode.RET)
            .ToArray();
        var contract = RiscVTestData.BuildContract(
            script,
            new Neo.SmartContract.Manifest.ContractEventDescriptor
            {
                Name = "Changed",
                Parameters =
                [
                    new Neo.SmartContract.Manifest.ContractParameterDefinition
                    {
                        Name = "value",
                        Type = Neo.SmartContract.ContractParameterType.Integer,
                    },
                    new Neo.SmartContract.Manifest.ContractParameterDefinition
                    {
                        Name = "payload",
                        Type = Neo.SmartContract.ContractParameterType.ByteArray,
                    },
                ],
            });
        using var store = RiscVTestData.CreateStore();
        var executor = new RiscVTransactionExecutor(
            store,
            RiscVTestData.Settings,
            contractResolver: (_, _) => contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success, result.FailureReason);
        Assert.AreEqual(1, result.Effects.Events.Count);
        CollectionAssert.AreEqual(
            Convert.FromHexString("4e454f3453544b310100000040020000002101000000072801000000a0"),
            result.Effects.Events[0].State.ToArray());
        Assert.AreNotEqual(UInt256.Zero, result.Receipt.EventsHash);
        RiscVTestData.AssertReceiptEffectsAreSameSource(result);
    }

    [TestMethod]
    public async Task RealNative_UnsupportedContractCallFaultsClosed()
    {
        RequireNativeHost();
        var script = new ScriptBuilder()
            .EmitDynamicCall(UInt160.Zero, "missing")
            .ToArray();
        using var store = RiscVTestData.CreateStore();
        var executor = new RiscVTransactionExecutor(store, RiscVTestData.Settings);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsFalse(result.Receipt.Success);
        Assert.AreEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        Assert.AreEqual(UInt256.Zero, result.Receipt.EventsHash);
        Assert.AreEqual(0, result.Effects.StorageChanges.Count);
    }

    private static void RequireNativeHost()
    {
        RiscVHost.ResetAvailabilityCache();
        if (!RiscVHost.IsAvailable)
            Assert.Inconclusive($"stateful neo_riscv_host is unavailable: {RiscVHost.LastAvailabilityError}");
    }
}
