using Neo.Extensions.IO;
using Neo.Extensions.VM;
using Neo.L2.Executor.Effects;
using Neo.L2.Persistence;
using Neo.SmartContract;
using Neo.SmartContract.Iterators;
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
    public void HostUnavailableException_PreservesMessageAndLoaderCause()
    {
        var cause = new DllNotFoundException("missing native library");
        var withCause = new RiscVHostUnavailableException("native host unavailable", cause);
        var withoutCause = new RiscVHostUnavailableException("native host unavailable");

        Assert.AreSame(cause, withCause.InnerException);
        Assert.AreEqual("native host unavailable", withCause.Message);
        Assert.IsNull(withoutCause.InnerException);
        Assert.AreEqual("native host unavailable", withoutCause.Message);
    }

    [TestMethod]
    public async Task RealNative_DefaultExecutorRunsRet()
    {
        RequireNativeHost();
        var executor = new RiscVTransactionExecutor();

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction([(byte)OpCode.RET]),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success, result.FailureReason);
        RiscVTestData.AssertReceiptEffectsAreSameSource(result);
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
    public async Task RealNative_NotifyRoundTripsNestedStackKindsAndNotifications()
    {
        RequireNativeHost();
        var script = new ScriptBuilder()
            .Emit(OpCode.PUSHNULL)
            .EmitPush(true)
            .EmitPush(System.Numerics.BigInteger.One << 80)
            .EmitPush(new byte[] { 0xA1 })
            .EmitPush(2)
            .Emit(OpCode.NEWBUFFER)
            .EmitPush(7)
            .EmitPush(1)
            .Emit(OpCode.PACKSTRUCT)
            .Emit(OpCode.NEWMAP)
            .EmitPush(7)
            .Emit(OpCode.PACK)
            .EmitPush("Types")
            .EmitSysCall(ApplicationEngine.System_Runtime_Notify)
            .Emit(OpCode.PUSHNULL)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetNotifications)
            .Emit(OpCode.DROP)
            .Emit(OpCode.RET)
            .ToArray();
        var eventDescriptor = new Neo.SmartContract.Manifest.ContractEventDescriptor
        {
            Name = "Types",
            Parameters = Enumerable.Range(0, 7)
                .Select(index => new Neo.SmartContract.Manifest.ContractParameterDefinition
                {
                    Name = $"value{index}",
                    Type = Neo.SmartContract.ContractParameterType.Any,
                })
                .ToArray(),
        };
        var contract = RiscVTestData.BuildContract(script, eventDescriptor);
        using var riscVStore = RiscVTestData.CreateStore();
        var riscV = new RiscVTransactionExecutor(
            riscVStore,
            RiscVTestData.Settings,
            contractResolver: (_, _) => contract);

        var riscVResult = await riscV.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);
        var expectedState = new Neo.VM.Types.Array(
            null,
            [
                new Neo.VM.Types.Map(null),
                new Neo.VM.Types.Struct(null, [new Neo.VM.Types.Integer(7)]),
                new Neo.VM.Types.ByteString(new byte[2]),
                new Neo.VM.Types.ByteString(new byte[] { 0xA1 }),
                new Neo.VM.Types.Integer(System.Numerics.BigInteger.One << 80),
                Neo.VM.Types.StackItem.True,
                Neo.VM.Types.StackItem.Null,
            ]);

        Assert.IsTrue(riscVResult.Receipt.Success, riscVResult.FailureReason);
        Assert.AreEqual(1, riscVResult.Effects.Events.Count);
        CollectionAssert.AreEqual(
            CanonicalStackStateSerializer.Serialize(expectedState),
            riscVResult.Effects.Events[0].State.ToArray());
        RiscVTestData.AssertReceiptEffectsAreSameSource(riscVResult);
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

    [TestMethod]
    public async Task RealNative_RuntimeContextRoundTripsComplexStackItems()
    {
        RequireNativeHost();
        var signer = UInt160.Parse("0x2222222222222222222222222222222222222222");
        var script = new ScriptBuilder()
            .EmitSysCall(ApplicationEngine.System_Runtime_Platform)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetTrigger)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetNetwork)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetAddressVersion)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GasLeft)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetScriptContainer)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetTime)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetExecutingScriptHash)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetCallingScriptHash)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetEntryScriptHash)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_GetInvocationCounter)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Runtime_CurrentSigners)
            .Emit(OpCode.DROP)
            .EmitPush(signer.GetSpan())
            .EmitSysCall(ApplicationEngine.System_Runtime_CheckWitness)
            .Emit(OpCode.DROP)
            .EmitPush(7)
            .EmitSysCall(ApplicationEngine.System_Runtime_BurnGas)
            .Emit(OpCode.RET)
            .ToArray();
        var serialized = RiscVTestData.BuildTransaction(script, signer: signer);
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

        Assert.IsTrue(applicationResult.Receipt.Success, applicationResult.FailureReason);
        Assert.IsTrue(riscVResult.Receipt.Success, riscVResult.FailureReason);
        CollectionAssert.AreEqual(
            applicationResult.Receipt.EncodeHashData(),
            riscVResult.Receipt.EncodeHashData());
    }

    [TestMethod]
    public async Task RealNative_StorageReadDeleteAndIteratorCommitCanonicalState()
    {
        RequireNativeHost();
        var prefix = new byte[] { 0xAA };
        var deletedKey = new byte[] { 0xAA, 0x01 };
        var retainedKey = new byte[] { 0xAA, 0x02 };
        var addedKey = new byte[] { 0xAA, 0x03 };
        var addedValue = new byte[] { 0x33 };
        var script = new ScriptBuilder()
            .EmitPush(deletedKey)
            .EmitSysCall(ApplicationEngine.System_Storage_GetReadOnlyContext)
            .EmitSysCall(ApplicationEngine.System_Storage_Get)
            .Emit(OpCode.DROP)
            .EmitPush(deletedKey)
            .EmitSysCall(ApplicationEngine.System_Storage_GetContext)
            .EmitSysCall(ApplicationEngine.System_Storage_Delete)
            .EmitPush(addedKey)
            .EmitPush(addedValue)
            .EmitSysCall(ApplicationEngine.System_Storage_Local_Put)
            .EmitPush(addedKey)
            .EmitSysCall(ApplicationEngine.System_Storage_Local_Get)
            .Emit(OpCode.DROP)
            .EmitPush(prefix)
            .EmitPush((byte)FindOptions.None)
            .EmitSysCall(ApplicationEngine.System_Storage_Local_Find)
            .Emit(OpCode.DUP)
            .EmitSysCall(ApplicationEngine.System_Iterator_Next)
            .Emit(OpCode.DROP)
            .EmitSysCall(ApplicationEngine.System_Iterator_Value)
            .Emit(OpCode.DROP)
            .Emit(OpCode.RET)
            .ToArray();
        var contract = RiscVTestData.BuildContract(script);
        using var store = RiscVTestData.CreateStore();
        store.Put(new StorageKey { Id = contract.Id, Key = deletedKey }.ToArray(), [0x11]);
        store.Put(new StorageKey { Id = contract.Id, Key = retainedKey }.ToArray(), [0x22]);
        var executor = new RiscVTransactionExecutor(
            store,
            RiscVTestData.Settings,
            contractResolver: (_, _) => contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success, result.FailureReason);
        Assert.IsNull(store.Get(new StorageKey { Id = contract.Id, Key = deletedKey }.ToArray()));
        CollectionAssert.AreEqual(
            new byte[] { 0x22 },
            store.Get(new StorageKey { Id = contract.Id, Key = retainedKey }.ToArray()));
        CollectionAssert.AreEqual(
            addedValue,
            store.Get(new StorageKey { Id = contract.Id, Key = addedKey }.ToArray()));
        Assert.AreEqual(2, result.Effects.StorageChanges.Count);
        Assert.AreNotEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        RiscVTestData.AssertReceiptEffectsAreSameSource(result);
    }

    [TestMethod]
    public async Task RealNative_CallbackOutOfGasRollsBackStateAndEffects()
    {
        RequireNativeHost();
        var key = new byte[] { 0xAA, 0x01 };
        var script = new ScriptBuilder()
            .EmitPush(new byte[] { 0xBB })
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
            gasLimit: 100,
            contractResolver: (_, _) => contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsFalse(result.Receipt.Success);
        Assert.IsNull(store.Get(new StorageKey { Id = contract.Id, Key = key }.ToArray()));
        Assert.AreEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        Assert.AreEqual(UInt256.Zero, result.Receipt.EventsHash);
        Assert.AreEqual(0, result.Effects.StorageChanges.Count);
    }

    [TestMethod]
    public async Task RealNative_StorageWithoutResolvedContractFaultsClosed()
    {
        RequireNativeHost();
        var script = new ScriptBuilder()
            .EmitSysCall(ApplicationEngine.System_Storage_GetContext)
            .Emit(OpCode.RET)
            .ToArray();
        using var store = RiscVTestData.CreateStore();
        var executor = new RiscVTransactionExecutor(store, RiscVTestData.Settings);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsFalse(result.Receipt.Success);
        Assert.AreEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        Assert.AreEqual(UInt256.Zero, result.Receipt.EventsHash);
        Assert.AreSame(CanonicalExecutionEffects.Empty, result.Effects);
    }

    private static void RequireNativeHost()
    {
        RiscVHost.ResetAvailabilityCache();
        if (RiscVHost.IsAvailable) return;

        var message = $"stateful neo_riscv_host is unavailable: {RiscVHost.LastAvailabilityError}";
        if (string.Equals(
                Environment.GetEnvironmentVariable("NEO_RISCV_NATIVE_TESTS"),
                "1",
                StringComparison.Ordinal))
        {
            Assert.Fail(message);
        }
        Assert.Inconclusive(message);
    }
}
