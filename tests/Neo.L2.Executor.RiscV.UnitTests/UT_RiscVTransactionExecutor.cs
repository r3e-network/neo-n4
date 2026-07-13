using Neo.Extensions.IO;
using Neo.L2.Persistence;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Manifest;
using Neo.SmartContract.Iterators;
using Neo.SmartContract.Native;
using Neo.VM;
using Neo.VM.Types;
using Array = Neo.VM.Types.Array;

namespace Neo.L2.Executor.RiscV.UnitTests;

/// <summary>Atomicity, overlay, runtime-context, and effect tests for stateful RISC-V execution.</summary>
[TestClass]
public class UT_RiscVTransactionExecutor
{
    [TestMethod]
    public async Task Halt_UsesNativeFeeAndReturnsCanonicalEmptyEffects()
    {
        using var store = RiscVTestData.CreateStore();
        var executor = Executor(store, (_, context) => RiscVTestData.Halt(context, 12_345));

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction([(byte)OpCode.RET]),
            RiscVTestData.Context);

        Assert.AreEqual(TransactionEffectsProfile.CanonicalNativeV1, executor.EffectsProfile);
        Assert.IsTrue(result.Receipt.Success, result.FailureReason);
        Assert.AreEqual(2, result.Receipt.GasConsumed);
        Assert.AreEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        Assert.AreEqual(UInt256.Zero, result.Receipt.EventsHash);
        RiscVTestData.AssertReceiptEffectsAreSameSource(result);
    }

    [TestMethod]
    public async Task Halt_CommitsReadThroughPutDeleteAndBeforeImages()
    {
        var script = new byte[] { (byte)OpCode.RET };
        var contract = RiscVTestData.BuildContract(script);
        var existingKey = new byte[] { 0x20 };
        var deletedKey = new byte[] { 0x30 };
        var newKey = new byte[] { 0x10 };
        using var store = RiscVTestData.CreateStore();
        PutContractValue(store, contract.Id, existingKey, [0x01]);
        PutContractValue(store, contract.Id, deletedKey, [0x03]);
        var executor = Executor(
            store,
            (_, context) =>
            {
                var storageContext = GetStorageContext(context);
                var before = context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Get,
                    [storageContext, new ByteString(existingKey)]);
                CollectionAssert.AreEqual(new byte[] { 0x01 }, before[^1].GetSpan().ToArray());

                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Put,
                    [storageContext, new ByteString(existingKey), new ByteString(new byte[] { 0x02 })]);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Put,
                    [storageContext, new ByteString(newKey), new ByteString(new byte[] { 0x04 })]);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Delete,
                    [storageContext, new ByteString(deletedKey)]);
                return RiscVTestData.Halt(context);
            },
            contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success, result.FailureReason);
        CollectionAssert.AreEqual(new byte[] { 0x02 }, GetContractValue(store, contract.Id, existingKey));
        CollectionAssert.AreEqual(new byte[] { 0x04 }, GetContractValue(store, contract.Id, newKey));
        Assert.IsNull(GetContractValue(store, contract.Id, deletedKey));
        Assert.AreEqual(3, result.Effects.StorageChanges.Count);
        CollectionAssert.AreEqual(newKey, result.Effects.StorageChanges[0].Key.Span[sizeof(int)..].ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x01 }, result.Effects.StorageChanges[1].OldValue!.Value.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x02 }, result.Effects.StorageChanges[1].NewValue!.Value.ToArray());
        Assert.AreEqual(Neo.L2.Executor.Effects.CanonicalStorageOperation.Delete, result.Effects.StorageChanges[2].Operation);
        Assert.AreNotEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        RiscVTestData.AssertReceiptEffectsAreSameSource(result);
    }

    [TestMethod]
    public async Task Fault_RollsBackStorageAndNotifications()
    {
        var script = new byte[] { (byte)OpCode.RET };
        var contract = ContractWithChangedEvent(script);
        var key = new byte[] { 0x01 };
        using var store = RiscVTestData.CreateStore();
        var executor = Executor(
            store,
            (_, context) =>
            {
                var storageContext = GetStorageContext(context);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Put,
                    [storageContext, new ByteString(key), new ByteString(new byte[] { 0x99 })]);
                EmitChanged(context, 7, [0xA0]);
                return RiscVTestData.Fault(context);
            },
            contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsFalse(result.Receipt.Success);
        Assert.IsNull(GetContractValue(store, contract.Id, key));
        Assert.AreEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
        Assert.AreEqual(UInt256.Zero, result.Receipt.EventsHash);
        Assert.AreSame(Neo.L2.Executor.Effects.CanonicalExecutionEffects.Empty, result.Effects);
    }

    [TestMethod]
    public async Task UnsupportedConsensusSyscall_FaultsAndRollsBack()
    {
        var script = new byte[] { (byte)OpCode.RET };
        var contract = RiscVTestData.BuildContract(script);
        var key = new byte[] { 0x01 };
        using var store = RiscVTestData.CreateStore();
        var executor = Executor(
            store,
            (_, context) =>
            {
                var storageContext = GetStorageContext(context);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Put,
                    [storageContext, new ByteString(key), new ByteString(new byte[] { 0x05 })]);
                context.InvokeSyscall(
                    ApplicationEngine.System_Contract_Call,
                    System.Array.Empty<StackItem>());
                return RiscVTestData.Halt(context);
            },
            contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsFalse(result.Receipt.Success);
        Assert.IsNull(GetContractValue(store, contract.Id, key));
        Assert.AreEqual(UInt256.Zero, result.Receipt.StorageDeltaHash);
    }

    [TestMethod]
    public async Task CallbackOutOfGas_RollsBackWithoutDummySuccess()
    {
        var script = new byte[] { (byte)OpCode.RET };
        var contract = RiscVTestData.BuildContract(script);
        var key = new byte[] { 0x01 };
        using var store = RiscVTestData.CreateStore();
        var executor = new RiscVTransactionExecutor(
            store,
            RiscVTestData.Settings,
            gasLimit: 100,
            contractResolver: (_, _) => contract,
            runner: (_, context) =>
            {
                var storageContext = GetStorageContext(context);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Put,
                    [storageContext, new ByteString(key), new ByteString(new byte[] { 0x07 })]);
                return RiscVTestData.Halt(context);
            });

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsFalse(result.Receipt.Success);
        Assert.IsNull(GetContractValue(store, contract.Id, key));
        Assert.IsTrue(result.Receipt.GasConsumed > 0);
    }

    [TestMethod]
    public async Task Find_MergesOverlayAndReturnsLexicographicOrder()
    {
        var script = new byte[] { (byte)OpCode.RET };
        var contract = RiscVTestData.BuildContract(script);
        using var store = RiscVTestData.CreateStore();
        PutContractValue(store, contract.Id, [0xAA, 0x02], [0x22]);
        PutContractValue(store, contract.Id, [0xAA, 0x03], [0x33]);
        var observedKeys = new List<byte[]>();
        var executor = Executor(
            store,
            (_, context) =>
            {
                var storageContext = GetStorageContext(context);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Put,
                    [storageContext, new ByteString(new byte[] { 0xAA, 0x01 }), new ByteString(new byte[] { 0x11 })]);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Delete,
                    [storageContext, new ByteString(new byte[] { 0xAA, 0x02 })]);
                var find = context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Find,
                    [storageContext, new ByteString(new byte[] { 0xAA }), new Integer((byte)FindOptions.None)]);
                var iterator = find[^1];
                while (context.InvokeSyscall(ApplicationEngine.System_Iterator_Next, [iterator])[^1].GetBoolean())
                {
                    var value = context.InvokeSyscall(ApplicationEngine.System_Iterator_Value, [iterator])[^1];
                    observedKeys.Add(((Array)value)[0].GetSpan().ToArray());
                }
                return RiscVTestData.Halt(context);
            },
            contract);

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success, result.FailureReason);
        Assert.AreEqual(2, observedKeys.Count);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0x01 }, observedKeys[0]);
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0x03 }, observedKeys[1]);
    }

    [TestMethod]
    public async Task Notify_BindsOrderedFullCanonicalStackState()
    {
        var first = await ExecuteNotificationAsync(7, [0xA0], nonce: 1);
        var second = await ExecuteNotificationAsync(8, [0xA0], nonce: 2);
        var third = await ExecuteNotificationAsync(7, [0xA1], nonce: 3);

        Assert.IsTrue(first.Receipt.Success);
        Assert.AreNotEqual(UInt256.Zero, first.Receipt.EventsHash);
        Assert.AreNotEqual(first.Receipt.EventsHash, second.Receipt.EventsHash);
        Assert.AreNotEqual(first.Receipt.EventsHash, third.Receipt.EventsHash);
        Assert.AreEqual(1, first.Effects.Events.Count);
        Assert.AreEqual("Changed", first.Effects.Events[0].EventName);
        RiscVTestData.AssertReceiptEffectsAreSameSource(first);
    }

    [TestMethod]
    public async Task RuntimeContextAndCheckWitness_MatchTransactionAndFirstBlock()
    {
        var signer = UInt160.Parse("0x2222222222222222222222222222222222222222");
        using var store = RiscVTestData.CreateStore();
        var executor = Executor(
            store,
            (_, context) =>
            {
                var time = context.InvokeSyscall(
                    ApplicationEngine.System_Runtime_GetTime,
                    System.Array.Empty<StackItem>())[^1].GetInteger();
                var network = context.InvokeSyscall(
                    ApplicationEngine.System_Runtime_GetNetwork,
                    System.Array.Empty<StackItem>())[^1].GetInteger();
                var container = context.InvokeSyscall(
                    ApplicationEngine.System_Runtime_GetScriptContainer,
                    System.Array.Empty<StackItem>())[^1];
                var signers = context.InvokeSyscall(
                    ApplicationEngine.System_Runtime_CurrentSigners,
                    System.Array.Empty<StackItem>())[^1];
                var witnessed = context.InvokeSyscall(
                    ApplicationEngine.System_Runtime_CheckWitness,
                    [new ByteString(signer.GetSpan().ToArray())])[^1].GetBoolean();

                Assert.AreEqual((System.Numerics.BigInteger)RiscVTestData.Context.FirstBlockTimestamp, time);
                Assert.AreEqual((System.Numerics.BigInteger)RiscVTestData.Context.Network, network);
                Assert.IsInstanceOfType<Array>(container);
                Assert.AreEqual(1, ((Array)signers).Count);
                Assert.IsTrue(witnessed);
                return RiscVTestData.Halt(context);
            });

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction([(byte)OpCode.RET], signer: signer),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success);
    }

    [TestMethod]
    public async Task RuntimeBurnGas_ChargesDescriptorAndAmountExactlyOnce()
    {
        const long amount = 123;
        using var store = RiscVTestData.CreateStore();
        var feeFactor = NativeContract.Policy.GetExecFeeFactor(new L2DataCacheAdapter(store));
        var expected = checked(ApplicationEngine.System_Runtime_BurnGas.FixedPrice * feeFactor + amount);
        var executor = Executor(
            store,
            (_, context) =>
            {
                var stack = context.InvokeSyscall(
                    ApplicationEngine.System_Runtime_BurnGas,
                    [new Integer(amount)]);

                Assert.AreEqual(0, stack.Count);
                Assert.AreEqual(expected, context.HostFeeConsumed);
                return RiscVTestData.Halt(context, nativeFeePico: 0);
            });

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction([(byte)OpCode.RET]),
            RiscVTestData.Context);

        Assert.IsTrue(result.Receipt.Success, result.FailureReason);
        Assert.AreEqual(expected, result.Receipt.GasConsumed);
    }

    [TestMethod]
    public async Task CollectorFailure_RollsBackStagedStorageAndEffects()
    {
        var script = new byte[] { (byte)OpCode.RET };
        var contract = RiscVTestData.BuildContract(script);
        var key = new byte[] { 0x01 };
        using var store = RiscVTestData.CreateStore();
        var executor = new RiscVTransactionExecutor(
            store,
            RiscVTestData.Settings,
            collector: new ThrowingCollector(),
            contractResolver: (_, _) => contract,
            runner: (_, context) =>
            {
                var storageContext = GetStorageContext(context);
                context.InvokeSyscall(
                    ApplicationEngine.System_Storage_Put,
                    [storageContext, new ByteString(key), new ByteString(new byte[] { 0x44 })]);
                return RiscVTestData.Halt(context);
            });

        var result = await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script),
            RiscVTestData.Context);

        Assert.IsFalse(result.Receipt.Success);
        Assert.IsNull(GetContractValue(store, contract.Id, key));
        Assert.AreSame(Neo.L2.Executor.Effects.CanonicalExecutionEffects.Empty, result.Effects);
    }

    [TestMethod]
    public async Task MalformedTransaction_FailsBeforeRunner()
    {
        var called = false;
        using var store = RiscVTestData.CreateStore();
        var executor = Executor(
            store,
            (_, context) =>
            {
                called = true;
                return RiscVTestData.Halt(context);
            });

        var result = await executor.ExecuteAsync(new byte[] { 0xFF, 0xFE }, RiscVTestData.Context);

        Assert.IsFalse(called);
        Assert.IsFalse(result.Receipt.Success);
    }

    private static RiscVTransactionExecutor Executor(
        IL2KeyValueStore store,
        RiscVTransactionExecutor.ProgramRunner runner,
        ContractState? contract = null)
        => new(
            store,
            RiscVTestData.Settings,
            contractResolver: contract is null ? null : (_, _) => contract,
            runner: runner);

    private static StackItem GetStorageContext(RiscVHostExecutionContext context)
        => context.InvokeSyscall(
            ApplicationEngine.System_Storage_GetContext,
            System.Array.Empty<StackItem>())[^1];

    private static ContractState ContractWithChangedEvent(byte[] script)
        => RiscVTestData.BuildContract(
            script,
            new ContractEventDescriptor
            {
                Name = "Changed",
                Parameters =
                [
                    new ContractParameterDefinition { Name = "value", Type = ContractParameterType.Integer },
                    new ContractParameterDefinition { Name = "payload", Type = ContractParameterType.ByteArray },
                ],
            });

    private static void EmitChanged(RiscVHostExecutionContext context, int value, byte[] payload)
    {
        context.InvokeSyscall(
            ApplicationEngine.System_Runtime_Notify,
            [
                new ByteString("Changed"u8.ToArray()),
                new Array(null, [new Integer(value), new ByteString(payload)]),
            ]);
    }

    private static async Task<TransactionExecutionResult> ExecuteNotificationAsync(
        int value,
        byte[] payload,
        uint nonce)
    {
        var script = new byte[] { (byte)OpCode.RET };
        var contract = ContractWithChangedEvent(script);
        using var store = RiscVTestData.CreateStore();
        var executor = Executor(
            store,
            (_, context) =>
            {
                EmitChanged(context, value, payload);
                return RiscVTestData.Halt(context);
            },
            contract);
        return await executor.ExecuteAsync(
            RiscVTestData.BuildTransaction(script, nonce),
            RiscVTestData.Context);
    }

    private static void PutContractValue(
        IL2KeyValueStore store,
        int contractId,
        byte[] key,
        byte[] value)
        => store.Put(new StorageKey { Id = contractId, Key = key }.ToArray(), value);

    private static byte[]? GetContractValue(
        IL2KeyValueStore store,
        int contractId,
        byte[] key)
        => store.Get(new StorageKey { Id = contractId, Key = key }.ToArray());

    private sealed class ThrowingCollector : INotificationCollector
    {
        public Task<(IReadOnlyList<WithdrawalRequest> withdrawals, IReadOnlyList<CrossChainMessage> messages)>
            CollectAsync(Transaction tx, IReadOnlyList<NotifyEventArgs> notifications)
            => throw new InvalidOperationException("collector failed");
    }
}
